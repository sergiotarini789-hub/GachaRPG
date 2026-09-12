using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Deterministic team battle between two <see cref="Team"/>s (built for 3v3,
/// works with fewer members). Plain C# so it is fully testable.
///
/// Turn selection (simple speed order, no real-time speed bar):
/// at the start of each round every living hero from both teams is lined up
/// by currentSpeed, highest first (ties keep team/slot order). They act in
/// that order; heroes who die mid-round are skipped and never act again.
/// When the round ends a new one is built from the survivors.
///
/// Targeting: the acting hero's first ready skill is preferred over the
/// basic attack, and its <see cref="SkillData.target"/> is resolved to a
/// concrete hero - Enemy: first living enemy; Self: the acting hero; Ally:
/// the first living ally other than the actor (if none exists the skill
/// fails safely: no effect, no cooldown consumed, turn spent). Basic
/// attacks always hit the first living enemy. Victory: a team wins when
/// every enemy hero is dead; a safety turn cap ends endless battles as a
/// draw. The PerformTurn overload injects a player-chosen action (manual
/// mode): it is validated against these same rules - invalid choices fall
/// back to the automatic behavior - and runs through the identical
/// pipeline, so there is only ever one combat engine.
/// </summary>
public class TeamBattle
{
    /// <summary>Hard cap on resolved turns so a stalemate can never loop forever.</summary>
    public const int MaxTurns = 100;

    private readonly Team team1;
    private readonly Team team2;

    /// <summary>Acting order for the current round; consumed from the front.</summary>
    private readonly List<HeroInstance> roundOrder = new List<HeroInstance>();

    private int turnsTaken;

    /// <summary>Whether the battle has finished (a team won or the turn cap was hit).</summary>
    public bool BattleOver { get; private set; }

    /// <summary>The winning team, or null when the battle ended in a draw.</summary>
    public Team Winner { get; private set; }

    /// <summary>How many turns have been resolved so far.</summary>
    public int TurnsTaken => turnsTaken;

    public TeamBattle(Team team1, Team team2)
    {
        this.team1 = team1;
        this.team2 = team2;
    }

    /// <summary>
    /// Picks the next acting hero: the fastest living hero that has not acted
    /// this round, skipping heroes who died earlier in the round. Returns
    /// null once the battle is over (or nobody is alive).
    /// </summary>
    public HeroInstance GetNextActor()
    {
        if (BattleOver)
        {
            return null;
        }

        SkipDeadFromRound();
        if (roundOrder.Count == 0)
        {
            BuildRound();
            SkipDeadFromRound();
        }

        if (roundOrder.Count == 0)
        {
            return null;
        }

        HeroInstance actor = roundOrder[0];
        roundOrder.RemoveAt(0);
        return actor;
    }

    /// <summary>
    /// Resolves the acting hero's turn automatically: cooldowns tick, then
    /// the hero uses its first ready skill - target resolved from the
    /// skill's SkillTarget - or falls back to a basic attack on the first
    /// living enemy. Returns a human readable battle-event line for UI
    /// logs, or null when nothing happened.
    /// </summary>
    public string PerformTurn(HeroInstance actor)
    {
        return ResolveTurn(actor, null, null, playerChosen: false);
    }

    /// <summary>
    /// Resolves the acting hero's turn with the player's chosen action:
    /// the chosen skill (null = basic attack) and target are validated and
    /// then run through the exact same pipeline as the automatic turn -
    /// damage, healing, cooldowns, turn counting, and victory checks are
    /// shared, never duplicated. An invalid choice (skill not owned or
    /// cooling down, target dead or on the wrong side) safely falls back
    /// to the automatic behavior, and a null target lets the engine
    /// resolve Self/Ally skills itself. Returns the same battle-event
    /// line format as <see cref="PerformTurn(HeroInstance)"/>.
    /// </summary>
    public string PerformTurn(HeroInstance actor, SkillData chosenSkill, HeroInstance chosenTarget)
    {
        return ResolveTurn(actor, chosenSkill, chosenTarget, playerChosen: true);
    }

    /// <summary>
    /// The single turn pipeline both entry points share: guards, cooldown
    /// tick, action choice (the player's when valid, otherwise the
    /// automatic first-ready-skill / basic-attack rule), execution through
    /// <see cref="CombatManager"/>, the battle-event message, turn
    /// counting, and victory checks.
    /// </summary>
    private string ResolveTurn(HeroInstance actor, SkillData chosenSkill, HeroInstance chosenTarget, bool playerChosen)
    {
        if (BattleOver || actor == null || !actor.isAlive)
        {
            return null;
        }

        // Cooldowns recover at the start of the acting hero's turn.
        actor.TickCooldowns();

        Team allies = team1.Contains(actor) ? team1 : team2;
        Team enemies = ReferenceEquals(allies, team1) ? team2 : team1;
        HeroInstance enemyTarget = enemies.FirstAlive();
        if (enemyTarget == null)
        {
            // Nothing left to fight; treat as an ended battle.
            Finish(allies);
            return null;
        }

        SkillData readySkill;
        HeroInstance actionTarget;
        if (playerChosen && IsValidChoice(actor, allies, enemies, chosenSkill, chosenTarget))
        {
            // Player's decision: the chosen skill (null = basic attack)
            // aimed at the chosen target; skills submitted without an
            // explicit target keep the engine's own target resolution.
            readySkill = chosenSkill;
            actionTarget = chosenSkill == null
                ? chosenTarget
                : chosenTarget ?? ResolveSkillTarget(actor, allies, chosenSkill, enemyTarget);
        }
        else
        {
            // Automatic decision: first ready skill, else basic attack.
            readySkill = FirstReadySkill(actor);
            actionTarget = readySkill == null ? enemyTarget : ResolveSkillTarget(actor, allies, readySkill, enemyTarget);
        }

        if (readySkill != null && actionTarget == null)
        {
            // Fail safely: the skill fizzles - no effect, no cooldown
            // consumed - but the turn is spent so the battle moves on.
            turnsTaken++;
            if (turnsTaken >= MaxTurns)
            {
                BattleOver = true;
                Winner = null;
            }

            return $"{actor.displayName} finds no valid target for {readySkill.skillName}";
        }

        string message = null;
        if (readySkill != null)
        {
            int healthBefore = actionTarget.currentHealth;
            CombatManager.PerformSkill(actor, actionTarget, readySkill);

            if (readySkill.type == SkillType.Heal)
            {
                int healed = actionTarget.currentHealth - healthBefore;
                message = ReferenceEquals(actionTarget, actor)
                    ? $"{actor.displayName} uses {readySkill.skillName} and recovers {healed} HP " +
                      $"({actionTarget.currentHealth}/{actionTarget.maxHealth})"
                    : $"{actor.displayName} uses {readySkill.skillName} on {actionTarget.displayName} and recovers {healed} HP " +
                      $"({actionTarget.displayName} HP: {actionTarget.currentHealth}/{actionTarget.maxHealth})";
            }
            else if (readySkill.type == SkillType.Buff)
            {
                message = $"{actor.displayName} uses {readySkill.skillName}";
            }
            else
            {
                message =
                    $"{actor.displayName} uses {readySkill.skillName} on {actionTarget.displayName} " +
                    $"for {healthBefore - actionTarget.currentHealth} damage " +
                    $"({actionTarget.displayName} HP: {actionTarget.currentHealth}/{actionTarget.maxHealth})";
            }
        }
        else
        {
            int healthBefore = actionTarget.currentHealth;
            CombatManager.PerformAttack(actor, actionTarget);
            message =
                $"{actor.displayName} attacks {actionTarget.displayName} " +
                $"for {healthBefore - actionTarget.currentHealth} damage " +
                $"({actionTarget.displayName} HP: {actionTarget.currentHealth}/{actionTarget.maxHealth})";
        }

        turnsTaken++;

        if (!enemies.HasLivingMembers())
        {
            Finish(allies);
        }
        else if (!allies.HasLivingMembers())
        {
            Finish(enemies);
        }
        else if (turnsTaken >= MaxTurns)
        {
            BattleOver = true;
            Winner = null;
        }

        return message;
    }

    /// <summary>The acting hero's first ready skill, or null when only the basic attack is available.</summary>
    private static SkillData FirstReadySkill(HeroInstance actor)
    {
        foreach (SkillData skill in actor.skills)
        {
            if (actor.IsSkillReady(skill))
            {
                return skill;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a skill's intended target to a concrete hero: Self is the
    /// acting hero, Ally the first living ally other than the actor, Enemy
    /// the given default enemy. Returns null when no valid target exists.
    /// </summary>
    private static HeroInstance ResolveSkillTarget(HeroInstance actor, Team allies, SkillData skill, HeroInstance defaultEnemy)
    {
        switch (skill.target)
        {
            case SkillTarget.Self:
                return actor;
            case SkillTarget.Ally:
                return allies.LivingMembers().FirstOrDefault(member => !ReferenceEquals(member, actor));
            default: // SkillTarget.Enemy
                return defaultEnemy;
        }
    }

    /// <summary>
    /// Whether a player-submitted action can run this turn: the skill must
    /// belong to the acting hero and be off cooldown (null means the basic
    /// attack), and the target - when given - must be a living hero the
    /// action can legally affect (an enemy for basic attacks and
    /// enemy-targeted skills, the actor itself for Self skills, an ally
    /// other than the actor for Ally skills). A null target is only valid
    /// for skills whose target the engine resolves itself (Self / Ally).
    /// </summary>
    private bool IsValidChoice(HeroInstance actor, Team allies, Team enemies, SkillData skill, HeroInstance target)
    {
        if (skill != null && (!actor.skills.Contains(skill) || !actor.IsSkillReady(skill)))
        {
            return false;
        }

        if (target == null)
        {
            return skill != null && skill.target != SkillTarget.Enemy;
        }

        if (!target.isAlive)
        {
            return false;
        }

        if (skill == null)
        {
            return enemies.Contains(target);
        }

        switch (skill.target)
        {
            case SkillTarget.Self:
                return ReferenceEquals(target, actor);
            case SkillTarget.Ally:
                return allies.Contains(target) && !ReferenceEquals(target, actor);
            default: // SkillTarget.Enemy
                return enemies.Contains(target);
        }
    }

    /// <summary>Drops heroes from the round queue who died before their turn came up.</summary>
    private void SkipDeadFromRound()
    {
        while (roundOrder.Count > 0 && !roundOrder[0].isAlive)
        {
            roundOrder.RemoveAt(0);
        }
    }

    /// <summary>
    /// Lines up all living heroes for a new round: fastest first, with ties
    /// resolved deterministically by team 1 slots before team 2 slots.
    /// OrderByDescending is a stable sort, so equal speeds keep this order.
    /// </summary>
    private void BuildRound()
    {
        roundOrder.Clear();
        roundOrder.AddRange(team1.LivingMembers());
        roundOrder.AddRange(team2.LivingMembers());

        List<HeroInstance> ordered = roundOrder.OrderByDescending(hero => hero.currentSpeed).ToList();
        roundOrder.Clear();
        roundOrder.AddRange(ordered);
    }

    private void Finish(Team winner)
    {
        BattleOver = true;
        Winner = winner;
    }
}
