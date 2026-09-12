using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized hero-progression configuration: the ascension table, level
/// caps, awakening defaults, soul rules, placeholder resource amounts, and
/// the skill/level-up cost model. Every system (HeroInstance, roster,
/// summon service, Heroes screen, save) reads these values from here -
/// numeric progression values must never be scattered across UI scripts.
///
/// All values are prototype placeholders chosen so the whole progression
/// loop is testable before a real economy exists; tune them here in one
/// place. The ascension level caps deliberately cap at 60, matching the
/// highest authored hero growth curve; a hero's effective maximum level is
/// always min(ascension cap, its own <see cref="HeroGrowth.maxLevel"/>).
/// </summary>
public static class HeroProgression
{
    // ---------------------------------------------------------------------
    // Save format
    // ---------------------------------------------------------------------

    /// <summary>Current save-file version; older or newer files are rejected safely.</summary>
    public const int CurrentSaveVersion = 1;

    // ---------------------------------------------------------------------
    // Ascension (raises the level cap and grants stat bonuses)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Maximum level per ascension rank (index = rank): 20 / 30 / 40 / 50 / 60.
    /// </summary>
    public static readonly int[] AscensionMaxLevels = { 20, 30, 40, 50, 60 };

    /// <summary>Gold cost to ascend TO each rank (index 0 = to rank 1).</summary>
    public static readonly int[] AscensionGoldCosts = { 1000, 2500, 5000, 10000, 20000 };

    /// <summary>Ascension material cost to ascend TO each rank (index 0 = to rank 1).</summary>
    public static readonly int[] AscensionMaterialCosts = { 5, 10, 15, 20, 30 };

    /// <summary>Percent stat bonus applied to every stat per ascension rank.</summary>
    public const float StatBonusPercentPerAscension = 10f;

    /// <summary>The highest ascension rank a hero can reach.</summary>
    public static int MaxAscensionRank => AscensionMaxLevels.Length - 1;

    /// <summary>Maximum level allowed at the given ascension rank (clamped).</summary>
    public static int AscensionMaxLevel(int rank)
    {
        return AscensionMaxLevels[Mathf.Clamp(rank, 0, MaxAscensionRank)];
    }

    /// <summary>Gold cost to ascend from <paramref name="rank"/> to the next rank.</summary>
    public static int AscensionGoldCost(int targetRank)
    {
        return AscensionGoldCosts[Mathf.Clamp(targetRank, 1, MaxAscensionRank) - 1];
    }

    /// <summary>Ascension material cost to ascend from <paramref name="rank"/> to the next rank.</summary>
    public static int AscensionMaterialCost(int targetRank)
    {
        return AscensionMaterialCosts[Mathf.Clamp(targetRank, 1, MaxAscensionRank) - 1];
    }

    // ---------------------------------------------------------------------
    // Awakening (hero-specific ranks bought with Hero Souls)
    // ---------------------------------------------------------------------

    /// <summary>How many souls a duplicate summon converts into.</summary>
    public const int SoulsPerDuplicate = 1;

    /// <summary>
    /// Default soul cost for awakening to a rank when the hero's
    /// <see cref="AwakeningStep"/> does not override it: 10 x target rank
    /// (10 / 20 / 30 / 40 / 50 / 60).
    /// </summary>
    public const int DefaultSoulCostPerRank = 10;

    /// <summary>The default soul cost for awakening to the given (1-based) rank.</summary>
    public static int SoulCostForRank(int targetRank)
    {
        return DefaultSoulCostPerRank * Mathf.Max(1, targetRank);
    }

    // ---------------------------------------------------------------------
    // Placeholder economy (a real economy system is a later milestone)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Gold cost of the Heroes screen's LEVEL UP button, multiplied by the
    /// target level (the button converts gold into exactly the XP needed
    /// for the next level through the real XP API).
    /// </summary>
    public const int LevelUpGoldCostPerLevel = 50;

    /// <summary>Prototype starting gold so ascension and leveling are testable.</summary>
    public const int StartingGold = 10000;

    /// <summary>Prototype starting ascension materials.</summary>
    public const int StartingAscensionMaterials = 50;

    /// <summary>Prototype starting skill materials.</summary>
    public const int StartingSkillMaterials = 50;

    // ---------------------------------------------------------------------
    // Default awakening steps
    // ---------------------------------------------------------------------

    /// <summary>Heroes whose awakening steps have not been authored yet (logged once).</summary>
    private static readonly HashSet<string> heroesUsingDefaultSteps = new HashSet<string>();

    /// <summary>
    /// The shared fallback awakening configuration for heroes without
    /// authored <see cref="HeroData.awakeningSteps"/>: six ranks, each
    /// granting +4% attack, +4% health, +2% defense and +1% speed, with the
    /// default soul costs (10 x rank). A development warning is logged once
    /// per hero so missing authored data stays visible.
    /// </summary>
    public static AwakeningStep[] DefaultAwakeningSteps(HeroData forHero)
    {
        string heroId = forHero != null ? forHero.HeroId : "unknown";
        if (heroesUsingDefaultSteps.Add(heroId))
        {
            Debug.LogWarning("HeroProgression: '" + heroId + "' has no authored awakeningSteps; " +
                "using the shared default steps (+4% ATK, +4% HP, +2% DEF, +1% SPD per rank). " +
                "Assign awakening steps on the HeroData asset to customize.");
        }

        AwakeningStep[] steps = new AwakeningStep[6];
        string[] numerals = { "I", "II", "III", "IV", "V", "VI" };
        for (int i = 0; i < steps.Length; i++)
        {
            steps[i] = new AwakeningStep
            {
                displayName = "Awakening " + numerals[i],
                description = "+4% ATK, +4% HP, +2% DEF, +1% SPD",
                requiredSouls = 0, // 0 = SoulCostForRank (10 x rank)
                healthBonusPercent = 4f,
                attackBonusPercent = 4f,
                defenseBonusPercent = 2f,
                speedBonusPercent = 1f,
                plannedEffect = string.Empty,
            };
        }

        return steps;
    }
}
