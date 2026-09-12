using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized equipment configuration: the level cap, upgrade costs,
/// substat count/quality/milestone rules, sell values, main-stat rules per
/// slot, the deterministic roll helper, and the equipment set registry.
/// Every system (EquipmentInstance, EquipmentInventory, EquipmentStats,
/// save/load, UI) reads these values from here - numeric equipment values
/// must never be scattered across scripts. Mirrors <see cref="HeroProgression"/>.
///
/// All values are prototype placeholders chosen so the whole equipment loop
/// is testable before a real economy exists; tune them here in one place.
/// Rarity-indexed arrays follow <see cref="HeroRarity"/> order
/// (Common, Rare, Epic, Legendary); stat-indexed arrays follow
/// <see cref="EquipmentStatType"/> order (HP, ATK, DEF, SPD).
/// </summary>
public static class EquipmentProgression
{
    // ---------------------------------------------------------------------
    // Equipment level & upgrades
    // ---------------------------------------------------------------------

    /// <summary>The maximum level any equipment instance can reach.</summary>
    public const int MaxEquipmentLevel = 16;

    /// <summary>
    /// Gold cost of upgrading FROM the given (current) equipment level:
    /// UpgradeGoldCostPerLevel x current level, so each successive level
    /// costs more.
    /// </summary>
    public const int UpgradeGoldCostPerLevel = 100;

    /// <summary>Gold cost to upgrade from <paramref name="currentLevel"/> to the next level.</summary>
    public static int UpgradeGoldCost(int currentLevel)
    {
        return UpgradeGoldCostPerLevel * Mathf.Max(1, currentLevel);
    }

    /// <summary>
    /// Equipment levels at which a substat milestone occurs: the item either
    /// gains a new substat (below the substat cap) or boosts an existing one.
    /// </summary>
    public static readonly int[] SubstatMilestoneLevels = { 4, 8, 12, 16 };

    /// <summary>Whether the given equipment level is a substat milestone.</summary>
    public static bool IsSubstatMilestone(int level)
    {
        return Array.IndexOf(SubstatMilestoneLevels, level) >= 0;
    }

    // ---------------------------------------------------------------------
    // Substats (rolls happen once at creation / milestones and are then
    // persisted on the instance - never regenerated during loading)
    // ---------------------------------------------------------------------

    /// <summary>The maximum number of substats one item can have.</summary>
    public const int MaxSubstats = 4;

    /// <summary>Minimum starting substats per rarity (index = HeroRarity).</summary>
    public static readonly int[] SubstatCountMinByRarity = { 0, 1, 2, 3 };

    /// <summary>Maximum starting substats per rarity (index = HeroRarity).</summary>
    public static readonly int[] SubstatCountMaxByRarity = { 1, 2, 3, 4 };

    /// <summary>Substat quality multiplier per rarity (index = HeroRarity) - scales rolled values.</summary>
    public static readonly float[] SubstatQualityByRarity = { 1f, 1.25f, 1.5f, 2f };

    /// <summary>Minimum rolled substat value per stat type, before the rarity quality multiplier (index = EquipmentStatType).</summary>
    public static readonly int[] SubstatMinValue = { 20, 5, 4, 2 };

    /// <summary>Maximum rolled substat value per stat type, before the rarity quality multiplier (index = EquipmentStatType).</summary>
    public static readonly int[] SubstatMaxValue = { 60, 15, 10, 5 };

    /// <summary>
    /// Deterministically picks a substat type not already in
    /// <paramref name="exclude"/> (empty candidates falls back to a plain
    /// roll - future stat types make this unreachable with the current cap).
    /// </summary>
    public static EquipmentStatType RollSubstatType(string seed, List<EquipmentStatType> exclude)
    {
        List<EquipmentStatType> candidates = new List<EquipmentStatType>();
        foreach (EquipmentStatType type in (EquipmentStatType[])Enum.GetValues(typeof(EquipmentStatType)))
        {
            if (exclude == null || !exclude.Contains(type))
            {
                candidates.Add(type);
            }
        }

        if (candidates.Count == 0)
        {
            return EquipmentStatType.HP;
        }

        return candidates[DeterministicRoll(seed, candidates.Count)];
    }

    /// <summary>
    /// Deterministically rolls a substat value for the stat type: a value in
    /// the stat's configured range, scaled by the rarity's quality multiplier.
    /// </summary>
    public static int RollSubstatValue(string seed, EquipmentStatType statType, HeroRarity rarity)
    {
        int index = Mathf.Clamp((int)statType, 0, SubstatMinValue.Length - 1);
        int min = SubstatMinValue[index];
        int max = Mathf.Max(min, SubstatMaxValue[index]);
        int roll = min + DeterministicRoll(seed, max - min + 1);

        float quality = SubstatQualityByRarity[Mathf.Clamp((int)rarity, 0, SubstatQualityByRarity.Length - 1)];
        return Mathf.Max(1, Mathf.RoundToInt(roll * quality));
    }

