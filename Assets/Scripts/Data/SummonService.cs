using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Performs hero summons for the prototype gacha: rolls a rarity on a
/// weighted table, picks a template from that rarity's pool (falling back
/// safely when a rarity has no heroes), creates a brand-new
/// <see cref="HeroInstance"/> from the chosen <see cref="HeroData"/>, and
/// adds it to the player's <see cref="HeroRoster"/>.
///
/// Plain C# with no UI dependencies: screens only call <see cref="Summon"/>
/// and render the returned <see cref="SummonResult"/>. The weight table and
/// the hero catalog are constructor inputs, so future systems can add
/// different banners, rates, guaranteed summons, pity, or event banners by
/// constructing their own service instance - without rewriting the Heroes
/// Collection or any other system.
///
/// Duplicate handling: when the player already owns the rolled hero's
/// template, NO second owned instance is created - the duplicate advances
/// the owned hero's constellation (C0-C6) through the roster's
/// authoritative duplicate path (<see cref="HeroRoster.ApplyDuplicate"/>);
/// an extra duplicate of a hero already at C6 converts into Hero Tokens on
/// the wallet. First-time heroes are brand-new, independent instances at
/// Level 1 / 0 XP / C0.
///
/// Invariants: <see cref="HeroData"/> templates are never modified; the
/// roster is the single owner of the created instances; the constellation
/// never exceeds C6.
/// </summary>
public class SummonService
{
    /// <summary>Prototype odds: Common 60 / Rare 30 / Epic 9 / Legendary 1.</summary>
    private const int CommonWeight = 60;

    /// <summary>See <see cref="CommonWeight"/>.</summary>
    private const int RareWeight = 30;

    /// <summary>See <see cref="CommonWeight"/>.</summary>
    private const int EpicWeight = 9;

    /// <summary>See <see cref="CommonWeight"/>.</summary>
    private const int LegendaryWeight = 1;

    /// <summary>
    /// One rarity's draw weight in the summon table. Weights are relative;
    /// the default prototype table sums to 100 (percent-style).
    /// </summary>
    public readonly struct RarityOdds
    {
        /// <summary>The rarity this entry rolls for.</summary>
        public readonly HeroRarity Rarity;

        /// <summary>Relative draw weight (larger = more likely).</summary>
        public readonly int Weight;

        public RarityOdds(HeroRarity rarity, int weight)
        {
            Rarity = rarity;
            Weight = weight;
        }
    }

    /// <summary>The distinct HeroData templates summons can produce, in catalog order.</summary>
    private readonly List<HeroData> catalog;

    /// <summary>The player's roster; every summoned hero is added here.</summary>
    private readonly HeroRoster roster;

    /// <summary>
    /// The player's wallet; extra duplicates of C6 heroes convert into
    /// Hero Tokens on it (through the roster's authoritative path).
    /// </summary>
    private readonly PlayerWallet wallet;

    /// <summary>The rarity table, in roll order (the roll walks entries cumulatively).</summary>
    private readonly List<RarityOdds> odds;

    /// <summary>Catalog templates grouped by rarity; missing rarities simply have empty pools.</summary>
    private readonly Dictionary<HeroRarity, List<HeroData>> pools;

    /// <summary>The rarity table this service rolls on, in order (for UI display).</summary>
    public IReadOnlyList<RarityOdds> Odds => odds;

    /// <summary>How many distinct templates summons can currently produce.</summary>
    public int CatalogCount => catalog.Count;

    /// <summary>
    /// Creates the prototype summon service: the given catalog, roster, and
    /// wallet with the default rarity weights (Common 60 / Rare 30 /
    /// Epic 9 / Legendary 1).
    /// </summary>
    public SummonService(IEnumerable<HeroData> availableHeroes, HeroRoster roster, PlayerWallet wallet)
        : this(availableHeroes, roster, wallet, DefaultOdds())
    {
    }

    /// <summary>
    /// Creates a summon service with an explicit rarity table (future
    /// banners, rates, or pity systems use this). Null templates are
    /// skipped and duplicate template references are deduplicated; entries
    /// with non-positive weights are ignored.
    /// </summary>
    public SummonService(IEnumerable<HeroData> availableHeroes, HeroRoster roster, PlayerWallet wallet, IEnumerable<RarityOdds> rarityOdds)
    {
        catalog = new List<HeroData>();
        if (availableHeroes != null)
        {
            foreach (HeroData template in availableHeroes)
            {
                if (template != null && !catalog.Contains(template))
                {
                    catalog.Add(template);
                }
            }
        }

        this.roster = roster;
        this.wallet = wallet;

        odds = new List<RarityOdds>();
        if (rarityOdds != null)
        {
            foreach (RarityOdds entry in rarityOdds)
            {
                if (entry.Weight > 0)
                {
                    odds.Add(entry);
                }
            }
        }

        pools = new Dictionary<HeroRarity, List<HeroData>>();
        foreach (HeroRarity rarity in (HeroRarity[])Enum.GetValues(typeof(HeroRarity)))
        {
            pools[rarity] = new List<HeroData>();
        }

        foreach (HeroData template in catalog)
        {
            pools[template.rarity].Add(template);
        }
    }

