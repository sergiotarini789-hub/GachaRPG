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
/// Targeting: damage skills and basic attacks hit the first living enemy;
/// heal skills target the acting hero. Victory: a team wins when every
/// enemy hero is dead; a safety turn cap ends endless battles as a draw.
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
    /// its first ready skill (heals target self, other skills target the
    /// first living enemy) or falls back to a basic attack. Returns a human
    /// readable battle-event line for UI logs, or null when nothing happened.
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
        HeroInstance target = enemies.FirstAlive();
        if (target == null)
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

        if (readySkill != null && readySkill.type == SkillType.Heal)
        {
            // Heal skills target the acting hero for now.
            int healthBefore = actor.currentHealth;
            CombatManager.PerformSkill(actor, actor, readySkill);
            message =
                $"{actor.displayName} uses {readySkill.skillName} and recovers " +
                $"{actor.currentHealth - healthBefore} HP ({actor.currentHealth}/{actor.data.baseHealth})";
        }
        else if (readySkill != null && readySkill.type == SkillType.Buff)
        {
            // No buff system yet; announce it and move on.
            CombatManager.PerformSkill(actor, target, readySkill);
            message = $"{actor.displayName} uses {readySkill.skillName}";
        }
        else if (readySkill != null)
        {
            int healthBefore = target.currentHealth;
            CombatManager.PerformSkill(actor, target, readySkill);
            message =
                $"{actor.displayName} uses {readySkill.skillName} on {target.displayName} " +
                $"for {healthBefore - target.currentHealth} damage " +
                $"({target.displayName} HP: {target.currentHealth}/{target.data.baseHealth})";
        }
        else
        {
            int healthBefore = target.currentHealth;
            CombatManager.PerformAttack(actor, target);
            message =
                $"{actor.displayName} attacks {target.displayName} " +
                $"for {healthBefore - target.currentHealth} damage " +
                $"({target.displayName} HP: {target.currentHealth}/{target.data.baseHealth})";
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
