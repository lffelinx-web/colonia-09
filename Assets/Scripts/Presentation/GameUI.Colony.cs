using System.Collections.Generic;
using System.Linq;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Colonia09.Presentation
{
    /// <summary>Colony screens: resources on the map, the lots of a secured sector and the building picker.</summary>
    public sealed partial class GameUI
    {
        GameObject colonySheet, pickerSheet, mapNoticeGo;
        Text materialsText, dataText, colonyKicker, colonyTitle, colonyResources, colonySummary, pickerTitle, mapNotice;
        RectTransform lotsRoot, pickerList;
        CampaignSector colonySector;
        int pickerSlot;
        float nextCollect, noticeUntil, nextSave;
        readonly Dictionary<BuildingType, Sprite> buildingSprites = new Dictionary<BuildingType, Sprite>();

        public Button ColonyButton { get; private set; }
        public bool ColonyOpen => colonySheet.activeSelf;
        public bool PickerOpen => pickerSheet.activeSelf;
        public readonly List<Button> LotButtons = new List<Button>();
        public readonly Dictionary<BuildingType, Button> PickerButtons = new Dictionary<BuildingType, Button>();

        Sprite BuildingSprite(BuildingType type)
        {
            if (!buildingSprites.TryGetValue(type, out var sprite))
                buildingSprites[type] = sprite = Resources.Load<Sprite>("Sprites/UI/icon_" + type.ToString().ToLowerInvariant());
            return sprite;
        }

        static string EffectText(BuildingType type, int level)
        {
            var def = ColonyData.Buildings[type];
            int i = level - 1;
            switch (type)
            {
                case BuildingType.Extractor: return $"+{def.MaterialsPerHour[i]:0} materiales por hora";
                case BuildingType.Laboratory: return $"+{def.DataPerHour[i]:0} datos por hora";
                case BuildingType.Refinery: return $"+{def.StartCharge[i]} de energía inicial en misiones";
                case BuildingType.Fortification: return $"+{def.CoreBonus[i]} de integridad de base en misiones";
                default: return $"+{def.SectorBoost[i] * 100:0} % de producción en este sector";
            }
        }

        static string CostText(BuildingType type, int level)
        {
            int m = ColonyData.UpgradeMaterials(type, level), d = ColonyData.UpgradeData(type, level);
            return d > 0 ? $"{m} materiales · {d} datos" : $"{m} materiales";
        }

        // ---- Resources bar on the map -------------------------------------------------

        void BuildResourceBar(RectTransform safe)
        {
            var bar = Rt("Resources", safe);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(.5f, 1);
            bar.sizeDelta = new Vector2(0, 84); bar.anchoredPosition = new Vector2(0, -116);
            bar.gameObject.AddComponent<Image>().color = new Color(.02f, .06f, .09f, .9f);
            materialsText = ResourceChip(bar, "icon_materials", 0, .5f);
            dataText = ResourceChip(bar, "icon_data", .5f, 1);

            mapNoticeGo = Rt("Notice", safe).gameObject;
            var notice = (RectTransform)mapNoticeGo.transform;
            notice.anchorMin = new Vector2(.05f, 0); notice.anchorMax = new Vector2(.95f, 0); notice.pivot = new Vector2(.5f, 0);
            notice.sizeDelta = new Vector2(0, 96); notice.anchoredPosition = new Vector2(0, 24);
            var bg = mapNoticeGo.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = new Color(.03f, .1f, .15f, .95f); bg.raycastTarget = false;
            mapNotice = Label(notice, "", 30, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            mapNotice.resizeTextForBestFit = true; mapNotice.resizeTextMinSize = 18; mapNotice.resizeTextMaxSize = 30;
            mapNotice.rectTransform.offsetMin = new Vector2(16, 4); mapNotice.rectTransform.offsetMax = new Vector2(-16, -4);
            mapNoticeGo.SetActive(false);
        }

        Text ResourceChip(RectTransform bar, string icon, float from, float to)
        {
            var chip = Rt("Chip", bar);
            chip.anchorMin = new Vector2(from, 0); chip.anchorMax = new Vector2(to, 1);
            chip.offsetMin = new Vector2(18, 10); chip.offsetMax = new Vector2(-18, -10);
            var bg = chip.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = Panel; bg.raycastTarget = false;
            var image = Rt("Icon", chip).gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>("Sprites/UI/" + icon); image.preserveAspect = true; image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0, .5f); image.rectTransform.pivot = new Vector2(0, .5f);
            image.rectTransform.sizeDelta = new Vector2(58, 58); image.rectTransform.anchoredPosition = new Vector2(8, 0);
            var text = Label(chip, "", 28, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = 28;
            text.rectTransform.offsetMin = new Vector2(74, 0); text.rectTransform.offsetMax = new Vector2(-10, 0);
            return text;
        }

        void ShowMapNotice(string text, float seconds)
        {
            mapNotice.text = text;
            mapNoticeGo.SetActive(true);
            mapNoticeGo.transform.SetAsLastSibling();
            noticeUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Adds the production since the last visit and tells the player what arrived.</summary>
        void CollectOnArrival()
        {
            Colony.Collect(out var m, out var d);
            SaveGame.Save();
            if (m >= 1 || d >= 1)
                ShowMapNotice($"Mientras no estabas la colonia produjo +{m:0} materiales y +{d:0} datos", 4.5f);
            nextCollect = Time.unscaledTime + 1;
        }

        void RefreshResources()
        {
            if (!campaignMap.activeSelf) return;
            if (Time.unscaledTime >= nextCollect)
            {
                Colony.Collect(out _, out _);  // Production also runs while the map is open.
                nextCollect = Time.unscaledTime + 1;
                if (Time.unscaledTime >= nextSave) { SaveGame.Save(); nextSave = Time.unscaledTime + 15; }
            }
            ColonyData.RatePerHour(Colony.Buildings(), out var mph, out var dph);
            var data = SaveGame.Data;
            materialsText.text = $"{Mathf.FloorToInt((float)data.materials)} <size=20><color=#99b5c7>materiales{(mph > 0 ? $"  +{mph:0}/h" : "")}</color></size>";
            dataText.text = $"{Mathf.FloorToInt((float)data.data)} <size=20><color=#99b5c7>datos{(dph > 0 ? $"  +{dph:0}/h" : "")}</color></size>";
            if (mapNoticeGo.activeSelf && Time.unscaledTime > noticeUntil) mapNoticeGo.SetActive(false);
            if (colonySheet.activeSelf) colonyResources.text = materialsText.text + "      " + dataText.text;
        }

        // ---- Buildings drawn around each sector on the map ----------------------------

        readonly Dictionary<string, RectTransform> nodeBuildings = new Dictionary<string, RectTransform>();

        void BuildNodeBuildings(RectTransform node, CampaignSector sector)
        {
            var root = Rt("Buildings", node);
            Stretch(root);
            root.SetAsFirstSibling();
            nodeBuildings[sector.Id] = root;
        }

        void RefreshNodeBuildings()
        {
            foreach (var pair in nodeBuildings)
            {
                var root = pair.Value;
                for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
                foreach (var b in SaveGame.Data.buildings.Where(b => b.sector == pair.Key))
                {
                    if (!System.Enum.TryParse(b.type, out BuildingType type)) continue;
                    // Lots sit around the sector's pad: left, right, upper left, upper right.
                    var offsets = new[] { new Vector2(-165, 20), new Vector2(165, 20), new Vector2(-125, 150), new Vector2(125, 150) };
                    var icon = Rt("Building", root).gameObject.AddComponent<Image>();
                    icon.sprite = BuildingSprite(type); icon.preserveAspect = true; icon.raycastTarget = false;
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, .5f);
                    float size = 104 + b.level * 14;
                    icon.rectTransform.sizeDelta = new Vector2(size, size);
                    icon.rectTransform.anchoredPosition = offsets[b.slot % offsets.Length];
                }
            }
        }

        // ---- Colony sheet: the lots of one sector -------------------------------------

        void BuildColony()
        {
            colonySheet = Overlay("Colony", out var sheet, .03f, .9f, () => CloseColony());
            colonyKicker = Label(sheet, "", 28, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            Box(colonyKicker.rectTransform, .93f, .98f);
            colonyTitle = Label(sheet, "", 54, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            colonyTitle.resizeTextForBestFit = true; colonyTitle.resizeTextMinSize = 32; colonyTitle.resizeTextMaxSize = 54;
            Box(colonyTitle.rectTransform, .86f, .93f);
            colonyResources = Label(sheet, "", 28, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            Box(colonyResources.rectTransform, .81f, .86f);

            lotsRoot = Rt("Lots", sheet);
            Box(lotsRoot, .25f, .8f);
            var layout = lotsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14; layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;

            colonySummary = Label(sheet, "", 26, Muted, TextAnchor.UpperLeft);
            colonySummary.resizeTextForBestFit = true; colonySummary.resizeTextMinSize = 18; colonySummary.resizeTextMaxSize = 26;
            Box(colonySummary.rectTransform, .13f, .24f);
            var close = TextButton(sheet, "VOLVER AL MAPA", 34, PanelLight, Txt, () => CloseColony());
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = new Vector2(.05f, .025f); closeRt.anchorMax = new Vector2(.95f, .11f);
            closeRt.offsetMin = closeRt.offsetMax = Vector2.zero;

            pickerSheet = Overlay("Picker", out var picker, .06f, .86f, () => pickerSheet.SetActive(false));
            pickerTitle = Label(picker, "", 46, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            Box(pickerTitle.rectTransform, .9f, .97f);
            pickerList = Rt("List", picker);
            Box(pickerList, .14f, .89f);
            var list = pickerList.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 12; list.childControlHeight = true; list.childControlWidth = true;
            list.childForceExpandHeight = true; list.childForceExpandWidth = true;
            foreach (var type in ColonyData.Types) PickerButtons[type] = PickerRow(type);
            var cancel = TextButton(picker, "CANCELAR", 32, PanelLight, Txt, () => pickerSheet.SetActive(false));
            var cancelRt = (RectTransform)cancel.transform;
            cancelRt.anchorMin = new Vector2(.05f, .025f); cancelRt.anchorMax = new Vector2(.95f, .11f);
            cancelRt.offsetMin = cancelRt.offsetMax = Vector2.zero;
            colonySheet.SetActive(false);
            pickerSheet.SetActive(false);
        }

        /// <summary>Dimmed full-screen layer with a sheet; tapping outside the sheet runs onOutside.</summary>
        GameObject Overlay(string name, out RectTransform sheet, float bottom, float top, UnityEngine.Events.UnityAction onOutside)
        {
            var root = Rt(name, campaignMap.transform);
            Stretch(root);
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0, 0, 0, .6f);
            var outside = root.gameObject.AddComponent<Button>();
            outside.targetGraphic = dim; outside.transition = Selectable.Transition.None;
            outside.onClick.AddListener(onOutside);
            sheet = Rt("Sheet", root);
            sheet.anchorMin = new Vector2(.03f, bottom); sheet.anchorMax = new Vector2(.97f, top);
            sheet.offsetMin = sheet.offsetMax = Vector2.zero;
            var bg = sheet.gameObject.AddComponent<Image>();
            bg.sprite = bevel; bg.type = Image.Type.Sliced; bg.color = Panel;
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            return root.gameObject;
        }

        Button PickerRow(BuildingType type)
        {
            var def = ColonyData.Buildings[type];
            var row = Rt("Row", pickerList);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = PanelLight; bg.raycastTarget = false;
            var icon = Rt("Icon", row).gameObject.AddComponent<Image>();
            icon.sprite = BuildingSprite(type); icon.preserveAspect = true; icon.raycastTarget = false;
            icon.rectTransform.anchorMin = new Vector2(0, .08f); icon.rectTransform.anchorMax = new Vector2(0, .92f); icon.rectTransform.pivot = new Vector2(0, .5f);
            icon.rectTransform.sizeDelta = new Vector2(150, 0); icon.rectTransform.anchoredPosition = new Vector2(10, 0);
            var text = Label(row, $"<b>{def.Name}</b>\n<size=22><color=#99b5c7>{def.Description}</color></size>\n<size=24><color=#91e5bd>{EffectText(type, 1)}</color></size>", 30, Txt, TextAnchor.MiddleLeft);
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = 30;
            text.rectTransform.offsetMin = new Vector2(170, 6); text.rectTransform.offsetMax = new Vector2(-250, -6);
            var button = TextButton(row, $"CONSTRUIR\n<size=20>{CostText(type, 1)}</size>", 28, Cyan, Bg, () => BuildHere(type));
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(1, .15f); rt.anchorMax = new Vector2(1, .85f); rt.pivot = new Vector2(1, .5f);
            rt.sizeDelta = new Vector2(230, 0); rt.anchoredPosition = new Vector2(-12, 0);
            return button;
        }

        public void OpenColony(CampaignSector sector)
        {
            game.Audio.Play("select", true);
            colonySector = sector;
            briefing.SetActive(false);
            colonySheet.SetActive(true);
            colonySheet.transform.SetAsLastSibling();
            RefreshColony();
        }

        void CloseColony()
        {
            game.Audio.Play("select", true);
            colonySheet.SetActive(false);
            pickerSheet.SetActive(false);
            RefreshMap();
        }

        void RefreshColony()
        {
            var sector = colonySector;
            colonyKicker.text = $"COLONIA · SECTOR {sector.Number}";
            colonyTitle.text = sector.Name;
            for (int i = lotsRoot.childCount - 1; i >= 0; i--) Destroy(lotsRoot.GetChild(i).gameObject);
            LotButtons.Clear();
            int lots = ColonyData.LotsFor(sector.Id);
            for (int slot = 0; slot < lots; slot++) LotButtons.Add(LotRow(sector, slot));

            ColonyData.MissionBonuses(Colony.Buildings(), out int charge, out int core);
            ColonyData.RatePerHour(Colony.Buildings(), out var mph, out var dph);
            colonySummary.text = $"<b>Toda la colonia</b>: +{mph:0} materiales/h · +{dph:0} datos/h\n" +
                $"En cada misión: +{charge} de energía inicial · +{core} de integridad de base\n" +
                $"La producción sigue con el juego cerrado (hasta {ColonyData.OfflineCapHours:0} horas).";
            RefreshResources();
        }

        Button LotRow(CampaignSector sector, int slot)
        {
            var saved = Colony.At(sector.Id, slot);
            var row = Rt("Lot", lotsRoot);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 168;
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = saved != null ? PanelLight : new Color(Panel.r * .8f, Panel.g * .8f, Panel.b * .8f, 1); bg.raycastTarget = false;
            var icon = Rt("Icon", row).gameObject.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            icon.rectTransform.anchorMin = new Vector2(0, .06f); icon.rectTransform.anchorMax = new Vector2(0, .94f); icon.rectTransform.pivot = new Vector2(0, .5f);
            icon.rectTransform.sizeDelta = new Vector2(150, 0); icon.rectTransform.anchoredPosition = new Vector2(10, 0);
            Text text;
            Button button;
            if (saved == null || !System.Enum.TryParse(saved.type, out BuildingType type))
            {
                icon.sprite = discSprite; icon.color = new Color(1, 1, 1, .08f);
                text = Label(row, $"<b>Lote {slot + 1}</b>\n<size=24><color=#99b5c7>Terreno libre. Elige qué construir.</color></size>", 30, Txt, TextAnchor.MiddleLeft);
                int captured = slot;
                button = TextButton(row, "CONSTRUIR", 30, Cyan, Bg, () => OpenPicker(captured));
            }
            else
            {
                icon.sprite = BuildingSprite(type);
                var def = ColonyData.Buildings[type];
                bool max = saved.level >= ColonyData.MaxLevel;
                string next = max ? "Nivel máximo" : $"Nivel {saved.level + 1}: {EffectText(type, saved.level + 1)}";
                text = Label(row, $"<b>{def.Name}</b>  <size=24><color=#ffd38b>NIVEL {saved.level}</color></size>\n<size=24><color=#91e5bd>{EffectText(type, saved.level)}</color></size>\n<size=21><color=#99b5c7>{next}</color></size>", 30, Txt, TextAnchor.MiddleLeft);
                int captured = slot;
                button = TextButton(row, max ? "MÁXIMO" : $"MEJORAR\n<size=20>{CostText(type, saved.level + 1)}</size>", 28, max ? Panel : Gold, max ? Muted : Bg, () => UpgradeHere(captured));
                button.interactable = !max && Colony.CanAfford(type, saved.level + 1);
            }
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = 30;
            text.rectTransform.offsetMin = new Vector2(170, 6); text.rectTransform.offsetMax = new Vector2(-260, -6);
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(1, .17f); rt.anchorMax = new Vector2(1, .83f); rt.pivot = new Vector2(1, .5f);
            rt.sizeDelta = new Vector2(240, 0); rt.anchoredPosition = new Vector2(-12, 0);
            return button;
        }

        void OpenPicker(int slot)
        {
            game.Audio.Play("select", true);
            pickerSlot = slot;
            pickerTitle.text = $"Construir en el lote {slot + 1}";
            foreach (var pair in PickerButtons) pair.Value.interactable = Colony.CanAfford(pair.Key, 1);
            pickerSheet.SetActive(true);
            pickerSheet.transform.SetAsLastSibling();
        }

        void BuildHere(BuildingType type)
        {
            if (!Colony.Build(colonySector.Id, pickerSlot, type)) { game.Audio.Play("hit", true); return; }
            game.Audio.Play("build", true);
            pickerSheet.SetActive(false);
            RefreshColony();
            RefreshNodeBuildings();
        }

        void UpgradeHere(int slot)
        {
            if (!Colony.Upgrade(colonySector.Id, slot)) { game.Audio.Play("hit", true); return; }
            game.Audio.Play("upgrade", true);
            RefreshColony();
            RefreshNodeBuildings();
        }
    }
}
