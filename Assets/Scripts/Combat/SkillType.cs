/// <summary>
/// What a skill does when performed. Buff is a placeholder for a future
/// stat-modification system.
/// </summary>
public enum SkillType
{
    /// <summary>Deals damage to the target.</summary>
    Damage,

    /// <summary>Restores the attacker's own health.</summary>
    Heal,

    /// <summary>Enhances the attacker (no stat system yet - logged only).</summary>
    Buff,
}
