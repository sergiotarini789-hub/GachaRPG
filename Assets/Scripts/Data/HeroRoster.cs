using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What one duplicate of a hero resolved to when it was applied to the
/// roster: a brand-new C0 hero, a constellation rank advance (C0-C6), or -
/// for a hero already at the C6 cap - an extra duplicate converted into
/// Hero Tokens.
/// </summary>
public enum DuplicateResult
{
    /// <summary>The hero was not owned yet; a new HeroInstance joined the roster at C0.</summary>
    NewHero,

    /// <summary>The hero was owned; its constellation advanced by one rank (never past C6).</summary>
    ConstellationAdvanced,

    /// <summary>The hero was already at C6; the extra duplicate became Hero Tokens on the wallet.</summary>
    MaxConstellationExtra
}

/// <summary>
/// The player's owned heroes: the authoritative collection of live
/// <see cref="HeroInstance"/> progression state (level, experience,
/// constellation). Plain C# with no Unity dependencies, so any future
/// system - the collection UI, gacha, rewards, a save system - can own,
/// query, and extend it.
///
/// The roster is intentionally minimal: it owns instances in acquisition
/// order and nothing else. It never touches combat - battles keep building
/// their own ephemeral <see cref="HeroInstance"/>s from the same
/// <see cref="HeroData"/> templates, exactly as they always have - and it
/// holds no UI state of its own.
///
/// Duplicates of the same hero never create second roster entries:
/// <see cref="ApplyDuplicate"/> is the one authoritative path every
/// duplicate takes (summons, debug tools, consolidation), advancing the
/// owned instance's constellation C0-C6 and converting extra C6 duplicates
/// into Hero Tokens on the wallet.
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
    /// <see cref="HeroInstance"/> (for example from an initial grant) and
    /// hand it over; the roster never mutates it. Duplicate copies of the
    /// same <see cref="HeroData"/> are allowed for legacy saves - they are
    /// consolidated on load (see <see cref="ConsolidateDuplicates"/>).
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
    /// owned. One primary instance per template - duplicates advance the
    /// constellation instead of adding entries.
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
    /// The player's instance of the hero with the given stable
    /// <see cref="HeroData.HeroId"/>, or null. Used by systems that know a
    /// hero id without holding the template (equipment ownership, future
    /// rewards); with consolidated duplicates there is exactly one instance
    /// per id.
    /// </summary>
    public HeroInstance FindByHeroId(string heroId)
    {
        if (string.IsNullOrEmpty(heroId))
        {
            return null;
        }

        foreach (HeroInstance hero in heroes)
        {
            if (hero != null && hero.data != null && hero.data.HeroId == heroId)
            {
                return hero;
            }
        }

        return null;
    }

    /// <summary>
    /// THE authoritative duplicate path - summons, the Heroes screen's
    /// debug duplicate tool, and any future system all apply duplicates
    /// through here, so the constellation can never become inconsistent:
    /// - hero not owned: a brand-new HeroInstance joins the roster at C0;
    /// - owned below C6: the owned instance's constellation advances by 1;
    /// - owned at C6: the constellation stays C6 and the extra duplicate
    ///   converts into Hero Tokens on the wallet (the shop's currency).
    /// Returns exactly what the duplicate resolved to.
    /// </summary>
    public DuplicateResult ApplyDuplicate(HeroData template, PlayerWallet wallet)
    {
        HeroInstance owned = FindByData(template);
        if (owned == null)
        {
            Add(new HeroInstance(template));
            return DuplicateResult.NewHero;
        }

        return ApplyDuplicateToInstance(owned, wallet);
    }

    /// <summary>
    /// The shared duplicate rule for an already-owned instance: advance the
    /// constellation through <see cref="HeroInstance.ApplyDuplicate"/>, or
    /// convert the extra duplicate into Hero Tokens at the C6 cap. Used by
    /// <see cref="ApplyDuplicate"/> and <see cref="ConsolidateDuplicates"/>
    /// so the rule exists in exactly one place.
    /// </summary>
    private static DuplicateResult ApplyDuplicateToInstance(HeroInstance hero, PlayerWallet wallet)
    {
        if (hero.ApplyDuplicate())
        {
            return DuplicateResult.ConstellationAdvanced;
        }

        if (wallet != null)
        {
            wallet.AddHeroTokens(HeroProgression.MaxConstellationDuplicateTokenReward);
        }

        return DuplicateResult.MaxConstellationExtra;
    }

    /// <summary>
    /// Safe migration for rosters that still hold more than one instance
    /// of the same template (data created before duplicates consolidated):
    /// keeps one primary instance per template - the most progressed (the
    /// highest level, ties keep the earlier slot) - and applies every extra
    /// instance as a duplicate through the authoritative path (constellation
    /// +1, or a Hero Token when the primary is at C6), so no level, XP,
    /// ascension, awakening, or constellation is ever lost. Returns how many
    /// duplicates were converted.
    /// </summary>
    public int ConsolidateDuplicates(PlayerWallet wallet)
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
                ApplyDuplicateToInstance(primary, wallet);
                converted++;
            }
        }

        if (converted > 0)
        {
            Debug.Log("HeroRoster: consolidated " + converted + " duplicate hero(s) into constellation progress (Hero Tokens past C6).");
        }

        return converted;
    }
}
