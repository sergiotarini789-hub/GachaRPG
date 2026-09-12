using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Scene test harness: attach to a GameObject and assign the three prototype
/// hero assets (attack / defense / support) in the Inspector (or the legacy
/// two HeroData fields for a mirrored 3v3). Press Play
/// to run a deterministic 3v3 battle: three HeroInstances per team, turn
/// order by highest current Speed each round, first ready skill preferred
/// over the basic attack, first living enemy targeted (heals target self).
///
/// A Canvas-based HUD (<see cref="BattleHud"/>) is generated entirely from
/// code on Start - no UI hierarchy has to be built in the Editor: three
/// health bars per side, the five most recent combat events, and a large
/// banner announcing the winning team. The original 1v1 battle
/// routine is kept unchanged as a minimal regression fixture.
/// </summary>
public class BattleTestRunner : MonoBehaviour
{
    /// <summary>Team 1's HeroData template - each slot spawns its own HeroInstance (assign in the Inspector).</summary>
    public HeroData hero1Data;

    /// <summary>Team 2's HeroData template - each slot spawns its own HeroInstance (assign in the Inspector).</summary>
    public HeroData hero2Data;

    /// <summary>
    /// Optional test skill granted to every Team 2 hero to demonstrate skill
    /// targeting (e.g. a heal). Leave unassigned for the default battle.
    /// </summary>
    public SkillData testHealSkill;

    /// <summary>Attack-role hero for the prototype 3v3 (assign in the Inspector).</summary>
    [FormerlySerializedAs("warriorData")]
    public HeroData attackHeroData;

    /// <summary>Defense-role hero for the prototype 3v3 (assign in the Inspector).</summary>
    [FormerlySerializedAs("tankData")]
    public HeroData defenseHeroData;

    /// <summary>Support-role hero for the prototype 3v3 (assign in the Inspector).</summary>
    [FormerlySerializedAs("healerData")]
    public HeroData supportHeroData;

    /// <summary>
    /// Test-only progression hook: experience granted to every Team 1 hero
    /// once the teams are built, exercising HeroInstance's progression API
    /// (level-ups and stat growth apply automatically). Leave 0 for the
    /// standard all-level-1 fixture.
    /// </summary>
    public int teamOneBonusExperience = 0;

    /// <summary>
    /// When true (default), Play mode opens the runtime main menu and the
    /// battle starts from its BATTLE button (see StartBattleFromMenu).
    /// Uncheck to run the battle immediately on Play, exactly as before.
    /// </summary>
    public bool startWithMainMenu = true;

    /// <summary>Hard cap on 1v1 loop iterations so a battle can never hang Play mode.</summary>
    private const int MaxTurns = 100;

    /// <summary>Real-time seconds between turns at x1 speed so the fight is watchable; the battle speed multiplier divides this at runtime.</summary>
    private const float SecondsPerTurn = 1f;

    /// <summary>
    /// How many recent battle events to keep for the on-screen log; also the
    /// number of lines the Canvas HUD shows.
    /// </summary>
    public const int MaxLogEntries = 5;

    /// <summary>1v1 runner's left hero; kept as a field so the HUD can draw its health bar.</summary>
    private HeroInstance hero1;

    /// <summary>1v1 runner's right hero; kept as a field so the HUD can draw its health bar.</summary>
    private HeroInstance hero2;

    /// <summary>Team 1 of the 3v3 battle; kept as a field so the HUD can draw its heroes' health bars.</summary>
    private Team team1;

    /// <summary>Team 2 of the 3v3 battle; kept as a field so the HUD can draw its heroes' health bars.</summary>
    private Team team2;

    /// <summary>The running 3v3 battle; drives turn selection and the victory check.</summary>
    private TeamBattle teamBattle;

    /// <summary>The Canvas HUD this runner spawned; kept so the coroutines can tell it who is acting (presentation only).</summary>
    private BattleHud hud;

    /// <summary>The runtime main menu, shown when startWithMainMenu is set; battles are started from its BATTLE button.</summary>
    private MainMenu menu;

