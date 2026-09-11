using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene test harness: attach to a GameObject and assign two HeroData assets
/// in the Inspector (the same asset is fine for all six heroes). Press Play
/// to run a deterministic 3v3 battle: three HeroInstances per team, turn
/// order by highest current Speed each round, first ready skill preferred
/// over the basic attack, first living enemy targeted (heals target self).
///
/// <see cref="OnGUI"/> draws a simple development HUD (no Canvas needed):
/// three health bars per side, the five most recent combat events, and a
/// large banner announcing the winning team. The original 1v1 battle
/// routine is kept unchanged as a minimal regression fixture.
/// </summary>
public class BattleTestRunner : MonoBehaviour
{
    /// <summary>Team 1's HeroData template - each slot spawns its own HeroInstance (assign in the Inspector).</summary>
    public HeroData hero1Data;

    /// <summary>Team 2's HeroData template - each slot spawns its own HeroInstance (assign in the Inspector).</summary>
    public HeroData hero2Data;

    /// <summary>Hard cap on 1v1 loop iterations so a battle can never hang Play mode.</summary>
    private const int MaxTurns = 100;

    /// <summary>Real-time seconds to pause between turns so the fight is watchable.</summary>
    private const float SecondsPerTurn = 1f;

    /// <summary>How many recent battle events to keep for the on-screen log.</summary>
    private const int MaxLogEntries = 5;

    /// <summary>1v1 runner's left hero; kept as a field so OnGUI can draw its health bar.</summary>
    private HeroInstance hero1;

    /// <summary>1v1 runner's right hero; kept as a field so OnGUI can draw its health bar.</summary>
    private HeroInstance hero2;

    /// <summary>Team 1 of the 3v3 battle; kept as a field so OnGUI can draw its heroes' health bars.</summary>
    private Team team1;

    /// <summary>Team 2 of the 3v3 battle; kept as a field so OnGUI can draw its heroes' health bars.</summary>
    private Team team2;

    /// <summary>The running 3v3 battle; drives turn selection and the victory check.</summary>
    private TeamBattle teamBattle;

    /// <summary>Last few battle events, most recent first.</summary>
    private readonly List<string> battleLog = new List<string>();

    /// <summary>True once the battle finished (win, draw, or safety limit).</summary>
    private bool battleEnded;

    /// <summary>Name shown in the end-of-battle banner ("Nobody" on a draw).</summary>
    private string winnerName = string.Empty;

    // Cached IMGUI resources, built lazily inside OnGUI.
    private GUIStyle nameStyle;
    private GUIStyle hpStyle;
    private GUIStyle logHeaderStyle;
    private GUIStyle logStyle;
    private GUIStyle winnerStyle;

    // 1x1 solid white texture. Tinting GUI.Box darkens colors (its texture is
    // multiplied in), which made the red health fill nearly invisible - a
    // tinted DrawTexture of pure white renders the exact color instead.
    private Texture2D whiteTexture;

