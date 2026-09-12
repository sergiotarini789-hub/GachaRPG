using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated Canvas battle HUD for <see cref="BattleTestRunner"/>.
///
/// Everything is built from code in <see cref="Awake"/> - no Canvas, panel,
/// bar, or text has to be assembled in the Unity Editor:
/// - a Screen Space - Overlay <see cref="Canvas"/> with a
///   <see cref="CanvasScaler"/> (Scale With Screen Size, 1920x1080 reference
///   resolution);
/// - a dark-blue background covering the whole screen;
/// - one panel per team, each holding a row per hero (name, health bar, HP
///   text), with names right-aligned on the right team like the old HUD;
/// - a bottom-center log panel with the
///   <see cref="BattleTestRunner.MaxLogEntries"/> most recent battle events;
/// - a large centered result banner, hidden until the battle ends:
///   "Victory!" when team 1 wins, "Defeat!" when team 2 wins, "Draw!"
///   when nobody does;
/// - dimmed rows for fallen heroes, so a dead hero never reads as still
///   fighting.
///
/// This is a pure view layer: every <see cref="Update"/> it only READS the
/// runner's public state (teams, heroes' health, the battle log, the end
/// result). It keeps no duplicate battle state, never mutates combat data,
/// and never drives battle flow - the coroutines in
/// <see cref="BattleTestRunner"/> keep running exactly as before.
/// </summary>
public class BattleHud : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Appearance/layout constants - the HUD is generated from code, so these
    // live here instead of the Inspector. All sizes assume the 1920x1080
    // reference resolution and scale with the CanvasScaler.
    // ---------------------------------------------------------------------

    /// <summary>Full-screen dark-blue backdrop behind the whole HUD.</summary>
    private static readonly Color BackgroundColor = new Color(0.055f, 0.075f, 0.145f);

    /// <summary>Panel behind one team's hero rows.</summary>
    private static readonly Color TeamPanelColor = new Color(0.09f, 0.12f, 0.22f, 0.95f);

    /// <summary>Translucent panel behind the battle log.</summary>
    private static readonly Color LogPanelColor = new Color(0f, 0f, 0f, 0.55f);

    /// <summary>Empty "track" of a health bar.</summary>
    private static readonly Color BarTrackColor = new Color(0.12f, 0.12f, 0.12f, 1f);

    /// <summary>Bright red "current health" fill of a health bar.</summary>
    private static readonly Color BarFillColor = new Color(0.95f, 0.15f, 0.15f, 1f);

    /// <summary>Translucent band behind the end-of-battle banner text.</summary>
    private static readonly Color BannerBandColor = new Color(0f, 0f, 0f, 0.65f);

    /// <summary>Result banner text color when the player's side (team 1) wins.</summary>
    private static readonly Color VictoryColor = new Color(0.36f, 0.9f, 0.45f);

    /// <summary>Result banner text color when the enemy side (team 2) wins.</summary>
    private static readonly Color DefeatColor = new Color(0.95f, 0.3f, 0.3f);

    /// <summary>Top-left anchor point, shared by most child rects.</summary>
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    /// <summary>Reference width the CanvasScaler scales against.</summary>
    private const float ReferenceWidth = 1920f;

    /// <summary>Reference height the CanvasScaler scales against.</summary>
    private const float ReferenceHeight = 1080f;

    /// <summary>Screen-edge margin for the team panels and the log panel.</summary>
    private const float ScreenMargin = 24f;

    /// <summary>Width of one team panel.</summary>
    private const float PanelWidth = 560f;

    /// <summary>Height of one team panel: three rows plus padding.</summary>
    private const float PanelHeight = 444f;

    /// <summary>Inner padding of a team panel.</summary>
    private const float PanelPadding = 20f;

    /// <summary>Vertical stride of one hero row inside a team panel.</summary>
    private const float RowStride = 136f;

    /// <summary>Usable width of a hero row (panel width minus padding).</summary>
    private const float RowWidth = PanelWidth - 2f * PanelPadding;

    /// <summary>Width of the bottom-center battle log panel.</summary>
    private const float LogPanelWidth = 960f;

    /// <summary>Height of the bottom-center battle log panel.</summary>
    private const float LogPanelHeight = 254f;

    /// <summary>Height of the horizontal result banner band.</summary>
    private const float BannerHeight = 130f;

    /// <summary>Row alpha applied to fallen heroes, so their rows read clearly as inactive.</summary>
    private const float DeadRowAlpha = 0.4f;

    /// <summary>Font size of a hero name.</summary>
    private const int HeroNameFontSize = 30;

    /// <summary>Font size of a hero's HP text.</summary>
    private const int HpFontSize = 24;

    /// <summary>Font size of the battle log header.</summary>
    private const int LogHeaderFontSize = 26;

    /// <summary>Font size of one battle log line.</summary>
    private const int LogLineFontSize = 20;

    /// <summary>Font size of the victory banner.</summary>
    private const int BannerFontSize = 54;

    // ---------------------------------------------------------------------
    // Runtime state: views built once in Awake plus cached values so the
    // per-frame refresh does near-zero work when nothing changed.
    // ---------------------------------------------------------------------

    /// <summary>The battle this HUD renders; assigned by <see cref="Create"/> right after the component is added.</summary>
    private BattleTestRunner runner;

    /// <summary>Unity's built-in legacy font (Arial.ttf no longer ships with Unity 6).</summary>
    private Font font;

    /// <summary>
    /// Shared plain-white sprite for every health-bar fill. A Filled Image
    /// only applies its fillAmount when it has a sprite; a sprite-less Image
    /// falls back to a plain full quad, which is why the bars never shrank.
    /// </summary>
    private Sprite fillSprite;

    /// <summary>Rows currently shown on the left side (team 1, or the 1v1 hero).</summary>
    private readonly List<HeroRow> leftRows = new List<HeroRow>();

    /// <summary>Rows currently shown on the right side (team 2, or the 1v1 hero).</summary>
    private readonly List<HeroRow> rightRows = new List<HeroRow>();

    /// <summary>Left team panel; hidden until a roster is bound.</summary>
    private RectTransform leftPanel;

    /// <summary>Right team panel; hidden until a roster is bound.</summary>
    private RectTransform rightPanel;

    /// <summary>Battle-log line views, top line = most recent event.</summary>
    private Text[] logLines;

    /// <summary>Last log entry handed to each line view, so unchanged frames skip text updates.</summary>
    private readonly string[] cachedLogLines = new string[BattleTestRunner.MaxLogEntries];

    /// <summary>Centered end-of-battle banner; hidden until the battle ends.</summary>
    private GameObject resultBanner;

    /// <summary>Large result text inside the banner band.</summary>
    private Text resultText;

    /// <summary>Team most recently bound to the panels (rebinds when a new 3v3 starts).</summary>
    private Team boundTeam1;

    /// <summary>Team most recently bound to the panels (rebinds when a new 3v3 starts).</summary>
    private Team boundTeam2;

    /// <summary>Hero most recently bound for the legacy 1v1 view.</summary>
    private HeroInstance boundHero1;

    /// <summary>Hero most recently bound for the legacy 1v1 view.</summary>
    private HeroInstance boundHero2;

    /// <summary>Cached banner visibility so unchanged frames do no work.</summary>
    private bool bannerVisible;

    /// <summary>Result text currently on the banner, so unchanged frames skip text updates.</summary>
    private string cachedBannerText;

    /// <summary>
    /// Creates the HUD for the given runner: a new "BattleHud" GameObject
    /// whose Canvas hierarchy builds itself in <see cref="Awake"/>. Called by
    /// <see cref="BattleTestRunner.Start"/> so the scene needs no manual UI
    /// setup.
    /// </summary>
    public static BattleHud Create(BattleTestRunner runner)
    {
        GameObject host = new GameObject("BattleHud");
        BattleHud hud = host.AddComponent<BattleHud>();
        hud.runner = runner;
        return hud;
    }

    /// <summary>Builds the whole Canvas hierarchy once; battle state is only read later, in <see cref="Update"/>.</summary>
    private void Awake()
    {
        // Unity 6 ships the legacy built-in font as "LegacyRuntime.ttf"
        // (Arial.ttf was removed from the engine in Unity 2022.2+).
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Image.type = Filled only honors fillAmount when the Image has a
        // sprite - without one, Image.OnPopulateMesh falls back to a plain
        // full quad and every bar renders 100% full regardless of health.
        // One shared white sprite (from Unity's built-in white texture) is
        // all a solid-color fill needs; each Image tints it with its color.
        fillSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = (RectTransform)transform;

        CreateStretchedImage(root, "Background", BackgroundColor);

        leftPanel = CreateTeamPanel(root, "TeamPanelLeft", rightSide: false);
        rightPanel = CreateTeamPanel(root, "TeamPanelRight", rightSide: true);

        BuildLogPanel(root);
        BuildResultBanner(root);
    }

    /// <summary>
    /// Frees the runtime-created sprite. Sprites are not components, so one
    /// created with <see cref="Sprite.Create"/> does not die with the
    /// GameObject and would leak without an explicit Destroy.
    /// </summary>
    private void OnDestroy()
    {
        if (fillSprite != null)
        {
            Destroy(fillSprite);
        }
    }

    /// <summary>Reads the runner's current state and updates only what changed since the last frame.</summary>
    private void Update()
    {
        BindRosters();
        RefreshRows(leftRows);
        RefreshRows(rightRows);
        RefreshLog();
        RefreshBanner();
    }

    /// <summary>
    /// Binds the team panels to the runner's current roster: the 3v3 teams
    /// when they exist, the legacy 1v1 heroes otherwise, or hides the panels
    /// entirely when no battle could start (matching the old HUD, which drew
    /// nothing in that case). Rebuilds the rows only when the bound roster
    /// actually changes.
    /// </summary>
    private void BindRosters()
    {
        if (runner == null)
        {
            return;
        }

        Team nextTeam1 = runner.Team1;
        Team nextTeam2 = runner.Team2;

        if (nextTeam1 != null && nextTeam2 != null)
        {
            if (ReferenceEquals(boundTeam1, nextTeam1) && ReferenceEquals(boundTeam2, nextTeam2))
            {
                return;
            }

            boundTeam1 = nextTeam1;
            boundTeam2 = nextTeam2;
            boundHero1 = null;
            boundHero2 = null;
            RebuildRows(leftRows, leftPanel, nextTeam1.Members, rightSide: false);
            RebuildRows(rightRows, rightPanel, nextTeam2.Members, rightSide: true);
            return;
        }

        HeroInstance nextHero1 = runner.Hero1;
        HeroInstance nextHero2 = runner.Hero2;
        if (nextHero1 != null && nextHero2 != null)
        {
            if (ReferenceEquals(boundHero1, nextHero1) && ReferenceEquals(boundHero2, nextHero2))
            {
                return;
            }

            boundTeam1 = null;
            boundTeam2 = null;
            boundHero1 = nextHero1;
            boundHero2 = nextHero2;
            RebuildSoloRow(leftRows, leftPanel, nextHero1, rightSide: false);
            RebuildSoloRow(rightRows, rightPanel, nextHero2, rightSide: true);
            return;
        }

        // No roster at all (e.g. HeroData unassigned): show nothing.
        if (leftPanel.gameObject.activeSelf || rightPanel.gameObject.activeSelf)
        {
            boundTeam1 = null;
            boundTeam2 = null;
            boundHero1 = null;
            boundHero2 = null;
            ClearRows(leftRows);
            ClearRows(rightRows);
            leftPanel.gameObject.SetActive(false);
            rightPanel.gameObject.SetActive(false);
        }
    }

    /// <summary>Rebuilds one side's rows from a team's members (at most <see cref="Team.MaxSize"/>).</summary>
    private void RebuildRows(List<HeroRow> rows, RectTransform panel, IReadOnlyList<HeroInstance> heroes, bool rightSide)
    {
        ClearRows(rows);
        for (int i = 0; i < heroes.Count && i < Team.MaxSize; i++)
        {
            rows.Add(new HeroRow(this, panel, heroes[i], i, rightSide));
        }

        panel.gameObject.SetActive(rows.Count > 0);
    }

    /// <summary>Rebuilds one side's single row for the legacy 1v1 fixture.</summary>
    private void RebuildSoloRow(List<HeroRow> rows, RectTransform panel, HeroInstance hero, bool rightSide)
    {
        ClearRows(rows);
        rows.Add(new HeroRow(this, panel, hero, 0, rightSide));
        panel.gameObject.SetActive(true);
    }

    /// <summary>Destroys the row views of one side and forgets them.</summary>
    private static void ClearRows(List<HeroRow> rows)
    {
        foreach (HeroRow row in rows)
        {
            if (row != null && row.Root != null)
            {
                Destroy(row.Root.gameObject);
            }
        }

        rows.Clear();
    }

    /// <summary>Refreshes every row of one side.</summary>
    private static void RefreshRows(List<HeroRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Refresh();
        }
    }

    /// <summary>
    /// Copies the runner's battle log (most recent first) into the log lines,
    /// top line = most recent event, older lines below, as in the old HUD.
    /// Skips the update entirely while nothing changed.
    /// </summary>
    private void RefreshLog()
    {
        IReadOnlyList<string> log = runner != null ? runner.BattleLog : null;
        int count = log != null ? log.Count : 0;

        bool changed = false;
        for (int i = 0; i < cachedLogLines.Length; i++)
        {
            string entry = i < count ? log[i] : null;
            if (!ReferenceEquals(cachedLogLines[i], entry))
            {
                cachedLogLines[i] = entry;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        for (int i = 0; i < logLines.Length; i++)
        {
            logLines[i].text = cachedLogLines[i] ?? string.Empty;
        }
    }

    /// <summary>
    /// Shows the centered result banner once the battle has ended (and a
    /// roster was drawn - like the old HUD, no banner when the battle never
    /// started). The 3v3 result is read straight from the battle's winner:
    /// team 1 (the player's side) wins = "Victory!", team 2 wins =
    /// "Defeat!", no winner = "Draw!". The legacy 1v1 fixture keeps its
    /// "{hero} wins!" label.
    /// </summary>
    private void RefreshBanner()
    {
        bool show = runner != null && runner.BattleEnded && leftRows.Count > 0;
        if (show != bannerVisible)
        {
            bannerVisible = show;
            resultBanner.SetActive(show);
        }

        if (!show)
        {
            return;
        }

        string text;
        Color color;
        if (boundTeam1 != null)
        {
            Team winner = runner.WinningTeam;
            if (winner == null)
            {
                text = "Draw!";
                color = Color.white;
            }
            else if (ReferenceEquals(winner, boundTeam1))
            {
                text = "Victory!";
                color = VictoryColor;
            }
            else
            {
                text = "Defeat!";
                color = DefeatColor;
            }
        }
        else
        {
            text = runner.WinnerName + " wins!";
            color = Color.white;
        }

        // Assign only when the result itself changed, so a stale label from a
        // previous battle can never linger and idle frames do no work.
        if (text != cachedBannerText)
        {
            cachedBannerText = text;
            resultText.text = text;
            resultText.color = color;
        }
    }

    /// <summary>Creates one team's panel anchored to the top-left or top-right screen edge.</summary>
    private RectTransform CreateTeamPanel(RectTransform root, string name, bool rightSide)
    {
        Vector2 anchor = new Vector2(rightSide ? 1f : 0f, 1f);
        Image panel = CreateImage(root, name, TeamPanelColor, anchor, anchor,
            new Vector2(rightSide ? -ScreenMargin : ScreenMargin, -ScreenMargin),
            new Vector2(PanelWidth, PanelHeight));

        // Hidden until a roster is bound in Update.
        panel.gameObject.SetActive(false);
        return panel.rectTransform;
    }

    /// <summary>Creates the bottom-center battle log panel with its header and line views.</summary>
    private void BuildLogPanel(RectTransform root)
    {
        Image panel = CreateImage(root, "BattleLogPanel", LogPanelColor,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, ScreenMargin), new Vector2(LogPanelWidth, LogPanelHeight));
        RectTransform panelRect = panel.rectTransform;

        CreateText(panelRect, "Header",
            "Battle Log (last " + BattleTestRunner.MaxLogEntries + ")",
            LogHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(20f, -14f), new Vector2(LogPanelWidth - 40f, 34f));

        logLines = new Text[BattleTestRunner.MaxLogEntries];
        for (int i = 0; i < logLines.Length; i++)
        {
            // Wrapping keeps long event lines readable instead of running off
            // the panel; the fixed slot height truncates a rare second line.
            logLines[i] = CreateText(panelRect, "Line" + (i + 1), string.Empty,
                LogLineFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(20f, -(58f + i * 36f)), new Vector2(LogPanelWidth - 40f, 34f),
                wrap: true);
        }
    }

    /// <summary>Creates the centered result banner band and its text, hidden until the battle ends.</summary>
    private void BuildResultBanner(RectTransform root)
    {
        RectTransform bannerRect = CreateRect(root, "ResultBanner");
        bannerRect.anchorMin = new Vector2(0f, 0.5f);
        bannerRect.anchorMax = new Vector2(1f, 0.5f);
        bannerRect.pivot = new Vector2(0.5f, 0.5f);
        bannerRect.sizeDelta = new Vector2(0f, BannerHeight);

        Image band = bannerRect.gameObject.AddComponent<Image>();
        band.color = BannerBandColor;
        band.raycastTarget = false;

        // Anchors are irrelevant here - Stretch overrides them below.
        resultText = CreateText(bannerRect, "ResultText", string.Empty,
            BannerFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(resultText.rectTransform);

        resultBanner = bannerRect.gameObject;
        resultBanner.SetActive(false);
    }

    /// <summary>Creates a solid-color Image stretched across its parent.</summary>
    private Image CreateStretchedImage(RectTransform parent, string name, Color color)
    {
        RectTransform rect = CreateRect(parent, name);
        Stretch(rect);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>Creates a white <see cref="Text"/> using the built-in font at the given anchored rect.</summary>
    private Text CreateText(Transform parent, string name, string initialText, int fontSize, FontStyle fontStyle, TextAnchor alignment, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, bool wrap = false)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        text.verticalOverflow = wrap ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
        text.text = initialText ?? string.Empty;
        return text;
    }

    /// <summary>Creates a solid-color <see cref="Image"/> at the given anchored rect.</summary>
    private Image CreateImage(Transform parent, string name, Color color, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>Creates a plain child RectTransform (relative to its parent).</summary>
    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>Stretches a RectTransform across its parent.</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// One hero's view inside a team panel: name text, health bar, and HP
    /// text. Reads the live <see cref="HeroInstance"/> directly and caches
    /// the last drawn values so unchanged frames skip string work.
    /// </summary>
    private sealed class HeroRow
    {
        /// <summary>The live hero this row renders (never null).</summary>
        public readonly HeroInstance Hero;

        /// <summary>Root RectTransform under the team panel; destroyed when the row is rebuilt.</summary>
        public readonly RectTransform Root;

        private readonly Image fillImage;

        private readonly Text hpText;

        /// <summary>Dims the whole row when the hero falls, so dead heroes read clearly as out of the fight.</summary>
        private readonly CanvasGroup rowGroup;

        private int lastHealth = int.MinValue;

        private int lastMaxHealth = int.MinValue;

        private bool lastAlive = true;

        public HeroRow(BattleHud hud, RectTransform panel, HeroInstance hero, int slot, bool rightSide)
        {
            Hero = hero;
            TextAnchor alignment = rightSide ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

            Root = CreateRect(panel, "HeroRow" + (slot + 1));
            Root.anchorMin = TopLeft;
            Root.anchorMax = TopLeft;
            Root.pivot = TopLeft;
            Root.anchoredPosition = new Vector2(0f, -(PanelPadding + slot * RowStride));
            Root.sizeDelta = new Vector2(PanelWidth, RowStride);

            // Dims the entire row (name, bar, HP text) at once when the hero dies.
            rowGroup = Root.gameObject.AddComponent<CanvasGroup>();

            hud.CreateText(Root, "Name", hero.displayName, HeroNameFontSize, FontStyle.Bold, alignment,
                TopLeft, TopLeft, new Vector2(PanelPadding, 0f), new Vector2(RowWidth, 44f));

            RectTransform track = hud.CreateImage(Root, "HpBar", BarTrackColor,
                TopLeft, TopLeft, new Vector2(PanelPadding, -52f), new Vector2(RowWidth, 34f)).rectTransform;

            // The fill is a horizontally Filled Image, so the bar shrinks with
            // remaining health without any extra mesh or sprite work.
            RectTransform fill = CreateRect(track, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            fillImage = fill.gameObject.AddComponent<Image>();
            // The shared white sprite is what makes Filled rendering work:
            // a sprite-less Image draws a full quad and ignores fillAmount.
            fillImage.sprite = hud.fillSprite;
            fillImage.color = BarFillColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.raycastTarget = false;

            hpText = hud.CreateText(Root, "HpText", string.Empty, HpFontSize, FontStyle.Normal, alignment,
                TopLeft, TopLeft, new Vector2(PanelPadding, -98f), new Vector2(RowWidth, 34f));

            Refresh();
        }

        /// <summary>
        /// Updates the bar fill, HP text, and dead/alive look whenever the
        /// hero's health or living state changed since the last frame.
        /// </summary>
        public void Refresh()
        {
            int maxHealth = Hero.data != null ? Hero.maxHealth : 0;
            if (Hero.currentHealth == lastHealth && maxHealth == lastMaxHealth && Hero.isAlive == lastAlive)
            {
                return;
            }

            lastHealth = Hero.currentHealth;
            lastMaxHealth = maxHealth;
            lastAlive = Hero.isAlive;
            fillImage.fillAmount = maxHealth > 0 ? Mathf.Clamp01((float)Hero.currentHealth / maxHealth) : 0f;
            hpText.text = "HP: " + Hero.currentHealth + "/" + maxHealth;

            // Fallen heroes dim so their row clearly reads as dead, while the
            // empty bar and 0 HP text show why.
            rowGroup.alpha = Hero.isAlive ? 1f : DeadRowAlpha;
        }
    }
}