    /// <summary>
    /// The player's owned heroes: one live HeroInstance per hero, carrying
    /// its own progression state. Built once from the same HeroData assets
    /// the battle fields; the Heroes collection screen reads it and future
    /// systems (gacha, save) will add to it.
    /// </summary>
    private HeroRoster roster;

    /// <summary>The runtime Heroes collection screen, created on first open and reused afterwards.</summary>
    private HeroesScreen heroesScreen;

    /// <summary>
    /// The summon system: rolls rarities on its weighted table, creates new
    /// HeroInstances from the catalog templates, and adds them to the
    /// roster. Built once in Start; the Summon screen only requests
    /// summons from it.
    /// </summary>
    private SummonService summonService;

    /// <summary>The runtime Summon screen, created on first open and reused afterwards.</summary>
    private SummonScreen summonScreen;

    /// <summary>
    /// The player's placeholder progression resources (gold, ascension
    /// materials, skill materials); persisted with the roster. Ascension,
    /// skill upgrades, and the LEVEL UP exchange consume from it.
    /// </summary>
    private PlayerWallet wallet;

    /// <summary>True while the 3v3 coroutine is resolving; guards against double starts from the menu.</summary>
    private bool battleRunning;

    /// <summary>Handle of the running 3v3 coroutine, so StopBattle can end it mid-run.</summary>
    private Coroutine battleCoroutine;

    /// <summary>True while the battle is paused through the HUD's pause menu; freezes turn pacing without touching battle state.</summary>
    private bool battlePaused;

    /// <summary>Current battle speed multiplier (1, 2, or 3); divides the per-turn delay so x2/x3 play the same turns faster. Combat itself is untouched.</summary>
    private int battleSpeed = 1;

    /// <summary>
    /// True while the battle runs itself (the engine's AI picks every
    /// action). AUTO OFF hands player-team turns to the player through the
    /// manual-action handshake below; enemy heroes always act through the
    /// AI. Every battle starts in AUTO.
    /// </summary>
    private bool autoBattle = true;

    /// <summary>The player-team hero currently waiting for a manual action, or null when the runner is not waiting for input.</summary>
    private HeroInstance manualActor;

    /// <summary>The skill submitted for the pending manual turn (null = basic attack).</summary>
    private SkillData manualSkill;

    /// <summary>The target submitted for the pending manual turn; null lets the engine resolve Self/Ally skills itself.</summary>
    private HeroInstance manualTarget;

    /// <summary>True once the player submitted the pending action; the battle loop consumes it the next frame.</summary>
    private bool manualActionReady;

    /// <summary>Last few battle events, most recent first.</summary>
    private readonly List<string> battleLog = new List<string>();

    /// <summary>True once the battle finished (win, draw, or safety limit).</summary>
    private bool battleEnded;

    /// <summary>Name shown in the end-of-battle banner ("Nobody" on a draw).</summary>
    private string winnerName = string.Empty;

    // ---------------------------------------------------------------------
    // Read-only view state for the Canvas HUD (BattleHud). These expose the
    // private fields the battle coroutines own - the HUD keeps no duplicate
    // battle state of its own.
    // ---------------------------------------------------------------------

    /// <summary>Team 1 of the current 3v3 battle, or null before it starts.</summary>
    public Team Team1 => team1;

    /// <summary>Team 2 of the current 3v3 battle, or null before it starts.</summary>
    public Team Team2 => team2;

    /// <summary>The legacy 1v1 fixture's left hero, or null outside a 1v1 run.</summary>
    public HeroInstance Hero1 => hero1;

    /// <summary>The legacy 1v1 fixture's right hero, or null outside a 1v1 run.</summary>
    public HeroInstance Hero2 => hero2;

    /// <summary>Recent battle events, most recent first (at most <see cref="MaxLogEntries"/>).</summary>
    public IReadOnlyList<string> BattleLog => battleLog;

    /// <summary>True once the battle finished (win, draw, or safety limit).</summary>
    public bool BattleEnded => battleEnded;

    /// <summary>Current battle speed multiplier (1-3); the HUD's SPEED toggle reads and cycles this.</summary>
    public int BattleSpeed => battleSpeed;

    /// <summary>True while the engine's AI controls every action (AUTO ON); the HUD's AUTO toggle reads this.</summary>
    public bool AutoBattle => autoBattle;

