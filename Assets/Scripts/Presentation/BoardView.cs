using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;

namespace Colonia09.Presentation
{
    /// <summary>
    /// Draws the battlefield. Every frame it reuses a pool of sprite renderers, so it mirrors
    /// the immediate-mode canvas of the prototype. One cell is one world unit: a cell at
    /// (row, column) is centered on (column, -row).
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        const int W = GameData.Width, H = GameData.Height;
        const int OrderBoard = 0, OrderWreck = 5, OrderLinks = 10, OrderModules = 20, OrderMarks = 25, OrderEnemies = 30, OrderBars = 40, OrderFx = 50;

        GameController game;
        Camera cam;
        Material material;
        Sprite square, circle, ring;
        readonly Dictionary<ModuleType, Sprite> moduleSprites = new Dictionary<ModuleType, Sprite>();
        readonly Dictionary<EnemyKind, Sprite> enemySprites = new Dictionary<EnemyKind, Sprite>();
        readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
        int used;
        float shakeUntil, shakePower, cellPixels = 100;
        Vector3 cameraBase;

        public void Init(GameController controller)
        {
            game = controller;
            cam = Camera.main;
            cam.orthographic = true;
            cam.backgroundColor = Hex("#071622");
            cam.clearFlags = CameraClearFlags.SolidColor;
            material = Resources.Load<Material>("Materials/SpriteUnlit");
            square = MakeSquare();
            circle = MakeCircle(128, false);
            ring = MakeCircle(128, true);
            foreach (var t in GameData.ModuleTypes) moduleSprites[t] = Resources.Load<Sprite>("Sprites/Modules/" + t.ToString().ToLowerInvariant());
            foreach (EnemyKind k in System.Enum.GetValues(typeof(EnemyKind))) enemySprites[k] = Resources.Load<Sprite>("Sprites/Enemies/" + k.ToString().ToLowerInvariant());
        }

        public Sprite ModuleSprite(ModuleType type) => moduleSprites[type];

        public void Shake(float power)
        {
            shakeUntil = Time.unscaledTime + .24f;
            shakePower = power;
        }

        public bool ScreenToCell(Vector2 screen, out int r, out int c)
        {
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            world -= cam.transform.position - cameraBase; // Ignore camera shake.
            c = Mathf.RoundToInt(world.x);
            r = Mathf.RoundToInt(-world.y);
            return r >= 0 && r < H && c >= 0 && c < W;
        }

        public Vector2 CellToScreen(int r, int c) => cam.WorldToScreenPoint(new Vector3(c, -r, 0));
        public Vector2 WorldToScreen(double x, double row) => cam.WorldToScreenPoint(new Vector3((float)x, -(float)row, 0));
        public float CellPixels => cellPixels;

        public void Render(Rect field)
        {
            FitCamera(field);
            used = 0;
            float t = Time.unscaledTime;
            DrawBoard(t);
            DrawLinks(t);
            DrawModules(t);
            DrawEnemies(t);
            DrawEffects();
            for (int i = used; i < pool.Count; i++)
                if (pool[i].enabled) pool[i].enabled = false;
        }

        void FitCamera(Rect field)
        {
            if (field.width <= 1 || field.height <= 1) return;
            cellPixels = Mathf.Min(field.width / W, field.height / H);
            cam.orthographicSize = Screen.height / (2f * cellPixels);
            float x = (W - 1) / 2f - (field.center.x - Screen.width / 2f) / cellPixels;
            float y = -(H - 1) / 2f - (field.center.y - Screen.height / 2f) / cellPixels;
            cameraBase = new Vector3(x, y, -10);
            var offset = Vector3.zero;
            if (Time.unscaledTime < shakeUntil)
            {
                float now = Time.unscaledTime * 1000f;
                float amount = Mathf.Clamp01((shakeUntil - Time.unscaledTime) / .24f) * shakePower / 60f;
                offset = new Vector3(Mathf.Sin(now * .079f), Mathf.Cos(now * .093f) * .62f, 0) * amount;
            }
            cam.transform.position = cameraBase + offset;
        }

        // ---- Drawing primitives -------------------------------------------------------

