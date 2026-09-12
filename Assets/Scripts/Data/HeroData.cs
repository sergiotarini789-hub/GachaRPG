using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Designer-authored definition of one hero (ScriptableObject template).
/// Everything a hero IS lives here as data - identity, rarity, role, base
/// stats, art reference, growth curve, and skill kit - so the roster can
/// grow into many unique heroes without code changes. Runtime battle state
/// (current stats, cooldowns) lives on <see cref="HeroInstance"/>, never
/// here.
/// </summary>
[CreateAssetMenu(fileName = "NewHero", menuName = "GachaRPG/Hero Data")]
public class HeroData : ScriptableObject
{
    /// <summary>
    /// Stable unique identifier for this hero (e.g. "kael"). Future systems
    /// (summoning, collection, save data) reference heroes by this id; keep
    /// it unique across the roster and never change it once published.
    /// </summary>
    public string heroId;

    /// <summary>
    /// The hero's stable identity used by saves and lookups: the authored
    /// heroId when set, otherwise the display name (so existing assets work
    /// without edits).
    /// </summary>
    public string HeroId => string.IsNullOrEmpty(heroId) ? heroName : heroId;

    /// <summary>Display name shown in the HUD, combat logs, and future meta screens.</summary>
    public string heroName;

    /// <summary>Rarity tier; drives future summoning availability and progression caps.</summary>
    public HeroRarity rarity;

    /// <summary>Combat role: the hero's job in battle (descriptive metadata; the engine never branches on it).</summary>
    public HeroRole role;

    /// <summary>Base maximum health at level 1.</summary>
    public int baseHealth;

    /// <summary>Base attack at level 1.</summary>
    public int baseAttack;

    /// <summary>Base defense at level 1.</summary>
    public int baseDefense;

    /// <summary>Base speed at level 1; feeds the turn order each round.</summary>
    public int baseSpeed;

    /// <summary>Portrait / art reference. The data slot exists now; art comes later.</summary>
    public Sprite icon;

    /// <summary>Character blurb for tooltips and future meta screens.</summary>
    public string description;

    /// <summary>
    /// Per-level growth curve. Read by <see cref="HeroInstance"/>'s
    /// progression system to calculate stats above level 1; never mutated.
    /// </summary>
    public HeroGrowth growth = new HeroGrowth();

    /// <summary>Skill kit the hero brings into battle (assign SkillData assets in the Inspector).</summary>
    public List<SkillData> skills;

    /// <summary>
    /// This hero's awakening configuration: one entry per awakening rank,
    /// in rank order (index 0 = awakening 1). Stat bonuses apply for real;
    /// plannedEffect text is displayed as planned only. An empty list falls
    /// back to <see cref="HeroProgression.DefaultAwakeningSteps"/> (logged
    /// once per hero).
    /// </summary>
    public List<AwakeningStep> awakeningSteps = new List<AwakeningStep>();
}

/// <summary>
/// One awakening rank's data-driven configuration: display info, the Hero
/// Soul cost, and the rank's stat bonuses. plannedEffect describes future
/// combat effects (bleeds, crit, execute...) that are NOT integrated into
/// combat until an effect system exists - the UI shows them as planned and
/// this milestone does not claim they function.
/// </summary>
[Serializable]
public class AwakeningStep
{
    /// <summary>Display name, e.g. "Awakening II".</summary>
    public string displayName = "";

    /// <summary>Player-facing description of what this rank grants.</summary>
    [TextArea]
    public string description = "";

    /// <summary>Hero Souls required for this rank; 0 = the HeroProgression default (10 x rank).</summary>
    public int requiredSouls;

    /// <summary>Percent bonus to max health granted at this rank.</summary>
    public float healthBonusPercent;

    /// <summary>Percent bonus to attack granted at this rank.</summary>
    public float attackBonusPercent;

    /// <summary>Percent bonus to defense granted at this rank.</summary>
    public float defenseBonusPercent;

    /// <summary>Percent bonus to speed granted at this rank.</summary>
    public float speedBonusPercent;

    /// <summary>
    /// Planned future combat effect (e.g. "Skill gains Bleed"). Displayed
    /// as planned; NOT applied in combat yet.
    /// </summary>
    [TextArea]
    public string plannedEffect = "";
}
