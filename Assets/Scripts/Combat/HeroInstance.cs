using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain C# class representing one owned hero at runtime, instantiated from
/// a <see cref="HeroData"/> template. Owns ALL of the hero's progression
/// state - level, experience, ascension rank, awakening rank, constellation
/// rank (C0-C6, one per duplicate copy of this hero), per-skill levels, and
/// the six equipment slots - and derives its combat stats from the
/// template's base stats plus its <see cref="HeroGrowth"/> curve, the
/// ascension, awakening, and constellation modifiers, and the equipment
/// loadout's flat bonuses (see <see cref="CalculateCurrentStats"/>). The
/// template asset never changes: progression mutates only this instance.
///
/// Equipment is LENT to the hero, never owned: the
/// <see cref="EquipmentInventory"/> owns every item and is the only system
/// that changes what this hero wears (see
/// <see cref="AttachEquipment"/>/<see cref="DetachEquipment"/>); a hero
/// with no equipment calculates exactly the stats it always did.
///
/// The effective maximum level is ascension-controlled (see
/// <see cref="MaxLevel"/>); duplicate summons of the same template raise the
/// constellation rank through <see cref="ApplyDuplicate"/> (hard-capped at
/// C6 - extra duplicates become Hero Tokens on the wallet; see
/// <see cref="HeroRoster.ApplyDuplicate"/>), and battles consume
/// <see cref="CreateCombatClone"/> copies so combat mutations (health,
/// cooldowns) never dirty the roster's authoritative progression.
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

    /// <summary>
    /// Crit chance, percent (0-100): the chance a damage hit becomes a
    /// critical strike. Heroes have NO innate crit - this is granted only
    /// by equipment, so an unequipped hero is exactly as before.
    /// </summary>
    public int critRate;

    /// <summary>
    /// Bonus crit damage, percent on top of the base 150% critical
    /// multiplier (equipment-granted only; 0 = crits deal 1.5x damage).
    /// </summary>
    public int critDamage;

    /// <summary>
    /// Accuracy, percent - the future debuff-landing stat. Carried on the
    /// final stat sheet and scored by equipment comparisons, but combat
    /// integration is PENDING a debuff/status system (never faked).
    /// </summary>
    public int accuracy;

    /// <summary>
    /// Resistance, percent - the future debuff-resist stat. Carried on the
    /// final stat sheet and scored by equipment comparisons, but combat
    /// integration is PENDING a debuff/status system (never faked).
    /// </summary>
    public int resistance;

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
    /// The hero's six equipment slots (slot -> the lent
    /// <see cref="EquipmentInstance"/>). Empty for a fresh hero. Mutated
    /// ONLY through <see cref="AttachEquipment"/>/
    /// <see cref="DetachEquipment"/> - those are the
    /// <see cref="EquipmentInventory"/>'s tools, not gameplay APIs.
    /// </summary>
    private readonly Dictionary<EquipmentSlot, EquipmentInstance> equippedItems = new Dictionary<EquipmentSlot, EquipmentInstance>();

    /// <summary>Level of each kit skill (missing entries are level 1); scales the skill's effect power.</summary>
    private readonly Dictionary<SkillData, int> skillLevels = new Dictionary<SkillData, int>();

    /// <summary>Ascension rank (0..HeroProgression.MaxAscensionRank); raises the level cap and adds stat bonuses.</summary>
    private int ascensionRank;

    /// <summary>Awakening rank (0..awakeningSteps.Length); each reached rank applies its step's stat bonuses.</summary>
    private int awakeningRank;

    /// <summary>
    /// Constellation rank (0..this hero's <see cref="MaxConstellation"/>,
    /// i.e. C0-C6): one rank per duplicate copy of this hero. C0 = obtained
    /// but no duplicates; the cap is hard and per-hero. NEVER mutated
    /// directly - duplicates flow through <see cref="ApplyDuplicate"/>, the
    /// single authoritative path.
    /// </summary>
    private int constellationRank;

    /// <summary>This instance's resolved awakening steps (authored per-hero data, or the shared defaults).</summary>
    private readonly AwakeningStep[] awakeningSteps;

    /// <summary>This instance's resolved constellation steps (authored per-hero data, or the shared defaults).</summary>
    private readonly ConstellationStep[] constellationSteps;

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
    /// The highest level this hero can currently reach: the ascension
    /// rank's cap, limited by the template's own growth curve (a hero never
    /// outgrows its authored curve). Ascending raises this over time.
    /// </summary>
    public int MaxLevel => Mathf.Clamp(
        HeroProgression.AscensionMaxLevel(ascensionRank), 1, Mathf.Max(1, data.growth.maxLevel));

    /// <summary>Current ascension rank; each rank raises <see cref="MaxLevel"/> and grants stat bonuses.</summary>
    public int Ascension => ascensionRank;

    /// <summary>Current awakening rank; each rank applies its step's stat bonuses.</summary>
    public int Awakening => awakeningRank;

    /// <summary>
    /// Current constellation rank (C0-C6): one per duplicate copy of this
    /// hero. Constellation is a separate progression layer from awakening -
    /// it is duplicate-driven, while awakening is material-driven.
    /// </summary>
    public int Constellation => constellationRank;

    /// <summary>
    /// The highest constellation rank THIS hero can reach: the template's
    /// authored <see cref="HeroData.maxConstellation"/> (default 6),
    /// clamped to the global hard cap. Duplicates past this rank convert
    /// into Hero Tokens.
    /// </summary>
    public int MaxConstellation => Mathf.Clamp(
        data != null ? data.maxConstellation : HeroProgression.MaxConstellationRank,
        0, HeroProgression.MaxConstellationRank);

    /// <summary>Whether this hero has reached its constellation cap.</summary>
    public bool IsMaxConstellation => constellationRank >= MaxConstellation;

    /// <summary>The highest awakening rank this hero can reach (0 when no steps exist).</summary>
    public int MaxAwakeningRank => awakeningSteps.Length;

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

        // Resolve the awakening configuration once: authored per-hero steps,
        // or the shared defaults (which log a development warning).
        awakeningSteps = data.awakeningSteps != null && data.awakeningSteps.Count > 0
            ? data.awakeningSteps.ToArray()
            : HeroProgression.DefaultAwakeningSteps(data);

        // Resolve the constellation configuration the same way: authored
        // per-hero steps, or shared placeholder defaults (also logged).
        constellationSteps = data.constellationSteps != null && data.constellationSteps.Count > 0
            ? data.constellationSteps.ToArray()
            : HeroProgression.DefaultConstellationSteps(data);

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
    /// Recomputes current stats through the single modifier pipeline:
    /// base + per-level growth (exactly as before), then multiplied by the
    /// ascension bonus (per rank) and the accumulated awakening AND
    /// constellation bonuses (each summed per reached rank), and finally the
    /// equipment loadout's bonuses are applied - flat stats (main stats,
    /// secondaries, set bonuses) on top of the four base stats, percent
    /// stats (crit rate/damage, accuracy, resistance) onto their own final
    /// fields. The whole calculation is deterministic from the
    /// template's base stats plus this instance's progression state - it
    /// never reads or mutates the previously calculated values, so
    /// refreshing the UI or re-equipping can never compound bonuses. Future
    /// modifiers (buffs) extend this same method. With no ascension,
    /// awakening, constellations, or equipment the values are identical to
    /// the raw growth-curve results. Current health is clamped to the new
    /// maximum, so gaining progression never reduces health.
    /// </summary>
    public void CalculateCurrentStats()
    {
        HeroGrowth growth = data.growth;
        int levelsGained = Level - 1;

        // Level modifier: base + growth (unchanged from the original formula).
        int health = data.baseHealth + Mathf.RoundToInt(growth.healthPerLevel * levelsGained);
        int attack = data.baseAttack + Mathf.RoundToInt(growth.attackPerLevel * levelsGained);
        int defense = data.baseDefense + Mathf.RoundToInt(growth.defensePerLevel * levelsGained);
        int speed = data.baseSpeed + Mathf.RoundToInt(growth.speedPerLevel * levelsGained);

        // Ascension modifier: +StatBonusPercentPerAscension per rank.
        float ascensionMultiplier = 1f + HeroProgression.StatBonusPercentPerAscension * 0.01f * ascensionRank;

        // Awakening + constellation modifiers: the summed percent bonuses of
        // every reached rank of each system (both feed the same percent
        // term, so their bonuses stack additively with each other).
        GetAwakeningBonuses(out float healthBonus, out float attackBonus, out float defenseBonus, out float speedBonus);
        GetConstellationBonuses(out float constellationHealth, out float constellationAttack, out float constellationDefense, out float constellationSpeed);

        // Equipment modifier: flat additions on top of the percentage
        // pipeline (main stats + secondary stats + set bonuses), summed by
        // the ONE central equipment calculator - never recomputed anywhere
        // else, so bonuses cannot double-apply, and an unequipped hero
        // contributes exactly zero (stats identical to pre-equipment).
        // Percent stats (crit and friends) are carried as-is: heroes have
        // no innate values there, so removing gear fully reverts them.
        EquipmentStatSet equipmentStats = EquipmentStats.GetLoadoutStats(this);

        maxHealth = Mathf.Max(1, Mathf.RoundToInt(health * ascensionMultiplier * (1f + (healthBonus + constellationHealth) * 0.01f)) + equipmentStats.hp);
        currentAttack = Mathf.Max(1, Mathf.RoundToInt(attack * ascensionMultiplier * (1f + (attackBonus + constellationAttack) * 0.01f)) + equipmentStats.atk);
        currentDefense = Mathf.Max(1, Mathf.RoundToInt(defense * ascensionMultiplier * (1f + (defenseBonus + constellationDefense) * 0.01f)) + equipmentStats.def);
        currentSpeed = Mathf.Max(1, Mathf.RoundToInt(speed * ascensionMultiplier * (1f + (speedBonus + constellationSpeed) * 0.01f)) + equipmentStats.spd);
        critRate = equipmentStats.critRate;
        critDamage = equipmentStats.critDamage;
        accuracy = equipmentStats.accuracy;
        resistance = equipmentStats.resistance;

        if (currentHealth > maxHealth)
        {
            currentHealth = maxHealth;
        }
    }

    /// <summary>
    /// Sums the stat bonuses of every reached constellation rank (percent
    /// values) - the constellation counterpart of
    /// <see cref="GetAwakeningBonuses"/>. Steps beyond this hero's cap are
    /// never counted.
    /// </summary>
    private void GetConstellationBonuses(out float health, out float attack, out float defense, out float speed)
    {
        health = 0f;
        attack = 0f;
        defense = 0f;
        speed = 0f;

        int ranks = Mathf.Min(constellationRank, constellationSteps.Length, MaxConstellation);
        for (int i = 0; i < ranks; i++)
        {
            ConstellationStep step = constellationSteps[i];
            if (step == null)
            {
                continue;
            }

            health += step.healthBonusPercent;
            attack += step.attackBonusPercent;
            defense += step.defenseBonusPercent;
            speed += step.speedBonusPercent;
        }
    }

    /// <summary>Sums the stat bonuses of every reached awakening rank (percent values).</summary>
    private void GetAwakeningBonuses(out float health, out float attack, out float defense, out float speed)
    {
        health = 0f;
        attack = 0f;
        defense = 0f;
        speed = 0f;

        int ranks = Mathf.Min(awakeningRank, awakeningSteps.Length);
        for (int i = 0; i < ranks; i++)
        {
            AwakeningStep step = awakeningSteps[i];
            if (step == null)
            {
                continue;
            }

            health += step.healthBonusPercent;
            attack += step.attackBonusPercent;
            defense += step.defenseBonusPercent;
            speed += step.speedBonusPercent;
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

    // ---------------------------------------------------------------------
    // Constellation (C0-C6; one rank per duplicate copy of this hero).
    // ---------------------------------------------------------------------

    /// <summary>
    /// THE single authoritative path every duplicate of this hero takes
    /// (summon duplicates, the roster's debug duplicate tool, and duplicate
    /// consolidation all flow through here - never poke the rank directly).
    /// Advances the constellation by exactly one rank, never past this
    /// hero's cap, and recalculates stats so the new rank's bonuses apply
    /// immediately. Returns true when the rank advanced; returns false -
    /// changing nothing - when the hero is already at its cap, in which
    /// case the caller converts the extra duplicate into Hero Tokens (see
    /// <see cref="HeroRoster.ApplyDuplicate"/>).
    /// </summary>
    public bool ApplyDuplicate()
    {
        if (constellationRank >= MaxConstellation)
        {
            return false; // the cap is hard; the caller converts the extra duplicate.
        }

        constellationRank++;
        CalculateCurrentStats();
        return true;
    }

    /// <summary>The next constellation rank's configuration, or null at (or beyond) the C6 cap.</summary>
    public ConstellationStep NextConstellationStep()
    {
        return constellationRank < constellationSteps.Length ? constellationSteps[constellationRank] : null;
    }

    /// <summary>
    /// The configuration of the given 1-based constellation rank (C1 = 1),
    /// or null when that rank has no step data (read by the Heroes screen's
    /// bonus list).
    /// </summary>
    public ConstellationStep GetConstellationStep(int rank)
    {
        int index = rank - 1;
        return index >= 0 && index < constellationSteps.Length ? constellationSteps[index] : null;
    }

    // ---------------------------------------------------------------------
    // Awakening (ranks bought with Awakening Materials from the wallet;
    // deliberately independent of duplicates and the constellation).
    // ---------------------------------------------------------------------

    /// <summary>The next awakening rank's configuration, or null at (or without) the cap.</summary>
    public AwakeningStep NextAwakeningStep()
    {
        return awakeningRank < awakeningSteps.Length ? awakeningSteps[awakeningRank] : null;
    }

    /// <summary>The Awakening Material cost of awakening to the given 1-based rank: the step's override, or the default (10 x rank).</summary>
    public int AwakeningMaterialCost(int targetRank)
    {
        if (awakeningSteps.Length == 0)
        {
            return int.MaxValue; // never affordable when no steps exist
        }

        int index = Mathf.Clamp(targetRank, 1, awakeningSteps.Length) - 1;
        AwakeningStep step = awakeningSteps[index];
        return step != null && step.requiredAwakeningMaterials > 0
            ? step.requiredAwakeningMaterials
            : HeroProgression.AwakeningMaterialCostForRank(targetRank);
    }

    /// <summary>
    /// Whether the hero can awaken right now: a next rank must exist and
    /// the wallet must cover the rank's Awakening Material cost. The reason
    /// describes what is missing.
    /// </summary>
    public bool CanAwaken(PlayerWallet wallet, out string reason)
    {
        if (awakeningSteps.Length == 0)
        {
            reason = "no awakening configured for this hero";
            return false;
        }

        if (awakeningRank >= awakeningSteps.Length)
        {
            reason = "maximum awakening reached";
            return false;
        }

        int cost = AwakeningMaterialCost(awakeningRank + 1);
        if (wallet == null || !wallet.CanAfford(0, 0, 0, cost))
        {
            reason = (wallet != null ? wallet.awakeningMaterials : 0) + " / " + cost + " awakening materials";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Awakens to the next rank: consumes the rank's Awakening Material
    /// cost from the wallet, raises the rank, and recalculates stats.
    /// Refuses (returns false, changes nothing - including the wallet) when
    /// <see cref="CanAwaken"/> fails.
    /// </summary>
    public bool Awaken(PlayerWallet wallet)
    {
        if (!CanAwaken(wallet, out _))
        {
            return false;
        }

        wallet.TryConsume(0, 0, 0, AwakeningMaterialCost(awakeningRank + 1));
        awakeningRank++;
        CalculateCurrentStats();
        return true;
    }

    // ---------------------------------------------------------------------
    // Ascension (raises the level cap; costs wallet resources).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Whether the hero can ascend right now: below the maximum rank, at
    /// the current level cap, and with the wallet covering the rank's gold
    /// and ascension material cost. The reason describes what blocks it.
    /// </summary>
    public bool CanAscend(PlayerWallet wallet, out string reason)
    {
        if (ascensionRank >= HeroProgression.MaxAscensionRank)
        {
            reason = "maximum ascension reached";
            return false;
        }

        int requiredLevel = HeroProgression.AscensionMaxLevel(ascensionRank);
        if (Level < requiredLevel)
        {
            reason = "requires level " + requiredLevel;
            return false;
        }

        int gold = HeroProgression.AscensionGoldCost(ascensionRank + 1);
        int materials = HeroProgression.AscensionMaterialCost(ascensionRank + 1);
        if (wallet == null || !wallet.CanAfford(gold, materials, 0, 0))
        {
            reason = "costs " + gold + " gold + " + materials + " ascension materials";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Ascends to the next rank: consumes the costs, raises the rank (which
    /// raises <see cref="MaxLevel"/>), and recalculates stats. Refuses
    /// (returns false, changes nothing - including the wallet) when
    /// <see cref="CanAscend"/> fails.
    /// </summary>
    public bool Ascend(PlayerWallet wallet)
    {
        if (!CanAscend(wallet, out _))
        {
            return false;
        }

        wallet.TryConsume(
            HeroProgression.AscensionGoldCost(ascensionRank + 1),
            HeroProgression.AscensionMaterialCost(ascensionRank + 1),
            0,
            0);
        ascensionRank++;
        CalculateCurrentStats();
        return true;
    }

    // ---------------------------------------------------------------------
    // Skill progression (levels live here; the effect scales through
    // GetSkillMultiplier, which the existing combat engine consumes).
    // ---------------------------------------------------------------------

    /// <summary>The skill's current level on this hero (missing entries are level 1).</summary>
    public int GetSkillLevel(SkillData skill)
    {
        int level;
        return skill != null && skillLevels.TryGetValue(skill, out level) ? Mathf.Max(1, level) : 1;
    }

    /// <summary>The skill's maximum level from its data.</summary>
    public int GetSkillMaxLevel(SkillData skill)
    {
        return skill != null ? Mathf.Max(1, skill.maxSkillLevel) : 1;
    }

    /// <summary>
    /// The skill's effective effect power at this hero's current skill
    /// level: damageMultiplier + levelMultiplierStep x (level - 1). At
    /// level 1 this equals the skill's authored multiplier exactly.
    /// </summary>
    public float GetSkillMultiplier(SkillData skill)
    {
        if (skill == null)
        {
            return 1f;
        }

        return skill.damageMultiplier + skill.levelMultiplierStep * (GetSkillLevel(skill) - 1);
    }

    /// <summary>
    /// Whether the skill can be upgraded right now: below its max level and
    /// the wallet covers the gold + skill material cost (per-skill cost
    /// fields times the current level).
    /// </summary>
    public bool CanUpgradeSkill(SkillData skill, PlayerWallet wallet, out string reason)
    {
        if (skill == null)
        {
            reason = "no skill";
            return false;
        }

        int level = GetSkillLevel(skill);
        if (level >= GetSkillMaxLevel(skill))
        {
            reason = "skill at max level";
            return false;
        }

        int gold = skill.upgradeGoldCost * level;
        int materials = skill.upgradeMaterialCost * level;
        if (wallet == null || !wallet.CanAfford(gold, 0, materials, 0))
        {
            reason = "costs " + gold + " gold + " + materials + " skill materials";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Upgrades the skill one level: consumes the costs and raises the
    /// level; effect power is read live at cast time, so no recalculation
    /// is needed. Refuses (returns false) when <see cref="CanUpgradeSkill"/>
    /// fails.
    /// </summary>
    public bool UpgradeSkill(SkillData skill, PlayerWallet wallet)
    {
        if (!CanUpgradeSkill(skill, wallet, out _))
        {
            return false;
        }

        int level = GetSkillLevel(skill);
        wallet.TryConsume(skill.upgradeGoldCost * level, 0, skill.upgradeMaterialCost * level, 0);
        skillLevels[skill] = level + 1;
        return true;
    }

    /// <summary>Restores a saved skill level (clamped to the skill's max); save/load support.</summary>
    public void SetSkillLevel(SkillData skill, int level)
    {
        if (skill != null)
        {
            skillLevels[skill] = Mathf.Clamp(level, 1, GetSkillMaxLevel(skill));
        }
    }

    // ---------------------------------------------------------------------
    // Equipment slots. The EquipmentInventory owns all items and is the
    // only caller of Attach/Detach; everything else reads.
    // ---------------------------------------------------------------------

    /// <summary>The item equipped in the given slot, or null when the slot is empty.</summary>
    public EquipmentInstance GetEquippedItem(EquipmentSlot slot)
    {
        return equippedItems.TryGetValue(slot, out EquipmentInstance item) ? item : null;
    }

    /// <summary>The instance id of the item equipped in the slot, or an empty string (save support).</summary>
    public string GetEquippedItemId(EquipmentSlot slot)
    {
        return GetEquippedItem(slot) != null ? GetEquippedItem(slot).instanceId : string.Empty;
    }

    /// <summary>How many of the six slots currently hold an item.</summary>
    public int EquippedItemCount => equippedItems.Count;

    /// <summary>
    /// Lends the item to this hero in the given slot and recalculates stats.
    /// EquipmentInventory ONLY: validates the item belongs in the slot.
    /// Equipping never duplicates an item or touches the inventory - the
    /// caller manages ownership and the previous slot occupant.
    /// </summary>
    public bool AttachEquipment(EquipmentSlot slot, EquipmentInstance item)
    {
        if (item == null || item.slot != slot)
        {
            return false;
        }

        equippedItems[slot] = item;
        CalculateCurrentStats();
        return true;
    }

    /// <summary>
    /// Returns the slot's item to the inventory pool and recalculates stats
    /// (the item itself is never destroyed). EquipmentInventory ONLY.
    /// </summary>
    public void DetachEquipment(EquipmentSlot slot)
    {
        if (equippedItems.Remove(slot))
        {
            CalculateCurrentStats();
        }
    }

    // ---------------------------------------------------------------------
    // Battle & persistence support.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates a battle-ready copy of this hero: the same template, level,
    /// XP, ascension, awakening, constellation, skill levels, and equipment
    /// loadout, with freshly calculated stats (equipment included) at full
    /// health. Battles mutate only the copy (health, cooldowns), so the
    /// roster's authoritative progression is never dirtied - the clone is
    /// re-derived from this instance at every battle start, making the
    /// roster the single source of truth.
    /// </summary>
    public HeroInstance CreateCombatClone()
    {
        HeroInstance clone = new HeroInstance(data);
        clone.displayName = displayName;
        clone.Level = Level;
        clone.Experience = Experience;
        clone.ascensionRank = ascensionRank;
        clone.awakeningRank = awakeningRank;
        clone.constellationRank = constellationRank;

        // Battle uses the hero's final calculated stats, so the clone wears
        // the same equipment (shared references: combat never mutates items,
        // and the stat recalculation below applies their bonuses).
        foreach (KeyValuePair<EquipmentSlot, EquipmentInstance> pair in equippedItems)
        {
            clone.equippedItems[pair.Key] = pair.Value;
        }
        foreach (KeyValuePair<SkillData, int> pair in skillLevels)
        {
            clone.skillLevels[pair.Key] = pair.Value;
        }

        clone.CalculateCurrentStats();
        clone.currentHealth = clone.maxHealth;
        return clone;
    }

    /// <summary>
    /// Restores progression from a save: ranks are clamped to their valid
    /// ranges, unknown skills keep level 1, level is clamped to the
    /// effective cap, and stats are recalculated at full health. Safe
    /// defaults (fresh hero) apply for missing fields.
    /// </summary>
    public void ApplySavedProgression(SavedHero saved)
    {
        if (saved == null)
        {
            return;
        }

        ascensionRank = Mathf.Clamp(saved.ascension, 0, HeroProgression.MaxAscensionRank);
        awakeningRank = Mathf.Clamp(saved.awakening, 0, awakeningSteps.Length);
        constellationRank = Mathf.Clamp(saved.constellation, 0, MaxConstellation);
        Level = Mathf.Clamp(saved.level, 1, MaxLevel);
        Experience = IsMaxLevel ? 0 : Mathf.Max(0, saved.experience);

        if (saved.skillIds != null && saved.skillLevels != null && skills != null)
        {
            for (int i = 0; i < saved.skillIds.Length && i < saved.skillLevels.Length; i++)
            {
                foreach (SkillData skill in skills)
                {
                    if (skill != null && skill.SkillId == saved.skillIds[i])
                    {
                        SetSkillLevel(skill, saved.skillLevels[i]);
                        break;
                    }
                }
            }
        }

        CalculateCurrentStats();
        currentHealth = maxHealth;
    }

    /// <summary>
    /// Development-only reset: wipes this hero back to a fresh level-1
    /// state (no XP, no ranks, no constellation, all skills level 1).
    /// </summary>
    public void ResetProgression()
    {
        Level = 1;
        Experience = 0;
        ascensionRank = 0;
        awakeningRank = 0;
        constellationRank = 0;
        skillLevels.Clear();
        CalculateCurrentStats();
        currentHealth = maxHealth;
    }
}
