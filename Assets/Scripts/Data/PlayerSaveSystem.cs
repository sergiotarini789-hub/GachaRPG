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
/// One equipment item's persisted state. Templates are referenced by stable
/// <see cref="EquipmentData.EquipmentId"/> so saves survive asset renames;
/// every player-owned value (level, rolled substats, main stat, lock) is
/// stored VERBATIM and never re-rolled or recomputed during loading.
/// Enums persist as ints (stable enum values).
/// </summary>
[Serializable]
public class SavedEquipment
{
    /// <summary>The item's unique instance id (e.g. "eq-7").</summary>
    public string instanceId;

    /// <summary>The template's stable id (empty when the template was removed; the item still restores).</summary>
    public string templateId;

    /// <summary>The item's slot (EquipmentSlot).</summary>
    public int slot;

    /// <summary>The item's rarity (HeroRarity).</summary>
    public int rarity;

    /// <summary>The item's level.</summary>
    public int level = 1;

    /// <summary>The main stat type (EquipmentStatType).</summary>
    public int mainStatType;

    /// <summary>The main stat's current value.</summary>
    public int mainStatValue;

    /// <summary>The substat types (EquipmentStatType values, parallel to <see cref="substatValues"/>).</summary>
    public int[] substatTypes;

    /// <summary>The substat values (parallel to <see cref="substatTypes"/>).</summary>
    public int[] substatValues;

    /// <summary>The item's set id (empty = no set).</summary>
    public string setId;

    /// <summary>The item's lock flag (locked items are never accidentally sold or destroyed).</summary>
    public bool locked;

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
            mainStatValue = item.mainStatValue,
            setId = item.setId ?? string.Empty,
            locked = item.locked,
        };

        List<int> types = new List<int>();
        List<int> values = new List<int>();
        if (item.substats != null)
        {
            foreach (EquipmentSubstat substat in item.substats)
            {
                if (substat != null)
                {
                    types.Add((int)substat.statType);
                    values.Add(substat.value);
                }
            }
        }

        saved.substatTypes = types.ToArray();
        saved.substatValues = values.ToArray();
        return saved;
    }

    /// <summary>
    /// Rebuilds the exact item (no re-rolls, no recomputation): every value
    /// comes from the save. The template may be null (removed asset) - the
    /// item stays fully usable except for upgrading.
    /// </summary>
    public EquipmentInstance Restore(EquipmentData template)
    {
        List<EquipmentSubstat> substats = new List<EquipmentSubstat>();
        if (substatTypes != null && substatValues != null)
        {
            for (int i = 0; i < substatTypes.Length && i < substatValues.Length; i++)
            {
                substats.Add(new EquipmentSubstat { statType = (EquipmentStatType)substatTypes[i], value = substatValues[i] });
            }
        }

        return new EquipmentInstance(
            instanceId,
            template,
            (HeroRarity)rarity,
            (EquipmentSlot)slot,
            (EquipmentStatType)mainStatType,
            mainStatValue,
            substats,
            setId,
            locked,
            level);
    }
}

/// <summary>
/// One hero's equipped-item relationships at save time: the hero's stable
/// id plus the equipped instance id per slot (6 entries, "" = empty slot;
/// index = <see cref="EquipmentSlot"/> value).
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
/// every owned hero, the equipment inventory, and the equip-state
/// relationships. Versioned so format changes migrate old files instead of
/// resetting them (see <see cref="PlayerSaveSystem.Load"/>).
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

    /// <summary>The equipped hero/slot relationships at save time (parallel to the roster).</summary>
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

                // The equipped hero/slot relationships (per hero, per slot).
                List<SavedEquipped> equipped = new List<SavedEquipped>();
                foreach (HeroInstance hero in roster.Heroes)
                {
                    if (hero == null || hero.data == null || hero.EquippedItemCount == 0)
                    {
                        continue;
                    }

                    string[] slotIds = new string[6];
                    for (int slot = 0; slot < 6; slot++)
                    {
                        slotIds[slot] = hero.GetEquippedItemId((EquipmentSlot)slot);
                    }

                    equipped.Add(new SavedEquipped { heroId = hero.data.HeroId, slotItemIds = slotIds });
                }

                profile.equipped = equipped.ToArray();
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
    /// restored VERBATIM (substats and main stat are never re-rolled or
    /// recomputed), templates are resolved by stable id against the catalog,
    /// and the equipped hero/slot relationships are re-attached to the
    /// roster's heroes. Saves without equipment data (pre-equipment formats)
    /// return an empty inventory. Unresolvable templates keep the item
    /// (usable, but it cannot upgrade); unresolvable heroes or items leave
    /// the affected equipment safely in the inventory pool - never
    /// destroyed, never a crash.
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
        // restored (and consolidated), so only surviving heroes receive items.
        if (profile.equipped != null && roster != null)
        {
            foreach (SavedEquipped equipped in profile.equipped)
            {
                if (equipped == null || string.IsNullOrEmpty(equipped.heroId))
                {
                    continue;
                }

                HeroInstance hero = roster.FindByHeroId(equipped.heroId);
                if (hero == null)
                {
                    Debug.LogWarning("PlayerSaveSystem: equipped hero '" + equipped.heroId +
                        "' not found; that hero's equipped items stay safely in the inventory pool.");
                    continue;
                }

                if (equipped.slotItemIds == null)
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

                    EquipmentInstance item = inventory.FindById(itemId);
                    if (item == null)
                    {
                        Debug.LogWarning("PlayerSaveSystem: equipped item '" + itemId +
                            "' not found in the inventory; slot left empty.");
                        continue;
                    }

                    inventory.RestoreEquipped(hero, (EquipmentSlot)slot, item);
                }
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
