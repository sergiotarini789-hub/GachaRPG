using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized equipment configuration: per-rarity level caps and
/// multipliers, secondary-stat counts and roll ranges, upgrade costs, sell
/// values, the primary-stat rules per slot, the deterministic roll helper,
/// and the equipment set registry. Every system (EquipmentInstance,
/// EquipmentFactory, EquipmentInventory, EquipmentStats, save/load, UI)
/// reads these values from here - numeric equipment values must never be
/// scattered across scripts. Mirrors <see cref="HeroProgression"/>.
///
/// All values are prototype placeholders chosen so the whole equipment loop
/// is testable before a real economy exists; tune them here in one place.
/// Rarity-indexed arrays follow <see cref="HeroRarity"/> order
/// (Common, Rare, Epic, Legendary); stat-indexed arrays follow
/// <see cref="EquipmentStatType"/> order
/// (HP, ATK, DEF, SPD, CRIT_RATE, CRIT_DAMAGE, ACCURACY, RESISTANCE).
/// </summary>
public static class EquipmentProgression
{
    // ---------------------------------------------------------------------
    // Rarity rules: level cap, stat scaling, secondary-stat count,
    // upgrade/sell multipliers (index = HeroRarity).
    // ---------------------------------------------------------------------

    /// <summary>Maximum equipment level per rarity: Common 8, Rare 12, Epic 16, Legendary 20.</summary>
    public static readonly int[] MaxLevelByRarity = { 8, 12, 16, 20 };

    /// <summary>The highest level an item of this rarity can reach (the item's own max level).</summary>
    public static int MaxLevelFor(HeroRarity rarity)
    {
        return MaxLevelByRarity[Mathf.Clamp((int)rarity, 0, MaxLevelByRarity.Length - 1)];
    }

    /// <summary>
    /// Base stat multiplier per rarity: scales the whole primary-stat curve
    /// (base + per-level growth) of whatever definition the item was
    /// generated from, so the same definition produced at a higher rarity
    /// is strictly stronger.
    /// </summary>
    public static readonly float[] BaseStatMultiplierByRarity = { 1f, 1.15f, 1.3f, 1.5f };

    /// <summary>The rarity's base stat multiplier (1 for unknown rarities).</summary>
    public static float BaseStatMultiplier(HeroRarity rarity)
    {
        return BaseStatMultiplierByRarity[Mathf.Clamp((int)rarity, 0, BaseStatMultiplierByRarity.Length - 1)];
    }

    /// <summary>
    /// How many secondary stats an item of this rarity rolls at creation:
    /// Common 0, Rare 1, Epic 2, Legendary 3. Fixed per rarity (data-driven
    /// here), so secondary-stat COUNT never depends on chance.
    /// </summary>
    public static readonly int[] SecondaryStatCountByRarity = { 0, 1, 2, 3 };

    /// <summary>
    /// Upgrade cost multiplier per rarity: the base gold cost (per level)
    /// times this multiplier, so higher-rarity items cost more to upgrade.
    /// </summary>
    public static readonly float[] UpgradeCostMultiplierByRarity = { 1f, 1.25f, 1.5f, 2f };

    // ---------------------------------------------------------------------
    // Equipment upgrades
    // ---------------------------------------------------------------------

    /// <summary>
    /// Base gold cost of upgrading FROM the given (current) equipment
    /// level, before the rarity multiplier: UpgradeGoldCostPerLevel x
    /// current level, so each successive level costs more.
    /// </summary>
    public const int UpgradeGoldCostPerLevel = 100;

    /// <summary>
    /// Gold cost to upgrade from <paramref name="currentLevel"/> to the
    /// next level at the item's rarity: base x level x rarity multiplier,
    /// rounded. Always positive.
    /// </summary>
    public static int UpgradeGoldCost(int currentLevel, HeroRarity rarity)
    {
        float multiplier = UpgradeCostMultiplierByRarity[Mathf.Clamp((int)rarity, 0, UpgradeCostMultiplierByRarity.Length - 1)];
        return Mathf.Max(1, Mathf.RoundToInt(UpgradeGoldCostPerLevel * Mathf.Max(1, currentLevel) * multiplier));
    }

    /// <summary>
    /// Equipment levels at which a secondary-stat boost occurs: one
    /// existing secondary stat deterministically gains extra value (items
    /// without secondary stats simply skip the boost). Configurable here.
    /// </summary>
    public static readonly int[] SecondaryBoostLevels = { 4, 8, 12, 16, 20 };

    /// <summary>Whether the given equipment level is a secondary-stat boost level.</summary>
    public static bool IsSecondaryBoostLevel(int level)
    {
        return Array.IndexOf(SecondaryBoostLevels, level) >= 0;
    }

    // ---------------------------------------------------------------------
    // Secondary stats (rolled once at creation / boost levels and then
    // persisted on the instance - never regenerated during loading)
    // ---------------------------------------------------------------------

    /// <summary>The maximum number of secondary stats one item can have.</summary>
    public const int MaxSecondaryStats = 4;

