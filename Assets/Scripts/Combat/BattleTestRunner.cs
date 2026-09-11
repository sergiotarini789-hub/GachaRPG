using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene test component: attach to a GameObject, assign two HeroData assets in
/// the Inspector, and press Play. Builds a HeroInstance for each, lets a
/// <see cref="TurnManager"/> decide turn order on the speed bar, and has each
/// acting unit attack the other until one falls (or the safety turn limit is
/// reached).
///
/// The battle runs as a coroutine with a one-second pause between turns so it
/// can be watched, and <see cref="OnGUI"/> draws a simple runtime HUD (no
/// Canvas needed): health bars for both heroes, the five most recent combat
/// events, and a large banner announcing the winner.
/// </summary>
public class BattleTestRunner : MonoBehaviour
{
    /// <summary>First combatant's HeroData asset (assign in the Inspector).</summary>
    public HeroData hero1Data;

    /// <summary>Second combatant's HeroData asset (assign in the Inspector).</summary>
    public HeroData hero2Data;

    /// <summary>Hard cap on loop iterations so a battle can never hang Play mode.</summary>
    private const int MaxTurns = 100;

    /// <summary>Real-time seconds to pause between turns so the fight is watchable.</summary>
    private const float SecondsPerTurn = 1f;

    /// <summary>How many recent battle events to keep for the on-screen log.</summary>
    private const int MaxLogEntries = 5;

    /// <summary>Runtime instance for hero1's side; kept as a field so OnGUI can draw its health bar.</summary>
    private HeroInstance hero1;

    /// <summary>Runtime instance for hero2's side; kept as a field so OnGUI can draw its health bar.</summary>
    private HeroInstance hero2;

    /// <summary>Last few attack messages, most recent first.</summary>
    private readonly List<string> battleLog = new List<string>();

    /// <summary>True once the battle finished (win, draw, or safety limit).</summary>
    private bool battleEnded;

    /// <summary>Name shown in the end-of-battle banner ("Nobody" on a draw).</summary>
    private string winnerName = string.Empty;

    // Cached IMGUI styles, built lazily inside OnGUI at readable font sizes.
    private GUIStyle nameStyle;
    private GUIStyle hpStyle;
    private GUIStyle logHeaderStyle;
    private GUIStyle logStyle;
    private GUIStyle winnerStyle;

    private void Start()
    {
        StartCoroutine(RunBattleRoutine());
    }

    /// <summary>
    /// Runs the full battle as a coroutine: one attack per
    /// <see cref="SecondsPerTurn"/> interval until a hero dies or the safety
    /// limit is reached. Public so tests and other scripts can drive it.
    /// </summary>
    public IEnumerator RunBattleRoutine()
    {
        if (hero1Data == null || hero2Data == null)
        {
            Debug.LogWarning("BattleTestRunner: assign both HeroData assets in the Inspector before starting the battle.");
            battleEnded = true;
            winnerName = "Nobody";
            yield break;
        }

        hero1 = new HeroInstance(hero1Data);
        hero2 = new HeroInstance(hero2Data);
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

            int damage = CombatManager.CalculateDamage(actor, target);
            CombatManager.PerformAttack(actor, target);
            AddBattleEvent(
                $"{actor.data.heroName} attacks {target.data.heroName} for {damage} damage. " +
                $"({target.data.heroName} HP: {target.currentHealth}/{target.data.baseHealth})");
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
    /// Simple immediate-mode HUD: hero panels (name, red health bar scaled by
    /// current/max health, and "HP: X/Y" text) on the left and right of the
    /// screen, the last five battle events along the bottom, and a large
    /// centered "{winner} wins!" banner once the battle has ended.
    /// </summary>
    private void OnGUI()
    {
        if (hero1 == null || hero2 == null)
        {
            return;
        }

        EnsureStyles();

        float panelWidth = Mathf.Min(380f, Screen.width * 0.5f - 40f);
        DrawHeroPanel(hero1, new Rect(20f, 20f, panelWidth, 104f), TextAnchor.MiddleLeft);
        DrawHeroPanel(hero2, new Rect(Screen.width - panelWidth - 20f, 20f, panelWidth, 104f), TextAnchor.MiddleRight);
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

        GUI.Label(new Rect(rect.x, rect.y, rect.width, 32f), hero.data.heroName, nameStyle);

        Rect barRect = new Rect(rect.x, rect.y + 40f, rect.width, 28f);
        GUI.Box(barRect, string.Empty); // dark frame/background of the bar

        // Inner red fill scales with remaining health (max = baseHealth).
        float fill = Mathf.Clamp01((float)hero.currentHealth / Mathf.Max(1, hero.data.baseHealth));
        GUI.color = Color.red;
        GUI.Box(new Rect(barRect.x + 2f, barRect.y + 2f, (barRect.width - 4f) * fill, barRect.height - 4f), string.Empty);
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

    /// <summary>Builds the IMGUI styles once, at readable font sizes.</summary>
    private void EnsureStyles()
    {
        if (nameStyle != null)
        {
            return;
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
