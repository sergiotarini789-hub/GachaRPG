using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-generated Canvas screen for the player's equipment inventory -
/// the player-facing equipment management UI, opened from the Heroes
/// screen's EQUIPMENT button (see
/// <see cref="BattleTestRunner.OpenEquipmentScreen"/>). The BACK button
/// returns to the Heroes screen.
///
/// Everything is built from code in <see cref="Awake"/> - no Canvas, panel,
/// or text has to be assembled in the Unity Editor:
/// - a Screen Space - Overlay <see cref="Canvas"/> (sorting above the
///   Heroes screen so it covers it) with a <see cref="CanvasScaler"/>
///   (1920x1080 reference resolution);
/// - a top bar with a BACK button, the screen title, and the live gold
///   balance;
/// - filter rows: ALL + one button per equipment slot, and ALL RARITIES +
///   one button per rarity;
/// - a paginated list of owned equipment cards (icon placeholder with a
///   rarity-colored frame, name, rarity, slot, level, primary stat, and an
///   equipped indicator showing the wearing hero);
/// - a details panel for the selected item: identity, PRIMARY STAT,
///   SECONDARY STATS, EQUIPPED BY, description, an EQUIP TO hero picker,
///   and the EQUIP/UNEQUIP, UPGRADE, and SELL actions with proper
///   enable/disable states (EQUIP becomes UNEQUIP while equipped; UPGRADE
///   shows MAX LEVEL at the cap; SELL refuses equipped or locked items).
///
/// This is a pure view layer: every value is read from the real
/// <see cref="EquipmentInventory"/> / <see cref="EquipmentInstance"/> /
/// wallet, every action goes through the real gameplay services
/// (inventory.Equip/Unequip/Sell, instance.Upgrade), and the profile is
/// persisted through the runner after each change. No equipment math lives
/// here - stat text comes straight off the instances.
/// </summary>
public class EquipmentScreen : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Appearance/layout constants (generated UI; 1920x1080 reference).
    // The palette mirrors HeroesScreen's dark fantasy look.
    // ---------------------------------------------------------------------

    /// <summary>Full-screen dark-navy backdrop behind the whole screen.</summary>
    private static readonly Color BackgroundColor = new Color(0.045f, 0.055f, 0.10f);

    /// <summary>Panel behind the top bar.</summary>
    private static readonly Color BarPanelColor = new Color(0.07f, 0.09f, 0.15f, 0.96f);

    /// <summary>Backdrop of item cards and the details panel.</summary>
    private static readonly Color CardColor = new Color(0.125f, 0.16f, 0.25f, 0.96f);

    /// <summary>Gold accent: title, active filters, highlighted values.</summary>
    private static readonly Color GoldAccent = new Color(0.85f, 0.72f, 0.35f);

    /// <summary>Dark chip behind small badges and filter buttons.</summary>
    private static readonly Color BadgeColor = new Color(0.04f, 0.05f, 0.09f, 0.92f);

    /// <summary>Dimmed text for subtitles, meta lines, and inactive filters.</summary>
    private static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.55f);

    /// <summary>Backing for an available (interactable) action button.</summary>
    private static readonly Color ActionReadyColor = new Color(0.10f, 0.13f, 0.20f, 0.98f);

    /// <summary>Backing for an unavailable (dimmed) action button.</summary>
    private static readonly Color ActionDimColor = new Color(0.06f, 0.075f, 0.12f, 0.85f);

    /// <summary>Reference width the CanvasScaler scales against.</summary>
    private const float ReferenceWidth = 1920f;

    /// <summary>Reference height the CanvasScaler scales against.</summary>
    private const float ReferenceHeight = 1080f;

    /// <summary>Screen-edge margin shared by the bars and the content area.</summary>
    private const float ScreenMargin = 24f;

    /// <summary>Height of the top bar.</summary>
    private const float TopBarHeight = 110f;

    /// <summary>Sorts above the Heroes screen (20) so this screen covers it.</summary>
    private const int ScreenSortingOrder = 25;

    /// <summary>Width of the left item list column.</summary>
    private const float ListWidth = 720f;

    /// <summary>How many item cards fit on one page.</summary>
    private const int ItemsPerPage = 7;

    /// <summary>Height of one item card.</summary>
    private const float CardHeight = 88f;

    /// <summary>Vertical gap between item cards.</summary>
    private const float CardGap = 8f;

    /// <summary>X position of the right details panel.</summary>
    private const float DetailsX = 780f;

    /// <summary>Width of the right details panel.</summary>
    private const float DetailsWidth = 1116f;

    // ----- font sizes -----
    private const int TitleFontSize = 34;
    private const int BackButtonFontSize = 22;
    private const int ChipFontSize = 18;
    private const int FilterFontSize = 14;
    private const int CardNameFontSize = 20;
    private const int CardMetaFontSize = 14;
    private const int CardPortraitFontSize = 26;
    private const int DetailNameFontSize = 30;
    private const int DetailBadgeFontSize = 15;
    private const int SectionHeaderFontSize = 20;
    private const int StatFontSize = 22;
    private const int SecondaryFontSize = 17;
    private const int ActionButtonFontSize = 18;
    private const int StatusFontSize = 15;
    private const int EmptyStateFontSize = 24;
    private const int DetailPortraitFontSize = 44;

    private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

    // ---------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------

    /// <summary>The battle runner that owns the roster, wallet, and equipment inventory.</summary>
    private BattleTestRunner runner;

    /// <summary>Unity's built-in legacy font (Arial.ttf no longer ships with Unity 6).</summary>
    private Font font;

    /// <summary>Shared solid rounded-rectangle sprite (9-sliced) for panels, cards, badges, and buttons.</summary>
    private Sprite panelSprite;

    /// <summary>Shared rounded-outline sprite (9-sliced) for rarity frames and the selection highlight.</summary>
    private Sprite frameSprite;

    /// <summary>Runtime-generated textures/sprites; destroyed in <see cref="OnDestroy"/> so re-entering Play mode never leaks them.</summary>
    private readonly List<Object> runtimeArt = new List<Object>();

    /// <summary>Root of the item card column; rebuilt on every refresh.</summary>
    private RectTransform listRoot;

    /// <summary>The card GameObjects of the current page, parallel to <see cref="cardItems"/>.</summary>
    private readonly List<GameObject> cardRoots = new List<GameObject>();

    /// <summary>The gold selection frame of each card; exactly one is visible at a time.</summary>
    private readonly List<Image> cardSelectionFrames = new List<Image>();

    /// <summary>The item each card shows, in the same order as the cards.</summary>
    private readonly List<EquipmentInstance> cardItems = new List<EquipmentInstance>();

    /// <summary>Slot filter: -1 = all slots, otherwise the <see cref="EquipmentSlot"/> value.</summary>
    private int slotFilter = -1;

    /// <summary>Rarity filter: -1 = all rarities, otherwise the <see cref="HeroRarity"/> value.</summary>
    private int rarityFilter = -1;

    /// <summary>The current list page (0-based).</summary>
    private int page;

    /// <summary>The currently inspected item, or null.</summary>
    private EquipmentInstance selectedItem;

    /// <summary>The hero new equips target (cycled with the picker buttons).</summary>
    private HeroInstance targetHero;

    /// <summary>Empty-state line shown when the filtered list has no items.</summary>
    private Text emptyText;

    /// <summary>Live gold balance chip in the top bar.</summary>
    private Text goldText;

    /// <summary>Item count + page indicator under the list.</summary>
    private Text pageText;

    /// <summary>One-line feedback for the latest action.</summary>
    private Text statusText;

    /// <summary>Slot filter buttons' backdrops (for active-state coloring), index 0 = ALL.</summary>
    private readonly List<Image> slotFilterBacks = new List<Image>();

    /// <summary>Slot filter buttons' labels, index 0 = ALL.</summary>
    private readonly List<Text> slotFilterLabels = new List<Text>();

    /// <summary>Rarity filter buttons' backdrops, index 0 = ALL RARITIES.</summary>
    private readonly List<Image> rarityFilterBacks = new List<Image>();

    /// <summary>Rarity filter buttons' labels, index 0 = ALL RARITIES.</summary>
    private readonly List<Text> rarityFilterLabels = new List<Text>();

    // ----- details panel fields (assigned in BuildDetailsPanel) -----

    /// <summary>Details icon frame; recolored with the item's rarity.</summary>
    private Image detailIconFrame;

    /// <summary>Details icon image (shown when the definition has art).</summary>
    private Image detailIconImage;

    /// <summary>Details icon initial glyph (shown when no art exists).</summary>
    private Text detailIconGlyph;

    /// <summary>Details item name.</summary>
    private Text detailName;

    /// <summary>Details rarity badge backdrop; recolored with the item's rarity.</summary>
    private Image detailRarityBack;

    /// <summary>Details rarity badge label.</summary>
    private Text detailRarityText;

    /// <summary>Details slot badge label.</summary>
    private Text detailSlotText;

    /// <summary>Details level badge label.</summary>
    private Text detailLevelText;

    /// <summary>Details primary stat line, e.g. "ATK +20".</summary>
    private Text detailPrimaryText;

    /// <summary>Details secondary stat lines (one per rolled secondary, up to four).</summary>
    private readonly Text[] detailSecondaryTexts = new Text[4];

    /// <summary>Details "no secondary stats" line.</summary>
    private Text detailNoSecondaryText;

    /// <summary>Details equipped-by line (the wearing hero's name, or "Not equipped").</summary>
    private Text detailEquippedText;

    /// <summary>Details target-hero label ("EQUIP TO: Kael Stormblade").</summary>
    private Text targetHeroText;

    /// <summary>Details description/flavor line.</summary>
    private Text detailDescription;

    /// <summary>EQUIP / UNEQUIP toggle button.</summary>
    private Button equipButton;

    /// <summary>EQUIP / UNEQUIP button label.</summary>
    private Text equipLabel;

    /// <summary>UPGRADE button.</summary>
    private Button upgradeButton;

    /// <summary>UPGRADE button label.</summary>
    private Text upgradeLabel;

    /// <summary>SELL button.</summary>
    private Button sellButton;

    /// <summary>SELL button label.</summary>
    private Text sellLabel;

    /// <summary>The details elements that only make sense with a selected item.</summary>
    private readonly List<Graphic> detailElements = new List<Graphic>();

    /// <summary>Details empty-state line, shown instead when nothing is selected.</summary>
    private Text detailEmptyText;

    /// <summary>
    /// Creates the screen for the given runner: a new "EquipmentScreen"
    /// GameObject whose Canvas hierarchy builds itself in <see cref="Awake"/>.
    /// Called by <see cref="BattleTestRunner.OpenEquipmentScreen"/> on first
    /// open; afterwards the same instance is re-shown via <see cref="Show"/>.
    /// </summary>
    public static EquipmentScreen Create(BattleTestRunner runner)
    {
        GameObject host = new GameObject("EquipmentScreen");
        EquipmentScreen screen = host.AddComponent<EquipmentScreen>();
        screen.runner = runner;
        screen.Initialize();
        return screen;
    }

    /// <summary>
    /// Shows the screen (re-activating the Canvas) with the given initial
    /// equip target; invalid or missing targets fall back to the first
    /// roster hero. Refreshes every value from the live inventory.
    /// </summary>
    public void Show(HeroInstance initialTarget)
    {
        gameObject.SetActive(true);

        HeroRoster roster = runner != null ? runner.Roster : null;
        if (initialTarget == null || roster == null || IndexInRoster(roster, initialTarget) < 0)
        {
            initialTarget = roster != null && roster.Heroes.Count > 0 ? roster.Heroes[0] : null;
        }

        targetHero = initialTarget;
        selectedItem = FindByIdSafe(selectedItem);
        page = 0;
        RebuildList();
        RefreshDetails();
        RefreshTopBar();
    }

    /// <summary>Hides the screen (the Heroes screen re-appears beneath it).</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Builds the roster-independent content once the runner reference has
    /// been assigned by <see cref="Create"/>: the item list and the initial
    /// selection.
    /// </summary>
    private void Initialize()
    {
        HeroRoster roster = runner != null ? runner.Roster : null;
        targetHero = roster != null && roster.Heroes.Count > 0 ? roster.Heroes[0] : null;
        RebuildList();
        RefreshDetails();
        RefreshTopBar();
    }

    /// <summary>
    /// Builds the static screen chrome. Runs during AddComponent, before
    /// <see cref="runner"/> is assigned, so it must not read the runner -
    /// the inventory-dependent list is built in <see cref="Initialize"/>.
    /// </summary>
    private void Awake()
    {
        // Unity 6 ships the legacy built-in font as "LegacyRuntime.ttf"
        // (Arial.ttf was removed from the engine in Unity 2022.2+).
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Mirrors the other screens' sprite factories (kept separate so the
        // other UI files stay untouched).
        panelSprite = CreateRoundedSprite(outlineOnly: false);
        frameSprite = CreateRoundedSprite(outlineOnly: true);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the Heroes screen, so this screen covers it while open.
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
        // consumed here instead of falling through to the Heroes screen
        // behind this (higher-sorting) canvas.
        Image background = CreateStretchedImage(root, "Background", BackgroundColor);
        background.raycastTarget = true;

        BuildTopBar(root);
        BuildFilters(root);
        BuildListPanel(root);
        BuildDetailsPanel(root);

        // Button clicks need an EventSystem; create one only if the scene
        // does not already provide it (the other screens use the same
        // guard). The input module must match the project's Input System
        // only setup.
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
    }

    // ---------------------------------------------------------------------
    // Layout construction.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds the top bar: BACK button on the left, the screen title in the
    /// center, and the live gold balance on the right.
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
        CreateText(bar, "Title", "EQUIPMENT",
            TitleFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(400f, 44f)).color = GoldAccent;

        // ---- Gold balance (right); filled in RefreshTopBar ----
        RectTransform goldRect = CreateRect(bar, "GoldChip");
        goldRect.anchorMin = new Vector2(1f, 0.5f);
        goldRect.anchorMax = new Vector2(1f, 0.5f);
        goldRect.pivot = new Vector2(1f, 0.5f);
        goldRect.anchoredPosition = new Vector2(-16f, 0f);
        goldRect.sizeDelta = new Vector2(240f, 40f);

        CreateSlicedImage(goldRect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, goldRect.sizeDelta);
        goldText = CreateText(goldRect, "Label", string.Empty,
            ChipFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, Vector2.zero, goldRect.sizeDelta);
        goldText.color = GoldAccent;
    }

    /// <summary>
    /// Builds the two filter rows under the top bar: ALL + one button per
    /// slot, then ALL RARITIES + one button per rarity. Buttons toggle the
    /// filters and rebuild the list.
    /// </summary>
    private void BuildFilters(RectTransform root)
    {
        // ---- Slot filters (row 1) ----
        string[] slotLabels = { "ALL", "WEAPON", "HELMET", "ARMOR", "GLOVES", "BOOTS", "ACCESSORY" };
        float x = ScreenMargin;
        for (int i = 0; i < slotLabels.Length; i++)
        {
            int captured = i - 1; // -1 = all slots
            BuildFilterButton(root, "SlotFilter" + slotLabels[i], slotLabels[i], new Vector2(x, -142f), 100f,
                () => OnSlotFilterClicked(captured), out Image back, out Text label);
            slotFilterBacks.Add(back);
            slotFilterLabels.Add(label);
            x += 100f + 4f;
        }

        // ---- Rarity filters (row 2) ----
        string[] rarityLabels = { "ALL RARITIES", "COMMON", "RARE", "EPIC", "LEGENDARY" };
        float[] widths = { 160f, 100f, 90f, 90f, 130f };
        x = ScreenMargin;
        for (int i = 0; i < rarityLabels.Length; i++)
        {
            int captured = i - 1; // -1 = all rarities
            float width = widths[i];
            BuildFilterButton(root, "RarityFilter" + rarityLabels[i], rarityLabels[i], new Vector2(x, -184f), width,
                () => OnRarityFilterClicked(captured), out Image back, out Text label);
            rarityFilterBacks.Add(back);
            rarityFilterLabels.Add(label);
            x += width + 4f;
        }
    }

    /// <summary>Builds one small filter chip-button with an active/inactive state.</summary>
    private void BuildFilterButton(Transform parent, string name, string label, Vector2 position, float width, UnityEngine.Events.UnityAction onClick, out Image back, out Text labelText)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = TopLeft;
        rect.anchorMax = TopLeft;
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 34f);

        back = CreateSlicedImage(rect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);
        back.raycastTarget = true;

        labelText = CreateText(rect, "Label", label,
            FilterFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(labelText.rectTransform);
        labelText.color = DimTextColor;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(onClick);
    }

    /// <summary>
    /// Builds the left column: the item card container (rebuilt per refresh
    /// in <see cref="RebuildList"/>), the empty state, and the pagination row.
    /// </summary>
    private void BuildListPanel(RectTransform root)
    {
        RectTransform list = CreateRect(root, "List");
        list.anchorMin = TopLeft;
        list.anchorMax = TopLeft;
        list.pivot = new Vector2(0f, 1f);
        list.anchoredPosition = new Vector2(ScreenMargin, -230f);
        list.sizeDelta = new Vector2(ListWidth, 0f);
        listRoot = list;

        // Empty state (shown only when the filtered list has no items).
        emptyText = CreateText(list, "Empty", "No equipment yet",
            EmptyStateFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -260f), new Vector2(ListWidth, 40f));
        emptyText.color = DimTextColor;
        emptyText.gameObject.SetActive(false);

        // ---- Pagination (bottom of the column) ----
        RectTransform pager = CreateRect(root, "Pager");
        pager.anchorMin = TopLeft;
        pager.anchorMax = TopLeft;
        pager.pivot = new Vector2(0f, 1f);
        pager.anchoredPosition = new Vector2(ScreenMargin, -936f);
        pager.sizeDelta = new Vector2(ListWidth, 44f);

        BuildPagerButton(pager, "Prev", "PREV", new Vector2(0f, 0f), 120f, OnPrevPage);
        BuildPagerButton(pager, "Next", "NEXT", new Vector2(ListWidth - 120f, 0f), 120f, OnNextPage);

        pageText = CreateText(pager, "Label", string.Empty,
            ChipFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 40f));
        pageText.color = DimTextColor;
    }

    /// <summary>Builds one PREV/NEXT pagination button.</summary>
    private void BuildPagerButton(Transform parent, string name, string label, Vector2 position, float width, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = TopLeft;
        rect.anchorMax = TopLeft;
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 44f);

        Image back = CreateSlicedImage(rect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);
        back.raycastTarget = true;

        Text text = CreateText(rect, "Label", label,
            FilterFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(text.rectTransform);
        text.color = GoldAccent;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(onClick);
    }

    /// <summary>
    /// Builds the right details panel: identity header (icon, name, rarity,
    /// slot, level badges), PRIMARY STAT, SECONDARY STATS, EQUIPPED BY, the
    /// EQUIP TO hero picker, the flavor description, the three action
    /// buttons, and the status line. Values are filled in
    /// <see cref="RefreshDetails"/>.
    /// </summary>
    private void BuildDetailsPanel(RectTransform root)
    {
        RectTransform panel = CreateRect(root, "Details");
        panel.anchorMin = TopLeft;
        panel.anchorMax = TopLeft;
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(DetailsX, -142f);
        panel.sizeDelta = new Vector2(DetailsWidth, 838f);

        CreateSlicedImage(panel, "Back", CardColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, panel.sizeDelta);

        // ---- Identity header ----
        detailIconFrame = CreateSlicedImage(panel, "IconFrame", Color.white, frameSprite,
            TopLeft, TopLeft, new Vector2(26f, -26f), new Vector2(96f, 96f));

        detailIconImage = CreateSlicedImage(panel, "Icon", Color.white, null,
            TopLeft, TopLeft, new Vector2(34f, -34f), new Vector2(80f, 80f));
        detailIconImage.preserveAspect = true;
        detailIconImage.gameObject.SetActive(false);

        detailIconGlyph = CreateText(panel, "IconGlyph", "?",
            DetailPortraitFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(34f, -34f), new Vector2(80f, 80f));

        detailName = CreateText(panel, "Name", string.Empty,
            DetailNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(140f, -32f), new Vector2(640f, 42f));

        detailRarityBack = CreateSlicedImage(panel, "RarityBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(140f, -84f), new Vector2(150f, 32f));
        detailRarityText = CreateText(panel, "RarityText", string.Empty,
            DetailBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(140f, -84f), new Vector2(150f, 32f));

        CreateSlicedImage(panel, "SlotBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(300f, -84f), new Vector2(150f, 32f));
        detailSlotText = CreateText(panel, "SlotText", string.Empty,
            DetailBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(300f, -84f), new Vector2(150f, 32f));
        detailSlotText.color = DimTextColor;

        CreateSlicedImage(panel, "LevelBadge", BadgeColor, panelSprite,
            TopLeft, TopLeft, new Vector2(460f, -84f), new Vector2(190f, 32f));
        detailLevelText = CreateText(panel, "LevelText", string.Empty,
            DetailBadgeFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(460f, -84f), new Vector2(190f, 32f));
        detailLevelText.color = GoldAccent;

        // ---- Primary stat ----
        CreateText(panel, "PrimaryHeader", "PRIMARY STAT",
            SectionHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(26f, -146f), new Vector2(400f, 28f)).color = GoldAccent;
        detailPrimaryText = CreateText(panel, "Primary", string.Empty,
            StatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(26f, -180f), new Vector2(500f, 30f));

        // ---- Secondary stats ----
        CreateText(panel, "SecondaryHeader", "SECONDARY STATS",
            SectionHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(26f, -232f), new Vector2(400f, 28f)).color = GoldAccent;
        for (int i = 0; i < detailSecondaryTexts.Length; i++)
        {
            detailSecondaryTexts[i] = CreateText(panel, "Secondary" + (i + 1), string.Empty,
                SecondaryFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(26f, -268f - i * 26f), new Vector2(500f, 24f));
            detailSecondaryTexts[i].color = Color.white;
        }

        detailNoSecondaryText = CreateText(panel, "NoSecondary", "None",
            SecondaryFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(26f, -268f), new Vector2(500f, 24f));
        detailNoSecondaryText.color = DimTextColor;

        // ---- Equipped-by + equip target ----
        CreateText(panel, "EquippedHeader", "EQUIPPED BY",
            SectionHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(26f, -390f), new Vector2(400f, 28f)).color = GoldAccent;
        detailEquippedText = CreateText(panel, "Equipped", string.Empty,
            StatFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(26f, -424f), new Vector2(500f, 30f));

        CreateText(panel, "TargetHeader", "EQUIP TO",
            SectionHeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(26f, -478f), new Vector2(400f, 28f)).color = GoldAccent;
        targetHeroText = CreateText(panel, "Target", string.Empty,
            StatFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            TopLeft, TopLeft, new Vector2(110f, -512f), new Vector2(560f, 34f));
        targetHeroText.color = Color.white;

        BuildSmallButton(panel, "TargetPrev", "<", new Vector2(26f, -512f), 44f, OnTargetPrev);
        BuildSmallButton(panel, "TargetNext", ">", new Vector2(700f, -512f), 44f, OnTargetNext);

        // ---- Description ----
        detailDescription = CreateText(panel, "Description", string.Empty,
            SecondaryFontSize, FontStyle.Italic, TextAnchor.UpperLeft,
            TopLeft, TopLeft, new Vector2(26f, -570f), new Vector2(DetailsWidth - 52f, 60f));
        detailDescription.color = DimTextColor;
        detailDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
        detailDescription.verticalOverflow = VerticalWrapMode.Overflow;

        // ---- Action buttons ----
        equipButton = BuildActionButton(panel, "EquipButton", new Vector2(26f, -664f), 300f, OnEquipClicked, out equipLabel);
        upgradeButton = BuildActionButton(panel, "UpgradeButton", new Vector2(346f, -664f), 340f, OnUpgradeClicked, out upgradeLabel);
        sellButton = BuildActionButton(panel, "SellButton", new Vector2(706f, -664f), 300f, OnSellClicked, out sellLabel);

        // ---- Status ----
        statusText = CreateText(panel, "Status", string.Empty,
            StatusFontSize, FontStyle.Normal, TextAnchor.UpperLeft,
            TopLeft, TopLeft, new Vector2(26f, -744f), new Vector2(DetailsWidth - 52f, 70f));
        statusText.color = DimTextColor;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow = VerticalWrapMode.Overflow;

        // ---- Empty state (shown only when no item is selected) ----
        detailEmptyText = CreateText(panel, "Empty", "Select an item",
            EmptyStateFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 40f));
        detailEmptyText.color = DimTextColor;

        // Everything except the empty state disappears without a selection.
        detailElements.AddRange(new Graphic[]
        {
            detailIconFrame, detailIconImage, detailIconGlyph, detailName,
            detailRarityBack, detailRarityText, detailSlotText, detailLevelText,
            detailPrimaryText, detailNoSecondaryText, detailEquippedText, targetHeroText,
            detailDescription, statusText,
        });
        detailElements.AddRange(detailSecondaryTexts);
    }

    /// <summary>Builds one action button (raycastable dark panel + gold label).</summary>
    private Button BuildActionButton(Transform parent, string name, Vector2 position, float width, UnityEngine.Events.UnityAction onClick, out Text label)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = TopLeft;
        rect.anchorMax = TopLeft;
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, 64f);

        Image back = CreateSlicedImage(rect, "Back", ActionReadyColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);
        back.raycastTarget = true;

        label = CreateText(rect, "Label", string.Empty,
            ActionButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(label.rectTransform);
        label.color = GoldAccent;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(onClick);
        return button;
    }

    /// <summary>Builds one small square button (the hero picker arrows).</summary>
    private Button BuildSmallButton(Transform parent, string name, string label, Vector2 position, float size, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect(parent, name);
        rect.anchorMin = TopLeft;
        rect.anchorMax = TopLeft;
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(size, size * 0.78f);

        Image back = CreateSlicedImage(rect, "Back", BadgeColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, rect.sizeDelta);
        back.raycastTarget = true;

        Text text = CreateText(rect, "Label", label,
            ActionButtonFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(text.rectTransform);
        text.color = GoldAccent;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        button.onClick.AddListener(onClick);
        return button;
    }

    // ---------------------------------------------------------------------
    // List rendering.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the item cards for the current filters and page, straight
    /// from the live inventory. Each card shows the icon placeholder (or
    /// definition art), name, rarity, slot, level, primary stat, and the
    /// wearing hero; clicking a card inspects it.
    /// </summary>
    private void RebuildList()
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
        cardItems.Clear();

        List<EquipmentInstance> filtered = FilteredItems();
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)ItemsPerPage));
        page = Mathf.Clamp(page, 0, pageCount - 1);

        int first = page * ItemsPerPage;
        int count = Mathf.Min(ItemsPerPage, filtered.Count - first);
        for (int i = 0; i < count; i++)
        {
            EquipmentInstance item = filtered[first + i];
            if (item == null)
            {
                continue;
            }

            cardItems.Add(item);
            BuildItemCard(item, i);
        }

        emptyText.gameObject.SetActive(filtered.Count == 0);
        pageText.text = filtered.Count + " item(s)   -   page " + (page + 1) + " / " + pageCount;

        // Keep a selection whenever possible: a sold or still-valid item
        // stays inspected; with nothing selected, inspect the first card.
        if (selectedItem == null && cardItems.Count > 0)
        {
            selectedItem = cardItems[0];
            cardSelectionFrames[0].gameObject.SetActive(true);
        }

        RefreshFilterStates();
    }

    /// <summary>
    /// Builds one item card: a raycastable dark panel with a rarity-framed
    /// icon placeholder, the item's name, a rarity/slot/level meta line,
    /// the primary stat, and an equipped indicator.
    /// </summary>
    private void BuildItemCard(EquipmentInstance item, int index)
    {
        RarityStyle rarity = RarityStyleFor(item.rarity);
        EquipmentData definition = item.data;

        RectTransform card = CreateRect(listRoot, "Item" + (index + 1));
        card.anchorMin = TopLeft;
        card.anchorMax = TopLeft;
        card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(0f, -index * (CardHeight + CardGap));
        card.sizeDelta = new Vector2(ListWidth, CardHeight);

        Image back = CreateSlicedImage(card, "Back", CardColor, panelSprite,
            TopLeft, TopLeft, Vector2.zero, card.sizeDelta);
        back.raycastTarget = true;

        // Selection highlight: a gold frame exactly over the card.
        Image selection = CreateSlicedImage(card, "Selection", GoldAccent, frameSprite,
            TopLeft, TopLeft, new Vector2(-3f, 3f), new Vector2(ListWidth + 6f, CardHeight + 6f));
        selection.gameObject.SetActive(ReferenceEquals(item, selectedItem));
        cardSelectionFrames.Add(selection);

        // Icon placeholder: rarity-colored frame + the item's initial (or
        // the definition's art when it exists).
        CreateSlicedImage(card, "IconFrame", rarity.Color, frameSprite,
            TopLeft, TopLeft, new Vector2(10f, -12f), new Vector2(64f, 64f));

        if (definition != null && definition.icon != null)
        {
            Image icon = CreateSlicedImage(card, "Icon", Color.white, null,
                TopLeft, TopLeft, new Vector2(18f, -20f), new Vector2(48f, 48f));
            icon.sprite = definition.icon;
            icon.preserveAspect = true;
        }
        else
        {
            string name = definition != null ? definition.equipmentName : item.instanceId;
            CreateText(card, "IconGlyph", InitialOf(name),
                CardPortraitFontSize, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(18f, -20f), new Vector2(48f, 48f)).color =
                Color.Lerp(rarity.Color, Color.white, 0.35f);
        }

        string itemName = definition != null && !string.IsNullOrEmpty(definition.equipmentName)
            ? definition.equipmentName
            : "Unknown item";
        CreateText(card, "Name", itemName,
            CardNameFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(86f, -8f), new Vector2(420f, 26f)).color = Color.white;

        CreateText(card, "Meta", rarity.Label + " " + SlotLabel(item.slot)
            + "   Lv " + item.level + "/" + item.MaxLevel
            + (item.locked ? "   [LOCKED]" : string.Empty),
            CardMetaFontSize, FontStyle.Normal, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(86f, -38f), new Vector2(420f, 20f)).color = DimTextColor;

        CreateText(card, "Stat", EquipmentStatLabel(item.mainStatType)
            + " " + EquipmentStatValueText(item.mainStatType, item.mainStatValue),
            CardMetaFontSize, FontStyle.Bold, TextAnchor.MiddleLeft,
            TopLeft, TopLeft, new Vector2(86f, -62f), new Vector2(420f, 20f)).color = GoldAccent;

        // Equipped indicator: the wearing hero's name (right side).
        HeroInstance wearer = FindHeroByItem(item);
        if (wearer != null)
        {
            CreateText(card, "Equipped", "EQUIPPED\n" + wearer.displayName,
                CardMetaFontSize, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(190f, 44f)).color = GoldAccent;
        }

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        EquipmentInstance captured = item;
        button.onClick.AddListener(() => SelectItem(captured));

        cardRoots.Add(card.gameObject);
    }

    /// <summary>
    /// The inventory's items matching the current slot and rarity filters,
    /// in acquisition order.
    /// </summary>
    private List<EquipmentInstance> FilteredItems()
    {
        EquipmentInventory inventory = runner != null ? runner.EquipmentInventory : null;
        List<EquipmentInstance> filtered = new List<EquipmentInstance>();
        if (inventory == null)
        {
            return filtered;
        }

        foreach (EquipmentInstance item in inventory.Items)
        {
            if (item == null)
            {
                continue;
            }

            if (slotFilter >= 0 && (int)item.slot != slotFilter)
            {
                continue;
            }

            if (rarityFilter >= 0 && (int)item.rarity != rarityFilter)
            {
                continue;
            }

            filtered.Add(item);
        }

        return filtered;
    }

    /// <summary>Selects an item for inspection (or clears the selection with null).</summary>
    private void SelectItem(EquipmentInstance item)
    {
        selectedItem = item;
        for (int i = 0; i < cardSelectionFrames.Count && i < cardItems.Count; i++)
        {
            cardSelectionFrames[i].gameObject.SetActive(ReferenceEquals(cardItems[i], selectedItem));
        }

        RefreshDetails();
    }

    // ---------------------------------------------------------------------
    // Refresh.
    // ---------------------------------------------------------------------

    /// <summary>Refreshes the gold chip from the live wallet.</summary>
    private void RefreshTopBar()
    {
        PlayerWallet wallet = runner != null ? runner.Wallet : null;
        if (goldText != null)
        {
            goldText.text = (wallet != null ? wallet.gold : 0) + " GOLD";
        }
    }

    /// <summary>Colors the filter buttons to show the active filters.</summary>
    private void RefreshFilterStates()
    {
        for (int i = 0; i < slotFilterBacks.Count; i++)
        {
            bool active = i - 1 == slotFilter;
            slotFilterBacks[i].color = active ? ActionReadyColor : BadgeColor;
            slotFilterLabels[i].color = active ? GoldAccent : DimTextColor;
        }

        for (int i = 0; i < rarityFilterBacks.Count; i++)
        {
            bool active = i - 1 == rarityFilter;
            rarityFilterBacks[i].color = active ? ActionReadyColor : BadgeColor;
            rarityFilterLabels[i].color = active ? GoldAccent : DimTextColor;
        }
    }

    /// <summary>
    /// Fills the details panel from the selected item and reflects the real
    /// action states: EQUIP becomes UNEQUIP while the item is worn, UPGRADE
    /// shows the gold cost (or MAX LEVEL at the cap) and disables when
    /// impossible, SELL shows the value and refuses equipped or locked
    /// items. Pure display - all logic lives in the gameplay services.
    /// </summary>
    private void RefreshDetails()
    {
        bool hasItem = selectedItem != null;
        foreach (Graphic element in detailElements)
        {
            element.gameObject.SetActive(hasItem);
        }

        detailEmptyText.gameObject.SetActive(!hasItem);
        if (!hasItem)
        {
            return;
        }

        EquipmentInstance item = selectedItem;
        EquipmentData definition = item.data;
        RarityStyle rarity = RarityStyleFor(item.rarity);

        // Identity.
        string itemName = definition != null && !string.IsNullOrEmpty(definition.equipmentName)
            ? definition.equipmentName
            : "Unknown item";
        detailName.text = itemName;

        if (definition != null && definition.icon != null)
        {
            detailIconImage.sprite = definition.icon;
            detailIconImage.gameObject.SetActive(true);
            detailIconGlyph.gameObject.SetActive(false);
        }
        else
        {
            detailIconImage.gameObject.SetActive(false);
            detailIconGlyph.gameObject.SetActive(true);
            detailIconGlyph.text = InitialOf(itemName);
            detailIconGlyph.color = Color.Lerp(rarity.Color, Color.white, 0.35f);
        }

        detailIconFrame.color = rarity.Color;
        detailRarityBack.color = Color.Lerp(rarity.Color, Color.black, 0.72f);
        detailRarityText.text = rarity.Label;
        detailRarityText.color = Color.Lerp(rarity.Color, Color.white, 0.25f);
        detailSlotText.text = SlotLabel(item.slot);
        detailLevelText.text = "LV " + item.level + " / " + item.MaxLevel;

        // Primary stat.
        detailPrimaryText.text = EquipmentStatLabel(item.mainStatType)
            + " " + EquipmentStatValueText(item.mainStatType, item.mainStatValue);

        // Secondary stats.
        int secondaryCount = item.secondaryStats != null ? item.secondaryStats.Count : 0;
        for (int i = 0; i < detailSecondaryTexts.Length; i++)
        {
            if (i < secondaryCount && item.secondaryStats[i] != null)
            {
                EquipmentSecondaryStat secondary = item.secondaryStats[i];
                detailSecondaryTexts[i].text = EquipmentStatLabel(secondary.statType)
                    + " " + EquipmentStatValueText(secondary.statType, secondary.value);
                detailSecondaryTexts[i].gameObject.SetActive(true);
            }
            else
            {
                detailSecondaryTexts[i].gameObject.SetActive(false);
            }
        }

        detailNoSecondaryText.gameObject.SetActive(secondaryCount == 0);

        // Equipped-by + equip target.
        HeroInstance wearer = FindHeroByItem(item);
        detailEquippedText.text = wearer != null ? wearer.displayName : "Not equipped";
        detailEquippedText.color = wearer != null ? Color.white : DimTextColor;
        targetHeroText.text = targetHero != null ? targetHero.displayName : "No heroes";

        // Description (flavor from the definition; items without one show
        // nothing extra - never a fake description).
        detailDescription.text = definition != null ? definition.description ?? string.Empty : string.Empty;

        // ----- Action buttons -----
        PlayerWallet wallet = runner != null ? runner.Wallet : null;
        bool wornByTarget = targetHero != null && item.equippedHeroId == HeroIdOf(targetHero);

        // EQUIP <-> UNEQUIP: while the item is worn (by anyone) the button
        // unequips it; otherwise it equips to the target hero.
        equipButton.interactable = targetHero != null || wearer != null;
        equipLabel.text = item.equippedHeroId.Length > 0
            ? "UNEQUIP" + (wornByTarget ? string.Empty : " (" + (wearer != null ? wearer.displayName : "other hero") + ")")
            : "EQUIP";

        // UPGRADE: the real cost, or MAX LEVEL at the cap; disabled when
        // the upgrade would fail right now.
        bool canUpgrade = item.CanUpgrade(wallet, out _);
        upgradeButton.interactable = canUpgrade;
        upgradeLabel.text = item.IsMaxLevel ? "MAX LEVEL" : "UPGRADE  " + item.UpgradeGoldCost + " g";

        // SELL: the real value; refuses equipped or locked items.
        bool sellable = item.equippedHeroId.Length == 0 && !item.locked;
        sellButton.interactable = sellable;
        sellLabel.text = sellable ? "SELL  +" + item.SellValue + " g" : "SELL";
    }

    // ---------------------------------------------------------------------
    // Actions. Each performs the real operation through the gameplay
    // services, refreshes the view, and persists immediately.
    // ---------------------------------------------------------------------

    /// <summary>EQUIP/UNEQUIP: equips to the target hero, or returns a worn item to the inventory.</summary>
    private void OnEquipClicked()
    {
        EquipmentInstance item = selectedItem;
        EquipmentInventory inventory = runner != null ? runner.EquipmentInventory : null;
        if (item == null || inventory == null)
        {
            return;
        }

        if (item.equippedHeroId.Length > 0)
        {
            HeroInstance wearer = FindHeroByItem(item);
            if (wearer == null || !inventory.Unequip(wearer, item.slot))
            {
                SetStatus("Could not unequip " + item.instanceId + ".");
                return;
            }

            SetStatus(item.instanceId + " returned to the inventory.");
        }
        else
        {
            if (targetHero == null)
            {
                SetStatus("No hero to equip to.");
                return;
            }

            if (inventory.Equip(targetHero, item) != EquipResult.Equipped)
            {
                SetStatus("Could not equip " + item.instanceId + ".");
                return;
            }

            SetStatus(item.instanceId + " equipped to " + targetHero.displayName + ".");
        }

        AfterAction();
    }

    /// <summary>UPGRADE: one real gold upgrade through the item's own API.</summary>
    private void OnUpgradeClicked()
    {
        EquipmentInstance item = selectedItem;
        PlayerWallet wallet = runner != null ? runner.Wallet : null;
        if (item == null || wallet == null)
        {
            return;
        }

        if (!item.Upgrade(wallet))
        {
            item.CanUpgrade(wallet, out string reason);
            SetStatus("Cannot upgrade: " + reason + ".");
            return;
        }

        SetStatus(item.instanceId + " upgraded to Lv " + item.level
            + " (" + EquipmentStatLabel(item.mainStatType) + " "
            + EquipmentStatValueText(item.mainStatType, item.mainStatValue) + ").");
        AfterAction();
    }

    /// <summary>SELL: removes the item and awards its gold value (equipped/locked items refuse).</summary>
    private void OnSellClicked()
    {
        EquipmentInstance item = selectedItem;
        EquipmentInventory inventory = runner != null ? runner.EquipmentInventory : null;
        PlayerWallet wallet = runner != null ? runner.Wallet : null;
        if (item == null || inventory == null || wallet == null)
        {
            return;
        }

        int value = item.SellValue;
        if (!inventory.Sell(item.instanceId, wallet))
        {
            SetStatus(item.locked ? "Locked items cannot be sold." : "Unequip the item before selling it.");
            return;
        }

        selectedItem = null;
        SetStatus("Sold " + item.instanceId + " for " + value + " gold.");
        AfterAction();
    }

    /// <summary>Cycles the equip target to the previous roster hero.</summary>
    private void OnTargetPrev()
    {
        CycleTargetHero(-1);
    }

    /// <summary>Cycles the equip target to the next roster hero.</summary>
    private void OnTargetNext()
    {
        CycleTargetHero(1);
    }

    /// <summary>Applies a slot filter (pass -1 for ALL) and returns to the first page.</summary>
    private void OnSlotFilterClicked(int slot)
    {
        slotFilter = slot;
        page = 0;
        RebuildList();
    }

    /// <summary>Applies a rarity filter (pass -1 for ALL RARITIES) and returns to the first page.</summary>
    private void OnRarityFilterClicked(int rarity)
    {
        rarityFilter = rarity;
        page = 0;
        RebuildList();
    }

    /// <summary>Turns to the previous page (clamped).</summary>
    private void OnPrevPage()
    {
        page = Mathf.Max(0, page - 1);
        RebuildList();
    }

    /// <summary>Turns to the next page (clamped).</summary>
    private void OnNextPage()
    {
        page = page + 1;
        RebuildList();
    }

    private void GoBack()
    {
        if (runner != null)
        {
            runner.CloseEquipmentScreen();
        }
    }

    /// <summary>Cycles the equip target by the given direction through the roster.</summary>
    private void CycleTargetHero(int direction)
    {
        HeroRoster roster = runner != null ? runner.Roster : null;
        if (roster == null || roster.Heroes.Count == 0)
        {
            return;
        }

        int index = targetHero != null ? IndexInRoster(roster, targetHero) : 0;
        if (index < 0)
        {
            index = 0;
        }

        index = (index + direction + roster.Heroes.Count) % roster.Heroes.Count;
        targetHero = roster.Heroes[index];
        RefreshDetails();
    }

    /// <summary>Refreshes the whole view and persists the profile after any equipment action.</summary>
    private void AfterAction()
    {
        RebuildList();
        RefreshDetails();
        RefreshTopBar();
        if (runner != null)
        {
            runner.SaveProfile();
        }
    }

    /// <summary>Shows one line of feedback for the latest action.</summary>
    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message ?? string.Empty;
        }
    }

    // ---------------------------------------------------------------------
    // Lookup helpers (read-only).
    // ---------------------------------------------------------------------

    /// <summary>The hero currently wearing the item, resolved through the roster (or null).</summary>
    private HeroInstance FindHeroByItem(EquipmentInstance item)
    {
        if (item == null || item.equippedHeroId.Length == 0)
        {
            return null;
        }

        HeroRoster roster = runner != null ? runner.Roster : null;
        return roster != null ? roster.FindByHeroId(item.equippedHeroId) : null;
    }

    /// <summary>The owned item with the selected instance id (keeps the selection stable across refreshes), or null.</summary>
    private EquipmentInstance FindByIdSafe(EquipmentInstance item)
    {
        if (item == null)
        {
            return null;
        }

        EquipmentInventory inventory = runner != null ? runner.EquipmentInventory : null;
        return inventory != null ? inventory.FindById(item.instanceId) : null;
    }

    /// <summary>The hero's stable HeroId (empty when the hero or its data is missing).</summary>
    private static string HeroIdOf(HeroInstance hero)
    {
        return hero != null && hero.data != null ? hero.data.HeroId : string.Empty;
    }

    /// <summary>The hero's index in the roster (-1 when absent).</summary>
    private static int IndexInRoster(HeroRoster roster, HeroInstance hero)
    {
        if (roster == null || hero == null)
        {
            return -1;
        }

        for (int i = 0; i < roster.Heroes.Count; i++)
        {
            if (ReferenceEquals(roster.Heroes[i], hero))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The first character of a name (icon placeholder glyph).</summary>
    private static string InitialOf(string name)
    {
        return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
    }

    /// <summary>Uppercase display label for an equipment slot (UI copy only - the slot enum itself is the data).</summary>
    private static string SlotLabel(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Weapon: return "WEAPON";
            case EquipmentSlot.Helmet: return "HELMET";
            case EquipmentSlot.Armor: return "ARMOR";
            case EquipmentSlot.Gloves: return "GLOVES";
            case EquipmentSlot.Boots: return "BOOTS";
            case EquipmentSlot.Accessory: return "ACCESSORY";
            default: return slot.ToString().ToUpperInvariant();
        }
    }

    /// <summary>Short label for an equipment stat type (UI copy only - the enum itself is the data).</summary>
    private static string EquipmentStatLabel(EquipmentStatType statType)
    {
        switch (statType)
        {
            case EquipmentStatType.HP: return "HP";
            case EquipmentStatType.ATK: return "ATK";
            case EquipmentStatType.DEF: return "DEF";
            case EquipmentStatType.SPD: return "SPD";
            case EquipmentStatType.CRIT_RATE: return "CRIT RATE";
            case EquipmentStatType.CRIT_DAMAGE: return "CRIT DMG";
            case EquipmentStatType.ACCURACY: return "ACC";
            case EquipmentStatType.RESISTANCE: return "RES";
            default: return statType.ToString().ToUpperInvariant();
        }
    }

    /// <summary>Formats one stat value: "+12" for flats, "+2%" for percentage stats.</summary>
    private static string EquipmentStatValueText(EquipmentStatType statType, int value)
    {
        return "+" + value + (EquipmentProgression.IsPercentStat(statType) ? "%" : string.Empty);
    }

    /// <summary>
    /// Style for a rarity tier: frame/badge color and display label. Unknown
    /// (future) rarities safely fall back to a neutral style labeled with
    /// the enum value's own name (mirrors the other screens' tables).
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

    /// <summary>Style for a rarity tier: frame/badge color and display label.</summary>
    private struct RarityStyle
    {
        /// <summary>The rarity's frame/badge color.</summary>
        public Color Color;

        /// <summary>The rarity's display label.</summary>
        public string Label;

        /// <summary>Creates a style from a color and a label.</summary>
        public RarityStyle(Color color, string label)
        {
            Color = color;
            Label = label;
        }
    }

    // ---------------------------------------------------------------------
    // Basic view factories (anchored rects, images, texts). Mirrors
    // HeroesScreen's so this file stays self-contained.
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
    // Runtime sprite generation (no art assets required). Mirrors the other
    // screens' factory so this file stays self-contained.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a shared 48x48 white rounded square as a 9-sliced sprite, so
    /// panels, cards, badges, and buttons get clean rounded corners at any
    /// size. The outline variant draws only a rounded border, used for
    /// rarity frames and the selection highlight.
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
