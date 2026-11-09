using System;
using System.Collections.Generic;

namespace McPetOverlay;

internal readonly record struct ChestSlotSnapshot(int Index, InventoryItemSnapshot Item)
{
	internal string ItemName => Item.InternalName;
}

internal readonly record struct ChestInventorySnapshot(bool IsOpen, int Rows, IReadOnlyList<ChestSlotSnapshot> Slots, bool OccupancyReliable, int GuiLeft, int GuiTop, int ScaledWidth, int ScaledHeight)
{
	internal static ChestInventorySnapshot Closed => new(false, 0, Array.Empty<ChestSlotSnapshot>(), false, 0, 0, 0, 0);
}