    /// <summary>Minimum rolled secondary value per stat type (index = EquipmentStatType; flats and percents alike).</summary>
    public static readonly int[] SecondaryStatMinValue = { 20, 5, 4, 2, 1, 3, 2, 2 };

    /// <summary>Maximum rolled secondary value per stat type (index = EquipmentStatType).</summary>
    public static readonly int[] SecondaryStatMaxValue = { 60, 15, 10, 5, 4, 8, 6, 6 };

    /// <summary>Whether the stat type is a percentage stat (displayed and carried as a percent; false = flat battle stat).</summary>
    public static bool IsPercentStat(EquipmentStatType statType)
    {
        return statType >= EquipmentStatType.CRIT_RATE;
    }

    /// <summary>
    /// Deterministically picks a secondary stat type not already in
    /// <paramref name="exclude"/> (empty candidates falls back to a plain
    /// roll - future stat types make this unreachable with the current cap).
    /// </summary>
    public static EquipmentStatType RollSecondaryStatType(string seed, List<EquipmentStatType> exclude)
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
    /// Deterministically rolls a secondary value for the stat type: a value
    /// in the stat's configured range (flats and percents each have their
    /// own range - see <see cref="SecondaryStatMinValue"/>).
    /// </summary>
    public static int RollSecondaryStatValue(string seed, EquipmentStatType statType)
    {
        int index = Mathf.Clamp((int)statType, 0, SecondaryStatMinValue.Length - 1);
        int min = SecondaryStatMinValue[index];
        int max = Mathf.Max(min, SecondaryStatMaxValue[index]);
        return Mathf.Max(1, min + DeterministicRoll(seed, max - min + 1));
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
    // Sell value (the Sell action consumes this; no shop exists)
    // ---------------------------------------------------------------------

    /// <summary>Base sell value per rarity (index = HeroRarity).</summary>
    public static readonly int[] SellValueBaseByRarity = { 50, 150, 500, 1500 };

    /// <summary>Additional sell value per equipment level above 1.</summary>
    public const int SellValuePerLevel = 25;

    /// <summary>Sell value multiplier per slot type (index = EquipmentSlot): heavier pieces are worth more.</summary>
    public static readonly float[] SlotSellValueMultiplier = { 1.2f, 1f, 1.1f, 0.9f, 0.9f, 1.3f };

    /// <summary>
    /// The item's sell value: a rarity-based base plus a per-level amount,
    /// scaled by the slot type - the single configurable formula, never
    /// recomputed elsewhere.
    /// </summary>
    public static int SellValueFor(HeroRarity rarity, int level, EquipmentSlot slot)
    {
        int baseValue = SellValueBaseByRarity[Mathf.Clamp((int)rarity, 0, SellValueBaseByRarity.Length - 1)];
        float slotMultiplier = SlotSellValueMultiplier[Mathf.Clamp((int)slot, 0, SlotSellValueMultiplier.Length - 1)];
        return Mathf.Max(1, Mathf.RoundToInt((baseValue + SellValuePerLevel * Mathf.Max(0, level - 1)) * slotMultiplier));
    }

    // ---------------------------------------------------------------------
    // Primary-stat rules per slot (single table; extend here, never in UI)
    // ---------------------------------------------------------------------

    /// <summary>
    /// The primary stats each slot accepts: Weapon ATK-only, Helmet
    /// HP-only, Armor DEF-only, Gloves ATK or CRIT_RATE, Boots SPD-only,
    /// Accessory HP / DEF / CRIT_DAMAGE. A definition's chosen primary
    /// stat must be one of its slot's allowed stats; the factory only
    /// produces legal combinations.
    /// </summary>
    public static readonly EquipmentStatType[][] AllowedPrimaryStats =
    {
        new[] { EquipmentStatType.ATK },                                  // Weapon
        new[] { EquipmentStatType.HP },                                   // Helmet
        new[] { EquipmentStatType.DEF },                                  // Armor
        new[] { EquipmentStatType.ATK, EquipmentStatType.CRIT_RATE },     // Gloves
        new[] { EquipmentStatType.SPD },                                  // Boots
        new[] { EquipmentStatType.HP, EquipmentStatType.DEF, EquipmentStatType.CRIT_DAMAGE }, // Accessory
    };

    /// <summary>Whether the slot allows the given primary stat (table-driven, no UI or gameplay hardcoding).</summary>
    public static bool IsPrimaryStatAllowed(EquipmentSlot slot, EquipmentStatType statType)
    {
        EquipmentStatType[] allowed = AllowedPrimaryStats[Mathf.Clamp((int)slot, 0, AllowedPrimaryStats.Length - 1)];
        return Array.IndexOf(allowed, statType) >= 0;
    }

    // ---------------------------------------------------------------------
    // Equipment sets (bonuses are stats, calculated separately from
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

    /// <summary>The stat the bonus grants.</summary>
    public EquipmentStatType StatType;

    /// <summary>The value added to that stat (flat or percent, matching the stat type).</summary>
    public int Value;
}
