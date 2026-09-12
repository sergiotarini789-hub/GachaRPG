using System.Collections.Generic;
using UnityEngine;

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

    /// <summary>
    /// The player's instance of a template, or null when the hero is not
    /// owned. With duplicate-to-souls summoning there is normally exactly
    /// one primary instance per template.
    /// </summary>
    public HeroInstance FindByData(HeroData data)
    {
        if (data == null)
        {
            return null;
        }

        foreach (HeroInstance hero in heroes)
        {
            if (hero != null && hero.data == data)
            {
                return hero;
            }
        }

        return null;
    }

    /// <summary>Whether the player owns at least one instance of the template.</summary>
    public bool Owns(HeroData data)
    {
        return FindByData(data) != null;
    }

    /// <summary>
    /// Safe migration for rosters created before duplicates became souls:
    /// keeps one primary instance per template - the most progressed (the
    /// highest level, ties keep the earlier slot) - and converts every
    /// extra instance into Hero Souls on that primary, so no level, XP,
    /// ascension, or awakening is ever lost. Returns how many duplicates
    /// were converted.
    /// </summary>
    public int ConsolidateDuplicates()
    {
        int converted = 0;
        for (int i = 0; i < heroes.Count; i++)
        {
            HeroInstance primary = heroes[i];
            if (primary == null)
            {
                continue;
            }

            for (int j = heroes.Count - 1; j > i; j--)
            {
                HeroInstance other = heroes[j];
                if (other == null || other.data != primary.data)
                {
                    continue;
                }

                // Keep the most progressed instance as the primary.
                if (other.Level > primary.Level)
                {
                    heroes[i] = other;
                    heroes[j] = primary;
                    primary = other;
                }

                heroes.RemoveAt(j);
                primary.AddSouls(HeroProgression.SoulsPerDuplicate);
                converted++;
            }
        }

        if (converted > 0)
        {
            Debug.Log("HeroRoster: consolidated " + converted + " duplicate hero(s) into Hero Souls.");
        }

        return converted;
    }
}
