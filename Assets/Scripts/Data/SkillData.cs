using UnityEngine;

/// <summary>
/// Designer-authored description of one skill (ScriptableObject template).
/// What a skill does is its <see cref="SkillType"/> (effect); who it affects
/// is its <see cref="target"/>. Runtime state (cooldowns) lives on
/// <see cref="HeroInstance"/>, not here - keep this asset free of mutable
/// battle data so it can be shared.
/// </summary>
[CreateAssetMenu(fileName = "NewSkill", menuName = "GachaRPG/Skill Data")]
public class SkillData : ScriptableObject
{
    /// <summary>Display name shown in combat logs and UI.</summary>
    public string skillName;

    /// <summary>What performing the skill does (effect).</summary>
    public SkillType type;

    /// <summary>Who the skill affects (target). Defaults to Enemy, so assets
    /// created before this field existed (e.g. Power Strike) keep behaving as
    /// enemy-targeted damage skills.</summary>
    public SkillTarget target = SkillTarget.Enemy;

    /// <summary>
    /// Multiplier applied to the attacker's currentAttack:
    /// 1.0 = 100% (base damage / heal), 1.5 = 150%, and so on.
    /// </summary>
    public float damageMultiplier = 1f;

    /// <summary>Cooldown in turns; 0 means usable every turn.</summary>
    public int cooldown;

    /// <summary>Designer notes / tooltip text.</summary>
    [TextArea]
    public string description;

    /// <summary>
    /// Highest level this skill can reach; 1 = no upgrades available.
    /// Skill levels are per-hero state on <see cref="HeroInstance"/>.
    /// </summary>
    public int maxSkillLevel = 5;

    /// <summary>
    /// Effect power added per skill level above 1, applied linearly:
    /// effective multiplier = damageMultiplier + levelMultiplierStep x
    /// (level - 1). 0 keeps the skill's power flat at every level.
    /// </summary>
    public float levelMultiplierStep = 0.1f;

    /// <summary>Gold cost per upgrade, multiplied by the skill's current level.</summary>
    public int upgradeGoldCost = 100;

    /// <summary>Skill material cost per upgrade, multiplied by the skill's current level.</summary>
    public int upgradeMaterialCost = 1;

    /// <summary>
    /// The skill's stable identity for saves: the skill name (unique inside
    /// each hero's kit).
    /// </summary>
    public string SkillId => string.IsNullOrEmpty(skillName) ? "skill" : skillName;
}
