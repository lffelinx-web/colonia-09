using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Colonia09.Presentation
{
    /// <summary>Colony panels over the isometric map: resources, the building picker for a tile and the building card.</summary>
    public sealed partial class GameUI
    {
        GameObject pickerSheet, infoSheet, mapNoticeGo;
        Text materialsText, dataText, pickerTitle, mapNotice, infoTitle, infoText;
        Image infoIcon;
        RectTransform pickerList;
        Button infoUpgrade;
        Text infoUpgradeLabel;
        CampaignSector pickSector;
        int pickX, pickY;
        BuildingSave shownBuilding;
        float nextCollect, noticeUntil, nextSave;
        readonly Dictionary<BuildingType, Sprite> buildingIcons = new Dictionary<BuildingType, Sprite>();

        public bool PickerOpen => pickerSheet.activeSelf;
        public bool BuildingInfoOpen => infoSheet.activeSelf;
        public Button UpgradeBuildingButton => infoUpgrade;
        public readonly Dictionary<BuildingType, Button> PickerButtons = new Dictionary<BuildingType, Button>();

        Sprite BuildingIcon(BuildingType type)
        {
            if (!buildingIcons.TryGetValue(type, out var sprite))
                buildingIcons[type] = sprite = Resources.Load<Sprite>("Sprites/UI/icon_" + type.ToString().ToLowerInvariant())
                    ?? Resources.Load<Sprite>("Sprites/Colony/bld_" + type.ToString().ToLowerInvariant());
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
                default: return $"+{def.SectorBoost[i] * 100:0} % de producción en esta zona";
            }
        }

        static string CostText(BuildingType type, int level)
        {
            int m = ColonyData.UpgradeMaterials(type, level), d = ColonyData.UpgradeData(type, level);
            return d > 0 ? $"{m} materiales · {d} datos" : $"{m} materiales";
        }

        // ---- Resources bar ------------------------------------------------------------

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
            notice.anchorMin = new Vector2(.05f, 1); notice.anchorMax = new Vector2(.95f, 1); notice.pivot = new Vector2(.5f, 1);
            notice.sizeDelta = new Vector2(0, 96); notice.anchoredPosition = new Vector2(0, -214);
            var bg = mapNoticeGo.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = new Color(.03f, .1f, .15f, .95f); bg.raycastTarget = false;
            mapNotice = Label(notice, "", 30, Txt, TextAnchor.MiddleCenter, FontStyle.Bold);
            mapNotice.resizeTextForBestFit = true; mapNotice.resizeTextMinSize = 18; mapNotice.resizeTextMaxSize = 30;
            mapNotice.rectTransform.offsetMin = new Vector2(16, 4); mapNotice.rectTransform.offsetMax = new Vector2(-16, -4);
            mapNoticeGo.SetActive(false);

            BuildPicker();
            BuildInfo();
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

        public void ShowMapNotice(string text, float seconds)
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
            if (infoSheet.activeSelf && shownBuilding != null) RefreshInfo();
        }

        public void CloseColonySheets()
        {
            pickerSheet.SetActive(false);
            infoSheet.SetActive(false);
            game.ColonyMap.Select(null);
        }

        /// <summary>Bottom sheet over the map; tapping its own close button hides it.</summary>
        GameObject Sheet(string name, out RectTransform sheet, float top)
        {
            var root = Rt(name, campaignMap.transform);
            root.anchorMin = Vector2.zero; root.anchorMax = new Vector2(1, top);
            root.offsetMin = root.offsetMax = Vector2.zero;
            sheet = Rt("Sheet", root);
            sheet.anchorMin = new Vector2(.02f, .01f); sheet.anchorMax = new Vector2(.98f, 1);
            sheet.offsetMin = sheet.offsetMax = Vector2.zero;
            var bg = sheet.gameObject.AddComponent<Image>();
            bg.sprite = bevel; bg.type = Image.Type.Sliced; bg.color = new Color(Panel.r, Panel.g, Panel.b, .97f);
            return root.gameObject;
        }

        // ---- Building picker for an empty tile ----------------------------------------

        void BuildPicker()
        {
            pickerSheet = Sheet("Picker", out var sheet, .58f);
            pickerTitle = Label(sheet, "", 40, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            pickerTitle.rectTransform.anchorMin = new Vector2(.04f, .88f); pickerTitle.rectTransform.anchorMax = new Vector2(.8f, .98f);
            pickerTitle.rectTransform.offsetMin = pickerTitle.rectTransform.offsetMax = Vector2.zero;
            var close = TextButton(sheet, "×", 44, PanelLight, Txt, CloseColonySheets);
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = closeRt.anchorMax = Vector2.one; closeRt.pivot = Vector2.one;
            closeRt.sizeDelta = new Vector2(90, 74); closeRt.anchoredPosition = new Vector2(-14, -12);
            pickerList = Rt("List", sheet);
            pickerList.anchorMin = new Vector2(.03f, .03f); pickerList.anchorMax = new Vector2(.97f, .86f);
            pickerList.offsetMin = pickerList.offsetMax = Vector2.zero;
            var list = pickerList.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 10; list.childControlHeight = true; list.childControlWidth = true;
            list.childForceExpandHeight = true; list.childForceExpandWidth = true;
            foreach (var type in ColonyData.Types) PickerButtons[type] = PickerRow(type);
            pickerSheet.SetActive(false);
        }

        Button PickerRow(BuildingType type)
        {
            var def = ColonyData.Buildings[type];
            var row = Rt("Row", pickerList);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = rounded; bg.type = Image.Type.Sliced; bg.color = PanelLight; bg.raycastTarget = false;
            var icon = Rt("Icon", row).gameObject.AddComponent<Image>();
            icon.sprite = BuildingIcon(type); icon.preserveAspect = true; icon.raycastTarget = false;
            icon.rectTransform.anchorMin = new Vector2(0, 0); icon.rectTransform.anchorMax = new Vector2(0, 1); icon.rectTransform.pivot = new Vector2(0, .5f);
            icon.rectTransform.sizeDelta = new Vector2(150, 0); icon.rectTransform.anchoredPosition = new Vector2(4, 0);
            var text = Label(row, $"<b>{def.Name}</b>  <size=22><color=#91e5bd>{EffectText(type, 1)}</color></size>\n<size=21><color=#99b5c7>{def.Description}</color></size>", 30, Txt, TextAnchor.MiddleLeft);
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = 30;
            text.rectTransform.offsetMin = new Vector2(160, 4); text.rectTransform.offsetMax = new Vector2(-250, -4);
            var button = TextButton(row, $"CONSTRUIR\n<size=20>{CostText(type, 1)}</size>", 26, Cyan, Bg, () => BuildHere(type));
            var rt = (RectTransform)button.transform;
            rt.anchorMin = new Vector2(1, .12f); rt.anchorMax = new Vector2(1, .88f); rt.pivot = new Vector2(1, .5f);
            rt.sizeDelta = new Vector2(230, 0); rt.anchoredPosition = new Vector2(-10, 0);
            return button;
        }

        /// <summary>Opens the picker for an empty tile of a liberated zone.</summary>
        public void OpenPicker(CampaignSector sector, int x, int y)
        {
            game.Audio.Play("select", true);
            pickSector = sector; pickX = x; pickY = y;
            int built = Colony.CountIn(sector.Id), capacity = ColonyData.LotsFor(sector.Id);
            pickerTitle.text = $"Construir · {sector.Name} <size=26><color=#99b5c7>{built}/{capacity} edificios</color></size>";
            foreach (var pair in PickerButtons) pair.Value.interactable = Colony.CanAfford(pair.Key, 1) && built < capacity;
            infoSheet.SetActive(false);
            pickerSheet.SetActive(true);
            pickerSheet.transform.SetAsLastSibling();
        }

        void BuildHere(BuildingType type)
        {
            if (!Colony.Build(pickSector.Id, pickX, pickY, type)) { game.Audio.Play("hit", true); return; }
            game.Audio.Play("build", true);
            CloseColonySheets();
            game.ColonyMap.Rebuild();
            game.ColonyMap.Pop(pickX, pickY);
            ShowMapNotice($"{ColonyData.Buildings[type].Name} construido · {EffectText(type, 1)}", 3);
        }

        // ---- Building card ------------------------------------------------------------

        void BuildInfo()
        {
            infoSheet = Sheet("BuildingInfo", out var sheet, .3f);
            var frame = Rt("IconFrame", sheet).gameObject.AddComponent<Image>();
            frame.sprite = rounded; frame.type = Image.Type.Sliced; frame.color = PanelLight; frame.raycastTarget = false;
            frame.rectTransform.anchorMin = new Vector2(.03f, .1f); frame.rectTransform.anchorMax = new Vector2(.03f, .9f); frame.rectTransform.pivot = new Vector2(0, .5f);
            frame.rectTransform.sizeDelta = new Vector2(220, 0);
            infoIcon = Rt("Icon", frame.transform).gameObject.AddComponent<Image>();
            infoIcon.preserveAspect = true; infoIcon.raycastTarget = false;
            Stretch(infoIcon.rectTransform);
            infoTitle = Label(sheet, "", 40, Txt, TextAnchor.MiddleLeft, FontStyle.Bold);
            infoTitle.rectTransform.anchorMin = new Vector2(0, .7f); infoTitle.rectTransform.anchorMax = new Vector2(1, .95f);
            infoTitle.rectTransform.offsetMin = new Vector2(270, 0); infoTitle.rectTransform.offsetMax = new Vector2(-120, 0);
            infoText = Label(sheet, "", 26, Muted, TextAnchor.UpperLeft);
            infoText.resizeTextForBestFit = true; infoText.resizeTextMinSize = 16; infoText.resizeTextMaxSize = 26;
            infoText.rectTransform.anchorMin = new Vector2(0, .36f); infoText.rectTransform.anchorMax = new Vector2(1, .7f);
            infoText.rectTransform.offsetMin = new Vector2(270, 0); infoText.rectTransform.offsetMax = new Vector2(-24, 0);
            infoUpgrade = TextButton(sheet, "", 28, Gold, Bg, UpgradeShown);
            infoUpgradeLabel = infoUpgrade.GetComponentInChildren<Text>();
            var upRt = (RectTransform)infoUpgrade.transform;
            upRt.anchorMin = new Vector2(0, .08f); upRt.anchorMax = new Vector2(1, .34f);
            upRt.offsetMin = new Vector2(270, 0); upRt.offsetMax = new Vector2(-24, 0);
            var close = TextButton(sheet, "×", 44, PanelLight, Txt, CloseColonySheets);
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = closeRt.anchorMax = Vector2.one; closeRt.pivot = Vector2.one;
            closeRt.sizeDelta = new Vector2(90, 74); closeRt.anchoredPosition = new Vector2(-14, -12);
            infoSheet.SetActive(false);
        }

        public void OpenBuildingInfo(BuildingSave building)
        {
            game.Audio.Play("select", true);
            shownBuilding = building;
            pickerSheet.SetActive(false);
            infoSheet.SetActive(true);
            infoSheet.transform.SetAsLastSibling();
            RefreshInfo();
        }

        void RefreshInfo()
        {
            var b = shownBuilding;
            if (!System.Enum.TryParse(b.type, out BuildingType type)) return;
            var def = ColonyData.Buildings[type];
            bool max = b.level >= ColonyData.MaxLevel;
            infoIcon.sprite = BuildingIcon(type);
            infoTitle.text = $"{def.Name}  <size=26><color=#ffd38b>NIVEL {b.level}</color></size>";
            infoText.text = $"<color=#91e5bd>{EffectText(type, b.level)}</color>\n{def.Description}" +
                (max ? "\nNivel máximo alcanzado." : $"\n<color=#eaf9fb>Nivel {b.level + 1}: {EffectText(type, b.level + 1)}</color>");
            infoUpgradeLabel.text = max ? "NIVEL MÁXIMO" : $"MEJORAR · {CostText(type, b.level + 1)}";
            infoUpgrade.interactable = !max && Colony.CanAfford(type, b.level + 1);
        }

        void UpgradeShown()
        {
            var b = shownBuilding;
            if (b == null || !Colony.Upgrade(b.x, b.y)) { game.Audio.Play("hit", true); return; }
            game.Audio.Play("upgrade", true);
            game.ColonyMap.Rebuild();
            game.ColonyMap.Pop(b.x, b.y);
            RefreshInfo();
        }
    }
}
