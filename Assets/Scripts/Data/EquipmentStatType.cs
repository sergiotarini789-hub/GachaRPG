/// <summary>
/// A stat an equipment item can carry. Values are append-only and stable
/// (saves store them as ints): existing entries must never be renumbered,
/// and new stats are added at the end only. Stats 0-3 are flat battle
/// stats (added to the hero's HP/ATK/DEF/SPD after the percentage
/// pipeline); stats 4-7 are percentage stats carried on the hero's final
/// stat sheet (CRIT_RATE/CRIT_DAMAGE are consumed by combat; ACCURACY and
/// RESISTANCE are persisted, displayed, and scored but await a
/// debuff/status system to affect combat - see EquipmentProgression).
/// </summary>
public enum EquipmentStatType
{
    /// <summary>Flat maximum health.</summary>
    HP = 0,

    /// <summary>Flat attack.</summary>
    ATK = 1,

    /// <summary>Flat defense.</summary>
    DEF = 2,

    /// <summary>Flat speed.</summary>
    SPD = 3,

    /// <summary>Critical hit chance, percent (equipment-granted; heroes have no innate crit).</summary>
    CRIT_RATE = 4,

    /// <summary>Bonus critical damage, percent on top of the base 150% crit multiplier.</summary>
    CRIT_DAMAGE = 5,

    /// <summary>Debuff-landing chance, percent (future status system; carried and scored now).</summary>
    ACCURACY = 6,

    /// <summary>Debuff-resist chance, percent (future status system; carried and scored now).</summary>
    RESISTANCE = 7,
}
