using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single source of truth for equipment stat contributions - the
/// equipment counterpart of HeroInstance's own stat pipeline. Main stats,
/// substats, and set bonuses are calculated ONLY here; the hero pipeline,
/// the UI, and battle code consume the results and never recompute them,
/// so equipment bonuses can never be double-applied.
///
/// All equipment contributions are FLAT integer additions applied on top of
/// the hero's percentage pipeline (level growth, ascension, awakening,
/// constellation), keeping the systems cleanly separated and fully
/// reversible on unequip.
/// </summary>
public static class EquipmentStats
{
    /// <summary>
    /// The main stat's value at the given equipment level:
    /// base + perLevel x (level - 1), clamped to the level cap. Items store
    /// the result (computed here) so saves stay exact.
    /// </summary>
    public static int CalculateMainStat(EquipmentData template, int level)
    {
        if (template == null)
        {
            return 0;
        }

        int clampedLevel = Mathf.Clamp(level, 1, EquipmentProgression.MaxEquipmentLevel);
        return Mathf.Max(1, template.mainStatBaseValue
            + Mathf.RoundToInt(template.mainStatPerLevel * (clampedLevel - 1)));
    }

    /// <summary>
    /// One item's total contribution (main stat + substats) as flat stats.
    /// A null item contributes nothing.
    /// </summary>
    public static void GetItemStats(EquipmentInstance item, out int hp, out int atk, out int def, out int spd)
    {
        hp = 0;
        atk = 0;
        def = 0;
        spd = 0;

        if (item == null)
        {
            return;
        }

        AddStat(item.mainStatType, item.mainStatValue, ref hp, ref atk, ref def, ref spd);
        if (item.substats != null)
        {
            foreach (EquipmentSubstat substat in item.substats)
            {
                if (substat != null)
                {
                    AddStat(substat.statType, substat.value, ref hp, ref atk, ref def, ref spd);
                }
            }
        }
    }

    /// <summary>
    /// The hero's total equipment contribution: every equipped item's stats
    /// plus all active set bonuses. Deterministic from the hero's loadout -
    /// recalculating never compounds values.
    /// </summary>
    public static void GetLoadoutStats(HeroInstance hero, out int hp, out int atk, out int def, out int spd)
    {
        hp = 0;
        atk = 0;
        def = 0;
        spd = 0;

        if (hero == null)
        {
            return;
        }

        for (int slot = 0; slot <= (int)EquipmentSlot.Accessory; slot++)
        {
            EquipmentInstance item = hero.GetEquippedItem((EquipmentSlot)slot);
            if (item == null)
            {
                continue;
            }

            GetItemStats(item, out int itemHp, out int itemAtk, out int itemDef, out int itemSpd);
            hp += itemHp;
            atk += itemAtk;
            def += itemDef;
            spd += itemSpd;
        }

        AddSetBonuses(hero, ref hp, ref atk, ref def, ref spd);
    }

    /// <summary>
    /// Adds the active set bonuses for the hero's current loadout: for each
    /// set, every bonus whose piece-count requirement is met contributes its
    /// flat stat. Calculated separately from individual item stats, exactly
    /// once per recalculation.
    /// </summary>
    private static void AddSetBonuses(HeroInstance hero, ref int hp, ref int atk, ref int def, ref int spd)
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
                    AddStat(bonus.StatType, bonus.Value, ref hp, ref atk, ref def, ref spd);
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

                bonuses.Append("+").Append(bonus.Value).Append(" ").Append(bonus.StatType);
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

    /// <summary>Adds a flat stat value to the matching accumulator.</summary>
    private static void AddStat(EquipmentStatType statType, int value, ref int hp, ref int atk, ref int def, ref int spd)
    {
        switch (statType)
        {
            case EquipmentStatType.HP: hp += value; break;
            case EquipmentStatType.ATK: atk += value; break;
            case EquipmentStatType.DEF: def += value; break;
            case EquipmentStatType.SPD: spd += value; break;
        }
    }
}
