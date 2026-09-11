using System;

/// <summary>
/// A hero's per-level progression curve. Pure data for a future leveling
/// system: nothing consumes it yet (combat reads <see cref="HeroData"/>'s
/// base stats only), but every hero carries its curve so progression can
/// be added later without re-authoring the roster.
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
