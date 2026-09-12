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

    /// <summary>Real-time seconds to pause between turns so the fight is watchable.</summary>
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

    /// <summary>True while the 3v3 coroutine is resolving; guards against double starts from the menu.</summary>
    private bool battleRunning;

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
        if (startWithMainMenu)
        {
            // The runtime main menu is the entry point; the battle starts
            // from its BATTLE button (see StartBattleFromMenu).
            menu = MainMenu.Create(this);
            return;
        }

        // Direct-battle mode: identical to the original flow.
        hud = BattleHud.Create(this);
        StartCoroutine(RunTeamBattleRoutine());
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

        StartCoroutine(RunTeamBattleRoutine());
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

        int turnCount = 0;
        while (hero1.isAlive && hero2.isAlive && turnCount < MaxTurns)
        {
            // One-second breather between turns so the fight unfolds visibly.
            yield return new WaitForSeconds(SecondsPerTurn);

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
            // Prototype battle: both teams field the same three named heroes,
            // one per role; slot order is set inside BuildMixedTeam.
            team1 = BuildMixedTeam(attackHeroData, defenseHeroData, supportHeroData, prefix: string.Empty);
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

        while (!teamBattle.BattleOver)
        {
            // One-second breather between turns so the fight unfolds visibly.
            yield return new WaitForSeconds(SecondsPerTurn);

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

            string battleEvent = teamBattle.PerformTurn(actor);
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
