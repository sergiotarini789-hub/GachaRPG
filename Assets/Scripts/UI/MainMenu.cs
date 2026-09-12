using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated Canvas main menu for <see cref="BattleTestRunner"/> -
/// the game's entry screen, styled to match the battle HUD's dark fantasy
/// mobile look.
///
/// Everything is built from code in <see cref="Awake"/> - no Canvas, panel,
/// or text has to be assembled in the Unity Editor:
/// - a Screen Space - Overlay <see cref="Canvas"/> with a
///   <see cref="CanvasScaler"/> (Scale With Screen Size, 1920x1080 reference
///   resolution), sorted above the battle HUD so it can cover it;
/// - a top bar with the player profile (avatar, name, account level) and
///   placeholder currency chips (no wallet/account system exists yet - the
///   displayed values are stand-in constants);
/// - a large center area for the player's squad: one showcase card per hero
///   template assigned to the runner (portrait frame, name, level, rarity,
///   role, base stats) plus a prominent BATTLE button that starts the
///   existing battle;
/// - a bottom navigation bar: Home / Heroes / Summon / Battle / Shop. Home
///   returns to the squad view; Heroes opens the runtime Heroes collection
///   screen (see HeroesScreen) and Summon opens the runtime Summon screen
///   (see SummonScreen), both through the runner; Shop shows a "Coming
///   Soon" placeholder; Battle starts the battle;
/// - every card is fully data-driven from the assigned <see cref="HeroData"/>
///   assets - no hero names, stats, or rarity are hardcoded here.
///
/// This is a pure view/flow layer: it reads the runner's public Inspector
/// fields, keeps no game state of its own, and contains no gacha, shop,
/// equipment, or progression logic. The BATTLE button only calls
/// <see cref="BattleTestRunner.StartBattleFromMenu"/>, which runs the same
/// battle coroutine as always - the combat system is untouched.
/// </summary>
public class MainMenu : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Appearance/layout constants - the menu is generated from code, so these
    // live here instead of the Inspector. All sizes assume the 1920x1080
    // reference resolution and scale with the CanvasScaler. The palette
    // mirrors BattleHud's dark fantasy look.
    // ---------------------------------------------------------------------

    /// <summary>Full-screen dark-navy backdrop behind the whole menu.</summary>
    private static readonly Color BackgroundColor = new Color(0.045f, 0.055f, 0.10f);

    /// <summary>Panel behind the top bar and the bottom navigation bar.</summary>
    private static readonly Color BarPanelColor = new Color(0.07f, 0.09f, 0.15f, 0.96f);

    /// <summary>Backdrop of one hero showcase card.</summary>
    private static readonly Color CardColor = new Color(0.125f, 0.16f, 0.25f, 0.96f);

    /// <summary>Gold accent: active tab, battle button, headers.</summary>
    private static readonly Color GoldAccent = new Color(0.85f, 0.72f, 0.35f);

    /// <summary>Darker gold used for the battle button's border.</summary>
    private static readonly Color GoldAccentDark = new Color(0.55f, 0.45f, 0.22f);

    /// <summary>Dark chip behind small badges (level, role, account).</summary>
    private static readonly Color BadgeColor = new Color(0.04f, 0.05f, 0.09f, 0.92f);

    /// <summary>Inner portrait backdrop (behind the icon or initial).</summary>
    private static readonly Color PortraitBackColor = new Color(0.05f, 0.06f, 0.10f, 1f);

    /// <summary>Coin-currency accent (amber).</summary>
    private static readonly Color CurrencyGold = new Color(0.95f, 0.74f, 0.32f);

    /// <summary>Gem-currency accent (cyan).</summary>
    private static readonly Color CurrencyGem = new Color(0.35f, 0.75f, 0.95f);

    /// <summary>Inactive navigation tab backdrop.</summary>
    private static readonly Color NavInactiveColor = new Color(0.11f, 0.14f, 0.22f, 0.95f);

    /// <summary>Text color on gold surfaces (active tab, battle button).</summary>
    private static readonly Color TextOnGold = new Color(0.09f, 0.11f, 0.17f);

    /// <summary>Dimmed text used for subtitles and empty-slot hints.</summary>
    private static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.55f);

    /// <summary>Rich-text color tag for stat labels inside a card.</summary>
    private const string StatLabelTag = "<color=#99A3AF>";

    // ----- placeholder account values (no wallet/account system exists yet) -----

    /// <summary>Placeholder player name shown in the profile area until a real account system exists.</summary>
    private const string PlaceholderPlayerName = "Player";

    /// <summary>Placeholder account level shown in the profile area until a real account system exists.</summary>
    private const int PlaceholderAccountLevel = 1;

    /// <summary>Placeholder coin amount shown in the top bar until a real wallet exists.</summary>
    private const string PlaceholderGoldAmount = "48,500";

    /// <summary>Placeholder gem amount shown in the top bar until a real wallet exists.</summary>
    private const string PlaceholderGemAmount = "1,250";

    // ----- layout -----

    /// <summary>Reference width the CanvasScaler scales against.</summary>
    private const float ReferenceWidth = 1920f;

    /// <summary>Reference height the CanvasScaler scales against.</summary>
    private const float ReferenceHeight = 1080f;

    /// <summary>Screen-edge margin shared by the bars and the center content.</summary>
    private const float ScreenMargin = 24f;

    /// <summary>Height of the top profile bar.</summary>
    private const float TopBarHeight = 110f;

    /// <summary>Height of the bottom navigation bar.</summary>
    private const float NavbarHeight = 110f;

    /// <summary>Width and height of one hero showcase card.</summary>
    private const float CardWidth = 380f;

    /// <summary>Height of one hero showcase card.</summary>
    private const float CardHeight = 420f;

    /// <summary>Size of a card portrait frame.</summary>
    private const float CardPortraitSize = 140f;

    /// <summary>Width and height of the prominent BATTLE button.</summary>
    private const float BattleButtonWidth = 460f;

    /// <summary>Height of the prominent BATTLE button.</summary>
    private const float BattleButtonHeight = 130f;

    /// <summary>Width of one bottom navigation tab.</summary>
    private const float NavTabWidth = 356f;

    /// <summary>Height of one bottom navigation tab.</summary>
    private const float NavTabHeight = 86f;

    /// <summary>The menu canvas draws above the battle HUD (default order 0).</summary>
    private const int MenuSortingOrder = 10;

    /// <summary>Top-left anchor point, shared by most child rects.</summary>
    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    /// <summary>Top-center anchor point.</summary>
    private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);

    // ----- font sizes -----

    private const int PlayerNameFontSize = 26;
    private const int BadgeFontSize = 15;
    private const int CurrencyFontSize = 22;
    private const int AvatarFontSize = 36;
    private const int SquadHeaderFontSize = 30;
    private const int CardNameFontSize = 28;
    private const int CardPortraitFontSize = 56;
    private const int CardStatFontSize = 19;
    private const int BattleButtonFontSize = 44;
    private const int NavLabelFontSize = 22;
    private const int PlaceholderTitleFontSize = 30;
    private const int PlaceholderMainFontSize = 54;
    private const int PlaceholderSubFontSize = 22;

    // ---------------------------------------------------------------------
    // Runtime state: views built once in Awake; the squad cards are built in
    // <see cref="Initialize"/> once the runner reference exists.
    // ---------------------------------------------------------------------

    /// <summary>The battle runner this menu starts battles from; assigned by <see cref="Create"/>.</summary>
    private BattleTestRunner runner;

    /// <summary>Unity's built-in legacy font (Arial.ttf no longer ships with Unity 6).</summary>
    private Font font;

    /// <summary>Shared solid rounded-rectangle sprite (9-sliced) for panels, cards, badges, and buttons.</summary>
    private Sprite panelSprite;

    /// <summary>Shared rounded-outline sprite (9-sliced) for frames: rarity borders and the battle button border.</summary>
    private Sprite frameSprite;

    /// <summary>Runtime-generated textures/sprites; destroyed in <see cref="OnDestroy"/> so re-entering Play mode never leaks them.</summary>
    private readonly List<Object> runtimeArt = new List<Object>();

    /// <summary>The center content shown on the Home tab: squad cards + BATTLE button.</summary>
    private GameObject homeView;

    /// <summary>The center content shown for Heroes / Summon / Shop.</summary>
    private GameObject placeholderView;

    /// <summary>Title of the placeholder view ("Heroes", "Summon", "Shop").</summary>
    private Text placeholderTitle;

    /// <summary>Backdrop image of each navigation tab, recolored for the active state.</summary>
    private readonly Image[] tabBackgrounds = new Image[TabCount];

    /// <summary>Label of each navigation tab, recolored for the active state.</summary>
    private readonly Text[] tabLabels = new Text[TabCount];

    /// <summary>Number of bottom navigation tabs.</summary>
    private const int TabCount = 5;

    /// <summary>
    /// Creates the menu for the given runner: a new "MainMenu" GameObject
    /// whose Canvas hierarchy builds itself in <see cref="Awake"/>. Called by
    /// <see cref="BattleTestRunner.Start"/> when
    /// <see cref="BattleTestRunner.startWithMainMenu"/> is set, so the scene
    /// needs no manual UI setup.
    /// </summary>
    public static MainMenu Create(BattleTestRunner runner)
    {
        GameObject host = new GameObject("MainMenu");
        MainMenu menu = host.AddComponent<MainMenu>();
        menu.runner = runner;
        menu.Initialize();
        return menu;
    }

    /// <summary>
    /// Builds the static menu chrome. Runs during AddComponent, before
    /// <see cref="runner"/> is assigned, so it must not read the runner -
    /// the runner-dependent squad cards are built in <see cref="Initialize"/>.
    /// </summary>
    private void Awake()
    {
        // Unity 6 ships the legacy built-in font as "LegacyRuntime.ttf"
        // (Arial.ttf was removed from the engine in Unity 2022.2+).
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 9-sliced rounded squares give every panel, card, badge, and button
        // clean corners at any size without any art assets; the outline
        // variant draws rarity frames and the battle button's border. This
        // factory intentionally mirrors BattleHud's (kept separate so the
        // battle HUD file stays untouched).
        panelSprite = CreateRoundedSprite(outlineOnly: false);
        frameSprite = CreateRoundedSprite(outlineOnly: true);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the battle HUD, so the menu can cover it after a battle.
        canvas.sortingOrder = MenuSortingOrder;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;

        // The GraphicRaycaster is what lets the EventSystem hit this Canvas's
        // graphics at all. Unity adds one automatically to canvases created
        // in the Editor, but NOT to ones built from code - without it, no
        // button in this canvas can ever receive a pointer click.
        gameObject.AddComponent<GraphicRaycaster>();

        RectTransform root = (RectTransform)transform;

        // The backdrop is raycastable so clicks on empty menu areas are
        // consumed here instead of falling through to the battle HUD's own
        // clickable elements behind this (higher-sorting) canvas.
        Image background = CreateStretchedImage(root, "Background", BackgroundColor);
        background.raycastTarget = true;

        BuildTopBar(root);
        BuildCenterViews(root);
        BuildBottomNav(root);

        // Button clicks need an EventSystem; create one only if the scene
        // does not already provide it (BattleHud uses the same guard). The
        // input module must match the project's Active Input Handling: this
        // project uses the Input System package only, so the legacy
        // StandaloneInputModule could never deliver pointer events here.
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }

    /// <summary>
    /// Builds the runner-dependent content (the squad cards) once the runner
    /// reference has been assigned by <see cref="Create"/>.
    /// </summary>
    private void Initialize()
    {
        BuildSquadCards();
        ShowTab(0);
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
    // Tab navigation (only Home is functional; the rest are placeholders).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Shows the Home tab: the squad showcase and the BATTLE button.
    /// </summary>
    private void ShowHome()
    {
        ShowTab(0);
    }

    /// <summary>
    /// Shows the "Coming Soon" placeholder view for a future system
    /// (Heroes, Summon, Shop). No system logic exists behind it.
    /// </summary>
    private void ShowComingSoon(int tabIndex, string title)
    {
        placeholderTitle.text = title;
        ShowTab(tabIndex);
    }

    /// <summary>
    /// Starts the existing battle through the runner. The menu hides itself
    /// (the runner does it) and the same battle coroutine as always runs.
    /// </summary>
    private void StartBattle()
    {
        if (runner != null)
        {
            runner.StartBattleFromMenu();
        }
    }

    /// <summary>
    /// Opens the Heroes collection through the runner: the runner hides
    /// this menu and shows its HeroesScreen; the screen's BACK button
    /// returns here. Navigation only.
    /// </summary>
    private void OpenHeroes()
    {
        if (runner != null)
        {
            runner.OpenHeroesCollection();
        }
    }

    /// <summary>
    /// Opens the Summon screen through the runner: the runner hides this
    /// menu and shows its SummonScreen; the screen's BACK button returns
    /// here. Navigation only.
    /// </summary>
    private void OpenSummon()
    {
        if (runner != null)
        {
            runner.OpenSummonScreen();
        }
    }

    /// <summary>Switches the center view and the navigation highlight to the given tab.</summary>
    private void ShowTab(int tab)
    {
        homeView.SetActive(tab == 0);
        placeholderView.SetActive(tab != 0);

        for (int i = 0; i < TabCount; i++)
        {
            bool isActive = i == tab;
            tabBackgrounds[i].color = isActive ? GoldAccent : NavInactiveColor;
            tabLabels[i].color = isActive ? TextOnGold : Color.white;
        }
    }

    // ---------------------------------------------------------------------
    // Layout construction.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds the top bar: player profile on the left (avatar, name, account
    /// level) and placeholder currency chips on the right.
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

        // ---- Player profile (left) ----
        CreateSlicedImage(bar, "AvatarFrame", GoldAccent, frameSprite,
            TopLeft, TopLeft, new Vector2(16f, 13f), new Vector2(84f, 84f));
        CreateSlicedImage(bar, "AvatarBack", PortraitBackColor, panelSprite,
            TopLeft, TopLeft, new Vector2(22f, 19f), new Vector2(72f, 72f));
        CreateText(bar, "AvatarGlyph", PlaceholderPlayerName.Substring(0, 1),
            AvatarFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(22f, 19f), new Vector2(72f, 72f)).color = GoldAccent;

        CreateText(bar, "PlayerName", PlaceholderPlayerName,
            PlayerNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(116f, -16f), new Vector2(300f, 36f));

        CreateSlicedImage(bar, "AccountBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(116f, -58f), new Vector2(170f, 30f));
        Text account = CreateText(bar, "AccountLevel", "ACCOUNT LV " + PlaceholderAccountLevel,
            BadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(116f, -58f), new Vector2(170f, 30f));
        account.color = GoldAccent;

        // ---- Placeholder currencies (right) ----
        BuildCurrencyChip(bar, "GoldChip", CurrencyGold, PlaceholderGoldAmount, -252f, rotated: false);
        BuildCurrencyChip(bar, "GemChip", CurrencyGem, PlaceholderGemAmount, -16f, rotated: true);
    }

    /// <summary>
    /// Builds one currency chip: a rounded panel with a colored coin/gem
    /// glyph and a placeholder amount. Pure decoration until a wallet exists.
    /// </summary>
    private void BuildCurrencyChip(RectTransform bar, string name, Color glyphColor, string amount, float rightOffset, bool rotated)
    {
        RectTransform chip = CreateRect(bar, name);
        chip.anchorMin = new Vector2(1f, 0.5f);
        chip.anchorMax = new Vector2(1f, 0.5f);
        chip.pivot = new Vector2(1f, 0.5f);
        chip.anchoredPosition = new Vector2(rightOffset, 0f);
        chip.sizeDelta = new Vector2(224f, 64f);

        CreateSlicedImage(chip, "Back", BadgeColor, panelSprite, TopLeft, TopLeft, Vector2.zero, chip.sizeDelta);

        // Coin/gem glyph: a small rounded square; gems are rotated 45 degrees
        // so the two currencies read differently without any art asset.
        RectTransform glyph = CreateRect(chip, "Glyph");
        glyph.anchorMin = new Vector2(0f, 0.5f);
        glyph.anchorMax = new Vector2(0f, 0.5f);
        glyph.pivot = new Vector2(0.5f, 0.5f);
        glyph.anchoredPosition = new Vector2(40f, 0f);
        glyph.sizeDelta = new Vector2(34f, 34f);
        if (rotated)
        {
            glyph.localEulerAngles = new Vector3(0f, 0f, 45f);
        }

        Image glyphImage = glyph.gameObject.AddComponent<Image>();
        glyphImage.color = glyphColor;
        glyphImage.raycastTarget = false;

        CreateText(chip, "Amount", amount, CurrencyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(72f, -14f), new Vector2(140f, 28f));
    }

    /// <summary>
    /// Builds the two center views: the Home squad view and the Coming Soon
    /// placeholder view (only one is visible at a time).
    /// </summary>
    private void BuildCenterViews(RectTransform root)
    {
        // ---- Home view: squad header, three cards, BATTLE button ----
        // Stretched full-screen so the children anchor against the screen.
        RectTransform homeRect = CreateRect(root, "HomeView");
        Stretch(homeRect);
        homeView = homeRect.gameObject;

        Text header = CreateText(homeView.transform, "SquadHeader", "YOUR SQUAD",
            SquadHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -176f), new Vector2(600f, 40f));
        header.color = GoldAccent;

        // The squad cards and the battle button are built in Initialize
        // (they read the runner's hero templates); their parent is ready here.

        // ---- Placeholder view: "Coming Soon" ----
        RectTransform placeholderRect = CreateRect(root, "PlaceholderView");
        Stretch(placeholderRect);
        placeholderView = placeholderRect.gameObject;

        RectTransform panel = CreateRect(placeholderView.transform, "Panel");
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(900f, 460f);

        CreateSlicedImage(panel, "Back", CardColor, panelSprite, TopLeft, TopLeft, Vector2.zero, panel.sizeDelta);

        placeholderTitle = CreateText(panel, "Title", string.Empty,
            PlaceholderTitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -64f), new Vector2(600f, 40f));
        placeholderTitle.color = GoldAccent;

        CreateText(panel, "Main", "Coming Soon",
            PlaceholderMainFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -170f), new Vector2(700f, 70f));

        CreateText(panel, "Sub", "This feature arrives in a future update.",
            PlaceholderSubFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -250f), new Vector2(700f, 30f)).color = DimTextColor;

        placeholderView.SetActive(false);
    }

    /// <summary>
    /// Builds the prominent BATTLE button under the squad cards. It starts
    /// the existing battle via <see cref="BattleTestRunner.StartBattleFromMenu"/>.
    /// </summary>
    private void BuildBattleButton()
    {
        RectTransform rect = CreateRect(homeView.transform, "BattleButton");
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 706f);
        rect.sizeDelta = new Vector2(BattleButtonWidth, BattleButtonHeight);

        Image back = CreateSlicedImage(rect, "Back", GoldAccent, panelSprite,
            TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);
        // Raycastable so the EventSystem can hit the button (the image
        // factory disables raycast targets for decoration).
        back.raycastTarget = true;
        CreateSlicedImage(rect, "Border", GoldAccentDark, frameSprite,
            TopLeft, TopLeft, new Vector2(-4f, -4f), new Vector2(BattleButtonWidth + 8f, BattleButtonHeight + 8f));

        Text label = CreateText(rect, "Label", "BATTLE",
            BattleButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(label.rectTransform);
        label.color = TextOnGold;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(StartBattle);
    }

    /// <summary>
    /// Builds one hero showcase card from a template: portrait frame
    /// (rarity-colored, showing <see cref="HeroData.icon"/> when art exists),
    /// name, level badge, rarity and role badges, and the base stats. When
    /// the template is null the card renders as an empty slot hint instead.
    /// </summary>
    private void BuildHeroCard(Transform parent, HeroData template, int index)
    {
        RectTransform card = CreateRect(parent, "HeroCard" + (index + 1));
        card.anchorMin = TopCenter;
        card.anchorMax = TopCenter;
        card.pivot = new Vector2(0.5f, 1f);
        card.anchoredPosition = new Vector2((index - 1) * (CardWidth + 40f), -236f);
        card.sizeDelta = new Vector2(CardWidth, CardHeight);

        RarityStyle rarity = RarityStyleFor(template != null ? template.rarity : HeroRarity.Common);

        CreateSlicedImage(card, "Back", CardColor, panelSprite, TopLeft, TopLeft, Vector2.zero, card.sizeDelta);
        CreateSlicedImage(card, "RarityFrame", rarity.Color, frameSprite,
            TopLeft, TopLeft, Vector2.zero, card.sizeDelta);

        if (template == null)
        {
            // Empty slot: hint where to assign a hero template.
            CanvasGroup group = card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.55f;
            CreateText(card, "EmptyTitle", "EMPTY SLOT", CardNameFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopCenter, TopCenter, new Vector2(0f, -190f), new Vector2(CardWidth - 40f, 36f)).color = DimTextColor;
            CreateText(card, "EmptyHint", "Assign a hero in the BattleTestRunner Inspector",
                BadgeFontSize, FontStyle.Normal, TextAnchor.MiddleCenter,
                TopCenter, TopCenter, new Vector2(0f, -236f), new Vector2(CardWidth - 40f, 30f)).color = DimTextColor;
            return;
        }

        // Level badge (the fixture fields heroes at level 1).
        CreateSlicedImage(card, "LevelBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(12f, -12f), new Vector2(74f, 28f));
        CreateText(card, "LevelText", "Lv 1", BadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(12f, -12f), new Vector2(74f, 28f)).color = GoldAccent;

        // Portrait: the template's icon when art exists, otherwise the hero's
        // initial on a rarity-tinted frame - swapping in real art later needs
        // no code change, just an assigned Sprite on the HeroData asset.
        float portraitX = (CardWidth - CardPortraitSize) * 0.5f;
        CreateSlicedImage(card, "PortraitFrame", rarity.Color, frameSprite,
            TopLeft, TopLeft, new Vector2(portraitX, -28f), new Vector2(CardPortraitSize, CardPortraitSize));
        CreateSlicedImage(card, "PortraitBack", PortraitBackColor, panelSprite,
            TopLeft, TopLeft, new Vector2(portraitX + 7f, -35f), new Vector2(CardPortraitSize - 14f, CardPortraitSize - 14f));

        if (template.icon != null)
        {
            Image icon = CreateSlicedImage(card, "PortraitIcon", Color.white, null,
                TopLeft, TopLeft, new Vector2(portraitX + 7f, -35f), new Vector2(CardPortraitSize - 14f, CardPortraitSize - 14f));
            icon.sprite = template.icon;
            icon.preserveAspect = true;
        }
        else
        {
            CreateText(card, "PortraitGlyph", InitialOf(template.heroName),
                CardPortraitFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(portraitX + 7f, -35f), new Vector2(CardPortraitSize - 14f, CardPortraitSize - 14f))
                .color = Color.Lerp(rarity.Color, Color.white, 0.35f);
        }

        // Name + badges.
        CreateText(card, "Name", template.heroName, CardNameFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -186f), new Vector2(CardWidth - 32f, 38f));

        CreateSlicedImage(card, "RarityBadge", Color.Lerp(rarity.Color, Color.black, 0.72f), panelSprite,
            TopLeft, TopLeft, new Vector2(70f, -236f), new Vector2(112f, 30f));
        CreateText(card, "RarityText", rarity.Label, BadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(70f, -236f), new Vector2(112f, 30f))
            .color = Color.Lerp(rarity.Color, Color.white, 0.25f);

        CreateSlicedImage(card, "RoleBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(198f, -236f), new Vector2(112f, 30f));
        CreateText(card, "RoleText", RoleLabel(template.role), BadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(198f, -236f), new Vector2(112f, 30f)).color = DimTextColor;

        // Base stats (what the level-1 fixture fields). Rich text dims the labels.
        CreateText(card, "StatHp", StatLine("HP", template.baseHealth),
            CardStatFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -290f), new Vector2(CardWidth - 60f, 26f));
        CreateText(card, "StatAtk", StatLine("ATK", template.baseAttack),
            CardStatFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -322f), new Vector2(CardWidth - 60f, 26f));
        CreateText(card, "StatDef", StatLine("DEF", template.baseDefense),
            CardStatFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -354f), new Vector2(CardWidth - 60f, 26f));
        CreateText(card, "StatSpd", StatLine("SPD", template.baseSpeed),
            CardStatFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopCenter, TopCenter, new Vector2(0f, -386f), new Vector2(CardWidth - 60f, 26f));
    }

    /// <summary>One stat line with a dimmed label, e.g. "HP  165".</summary>
    private static string StatLine(string label, int value)
    {
        return StatLabelTag + label + "</color>  " + value;
    }

    /// <summary>
    /// Builds the three squad cards from the runner's hero templates (the
    /// same assets the battle uses) and the BATTLE button under them.
    /// </summary>
    private void BuildSquadCards()
    {
        HeroData[] squad =
        {
            runner != null ? runner.defenseHeroData : null,
            runner != null ? runner.attackHeroData : null,
            runner != null ? runner.supportHeroData : null,
        };

        for (int i = 0; i < squad.Length; i++)
        {
            BuildHeroCard(homeView.transform, squad[i], i);
        }

        BuildBattleButton();
    }

    /// <summary>
    /// Builds the bottom navigation bar: Home / Heroes / Summon / Battle /
    /// Shop. Home returns to the squad view; Heroes opens the Heroes
    /// collection screen; Summon opens the Summon screen; Shop shows the
    /// Coming Soon placeholder; Battle starts the battle. Tabs are
    /// highlighted when active.
    /// </summary>
    private void BuildBottomNav(RectTransform root)
    {
        RectTransform bar = CreateRect(root, "BottomNav");
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.offsetMin = new Vector2(ScreenMargin, ScreenMargin);
        bar.offsetMax = new Vector2(-ScreenMargin, NavbarHeight + ScreenMargin);

        CreateStretchedSlicedImage(bar, "Back", BarPanelColor, panelSprite);

        string[] labels = { "HOME", "HEROES", "SUMMON", "BATTLE", "SHOP" };
        for (int i = 0; i < TabCount; i++)
        {
            BuildNavTab(bar, i, labels[i]);
        }
    }

    /// <summary>Builds one navigation tab button and wires its behavior.</summary>
    private void BuildNavTab(RectTransform bar, int index, string label)
    {
        RectTransform rect = CreateRect(bar, "Tab" + label);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(22f + index * (NavTabWidth + 12f), 0f);
        rect.sizeDelta = new Vector2(NavTabWidth, NavTabHeight);

        Image back = CreateSlicedImage(rect, "Back", NavInactiveColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);
        // Raycastable so the EventSystem can hit the tab (the image factory
        // disables raycast targets for decoration).
        back.raycastTarget = true;

        Text text = CreateText(rect, "Label", label, NavLabelFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(text.rectTransform);

        tabBackgrounds[index] = back;
        tabLabels[index] = text;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        switch (index)
        {
            case 0:
                button.onClick.AddListener(ShowHome);
                break;
            case 1:
                button.onClick.AddListener(OpenHeroes);
                break;
            case 2:
                button.onClick.AddListener(OpenSummon);
                break;
            case 3:
                button.onClick.AddListener(StartBattle);
                break;
            case 4:
                button.onClick.AddListener(() => ShowComingSoon(4, "Shop"));
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Rarity presentation. This table mirrors BattleHud's (kept separate so
    // the battle HUD file stays untouched); the two must stay in sync until
    // a shared UI theme exists.
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
    /// breaks the menu.
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
    // Runtime sprite generation (no art assets required). Mirrors BattleHud's
    // factory so this file stays self-contained.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a shared 48x48 white rounded square as a 9-sliced sprite, so
    /// panels, cards, badges, and buttons get clean rounded corners at any
    /// size. The outline variant draws only a rounded border, used for rarity
    /// frames and the battle button's border.
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
