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
}