    private void Start()
    {
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
                    CombatManager.PerformSkill(actor, target, readySkill);
                    AddBattleEvent(
                        $"{actor.data.heroName} uses {readySkill.skillName} and recovers " +
                        $"{actor.currentHealth - healthBefore} HP ({actor.currentHealth}/{actor.data.baseHealth})");
                }
                else
                {
                    int healthBefore = target.currentHealth;
                    CombatManager.PerformSkill(actor, target, readySkill);
                    AddBattleEvent(
                        $"{actor.data.heroName} uses {readySkill.skillName} on {target.data.heroName} " +
                        $"for {healthBefore - target.currentHealth} damage " +
                        $"({target.data.heroName} HP: {target.currentHealth}/{target.data.baseHealth})");
                }
            }
            else
            {
                int damage = CombatManager.CalculateDamage(actor, target);
                CombatManager.PerformAttack(actor, target);
                AddBattleEvent(
                    $"{actor.data.heroName} attacks {target.data.heroName} for {damage} damage " +
                    $"({target.data.heroName} HP: {target.currentHealth}/{target.data.baseHealth})");
            }

            turnCount++;
        }

        battleEnded = true;
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
        if (hero1Data == null || hero2Data == null)
        {
            Debug.LogWarning("BattleTestRunner: assign both HeroData assets in the Inspector before starting the battle.");
            battleEnded = true;
            winnerName = "Nobody";
            yield break;
        }

        team1 = BuildTeam(hero1Data, prefix: string.Empty);
        team2 = BuildTeam(hero2Data, prefix: "Enemy ");
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

            string battleEvent = teamBattle.PerformTurn(actor);
            if (battleEvent != null)
            {
                AddBattleEvent(battleEvent);
            }
        }

        battleEnded = true;
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
    }

    /// <summary>
    /// Builds a team of three independent HeroInstances from one HeroData
    /// template, labeling each instance ("Warrior 1", "Enemy Warrior 2", ...)
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

    /// <summary>Records a battle event, most recent first, keeping only the last five.</summary>
    private void AddBattleEvent(string message)
    {
        battleLog.Insert(0, message);
        if (battleLog.Count > MaxLogEntries)
        {
            battleLog.RemoveAt(battleLog.Count - 1);
        }
    }

    /// <summary>
    /// Simple immediate-mode HUD: 3v3 shows each team's three hero panels
    /// stacked on its side of the screen; the legacy 1v1 view shows one panel
    /// per side. Below both, the last five battle events, and a large
    /// centered "{winner} wins!" banner once the battle has ended.
    /// </summary>
    private void OnGUI()
    {
        if (team1 != null && team2 != null)
        {
            DrawTeamHud();
            return;
        }

        if (hero1 == null || hero2 == null)
        {
            return;
        }

        EnsureStyles();
        float panelWidth = Mathf.Min(380f, Screen.width * 0.5f - 40f);
        DrawHeroPanel(hero1, new Rect(20f, 20f, panelWidth, 104f), TextAnchor.MiddleLeft);
        DrawHeroPanel(hero2, new Rect(Screen.width - panelWidth - 20f, 20f, panelWidth, 104f), TextAnchor.MiddleRight);
        DrawSharedHud();
    }

    /// <summary>
    /// 3v3 HUD: team 1's three heroes stacked on the left, team 2's on the
    /// right. Same panel style as the 1v1 view, just stacked to fit six.
    /// </summary>
    private void DrawTeamHud()
    {
        EnsureStyles();
        float panelWidth = Mathf.Min(380f, Screen.width * 0.5f - 40f);
        for (int i = 0; i < team1.Members.Count; i++)
        {
            Rect rect = new Rect(20f, 20f + i * 112f, panelWidth, 104f);
            DrawHeroPanel(team1.Members[i], rect, TextAnchor.MiddleLeft);
        }

        for (int i = 0; i < team2.Members.Count; i++)
        {
            Rect rect = new Rect(Screen.width - panelWidth - 20f, 20f + i * 112f, panelWidth, 104f);
            DrawHeroPanel(team2.Members[i], rect, TextAnchor.MiddleRight);
        }

        DrawSharedHud();
    }

    /// <summary>Draws the battle log and the winner banner shared by both HUD modes.</summary>
    private void DrawSharedHud()
    {
        DrawBattleLog();

        if (battleEnded)
        {
            Rect band = new Rect(0f, Screen.height * 0.5f - 55f, Screen.width, 110f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Box(band, string.Empty);
            GUI.color = Color.white;
            GUI.Label(band, $"{winnerName} wins!", winnerStyle);
        }
    }

    /// <summary>Draws one hero's name, scaled red health bar, and HP text inside the given rect.</summary>
    private void DrawHeroPanel(HeroInstance hero, Rect rect, TextAnchor alignment)
    {
        nameStyle.alignment = alignment;
        hpStyle.alignment = alignment;

        GUI.Label(new Rect(rect.x, rect.y, rect.width, 32f), hero.displayName, nameStyle);

        Rect barRect = new Rect(rect.x, rect.y + 40f, rect.width, 28f);

        // Thin white frame so the bar reads against any background.
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(barRect.x - 2f, barRect.y - 2f, barRect.width + 4f, barRect.height + 4f), whiteTexture);

        // Dark track behind the fill.
        GUI.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        GUI.DrawTexture(barRect, whiteTexture);

        // Bright red fill that shrinks proportionally with remaining health
        // (max health = baseHealth). Drawn as a tinted white texture so the
        // color stays vivid; tinted GUI.Box was rendering almost black.
        float fill = Mathf.Clamp01((float)hero.currentHealth / Mathf.Max(1, hero.data.baseHealth));
        GUI.color = new Color(0.95f, 0.15f, 0.15f, 1f);
        GUI.DrawTexture(new Rect(barRect.x + 2f, barRect.y + 2f, (barRect.width - 4f) * fill, barRect.height - 4f), whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(rect.x, rect.y + 74f, rect.width, 26f), $"HP: {hero.currentHealth}/{hero.data.baseHealth}", hpStyle);
    }

    /// <summary>Draws the last few battle events in a translucent panel at the bottom of the screen.</summary>
    private void DrawBattleLog()
    {
        const float lineHeight = 28f;
        float width = Mathf.Min(760f, Screen.width - 40f);
        float height = 40f + MaxLogEntries * lineHeight;
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height - height - 20f;

        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.Box(new Rect(x, y, width, height), string.Empty);
        GUI.color = Color.white;

        GUI.Label(new Rect(x + 12f, y + 6f, width - 24f, 28f), $"Battle Log (last {MaxLogEntries})", logHeaderStyle);
        for (int i = 0; i < battleLog.Count; i++)
        {
            GUI.Label(new Rect(x + 12f, y + 40f + i * lineHeight, width - 24f, lineHeight), battleLog[i], logStyle);
        }
    }

    /// <summary>Builds the IMGUI styles and shared textures once, at readable sizes.</summary>
    private void EnsureStyles()
    {
        if (nameStyle != null)
        {
            return;
        }

        if (whiteTexture == null)
        {
            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        nameStyle = MakeStyle(24, FontStyle.Bold);
        hpStyle = MakeStyle(20, FontStyle.Normal);
        logHeaderStyle = MakeStyle(22, FontStyle.Bold);
        logStyle = MakeStyle(20, FontStyle.Normal);
        winnerStyle = MakeStyle(52, FontStyle.Bold, TextAnchor.MiddleCenter);
    }

    private static GUIStyle MakeStyle(int fontSize, FontStyle fontStyle, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        return new GUIStyle
        {
            fontSize = fontSize,
            fontStyle = fontStyle,
            alignment = alignment,
            normal = new GUIStyleState { textColor = Color.white },
        };
    }
}
