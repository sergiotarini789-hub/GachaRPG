using System;
using UnityEngine;

/// <summary>
/// The player's placeholder progression resources: gold, ascension
/// materials, and skill materials. This is deliberately the minimum
/// foundation the progression systems need before a real economy exists -
/// a future economy system replaces or extends this class, everything else
/// keeps consuming through <see cref="TryConsume"/>.
///
/// Owned by the <see cref="BattleTestRunner"/> (the composition root),
/// persisted with the roster by <see cref="PlayerSaveSystem"/>, and passed
/// into the hero progression APIs (Ascend, UpgradeSkill, the LEVEL UP
/// exchange) which check and consume from it. Never a negative balance.
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

    public PlayerWallet(int gold, int ascensionMaterials, int skillMaterials)
    {
        this.gold = Mathf.Max(0, gold);
        this.ascensionMaterials = Mathf.Max(0, ascensionMaterials);
        this.skillMaterials = Mathf.Max(0, skillMaterials);
    }

    /// <summary>Whether the wallet covers all three costs.</summary>
    public bool CanAfford(int goldCost, int ascensionMaterialCost, int skillMaterialCost)
    {
        return gold >= Mathf.Max(0, goldCost)
            && ascensionMaterials >= Mathf.Max(0, ascensionMaterialCost)
            && skillMaterials >= Mathf.Max(0, skillMaterialCost);
    }

    /// <summary>Consumes the given amounts when affordable; nothing changes otherwise.</summary>
    public bool TryConsume(int goldCost, int ascensionMaterialCost, int skillMaterialCost)
    {
        if (!CanAfford(goldCost, ascensionMaterialCost, skillMaterialCost))
        {
            return false;
        }

        gold -= goldCost;
        ascensionMaterials -= ascensionMaterialCost;
        skillMaterials -= skillMaterialCost;
        return true;
    }

    /// <summary>Adds gold (development tools and future rewards).</summary>
    public void AddGold(int amount)
    {
        gold = Mathf.Max(0, gold + amount);
    }
}
