using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates equipment instances from definitions - the single acquisition
/// path every source (debug tools, future drops/summons/crafting) uses:
/// <see cref="GenerateEquipment"/> produces a persistent item for a
/// requested slot and rarity, <see cref="GenerateRandomEquipment"/> rolls
/// both. All items are created through the owning
/// <see cref="EquipmentInventory"/>, so instance ids are unique, secondary
/// stats roll deterministically once at creation (never re-rolled on
/// display), and the item is immediately owned and persistable.
///
/// Definition selection is data-driven: definitions whose slot AND typical
/// rarity match are preferred; when a slot has no definition for the
/// requested rarity, any definition of that slot is used (base stats scale
/// through the rarity multiplier, so the generated item is still a legal,
/// correctly-powered item of the requested rarity).
/// </summary>
public static class EquipmentFactory
{
    /// <summary>
    /// Generates one item of the requested slot and rarity into the
    /// inventory. Returns the new persistent instance, or null when no
    /// definition for that slot exists / the request was invalid.
    /// </summary>
    public static EquipmentInstance GenerateEquipment(
        EquipmentInventory inventory,
        EquipmentSlot slot,
        HeroRarity rarity,
        IReadOnlyList<EquipmentData> catalog)
    {
        EquipmentData template = PickDefinition(slot, rarity, catalog);
        if (template == null || inventory == null)
        {
            return null;
        }

        return inventory.CreateEquipment(template, rarity);
    }

    /// <summary>
    /// Generates one item of a random slot and rarity (deterministically
    /// rolled from the seed) into the inventory. Returns the new persistent
    /// instance, or null when the catalog has no definitions.
    /// </summary>
    public static EquipmentInstance GenerateRandomEquipment(
        EquipmentInventory inventory,
        IReadOnlyList<EquipmentData> catalog,
        string seed)
    {
        List<EquipmentData> definitions = UsableDefinitions(catalog);
        if (definitions.Count == 0 || inventory == null)
        {
            return null;
        }

        EquipmentSlot slot = (EquipmentSlot)EquipmentProgression.DeterministicRoll(seed + ":slot", 6);
        HeroRarity rarity = RollRarity(seed);
        EquipmentData template = PickDefinition(slot, rarity, catalog);
        if (template == null)
        {
            // Defensive: no definition for the rolled slot at all (empty
            // catalog) - nothing to generate from.
            return null;
        }

        return inventory.CreateEquipment(template, rarity);
    }

    /// <summary>
    /// Picks the definition to generate from for the requested slot and
    /// rarity: prefers definitions whose typical rarity matches; otherwise
    /// any definition of the slot (deterministically chosen). Returns null
    /// when the slot has no definitions.
    /// </summary>
    public static EquipmentData PickDefinition(EquipmentSlot slot, HeroRarity rarity, IReadOnlyList<EquipmentData> catalog)
    {
        List<EquipmentData> exact = new List<EquipmentData>();
        List<EquipmentData> any = new List<EquipmentData>();
        foreach (EquipmentData template in UsableDefinitions(catalog))
        {
            if (template.slot != slot)
            {
                continue;
            }

            any.Add(template);
            if (template.rarity == rarity)
            {
                exact.Add(template);
            }
        }

        if (exact.Count > 0)
        {
            return exact[EquipmentProgression.DeterministicRoll(slot + ":" + rarity + ":pick", exact.Count)];
        }

        if (any.Count > 0)
        {
            return any[EquipmentProgression.DeterministicRoll(slot + ":any:pick", any.Count)];
        }

        return null;
    }

    /// <summary>
    /// Rolls a rarity from the prototype's weighted table (Common 60%,
    /// Rare 30%, Epic 9%, Legendary 1%) - deterministic for the seed.
    /// </summary>
    public static HeroRarity RollRarity(string seed)
    {
        int roll = EquipmentProgression.DeterministicRoll(seed + ":rarity", 100);
        if (roll < 60)
        {
            return HeroRarity.Common;
        }

        if (roll < 90)
        {
            return HeroRarity.Rare;
        }

        if (roll < 99)
        {
            return HeroRarity.Epic;
        }

        return HeroRarity.Legendary;
    }

    /// <summary>The catalog's non-null definitions.</summary>
    private static List<EquipmentData> UsableDefinitions(IReadOnlyList<EquipmentData> catalog)
    {
        List<EquipmentData> definitions = new List<EquipmentData>();
        if (catalog != null)
        {
            foreach (EquipmentData template in catalog)
            {
                if (template != null)
                {
                    definitions.Add(template);
                }
            }
        }

        return definitions;
    }
}