    /// <summary>
    /// The player-team hero waiting for a manual action (AUTO OFF), or
    /// null outside a manual turn. The HUD enables the action bar and the
    /// targeting hint while this is set.
    /// </summary>
    public HeroInstance ManualActor => manualActor;

    /// <summary>Winner shown in the end banner: "Team 1", "Team 2", or "Nobody" on a draw.</summary>
    public string WinnerName => winnerName;

    /// <summary>
    /// The 3v3 winner exactly as <see cref="TeamBattle"/> reports it (team 1
    /// or team 2), or null on a draw or before the battle starts. Read by the
    /// HUD's result banner; carries no logic of its own.
    /// </summary>
    public Team WinningTeam => teamBattle != null ? teamBattle.Winner : null;

    private void Start()
    {
        // The roster (the player's owned heroes) and wallet load from the
        // local save when one exists, and fall back to the initial grant
        // otherwise; the Heroes collection reads the roster from the menu.
        LoadProfile();

        // The summon system draws new owned heroes from the same templates
        // and adds them to the roster.
        summonService = new SummonService(BuildSummonCatalog(), roster);

        if (startWithMainMenu)
        {
            // The runtime main menu is the entry point; the battle starts
            // from its BATTLE button (see StartBattleFromMenu).
            menu = MainMenu.Create(this);
            return;
        }

        // Direct-battle mode: identical to the original flow.
        hud = BattleHud.Create(this);
        battleCoroutine = StartCoroutine(RunTeamBattleRoutine());
    }

    /// <summary>
    /// Starts the 3v3 battle from the main menu's BATTLE button: hides the
    /// menu, builds the HUD once, and runs the exact same routine as always -
    /// no combat behavior changes. Ignored while a battle is still running.
    /// </summary>
    public void StartBattleFromMenu()
    {
        if (battleRunning)
        {
            return;
        }

        if (menu != null)
        {
            menu.gameObject.SetActive(false);
        }

        if (hud == null)
        {
            hud = BattleHud.Create(this);
        }

        battleCoroutine = StartCoroutine(RunTeamBattleRoutine());
    }

    /// <summary>The player's owned heroes; level/XP state lives on each HeroInstance.</summary>
    public HeroRoster Roster => roster;

    /// <summary>
    /// Opens the Heroes collection from the main menu's HEROES tab: hides
    /// the menu and shows the runtime collection screen (built once on
    /// first open, re-activated afterwards). Pure navigation - no battle
    /// state is touched.
    /// </summary>
    public void OpenHeroesCollection()
    {
        if (heroesScreen == null)
        {
            heroesScreen = HeroesScreen.Create(this);
        }

        heroesScreen.gameObject.SetActive(true);

        if (summonScreen != null)
        {
            summonScreen.gameObject.SetActive(false);
        }

        if (menu != null)
        {
            menu.gameObject.SetActive(false);
        }
    }

    /// <summary>Closes the Heroes collection and returns to the main menu.</summary>
    public void CloseHeroesCollection()
    {
        if (heroesScreen != null)
        {
            heroesScreen.gameObject.SetActive(false);
        }

        if (menu != null)
        {
            menu.gameObject.SetActive(true);
        }
    }

    /// <summary>The summon system; the Summon screen requests summons from it. No gacha logic lives in the UI.</summary>
    public SummonService SummonService => summonService;

    /// <summary>
    /// Opens the Summon screen from the main menu's SUMMON tab: hides the
    /// menu (and the Heroes screen, should one be open) and shows the
    /// runtime summon screen (built once on first open, re-activated
    /// afterwards). Pure navigation - no battle state is touched.
    /// </summary>
    public void OpenSummonScreen()
    {
        if (summonScreen == null)
        {
            summonScreen = SummonScreen.Create(this);
        }

        summonScreen.gameObject.SetActive(true);

        if (heroesScreen != null)
        {
            heroesScreen.gameObject.SetActive(false);
        }

        if (menu != null)
        {
            menu.gameObject.SetActive(false);
        }
    }

