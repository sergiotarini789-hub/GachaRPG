using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain C# class representing one owned hero at runtime, instantiated from
/// a <see cref="HeroData"/> template. Owns the hero's progression state -
/// level and experience - and derives its combat stats from the template's
/// base stats plus its <see cref="HeroGrowth"/> curve (see
/// <see cref="CalculateCurrentStats"/>). The template asset never changes:
/// leveling mutates only this instance.
///
/// Also tracks live battle values (health, cooldowns) and an
/// <see cref="isAlive"/> flag used by systems such as <see cref="TurnManager"/>
/// to skip dead units, plus the hero's skill kit.
/// </summary>
public class HeroInstance
{
    /// <summary>The <see cref="HeroData"/> asset this instance was created from.</summary>
    public HeroData data;

    /// <summary>
    /// Maximum health at the hero's current level (base health plus growth),
    /// maintained by <see cref="CalculateCurrentStats"/>. Healing caps here
    /// and the HUD reads this as the HP denominator.
    /// </summary>
    public int maxHealth;

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
    /// The hero's current level, from 1 up to <see cref="MaxLevel"/>. Changes
    /// only through <see cref="LevelUp"/> - levels are never lost.
    /// </summary>
    public int Level { get; private set; }

    /// <summary>
    /// Experience accumulated toward the next level. Resets to 0 once the
    /// hero reaches <see cref="MaxLevel"/>.
    /// </summary>
    public int Experience { get; private set; }

    /// <summary>
    /// The highest level this hero can reach, from the template's growth
    /// curve (guarded to at least 1 so bad data can never yield level 0).
    /// </summary>
    public int MaxLevel => Mathf.Max(1, data.growth.maxLevel);

    /// <summary>Whether the hero has reached <see cref="MaxLevel"/>.</summary>
    public bool IsMaxLevel => Level >= MaxLevel;

    /// <summary>Experience still needed to reach the next level; 0 at max level.</summary>
    public int ExperienceToNextLevel => IsMaxLevel ? 0 : ExperienceRequiredForLevel(Level);

    /// <summary>
    /// Whether the hero can advance a level right now: room to grow and
    /// enough accumulated experience.
    /// </summary>
    public bool CanLevelUp => !IsMaxLevel && Experience >= ExperienceToNextLevel;

    /// <summary>Experience curve step: the XP cost of level 1 to 2; each further level costs step times the level being left.</summary>
    private const int XpPerLevelStep = 100;

    /// <summary>
    /// Experience required to advance from <paramref name="level"/> to the
    /// next one: a simple linear curve (each level costs 100 XP more than
    /// the last), shared by all heroes and fully deterministic.
    /// </summary>
    public static int ExperienceRequiredForLevel(int level)
    {
        return XpPerLevelStep * level;
    }

    /// <summary>
    /// Creates a level 1 instance from a hero template: base stats, full
    /// health, its own copy of the template's skill kit, all skills ready.
    /// </summary>
    /// <param name="sourceData">The <see cref="HeroData"/> asset to instantiate.</param>
    public HeroInstance(HeroData sourceData) : this(sourceData, 1)
    {
    }

    /// <summary>
    /// Creates an instance at the given level: stats are calculated from the
    /// template's base stats plus its growth curve, and the hero starts alive
    /// at full health with its own copy of the template's skill kit.
    /// </summary>
    /// <param name="sourceData">The <see cref="HeroData"/> asset to instantiate.</param>
    /// <param name="level">Starting level, clamped to [1, <see cref="MaxLevel"/>].</param>
    public HeroInstance(HeroData sourceData, int level)
    {
        data = sourceData;
        Level = Mathf.Clamp(level, 1, MaxLevel);
        isAlive = true;
        displayName = sourceData.heroName;

        // Copy the kit so runtime cooldown tracking never mutates the shared
        // HeroData asset (or another hero built from the same template).
        skills = sourceData.skills != null
            ? new List<SkillData>(sourceData.skills)
            : new List<SkillData>();

        cooldownTracker = new Dictionary<SkillData, int>();

        CalculateCurrentStats();
        currentHealth = maxHealth;
    }

    /// <summary>
    /// Grants experience and applies every level-up it affords, carrying any
    /// remainder into the next level's progress. Experience past the maximum
    /// level is ignored. Returns the number of levels gained.
    /// </summary>
    /// <param name="amount">Experience to grant; negative amounts are ignored.</param>
    public int AddExperience(int amount)
    {
        if (amount <= 0 || IsMaxLevel)
        {
            return 0;
        }

        Experience += amount;

        int levelsGained = 0;
        while (CanLevelUp)
        {
            LevelUp();
            levelsGained++;
        }

        return levelsGained;
    }

    /// <summary>
    /// Consumes the experience required for the next level and advances one
    /// level, refreshing calculated stats. Returns false - changing nothing -
    /// when the hero cannot level up.
    /// </summary>
    public bool LevelUp()
    {
        if (!CanLevelUp)
        {
            return false;
        }

        Experience -= ExperienceToNextLevel;
        Level++;
        if (IsMaxLevel)
        {
            // No next level to progress toward; drop any remainder.
            Experience = 0;
        }

        CalculateCurrentStats();
        return true;
    }

    /// <summary>
    /// Recomputes current stats from the template: each stat is its base
    /// value plus the growth curve's per-level gain times the levels gained
    /// above level 1, rounded to the nearest integer. Current health is
    /// clamped to the new maximum, so gaining a level never reduces health.
    /// </summary>
    public void CalculateCurrentStats()
    {
        HeroGrowth growth = data.growth;
        int levelsGained = Level - 1;

        maxHealth = data.baseHealth + Mathf.RoundToInt(growth.healthPerLevel * levelsGained);
        currentAttack = data.baseAttack + Mathf.RoundToInt(growth.attackPerLevel * levelsGained);
        currentDefense = data.baseDefense + Mathf.RoundToInt(growth.defensePerLevel * levelsGained);
        currentSpeed = data.baseSpeed + Mathf.RoundToInt(growth.speedPerLevel * levelsGained);

        if (currentHealth > maxHealth)
        {
            currentHealth = maxHealth;
        }
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
    /// current maximum health (base health plus growth for its level).
    /// </summary>
    /// <param name="amount">Amount of healing to apply.</param>
    public void Heal(int amount)
    {
        currentHealth = Math.Min(maxHealth, currentHealth + amount);
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
