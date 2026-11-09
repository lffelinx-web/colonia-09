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
        int W => game.Sim.Width;
        int H => game.Sim.Height;
        bool StandardBoard => W == GameData.Width && H == GameData.Height;
        // Pieces leave a margin inside their cell so a full board still shows the deck between them.
        const float ModuleScale = .82f, EnemyScale = .88f;
        const int OrderBoard = 0, OrderWreck = 5, OrderLinks = 10, OrderModules = 20, OrderMarks = 25, OrderEnemies = 30, OrderBars = 40, OrderFx = 50;

        GameController game;
        Camera cam;
        Material material;
        Sprite square, circle, ring;
        readonly Dictionary<ModuleType, Sprite> moduleSprites = new Dictionary<ModuleType, Sprite>();
        readonly Dictionary<EnemyKind, Sprite[]> enemySprites = new Dictionary<EnemyKind, Sprite[]>();
        Sprite boardSprite, generatorBase, generatorFan, pulseBase, pulseBarrel, cryoBase, cryoTurret, helicopterRotor;
        readonly Dictionary<EnemyKind, Sprite> enemyTurrets = new Dictionary<EnemyKind, Sprite>();
        readonly Dictionary<GameModule, float> fanAngle = new Dictionary<GameModule, float>();
        readonly Dictionary<GameModule, float> moduleAim = new Dictionary<GameModule, float>();
        readonly Dictionary<int, float> enemyAim = new Dictionary<int, float>();
        readonly Dictionary<GameModule, float> moduleLastHp = new Dictionary<GameModule, float>();
        readonly Dictionary<GameModule, float> moduleHitUntil = new Dictionary<GameModule, float>();
        readonly Dictionary<int, bool> enemyWasCharging = new Dictionary<int, bool>();
        readonly Dictionary<int, float> enemyKickUntil = new Dictionary<int, float>();
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
            foreach (EnemyKind k in System.Enum.GetValues(typeof(EnemyKind)))
            {
                var frames = new List<Sprite>();
                for (int i = 0; i < 4; i++)
                {
                    var frame = Resources.Load<Sprite>($"Sprites/Enemies/{k.ToString().ToLowerInvariant()}_{i}");
                    if (frame != null) frames.Add(frame);
                }
                enemySprites[k] = frames.ToArray();
            }
            boardSprite = Resources.Load<Sprite>("Sprites/Board/board");
            generatorBase = Resources.Load<Sprite>("Sprites/Modules/generator_base");
            generatorFan = Resources.Load<Sprite>("Sprites/Modules/generator_fan");
            pulseBase = Resources.Load<Sprite>("Sprites/Modules/pulse_base");
            pulseBarrel = Resources.Load<Sprite>("Sprites/Modules/pulse_barrel");
            cryoBase = Resources.Load<Sprite>("Sprites/Modules/cryo_base");
            cryoTurret = Resources.Load<Sprite>("Sprites/Modules/cryo_turret");
            helicopterRotor = Resources.Load<Sprite>("Sprites/Enemies/helicopter_rotor");
            foreach (var k in new[] { EnemyKind.Tank, EnemyKind.Hunter, EnemyKind.Commander })
            {
                var turret = Resources.Load<Sprite>($"Sprites/Enemies/{k.ToString().ToLowerInvariant()}_turret");
                if (turret != null) enemyTurrets[k] = turret;
            }
        }

        static Vector2 Rotate(Vector2 v, float degrees) => Quaternion.Euler(0, 0, degrees) * v;

        // ---- Aiming -------------------------------------------------------------------
        // Turrets turn toward what they are about to hit. Presentation only: the rules never read these angles.

        bool RecentBeamFrom(double x, double y, double tolerance, out Vector2 target)
        {
            target = default;
            FxEvent best = null;
            foreach (var fx in game.Sim.Effects)
                if (fx.Type == "beam" && fx.HasTarget && fx.Age < .45 && System.Math.Abs(fx.X - x) < .01 && System.Math.Abs(fx.Y - y) < tolerance
                    && (best == null || fx.Age < best.Age)) best = fx;
            if (best == null) return false;
            target = new Vector2((float)best.X2, -(float)best.Y2);
            return true;
        }

        bool ModuleAimTarget(GameModule m, out Vector2 target)
        {
            target = default;
            if (RecentBeamFrom(m.C, m.R, .01, out target)) return true;
            if (!m.Powered || m.IsDeploying) return false;
            Enemy best = null;
            foreach (var e in game.Sim.Enemies)
            {
                bool inRange = m.Type == ModuleType.Pulse
                    ? e.C == m.C && e.Y < m.R - .15 && e.Y > m.R - 5.9
                    : System.Math.Abs(e.C - m.C) <= 1 && e.Y < m.R + .8 && e.Y > m.R - 4.3;
                if (inRange && e.Hp > 0 && (best == null || e.Y > best.Y)) best = e;
            }
            if (best == null) return false;
            target = new Vector2(best.C, -(float)best.Y);
            return true;
        }

        float ModuleAim(GameModule m, Vector2 pos)
        {
            moduleAim.TryGetValue(m, out var angle);
            float goal = 0; // Idle: facing the threat zone.
            if (ModuleAimTarget(m, out var target))
            {
                var d = target - pos;
                if (d.sqrMagnitude > .01f) goal = Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg;
            }
            angle = Mathf.MoveTowardsAngle(angle, goal, Time.unscaledDeltaTime * 320);
            moduleAim[m] = angle;
            return angle;
        }

        bool EnemyAimTarget(Enemy e, out Vector2 target)
        {
            target = default;
            int key = -1;
            if (e.StrikeWind > 0 && e.StrikeTargets.Count > 0)
            {
                var sum = Vector2.zero;
                foreach (int k in e.StrikeTargets) sum += new Vector2(k % W, -(k / W));
                target = sum / e.StrikeTargets.Count;
                return true;
            }
            if (e.Kind == EnemyKind.Hunter && e.HuntWind > 0) key = e.HuntTarget;
            else if (e.Kind == EnemyKind.Commander && e.BossWind > 0) key = e.BossTarget;
            if (key >= 0)
            {
                target = new Vector2(key % W, -(key / W));
                return true;
            }
            return RecentBeamFrom(e.C, e.Y, .7, out target);
        }

        float EnemyAim(Enemy e, Vector2 pos)
        {
            enemyAim.TryGetValue(e.Id, out var angle);
            float goal = 0; // Idle: cannon forward, toward the base.
            if (EnemyAimTarget(e, out var target))
            {
                var d = target - pos;
                if (d.sqrMagnitude > .01f) goal = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
            }
            angle = Mathf.MoveTowardsAngle(angle, goal, Time.unscaledDeltaTime * 200);
            enemyAim[e.Id] = angle;
            return angle;
        }

        public Sprite ModuleSprite(ModuleType type) => moduleSprites[type];
        public Sprite EnemySprite(EnemyKind kind) => enemySprites[kind].Length > 0 ? enemySprites[kind][0] : null;

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
            DrawReach(t);
            DrawLinks(t);
            DrawModules(t);
            DrawEnemies(t);
            DrawEffects();
            DrawHighlight(t);
            for (int i = used; i < pool.Count; i++)
                if (pool[i].enabled) pool[i].enabled = false;
        }

        void FitCamera(Rect field)
        {
            if (field.width <= 1 || field.height <= 1) return;
            cellPixels = Mathf.Min(field.width / W, field.height / H);
            // Small campaign boards stay close to the normal piece size instead of filling the screen.
            float standard = Mathf.Min(field.width / GameData.Width, field.height / GameData.Height);
            cellPixels = Mathf.Min(cellPixels, standard * 1.3f);
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

        readonly Dictionary<string, Sprite> campaignBoards = new Dictionary<string, Sprite>();

        /// <summary>Rendered deck for a campaign board size (Tools/Render/render_board.py WxH), or null.</summary>
        Sprite CampaignBoard()
        {
            string key = $"board_{W}x{H}";
            if (!campaignBoards.TryGetValue(key, out var sprite))
                campaignBoards[key] = sprite = Resources.Load<Sprite>("Sprites/Board/" + key);
            return sprite;
        }

        /// <summary>Cell the tutorial points at, drawn as a pulsing frame; null for none.</summary>
        public Vector2Int? HighlightCell;

        /// <summary>
        /// While a card is chosen and no piece is open, empty cells where that module would connect get a soft
        /// green mark, so the player sees where it will work before spending energy.
        /// </summary>
        float reachUntil;
        /// <summary>Keeps the green reach marks on while a tutorial step explains them.</summary>
        public bool ForceReach;

        /// <summary>Shows the green reach marks for a few seconds, fading out at the end.</summary>
        public void FlashReach(float seconds) => reachUntil = Time.unscaledTime + seconds;

        void DrawReach(float t)
        {
            var sim = game.Sim;
            if (sim.Mode != GameMode.Playing || game.Selected != null || game.SelectedType == ModuleType.Wall) return;
            float left = reachUntil - Time.unscaledTime;
            if (!ForceReach && left <= 0) return;
            var type = game.SelectedType;
            float fade = ForceReach ? 1 : Mathf.Clamp01(left / .8f);
            float glow = (.12f + .05f * Mathf.Sin(t * 3)) * fade;
            var nodes = sim.ConnectionNodes(type);  // Once per frame, then a cheap distance test per cell.
            for (int r = 1; r < H - 1; r++)
                for (int c = 0; c < W; c++)
                {
                    if (sim.ModuleAt(r, c) != null) continue;
                    bool reach = type == ModuleType.Reactor;
                    for (int i = 0; i < nodes.Count && !reach; i++) reach = Mathf.Abs(nodes[i].R - r) + Mathf.Abs(nodes[i].C - c) <= 2;
                    if (reach) Draw(square, new Vector2(c, -r), Vector2.one * .9f, new Color(.45f, .95f, .7f, glow), OrderBoard + 4);
                }
        }

        void DrawHighlight(float t)
        {
            if (HighlightCell == null) return;
            var cell = HighlightCell.Value;
            var pos = new Vector2(cell.y, -cell.x);
            float pulse = Mathf.PingPong(t * 1.6f, 1);
            Draw(square, pos, Vector2.one * .96f, new Color(1, .83f, .55f, .12f + pulse * .12f), OrderMarks);
            Draw(ring, pos, Vector2.one * (1.02f + pulse * .16f), new Color(1, .83f, .55f, .95f - pulse * .5f), OrderMarks + 1);
        }

        void DrawBoard(float t)
        {
            // Campaign missions on the full 7 x 11 size still stand on their outpost deck.
            var deck = StandardBoard && game.Sim.Level.IsQuickPlay ? boardSprite : CampaignBoard() ?? (StandardBoard ? boardSprite : null);
            if (deck != null)
            {
                // Rendered deck and colony scenery; it is wider than the grid so it fills the sides of the screen.
                Draw(deck, new Vector2((W - 1) / 2f, -(H - 1) / 2f), Vector2.one, Color.white, OrderBoard);
            }
            else
            {
                // A board size without a rendered deck: plated tiles drawn in code.
                Rect((W - 1) / 2f, -(H - 1) / 2f, W + .24f, H + .24f, Hex("#1d2a33"), OrderBoard);
                for (int r = 0; r < H; r++)
                    for (int c = 0; c < W; c++)
                    {
                        var tile = r == 0 ? Hex("#3a2a2e") : r == H - 1 ? Hex("#1f3a35") : (r + c) % 2 == 0 ? Hex("#2c3d48") : Hex("#283844");
                        Rect(c, -r, .94f, .94f, tile, OrderBoard + 1);
                    }
                Rect((W - 1) / 2f, -.47f, W - .1f, .05f, Hex("#e0ac1a"), OrderBoard + 2);
                Rect((W - 1) / 2f, -(H - 1) + .47f, W - .1f, .04f, Hex("#5fd39e"), OrderBoard + 2);
            }

            var assault = game.Sim.Assault;
            if (assault != null)
                Rect(assault.Lane, -(H - 1) / 2f, 1, H, new Color(1f, .55f, .35f, .08f + .05f * Mathf.Sin(t * 5) * Mathf.Sin(t * 5)), OrderBoard + 3);

            if (game.Sim.Mode != GameMode.Idle)
            {
                game.Ui.WorldLabel("ZONA DE AMENAZA", Alpha(Hex("#a7bed0"), .32f), (W - 1) / 2.0, 0, 26);
                game.Ui.WorldLabel("BASE CENTRAL · COLONIA 09", Alpha(Hex("#aef0d3"), .5f), (W - 1) / 2.0, H - 1, 26);
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

                float hp = (float)m.Hp;
                if (moduleLastHp.TryGetValue(m, out var lastHp) && hp < lastHp - .001f) moduleHitUntil[m] = t + .16f;
                moduleLastHp[m] = hp;
                bool beingHit = moduleHitUntil.TryGetValue(m, out var hitUntil) && t < hitUntil;
                if (beingHit) pos += new Vector2(Mathf.Sin(t * 83 + m.C), Mathf.Cos(t * 97 + m.R)) * .025f;

                // Firing: the whole module dips slightly, as in the prototype.
                float fire = Mathf.Clamp01((float)m.Fire / .18f);
                pos.y -= fire * .05f;
                if (m.Type == ModuleType.Generator && generatorBase != null && generatorFan != null)
                {
                    // Fan turns slowly and continuously; speed rises with level. Stops without power.
                    fanAngle.TryGetValue(m, out var angle);
                    if (m.Powered && !m.IsDeploying && !jam) angle += Time.unscaledDeltaTime * (38 + m.Level * 16);
                    fanAngle[m] = angle;
                    Draw(generatorBase, pos, Vector2.one * ModuleScale, color, OrderModules);
                    Draw(generatorFan, pos, Vector2.one * ModuleScale, color, OrderModules + 1, -angle);
                }
                float aim = 0;
                if (m.Type == ModuleType.Pulse && pulseBase != null && pulseBarrel != null)
                {
                    // The turret turns to its target; the barrel slides back along its line when it fires.
                    aim = ModuleAim(m, pos);
                    Draw(pulseBase, pos, Vector2.one * ModuleScale, color, OrderModules);
                    Draw(pulseBarrel, pos + Rotate(new Vector2(0, -fire * .1f), aim), Vector2.one * ModuleScale, color, OrderModules + 1, aim);
                }
                else if (m.Type == ModuleType.Cryo && cryoBase != null && cryoTurret != null)
                {
                    aim = ModuleAim(m, pos);
                    Draw(cryoBase, pos, Vector2.one * ModuleScale, color, OrderModules);
                    Draw(cryoTurret, pos + Rotate(new Vector2(0, -fire * .05f), aim), Vector2.one * ModuleScale, color, OrderModules + 1, aim);
                }
                else
                {
                    Draw(moduleSprites[m.Type], pos, Vector2.one * ModuleScale, color, OrderModules);
                }
                if (fire > 0 && (m.Type == ModuleType.Pulse || m.Type == ModuleType.Cryo || m.Type == ModuleType.Tesla))
                {
                    var bloom = m.Type == ModuleType.Tesla ? Hex("#caa4ff") : m.Type == ModuleType.Cryo ? Hex("#a9efff") : Hex("#ffe7a7");
                    float reach = m.Type == ModuleType.Pulse ? .38f - fire * .1f : m.Type == ModuleType.Cryo ? .36f : 0;
                    var muzzle = pos + Rotate(new Vector2(0, reach), aim);
                    Draw(circle, muzzle, Vector2.one * (.18f + fire * .26f), Alpha(bloom, fire * .7f), OrderMarks);
                    Draw(circle, muzzle, Vector2.one * (.08f + fire * .1f), new Color(1, 1, 1, fire), OrderMarks + 1);
                }
                if (beingHit) Draw(circle, pos, Vector2.one * .86f, new Color(1, .82f, .62f, .36f), OrderMarks);
                if (!m.IsDeploying && m.Hp < m.MaxHp * .35) DrawSmoke(pos, m.C * 3 + m.R * 7, .5f, t);

                if (m == game.Selected)
                    Draw(ring, pos, Vector2.one * .98f, Alpha(Hex("#ffd38b"), .7f + .3f * Mathf.Sin(t * 6)), OrderMarks);

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
            if (k == EnemyKind.Commander) return .64f;
            return k == EnemyKind.Armor || k == EnemyKind.Tank || k == EnemyKind.Bomber || k == EnemyKind.Helicopter ? .57f : .51f;
        }

        void DrawSmoke(Vector2 pos, int seed, float strength, float t)
        {
            for (int i = 0; i < 3; i++)
            {
                float life = Mathf.Repeat(t * .55f + i / 3f + seed * .13f, 1);
                var puff = pos + new Vector2(Mathf.Sin(seed + i * 2.1f + life * 3) * .14f, life * .5f);
                Draw(circle, puff, Vector2.one * (.1f + life * .22f), new Color(.1f, .12f, .14f, (1 - life) * strength), OrderFx - 1);
            }
        }

        static float ChargeProgress(Enemy e)
        {
            // Telegraphed attacks: how close the shot is, from 0 to 1.
            if (e.StrikeWind > 0) return 1 - (float)(e.StrikeWind / (e.Kind == EnemyKind.Bomber ? 2.6 : 2.0));
            if (e.HuntWind > 0) return 1 - (float)(e.HuntWind / 1.95);
            if (e.BossWind > 0) return 1 - (float)(e.BossWind / 2.0);
            if (e.CounterWind > 0) return 1 - (float)(e.CounterWind / 1.55);
            return -1;
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
                bool air = GameData.IsAir(e.Kind);
                float ph = t * 4 + (float)e.Phase;
                var pos = new Vector2(e.C, -(float)e.Y);
                if (air) pos.y += Mathf.Sin(ph * .6f) * .025f;

                // Movement: animation speed follows what the enemy is doing.
                float fps = e.Kind == EnemyKind.Runner ? 15 : air ? 14 : 9;
                if (e.Slow > 0) fps *= .4f;
                if (e.ContactTarget != null) fps *= .45f;

                // Ramming: it pulls back and strikes the module in front of it, throwing sparks.
                if (e.ContactTarget != null)
                {
                    float swing = Mathf.PingPong(t * 3.4f + (float)e.Phase, 1);
                    swing *= swing;
                    pos.y -= swing * .11f - .03f;
                    if (swing > .55f)
                    {
                        var contact = new Vector2(e.C, -(float)e.Y - .42f);
                        for (int i = 0; i < 5; i++)
                        {
                            float a = t * 37 + i * 1.9f + e.Id;
                            var spark = contact + new Vector2(Mathf.Cos(a) * .22f, Mathf.Abs(Mathf.Sin(a * 1.3f)) * .16f - .06f) * swing;
                            Draw(circle, spark, Vector2.one * (.035f + .03f * Mathf.Abs(Mathf.Sin(a))), i % 2 == 0 ? new Color(1, .86f, .5f, 1) : new Color(1, .55f, .25f, 1), OrderFx);
                        }
                    }
                }

                // Telegraphed shot: energy gathers at the muzzle, then the enemy kicks back when it fires.
                float charge = ChargeProgress(e);
                bool charging = charge >= 0;
                enemyWasCharging.TryGetValue(e.Id, out var wasCharging);
                if (wasCharging && !charging) enemyKickUntil[e.Id] = t + .26f;
                enemyWasCharging[e.Id] = charging;
                if (charging) pos.y += charge * .05f + Mathf.Sin(t * 60) * charge * .008f;
                if (enemyKickUntil.TryGetValue(e.Id, out var kickUntil) && t < kickUntil) pos.y += (kickUntil - t) / .26f * .16f;
                if (e.Hit > 0) pos.y += (float)(e.Hit / .2) * .035f;

                DrawThreats(e, pos, t);

                var tint = e.Slow > 0 ? new Color(.72f, .92f, 1f, 1f) : Color.white;
                int order = OrderEnemies + (air ? 4 : 0);
                float rot = e.Kind == EnemyKind.Helicopter ? Mathf.Sin(t * 1.7f + e.Id) * 3 : 0;
                var frames = enemySprites[e.Kind];
                Draw(frames[Mathf.FloorToInt(t * fps + (float)e.Phase * 2) % frames.Length], pos, Vector2.one * EnemyScale, tint, order, rot);
                float aim = 0;
                if (enemyTurrets.TryGetValue(e.Kind, out var turret))
                {
                    // The cannon swings toward the module it is about to hit.
                    aim = EnemyAim(e, pos);
                    Draw(turret, pos, Vector2.one * EnemyScale, tint, order + 1, aim);
                }
                if (e.Kind == EnemyKind.Helicopter && helicopterRotor != null)
                {
                    float spin = t * 600 + e.Id * 37;
                    Draw(helicopterRotor, pos, Vector2.one * EnemyScale, new Color(tint.r, tint.g, tint.b, .35f), order + 1, rot - spin + 14);
                    Draw(helicopterRotor, pos, Vector2.one * EnemyScale, tint, order + 2, rot - spin);
                }
                if (e.Hit > 0) Draw(circle, pos, Vector2.one * .56f, new Color(1, 1, 1, (float)(e.Hit / .2) * .3f), order + 1);

                if (charging)
                {
                    var muzzle = pos + Rotate(new Vector2(0, -.46f), aim);
                    var chargeColor = e.Kind == EnemyKind.Bomber ? Hex("#ff7a3c") : EnemyBarColor(e);
                    Draw(circle, muzzle, Vector2.one * (.08f + charge * .3f), Alpha(chargeColor, .35f + charge * .35f), order + 2);
                    Draw(circle, muzzle, Vector2.one * (.04f + charge * .14f), new Color(1, 1, 1, .5f + charge * .5f), order + 3);
                }
                if (e.Hp < e.MaxHp * .4f) DrawSmoke(pos, e.Id, .55f, t);

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
                // A projectile that travels from the shooter to its target, with a muzzle flash and a fading trail.
                // It leaves from the barrel tip, which now points at the target.
                if ((to - from).magnitude > .8f) from += (to - from).normalized * .36f;
                float p = 1 - a;
                float travel = Mathf.Clamp01(p * 1.7f);
                var head = Vector2.Lerp(from, to, travel);
                Line(Vector2.Lerp(from, to, Mathf.Clamp01(travel - .4f)), head, width + .02f, Alpha(col, a * .55f), OrderFx);
                Draw(circle, head, Vector2.one * (.14f + width), Alpha(col, Mathf.Clamp01(a * 2)), OrderFx + 1);
                Draw(circle, head, Vector2.one * .07f, new Color(1, 1, 1, Mathf.Clamp01(a * 2)), OrderFx + 2);
                if (p < .25f) Draw(circle, from, Vector2.one * (.3f * (1 - p * 4) + .08f), new Color(1, .95f, .8f, (1 - p * 4) * .9f), OrderFx + 1);
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
