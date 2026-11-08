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
            public RectTransform Tag;
            public Image Ring;
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
        // The game camera draws the isometric colony (ColonyView); this layer adds the bars,
        // a floating tag over each zone and the touch area that pans, zooms and picks tiles.

        void BuildMap()
        {
            var root = Rt("CampaignMap", safeArea.parent);
            Stretch(root);
            campaignMap = root.gameObject;
            var touch = root.gameObject.AddComponent<MapTouchArea>();
            touch.Game = game;
            touch.raycastTarget = true;
            var safe = Rt("Safe", root);
            safeCopies.Add(safe);
            mapTags = Rt("Tags", safe);
            Stretch(mapTags);

            var bar = Rt("TopBar", safe);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(.5f, 1);
            bar.sizeDelta = new Vector2(0, 116); bar.anchoredPosition = Vector2.zero;
            var barBg = bar.gameObject.AddComponent<Image>();
            barBg.color = new Color(.03f, .08f, .12f, .94f);
            MapBackButton = TextButton(bar, "‹ MENÚ", 32, PanelLight, Txt, () => { game.Audio.Play("select", true); game.ShowMainMenu(); });
            var backRt = (RectTransform)MapBackButton.transform;
            backRt.anchorMin = backRt.anchorMax = new Vector2(0, .5f); backRt.pivot = new Vector2(0, .5f);
            backRt.sizeDelta = new Vector2(190, 80); backRt.anchoredPosition = new Vector2(18, 0);
            var title = Label(bar, "COLONIA", 44, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
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

            foreach (var sector in CampaignData.Sectors) mapNodes.Add(BuildNode(sector));
            BuildResourceBar(safe);
            BuildMapHint(safe);
            campaignMap.SetActive(false);
        }

        RectTransform mapTags;
        Text mapHint;

        void BuildMapHint(RectTransform safe)
        {
            var hint = Rt("Hint", safe);
            hint.anchorMin = new Vector2(.05f, 0); hint.anchorMax = new Vector2(.95f, 0); hint.pivot = new Vector2(.5f, 0);
            hint.sizeDelta = new Vector2(0, 70); hint.anchoredPosition = new Vector2(0, 130);
            var bg = hint.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = new Color(.02f, .06f, .09f, .8f); bg.raycastTarget = false;
            mapHint = Label(hint, "", 26, Muted, TextAnchor.MiddleCenter);
            mapHint.resizeTextForBestFit = true; mapHint.resizeTextMinSize = 16; mapHint.resizeTextMaxSize = 26;
            mapHint.rectTransform.offsetMin = new Vector2(14, 0); mapHint.rectTransform.offsetMax = new Vector2(-14, 0);
        }

        MapNode BuildNode(CampaignSector sector)
        {
            var node = new MapNode { Sector = sector };
            var tag = Rt("Zone" + sector.Number, mapTags);
            tag.anchorMin = tag.anchorMax = Vector2.zero; tag.pivot = new Vector2(.5f, 0);
            tag.sizeDelta = new Vector2(300, 96);
            node.Tag = tag;
            var tagBg = tag.gameObject.AddComponent<Image>();
            tagBg.sprite = rounded; tagBg.type = Image.Type.Sliced; tagBg.color = new Color(.04f, .1f, .15f, .9f);
            node.Button = tag.gameObject.AddComponent<Button>();
            node.Button.targetGraphic = tagBg;
            node.Button.onClick.AddListener(() => OpenBriefing(sector));
            node.Ring = Rt("Edge", tag).gameObject.AddComponent<Image>();
            node.Ring.sprite = frameSprite; node.Ring.type = Image.Type.Sliced; node.Ring.raycastTarget = false;
            Stretch(node.Ring.rectTransform);
            node.Caption = Label(tag, $"{sector.Number} · {sector.Name}", 28, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            node.Caption.rectTransform.anchorMin = new Vector2(0, .45f);
            node.Caption.resizeTextForBestFit = true; node.Caption.resizeTextMinSize = 18; node.Caption.resizeTextMaxSize = 28;
            node.Caption.rectTransform.offsetMin = new Vector2(10, 0); node.Caption.rectTransform.offsetMax = new Vector2(-10, -2);
            node.State = Label(tag, "", 22, Muted, TextAnchor.MiddleCenter, FontStyle.Bold);
            node.State.rectTransform.anchorMax = new Vector2(1, .45f);
            for (int i = 0; i < 3; i++)
            {
                var s = Rt("Star", tag).gameObject.AddComponent<Image>();
                s.sprite = starSprite; s.raycastTarget = false;
                s.rectTransform.anchorMin = s.rectTransform.anchorMax = new Vector2(.5f, .24f);
                s.rectTransform.sizeDelta = new Vector2(34, 34); s.rectTransform.anchoredPosition = new Vector2((i - 1) * 40, 0);
                node.Stars.Add(s);
            }
            return node;
        }

        public void ShowCampaignMap()
        {
            campaignMap.SetActive(true);
            campaignMap.transform.SetAsLastSibling();
            safeArea.gameObject.SetActive(false);  // The battle HUD and dock stay hidden behind the colony.
            briefing.SetActive(false);
            CloseColonySheets();
            game.ColonyMap.Show();
            RefreshMap();
            CollectOnArrival();
            // Center the view on the zone to liberate next, or the latest one.
            var next = System.Array.Find(CampaignData.Sectors, s => CampaignDirector.Unlocked(s) && !SaveGame.Completed(s.Id))
                ?? System.Array.FindLast(CampaignData.Sectors, s => CampaignDirector.Unlocked(s));
            if (next != null) game.ColonyMap.Focus(next);
        }

        public void HideCampaignMap()
        {
            if (campaignMap.activeSelf) SaveGame.Save();
            campaignMap.SetActive(false);
            safeArea.gameObject.SetActive(true);
            briefing.SetActive(false);
            game.ColonyMap.Hide();
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
                node.State.text = stars > 0 ? "" : !sector.Playable ? "PRÓXIMAMENTE" : unlocked ? "LIBERAR ZONA" : "ZONA HOSTIL";
                node.State.color = unlocked ? Gold : Muted;
                node.Caption.color = unlocked ? Txt : Muted;
                for (int i = 0; i < 3; i++) node.Stars[i].gameObject.SetActive(stars > 0);
                for (int i = 0; i < 3; i++) node.Stars[i].color = i < stars ? StarOn : StarOff;
            }
            mapStarsText.text = $"{total}/{playable * 3}";
            game.ColonyMap.Rebuild();
        }

        /// <summary>Keeps each zone's tag floating over its zone, and the hint line up to date.</summary>
        void PlaceMapTags()
        {
            if (!campaignMap.activeSelf) return;
            float scale = mapTags.lossyScale.x;
            foreach (var node in mapNodes)
            {
                var screen = game.ColonyMap.ZoneTagScreen(node.Sector);
                var local = (screen - (Vector2)mapTags.position) / scale + mapTags.rect.size * mapTags.pivot;
                node.Tag.anchoredPosition = local;
                bool next = CampaignDirector.Unlocked(node.Sector) && SaveGame.Stars(node.Sector.Id) == 0;
                float pulse = Mathf.PingPong(Time.unscaledTime * 1.4f, 1);
                node.Ring.color = next ? new Color(Gold.r, Gold.g, Gold.b, .4f + .6f * pulse) : SaveGame.Stars(node.Sector.Id) > 0 ? new Color(Green.r, Green.g, Green.b, .6f) : new Color(1, 1, 1, .08f);
                node.Tag.localScale = Vector3.one * Mathf.Clamp(game.ColonyMap.TagScale, .7f, 1.1f);
            }
            mapHint.text = game.ColonyMap.Hint;
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
            BriefCloseButton = TextButton(sheet, "CERRAR", 32, PanelLight, Txt, () => { game.Audio.Play("select", true); briefing.SetActive(false); });
            briefing.SetActive(false);
        }

        static void Box(RectTransform rt, float min, float max)
        {
            rt.anchorMin = new Vector2(.05f, min); rt.anchorMax = new Vector2(.95f, max);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public void OpenBriefing(CampaignSector sector)
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
            // Secured sectors also open their colony lots.
            // Buildings go straight on the map: tap a free tile of a liberated zone.
            Span(BriefStartButton, .04f, .64f);
            Span(BriefCloseButton, .67f, .96f);
            BriefStartButton.interactable = unlocked;
            BriefStartButton.GetComponentInChildren<Text>().text = !sector.Playable ? "PRÓXIMAMENTE" : !unlocked ? "COMPLETA EL ANTERIOR" : stars > 0 ? "JUGAR DE NUEVO" : "DEFENDER SECTOR";
            briefing.SetActive(true);
        }

        static void Span(Button button, float from, float to)
        {
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(from, .04f); rt.anchorMax = new Vector2(to, .16f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
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

        // ---- Tutorial guide: a spotlight on what to touch ------------------------------
        // The screen darkens except for the card or cell the step asks for. An arrow bobs toward
        // it and the instruction sits in a large bubble right next to it. Touches outside the
        // hole are blocked; the hole itself lets them through to the board or the card.

        RectTransform coachRoot, coachBubble, coachArrow, coachFrame;
        readonly Image[] coachDims = new Image[4];
        Sprite arrowSprite;

        void BuildCoach()
        {
            coachRoot = Rt("Guide", safeArea.parent);
            Stretch(coachRoot);
            coachRoot.SetSiblingIndex(safeArea.GetSiblingIndex() + 1);  // Above the game, below menus and modals.
            coach = coachRoot.gameObject;
            for (int i = 0; i < 4; i++)
            {
                coachDims[i] = Rt("Dim", coachRoot).gameObject.AddComponent<Image>();
                coachDims[i].color = new Color(0, .02f, .04f, .72f);
                coachDims[i].raycastTarget = true;
                coachDims[i].rectTransform.anchorMin = coachDims[i].rectTransform.anchorMax = new Vector2(.5f, .5f);
            }
            coachFrame = Rt("Hole", coachRoot);
            coachFrame.anchorMin = coachFrame.anchorMax = new Vector2(.5f, .5f);
            var frame = coachFrame.gameObject.AddComponent<Image>();
            frame.sprite = frameSprite; frame.type = Image.Type.Sliced; frame.color = Gold; frame.raycastTarget = false;

            arrowSprite = MakeArrow();
            coachArrow = Rt("Arrow", coachRoot);
            coachArrow.anchorMin = coachArrow.anchorMax = new Vector2(.5f, .5f);
            coachArrow.sizeDelta = new Vector2(110, 110);
            var arrow = coachArrow.gameObject.AddComponent<Image>();
            arrow.sprite = arrowSprite; arrow.color = Gold; arrow.raycastTarget = false;
            var arrowShadow = coachArrow.gameObject.AddComponent<Shadow>();
            arrowShadow.effectColor = new Color(0, 0, 0, .6f); arrowShadow.effectDistance = new Vector2(0, -6);

            coachBubble = Rt("Bubble", coachRoot);
            coachBubble.anchorMin = coachBubble.anchorMax = new Vector2(.5f, .5f);
            var bg = coachBubble.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = new Color(.04f, .12f, .18f, .98f); bg.raycastTarget = true;
            var edge = Rt("Edge", coachBubble).gameObject.AddComponent<Image>();
            edge.sprite = frameSprite; edge.type = Image.Type.Sliced; edge.color = Gold; edge.raycastTarget = false;
            Stretch(edge.rectTransform);
            coachText = Label(coachBubble, "", 38, Txt, TextAnchor.MiddleCenter);
            coachText.resizeTextForBestFit = true; coachText.resizeTextMinSize = 24; coachText.resizeTextMaxSize = 38;
            CoachButton = TextButton(coachBubble, "ENTENDIDO", 34, Gold, Bg, () => game.Campaign.Confirm());
            coachButtonGo = CoachButton.gameObject;
            var buttonRt = (RectTransform)CoachButton.transform;
            buttonRt.anchorMin = buttonRt.anchorMax = new Vector2(.5f, 0); buttonRt.pivot = new Vector2(.5f, 0);
            buttonRt.sizeDelta = new Vector2(340, 92); buttonRt.anchoredPosition = new Vector2(0, 22);
            coach.SetActive(false);
        }

        /// <summary>Shows a tutorial step or a tip, or hides the guide with null.</summary>
        public void ShowCoach(TutorialStep step)
        {
            coachStep = step;
            coach.SetActive(step != null);
            if (step == null) return;
            coachText.text = step.Text;
            coachButtonGo.SetActive(step.Done == null);
            UpdateSpotlight();
        }

        /// <summary>Screen rectangle of what the current step points at, if anything.</summary>
        bool CoachTarget(out Rect screen)
        {
            screen = default;
            var step = coachStep;
            bool wantCard = step.Card.HasValue && (game.SelectedType != step.Card.Value || !step.Cell.HasValue);
            if (wantCard && CardButtons.TryGetValue(step.Card.Value, out var card) && card.gameObject.activeInHierarchy)
            {
                var corners = new Vector3[4];
                ((RectTransform)card.transform).GetWorldCorners(corners);
                screen = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                return true;
            }
            if (step.Cell.HasValue)
            {
                var c = game.Board.CellToScreen(step.Cell.Value.x, step.Cell.Value.y);
                float half = game.Board.CellPixels * .55f;
                screen = Rect.MinMaxRect(c.x - half, c.y - half, c.x + half, c.y + half);
                return true;
            }
            return false;
        }

        void UpdateSpotlight()
        {
            if (!coach.activeSelf || coachStep == null) return;
            var size = coachRoot.rect.size;
            bool tip = coachStep.IsTip;
            bool hasTarget = CoachTarget(out var screen);
            Rect hole = default;
            if (hasTarget)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(coachRoot, screen.min, null, out var min);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(coachRoot, screen.max, null, out var max);
                hole = Rect.MinMaxRect(min.x - 8, min.y - 8, max.x + 8, max.y + 8);
            }

            // Dark frame around the hole; a tip leaves the game uncovered.
            float left = -size.x / 2, right = size.x / 2, bottom = -size.y / 2, top = size.y / 2;
            if (!hasTarget) hole = new Rect(0, 0, 0, 0);
            SetDim(0, tip ? default : Rect.MinMaxRect(left, hasTarget ? hole.yMax : bottom, right, top));
            SetDim(1, tip || !hasTarget ? default : Rect.MinMaxRect(left, bottom, right, hole.yMin));
            SetDim(2, tip || !hasTarget ? default : Rect.MinMaxRect(left, hole.yMin, hole.xMin, hole.yMax));
            SetDim(3, tip || !hasTarget ? default : Rect.MinMaxRect(hole.xMax, hole.yMin, right, hole.yMax));

            coachFrame.gameObject.SetActive(hasTarget);
            coachArrow.gameObject.SetActive(hasTarget);
            float pulse = Mathf.PingPong(Time.unscaledTime * 2, 1);
            float bubbleHeight = coachButtonGo.activeSelf ? 330 : 230;
            var bubbleSize = new Vector2(size.x - 60, bubbleHeight);
            coachBubble.sizeDelta = bubbleSize;
            coachText.rectTransform.offsetMin = new Vector2(30, coachButtonGo.activeSelf ? 120 : 18);
            coachText.rectTransform.offsetMax = new Vector2(-30, -18);
            if (hasTarget)
            {
                coachFrame.anchoredPosition = hole.center;
                coachFrame.sizeDelta = hole.size + Vector2.one * (8 + pulse * 10);
                // Bubble on the roomier side of the target, the arrow between them pointing at it.
                bool above = hole.center.y < 0;
                float arrowY = above ? hole.yMax + 70 + pulse * 18 : hole.yMin - 70 - pulse * 18;
                coachArrow.anchoredPosition = new Vector2(Mathf.Clamp(hole.center.x, left + 70, right - 70), arrowY);
                coachArrow.localRotation = Quaternion.Euler(0, 0, above ? 0 : 180);
                float bubbleY = above ? hole.yMax + 130 + bubbleHeight / 2 : hole.yMin - 130 - bubbleHeight / 2;
                bubbleY = Mathf.Clamp(bubbleY, bottom + bubbleHeight / 2 + 20, top - bubbleHeight / 2 - 20);
                coachBubble.anchoredPosition = new Vector2(0, bubbleY);
            }
            else coachBubble.anchoredPosition = tip ? new Vector2(0, bottom + DockHeight + bubbleHeight / 2 + 30) : Vector2.zero;
        }

        void SetDim(int i, Rect r)
        {
            var dim = coachDims[i];
            bool on = r.width > 0 && r.height > 0;
            if (dim.gameObject.activeSelf != on) dim.gameObject.SetActive(on);
            if (!on) return;
            dim.rectTransform.anchoredPosition = r.center;
            dim.rectTransform.sizeDelta = r.size;
        }

        /// <summary>A thick arrow pointing down, used to point at the target.</summary>
        static Sprite MakeArrow()
        {
            const int Size = 96;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            var head = new[] { new Vector2(8, 52), new Vector2(88, 52), new Vector2(48, 4) };
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            var p = new Vector2(x + (sx + .5f) / 4, y + (sy + .5f) / 4);
                            bool shaft = p.x > 32 && p.x < 64 && p.y >= 50 && p.y < 92;
                            if (shaft || InPolygon(head, p)) inside++;
                        }
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 16));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 100);
        }

        void RefreshCampaign()
        {
            RefreshResources();
            PlaceMapTags();
            UpdateSpotlight();
            foreach (var pair in cardHighlights)
            {
                bool on = coachStep != null && coachStep.Card == pair.Key && coach.activeSelf;
                // While the guide asks for one card, the others fade back.
                if (coachStep != null && !coachStep.IsTip && coach.activeSelf && (coachStep.Card.HasValue || coachStep.Done == null) && !on)
                    cardGroup[pair.Key].alpha = .3f;
                if (pair.Value.gameObject.activeSelf != on) pair.Value.gameObject.SetActive(on);
                if (on) pair.Value.color = new Color(Gold.r, Gold.g, Gold.b, .55f + .45f * Mathf.PingPong(Time.unscaledTime * 2, 1));
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
                        + (win ? $"\n\n<color=#ffd38b><b>Recompensa: +{campaign.LastMaterials} materiales · +{campaign.LastData} datos</b></color>" : "")
                        + (win ? "\n<color=#99b5c7>3 estrellas: termina con más del 80 % de la base.</color>" : "");
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
