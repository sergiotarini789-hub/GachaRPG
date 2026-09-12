using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A bundle of the eight equipment stat values (index-aligned with
/// <see cref="EquipmentStatType"/>): flats (HP/ATK/DEF/SPD) and
/// percents (CRIT_RATE/CRIT_DAMAGE/ACCURACY/RESISTANCE) alike. Mutable
/// accumulator used while summing an item or a whole loadout; read as the
/// final equipment contribution.
/// </summary>
public struct EquipmentStatSet
{
    /// <summary>Flat HP.</summary>
    public int hp;

    /// <summary>Flat ATK.</summary>
    public int atk;

    /// <summary>Flat DEF.</summary>
    public int def;

    /// <summary>Flat SPD.</summary>
    public int spd;

    /// <summary>Crit chance, percent.</summary>
    public int critRate;

    /// <summary>Bonus crit damage, percent.</summary>
    public int critDamage;

    /// <summary>Accuracy, percent.</summary>
    public int accuracy;

    /// <summary>Resistance, percent.</summary>
    public int resistance;

    /// <summary>Resets every value to zero.</summary>
    public void Clear()
    {
        hp = 0;
        atk = 0;
        def = 0;
        spd = 0;
        critRate = 0;
        critDamage = 0;
        accuracy = 0;
        resistance = 0;
    }

    /// <summary>Adds a stat value to the matching field (any stat type).</summary>
    public void Add(EquipmentStatType statType, int value)
    {
        switch (statType)
        {
            case EquipmentStatType.HP: hp += value; break;
            case EquipmentStatType.ATK: atk += value; break;
            case EquipmentStatType.DEF: def += value; break;
            case EquipmentStatType.SPD: spd += value; break;
            case EquipmentStatType.CRIT_RATE: critRate += value; break;
            case EquipmentStatType.CRIT_DAMAGE: critDamage += value; break;
            case EquipmentStatType.ACCURACY: accuracy += value; break;
            case EquipmentStatType.RESISTANCE: resistance += value; break;
        }
    }

    /// <summary>The value of one stat type (by EquipmentStatType index).</summary>
    public int Get(EquipmentStatType statType)
    {
        switch (statType)
        {
            case EquipmentStatType.HP: return hp;
            case EquipmentStatType.ATK: return atk;
            case EquipmentStatType.DEF: return def;
            case EquipmentStatType.SPD: return spd;
            case EquipmentStatType.CRIT_RATE: return critRate;
            case EquipmentStatType.CRIT_DAMAGE: return critDamage;
            case EquipmentStatType.ACCURACY: return accuracy;
            case EquipmentStatType.RESISTANCE: return resistance;
            default: return 0;
        }
    }

    /// <summary>Adds another stat set field by field.</summary>
    public void AddSet(EquipmentStatSet other)
    {
        hp += other.hp;
        atk += other.atk;
        def += other.def;
        spd += other.spd;
        critRate += other.critRate;
        critDamage += other.critDamage;
        accuracy += other.accuracy;
        resistance += other.resistance;
    }
}

/// <summary>
/// The single source of truth for equipment stat contributions - the
/// equipment counterpart of HeroInstance's own stat pipeline. Primary
/// stats, secondary stats, and set bonuses are calculated ONLY here; the
/// hero pipeline, the UI, and battle code consume the results and never
/// recompute them, so equipment bonuses can never be double-applied.
///
/// Flat stats (HP/ATK/DEF/SPD) are integer additions applied on top of the
/// hero's percentage pipeline (level growth, ascension, awakening,
/// constellation); percent stats (crit and friends) are carried on the
/// hero's final stat sheet (heroes have no innate values there - equipment
/// is the only source, so removing gear fully reverts them).
///
/// Also hosts the role-based equipment scoring used by "EQUIP BEST" and
/// the UI - one configurable weight table, never per-screen heuristics.
/// </summary>
public static class EquipmentStats
{
    // ---------------------------------------------------------------------
    // Primary stat
    // ---------------------------------------------------------------------

    /// <summary>
    /// The primary stat's value at the given equipment level and rarity:
    /// (definition base + per-level growth) x the rarity's base stat
    /// multiplier, clamped to the rarity's level cap. Items store the
    /// result (computed here) so saves stay exact.
    /// </summary>
    public static int CalculateMainStat(EquipmentData template, HeroRarity rarity, int level)
    {
        if (template == null)
        {
            return 0;
        }

        int clampedLevel = Mathf.Clamp(level, 1, EquipmentProgression.MaxLevelFor(rarity));
        float curve = template.mainStatBaseValue + template.mainStatPerLevel * (clampedLevel - 1);
        return Mathf.Max(1, Mathf.RoundToInt(curve * EquipmentProgression.BaseStatMultiplier(rarity)));
    }

    // ---------------------------------------------------------------------
    // Item / loadout contributions
    // ---------------------------------------------------------------------

    /// <summary>
    /// One item's total contribution (primary stat + secondary stats) as a
    /// stat set. A null item contributes nothing.
    /// </summary>
    public static EquipmentStatSet GetItemStats(EquipmentInstance item)
    {
        EquipmentStatSet stats = default;
        if (item == null)
        {
            return stats;
        }

        stats.Add(item.mainStatType, item.mainStatValue);
        if (item.secondaryStats != null)
        {
            foreach (EquipmentSecondaryStat secondary in item.secondaryStats)
            {
                if (secondary != null)
                {
                    stats.Add(secondary.statType, secondary.value);
                }
            }
        }

        return stats;
    }

    /// <summary>
    /// The hero's total equipment contribution: every equipped item's stats
    /// plus all active set bonuses. Deterministic from the hero's loadout -
    /// recalculating never compounds values.
    /// </summary>
    public static EquipmentStatSet GetLoadoutStats(HeroInstance hero)
    {
        EquipmentStatSet stats = default;
        if (hero == null)
        {
            return stats;
        }

        for (int slot = 0; slot <= (int)EquipmentSlot.Accessory; slot++)
        {
            EquipmentInstance item = hero.GetEquippedItem((EquipmentSlot)slot);
            if (item == null)
            {
                continue;
            }

            stats.AddSet(GetItemStats(item));
        }

        AddSetBonuses(hero, ref stats);
        return stats;
    }

    /// <summary>
    /// Adds the active set bonuses for the hero's current loadout: for each
    /// set, every bonus whose piece-count requirement is met contributes its
    /// stat. Calculated separately from individual item stats, exactly
    /// once per recalculation.
    /// </summary>
    private static void AddSetBonuses(HeroInstance hero, ref EquipmentStatSet stats)
    {
        CountEquippedSets(hero, out List<string> setIds, out Dictionary<string, int> pieceCounts);
        foreach (string setId in setIds)
        {
            EquipmentSetDefinition set = EquipmentProgression.FindSet(setId);
            if (set == null || set.Bonuses == null)
            {
                continue;
            }

            int pieces = pieceCounts[setId];
            foreach (EquipmentSetBonus bonus in set.Bonuses)
            {
                if (bonus != null && pieces >= bonus.PiecesRequired)
                {
                    stats.Add(bonus.StatType, bonus.Value);
                }
            }
        }
    }

    /// <summary>
    /// Human-readable summary of the hero's active set bonuses for the UI,
    /// e.g. "Vanguard 3 pcs: +10 DEF" - or an empty string with no active
    /// set. Built from the same data the calculation uses.
    /// </summary>
    public static string DescribeSetBonuses(HeroInstance hero)
    {
        if (hero == null)
        {
            return string.Empty;
        }

        CountEquippedSets(hero, out List<string> setIds, out Dictionary<string, int> pieceCounts);
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (string setId in setIds)
        {
            EquipmentSetDefinition set = EquipmentProgression.FindSet(setId);
            if (set == null || set.Bonuses == null || set.Bonuses.Length == 0)
            {
                continue;
            }

            int pieces = pieceCounts[setId];
            bool anyActive = false;
            System.Text.StringBuilder bonuses = new System.Text.StringBuilder();
            foreach (EquipmentSetBonus bonus in set.Bonuses)
            {
                if (bonus == null || pieces < bonus.PiecesRequired)
                {
                    continue;
                }

                anyActive = true;
                if (bonuses.Length > 0)
                {
                    bonuses.Append(", ");
                }

                bonuses.Append("+").Append(bonus.Value);
                if (EquipmentProgression.IsPercentStat(bonus.StatType))
                {
                    bonuses.Append("%");
                }

                bonuses.Append(" ").Append(bonus.StatType);
            }

            if (anyActive)
            {
                if (text.Length > 0)
                {
                    text.Append("   ");
                }

                text.Append(set.SetName).Append(" ").Append(pieces).Append(" pcs: ").Append(bonuses);
            }
        }

        return text.ToString();
    }

    /// <summary>Counts equipped pieces per set id (order of first equipping; sets without pieces are skipped).</summary>
    private static void CountEquippedSets(HeroInstance hero, out List<string> setIds, out Dictionary<string, int> pieceCounts)
    {
        setIds = new List<string>();
        pieceCounts = new Dictionary<string, int>();

        for (int slot = 0; slot <= (int)EquipmentSlot.Accessory; slot++)
        {
            EquipmentInstance item = hero.GetEquippedItem((EquipmentSlot)slot);
            if (item == null || string.IsNullOrEmpty(item.setId))
            {
                continue;
            }

            if (!pieceCounts.ContainsKey(item.setId))
            {
                pieceCounts[item.setId] = 0;
                setIds.Add(item.setId);
            }

            pieceCounts[item.setId]++;
        }
    }

    // ---------------------------------------------------------------------
    // Role-based equipment scoring ("EQUIP BEST" and UI comparisons).
    // One configurable table - never per-screen heuristics or rarity
    // comparisons: a higher-scored item is "better" for that role.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Stat weights per hero role, index-aligned with
    /// <see cref="EquipmentStatType"/>. Score = sum of (stat value x
    /// weight) over the item's full contribution; flats and percents are
    /// both scored by their raw value. Tune the numbers here - EQUIP BEST
    /// and any future auto-loadout consume this table only.
    /// </summary>
    private static readonly Dictionary<HeroRole, float[]> roleStatWeights = new Dictionary<HeroRole, float[]>
    {
        //                       HP    ATK   DEF   SPD  CRIT_RATE CRIT_DMG ACC  RES
        { HeroRole.Attack,  new[] { 0.5f, 3f,   0.5f, 1.5f, 2.5f, 2f,   0.25f, 0.25f } },
        { HeroRole.Defense, new[] { 3f,   0.5f, 2.5f, 1f,   0.25f, 0.25f, 0.25f, 2f } },
        { HeroRole.Support, new[] { 2f,   0.5f, 1.5f, 3f,   0.25f, 0.25f, 2f,   1f } },
    };

    /// <summary>
    /// Fallback weights for unknown/future roles (or items compared
    /// without a hero): a balanced generic score.
    /// </summary>
    private static readonly float[] genericStatWeights = { 1f, 1.5f, 1f, 1f, 1f, 0.75f, 0.5f, 0.5f };

    /// <summary>
    /// The item's score for the given hero role: the weighted sum of its
    /// full stat contribution (primary + secondaries). Used by EQUIP BEST
    /// and UI comparisons - "better" means a higher score for that role,
    /// never simply a higher rarity.
    /// </summary>
    public static float CalculateItemScore(EquipmentInstance item, HeroRole role)
    {
        if (item == null)
        {
            return 0f;
        }

        float[] weights;
        if (!roleStatWeights.TryGetValue(role, out weights))
        {
            weights = genericStatWeights;
        }

        EquipmentStatSet stats = GetItemStats(item);
        float score = 0f;
        for (int statType = 0; statType <= (int)EquipmentStatType.RESISTANCE; statType++)
        {
            score += stats.Get((EquipmentStatType)statType) * weights[Mathf.Clamp(statType, 0, weights.Length - 1)];
        }

        return score;
    }

    /// <summary>The item's score without a hero context (generic weights).</summary>
    public static float CalculateItemScore(EquipmentInstance item)
    {
        if (item == null)
        {
            return 0f;
        }

        EquipmentStatSet stats = GetItemStats(item);
        float score = 0f;
        for (int statType = 0; statType <= (int)EquipmentStatType.RESISTANCE; statType++)
        {
            score += stats.Get((EquipmentStatType)statType) * genericStatWeights[Mathf.Clamp(statType, 0, genericStatWeights.Length - 1)];
        }

        return score;
    }
}
