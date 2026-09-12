using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized hero-progression configuration: the ascension table, level
/// caps, constellation rules (C0-C6), awakening material costs, placeholder
/// resource amounts, and the skill/level-up cost model. Every system
/// (HeroInstance, roster, summon service, Heroes screen, save) reads these
/// values from here - numeric progression values must never be scattered
/// across UI scripts.
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

    /// <summary>
    /// Current save-file version. Version 2 replaced the Hero Souls counter
    /// with the C0-C6 constellation and added awakening materials + Hero
    /// Tokens; version-1 files are migrated on load (see
    /// <see cref="PlayerSaveSystem"/>), future versions are rejected safely.
    /// </summary>
    public const int CurrentSaveVersion = 2;

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
    // Constellation (duplicate-driven ranks C0-C6)
    // ---------------------------------------------------------------------

    /// <summary>
    /// The hard constellation cap: ranks run C0 (first copy) to C6 (seventh
    /// copy). A hero can never exceed C6; duplicates past C6 convert into
    /// Hero Tokens instead.
    /// </summary>
    public const int MaxConstellationRank = 6;

    /// <summary>
    /// How many Hero Tokens one extra duplicate of a max-constellation (C6)
    /// hero converts into. The single configurable source for the reward:
    /// the roster's duplicate rule and every UI read it from here, never a
    /// second hardcoded copy.
    /// </summary>
    public const int MaxConstellationDuplicateTokenReward = 10;

    /// <summary>
    /// Display label of a constellation rank ("C0" .. "C6"), clamped to the
    /// valid range - the single place the label format lives, so the UI
    /// never formats it itself.
    /// </summary>
    public static string ConstellationLabel(int rank)
    {
        return "C" + Mathf.Clamp(rank, 0, MaxConstellationRank);
    }

    // ---------------------------------------------------------------------
    // Awakening (hero-specific ranks bought with Awakening Materials)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Default Awakening Material cost for awakening to a rank when the
    /// hero's <see cref="AwakeningStep"/> does not override it: 10 x target
    /// rank (10 / 20 / 30 / 40 / 50 / 60). Awakening never depends on
    /// duplicate copies - that is the constellation system.
    /// </summary>
    public const int DefaultAwakeningMaterialCostPerRank = 10;

    /// <summary>The default Awakening Material cost for awakening to the given (1-based) rank.</summary>
    public static int AwakeningMaterialCostForRank(int targetRank)
    {
        return DefaultAwakeningMaterialCostPerRank * Mathf.Max(1, targetRank);
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

    /// <summary>Prototype starting Awakening Materials (awakening is material-driven now).</summary>
    public const int StartingAwakeningMaterials = 50;

    /// <summary>Prototype starting Hero Tokens (earned from extra duplicates past C6).</summary>
    public const int StartingHeroTokens = 0;

    // ---------------------------------------------------------------------
    // Default awakening steps
    // ---------------------------------------------------------------------

    /// <summary>Heroes whose awakening steps have not been authored yet (logged once).</summary>
    private static readonly HashSet<string> heroesUsingDefaultSteps = new HashSet<string>();

    /// <summary>
    /// The shared fallback awakening configuration for heroes without
    /// authored <see cref="HeroData.awakeningSteps"/>: six ranks, each
    /// granting +4% attack, +4% health, +2% defense and +1% speed, with the
    /// default material costs (10 x rank). A development warning is logged
    /// once per hero so missing authored data stays visible.
    /// </summary>
    public static AwakeningStep[] DefaultAwakeningSteps(HeroData forHero)
    {
        string heroId = forHero != null ? forHero.HeroId : "unknown";
        if (heroesUsingDefaultSteps.Add(heroId))
        {
            // Info, not a warning: falling back to the shared defaults is
            // expected, working behavior during normal gameplay (every hero
            // without authored steps uses them); real errors still log as
            // warnings elsewhere (save/load, unresolved heroes, etc.).
            Debug.Log("HeroProgression: '" + heroId + "' has no authored awakeningSteps; " +
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
                requiredAwakeningMaterials = 0, // 0 = AwakeningMaterialCostForRank (10 x rank)
                healthBonusPercent = 4f,
                attackBonusPercent = 4f,
                defenseBonusPercent = 2f,
                speedBonusPercent = 1f,
                plannedEffect = string.Empty,
            };
        }

        return steps;
    }

    // ---------------------------------------------------------------------
    // Default constellation steps
    // ---------------------------------------------------------------------

    /// <summary>Heroes whose constellation steps have not been authored yet (logged once).</summary>
    private static readonly HashSet<string> heroesUsingDefaultConstellationSteps = new HashSet<string>();

    /// <summary>
    /// The shared fallback constellation configuration for heroes without
    /// authored <see cref="HeroData.constellationSteps"/>: the standard
    /// generic stat progression - C1 +5% ATK, C2 +5% HP, C3 +5% DEF,
    /// C4 +5% SPD, C5 +10% ATK, C6 +10% HP and +10% ATK. These bonuses are
    /// REAL: they are summed per reached rank and applied by
    /// <see cref="HeroInstance.CalculateCurrentStats"/>. A development
    /// warning is logged once per hero so missing authored data stays
    /// visible; author constellation steps on the HeroData asset to give a
    /// hero unique effects (including the future skill-modifier hook).
    /// </summary>
    public static ConstellationStep[] DefaultConstellationSteps(HeroData forHero)
    {
        string heroId = forHero != null ? forHero.HeroId : "unknown";
        if (heroesUsingDefaultConstellationSteps.Add(heroId))
        {
            // Info, not a warning: the shared default stat progression is the
            // intended fallback during normal gameplay; authored per-hero
            // steps simply override it.
            Debug.Log("HeroProgression: '" + heroId + "' has no authored constellationSteps; " +
                "using the shared default steps (+5% ATK / +5% HP / +5% DEF / +5% SPD / +10% ATK / +10% HP +10% ATK). " +
                "Assign constellation steps on the HeroData asset to customize.");
        }

        ConstellationStep[] steps = new ConstellationStep[MaxConstellationRank];
        for (int i = 0; i < steps.Length; i++)
        {
            steps[i] = new ConstellationStep
            {
                displayName = ConstellationLabel(i + 1),
                effectType = "none", // future skill-modifier hook; unused by the stat progression
                targetSkillId = string.Empty,
                effectValue = 0f,
            };
        }

        // C1: +5% Attack.
        steps[0].attackBonusPercent = 5f;
        steps[0].description = "+5% ATK";

        // C2: +5% HP.
        steps[1].healthBonusPercent = 5f;
        steps[1].description = "+5% HP";

        // C3: +5% Defense.
        steps[2].defenseBonusPercent = 5f;
        steps[2].description = "+5% DEF";

        // C4: +5% Speed.
        steps[3].speedBonusPercent = 5f;
        steps[3].description = "+5% SPD";

        // C5: +10% Attack.
        steps[4].attackBonusPercent = 10f;
        steps[4].description = "+10% ATK";

        // C6: +10% HP and +10% Attack.
        steps[5].healthBonusPercent = 10f;
        steps[5].attackBonusPercent = 10f;
        steps[5].description = "+10% HP / +10% ATK";

        return steps;
    }
}
