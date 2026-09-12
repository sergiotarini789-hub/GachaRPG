using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// One hero's persisted progression state. Heroes are referenced by their
/// stable <see cref="HeroData.HeroId"/> so saves survive asset renames;
/// skill levels are stored as parallel id/level arrays because JsonUtility
/// cannot serialize dictionaries. Missing fields default to a fresh hero
/// (level 1, 0 XP, rank 0, C0, skill level 1).
/// </summary>
[Serializable]
public class SavedHero
{
    /// <summary>The hero template's stable id (<see cref="HeroData.HeroId"/>).</summary>
    public string heroId;

    /// <summary>Level at save time.</summary>
    public int level = 1;

    /// <summary>Experience toward the next level at save time.</summary>
    public int experience;

    /// <summary>Ascension rank at save time.</summary>
    public int ascension;

    /// <summary>Awakening rank at save time.</summary>
    public int awakening;

    /// <summary>Constellation rank at save time (0..6 = C0..C6; one per duplicate copy).</summary>
    public int constellation;

    /// <summary>
    /// LEGACY (save version 1): banked Hero Souls, replaced by the
    /// constellation in version 2. Read ONLY by the v1-to-v2 migration
    /// (which converts souls into constellation progress, capped at C6);
    /// never written by current saves.
    /// </summary>
    public int souls;

    /// <summary>Ids of the skills whose levels were raised (parallel to <see cref="skillLevels"/>).</summary>
    public string[] skillIds;

    /// <summary>Levels of the skills in <see cref="skillIds"/> (parallel array).</summary>
    public int[] skillLevels;

    /// <summary>Captures a live instance's progression.</summary>
    public static SavedHero From(HeroInstance hero)
    {
        SavedHero saved = new SavedHero
        {
            heroId = hero.data != null ? hero.data.HeroId : string.Empty,
            level = hero.Level,
            experience = hero.Experience,
            ascension = hero.Ascension,
            awakening = hero.Awakening,
            constellation = hero.Constellation,
        };

        List<string> ids = new List<string>();
        List<int> levels = new List<int>();
        if (hero.skills != null)
        {
            foreach (SkillData skill in hero.skills)
            {
                if (skill == null)
                {
                    continue;
                }

                ids.Add(skill.SkillId);
                levels.Add(hero.GetSkillLevel(skill));
            }
        }

        saved.skillIds = ids.ToArray();
        saved.skillLevels = levels.ToArray();
        return saved;
    }
}

/// <summary>
/// <summary>
/// One equipment item's persisted state. Definitions are referenced by
/// stable <see cref="EquipmentData.EquipmentId"/> so saves survive asset
/// renames; every player-owned value (level, rolled secondary stats,
/// primary stat, base value, lock, equipped hero) is stored VERBATIM and
/// never re-rolled or recomputed during loading. Enums persist as ints
/// (stable enum values).
/// </summary>
[Serializable]
public class SavedEquipment
{
    /// <summary>The item's unique instance id (e.g. "eq-7").</summary>
    public string instanceId;

    /// <summary>The definition's stable id (empty when the definition was removed; the item still restores).</summary>
    public string templateId;

    /// <summary>The item's slot (EquipmentSlot).</summary>
    public int slot;

    /// <summary>The item's rarity (HeroRarity).</summary>
    public int rarity;

    /// <summary>The item's level.</summary>
    public int level = 1;

    /// <summary>The primary stat type (EquipmentStatType).</summary>
    public int mainStatType;

    /// <summary>The primary stat's base value at level 1 (0 in pre-v4 saves; recovered on restore).</summary>
    public int mainStatBaseValue;

    /// <summary>The primary stat's current value.</summary>
    public int mainStatValue;

    /// <summary>The secondary stat types (EquipmentStatType values, parallel to <see cref="substatValues"/>).</summary>
    public int[] substatTypes;

    /// <summary>The secondary stat values (parallel to <see cref="substatTypes"/>).</summary>
    public int[] substatValues;

    /// <summary>The item's set id (empty = no set).</summary>
    public string setId;

    /// <summary>The item's lock flag (locked items are never accidentally sold or destroyed).</summary>
    public bool locked;

    /// <summary>The HeroId of the hero wearing the item (empty = in the inventory pool).</summary>
    public string equippedHeroId;

