using System.Collections.Generic;
using UnityEngine;

/// <summary>One rolled substat on an equipment instance (flat stat + value).</summary>
public class EquipmentSubstat
{
    /// <summary>The stat the substat boosts.</summary>
    public EquipmentStatType statType;

    /// <summary>The flat value added to that stat.</summary>
    public int value;
}

/// <summary>
/// One player-owned equipment item - the equipment counterpart of
/// <see cref="HeroInstance"/>. Two items from the same
/// <see cref="EquipmentData"/> template are fully independent instances
/// with their own level, rolled substats, and lock state. Everything
/// player-owned lives here; the template stays immutable and shared.
///
/// Identity is the stable <see cref="InstanceId"/> string (never a Unity
/// object reference), so saves, future cloud saves, and inventory
/// management stay robust. All state is captured/restored verbatim by the
/// save system - rolled substats are NEVER regenerated during loading.
/// Stat math lives in <see cref="EquipmentStats"/>; configuration in
/// <see cref="EquipmentProgression"/>.
/// </summary>
public class EquipmentInstance
{
    /// <summary>Stable unique instance id (e.g. "eq-7"), assigned by the inventory.</summary>
    public string instanceId;

    /// <summary>The item's template (null only when the saved template no longer exists; the item stays usable, but cannot upgrade).</summary>
    public EquipmentData data;

    /// <summary>The item's rarity, captured at creation (drives substat quality and sell value).</summary>
    public HeroRarity rarity;

    /// <summary>The item's slot, captured at creation from the template.</summary>
    public EquipmentSlot slot;

    /// <summary>The item's level, 1..<see cref="EquipmentProgression.MaxEquipmentLevel"/>.</summary>
    public int level = 1;

    /// <summary>The item's main stat type (captured at creation).</summary>
    public EquipmentStatType mainStatType;

    /// <summary>The main stat's current value (recalculated by <see cref="EquipmentStats"/> on upgrade; stored so saves stay exact).</summary>
    public int mainStatValue;

    /// <summary>The item's rolled substats (unique to this instance; persisted verbatim).</summary>
    public List<EquipmentSubstat> substats = new List<EquipmentSubstat>();

    /// <summary>The item's set id (empty = no set).</summary>
    public string setId;

    /// <summary>
    /// Lock flag for future inventory management: a locked item must never
    /// be accidentally sold or destroyed (see
    /// <see cref="EquipmentInventory.RemoveEquipment"/>). Equipping a locked
    /// item is allowed.
    /// </summary>
    public bool locked;

    /// <summary>Creates a brand-new item from a template at level 1, rolling its initial substats deterministically from the instance id.</summary>
    public EquipmentInstance(EquipmentData template, string assignedInstanceId)
    {
        instanceId = string.IsNullOrEmpty(assignedInstanceId) ? "eq" : assignedInstanceId;
        data = template;
        rarity = template != null ? template.rarity : HeroRarity.Common;
        slot = template != null ? template.slot : EquipmentSlot.Weapon;
        mainStatType = template != null ? template.mainStatType : EquipmentStatType.ATK;
        mainStatValue = EquipmentStats.CalculateMainStat(template, 1);
        setId = template != null ? template.setId : string.Empty;

        RollInitialSubstats();
    }

    /// <summary>
    /// Restores an item's exact saved state (save/load only): every value
    /// comes from the save - nothing is re-rolled or recomputed from the
    /// template. The template may be null (removed asset): the item remains
    /// fully usable except for upgrading.
    /// </summary>
    public EquipmentInstance(
        string assignedInstanceId,
        EquipmentData template,
        HeroRarity savedRarity,
        EquipmentSlot savedSlot,
        EquipmentStatType savedMainStatType,
        int savedMainStatValue,
        List<EquipmentSubstat> savedSubstats,
        string savedSetId,
        bool savedLocked,
        int savedLevel)
    {
        instanceId = assignedInstanceId;
        data = template;
        rarity = savedRarity;
        slot = savedSlot;
        mainStatType = savedMainStatType;
        mainStatValue = savedMainStatValue;
        substats = savedSubstats != null ? savedSubstats : new List<EquipmentSubstat>();
        setId = savedSetId ?? string.Empty;
        locked = savedLocked;
        level = Mathf.Clamp(savedLevel, 1, EquipmentProgression.MaxEquipmentLevel);
    }

    /// <summary>Whether the item has reached the configured equipment level cap.</summary>
    public bool IsMaxLevel => level >= EquipmentProgression.MaxEquipmentLevel;

