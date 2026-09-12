using UnityEngine;

/// <summary>
/// Designer-authored definition of one equipment template (ScriptableObject)
/// - the equipment counterpart of <see cref="HeroData"/>. Everything an
/// item IS lives here as data: identity, slot, rarity, main stat and its
/// level scaling, and set membership. Player-owned state (level, rolled
/// substats, lock, owner) NEVER lives here - it lives on
/// <see cref="EquipmentInstance"/>, so two items built from the same
/// template are fully independent.
/// </summary>
[CreateAssetMenu(fileName = "NewEquipment", menuName = "GachaRPG/Equipment Data")]
public class EquipmentData : ScriptableObject
{
    /// <summary>
    /// Stable unique identifier for this template (e.g. "trainee_blade").
    /// Save data references templates by this id; keep it unique and never
    /// change it once published.
    /// </summary>
    public string equipmentId;

    /// <summary>Display name.</summary>
    public string equipmentName;

    /// <summary>The slot this item occupies; items are created into this slot only.</summary>
    public EquipmentSlot slot;

    /// <summary>The item's rarity: drives substat count/quality, sell value, and future drop tables.</summary>
    public HeroRarity rarity;

    /// <summary>The item's main stat type (must be legal for the slot - see <see cref="EquipmentProgression.IsMainStatAllowed"/>).</summary>
    public EquipmentStatType mainStatType;

    /// <summary>The main stat's value at equipment level 1.</summary>
    public int mainStatBaseValue;

    /// <summary>The main stat's linear per-level scaling: value = base + this x (level - 1).</summary>
    public float mainStatPerLevel;

    /// <summary>The equipment set this template belongs to (empty = no set).</summary>
    public string setId;

    /// <summary>
    /// The template's stable identity: the authored equipmentId when set,
    /// otherwise the display name (so quick assets work without edits).
    /// </summary>
    public string EquipmentId => string.IsNullOrEmpty(equipmentId) ? equipmentName : equipmentId;
}
