using System.Collections.Generic;
using UnityEngine;

/// <summary>One rolled secondary stat on an equipment instance (stat type + value).</summary>
public class EquipmentSecondaryStat
{
    /// <summary>The stat the secondary boosts.</summary>
    public EquipmentStatType statType;

    /// <summary>The value added to that stat (flat or percent, matching the stat type).</summary>
    public int value;
}

/// <summary>
/// One player-owned equipment item - the equipment counterpart of
/// <see cref="HeroInstance"/>. Two items generated from the same
/// <see cref="EquipmentData"/> definition are fully independent instances
/// ("Ironfang #001" vs "Ironfang #002") with their own level, rolled
/// secondary stats, lock state, and owner. Everything player-owned lives
/// here; the definition stays immutable and shared.
///
/// Identity is the stable <see cref="instanceId"/> string (never a Unity
/// object reference and never just the definition id), so saves, future
/// cloud saves, and inventory management stay robust. All state is
/// captured/restored verbatim by the save system - rolled secondary stats
/// are NEVER regenerated during loading. Stat math lives in
/// <see cref="EquipmentStats"/>; configuration in
/// <see cref="EquipmentProgression"/>.
/// </summary>
public class EquipmentInstance
{
    /// <summary>Stable unique instance id (e.g. "eq-7"), assigned by the inventory.</summary>
    public string instanceId;

    /// <summary>The item's definition (null only when the saved definition no longer exists; the item stays usable, but cannot upgrade).</summary>
    public EquipmentData data;

    /// <summary>The item's rarity, captured at generation (drives max level, stat multiplier, secondary count, costs).</summary>
    public HeroRarity rarity;

    /// <summary>The item's slot, captured at generation from the definition.</summary>
    public EquipmentSlot slot;

    /// <summary>The item's level, 1..<see cref="MaxLevel"/> (the rarity's cap).</summary>
    public int level = 1;

    /// <summary>The primary stat type (captured at generation).</summary>
    public EquipmentStatType mainStatType;

    /// <summary>The primary stat's base value at level 1 (captured at generation, rarity multiplier included).</summary>
    public int mainStatBaseValue;

    /// <summary>The primary stat's current value (recalculated by <see cref="EquipmentStats"/> on upgrade; stored so saves stay exact).</summary>
    public int mainStatValue;

    /// <summary>The item's rolled secondary stats (unique to this instance; persisted verbatim).</summary>
    public List<EquipmentSecondaryStat> secondaryStats = new List<EquipmentSecondaryStat>();

    /// <summary>The item's set id (empty = no set).</summary>
    public string setId;

    /// <summary>
    /// Lock flag for inventory management: a locked item must never be
    /// accidentally sold or destroyed (see
    /// <see cref="EquipmentInventory.RemoveEquipment"/>). Equipping a locked
    /// item is allowed.
    /// </summary>
    public bool locked;

    /// <summary>
    /// The stable HeroId of the hero wearing this item, or empty when it
    /// sits in the inventory pool. Written ONLY by
    /// <see cref="EquipmentInventory"/> through its equip/unequip path -
    /// the single source of truth for who owns the loan.
    /// </summary>
    public string equippedHeroId;

    /// <summary>Creates a brand-new item from a definition at level 1, rolling its secondary stats deterministically from the instance id.</summary>
    /// <param name="template">The definition to generate from (its own rarity is used).</param>
    /// <param name="assignedInstanceId">The unique instance id assigned by the inventory.</param>
    public EquipmentInstance(EquipmentData template, string assignedInstanceId)
        : this(template, assignedInstanceId, template != null ? template.rarity : HeroRarity.Common)
    {
    }

    /// <summary>
    /// Creates a brand-new item from a definition at a specific rarity
    /// (the factory's path - the generated rarity may differ from the
    /// definition's typical one; base stats scale through the rarity
    /// multiplier). Rolls the rarity's secondary-stat count
    /// deterministically from the instance id.
    /// </summary>
    public EquipmentInstance(EquipmentData template, string assignedInstanceId, HeroRarity generatedRarity)
    {
        instanceId = string.IsNullOrEmpty(assignedInstanceId) ? "eq" : assignedInstanceId;
        data = template;
        rarity = generatedRarity;
        slot = template != null ? template.slot : EquipmentSlot.Weapon;
        mainStatType = template != null ? template.mainStatType : EquipmentStatType.ATK;
        mainStatBaseValue = EquipmentStats.CalculateMainStat(template, rarity, 1);
        mainStatValue = mainStatBaseValue;
        setId = template != null ? template.setId : string.Empty;
        equippedHeroId = string.Empty;
        level = 1;

        RollSecondaryStats();
    }

