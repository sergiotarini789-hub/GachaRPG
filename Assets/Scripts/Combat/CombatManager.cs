using System;
using UnityEngine;

/// <summary>
/// Plain C# helper with the core combat math. Kept static and engine-free
/// (apart from logging) so battle logic is easy to test in isolation.
/// </summary>
public static class CombatManager
{
    /// <summary>
    /// Calculates attack damage as attacker's currentAttack minus defender's
    /// currentDefense. Damage can never drop below 1, so every attack at
    /// least scratches (never 0 or negative).
    /// </summary>
    /// <param name="attacker">The acting unit.</param>
    /// <param name="defender">The unit receiving the hit.</param>
    /// <returns>Damage to apply, minimum 1.</returns>
    public static int CalculateDamage(HeroInstance attacker, HeroInstance defender)
    {
        int damage = attacker.currentAttack - defender.currentDefense;
        return Math.Max(1, damage);
    }

    /// <summary>
    /// Resolves one attack: calculates damage and applies it to the defender,
    /// logging the result and the defender's remaining HP.
    /// </summary>
    /// <param name="attacker">The acting unit.</param>
    /// <param name="defender">The unit receiving the hit.</param>
    public static void PerformAttack(HeroInstance attacker, HeroInstance defender)
    {
        int damage = CalculateDamage(attacker, defender);
        defender.TakeDamage(damage);

        Debug.Log(
            $"{attacker.data.heroName} attacks {defender.data.heroName} for {damage} damage. " +
            $"{defender.data.heroName} HP: {defender.currentHealth}/{defender.data.baseHealth}");
    }

    /// <summary>
    /// Resolves one skill use, scaling with the attacker's currentAttack:
    /// Damage skills hit the defender for (attack x multiplier - defense,
    /// minimum 1); Heal skills restore the attacker's own health (capped at
    /// base health). Puts the skill on cooldown at the end. Readiness should
    /// be checked first via <see cref="HeroInstance.IsSkillReady"/>.
    /// </summary>
    /// <param name="attacker">The unit using the skill.</param>
    /// <param name="defender">The opposing unit (target of damage skills).</param>
    /// <param name="skill">The skill being used.</param>
    public static void PerformSkill(HeroInstance attacker, HeroInstance defender, SkillData skill)
    {
        string attackerName = attacker.displayName;
        string defenderName = defender.displayName;

        if (skill.type == SkillType.Damage)
        {
            int damage = Mathf.RoundToInt(attacker.currentAttack * skill.damageMultiplier) - defender.currentDefense;
            damage = Math.Max(1, damage);
            defender.TakeDamage(damage);

            Debug.Log(
                $"{attackerName} uses {skill.skillName} on {defenderName} for {damage} damage. " +
                $"{defenderName} HP: {defender.currentHealth}/{defender.data.baseHealth}");
        }
        else if (skill.type == SkillType.Heal)
        {
            int healAmount = Mathf.RoundToInt(attacker.currentAttack * skill.damageMultiplier);
            int healthBefore = attacker.currentHealth;
            attacker.Heal(healAmount);

            Debug.Log(
                $"{attackerName} uses {skill.skillName} and heals for {attacker.currentHealth - healthBefore}. " +
                $"{attackerName} HP: {attacker.currentHealth}/{attacker.data.baseHealth}");
        }
        else // SkillType.Buff - no stat-modification system yet, so just announce it.
        {
            Debug.Log($"{attackerName} uses {skill.skillName}.");
        }

        attacker.UseSkillCooldown(skill);
    }
}
