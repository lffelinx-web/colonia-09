using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Colonia09.Presentation
{
    public enum ModalKind { Intro, Pause, Win, Lost }

    /// <summary>Forwards taps on the battlefield area. Modals block it through normal UI raycasts.</summary>
    public sealed class BoardTouchArea : Graphic, IPointerDownHandler
    {
        public GameController Game;
        public void OnPointerDown(PointerEventData eventData) => Game.TapScreen(eventData.position);
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }

    /// <summary>Builds the whole portrait interface from code, inside the device safe area.</summary>
    public sealed class GameUI : MonoBehaviour
    {
        const float RefWidth = 1080, HudHeight = 218, DockHeight = 352;

        static readonly Color Bg = Html("#071622"), Panel = Html("#102638"), PanelLight = Html("#173449"), Line = Html("#35566a"),
            Cyan = Html("#86e6ee"), Txt = Html("#eaf9fb"), Muted = Html("#99b5c7"), Gold = Html("#ffd38b"), Red = Html("#ff8c8a"), Green = Html("#91e5bd");

        GameController game;
        Font font;
        Sprite rounded;
        RectTransform safeArea, field;
        Text waveText, coreText, chargeText, powerText, usageText, statusText, toastText, dockTitle, dockHint;
        RectTransform waveFill, energyFill;
        Image energyFillImage;
        readonly List<Image> corePips = new List<Image>();
        Text modalKicker, modalTitle, modalLead, modalInfo, modalFooter, primaryLabel, secondaryLabel;
        Text inspectName, inspectDetail, upgradeLabel, sellLabel, inspectStatus, inspectHp, upgradeCost, sellCost;
        Image inspectStatusBg, inspectHpFillImage;
        RectTransform inspectHpFill;
        GameObject upgradeChip;
        readonly List<Image> levelPips = new List<Image>();
        Sprite bevel;
        Image inspectIcon, toastBg;
        Button upgradeButton;
        GameObject modal, inspector, cardsRoot, secondaryButton, difficultyRow, closeInspector;
        readonly Dictionary<ModuleType, Image> cardBg = new Dictionary<ModuleType, Image>();
        readonly Dictionary<ModuleType, CanvasGroup> cardGroup = new Dictionary<ModuleType, CanvasGroup>();
        readonly List<Button> difficultyButtons = new List<Button>();
        readonly List<Text> labels = new List<Text>();
        RectTransform labelRoot;
        int labelsUsed;
        float toastUntil;
        bool deckDirty = true;
        Rect lastSafe;
        Difficulty chosenDifficulty = Difficulty.Tactical;
        ModalKind modalKind;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public float DebugSafeInsetTop, DebugSafeInsetBottom;
        public void ReapplySafeArea() => ApplySafeArea();
#endif

        public Rect FieldPixels { get; private set; }
        public ModalKind CurrentModal => modalKind;
        public bool ModalOpen => modal.activeSelf;
        public bool InspectorOpen => inspector.activeSelf;

        // Exposed so the automated test presses the same buttons as a player.
        public readonly Dictionary<ModuleType, Button> CardButtons = new Dictionary<ModuleType, Button>();
        public Button PrimaryButton { get; private set; }
        public Button SecondaryButton { get; private set; }
        public Button MenuButton { get; private set; }
        public Button AudioButton { get; private set; }
        Text audioLabel;

        void RefreshAudioButton()
        {
            bool on = game.Audio.Enabled;
            audioLabel.text = "♪";
            audioLabel.color = on ? Cyan : new Color(Muted.r, Muted.g, Muted.b, .35f);
        }
        // Main menu and options.
        GameObject mainMenu, menuPage, optionsPage;
        Text effectsLabel, musicLabel;
        public bool MainMenuOpen => mainMenu.activeSelf;
        public bool OptionsOpen => mainMenu.activeSelf && optionsPage.activeSelf;
        public Button CampaignButton { get; private set; }
        public Button QuickPlayButton { get; private set; }
        public Button MultiplayerButton { get; private set; }
        public Button OptionsButton { get; private set; }
        public Button OptionsBackButton { get; private set; }
        public Button EffectsButton { get; private set; }
        public Button MusicButton { get; private set; }

        public Button UpgradeButton => upgradeButton;
        public Button SellButton { get; private set; }
        public Button CloseButton { get; private set; }

        static Color Html(string s) { ColorUtility.TryParseHtmlString(s, out var c); return c; }

        /// <summary>Replaces glyphs the mobile fallback fonts may not include.</summary>
        static string Safe(string s) => s.Replace("⚠ ", "! ").Replace("🔒 ", "").Replace("⏳ ", "").Replace("↑ ", "").Replace("ϟ ", "").Replace("⚡ ", "");

        public void Init(GameController controller)
        {
            game = controller;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rounded = MakeRounded();
            bevel = MakeBevel();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, 1920);
            scaler.matchWidthOrHeight = 0;

            safeArea = Rt("SafeArea", canvasGo.transform);
            Stretch(safeArea);
            BuildField();
            labelRoot = Rt("WorldLabels", safeArea);
            Stretch(labelRoot);
            BuildHud();
            BuildDock();
            BuildToast();
            BuildModal();
            BuildMainMenu();
            ApplySafeArea();
        }

        // ---- Main menu ----------------------------------------------------------------

        void BuildMainMenu()
        {
            var root = Rt("MainMenu", safeArea.parent);
            Stretch(root);
            mainMenu = root.gameObject;
            root.gameObject.AddComponent<Image>().color = Html("#06121f");
            // Faint grid of deck plates behind the title, so the screen is not a flat color.
            var plates = Rt("Plates", root);
            Stretch(plates);
            var grid = plates.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(150, 150); grid.spacing = new Vector2(8, 8);
            grid.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < 7 * 16; i++)
            {
                var plate = Rt("Plate", plates).gameObject.AddComponent<Image>();
                plate.sprite = rounded; plate.type = Image.Type.Sliced; plate.raycastTarget = false;
                plate.color = new Color(.11f, .2f, .27f, (i * 7 % 5) == 0 ? .32f : .18f);
            }
            plates.gameObject.AddComponent<RectMask2D>();

            menuPage = Rt("Menu", root).gameObject;
            var page = (RectTransform)menuPage.transform;
            page.anchorMin = new Vector2(.04f, .04f); page.anchorMax = new Vector2(.96f, .96f);
            page.offsetMin = page.offsetMax = Vector2.zero;

            var kicker = Label(page, "ZEITEND INTERACTIVE PRESENTA", 28, Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            AnchorV(kicker.rectTransform, .915f, .955f);
            var title = Label(page, "COLONIA 09", 140, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.resizeTextForBestFit = true; title.resizeTextMinSize = 60; title.resizeTextMaxSize = 140;
            AnchorV(title.rectTransform, .81f, .915f);
            var glow = title.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, .35f); glow.effectDistance = new Vector2(3, -3);
            var tagline = Label(page, "Defiende la colonia. Construye su futuro.", 34, Muted, TextAnchor.MiddleCenter);
            AnchorV(tagline.rectTransform, .765f, .81f);

            // Emblem: the colony core, framed by the network relays.
            var emblem = Rt("Emblem", page);
            emblem.anchorMin = emblem.anchorMax = new Vector2(.5f, .655f);
            emblem.sizeDelta = new Vector2(250, 250);
            var core = Rt("Core", emblem).gameObject.AddComponent<Image>();
            core.sprite = game.Board.ModuleSprite(ModuleType.Reactor); core.preserveAspect = true; core.raycastTarget = false;
            Stretch(core.rectTransform);
            foreach (var side in new[] { -1, 1 })
            {
                var relay = Rt("Relay", emblem).gameObject.AddComponent<Image>();
                relay.sprite = game.Board.ModuleSprite(ModuleType.Relay); relay.preserveAspect = true; relay.raycastTarget = false;
                relay.rectTransform.anchorMin = relay.rectTransform.anchorMax = new Vector2(.5f, .5f);
                relay.rectTransform.sizeDelta = new Vector2(130, 130);
                relay.rectTransform.anchoredPosition = new Vector2(side * 250, -20);
                var link = Rt("Link", emblem).gameObject.AddComponent<Image>();
                link.color = new Color(.54f, .9f, .93f, .45f); link.raycastTarget = false;
                link.rectTransform.anchorMin = link.rectTransform.anchorMax = new Vector2(.5f, .5f);
                link.rectTransform.sizeDelta = new Vector2(120, 6);
                link.rectTransform.anchoredPosition = new Vector2(side * 158, -12);
                link.transform.SetAsFirstSibling();
            }

            var stack = Rt("Buttons", page);
            stack.anchorMin = new Vector2(.06f, .12f); stack.anchorMax = new Vector2(.94f, .52f);
            stack.offsetMin = stack.offsetMax = Vector2.zero;
            var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 26; layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = true; layout.childForceExpandWidth = true;
            CampaignButton = MenuEntry(stack, "CAMPAÑA", "Próximamente · el mapa de la colonia", PanelLight, Txt, null);
            QuickPlayButton = MenuEntry(stack, "PARTIDA RÁPIDA", "9 oleadas en el tablero completo", Cyan, Bg, () => { game.Audio.Play("select", true); HideMainMenu(); ShowModal(ModalKind.Intro); });
            MultiplayerButton = MenuEntry(stack, "MULTIJUGADOR", "Próximamente", PanelLight, Txt, null);
            OptionsButton = MenuEntry(stack, "OPCIONES", "Sonido, música y créditos", PanelLight, Txt, () => ShowOptions(true));

            var footer = Label(page, "v0.9.1 · Zeitend Interactive", 24, Muted, TextAnchor.MiddleCenter);
            AnchorV(footer.rectTransform, .02f, .06f);

            BuildOptions(root);
            mainMenu.SetActive(false);
        }

        /// <summary>Large menu button with a title and a one-line description. Without an action it shows as unavailable.</summary>
        Button MenuEntry(Transform parent, string title, string subtitle, Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
        {
            var button = TextButton(parent, title, 46, bg, fg, onClick ?? (() => { }));
            var label = button.GetComponentInChildren<Text>();
            label.rectTransform.anchorMin = new Vector2(0, .4f);
            label.rectTransform.offsetMin = new Vector2(20, 0); label.rectTransform.offsetMax = new Vector2(-20, -8);
            var sub = Label(button.transform, subtitle, 26, fg == Bg ? new Color(Bg.r, Bg.g, Bg.b, .75f) : Muted, TextAnchor.MiddleCenter);
            sub.rectTransform.anchorMax = new Vector2(1, .44f);
            sub.rectTransform.offsetMin = new Vector2(20, 10); sub.rectTransform.offsetMax = new Vector2(-20, 0);
            if (onClick == null)
            {
                button.interactable = false;
                label.color = new Color(fg.r, fg.g, fg.b, .45f);
            }
            return button;
        }

        void BuildOptions(RectTransform root)
        {
            optionsPage = Rt("Options", root).gameObject;
            var page = (RectTransform)optionsPage.transform;
            page.anchorMin = new Vector2(.06f, .06f); page.anchorMax = new Vector2(.94f, .94f);
            page.offsetMin = page.offsetMax = Vector2.zero;

            var title = Label(page, "OPCIONES", 80, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            AnchorV(title.rectTransform, .86f, .95f);
            var section = Label(page, "SONIDO", 28, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AnchorV(section.rectTransform, .79f, .83f);
            EffectsButton = OptionRow(page, "Efectos de sonido", .69f, () => { game.Audio.SetEffects(!game.Audio.EffectsOn); RefreshOptions(); }, out effectsLabel);
            MusicButton = OptionRow(page, "Música", .59f, () => { game.Audio.SetMusic(!game.Audio.MusicOn); game.Audio.Play("select", true); RefreshOptions(); }, out musicLabel);

            var creditsTitle = Label(page, "CRÉDITOS", 28, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AnchorV(creditsTitle.rectTransform, .48f, .52f);
            var credits = Label(page,
                "<b>Colonia 09</b> · versión 0.9.1\nUn juego de <b>Zeitend Interactive</b>\n\nModelos 3D renderizados en Blender.\nSonido sintetizado en tiempo real.\nHecho con Unity.",
                30, Txt, TextAnchor.UpperLeft);
            AnchorV(credits.rectTransform, .2f, .47f);

            OptionsBackButton = TextButton(page, "VOLVER", 40, Cyan, Bg, () => { game.Audio.Play("select", true); ShowOptions(false); });
            AnchorV((RectTransform)OptionsBackButton.transform, .04f, .12f);
            optionsPage.SetActive(false);
        }

        Button OptionRow(RectTransform page, string text, float y, UnityEngine.Events.UnityAction onClick, out Text state)
        {
            var row = Rt("Row", page);
            row.anchorMin = new Vector2(0, y); row.anchorMax = new Vector2(1, y + .085f);
            row.offsetMin = row.offsetMax = Vector2.zero;
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = Panel; bg.raycastTarget = false;
            var label = Label(row, text, 36, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            label.rectTransform.offsetMin = new Vector2(30, 0);
            var button = TextButton(row, "", 32, Cyan, Bg, onClick);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(1, .5f); rt.anchorMax = new Vector2(1, .5f); rt.pivot = new Vector2(1, .5f);
            rt.sizeDelta = new Vector2(260, 92); rt.anchoredPosition = new Vector2(-16, 0);
            state = button.GetComponentInChildren<Text>();
            return button;
        }

        void RefreshOptions()
        {
            SetToggle(EffectsButton, effectsLabel, game.Audio.EffectsOn);
            SetToggle(MusicButton, musicLabel, game.Audio.MusicOn);
        }

        void SetToggle(Button button, Text label, bool on)
        {
            label.text = on ? "ACTIVADO" : "APAGADO";
            label.color = on ? Bg : Muted;
            button.GetComponent<Image>().color = on ? Cyan : PanelLight;
        }

        void ShowOptions(bool show)
        {
            if (show) game.Audio.Play("select", true);
            menuPage.SetActive(!show);
            optionsPage.SetActive(show);
            RefreshOptions();
        }

        public void ShowMainMenu()
        {
            mainMenu.SetActive(true);
            mainMenu.transform.SetAsLastSibling();
            ShowOptions(false);
        }

        public void HideMainMenu() => mainMenu.SetActive(false);

        // ---- Layout -------------------------------------------------------------------

        void ApplySafeArea()
        {
            var s = Screen.safeArea;
            lastSafe = s;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Lets the automated test emulate a notch and a home bar on a desktop window.
            s.yMin += DebugSafeInsetBottom;
            s.yMax -= DebugSafeInsetTop;
#endif
            safeArea.anchorMin = new Vector2(s.xMin / Screen.width, s.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(s.xMax / Screen.width, s.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }

        void UpdateFieldPixels()
        {
            var corners = new Vector3[4];
            field.GetWorldCorners(corners);
            FieldPixels = new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }

        void BuildField()
        {
            field = Rt("Field", safeArea);
            field.anchorMin = Vector2.zero; field.anchorMax = Vector2.one;
            field.offsetMin = new Vector2(8, DockHeight + 6);
            field.offsetMax = new Vector2(-8, -HudHeight - 6);
            var area = field.gameObject.AddComponent<BoardTouchArea>();
            area.Game = game;
            area.raycastTarget = true;
        }

        void BuildHud()
        {
            var hud = Rt("Hud", safeArea);
            hud.anchorMin = new Vector2(0, 1); hud.anchorMax = Vector2.one; hud.pivot = new Vector2(.5f, 1);
            hud.sizeDelta = new Vector2(0, HudHeight);
            hud.anchoredPosition = Vector2.zero;

            // Top row: wave chip, status ticker, audio and menu buttons.
            Icon(hud, "icon_wave", 14, -8, 66);
            waveText = Label(hud, "", 36, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            TopLeft(waveText.rectTransform, 86, -6, 240, 50);
            waveFill = Bar(hud, 88, -60, 220, 12, Cyan);

            statusText = Label(hud, "", 24, Muted, TextAnchor.MiddleLeft);
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText.resizeTextForBestFit = true; statusText.resizeTextMinSize = 14; statusText.resizeTextMaxSize = 24;
            var statusRt = statusText.rectTransform;
            statusRt.anchorMin = new Vector2(0, 1); statusRt.anchorMax = Vector2.one; statusRt.pivot = new Vector2(.5f, 1);
            statusRt.offsetMin = new Vector2(330, -84); statusRt.offsetMax = new Vector2(-206, -4);

            MenuButton = TextButton(hud, "≡", 52, PanelLight, Txt, game.Pause);
            var menuRt = (RectTransform)MenuButton.transform;
            menuRt.anchorMin = menuRt.anchorMax = Vector2.one; menuRt.pivot = Vector2.one;
            menuRt.sizeDelta = new Vector2(86, 72); menuRt.anchoredPosition = new Vector2(-14, -8);
            AudioButton = TextButton(hud, "♪", 46, PanelLight, Cyan, () => { game.Audio.Toggle(); RefreshAudioButton(); });
            audioLabel = AudioButton.GetComponentInChildren<Text>();
            var audioRt = (RectTransform)AudioButton.transform;
            audioRt.anchorMin = audioRt.anchorMax = Vector2.one; audioRt.pivot = Vector2.one;
            audioRt.sizeDelta = new Vector2(86, 72); audioRt.anchoredPosition = new Vector2(-110, -8);
            RefreshAudioButton();

            // Bottom row: base integrity on the left, the energy battery on the right.
            var panel = Rt("Vitals", hud);
            panel.anchorMin = Vector2.zero; panel.anchorMax = new Vector2(1, 0); panel.pivot = new Vector2(.5f, 0);
            panel.sizeDelta = new Vector2(-24, 118);
            panel.anchoredPosition = new Vector2(0, 6);
            var panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.sprite = rounded; panelBg.type = Image.Type.Sliced; panelBg.color = Panel; panelBg.raycastTarget = false;
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Line.r, Line.g, Line.b, .8f); outline.effectDistance = new Vector2(2, -2);

            Icon(panel, "icon_base", 10, -12, 92);
            coreText = Label(panel, "", 50, Red, TextAnchor.MiddleLeft, FontStyle.Bold);
            TopLeft(coreText.rectTransform, 108, -6, 120, 62);
            var baseCaption = Label(panel, "BASE", 20, Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            TopLeft(baseCaption.rectTransform, 196, -20, 120, 36);
            for (int i = 0; i < GameData.CoreMax; i++)
            {
                var pip = Rt("Pip", panel).gameObject.AddComponent<Image>();
                pip.sprite = rounded; pip.type = Image.Type.Sliced; pip.raycastTarget = false;
                TopLeft(pip.rectTransform, 110 + i * 15, -78, 12, 24);
                corePips.Add(pip);
            }

            Icon(panel, "icon_energy", 348, -12, 92);
            var track = Rt("Battery", panel);
            track.anchorMin = new Vector2(0, 1); track.anchorMax = Vector2.one; track.pivot = new Vector2(0, 1);
            track.offsetMin = new Vector2(446, -64); track.offsetMax = new Vector2(-18, -14);
            var trackBg = track.gameObject.AddComponent<Image>();
            trackBg.sprite = rounded; trackBg.type = Image.Type.Sliced; trackBg.color = Html("#071620"); trackBg.raycastTarget = false;
            energyFill = Rt("Charge", track);
            energyFill.anchorMin = Vector2.zero; energyFill.anchorMax = new Vector2(.5f, 1);
            energyFill.offsetMin = new Vector2(3, 3); energyFill.offsetMax = new Vector2(-3, -3);
            energyFillImage = energyFill.gameObject.AddComponent<Image>();
            energyFillImage.sprite = rounded; energyFillImage.type = Image.Type.Sliced; energyFillImage.color = Gold; energyFillImage.raycastTarget = false;
            var sheen = Rt("Sheen", energyFill).gameObject.AddComponent<Image>();
            sheen.sprite = rounded; sheen.type = Image.Type.Sliced; sheen.color = new Color(1, 1, 1, .22f); sheen.raycastTarget = false;
            sheen.rectTransform.anchorMin = new Vector2(0, .55f); sheen.rectTransform.anchorMax = Vector2.one;
            sheen.rectTransform.offsetMin = new Vector2(4, 0); sheen.rectTransform.offsetMax = new Vector2(-4, -3);
            chargeText = Label(track, "", 30, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            var chargeOutline = chargeText.gameObject.AddComponent<Outline>();  // Readable over both the gold fill and the dark track.
            chargeOutline.effectColor = new Color(.03f, .08f, .12f, .95f); chargeOutline.effectDistance = new Vector2(2, -2);

            powerText = Label(panel, "", 22, Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            var powerRt = powerText.rectTransform;
            powerRt.anchorMin = new Vector2(0, 0); powerRt.anchorMax = new Vector2(1, 0); powerRt.pivot = new Vector2(0, 0);
            powerRt.offsetMin = new Vector2(450, 8); powerRt.offsetMax = new Vector2(-20, 44);
            usageText = Label(panel, "", 22, Red, TextAnchor.MiddleRight, FontStyle.Bold);
            var usageRt = usageText.rectTransform;
            usageRt.anchorMin = new Vector2(0, 0); usageRt.anchorMax = new Vector2(1, 0); usageRt.pivot = new Vector2(0, 0);
            usageRt.offsetMin = new Vector2(450, 8); usageRt.offsetMax = new Vector2(-20, 44);
        }

        void Icon(RectTransform parent, string name, float x, float y, float size)
        {
            var image = Rt("Icon", parent).gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("Sprites/UI/" + name);
            image.preserveAspect = true; image.raycastTarget = false;
            TopLeft(image.rectTransform, x, y, size, size);
        }

        RectTransform Bar(RectTransform parent, float x, float y, float width, float height, Color color)
        {
            var track = Rt("Bar", parent).gameObject.AddComponent<Image>();
            track.sprite = rounded; track.type = Image.Type.Sliced; track.color = Html("#0b2232"); track.raycastTarget = false;
            TopLeft(track.rectTransform, x, y, width, height);
            var fill = Rt("Fill", track.transform).gameObject.AddComponent<Image>();
            fill.sprite = rounded; fill.type = Image.Type.Sliced; fill.color = color; fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            return fill.rectTransform;
        }

        static void TopLeft(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }
        void BuildDock()
        {
            var dock = Rt("Dock", safeArea);
            dock.anchorMin = Vector2.zero; dock.anchorMax = new Vector2(1, 0); dock.pivot = new Vector2(.5f, 0);
            dock.sizeDelta = new Vector2(0, DockHeight);
            dock.anchoredPosition = Vector2.zero;
            var bg = dock.gameObject.AddComponent<Image>();
            bg.color = Html("#0b1e2d");

            dockTitle = Label(dock, "SISTEMAS · 8 MÓDULOS", 28, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(dockTitle.rectTransform, new Vector2(24, -66), new Vector2(520, -6), true);
            dockHint = Label(dock, "", 25, Muted, TextAnchor.MiddleRight);
            var hintRt = dockHint.rectTransform;
            hintRt.anchorMin = new Vector2(0, 1); hintRt.anchorMax = Vector2.one; hintRt.pivot = new Vector2(.5f, 1);
            hintRt.offsetMin = new Vector2(520, -66); hintRt.offsetMax = new Vector2(-108, -6);

            CloseButton = TextButton(dock, "×", 48, PanelLight, Txt, game.Deselect);
            closeInspector = CloseButton.gameObject;
            var closeRt = (RectTransform)CloseButton.transform;
            closeRt.anchorMin = closeRt.anchorMax = Vector2.one; closeRt.pivot = Vector2.one;
            closeRt.sizeDelta = new Vector2(80, 60); closeRt.anchoredPosition = new Vector2(-16, -8);

            BuildCards(dock);
            BuildInspector(dock);
        }

        void BuildCards(RectTransform dock)
        {
            var scrollRt = Rt("Cards", dock);
            scrollRt.anchorMin = Vector2.zero; scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(0, 8); scrollRt.offsetMax = new Vector2(0, -72);
            cardsRoot = scrollRt.gameObject;
            var scroll = cardsRoot.AddComponent<ScrollRect>();
            scroll.horizontal = true; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            cardsRoot.AddComponent<RectMask2D>();
            var hit = cardsRoot.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            var content = Rt("Content", scrollRt);
            content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(0, 1); content.pivot = new Vector2(0, .5f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12; layout.padding = new RectOffset(16, 16, 0, 0);
            layout.childControlHeight = true; layout.childForceExpandHeight = true; layout.childControlWidth = false; layout.childForceExpandWidth = false;
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = scrollRt;

            foreach (var type in GameData.ModuleTypes)
            {
                var def = GameData.Modules[type];
                var card = Rt("Card_" + type, content);
                card.sizeDelta = new Vector2(204, 0);
                var cardImage = card.gameObject.AddComponent<Image>();
                cardImage.sprite = bevel; cardImage.type = Image.Type.Sliced; cardImage.color = Panel;
                var button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = cardImage;
                var captured = type;
                button.onClick.AddListener(() => game.SelectType(captured));
                cardBg[type] = cardImage;
                CardButtons[type] = button;
                cardGroup[type] = card.gameObject.AddComponent<CanvasGroup>();

                var icon = Rt("Icon", card).gameObject.AddComponent<Image>();
                icon.sprite = game.Board.ModuleSprite(type); icon.preserveAspect = true; icon.raycastTarget = false;
                icon.rectTransform.anchorMin = new Vector2(.08f, .38f); icon.rectTransform.anchorMax = new Vector2(.92f, .98f);
                icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
                var name = Label(card, def.Name, 28, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
                name.rectTransform.anchorMin = new Vector2(0, .19f); name.rectTransform.anchorMax = new Vector2(1, .39f);
                name.rectTransform.offsetMin = name.rectTransform.offsetMax = Vector2.zero;
                var price = Label(card, $"{def.Cost}  <size=22><color=#99b5c7>ENERGÍA</color></size>", 30, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                price.rectTransform.anchorMin = new Vector2(0, .02f); price.rectTransform.anchorMax = new Vector2(1, .2f);
                price.rectTransform.offsetMin = price.rectTransform.offsetMax = Vector2.zero;
            }
        }

        void BuildInspector(RectTransform dock)
        {
            var root = Rt("Inspector", dock);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = new Vector2(16, 12); root.offsetMax = new Vector2(-16, -72);
            inspector = root.gameObject;

            var iconFrame = Rt("IconFrame", root).gameObject.AddComponent<Image>();
            iconFrame.sprite = bevel; iconFrame.type = Image.Type.Sliced; iconFrame.color = PanelLight; iconFrame.raycastTarget = false;
            var frameRt = iconFrame.rectTransform;
            frameRt.anchorMin = new Vector2(0, .3f); frameRt.anchorMax = new Vector2(0, 1); frameRt.pivot = new Vector2(0, .5f);
            frameRt.sizeDelta = new Vector2(168, 0); frameRt.anchoredPosition = Vector2.zero;
            inspectIcon = Rt("Icon", frameRt).gameObject.AddComponent<Image>();
            inspectIcon.preserveAspect = true; inspectIcon.raycastTarget = false;
            Stretch(inspectIcon.rectTransform);
            inspectIcon.rectTransform.offsetMin = new Vector2(10, 10); inspectIcon.rectTransform.offsetMax = new Vector2(-10, -10);

            inspectName = Label(root, "", 34, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            TopLeft(inspectName.rectTransform, 186, 0, 360, 46);
            for (int i = 0; i < 3; i++)  // Level marks.
            {
                var pip = Rt("Level", root).gameObject.AddComponent<Image>();
                pip.sprite = rounded; pip.type = Image.Type.Sliced; pip.raycastTarget = false;
                pip.rectTransform.anchorMin = pip.rectTransform.anchorMax = new Vector2(0, 1); pip.rectTransform.pivot = new Vector2(0, 1);
                pip.rectTransform.sizeDelta = new Vector2(26, 14); pip.rectTransform.anchoredPosition = new Vector2(186 + i * 32, -54);
                levelPips.Add(pip);
            }
            inspectStatusBg = Rt("Status", root).gameObject.AddComponent<Image>();
            inspectStatusBg.sprite = rounded; inspectStatusBg.type = Image.Type.Sliced; inspectStatusBg.raycastTarget = false;
            var statusRt = inspectStatusBg.rectTransform;
            statusRt.anchorMin = statusRt.anchorMax = Vector2.one; statusRt.pivot = Vector2.one;
            statusRt.sizeDelta = new Vector2(300, 40); statusRt.anchoredPosition = new Vector2(0, -2);
            inspectStatus = Label(statusRt, "", 22, Bg, TextAnchor.MiddleCenter, FontStyle.Bold);
            inspectStatus.resizeTextForBestFit = true; inspectStatus.resizeTextMinSize = 12; inspectStatus.resizeTextMaxSize = 22;
            inspectStatus.rectTransform.offsetMin = new Vector2(10, 0); inspectStatus.rectTransform.offsetMax = new Vector2(-10, 0);

            var hpTrack = Rt("Health", root).gameObject.AddComponent<Image>();
            hpTrack.sprite = rounded; hpTrack.type = Image.Type.Sliced; hpTrack.color = Html("#071620"); hpTrack.raycastTarget = false;
            var hpRt = hpTrack.rectTransform;
            hpRt.anchorMin = new Vector2(0, 1); hpRt.anchorMax = Vector2.one; hpRt.pivot = new Vector2(0, 1);
            hpRt.offsetMin = new Vector2(310, -80); hpRt.offsetMax = new Vector2(0, -52);
            var hpFill = Rt("Fill", hpRt).gameObject.AddComponent<Image>();
            hpFill.sprite = rounded; hpFill.type = Image.Type.Sliced; hpFill.raycastTarget = false;
            inspectHpFillImage = hpFill;
            inspectHpFill = hpFill.rectTransform;
            inspectHpFill.anchorMin = Vector2.zero; inspectHpFill.offsetMin = new Vector2(3, 3); inspectHpFill.offsetMax = new Vector2(-3, -3);
            inspectHp = Label(hpRt, "", 19, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            var hpOutline = inspectHp.gameObject.AddComponent<Outline>();
            hpOutline.effectColor = new Color(.03f, .08f, .12f, .95f);

            inspectDetail = Label(root, "", 24, Muted, TextAnchor.UpperLeft);
            inspectDetail.rectTransform.anchorMin = new Vector2(0, .3f); inspectDetail.rectTransform.anchorMax = new Vector2(1, 1);
            inspectDetail.rectTransform.offsetMin = new Vector2(186, 6); inspectDetail.rectTransform.offsetMax = new Vector2(0, -90);
            inspectDetail.resizeTextForBestFit = true; inspectDetail.resizeTextMinSize = 16; inspectDetail.resizeTextMaxSize = 24;

            upgradeButton = ActionButton(root, "MEJORAR", Cyan, Bg, "icon_upgrade", game.UpgradeSelected, out upgradeLabel, out upgradeCost, out upgradeChip);
            var upRt = (RectTransform)upgradeButton.transform;
            upRt.anchorMin = new Vector2(0, 0); upRt.anchorMax = new Vector2(.6f, .27f);
            upRt.offsetMin = upRt.offsetMax = Vector2.zero;
            SellButton = ActionButton(root, "RECICLAR", PanelLight, Txt, "icon_recycle", game.RecycleSelected, out sellLabel, out sellCost, out _);
            var sellRt = (RectTransform)SellButton.transform;
            sellRt.anchorMin = new Vector2(.62f, 0); sellRt.anchorMax = new Vector2(1, .27f);
            sellRt.offsetMin = sellRt.offsetMax = Vector2.zero;
            inspector.SetActive(false);
        }

        /// <summary>Raised button with an icon, a label and a cost chip with the energy bolt.</summary>
        Button ActionButton(Transform parent, string text, Color bg, Color fg, string icon, UnityEngine.Events.UnityAction onClick,
            out Text label, out Text cost, out GameObject chip)
        {
            var button = TextButton(parent, text, 30, bg, fg, onClick);
            var rt = (RectTransform)button.transform;
            label = button.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(78, 2); label.rectTransform.offsetMax = new Vector2(-130, -2);
            var image = Rt("Icon", rt).gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("Sprites/UI/" + icon); image.preserveAspect = true; image.raycastTarget = false;
            image.rectTransform.anchorMin = new Vector2(0, .5f); image.rectTransform.anchorMax = new Vector2(0, .5f); image.rectTransform.pivot = new Vector2(0, .5f);
            image.rectTransform.sizeDelta = new Vector2(56, 56); image.rectTransform.anchoredPosition = new Vector2(14, 0);
            var chipImage = Rt("Chip", rt).gameObject.AddComponent<Image>();
            chipImage.sprite = rounded; chipImage.type = Image.Type.Sliced; chipImage.color = new Color(.02f, .07f, .1f, .78f); chipImage.raycastTarget = false;
            var chipRt = chipImage.rectTransform;
            chipRt.anchorMin = new Vector2(1, .5f); chipRt.anchorMax = new Vector2(1, .5f); chipRt.pivot = new Vector2(1, .5f);
            chipRt.sizeDelta = new Vector2(118, 48); chipRt.anchoredPosition = new Vector2(-12, 0);
            var bolt = Rt("Bolt", chipRt).gameObject.AddComponent<Image>();
            bolt.sprite = Resources.Load<Sprite>("Sprites/UI/icon_energy"); bolt.preserveAspect = true; bolt.raycastTarget = false;
            bolt.rectTransform.anchorMin = bolt.rectTransform.anchorMax = new Vector2(0, .5f); bolt.rectTransform.pivot = new Vector2(0, .5f);
            bolt.rectTransform.sizeDelta = new Vector2(36, 36); bolt.rectTransform.anchoredPosition = new Vector2(8, 0);
            cost = Label(chipRt, "", 28, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            cost.rectTransform.offsetMin = new Vector2(40, 0); cost.rectTransform.offsetMax = new Vector2(-6, 0);
            chip = chipImage.gameObject;
            return button;
        }
        void BuildToast()
        {
            var toast = Rt("Toast", safeArea);
            // Over the base row, where nothing can be built, so it never hides the enemies coming in.
            toast.anchorMin = new Vector2(.05f, 0); toast.anchorMax = new Vector2(.95f, 0); toast.pivot = new Vector2(.5f, 0);
            toast.sizeDelta = new Vector2(0, 88);
            toast.anchoredPosition = new Vector2(0, DockHeight + 14);
            toastBg = toast.gameObject.AddComponent<Image>();
            toastBg.sprite = rounded; toastBg.type = Image.Type.Sliced; toastBg.color = new Color(.03f, .1f, .16f, .93f); toastBg.raycastTarget = false;
            toastText = Label(toast, "", 30, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            toastText.rectTransform.anchorMin = Vector2.zero; toastText.rectTransform.anchorMax = Vector2.one;
            toastText.rectTransform.offsetMin = new Vector2(16, 4); toastText.rectTransform.offsetMax = new Vector2(-16, -4);
            toastText.resizeTextForBestFit = true; toastText.resizeTextMinSize = 18; toastText.resizeTextMaxSize = 30;
            toast.gameObject.SetActive(false);
        }

        void BuildModal()
        {
            var root = Rt("Modal", safeArea.parent);
            Stretch(root);
            modal = root.gameObject;
            root.gameObject.AddComponent<Image>().color = Html("#06121f");

            var panel = Rt("Panel", root);
            panel.anchorMin = new Vector2(.04f, .04f); panel.anchorMax = new Vector2(.96f, .96f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;

            modalKicker = Label(panel, "", 28, Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            AnchorV(modalKicker.rectTransform, .94f, 1f);
            modalTitle = Label(panel, "", 104, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            modalTitle.resizeTextForBestFit = true; modalTitle.resizeTextMinSize = 40; modalTitle.resizeTextMaxSize = 104;
            AnchorV(modalTitle.rectTransform, .84f, .94f);
            modalLead = Label(panel, "", 33, Muted, TextAnchor.UpperCenter);
            modalLead.resizeTextForBestFit = true; modalLead.resizeTextMinSize = 22; modalLead.resizeTextMaxSize = 33;
            AnchorV(modalLead.rectTransform, .745f, .835f);

            var row = Rt("Difficulty", panel);
            row.anchorMin = new Vector2(0, .675f); row.anchorMax = new Vector2(1, .74f);
            row.offsetMin = row.offsetMax = Vector2.zero;
            difficultyRow = row.gameObject;
            var layout = difficultyRow.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12; layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = true; layout.childForceExpandWidth = true;
            foreach (Difficulty d in new[] { Difficulty.Explore, Difficulty.Tactical, Difficulty.Extreme })
            {
                var captured = d;
                var b = TextButton(row, GameData.Difficulties[d].Label, 32, Panel, Txt, () => { chosenDifficulty = captured; RefreshDifficulty(); });
                difficultyButtons.Add(b);
            }

            modalInfo = Label(panel, "", 30, Txt, TextAnchor.UpperLeft);
            modalInfo.resizeTextForBestFit = true; modalInfo.resizeTextMinSize = 19; modalInfo.resizeTextMaxSize = 31;
            AnchorV(modalInfo.rectTransform, .20f, .665f);

            PrimaryButton = TextButton(panel, "", 40, Cyan, Bg, OnPrimary);
            primaryLabel = PrimaryButton.GetComponentInChildren<Text>();
            AnchorV((RectTransform)PrimaryButton.transform, .105f, .185f);
            SecondaryButton = TextButton(panel, "", 32, PanelLight, Txt, OnSecondary);
            secondaryLabel = SecondaryButton.GetComponentInChildren<Text>();
            secondaryButton = SecondaryButton.gameObject;
            AnchorV((RectTransform)SecondaryButton.transform, .04f, .095f);
            modalFooter = Label(panel, "", 24, Muted, TextAnchor.MiddleCenter);
            AnchorV(modalFooter.rectTransform, 0, .04f);
        }

        static void AnchorV(RectTransform rt, float min, float max)
        {
            rt.anchorMin = new Vector2(0, min); rt.anchorMax = new Vector2(1, max);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void RefreshDifficulty()
        {
            var order = new[] { Difficulty.Explore, Difficulty.Tactical, Difficulty.Extreme };
            for (int i = 0; i < difficultyButtons.Count; i++)
            {
                bool on = order[i] == chosenDifficulty;
                difficultyButtons[i].GetComponent<Image>().color = on ? Gold : Panel;
                difficultyButtons[i].GetComponentInChildren<Text>().color = on ? Bg : Txt;
            }
        }

        public void ChooseDifficulty(Difficulty d) { chosenDifficulty = d; RefreshDifficulty(); }

        void OnPrimary()
        {
            switch (modalKind)
            {
                case ModalKind.Intro: game.StartGame(chosenDifficulty); break;
                case ModalKind.Pause: game.Resume(); break;
                default: game.ReturnToMenu(); break;
            }
        }

        void OnSecondary()
        {
            // Pause restarts Quick Play from its setup screen; every other screen goes back to the main menu.
            if (modalKind == ModalKind.Pause) game.ReturnToMenu();
            else game.ShowMainMenu();
        }

        // ---- Modal content ------------------------------------------------------------

        const string RulesIntro =
            "<b><color=#86e6ee>01</color></b>  Empiezas con un <b>Núcleo central</b> y energía inicial. Los primeros enemigos también llegan por los flancos. Coloca un <b>Generador</b> a dos casillas o menos para recargarlo. Sus niveles 2 y 3 se desbloquean en las oleadas 3 y 6.\n\n" +
            "<b><color=#86e6ee>02</color></b>  Una sola <b>batería</b> sirve para construir, mejorar y atacar. Los <b>Relés</b> extienden la conexión de las defensas, no de los Generadores.\n\n" +
            "<b><color=#86e6ee>03</color></b>  Nueve oleadas: el Cazador ataca Talleres y Criogénicos desde la ola 3; tanques y helicópteros alternan objetivos; el bombardero marca hasta cuatro casillas. Sus ataques siempre se avisan.\n\n" +
            "<b><color=#86e6ee>04</color></b>  <b>Coraza</b> resiste Pulso, <b>Térmico</b> resiste Criogénico y <b>Aislante</b> resiste Tesla. Combina armas.\n\n" +
            "<b><color=#86e6ee>05</color></b>  Toca piezas instaladas para <b>mejorarlas o reciclarlas</b>. Los módulos tardan 1–4 segundos en montarse y los Talleres cercanos lo aceleran.";

        const string RulesPause =
            "<b>Generador:</b> produce. <b>Núcleo:</b> almacena y entrega la única energía del juego. <b>Relé:</b> extiende la conexión dos casillas.\n\n" +
            "<b>Especialistas (marcas P/C/T):</b> Coraza resiste Pulso, usa Criogénico. Térmico resiste frío, usa Tesla. Aislante resiste Tesla, usa Pulso. Sus ataques se anuncian antes de ocurrir.\n\n" +
            "Mejora tus módulos hasta nivel 3 para especializar la defensa.";

        public void ShowModal(ModalKind kind)
        {
            modalKind = kind;
            modal.SetActive(true);
            var sim = game.Sim;
            bool intro = kind == ModalKind.Intro, pause = kind == ModalKind.Pause, win = kind == ModalKind.Win;
            modalKicker.text = intro ? "ZEITEND INTERACTIVE · VERSIÓN UNITY" : pause ? "CENTRO DE CONTROL" : win ? "MISIÓN COMPLETADA" : "FALLO EN EL NÚCLEO";
            modalTitle.text = intro ? "COLONIA 09" : pause ? "Simulación pausada" : win ? "Colonia protegida" : "La colonia cayó";
            modalLead.text = intro ? "Cada pieza tiene una función. Cada conexión cambia el resultado."
                : pause ? "El Generador se conecta directamente al Núcleo, nunca mediante Relés."
                : win ? $"Detuviste {sim.TotalKills} amenazas con {Mathf.CeilToInt((float)sim.Core)} puntos de integridad restantes."
                : $"Llegaste hasta la oleada {sim.Wave}. Revisa tu suministro y los puntos débiles de la red.";
            difficultyRow.SetActive(intro);
            modalInfo.alignment = intro || pause ? TextAnchor.UpperLeft : TextAnchor.UpperCenter;
            modalInfo.text = intro ? RulesIntro : pause ? RulesPause
                : $"Enemigos detenidos: <b>{sim.TotalKills}</b>\nOleada: <b>{sim.Wave}/{sim.MaxWave}</b>\nDificultad: <b>{GameData.Difficulties[sim.Difficulty].Label}</b>";
            primaryLabel.text = intro ? "INICIAR DEFENSA" : pause ? "CONTINUAR PARTIDA" : "VOLVER A JUGAR";
            secondaryButton.SetActive(true);
            secondaryLabel.text = pause ? "REINICIAR SIMULACIÓN" : intro ? "VOLVER AL MENÚ" : "MENÚ PRINCIPAL";
            modalFooter.text = intro ? "7 × 11 · 9 oleadas · economía gradual" : pause ? "Partida en pausa" : "Puedes seleccionar otra dificultad en la siguiente partida";
            RefreshDifficulty();
        }

        public void HideModal() => modal.SetActive(false);

        // ---- Per-frame refresh --------------------------------------------------------

        public void MarkDeckDirty() => deckDirty = true;

        public void ShowToast(string message, float seconds)
        {
            toastText.text = Safe(message);
            toastUntil = Time.unscaledTime + seconds;
            toastBg.gameObject.SetActive(true);
        }

        public void Refresh()
        {
            if (Screen.safeArea != lastSafe) ApplySafeArea();
            UpdateFieldPixels();
            for (int i = labelsUsed; i < labels.Count; i++)
                if (labels[i].enabled) labels[i].enabled = false;
            labelsUsed = 0;

            var sim = game.Sim;
            waveText.text = $"OLA {sim.Wave}<size=26><color=#99b5c7>/{sim.MaxWave}</color></size>";
            float waveDone = sim.WaveTotal > 0 && sim.WaveBreak <= 0 ? 1 - (sim.SpawnRemaining + sim.Enemies.Count) / (float)sim.WaveTotal : 1;
            waveFill.anchorMax = new Vector2(Mathf.Clamp01(waveDone), 1);

            int core = Mathf.CeilToInt((float)sim.Core);
            coreText.text = core.ToString();
            for (int i = 0; i < corePips.Count; i++)
            {
                bool shown = i < sim.Level.CoreMax;
                if (corePips[i].enabled != shown) corePips[i].enabled = shown;
                corePips[i].color = i < core ? (core <= 5 ? Color.Lerp(Red, Color.white, Mathf.PingPong(Time.unscaledTime * 2, .35f)) : Red) : Html("#2a1d22");
            }

            float capacity = Mathf.Max(1, (float)sim.EnergyCapacity);
            float fraction = Mathf.Clamp01((float)sim.Charge / capacity);
            energyFill.anchorMax = new Vector2(Mathf.Max(.02f, fraction), 1);
            bool low = sim.Charge < GameSimulation.BuildCost(ModuleType.Relay);
            energyFillImage.color = low ? Color.Lerp(Red, Gold, Mathf.PingPong(Time.unscaledTime * 2, 1)) : Gold;
            chargeText.text = $"{Mathf.FloorToInt((float)sim.Charge)} <size=22>/ {sim.EnergyCapacity:0}</size>";
            powerText.text = $"+{sim.ChargingGeneration:0.0}/s  PRODUCCIÓN";
            usageText.text = sim.ConsumptionRate > .05 ? $"GASTO  −{sim.ConsumptionRate:0.0}/s" : "";
            statusText.text = Safe(sim.StatusText());
            statusText.color = statusText.text.StartsWith("!") ? Red : Muted;
            if (toastBg.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastBg.gameObject.SetActive(false);

            var selected = game.Selected;
            if (deckDirty)
            {
                deckDirty = false;
                inspector.SetActive(selected != null);
                cardsRoot.SetActive(selected == null);
                closeInspector.SetActive(selected != null);
                dockTitle.text = selected != null ? "MÓDULO SELECCIONADO" : $"SISTEMAS · {sim.Level.Modules.Length} MÓDULOS";
                foreach (var type in GameData.ModuleTypes)
                {
                    bool available = sim.Level.Allows(type);  // Campaign missions unlock modules a few at a time.
                    if (CardButtons[type].gameObject.activeSelf != available) CardButtons[type].gameObject.SetActive(available);
                }
                dockHint.text = selected != null ? "Mejora o recicla el módulo" : "Elige módulo y toca una casilla";
                if (selected != null) inspectIcon.sprite = game.Board.ModuleSprite(selected.Type);
            }
            foreach (var type in GameData.ModuleTypes)
            {
                cardBg[type].color = type == game.SelectedType ? Html("#23506a") : Panel;
                cardGroup[type].alpha = sim.Charge < GameSimulation.BuildCost(type) ? .5f : 1f;
            }
            if (selected != null)
            {
                var def = GameData.Modules[selected.Type];
                inspectName.text = def.Name;
                for (int i = 0; i < levelPips.Count; i++) levelPips[i].color = i < selected.Level ? Html(def.Color) : Html("#24384a");

                string status = Safe(sim.ModuleStatus(selected));
                inspectStatus.text = status;
                inspectStatusBg.color = status.StartsWith("OPERATIVO") || status.StartsWith("AUTÓNOMA") ? Green
                    : status.StartsWith("EN MONTAJE") ? Cyan : status.StartsWith("EMP") ? Html("#c9a2ff") : Gold;
                if (status.StartsWith("SIN") || status.StartsWith("MONTAJE INTERRUMPIDO") || status.StartsWith("REPARACIÓN")) inspectStatusBg.color = Red;

                float hp = Mathf.Clamp01((float)(selected.Hp / selected.MaxHp));
                inspectHpFill.anchorMax = new Vector2(Mathf.Max(.03f, hp), 1);
                inspectHpFillImage.color = hp > .5f ? Green : hp > .25f ? Gold : Red;
                inspectHp.text = $"{Mathf.Ceil((float)selected.Hp)} / {Mathf.Ceil((float)selected.MaxHp)} PV";

                var detail = Safe(sim.ModuleDetail(selected)).Split(new[] { " · " }, System.StringSplitOptions.None);
                inspectDetail.text = string.Join("\n", System.Array.FindAll(detail, d => !d.EndsWith(" PV")));

                bool canUpgrade = sim.CanUpgrade(selected);
                bool upgradeOpen = !selected.IsDeploying && selected.Level < 3 && sim.Wave >= sim.UpgradeUnlock(selected);
                upgradeLabel.text = upgradeOpen ? "MEJORAR" : Safe(sim.UpgradeLabel(selected));
                upgradeChip.SetActive(upgradeOpen);
                upgradeCost.text = sim.UpgradeCost(selected).ToString();
                upgradeCost.color = sim.Charge >= sim.UpgradeCost(selected) ? Gold : Red;
                upgradeButton.interactable = canUpgrade;
                sellLabel.text = "RECICLAR";
                sellCost.text = $"+{sim.RecycleValue(selected)}";
            }
        }

        /// <summary>Draws a one-frame label at a board position. The board calls this while rendering.</summary>
        public void WorldLabel(string text, Color color, double x, double row, int size)
        {
            Text label;
            if (labelsUsed < labels.Count) label = labels[labelsUsed];
            else
            {
                label = Label(labelRoot, "", size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.rectTransform.sizeDelta = new Vector2(10, 10);
                var shadow = label.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, .8f);
                labels.Add(label);
            }
            labelsUsed++;
            label.enabled = true;
            label.text = text;
            label.color = color;
            label.fontSize = size;
            label.rectTransform.position = game.Board.WorldToScreen(x, row);
        }

        // ---- UI helpers ---------------------------------------------------------------

        static RectTransform Rt(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform rt, Vector2 min, Vector2 max, bool fromTopLeft)
        {
            rt.anchorMin = rt.anchorMax = fromTopLeft ? new Vector2(0, 1) : Vector2.zero;
            rt.pivot = new Vector2(0, 1);
            rt.offsetMin = min;
            rt.offsetMax = max;
        }

        Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor, FontStyle style = FontStyle.Normal)
        {
            var t = Rt("Text", parent).gameObject.AddComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = anchor; t.fontStyle = style;
            t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(t.rectTransform);
            return t;
        }

        Button TextButton(Transform parent, string text, int size, Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
        {
            var rt = Rt("Button", parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = bevel; image.type = Image.Type.Sliced; image.color = bg;
            var shade = rt.gameObject.AddComponent<Shadow>();  // Drop shadow gives the button depth.
            shade.effectColor = new Color(0, 0, 0, .55f); shade.effectDistance = new Vector2(0, -5);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var colors = button.colors;
            colors.disabledColor = new Color(.5f, .5f, .5f, .55f);
            button.colors = colors;
            var label = Label(rt, text, size, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 16; label.resizeTextMaxSize = size;
            label.rectTransform.offsetMin = new Vector2(8, 2); label.rectTransform.offsetMax = new Vector2(-8, -2);
            return button;
        }

        /// <summary>Rounded button face: light top edge, soft vertical gradient and a darker bottom lip.</summary>
        static Sprite MakeBevel()
        {
            const int Size = 64, Radius = 18;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = Mathf.Max(Radius - x - .5f, x + .5f - (Size - Radius), 0);
                    float dy = Mathf.Max(Radius - y - .5f, y + .5f - (Size - Radius), 0);
                    float distance = Radius - Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(distance + .5f);
                    float shade = Mathf.Lerp(.74f, 1f, y / (float)(Size - 1));
                    if (y < 5) shade *= .62f;                              // Bottom lip.
                    if (y > Size - 6 && distance > 1.5f) shade = 1.18f;   // Top highlight.
                    if (distance < 2.5f) shade *= .8f;                     // Rim.
                    byte v = (byte)Mathf.Clamp(shade * 216, 0, 255);
                    px[y * Size + x] = new Color32(v, v, v, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(Radius, Radius, Radius, Radius));
        }

        static Sprite MakeRounded()
        {
            const int Size = 64, Radius = 18;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = Mathf.Max(Radius - x - .5f, x + .5f - (Size - Radius), 0);
                    float dy = Mathf.Max(Radius - y - .5f, y + .5f - (Size - Radius), 0);
                    float alpha = Mathf.Clamp01(Radius - Mathf.Sqrt(dx * dx + dy * dy) + .5f);
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(Radius, Radius, Radius, Radius));
        }
    }
}
