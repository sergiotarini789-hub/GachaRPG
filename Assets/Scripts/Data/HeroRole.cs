/// <summary>
/// A hero's combat role - its job in battle, independent of identity,
/// faction, or rarity. Roles are descriptive metadata for team building:
/// the combat engine never branches on them, and future heroes may blur
/// or extend these categories.
/// </summary>
public enum HeroRole
{
    /// <summary>Focuses on dealing damage; usually fragile but hard-hitting.</summary>
    Attack,

    /// <summary>Focuses on protecting the team and absorbing enemy focus.</summary>
    Defense,

    /// <summary>Focuses on sustaining and enabling allies.</summary>
    Support,
}
