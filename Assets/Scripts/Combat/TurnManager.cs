using System.Collections.Generic;

/// <summary>
/// Plain C# class that manages turn order for a battle using a speed-bar
/// ("turn gauge") system, similar to gacha RPGs such as Raid: Shadow Legends.
///
/// Speed-bar logic:
/// On every tick, each alive unit's gauge grows by its current speed, so faster
/// units fill their bar sooner and act more often. The first unit whose gauge
/// reaches <see cref="TURN_THRESHOLD"/> takes the turn, and only the threshold
/// amount is subtracted from its gauge. The leftover overflow carries over into
/// the next cycle, which keeps cumulative turn distribution fair (a slightly
/// faster unit earns proportionally more turns instead of trading turns 1:1).
/// </summary>
public class TurnManager
{
    private List<HeroInstance> allUnits;

    /// <summary>Accumulated turn meter per unit. A unit may act once its gauge reaches <see cref="TURN_THRESHOLD"/>.</summary>
    private Dictionary<HeroInstance, float> turnGauge;

    /// <summary>Gauge value a unit must accumulate to earn a turn.</summary>
    private const float TURN_THRESHOLD = 1000f;

    /// <summary>
    /// Creates a turn manager for the given units and starts every unit's gauge at zero.
    /// </summary>
    /// <param name="units">All units participating in the battle.</param>
    public TurnManager(List<HeroInstance> units)
    {
        allUnits = units;
        turnGauge = new Dictionary<HeroInstance, float>();
        foreach (HeroInstance unit in units)
        {
            turnGauge[unit] = 0f;
        }
    }

    /// <summary>
    /// Advances the speed bar until a unit earns a turn.
    /// Returns immediately without ticking when fewer than two units are alive,
    /// and returns <c>null</c> when no units remain alive.
    /// </summary>
    /// <returns>
    /// The unit that filled its gauge (its gauge is reduced by <see cref="TURN_THRESHOLD"/>
    /// so overflow is preserved), the last unit standing, or <c>null</c> if everyone is dead.
    /// </returns>
    public HeroInstance GetNextTurn()
    {
        HeroInstance lastAlive = null;
        int aliveCount = 0;
        foreach (HeroInstance unit in allUnits)
        {
            if (unit.isAlive)
            {
                aliveCount++;
                lastAlive = unit;
            }
        }

        // Battle over: nobody can take a turn.
        if (aliveCount == 0)
        {
            return null;
        }

        // Last unit standing: it acts immediately, no ticking needed.
        if (aliveCount == 1)
        {
            return lastAlive;
        }

        // Tick the speed bar until a unit can act.
        while (true)
        {
            // Each tick, EVERY alive unit's gauge grows by its current speed.
            // Incrementing everyone before checking keeps turn distribution
            // proportional to speed (a unit earlier in the list crossing the
            // threshold does not rob later units of their tick).
            foreach (HeroInstance unit in allUnits)
            {
                // Dead units never gain gauge and are skipped entirely.
                if (!unit.isAlive)
                {
                    continue;
                }

                turnGauge[unit] += unit.currentSpeed;
            }

            // The first unit (in list order) that reached the threshold acts.
            foreach (HeroInstance unit in allUnits)
            {
                if (!unit.isAlive)
                {
                    continue;
                }

                if (turnGauge[unit] >= TURN_THRESHOLD)
                {
                    // Subtract (rather than reset to zero) so any overflow speed
                    // carries over, keeping turn order fair over many cycles.
                    turnGauge[unit] -= TURN_THRESHOLD;
                    return unit;
                }
            }
        }
    }
}