    /// <summary>Closes the Summon screen and returns to the main menu.</summary>
    public void CloseSummonScreen()
    {
        if (summonScreen != null)
        {
            summonScreen.gameObject.SetActive(false);
        }

        if (menu != null)
        {
            menu.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Collects the distinct HeroData templates summons can produce: the
    /// prototype kit trio first, then the legacy pair, deduplicated by
    /// reference. No hero is hardcoded - swap or add Inspector assets and
    /// the summon catalog follows.
    /// </summary>
    private List<HeroData> BuildSummonCatalog()
    {
        List<HeroData> catalog = new List<HeroData>();
        TryAddToCatalog(catalog, attackHeroData);
        TryAddToCatalog(catalog, defenseHeroData);
        TryAddToCatalog(catalog, supportHeroData);
        TryAddToCatalog(catalog, hero1Data);
        TryAddToCatalog(catalog, hero2Data);
        return catalog;
    }

    /// <summary>Adds a template to the catalog unless it is null or already present.</summary>
    private static void TryAddToCatalog(List<HeroData> catalog, HeroData template)
    {
        if (template != null && !catalog.Contains(template))
        {
            catalog.Add(template);
        }
    }

    /// <summary>
    /// Loads the player profile from the local save, or builds the initial
    /// grant when no valid save exists, then migrates any pre-souls
    /// duplicate roster entries safely. The wallet always exists so
    /// progression actions have something to consume from.
    /// </summary>
    private void LoadProfile()
    {
        SavedProfile save = PlayerSaveSystem.Load();
        if (save != null)
        {
            roster = PlayerSaveSystem.RestoreRoster(save, BuildSummonCatalog());
            wallet = PlayerSaveSystem.RestoreWallet(save);
        }
        else
        {
            BuildInitialRoster();
            wallet = new PlayerWallet(
                HeroProgression.StartingGold,
                HeroProgression.StartingAscensionMaterials,
                HeroProgression.StartingSkillMaterials);
        }

        // Safe migration for data created before duplicates became souls.
        roster.ConsolidateDuplicates();
    }

    /// <summary>The player's placeholder resource wallet; progression actions consume from it.</summary>
    public PlayerWallet Wallet => wallet;

    /// <summary>
    /// Persists the roster and wallet immediately. Called after every
    /// progression change (level up, ascend, awaken, skill upgrade, summon,
    /// debug actions) and on quit, so progression always survives a restart.
    /// </summary>
    public void SaveProfile()
    {
        if (roster != null && wallet != null)
        {
            PlayerSaveSystem.Save(roster, wallet);
        }
    }

    private void OnApplicationQuit()
    {
        SaveProfile();
    }

    /// <summary>
    /// Builds the player's battle team from the roster: for each squad slot
    /// (defense, support, attack - the same deliberate order as
    /// BuildMixedTeam) the owned instance's combat clone carries the hero's
    /// real progression (level, ascension, awakening, skill levels) into
    /// battle, so combat always uses the current calculated state. Slots
    /// whose template the player does not own field a fresh level-1
    /// instance exactly like before. The roster's authoritative instances
    /// are never mutated by the battle - only their clones are.
    /// </summary>
    private Team BuildPlayerTeam()
    {
        var team = new Team();
        AddPlayerHero(team, defenseHeroData);
        AddPlayerHero(team, supportHeroData);
        AddPlayerHero(team, attackHeroData);
        return team;
    }

    /// <summary>Fields one squad slot: the owned hero's combat clone, or a fresh fallback instance.</summary>
    private void AddPlayerHero(Team team, HeroData template)
    {
        if (template == null)
        {
            return;
        }

        HeroInstance owned = roster != null ? roster.FindByData(template) : null;
        HeroInstance hero = owned != null ? owned.CreateCombatClone() : new HeroInstance(template);
        hero.displayName = template.heroName;
        team.Add(hero);
    }

    /// <summary>
    /// Builds the initial roster from the same HeroData assets the battle
    /// fields (the prototype kit trio, or the legacy 1v1 pair when the trio
    /// is not assigned): one owned HeroInstance per hero, each carrying its
    /// own progression state. No hero names are hardcoded - swap the
    /// Inspector assets and both the roster and the battle follow.
    /// </summary>
    private void BuildInitialRoster()
    {
        roster = new HeroRoster();

        bool heroKit = attackHeroData != null && defenseHeroData != null && supportHeroData != null;
        if (heroKit)
        {
            roster.Add(new HeroInstance(attackHeroData));
            roster.Add(new HeroInstance(defenseHeroData));
            roster.Add(new HeroInstance(supportHeroData));
        }
        else
        {
            if (hero1Data != null)
            {
                roster.Add(new HeroInstance(hero1Data));
            }

            if (hero2Data != null)
            {
                roster.Add(new HeroInstance(hero2Data));
            }
        }
    }

    /// <summary>
    /// Pauses or resumes the battle from the HUD's pause menu. Pausing sets
    /// Time.timeScale to 0, which immediately freezes the in-flight turn
    /// delay (WaitForSeconds runs on scaled time); the battle loop also
    /// holds at its pause gate. Resuming restores the time scale and the
    /// interrupted wait continues with its remaining time - the current
    /// battle state is never touched, so RESUME continues exactly where the
    /// battle stopped.
    /// </summary>
    public void SetBattlePaused(bool paused)
    {
        battlePaused = paused;
        Time.timeScale = paused ? 0f : 1f;
    }

    /// <summary>
    /// Sets the battle speed multiplier (clamped to 1-3). The per-turn delay
    /// becomes <see cref="SecondsPerTurn"/> divided by the multiplier, so x2
    /// and x3 run the exact same turn sequence two or three times faster in
    /// real time - turn order, damage, healing, cooldowns, and results are
    /// identical at every speed. An in-flight wait keeps its original
    /// duration; the new speed applies from the next turn. Safe to call
    /// mid-battle.
    /// </summary>
    public void SetBattleSpeed(int multiplier)
    {
        battleSpeed = Mathf.Clamp(multiplier, 1, 3);
    }

    /// <summary>
    /// Switches between AUTO (the engine's AI acts for everyone) and
    /// MANUAL (player-team turns wait for <see cref="SubmitManualAction"/>).
    /// Turning AUTO ON mid-manual-turn makes the AI take over the pending
    /// turn immediately; turning it OFF takes effect at the next
    /// player-team turn. Battle math never changes - only who decides.
    /// </summary>
    public void SetAutoBattle(bool enabled)
    {
        autoBattle = enabled;
        if (enabled)
        {
            ClearManualTurn();
        }
    }

    /// <summary>
    /// Hands the player's chosen action to the battle loop: the skill
    /// (null = basic attack) and its target (null lets the engine resolve
    /// Self/Ally skills itself). Ignored unless a manual turn is actually
    /// pending; the engine re-validates the choice before executing it.
    /// </summary>
    public void SubmitManualAction(SkillData skill, HeroInstance target)
    {
        if (manualActor == null || battleEnded)
        {
            return;
        }

        manualSkill = skill;
        manualTarget = target;
        manualActionReady = true;
    }

    /// <summary>Parks the given player-team hero's turn until the player submits an action (or AUTO takes over).</summary>
    private void BeginManualTurn(HeroInstance actor)
    {
        manualActor = actor;
        manualSkill = null;
        manualTarget = null;
        manualActionReady = false;
    }

    /// <summary>Clears the manual-action handshake; called after every resolved turn, on AUTO takeover, and when the battle stops.</summary>
    private void ClearManualTurn()
    {
        manualActor = null;
        manualSkill = null;
        manualTarget = null;
        manualActionReady = false;
    }

    /// <summary>
    /// Stops the current battle cleanly (the pause menu's EXIT BATTLE):
    /// ends the routine mid-run, clears the pause state, restores the time
    /// scale, and flags the battle as ended. Re-shows the main menu when
    /// there is one; the next StartBattleFromMenu call starts a fresh
    /// battle with rebuilt teams, exactly like the first run.
    /// </summary>
    public void StopBattle()
    {
        if (battleCoroutine != null)
        {
            StopCoroutine(battleCoroutine);
            battleCoroutine = null;
        }

        battlePaused = false;
        Time.timeScale = 1f;
        battleRunning = false;
        ClearManualTurn();

        battleEnded = true;
        winnerName = "Nobody";

        if (hud != null)
        {
            hud.SetActiveHero(null);
        }

        if (menu != null)
        {
            menu.gameObject.SetActive(true);
        }
    }

    /// <summary>Restores the time scale when disabled while a pause is active, so a paused battle can never leak a frozen clock.</summary>
    private void OnDisable()
    {
        if (battlePaused)
        {
            SetBattlePaused(false);
        }
    }

    /// <summary>Starts a 1v1 battle with no injected skills (kept for minimal regression testing).</summary>
    public IEnumerator RunBattleRoutine()
    {
        return RunBattleRoutine(null, null);
    }

    /// <summary>
    /// Runs the original 1v1 battle as a coroutine: one attack (or skill) per
    /// <see cref="SecondsPerTurn"/> interval until a hero dies or the safety
    /// limit is reached. Optional skill lists are granted to each side at
    /// spawn, mainly so tests can exercise the skill path.
    /// </summary>
    public IEnumerator RunBattleRoutine(List<SkillData> hero1Skills, List<SkillData> hero2Skills)
    {
        if (hero1Data == null || hero2Data == null)
        {
            Debug.LogWarning("BattleTestRunner: assign both HeroData assets in the Inspector before starting the battle.");
            battleEnded = true;
            winnerName = "Nobody";
            yield break;
        }

        hero1 = new HeroInstance(hero1Data);
        if (hero1Skills != null)
        {
            hero1.skills.AddRange(hero1Skills);
        }

        hero2 = new HeroInstance(hero2Data);
        if (hero2Skills != null)
        {
            hero2.skills.AddRange(hero2Skills);
        }

        var turnManager = new TurnManager(new List<HeroInstance> { hero1, hero2 });
        battleLog.Clear();
        battleEnded = false;
        winnerName = string.Empty;
        battleSpeed = 1;

        int turnCount = 0;
        while (hero1.isAlive && hero2.isAlive && turnCount < MaxTurns)
        {
            // Per-turn breather at the current speed (the x1 interval
            // divided by the multiplier) so the fight unfolds visibly.
            yield return new WaitForSeconds(SecondsPerTurn / battleSpeed);

            HeroInstance actor = turnManager.GetNextTurn();
            HeroInstance target = ReferenceEquals(actor, hero1) ? hero2 : hero1;

            // Presentation only: highlight the acting hero in the HUD.
            if (hud != null)
            {
                hud.SetActiveHero(actor);
            }

            // Cooldowns recover at the start of the acting hero's turn.
            actor.TickCooldowns();

            // Prefer the first ready skill; fall back to the basic attack.
            SkillData readySkill = null;
            foreach (SkillData skill in actor.skills)
            {
                if (actor.IsSkillReady(skill))
                {
                    readySkill = skill;
                    break;
                }
            }

            if (readySkill != null)
            {
                if (readySkill.type == SkillType.Heal)
                {
                    int healthBefore = actor.currentHealth;
                    CombatManager.PerformSkill(actor, actor, readySkill); // heals target self for now
                    AddBattleEvent(
                        $"{actor.data.heroName} uses {readySkill.skillName} and recovers " +
                        $"{actor.currentHealth - healthBefore} HP ({actor.currentHealth}/{actor.maxHealth})");
                }
                else
                {
                    int healthBefore = target.currentHealth;
                    CombatManager.PerformSkill(actor, target, readySkill);
                    AddBattleEvent(
                        $"{actor.data.heroName} uses {readySkill.skillName} on {target.data.heroName} " +
                        $"for {healthBefore - target.currentHealth} damage " +
                        $"({target.data.heroName} HP: {target.currentHealth}/{target.maxHealth})");
                }
            }
            else
            {
                int damage = CombatManager.CalculateDamage(actor, target);
                CombatManager.PerformAttack(actor, target);
                AddBattleEvent(
                    $"{actor.data.heroName} attacks {target.data.heroName} for {damage} damage " +
                    $"({target.data.heroName} HP: {target.currentHealth}/{target.maxHealth})");
            }

            turnCount++;
        }

        battleEnded = true;
        if (hud != null)
        {
            hud.SetActiveHero(null);
        }

        if (!hero1.isAlive && !hero2.isAlive)
        {
            winnerName = "Nobody";
            Debug.LogWarning("Both heroes are down - the battle ends in a draw.");
        }
        else if (hero1.isAlive && hero2.isAlive)
        {
            winnerName = "Nobody";
            Debug.LogWarning($"Battle hit the {MaxTurns}-turn safety limit and ended in a draw.");
        }
        else
        {
            HeroInstance winner = hero1.isAlive ? hero1 : hero2;
            winnerName = winner.data.heroName;
            Debug.Log($"{winnerName} wins the battle!");
        }
    }

    /// <summary>
    /// Runs a deterministic 3v3 battle as a coroutine: three HeroInstances per
    /// team built from the Inspector's HeroData templates (the same asset may
    /// be used for all six), one second between turns, first ready skill
    /// preferred over the basic attack, first living enemy targeted (heals
    /// target self), until a team is wiped or the safety cap is hit.
    /// </summary>
    public IEnumerator RunTeamBattleRoutine()
    {
        battleRunning = true;

        bool heroKit = attackHeroData != null && defenseHeroData != null && supportHeroData != null;
        if (!heroKit && (hero1Data == null || hero2Data == null))
        {
            Debug.LogWarning("BattleTestRunner: assign both HeroData assets in the Inspector before starting the battle.");
            battleEnded = true;
            winnerName = "Nobody";
            battleRunning = false;
            if (menu != null)
            {
                menu.gameObject.SetActive(true);
            }

            yield break;
        }

        if (heroKit)
        {
            // Prototype battle: the player's team fields the roster's real
            // instances (as combat clones carrying live progression - see
            // BuildPlayerTeam); the enemy team fields fresh copies of the
            // same three named heroes, one per role.
            team1 = BuildPlayerTeam();
            team2 = BuildMixedTeam(attackHeroData, defenseHeroData, supportHeroData, prefix: "Enemy ");
        }
        else
        {
            // Legacy mirrored 3v3 kept as a test fixture.
            team1 = BuildTeam(hero1Data, prefix: string.Empty);
            team2 = BuildTeam(hero2Data, prefix: "Enemy ");
            if (testHealSkill != null)
            {
                foreach (HeroInstance member in team2.Members)
                {
                    member.skills.Add(testHealSkill);
                }
            }
        }

        // Test-only progression hook: level Team 1 through the real
        // progression API so leveled combat can be observed before any
        // reward or save system exists.
        if (teamOneBonusExperience > 0)
        {
            foreach (HeroInstance hero in team1.Members)
            {
                int levelsGained = hero.AddExperience(teamOneBonusExperience);
                if (levelsGained > 0)
                {
                    Debug.Log($"BattleTestRunner: {hero.displayName} is now level {hero.Level} (max {hero.MaxLevel}).");
                }
            }
        }

        teamBattle = new TeamBattle(team1, team2);
        battleLog.Clear();
        battleEnded = false;
        winnerName = string.Empty;
        battleSpeed = 1; // every battle starts at x1; the HUD resyncs its SPEED toggle
        autoBattle = true; // ...and in AUTO; the HUD resyncs its AUTO toggle
        ClearManualTurn();

        while (!teamBattle.BattleOver)
        {
            // Per-turn breather at the current speed (the x1 interval
            // divided by the multiplier) so the fight unfolds visibly;
            // pausing still freezes this wait mid-flight via Time.timeScale.
            yield return new WaitForSeconds(SecondsPerTurn / battleSpeed);

            // Pause gate: while the HUD's pause menu is open, hold here
            // without resolving a turn. Pausing also sets Time.timeScale to
            // 0, which freezes the wait above mid-flight; this gate covers
            // the frame the pause landed between the wait and the turn.
            while (battlePaused && !teamBattle.BattleOver)
            {
                yield return null;
            }

            HeroInstance actor = teamBattle.GetNextActor();
            if (actor == null)
            {
                break;
            }

            // Presentation only: highlight the acting hero in the HUD.
            if (hud != null)
            {
                hud.SetActiveHero(actor);
            }

            // MANUAL (AUTO OFF): player-team heroes park their turn until
            // the HUD submits an action; toggling AUTO ON mid-wait lets the
            // AI take over instead. Enemy heroes (and every hero in AUTO)
            // always take the unchanged AI path below.
            bool manualTurn = !autoBattle && team1.Contains(actor);
            if (manualTurn)
            {
                BeginManualTurn(actor);

                // Hold the turn: nothing resolves while we wait. Pause needs
                // no gate here (the pause overlay blocks the clicks that
                // would submit an action), but a submitted action is gated
                // on pause below so nothing ever resolves while frozen.
                while (!teamBattle.BattleOver && !autoBattle && !manualActionReady)
                {
                    yield return null;
                }

                if (manualActionReady)
                {
                    // Pause safety: never execute a submitted action while
                    // the battle is frozen.
                    while (battlePaused && !teamBattle.BattleOver)
                    {
                        yield return null;
                    }
                }
            }

            string battleEvent = manualTurn && manualActionReady
                ? teamBattle.PerformTurn(actor, manualSkill, manualTarget)
                : teamBattle.PerformTurn(actor);
            ClearManualTurn();
            if (battleEvent != null)
            {
                AddBattleEvent(battleEvent);
            }
        }

        battleEnded = true;
        if (hud != null)
        {
            hud.SetActiveHero(null);
        }

        if (ReferenceEquals(teamBattle.Winner, team1))
        {
            winnerName = "Team 1";
        }
        else if (ReferenceEquals(teamBattle.Winner, team2))
        {
            winnerName = "Team 2";
        }
        else
        {
            winnerName = "Nobody";
        }

        if (winnerName == "Nobody")
        {
            Debug.LogWarning($"Battle hit the {TeamBattle.MaxTurns}-turn safety limit and ended in a draw.");
        }
        else
        {
            Debug.Log($"{winnerName} wins the battle!");
        }

        battleRunning = false;
        battleCoroutine = null;

        // Return to the main menu so the whole flow (menu -> battle -> menu)
        // can be replayed within one Play session; the result banner stays
        // behind the menu canvas, which sorts above the HUD.
        if (menu != null)
        {
            menu.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Builds a team of three independent HeroInstances from one HeroData
    /// template, labeling each instance ("Hero 1", "Enemy Hero 2", ...)
    /// so look-alike heroes stay readable in logs and the HUD.
    /// </summary>
    private static Team BuildTeam(HeroData template, string prefix)
    {
        var team = new Team();
        for (int i = 0; i < Team.MaxSize; i++)
        {
            var hero = new HeroInstance(template);
            hero.displayName = $"{prefix}{template.heroName} {i + 1}";
            team.Add(hero);
        }

        return team;
    }

    /// <summary>
    /// Builds a prototype team from three hero templates, one per role.
    /// Labels are the heroes' own data names with the team prefix.
    ///
    /// Slot order is deliberate fixture configuration: combat always hits
    /// the first living enemy, so the defense hero leads and absorbs the
    /// early focus, the support hero follows (its heals land on the damaged
    /// frontliner), and the attack hero - the only one whose damage breaks
    /// tanky defense - closes the fight. An attack-hero-first lineup gets
    /// both attackers killed immediately, leaving an unkillable tanky
    /// mirror (a few points of damage per two rounds against double-digit
    /// healing), so the fixture could never reach a real winner.
    /// </summary>
    private static Team BuildMixedTeam(HeroData attack, HeroData defense, HeroData support, string prefix)
    {
        var team = new Team();
        AddLabeled(team, defense, prefix);
        AddLabeled(team, support, prefix);
        AddLabeled(team, attack, prefix);
        return team;
    }

    /// <summary>Creates one HeroInstance from the template and labels it for the logs.</summary>
    private static void AddLabeled(Team team, HeroData template, string prefix)
    {
        var hero = new HeroInstance(template);
        hero.displayName = $"{prefix}{template.heroName}";
        team.Add(hero);
    }

    /// <summary>Records a battle event, most recent first, keeping only the last five.</summary>
    private void AddBattleEvent(string message)
    {
        battleLog.Insert(0, message);
        if (battleLog.Count > MaxLogEntries)
        {
            battleLog.RemoveAt(battleLog.Count - 1);
        }
    }
}
