using System;
using System.Collections.Generic;
using System.Linq;

namespace McPetOverlay;

internal static class InventoryItemEvaluator
{
	private const int Protection = 0;
	private const int Sharpness = 16;
	private const int Efficiency = 32;
	private const int Power = 48;
	private const int Punch = 49;
	private const int Flame = 50;
	private const int Infinity = 51;

	internal static float ScoreWeapon(InventoryItemSnapshot item)
	{
		int sharpness = item.EnchantmentLevel(Sharpness);
		return item.BaseAttackDamage + (sharpness > 0 ? sharpness * 0.5f + 0.5f : 0f);
	}

	internal static float ScoreTool(InventoryItemSnapshot item)
	{
		int efficiency = item.EnchantmentLevel(Efficiency);
		return item.ToolEfficiency + (efficiency > 0 ? efficiency * 0.0075f : 0f);
	}

	internal static float ScoreArmor(InventoryItemSnapshot item) => item.ArmorReduction * 100f + item.EnchantmentLevel(Protection);

	internal static float ScorePowerBow(InventoryItemSnapshot item)
	{
		float durabilityTieBreaker = item.MaxDamage > 0 ? (item.MaxDamage - item.Damage) / (float)item.MaxDamage * 0.001f : 0f;
		return 10f + item.EnchantmentLevel(Punch) / 10f + item.EnchantmentLevel(Infinity) + item.EnchantmentLevel(Flame) + item.EnchantmentLevel(Power) + durabilityTieBreaker;
	}

	internal static float ScorePunchBow(InventoryItemSnapshot item)
	{
		float durabilityTieBreaker = item.MaxDamage > 0 ? (item.MaxDamage - item.Damage) / (float)item.MaxDamage * 0.001f : 0f;
		return 10f + item.EnchantmentLevel(Punch) + item.EnchantmentLevel(Infinity) + item.EnchantmentLevel(Flame) + item.EnchantmentLevel(Power) / 10f + durabilityTieBreaker;
	}

	internal static InventoryItemSnapshot? Best(IEnumerable<InventoryItemSnapshot> items, Func<InventoryItemSnapshot, bool> predicate, Func<InventoryItemSnapshot, float> score, int preferredIndex = -1)
	{
		InventoryItemSnapshot? best = null;
		float bestScore = float.MinValue;
		foreach (InventoryItemSnapshot item in items)
		{
			if (item.IsEmpty || !predicate(item)) continue;
			float current = score(item);
			if (!best.HasValue || current > bestScore || Math.Abs(current - bestScore) < 0.0001f && Prefer(item, best.Value, preferredIndex))
			{
				best = item;
				bestScore = current;
			}
		}
		return best;
	}

	internal static InventoryItemSnapshot? LargestStack(IEnumerable<InventoryItemSnapshot> items, Func<InventoryItemSnapshot, bool> predicate, int preferredIndex = -1)
	{
		return items.Where(item => !item.IsEmpty && predicate(item)).OrderByDescending(item => item.Count).ThenByDescending(item => item.InventoryIndex == preferredIndex).ThenBy(item => item.Damage).Cast<InventoryItemSnapshot?>().FirstOrDefault();
	}

	internal static bool Prefer(InventoryItemSnapshot left, InventoryItemSnapshot right, int preferredIndex = -1)
	{
		if (left.InventoryIndex == preferredIndex || right.InventoryIndex == preferredIndex) return left.InventoryIndex == preferredIndex;
		int leftRemaining = left.MaxDamage > 0 ? left.MaxDamage - left.Damage : int.MaxValue;
		int rightRemaining = right.MaxDamage > 0 ? right.MaxDamage - right.Damage : int.MaxValue;
		if (leftRemaining != rightRemaining) return leftRemaining > rightRemaining;
		if (left.Count != right.Count) return left.Count > right.Count;
		return left.InventoryIndex < right.InventoryIndex;
	}

	internal static bool IsValidBlock(InventoryItemSnapshot item)
	{
		if (item.Kind != InventoryItemKind.Block) return false;
		string[] blocked = { "flower", "sapling", "button", "pressurePlate", "tnt", "chest", "workbench", "craftingTable", "torch", "ladder", "fence", "web", "carpet" };
		return !blocked.Any(word => item.InternalName.Contains(word, StringComparison.OrdinalIgnoreCase));
	}
}
