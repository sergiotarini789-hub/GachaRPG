using UnityEngine;

/// <summary>
/// Designer-authored definition of one equipment concept
/// (ScriptableObject template) - the equipment counterpart of
/// <see cref="HeroData"/>. Everything an item IS lives here as data:
/// identity, slot, typical rarity, primary stat, stat curve, set, art.
/// Base values are authored at a neutral scale: the generated item's
/// actual primary stat is the definition's curve scaled by the generated
/// rarity's multiplier (<see cref="EquipmentProgression.BaseStatMultiplierByRarity"/>),
/// so the same definition can produce any rarity.
///
/// Runtime player state (level, rolled secondary stats, owner) lives on
/// <see cref="EquipmentInstance"/>, NEVER here - two items generated from
/// the same definition are fully independent instances.
/// </summary>
[CreateAssetMenu(fileName = "NewEquipment", menuName = "GachaRPG/Equipment Data")]
public class EquipmentData : ScriptableObject
{
    /// <summary>
    /// Stable unique identifier for this definition (e.g. "tempest_fang").
    /// Saves reference definitions by this id; keep it unique and never
    /// change it once published.
    /// </summary>
    public string equipmentId;

    /// <summary>Display name shown in the inventory, details panel, and hero loadout.</summary>
    public string equipmentName;

    /// <summary>The slot this definition's items occupy.</summary>
    public EquipmentSlot slot;

    /// <summary>
    /// The typical rarity items of this definition are generated at (the
    /// factory can still generate other rarities from it - the item's
    /// actual rarity is captured on the instance).
    /// </summary>
    public HeroRarity rarity;

    /// <summary>
    /// The primary stat's type; must be legal for the slot
    /// (<see cref="EquipmentProgression.IsPrimaryStatAllowed"/>).
    /// </summary>
    public EquipmentStatType mainStatType;

    /// <summary>Primary stat base value at level 1, before the rarity multiplier.</summary>
    public int mainStatBaseValue;

    /// <summary>Primary stat growth per level above 1, before the rarity multiplier.</summary>
    public float mainStatPerLevel;

    /// <summary>The set this definition belongs to, by stable id (empty = no set).</summary>
    public string setId;

    /// <summary>Icon / art reference. The data slot exists now; art comes later (UI falls back to a rarity-colored initial).</summary>
    public Sprite icon;

    /// <summary>Flavor text for tooltips and the details panel.</summary>
    [TextArea]
    public string description;

    /// <summary>
    /// The definition's stable identity used by saves and lookups: the
    /// authored equipmentId when set, otherwise the display name (so
    /// existing assets work without edits).
    /// </summary>
    public string EquipmentId => string.IsNullOrEmpty(equipmentId) ? equipmentName : equipmentId;
}
