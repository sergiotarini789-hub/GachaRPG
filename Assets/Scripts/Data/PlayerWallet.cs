using System;
using UnityEngine;

/// <summary>
/// The player's placeholder progression resources: gold, ascension
/// materials, skill materials, awakening materials, and Hero Tokens (the
/// post-C6 duplicate currency a future shop will consume). This is
/// deliberately the minimum foundation the progression systems need before
/// a real economy exists - a future economy system replaces or extends this
/// class, everything else keeps consuming through <see cref="TryConsume"/>
/// (materials) and <see cref="TryConsumeHeroTokens"/>.
///
/// Owned by the <see cref="BattleTestRunner"/> (the composition root),
/// persisted with the roster by <see cref="PlayerSaveSystem"/>, and passed
/// into the hero progression APIs (Ascend, Awaken, UpgradeSkill, the LEVEL
/// UP exchange, duplicate conversion) which check and consume from it.
/// Never a negative balance.
/// </summary>
[Serializable]
public class PlayerWallet
{
    /// <summary>Generic progression currency (placeholder).</summary>
    public int gold;

    /// <summary>Materials spent on ascension (placeholder).</summary>
    public int ascensionMaterials;

    /// <summary>Materials spent on skill upgrades (placeholder).</summary>
    public int skillMaterials;

    /// <summary>Materials spent on awakening (placeholder; awakening no longer uses duplicates).</summary>
    public int awakeningMaterials;

    /// <summary>
    /// Hero Tokens: earned from extra duplicates of heroes already at
    /// constellation C6. Placeholder currency for a future hero shop; the
    /// shop will read and consume this balance.
    /// </summary>
    public int heroTokens;

    public PlayerWallet(int gold, int ascensionMaterials, int skillMaterials, int awakeningMaterials, int heroTokens)
    {
        this.gold = Mathf.Max(0, gold);
        this.ascensionMaterials = Mathf.Max(0, ascensionMaterials);
        this.skillMaterials = Mathf.Max(0, skillMaterials);
        this.awakeningMaterials = Mathf.Max(0, awakeningMaterials);
        this.heroTokens = Mathf.Max(0, heroTokens);
    }

    /// <summary>Whether the wallet covers all four gold/material costs.</summary>
    public bool CanAfford(int goldCost, int ascensionMaterialCost, int skillMaterialCost, int awakeningMaterialCost)
    {
        return gold >= Mathf.Max(0, goldCost)
            && ascensionMaterials >= Mathf.Max(0, ascensionMaterialCost)
            && skillMaterials >= Mathf.Max(0, skillMaterialCost)
            && awakeningMaterials >= Mathf.Max(0, awakeningMaterialCost);
    }

    /// <summary>Consumes the given amounts when affordable; nothing changes otherwise.</summary>
    public bool TryConsume(int goldCost, int ascensionMaterialCost, int skillMaterialCost, int awakeningMaterialCost)
    {
        if (!CanAfford(goldCost, ascensionMaterialCost, skillMaterialCost, awakeningMaterialCost))
        {
            return false;
        }

        gold -= goldCost;
        ascensionMaterials -= ascensionMaterialCost;
        skillMaterials -= skillMaterialCost;
        awakeningMaterials -= awakeningMaterialCost;
        return true;
    }

    /// <summary>Adds gold (development tools and future rewards).</summary>
    public void AddGold(int amount)
    {
        gold = Mathf.Max(0, gold + amount);
    }

    /// <summary>Adds Hero Tokens (extra duplicates past C6, development tools, future rewards).</summary>
    public void AddHeroTokens(int amount)
    {
        heroTokens = Mathf.Max(0, heroTokens + amount);
    }

    /// <summary>
    /// Consumes Hero Tokens when the balance covers the cost; nothing
    /// changes otherwise. The future hero shop spends through this method -
    /// no other system decrements the balance directly.
    /// </summary>
    public bool TryConsumeHeroTokens(int amount)
    {
        int cost = Mathf.Max(0, amount);
        if (heroTokens < cost)
        {
            return false;
        }

        heroTokens -= cost;
        return true;
    }
}
