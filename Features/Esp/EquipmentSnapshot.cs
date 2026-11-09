using System;

namespace McPetOverlay;

internal sealed record EquipmentItemSnapshot(string Kind, string Material, int Damage, int MaxDamage)
{
	internal float Durability => MaxDamage > 0 ? Math.Clamp(1f - (float)Damage / MaxDamage, 0f, 1f) : 1f;
}

internal sealed record PlayerEquipmentSnapshot(
	EquipmentItemSnapshot? HeldItem,
	EquipmentItemSnapshot? Boots,
	EquipmentItemSnapshot? Leggings,
	EquipmentItemSnapshot? Chestplate,
	EquipmentItemSnapshot? Helmet)
{
	internal static readonly PlayerEquipmentSnapshot Empty = new PlayerEquipmentSnapshot(null, null, null, null, null);

	internal EquipmentItemSnapshot?[] Slots => new[] { HeldItem, Helmet, Chestplate, Leggings, Boots };

	internal bool HasAny => HeldItem != null || Boots != null || Leggings != null || Chestplate != null || Helmet != null;
}
