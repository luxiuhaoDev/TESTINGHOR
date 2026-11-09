using System;
using System.Collections.Generic;
using System.Linq;

namespace McPetOverlay;

internal enum InventoryItemKind
{
	Unknown,
	Sword,
	Pickaxe,
	Axe,
	Shovel,
	Bow,
	FishingRod,
	Block,
	Helmet,
	Chestplate,
	Leggings,
	Boots,
	WaterBucket,
	LavaBucket,
	EnderPearl,
	FireCharge,
	GoldenApple,
	Arrow,
	Egg,
	Snowball,
	Compass,
	Food,
	Potion
}

internal readonly record struct InventoryItemSnapshot(
	int InventoryIndex,
	string InternalName,
	InventoryItemKind Kind,
	int Count,
	int Damage,
	int MaxDamage,
	float BaseAttackDamage,
	float ToolEfficiency,
	int ArmorReduction,
	int ArmorType,
	IReadOnlyDictionary<int, int> Enchantments,
	bool HasCustomName)
{
	internal bool IsEmpty => string.IsNullOrEmpty(InternalName) || Count <= 0;

	internal int EnchantmentLevel(int id) => Enchantments.TryGetValue(id, out int level) ? level : 0;

	internal string Signature => $"{InternalName}|{Count}|{Damage}|{string.Join(',', (Enchantments ?? new Dictionary<int, int>()).OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value))}";
}

internal readonly record struct PlayerInventorySnapshot(
	bool IsOpen,
	IReadOnlyList<InventoryItemSnapshot> MainSlots,
	IReadOnlyList<InventoryItemSnapshot> ArmorSlots,
	int GuiLeft,
	int GuiTop,
	int ScaledWidth,
	int ScaledHeight,
	int SelectedHotbarSlot)
{
	internal static PlayerInventorySnapshot Closed => new(false, Array.Empty<InventoryItemSnapshot>(), Array.Empty<InventoryItemSnapshot>(), 0, 0, 0, 0, -1);
}