    /// <summary>The gold cost of the next upgrade (0-style at the cap; still reported for display).</summary>
    public int UpgradeGoldCost => EquipmentProgression.UpgradeGoldCost(level);

    /// <summary>The item's sell value (single formula in <see cref="EquipmentProgression"/>).</summary>
    public int SellValue => EquipmentProgression.SellValueFor(rarity, level);

    /// <summary>
    /// Whether the item can upgrade right now: template known, below the
    /// level cap, and the wallet covers the gold cost. The reason describes
    /// what blocks it.
    /// </summary>
    public bool CanUpgrade(PlayerWallet wallet, out string reason)
    {
        if (data == null)
        {
            reason = "template missing";
            return false;
        }

        if (IsMaxLevel)
        {
            reason = "max level";
            return false;
        }

        int cost = UpgradeGoldCost;
        if (wallet == null || !wallet.CanAfford(cost, 0, 0, 0))
        {
            reason = "costs " + cost + " gold";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Upgrades the item by exactly one level: consumes the gold cost,
    /// raises the level, recalculates the main stat through the central
    /// calculator, and applies the substat milestone behavior at configured
    /// levels (a new substat below the cap, otherwise a boost to an existing
    /// one - deterministically rolled). Refuses (returns false, changes
    /// nothing - including the wallet) when <see cref="CanUpgrade"/> fails.
    /// </summary>
    public bool Upgrade(PlayerWallet wallet)
    {
        if (!CanUpgrade(wallet, out _))
        {
            return false;
        }

        wallet.TryConsume(UpgradeGoldCost, 0, 0, 0);
        level++;
        mainStatValue = EquipmentStats.CalculateMainStat(data, level);
        if (EquipmentProgression.IsSubstatMilestone(level))
        {
            ApplySubstatMilestone();
        }

        return true;
    }

    /// <summary>
    /// Rolls the item's initial substats: a deterministic count within the
    /// rarity's range, then a distinct type and a rarity-scaled value for
    /// each. Seeded by the instance id, so the same id always rolls the
    /// same substats.
    /// </summary>
    private void RollInitialSubstats()
    {
        int rarityIndex = Mathf.Clamp((int)rarity, 0, EquipmentProgression.SubstatCountMaxByRarity.Length - 1);
        int min = Mathf.Clamp(EquipmentProgression.SubstatCountMinByRarity[rarityIndex], 0, EquipmentProgression.MaxSubstats);
        int max = Mathf.Clamp(EquipmentProgression.SubstatCountMaxByRarity[rarityIndex], min, EquipmentProgression.MaxSubstats);
        int count = min + EquipmentProgression.DeterministicRoll(instanceId + ":count", max - min + 1);

        List<EquipmentStatType> usedTypes = new List<EquipmentStatType>();
        for (int i = 0; i < count; i++)
        {
            EquipmentStatType type = EquipmentProgression.RollSubstatType(instanceId + ":type" + i, usedTypes);
            usedTypes.Add(type);
            substats.Add(new EquipmentSubstat
            {
                statType = type,
                value = EquipmentProgression.RollSubstatValue(instanceId + ":value" + i, type, rarity),
            });
        }
    }

    /// <summary>
    /// The milestone substat behavior: below the substat cap the item gains
    /// a new substat; at the cap an existing one gains a value. Both rolls
    /// are deterministic (seeded by instance id + milestone level).
    /// </summary>
    private void ApplySubstatMilestone()
    {
        if (substats.Count < EquipmentProgression.MaxSubstats)
        {
            List<EquipmentStatType> usedTypes = new List<EquipmentStatType>();
            foreach (EquipmentSubstat substat in substats)
            {
                if (substat != null)
                {
                    usedTypes.Add(substat.statType);
                }
            }

            EquipmentStatType type = EquipmentProgression.RollSubstatType(instanceId + ":mtype" + level, usedTypes);
            substats.Add(new EquipmentSubstat
            {
                statType = type,
                value = EquipmentProgression.RollSubstatValue(instanceId + ":mval" + level, type, rarity),
            });
        }
        else
        {
            int index = EquipmentProgression.DeterministicRoll(instanceId + ":mboost" + level, substats.Count);
            EquipmentSubstat substat = substats[index];
            substat.value += EquipmentProgression.RollSubstatValue(instanceId + ":mboostval" + level, substat.statType, rarity);
        }
    }
}
