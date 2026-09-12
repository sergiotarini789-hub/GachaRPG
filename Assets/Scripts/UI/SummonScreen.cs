using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated Canvas screen for the prototype hero summon. Opened
/// from the main menu's SUMMON tab
/// (<see cref="BattleTestRunner.OpenSummonScreen"/>); the BACK button
/// returns to the menu through
/// <see cref="BattleTestRunner.CloseSummonScreen"/>.
///
/// Everything is built from code in <see cref="Awake"/> - no Canvas, panel,
/// or text has to be assembled in the Unity Editor:
/// - a top bar with a BACK button and the screen title;
/// - a rarity-odds panel listing the summon table exactly as the
///   <see cref="SummonService"/> reports it (the UI holds no rates of its
///   own);
/// - a free SUMMON x1 button (no currency/economy system exists yet);
/// - a result area showing the hero produced by the latest summon:
///   "SUMMONED!", portrait placeholder, name, rarity, role, level, and
///   current stats, all read from the newly created
///   <see cref="HeroInstance"/>, plus a note when an empty rarity pool
///   caused a fallback;
/// - a VIEW HEROES button that opens the Heroes collection to verify the
///   new roster entry.
///
/// This is a pure view layer: pressing SUMMON only asks the runner's
/// <see cref="SummonService"/> to perform a summon and then renders the
/// returned <see cref="SummonResult"/>. All gacha logic (weights, pools,
/// fallbacks, instance creation, roster insertion) lives in the service -
/// none of it is duplicated here.
/// </summary>
public class SummonScreen : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Appearance/layout constants - the screen is generated from code, so
    // these live here instead of the Inspector. All sizes assume the
    // 1920x1080 reference resolution and scale with the CanvasScaler. The
    // palette mirrors MainMenu's and HeroesScreen's dark fantasy look.
    // ---------------------------------------------------------------------

    /// <summary>Full-screen dark-navy backdrop behind the whole screen.</summary>
    private static readonly Color BackgroundColor = new Color(0.045f, 0.055f, 0.10f);

    /// <summary>Panel behind the top bar.</summary>
    private static readonly Color BarPanelColor = new Color(0.07f, 0.09f, 0.15f, 0.96f);

    /// <summary>Backdrop of the odds panel and the result panel.</summary>
    private static readonly Color CardColor = new Color(0.125f, 0.16f, 0.25f, 0.96f);

    /// <summary>Gold accent: title, summon button, highlighted values.</summary>
    private static readonly Color GoldAccent = new Color(0.85f, 0.72f, 0.35f);

    /// <summary>Darker gold used for the summon button's border.</summary>
    private static readonly Color GoldAccentDark = new Color(0.55f, 0.45f, 0.22f);

    /// <summary>Dark chip behind small badges and buttons.</summary>
    private static readonly Color BadgeColor = new Color(0.04f, 0.05f, 0.09f, 0.92f);

    /// <summary>Inner portrait backdrop (behind the icon or initial).</summary>
    private static readonly Color PortraitBackColor = new Color(0.05f, 0.06f, 0.10f, 1f);

    /// <summary>Text color on gold surfaces.</summary>
    private static readonly Color TextOnGold = new Color(0.09f, 0.11f, 0.17f);

    /// <summary>Dimmed text used for notes, labels, and stats.</summary>
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

    /// <summary>Width and height of the rarity-odds panel.</summary>
    private const float OddsPanelWidth = 620f;

    /// <summary>Height of the rarity-odds panel.</summary>
    private const float OddsPanelHeight = 250f;

    /// <summary>Width and height of the SUMMON x1 button.</summary>
    private const float SummonButtonWidth = 460f;

    /// <summary>Height of the SUMMON x1 button.</summary>
    private const float SummonButtonHeight = 110f;

    /// <summary>Width and height of the result panel.</summary>
    private const float ResultPanelWidth = 900f;

    /// <summary>Height of the result panel.</summary>
    private const float ResultPanelHeight = 430f;

    /// <summary>Size of the result portrait frame.</summary>
    private const float ResultPortraitSize = 150f;

    /// <summary>Width and height of the VIEW HEROES button.</summary>
    private const float ViewHeroesButtonWidth = 240f;

    /// <summary>Height of the VIEW HEROES button (see its width constant).</summary>
    private const float ViewHeroesButtonHeight = 54f;

    /// <summary>The screen draws above the main menu (10) and battle HUD (0).</summary>
    private const int ScreenSortingOrder = 20;

    /// <summary>Top-left anchor point, shared by most child rects.</summary>
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    /// <summary>Top-center anchor point.</summary>
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

    // ----- font sizes -----

    private const int TitleFontSize = 34;
    private const int BackButtonFontSize = 22;
    private const int OddsHeaderFontSize = 24;
    private const int OddsLabelFontSize = 21;
    private const int SummonButtonFontSize = 36;
    private const int ResultTitleFontSize = 34;
    private const int ResultNameFontSize = 30;
    private const int ResultBadgeFontSize = 15;
    private const int ResultStatFontSize = 22;
    private const int ResultNoteFontSize = 16;
    private const int ResultPortraitFontSize = 62;
    private const int ViewHeroesButtonFontSize = 18;

    // ---------------------------------------------------------------------
    // Runtime state: chrome built once in Awake; the odds panel is filled
    // once the runner reference exists.
    // ---------------------------------------------------------------------

    /// <summary>The battle runner that owns this screen and the summon service; assigned by <see cref="Create"/>.</summary>
    private BattleTestRunner runner;

    /// <summary>Unity's built-in legacy font (Arial.ttf no longer ships with Unity 6).</summary>
    private Font font;

    /// <summary>Shared solid rounded-rectangle sprite (9-sliced) for panels, badges, and buttons.</summary>
    private Sprite panelSprite;

    /// <summary>Shared rounded-outline sprite (9-sliced) for frames and borders.</summary>
    private Sprite frameSprite;

    /// <summary>Runtime-generated textures/sprites; destroyed in <see cref="OnDestroy"/> so re-entering Play mode never leaks them.</summary>
    private readonly List<Object> runtimeArt = new List<Object>();

    /// <summary>The result panel; hidden until the first summon.</summary>
    private GameObject resultPanel;

    /// <summary>The rarity-odds panel; its rows are filled in <see cref="BuildOddsPanel"/>.</summary>
    private RectTransform oddsPanelRoot;

    /// <summary>Result title: "NEW HERO!" or "DUPLICATE".</summary>
    private Text resultTitle;

    /// <summary>Result soul line ("+1 Kael Stormblade Soul (12 total)"); shown for duplicates only.</summary>
    private Text resultSoulLine;

    /// <summary>Result fallback note ("rolled Common - pool empty, fell back to Rare"); hidden unless a fallback happened.</summary>
    private Text resultFallbackNote;

    /// <summary>Result portrait frame; recolored with the summoned hero's rarity.</summary>
    private Image resultPortraitFrame;

    /// <summary>Result portrait icon (shown when the template has art).</summary>
    private Image resultPortraitIcon;

    /// <summary>Result portrait initial glyph (shown when no art exists).</summary>
    private Text resultPortraitGlyph;

    /// <summary>Result hero name.</summary>
    private Text resultName;

    /// <summary>Result rarity badge backdrop; recolored with the rarity.</summary>
    private Image resultRarityBack;

    /// <summary>Result rarity badge label.</summary>
    private Text resultRarityText;

    /// <summary>Result role badge label.</summary>
    private Text resultRoleText;

    /// <summary>Result level badge label ("LEVEL 1").</summary>
    private Text resultLevelText;

    /// <summary>Result HP line.</summary>
    private Text resultStatHp;

    /// <summary>Result attack line.</summary>
    private Text resultStatAtk;

    /// <summary>Result defense line.</summary>
    private Text resultStatDef;

    /// <summary>Result speed line.</summary>
    private Text resultStatSpd;

    /// <summary>
    /// Creates the screen for the given runner: a new "SummonScreen"
    /// GameObject whose Canvas hierarchy builds itself in <see cref="Awake"/>.
    /// Called by <see cref="BattleTestRunner.OpenSummonScreen"/> on first
    /// open; afterwards the same instance is re-activated.
    /// </summary>
    public static SummonScreen Create(BattleTestRunner runner)
    {
        GameObject host = new GameObject("SummonScreen");
        SummonScreen screen = host.AddComponent<SummonScreen>();
        screen.runner = runner;
        screen.Initialize();
        return screen;
    }

    /// <summary>
    /// Builds the static screen chrome. Runs during AddComponent, before
    /// <see cref="runner"/> is assigned, so it must not read the runner -
    /// the service-dependent odds panel is filled in <see cref="Initialize"/>.
    /// </summary>
    private void Awake()
    {
        // Unity 6 ships the legacy built-in font as "LegacyRuntime.ttf"
        // (Arial.ttf was removed from the engine in Unity 2022.2+).
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 9-sliced rounded squares give every panel, badge, and button clean
        // corners at any size without any art assets; the outline variant
        // draws frames and borders. This factory intentionally mirrors
        // MainMenu's and HeroesScreen's (kept separate so the other UI
        // files stay untouched).
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
        // does not already provide it (MainMenu and HeroesScreen use the
        // same guard). The input module must match the project's Input
        // System only setup.
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }

    /// <summary>
    /// Builds the service-dependent content once the runner reference has
    /// been assigned by <see cref="Create"/>: the rarity odds table.
    /// </summary>
    private void Initialize()
    {
        BuildOddsPanel();
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

    // ---------------------------------------------------------------------
    // Buttons.
    // ---------------------------------------------------------------------

    /// <summary>
    /// BACK button: closes the summon screen and returns to the main menu
    /// through the runner. Navigation only.
    /// </summary>
    private void GoBack()
    {
        if (runner != null)
        {
            runner.CloseSummonScreen();
        }
    }

    /// <summary>
    /// SUMMON x1: asks the runner's summon service to perform one summon
    /// and renders the result. Free for the prototype - no currency system
    /// exists yet. All gacha logic lives in the service.
    /// </summary>
    private void OnSummonClicked()
    {
        SummonService service = runner != null ? runner.SummonService : null;
        if (service == null)
        {
            return;
        }

        SummonResult result = service.Summon();
        if (result == null)
        {
            // The service already logged why (empty catalog or roster).
            return;
        }

        ShowResult(result);

        // Duplicates banked souls and new heroes joined the roster - persist.
        runner.SaveProfile();
    }

    /// <summary>
    /// VIEW HEROES: opens the Heroes collection (the runner hides this
    /// screen) so the freshly summoned hero can be verified in the roster.
    /// </summary>
    private void OnViewHeroesClicked()
    {
        if (runner != null)
        {
            runner.OpenHeroesCollection();
        }
    }

    /// <summary>
    /// Renders one summon result: every value is read from the newly
    /// created <see cref="HeroInstance"/> and its template - name, rarity,
    /// role, level, and current stats.
    /// </summary>
    private void ShowResult(SummonResult result)
    {
        resultPanel.SetActive(true);

        HeroInstance hero = result.Hero;
        HeroData data = hero != null ? hero.data : null;

        // New hero or duplicate: the title says which immediately; duplicates
        // also show the soul gained and the new total.
        resultTitle.text = result.IsNewHero ? "NEW HERO!" : "DUPLICATE";
        resultSoulLine.gameObject.SetActive(!result.IsNewHero && data != null);
        if (!result.IsNewHero && data != null)
        {
            resultSoulLine.text = "+" + HeroProgression.SoulsPerDuplicate + " " + data.heroName
                + " Soul   (" + hero.Souls + " total)";
        }

        if (data == null)
        {
            return;
        }

        RarityStyle rarity = RarityStyleFor(data.rarity);

        // Portrait: the template's icon when art exists, otherwise the
        // hero's initial on a rarity-tinted frame.
        if (data.icon != null)
        {
            resultPortraitIcon.sprite = data.icon;
            resultPortraitIcon.gameObject.SetActive(true);
            resultPortraitGlyph.gameObject.SetActive(false);
        }
        else
        {
            resultPortraitIcon.gameObject.SetActive(false);
            resultPortraitGlyph.gameObject.SetActive(true);
            resultPortraitGlyph.text = InitialOf(data.heroName);
            resultPortraitGlyph.color = Color.Lerp(rarity.Color, Color.white, 0.35f);
        }

        resultPortraitFrame.color = rarity.Color;

        resultName.text = hero.displayName;
        resultRarityBack.color = Color.Lerp(rarity.Color, Color.black, 0.72f);
        resultRarityText.text = rarity.Label;
        resultRarityText.color = Color.Lerp(rarity.Color, Color.white, 0.25f);
        resultRoleText.text = RoleLabel(data.role);
        resultLevelText.text = "LEVEL " + hero.Level;

        resultStatHp.text = StatLine("HP", hero.currentHealth + " / " + hero.maxHealth);
        resultStatAtk.text = StatLine("ATTACK", hero.currentAttack.ToString());
        resultStatDef.text = StatLine("DEFENSE", hero.currentDefense.ToString());
        resultStatSpd.text = StatLine("SPEED", hero.currentSpeed.ToString());

        // Transparency note when the rolled rarity had no heroes and a
        // fallback pool was used instead.
        resultFallbackNote.gameObject.SetActive(result.FallbackUsed);
        if (result.FallbackUsed)
        {
            resultFallbackNote.text = "Rolled " + result.RolledRarity + " - pool empty, fell back to "
                + result.ActualRarity + ".";
        }
    }

    // ---------------------------------------------------------------------
    // Layout construction.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds the top bar: BACK button on the left and the screen title in
    /// the center.
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
        CreateText(bar, "Title", "SUMMON",
            TitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -38f), new Vector2(400f, 44f)).color = GoldAccent;
    }

    /// <summary>
    /// Builds the content area between the bars: the rarity-odds panel
    /// (filled in <see cref="Initialize"/>), the SUMMON x1 button, and the
    /// result panel (hidden until the first summon).
    /// </summary>
    private void BuildContent(RectTransform root)
    {
        RectTransform content = CreateRect(root, "Content");
        Stretch(content, ScreenMargin);
        content.offsetMax = new Vector2(-ScreenMargin, -TopBarHeight - ScreenMargin);

        // ---- Odds panel skeleton ----
        RectTransform oddsRect = CreateRect(content, "OddsPanel");
        oddsRect.anchorMin = TopCenter;
        oddsRect.anchorMax = TopCenter;
        oddsRect.pivot = new Vector2(0.5f, 1f);
        oddsRect.anchoredPosition = new Vector2(0f, -20f);
        oddsRect.sizeDelta = new Vector2(OddsPanelWidth, OddsPanelHeight);
        oddsPanelRoot = oddsRect;

        CreateSlicedImage(oddsRect, "Back", CardColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, oddsRect.sizeDelta);

        CreateText(oddsRect, "Header", "RARITY CHANCES",
            OddsHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -30f), new Vector2(480f, 34f)).color = GoldAccent;

        // The odds rows are created in BuildOddsPanel (they read the
        // service's table); their parent is ready here.

        // ---- SUMMON x1 button ----
        RectTransform summonRect = CreateRect(content, "SummonButton");
        summonRect.anchorMin = TopCenter;
        summonRect.anchorMax = TopCenter;
        summonRect.pivot = new Vector2(0.5f, 1f);
        summonRect.anchoredPosition = new Vector2(0f, -300f);
        summonRect.sizeDelta = new Vector2(SummonButtonWidth, SummonButtonHeight);

        Image summonBack = CreateSlicedImage(summonRect, "Back", GoldAccent, panelSprite,
            TopLeft, TopLeft, Vector2.zero, summonRect.sizeDelta);
        // Raycastable so the EventSystem can hit the button.
        summonBack.raycastTarget = true;
        CreateSlicedImage(summonRect, "Border", GoldAccentDark, frameSprite,
            TopLeft, TopLeft, new Vector2(-5f, 5f), new Vector2(SummonButtonWidth + 10f, SummonButtonHeight + 10f));

        Text summonLabel = CreateText(summonRect, "Label", "SUMMON x1",
            SummonButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(summonLabel.rectTransform);
        summonLabel.color = TextOnGold;

        Button summonButton = summonRect.gameObject.AddComponent<Button>();
        summonButton.targetGraphic = summonBack;
        summonButton.onClick.AddListener(OnSummonClicked);

        // ---- Result panel (hidden until the first summon) ----
        RectTransform resultRect = CreateRect(content, "ResultPanel");
        resultRect.anchorMin = TopCenter;
        resultRect.anchorMax = TopCenter;
        resultRect.pivot = new Vector2(0.5f, 1f);
        resultRect.anchoredPosition = new Vector2(0f, -450f);
        resultRect.sizeDelta = new Vector2(ResultPanelWidth, ResultPanelHeight);

        CreateSlicedImage(resultRect, "Back", CardColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, resultRect.sizeDelta);
        resultPanel = resultRect.gameObject;

        BuildResultPanel(resultRect);
        resultPanel.SetActive(false);
    }

    /// <summary>
    /// Fills the odds panel with one row per rarity table entry, exactly as
    /// the summon service reports it - the UI holds no rates of its own.
    /// </summary>
    private void BuildOddsPanel()
    {
        SummonService service = runner != null ? runner.SummonService : null;
        if (oddsPanelRoot == null || service == null)
        {
            return;
        }

        IReadOnlyList<SummonService.RarityOdds> table = service.Odds;
        for (int i = 0; i < table.Count; i++)
        {
            float y = -84f - i * 38f;
            CreateText(oddsPanelRoot, "Rarity" + i, table[i].Rarity.ToString(),
                OddsLabelFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(60f, y), new Vector2(340f, 30f));
            CreateText(oddsPanelRoot, "Chance" + i, table[i].Weight + "%",
                OddsLabelFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, y), new Vector2(140f, 30f)).color = GoldAccent;
        }
    }

    /// <summary>Builds the result panel's static elements; values are filled in <see cref="ShowResult"/>.</summary>
    private void BuildResultPanel(RectTransform panel)
    {
        resultTitle = CreateText(panel, "Title", string.Empty,
            ResultTitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -24f), new Vector2(500f, 40f));
        resultTitle.color = GoldAccent;

        resultSoulLine = CreateText(panel, "SoulLine", string.Empty,
            ResultNoteFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -68f), new Vector2(700f, 26f));
        resultSoulLine.color = GoldAccent;
        resultSoulLine.gameObject.SetActive(false);

        resultFallbackNote = CreateText(panel, "FallbackNote", string.Empty,
            ResultNoteFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -96f), new Vector2(780f, 20f));
        resultFallbackNote.color = DimTextColor;
        resultFallbackNote.gameObject.SetActive(false);

        // ---- Portrait (left) ----
        resultPortraitFrame = CreateSlicedImage(panel, "PortraitFrame", Color.white, frameSprite,
            TopLeft, TopLeft, new Vector2(50f, -130f), new Vector2(ResultPortraitSize, ResultPortraitSize));
        CreateSlicedImage(panel, "PortraitBack", PortraitBackColor, panelSprite,
            TopLeft, TopLeft, new Vector2(58f, -138f), new Vector2(ResultPortraitSize - 16f, ResultPortraitSize - 16f));

        resultPortraitIcon = CreateSlicedImage(panel, "PortraitIcon", Color.white, null,
            TopLeft, TopLeft, new Vector2(58f, -138f), new Vector2(ResultPortraitSize - 16f, ResultPortraitSize - 16f));
        resultPortraitIcon.preserveAspect = true;
        resultPortraitIcon.gameObject.SetActive(false);

        resultPortraitGlyph = CreateText(panel, "PortraitGlyph", "?",
            ResultPortraitFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(58f, -138f), new Vector2(ResultPortraitSize - 16f, ResultPortraitSize - 16f));

        // ---- Name, badges, stats (right of the portrait) ----
        resultName = CreateText(panel, "Name", string.Empty,
            ResultNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(230f, -132f), new Vector2(600f, 38f));

        resultRarityBack = CreateSlicedImage(panel, "RarityBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(230f, -186f), new Vector2(130f, 30f));
        resultRarityText = CreateText(panel, "RarityText", string.Empty,
            ResultBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(230f, -186f), new Vector2(130f, 30f));

        CreateSlicedImage(panel, "RoleBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(372f, -186f), new Vector2(130f, 30f));
        resultRoleText = CreateText(panel, "RoleText", string.Empty,
            ResultBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(372f, -186f), new Vector2(130f, 30f));
        resultRoleText.color = DimTextColor;

        CreateSlicedImage(panel, "LevelBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(514f, -186f), new Vector2(120f, 30f));
        resultLevelText = CreateText(panel, "LevelText", string.Empty,
            ResultBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(514f, -186f), new Vector2(120f, 30f));
        resultLevelText.color = GoldAccent;

        resultStatHp = CreateText(panel, "StatHp", string.Empty,
            ResultStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(230f, -236f), new Vector2(600f, 28f));
        resultStatAtk = CreateText(panel, "StatAtk", string.Empty,
            ResultStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(230f, -268f), new Vector2(600f, 28f));
        resultStatDef = CreateText(panel, "StatDef", string.Empty,
            ResultStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(230f, -300f), new Vector2(600f, 28f));
        resultStatSpd = CreateText(panel, "StatSpd", string.Empty,
            ResultStatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(230f, -332f), new Vector2(600f, 28f));

        // ---- VIEW HEROES button (bottom center) ----
        RectTransform viewRect = CreateRect(panel, "ViewHeroesButton");
        viewRect.anchorMin = new Vector2(0.5f, 0f);
        viewRect.anchorMax = new Vector2(0.5f, 0f);
        viewRect.pivot = new Vector2(0.5f, 0f);
        viewRect.anchoredPosition = new Vector2(0f, 18f);
        viewRect.sizeDelta = new Vector2(ViewHeroesButtonWidth, ViewHeroesButtonHeight);

        Image viewBack = CreateSlicedImage(viewRect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, viewRect.sizeDelta);
        // Raycastable so the EventSystem can hit the button.
        viewBack.raycastTarget = true;

        Text viewLabel = CreateText(viewRect, "Label", "VIEW HEROES",
            ViewHeroesButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(viewLabel.rectTransform);
        viewLabel.color = GoldAccent;

        Button viewButton = viewRect.gameObject.AddComponent<Button>();
        viewButton.targetGraphic = viewBack;
        viewButton.onClick.AddListener(OnViewHeroesClicked);
    }

    /// <summary>One stat line with a dimmed label, e.g. "HP  95 / 95".</summary>
    private static string StatLine(string label, string value)
    {
        return StatLabelTag + label + "</color>  " + value;
    }

    // ---------------------------------------------------------------------
    // Rarity presentation. This table mirrors MainMenu's and HeroesScreen's
    // (kept separate so the other UI files stay untouched); the copies must
    // stay in sync until a shared UI theme exists.
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

    /// <summary>The first letter of a hero's name, for placeholder portraits ("?" when unknown).</summary>
    private static string InitialOf(string heroName)
    {
        return string.IsNullOrEmpty(heroName) ? "?" : heroName.Substring(0, 1).ToUpperInvariant();
    }

    // ---------------------------------------------------------------------
    // Basic view factories (anchored rects, images, texts). Mirrors
    // MainMenu's and HeroesScreen's so this file stays self-contained.
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
    // Runtime sprite generation (no art assets required). Mirrors MainMenu's
    // and HeroesScreen's factory so this file stays self-contained.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a shared 48x48 white rounded square as a 9-sliced sprite, so
    /// panels, badges, and buttons get clean rounded corners at any size.
    /// The outline variant draws only a rounded border, used for frames and
    /// the summon button's border.
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
