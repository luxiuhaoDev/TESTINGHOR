using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class InventoryManagerController : IDisposable
{
	private enum PlannedActionKind { HotbarSwap, UnequipArmor, EquipArmor, DropItem }
	private readonly record struct DesiredItem(string Name, int TargetIndex, InventoryItemSnapshot Item, PlannedActionKind Kind = PlannedActionKind.HotbarSwap);

	private readonly AppConfig config;
	private readonly Func<bool> canRun;
	private readonly Func<bool> inventoryScreenOpen;
	private readonly Func<bool> gameplayScreenClear;
	private readonly Func<int> inventoryKeyProvider;
	private readonly Func<PlayerInventorySnapshot> snapshotProvider;
	private readonly Func<Rectangle?> clientBoundsProvider;
	private readonly Timer timer = new() { Interval = 10 };
	private readonly Stopwatch clock = Stopwatch.StartNew();
	private readonly Random random = new();
	private long nextActionMs;
	private bool inventoryWasOpen;
	private bool savedCursor;
	private NativeMethods.Point originalCursor;
	private int pendingSource = -1;
	private int pendingTarget = -1;
	private string pendingSourceSignature = string.Empty;
	private string pendingTargetSignature = string.Empty;
	private long pendingCheckAtMs;
	private string blockedInventorySignature = string.Empty;
	private string lastConflictWarning = string.Empty;
	private bool inventoryKeyWasDown;
	private bool detectionActive;
	private long detectionEndsMs;
	private long nextDetectionLogMs;
	private long nextInventoryKeyReadMs;
	private int cachedInventoryKey;
	private DesiredItem? preparedAction;
	private long preparedActionAtMs;
	private PlannedActionKind pendingActionKind;

	internal InventoryManagerController(AppConfig config, Func<bool> canRun, Func<bool> inventoryScreenOpen, Func<bool> gameplayScreenClear, Func<int> inventoryKeyProvider, Func<PlayerInventorySnapshot> snapshotProvider, Func<Rectangle?> clientBoundsProvider)
	{
		this.config = config;
		this.canRun = canRun;
		this.inventoryScreenOpen = inventoryScreenOpen;
		this.gameplayScreenClear = gameplayScreenClear;
		this.inventoryKeyProvider = inventoryKeyProvider;
		this.snapshotProvider = snapshotProvider;
		this.clientBoundsProvider = clientBoundsProvider;
		timer.Tick += (_, _) =>
		{
			try
			{
				Tick();
			}
			catch (Exception ex)
			{
				AppLog.Write($"[INV-ERROR] {ex}");
				ResetSession();
			}
		};
		timer.Start();
	}

	private void Tick()
	{
		long now = clock.ElapsedMilliseconds;
		if (now >= nextInventoryKeyReadMs)
		{
			cachedInventoryKey = inventoryKeyProvider();
			nextInventoryKeyReadMs = now + 250;
		}
		bool inventoryKeyDown = cachedInventoryKey != 0 && IsKeyDown(cachedInventoryKey);
		bool inventoryKeyPressed = inventoryKeyDown && !inventoryKeyWasDown;
		inventoryKeyWasDown = inventoryKeyDown;
		if (!config.InventoryManagerEnabled || !canRun())
		{
			ResetSession();
			return;
		}
		if (IsManualMouseInputActive())
		{
			preparedAction = null;
			ClearPending();
			RestoreCursor();
			nextActionMs = now + 150;
			if (!inventoryWasOpen) ResetSession();
			return;
		}
		if (inventoryKeyPressed && !inventoryWasOpen && (gameplayScreenClear() || inventoryScreenOpen()))
		{
			detectionActive = true;
			detectionEndsMs = now + 2000;
			nextDetectionLogMs = now;
			nextActionMs = 0;
			AppLog.Write("[INV-DETECT] Inventory key pressed; detection window started");
		}
		if (!inventoryWasOpen)
		{
			if (!detectionActive) return;
			if (now >= detectionEndsMs)
			{
				detectionActive = false;
				AppLog.Write("[INV-DETECT] Detection window expired");
				return;
			}
			bool screenOpen = inventoryScreenOpen();
			if (now >= nextDetectionLogMs)
			{
				nextDetectionLogMs = now + 200;
				AppLog.Write($"[INV-DETECT-STATE] ScreenOpen={screenOpen} CanRun={canRun()} RemainingMs={Math.Max(0, detectionEndsMs - now)}");
			}
			if (!screenOpen) return;
		}

		PlayerInventorySnapshot snapshot = snapshotProvider();
		if (!snapshot.IsOpen || snapshot.MainSlots.Count < 36)
		{
			if (inventoryWasOpen) ResetSession();
			return;
		}

		if (!inventoryWasOpen)
		{
			detectionActive = false;
			AppLog.Write("[INV-DETECT] GuiInventory confirmed; manager activated");
			BeginSession(snapshot, now);
		}
		if (now < nextActionMs) return;
		if (IsKeyDown(NativeMethods.VkLShift) || IsKeyDown(NativeMethods.VkLControl))
		{
			nextActionMs = now + 100;
			return;
		}

		if (pendingSource >= 0)
		{
			if (ConfirmPending(snapshot))
			{
				blockedInventorySignature = string.Empty;
				RestoreCursor();
				nextActionMs = now + NextDelay();
				ClearPending();
			}
			else if (now >= pendingCheckAtMs)
			{
				RestoreCursor();
				blockedInventorySignature = GetInventorySignature(snapshot);
				AppLog.Write($"[INV-ACTION] {pendingActionKind} was not confirmed; automatic actions paused until inventory changes or closes");
				ClearPending();
				nextActionMs = now + 1000;
				return;
			}
			else
			{
				return;
			}
		}
		if (preparedAction.HasValue)
		{
			if (now < preparedActionAtMs) return;
			if (!canRun() || !inventoryScreenOpen() || !clientBoundsProvider().HasValue)
			{
				preparedAction = null;
				RestoreCursor();
				return;
			}
			PlayerInventorySnapshot currentSnapshot = snapshotProvider();
			if (!currentSnapshot.IsOpen || !IsActionSourceCurrent(preparedAction.Value, currentSnapshot))
			{
				preparedAction = null;
				RestoreCursor();
				nextActionMs = now + 100;
				return;
			}
			ExecuteAction(preparedAction.Value, now, currentSnapshot);
			preparedAction = null;
			return;
		}

		Rectangle? clientBounds = clientBoundsProvider();
		if (!clientBounds.HasValue) return;

		DesiredItem? action = BuildNextAction(snapshot);
		if (!action.HasValue)
		{
			nextActionMs = now + 180;
			return;
		}

		InventoryItemSnapshot source = action.Value.Item;
		if (action.Value.Kind == PlannedActionKind.HotbarSwap && action.Value.TargetIndex is < 0 or > 8) return;
		if (!savedCursor && NativeMethods.GetCursorPos(out originalCursor)) savedCursor = true;
		Point point = ResolveSlotCenter(clientBounds.Value, snapshot, source.InventoryIndex);
		NativeMethods.SetCursorPos(point.X, point.Y);
		preparedAction = action;
		preparedActionAtMs = now + 140;
		nextActionMs = preparedActionAtMs;
	}

	private void ExecuteAction(DesiredItem action, long now, PlayerInventorySnapshot snapshot)
	{
		InventoryItemSnapshot source = action.Item;
		InventoryItemSnapshot target = action.TargetIndex is >= 0 and < 9 ? snapshot.MainSlots[action.TargetIndex] : default;
		uint sent;
		switch (action.Kind)
		{
			case PlannedActionKind.HotbarSwap:
				sent = SendNumberKey(action.TargetIndex);
				break;
			case PlannedActionKind.UnequipArmor:
				sent = SendModifiedLeftClick();
				break;
			case PlannedActionKind.EquipArmor:
				sent = SendModifiedLeftClick();
				break;
			case PlannedActionKind.DropItem:
				sent = SendDropKey();
				break;
			default:
				sent = 0;
				break;
		}
		AppLog.Write($"[INV-ACTION] Kind={action.Kind} Name={action.Name} Source={source.InventoryIndex} Target={action.TargetIndex} SendInput={sent}");
		if (sent == 0)
		{
			RestoreCursor();
			blockedInventorySignature = GetInventorySignature(snapshot);
			ClearPending();
			return;
		}
		if (sent != (action.Kind == PlannedActionKind.DropItem ? 4u : action.Kind == PlannedActionKind.HotbarSwap ? 2u : 4u))
		{
			ReleaseInjectedModifiers(action.Kind);
			RestoreCursor();
			blockedInventorySignature = GetInventorySignature(snapshot);
			ClearPending();
			AppLog.Write("[INV-ACTION] SendInput returned a partial input count; action aborted");
			return;
		}
		pendingSource = source.InventoryIndex;
		pendingTarget = action.Kind == PlannedActionKind.HotbarSwap ? action.TargetIndex : -1;
		pendingSourceSignature = source.Signature;
		pendingTargetSignature = target.Signature;
		pendingActionKind = action.Kind;
		pendingCheckAtMs = now + 1600;
		nextActionMs = now + NextDelay();
	}

	private static bool IsActionSourceCurrent(DesiredItem action, PlayerInventorySnapshot snapshot)
	{
		int index = action.Item.InventoryIndex;
		if (index is >= 36 and < 40)
			return index - 36 < snapshot.ArmorSlots.Count && snapshot.ArmorSlots[index - 36].Signature == action.Item.Signature;
		return index is >= 0 and < 36 && snapshot.MainSlots[index].Signature == action.Item.Signature;
	}

	private DesiredItem? BuildNextAction(PlayerInventorySnapshot snapshot)
	{
		string currentSignature = GetInventorySignature(snapshot);
		if (blockedInventorySignature.Length > 0 && string.Equals(blockedInventorySignature, currentSignature, StringComparison.Ordinal)) return null;
		if (!string.Equals(blockedInventorySignature, currentSignature, StringComparison.Ordinal)) blockedInventorySignature = string.Empty;
		List<DesiredItem> desired = BuildDesiredItems(snapshot);
		DesiredItem? armor = BuildArmorAction(snapshot);
		if (armor.HasValue) return armor;
		HashSet<int> occupiedTargets = new();
		HashSet<string> selectedItems = new(StringComparer.Ordinal);
		List<string> conflicts = new();

		foreach (DesiredItem entry in desired)
		{
			if (!occupiedTargets.Add(entry.TargetIndex))
			{
				conflicts.Add(entry.Name + "→" + (entry.TargetIndex + 1));
				continue;
			}
			if (!selectedItems.Add(entry.Item.Signature)) continue;
			if (entry.Item.InventoryIndex != entry.TargetIndex)
			{
				LogConflicts(conflicts);
				return entry;
			}
		}
		LogConflicts(conflicts);
		if (config.InventoryManagerThrowItems) return BuildDropAction(snapshot);
		return null;
	}

	private static string GetInventorySignature(PlayerInventorySnapshot snapshot) => string.Join(";", snapshot.MainSlots.Select(item => item.Signature));

	private List<DesiredItem> BuildDesiredItems(PlayerInventorySnapshot snapshot)
	{
		IReadOnlyList<InventoryItemSnapshot> items = snapshot.MainSlots;
		List<DesiredItem> result = new();
		void Add(string name, bool enabled, int slot, InventoryItemSnapshot? item)
		{
			if (enabled && item.HasValue) result.Add(new DesiredItem(name, Math.Clamp(slot, 1, 9) - 1, item.Value));
		}

		Add("金苹果", config.InventoryManagerSwitchGoldenApple, config.InventoryManagerGoldenAppleSlot,
			InventoryItemEvaluator.LargestStack(items, item => item.Kind == InventoryItemKind.GoldenApple, config.InventoryManagerGoldenAppleSlot - 1));
		Add("方块", config.InventoryManagerSwitchBlock, config.InventoryManagerBlockSlot,
			InventoryItemEvaluator.LargestStack(items, InventoryItemEvaluator.IsValidBlock, config.InventoryManagerBlockSlot - 1));
		Add("武器", config.InventoryManagerSwitchSword, config.InventoryManagerSwordSlot,
			InventoryItemEvaluator.Best(items, item => item.Kind is InventoryItemKind.Sword or InventoryItemKind.Axe, InventoryItemEvaluator.ScoreWeapon, config.InventoryManagerSwordSlot - 1));
		Add("镐", config.InventoryManagerSwitchPickaxe, config.InventoryManagerPickaxeSlot,
			InventoryItemEvaluator.Best(items, item => item.Kind == InventoryItemKind.Pickaxe, InventoryItemEvaluator.ScoreTool, config.InventoryManagerPickaxeSlot - 1));
		Add("斧", config.InventoryManagerSwitchAxe, config.InventoryManagerAxeSlot,
			InventoryItemEvaluator.Best(items, item => item.Kind == InventoryItemKind.Axe, InventoryItemEvaluator.ScoreTool, config.InventoryManagerAxeSlot - 1));
		Add("鱼竿", config.InventoryManagerSwitchRod, config.InventoryManagerRodSlot,
			InventoryItemEvaluator.Best(items, item => item.Kind == InventoryItemKind.FishingRod, _ => 1f, config.InventoryManagerRodSlot - 1));
		Add("弓", config.InventoryManagerSwitchBow, config.InventoryManagerBowSlot,
			InventoryItemEvaluator.Best(items, item => item.Kind == InventoryItemKind.Bow, config.InventoryManagerBowPriority == 1 ? InventoryItemEvaluator.ScorePunchBow : InventoryItemEvaluator.ScorePowerBow, config.InventoryManagerBowSlot - 1));
		Add("末影珍珠", config.InventoryManagerSwitchEnderPearl, config.InventoryManagerEnderPearlSlot,
			InventoryItemEvaluator.LargestStack(items, item => item.Kind == InventoryItemKind.EnderPearl, config.InventoryManagerEnderPearlSlot - 1));
		Add("水桶", config.InventoryManagerSwitchWaterBucket, config.InventoryManagerWaterBucketSlot,
			InventoryItemEvaluator.Best(items, item => item.Kind == InventoryItemKind.WaterBucket, _ => 1f, config.InventoryManagerWaterBucketSlot - 1));
		Add("火焰弹", config.InventoryManagerSwitchFireball, config.InventoryManagerFireballSlot,
			InventoryItemEvaluator.LargestStack(items, item => item.Kind == InventoryItemKind.FireCharge, config.InventoryManagerFireballSlot - 1));
		Add("投掷物", config.InventoryManagerSwitchProjectile, config.InventoryManagerProjectileSlot,
			InventoryItemEvaluator.LargestStack(items, item => item.Kind is InventoryItemKind.Egg or InventoryItemKind.Snowball, config.InventoryManagerProjectileSlot - 1));
		return result;
	}

	private DesiredItem? BuildArmorAction(PlayerInventorySnapshot snapshot)
	{
		if (!config.InventoryManagerAutoArmor || snapshot.ArmorSlots.Count < 4) return null;
		InventoryItemKind[] kinds = { InventoryItemKind.Boots, InventoryItemKind.Leggings, InventoryItemKind.Chestplate, InventoryItemKind.Helmet };
		for (int armorIndex = 0; armorIndex < kinds.Length; armorIndex++)
		{
			InventoryItemKind kind = kinds[armorIndex];
			InventoryItemSnapshot? best = InventoryItemEvaluator.Best(snapshot.MainSlots, item => item.Kind == kind, InventoryItemEvaluator.ScoreArmor);
			if (!best.HasValue) continue;
			InventoryItemSnapshot equipped = snapshot.ArmorSlots[armorIndex];
			if (!equipped.IsEmpty && InventoryItemEvaluator.ScoreArmor(best.Value) > InventoryItemEvaluator.ScoreArmor(equipped) + 0.0001f)
				return new DesiredItem("卸下旧护甲", -1, equipped with { InventoryIndex = 36 + armorIndex }, PlannedActionKind.UnequipArmor);
			if (equipped.IsEmpty)
				return new DesiredItem("自动穿甲", -1, best.Value, PlannedActionKind.EquipArmor);
		}
		return null;
	}

	private DesiredItem? BuildDropAction(PlayerInventorySnapshot snapshot)
	{
		InventoryItemSnapshot[] items = snapshot.MainSlots.Where(item => !item.IsEmpty).ToArray();
		foreach (InventoryItemSnapshot item in items)
		{
			if (config.InventoryManagerProtectCustomNamed && item.HasCustomName) continue;
			bool trash = item.Kind switch
			{
				InventoryItemKind.Helmet or InventoryItemKind.Chestplate or InventoryItemKind.Leggings or InventoryItemKind.Boots
					=> IsInferiorEquipment(item, items),
				InventoryItemKind.Sword => IsInferior(item, items, candidate => candidate.Kind == InventoryItemKind.Sword, InventoryItemEvaluator.ScoreWeapon),
				InventoryItemKind.Pickaxe => IsInferior(item, items, candidate => candidate.Kind == InventoryItemKind.Pickaxe, InventoryItemEvaluator.ScoreTool),
				InventoryItemKind.Axe => IsInferior(item, items, candidate => candidate.Kind == InventoryItemKind.Axe, InventoryItemEvaluator.ScoreTool),
				InventoryItemKind.Shovel => IsInferior(item, items, candidate => candidate.Kind == InventoryItemKind.Shovel, InventoryItemEvaluator.ScoreTool),
				InventoryItemKind.Block => !InventoryItemEvaluator.IsValidBlock(item),
				_ => IsKnownTrash(item.InternalName)
			};
			if (trash) return new DesiredItem("丢弃物品", -1, item, PlannedActionKind.DropItem);
		}
		return null;
	}

	private static bool IsInferiorEquipment(InventoryItemSnapshot item, IEnumerable<InventoryItemSnapshot> items)
	{
		return IsInferior(item, items, candidate => candidate.Kind == item.Kind, InventoryItemEvaluator.ScoreArmor);
	}

	private static bool IsInferior(InventoryItemSnapshot item, IEnumerable<InventoryItemSnapshot> items, Func<InventoryItemSnapshot, bool> predicate, Func<InventoryItemSnapshot, float> score)
	{
		return items.Any(candidate => candidate.InventoryIndex != item.InventoryIndex && predicate(candidate) && score(candidate) > score(item) + 0.0001f);
	}

	private static bool IsKnownTrash(string name)
	{
		string[] trash = { "rottenFlesh", "spiderEye", "potatoPoisonous", "deadbush", "record_" };
		return trash.Any(value => name.Contains(value, StringComparison.OrdinalIgnoreCase));
	}

	private bool ConfirmPending(PlayerInventorySnapshot snapshot)
	{
		if (pendingSource is >= 36 and < 40)
		{
			int armorIndex = pendingSource - 36;
			if (pendingActionKind != PlannedActionKind.UnequipArmor) return false;
			return !string.Equals(snapshot.ArmorSlots[armorIndex].Signature, pendingSourceSignature, StringComparison.Ordinal);
		}
		if (pendingSource is < 0 or >= 36) return false;
		if (pendingTarget == -1)
		{
			if (pendingActionKind == PlannedActionKind.EquipArmor)
				return snapshot.ArmorSlots.Any(item => item.Signature == pendingSourceSignature)
					&& !string.Equals(snapshot.MainSlots[pendingSource].Signature, pendingSourceSignature, StringComparison.Ordinal);
			return snapshot.MainSlots[pendingSource].Signature != pendingSourceSignature;
		}
		if (pendingTarget is < 0 or >= 9) return false;
		string currentSource = snapshot.MainSlots[pendingSource].Signature;
		string currentTarget = snapshot.MainSlots[pendingTarget].Signature;
		return string.Equals(currentTarget, pendingSourceSignature, StringComparison.Ordinal)
			&& string.Equals(currentSource, pendingTargetSignature, StringComparison.Ordinal);
	}

	private void BeginSession(PlayerInventorySnapshot snapshot, long now)
	{
		inventoryWasOpen = true;
		savedCursor = false;
		ClearPending();
		nextActionMs = now + 220;
		foreach (InventoryItemSnapshot item in snapshot.MainSlots.Concat(snapshot.ArmorSlots).Where(item => !item.IsEmpty))
		{
			AppLog.Write($"[背包诊断] 槽={item.InventoryIndex} 名称={item.InternalName} 类型={item.Kind} 数量={item.Count} 耐久={item.Damage}/{item.MaxDamage} 攻击={item.BaseAttackDamage:0.###} 效率={item.ToolEfficiency:0.###} 护甲={item.ArmorReduction} 自定义名={item.HasCustomName} 附魔={string.Join(',', item.Enchantments.Select(pair => pair.Key + ":" + pair.Value))}");
		}
	}

	private void LogConflicts(List<string> conflicts)
	{
		string warning = string.Join("、", conflicts);
		if (string.Equals(warning, lastConflictWarning, StringComparison.Ordinal)) return;
		lastConflictWarning = warning;
		if (warning.Length > 0) AppLog.Write("[背包管理] 快捷栏槽位冲突，已跳过低优先级项：" + warning);
	}

	private bool IsManualMouseInputActive()
	{
		if (!config.InventoryManagerPauseOnManualInput) return false;
		return IsKeyDown(NativeMethods.VkLButton) || IsKeyDown(NativeMethods.VkRButton) || IsKeyDown(NativeMethods.VkMButton);
	}

	private static bool IsKeyDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

	private int NextDelay()
	{
		int minimum = Math.Clamp(config.InventoryManagerMinDelayMs, 0, 1000);
		int maximum = Math.Clamp(config.InventoryManagerMaxDelayMs, minimum, 1000);
		return maximum <= minimum ? minimum : random.Next(minimum, maximum + 1);
	}

	private static Point ResolveSlotCenter(Rectangle client, PlayerInventorySnapshot snapshot, int index)
	{
		if (index is >= 36 and < 40)
		{
			int armorOrder = 3 - (index - 36);
			int x = snapshot.GuiLeft + 16;
			int y = snapshot.GuiTop + 16 + armorOrder * 18;
			double armorRatioX = client.Width / (double)Math.Max(1, snapshot.ScaledWidth);
			double armorRatioY = client.Height / (double)Math.Max(1, snapshot.ScaledHeight);
			return new Point(client.Left + (int)Math.Round(x * armorRatioX), client.Top + (int)Math.Round(y * armorRatioY));
		}
		int column = index % 9;
		int scaledX = snapshot.GuiLeft + 16 + column * 18;
		int scaledY = index < 9 ? snapshot.GuiTop + 150 : snapshot.GuiTop + 92 + (index - 9) / 9 * 18;
		double ratioX = client.Width / (double)Math.Max(1, snapshot.ScaledWidth);
		double ratioY = client.Height / (double)Math.Max(1, snapshot.ScaledHeight);
		return new Point(client.Left + (int)Math.Round(scaledX * ratioX), client.Top + (int)Math.Round(scaledY * ratioY));
	}

	private static uint SendNumberKey(int targetIndex)
	{
		ushort key = (ushort)(0x31 + Math.Clamp(targetIndex, 0, 8));
		NativeMethods.Input[] inputs =
		{
			KeyInput(key, false),
			KeyInput(key, true)
		};
		return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
	}

	private static uint SendModifiedLeftClick()
	{
		NativeMethods.Input[] inputs =
		{
			KeyInput((ushort)NativeMethods.VkLShift, false),
			MouseInput(NativeMethods.MouseEventLeftDown),
			MouseInput(NativeMethods.MouseEventLeftUp),
			KeyInput((ushort)NativeMethods.VkLShift, true)
		};
		return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
	}

	private static uint SendDropKey()
	{
		NativeMethods.Input[] inputs =
		{
			KeyInput((ushort)NativeMethods.VkLControl, false),
			KeyInput(0x51, false),
			KeyInput(0x51, true),
			KeyInput((ushort)NativeMethods.VkLControl, true)
		};
		return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
	}

	private static void ReleaseInjectedModifiers(PlannedActionKind kind)
	{
		if (kind is PlannedActionKind.EquipArmor or PlannedActionKind.UnequipArmor)
			NativeMethods.SendInput(1u, new[] { KeyInput((ushort)NativeMethods.VkLShift, true) }, Marshal.SizeOf<NativeMethods.Input>());
		else if (kind == PlannedActionKind.DropItem)
			NativeMethods.SendInput(1u, new[] { KeyInput((ushort)NativeMethods.VkLControl, true) }, Marshal.SizeOf<NativeMethods.Input>());
	}

	private static NativeMethods.Input KeyInput(ushort key, bool keyUp) => new()
	{
		Type = NativeMethods.InputKeyboard,
		Data = new NativeMethods.InputUnion
		{
			Keyboard = new NativeMethods.KeyboardInput { VirtualKey = key, Flags = keyUp ? NativeMethods.KeyEventKeyUp : 0u }
		}
	};

	private static NativeMethods.Input MouseInput(uint flags) => new()
	{
		Type = NativeMethods.InputMouse,
		Data = new NativeMethods.InputUnion { Mouse = new NativeMethods.MouseInput { Flags = flags } }
	};

	private void RestoreCursor()
	{
		if (!savedCursor) return;
		NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
		savedCursor = false;
	}

	private void ClearPending()
	{
		pendingSource = -1;
		pendingTarget = -1;
		pendingSourceSignature = string.Empty;
		pendingTargetSignature = string.Empty;
		pendingCheckAtMs = 0;
		pendingActionKind = PlannedActionKind.HotbarSwap;
	}

	private void ResetSession()
	{
		RestoreCursor();
		inventoryWasOpen = false;
		savedCursor = false;
		nextActionMs = 0;
		ClearPending();
		preparedAction = null;
		blockedInventorySignature = string.Empty;
		detectionActive = false;
		detectionEndsMs = 0;
		nextDetectionLogMs = 0;
	}

	public void Dispose()
	{
		timer.Stop();
		timer.Dispose();
		RestoreCursor();
	}
}
