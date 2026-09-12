using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated Canvas screen listing the player's owned heroes (the
/// runner's <see cref="HeroRoster"/>) with a details view for the selected
/// hero. Opened from the main menu's HEROES tab
/// (<see cref="BattleTestRunner.OpenHeroesCollection"/>); the BACK button
/// returns to the menu through
/// <see cref="BattleTestRunner.CloseHeroesCollection"/>.
///
/// Everything is built from code in <see cref="Awake"/> - no Canvas, panel,
/// or text has to be assembled in the Unity Editor:
/// - a Screen Space - Overlay <see cref="Canvas"/> with a
///   <see cref="CanvasScaler"/> (Scale With Screen Size, 1920x1080
///   reference resolution), sorted above the main menu so it can cover it;
/// - a top bar with a BACK button, the screen title, and the owned-hero
///   count read live from the roster;
/// - a left column with one card per owned hero (portrait placeholder,
///   name, rarity, role, current level) - clicking a card selects that
///   hero and updates the details view;
/// - a details panel on the right showing the selected hero's portrait
///   placeholder, name, rarity, role, level, current XP and the XP
///   required for the next level, current stats (HP / ATK / DEF / SPD),
///   and its available skills.
///
/// This is a pure view layer: every value is read from the actual
/// <see cref="HeroInstance"/> (progression and current stats) and its
/// <see cref="HeroData"/> template (name, rarity, role, skill kit) -
/// nothing about any specific hero is hardcoded here, so future heroes and
/// future rarities render without code changes. There is no leveling UI
/// (progression interaction is a later milestone), no gacha, and no combat
/// logic; battles keep building their own ephemeral instances exactly as
/// before.
/// </summary>
public class HeroesScreen : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Appearance/layout constants - the screen is generated from code, so
    // these live here instead of the Inspector. All sizes assume the
    // 1920x1080 reference resolution and scale with the CanvasScaler. The
    // palette mirrors MainMenu's dark fantasy look.
    // ---------------------------------------------------------------------

    /// <summary>Full-screen dark-navy backdrop behind the whole screen.</summary>
    private static readonly Color BackgroundColor = new Color(0.045f, 0.055f, 0.10f);

    /// <summary>Panel behind the top bar.</summary>
    private static readonly Color BarPanelColor = new Color(0.07f, 0.09f, 0.15f, 0.96f);

    /// <summary>Backdrop of hero cards and the details panel.</summary>
    private static readonly Color CardColor = new Color(0.125f, 0.16f, 0.25f, 0.96f);

    /// <summary>Gold accent: title, selected-card frame, highlighted values.</summary>
    private static readonly Color GoldAccent = new Color(0.85f, 0.72f, 0.35f);

    /// <summary>Dark chip behind small badges (level, role, count).</summary>
    private static readonly Color BadgeColor = new Color(0.04f, 0.05f, 0.09f, 0.92f);

    /// <summary>Inner portrait backdrop (behind the icon or initial).</summary>
    private static readonly Color PortraitBackColor = new Color(0.05f, 0.06f, 0.10f, 1f);

    /// <summary>Text color on gold surfaces.</summary>
    private static readonly Color TextOnGold = new Color(0.09f, 0.11f, 0.17f);

    /// <summary>Dimmed text used for subtitles, stat labels, and skill meta.</summary>
    private static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.55f);

    /// <summary>Rich-text color tag for stat labels.</summary>
    private const string StatLabelTag = "<color=#99A3AF>";

    // ----- layout -----

    /// <summary>Reference width the CanvasScaler scales against.</summary>
    private const float ReferenceWidth = 1920f;

    /// <summary>Reference height the CanvasScaler scales against.</summary>
    private const float ReferenceHeight = 1080f;

    /// <summary>Screen-edge margin shared by the bars and the content area.</summary>
    private const float ScreenMargin = 24f;

    /// <summary>Height of the top bar.</summary>
    private const float TopBarHeight = 110f;

    /// <summary>Width of one hero card in the left column.</summary>
    private const float CardWidth = 500f;

    /// <summary>Height of one hero card in the left column.</summary>
    private const float CardHeight = 158f;

    /// <summary>Vertical gap between hero cards.</summary>
    private const float CardGap = 14f;

    /// <summary>Size of a card portrait frame.</summary>
    private const float CardPortraitSize = 118f;

    /// <summary>Size of the details panel's portrait frame.</summary>
    private const float DetailPortraitSize = 220f;

    /// <summary>Width and height of one skill row in the details panel.</summary>
    private const float SkillRowWidth = 1276f;

    /// <summary>Height of one skill row in the details panel.</summary>
    private const float SkillRowHeight = 64f;

    /// <summary>The screen draws above the main menu (10) and battle HUD (0).</summary>
    private const int ScreenSortingOrder = 20;

    /// <summary>Top-left anchor point, shared by most child rects.</summary>
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    /// <summary>Top-center anchor point.</summary>
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

    // ----- font sizes -----

    private const int TitleFontSize = 34;
    private const int BackButtonFontSize = 22;
    private const int CountFontSize = 18;
    private const int CardNameFontSize = 24;
    private const int CardBadgeFontSize = 15;
    private const int CardPortraitFontSize = 46;
    private const int SectionHeaderFontSize = 26;
    private const int DetailNameFontSize = 38;
    private const int DetailBadgeFontSize = 16;
    private const int DetailXpFontSize = 22;
    private const int DetailStatFontSize = 24;
    private const int DetailSkillNameFontSize = 22;
    private const int DetailSkillMetaFontSize = 18;
    private const int DetailPortraitFontSize = 92;
    private const int EmptyStateFontSize = 26;

    // ---------------------------------------------------------------------
    // Runtime state: chrome built once in Awake; the hero cards and the
    // details view are (re)built from the roster once the runner reference
    // exists.
    // ---------------------------------------------------------------------

    /// <summary>The battle runner that owns this screen and the roster; assigned by <see cref="Create"/>.</summary>
    private BattleTestRunner runner;

    /// <summary>Unity's built-in legacy font (Arial.ttf no longer ships with Unity 6).</summary>
    private Font font;

    /// <summary>Shared solid rounded-rectangle sprite (9-sliced) for panels, cards, badges, and buttons.</summary>
    private Sprite panelSprite;

    /// <summary>Shared rounded-outline sprite (9-sliced) for frames and the selected-card highlight.</summary>
    private Sprite frameSprite;

    /// <summary>Runtime-generated textures/sprites; destroyed in <see cref="OnDestroy"/> so re-entering Play mode never leaks them.</summary>
    private readonly List<Object> runtimeArt = new List<Object>();

    /// <summary>Parent of the hero cards in the left column.</summary>
    private RectTransform cardListRoot;

    /// <summary>Root of the details panel's skill rows; rebuilt for each selection.</summary>
    private RectTransform skillsRoot;

    /// <summary>The hero cards' root objects, one per roster hero.</summary>
    private readonly List<GameObject> cardRoots = new List<GameObject>();

    /// <summary>The gold selection frame of each hero card; exactly one is visible at a time.</summary>
    private readonly List<Image> cardSelectionFrames = new List<Image>();

    /// <summary>The hero each card shows, in the same order as the cards.</summary>
    private readonly List<HeroInstance> cardHeroes = new List<HeroInstance>();

    /// <summary>Owned-hero count chip in the top bar.</summary>
    private Text countText;

    /// <summary>The currently selected hero whose details are shown, or null when the roster is empty.</summary>
    private HeroInstance selectedHero;

    // ----- details view fields (assigned in BuildDetailsPanel) -----

    /// <summary>Details portrait frame; recolored with the hero's rarity.</summary>
    private Image detailPortraitFrame;

    /// <summary>Details portrait icon (shown when the template has art).</summary>
    private Image detailPortraitIcon;

    /// <summary>Details portrait initial glyph (shown when no art exists).</summary>
    private Text detailPortraitGlyph;

    /// <summary>Details hero name.</summary>
    private Text detailName;

    /// <summary>Details rarity badge backdrop; recolored with the hero's rarity.</summary>
    private Image detailRarityBack;

    /// <summary>Details rarity badge label.</summary>
    private Text detailRarityText;

    /// <summary>Details role badge label.</summary>
    private Text detailRoleText;

    /// <summary>Details level badge label ("LEVEL 5").</summary>
    private Text detailLevelText;

    /// <summary>Details XP progress line ("XP 40 / 100" or "MAX LEVEL").</summary>
    private Text detailXpText;

    /// <summary>Details HP line.</summary>
    private Text detailStatHp;

    /// <summary>Details attack line.</summary>
    private Text detailStatAtk;

    /// <summary>Details defense line.</summary>
    private Text detailStatDef;

    /// <summary>Details speed line.</summary>
    private Text detailStatSpd;

    /// <summary>Details skills section header.</summary>
    private Text detailSkillsHeader;

    /// <summary>Details empty-state line, shown instead of everything else when no hero is selected.</summary>
    private Text detailEmptyText;

    /// <summary>The details elements that only make sense with a selected hero.</summary>
    private readonly List<Graphic> detailHeroElements = new List<Graphic>();

    /// <summary>
    /// Creates the screen for the given runner: a new "HeroesScreen"
    /// GameObject whose Canvas hierarchy builds itself in <see cref="Awake"/>.
    /// Called by <see cref="BattleTestRunner.OpenHeroesCollection"/> on
    /// first open; afterwards the same instance is re-activated.
    /// </summary>
    public static HeroesScreen Create(BattleTestRunner runner)
    {
        GameObject host = new GameObject("HeroesScreen");
        HeroesScreen screen = host.AddComponent<HeroesScreen>();
        screen.runner = runner;
        screen.Initialize();
        return screen;
    }

    /// <summary>
    /// Builds the static screen chrome. Runs during AddComponent, before
    /// <see cref="runner"/> is assigned, so it must not read the runner -
    /// the roster-dependent hero cards are built in <see cref="Initialize"/>.
    /// </summary>
    private void Awake()
    {
        // Unity 6 ships the legacy built-in font as "LegacyRuntime.ttf"
        // (Arial.ttf was removed from the engine in Unity 2022.2+).
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 9-sliced rounded squares give every panel, card, badge, and button
        // clean corners at any size without any art assets; the outline
        // variant draws rarity frames and the selected-card highlight. This
        // factory intentionally mirrors MainMenu's (kept separate so the
        // other UI files stay untouched).
        panelSprite = CreateRoundedSprite(outlineOnly: false);
        frameSprite = CreateRoundedSprite(outlineOnly: true);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the main menu, so the screen covers it while open.
        canvas.sortingOrder = ScreenSortingOrder;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;

        // The GraphicRaycaster is what lets the EventSystem hit this Canvas's
        // graphics at all; runtime-generated canvases do not get one
        // automatically.
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = (RectTransform)transform;

        // The backdrop is raycastable so clicks on empty screen areas are
        // consumed here instead of falling through to the menu or the battle
        // HUD behind this (higher-sorting) canvas.
        Image background = CreateStretchedImage(root, "Background", BackgroundColor);
        background.raycastTarget = true;

        BuildTopBar(root);
        BuildContent(root);

        // Button clicks need an EventSystem; create one only if the scene
        // does not already provide it (MainMenu and BattleHud use the same
        // guard). The input module must match the project's Input System
        // only setup.
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }

    /// <summary>
    /// Builds the roster-dependent content once the runner reference has
    /// been assigned by <see cref="Create"/>: the hero cards and the
    /// initial selection.
    /// </summary>
    private void Initialize()
    {
        RebuildHeroCards();
        SelectHero(cardHeroes.Count > 0 ? cardHeroes[0] : null);
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
    }

    /// <summary>
    /// Rebuilds the hero cards every time the screen is (re)opened, so
    /// heroes added to the roster while it was closed - a fresh summon, for
    /// example - appear immediately. The very first OnEnable runs during
    /// <see cref="Create"/> before the runner is assigned; Initialize
    /// performs that initial build instead.
    /// </summary>
    private void OnEnable()
    {
        if (runner == null)
        {
            return;
        }

        RebuildHeroCards();
        SelectHero(FindHeroToSelect());
    }

    /// <summary>
    /// The hero to select after a rebuild: the previously selected instance
    /// when it is still owned (matched by reference, so duplicate heroes of
    /// the same template stay distinct), otherwise the first card.
    /// </summary>
    private HeroInstance FindHeroToSelect()
    {
        foreach (HeroInstance hero in cardHeroes)
        {
            if (ReferenceEquals(hero, selectedHero))
            {
                return hero;
            }
        }

        return cardHeroes.Count > 0 ? cardHeroes[0] : null;
    }

    // ---------------------------------------------------------------------
    // Navigation and selection.
    // ---------------------------------------------------------------------

    /// <summary>
    /// BACK button: closes the collection and returns to the main menu
    /// through the runner. Navigation only - no state is touched.
    /// </summary>
    private void GoBack()
    {
        if (runner != null)
        {
            runner.CloseHeroesCollection();
        }
    }

    /// <summary>
    /// Selects the given hero (or null for the empty state) and refreshes
    /// the details view and the card highlights from the actual
    /// <see cref="HeroInstance"/> state.
    /// </summary>
    private void SelectHero(HeroInstance hero)
    {
        selectedHero = hero;

        for (int i = 0; i < cardHeroes.Count; i++)
        {
            bool selected = hero != null && ReferenceEquals(cardHeroes[i], hero);
            if (cardSelectionFrames[i].gameObject.activeSelf != selected)
            {
                cardSelectionFrames[i].gameObject.SetActive(selected);
            }
        }

        RefreshDetails();
    }

    /// <summary>
    /// Reads every details value from the selected hero's live
    /// <see cref="HeroInstance"/> (level, XP, current stats) and its
    /// <see cref="HeroData"/> template (name, rarity, role, skill kit).
    /// </summary>
    private void RefreshDetails()
    {
        HeroInstance hero = selectedHero;

        // Empty roster (or a null selection): show the empty state instead.
        bool hasHero = hero != null && hero.data != null;
        foreach (Graphic element in detailHeroElements)
        {
            element.gameObject.SetActive(hasHero);
        }

        detailEmptyText.gameObject.SetActive(!hasHero);
        if (!hasHero)
        {
            ClearSkillRows();
            return;
        }

        HeroData data = hero.data;
        RarityStyle rarity = RarityStyleFor(data.rarity);

        // Portrait: the template's icon when art exists, otherwise the
        // hero's initial on a rarity-tinted frame.
        if (data.icon != null)
        {
            detailPortraitIcon.sprite = data.icon;
            detailPortraitIcon.gameObject.SetActive(true);
            detailPortraitGlyph.gameObject.SetActive(false);
        }
        else
        {
            detailPortraitIcon.gameObject.SetActive(false);
            detailPortraitGlyph.gameObject.SetActive(true);
            detailPortraitGlyph.text = InitialOf(data.heroName);
            detailPortraitGlyph.color = Color.Lerp(rarity.Color, Color.white, 0.35f);
        }

        detailPortraitFrame.color = rarity.Color;

        detailName.text = hero.displayName;
        detailRarityBack.color = Color.Lerp(rarity.Color, Color.black, 0.72f);
        detailRarityText.text = rarity.Label;
        detailRarityText.color = Color.Lerp(rarity.Color, Color.white, 0.25f);
        detailRoleText.text = RoleLabel(data.role);
        detailLevelText.text = "LEVEL " + hero.Level;

        // XP: current experience vs. the requirement for the next level;
        // max-level heroes show MAX LEVEL instead of a meaningless 0 / 0.
        detailXpText.text = hero.IsMaxLevel
            ? "MAX LEVEL  (Lv " + hero.MaxLevel + ")"
            : "XP  " + hero.Experience + " / " + hero.ExperienceToNextLevel + "  to next level";

        // Current stats straight off the instance.
        detailStatHp.text = StatLine("HP", hero.currentHealth + " / " + hero.maxHealth);
        detailStatAtk.text = StatLine("ATTACK", hero.currentAttack.ToString());
        detailStatDef.text = StatLine("DEFENSE", hero.currentDefense.ToString());
        detailStatSpd.text = StatLine("SPEED", hero.currentSpeed.ToString());

        RebuildSkillRows(hero);
    }

    // ---------------------------------------------------------------------
    // Layout construction.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds the top bar: BACK button on the left, the screen title in the
    /// center, and the owned-hero count on the right.
    /// </summary>
    private void BuildTopBar(RectTransform root)
    {
        RectTransform bar = CreateRect(root, "TopBar");
        bar.anchorMin = TopLeft;
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.offsetMin = new Vector2(ScreenMargin, -TopBarHeight - ScreenMargin);
        bar.offsetMax = new Vector2(-ScreenMargin, -ScreenMargin);

        CreateStretchedSlicedImage(bar, "Back", BarPanelColor, panelSprite);

        // ---- BACK button (left) ----
        RectTransform backRect = CreateRect(bar, "BackButton");
        backRect.anchorMin = TopLeft;
        backRect.anchorMax = TopLeft;
        backRect.pivot = new Vector2(0f, 0.5f);
        backRect.anchoredPosition = new Vector2(16f, -TopBarHeight * 0.5f);
        backRect.sizeDelta = new Vector2(170f, 64f);

        Image backBack = CreateSlicedImage(backRect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, backRect.sizeDelta);
        // Raycastable so the EventSystem can hit the button (the image
        // factory disables raycast targets for decoration).
        backBack.raycastTarget = true;

        Text backLabel = CreateText(backRect, "Label", "BACK",
            BackButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(backLabel.rectTransform);
        backLabel.color = GoldAccent;

        Button backButton = backRect.gameObject.AddComponent<Button>();
        backButton.targetGraphic = backBack;
        backButton.onClick.AddListener(GoBack);

        // ---- Title (center) ----
        CreateText(bar, "Title", "HEROES",
            TitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -38f), new Vector2(400f, 44f)).color = GoldAccent;

        // ---- Owned count (right); filled in RebuildHeroCards ----
        RectTransform countRect = CreateRect(bar, "CountChip");
        countRect.anchorMin = new Vector2(1f, 0.5f);
        countRect.anchorMax = new Vector2(1f, 0.5f);
        countRect.pivot = new Vector2(1f, 0.5f);
        countRect.anchoredPosition = new Vector2(-16f, 0f);
        countRect.sizeDelta = new Vector2(190f, 40f);

        CreateSlicedImage(countRect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, countRect.sizeDelta);
        countText = CreateText(countRect, "Label", string.Empty,
            CountFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, Vector2.zero, countRect.sizeDelta);
        countText.color = GoldAccent;
    }

    /// <summary>
    /// Builds the content area between the bars: the left card column and
    /// the right details panel. The hero cards themselves are (re)built in
    /// <see cref="RebuildHeroCards"/> once the roster is reachable.
    /// </summary>
    private void BuildContent(RectTransform root)
    {
        RectTransform content = CreateRect(root, "Content");
        Stretch(content, ScreenMargin);
        content.offsetMax = new Vector2(-ScreenMargin, -TopBarHeight - ScreenMargin);

        // ---- Left: hero card column ----
        RectTransform list = CreateRect(content, "CardList");
        list.anchorMin = TopLeft;
        list.anchorMax = TopLeft;
        list.pivot = new Vector2(0f, 1f);
        list.anchoredPosition = new Vector2(0f, 0f);
        list.sizeDelta = new Vector2(CardWidth, 0f);
        cardListRoot = list;

        // ---- Right: details panel ----
        RectTransform details = CreateRect(content, "DetailsPanel");
        details.anchorMin = TopLeft;
        details.anchorMax = new Vector2(1f, 1f);
        details.pivot = new Vector2(0f, 1f);
        details.offsetMin = new Vector2(CardWidth + 20f, 0f);
        details.offsetMax = Vector2.zero;

        CreateStretchedSlicedImage(details, "Back", CardColor, panelSprite);
        BuildDetailsPanel(details);
    }

    /// <summary>
    /// Builds the details panel's static elements: portrait, name, badges,
    /// XP line, stat lines, and the skills section. All values are filled
    /// in <see cref="RefreshDetails"/>.
    /// </summary>
    private void BuildDetailsPanel(RectTransform panel)
    {
        // ---- Portrait (top-left) ----
        detailPortraitFrame = CreateSlicedImage(panel, "PortraitFrame", Color.white, frameSprite,
            TopLeft, TopLeft, new Vector2(36f, -36f), new Vector2(DetailPortraitSize, DetailPortraitSize));
        CreateSlicedImage(panel, "PortraitBack", PortraitBackColor, panelSprite,
            TopLeft, TopLeft, new Vector2(45f, -45f), new Vector2(DetailPortraitSize - 18f, DetailPortraitSize - 18f));

        detailPortraitIcon = CreateSlicedImage(panel, "PortraitIcon", Color.white, null,
            TopLeft, TopLeft, new Vector2(45f, -45f), new Vector2(DetailPortraitSize - 18f, DetailPortraitSize - 18f));
        detailPortraitIcon.preserveAspect = true;
        detailPortraitIcon.gameObject.SetActive(false);

        detailPortraitGlyph = CreateText(panel, "PortraitGlyph", "?",
            DetailPortraitFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(45f, -45f), new Vector2(DetailPortraitSize - 18f, DetailPortraitSize - 18f));

        // ---- Name, badges, XP (right of the portrait) ----
        detailName = CreateText(panel, "Name", string.Empty,
            DetailNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(300f, -44f), new Vector2(640f, 46f));

        detailRarityBack = CreateSlicedImage(panel, "RarityBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(300f, -104f), new Vector2(140f, 34f));
        detailRarityText = CreateText(panel, "RarityText", string.Empty,
            DetailBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(300f, -104f), new Vector2(140f, 34f));

        CreateSlicedImage(panel, "RoleBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(452f, -104f), new Vector2(140f, 34f));
        detailRoleText = CreateText(panel, "RoleText", string.Empty,
            DetailBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(452f, -104f), new Vector2(140f, 34f));
        detailRoleText.color = DimTextColor;

        RectTransform levelRect = CreateRect(panel, "LevelBadge");
        levelRect.anchorMin = new Vector2(1f, 1f);
        levelRect.anchorMax = new Vector2(1f, 1f);
        levelRect.pivot = new Vector2(1f, 1f);
        levelRect.anchoredPosition = new Vector2(-36f, -36f);
        levelRect.sizeDelta = new Vector2(160f, 40f);
        CreateSlicedImage(levelRect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, levelRect.sizeDelta);
        detailLevelText = CreateText(levelRect, "Label", string.Empty,
            DetailBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, Vector2.zero, levelRect.sizeDelta);
        detailLevelText.color = GoldAccent;

        detailXpText = CreateText(panel, "Xp", string.Empty,
            DetailXpFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(300f, -156f), new Vector2(760f, 28f));
        detailXpText.color = DimTextColor;

        // ---- Stats (under name/badges) ----
        detailStatHp = CreateText(panel, "StatHp", string.Empty,
            DetailStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(300f, -206f), new Vector2(560f, 30f));
        detailStatAtk = CreateText(panel, "StatAtk", string.Empty,
            DetailStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(300f, -246f), new Vector2(560f, 30f));
        detailStatDef = CreateText(panel, "StatDef", string.Empty,
            DetailStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(300f, -286f), new Vector2(560f, 30f));
        detailStatSpd = CreateText(panel, "StatSpd", string.Empty,
            DetailStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(300f, -326f), new Vector2(560f, 30f));

        // ---- Skills section ----
        detailSkillsHeader = CreateText(panel, "SkillsHeader", "SKILLS",
            SectionHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(36f, -356f), new Vector2(300f, 34f));
        detailSkillsHeader.color = GoldAccent;

        RectTransform skills = CreateRect(panel, "Skills");
        skills.anchorMin = TopLeft;
        skills.anchorMax = TopLeft;
        skills.pivot = new Vector2(0f, 1f);
        skills.anchoredPosition = new Vector2(36f, -398f);
        skills.sizeDelta = new Vector2(SkillRowWidth, 0f);
        skillsRoot = skills;

        // ---- Empty state (shown only when the roster has no heroes) ----
        detailEmptyText = CreateText(panel, "Empty", "No heroes yet",
            EmptyStateFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -160f), new Vector2(700f, 40f));
        detailEmptyText.color = DimTextColor;
        detailEmptyText.gameObject.SetActive(false);

        // Everything except the empty state disappears when no hero is
        // selected.
        detailHeroElements.AddRange(new Graphic[]
        {
            detailPortraitFrame, detailPortraitIcon, detailPortraitGlyph, detailName,
            detailRarityBack, detailRarityText, detailRoleText, detailLevelText, detailXpText,
            detailStatHp, detailStatAtk, detailStatDef, detailStatSpd, detailSkillsHeader,
        });
    }

    /// <summary>
    /// Rebuilds the hero cards from the roster: one row card per owned hero
    /// (portrait placeholder, name, rarity, role, current level), clickable
    /// to select. Also refreshes the owned-count chip. Called once on
    /// <see cref="Initialize"/>; a future roster change (e.g. gacha) can
    /// call it again to reflect new heroes.
    /// </summary>
    private void RebuildHeroCards()
    {
        foreach (GameObject card in cardRoots)
        {
            if (card != null)
            {
                Destroy(card);
            }
        }

        cardRoots.Clear();
        cardSelectionFrames.Clear();
        cardHeroes.Clear();

        HeroRoster roster = runner != null ? runner.Roster : null;
        int count = roster != null ? roster.Count : 0;

        countText.text = count + " OWNED";

        if (roster == null)
        {
            return;
        }

        for (int i = 0; i < roster.Heroes.Count; i++)
        {
            BuildHeroCard(roster.Heroes[i], i);
        }
    }

    /// <summary>
    /// Builds one hero card: portrait placeholder (rarity frame with the
    /// template's icon or the hero's initial), name, level badge, rarity
    /// and role badges, and a gold frame shown while selected.
    /// </summary>
    private void BuildHeroCard(HeroInstance hero, int index)
    {
        HeroData data = hero != null ? hero.data : null;
        RarityStyle rarity = RarityStyleFor(data != null ? data.rarity : HeroRarity.Common);

        RectTransform card = CreateRect(cardListRoot, "HeroCard" + (index + 1));
        card.anchorMin = TopLeft;
        card.anchorMax = TopLeft;
        card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(0f, -index * (CardHeight + CardGap));
        card.sizeDelta = new Vector2(CardWidth, CardHeight);

        Image back = CreateSlicedImage(card, "Back", CardColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, card.sizeDelta);
        CreateSlicedImage(card, "RarityFrame", rarity.Color, frameSprite,
            TopLeft, TopLeft, Vector2.zero, card.sizeDelta);

        // Gold frame shown while this card is the selected hero.
        Image selection = CreateSlicedImage(card, "SelectionFrame", GoldAccent, frameSprite,
            TopLeft, TopLeft, new Vector2(-5f, 5f), new Vector2(CardWidth + 10f, CardHeight + 10f));
        selection.gameObject.SetActive(false);

        // Portrait placeholder (left).
        CreateSlicedImage(card, "PortraitFrame", rarity.Color, frameSprite,
            TopLeft, TopLeft, new Vector2(16f, -20f), new Vector2(CardPortraitSize, CardPortraitSize));
        CreateSlicedImage(card, "PortraitBack", PortraitBackColor, panelSprite,
            TopLeft, TopLeft, new Vector2(23f, -27f), new Vector2(CardPortraitSize - 14f, CardPortraitSize - 14f));

        if (data != null && data.icon != null)
        {
            Image icon = CreateSlicedImage(card, "PortraitIcon", Color.white, null,
                TopLeft, TopLeft, new Vector2(23f, -27f), new Vector2(CardPortraitSize - 14f, CardPortraitSize - 14f));
            icon.sprite = data.icon;
            icon.preserveAspect = true;
        }
        else
        {
            CreateText(card, "PortraitGlyph",
                data != null ? InitialOf(data.heroName) : "?",
                CardPortraitFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(23f, -27f), new Vector2(CardPortraitSize - 14f, CardPortraitSize - 14f))
                .color = Color.Lerp(rarity.Color, Color.white, 0.35f);
        }

        if (data == null)
        {
            // Defensive: the roster never stores null, but a card built
            // without a template still renders sensibly.
            CreateText(card, "Name", "Unknown hero",
                CardNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(152f, -24f), new Vector2(330f, 34f)).color = DimTextColor;
            return;
        }

        // Name, level, rarity, role (right of the portrait).
        CreateText(card, "Name", hero.displayName,
            CardNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(152f, -24f), new Vector2(330f, 34f));

        CreateSlicedImage(card, "LevelBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(152f, -70f), new Vector2(84f, 28f));
        CreateText(card, "LevelText", "Lv " + hero.Level,
            CardBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(152f, -70f), new Vector2(84f, 28f)).color = GoldAccent;

        CreateSlicedImage(card, "RarityBadge", Color.Lerp(rarity.Color, Color.black, 0.72f), panelSprite,
            TopLeft, TopLeft, new Vector2(248f, -70f), new Vector2(104f, 28f));
        CreateText(card, "RarityText", rarity.Label,
            CardBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(248f, -70f), new Vector2(104f, 28f))
            .color = Color.Lerp(rarity.Color, Color.white, 0.25f);

        CreateSlicedImage(card, "RoleBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(364f, -70f), new Vector2(110f, 28f));
        CreateText(card, "RoleText", RoleLabel(data.role),
            CardBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(364f, -70f), new Vector2(110f, 28f)).color = DimTextColor;

        // Clickable card: the backdrop is the raycast target (the image
        // factory disables raycast targets for decoration).
        back.raycastTarget = true;
        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        HeroInstance captured = hero;
        button.onClick.AddListener(() => SelectHero(captured));

        cardRoots.Add(card.gameObject);
        cardSelectionFrames.Add(selection);
        cardHeroes.Add(hero);
    }

    /// <summary>Removes the current skill rows (called before rebuilding).</summary>
    private void ClearSkillRows()
    {
        for (int i = 0; i < skillsRoot.childCount; i++)
        {
            Destroy(skillsRoot.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// Builds one row per skill in the selected hero's kit, straight from
    /// the live <see cref="HeroInstance.skills"/> list: skill name on the
    /// left, effect / target / power / cooldown on the right.
    /// </summary>
    private void RebuildSkillRows(HeroInstance hero)
    {
        ClearSkillRows();

        List<SkillData> skills = hero.skills;
        if (skills == null)
        {
            return;
        }

        for (int i = 0; i < skills.Count; i++)
        {
            SkillData skill = skills[i];
            if (skill == null)
            {
                continue;
            }

            RectTransform row = CreateRect(skillsRoot, "Skill" + (i + 1));
            row.anchorMin = TopLeft;
            row.anchorMax = TopLeft;
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(0f, -i * (SkillRowHeight + 10f));
            row.sizeDelta = new Vector2(SkillRowWidth, SkillRowHeight);

            CreateSlicedImage(row, "Back", BadgeColor, panelSprite,
                TopLeft, TopLeft, Vector2.zero, row.sizeDelta);

            string name = string.IsNullOrEmpty(skill.skillName) ? "Unnamed skill" : skill.skillName;
            CreateText(row, "Name", name,
                DetailSkillNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(20f, -8f), new Vector2(480f, 30f)).color = GoldAccent;

            CreateText(row, "Meta", SkillMetaLine(skill),
                DetailSkillMetaFontSize, FontStyle.Normal, TextAnchor.MiddleRight,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, -14f), new Vector2(700f, 24f))
                .color = DimTextColor;
        }
    }

    /// <summary>One skill meta line: effect, target, power, cooldown.</summary>
    private static string SkillMetaLine(SkillData skill)
    {
        return SkillTypeLabel(skill.type) + "  |  " + SkillTargetLabel(skill.target)
            + "  |  " + skill.damageMultiplier.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture) + "x"
            + "  |  CD " + skill.cooldown;
    }

    /// <summary>One stat line with a dimmed label, e.g. "HP  175 / 175".</summary>
    private static string StatLine(string label, string value)
    {
        return StatLabelTag + label + "</color>  " + value;
    }

    // ---------------------------------------------------------------------
    // Rarity presentation. This table mirrors MainMenu's (kept separate so
    // the other UI files stay untouched); the copies must stay in sync
    // until a shared UI theme exists.
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
    /// the enum value's own name, so extending <see cref="HeroRarity"/>
    /// never breaks this screen.
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

    /// <summary>Human label for a hero role; future enum values fall through to their own name.</summary>
    private static string RoleLabel(HeroRole role)
    {
        return role.ToString();
    }

    /// <summary>Human label for a skill's effect; future enum values fall through to their own name.</summary>
    private static string SkillTypeLabel(SkillType type)
    {
        return type.ToString();
    }

    /// <summary>Human label for a skill's target; future enum values fall through to their own name.</summary>
    private static string SkillTargetLabel(SkillTarget target)
    {
        return target.ToString();
    }

    /// <summary>The first letter of a hero's name, for placeholder portraits ("?" when unknown).</summary>
    private static string InitialOf(string heroName)
    {
        return string.IsNullOrEmpty(heroName) ? "?" : heroName.Substring(0, 1).ToUpperInvariant();
    }

    // ---------------------------------------------------------------------
    // Basic view factories (anchored rects, images, texts). Mirrors
    // MainMenu's so this file stays self-contained.
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
    private Text CreateText(Transform parent, string name, string initialText, int fontSize, FontStyle fontStyle, TextAnchor alignment, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
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
        text.text = initialText ?? string.Empty;
        return text;
    }

    /// <summary>Creates a 9-sliced Image stretched across its parent (for stretched bars).</summary>
    private Image CreateStretchedSlicedImage(RectTransform parent, string name, Color color, Sprite sprite)
    {
        RectTransform rect = CreateRect(parent, name);
        Stretch(rect);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        image.raycastTarget = false;
        return image;
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
    /// cards, badges, buttons); pass a null sprite for a plain quad.
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
    // Runtime sprite generation (no art assets required). Mirrors MainMenu's
    // factory so this file stays self-contained.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a shared 48x48 white rounded square as a 9-sliced sprite, so
    /// panels, cards, badges, and buttons get clean rounded corners at any
    /// size. The outline variant draws only a rounded border, used for
    /// rarity frames and the selected-card highlight.
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
}
