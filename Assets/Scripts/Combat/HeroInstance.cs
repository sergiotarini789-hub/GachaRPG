using System;
using System.Collections.Generic;

/// <summary>
/// Plain C# class holding a hero's runtime battle state, instantiated from a
/// <see cref="HeroData"/> template. Starts with the hero's base stats and
/// tracks live values (health, attack, defense, speed) that combat effects
/// can modify, plus an <see cref="isAlive"/> flag used by systems such as
/// <see cref="TurnManager"/> to skip dead units.
///
/// Also owns the hero's skill kit and per-skill cooldown tracking.
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
    /// Runtime label for logs and UI. Defaults to the template's heroName;
    /// the test harness relabels look-alike heroes (e.g. "Hero 2") so
    /// battle logs stay readable.
    /// </summary>
    public string displayName;

    /// <summary>Skills this hero can use in battle. Populate at spawn time from the hero's kit.</summary>
    public List<SkillData> skills;

    /// <summary>Turns remaining before each tracked skill is usable again. Skills not in the map are ready.</summary>
    private Dictionary<SkillData, int> cooldownTracker;

    /// <summary>
    /// Creates a battle-ready instance from a hero template: all current stats
    /// start at the template's base values, the hero starts alive with its own
    /// copy of the template's skill kit, and all skills are ready.
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
        displayName = sourceData.heroName;

        // Copy the kit so runtime cooldown tracking never mutates the shared
        // HeroData asset (or another hero built from the same template).
        skills = sourceData.skills != null
            ? new List<SkillData>(sourceData.skills)
            : new List<SkillData>();

        cooldownTracker = new Dictionary<SkillData, int>();
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

    /// <summary>
    /// Whether the skill can be used right now. True unless the skill is
    /// still cooling down; skills with cooldown 0 are ready every turn.
    /// </summary>
    /// <param name="skill">The skill to check.</param>
    /// <returns>True if the skill is off cooldown.</returns>
    public bool IsSkillReady(SkillData skill)
    {
        return !cooldownTracker.TryGetValue(skill, out int turnsRemaining) || turnsRemaining <= 0;
    }

    /// <summary>
    /// Puts the skill on cooldown for its configured number of turns.
    /// </summary>
    /// <param name="skill">The skill that was just used.</param>
    public void UseSkillCooldown(SkillData skill)
    {
        cooldownTracker[skill] = skill.cooldown;
    }

    /// <summary>
    /// Reduces every tracked cooldown by one turn, floored at 0. Call at the
    /// start of this hero's turn so its cooldowns recover while it acts.
    /// </summary>
    public void TickCooldowns()
    {
        // Copy the keys so the dictionary is never modified while enumerated.
        List<SkillData> tracked = new List<SkillData>(cooldownTracker.Keys);
        foreach (SkillData skill in tracked)
        {
            cooldownTracker[skill] = Math.Max(0, cooldownTracker[skill] - 1);
        }
    }
}