        SpriteRenderer Draw(Sprite sprite, Vector2 position, Vector2 scale, Color color, int order, float rotation = 0)
        {
            SpriteRenderer r;
            if (used < pool.Count) r = pool[used];
            else
            {
                var go = new GameObject("Draw");
                go.transform.SetParent(transform, false);
                r = go.AddComponent<SpriteRenderer>();
                if (material != null) r.sharedMaterial = material;
                pool.Add(r);
            }
            used++;
            r.enabled = true;
            r.sprite = sprite;
            r.color = color;
            r.sortingOrder = order;
            var tr = r.transform;
            tr.localPosition = position;
            tr.localScale = new Vector3(scale.x, scale.y, 1);
            tr.localRotation = rotation != 0 ? Quaternion.Euler(0, 0, rotation) : Quaternion.identity;
            return r;
        }

        void Rect(float x, float y, float w, float h, Color color, int order) =>
            Draw(square, new Vector2(x, y), new Vector2(w, h), color, order);

        void Line(Vector2 a, Vector2 b, float width, Color color, int order)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            Draw(square, (a + b) / 2, new Vector2(d.magnitude, width), color, order, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        void Bar(float x, float y, float width, float value, Color fill)
        {
            Rect(x, y, width + .03f, .075f, new Color(.03f, .07f, .11f, .95f), OrderBars);
            float w = width * Mathf.Clamp01(value);
            if (w > 0) Draw(square, new Vector2(x - (width - w) / 2, y), new Vector2(w, .045f), fill, OrderBars + 1);
        }

        Color Hex(string html)
        {
            if (colors.TryGetValue(html, out var c)) return c;
            ColorUtility.TryParseHtmlString(html, out c);
            colors[html] = c;
            return c;
        }

        static Color Alpha(Color c, float a) { c.a = a; return c; }

        // ---- Board --------------------------------------------------------------------

        void DrawBoard(float t)
        {
            Rect((W - 1) / 2f, -(H - 1) / 2f, W, H, Hex("#0a2436"), OrderBoard);
            Rect((W - 1) / 2f, 0, W, 1, Hex("#3a2230"), OrderBoard + 1);
            Rect((W - 1) / 2f, -(H - 1), W, 1, Hex("#123a39"), OrderBoard + 1);
            for (int r = 1; r < H - 1; r++)
                for (int c = 0; c < W; c++)
                    if ((r + c) % 2 == 0) Rect(c, -r, 1, 1, Hex("#0e2c41"), OrderBoard + 1);
            var line = new Color(.53f, .78f, .85f, .10f);
            for (int c = 0; c <= W; c++) Rect(c - .5f, -(H - 1) / 2f, .018f, H, line, OrderBoard + 2);
            for (int r = 0; r <= H; r++) Rect((W - 1) / 2f, -r + .5f, W, .018f, line, OrderBoard + 2);

            var assault = game.Sim.Assault;
            if (assault != null)
                Rect(assault.Lane, -(H - 1) / 2f, 1, H, new Color(1f, .55f, .35f, .08f + .05f * Mathf.Sin(t * 5) * Mathf.Sin(t * 5)), OrderBoard + 3);

            if (game.Sim.Mode != GameMode.Idle)
            {
                game.Ui.WorldLabel("ZONA DE AMENAZA", Hex("#a7bed0"), (W - 1) / 2.0, 0, 30);
                game.Ui.WorldLabel("BASE CENTRAL · COLONIA 09", Hex("#aef0d3"), (W - 1) / 2.0, H - 1, 30);
            }

            for (int i = 0; i < game.Sim.Effects.Count; i++)
                if (game.Sim.Effects[i].Type == "wreck") DrawWreck(game.Sim.Effects[i]);
        }

        void DrawWreck(FxEvent fx)
        {
            float p = (float)(fx.Age / fx.Duration);
            float a = Mathf.Clamp01((1 - p) * 3) * .6f;
            var pos = new Vector2((float)fx.X, -(float)fx.Y);
            Draw(circle, pos, Vector2.one * .78f * (float)fx.Power, Alpha(Hex("#050d14"), a), OrderWreck);
            for (int i = 0; i < 5; i++)
            {
                float angle = (float)fx.Seed + i * 2.4f;
                Draw(square, pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (.13f + i * .05f), Vector2.one * (.05f + i * .012f),
                    Alpha(Hex(fx.Color), a), OrderWreck + 1, angle * 57f);
            }
        }

        void DrawLinks(float t)
        {
            foreach (var link in game.Sim.Links)
            {
                var a = new Vector2(link.A.C, -link.A.R);
                var b = new Vector2(link.B.C, -link.B.R);
                Line(a, b, .05f, new Color(.54f, .9f, .93f, .28f), OrderLinks);
                float k = Mathf.Repeat(t * .9f + (link.A.R + link.B.C) * .17f, 1);
                Draw(circle, Vector2.Lerp(a, b, k), Vector2.one * .11f, new Color(.78f, 1f, 1f, .9f), OrderLinks + 1);
            }
        }

        // ---- Modules ------------------------------------------------------------------

        void DrawModules(float t)
        {
            var sim = game.Sim;
            foreach (var m in sim.Modules)
            {
                var def = GameData.Modules[m.Type];
                var pos = new Vector2(m.C, -m.R);
                var color = Color.white;
                bool jam = sim.Jammed(m);
                if (m.IsDeploying) color = new Color(.75f, .9f, .95f, .55f);
                else if (jam) color = new Color(.78f, .6f, 1f, 1f);
                else if (!m.Powered && m.Type != ModuleType.Wall) color = new Color(.55f, .58f, .62f, 1f);

                float pop = 1 + (float)m.Fire * .35f;
                if (m.Type == ModuleType.Generator && m.Powered && !m.IsDeploying) pop += Mathf.Sin(t * 6 + m.C) * .012f;
                Draw(moduleSprites[m.Type], pos, Vector2.one * .96f * pop, color, OrderModules);

                if (m == game.Selected)
                    Draw(ring, pos, Vector2.one * 1.1f, Alpha(Hex("#ffd38b"), .7f + .3f * Mathf.Sin(t * 6)), OrderMarks);

                for (int i = 0; i < m.Level - 1; i++)
                    Draw(circle, pos + new Vector2(-.36f + i * .13f, -.38f), Vector2.one * .1f, Hex(def.Color), OrderMarks);

                if (m.IsDeploying)
                {
                    float progress = (float)(1 - m.DeployRemaining / m.DeployTotal);
                    Bar(pos.x, pos.y - .36f, .74f, progress, m.DeployPausedUntil > sim.Elapsed ? Hex("#ff8c8a") : Hex("#95ead9"));
                }
                else if (m.Hp < m.MaxHp - .5)
                {
                    float ratio = (float)(m.Hp / m.MaxHp);
                    Bar(pos.x, pos.y + .42f, .74f, ratio, ratio > .5f ? Hex("#91e5bd") : ratio > .25f ? Hex("#ffd38b") : Hex("#ff8c8a"));
                }
                if (jam) Draw(ring, pos, Vector2.one * (.82f + .1f * Mathf.Sin(t * 9)), new Color(.87f, .65f, 1f, .75f), OrderMarks);
            }
        }

        // ---- Enemies ------------------------------------------------------------------

        static float EnemyBarWidth(EnemyKind k)
        {
            if (k == EnemyKind.Commander) return .78f;
            return k == EnemyKind.Armor || k == EnemyKind.Sapper || k == EnemyKind.Bulwark || k == EnemyKind.Tank || k == EnemyKind.Bomber ? .63f : .53f;
        }

        static float EnemyBarOffset(EnemyKind k)
        {
            if (k == EnemyKind.Commander) return .60f;
            return k == EnemyKind.Armor || k == EnemyKind.Sapper || k == EnemyKind.Bulwark || k == EnemyKind.Tank || k == EnemyKind.Bomber ? .48f : .42f;
        }

        Color EnemyBarColor(Enemy e)
        {
            if (e.Slow > 0) return Hex("#91edff");
            if (GameData.Counters.TryGetValue(e.Kind, out var info)) return Hex(info.Color);
            switch (e.Kind)
            {
                case EnemyKind.Armor: return Hex("#ffce9d");
                case EnemyKind.Helicopter: return Hex("#a8ecfa");
                case EnemyKind.Bomber: return Hex("#ffd49a");
                default: return Hex("#ff9c93");
            }
        }

        void DrawEnemies(float t)
        {
            var sim = game.Sim;
            foreach (var e in sim.Enemies)
            {
                if (e.Y < -.6 || e.Y > H) continue;
                float ph = t * 4 + (float)e.Phase;
                var pos = new Vector2(e.C, -(float)e.Y + Mathf.Sin(ph) * .03f);
                DrawThreats(e, pos, t);

                var tint = e.Slow > 0 ? new Color(.72f, .92f, 1f, 1f) : Color.white;
                int order = OrderEnemies + (GameData.IsAir(e.Kind) ? 4 : 0);
                float rot = e.Kind == EnemyKind.Helicopter ? Mathf.Sin(t * 3 + e.Id) * 3 : 0;
                Draw(enemySprites[e.Kind], pos, Vector2.one, tint, order, rot);
                if (e.Hit > 0) Draw(circle, pos, Vector2.one * .72f, new Color(1, 1, 1, (float)(e.Hit / .2) * .4f), order + 1);

                float bw = EnemyBarWidth(e.Kind);
                float by = pos.y + EnemyBarOffset(e.Kind);
                Bar(pos.x, by, bw, (float)(e.Hp / e.MaxHp), EnemyBarColor(e));
                if (e.ArmorMax > 0 && e.Armor > 0) Bar(pos.x, by + .08f, bw, (float)(e.Armor / e.ArmorMax), Hex("#9adaff"));
                if (e.ArrivalShieldUntil > sim.Elapsed)
                    Draw(ring, pos, Vector2.one * .96f, new Color(.7f, .91f, 1f, .4f + .25f * Mathf.Sin(t * 6) * Mathf.Sin(t * 6)), order + 1);

                string tag = EnemyTag(e.Kind);
                if (tag != null) game.Ui.WorldLabel(tag, EnemyBarColor(e), e.C, e.Y - EnemyBarOffset(e.Kind) - .17, 20);
            }
        }

        static string EnemyTag(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Hunter: return "CAZADOR";
                case EnemyKind.Commander: return "CENTINELA";
                case EnemyKind.Tank: return "TANQUE";
                case EnemyKind.Helicopter: return "HELI";
                case EnemyKind.Bomber: return "BOMBARDERO";
                case EnemyKind.Bulwark: return "P";
                case EnemyKind.Thermal: return "C";
                case EnemyKind.Grounder: return "T";
                default: return null;
            }
        }

