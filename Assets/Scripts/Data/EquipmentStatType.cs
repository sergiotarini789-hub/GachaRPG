/// <summary>
/// The stat types equipment can modify (main stats, substats, and set
/// bonuses all use this). Extensible: new types append at the end and only
/// need matching entries in the per-stat config arrays - never renumber,
/// the values persist in saves and index configuration arrays.
/// </summary>
public enum EquipmentStatType
{
    HP = 0,

    ATK = 1,

    DEF = 2,

    SPD = 3
}
