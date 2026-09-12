using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// One hero's persisted progression state. Heroes are referenced by their
/// stable <see cref="HeroData.HeroId"/> so saves survive asset renames;
/// skill levels are stored as parallel id/level arrays because JsonUtility
/// cannot serialize dictionaries. Missing fields default to a fresh hero
/// (level 1, 0 XP, rank 0, skill level 1).
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

    /// <summary>Banked Hero Souls at save time.</summary>
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
            souls = hero.Souls,
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
/// The whole persisted player profile: the save-format version, the
/// wallet, and every owned hero. Versioned so future format changes can
/// migrate old files instead of resetting them.
/// </summary>
[Serializable]
public class SavedProfile
{
    /// <summary>Save-format version; must equal <see cref="HeroProgression.CurrentSaveVersion"/>.</summary>
    public int version = HeroProgression.CurrentSaveVersion;

    /// <summary>Wallet balances at save time.</summary>
    public int gold;

    /// <summary>See <see cref="SavedProfile.gold"/>.</summary>
    public int ascensionMaterials;

    /// <summary>See <see cref="SavedProfile.gold"/>.</summary>
    public int skillMaterials;

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
    /// Writes the roster and wallet to disk. Safe to call after any
    /// progression change; failures are logged, never thrown.
    /// </summary>
    public static void Save(HeroRoster roster, PlayerWallet wallet)
    {
        try
        {
            SavedProfile profile = new SavedProfile
            {
                version = HeroProgression.CurrentSaveVersion,
                gold = wallet != null ? wallet.gold : 0,
                ascensionMaterials = wallet != null ? wallet.ascensionMaterials : 0,
                skillMaterials = wallet != null ? wallet.skillMaterials : 0,
                heroes = new SavedHero[0],
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

            File.WriteAllText(SavePath, JsonUtility.ToJson(profile, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("PlayerSaveSystem: saving failed (" + e.Message + "). Progression continues in memory.");
        }
    }

    /// <summary>
    /// Loads the saved profile, or null when no save exists, the file is
    /// unreadable, or the version does not match (a logged warning explains
    /// which). Callers fall back to fresh defaults on null.
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

            if (profile.version != HeroProgression.CurrentSaveVersion)
            {
                Debug.LogWarning("PlayerSaveSystem: save version " + profile.version +
                    " does not match the current version " + HeroProgression.CurrentSaveVersion +
                    "; starting fresh (migrations land with future format changes).");
                return null;
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
    /// Rebuilds the roster from a save: every saved hero is matched to a
    /// <see cref="HeroData"/> from the catalog by stable id and restored as
    /// a live HeroInstance (level, XP, ascension, awakening, souls, skill
    /// levels). Unresolvable ids (asset removed?) are skipped with a logged
    /// warning - never a crash, never a reset of the resolvable heroes.
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
            ? new PlayerWallet(profile.gold, profile.ascensionMaterials, profile.skillMaterials)
            : new PlayerWallet(HeroProgression.StartingGold, HeroProgression.StartingAscensionMaterials, HeroProgression.StartingSkillMaterials);
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
}