        void AimAt(Vector2 from, int key, string color, float radius, float t)
        {
            if (key < 0) return;
            int r = key / W, c = key % W;
            var m = game.Sim.ModuleAt(r, c);
            if (m == null) return;
            var col = Alpha(Hex(color), .55f + .3f * Mathf.Sin(t * 11) * Mathf.Sin(t * 11));
            var to = new Vector2(c, -r);
            int dashes = Mathf.Max(2, Mathf.CeilToInt((to - from).magnitude * 3.5f));
            for (int i = 0; i < dashes; i += 2)
                Line(Vector2.Lerp(from, to, i / (float)dashes), Vector2.Lerp(from, to, (i + 1) / (float)dashes), .035f, col, OrderMarks);
            Draw(ring, to, Vector2.one * radius * 2, col, OrderMarks + 1);
        }

        void DrawThreats(Enemy e, Vector2 pos, float t)
        {
            if (e.Kind == EnemyKind.Hunter && e.HuntWind > 0) AimAt(pos, e.HuntTarget, "#ffac82", .38f, t);
            if (e.Kind == EnemyKind.Commander && e.BossWind > 0) AimAt(pos, e.BossTarget, "#ffe1a5", .43f, t);
            if (e.CounterWind > 0 && GameData.Counters.TryGetValue(e.Kind, out var info)) AimAt(pos, e.CounterTarget, info.Color, .42f, t);
            if (e.StrikeWind > 0)
            {
                var col = new Color(1f, .42f, .3f, .22f + .22f * Mathf.Sin(t * 11) * Mathf.Sin(t * 11));
                foreach (int k in e.StrikeTargets)
                {
                    int r = k / W, c = k % W;
                    Rect(c, -r, .96f, .96f, col, OrderMarks);
                    if (e.Kind != EnemyKind.Bomber) Line(pos, new Vector2(c, -r), .03f, Alpha(col, .6f), OrderMarks);
                }
            }
        }

