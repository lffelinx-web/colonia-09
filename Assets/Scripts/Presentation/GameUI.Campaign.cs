using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Colonia09.Presentation
{
    /// <summary>Campaign screens: the colony map, the mission briefing, the tutorial coach and the result stars.</summary>
    public sealed partial class GameUI
    {
        GameObject campaignMap, briefing, coach, coachButtonGo, modalStarsRoot;
        RectTransform mapContent, mapViewport;
        ScrollRect mapScroll;
        Text mapStarsText, briefKicker, briefTitle, briefText, briefFacts, coachText;
        RectTransform briefNewRow;
        readonly List<Image> briefStars = new List<Image>();
        readonly List<Image> modalStars = new List<Image>();
        Image modalArt;
        Sprite starSprite, discSprite, ringSprite, frameSprite;
        CampaignSector briefed;
        EnemyKind introKind;
        TutorialStep coachStep;
        readonly Dictionary<ModuleType, Image> cardHighlights = new Dictionary<ModuleType, Image>();

        sealed class MapNode
        {
            public CampaignSector Sector;
            public Button Button;
            public Image Lock, Ring;
            public Text Caption, State;
            public readonly List<Image> Stars = new List<Image>();
        }
        readonly List<MapNode> mapNodes = new List<MapNode>();

        public bool MapOpen => campaignMap.activeSelf;
        public bool BriefingOpen => briefing.activeSelf;
        public bool CoachOpen => coach.activeSelf;
        public Button MapBackButton { get; private set; }
        public Button BriefStartButton { get; private set; }
        public Button BriefCloseButton { get; private set; }
        public Button CoachButton { get; private set; }
        public Button SectorButton(int number) => mapNodes.Find(n => n.Sector.Number == number)?.Button;

        static readonly Color StarOn = Html("#ffd38b"), StarOff = Html("#2c3b48");

        void BuildCampaign()
        {
            starSprite = MakeStar();
            discSprite = MakeDisc(false);
            ringSprite = MakeDisc(true);
            frameSprite = MakeFrame();
            CampaignButton.interactable = true;  // Campaign is playable from now on.
            CampaignButton.onClick.AddListener(() => { game.Audio.Play("select", true); game.ShowCampaignMap(); });
            var campaignLabels = CampaignButton.GetComponentsInChildren<Text>();
            campaignLabels[0].color = Txt;
            campaignLabels[1].text = "Defiende y reconstruye la colonia";
            CampaignButton.GetComponent<Image>().color = Html("#2a6e78");

            BuildMap();
            BuildBriefing();
            BuildCoach();
            foreach (var pair in CardButtons)  // Gold frame that points at a card during the tutorial.
            {
                var frame = Rt("Highlight", pair.Value.transform).gameObject.AddComponent<Image>();
                frame.sprite = frameSprite; frame.type = Image.Type.Sliced; frame.color = Gold; frame.raycastTarget = false;
                Stretch(frame.rectTransform);
                frame.rectTransform.offsetMin = new Vector2(-6, -6); frame.rectTransform.offsetMax = new Vector2(6, 6);
                frame.gameObject.SetActive(false);
                cardHighlights[pair.Key] = frame;
            }
        }

        // ---- Colony map ---------------------------------------------------------------

        void BuildMap()
        {
            var root = Rt("CampaignMap", safeArea.parent);
            Stretch(root);
            campaignMap = root.gameObject;
            root.gameObject.AddComponent<Image>().color = Html("#140e0c");
            var safe = Rt("Safe", root);
            safeCopies.Add(safe);

            var bar = Rt("TopBar", safe);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(.5f, 1);
            bar.sizeDelta = new Vector2(0, 116); bar.anchoredPosition = Vector2.zero;
            var barBg = bar.gameObject.AddComponent<Image>();
            barBg.color = new Color(.03f, .08f, .12f, .94f);
            MapBackButton = TextButton(bar, "‹ MENÚ", 32, PanelLight, Txt, () => { game.Audio.Play("select", true); game.ShowMainMenu(); });
            var backRt = (RectTransform)MapBackButton.transform;
            backRt.anchorMin = backRt.anchorMax = new Vector2(0, .5f); backRt.pivot = new Vector2(0, .5f);
            backRt.sizeDelta = new Vector2(190, 80); backRt.anchoredPosition = new Vector2(18, 0);
            var title = Label(bar, "CAMPAÑA", 44, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.offsetMin = new Vector2(220, 0); title.rectTransform.offsetMax = new Vector2(-220, 0);
            var chip = Rt("Stars", bar);
            chip.anchorMin = chip.anchorMax = new Vector2(1, .5f); chip.pivot = new Vector2(1, .5f);
            chip.sizeDelta = new Vector2(190, 76); chip.anchoredPosition = new Vector2(-18, 0);
            var chipBg = chip.gameObject.AddComponent<Image>();
            chipBg.sprite = rounded; chipBg.type = Image.Type.Sliced; chipBg.color = Panel; chipBg.raycastTarget = false;
            var star = Rt("Star", chip).gameObject.AddComponent<Image>();
            star.sprite = starSprite; star.color = StarOn; star.raycastTarget = false;
            star.rectTransform.anchorMin = star.rectTransform.anchorMax = new Vector2(0, .5f); star.rectTransform.pivot = new Vector2(0, .5f);
            star.rectTransform.sizeDelta = new Vector2(48, 48); star.rectTransform.anchoredPosition = new Vector2(14, 0);
            mapStarsText = Label(chip, "", 32, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            mapStarsText.rectTransform.offsetMin = new Vector2(62, 0);

            mapViewport = Rt("Viewport", safe);
            mapViewport.anchorMin = Vector2.zero; mapViewport.anchorMax = Vector2.one;
            mapViewport.offsetMin = Vector2.zero; mapViewport.offsetMax = new Vector2(0, -116);
            mapViewport.gameObject.AddComponent<RectMask2D>();
            mapViewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            mapScroll = mapViewport.gameObject.AddComponent<ScrollRect>();
            mapScroll.horizontal = false; mapScroll.vertical = true;
            mapScroll.movementType = ScrollRect.MovementType.Clamped;
            mapScroll.decelerationRate = .12f;

            mapContent = Rt("Content", mapViewport);
            mapContent.anchorMin = new Vector2(0, 1); mapContent.anchorMax = Vector2.one; mapContent.pivot = new Vector2(.5f, 1);
            mapContent.offsetMin = mapContent.offsetMax = Vector2.zero;
            var fitter = mapContent.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            fitter.aspectRatio = .5f;  // The rendered map is 1 : 2.
            mapScroll.viewport = mapViewport;
            mapScroll.content = mapContent;
            var map = mapContent.gameObject.AddComponent<Image>();
            map.sprite = Resources.Load<Sprite>("Sprites/Map/map");
            map.color = map.sprite != null ? Color.white : Html("#4a3426");
            map.raycastTarget = true;

            foreach (var sector in CampaignData.Sectors) mapNodes.Add(BuildNode(sector));
            campaignMap.SetActive(false);
        }

        MapNode BuildNode(CampaignSector sector)
        {
            var node = new MapNode { Sector = sector };
            var rt = Rt("Sector" + sector.Number, mapContent);
            rt.anchorMin = rt.anchorMax = new Vector2(sector.MapX, sector.MapY);
            rt.sizeDelta = new Vector2(232, 232);

            node.Ring = Rt("Ring", rt).gameObject.AddComponent<Image>();
            node.Ring.sprite = ringSprite; node.Ring.color = Cyan; node.Ring.raycastTarget = false;
            Stretch(node.Ring.rectTransform);
            node.Lock = Rt("Lock", rt).gameObject.AddComponent<Image>();
            node.Lock.sprite = discSprite; node.Lock.color = new Color(.02f, .04f, .06f, .72f); node.Lock.raycastTarget = false;
            Stretch(node.Lock.rectTransform);
            node.State = Label(node.Lock.transform, "", 26, Muted, TextAnchor.MiddleCenter, FontStyle.Bold);

            var hit = rt.gameObject.AddComponent<Image>();
            hit.sprite = discSprite; hit.color = new Color(1, 1, 1, 0);
            node.Button = rt.gameObject.AddComponent<Button>();
            node.Button.targetGraphic = hit;
            node.Button.onClick.AddListener(() => OpenBriefing(sector));

            var tag = Rt("Tag", rt);
            tag.anchorMin = tag.anchorMax = new Vector2(.5f, 0); tag.pivot = new Vector2(.5f, 1);
            tag.sizeDelta = new Vector2(300, 88); tag.anchoredPosition = new Vector2(0, 6);
            var tagBg = tag.gameObject.AddComponent<Image>();
            tagBg.sprite = rounded; tagBg.type = Image.Type.Sliced; tagBg.color = new Color(.04f, .1f, .15f, .9f); tagBg.raycastTarget = false;
            node.Caption = Label(tag, $"{sector.Number} · {sector.Name}", 28, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            node.Caption.rectTransform.anchorMin = new Vector2(0, .45f);
            node.Caption.resizeTextForBestFit = true; node.Caption.resizeTextMinSize = 18; node.Caption.resizeTextMaxSize = 28;
            node.Caption.rectTransform.offsetMin = new Vector2(10, 0); node.Caption.rectTransform.offsetMax = new Vector2(-10, -2);
            for (int i = 0; i < 3; i++)
            {
                var s = Rt("Star", tag).gameObject.AddComponent<Image>();
                s.sprite = starSprite; s.raycastTarget = false;
                s.rectTransform.anchorMin = s.rectTransform.anchorMax = new Vector2(.5f, .25f);
                s.rectTransform.sizeDelta = new Vector2(34, 34); s.rectTransform.anchoredPosition = new Vector2((i - 1) * 40, 0);
                node.Stars.Add(s);
            }
            return node;
        }

        public void ShowCampaignMap()
        {
            campaignMap.SetActive(true);
            campaignMap.transform.SetAsLastSibling();
            briefing.SetActive(false);
            RefreshMap();
            // Scroll so the next sector to play is in view.
            Canvas.ForceUpdateCanvases();
            var next = System.Array.Find(CampaignData.Sectors, s => CampaignDirector.Unlocked(s) && !SaveGame.Completed(s.Id))
                ?? System.Array.FindLast(CampaignData.Sectors, s => CampaignDirector.Unlocked(s));
            float viewShare = mapViewport.rect.height / Mathf.Max(1, mapContent.rect.height);
            float target = next != null ? (next.MapY - viewShare * .35f) / Mathf.Max(.01f, 1 - viewShare) : 0;
            mapScroll.verticalNormalizedPosition = Mathf.Clamp01(target);
        }

        public void HideCampaignMap()
        {
            campaignMap.SetActive(false);
            briefing.SetActive(false);
        }

        void RefreshMap()
        {
            int total = 0, playable = 0;
            foreach (var node in mapNodes)
            {
                var sector = node.Sector;
                int stars = SaveGame.Stars(sector.Id);
                bool unlocked = CampaignDirector.Unlocked(sector);
                total += stars;
                if (sector.Playable) playable++;
                node.Lock.gameObject.SetActive(!unlocked);
                node.State.text = sector.Playable ? "BLOQUEADO" : "PRÓXIMAMENTE";
                node.Ring.gameObject.SetActive(unlocked);
                node.Ring.color = stars > 0 ? Green : Cyan;
                node.Caption.color = unlocked ? Txt : Muted;
                for (int i = 0; i < 3; i++) node.Stars[i].color = i < stars ? StarOn : StarOff;
            }
            mapStarsText.text = $"{total}/{playable * 3}";
        }

        // ---- Mission briefing ---------------------------------------------------------

        void BuildBriefing()
        {
            var root = Rt("Briefing", campaignMap.transform);
            Stretch(root);
            briefing = root.gameObject;
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0, 0, 0, .55f);
            var dimButton = root.gameObject.AddComponent<Button>();  // Tapping outside the sheet closes it.
            dimButton.targetGraphic = dim;
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(() => briefing.SetActive(false));

            var sheet = Rt("Sheet", root);
            sheet.anchorMin = new Vector2(.03f, .03f); sheet.anchorMax = new Vector2(.97f, .66f);
            sheet.offsetMin = sheet.offsetMax = Vector2.zero;
            var bg = sheet.gameObject.AddComponent<Image>();
            bg.sprite = bevel; bg.type = Image.Type.Sliced; bg.color = Panel;
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;  // Swallows taps on the sheet itself.

            briefKicker = Label(sheet, "", 28, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            Box(briefKicker.rectTransform, .9f, .97f);
            briefTitle = Label(sheet, "", 60, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            briefTitle.resizeTextForBestFit = true; briefTitle.resizeTextMinSize = 36; briefTitle.resizeTextMaxSize = 60;
            Box(briefTitle.rectTransform, .79f, .9f);
            briefText = Label(sheet, "", 30, Muted, TextAnchor.UpperLeft);
            briefText.resizeTextForBestFit = true; briefText.resizeTextMinSize = 24; briefText.resizeTextMaxSize = 34;
            Box(briefText.rectTransform, .58f, .785f);
            briefFacts = Label(sheet, "", 28, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            Box(briefFacts.rectTransform, .52f, .58f);

            var newLabel = Label(sheet, "NOVEDADES", 26, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            Box(newLabel.rectTransform, .47f, .53f);
            briefNewRow = Rt("New", sheet);
            Box(briefNewRow, .27f, .47f);
            var row = briefNewRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18; row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = false; row.childControlHeight = true; row.childForceExpandWidth = false; row.childForceExpandHeight = true;

            var best = Label(sheet, "MEJOR RESULTADO", 24, Muted, TextAnchor.MiddleLeft, FontStyle.Bold);
            Box(best.rectTransform, .2f, .26f);
            for (int i = 0; i < 3; i++)
            {
                var s = Rt("Star", sheet).gameObject.AddComponent<Image>();
                s.sprite = starSprite; s.raycastTarget = false;
                s.rectTransform.anchorMin = s.rectTransform.anchorMax = new Vector2(.5f, .23f);
                s.rectTransform.sizeDelta = new Vector2(52, 52); s.rectTransform.anchoredPosition = new Vector2(30 + i * 60, 0);
                briefStars.Add(s);
            }

            BriefStartButton = TextButton(sheet, "DEFENDER SECTOR", 38, Cyan, Bg, () => { if (briefed != null) game.StartSector(briefed); });
            var startRt = (RectTransform)BriefStartButton.transform;
            startRt.anchorMin = new Vector2(.04f, .04f); startRt.anchorMax = new Vector2(.64f, .16f);
            startRt.offsetMin = startRt.offsetMax = Vector2.zero;
            BriefCloseButton = TextButton(sheet, "CERRAR", 32, PanelLight, Txt, () => { game.Audio.Play("select", true); briefing.SetActive(false); });
            var closeRt = (RectTransform)BriefCloseButton.transform;
            closeRt.anchorMin = new Vector2(.67f, .04f); closeRt.anchorMax = new Vector2(.96f, .16f);
            closeRt.offsetMin = closeRt.offsetMax = Vector2.zero;
            briefing.SetActive(false);
        }

        static void Box(RectTransform rt, float min, float max)
        {
            rt.anchorMin = new Vector2(.05f, min); rt.anchorMax = new Vector2(.95f, max);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void OpenBriefing(CampaignSector sector)
        {
            game.Audio.Play("select", true);
            briefed = sector;
            bool unlocked = CampaignDirector.Unlocked(sector);
            briefKicker.text = $"SECTOR {sector.Number}";
            briefTitle.text = sector.Name;
            briefText.text = sector.Brief;
            var level = sector.Level;
            briefFacts.text = level == null ? "En preparación para la siguiente fase"
                : $"Tablero {level.Width} × {level.Height}   ·   {level.Waves} oleadas   ·   Base {level.CoreMax}";
            for (int i = briefNewRow.childCount - 1; i >= 0; i--) Destroy(briefNewRow.GetChild(i).gameObject);
            foreach (var m in sector.NewModules) NewItem(game.Board.ModuleSprite(m), GameData.Modules[m].Name);
            foreach (var e in sector.NewEnemies) NewItem(game.Board.EnemySprite(e), CampaignData.Enemies[e].Name);
            int stars = SaveGame.Stars(sector.Id);
            for (int i = 0; i < 3; i++) briefStars[i].color = i < stars ? StarOn : StarOff;
            BriefStartButton.interactable = unlocked;
            BriefStartButton.GetComponentInChildren<Text>().text = !sector.Playable ? "PRÓXIMAMENTE" : !unlocked ? "COMPLETA EL ANTERIOR" : stars > 0 ? "JUGAR DE NUEVO" : "DEFENDER SECTOR";
            briefing.SetActive(true);
        }

        void NewItem(Sprite sprite, string caption)
        {
            var item = Rt("Item", briefNewRow);
            item.sizeDelta = new Vector2(150, 0);
            var frame = item.gameObject.AddComponent<Image>();
            frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = PanelLight; frame.raycastTarget = false;
            var icon = Rt("Icon", item).gameObject.AddComponent<Image>();
            icon.sprite = sprite; icon.preserveAspect = true; icon.raycastTarget = false;
            icon.rectTransform.anchorMin = new Vector2(.08f, .3f); icon.rectTransform.anchorMax = new Vector2(.92f, .97f);
            icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
            var label = Label(item, caption, 22, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 14; label.resizeTextMaxSize = 22;
            label.rectTransform.anchorMax = new Vector2(1, .3f);
        }

        // ---- Tutorial coach -----------------------------------------------------------

        void BuildCoach()
        {
            var panel = Rt("Coach", safeArea);
            panel.anchorMin = new Vector2(0, 1); panel.anchorMax = Vector2.one; panel.pivot = new Vector2(.5f, 1);
            panel.sizeDelta = new Vector2(-28, 178); panel.anchoredPosition = new Vector2(0, -HudHeight - 10);
            coach = panel.gameObject;
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = new Color(.03f, .1f, .15f, .96f); bg.raycastTarget = false;
            var edge = Rt("Edge", panel).gameObject.AddComponent<Image>();
            edge.sprite = frameSprite; edge.type = Image.Type.Sliced; edge.color = new Color(Gold.r, Gold.g, Gold.b, .85f); edge.raycastTarget = false;
            Stretch(edge.rectTransform);
            var icon = Rt("Icon", panel).gameObject.AddComponent<Image>();
            icon.sprite = Resources.Load<Sprite>("Sprites/UI/icon_wave"); icon.preserveAspect = true; icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f); icon.rectTransform.pivot = new Vector2(0, .5f);
            icon.rectTransform.sizeDelta = new Vector2(84, 84); icon.rectTransform.anchoredPosition = new Vector2(16, 0);
            coachText = Label(panel, "", 30, Txt, TextAnchor.MiddleLeft);
            coachText.resizeTextForBestFit = true; coachText.resizeTextMinSize = 20; coachText.resizeTextMaxSize = 30;
            coachText.rectTransform.offsetMin = new Vector2(112, 10); coachText.rectTransform.offsetMax = new Vector2(-24, -10);
            CoachButton = TextButton(panel, "ENTENDIDO", 28, Gold, Bg, () => game.Campaign.Confirm());
            coachButtonGo = CoachButton.gameObject;
            var buttonRt = (RectTransform)CoachButton.transform;
            buttonRt.anchorMin = buttonRt.anchorMax = new Vector2(1, .5f); buttonRt.pivot = new Vector2(1, .5f);
            buttonRt.sizeDelta = new Vector2(230, 84); buttonRt.anchoredPosition = new Vector2(-18, 0);
            coach.SetActive(false);
        }

        /// <summary>Shows a tutorial step, or hides the coach with null.</summary>
        public void ShowCoach(TutorialStep step)
        {
            coachStep = step;
            coach.SetActive(step != null);
            if (step == null) return;
            coach.transform.SetAsLastSibling();
            coachText.text = step.Text;
            bool needsButton = step.Done == null;
            coachButtonGo.SetActive(needsButton);
            coachText.rectTransform.offsetMax = new Vector2(needsButton ? -262 : -24, -10);
        }

        void RefreshCampaign()
        {
            foreach (var pair in cardHighlights)
            {
                bool on = coachStep != null && coachStep.Card == pair.Key && coach.activeSelf;
                if (pair.Value.gameObject.activeSelf != on) pair.Value.gameObject.SetActive(on);
                if (on) pair.Value.color = new Color(Gold.r, Gold.g, Gold.b, .55f + .45f * Mathf.PingPong(Time.unscaledTime * 2, 1));
            }
            if (campaignMap.activeSelf)
            {
                float pulse = Mathf.PingPong(Time.unscaledTime * 1.4f, 1);
                foreach (var node in mapNodes)
                {
                    if (!node.Ring.gameObject.activeSelf) continue;
                    bool next = SaveGame.Stars(node.Sector.Id) == 0;
                    node.Ring.rectTransform.localScale = Vector3.one * (next ? 1 + pulse * .08f : 1);
                    node.Ring.color = next ? new Color(Cyan.r, Cyan.g, Cyan.b, .5f + .5f * pulse) : Green;
                }
            }
        }

        // ---- Modal additions: result stars and enemy cards ----------------------------

        void BuildModalExtras(RectTransform panel)
        {
            modalStarsRoot = Rt("Stars", panel).gameObject;
            var stars = (RectTransform)modalStarsRoot.transform;
            AnchorV(stars, .56f, .66f);
            for (int i = 0; i < 3; i++)
            {
                var s = Rt("Star", stars).gameObject.AddComponent<Image>();
                s.sprite = starSprite ?? (starSprite = MakeStar()); s.raycastTarget = false;
                s.rectTransform.anchorMin = s.rectTransform.anchorMax = new Vector2(.5f, .5f);
                s.rectTransform.sizeDelta = new Vector2(i == 1 ? 150 : 120, i == 1 ? 150 : 120);
                s.rectTransform.anchoredPosition = new Vector2((i - 1) * 160, i == 1 ? 12 : 0);
                modalStars.Add(s);
            }
            modalArt = Rt("Art", panel).gameObject.AddComponent<Image>();
            modalArt.preserveAspect = true; modalArt.raycastTarget = false;
            AnchorV(modalArt.rectTransform, .43f, .68f);
            modalStarsRoot.SetActive(false);
            modalArt.gameObject.SetActive(false);
        }

        public void ShowEnemyIntro(EnemyKind kind)
        {
            introKind = kind;
            ShowModal(ModalKind.EnemyIntro);
        }

        void ApplyCampaignModal(ModalKind kind)
        {
            modalStarsRoot.SetActive(false);
            modalArt.gameObject.SetActive(false);
            AnchorV(modalInfo.rectTransform, .20f, .665f);
            var campaign = game.Campaign;
            if (kind == ModalKind.EnemyIntro)
            {
                var info = CampaignData.Enemies[introKind];
                modalKicker.text = "NUEVO ENEMIGO";
                modalTitle.text = info.Name;
                modalLead.text = info.Description;
                modalArt.sprite = game.Board.EnemySprite(introKind);
                modalArt.gameObject.SetActive(true);
                modalInfo.alignment = TextAnchor.UpperCenter;
                modalInfo.text = $"<color=#ffd38b><b>CONSEJO</b></color>\n{info.Tip}";
                AnchorV(modalInfo.rectTransform, .22f, .41f);
                primaryLabel.text = "ENTENDIDO";
                secondaryButton.SetActive(false);
                modalFooter.text = "La partida está en pausa";
                return;
            }
            if (!campaign.Active) return;
            var sector = campaign.Current;
            switch (kind)
            {
                case ModalKind.Pause:
                    modalKicker.text = $"SECTOR {sector.Number} · {sector.Name.ToUpperInvariant()}";
                    secondaryLabel.text = "ABANDONAR MISIÓN";
                    break;
                case ModalKind.Win:
                case ModalKind.Lost:
                    bool win = kind == ModalKind.Win;
                    var sim = game.Sim;
                    modalKicker.text = win ? "SECTOR ASEGURADO" : "SECTOR PERDIDO";
                    modalTitle.text = sector.Name;
                    modalLead.text = win
                        ? (campaign.LastWasBest ? "¡Nuevo récord en este sector!" : "La colonia puede avanzar.")
                        : $"Resististe hasta la oleada {sim.Wave} de {sim.MaxWave}. Cubre todas las columnas y vuelve a intentarlo.";
                    modalStarsRoot.SetActive(win);
                    for (int i = 0; i < 3; i++) modalStars[i].color = i < campaign.LastStars ? StarOn : StarOff;
                    modalInfo.alignment = TextAnchor.UpperCenter;
                    modalInfo.text = $"Enemigos detenidos: <b>{sim.TotalKills}</b>\nIntegridad de la base: <b>{Mathf.CeilToInt((float)sim.Core)}/{sim.Level.CoreMax}</b>"
                        + (win ? "\n\n<color=#99b5c7>3 estrellas: termina con más del 80 % de la base.</color>" : "");
                    if (win) AnchorV(modalInfo.rectTransform, .2f, .54f);
                    primaryLabel.text = win ? "CONTINUAR" : "REINTENTAR";
                    secondaryLabel.text = win ? "REPETIR SECTOR" : "VOLVER AL MAPA";
                    modalFooter.text = $"Estrellas totales: {SaveGame.TotalStars()}";
                    break;
            }
        }

        bool CampaignPrimary()
        {
            if (modalKind == ModalKind.EnemyIntro) { game.Resume(); return true; }
            if (!game.Campaign.Active) return false;
            var sector = game.Campaign.Current;
            switch (modalKind)
            {
                case ModalKind.Win: game.ShowCampaignMap(); return true;
                case ModalKind.Lost: game.StartSector(sector); return true;
                default: return false;
            }
        }

        bool CampaignSecondary()
        {
            if (!game.Campaign.Active) return false;
            var sector = game.Campaign.Current;
            if (modalKind == ModalKind.Win) game.StartSector(sector);
            else game.ShowCampaignMap();
            return true;
        }

        // ---- Generated sprites --------------------------------------------------------

        static Sprite MakeStar()
        {
            const int Size = 96;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI / 5;
                float r = i % 2 == 0 ? .48f : .2f;
                points[i] = new Vector2(.5f + Mathf.Cos(a) * r, .48f + Mathf.Sin(a) * r) * Size;
            }
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // 4x4 supersampling for smooth edges.
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (InPolygon(points, new Vector2(x + (sx + .5f) / 4, y + (sy + .5f) / 4))) inside++;
                    byte shade = (byte)Mathf.Lerp(200, 255, y / (float)Size);
                    px[y * Size + x] = new Color32(shade, shade, shade, (byte)(inside * 255 / 16));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100);
        }

        static bool InPolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        static Sprite MakeDisc(bool ringOnly)
        {
            const int Size = 128;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(Size / 2f, Size / 2f));
                    float outer = Mathf.Clamp01(Size / 2f - 1 - d);
                    float alpha = ringOnly ? outer * Mathf.Clamp01(d - (Size / 2f - 8)) : outer;
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100);
        }

        /// <summary>Rounded outline used to point at cards and frame the coach.</summary>
        static Sprite MakeFrame()
        {
            const int Size = 64, Radius = 20, Thickness = 5;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = Mathf.Max(Radius - x - .5f, x + .5f - (Size - Radius), 0);
                    float dy = Mathf.Max(Radius - y - .5f, y + .5f - (Size - Radius), 0);
                    float edge = Radius - Mathf.Sqrt(dx * dx + dy * dy);  // Distance inside the rounded rectangle.
                    float alpha = Mathf.Clamp01(edge + .5f) * Mathf.Clamp01(Thickness - edge + .5f);
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(Radius, Radius, Radius, Radius));
        }
    }
}
