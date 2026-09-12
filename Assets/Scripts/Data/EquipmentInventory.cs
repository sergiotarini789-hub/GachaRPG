using System.Collections.Generic;
using UnityEngine;

/// <summary>Outcome of an equip request through the inventory's single equip path.</summary>
public enum EquipResult
{
    /// <summary>The item is equipped to the hero (a previous item, if any, was returned to the inventory pool).</summary>
    Equipped,

    /// <summary>The item is not in this inventory.</summary>
    ItemNotInInventory,

    /// <summary>The item cannot occupy the hero's slot for it (defensive - slot checks normally happen earlier).</summary>
    SlotMismatch,

    /// <summary>The request itself was invalid (null hero/item).</summary>
    InvalidRequest
}

/// <summary>
/// The player's owned equipment: every <see cref="EquipmentInstance"/> the
/// player owns plus the authoritative equip-state bookkeeping - the
/// equipment counterpart of <see cref="HeroRoster"/>. Equipment belongs to
/// the player account, never to a hero: heroes are only lent items through
/// slots, and removing or resetting a hero never destroys equipment.
///
/// Equip/unequip flows exclusively through <see cref="Equip"/> /
/// <see cref="Unequip"/> - the single transactional path that enforces one
/// item per slot, one owner per item, and correct handling of whatever item
/// previously occupied the slot (it simply returns to the unequipped pool;
/// inventory entries are never created or destroyed by equipping). The
/// persisted equip-state truth is each item's
/// <see cref="EquipmentInstance.equippedHeroId"/>; the heroes' slot maps
/// are the runtime cache the stat pipeline reads, kept in sync only by
/// this class.
/// </summary>
public class EquipmentInventory
{
    /// <summary>All owned items, in acquisition order.</summary>
    private readonly List<EquipmentInstance> items = new List<EquipmentInstance>();

    /// <summary>The roster, so equips can resolve and move items between heroes by hero id.</summary>
    private readonly HeroRoster roster;

    /// <summary>The next instance-id counter (persisted with the profile so ids stay unique across restarts).</summary>
    private int nextInstanceId;

    /// <summary>Creates an empty inventory bound to the player's roster.</summary>
    public EquipmentInventory(HeroRoster roster)
    {
        this.roster = roster;
    }

    /// <summary>All owned items, in acquisition order.</summary>
    public IReadOnlyList<EquipmentInstance> Items => items;

    /// <summary>How many items the player owns (equipped or not).</summary>
    public int Count => items.Count;

    // ---------------------------------------------------------------------
    // Ownership (adding / finding / removing)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Creates a brand-new item from the definition at the definition's own
    /// rarity, assigns it the next unique instance id, rolls its secondary
    /// stats, and adds it to the inventory. Returns null for a null
    /// definition.
    /// </summary>
    public EquipmentInstance CreateEquipment(EquipmentData template)
    {
        return CreateEquipment(template, template != null ? template.rarity : HeroRarity.Common);
    }

    /// <summary>
    /// Creates a brand-new item from the definition at the given rarity
    /// (the factory's path - rarity may differ from the definition's
    /// typical one), assigns the next unique instance id, rolls secondary
    /// stats, and adds it. Returns null for a null definition.
    /// </summary>
    public EquipmentInstance CreateEquipment(EquipmentData template, HeroRarity rarity)
    {
        if (template == null)
        {
            return null;
        }

        nextInstanceId++;
        EquipmentInstance item = new EquipmentInstance(template, "eq-" + nextInstanceId, rarity);
        items.Add(item);
        return item;
    }

    /// <summary>
    /// Adds an existing item instance (save/load restore). Rejects null
    /// items and ids that would collide with an owned item - instance ids
    /// are unique by construction.
    /// </summary>
    public bool Add(EquipmentInstance item)
    {
        if (item == null || string.IsNullOrEmpty(item.instanceId) || FindById(item.instanceId) != null)
        {
            return false;
        }

        items.Add(item);
        return true;
    }

    /// <summary>The owned item with the given instance id, or null.</summary>
    public EquipmentInstance FindById(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return null;
        }