        // ---- Effects ------------------------------------------------------------------

        void DrawEffects()
        {
            foreach (var fx in game.Sim.Effects)
            {
                float p = (float)(fx.Age / fx.Duration);
                float a = 1 - p;
                float power = (float)fx.Power;
                var pos = new Vector2((float)fx.X, -(float)fx.Y);
                var col = Hex(fx.Color);
                switch (fx.Type)
                {
                    case "beam": DrawBeam(fx, pos, col, a); break;
                    case "ring":
                        Draw(ring, pos, Vector2.one * (.45f + p * .85f) * power, Alpha(col, a * .9f), OrderFx);
                        break;
                    case "impact":
                        Draw(circle, pos, Vector2.one * (.18f + p * .5f) * power, Alpha(col, a * .75f), OrderFx);
                        Draw(circle, pos, Vector2.one * (.08f + p * .16f) * power, new Color(1, 1, 1, a * .8f), OrderFx + 1);
                        break;
                    case "explode":
                    case "crash":
                        Draw(circle, pos, Vector2.one * (.28f + p * 1.05f) * power, Alpha(col, a * .5f), OrderFx);
                        Draw(ring, pos, Vector2.one * (.35f + p * 1.3f) * power, Alpha(col, a * .85f), OrderFx + 1);
                        for (int i = 0; i < 8; i++)
                        {
                            float angle = (float)fx.Seed + i * .785f;
                            Draw(circle, pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * p * .8f * power, Vector2.one * .09f * a, Alpha(Color.white, a), OrderFx + 2);
                        }
                        break;
                    case "emp":
                        for (int i = 0; i < 3; i++)
                            Draw(ring, pos, Vector2.one * Mathf.Repeat(p + i * .33f, 1) * 2.4f * power, Alpha(col, a * .7f), OrderFx);
                        break;
                    case "float":
                        game.Ui.WorldLabel(fx.Text, Alpha(col, a), fx.X, fx.Y - .35 - p * .3, 26);
                        break;
                    case "heal":
                        game.Ui.WorldLabel("+", Alpha(col, a), fx.X, fx.Y - p * .42, 44);
                        break;
                }
            }
        }