    /// <summary>
    /// Restores an item's exact saved state (save/load only): every value
    /// comes from the save - nothing is re-rolled or recomputed from the
    /// definition. The definition may be null (removed asset): the item
    /// remains fully usable except for upgrading. The level is clamped to
    /// the rarity's CURRENT cap (save migration from older level-cap
    /// rules); a clamped item with a known definition has its primary stat
    /// recomputed to match.
    /// </summary>
    public EquipmentInstance(
        string assignedInstanceId,
        EquipmentData template,
        HeroRarity savedRarity,
        EquipmentSlot savedSlot,
        EquipmentStatType savedMainStatType,
        int savedMainStatBaseValue,
        int savedMainStatValue,
        List<EquipmentSecondaryStat> savedSecondaryStats,
        string savedSetId,
        bool savedLocked,
        string savedEquippedHeroId,
        int savedLevel)
    {
        instanceId = assignedInstanceId;
        data = template;
        rarity = savedRarity;
        slot = savedSlot;
        mainStatType = savedMainStatType;
        mainStatBaseValue = savedMainStatBaseValue;
        mainStatValue = savedMainStatValue;
        secondaryStats = savedSecondaryStats != null ? savedSecondaryStats : new List<EquipmentSecondaryStat>();
        setId = savedSetId ?? string.Empty;
        locked = savedLocked;
        equippedHeroId = savedEquippedHeroId ?? string.Empty;

        int clampedLevel = Mathf.Clamp(savedLevel, 1, MaxLevel);
        level = clampedLevel;

        // Migration safety: a missing base value marks a pre-v4 save (the
        // primary-stat formula changed with per-rarity multipliers), and a
        // level clamped down by a cap change invalidates the stored value -
        // in both cases recompute from the definition so the item matches
        // the current rules. Items without a definition keep their stored
        // values verbatim.
        bool baseWasMissing = mainStatBaseValue <= 0;
        if (baseWasMissing && data != null)
        {
            mainStatBaseValue = EquipmentStats.CalculateMainStat(data, rarity, 1);
        }

        if ((baseWasMissing || clampedLevel != savedLevel || mainStatValue <= 0) && data != null)
        {
            mainStatValue = EquipmentStats.CalculateMainStat(data, rarity, level);
        }
    }

    /// <summary>The maximum level for this item (its rarity's cap, data-driven).</summary>
    public int MaxLevel => EquipmentProgression.MaxLevelFor(rarity);

    /// <summary>Whether the item has reached its level cap.</summary>
    public bool IsMaxLevel => level >= MaxLevel;

    /// <summary>The gold cost of the next upgrade (the single configured formula).</summary>
    public int UpgradeGoldCost => EquipmentProgression.UpgradeGoldCost(level, rarity);

    /// <summary>The item's sell value (single formula in <see cref="EquipmentProgression"/>).</summary>
    public int SellValue => EquipmentProgression.SellValueFor(rarity, level, slot);

    /// <summary>The item's full stat contribution (primary + secondaries), calculated by <see cref="EquipmentStats"/>.</summary>
    public EquipmentStatSet StatContribution => EquipmentStats.GetItemStats(this);

    /// <summary>
    /// Whether the item can upgrade right now: definition known, below the
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
    /// raises the level, recalculates the primary stat through the central
    /// calculator, and applies the secondary-stat boost at configured
    /// levels (one existing secondary deterministically gains extra value;
    /// items without secondaries skip it). Refuses (returns false, changes
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
        mainStatValue = EquipmentStats.CalculateMainStat(data, rarity, level);
        if (EquipmentProgression.IsSecondaryBoostLevel(level))
        {
            ApplySecondaryBoost();
        }

        return true;
    }

    /// <summary>
    /// Rolls the item's secondary stats: exactly the rarity's configured
    /// count, each a distinct type (never the primary stat's type) with a
    /// value in the stat's configured range. Seeded by the instance id, so
    /// the same id always rolls the same secondary stats.
    /// </summary>
    private void RollSecondaryStats()
    {
        int count = Mathf.Clamp(
            EquipmentProgression.SecondaryStatCountByRarity[Mathf.Clamp((int)rarity, 0, EquipmentProgression.SecondaryStatCountByRarity.Length - 1)],
            0, EquipmentProgression.MaxSecondaryStats);

        List<EquipmentStatType> usedTypes = new List<EquipmentStatType> { mainStatType };
        for (int i = 0; i < count; i++)
        {
            EquipmentStatType type = EquipmentProgression.RollSecondaryStatType(instanceId + ":type" + i, usedTypes);
            usedTypes.Add(type);
            secondaryStats.Add(new EquipmentSecondaryStat
            {
                statType = type,
                value = EquipmentProgression.RollSecondaryStatValue(instanceId + ":value" + i, type),
            });
        }
    }

    /// <summary>
    /// The secondary-stat boost at milestone levels: one existing secondary
    /// (deterministically chosen) gains a value equal to a fresh roll in
    /// its stat's range. Deterministic - seeded by instance id + level.
    /// </summary>
    private void ApplySecondaryBoost()
    {
        if (secondaryStats.Count == 0)
        {
            return;
        }

        int index = EquipmentProgression.DeterministicRoll(instanceId + ":boost" + level, secondaryStats.Count);
        EquipmentSecondaryStat secondary = secondaryStats[index];
        if (secondary != null)
        {
            secondary.value += EquipmentProgression.RollSecondaryStatValue(instanceId + ":boostval" + level, secondary.statType);
        }
    }
}
