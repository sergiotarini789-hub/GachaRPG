using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated Canvas battle HUD for <see cref="BattleTestRunner"/>,
/// styled as a mobile hero-collection RPG battle screen.
///
/// Everything is built from code in <see cref="Awake"/> - no Canvas, panel,
/// bar, or text has to be assembled in the Unity Editor:
/// - a Screen Space - Overlay <see cref="Canvas"/> with a
///   <see cref="CanvasScaler"/> (Scale With Screen Size, 1920x1080 reference
///   resolution);
/// - a dark fantasy backdrop covering the whole screen;
/// - one hero panel per team flanking the screen - the player's team on the
///   left, the enemy team on the right - each holding one slot per hero:
///   portrait frame (rarity-colored), hero name, level badge, rarity badge,
///   health bar with current/max HP, and alive/dead visuals;
/// - a compact battle log strip at the top-center showing the
///   <see cref="BattleTestRunner.MaxLogEntries"/> most recent events;
/// - a bottom action bar: the currently acting hero, a basic-attack slot,
///   three skill slots fed from the acting hero's kit, and AUTO / SPEED
///   placeholder toggles (visual only - they drive no combat logic);
/// - a centered result banner, hidden until the battle ends: "Victory!" when
///   team 1 wins, "Defeat!" when team 2 wins, "Draw!" when nobody does;
/// - a PAUSE button beside the battle log: it freezes the battle (turn
///   pacing only, runner-owned) behind a full-screen pause overlay with
///   RESUME and EXIT BATTLE, the latter guarded by a LEAVE BATTLE?
///   confirmation.
///
/// Hero presentation states are pure UI: alive, dead (dimmed row, dark
/// portrait, empty bar), currently acting (pulsing gold frame - set through
/// <see cref="SetActiveHero"/>), and selected target (red frame - set by
/// clicking an enemy slot or through <see cref="SetSelectedTarget"/>).
///
/// This is a pure view layer: every <see cref="Update"/> it only READS the
/// runner's public state (teams, heroes' health/level, the battle log, the
/// end result) plus the two UI-only references it was handed (acting hero,
/// selected target). It keeps no duplicate battle state, never mutates
/// combat data, and never drives battle flow - the coroutines in
/// <see cref="BattleTestRunner"/> keep running exactly as before.
/// </summary>
public class BattleHud : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Appearance/layout constants - the HUD is generated from code, so these
    // live here instead of the Inspector. All sizes assume the 1920x1080
    // reference resolution and scale with the CanvasScaler.
    // ---------------------------------------------------------------------

    /// <summary>Full-screen dark-navy backdrop behind the whole HUD.</summary>
    private static readonly Color BackgroundColor = new Color(0.045f, 0.055f, 0.10f);

    /// <summary>Panel behind one team's hero slots.</summary>
    private static readonly Color TeamPanelColor = new Color(0.09f, 0.115f, 0.19f, 0.94f);

    /// <summary>Backdrop of a single hero slot.</summary>
    private static readonly Color SlotColor = new Color(0.125f, 0.16f, 0.25f, 0.96f);

    /// <summary>Inner portrait backdrop (behind the icon or initial).</summary>
    private static readonly Color PortraitBackColor = new Color(0.05f, 0.06f, 0.10f, 1f);

    /// <summary>Player-side accent (gold): panel edge, HP fill, header.</summary>
    private static readonly Color PlayerAccent = new Color(0.85f, 0.72f, 0.35f);

    /// <summary>Enemy-side accent (red): panel edge, HP fill, header.</summary>
    private static readonly Color EnemyAccent = new Color(0.80f, 0.28f, 0.28f);

    /// <summary>Empty "track" of a health bar.</summary>
    private static readonly Color BarTrackColor = new Color(0.07f, 0.075f, 0.10f, 1f);

    /// <summary>Health-bar fill for the player's heroes (green).</summary>
    private static readonly Color BarFillPlayer = new Color(0.29f, 0.75f, 0.36f, 1f);

    /// <summary>Health-bar fill for enemy heroes (red).</summary>
    private static readonly Color BarFillEnemy = new Color(0.88f, 0.30f, 0.30f, 1f);

    /// <summary>Dark chip behind small badges (level, rarity, target).</summary>
    private static readonly Color BadgeColor = new Color(0.04f, 0.05f, 0.09f, 0.92f);

    /// <summary>Frame color of the currently acting hero's slot (pulsing gold).</summary>
    private static readonly Color ActiveFrameColor = new Color(1f, 0.83f, 0.45f, 1f);

    /// <summary>Frame color of the selected target's slot.</summary>
    private static readonly Color TargetFrameColor = new Color(1f, 0.35f, 0.30f, 1f);

    /// <summary>Chip color of the selected target's "TARGET" tag.</summary>
    private static readonly Color TargetTagColor = new Color(0.55f, 0.10f, 0.10f, 0.95f);

    /// <summary>Dark overlay for a fallen hero's portrait.</summary>
    private static readonly Color DeadOverlayColor = new Color(0f, 0f, 0f, 0.55f);

    /// <summary>Translucent panel behind the battle log.</summary>
    private static readonly Color LogPanelColor = new Color(0f, 0f, 0f, 0.55f);

    /// <summary>Panel behind the bottom action bar.</summary>
    private static readonly Color ActionBarColor = new Color(0.07f, 0.09f, 0.15f, 0.96f);

    /// <summary>Backdrop of one action/skill slot in the action bar.</summary>
    private static readonly Color ActionSlotColor = new Color(0.14f, 0.17f, 0.26f, 1f);

    /// <summary>Dimmed variant for locked or cooling-down action slots.</summary>
    private static readonly Color ActionSlotDimColor = new Color(0.10f, 0.115f, 0.17f, 0.85f);

    /// <summary>Border of a ready action/skill slot.</summary>
    private static readonly Color ActionSlotReadyColor = new Color(0.78f, 0.66f, 0.38f, 1f);

    /// <summary>Band behind the end-of-battle banner.</summary>
    private static readonly Color BannerPanelColor = new Color(0.05f, 0.06f, 0.11f, 0.96f);

    /// <summary>Result banner text color when the player's side (team 1) wins.</summary>
    private static readonly Color VictoryColor = new Color(0.36f, 0.9f, 0.45f);

    /// <summary>Result banner text color when the enemy side (team 2) wins.</summary>
    private static readonly Color DefeatColor = new Color(0.95f, 0.3f, 0.3f);

    /// <summary>Draw result color (also the 1v1 "{hero} wins!" color).</summary>
    private static readonly Color DrawColor = Color.white;

    /// <summary>Top-left anchor point, shared by most child rects.</summary>
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    /// <summary>Top-center anchor point.</summary>
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

    /// <summary>Reference width the CanvasScaler scales against.</summary>
    private const float ReferenceWidth = 1920f;

    /// <summary>Reference height the CanvasScaler scales against.</summary>
    private const float ReferenceHeight = 1080f;

    /// <summary>Screen-edge margin for the panels and the action bar.</summary>
    private const float ScreenMargin = 24f;

    /// <summary>Width of one team panel.</summary>
    private const float PanelWidth = 500f;

    /// <summary>Height of one team panel: header plus three hero slots.</summary>
    private const float PanelHeight = 470f;

    /// <summary>Inner padding of a team panel.</summary>
    private const float PanelPadding = 12f;

    /// <summary>Vertical stride of one hero slot inside a team panel.</summary>
    private const float SlotStride = 138f;

    /// <summary>Height of one hero slot.</summary>
    private const float SlotHeight = 126f;

    /// <summary>Usable width of a hero slot (panel width minus padding).</summary>
    private const float SlotWidth = PanelWidth - 2f * PanelPadding;

    /// <summary>Size of a hero portrait frame (the icon/initial area inside).</summary>
    private const float PortraitSize = 96f;

    /// <summary>Width of the compact top-center battle log panel.</summary>
    private const float LogPanelWidth = 660f;

    /// <summary>Height of the compact top-center battle log panel.</summary>
    private const float LogPanelHeight = 148f;

    /// <summary>Height of the bottom action bar.</summary>
    private const float ActionBarHeight = 170f;

    /// <summary>Size of one action/skill square in the action bar.</summary>
    private const float ActionSlotSize = 104f;

    /// <summary>Row alpha applied to fallen heroes, so their slots read clearly as inactive.</summary>
    private const float DeadRowAlpha = 0.35f;

    /// <summary>Font size of a hero name.</summary>
    private const int HeroNameFontSize = 24;

    /// <summary>Font size of the level/rarity badges.</summary>
    private const int BadgeFontSize = 16;

    /// <summary>Font size of the HP text inside the health bar.</summary>
    private const int HpFontSize = 15;

    /// <summary>Font size of the hero's portrait initial.</summary>
    private const int PortraitGlyphFontSize = 42;

    /// <summary>Font size of a team panel header.</summary>
    private const int TeamHeaderFontSize = 15;

    /// <summary>Font size of the battle log header.</summary>
    private const int LogHeaderFontSize = 13;

    /// <summary>Font size of one battle log line.</summary>
    private const int LogLineFontSize = 16;

    /// <summary>Font size of the acting hero's name in the action bar.</summary>
    private const int ActionNameFontSize = 24;

    /// <summary>Font size of the acting hero's level/role line.</summary>
    private const int ActionSubFontSize = 17;

    /// <summary>Font size inside action/skill squares and their labels.</summary>
    private const int ActionSlotFontSize = 22;

    /// <summary>Font size of the label under an action/skill square.</summary>
    private const int ActionLabelFontSize = 14;

    /// <summary>Font size of the AUTO/SPEED toggle buttons.</summary>
    private const int ToggleFontSize = 19;

    /// <summary>Font size of the result banner.</summary>
    private const int BannerFontSize = 60;

    /// <summary>Full-screen dim behind the pause overlay; doubles as the click blocker while paused.</summary>
    private static readonly Color PauseBackdropColor = new Color(0f, 0f, 0f, 0.78f);

    /// <summary>Full-screen dim behind the LEAVE BATTLE? confirmation; slightly darker.</summary>
    private static readonly Color ConfirmBackdropColor = new Color(0f, 0f, 0f, 0.85f);

    /// <summary>Dimmed subtitle text inside the confirmation dialog.</summary>
    private static readonly Color OverlaySubColor = new Color(1f, 1f, 1f, 0.55f);

    /// <summary>Dark label color on gold (PlayerAccent) buttons; mirrors the main menu's gold-button text.</summary>
    private static readonly Color TextOnGold = new Color(0.09f, 0.11f, 0.17f);

    /// <summary>Font size of the PAUSE button label.</summary>
    private const int PauseButtonFontSize = 18;

    /// <summary>Font size of the PAUSED overlay title.</summary>
    private const int PauseTitleFontSize = 40;

    /// <summary>Font size of the LEAVE BATTLE? title.</summary>
    private const int ConfirmTitleFontSize = 36;

    /// <summary>Font size of the confirmation subtitle.</summary>
    private const int ConfirmSubFontSize = 20;

    /// <summary>Font size of overlay buttons (RESUME / EXIT BATTLE / YES / NO).</summary>
    private const int OverlayButtonFontSize = 24;

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

    /// <summary>Shared solid rounded-rectangle sprite (9-sliced) for panels, slots, and badges.</summary>
    private Sprite panelSprite;

    /// <summary>Shared rounded-outline sprite (9-sliced) for frames: rarity borders, acting/target outlines, banner border.</summary>
    private Sprite frameSprite;

    /// <summary>Runtime-generated textures/sprites; destroyed in <see cref="OnDestroy"/> so re-entering Play mode never leaks them.</summary>
    private readonly List<Object> runtimeArt = new List<Object>();

    /// <summary>Slots currently shown on the left side (team 1, or the 1v1 hero).</summary>
    private readonly List<HeroSlot> leftSlots = new List<HeroSlot>();

    /// <summary>Slots currently shown on the right side (team 2, or the 1v1 hero).</summary>
    private readonly List<HeroSlot> rightSlots = new List<HeroSlot>();

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

    /// <summary>Large result text inside the banner.</summary>
    private Text resultText;

    /// <summary>Border frame of the banner, tinted by the result color.</summary>
    private Image resultBorder;

    /// <summary>The PAUSE button (beside the battle log); visible only while a battle is live.</summary>
    private GameObject pauseButtonRoot;

    /// <summary>Full-screen pause overlay: dim + PAUSED panel with RESUME / EXIT BATTLE.</summary>
    private GameObject pauseOverlay;

    /// <summary>Full-screen LEAVE BATTLE? confirmation, shown on top of the pause overlay.</summary>
    private GameObject confirmOverlay;

    /// <summary>Cached pause-button visibility so unchanged frames do no work.</summary>
    private bool cachedPauseButtonVisible;

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

    // ----- UI-only presentation state (never combat state) -----

    /// <summary>The hero currently taking its turn, for the acting highlight and the action bar; pushed by the runner.</summary>
    private HeroInstance activeHero;

    /// <summary>The hero currently marked as selected target (UI state only).</summary>
    private HeroInstance selectedTarget;

    // ----- action bar views -----

    /// <summary>Portrait frame of the acting-hero area.</summary>
    private Image activePortraitFrame;

    /// <summary>Portrait icon of the acting hero (visible only when the template has art).</summary>
    private Image activePortraitIcon;

    /// <summary>Portrait initial text of the acting hero (visible only when the template has no art).</summary>
    private Text activePortraitGlyph;

    /// <summary>Name text of the acting hero.</summary>
    private Text activeNameText;

    /// <summary>Level/role line of the acting hero.</summary>
    private Text activeSubText;

    /// <summary>Rarity chip of the acting hero.</summary>
    private Image activeRarityChip;

    /// <summary>Rarity chip text of the acting hero.</summary>
    private Text activeRarityText;

    /// <summary>Accent strip that shows which side the acting hero fights on.</summary>
    private Image activeTeamStrip;

    /// <summary>Canvas group dimming the whole acting-hero area when nobody is acting.</summary>
    private CanvasGroup activeGroup;

    /// <summary>One view per skill square (index 0 doubles as the basic-attack square).</summary>
    private ActionSlotView[] skillSlots;

    /// <summary>Basic-attack square view (always shown, engine's fallback move).</summary>
    private ActionSlotView attackSlot;

    /// <summary>AUTO toggle label (visual placeholder only).</summary>
    private Text autoLabel;

    /// <summary>AUTO toggle background (visual placeholder only).</summary>
    private Image autoImage;

    /// <summary>Whether the AUTO placeholder toggle is switched on (visual state only).</summary>
    private bool autoOn;

    /// <summary>SPEED toggle label (visual placeholder only).</summary>
    private Text speedLabel;

    /// <summary>SPEED toggle background (visual placeholder only).</summary>
    private Image speedImage;

    /// <summary>Whether the SPEED placeholder toggle is switched on (visual state only).</summary>
    private bool speedOn;

    /// <summary>Whether the action bar has drawn once; gates the full redraw to acting-hero changes.</summary>
    private bool actionBarDrawn;

    /// <summary>The hero the action bar currently shows (null = placeholder state).</summary>
    private HeroInstance drawnActionBarHero;

    /// <summary>Last level/role line drawn, so steady frames skip string work.</summary>
    private string cachedActionSubText;

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

    // ---------------------------------------------------------------------
    // UI-only presentation state API. Neither method mutates combat data;
    // they only change what the HUD highlights.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Sets the hero whose turn is currently being resolved: their slot gets
    /// a pulsing gold frame and the action bar shows their kit. Pass null to
    /// clear (e.g. when the battle ends). Called by the runner's coroutines;
    /// pure presentation, no battle flow depends on it.
    /// </summary>
    public void SetActiveHero(HeroInstance hero)
    {
        activeHero = hero;
    }

    /// <summary>
    /// Sets the hero presented as the selected target (red frame + tag), or
    /// null to clear. UI state only - targeting has no combat effect yet.
    /// Clicking an enemy slot calls this; a future input system will too.
    /// </summary>
    public void SetSelectedTarget(HeroInstance hero)
    {
        selectedTarget = hero;
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

        // 9-sliced rounded squares give every panel, badge, and button clean
        // corners at any size without any art assets; the outline variant
        // draws rarity borders, acting/target frames, and the banner border.
        panelSprite = CreateRoundedSprite(outlineOnly: false);
        frameSprite = CreateRoundedSprite(outlineOnly: true);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;

        // The GraphicRaycaster is what lets the EventSystem hit this Canvas's
        // graphics at all. Unity adds one automatically to canvases created
        // in the Editor, but NOT to ones built from code - without it, the
        // enemy-slot clicks and the AUTO/SPEED toggles could never fire.
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = (RectTransform)transform;

        CreateStretchedImage(root, "Background", BackgroundColor);

        leftPanel = CreateTeamPanel(root, "TeamPanelLeft", rightSide: false);
        rightPanel = CreateTeamPanel(root, "TeamPanelRight", rightSide: true);

        BuildLogPanel(root);
        BuildActionBar(root);
        BuildResultBanner(root);
        BuildPauseUi(root);

        // Slot clicks and toggle buttons need an EventSystem; create one only
        // if the scene does not already provide it. The input module must
        // match the project's Active Input Handling: this project uses the
        // Input System package only, so the legacy StandaloneInputModule
        // could never deliver pointer events here.
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }

    /// <summary>
    /// Frees the runtime-created sprites and textures. Sprites are not
    /// components, so ones created with <see cref="Sprite.Create"/> do not
    /// die with the GameObject and would leak without an explicit Destroy.
    /// </summary>
    private void OnDestroy()
    {
        for (int i = 0; i < runtimeArt.Count; i++)
        {
            if (runtimeArt[i] != null)
            {
                Destroy(runtimeArt[i]);
            }
        }

        // The fill sprite wraps Unity's shared white texture: destroy the
        // sprite only, never the texture itself.
        if (fillSprite != null)
        {
            Destroy(fillSprite);
        }
    }

    /// <summary>Reads the runner's current state and updates only what changed since the last frame.</summary>
    private void Update()
    {
        BindRosters();
        RefreshSlots(leftSlots);
        RefreshSlots(rightSlots);
        RefreshLog();
        RefreshBanner();
        RefreshPauseButton();
        RefreshActionBar();
    }

    // ---------------------------------------------------------------------
    // Roster binding: rebuild slot views only when the bound roster changes.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Binds the team panels to the runner's current roster: the 3v3 teams
    /// when they exist, the legacy 1v1 heroes otherwise, or hides the panels
    /// entirely when no battle could start. Rebuilds the slots only when the
    /// bound roster actually changes.
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
            selectedTarget = null;
            RebuildSlots(leftSlots, leftPanel, nextTeam1.Members, rightSide: false);
            RebuildSlots(rightSlots, rightPanel, nextTeam2.Members, rightSide: true);
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
            selectedTarget = null;
            RebuildSoloSlot(leftSlots, leftPanel, nextHero1, rightSide: false);
            RebuildSoloSlot(rightSlots, rightPanel, nextHero2, rightSide: true);
            return;
        }

        // No roster at all (e.g. HeroData unassigned): show nothing.
        if (leftPanel.gameObject.activeSelf || rightPanel.gameObject.activeSelf)
        {
            boundTeam1 = null;
            boundTeam2 = null;
            boundHero1 = null;
            boundHero2 = null;
            selectedTarget = null;
            ClearSlots(leftSlots);
            ClearSlots(rightSlots);
            leftPanel.gameObject.SetActive(false);
            rightPanel.gameObject.SetActive(false);
        }
    }

    /// <summary>Rebuilds one side's slots from a team's members (at most <see cref="Team.MaxSize"/>).</summary>
    private void RebuildSlots(List<HeroSlot> slots, RectTransform panel, IReadOnlyList<HeroInstance> heroes, bool rightSide)
    {
        ClearSlots(slots);
        for (int i = 0; i < heroes.Count && i < Team.MaxSize; i++)
        {
            slots.Add(new HeroSlot(this, panel, heroes[i], i, rightSide));
        }

        panel.gameObject.SetActive(slots.Count > 0);
    }

    /// <summary>Rebuilds one side's single slot for the legacy 1v1 fixture.</summary>
    private void RebuildSoloSlot(List<HeroSlot> slots, RectTransform panel, HeroInstance hero, bool rightSide)
    {
        ClearSlots(slots);
        slots.Add(new HeroSlot(this, panel, hero, 0, rightSide));
        panel.gameObject.SetActive(true);
    }

    /// <summary>Destroys the slot views of one side and forgets them.</summary>
    private static void ClearSlots(List<HeroSlot> slots)
    {
        foreach (HeroSlot slot in slots)
        {
            if (slot != null && slot.Root != null)
            {
                Destroy(slot.Root.gameObject);
            }
        }

        slots.Clear();
    }

    /// <summary>Refreshes every slot of one side, including the acting/target highlight frames.</summary>
    private static void RefreshSlots(List<HeroSlot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].Refresh();
        }
    }

    // ---------------------------------------------------------------------
    // Battle log (compact, top-center).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Copies the runner's battle log (most recent first) into the log lines,
    /// top line = most recent event, older lines below. Skips the update
    /// entirely while nothing changed.
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

    // ---------------------------------------------------------------------
    // Result banner.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Shows the centered result banner once the battle has ended (and a
    /// roster was drawn - no banner when the battle never started). The 3v3
    /// result is read straight from the battle's winner: team 1 (the
    /// player's side) wins = "Victory!", team 2 wins = "Defeat!", no winner
    /// = "Draw!". The legacy 1v1 fixture keeps its "{hero} wins!" label.
    /// </summary>
    private void RefreshBanner()
    {
        bool show = runner != null && runner.BattleEnded && leftSlots.Count > 0;
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
                color = DrawColor;
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
            color = DrawColor;
        }

        // Assign only when the result itself changed, so a stale label from a
        // previous battle can never linger and idle frames do no work.
        if (text != cachedBannerText)
        {
            cachedBannerText = text;
            resultText.text = text;
            resultText.color = color;
            resultBorder.color = new Color(color.r, color.g, color.b, 0.85f);
        }
    }

    // ---------------------------------------------------------------------
    // Layout construction helpers.
    // ---------------------------------------------------------------------

    /// <summary>Creates one team's panel anchored to the top-left or top-right screen edge, with a small header and a team-colored edge accent.</summary>
    private RectTransform CreateTeamPanel(RectTransform root, string name, bool rightSide)
    {
        Vector2 anchor = new Vector2(rightSide ? 1f : 0f, 1f);
        Image panel = CreateSlicedImage(root, name, TeamPanelColor, panelSprite, anchor, anchor,
            new Vector2(rightSide ? -ScreenMargin : ScreenMargin, -ScreenMargin),
            new Vector2(PanelWidth, PanelHeight));

        // Team header (ALLIES / ENEMIES) in the team's accent color.
        CreateText(panel.rectTransform, "Header", rightSide ? "ENEMIES" : "ALLIES",
            TeamHeaderFontSize, FontStyle.Bold, rightSide ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(PanelPadding + 6f, -6f), new Vector2(SlotWidth - 12f, 24f))
            .color = rightSide ? EnemyAccent : PlayerAccent;

        // Accent strip on the panel's inner vertical edge (toward the
        // battlefield): gold for the player's side, red for the enemy's.
        CreateImage(panel.rectTransform, "Accent", rightSide ? EnemyAccent : PlayerAccent,
            TopLeft, TopLeft, new Vector2(rightSide ? 0f : PanelWidth - 6f, -30f), new Vector2(6f, PanelHeight - 36f));

        // Hidden until a roster is bound in Update.
        panel.gameObject.SetActive(false);
        return panel.rectTransform;
    }

    /// <summary>Creates the compact top-center battle log panel with its header and line views.</summary>
    private void BuildLogPanel(RectTransform root)
    {
        Image panel = CreateSlicedImage(root, "BattleLogPanel", LogPanelColor, panelSprite,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -ScreenMargin), new Vector2(LogPanelWidth, LogPanelHeight));
        RectTransform panelRect = panel.rectTransform;

        CreateText(panelRect, "Header", "BATTLE LOG",
            LogHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(16f, -4f), new Vector2(LogPanelWidth - 32f, 20f))
            .color = new Color(1f, 1f, 1f, 0.55f);

        logLines = new Text[BattleTestRunner.MaxLogEntries];
        for (int i = 0; i < logLines.Length; i++)
        {
            // Wrapping keeps long event lines readable instead of running off
            // the panel; the fixed slot height truncates a rare second line.
            logLines[i] = CreateText(panelRect, "Line" + (i + 1), string.Empty,
                LogLineFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(16f, -(26f + i * 23f)), new Vector2(LogPanelWidth - 32f, 22f),
                wrap: true);
            logLines[i].color = new Color(1f, 1f, 1f, 0.82f);
        }
    }

    /// <summary>Creates the centered result banner panel and its text, hidden until the battle ends.</summary>
    private void BuildResultBanner(RectTransform root)
    {
        RectTransform bannerRect = CreateRect(root, "ResultBanner");
        bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
        bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
        bannerRect.pivot = new Vector2(0.5f, 0.5f);
        bannerRect.anchoredPosition = Vector2.zero;
        bannerRect.sizeDelta = new Vector2(860f, 210f);

        Image panel = bannerRect.gameObject.AddComponent<Image>();
        panel.sprite = panelSprite;
        panel.type = Image.Type.Sliced;
        panel.color = BannerPanelColor;
        panel.raycastTarget = false;

        // Thin outline around the banner, tinted with the result color.
        RectTransform borderRect = CreateRect(bannerRect, "Border");
        Stretch(borderRect, 8f);
        resultBorder = borderRect.gameObject.AddComponent<Image>();
        resultBorder.sprite = frameSprite;
        resultBorder.type = Image.Type.Sliced;
        resultBorder.color = VictoryColor;
        resultBorder.raycastTarget = false;

        resultText = CreateText(bannerRect, "ResultText", string.Empty,
            BannerFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(resultText.rectTransform);

        resultBanner = bannerRect.gameObject;
        resultBanner.SetActive(false);
    }

    // ---------------------------------------------------------------------
    // Bottom action bar (future player abilities - visual placeholders).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds the bottom action bar: the acting hero's summary on the left,
    /// the basic-attack square plus three skill squares in the middle, and
    /// the AUTO / SPEED placeholder toggles on the right. Nothing here drives
    /// combat; skill squares only read the acting hero's kit and readiness.
    /// </summary>
    private void BuildActionBar(RectTransform root)
    {
        Image bar = CreateSlicedImage(root, "ActionBar", ActionBarColor, panelSprite,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, ScreenMargin), new Vector2(ReferenceWidth - 2f * ScreenMargin, ActionBarHeight));
        RectTransform barRect = bar.rectTransform;

        // ---- Acting hero summary (left) ----
        RectTransform heroArea = CreateRect(barRect, "ActiveHero");
        heroArea.anchorMin = TopLeft;
        heroArea.anchorMax = TopLeft;
        heroArea.pivot = TopLeft;
        heroArea.anchoredPosition = new Vector2(12f, -20f);
        heroArea.sizeDelta = new Vector2(396f, 130f);
        activeGroup = heroArea.gameObject.AddComponent<CanvasGroup>();

        CreateSlicedImage(heroArea, "Back", SlotColor, panelSprite, TopLeft, TopLeft, Vector2.zero, heroArea.sizeDelta);
        activeTeamStrip = CreateImage(heroArea, "TeamStrip", PlayerAccent, TopLeft, TopLeft, Vector2.zero, new Vector2(6f, 130f));

        activePortraitFrame = CreateSlicedImage(heroArea, "PortraitFrame", PlayerAccent, frameSprite,
            TopLeft, TopLeft, new Vector2(14f, -16f), new Vector2(98f, 98f));
        CreateSlicedImage(heroArea, "PortraitBack", PortraitBackColor, panelSprite,
            TopLeft, TopLeft, new Vector2(20f, -22f), new Vector2(86f, 86f));

        activePortraitIcon = CreateSlicedImage(heroArea, "PortraitIcon", Color.white, null,
            TopLeft, TopLeft, new Vector2(20f, -22f), new Vector2(86f, 86f));
        activePortraitIcon.preserveAspect = true;
        activePortraitIcon.gameObject.SetActive(false);

        activePortraitGlyph = CreateText(heroArea, "PortraitGlyph", "?",
            PortraitGlyphFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(20f, -22f), new Vector2(86f, 86f));

        activeNameText = CreateText(heroArea, "Name", "No active hero",
            ActionNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(126f, -8f), new Vector2(260f, 34f));

        activeSubText = CreateText(heroArea, "Sub", string.Empty,
            ActionSubFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(126f, -46f), new Vector2(260f, 24f));
        activeSubText.color = new Color(1f, 1f, 1f, 0.65f);

        activeRarityChip = CreateSlicedImage(heroArea, "RarityChip", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(126f, -78f), new Vector2(96f, 26f));
        activeRarityText = CreateText(heroArea, "RarityText", string.Empty,
            BadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(126f, -78f), new Vector2(96f, 26f));

        // ---- Action squares (center): basic attack + three skill slots ----
        attackSlot = new ActionSlotView(this, barRect, "AttackSlot", -229f);
        attackSlot.SetContent("ATK", "ATTACK", ready: true);

        skillSlots = new ActionSlotView[3];
        for (int i = 0; i < skillSlots.Length; i++)
        {
            skillSlots[i] = new ActionSlotView(this, barRect, "SkillSlot" + (i + 1), -229f + ActionSlotSize + 14f + i * (ActionSlotSize + 14f));
        }

        // ---- Placeholder toggles (right): AUTO and SPEED ----
        BuildToggle(barRect, "AutoToggle", out autoImage, out autoLabel, -84f, "AUTO", OnAutoClicked);
        BuildToggle(barRect, "SpeedToggle", out speedImage, out speedLabel, -16f, "SPEED x1", OnSpeedClicked);
    }

    /// <summary>Creates one placeholder toggle button (rounded panel + bold label); the click only flips a visual state.</summary>
    private void BuildToggle(RectTransform bar, string name, out Image image, out Text label, float yOffset, string initial, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect(bar, name);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-12f, yOffset);
        rect.sizeDelta = new Vector2(148f, 58f);

        image = rect.gameObject.AddComponent<Image>();
        image.sprite = panelSprite;
        image.type = Image.Type.Sliced;
        image.color = ActionSlotDimColor;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        label = CreateText(rect, "Label", initial,
            ToggleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(label.rectTransform);
    }

    /// <summary>AUTO placeholder click: flips the visual toggle only; auto-battle is a future system.</summary>
    private void OnAutoClicked()
    {
        autoOn = !autoOn;
        autoLabel.text = autoOn ? "AUTO ON" : "AUTO";
        autoLabel.color = autoOn ? PlayerAccent : Color.white;
        autoImage.color = autoOn ? ActionSlotColor : ActionSlotDimColor;
    }

    /// <summary>SPEED placeholder click: flips the visual toggle only; battle pacing stays as configured.</summary>
    private void OnSpeedClicked()
    {
        speedOn = !speedOn;
        speedLabel.text = speedOn ? "SPEED x2" : "SPEED x1";
        speedLabel.color = speedOn ? PlayerAccent : Color.white;
        speedImage.color = speedOn ? ActionSlotColor : ActionSlotDimColor;
    }

    /// <summary>
    /// Refreshes the acting-hero summary and the skill squares: binds to
    /// <see cref="activeHero"/> (pushed by the runner), shows their portrait,
    /// name, level/role and rarity, and mirrors their kit's skills with
    /// live readiness. Everything is read straight from the hero - no names,
    /// levels, or skills are ever hardcoded here.
    /// </summary>
    private void RefreshActionBar()
    {
        HeroInstance hero = activeHero != null && activeHero.data != null ? activeHero : null;

        // Full redraw only when the acting hero (or its absence) changed;
        // steady frames just refresh live values (readiness, level line).
        if (actionBarDrawn && ReferenceEquals(drawnActionBarHero, hero))
        {
            if (hero == null)
            {
                return;
            }

            for (int i = 0; i < skillSlots.Length; i++)
            {
                skillSlots[i].RefreshReadiness(hero);
            }

            string sub = "Lv " + hero.Level + "  " + RoleLabel(hero.data.role);
            if (sub != cachedActionSubText)
            {
                cachedActionSubText = sub;
                activeSubText.text = sub;
            }

            return;
        }

        actionBarDrawn = true;
        drawnActionBarHero = hero;
        activeGroup.alpha = hero != null ? 1f : 0.45f;

        if (hero == null)
        {
            activeNameText.text = runner != null && runner.BattleEnded ? "Battle over" : "No active hero";
            activeSubText.text = string.Empty;
            activeRarityText.text = string.Empty;
            activePortraitIcon.gameObject.SetActive(false);
            activePortraitGlyph.gameObject.SetActive(true);
            activePortraitGlyph.text = "?";
            activePortraitGlyph.color = new Color(1f, 1f, 1f, 0.4f);
            activePortraitFrame.color = new Color(1f, 1f, 1f, 0.25f);
            activeTeamStrip.color = new Color(1f, 1f, 1f, 0.15f);
            attackSlot.SetContent("ATK", "ATTACK", ready: false);
            for (int i = 0; i < skillSlots.Length; i++)
            {
                skillSlots[i].SetLocked();
            }

            return;
        }

        HeroData data = hero.data;

        // Portrait: the template's icon when art exists, otherwise the hero's
        // initial on a rarity-tinted frame - swapping in real art later needs
        // no code change, just an assigned Sprite on the HeroData asset.
        RarityStyle rarity = RarityStyleFor(data.rarity);
        if (data.icon != null)
        {
            activePortraitIcon.sprite = data.icon;
            activePortraitIcon.gameObject.SetActive(true);
            activePortraitGlyph.gameObject.SetActive(false);
        }
        else
        {
            activePortraitIcon.gameObject.SetActive(false);
            activePortraitGlyph.gameObject.SetActive(true);
            activePortraitGlyph.text = InitialOf(data.heroName);
            activePortraitGlyph.color = Color.Lerp(rarity.Color, Color.white, 0.35f);
        }

        activePortraitFrame.color = rarity.Color;

        // Team accent: gold when the acting hero fights for the bound player
        // team, red otherwise.
        bool onPlayerTeam = boundTeam1 != null && boundTeam1.Contains(activeHero);
        Color accent = onPlayerTeam ? PlayerAccent : EnemyAccent;
        activeTeamStrip.color = accent;

        activeNameText.text = activeHero.displayName;
        cachedActionSubText = "Lv " + activeHero.Level + "  " + RoleLabel(data.role);
        activeSubText.text = cachedActionSubText;
        activeRarityChip.color = Color.Lerp(rarity.Color, Color.black, 0.72f);
        activeRarityText.text = rarity.Label;
        activeRarityText.color = Color.Lerp(rarity.Color, Color.white, 0.25f);

        // The engine's basic attack is always available while a hero acts.
        attackSlot.SetContent("ATK", "ATTACK", ready: true);

        // Skill squares follow the acting hero's kit (data-driven): one
        // square per skill, locked squares for the rest of the row.
        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (data.skills != null && i < data.skills.Count)
            {
                skillSlots[i].BindSkill(data.skills[i]);
            }
            else
            {
                skillSlots[i].SetLocked();
            }
        }

        for (int i = 0; i < skillSlots.Length; i++)
        {
            skillSlots[i].RefreshReadiness(hero);
        }
    }

    /// <summary>Human label for a hero role; future enum values fall through to their own name.</summary>
    private static string RoleLabel(HeroRole role)
    {
        return role.ToString();
    }

    /// <summary>The first letter of a hero's name, for placeholder portraits ("?" when unknown).</summary>
    private static string InitialOf(string heroName)
    {
        return string.IsNullOrEmpty(heroName) ? "?" : heroName.Substring(0, 1).ToUpperInvariant();
    }

    // ---------------------------------------------------------------------
    // Pause menu (battle UX only - pausing freezes the runner's turn pacing;
    // no battle state is ever modified).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds the pause UI: a compact PAUSE button in the gap between the
    /// battle log and the enemy panel, a centered pause overlay (RESUME /
    /// EXIT BATTLE), and a LEAVE BATTLE? confirmation above it. Both
    /// overlays are full-screen raycastable blockers, so the battle UI
    /// cannot be clicked while they are open.
    /// </summary>
    private void BuildPauseUi(RectTransform root)
    {
        // ---- PAUSE button ----
        RectTransform buttonRect = CreateRect(root, "PauseButton");
        buttonRect.anchorMin = TopCenter;
        buttonRect.anchorMax = TopCenter;
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = new Vector2(385f, -ScreenMargin);
        buttonRect.sizeDelta = new Vector2(94f, 56f);

        // Created directly (not via the image factory) so it stays raycastable.
        Image buttonBack = buttonRect.gameObject.AddComponent<Image>();
        buttonBack.sprite = panelSprite;
        buttonBack.type = Image.Type.Sliced;
        buttonBack.color = ActionSlotDimColor;

        CreateSlicedImage(buttonRect, "Border", ActionSlotReadyColor, frameSprite,
            TopLeft, TopLeft, new Vector2(-3f, -3f), new Vector2(100f, 62f));

        Text pauseLabel = CreateText(buttonRect, "Label", "PAUSE",
            PauseButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(pauseLabel.rectTransform);

        Button pauseButton = buttonRect.gameObject.AddComponent<Button>();
        pauseButton.targetGraphic = buttonBack;
        pauseButton.onClick.AddListener(OnPauseClicked);

        pauseButtonRoot = buttonRect.gameObject;
        pauseButtonRoot.SetActive(false);

        // ---- Pause overlay (full-screen blocker + centered panel) ----
        pauseOverlay = CreateOverlayBlocker(root, "PauseOverlay", PauseBackdropColor);
        BuildPausePanel(pauseOverlay.transform);
        pauseOverlay.SetActive(false);

        // ---- LEAVE BATTLE? confirmation (built after, renders above) ----
        confirmOverlay = CreateOverlayBlocker(root, "ConfirmOverlay", ConfirmBackdropColor);
        BuildConfirmPanel(confirmOverlay.transform);
        confirmOverlay.SetActive(false);
    }

    /// <summary>Builds the PAUSED panel: title, RESUME, and EXIT BATTLE.</summary>
    private void BuildPausePanel(Transform parent)
    {
        RectTransform panel = CreateRect(parent, "Panel");
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(560f, 420f);

        CreateSlicedImage(panel, "Back", ActionBarColor, panelSprite, TopLeft, TopLeft, Vector2.zero, panel.sizeDelta);
        CreateSlicedImage(panel, "Border", PlayerAccent, frameSprite, TopLeft, TopLeft, Vector2.zero, panel.sizeDelta);

        CreateText(panel, "Title", "PAUSED", PauseTitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -56f), new Vector2(460f, 44f)).color = PlayerAccent;

        CreateOverlayButton(panel, "ResumeButton", "RESUME", PlayerAccent, TextOnGold,
            new Vector2(0f, -160f), new Vector2(420f, 86f), OnResumeClicked);

        CreateOverlayButton(panel, "ExitButton", "EXIT BATTLE", ActionSlotDimColor, Color.white,
            new Vector2(0f, -268f), new Vector2(420f, 86f), OnExitBattleClicked);
    }

    /// <summary>Builds the LEAVE BATTLE? panel: title, subtitle, YES, NO.</summary>
    private void BuildConfirmPanel(Transform parent)
    {
        RectTransform panel = CreateRect(parent, "Panel");
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(660f, 430f);

        CreateSlicedImage(panel, "Back", ActionBarColor, panelSprite, TopLeft, TopLeft, Vector2.zero, panel.sizeDelta);
        CreateSlicedImage(panel, "Border", EnemyAccent, frameSprite, TopLeft, TopLeft, Vector2.zero, panel.sizeDelta);

        CreateText(panel, "Title", "LEAVE BATTLE?", ConfirmTitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -60f), new Vector2(560f, 42f));

        CreateText(panel, "Sub", "Current battle progress will be lost.", ConfirmSubFontSize, FontStyle.Normal,
            TextAnchor.MiddleCenter, TopCenter, TopCenter, new Vector2(0f, -112f), new Vector2(560f, 26f)).color = OverlaySubColor;

        CreateOverlayButton(panel, "YesButton", "YES", EnemyAccent, Color.white,
            new Vector2(-150f, -260f), new Vector2(260f, 86f), OnConfirmYesClicked);

        CreateOverlayButton(panel, "NoButton", "NO", ActionSlotDimColor, Color.white,
            new Vector2(150f, -260f), new Vector2(260f, 86f), OnConfirmNoClicked);
    }

    /// <summary>
    /// Creates a full-screen blocker: a stretched, raycastable dark Image
    /// that dims the battle and swallows every click while an overlay is
    /// open (the panels are its children, so they render - and receive
    /// clicks - above it).
    /// </summary>
    private GameObject CreateOverlayBlocker(RectTransform parent, string name, Color color)
    {
        RectTransform rect = CreateRect(parent, name);
        Stretch(rect);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return rect.gameObject;
    }

    /// <summary>
    /// Creates one overlay button: a rounded, raycastable panel with a bold
    /// centered label. The image is created directly so it stays raycastable
    /// (the shared factory disables raycast targets for decoration).
    /// </summary>
    private void CreateOverlayButton(RectTransform parent, string name, string label, Color backColor, Color textColor, Vector2 center, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = center;
        rect.sizeDelta = size;

        Image back = rect.gameObject.AddComponent<Image>();
        back.sprite = panelSprite;
        back.type = Image.Type.Sliced;
        back.color = backColor;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(onClick);

        Text text = CreateText(rect, "Label", label, OverlayButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(text.rectTransform);
        text.color = textColor;
    }

    /// <summary>Shows the PAUSE button only while a battle is actually live.</summary>
    private void RefreshPauseButton()
    {
        bool visible = runner != null && !runner.BattleEnded && leftSlots.Count > 0;
        if (visible != cachedPauseButtonVisible)
        {
            cachedPauseButtonVisible = visible;
            pauseButtonRoot.SetActive(visible);
        }
    }

    /// <summary>PAUSE button: freezes the battle (runner-owned pacing) and opens the pause overlay.</summary>
    private void OnPauseClicked()
    {
        if (runner == null || runner.BattleEnded)
        {
            return;
        }

        runner.SetBattlePaused(true);
        pauseOverlay.SetActive(true);
    }

    /// <summary>RESUME: closes the pause UI and unpauses; the current battle continues exactly where it stopped.</summary>
    private void OnResumeClicked()
    {
        if (runner != null)
        {
            runner.SetBattlePaused(false);
        }

        pauseOverlay.SetActive(false);
        confirmOverlay.SetActive(false);
    }

    /// <summary>EXIT BATTLE: opens the LEAVE BATTLE? confirmation on top of the pause overlay (battle stays paused).</summary>
    private void OnExitBattleClicked()
    {
        confirmOverlay.SetActive(true);
    }

    /// <summary>YES: stops the battle cleanly and returns to the main menu (the runner re-shows it).</summary>
    private void OnConfirmYesClicked()
    {
        confirmOverlay.SetActive(false);
        pauseOverlay.SetActive(false);
        if (runner != null)
        {
            runner.StopBattle();
        }
    }

    /// <summary>NO: closes the confirmation and returns to the pause menu (battle stays paused).</summary>
    private void OnConfirmNoClicked()
    {
        confirmOverlay.SetActive(false);
    }

    // ---------------------------------------------------------------------
    // Rarity presentation. The table below is the ONLY place rarity look is
    // defined; adding a new rarity later means adding one case (or relying
    // on the safe default) - the HUD never needs a rewrite.
    // ---------------------------------------------------------------------

    /// <summary>Visual style of one rarity tier.</summary>
    private struct RarityStyle
    {
        public Color Color;

        public string Label;

        public RarityStyle(Color color, string label)
        {
            Color = color;
            Label = label;
        }
    }

    /// <summary>
    /// Style for a rarity tier: frame/badge color and display label. Unknown
    /// (future) rarities safely fall back to a neutral style labeled with
    /// the enum value's own name, so extending <see cref="HeroRarity"/> never
    /// breaks the HUD.
    /// </summary>
    private static RarityStyle RarityStyleFor(HeroRarity rarity)
    {
        switch (rarity)
        {
            case HeroRarity.Common:
                return new RarityStyle(new Color(0.62f, 0.66f, 0.72f), "Common");
            case HeroRarity.Rare:
                return new RarityStyle(new Color(0.30f, 0.58f, 0.95f), "Rare");
            case HeroRarity.Epic:
                return new RarityStyle(new Color(0.68f, 0.44f, 0.95f), "Epic");
            case HeroRarity.Legendary:
                return new RarityStyle(new Color(0.95f, 0.74f, 0.32f), "Legendary");
            default:
                return new RarityStyle(new Color(0.62f, 0.66f, 0.72f), rarity.ToString());
        }
    }

    // ---------------------------------------------------------------------
    // Basic view factories (anchored rects, images, texts).
    // ---------------------------------------------------------------------

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

    /// <summary>Creates a solid-color Image at the given anchored rect.</summary>
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

    /// <summary>
    /// Creates a 9-sliced Image from the shared rounded sprite (panels,
    /// badges, buttons); pass a null sprite for a plain quad.
    /// </summary>
    private Image CreateSlicedImage(Transform parent, string name, Color color, Sprite sprite, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        Image image = CreateImage(parent, name, color, anchor, pivot, position, size);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

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

    /// <summary>Stretches a RectTransform across its parent, with an optional inset.</summary>
    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    // ---------------------------------------------------------------------
    // Runtime sprite generation (no art assets required).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a shared 48x48 white rounded square as a 9-sliced sprite, so
    /// panels, badges, and buttons get clean rounded corners at any size.
    /// The outline variant draws only a rounded border, used for rarity
    /// frames, acting/target highlights, and the banner border.
    /// </summary>
    private Sprite CreateRoundedSprite(bool outlineOnly)
    {
        const int Size = 48;
        const int Radius = 12;
        const int Stroke = 4;

        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                bool outer = InsideRoundedSquare(x, y, 0, Size - 1, Radius);
                bool inner = InsideRoundedSquare(x, y, Stroke, Size - 1 - Stroke, Radius - Stroke);
                bool solid = outlineOnly ? (outer && !inner) : outer;
                texture.SetPixel(x, y, solid ? Color.white : Color.clear);
            }
        }

        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
        runtimeArt.Add(texture);
        runtimeArt.Add(sprite);
        return sprite;
    }

    /// <summary>Whether a pixel lies inside the rounded square [min..max] with the given corner radius.</summary>
    private static bool InsideRoundedSquare(int x, int y, int min, int max, int radius)
    {
        int center = (min + max) / 2;
        int half = (max - min) / 2;
        int straight = half - radius;
        int dx = Mathf.Max(Mathf.Abs(x - center) - straight, 0);
        int dy = Mathf.Max(Mathf.Abs(y - center) - straight, 0);
        return dx * dx + dy * dy <= radius * radius;
    }

    // ---------------------------------------------------------------------
    // Hero slot view.
    // ---------------------------------------------------------------------

    /// <summary>
    /// One hero's view inside a team panel: portrait frame (rarity-colored),
    /// name, level badge, rarity badge, health bar with current/max HP, and
    /// the alive/dead/acting/target presentation states. Reads the live
    /// <see cref="HeroInstance"/> directly and caches the last drawn values
    /// so unchanged frames skip string work. Enemy slots are clickable to
    /// mark the selected target (pure UI state).
    /// </summary>
    private sealed class HeroSlot
    {
        /// <summary>The live hero this slot renders (never null).</summary>
        public readonly HeroInstance Hero;

        /// <summary>Root RectTransform under the team panel; destroyed when the slot is rebuilt.</summary>
        public readonly RectTransform Root;

        private readonly BattleHud hud;

        private readonly Image portraitIcon;

        private readonly Text portraitGlyph;

        private readonly Image portraitDeadOverlay;

        private readonly Text levelText;

        private readonly Text rarityText;

        private readonly Image fillImage;

        private readonly Text hpText;

        /// <summary>Dims the whole slot when the hero falls, so dead heroes read clearly as out of the fight.</summary>
        private readonly CanvasGroup slotGroup;

        /// <summary>Pulsing gold outline shown while this hero is taking its turn.</summary>
        private readonly Image activeFrame;

        /// <summary>Red outline + "TARGET" chip shown while this hero is the selected target.</summary>
        private readonly Image targetFrame;

        private readonly GameObject targetTag;

        private int lastHealth = int.MinValue;

        private int lastMaxHealth = int.MinValue;

        private int lastLevel = int.MinValue;

        private bool lastAlive = true;

        public HeroSlot(BattleHud hud, RectTransform panel, HeroInstance hero, int slot, bool rightSide)
        {
            this.hud = hud;
            Hero = hero;

            Root = CreateRect(panel, "HeroSlot" + (slot + 1));
            Root.anchorMin = TopLeft;
            Root.anchorMax = TopLeft;
            Root.pivot = TopLeft;
            Root.anchoredPosition = new Vector2(PanelPadding, -(34f + slot * SlotStride));
            Root.sizeDelta = new Vector2(SlotWidth, SlotHeight);

            // Slot backdrop; on the enemy side it doubles as the click target
            // for the (UI-only) selected-target state.
            Image back = hud.CreateSlicedImage(Root, "Back", SlotColor, hud.panelSprite,
                TopLeft, TopLeft, Vector2.zero, Root.sizeDelta);

            // Acting highlight: rounded outline just outside the slot.
            activeFrame = hud.CreateSlicedImage(Root, "ActiveFrame", ActiveFrameColor, hud.frameSprite,
                TopLeft, TopLeft, new Vector2(-4f, -4f), new Vector2(SlotWidth + 8f, SlotHeight + 8f));
            activeFrame.gameObject.SetActive(false);

            // Selected target: outline on the slot edge plus a small tag.
            targetFrame = hud.CreateSlicedImage(Root, "TargetFrame", TargetFrameColor, hud.frameSprite,
                TopLeft, TopLeft, Vector2.zero, Root.sizeDelta);
            targetFrame.gameObject.SetActive(false);

            // Portrait: rarity-colored rounded frame; the template's icon
            // when art exists, otherwise the hero's initial. Assigning a
            // Sprite to HeroData.icon later swaps it in with no code change.
            RarityStyle rarity = RarityStyleFor(hero.data != null ? hero.data.rarity : HeroRarity.Common);
            float portraitX = rightSide ? SlotWidth - PortraitSize - 12f : 12f;
            hud.CreateSlicedImage(Root, "PortraitFrame", rarity.Color, hud.frameSprite,
                TopLeft, TopLeft, new Vector2(portraitX, -14f), new Vector2(PortraitSize, PortraitSize));
            hud.CreateSlicedImage(Root, "PortraitBack", PortraitBackColor, hud.panelSprite,
                TopLeft, TopLeft, new Vector2(portraitX + 5f, -19f), new Vector2(PortraitSize - 10f, PortraitSize - 10f));

            portraitIcon = hud.CreateSlicedImage(Root, "PortraitIcon", Color.white, null,
                TopLeft, TopLeft, new Vector2(portraitX + 5f, -19f), new Vector2(PortraitSize - 10f, PortraitSize - 10f));
            portraitIcon.preserveAspect = true;

            portraitGlyph = hud.CreateText(Root, "PortraitGlyph",
                InitialOf(hero.data != null ? hero.data.heroName : null),
                PortraitGlyphFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(portraitX + 5f, -19f), new Vector2(PortraitSize - 10f, PortraitSize - 10f));
            portraitGlyph.color = Color.Lerp(rarity.Color, Color.white, 0.35f);

            portraitDeadOverlay = hud.CreateImage(Root, "PortraitDead", DeadOverlayColor,
                TopLeft, TopLeft, new Vector2(portraitX + 5f, -19f), new Vector2(PortraitSize - 10f, PortraitSize - 10f));
            portraitDeadOverlay.gameObject.SetActive(false);

            if (hero.data != null && hero.data.icon != null)
            {
                portraitIcon.sprite = hero.data.icon;
                portraitGlyph.gameObject.SetActive(false);
            }

            // Text column on the side opposite the portrait; enemy slots are
            // right-aligned so the two panels mirror each other.
            float textX = rightSide ? 12f : PortraitSize + 24f;
            float textW = SlotWidth - PortraitSize - 36f;
            TextAnchor align = rightSide ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

            hud.CreateText(Root, "Name", hero.displayName, HeroNameFontSize, FontStyle.Bold, align,
                TopLeft, TopLeft, new Vector2(textX, -6f), new Vector2(textW, 32f));

            // Level badge (data-driven from the hero's progression state).
            hud.CreateSlicedImage(Root, "LevelBadge", BadgeColor, hud.panelSprite,
                TopLeft, TopLeft, new Vector2(textX, -44f), new Vector2(64f, 26f));
            levelText = hud.CreateText(Root, "LevelText", string.Empty,
                BadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(textX, -44f), new Vector2(64f, 26f));
            levelText.color = PlayerAccent;

            // Rarity badge: dark chip, rarity-colored text (data-driven).
            float rarityX = rightSide ? SlotWidth - 12f - 92f : textX + 72f;
            hud.CreateSlicedImage(Root, "RarityBadge", Color.Lerp(rarity.Color, Color.black, 0.72f), hud.panelSprite,
                TopLeft, TopLeft, new Vector2(rarityX, -44f), new Vector2(92f, 26f));
            rarityText = hud.CreateText(Root, "RarityText", rarity.Label,
                BadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(rarityX, -44f), new Vector2(92f, 26f));
            rarityText.color = Color.Lerp(rarity.Color, Color.white, 0.25f);

            // Health bar with the current/max HP printed inside it.
            RectTransform track = hud.CreateSlicedImage(Root, "HpBar", BarTrackColor, hud.panelSprite,
                TopLeft, TopLeft, new Vector2(textX, -82f), new Vector2(textW, 26f)).rectTransform;

            RectTransform fill = CreateRect(track, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            fillImage = fill.gameObject.AddComponent<Image>();
            // The shared white sprite is what makes Filled rendering work:
            // a sprite-less Image draws a full quad and ignores fillAmount.
            fillImage.sprite = hud.fillSprite;
            fillImage.color = rightSide ? BarFillEnemy : BarFillPlayer;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            // Player bars drain toward the left edge, enemy bars toward the
            // right, mirroring the two panels around the battlefield.
            fillImage.fillOrigin = (int)(rightSide ? Image.OriginHorizontal.Right : Image.OriginHorizontal.Left);
            fillImage.raycastTarget = false;

            hpText = hud.CreateText(track, "HpText", string.Empty, HpFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                TopLeft, TopLeft, Vector2.zero, Vector2.zero);
            Stretch(hpText.rectTransform);
            hpText.rectTransform.offsetMax = new Vector2(-10f, 0f);

            // "TARGET" chip over the slot's top corner (selected target state).
            targetTag = CreateRect(Root, "TargetTag").gameObject;
            RectTransform tagRect = (RectTransform)targetTag.transform;
            tagRect.anchorMin = TopLeft;
            tagRect.anchorMax = TopLeft;
            tagRect.pivot = TopLeft;
            tagRect.anchoredPosition = new Vector2(SlotWidth - 78f, -2f);
            tagRect.sizeDelta = new Vector2(74f, 22f);
            hud.CreateSlicedImage(targetTag.transform, "Back", TargetTagColor, hud.panelSprite,
                TopLeft, TopLeft, Vector2.zero, new Vector2(74f, 22f));
            hud.CreateText(targetTag.transform, "Label", "TARGET", 12, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, Vector2.zero, new Vector2(74f, 22f));
            targetTag.SetActive(false);

            // Enemy slots are clickable: clicking marks (or unmarks) the hero
            // as the selected target. Presentation only - no combat effect.
            if (rightSide)
            {
                back.raycastTarget = true;
                Button button = Root.gameObject.AddComponent<Button>();
                button.targetGraphic = back;
                button.onClick.AddListener(() => hud.OnEnemySlotClicked(Hero));
            }

            // Dims the entire slot at once when the hero dies.
            slotGroup = Root.gameObject.AddComponent<CanvasGroup>();

            Refresh();
        }

        /// <summary>
        /// Updates the level badge, bar fill, HP text, dead/alive look, and
        /// the acting/target highlight frames whenever something changed
        /// since the last frame. All values are read from the live hero.
        /// </summary>
        public void Refresh()
        {
            int maxHealth = Hero.data != null ? Hero.maxHealth : 0;
            if (Hero.currentHealth != lastHealth || maxHealth != lastMaxHealth
                || Hero.Level != lastLevel || Hero.isAlive != lastAlive)
            {
                lastHealth = Hero.currentHealth;
                lastMaxHealth = maxHealth;
                lastLevel = Hero.Level;
                lastAlive = Hero.isAlive;
                fillImage.fillAmount = maxHealth > 0 ? Mathf.Clamp01((float)Hero.currentHealth / maxHealth) : 0f;
                hpText.text = Hero.currentHealth + " / " + maxHealth;
                levelText.text = "Lv " + Hero.Level;

                // Fallen heroes dim and their portrait darkens, so the slot
                // clearly reads as out of the fight; the empty bar and 0 HP
                // text show why.
                slotGroup.alpha = Hero.isAlive ? 1f : DeadRowAlpha;
                portraitDeadOverlay.gameObject.SetActive(!Hero.isAlive);
            }

            // Acting highlight: a gently pulsing gold outline around the slot
            // of the hero whose turn is being resolved.
            bool acting = hud.activeHero != null && ReferenceEquals(hud.activeHero, Hero);
            if (activeFrame.gameObject.activeSelf != acting)
            {
                activeFrame.gameObject.SetActive(acting);
            }

            if (acting)
            {
                float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f));
                activeFrame.color = new Color(ActiveFrameColor.r, ActiveFrameColor.g, ActiveFrameColor.b, pulse);
            }

            // Selected target: red outline plus the TARGET chip; the state
            // clears itself when the marked hero falls.
            bool targeted = hud.selectedTarget != null && ReferenceEquals(hud.selectedTarget, Hero) && Hero.isAlive;
            if (targetFrame.gameObject.activeSelf != targeted)
            {
                targetFrame.gameObject.SetActive(targeted);
                targetTag.SetActive(targeted);
            }
        }
    }

    /// <summary>
    /// Click handler for enemy slots: marks the hero as the selected target,
    /// or clears the mark when the same slot is clicked again. Pure UI state.
    /// </summary>
    private void OnEnemySlotClicked(HeroInstance hero)
    {
        if (hero == null || !hero.isAlive)
        {
            return;
        }

        selectedTarget = ReferenceEquals(selectedTarget, hero) ? null : hero;
    }

    /// <summary>
    /// One square in the action bar's middle cluster: the basic-attack
    /// square or one of the hero's skill squares. Shows a short glyph, a
    /// label underneath, and a ready/cooldown look read live from the acting
    /// hero (read-only). Placeholder visuals only - clicking does nothing.
    /// </summary>
    private sealed class ActionSlotView
    {
        private readonly Image back;

        private readonly Image border;

        private readonly Text glyph;

        private readonly Text label;

        private readonly Text cooldownTag;

        /// <summary>The skill this square shows, or null for the basic-attack / locked squares.</summary>
        private SkillData boundSkill;

        private string lastLabel;

        private bool lastReady;

        public ActionSlotView(BattleHud hud, RectTransform bar, string name, float x)
        {
            RectTransform rect = CreateRect(bar, name);
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -16f);
            rect.sizeDelta = new Vector2(ActionSlotSize, ActionSlotSize);

            back = hud.CreateSlicedImage(rect, "Back", ActionSlotColor, hud.panelSprite,
                TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);
            border = hud.CreateSlicedImage(rect, "Border", ActionSlotReadyColor, hud.frameSprite,
                TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);

            glyph = hud.CreateText(rect, "Glyph", string.Empty,
                ActionSlotFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);

            label = hud.CreateText(bar, name + "Label", string.Empty,
                ActionLabelFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(x - 6f, -(16f + ActionSlotSize + 4f)), new Vector2(ActionSlotSize + 12f, 20f), wrap: true);
            label.color = new Color(1f, 1f, 1f, 0.75f);

            // Small "CD" chip shown while the bound skill is cooling down.
            RectTransform cdRect = CreateRect(rect, "CooldownTag");
            cdRect.anchorMin = new Vector2(1f, 1f);
            cdRect.anchorMax = new Vector2(1f, 1f);
            cdRect.pivot = new Vector2(1f, 1f);
            cdRect.anchoredPosition = new Vector2(-4f, -4f);
            cdRect.sizeDelta = new Vector2(34f, 20f);
            hud.CreateSlicedImage(cdRect, "Back", TargetTagColor, hud.panelSprite,
                TopLeft, TopLeft, Vector2.zero, cdRect.sizeDelta);
            cooldownTag = hud.CreateText(cdRect, "Label", "CD", 12, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, Vector2.zero, Vector2.zero);
            Stretch(cooldownTag.rectTransform);
            cdRect.gameObject.SetActive(false);
        }

        /// <summary>Sets static content (used by the basic-attack square).</summary>
        public void SetContent(string glyphText, string labelText, bool ready)
        {
            boundSkill = null;
            glyph.text = glyphText;
            ApplyLabel(labelText);
            ApplyReadiness(ready);
        }

        /// <summary>Binds the square to one of the acting hero's skills.</summary>
        public void BindSkill(SkillData skill)
        {
            boundSkill = skill;
            glyph.text = string.IsNullOrEmpty(skill.skillName) ? "?" : skill.skillName.Substring(0, 1);
            ApplyLabel(skill.skillName);
        }

        /// <summary>Marks the square as locked (no skill in this position yet).</summary>
        public void SetLocked()
        {
            boundSkill = null;
            glyph.text = "-";
            ApplyLabel("LOCKED");
            ApplyReadiness(false);
        }

        /// <summary>Refreshes the ready/cooldown look from the acting hero's live state (read-only).</summary>
        public void RefreshReadiness(HeroInstance hero)
        {
            if (boundSkill == null || hero == null)
            {
                return;
            }

            ApplyReadiness(hero.IsSkillReady(boundSkill));
        }

        private void ApplyLabel(string text)
        {
            if (text != lastLabel)
            {
                lastLabel = text;
                label.text = text ?? string.Empty;
            }
        }

        private void ApplyReadiness(bool ready)
        {
            if (ready == lastReady)
            {
                return;
            }

            lastReady = ready;
            back.color = ready ? ActionSlotColor : ActionSlotDimColor;
            border.color = ready ? ActionSlotReadyColor : new Color(1f, 1f, 1f, 0.18f);
            glyph.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            cooldownTag.transform.parent.gameObject.SetActive(!ready && boundSkill != null);
        }
    }
}
