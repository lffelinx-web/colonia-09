using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Colonia09.Presentation
{
    /// <summary>Invisible full-screen area over the colony map: one finger pans, two fingers zoom, a tap picks a tile.</summary>
    public sealed class MapTouchArea : Graphic, IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler
    {
        public GameController Game;
        readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        Vector2 downAt;
        float downTime, travel;
        bool pinched;

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        public void OnPointerDown(PointerEventData e)
        {
            pointers[e.pointerId] = e.position;
            if (pointers.Count == 1) { downAt = e.position; downTime = Time.unscaledTime; travel = 0; pinched = false; }
            else pinched = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!pointers.TryGetValue(e.pointerId, out var previous)) return;
            if (pointers.Count >= 2)
            {
                Vector2 other = Vector2.zero;
                foreach (var pair in pointers) if (pair.Key != e.pointerId) { other = pair.Value; break; }
                float before = Vector2.Distance(previous, other), after = Vector2.Distance(e.position, other);
                if (before > 10) Game.ColonyMap.Zoom(before / after);
            }
            else
            {
                Game.ColonyMap.Pan(e.position - previous);
                travel += (e.position - previous).magnitude;
            }
            pointers[e.pointerId] = e.position;
        }

        public void OnScroll(PointerEventData e)
        {
            Game.ColonyMap.Zoom(Mathf.Pow(.86f, e.scrollDelta.y));
        }

        public void OnPointerUp(PointerEventData e)
        {
            pointers.Remove(e.pointerId);
            bool tap = !pinched && pointers.Count == 0 && travel < 18 && Time.unscaledTime - downTime < .6f;
            if (tap) Game.ColonyMap.Tap(downAt);
        }
    }

    /// <summary>
    /// The alpine island and its road network share a camera with the interactive districts.
    /// Barricades mark hostile ground; energy pulses and cargo drones follow completed connections.
    /// Tile (x, y) is a diamond one unit wide and half a unit tall.
    /// </summary>
    public sealed partial class ColonyView : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(300, 0, 0);
        /// <summary>The sprites were rendered aimed 0.35 above the ground; this lifts them back onto their tile.</summary>
        const float SpriteLift = .2143f;
        /// <summary>Landmarks were aimed 0.6 above the ground.</summary>
        const float LandmarkLift = .3674f;
        /// <summary>Screen height of one Blender unit of height (cos 30° / sqrt 2).</summary>
        const float Rise = .6124f;

        GameController game;
        Camera cam;
        Material material;
        Transform root;
        Sprite diamond, outline, glow, bigDiamond, bigOutline;
        readonly Dictionary<string, (Sprite day, Sprite night)> art = new Dictionary<string, (Sprite, Sprite)>();
        readonly Dictionary<Vector2Int, (Transform node, float scale)> placed = new Dictionary<Vector2Int, (Transform, float)>();
        readonly List<SpriteRenderer> nightLayers = new List<SpriteRenderer>();
        SpriteRenderer selection;
        float zoom = 9f;
        Vector2 center;
        Vector2Int popTile;
        float popAt = -10;
        bool popBig;

        public bool Active { get; private set; }
        public string Hint { get; private set; } = "";
        public float TagScale => 5.2f / zoom;
        /// <summary>Hour of the day used for lighting; null follows the phone's clock. Tests set it.</summary>
        public float? ForceHour;
        /// <summary>0 in full daylight, 1 at night, in between at dawn and dusk.</summary>
        public float Nightness { get; private set; }

        public void Init(GameController controller)
        {
            game = controller;
            cam = Camera.main;
            material = Resources.Load<Material>("Materials/SpriteUnlit");
            diamond = MakeDiamond(false, 128);
            outline = MakeDiamond(true, 128);
            // Zone-sized shapes get their own big textures so their edges stay smooth at nine tiles across.
            bigDiamond = MakeDiamond(false, 1024, 8);
            bigOutline = MakeDiamond(true, 1024, 26);
            glow = MakeGlow();
            root = new GameObject("Colony").transform;
            root.SetParent(transform, false);
            root.gameObject.SetActive(false);
        }

        /// <summary>Day and night versions of a colony sprite; the night one may be missing.</summary>
        (Sprite day, Sprite night) Art(string name)
        {
            if (!art.TryGetValue(name, out var pair))
                art[name] = pair = (Resources.Load<Sprite>("Sprites/Colony/" + name), Resources.Load<Sprite>("Sprites/Colony/" + name + "_night"));
            return pair;
        }

        /// <summary>A point of a zone's landmark scene (Blender units around the zone center, z up) in the world.</summary>
        static Vector3 LandmarkPoint(CampaignSector sector, float lx, float ly, float lz)
        {
            var (cx, cy) = CampaignData.LandmarkCenter(sector);
            return TileWorld(cx + lx, cy - ly) + new Vector3(0, lz * Rise, 0);
        }

        static float NightFor(float hour)
        {
            // Full night 20:00 to 05:00, full day 07:00 to 18:00, smooth in between.
            if (hour >= 7 && hour <= 18) return 0;
            if (hour >= 20 || hour <= 5) return 1;
            return hour < 12 ? 1 - (hour - 5) / 2 : (hour - 18) / 2;
        }

        Color boardBackground;

        public void Show()
        {
            if (!Active) boardBackground = cam.backgroundColor;
            Active = true;
            root.gameObject.SetActive(true);
            Rebuild();
            if (world != null) world.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (Active) cam.backgroundColor = boardBackground;
            Active = false;
            root.gameObject.SetActive(false);
            if (world != null) world.gameObject.SetActive(false);
        }

        float insetTop, insetBottom;

        /// <summary>Screen pixels covered by the interface at the top and bottom; focused zones are centered in between.</summary>
        public void SetViewInsets(float top, float bottom)
        {
            insetTop = top;
            insetBottom = bottom;
        }

        // ---- Building the scene -------------------------------------------------------

        SpriteRenderer Put(Sprite sprite, Vector3 position, int order, Color color, float scale = 1, Sprite night = null)
        {
            var go = new GameObject("Sprite");
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = color;
            r.sortingOrder = order * 2;
            if (material != null) r.sharedMaterial = material;
            if (night != null)
            {
                // The night version sits on top and fades in with the hour.
                var n = new GameObject("Night").AddComponent<SpriteRenderer>();
                n.transform.SetParent(go.transform, false);
                n.sprite = night;
                n.color = new Color(color.r, color.g, color.b, 0);
                n.sortingOrder = order * 2 + 1;
                if (material != null) n.sharedMaterial = material;
                nightLayers.Add(n);
            }
            Place3D(r, sprite == diamond || sprite == outline || sprite == bigDiamond || sprite == bigOutline);
            return r;
        }

        SpriteRenderer Put(string name, Vector3 position, int order, float scale = 1)
        {
            var (day, night) = Art(name);
            return day == null ? null : Put(day, position, order, Color.white, scale, night);
        }

        /// <summary>A soft light: drawn with a raw sorting order so it can sit over everything around it.</summary>
        SpriteRenderer Light(Vector3 position, Color color, float size, int sortingOrder)
        {
            var r = Put(glow, position, 0, color, size);
            r.sortingOrder = sortingOrder;
            return r;
        }

        static int Depth(int x, int y) => (x + y) * 4;

        /// <summary>The middle of a 2 x 2 lot named by its anchor tile (or of a single tile), on the ground.</summary>
        static Vector3 LotWorld(int x, int y)
        {
            var (lx, ly) = CampaignData.LotCenter(x, y);
            return TileWorld(lx, ly);
        }

        /// <summary>Tiles across a lot: 2 for a building lot, 1 for any other tile.</summary>
        static float LotSize(int x, int y) => CampaignData.IsLotAnchor(x, y) ? 2 : 1;

        // Moving things, refreshed every frame by Render.
        sealed class Path
        {
            readonly List<Vector3> points = new List<Vector3>();
            readonly List<float> distances = new List<float>();
            public float Length => distances.Count == 0 ? 0 : distances[distances.Count - 1];

            public void Append(Vector3 point)
            {
                float length = points.Count == 0 ? 0 : Length + Vector3.Distance(points[points.Count - 1], point);
                if (points.Count != 0 && length <= Length) return;
                points.Add(point);
                distances.Add(length);
            }

            public Vector3 At(float distance)
            {
                float d = Mathf.Clamp(distance, 0, Length);
                int i = distances.BinarySearch(d);
                if (i >= 0) return points[i];
                i = ~i;
                return Vector3.Lerp(points[i - 1], points[i], (d - distances[i - 1]) / (distances[i] - distances[i - 1]));
            }
        }
        sealed class Pulse { public Path Path; public SpriteRenderer R; public float Offset; }
        sealed class Drone { public Path Path; public Transform R; public SpriteRenderer Shadow; public float Offset, Speed; }
        sealed class Blink { public SpriteRenderer R; public Color Color; public float Rate, Phase, Base; }
        sealed class Frames { public SpriteRenderer Day, Night; public Sprite[] DaySprites, NightSprites; public float Fps; }
        sealed class Puff { public Vector3 From; public SpriteRenderer R; public float Phase, Rate; }
        sealed class LinkBuild { public LinkSave Work; public List<GameObject> Tiles = new List<GameObject>(); public Path Path; public SpriteRenderer Tip; }
        sealed class Spark { public SpriteRenderer R; public Vector3 Center; }

        readonly List<Pulse> pulses = new List<Pulse>();
        readonly List<Drone> drones = new List<Drone>();
        readonly List<Blink> blinks = new List<Blink>();
        readonly List<Frames> frames = new List<Frames>();
        readonly List<Puff> puffs = new List<Puff>();
        readonly List<LinkBuild> linkBuilds = new List<LinkBuild>();
        readonly List<Spark> sparks = new List<Spark>();
        readonly Dictionary<string, Path> roadPaths = new Dictionary<string, Path>();
        SpriteRenderer sweep, flash;
        float sweepAt = -10, flashAt = -10;
        Path sweepPath;
        Vector3 flashAtPoint;

        static readonly Color PulseColor = new Color(.45f, .95f, 1f, .9f);

        public void Rebuild()
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            placed.Clear();
            nightLayers.Clear();
            pulses.Clear(); drones.Clear(); blinks.Clear(); frames.Clear(); puffs.Clear(); linkBuilds.Clear(); sparks.Clear(); roadPaths.Clear(); spinners.Clear();
            DrawIsland();
            var lift = new Vector3(0, SpriteLift, 0);

            // Lit walkways are the energy links: they join each connected zone to the one before it.
            var sectors = CampaignData.Sectors;
            var state = Colony.State();
            for (int i = 0; i + 1 < sectors.Length; i++)
            {
                if (!state.Connected.Contains(sectors[i].Id)) continue;
                var work = Colony.LinkWork(sectors[i + 1].Id);
                if (!state.Connected.Contains(sectors[i + 1].Id) && work == null) continue;
                var tiles = LightRoad(sectors[i + 1], out var path);
                roadPaths[sectors[i + 1].Id] = path;
                if (work != null)
                {
                    // Still being laid: the lights come on tile by tile as the work advances.
                    var build = new LinkBuild { Work = work, Tiles = tiles, Path = path };
                    build.Tip = Light(Vector3.zero, new Color(1f, .8f, .4f, 1), .5f, 9000);
                    linkBuilds.Add(build);
                    continue;
                }
                if (path.Length <= 0) continue;
                for (float d = 0; d < path.Length; d += 2.6f)
                    pulses.Add(new Pulse { Path = path, Offset = d, R = Light(Vector3.zero, PulseColor, .32f, -5800) });
                var drone = Drone3D();
                if (drone != null)
                {
                    var shadow = Put(diamond, Vector3.zero, 0, new Color(0, 0, 0, .28f), .3f);
                    shadow.sortingOrder = -5790;
                    drones.Add(new Drone { Path = path, R = drone, Shadow = shadow, Offset = i * 2.3f, Speed = .55f + .07f * i });
                }
            }

            foreach (var sector in sectors)
            {
                var (cx, cy) = CampaignData.ZoneCenter(sector);
                bool free = SaveGame.Completed(sector.Id);
                bool next = !free && CampaignDirector.Unlocked(sector);
                bool connected = state.Connected.Contains(sector.Id);
                int rad = CampaignData.ZoneRadius;
                float side = rad * 2 + 1;
                var (lx, ly) = CampaignData.LandmarkCenter(sector);
                if (free)
                {
                    // Lamps just outside the corners and beside the streets where they leave the zone; every tile inside is a lot.
                    float edge = rad + .62f;
                    foreach (var (dx, dy) in new[] { (edge, -edge), (edge, edge), (-edge, edge), (.62f, -edge), (-edge, .62f), (.62f, edge), (edge, .62f) })
                    {
                        var lamp = TileWorld(cx + dx, cy + dy);
                        Lamp(lamp);
                        blinks.Add(new Blink { R = Light(lamp + new Vector3(.13f, .78f, -.13f), new Color(1, .8f, .5f, 1), .5f, 9000), Color = new Color(1, .8f, .5f, .5f), Rate = .05f, Base = .9f });
                    }
                    // The zone's landmark stands on its plaza behind the district: what this place is for.
                    Landmark3D(sector, TileWorld(lx, ly), connected);
                    if (connected) AddZoneLife(sector);
                }
                else
                {
                    // Enemy ground: the whole platform glows red; the next battle glows amber.
                    var tint = next ? new Color(1f, .55f, .2f, .10f) : new Color(.48f, .11f, .07f, .16f);
                    Put(bigDiamond, TileWorld(cx, cy), -2500, tint, side * .99f);
                    Put(bigOutline, TileWorld(cx, cy), -2490, new Color(1f, .25f, .2f, next ? .8f : .5f), side * .985f);
                    foreach (var (dx, dy) in new[] { (-3, 0), (3, 0), (0, 3), (0, -3), (-2, 2), (2, -2), (2, 2), (1, 0), (0, 1) })
                        Barricade(TileWorld(cx + dx, cy + dy), dx == 0 ? 0 : dy == 0 ? 90 : 45);
                    // Enemy pylons on the corners and the middle of each side, their red lights blinking out of step.
                    int k = 0;
                    foreach (var (dx, dy) in new[] { (-rad, -rad), (rad, -rad), (rad, rad), (-rad, rad), (-rad, 0), (rad, 0), (0, rad), (0, -rad) })
                    {
                        int px = cx + dx, py = cy + dy;
                        Pylon(TileWorld(px, py));
                        var top = TileWorld(px, py) + new Vector3(0, 1.2f, 0);
                        blinks.Add(new Blink { R = Light(top, Color.red, .55f, Depth(px, py) * 2 + 4), Color = new Color(1f, .2f, .12f, 1), Rate = 1.1f, Phase = k++ * .37f + sector.Number * .21f, Base = 0 });
                    }
                }
            }

            foreach (var b in Colony.Saved)
            {
                if (!System.Enum.TryParse(b.type, out BuildingType type)) continue;
                // A building fills its 2 x 2 lot: it stands on the lot's middle and sorts by its front tile.
                float size = LotSize(b.x, b.y);
                int front = (int)size - 1;
                int order = Depth(b.x + front, b.y + front) + 2;
                var at = LotWorld(b.x, b.y) + lift;
                // What stands now: the foundation of a new building, or the building at its level.
                var raised = at + Vector3.up * (SpriteLift * (size - 1));
                var r = b.level == 0 ? WorksSite(LotWorld(b.x, b.y), size) : PutBuilding(type, b.style, b.level, at, order - 1, size);
                if (r != null) placed[new Vector2Int(b.x, b.y)] = (r, r.localScale.x);
                if (!b.UnderWork && b.level >= 1 && Colony.IsConnected(b.sector)) AddBuildingLife(type, LotWorld(b.x, b.y), size, order, b.x * .37f + b.y * .23f);
                if (b.UnderWork)
                {
                    // Scaffold, a hazard light and welding sparks while the work runs.
                    Scaffold(LotWorld(b.x, b.y), size);
                    var tileCenter = LotWorld(b.x, b.y);
                    blinks.Add(new Blink { R = Light(tileCenter + new Vector3(-.21f, 1.26f * Rise, 0) * size, new Color(1, .6f, .15f, 1), .35f * size, order * 2 + 5), Color = new Color(1, .6f, .15f, 1), Rate = 1.6f, Phase = b.x * .3f, Base = .15f });
                    sparks.Add(new Spark { R = Light(tileCenter, new Color(1f, .9f, .6f, 1), .22f * size, order * 2 + 6), Center = tileCenter + new Vector3(0, .45f * Rise * size, 0) });
                }
            }
            if (showFree)
            {
                // Construction mode: every free lot of a liberated zone gets a green outline.
                foreach (var sector in sectors)
                {
                    if (!SaveGame.Completed(sector.Id)) continue;
                    foreach (var (x, y) in CampaignData.Lots(sector))
                        if (Colony.At(x, y) == null)
                            Put(outline, LotWorld(x, y), -2100, new Color(.5f, 1f, .7f, .9f), 1.9f);
                }
            }
            selection = Put(outline, Vector3.zero, -2000, new Color(1, .83f, .55f, 1));
            selection.gameObject.SetActive(false);
            sweep = Light(Vector3.zero, new Color(.7f, 1f, 1f, 1), 1.2f, 9500);
            sweep.gameObject.SetActive(false);
            flash = Light(Vector3.zero, new Color(.6f, 1f, .9f, 1), 4f, 9400);
            flash.gameObject.SetActive(false);
            UpdateHint();
            ApplyLight();
        }

        static readonly Dictionary<BuildingType, string[]> ModelNames = new Dictionary<BuildingType, string[]>
        {
            { BuildingType.Extractor, new[] { "EX01_ExtractorBasico", "EX02_ExtractorProcesador", "EX03_ExtractorIndustrial" } },
            { BuildingType.Laboratory, new[] { "LB01_LaboratorioBasico", "LB02_LaboratorioDeAnalisis", "LB03_LaboratorioAvanzado" } },
            { BuildingType.Solar, new[] { "PS01_PlantaSolarBasica", "PS02_PlantaSolarModular", "PS03_PlantaSolarAvanzada" } },
            { BuildingType.Farm, new[] { "GH01_GranjaHidroponicaBasica", "GH02_GranjaHidroponicaModular", "GH03_GranjaHidroponicaAvanzada" } },
            { BuildingType.WaterTower, new[] { "TA01_TorreDeAguaBasica", "TA02_TorreDeAguaModular", "TA03_TorreDeAguaAvanzada" } },
            { BuildingType.Refinery, new[] { "RF01_RefineriaBasica", "RF02_RefineriaModular", "RF03_RefineriaIndustrial" } },
            { BuildingType.Works, new[] { "TO01_TallerBasico", "TO02_TallerConGrua", "TO03_TallerAvanzado" } }
        };
        static readonly string[,] HousingModels =
        {
            { "N1_RefugioBasico", "N2_ViviendaModular", "N3_ViviendaFamiliar" },
            { "N2_ViviendaModular", "N3_ViviendaFamiliar", "N4_ResidencialColectivo" },
            { "N1_RefugioBasico", "N2_ViviendaModular", "AS01_ViviendaAutosuficiente" }
        };

        public static string BuildingArt(BuildingType type, int style, int level)
        {
            if (type == BuildingType.SelfSufficientHousing) return "Planos/AS01_ViviendaAutosuficiente";
            int i = Mathf.Clamp(level, 1, ColonyData.MaxLevel) - 1;
            if (type == BuildingType.Housing)
                return "Planos/" + HousingModels[Mathf.Clamp(style, 0, HousingModels.GetLength(0) - 1), i];
            if (ModelNames.TryGetValue(type, out var names)) return "Planos/" + names[i];
            return $"bld_{type.ToString().ToLowerInvariant()}_{style}_{i + 1}";
        }

        Transform PutBuilding(BuildingType type, int style, int level, Vector3 at, int order, float lot = 1)
        {
            int looks = ColonyData.Buildings[type].Styles;
            int appearance = ((style % looks) + looks) % looks;
            float scale = (type == BuildingType.Fortification ? 1f : 1.24f) * lot;
            // Old homes may still have the third cosmetic style saved before AS01 became its own building.
            var artName = BuildingArt(type, type == BuildingType.Housing ? style : appearance, level);
            // The outside 3D model when there is one; the old picture otherwise (the fortification).
            var model = ModelName(artName) is string modelName ? PutModel(modelName, at - new Vector3(0, SpriteLift, 0), appearance % 2 == 1 && ModelNames.ContainsKey(type)) : null;
            if (model != null) return model.transform;
            if (type == BuildingType.Fortification) return Turret(at - new Vector3(0, SpriteLift, 0), lot, level);
            var building = Put(artName, at + Vector3.up * (SpriteLift * (scale - 1)), order, scale)
                ?? Put("bld_" + type.ToString().ToLowerInvariant(), at + Vector3.up * (SpriteLift * (lot - 1)), order, (.92f + level * .06f) * lot);
            if (building != null && appearance % 2 == 1 && ModelNames.ContainsKey(type))
                foreach (var layer in building.GetComponentsInChildren<SpriteRenderer>()) layer.flipX = true;
            return building != null ? building.transform : null;
        }

        static readonly Dictionary<Sprite, Sprite> trimCache = new Dictionary<Sprite, Sprite>();

        /// <summary>The same picture cut to its visible pixels, so an icon fills its box instead of its transparent margin.</summary>
        public static Sprite Trimmed(Sprite full)
        {
            if (full == null) return null;
            if (trimCache.TryGetValue(full, out var cut)) return cut;
            var rect = full.rect;
            var min = new Vector2(rect.width, rect.height);
            var max = Vector2.zero;
            foreach (var vertex in full.vertices)
            {
                var p = full.pivot + vertex * full.pixelsPerUnit;
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            min = Vector2.Max(min - Vector2.one * 2, Vector2.zero);
            max = Vector2.Min(max + Vector2.one * 2, new Vector2(rect.width, rect.height));
            cut = max.x - min.x < 4 || max.y - min.y < 4 ? full
                : Sprite.Create(full.texture, new Rect(rect.x + min.x, rect.y + min.y, max.x - min.x, max.y - min.y), new Vector2(.5f, .5f), full.pixelsPerUnit);
            trimCache[full] = cut;
            return cut;
        }

        /// <summary>
        /// A working building shows it: windows that glow and flicker, smoke from the extractor and refinery, welding
        /// at the works yard, grow lights in the farm, a pulsing tank in the water tower and beacons on the defenses.
        /// </summary>
        void AddBuildingLife(BuildingType type, Vector3 center, float size, int order, float phase)
        {
            int top = order * 2 + 5;
            Vector3 Up(float h) => center + new Vector3(0, h * size * .5f, 0);
            switch (type)
            {
                case BuildingType.Extractor:
                    puffs.AddRange(Smoke(Up(1.1f), top, new Color(.62f, .52f, .42f, .5f), 3));
                    blinks.Add(new Blink { R = Light(Up(.9f) + new Vector3(-.18f * size, 0, 0), new Color(1, .7f, .3f, 1), .28f * size, top), Color = new Color(1, .7f, .3f, 1), Rate = 1.3f, Phase = phase, Base = .1f });
                    break;
                case BuildingType.Refinery:
                    puffs.AddRange(Smoke(Up(1.3f) + new Vector3(.15f * size, 0, 0), top, new Color(.72f, .74f, .76f, .5f), 4));
                    blinks.Add(new Blink { R = Light(Up(.45f), new Color(1, .55f, .2f, 1), .55f * size, top), Color = new Color(1, .55f, .2f, .5f), Rate = 3.1f, Phase = phase, Base = .45f });
                    break;
                case BuildingType.Works:
                    sparks.Add(new Spark { R = Light(Up(.5f), new Color(1f, .9f, .6f, 1), .22f * size, top + 1), Center = Up(.55f) });
                    blinks.Add(new Blink { R = Light(Up(1.15f), new Color(1, .65f, .2f, 1), .3f * size, top), Color = new Color(1, .65f, .2f, 1), Rate = 1.0f, Phase = phase, Base = 0 });
                    break;
                case BuildingType.Laboratory:
                    blinks.Add(new Blink { R = Light(Up(.55f), new Color(.35f, .9f, 1, 1), .8f * size, top), Color = new Color(.35f, .9f, 1, .4f), Rate = .45f, Phase = phase, Base = .35f });
                    blinks.Add(new Blink { R = Light(Up(1.0f) + new Vector3(.2f * size, 0, 0), Color.white, .18f * size, top), Color = new Color(.8f, 1, 1, 1), Rate = 2.2f, Phase = phase, Base = 0 });
                    break;
                case BuildingType.Solar:
                    blinks.Add(new Blink { R = Light(Up(.45f), new Color(.5f, .75f, 1, 1), 1.1f * size, top), Color = new Color(.55f, .8f, 1, .22f), Rate = .2f, Phase = phase, Base = .3f });
                    break;
                case BuildingType.Farm:
                    blinks.Add(new Blink { R = Light(Up(.4f), new Color(.45f, 1, .5f, 1), .95f * size, top), Color = new Color(.45f, 1, .5f, .35f), Rate = .25f, Phase = phase, Base = .45f });
                    break;
                case BuildingType.WaterTower:
                    blinks.Add(new Blink { R = Light(Up(.7f), new Color(.3f, .75f, 1, 1), .8f * size, top), Color = new Color(.3f, .75f, 1, .38f), Rate = .35f, Phase = phase, Base = .35f });
                    blinks.Add(new Blink { R = Light(Up(1.35f), Color.red, .16f * size, top), Color = new Color(1, .25f, .15f, 1), Rate = .9f, Phase = phase, Base = 0 });
                    break;
                case BuildingType.Fortification:
                    blinks.Add(new Blink { R = Light(Up(1.0f), Color.red, .22f * size, top), Color = new Color(1, .2f, .12f, 1), Rate = 1.1f, Phase = phase, Base = 0 });
                    break;
                case BuildingType.SelfSufficientHousing:
                    blinks.Add(new Blink { R = Light(Up(.45f), new Color(.5f, 1, .55f, 1), .7f * size, top), Color = new Color(.5f, 1, .55f, .3f), Rate = .3f, Phase = phase, Base = .45f });
                    blinks.Add(new Blink { R = Light(Up(.6f), new Color(1, .8f, .5f, 1), .75f * size, top), Color = new Color(1, .8f, .5f, .3f), Rate = .12f, Phase = phase + .5f, Base = .5f });
                    break;
                default:
                    // Homes: warm windows that brighten and dim as people come and go.
                    blinks.Add(new Blink { R = Light(Up(.6f), new Color(1, .8f, .5f, 1), .75f * size, top), Color = new Color(1, .8f, .5f, .3f), Rate = .12f, Phase = phase, Base = .5f });
                    break;
            }
        }

        /// <summary>Lights and smoke that show a connected zone at work.</summary>
        void AddZoneLife(CampaignSector sector)
        {
            var (cx, cy) = CampaignData.LandmarkCenter(sector);
            int top = Depth(cx + 2, cy + 2) * 2 + 8;
            switch (sector.Id)
            {
                case "landing":
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, 1.05f, -1.05f, 1.96f), Color.red, .45f, top), Color = new Color(1, .2f, .12f, 1), Rate = .9f, Base = 0 });
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, 1.0f, .95f, .2f), Color.cyan, 1.2f, top), Color = new Color(.4f, .95f, 1, .35f), Rate = .35f, Base = .3f });
                    break;
                case "camp":
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, 0, 0, .6f), Color.white, 3.2f, top), Color = new Color(1, .8f, .5f, .18f), Rate = .2f, Base = .55f });
                    break;
                case "well":
                    foreach (var (x, y) in new[] { (-.7f, .6f), (.55f, .7f), (-.6f, -.6f) })
                        blinks.Add(new Blink { R = Light(LandmarkPoint(sector, x, y, .55f), Color.cyan, 1.1f, top), Color = new Color(.3f, .85f, 1, .35f), Rate = .4f, Phase = x, Base = .35f });
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, .75f, -.7f, .15f), Color.cyan, 1.4f, top), Color = new Color(.2f, .7f, 1, .4f), Rate = .6f, Base = .4f });
                    break;
                case "greenhouse":
                    foreach (var x in new[] { -.78f, 0, .78f })
                        blinks.Add(new Blink { R = Light(LandmarkPoint(sector, x, 0, .3f), Color.green, 2.2f, top), Color = new Color(.45f, 1, .5f, .32f), Rate = .25f, Phase = x, Base = .45f });
                    break;
                case "mines":
                    puffs.AddRange(Smoke(LandmarkPoint(sector, .3f, .2f, .3f), top, new Color(.55f, .45f, .38f, .5f), 3));
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, -1.0f, 1.0f, 1.12f), Color.yellow, .6f, top), Color = new Color(1, .75f, .35f, 1), Rate = .5f, Base = .5f });
                    break;
                case "workshop":
                    puffs.AddRange(Smoke(LandmarkPoint(sector, .75f, .75f, 1.72f), top, new Color(.75f, .78f, .8f, .5f), 4));
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, -.35f, -.4f, .3f), Color.white, 1.4f, top), Color = new Color(1, .85f, .5f, .35f), Rate = 2.3f, Base = .2f });
                    break;
                case "port":
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, -1.3f, -.15f, 1.65f), Color.red, .45f, top), Color = new Color(1, .2f, .12f, 1), Rate = .8f, Base = 0 });
                    break;
                default:
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, 0, 0, .7f), Color.green, 3.6f, top), Color = new Color(.45f, 1, .6f, .35f), Rate = .3f, Base = .4f });
                    blinks.Add(new Blink { R = Light(LandmarkPoint(sector, 0, 0, 1.1f), Color.green, .9f, top), Color = new Color(.6f, 1, .7f, .9f), Rate = .7f, Base = .4f });
                    break;
            }
        }

        IEnumerable<Puff> Smoke(Vector3 from, int order, Color color, int count)
        {
            for (int i = 0; i < count; i++)
                yield return new Puff { From = from, R = Light(from, color, .4f, order), Phase = i / (float)count, Rate = .28f };
        }

        void ApplyLight()
        {
            var now = System.DateTime.Now;
            Nightness = NightFor(ForceHour ?? now.Hour + now.Minute / 60f);
            foreach (var n in nightLayers)
            {
                if (n == null) continue;
                var c = n.color;
                c.a = Nightness;
                n.color = c;
            }
            // The open sea of the rendered art, which the background continues past the overview's edges.
            LightIsland();
        }

        void UpdateHint()
        {
            var next = System.Array.Find(CampaignData.Sectors, s => CampaignDirector.Unlocked(s) && !SaveGame.Completed(s.Id));
            bool anyFree = System.Array.Exists(CampaignData.Sectors, s => SaveGame.Completed(s.Id));
            Hint = anyFree ? "Toca una casilla libre de una zona liberada para construir · arrastra para moverte"
                : next != null ? $"Toca «{next.Name}» para liberar la zona · arrastra para moverte" : "";
        }

        /// <summary>Highlights a tile, or nothing with null.</summary>
        bool showFree;

        /// <summary>Outlines the free tiles of every liberated zone (construction mode).</summary>
        public void ShowFreeTiles(bool show)
        {
            showFree = show;
            Rebuild();
        }

        public void Select(Vector2Int? tile)
        {
            if (selection == null) return;
            selection.gameObject.SetActive(tile.HasValue);
            if (!tile.HasValue) return;
            selection.transform.position = LotWorld(tile.Value.x, tile.Value.y) + Vector3.up * .12f;
            float s = LotSize(tile.Value.x, tile.Value.y) * .96f * 1.41421356f;
            selection.transform.localScale = new Vector3(s, 2 * s, 1);
        }

        /// <summary>Plays a short bounce on a building that was just started; a bigger one with a flash when it is finished.</summary>
        public void Pop(int x, int y, bool finished)
        {
            popTile = new Vector2Int(x, y);
            popAt = Time.unscaledTime;
            popBig = finished;
            if (finished)
            {
                flashAtPoint = LotWorld(x, y) + new Vector3(0, .3f * LotSize(x, y), 0);
                flashAt = Time.unscaledTime;
                flashSize = 1.6f * LotSize(x, y);
            }
        }

        float flashSize = 4;

        /// <summary>A freshly linked zone: a light runs along its walkway and the zone flashes.</summary>
        public void CelebrateLink(string sectorId)
        {
            var sector = CampaignData.Find(sectorId);
            if (sector == null) return;
            sweepPath = roadPaths.TryGetValue(sectorId, out var p) && p.Length > 0 ? p : null;
            sweepAt = Time.unscaledTime;
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            flashAtPoint = TileWorld(cx, cy) + new Vector3(0, .3f, 0);
            flashAt = Time.unscaledTime + (sweepPath != null ? SweepTime : 0);
            flashSize = 5;
        }

        const float SweepTime = 1.1f;

        // ---- Camera and input -----------------------------------------------------------

        /// <summary>Called every frame while the map is open, instead of the battle board.</summary>
        public void Render()
        {
            if (!Active) return;
            if (Time.frameCount % 120 == 0) ApplyLight();  // The hour changes slowly.
            ApplyCamera();
            float now = Time.unscaledTime;
            TurnSpinners(now);
            float glowBoost = .65f + .35f * Nightness;  // Lights read stronger after dark.

            float t = now - popAt;
            float popTime = popBig ? .6f : .45f;
            if (popAt >= 0 && placed.TryGetValue(popTile, out var entry) && entry.node != null)
            {
                float bounce = t < popTime ? Mathf.Sin(t / popTime * Mathf.PI) * (popBig ? .3f : .18f) : 0;
                entry.node.localScale = Vector3.one * entry.scale * (1 + bounce);
                if (t >= popTime) popAt = -10;
            }
            if (selection != null && selection.gameObject.activeSelf)
                selection.color = new Color(1, .83f, .55f, .6f + .4f * Mathf.PingPong(now * 2, 1));

            foreach (var p in pulses)
            {
                float d = Mathf.Repeat(now * 1.2f + p.Offset, p.Path.Length);
                p.R.transform.position = p.Path.At(d);
                float edge = Mathf.Clamp01(Mathf.Min(d, p.Path.Length - d) / .4f);  // Fade at the platform edges.
                p.R.color = new Color(PulseColor.r, PulseColor.g, PulseColor.b, PulseColor.a * edge * glowBoost);
            }
            foreach (var dr in drones)
            {
                // Back and forth along the walkway, a little above it, bobbing.
                float d = Mathf.PingPong(now * dr.Speed + dr.Offset, dr.Path.Length);
                var ground = dr.Path.At(d);
                float bob = Mathf.Sin(now * 2.4f + dr.Offset) * .04f;
                dr.R.position = ground + new Vector3(0, .9f + bob, 0);
                var ahead = dr.Path.At(Mathf.Min(dr.Path.Length, d + .3f)) - ground;
                if (ahead.sqrMagnitude > 1e-4f) dr.R.rotation = Quaternion.LookRotation(new Vector3(ahead.x, 0, ahead.z));
                dr.Shadow.transform.position = ground;
            }
            foreach (var bl in blinks)
            {
                float wave = .5f + .5f * Mathf.Sin((now * bl.Rate + bl.Phase) * Mathf.PI * 2);
                float a = bl.Base + (1 - bl.Base) * wave * wave;
                bl.R.color = new Color(bl.Color.r, bl.Color.g, bl.Color.b, bl.Color.a * a * glowBoost);
            }
            foreach (var f in frames)
            {
                int i = (int)(now * f.Fps) % f.DaySprites.Length;
                f.Day.sprite = f.DaySprites[i];
                if (f.Night != null) f.Night.sprite = f.NightSprites[i];
            }
            foreach (var p in puffs)
            {
                // Smoke rises, swells and fades.
                float life = Mathf.Repeat(now * p.Rate + p.Phase, 1);
                p.R.transform.position = p.From + new Vector3(life * .18f, life * .9f, 0);
                p.R.transform.localScale = Vector3.one * (.25f + life * .6f);
                var c = p.R.color;
                c.a = Mathf.Sin(life * Mathf.PI) * .45f;
                p.R.color = c;
            }
            foreach (var s in sparks)
            {
                // Welding: short bright flickers at random spots on the scaffold.
                bool on = Mathf.PerlinNoise(now * 7, s.Center.x * 3) > .62f;
                s.R.gameObject.SetActive(on);
                if (on) s.R.transform.position = s.Center + new Vector3((Mathf.PerlinNoise(now * 2, 1) - .5f) * .5f, (Mathf.PerlinNoise(1, now * 2) - .5f) * .4f, 0);
            }
            foreach (var lb in linkBuilds)
            {
                float progress = Colony.Progress(lb.Work.startTicks, lb.Work.readyTicks);
                int lit = Mathf.FloorToInt(progress * lb.Tiles.Count + .001f);
                for (int i = 0; i < lb.Tiles.Count; i++) lb.Tiles[i].SetActive(i < lit);
                bool tip = lb.Path.Length > 0;
                lb.Tip.gameObject.SetActive(tip);
                if (tip)
                {
                    lb.Tip.transform.position = lb.Path.At(progress * lb.Path.Length) + new Vector3(0, .05f, 0);
                    lb.Tip.color = new Color(1, .8f, .4f, .6f + .4f * Mathf.PingPong(now * 4, 1));
                }
            }
            float st = now - sweepAt;
            sweep.gameObject.SetActive(sweepPath != null && st < SweepTime);
            if (sweep.gameObject.activeSelf) sweep.transform.position = sweepPath.At(st / SweepTime * sweepPath.Length);
            float ft = now - flashAt;
            flash.gameObject.SetActive(ft >= 0 && ft < .8f);
            if (flash.gameObject.activeSelf)
            {
                flash.transform.position = flashAtPoint;
                flash.transform.localScale = Vector3.one * flashSize * (.6f + ft);
                flash.color = new Color(.6f, 1f, .9f, (1 - ft / .8f) * .8f);
            }
        }

        public Vector2 TileScreen(int x, int y) => cam.WorldToScreenPoint(TileWorld(x, y));

        /// <summary>Screen point above a building's tile, where its construction timer floats.</summary>
        public Vector2 TileTagScreen(int x, int y) => cam.WorldToScreenPoint(LotWorld(x, y) + new Vector3(0, 1.1f * LotSize(x, y), 0));

        /// <summary>Screen point just above a zone's far corner, where its floating tag sits.</summary>
        public Vector2 ZoneTagScreen(CampaignSector sector)
        {
            float edge = -CampaignData.ZoneRadius - .5f;
            return cam.WorldToScreenPoint(LocalTile(sector, edge, edge) + new Vector3(0, .15f, 0));
        }

        public void Tap(Vector2 screen)
        {
            var ui = game.Ui;
            if (!ScreenToTile(screen, out int x, out int y)) { Select(null); ui.CloseColonySheets(); return; }
            var zone = CampaignData.ZoneAt(x, y);
            if (zone == null) { Select(null); ui.CloseColonySheets(); return; }
            if (!SaveGame.Completed(zone.Id))
            {
                Select(null);
                if (CampaignDirector.Unlocked(zone)) ui.OpenBriefing(zone);
                else ui.ShowMapNotice(zone.Playable ? "Zona hostil: libera primero las zonas anteriores" : "Esta zona llegará en una próxima actualización", 2.4f);
                return;
            }
            var building = Colony.At(x, y);
            Select(new Vector2Int(x, y));
            if (building != null) { ui.OpenBuildingInfo(building); return; }
            var refusal = Colony.RefuseTile(x, y);
            if (refusal != null) { ui.ShowMapNotice(refusal, 2.2f); return; }
            ui.OpenPicker(zone, x, y);
        }

        /// <summary>A diamond one unit wide; the outline is <paramref name="line"/> texture pixels thick.</summary>
        static Sprite MakeDiamond(bool outlineOnly, int W, float line = 6)
        {
            int H = W / 2;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // Distance inside the diamond |dx| / (W/2) + |dy| / (H/2) <= 1, in pixels along x.
                    float d = (1 - (Mathf.Abs(x + .5f - W / 2f) / (W / 2f) + Mathf.Abs(y + .5f - H / 2f) / (H / 2f))) * (W / 2f);
                    float alpha = outlineOnly ? Mathf.Clamp01(d) * Mathf.Clamp01(line - d) : Mathf.Clamp01(d);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), W);
        }

        /// <summary>A soft round light, one unit across, bright in the middle and fading out.</summary>
        static Sprite MakeGlow()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                    float a = Mathf.Clamp01(1 - d);
                    a = a * a * (.6f + .4f * a);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), S);
        }
    }
}
