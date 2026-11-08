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
    /// The isometric colony, drawn by the game camera far from the battle board. Zones still held by
    /// the enemy show barricades over red ground; liberated zones turn into paved tiles where the player
    /// places buildings. Tile (x, y) is a diamond one unit wide and half a unit tall.
    /// </summary>
    public sealed class ColonyView : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(300, 0, 0);
        /// <summary>The sprites were rendered aimed 0.35 above the ground; this lifts them back onto their tile.</summary>
        const float SpriteLift = .2143f;
        const float MinZoom = 4f, MaxZoom = 8.5f;  // Closer than this the art would blur.

        GameController game;
        Camera cam;
        Material material;
        Transform root;
        Sprite pave, road, lamp, ship, barricade, diamond, outline;
        readonly Sprite[,] terrain = new Sprite[3, 3];
        readonly Dictionary<BuildingType, Sprite> buildings = new Dictionary<BuildingType, Sprite>();
        readonly Dictionary<Vector2Int, Transform> placed = new Dictionary<Vector2Int, Transform>();
        SpriteRenderer selection;
        float zoom = 5.2f;
        Vector2 center;
        Vector2Int popTile;
        float popAt = -10;

        public bool Active { get; private set; }
        public string Hint { get; private set; } = "";
        public float TagScale => 5.2f / zoom;

        public void Init(GameController controller)
        {
            game = controller;
            cam = Camera.main;
            material = Resources.Load<Material>("Materials/SpriteUnlit");
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                    terrain[i, j] = Load($"colony_terrain_{i}_{j}");
            pave = Load("tile_pave");
            road = Load("tile_road");
            lamp = Load("deco_lamp");
            ship = Load("deco_ship");
            barricade = Load("deco_barricade");
            foreach (var type in ColonyData.Types) buildings[type] = Load("bld_" + type.ToString().ToLowerInvariant());
            diamond = MakeDiamond(false);
            outline = MakeDiamond(true);
            root = new GameObject("Colony").transform;
            root.SetParent(transform, false);
            root.gameObject.SetActive(false);
        }

        static Sprite Load(string name) => Resources.Load<Sprite>("Sprites/Colony/" + name);

        public static Vector3 TileWorld(float x, float y) => Origin + new Vector3((x - y) * .5f, -(x + y) * .25f, 0);

        Color boardBackground;

        public void Show()
        {
            if (!Active) boardBackground = cam.backgroundColor;
            cam.backgroundColor = new Color(.29f, .2f, .15f);  // Ground color, in case an edge ever shows.
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

        public void Focus(CampaignSector sector)
        {
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            var p = TileWorld(cx, cy) - Origin;
            center = new Vector2(p.x, p.y);
            Clamp();
        }

        // ---- Building the scene -------------------------------------------------------

        SpriteRenderer Put(Sprite sprite, Vector3 position, int order, Color color, float scale = 1)
        {
            var go = new GameObject("Sprite");
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = color;
            r.sortingOrder = order;
            if (material != null) r.sharedMaterial = material;
            return r;
        }

        static int Depth(int x, int y) => (x + y) * 4;

        public void Rebuild()
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            placed.Clear();
            float half = (CampaignData.ColonySize - 1) / 2f;
            // The ground comes in 3 x 3 pieces of 28/3 x 5 units around the map center.
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                    if (terrain[i, j] != null)
                        Put(terrain[i, j], TileWorld(half, half) + new Vector3((i - 1) * 28f / 3, (1 - j) * 5f, 0), -5000, Color.white);
            var lift = new Vector3(0, SpriteLift, 0);

            // Roads from each liberated zone to the next one.
            var sectors = CampaignData.Sectors;
            for (int i = 0; i + 1 < sectors.Length; i++)
            {
                if (!SaveGame.Completed(sectors[i].Id) || !CampaignDirector.Unlocked(sectors[i + 1]) && !SaveGame.Completed(sectors[i + 1].Id)) continue;
                foreach (var (x, y) in CampaignData.Road(sectors[i], sectors[i + 1]))
                    if (road != null) Put(road, TileWorld(x, y) + lift, -3000 + x + y, Color.white);
            }

            foreach (var sector in sectors)
            {
                var (cx, cy) = CampaignData.ZoneCenter(sector);
                bool free = SaveGame.Completed(sector.Id);
                bool next = !free && CampaignDirector.Unlocked(sector);
                for (int y = cy - CampaignData.ZoneRadius; y <= cy + CampaignData.ZoneRadius; y++)
                    for (int x = cx - CampaignData.ZoneRadius; x <= cx + CampaignData.ZoneRadius; x++)
                    {
                        var pos = TileWorld(x, y);
                        if (free)
                        {
                            if (pave != null) Put(pave, pos + lift, -3000 + x + y, Color.white);
                            bool corner = System.Math.Abs(x - cx) == CampaignData.ZoneRadius && System.Math.Abs(y - cy) == CampaignData.ZoneRadius;
                            if (corner && lamp != null) Put(lamp, pos + lift, Depth(x, y), Color.white);
                        }
                        else
                        {
                            // Enemy ground: amber where the next battle is, dark red further on.
                            var tint = next ? new Color(1f, .62f, .25f, .3f) : new Color(.55f, .08f, .06f, .5f);
                            Put(diamond, pos, -2500 + x + y, tint, .96f);
                        }
                    }
                if (!free && barricade != null)
                    foreach (var (dx, dy) in new[] { (-1, 1), (1, -1), (0, 2), (-2, 0) })
                        Put(barricade, TileWorld(cx + dx, cy + dy) + lift, Depth(cx + dx, cy + dy), Color.white);
                if (sector.Number == 1 && ship != null) Put(ship, TileWorld(cx, cy) + lift, Depth(cx + 1, cy + 1) + 1, Color.white, 1.35f);
            }

            foreach (var b in Colony.Saved)
            {
                if (!System.Enum.TryParse(b.type, out BuildingType type) || !buildings.TryGetValue(type, out var sprite) || sprite == null) continue;
                var r = Put(sprite, TileWorld(b.x, b.y) + lift, Depth(b.x, b.y) + 2, Color.white, .92f + b.level * .06f);
                placed[new Vector2Int(b.x, b.y)] = r.transform;
            }
            selection = Put(outline, Vector3.zero, -2000, new Color(1, .83f, .55f, 1));
            selection.gameObject.SetActive(false);
            UpdateHint();
        }

        void UpdateHint()
        {
            var next = System.Array.Find(CampaignData.Sectors, s => CampaignDirector.Unlocked(s) && !SaveGame.Completed(s.Id));
            bool anyFree = System.Array.Exists(CampaignData.Sectors, s => SaveGame.Completed(s.Id));
            Hint = anyFree ? "Toca una casilla libre de una zona liberada para construir · arrastra para moverte"
                : next != null ? $"Toca «{next.Name}» para liberar la zona · arrastra para moverte" : "";
        }

        /// <summary>Highlights a tile, or nothing with null.</summary>
        public void Select(Vector2Int? tile)
        {
            if (selection == null) return;
            selection.gameObject.SetActive(tile.HasValue);
            if (tile.HasValue) selection.transform.position = TileWorld(tile.Value.x, tile.Value.y);
        }

        /// <summary>Plays a short bounce on a building that was just built or upgraded.</summary>
        public void Pop(int x, int y)
        {
            popTile = new Vector2Int(x, y);
            popAt = Time.unscaledTime;
        }

        // ---- Camera and input -----------------------------------------------------------

        /// <summary>Called every frame while the map is open, instead of the battle board.</summary>
        public void Render()
        {
            if (!Active) return;
            cam.orthographicSize = zoom;
            cam.transform.position = Origin + new Vector3(center.x, center.y, -10);
            float t = Time.unscaledTime - popAt;
            if (t < .45f && placed.TryGetValue(popTile, out var tr))
            {
                var b = Colony.At(popTile.x, popTile.y);
                float baseScale = .92f + (b?.level ?? 1) * .06f;
                tr.localScale = Vector3.one * baseScale * (1 + Mathf.Sin(t / .45f * Mathf.PI) * .18f);
            }
            if (selection != null && selection.gameObject.activeSelf)
                selection.color = new Color(1, .83f, .55f, .6f + .4f * Mathf.PingPong(Time.unscaledTime * 2, 1));
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

        /// <summary>Keeps the view inside the rendered terrain (28 x 15 units around the map center) at any zoom.</summary>
        void Clamp()
        {
            const float HalfW = 14, HalfH = 7.5f;
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            zoom = Mathf.Min(zoom, HalfH, HalfW / aspect);
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
    }
}
