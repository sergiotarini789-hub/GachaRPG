using System;

/// <summary>
/// A hero's per-level progression curve, authored per hero on its
/// <see cref="HeroData"/> asset. Consumed by
/// <see cref="HeroInstance.CalculateCurrentStats"/> to derive a hero's
/// stats at its current level; at level 1 the base stats apply as-is.
/// </summary>
[Serializable]
public class HeroGrowth
{
    /// <summary>Maximum level this hero can reach.</summary>
    public int maxLevel = 50;

    /// <summary>Health gained per level.</summary>
    public float healthPerLevel = 1f;

    /// <summary>Attack gained per level.</summary>
    public float attackPerLevel = 1f;

    /// <summary>Defense gained per level.</summary>
    public float defensePerLevel = 1f;

    /// <summary>Speed gained per level.</summary>
    public float speedPerLevel = 0.1f;
}
