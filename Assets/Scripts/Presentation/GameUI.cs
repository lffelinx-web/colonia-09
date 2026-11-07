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
        const float RefWidth = 1080, HudHeight = 226, DockHeight = 352;

        static readonly Color Bg = Html("#071622"), Panel = Html("#102638"), PanelLight = Html("#173449"), Line = Html("#35566a"),
            Cyan = Html("#86e6ee"), Txt = Html("#eaf9fb"), Muted = Html("#99b5c7"), Gold = Html("#ffd38b"), Red = Html("#ff8c8a"), Green = Html("#91e5bd");

        GameController game;
        Font font;
        Sprite rounded;
        RectTransform safeArea, field;
        Text waveText, coreText, chargeText, capacityText, powerText, statusText, toastText, dockTitle, dockHint;
        Text modalKicker, modalTitle, modalLead, modalInfo, modalFooter, primaryLabel, secondaryLabel;
        Text inspectName, inspectDetail, upgradeLabel, sellLabel;
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
            ApplySafeArea();
        }

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

            var brand = Label(hud, "COLONIA <color=#86e6ee>09</color>", 42, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(brand.rectTransform, new Vector2(24, -88), new Vector2(330, 0), true);
            statusText = Label(hud, "", 25, Muted, TextAnchor.MiddleLeft);
            statusText.resizeTextForBestFit = true; statusText.resizeTextMinSize = 16; statusText.resizeTextMaxSize = 25;
            var statusRt = statusText.rectTransform;
            statusRt.anchorMin = new Vector2(0, 1); statusRt.anchorMax = Vector2.one; statusRt.pivot = new Vector2(.5f, 1);
            statusRt.offsetMin = new Vector2(345, -88); statusRt.offsetMax = new Vector2(-212, 0);

            MenuButton = TextButton(hud, "≡", 52, PanelLight, Txt, game.Pause);
            var menuRt = (RectTransform)MenuButton.transform;
            menuRt.anchorMin = menuRt.anchorMax = Vector2.one; menuRt.pivot = Vector2.one;
            menuRt.sizeDelta = new Vector2(88, 74); menuRt.anchoredPosition = new Vector2(-14, -8);
            AudioButton = TextButton(hud, "♪", 46, PanelLight, Cyan, () => { game.Audio.Toggle(); RefreshAudioButton(); });
            audioLabel = AudioButton.GetComponentInChildren<Text>();
            var audioRt = (RectTransform)AudioButton.transform;
            audioRt.anchorMin = audioRt.anchorMax = Vector2.one; audioRt.pivot = Vector2.one;
            audioRt.sizeDelta = new Vector2(88, 74); audioRt.anchoredPosition = new Vector2(-114, -8);
            RefreshAudioButton();

            var row = Rt("Stats", hud);
            row.anchorMin = Vector2.zero; row.anchorMax = new Vector2(1, 0); row.pivot = new Vector2(.5f, 0);
            row.sizeDelta = new Vector2(-28, 128);
            row.anchoredPosition = new Vector2(0, 8);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12; layout.childForceExpandHeight = true; layout.childForceExpandWidth = true;
            layout.childControlHeight = true; layout.childControlWidth = true;

            waveText = StatBox(row, "OLA", Cyan, out _);
            coreText = StatBox(row, "BASE", Red, out _);
            chargeText = StatBox(row, "/230 MÁX.", Gold, out capacityText);
            powerText = StatBox(row, "ENERGÍA /SEG.", Green, out _);
        }

        Text StatBox(RectTransform parent, string caption, Color accent, out Text captionText)
        {
            var box = Rt("Stat", parent);
            var bg = box.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = Panel; bg.raycastTarget = false;
            var value = Label(box, "", 54, accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            value.resizeTextForBestFit = true; value.resizeTextMinSize = 28; value.resizeTextMaxSize = 54;
            value.rectTransform.anchorMin = new Vector2(0, .34f); value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = new Vector2(6, 0); value.rectTransform.offsetMax = new Vector2(-6, -4);
            captionText = Label(box, caption, 24, Muted, TextAnchor.MiddleCenter);
            captionText.rectTransform.anchorMin = Vector2.zero; captionText.rectTransform.anchorMax = new Vector2(1, .36f);
            captionText.rectTransform.offsetMin = new Vector2(4, 4); captionText.rectTransform.offsetMax = new Vector2(-4, 0);
            return value;
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
                cardImage.sprite = rounded; cardImage.type = Image.Type.Sliced; cardImage.color = Panel;
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

            inspectIcon = Rt("Icon", root).gameObject.AddComponent<Image>();
            inspectIcon.preserveAspect = true; inspectIcon.raycastTarget = false;
            var iconRt = inspectIcon.rectTransform;
            iconRt.anchorMin = new Vector2(0, .22f); iconRt.anchorMax = new Vector2(0, 1); iconRt.pivot = new Vector2(0, .5f);
            iconRt.sizeDelta = new Vector2(190, 0); iconRt.anchoredPosition = Vector2.zero;

            inspectName = Label(root, "", 31, Txt, TextAnchor.UpperLeft, FontStyle.Bold);
            inspectName.rectTransform.anchorMin = new Vector2(0, .68f); inspectName.rectTransform.anchorMax = Vector2.one;
            inspectName.rectTransform.offsetMin = new Vector2(205, 0); inspectName.rectTransform.offsetMax = Vector2.zero;
            inspectName.resizeTextForBestFit = true; inspectName.resizeTextMinSize = 20; inspectName.resizeTextMaxSize = 31;
            inspectDetail = Label(root, "", 27, Muted, TextAnchor.UpperLeft);
            inspectDetail.rectTransform.anchorMin = new Vector2(0, .3f); inspectDetail.rectTransform.anchorMax = new Vector2(1, .68f);
            inspectDetail.rectTransform.offsetMin = new Vector2(205, 0); inspectDetail.rectTransform.offsetMax = Vector2.zero;
            inspectDetail.resizeTextForBestFit = true; inspectDetail.resizeTextMinSize = 18; inspectDetail.resizeTextMaxSize = 27;

            upgradeButton = TextButton(root, "MEJORAR", 30, Cyan, Bg, game.UpgradeSelected);
            upgradeLabel = upgradeButton.GetComponentInChildren<Text>();
            var upRt = (RectTransform)upgradeButton.transform;
            upRt.anchorMin = new Vector2(0, 0); upRt.anchorMax = new Vector2(.62f, .27f);
            upRt.offsetMin = upRt.offsetMax = Vector2.zero;
            SellButton = TextButton(root, "RECICLAR", 30, PanelLight, Txt, game.RecycleSelected);
            sellLabel = SellButton.GetComponentInChildren<Text>();
            var sellRt = (RectTransform)SellButton.transform;
            sellRt.anchorMin = new Vector2(.64f, 0); sellRt.anchorMax = new Vector2(1, .27f);
            sellRt.offsetMin = sellRt.offsetMax = Vector2.zero;
            inspector.SetActive(false);
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
            SecondaryButton = TextButton(panel, "", 32, PanelLight, Txt, game.ReturnToMenu);
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
                : $"Enemigos detenidos: <b>{sim.TotalKills}</b>\nOleada: <b>{sim.Wave}/{GameData.MaxWave}</b>\nDificultad: <b>{GameData.Difficulties[sim.Difficulty].Label}</b>";
            primaryLabel.text = intro ? "INICIAR DEFENSA" : pause ? "CONTINUAR PARTIDA" : "VOLVER A JUGAR";
            secondaryButton.SetActive(pause);
            secondaryLabel.text = "REINICIAR SIMULACIÓN";
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
            waveText.text = $"{sim.Wave}/{GameData.MaxWave}";
            coreText.text = Mathf.CeilToInt((float)sim.Core).ToString();
            chargeText.text = Mathf.FloorToInt((float)sim.Charge).ToString();
            capacityText.text = $"ENERGÍA /{sim.EnergyCapacity:0}";
            powerText.text = $"+{sim.ChargingGeneration:0.0}";
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
                dockTitle.text = selected != null ? "MÓDULO SELECCIONADO" : "SISTEMAS · 8 MÓDULOS";
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
                inspectName.text = Safe($"{GameData.Modules[selected.Type].Name} · NIVEL {selected.Level} · {sim.ModuleStatus(selected)}");
                inspectDetail.text = Safe(sim.ModuleDetail(selected));
                upgradeLabel.text = Safe(sim.UpgradeLabel(selected));
                upgradeButton.interactable = sim.CanUpgrade(selected);
                sellLabel.text = $"RECICLAR +{sim.RecycleValue(selected)}";
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
            image.sprite = rounded; image.type = Image.Type.Sliced; image.color = bg;
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