    /// <summary>
    /// A simple deterministic roll (FNV-1a hash of the seed, modulo the
    /// range): the same seed always produces the same result, so equipment
    /// rolls are reproducible and testable. Replace with weighted
    /// randomization later without touching callers - persisted values never
    /// re-roll regardless.
    /// </summary>
    public static int DeterministicRoll(string seed, int exclusiveMax)
    {
        if (exclusiveMax <= 1 || string.IsNullOrEmpty(seed))
        {
            return 0;
        }

        uint hash = 2166136261u;
        foreach (char c in seed)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return (int)(hash % (uint)exclusiveMax);
    }

    // ---------------------------------------------------------------------
    // Sell value (the future Shop/Inventory consumes this; no shop exists yet)
    // ---------------------------------------------------------------------

    /// <summary>Base sell value per rarity (index = HeroRarity).</summary>
    public static readonly int[] SellValueBaseByRarity = { 50, 150, 500, 1500 };

    /// <summary>Additional sell value per equipment level above 1.</summary>
    public const int SellValuePerLevel = 25;

    /// <summary>
    /// The item's sell value: a rarity-based base plus a per-level amount -
    /// the single configurable formula, never recomputed elsewhere.
    /// </summary>
    public static int SellValueFor(HeroRarity rarity, int level)
    {
        int baseValue = SellValueBaseByRarity[Mathf.Clamp((int)rarity, 0, SellValueBaseByRarity.Length - 1)];
        return baseValue + SellValuePerLevel * Mathf.Max(0, level - 1);
    }

    // ---------------------------------------------------------------------
    // Main-stat rules per slot
    // ---------------------------------------------------------------------

    /// <summary>
    /// Whether the slot allows the given main stat: Weapon is ATK-only,
    /// Helmet HP-only, Armor DEF-only; Gloves, Boots, and Accessory accept
    /// any current stat type (their template decides). Future slots or stat
    /// types extend this one table.
    /// </summary>
    public static bool IsMainStatAllowed(EquipmentSlot slot, EquipmentStatType statType)
    {
        switch (slot)
        {
            case EquipmentSlot.Weapon:
                return statType == EquipmentStatType.ATK;
            case EquipmentSlot.Helmet:
                return statType == EquipmentStatType.HP;
            case EquipmentSlot.Armor:
                return statType == EquipmentStatType.DEF;
            case EquipmentSlot.Gloves:
            case EquipmentSlot.Boots:
            case EquipmentSlot.Accessory:
                return true;
            default:
                return true;
        }
    }

    // ---------------------------------------------------------------------
    // Equipment sets (bonuses are flat stats, calculated separately from
    // individual item stats by EquipmentStats)
    // ---------------------------------------------------------------------

    /// <summary>
    /// The known equipment sets, by set id. Templates reference sets by id;
    /// unknown ids simply grant no bonuses. Prototype content: one test set
    /// with a 2-piece and a 4-piece bonus (6-piece is deliberately not
    /// required); replace with authored set assets later without touching
    /// the calculation system.
    /// </summary>
    private static readonly EquipmentSetDefinition[] DefaultSets =
    {
        new EquipmentSetDefinition
        {
            SetId = "vanguard",
            SetName = "Vanguard",
            Bonuses = new[]
            {
                new EquipmentSetBonus { PiecesRequired = 2, StatType = EquipmentStatType.DEF, Value = 10 },
                new EquipmentSetBonus { PiecesRequired = 4, StatType = EquipmentStatType.HP, Value = 50 },
            },
        },
    };

    /// <summary>The set definition for the given set id, or null when unknown/empty.</summary>
    public static EquipmentSetDefinition FindSet(string setId)
    {
        if (string.IsNullOrEmpty(setId))
        {
            return null;
        }

        foreach (EquipmentSetDefinition set in DefaultSets)
        {
            if (set.SetId == setId)
            {
                return set;
            }
        }

        return null;
    }
}

/// <summary>One equipment set: its stable id, display name, and piece-count bonuses.</summary>
public class EquipmentSetDefinition
{
    /// <summary>Stable set identifier referenced by <see cref="EquipmentData.setId"/>.</summary>
    public string SetId;

    /// <summary>Display name.</summary>
    public string SetName;

    /// <summary>The set's bonuses in piece-count order (e.g. 2-piece, then 4-piece).</summary>
    public EquipmentSetBonus[] Bonuses;
}

/// <summary>One set bonus: granted while the hero has at least PiecesRequired set pieces equipped.</summary>
public class EquipmentSetBonus
{
    /// <summary>How many equipped pieces of the set activate this bonus.</summary>
    public int PiecesRequired;

    /// <summary>The flat stat the bonus grants.</summary>
    public EquipmentStatType StatType;

    /// <summary>The flat value added to that stat.</summary>
    public int Value;
}