        foreach (EquipmentInstance item in items)
        {
            if (item != null && item.instanceId == instanceId)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// Removes (destroys) an item from the inventory. Refuses - changing
    /// nothing - while the item is equipped or locked, so equipped or
    /// locked gear can never be lost accidentally. Sell flows through here
    /// (via <see cref="Sell"/>) after the equipped check.
    /// </summary>
    public bool RemoveEquipment(string instanceId)
    {
        EquipmentInstance item = FindById(instanceId);
        if (item == null || item.locked || IsEquipped(instanceId))
        {
            return false;
        }

        items.Remove(item);
        return true;
    }

    /// <summary>
    /// Sells the item: awards its sell value to the wallet and removes it
    /// from the inventory. Refuses - changing nothing - while the item is
    /// equipped (unequip first) or locked. Gold never goes negative (the
    /// value is always positive by formula).
    /// </summary>
    public bool Sell(string instanceId, PlayerWallet wallet)
    {
        EquipmentInstance item = FindById(instanceId);
        if (item == null || item.locked || IsEquipped(instanceId) || wallet == null)
        {
            return false;
        }

        int value = item.SellValue;
        items.Remove(item);
        wallet.AddGold(value);
        return true;
    }

    // ---------------------------------------------------------------------
    // Equip-state queries (read the item's equippedHeroId - the truth)
    // ---------------------------------------------------------------------

    /// <summary>Whether some hero currently wears the item with the given instance id.</summary>
    public bool IsEquipped(string instanceId)
    {
        EquipmentInstance item = FindById(instanceId);
        return item != null && !string.IsNullOrEmpty(item.equippedHeroId);
    }

    /// <summary>The HeroId of the hero wearing the item, or null when unequipped.</summary>
    public string EquippedByHeroId(string instanceId)
    {
        EquipmentInstance item = FindById(instanceId);
        return item != null && !string.IsNullOrEmpty(item.equippedHeroId) ? item.equippedHeroId : null;
    }

    // ---------------------------------------------------------------------
    // Equip / unequip - the single transactional path
    // ---------------------------------------------------------------------

    /// <summary>
    /// THE equip path: equips the item to the hero's matching slot.
    /// - the item must be owned by this inventory;
    /// - if another hero wears it, it is unequipped from that hero first
    ///   (one owner per item);
    /// - whatever item occupied the slot is unequipped and simply returns
    ///   to the unequipped pool (never destroyed, never duplicated);
    /// - the hero's stats recalculate through the existing pipeline.
    /// </summary>
    public EquipResult Equip(HeroInstance hero, EquipmentInstance item)
    {
        if (hero == null || hero.data == null || item == null)
        {
            return EquipResult.InvalidRequest;
        }

        if (!items.Contains(item))
        {
            return EquipResult.ItemNotInInventory;
        }

        EquipmentSlot slot = item.slot;

        // Already equipped by this hero in that slot: nothing to do.
        if (ReferenceEquals(hero.GetEquippedItem(slot), item))
        {
            return EquipResult.Equipped;
        }

        // One owner per item: free the item from any other hero first.
        if (!string.IsNullOrEmpty(item.equippedHeroId))
        {
            HeroInstance owner = roster != null ? roster.FindByHeroId(item.equippedHeroId) : null;
            if (owner != null)
            {
                Unequip(owner, slot);
            }
            else
            {
                item.equippedHeroId = string.Empty; // stale bookkeeping only (hero no longer exists)
            }
        }

        // The slot's previous item returns to the unequipped pool.
        Unequip(hero, slot);

        if (!hero.AttachEquipment(slot, item))
        {
            return EquipResult.SlotMismatch;
        }

        item.equippedHeroId = hero.data.HeroId;
        return EquipResult.Equipped;
    }

    /// <summary>
    /// Unequips whatever the hero wears in the slot (the item stays owned,
    /// unequipped, and unchanged) and recalculates the hero's stats.
    /// Returns false when the slot was already empty.
    /// </summary>
    public bool Unequip(HeroInstance hero, EquipmentSlot slot)
    {
        if (hero == null)
        {
            return false;
        }

        EquipmentInstance item = hero.GetEquippedItem(slot);
        if (item == null)
        {
            return false;
        }

        hero.DetachEquipment(slot);
        item.equippedHeroId = string.Empty;
        return true;
    }

    /// <summary>
    /// Restores an equipped relationship from a save (save/load only):
    /// attaches the exact item to the hero's slot and records the owner.
    /// Trusted input - the save already validated it (defensively refuses
    /// slot collisions from corrupt data).
    /// </summary>
    public void RestoreEquipped(HeroInstance hero, EquipmentSlot slot, EquipmentInstance item)
    {
        if (hero == null || item == null)
        {
            return;
        }

        if (hero.GetEquippedItem(slot) != null)
        {
            // Corrupt save: two items claim the same hero slot. Keep the
            // first; this item returns to the pool.
            item.equippedHeroId = string.Empty;
            return;
        }

        hero.AttachEquipment(slot, item);
        item.equippedHeroId = hero.data != null ? hero.data.HeroId : string.Empty;
    }

    // ---------------------------------------------------------------------
    // Auto-equip (real "EQUIP BEST": role-scored, never rarity-blind)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Equips the best available item per slot on the hero: every candidate
    /// from the unequipped pool (or already on this hero) is scored with
    /// the hero's role weights (<see cref="EquipmentStats.CalculateItemScore"/>)
    /// and equipped only when it strictly beats the currently equipped
    /// item's score. Never steals items from other heroes. Returns how many
    /// items were equipped.
    /// </summary>
    public int EquipBest(HeroInstance hero)
    {
        if (hero == null || hero.data == null)
        {
            return 0;
        }

        string heroId = hero.data.HeroId;
        HeroRole role = hero.data.role;
        int equipped = 0;
        for (int slot = 0; slot <= (int)EquipmentSlot.Accessory; slot++)
        {
            EquipmentInstance current = hero.GetEquippedItem((EquipmentSlot)slot);
            float bestScore = EquipmentStats.CalculateItemScore(current, role);
            EquipmentInstance best = current;

            foreach (EquipmentInstance item in items)
            {
                if (item == null || item.slot != (EquipmentSlot)slot)
                {
                    continue;
                }

                // Only items this hero could take without stealing: in the
                // unequipped pool, or already on this hero.
                if (!string.IsNullOrEmpty(item.equippedHeroId) && item.equippedHeroId != heroId)
                {
                    continue;
                }

                float score = EquipmentStats.CalculateItemScore(item, role);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }

            if (best != null && !ReferenceEquals(best, current) && Equip(hero, best) == EquipResult.Equipped)
            {
                equipped++;
            }
        }

        return equipped;
    }

    // ---------------------------------------------------------------------
    // Save support & development reset
    // ---------------------------------------------------------------------

    /// <summary>
    /// Ensures the instance-id counter is at least the given value and at
    /// least past every owned item's "eq-N" id, so restored and new ids can
    /// never collide.
    /// </summary>
    public void SetNextInstanceId(int value)
    {
        nextInstanceId = Mathf.Max(nextInstanceId, Mathf.Max(0, value));
        foreach (EquipmentInstance item in items)
        {
            if (item != null && item.instanceId != null && item.instanceId.StartsWith("eq-")
                && int.TryParse(item.instanceId.Substring(3), out int parsed))
            {
                nextInstanceId = Mathf.Max(nextInstanceId, parsed);
            }
        }
    }

    /// <summary>
    /// Development-only reset: unequips every item from every hero (items
    /// return to the pool) and wipes the inventory. Clearly a dev tool -
    /// never part of normal gameplay.
    /// </summary>
    public void ResetAll()
    {
        if (roster != null)
        {
            foreach (HeroInstance hero in roster.Heroes)
            {
                for (int slot = 0; slot <= (int)EquipmentSlot.Accessory; slot++)
                {
                    Unequip(hero, (EquipmentSlot)slot);
                }
            }
        }

        items.Clear();
        nextInstanceId = 0;
    }
}
