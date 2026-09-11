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
/// draw.
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
    /// Resolves the acting hero's turn: cooldowns tick, then the hero uses
    /// its first ready skill - target resolved from the skill's SkillTarget -
    /// or falls back to a basic attack on the first living enemy. Returns a
    /// human readable battle-event line for UI logs, or null when nothing
    /// happened.
    /// </summary>
    public string PerformTurn(HeroInstance actor)
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

        string message = null;
        SkillData readySkill = null;
        foreach (SkillData skill in actor.skills)
        {
            if (actor.IsSkillReady(skill))
            {
                readySkill = skill;
                break;
            }
        }

        if (readySkill != null)
        {
            // Resolve the skill's intended target to a concrete hero.
            HeroInstance skillTarget;
            switch (readySkill.target)
            {
                case SkillTarget.Self:
                    skillTarget = actor;
                    break;
                case SkillTarget.Ally:
                    skillTarget = allies.LivingMembers().FirstOrDefault(member => !ReferenceEquals(member, actor));
                    break;
                default: // SkillTarget.Enemy
                    skillTarget = enemyTarget;
                    break;
            }

            if (skillTarget == null)
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

            int healthBefore = skillTarget.currentHealth;
            CombatManager.PerformSkill(actor, skillTarget, readySkill);

            if (readySkill.type == SkillType.Heal)
            {
                int healed = skillTarget.currentHealth - healthBefore;
                message = ReferenceEquals(skillTarget, actor)
                    ? $"{actor.displayName} uses {readySkill.skillName} and recovers {healed} HP " +
                      $"({skillTarget.currentHealth}/{skillTarget.data.baseHealth})"
                    : $"{actor.displayName} uses {readySkill.skillName} on {skillTarget.displayName} and recovers {healed} HP " +
                      $"({skillTarget.displayName} HP: {skillTarget.currentHealth}/{skillTarget.data.baseHealth})";
            }
            else if (readySkill.type == SkillType.Buff)
            {
                message = $"{actor.displayName} uses {readySkill.skillName}";
            }
            else
            {
                message =
                    $"{actor.displayName} uses {readySkill.skillName} on {skillTarget.displayName} " +
                    $"for {healthBefore - skillTarget.currentHealth} damage " +
                    $"({skillTarget.displayName} HP: {skillTarget.currentHealth}/{skillTarget.data.baseHealth})";
            }
        }
        else
        {
            int healthBefore = enemyTarget.currentHealth;
            CombatManager.PerformAttack(actor, enemyTarget);
            message =
                $"{actor.displayName} attacks {enemyTarget.displayName} " +
                $"for {healthBefore - enemyTarget.currentHealth} damage " +
                $"({enemyTarget.displayName} HP: {enemyTarget.currentHealth}/{enemyTarget.data.baseHealth})";
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
