using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene test component: attach to a GameObject, assign two HeroData assets in
/// the Inspector, and press Play. Builds a HeroInstance for each, lets a
/// <see cref="TurnManager"/> decide turn order on the speed bar, and has each
/// acting unit attack the other until one falls (or the safety turn limit is
/// reached).
/// </summary>
public class BattleTestRunner : MonoBehaviour
{
    [Tooltip("First combatant's HeroData asset.")]
    public HeroData hero1Data;

    [Tooltip("Second combatant's HeroData asset.")]
    public HeroData hero2Data;

    /// <summary>Hard cap on loop iterations so a battle can never hang Play mode.</summary>
    private const int MaxTurns = 100;

    private void Start()
    {
        RunBattle();
    }

    /// <summary>
    /// Runs a full battle to completion and logs the winner. Public so the
    /// battle can also be triggered from other scripts or tests.
    /// </summary>
    public void RunBattle()
    {
        var hero1 = new HeroInstance(hero1Data);
        var hero2 = new HeroInstance(hero2Data);

        var units = new List<HeroInstance> { hero1, hero2 };
        var turnManager = new TurnManager(units);

        int turnCount = 0;
        while (hero1.isAlive && hero2.isAlive && turnCount < MaxTurns)
        {
            HeroInstance actor = turnManager.GetNextTurn();
            HeroInstance target = ReferenceEquals(actor, hero1) ? hero2 : hero1;

            CombatManager.PerformAttack(actor, target);
            turnCount++;
        }

        if (!hero1.isAlive && !hero2.isAlive)
        {
            Debug.LogWarning("Both heroes are down - the battle ends in a draw.");
            return;
        }

        if (hero1.isAlive && hero2.isAlive)
        {
            Debug.LogWarning($"Battle hit the {MaxTurns}-turn safety limit and ended in a draw.");
            return;
        }

        HeroInstance winner = hero1.isAlive ? hero1 : hero2;
        Debug.Log($"{winner.data.heroName} wins the battle!");
    }
}
