using System.Collections.Generic;

/// <summary>
/// The player's owned heroes: the authoritative collection of live
/// <see cref="HeroInstance"/> progression state (level, experience). Plain
/// C# with no Unity dependencies, so any future system - the collection UI,
/// gacha, rewards, a save system - can own, query, and extend it.
///
/// The roster is intentionally minimal: it owns instances in acquisition
/// order and nothing else. It never touches combat - battles keep building
/// their own ephemeral <see cref="HeroInstance"/>s from the same
/// <see cref="HeroData"/> templates, exactly as they always have - and it
/// holds no UI state of its own.
/// </summary>
public class HeroRoster
{
    private readonly List<HeroInstance> heroes = new List<HeroInstance>();

    /// <summary>The owned heroes, in acquisition order.</summary>
    public IReadOnlyList<HeroInstance> Heroes => heroes;

    /// <summary>How many heroes the player owns.</summary>
    public int Count => heroes.Count;

    /// <summary>
    /// Adds an owned hero to the roster. Callers instantiate the
    /// <see cref="HeroInstance"/> (for example from a gacha result or the
    /// initial grant) and hand it over; the roster never mutates it.
    /// Duplicate copies of the same <see cref="HeroData"/> are allowed -
    /// hero collectors can own more than one of the same hero.
    /// </summary>
    public void Add(HeroInstance hero)
    {
        if (hero != null)
        {
            heroes.Add(hero);
        }
    }
}
