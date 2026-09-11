using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Runtime team of up to three <see cref="HeroInstance"/>s. Pure container:
/// holds only the instances added to it (never HeroData assets), and every
/// instance keeps its own health, skills, and cooldown state.
/// </summary>
public class Team
{
    /// <summary>Maximum number of heroes on one team.</summary>
    public const int MaxSize = 3;

    private readonly List<HeroInstance> members = new List<HeroInstance>();

    /// <summary>The heroes on this team, in slot order.</summary>
    public IReadOnlyList<HeroInstance> Members => members;

    public Team()
    {
    }

    /// <summary>Creates a team from the given heroes (up to <see cref="MaxSize"/>).</summary>
    public Team(params HeroInstance[] heroes)
    {
        foreach (HeroInstance hero in heroes)
        {
            Add(hero);
        }
    }

    /// <summary>
    /// Adds a hero to the team. Fails fast when the team is already full so
    /// setup mistakes surface immediately instead of skewing battles.
    /// </summary>
    public void Add(HeroInstance hero)
    {
        if (hero == null)
        {
            throw new ArgumentNullException(nameof(hero));
        }

        if (members.Count >= MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(hero), $"A team can hold at most {MaxSize} heroes.");
        }

        members.Add(hero);
    }

    /// <summary>Whether the hero is on this team.</summary>
    public bool Contains(HeroInstance hero)
    {
        return members.Contains(hero);
    }

    /// <summary>The first living member in slot order, or null if the team is wiped.</summary>
    public HeroInstance FirstAlive()
    {
        return members.FirstOrDefault(member => member.isAlive);
    }

    /// <summary>Whether at least one member is still alive.</summary>
    public bool HasLivingMembers()
    {
        return members.Any(member => member.isAlive);
    }

    /// <summary>All living members in slot order.</summary>
    public IEnumerable<HeroInstance> LivingMembers()
    {
        return members.Where(member => member.isAlive);
    }
}
