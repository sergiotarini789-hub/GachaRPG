/// <summary>
/// Who a skill is aimed at when the battle resolves its turn.
/// The acting hero can always pick itself (Self); Ally never includes the
/// acting hero.
/// </summary>
public enum SkillTarget
{
    /// <summary>The opposing team's first living hero.</summary>
    Enemy,

    /// <summary>The acting hero.</summary>
    Self,

    /// <summary>The first living ally other than the acting hero.</summary>
    Ally,
}