    /// <summary>Captures a live item's exact state.</summary>
    public static SavedEquipment From(EquipmentInstance item)
    {
        SavedEquipment saved = new SavedEquipment
        {
            instanceId = item.instanceId,
            templateId = item.data != null ? item.data.EquipmentId : string.Empty,
            slot = (int)item.slot,
            rarity = (int)item.rarity,
            level = item.level,
            mainStatType = (int)item.mainStatType,
            mainStatBaseValue = item.mainStatBaseValue,
            mainStatValue = item.mainStatValue,
            setId = item.setId ?? string.Empty,
            locked = item.locked,
            equippedHeroId = item.equippedHeroId ?? string.Empty,
        };

        List<int> types = new List<int>();
        List<int> values = new List<int>();
        if (item.secondaryStats != null)
        {
            foreach (EquipmentSecondaryStat secondary in item.secondaryStats)
            {
                if (secondary != null)
                {
                    types.Add((int)secondary.statType);
                    values.Add(secondary.value);
                }
            }
        }

        saved.substatTypes = types.ToArray();
        saved.substatValues = values.ToArray();
        return saved;
    }

    /// <summary>
    /// Rebuilds the exact item (no re-rolls, no recomputation): every value
    /// comes from the save. The definition may be null (removed asset) - the
    /// item stays fully usable except for upgrading.
    /// </summary>
    public EquipmentInstance Restore(EquipmentData template)
    {
        List<EquipmentSecondaryStat> secondaryStats = new List<EquipmentSecondaryStat>();
        if (substatTypes != null && substatValues != null)
        {
            for (int i = 0; i < substatTypes.Length && i < substatValues.Length; i++)
            {
                secondaryStats.Add(new EquipmentSecondaryStat { statType = (EquipmentStatType)substatTypes[i], value = substatValues[i] });
            }
        }

        return new EquipmentInstance(
            instanceId,
            template,
            (HeroRarity)rarity,
            (EquipmentSlot)slot,
            (EquipmentStatType)mainStatType,
            mainStatBaseValue,
            mainStatValue,
            secondaryStats,
            setId,
            locked,
            equippedHeroId,
            level);
    }
}

/// <summary>
/// Legacy v3 equip-state record: the hero's equipped instance id per slot.
/// Superseded in v4 by each item's own
/// <see cref="SavedEquipment.equippedHeroId"/>; kept ONLY so v3 saves can
/// be migrated forward (never written for new saves).
/// </summary>
[Serializable]
public class SavedEquipped
{
    /// <summary>The equipped hero's stable <see cref="HeroData.HeroId"/>.</summary>
    public string heroId;

    /// <summary>The equipped instance id per slot (index = EquipmentSlot; empty string = empty slot).</summary>
    public string[] slotItemIds;
}

/// <summary>
/// The whole persisted player profile: the save-format version, the wallet,
/// every owned hero, and the equipment inventory (each item carries its own
/// equipped hero id - the equip-state truth). Versioned so format changes
/// migrate old files instead of resetting them (see
/// <see cref="PlayerSaveSystem.Load"/>).
/// </summary>
[Serializable]
public class SavedProfile
{
    /// <summary>Save-format version; migrated up to <see cref="HeroProgression.CurrentSaveVersion"/> on load.</summary>
    public int version = HeroProgression.CurrentSaveVersion;

    /// <summary>Wallet balances at save time.</summary>
    public int gold;

    /// <summary>See <see cref="SavedProfile.gold"/>.</summary>
    public int ascensionMaterials;

    /// <summary>See <see cref="SavedProfile.gold"/>.</summary>
    public int skillMaterials;

    /// <summary>Awakening Materials at save time (awakening's cost since save version 2).</summary>
    public int awakeningMaterials;

    /// <summary>Hero Tokens at save time (post-C6 duplicate currency since save version 2).</summary>
    public int heroTokens;

    /// <summary>The equipment inventory at save time (empty/null on pre-equipment saves).</summary>
    public SavedEquipment[] equipment;

    /// <summary>
    /// LEGACY v3 equip-state records - read only for migration. Since v4
    /// each item's <see cref="SavedEquipment.equippedHeroId"/> is the
    /// equip-state truth; new saves write an empty array here.
    /// </summary>
    public SavedEquipped[] equipped;

