/// <summary>
/// The six equipment slots every hero has. One item per slot; an item's
/// legal slot comes from its <see cref="EquipmentData"/> template (stored
/// on the instance). The explicit values are stable save-format indices -
/// append new slots at the end, never renumber.
/// </summary>
public enum EquipmentSlot
{
    Weapon = 0,

    Helmet = 1,

    Armor = 2,

    Gloves = 3,

    Boots = 4,

    Accessory = 5
}
