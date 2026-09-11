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
            $"{attacker.displayName} attacks {defender.displayName} for {damage} damage. " +
            $"{defender.displayName} HP: {defender.currentHealth}/{defender.data.baseHealth}");
    }

    /// <summary>
    /// Resolves one skill use, scaling with the attacker's currentAttack and
    /// applying the skill's effect (SkillType) to the resolved target:
    /// Damage skills hit the target for (attack x multiplier - defense,
    /// minimum 1); Heal skills restore the target's health (capped at the
    /// target's base health - pass the attacker itself for self-heals);
    /// Buff is a placeholder announcement. Puts the skill on cooldown at the
    /// end; a skill with no valid target fails safely instead (no effect, no
    /// cooldown). Target selection (enemy/self/ally) is the caller's job -
    /// see SkillTarget and TeamBattle.
    /// </summary>
    /// <param name="attacker">The unit using the skill.</param>
    /// <param name="target">The hero the skill is aimed at.</param>
    /// <param name="skill">The skill being used.</param>
    public static void PerformSkill(HeroInstance attacker, HeroInstance target, SkillData skill)
    {
        string attackerName = attacker.displayName;

        if (skill.type == SkillType.Damage)
        {
            if (target == null || !target.isAlive)
            {
                Debug.LogWarning($"{attackerName} fails to use {skill.skillName}: no valid target.");
                return;
            }

            string targetName = target.displayName;
            int damage = Mathf.RoundToInt(attacker.currentAttack * skill.damageMultiplier) - target.currentDefense;
            damage = Math.Max(1, damage);
            target.TakeDamage(damage);

            Debug.Log(
                $"{attackerName} uses {skill.skillName} on {targetName} for {damage} damage. " +
                $"{targetName} HP: {target.currentHealth}/{target.data.baseHealth}");
        }
        else if (skill.type == SkillType.Heal)
        {
            if (target == null || !target.isAlive)
            {
                Debug.LogWarning($"{attackerName} fails to use {skill.skillName}: no valid heal target.");
                return;
            }

            string targetName = target.displayName;
            int healAmount = Mathf.RoundToInt(attacker.currentAttack * skill.damageMultiplier);
            int healthBefore = target.currentHealth;
            target.Heal(healAmount);

            // Self-heals keep the original log format; healing someone else
            // names the recipient.
            if (ReferenceEquals(target, attacker))
            {
                Debug.Log(
                    $"{attackerName} uses {skill.skillName} and heals for {target.currentHealth - healthBefore}. " +
                    $"{targetName} HP: {target.currentHealth}/{target.data.baseHealth}");
            }
            else
            {
                Debug.Log(
                    $"{attackerName} uses {skill.skillName} and heals {targetName} for {target.currentHealth - healthBefore}. " +
                    $"{targetName} HP: {target.currentHealth}/{target.data.baseHealth}");
            }
        }
        else // SkillType.Buff - no stat-modification system yet, so just announce it.
        {
            Debug.Log($"{attackerName} uses {skill.skillName}.");
        }

        attacker.UseSkillCooldown(skill);
    }
}
