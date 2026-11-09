using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace McPetOverlay;

internal sealed class QuickItemUseController
{
	private readonly Func<bool> canAutomate;
	private readonly Func<bool> isGameplayScreenClear;
	private readonly Func<PlayerInventorySnapshot> snapshotProvider;
	private int busy;

	internal QuickItemUseController(Func<bool> canAutomate, Func<bool> isGameplayScreenClear, Func<PlayerInventorySnapshot> snapshotProvider)
	{
		this.canAutomate = canAutomate;
		this.isGameplayScreenClear = isGameplayScreenClear;
		this.snapshotProvider = snapshotProvider;
	}

	internal void TryUse(InventoryItemKind kind)
	{
		if (!canAutomate() || !isGameplayScreenClear() || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;

		PlayerInventorySnapshot snapshot = snapshotProvider();
		InventoryItemSnapshot item = snapshot.MainSlots
			.Where(candidate => candidate.InventoryIndex is >= 0 and < 9 && !candidate.IsEmpty && candidate.Kind == kind)
			.OrderByDescending(candidate => candidate.InventoryIndex == snapshot.SelectedHotbarSlot)
			.ThenByDescending(candidate => candidate.Count)
			.FirstOrDefault();
		if (item.IsEmpty || snapshot.SelectedHotbarSlot is < 0 or > 8)
		{
			Interlocked.Exchange(ref busy, 0);
			return;
		}

		int originalSlot = snapshot.SelectedHotbarSlot;
		int targetSlot = item.InventoryIndex;
		Thread thread = new Thread(() => UseItem(kind, originalSlot, targetSlot))
		{
			IsBackground = true,
			Name = kind == InventoryItemKind.EnderPearl ? "QuickPearl" : "QuickFireball"
		};
		thread.Start();
	}

	private void UseItem(InventoryItemKind kind, int originalSlot, int targetSlot)
	{
		bool switched = false;
		try
		{
			if (!canAutomate() || !isGameplayScreenClear()) return;
			if (targetSlot != originalSlot)
			{
				SendKey('1' + targetSlot);
				switched = true;
				Thread.Sleep(45);
			}
			if (!canAutomate() || !isGameplayScreenClear()) return;
			SendRightClick();
			Thread.Sleep(90);
			if (switched && canAutomate() && isGameplayScreenClear())
			{
				SendKey('1' + originalSlot);
				switched = false;
			}
			AppLog.Write($"[QUICK-ITEM] Kind={kind} Slot={targetSlot + 1} Restored={originalSlot + 1}");
		}
		finally
		{
			if (switched && canAutomate() && isGameplayScreenClear()) SendKey('1' + originalSlot);
			Interlocked.Exchange(ref busy, 0);
		}
	}

	private static void SendKey(int virtualKey)
	{
		NativeMethods.Input[] inputs = { KeyInput(virtualKey, false), KeyInput(virtualKey, true) };
		NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
	}

	private static void SendRightClick()
	{
		NativeMethods.Input[] inputs = { MouseInput(NativeMethods.MouseEventRightDown), MouseInput(NativeMethods.MouseEventRightUp) };
		NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
	}

	private static NativeMethods.Input KeyInput(int key, bool keyUp) => new()
	{
		Type = NativeMethods.InputKeyboard,
		Data = new NativeMethods.InputUnion
		{
			Keyboard = new NativeMethods.KeyboardInput { VirtualKey = (ushort)key, Flags = keyUp ? NativeMethods.KeyEventKeyUp : 0u }
		}
	};

	private static NativeMethods.Input MouseInput(uint flags) => new()
	{
		Type = NativeMethods.InputMouse,
		Data = new NativeMethods.InputUnion { Mouse = new NativeMethods.MouseInput { Flags = flags } }
	};
}
