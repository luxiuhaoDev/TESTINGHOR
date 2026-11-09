using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class ChestStealerController : IDisposable
{
	private readonly AppConfig config;
	private readonly Func<bool> canRun;
	private readonly Func<ChestInventorySnapshot> snapshotProvider;
	private readonly Func<PlayerInventorySnapshot> playerInventoryProvider;
	private readonly Func<Rectangle?> clientBoundsProvider;
	private readonly Action exitProgram;
	private readonly Timer timer = new() { Interval = 10 };
	private readonly Stopwatch clock = Stopwatch.StartNew();
	private readonly Dictionary<int, int> attempts = new();
	private long nextActionMs;
	private bool chestWasOpen;
	private bool savedCursor;
	private bool cursorRestored;
	private NativeMethods.Point originalCursor;
	private int activeRows;
	private bool injectedShiftDown;
	private int injectedModifierKey;
	private long shiftReleaseAtMs;
	private int pendingSlot = -1;
	private string pendingItemName = string.Empty;
	private long pendingCheckAtMs;
	private int currentPass;
	private bool closeSent;

	internal ChestStealerController(AppConfig config, Func<bool> canRun, Func<ChestInventorySnapshot> snapshotProvider, Func<PlayerInventorySnapshot> playerInventoryProvider, Func<Rectangle?> clientBoundsProvider, Action exitProgram)
	{
		this.config = config;
		this.canRun = canRun;
		this.snapshotProvider = snapshotProvider;
		this.playerInventoryProvider = playerInventoryProvider;
		this.clientBoundsProvider = clientBoundsProvider;
		this.exitProgram = exitProgram;
		timer.Tick += (_, _) => Tick();
		timer.Start();
	}

	private void Tick()
	{
		long now = clock.ElapsedMilliseconds;
		if (injectedShiftDown && now >= shiftReleaseAtMs)
		{
			SendKey(injectedModifierKey, true);
			injectedShiftDown = false;
			injectedModifierKey = 0;
		}
		if (!config.ChestStealerEnabled || !canRun())
		{
			ResetSession();
			return;
		}

		ChestInventorySnapshot snapshot = snapshotProvider();
		if (!snapshot.IsOpen || snapshot.Rows is < 1 or > 6)
		{
			ResetSession();
			return;
		}

		if (!chestWasOpen || activeRows != snapshot.Rows)
		{
			BeginSession(snapshot.Rows, now);
		}
		if (now < nextActionMs)
		{
			return;
		}
		PlayerInventorySnapshot playerInventory = playerInventoryProvider();

		if (pendingSlot >= 0 && snapshot.OccupancyReliable)
		{
			ChestSlotSnapshot[] matches = snapshot.Slots.Where(slot => slot.Index == pendingSlot).Take(1).ToArray();
			bool removed = matches.Length == 0 || !string.Equals(matches[0].ItemName, pendingItemName, StringComparison.Ordinal);
			if (removed)
			{
				ClearPendingSlot();
			}
			else if (now < pendingCheckAtMs)
			{
				return;
			}
			else
			{
				ClearPendingSlot();
			}
		}

		Rectangle? clientBounds = clientBoundsProvider();
		if (!clientBounds.HasValue)
		{
			return;
		}

		int slot = pendingSlot >= 0 ? pendingSlot : FindNextSlot(snapshot, playerInventory);
		if (slot < 0)
		{
			if (TryBeginSecondPass(snapshot, playerInventory, now))
			{
				return;
			}
			if ((config.ChestStealerAutoClose || config.ChestStealerExitProgramOnComplete) && snapshot.OccupancyReliable && !HasEligibleItems(snapshot, playerInventory) && !closeSent)
			{
				ReleaseShift();
				RestoreCursor();
				if (config.ChestStealerAutoClose)
				{
					SendKey((int)Keys.Escape, false);
					SendKey((int)Keys.Escape, true);
				}
				closeSent = true;
				nextActionMs = now + 500;
				if (config.ChestStealerExitProgramOnComplete)
				{
					exitProgram();
				}
				return;
			}
			RestoreCursor();
			nextActionMs = now + 160;
			return;
		}

		if (!savedCursor && NativeMethods.GetCursorPos(out originalCursor))
		{
			savedCursor = true;
		}
		cursorRestored = false;
		Point point = ResolveSlotCenter(clientBounds.Value, snapshot, slot);
		NativeMethods.SetCursorPos(point.X, point.Y);
		SendShiftClick(now);
		attempts[slot] = attempts.TryGetValue(slot, out int count) ? count + 1 : 1;
		if (snapshot.OccupancyReliable)
		{
			ChestSlotSnapshot clicked = snapshot.Slots.First(candidate => candidate.Index == slot);
			pendingSlot = slot;
			pendingItemName = clicked.ItemName;
			pendingCheckAtMs = now + Math.Max(90, 1000 / Math.Clamp(config.ChestStealerSpeedCps, 1, 20));
		}
		RestoreCursor();
		nextActionMs = now + Math.Max(50, 1000 / Math.Clamp(config.ChestStealerSpeedCps, 1, 20));
	}

	private int FindNextSlot(ChestInventorySnapshot snapshot, PlayerInventorySnapshot playerInventory)
	{
		int limit = snapshot.Rows * 9;
		string[] blacklist = ParseBlacklist(config.ChestStealerBlacklist);
		if (!snapshot.OccupancyReliable && (blacklist.Length > 0 || config.ChestStealerUseInventoryPriority))
		{
			return -1;
		}
		IEnumerable<int> candidates = snapshot.OccupancyReliable
			? snapshot.Slots.Where(slot => IsEligible(slot, playerInventory, blacklist)).Select(slot => slot.Index)
			: Enumerable.Range(0, limit);
		foreach (int slot in candidates)
		{
			if (slot < 0 || slot >= limit)
			{
				continue;
			}
			if (!attempts.ContainsKey(slot))
			{
				return slot;
			}
		}
		return -1;
	}

	private bool TryBeginSecondPass(ChestInventorySnapshot snapshot, PlayerInventorySnapshot playerInventory, long now)
	{
		if (!snapshot.OccupancyReliable || currentPass != 1)
		{
			return false;
		}
		string[] blacklist = ParseBlacklist(config.ChestStealerBlacklist);
		bool hasRemaining = snapshot.Slots.Any(slot => IsEligible(slot, playerInventory, blacklist));
		if (!hasRemaining)
		{
			return false;
		}
		attempts.Clear();
		ClearPendingSlot();
		currentPass = 2;
		nextActionMs = now + 180;
		return true;
	}

	private bool HasEligibleItems(ChestInventorySnapshot snapshot, PlayerInventorySnapshot playerInventory)
	{
		string[] blacklist = ParseBlacklist(config.ChestStealerBlacklist);
		return snapshot.Slots.Any(slot => IsEligible(slot, playerInventory, blacklist));
	}

	private bool IsEligible(ChestSlotSnapshot slot, PlayerInventorySnapshot inventory, string[] blacklist)
	{
		InventoryItemSnapshot item = slot.Item;
		if (item.IsEmpty || IsBlacklisted(item.InternalName, blacklist)) return false;
		if (!config.ChestStealerUseInventoryPriority) return true;
		if (config.InventoryManagerProtectCustomNamed && item.HasCustomName) return false;
		IEnumerable<InventoryItemSnapshot> owned = inventory.MainSlots.Concat(inventory.ArmorSlots).Where(value => !value.IsEmpty);

		return item.Kind switch
		{
			InventoryItemKind.Helmet or InventoryItemKind.Chestplate or InventoryItemKind.Leggings or InventoryItemKind.Boots
				=> IsBetter(item, owned.Where(value => value.Kind == item.Kind), InventoryItemEvaluator.ScoreArmor),
			InventoryItemKind.Sword
				=> config.InventoryManagerSwitchSword && IsBetter(item, owned.Where(value => value.Kind is InventoryItemKind.Sword or InventoryItemKind.Axe), InventoryItemEvaluator.ScoreWeapon),
			InventoryItemKind.Pickaxe
				=> config.InventoryManagerSwitchPickaxe && IsBetter(item, owned.Where(value => value.Kind == item.Kind), InventoryItemEvaluator.ScoreTool),
			InventoryItemKind.Axe
				=> config.InventoryManagerSwitchAxe && (IsBetter(item, owned.Where(value => value.Kind == item.Kind), InventoryItemEvaluator.ScoreTool) || config.InventoryManagerSwitchSword && IsBetter(item, owned.Where(value => value.Kind is InventoryItemKind.Sword or InventoryItemKind.Axe), InventoryItemEvaluator.ScoreWeapon)),
			InventoryItemKind.Bow
				=> config.InventoryManagerSwitchBow && IsBetter(item, owned.Where(value => value.Kind == item.Kind), config.InventoryManagerBowPriority == 1 ? InventoryItemEvaluator.ScorePunchBow : InventoryItemEvaluator.ScorePowerBow),
			InventoryItemKind.FishingRod
				=> config.InventoryManagerSwitchRod && !owned.Any(value => value.Kind == InventoryItemKind.FishingRod),
			InventoryItemKind.Block
				=> config.InventoryManagerSwitchBlock && InventoryItemEvaluator.IsValidBlock(item) && Count(owned, InventoryItemKind.Block) < config.InventoryManagerMaxBlockSize,
			InventoryItemKind.WaterBucket
				=> config.InventoryManagerSwitchWaterBucket && Count(owned, InventoryItemKind.WaterBucket) < config.InventoryManagerKeepWaterBuckets,
			InventoryItemKind.LavaBucket
				=> Count(owned, InventoryItemKind.LavaBucket) < config.InventoryManagerKeepLavaBuckets,
			InventoryItemKind.EnderPearl => config.InventoryManagerSwitchEnderPearl,
			InventoryItemKind.FireCharge => config.InventoryManagerSwitchFireball,
			InventoryItemKind.GoldenApple => config.InventoryManagerSwitchGoldenApple,
			InventoryItemKind.Arrow => Count(owned, InventoryItemKind.Arrow) < config.InventoryManagerMaxArrowSize,
			InventoryItemKind.Egg or InventoryItemKind.Snowball
				=> config.InventoryManagerKeepProjectile && Count(owned, InventoryItemKind.Egg, InventoryItemKind.Snowball) < config.InventoryManagerMaxProjectileSize,
			_ => false
		};
	}

	private static bool IsBetter(InventoryItemSnapshot candidate, IEnumerable<InventoryItemSnapshot> owned, Func<InventoryItemSnapshot, float> score)
	{
		InventoryItemSnapshot[] values = owned.ToArray();
		if (values.Length == 0) return true;
		float candidateScore = score(candidate);
		float bestOwned = values.Max(score);
		return candidateScore > bestOwned + 0.0001f;
	}

	private static int Count(IEnumerable<InventoryItemSnapshot> items, params InventoryItemKind[] kinds)
	{
		HashSet<InventoryItemKind> accepted = kinds.ToHashSet();
		return items.Where(item => accepted.Contains(item.Kind)).Sum(item => item.Count);
	}

	private static string[] ParseBlacklist(string text)
	{
		return (text ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static bool IsBlacklisted(string itemName, string[] blacklist)
	{
		if (string.IsNullOrWhiteSpace(itemName))
		{
			return false;
		}
		return blacklist.Any(keyword => itemName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
	}

	private void BeginSession(int rows, long now)
	{
		RestoreCursor();
		attempts.Clear();
		activeRows = rows;
		chestWasOpen = true;
		currentPass = 1;
		closeSent = false;
		savedCursor = false;
		cursorRestored = false;
		ClearPendingSlot();
		nextActionMs = now + 220;
	}

	private void ResetSession()
	{
		ReleaseShift();
		RestoreCursor();
		attempts.Clear();
		ClearPendingSlot();
		activeRows = 0;
		chestWasOpen = false;
		currentPass = 0;
		closeSent = false;
		nextActionMs = 0;
	}

	private void ClearPendingSlot()
	{
		pendingSlot = -1;
		pendingItemName = string.Empty;
		pendingCheckAtMs = 0;
	}

	private void RestoreCursor()
	{
		if (savedCursor && !cursorRestored)
		{
			NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
			cursorRestored = true;
		}
	}

	private static Point ResolveSlotCenter(Rectangle client, ChestInventorySnapshot snapshot, int slot)
	{
		int column = slot % 9;
		int row = slot / 9;
		int scaledWidth = Math.Max(1, snapshot.ScaledWidth);
		int scaledHeight = Math.Max(1, snapshot.ScaledHeight);
		double ratioX = client.Width / (double)scaledWidth;
		double ratioY = client.Height / (double)scaledHeight;
		int x = client.Left + (int)Math.Round((snapshot.GuiLeft + 16 + column * 18) * ratioX);
		int y = client.Top + (int)Math.Round((snapshot.GuiTop + 26 + row * 18) * ratioY);
		return new Point(x, y);
	}

	private void SendShiftClick(long now)
	{
		if (!injectedShiftDown)
		{
			injectedModifierKey = Math.Clamp(config.ChestStealerModifierKey, 1, 255);
			SendKey(injectedModifierKey, false);
			injectedShiftDown = true;
		}
		uint mouseDown = config.ChestStealerMouseButton == 1 ? NativeMethods.MouseEventRightDown : NativeMethods.MouseEventLeftDown;
		uint mouseUp = config.ChestStealerMouseButton == 1 ? NativeMethods.MouseEventRightUp : NativeMethods.MouseEventLeftUp;
		NativeMethods.Input[] inputs =
		{
			MouseInput(mouseDown),
			MouseInput(mouseUp)
		};
		NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
		shiftReleaseAtMs = now + 35;
	}

	private static void SendKey(int virtualKey, bool keyUp)
	{
		NativeMethods.Input input = KeyInput(virtualKey, keyUp);
		NativeMethods.SendInput(1u, new[] { input }, Marshal.SizeOf<NativeMethods.Input>());
	}

	private void ReleaseShift()
	{
		if (injectedShiftDown)
		{
			SendKey(injectedModifierKey, true);
			injectedShiftDown = false;
			injectedModifierKey = 0;
		}
	}

	private static NativeMethods.Input KeyInput(int virtualKey, bool keyUp) => new()
	{
		Type = NativeMethods.InputKeyboard,
		Data = new NativeMethods.InputUnion
		{
			Keyboard = new NativeMethods.KeyboardInput
			{
				VirtualKey = (ushort)virtualKey,
				Flags = keyUp ? NativeMethods.KeyEventKeyUp : 0u
			}
		}
	};

	private static NativeMethods.Input MouseInput(uint flags) => new()
	{
		Type = NativeMethods.InputMouse,
		Data = new NativeMethods.InputUnion
		{
			Mouse = new NativeMethods.MouseInput { Flags = flags }
		}
	};

	public void Dispose()
	{
		timer.Stop();
		timer.Dispose();
		RestoreCursor();
		ReleaseShift();
	}
}