        void DrawBeam(FxEvent fx, Vector2 from, Color col, float a)
        {
            if (!fx.HasTarget) return;
            var to = new Vector2((float)fx.X2, -(float)fx.Y2);
            float width = .07f * (float)fx.Power * a;
            bool lightning = fx.Color == "#c8adff";
            if (!lightning)
            {
                Line(from, to, width + .02f, Alpha(col, a * .55f), OrderFx);
                Line(from, to, width * .4f, new Color(1, 1, 1, a), OrderFx + 1);
                return;
            }
            var previous = from;
            var normal = new Vector2(-(to - from).y, (to - from).x).normalized;
            const int Segments = 6;
            for (int i = 1; i <= Segments; i++)
            {
                float k = i / (float)Segments;
                float jitter = i == Segments ? 0 : Mathf.Sin((float)fx.Seed * 3.1f + i * 2.7f + Time.unscaledTime * 40) * .13f;
                var point = Vector2.Lerp(from, to, k) + normal * jitter;
                Line(previous, point, width + .02f, Alpha(col, a * .85f), OrderFx);
                previous = point;
            }
        }

        // ---- Generated sprites --------------------------------------------------------

        static Sprite MakeSquare()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4, 0, SpriteMeshType.FullRect);
        }

        static Sprite MakeCircle(int size, bool outlineOnly)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = (size - 1) / 2f, radius = size / 2f - 1;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float alpha = Mathf.Clamp01(radius - d);
                    if (outlineOnly) alpha *= Mathf.Clamp01(d - (radius - 5));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size, 0, SpriteMeshType.FullRect);
        }
    }
}