    /// <summary>
    /// Performs one summon: rolls a rarity on the weighted table, picks a
    /// random template from that rarity's pool (walking to the next rarity
    /// with heroes when the rolled pool is empty - the fallback is logged),
    /// then applies the outcome through the roster's authoritative
    /// duplicate path: a brand-new Level 1 / 0 XP / C0
    /// <see cref="HeroInstance"/> joins the roster the first time this
    /// hero is obtained, a duplicate advances the owned hero's
    /// constellation by one rank (C0-C6), and an extra duplicate of a hero
    /// already at C6 converts into Hero Tokens on the wallet - the original
    /// hero and its progression are never overwritten or merged. Returns
    /// the result for the UI to render, or null when no hero could be
    /// summoned at all (empty catalog or roster; the reason is logged -
    /// never thrown).
    /// </summary>
    public SummonResult Summon()
    {
        if (roster == null || catalog.Count == 0 || odds.Count == 0)
        {
            Debug.Log("SummonService: no heroes available to summon (empty catalog, roster, or rarity table).");
            return null;
        }

        HeroRarity rolled = RollRarity();
        HeroData chosen = PickFromRarity(rolled);
        if (chosen == null)
        {
            Debug.Log("SummonService: every rarity pool is empty; summon cancelled.");
            return null;
        }

        // Apply the summon through the roster's one authoritative duplicate
        // path: new hero at C0, owned hero's constellation +1, or a Hero
        // Token when the owned hero is already at the C6 cap.
        HeroInstance owned = roster.FindByData(chosen);
        int constellationBefore = owned != null ? owned.Constellation : 0;
        DuplicateResult outcome = roster.ApplyDuplicate(chosen, wallet);
        HeroInstance hero = outcome == DuplicateResult.NewHero ? roster.FindByData(chosen) : owned;

        return new SummonResult(hero, rolled, outcome, constellationBefore);
    }

    /// <summary>
    /// Rolls one rarity on the weighted table: a uniform roll over the
    /// total weight, resolved by walking the table cumulatively.
    /// </summary>
    private HeroRarity RollRarity()
    {
        int total = 0;
        foreach (RarityOdds entry in odds)
        {
            total += entry.Weight;
        }

        int roll = UnityEngine.Random.Range(0, total); // [0, total)
        int cumulative = 0;
        foreach (RarityOdds entry in odds)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
            {
                return entry.Rarity;
            }
        }

        return odds[odds.Count - 1].Rarity; // mathematically unreachable
    }

    /// <summary>
    /// Picks a random template from the given rarity's pool. When that pool
    /// is empty, walks the rarity chain (enum order, wrapping around) to
    /// the next rarity that has heroes and logs the fallback - the current
    /// catalog has no Common or Legendary heroes, so their rolls fall back
    /// to populated rarities instead of crashing. Returns null only when
    /// every pool is empty.
    /// </summary>
    private HeroData PickFromRarity(HeroRarity rarity)
    {
        List<HeroData> pool = pools[rarity];
        if (pool.Count > 0)
        {
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        HeroRarity[] order = (HeroRarity[])Enum.GetValues(typeof(HeroRarity));
        int start = Array.IndexOf(order, rarity);
        for (int step = 1; step < order.Length; step++)
        {
            HeroRarity candidate = order[(start + step) % order.Length];
            if (pools[candidate].Count > 0)
            {
                Debug.Log("SummonService: rolled " + rarity + " but no " + rarity
                    + " heroes exist in the catalog; falling back to " + candidate + ".");
                return pools[candidate][UnityEngine.Random.Range(0, pools[candidate].Count)];
            }
        }

        return null;
    }

    /// <summary>The prototype rarity table, in roll order.</summary>
    private static RarityOdds[] DefaultOdds()
    {
        return new[]
        {
            new RarityOdds(HeroRarity.Common, CommonWeight),
            new RarityOdds(HeroRarity.Rare, RareWeight),
            new RarityOdds(HeroRarity.Epic, EpicWeight),
            new RarityOdds(HeroRarity.Legendary, LegendaryWeight),
        };
    }
}

/// <summary>
/// Outcome of one summon: the hero the summon resolved to (the brand-new
/// instance for a first-time hero, or the existing owned instance that just
/// advanced its constellation), what the rarity roll produced, what the
/// duplicate resolved to (new hero / constellation advance / Hero Token),
/// and the constellation rank before the summon - everything the UI needs
/// to show exactly what happened (e.g. "C2 -> C3") without opening another
/// screen or formatting any of it itself.
/// </summary>
public class SummonResult
{
    /// <summary>The hero the summon resolved to: new instance, or the existing owner of a duplicate.</summary>
    public HeroInstance Hero { get; }

    /// <summary>The rarity the weighted roll selected.</summary>
    public HeroRarity RolledRarity { get; }

    /// <summary>What the summon resolved to: new hero, constellation advance, or extra duplicate past C6.</summary>
    public DuplicateResult Outcome { get; }

    /// <summary>The hero's constellation rank before this summon (0 for a new hero).</summary>
    public int ConstellationBefore { get; }

    /// <summary>The hero's constellation rank after this summon (C0 for a new hero).</summary>
    public int ConstellationAfter => Hero != null ? Hero.Constellation : 0;

    /// <summary>True when a brand-new HeroInstance was added to the roster; false for a duplicate.</summary>
    public bool IsNewHero => Outcome == DuplicateResult.NewHero;

    /// <summary>The rarity of the hero actually summoned (the pool that was used).</summary>
    public HeroRarity ActualRarity => Hero != null && Hero.data != null ? Hero.data.rarity : RolledRarity;

    /// <summary>True when the rolled rarity had no heroes and a fallback pool was used.</summary>
    public bool FallbackUsed => ActualRarity != RolledRarity;

    public SummonResult(HeroInstance hero, HeroRarity rolledRarity, DuplicateResult outcome, int constellationBefore)
    {
        Hero = hero;
        RolledRarity = rolledRarity;
        Outcome = outcome;
        ConstellationBefore = constellationBefore;
    }
}
