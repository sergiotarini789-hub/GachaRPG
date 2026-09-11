using System;

/// <summary>
/// Plain C# class holding a hero's runtime battle state, instantiated from a
/// <see cref="HeroData"/> template. Starts with the hero's base stats and
/// tracks live values (health, attack, defense, speed) that combat effects
/// can modify, plus an <see cref="isAlive"/> flag used by systems such as
/// <see cref="TurnManager"/> to skip dead units.
/// </summary>
public class HeroInstance
{
    /// <summary>The <see cref="HeroData"/> asset this instance was created from.</summary>
    public HeroData data;

    /// <summary>Current health. At 0 the hero is considered dead.</summary>
    public int currentHealth;

    /// <summary>Current attack value.</summary>
    public int currentAttack;

    /// <summary>Current defense value.</summary>
    public int currentDefense;

    /// <summary>Current speed value; feeds the turn gauge each tick.</summary>
    public int currentSpeed;

    /// <summary>Whether the hero is still alive and able to take turns.</summary>
    public bool isAlive;

    /// <summary>
    /// Creates a battle-ready instance from a hero template: all current stats
    /// start at the template's base values and the hero starts alive.
    /// </summary>
    /// <param name="sourceData">The <see cref="HeroData"/> asset to instantiate.</param>
    public HeroInstance(HeroData sourceData)
    {
        data = sourceData;
        currentHealth = sourceData.baseHealth;
        currentAttack = sourceData.baseAttack;
        currentDefense = sourceData.baseDefense;
        currentSpeed = sourceData.baseSpeed;
        isAlive = true;
    }

    /// <summary>
    /// Reduces current health by the given amount, floored at 0.
    /// The hero dies (isAlive = false) when health reaches 0.
    /// </summary>
    /// <param name="amount">Amount of damage to apply.</param>
    public void TakeDamage(int amount)
    {
        currentHealth = Math.Max(0, currentHealth - amount);

        if (currentHealth == 0)
        {
            isAlive = false;
        }
    }

    /// <summary>
    /// Increases current health by the given amount, capped at the hero's
    /// base health from its <see cref="HeroData"/> template.
    /// </summary>
    /// <param name="amount">Amount of healing to apply.</param>
    public void Heal(int amount)
    {
        currentHealth = Math.Min(data.baseHealth, currentHealth + amount);
    }
}