    /// <summary>The inventory's instance-id counter, so restored and new ids never collide.</summary>
    public int nextEquipmentId;

    /// <summary>Every owned hero, in roster order.</summary>
    public SavedHero[] heroes;
}

/// <summary>
/// Lightweight local save/load for the player profile: the roster (every
/// owned HeroInstance's progression) and the wallet, written as JSON to
/// <see cref="Application.persistentDataPath"/>. Local only - no accounts,
/// backend, cloud, or networking. Every operation is wrapped so a missing,
/// corrupt, or future-versioned file degrades to fresh defaults with a
/// logged warning instead of crashing.
/// </summary>
public static class PlayerSaveSystem
{
    /// <summary>Where the profile lives (Application.persistentDataPath is per-player, per-machine).</summary>
    public static string SavePath => Path.Combine(Application.persistentDataPath, "player-profile.json");

    /// <summary>
    /// Writes the roster, wallet, and equipment inventory to disk. Safe to
    /// call after any progression change; failures are logged, never thrown.
    /// </summary>
    public static void Save(HeroRoster roster, PlayerWallet wallet, EquipmentInventory equipment)
    {
        try
        {
            SavedProfile profile = new SavedProfile
            {
                version = HeroProgression.CurrentSaveVersion,
                gold = wallet != null ? wallet.gold : 0,
                ascensionMaterials = wallet != null ? wallet.ascensionMaterials : 0,
                skillMaterials = wallet != null ? wallet.skillMaterials : 0,
                awakeningMaterials = wallet != null ? wallet.awakeningMaterials : 0,
                heroTokens = wallet != null ? wallet.heroTokens : 0,
                heroes = new SavedHero[0],
                equipment = new SavedEquipment[0],
                equipped = new SavedEquipped[0],
            };

            if (roster != null)
            {
                List<SavedHero> heroes = new List<SavedHero>();
                foreach (HeroInstance hero in roster.Heroes)
                {
                    if (hero != null && hero.data != null)
                    {
                        heroes.Add(SavedHero.From(hero));
                    }
                }

                profile.heroes = heroes.ToArray();
            }

            if (equipment != null)
            {
                List<SavedEquipment> items = new List<SavedEquipment>();
                foreach (EquipmentInstance item in equipment.Items)
                {
                    if (item != null)
                    {
                        items.Add(SavedEquipment.From(item));
                    }
                }

                profile.equipment = items.ToArray();
            }

            File.WriteAllText(SavePath, JsonUtility.ToJson(profile, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("PlayerSaveSystem: saving failed (" + e.Message + "). Progression continues in memory.");
        }
    }

    /// <summary>
    /// Loads the saved profile, or null when no save exists, the file is
    /// unreadable, or the file was written by a FUTURE version (a logged
    /// warning explains which). Older files are migrated forward in place
    /// (see <see cref="MigrateToCurrentVersion"/>) so progression is never
    /// reset by a format change. Callers fall back to fresh defaults on
    /// null.
    /// </summary>
    public static SavedProfile Load()
    {
        try
        {
            if (!File.Exists(SavePath))
            {
                return null;
            }

            SavedProfile profile = JsonUtility.FromJson<SavedProfile>(File.ReadAllText(SavePath));
            if (profile == null)
            {
                Debug.LogWarning("PlayerSaveSystem: save file could not be parsed; starting fresh.");
                return null;
            }

            if (profile.version > HeroProgression.CurrentSaveVersion)
            {
                Debug.LogWarning("PlayerSaveSystem: save version " + profile.version +
                    " is newer than the current version " + HeroProgression.CurrentSaveVersion +
                    "; starting fresh.");
                return null;
            }

            if (profile.version < HeroProgression.CurrentSaveVersion)
            {
                MigrateToCurrentVersion(profile);
            }

            return profile;
        }
        catch (Exception e)
        {
            Debug.LogWarning("PlayerSaveSystem: loading failed (" + e.Message + "); starting fresh.");
            return null;
        }
    }

    /// <summary>
    /// Migrates an older save format to the current version, in place:
    ///
    /// Version 1 to 2 - the Hero Souls counter became the C0-C6
    /// constellation: each saved hero's souls convert into constellation
    /// progress (souls 0..6 map to C0..C6 one-to-one, 6+ clamps to C6 -
    /// excess souls are discarded, never stored as counters). Awakening
    /// became material-driven, so migrated profiles receive the standard
    /// starting Awakening Materials; Hero Tokens did not exist in v1 and
    /// start at 0. Everything else (gold, materials, levels, XP,
    /// ascension, awakening, skill levels) is preserved untouched.
    ///
    /// Version 2 to 3 - the equipment inventory was added. Pre-equipment
    /// saves have no equipment data, which restores as an empty inventory
    /// (see <see cref="RestoreEquipment"/>); nothing else changes.
    ///
    /// Version 3 to 4 - equip state moved from separate hero records onto
    /// each item's own equippedHeroId (the records are merged into the
    /// items), and per-rarity level caps arrived: over-level items clamp to
    /// their rarity's new cap with the primary stat recomputed during
    /// restore. Nothing else changes.
    /// </summary>
    private static void MigrateToCurrentVersion(SavedProfile profile)
    {
        if (profile.version < 2)
        {
            if (profile.heroes != null)
            {
                foreach (SavedHero saved in profile.heroes)
                {
                    if (saved == null)
                    {
                        continue;
                    }

                    // Souls -> constellation: 0 souls = C0, 1 = C1, ... 6+ = C6.
                    // (Banked souls undercount past duplicates that were
                    // already spent on awakening - the closest recoverable
                    // mapping; excess souls beyond C6 are discarded.)
                    saved.constellation = Mathf.Clamp(saved.souls, 0, HeroProgression.MaxConstellationRank);
                    saved.souls = 0;
                }
            }

            profile.awakeningMaterials = HeroProgression.StartingAwakeningMaterials;
            profile.heroTokens = 0;
            Debug.Log("PlayerSaveSystem: migrated save v1 to v2 (Hero Souls converted to constellation ranks, capped at C6).");
        }

        if (profile.version < 3)
        {
            // Equipment data did not exist before v3; null arrays restore as
            // an empty inventory. Logged for transparency, not warned: an
            // old save loading with no equipment is expected, safe behavior.
            Debug.Log("PlayerSaveSystem: migrated save v2 to v3 (no equipment data; starting with an empty equipment inventory).");
        }

        if (profile.version < 4)
        {
            // v3 kept equip-state as separate hero records; v4 stores the
            // owner on each item. Move the records onto the items (items
            // without a record stay in the pool); level caps also changed
            // with v4's per-rarity rules - the item restore path clamps and
            // recomputes safely.
            if (profile.equipped != null && profile.equipment != null)
            {
                foreach (SavedEquipped equipped in profile.equipped)
                {
                    if (equipped == null || string.IsNullOrEmpty(equipped.heroId) || equipped.slotItemIds == null)
                    {
                        continue;
                    }

                    for (int slot = 0; slot < equipped.slotItemIds.Length && slot <= (int)EquipmentSlot.Accessory; slot++)
                    {
                        string itemId = equipped.slotItemIds[slot];
                        if (string.IsNullOrEmpty(itemId))
                        {
                            continue;
                        }

                        foreach (SavedEquipment saved in profile.equipment)
                        {
                            if (saved != null && saved.instanceId == itemId)
                            {
                                saved.equippedHeroId = equipped.heroId;
                                break;
                            }
                        }
                    }
                }
            }

            profile.equipped = null;
            Debug.Log("PlayerSaveSystem: migrated save v3 to v4 (equip state moved onto equipment items; per-rarity level caps applied on restore).");
        }

        profile.version = HeroProgression.CurrentSaveVersion;
    }

    /// <summary>
    /// Rebuilds the roster from a save: every saved hero is matched to a
    /// <see cref="HeroData"/> from the catalog by stable id and restored as
    /// a live HeroInstance (level, XP, ascension, awakening, constellation,
    /// skill levels). Unresolvable ids (asset removed?) are skipped with a
    /// logged warning - never a crash, never a reset of the resolvable
    /// heroes.
    /// </summary>
    public static HeroRoster RestoreRoster(SavedProfile profile, List<HeroData> catalog)
    {
        HeroRoster roster = new HeroRoster();
        if (profile == null || profile.heroes == null)
        {
            return roster;
        }

        foreach (SavedHero saved in profile.heroes)
        {
            if (saved == null)
            {
                continue;
            }

            HeroData data = FindHeroData(catalog, saved.heroId);
            if (data == null)
            {
                Debug.LogWarning("PlayerSaveSystem: saved hero '" + saved.heroId +
                    "' has no matching HeroData in the catalog (removed asset?); hero skipped.");
                continue;
            }

            HeroInstance hero = new HeroInstance(data);
            hero.ApplySavedProgression(saved);
            roster.Add(hero);
        }

        return roster;
    }

    /// <summary>Restores the wallet from a save (fresh starting values on null).</summary>
    public static PlayerWallet RestoreWallet(SavedProfile profile)
    {
        return profile != null
            ? new PlayerWallet(profile.gold, profile.ascensionMaterials, profile.skillMaterials, profile.awakeningMaterials, profile.heroTokens)
            : new PlayerWallet(HeroProgression.StartingGold, HeroProgression.StartingAscensionMaterials, HeroProgression.StartingSkillMaterials, HeroProgression.StartingAwakeningMaterials, HeroProgression.StartingHeroTokens);
    }

    /// <summary>
    /// Rebuilds the equipment inventory from a save: every saved item is
    /// restored VERBATIM (secondary stats and the primary stat are never
    /// re-rolled), definitions are resolved by stable id against the
    /// catalog, and each item's equipped hero is re-attached to the roster
    /// (the item's own equippedHeroId is the truth; level clamps to the
    /// rarity's current cap, recomputing the primary stat when needed).
    /// Saves without equipment data (pre-equipment formats) return an
    /// empty inventory. Unresolvable definitions keep the item (usable,
    /// but it cannot upgrade); an unresolvable hero leaves the item safely
    /// in the inventory pool - never destroyed, never a crash.
    /// </summary>
    public static EquipmentInventory RestoreEquipment(SavedProfile profile, HeroRoster roster, List<EquipmentData> catalog)
    {
        EquipmentInventory inventory = new EquipmentInventory(roster);

        if (profile == null || profile.equipment == null)
        {
            return inventory;
        }

        foreach (SavedEquipment saved in profile.equipment)
        {
            if (saved == null || string.IsNullOrEmpty(saved.instanceId))
            {
                continue;
            }

            EquipmentData template = FindEquipmentData(catalog, saved.templateId);
            if (template == null)
            {
                Debug.LogWarning("PlayerSaveSystem: equipment '" + saved.instanceId +
                    "' has no matching EquipmentData in the catalog (removed asset?); item restored without a template (cannot upgrade).");
            }

            inventory.Add(saved.Restore(template));
        }

        inventory.SetNextInstanceId(profile.nextEquipmentId);

        // Re-attach the equipped relationships AFTER the roster is fully
        // restored (and consolidated), so only surviving heroes receive
        // items. Since v4 the truth is each item's equippedHeroId (v3
        // records were already merged into the items by migration).
        if (roster != null)
        {
            foreach (EquipmentInstance item in inventory.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.equippedHeroId))
                {
                    continue;
                }

                HeroInstance hero = roster.FindByHeroId(item.equippedHeroId);
                if (hero == null)
                {
                    Debug.LogWarning("PlayerSaveSystem: equipped hero '" + item.equippedHeroId +
                        "' not found; that hero's equipped items stay safely in the inventory pool.");
                    item.equippedHeroId = string.Empty;
                    continue;
                }

                inventory.RestoreEquipped(hero, item.slot, item);
            }
        }

        return inventory;
    }

    /// <summary>Finds a catalog template by stable id (HeroId, falling back to the authored hero name).</summary>
    private static HeroData FindHeroData(List<HeroData> catalog, string heroId)
    {
        if (string.IsNullOrEmpty(heroId) || catalog == null)
        {
            return null;
        }

        foreach (HeroData data in catalog)
        {
            if (data != null && data.HeroId == heroId)
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>Finds an equipment template by stable id (EquipmentId, falling back to the authored name).</summary>
    private static EquipmentData FindEquipmentData(List<EquipmentData> catalog, string templateId)
    {
        if (string.IsNullOrEmpty(templateId) || catalog == null)
        {
            return null;
        }

        foreach (EquipmentData data in catalog)
        {
            if (data != null && data.EquipmentId == templateId)
            {
                return data;
            }
        }

        return null;
    }
}
