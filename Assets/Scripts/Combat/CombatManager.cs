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
}
