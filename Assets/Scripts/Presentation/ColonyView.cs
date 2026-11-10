using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Colonia09.Presentation
{
    /// <summary>Invisible full-screen area over the colony map: one finger pans, two fingers zoom, a tap picks a tile.</summary>
    public sealed class MapTouchArea : Graphic, IPointerDownHandler, IDragHandler, IPointerUpHandler
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

        public void OnPointerUp(PointerEventData e)
        {
            pointers.Remove(e.pointerId);
            bool tap = !pinched && pointers.Count == 0 && travel < 18 && Time.unscaledTime - downTime < .6f;
            if (tap) Game.ColonyMap.Tap(downAt);
        }
    }

    /// <summary>
    /// The isometric colony, drawn by the game camera far from the battle board. The zones stand on raised
    /// platforms joined by walkways (both part of the terrain art). Zones still held by the enemy show red
    /// ground, barricades and blinking pylons; liberated zones turn into paved tiles where the player places
    /// buildings. Linked walkways light up with energy pulses and cargo drones, and the landmarks move.
    /// Tile (x, y) is a diamond one unit wide and half a unit tall.
    /// </summary>
    public sealed class ColonyView : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(300, 0, 0);
        /// <summary>The sprites were rendered aimed 0.35 above the ground; this lifts them back onto their tile.</summary>
        const float SpriteLift = .2143f;
        /// <summary>Landmarks were aimed 0.6 above the ground.</summary>
        const float LandmarkLift = .3674f;
        /// <summary>Screen height of one Blender unit of height (cos 30° / sqrt 2).</summary>
        const float Rise = .6124f;
        const float MinZoom = 4f, MaxZoom = 13f;  // Closer than MinZoom the art would blur.
        const float StartZoom = 6.2f;

        GameController game;
        Camera cam;
        Material material;
        Transform root;
        Sprite diamond, outline, glow;
        readonly Dictionary<string, (Sprite day, Sprite night)> art = new Dictionary<string, (Sprite, Sprite)>();
        readonly Dictionary<Vector2Int, Transform> placed = new Dictionary<Vector2Int, Transform>();
        readonly List<SpriteRenderer> nightLayers = new List<SpriteRenderer>();
        SpriteRenderer selection;
        float zoom = StartZoom;
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

        public static string LandmarkFor(CampaignSector sector)
        {
            switch (sector.Id)
            {
                case "landing": return "lm_landing";
                case "camp": return "lm_camp";
                case "well": return "lm_well";
                case "greenhouse": return "lm_greenhouse";
                case "mines": return "lm_mines";
                case "workshop": return "lm_workshop";
                case "port": return "lm_port";
                default: return "lm_core";
            }
        }

        public void Init(GameController controller)
        {
            game = controller;
            cam = Camera.main;
            material = Resources.Load<Material>("Materials/SpriteUnlit");
            diamond = MakeDiamond(false);
            outline = MakeDiamond(true);
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

        public static Vector3 TileWorld(float x, float y) => Origin + new Vector3((x - y) * .5f, -(x + y) * .25f, 0);

        /// <summary>A point of a zone's landmark scene (Blender units around the zone center, z up) in the world.</summary>
        static Vector3 LandmarkPoint(CampaignSector sector, float lx, float ly, float lz)
        {
            var (cx, cy) = CampaignData.ZoneCenter(sector);
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
        }

        public void Hide()
        {
            if (Active) cam.backgroundColor = boardBackground;
            Active = false;
            root.gameObject.SetActive(false);
        }

        float insetTop, insetBottom;

        /// <summary>Screen pixels covered by the interface at the top and bottom; focused zones are centered in between.</summary>
        public void SetViewInsets(float top, float bottom)
        {
            insetTop = top;
            insetBottom = bottom;
        }

        public void Focus(CampaignSector sector)
        {
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            var p = TileWorld(cx, cy) - Origin;
            center = new Vector2(p.x, p.y - (insetBottom - insetTop) / 2 * UnitsPerPixel);
            Clamp();
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

        // Moving things, refreshed every frame by Render.
        sealed class Path
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public float Length;
            public Vector3 At(float d)
            {
                d = Mathf.Clamp(d, 0, Length);
                for (int i = 1; i < Points.Count; i++)
                {
                    float seg = Vector3.Distance(Points[i - 1], Points[i]);
                    if (d <= seg || i == Points.Count - 1) return Vector3.Lerp(Points[i - 1], Points[i], seg > 0 ? Mathf.Clamp01(d / seg) : 0);
                    d -= seg;
                }
                return Points[0];
            }
        }
        sealed class Pulse { public Path Path; public SpriteRenderer R; public float Offset; }
        sealed class Drone { public Path Path; public SpriteRenderer R, Shadow; public float Offset, Speed; }
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

        /// <summary>Every tile of the walk from one zone's center to the next: along x, then along y.</summary>
        static List<Vector2Int> Walk(CampaignSector from, CampaignSector to)
        {
            var steps = new List<Vector2Int>();
            var (x, y) = CampaignData.ZoneCenter(from);
            var (tx, ty) = CampaignData.ZoneCenter(to);
            steps.Add(new Vector2Int(x, y));
            while (x != tx) { x += System.Math.Sign(tx - x); steps.Add(new Vector2Int(x, y)); }
            while (y != ty) { y += System.Math.Sign(ty - y); steps.Add(new Vector2Int(x, y)); }
            return steps;
        }

        /// <summary>Draws a walkway's lit lines: half a tile toward each neighbor on the walk, and a glowing pad at each platform entrance.</summary>
        List<GameObject> LightRoad(CampaignSector from, CampaignSector to, out Path path)
        {
            var made = new List<GameObject>();
            var steps = Walk(from, to);
            path = new Path();
            var lift = new Vector3(0, SpriteLift, 0);
            var half = Art("road_half_pos").day != null;
            for (int i = 1; i + 1 < steps.Count; i++)
            {
                var s = steps[i];
                if (CampaignData.ZoneAt(s.x, s.y) != null) continue;
                var pos = TileWorld(s.x, s.y);
                path.Points.Add(pos);
                int order = -3000 + s.x + s.y;
                if (!half)
                {
                    var r = Put("tile_road", pos + lift, order);
                    if (r != null) made.Add(r.gameObject);
                    continue;
                }
                foreach (var n in new[] { steps[i - 1], steps[i + 1] })
                {
                    var d = n - s;
                    // +x and -x halves as drawn; the y halves are the same art mirrored left to right.
                    var r = Put(d.x + d.y > 0 ? "road_half_pos" : "road_half_neg", pos + lift, order);
                    if (r == null) continue;
                    r.flipX = d.y != 0;
                    foreach (var child in r.GetComponentsInChildren<SpriteRenderer>()) child.flipX = d.y != 0;
                    made.Add(r.gameObject);
                    if (CampaignData.ZoneAt(n.x, n.y) != null)
                    {
                        var node = Put("road_node", pos + lift, order + 1);
                        if (node != null) made.Add(node.gameObject);
                    }
                }
            }
            // The pulses run from platform edge to platform edge.
            if (path.Points.Count > 0)
            {
                var first = steps[0]; var last = steps[steps.Count - 1];
                path.Points.Insert(0, Vector3.Lerp(TileWorld(first.x, first.y), path.Points[0], .75f));
                path.Points.Add(Vector3.Lerp(TileWorld(last.x, last.y), path.Points[path.Points.Count - 1], .75f));
            }
            for (int i = 1; i < path.Points.Count; i++) path.Length += Vector3.Distance(path.Points[i - 1], path.Points[i]);
            return made;
        }

        public void Rebuild()
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            placed.Clear();
            nightLayers.Clear();
            pulses.Clear(); drones.Clear(); blinks.Clear(); frames.Clear(); puffs.Clear(); linkBuilds.Clear(); sparks.Clear(); roadPaths.Clear();
            float half = (CampaignData.ColonySize - 1) / 2f;
            // A wide, softer picture of the whole island and its sea lies behind, for zooming out.
            Put("colony_overview", TileWorld(half, half), -5100);
            // The island comes in 3 x 3 pieces of 28/3 x 5 units around the map center.
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                    Put($"colony_terrain_{i}_{j}", TileWorld(half, half) + new Vector3((i - 1) * 28f / 3, (1 - j) * 5f, 0), -5000);
            var lift = new Vector3(0, SpriteLift, 0);

            // Lit walkways are the energy links: they join each connected zone to the one before it.
            var sectors = CampaignData.Sectors;
            var state = Colony.State();
            for (int i = 0; i + 1 < sectors.Length; i++)
            {
                if (!state.Connected.Contains(sectors[i].Id)) continue;
                var work = Colony.LinkWork(sectors[i + 1].Id);
                if (!state.Connected.Contains(sectors[i + 1].Id) && work == null) continue;
                var tiles = LightRoad(sectors[i], sectors[i + 1], out var path);
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
                for (float d = 0; d < path.Length; d += 1.6f)
                    pulses.Add(new Pulse { Path = path, Offset = d, R = Light(Vector3.zero, PulseColor, .32f, -5800) });
                var drone = Put("deco_drone", Vector3.zero, 0);
                if (drone != null)
                {
                    foreach (var r in drone.GetComponentsInChildren<SpriteRenderer>()) r.sortingOrder += 9200;
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
                for (int y = cy - CampaignData.ZoneRadius; y <= cy + CampaignData.ZoneRadius; y++)
                    for (int x = cx - CampaignData.ZoneRadius; x <= cx + CampaignData.ZoneRadius; x++)
                    {
                        var pos = TileWorld(x, y);
                        if (free)
                        {
                            Put("tile_pave", pos + lift, -3000 + x + y);
                            bool corner = System.Math.Abs(x - cx) == CampaignData.ZoneRadius && System.Math.Abs(y - cy) == CampaignData.ZoneRadius;
                            if (corner) Put("deco_lamp", pos + lift, Depth(x, y));
                        }
                        else
                        {
                            // Enemy ground: a red deck with a red grid; the next battle glows amber.
                            var tint = next ? new Color(1f, .45f, .2f, .32f) : new Color(.6f, .06f, .05f, .5f);
                            Put(diamond, pos, -2500 + x + y, tint, .96f);
                            Put(outline, pos, -2490 + x + y, new Color(1f, .25f, .2f, next ? .55f : .35f), .94f);
                        }
                    }
                if (free)
                {
                    // The zone's landmark stands on its 3 x 3 center: what this place is for.
                    var lmName = LandmarkFor(sector);
                    int lmOrder = Depth(cx + 1, cy + 1) + 1;
                    var landmark = Put(lmName, TileWorld(cx, cy) + new Vector3(0, LandmarkLift, 0), lmOrder);
                    // Until the zone is connected its landmark stands dark and idle.
                    if (landmark != null && !connected) Darken(landmark);
                    AddMovingPart(sector, lmName, lmOrder, connected);
                    if (connected) AddZoneLife(sector);
                }
                else
                {
                    foreach (var (dx, dy) in new[] { (-1, 1), (1, -1), (0, 2), (-2, 0) })
                        Put("deco_barricade", TileWorld(cx + dx, cy + dy) + lift, Depth(cx + dx, cy + dy));
                    // Enemy pylons on the corners, their red lights blinking out of step.
                    int k = 0;
                    foreach (var (dx, dy) in new[] { (-2, -2), (2, -2), (2, 2), (-2, 2) })
                    {
                        int px = cx + dx, py = cy + dy;
                        var beacon = Put("deco_beacon", TileWorld(px, py) + lift, Depth(px, py) + 1);
                        if (beacon == null) continue;
                        var top = TileWorld(px, py) + new Vector3(0, 1.05f * Rise, 0);
                        blinks.Add(new Blink { R = Light(top, Color.red, .55f, Depth(px, py) * 2 + 4), Color = new Color(1f, .2f, .12f, 1), Rate = 1.1f, Phase = k++ * .37f + sector.Number * .21f, Base = 0 });
                    }
                }
                if (sector.Number == 1) Put("deco_ship", TileWorld(cx, cy) + lift, Depth(cx + 1, cy + 1) + 2, 1.35f);
            }

            foreach (var b in Colony.Saved)
            {
                if (!System.Enum.TryParse(b.type, out BuildingType type)) continue;
                int order = Depth(b.x, b.y) + 2;
                var at = TileWorld(b.x, b.y) + lift;
                SpriteRenderer r;
                if (b.level == 0)
                    r = Put("deco_site", at, order) ?? Put("bld_" + type.ToString().ToLowerInvariant(), at, order, .7f);
                else
                    r = Put("bld_" + type.ToString().ToLowerInvariant(), at, order, .92f + b.level * .06f);
                if (r != null) placed[new Vector2Int(b.x, b.y)] = r.transform;
                if (b.UnderWork)
                {
                    // Scaffold, a hazard light and welding sparks while the work runs.
                    Put("deco_scaffold", at, order + 1);
                    var tileCenter = TileWorld(b.x, b.y);
                    blinks.Add(new Blink { R = Light(tileCenter + new Vector3(-.21f, 1.26f * Rise, 0), new Color(1, .6f, .15f, 1), .35f, order * 2 + 5), Color = new Color(1, .6f, .15f, 1), Rate = 1.6f, Phase = b.x * .3f, Base = .15f });
                    sparks.Add(new Spark { R = Light(tileCenter, new Color(1f, .9f, .6f, 1), .22f, order * 2 + 6), Center = tileCenter + new Vector3(0, .45f * Rise, 0) });
                }
            }
            if (showFree)
            {
                // Construction mode: every free tile of a liberated zone gets a green outline.
                foreach (var sector in sectors)
                {
                    if (!SaveGame.Completed(sector.Id)) continue;
                    var (cx, cy) = CampaignData.ZoneCenter(sector);
                    for (int y = cy - CampaignData.ZoneRadius; y <= cy + CampaignData.ZoneRadius; y++)
                        for (int x = cx - CampaignData.ZoneRadius; x <= cx + CampaignData.ZoneRadius; x++)
                            if (!CampaignData.Reserved(x, y) && Colony.At(x, y) == null)
                                Put(outline, TileWorld(x, y), -2100, new Color(.5f, 1f, .7f, .9f), .92f);
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

        void Darken(SpriteRenderer r)
        {
            r.color = new Color(.55f, .55f, .6f);
            if (r.transform.childCount > 0)
            {
                var night = r.transform.GetChild(0).GetComponent<SpriteRenderer>();
                nightLayers.Remove(night);
                Destroy(night.gameObject);
            }
        }

        /// <summary>The landmark's moving part (radar dish, excavator arm, gantry crane), played over its picture.</summary>
        void AddMovingPart(CampaignSector sector, string lmName, int order, bool running)
        {
            string part = lmName == "lm_landing" ? "radar" : lmName == "lm_mines" ? "arm" : lmName == "lm_port" ? "crane" : null;
            if (part == null) return;
            var day = new List<Sprite>();
            var night = new List<Sprite>();
            for (int f = 0; f < 32; f++)
            {
                var (d, n) = Art($"{lmName}_{part}_{f}");
                if (d == null) break;
                day.Add(d);
                night.Add(n ?? d);
            }
            if (day.Count == 0) return;
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            var r = Put(day[0], TileWorld(cx, cy) + new Vector3(0, LandmarkLift, 0), order, Color.white, 1, night[0]);
            r.sortingOrder += 1;  // Just over the landmark's own night layer.
            if (r.transform.childCount > 0) r.transform.GetChild(0).GetComponent<SpriteRenderer>().sortingOrder += 1;
            if (!running) { Darken(r); return; }
            var nightRenderer = r.transform.childCount > 0 ? r.transform.GetChild(0).GetComponent<SpriteRenderer>() : null;
            frames.Add(new Frames { Day = r, Night = nightRenderer, DaySprites = day.ToArray(), NightSprites = night.ToArray(), Fps = part == "radar" ? 6 : 3 });
        }

        /// <summary>Lights and smoke that show a connected zone at work.</summary>
        void AddZoneLife(CampaignSector sector)
        {
            var (cx, cy) = CampaignData.ZoneCenter(sector);
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
            cam.backgroundColor = Color.Lerp(new Color32(55, 91, 113, 255), new Color32(15, 70, 96, 255), Nightness);
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
            if (tile.HasValue) selection.transform.position = TileWorld(tile.Value.x, tile.Value.y);
        }

        /// <summary>Plays a short bounce on a building that was just started; a bigger one with a flash when it is finished.</summary>
        public void Pop(int x, int y, bool finished)
        {
            popTile = new Vector2Int(x, y);
            popAt = Time.unscaledTime;
            popBig = finished;
            if (finished)
            {
                flashAtPoint = TileWorld(x, y) + new Vector3(0, .3f, 0);
                flashAt = Time.unscaledTime;
                flashSize = 1.6f;
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
            cam.orthographicSize = zoom;
            cam.transform.position = Origin + new Vector3(center.x, center.y, -10);
            float now = Time.unscaledTime;
            float glowBoost = .65f + .35f * Nightness;  // Lights read stronger after dark.

            float t = now - popAt;
            float popTime = popBig ? .6f : .45f;
            if (t < popTime && placed.TryGetValue(popTile, out var tr) && tr != null)
            {
                var b = Colony.At(popTile.x, popTile.y);
                float baseScale = b != null && b.level == 0 ? 1 : .92f + (b?.level ?? 1) * .06f;
                tr.localScale = Vector3.one * baseScale * (1 + Mathf.Sin(t / popTime * Mathf.PI) * (popBig ? .3f : .18f));
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
                dr.R.transform.position = ground + new Vector3(0, SpriteLift + .55f + bob, 0);
                dr.R.transform.localScale = Vector3.one * .9f;
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

        float UnitsPerPixel => 2 * zoom / Mathf.Max(1, Screen.height);

        public void Pan(Vector2 screenDelta)
        {
            center -= screenDelta * UnitsPerPixel;
            Clamp();
        }

        public void Zoom(float factor)
        {
            zoom = Mathf.Clamp(zoom * factor, MinZoom, MaxZoom);
            Clamp();
        }

        /// <summary>
        /// Keeps the view on the island overview (44 x 30 units around the map center); past its edges the camera's
        /// sea color carries on, so the far zoom can show the whole island on a wide screen.
        /// </summary>
        void Clamp()
        {
            const float HalfW = 22, HalfH = 15;
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            zoom = Mathf.Min(zoom, MaxZoom);
            float mapY = -(CampaignData.ColonySize - 1) * .25f;
            float spanX = Mathf.Max(0, HalfW - zoom * aspect), spanY = Mathf.Max(0, HalfH - zoom);
            center.x = Mathf.Clamp(center.x, -spanX, spanX);
            center.y = Mathf.Clamp(center.y, mapY - spanY, mapY + spanY);
        }

        public bool ScreenToTile(Vector2 screen, out int x, out int y)
        {
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10)) - Origin;
            float u = world.x / .5f, v = -world.y / .25f;  // u = x - y, v = x + y
            x = Mathf.RoundToInt((u + v) / 2);
            y = Mathf.RoundToInt((v - u) / 2);
            return x >= 0 && y >= 0 && x < CampaignData.ColonySize && y < CampaignData.ColonySize;
        }

        public Vector2 TileScreen(int x, int y) => cam.WorldToScreenPoint(TileWorld(x, y));

        /// <summary>Screen point above a building's tile, where its construction timer floats.</summary>
        public Vector2 TileTagScreen(int x, int y) => cam.WorldToScreenPoint(TileWorld(x, y) + new Vector3(0, 1.1f, 0));

        /// <summary>Screen point just above a zone's far corner, where its floating tag sits.</summary>
        public Vector2 ZoneTagScreen(CampaignSector sector)
        {
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            return cam.WorldToScreenPoint(TileWorld(cx - CampaignData.ZoneRadius - .5f, cy - CampaignData.ZoneRadius - .5f) + new Vector3(0, .15f, 0));
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

        static Sprite MakeDiamond(bool outlineOnly)
        {
            const int W = 128, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // Distance inside the diamond |dx| / (W/2) + |dy| / (H/2) <= 1, in pixels along x.
                    float d = (1 - (Mathf.Abs(x + .5f - W / 2f) / (W / 2f) + Mathf.Abs(y + .5f - H / 2f) / (H / 2f))) * (W / 2f);
                    float alpha = outlineOnly ? Mathf.Clamp01(d) * Mathf.Clamp01(6 - d) : Mathf.Clamp01(d);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 128);
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
