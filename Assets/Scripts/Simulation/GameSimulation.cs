using System;
using System.Collections.Generic;
using System.Linq;

namespace Colonia09.Simulation
{
    /// <summary>
    /// Pure port of the Colonia 09 v0.9.1 rules. No UnityEngine dependency: the view reads
    /// this state and listens to the callbacks. Numbers are doubles and collections keep
    /// JavaScript's ordering so that traces match the prototype exactly (see Tools/Parity).
    /// </summary>
    public sealed class GameSimulation
    {
        const int W = GameData.Width, H = GameData.Height, MaxWave = GameData.MaxWave;

        // Callbacks for presentation. None of them affect gameplay.
        public Action<string> OnSfx;
        public Action<string, double> OnNotify;
        public Action<double> OnShake;
        public Action OnBoardChanged;
        public Action<bool> OnFinished;

        readonly List<GameModule> modules = new List<GameModule>();
        readonly GameModule[] grid = new GameModule[W * H];
        readonly Random visualRandom = new Random(9);

        public IReadOnlyList<GameModule> Modules => modules;
        public List<Enemy> Enemies { get; private set; } = new List<Enemy>();
        public List<FxEvent> Effects { get; } = new List<FxEvent>();
        public List<NetworkLink> Links { get; } = new List<NetworkLink>();

        public GameMode Mode = GameMode.Idle;
        public Difficulty Difficulty = Difficulty.Tactical;
        public int Wave { get; private set; } = 1;
        public double Charge { get; private set; }
        public double Core { get; private set; }
        public double EnergyCapacity { get; private set; }
        public double ChargingGeneration { get; private set; }
        public double ConsumptionRate { get; private set; }
        public int TotalKills { get; private set; }
        public double Elapsed { get; private set; }
        public int SpawnRemaining { get; private set; }
        public double WaveBreak { get; private set; }
        public double FrontDelay { get; private set; }
        public uint RandomSeed { get; private set; } = 271;
        public AssaultPlan Assault => ActiveAssault();

        int nextId, spawnIndex, waveGateAt, secondFront, assaultCount;
        double spawnClock, networkClock, lastCounterAttackAt, assaultCooldownUntil, nextEliteStrikeAt;
        double consumptionThisFrame, weakSectorUntil, weakSectorNotice, lastCoreDangerAt;
        int weakSector;
        bool armorShotNotice, gridOnline;
        AssaultPlan assaultPlan;
        double[] breachPressure = new double[W];
        double[] recentAssaultLanes = new double[W];
        readonly Dictionary<int, double> strategicTargets = new Dictionary<int, double>();
        readonly HashSet<EnemyKind> counterSeen = new HashSet<EnemyKind>();

        DifficultyDef Conf => GameData.Difficulties[Difficulty];
        WavePlan Plan => GameData.WavePlans[Math.Min(MaxWave, Math.Max(1, Wave))];

        static int Key(int r, int c) => r * W + c;
        public GameModule ModuleAt(int r, int c) => r < 0 || r >= H || c < 0 || c >= W ? null : grid[Key(r, c)];
        GameModule ModuleByKey(int k) => k < 0 ? null : grid[k];
        static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));
        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
        static double JsRound(double v) => Math.Floor(v + .5);

        static List<T> Sorted<T>(IEnumerable<T> items, Comparison<T> cmp) =>
            items.OrderBy(x => x, Comparer<T>.Create(cmp)).ToList(); // Stable, like Array.prototype.sort.

        static int Sign(double d) => d < 0 ? -1 : d > 0 ? 1 : 0;

        double Rng()
        {
            unchecked { RandomSeed = RandomSeed * 1664525u + 1013904223u; }
            return RandomSeed / 4294967296.0;
        }

        void Sfx(string name) => OnSfx?.Invoke(name);
        void Notify(string message, double duration = 1.7) => OnNotify?.Invoke(message, duration);
        void BoardChanged() => OnBoardChanged?.Invoke();

        void AddModule(GameModule m) { modules.Add(m); grid[Key(m.R, m.C)] = m; }
        void DeleteModule(GameModule m) { modules.Remove(m); grid[Key(m.R, m.C)] = null; }

        // ---- Lifecycle ----------------------------------------------------------------

        public void Reset()
        {
            modules.Clear(); Array.Clear(grid, 0, grid.Length);
            Enemies = new List<Enemy>(); Effects.Clear(); Links.Clear();
            Mode = GameMode.Idle; Wave = 1; Core = GameData.CoreMax; Charge = Conf.Start;
            TotalKills = 0; Elapsed = 0; SpawnRemaining = 0; waveGateAt = 0; secondFront = 0; FrontDelay = 0; WaveBreak = 0;
            armorShotNotice = false; lastCounterAttackAt = -20; counterSeen.Clear(); nextId = 0; networkClock = 0;
            EnergyCapacity = 0; ChargingGeneration = 0; gridOnline = false; ConsumptionRate = 0; consumptionThisFrame = 0;
            breachPressure = new double[W]; weakSector = -1; weakSectorUntil = 0; weakSectorNotice = -30;
            assaultPlan = null; assaultCount = 0; assaultCooldownUntil = 0; strategicTargets.Clear();
            recentAssaultLanes = Enumerable.Repeat(-100.0, W).ToArray(); lastCoreDangerAt = -30; RandomSeed = 271;
            AddModule(new GameModule
            {
                Type = ModuleType.Reactor, C = GameData.StartCoreColumn, R = GameData.StartCoreRow,
                Hp = GameData.Modules[ModuleType.Reactor].Hp, Powered = true, Linked = true, Starter = true
            });
            UpdateNetwork();
            BoardChanged();
        }

        public void Start()
        {
            Reset();
            Mode = GameMode.Playing;
            StartWave();
            Notify("Ya tienes un Núcleo · coloca Generadores para cargarlo", 3);
        }

        void StartWave()
        {
            assaultPlan = null; assaultCount = 0; assaultCooldownUntil = 0; nextEliteStrikeAt = 0;
            var plan = Plan;
            spawnIndex = 0;
            SpawnRemaining = Math.Max(1, (int)JsRound(plan.Count * Conf.Size));
            waveGateAt = (int)Math.Ceiling(SpawnRemaining / 3.0);
            secondFront = 0; FrontDelay = 0;
            spawnClock = Wave == 1 ? 2.4 : Math.Max(1.1, plan.Gap * .95);
            Sfx("wave");
            Notify(Wave == 1 ? "OLEADA 1 · VIGILA TAMBIÉN LOS FLANCOS" : $"OLEADA {Wave} · FRENTE 1/3", 2.35);
        }

        /// <summary>Development and test helper: restarts the given wave right away.</summary>
        public void DebugStartAtWave(int wave)
        {
            Wave = Math.Min(MaxWave, Math.Max(1, wave));
            StartWave();
        }

        /// <summary>Development and test helper: adds energy, capped by the battery capacity.</summary>
        public void DebugAddCharge(double amount) => Charge = Math.Min(EnergyCapacity, Charge + amount);

        void Finish(bool win)
        {
            Mode = win ? GameMode.Won : GameMode.Lost;
            Sfx(win ? "win" : "lost");
            OnFinished?.Invoke(win);
        }

        // ---- Player actions -----------------------------------------------------------

        public static int BuildCost(ModuleType type) => GameData.Modules[type].Cost;

        public int UpgradeCost(GameModule m)
        {
            if (m.Type == ModuleType.Generator) return m.Level == 1 ? 45 : 78;
            var d = GameData.Modules[m.Type];
            return (int)JsRound(d.Cost * .76 + (m.Level - 1) * d.Cost * .34);
        }

        public int UpgradeUnlock(GameModule m) =>
            m.Type == ModuleType.Generator ? (m.Level == 1 ? 3 : m.Level == 2 ? 6 : 99) : 1;

        public int RecycleValue(GameModule m) =>
            (int)Math.Floor(GameData.Modules[m.Type].Cost * (.32 + .14 * (m.Level - 1)));

        public bool CanUpgrade(GameModule m) =>
            m != null && !m.IsDeploying && m.Level < 3 && Wave >= UpgradeUnlock(m) && Charge >= UpgradeCost(m);

        public string UpgradeLabel(GameModule m)
        {
            if (m.IsDeploying) return "⏳ EN MONTAJE";
            if (m.Level >= 3) return "NIVEL MÁXIMO";
            int unlock = UpgradeUnlock(m);
            if (Wave < unlock) return $"🔒 NIVEL {m.Level + 1} · OLA {unlock}";
            return $"↑ MEJORAR · ϟ {UpgradeCost(m)}";
        }

        /// <summary>Equivalent of tapping an empty cell with a module selected. Returns true if it started building.</summary>
        public bool TryBuild(ModuleType type, int r, int c)
        {
            if (Mode != GameMode.Playing) return false;
            if (r == 0 || r == H - 1)
            {
                Notify(r == 0 ? "Zona de aproximación enemiga" : "La Base Central no admite construcción", 1.2);
                return false;
            }
            if (ModuleAt(r, c) != null) return false;
            if (Charge < BuildCost(type)) { Notify("Carga eléctrica insuficiente"); return false; }
            Charge -= BuildCost(type);
            Effects.RemoveAll(fx => fx.Type == "wreck" && fx.X == c && fx.Y == r);
            Sfx("build");
            var d = GameData.Modules[type];
            AddModule(new GameModule
            {
                Type = type, C = c, R = r, Hp = d.Hp * .75,
                DeployTotal = d.DeploySeconds, DeployRemaining = d.DeploySeconds
            });
            SpawnEffect("ring", c, r, d.Color);
            UpdateNetwork(); BoardChanged();
            Notify($"{d.Name} en montaje · {d.DeploySeconds:0.0}s", 1.8);
            return true;
        }

        public bool TryUpgrade(GameModule m)
        {
            if (m == null || m.Level >= 3 || m.IsDeploying) return false;
            var def = GameData.Modules[m.Type];
            int cost = UpgradeCost(m);
            if (Wave < UpgradeUnlock(m)) { Notify($"Mejora disponible desde la oleada {UpgradeUnlock(m)}", 2); return false; }
            if (Charge < cost) { Notify("Carga eléctrica insuficiente"); return false; }
            Charge -= cost; m.Level++; m.Hp += def.Hp * .65; Sfx("upgrade");
            m.Cool = Math.Max(0, m.Cool - .15);
            SpawnEffect("ring", m.C, m.R, def.Color);
            UpdateNetwork(); BoardChanged();
            Notify($"{def.Name} mejorado · nivel {m.Level}");
            return true;
        }

        public bool TryRecycle(GameModule m)
        {
            if (m == null) return false;
            if (m.Type == ModuleType.Reactor && modules.Count(n => n.Type == ModuleType.Reactor && !n.IsDeploying) == 1)
            {
                Notify("Necesitas al menos un Núcleo para mantener tu colonia", 2);
                return false;
            }
            Charge = Math.Min(EnergyCapacity, Charge + RecycleValue(m));
            SpawnEffect("ring", m.C, m.R, "#ffad8b");
            DeleteModule(m);
            Sfx("recycle");
            UpdateNetwork(); BoardChanged();
            Notify("Módulo reciclado · carga recuperada");
            return true;
        }

        // ---- Network ------------------------------------------------------------------

        public bool Jammed(GameModule m) => m.EmpUntil > Elapsed;
        static double DrawRate(GameModule m) =>
            m.Type == ModuleType.Pulse ? 1.55 : m.Type == ModuleType.Cryo ? 2.35 : m.Type == ModuleType.Tesla ? 3.6 : m.Type == ModuleType.Repair ? 1.7 : 0;
        static double StorageCap(GameModule m) => (m.Starter ? 230 : 140) + (m.Level - 1) * 85;
        public static double GeneratorRate(GameModule m)
        {
            switch (m.Level) { case 1: return 2.7; case 2: return 3.25; case 3: return 3.75; default: return 2.7; }
        }
        static bool Nearby(GameModule a, GameModule b) => Math.Abs(a.R - b.R) + Math.Abs(a.C - b.C) <= 2;

        bool SpendActionEnergy(double cost)
        {
            bool anyLive = false;
            foreach (var m in modules) if (m.Type == ModuleType.Reactor && m.Hp > 0 && !Jammed(m)) { anyLive = true; break; }
            if (!anyLive) return false;
            if (Charge + 1e-6 < cost) return false;
            Charge = Math.Max(0, Charge - cost);
            consumptionThisFrame += cost;
            return true;
        }

        public void UpdateNetwork()
        {
            Links.Clear();
            var all = modules.Where(m => m.Hp > 0 && !m.IsDeploying).ToList();
            var stores = all.Where(m => m.Type == ModuleType.Reactor && !Jammed(m)).ToList();
            var allStores = all.Where(m => m.Type == ModuleType.Reactor).ToList();
            var relays = all.Where(m => m.Type == ModuleType.Relay && !Jammed(m)).ToList();
            var generators = all.Where(m => m.Type == ModuleType.Generator && !Jammed(m)).ToList();
            EnergyCapacity = allStores.Sum(StorageCap);
            Charge = Clamp(Charge, 0, EnergyCapacity);
            ChargingGeneration = 0;
            foreach (var m in all)
            {
                m.Linked = false; m.OfflineReason = ""; m.Powered = m.Type == ModuleType.Wall;
                if (Jammed(m)) m.OfflineReason = "EMP TEMPORAL";
            }
            var reachable = new HashSet<GameModule>(stores);
            var poweredNodes = new List<GameModule>(stores);
            for (int q = 0; q < poweredNodes.Count; q++)
            {
                var node = poweredNodes[q];
                foreach (var r in relays)
                    if (!reachable.Contains(r) && Nearby(node, r))
                    {
                        reachable.Add(r); poweredNodes.Add(r);
                        Links.Add(new NetworkLink { A = node, B = r });
                    }
            }
            foreach (var g in generators)
            {
                var node = stores.FirstOrDefault(n => Nearby(n, g));
                g.Linked = node != null;
                g.Powered = true; // Spins even when isolated; only delivers near a Núcleo.
                g.OfflineReason = node != null ? "" : "ACERCA AL NÚCLEO";
                if (node != null)
                {
                    Links.Add(new NetworkLink { A = g, B = node });
                    ChargingGeneration += GeneratorRate(g);
                }
            }
            bool havePower = stores.Count > 0 && (Charge > .01 || ChargingGeneration > .01);
            foreach (var st in allStores)
            {
                st.Linked = !Jammed(st); st.Powered = !Jammed(st) && havePower;
                st.OfflineReason = Jammed(st) ? "EMP TEMPORAL" : havePower ? "" : "SIN ENERGÍA";
            }
            foreach (var r in relays)
            {
                r.Linked = reachable.Contains(r); r.Powered = r.Linked && havePower;
                r.OfflineReason = r.Linked ? (havePower ? "" : "SIN ENERGÍA") : "SIN CONEXIÓN";
            }
            foreach (var m in all)
            {
                if (m.Type == ModuleType.Generator || m.Type == ModuleType.Reactor || m.Type == ModuleType.Relay || m.Type == ModuleType.Wall) continue;
                if (Jammed(m)) { m.OfflineReason = "EMP TEMPORAL"; continue; }
                var node = poweredNodes.FirstOrDefault(n => Nearby(n, m));
                m.Linked = node != null;
                if (node == null) { m.OfflineReason = "SIN CONEXIÓN"; continue; }
                m.Powered = havePower; m.OfflineReason = havePower ? "" : "SIN ENERGÍA";
                if (m.Powered) Links.Add(new NetworkLink { A = node, B = m });
            }
            if (!havePower) Links.Clear();
            gridOnline = havePower;
        }

        // ---- Effects ------------------------------------------------------------------

        void SpawnEffect(string type, double x, double y, string color, double duration = .55, double power = 1,
            double? x2 = null, double? y2 = null, string text = null, double? seed = null)
        {
            if (Effects.Count > 160) Effects.RemoveRange(0, Effects.Count - 145);
            Effects.Add(new FxEvent
            {
                Type = type, X = x, Y = y, Color = color, Duration = duration, Power = power,
                HasTarget = x2.HasValue, X2 = x2 ?? 0, Y2 = y2 ?? 0, Text = text,
                Seed = seed ?? visualRandom.NextDouble() * 500
            });
            if (type == "explode" && power >= 1.15) OnShake?.Invoke(Math.Min(2.9, power * 1.35));
        }

        void WreckAt(int r, int c, double intensity = 1) =>
            SpawnEffect("wreck", c, r, "#88a7b1", 8.5, intensity, seed: (c + 1) * 51 + (r + 1) * 17);

        // ---- Deployment ---------------------------------------------------------------

        double DeploymentBoost(GameModule m)
        {
            double bonus = 0;
            foreach (var w in modules)
            {
                if (w == m || w.Type != ModuleType.Repair || w.Hp <= 0 || w.IsDeploying || !w.Powered || Jammed(w)) continue;
                if (Math.Abs(w.R - m.R) + Math.Abs(w.C - m.C) <= 2) bonus += w.Level >= 3 ? .4 : .3;
            }
            return Math.Min(.8, bonus);
        }

        void DelayBuildOnHit(GameModule m)
        {
            if (!m.IsDeploying) return;
            m.DeployPausedUntil = Math.Max(m.DeployPausedUntil, Elapsed + 1.3);
            m.DeployRemaining = Math.Min(m.DeployTotal, m.DeployRemaining + .35);
        }

        void UpdateDeployments(double dt)
        {
            bool changed = false;
            foreach (var m in modules)
            {
                if (!m.IsDeploying || Elapsed < m.DeployPausedUntil) continue;
                m.DeployRemaining = Math.Max(0, m.DeployRemaining - dt * (1 + DeploymentBoost(m)));
                if (m.DeployRemaining <= 0)
                {
                    m.DeployRemaining = 0;
                    double full = GameData.Modules[m.Type].Hp;
                    m.Hp = Math.Min(full, m.Hp + full * .25);
                    SpawnEffect("ring", m.C, m.R, "#95ead9", .75, 1.1);
                    changed = true;
                }
            }
            if (changed) { UpdateNetwork(); BoardChanged(); }
        }

        // ---- Enemy intelligence -------------------------------------------------------

        double SectorCoverage(int column)
        {
            double cover = 0;
            foreach (var m in modules)
            {
                if (m.Hp <= 0 || m.R == 0 || m.R == H - 1) continue;
                int dist = Math.Abs(column - m.C);
                double live = (m.Powered || m.Type == ModuleType.Wall) ? 1 : .14;
                double condition = Clamp(m.Hp / m.MaxHp, .15, 1);
                double strength =
                    m.Type == ModuleType.Pulse ? (dist == 0 ? 2.8 : 0)
                    : m.Type == ModuleType.Tesla ? (dist <= 2 ? 2.5 * (1 - dist * .24) : 0)
                    : m.Type == ModuleType.Cryo ? (dist <= 1 ? 1.65 * (1 - dist * .4) : 0)
                    : m.Type == ModuleType.Wall ? (dist == 0 ? 1.8 : 0) : 0;
                cover += strength * live * condition * (1 + (m.Level - 1) * .22);
            }
            return cover;
        }

        void MarkBreach(int column, double weight = 1)
        {
            if (column < 0 || column >= W) return;
            breachPressure[column] = Math.Min(7, breachPressure[column] + weight);
            if (weight >= 2.5 && Wave >= 5)
            {
                weakSector = -1; weakSectorUntil = 0;
                if (assaultPlan != null && Elapsed < assaultPlan.Until)
                {
                    assaultPlan.Lane = column;
                    assaultPlan.Until = Math.Max(assaultPlan.Until, Elapsed + 9);
                }
            }
        }

        struct LaneScore { public int C; public double Score; }

        List<LaneScore> WeakestSector()
        {
            var lanes = new List<LaneScore>();
            for (int c = 0; c < W; c++) lanes.Add(new LaneScore { C = c, Score = SectorCoverage(c) - breachPressure[c] * 2.0 });
            return Sorted(lanes, (a, b) =>
            {
                int s = Sign(a.Score - b.Score);
                return s != 0 ? s : Math.Abs(a.C - 3) - Math.Abs(b.C - 3);
            });
        }

        void AdvanceEnemyIntel(double dt)
        {
            for (int c = 0; c < W; c++) breachPressure[c] = Math.Max(0, breachPressure[c] - dt * .014);
            if (Elapsed > weakSectorUntil) weakSector = -1;
        }

        static readonly ModuleType G = ModuleType.Generator, Rc = ModuleType.Reactor, Rl = ModuleType.Relay, P = ModuleType.Pulse,
            Cr = ModuleType.Cryo, T = ModuleType.Tesla, Wl = ModuleType.Wall, Rp = ModuleType.Repair;

        static readonly Dictionary<EnemyKind, ModuleType[][]> Profiles = new Dictionary<EnemyKind, ModuleType[][]>
        {
            { EnemyKind.Tank, new[] { new[] { G, Rc, P, T, Wl }, new[] { Rc, G, T, P, Rl }, new[] { P, T, G, Rc, Wl } } },
            { EnemyKind.Helicopter, new[] { new[] { Rl, Rp, Cr, Rc }, new[] { Rp, Cr, Rl, T, Rc }, new[] { Cr, Rl, Rp, Rc } } },
            { EnemyKind.Hunter, new[] { new[] { Rp, Cr, T }, new[] { Cr, Rp, T }, new[] { T, Rp, Cr } } },
            { EnemyKind.Commander, new[] { new[] { Rc, T, Rp, P }, new[] { T, Cr, Rc, Rl } } },
        };

        double StrategicScore(GameModule m, Enemy e, ModuleType[] priorities)
        {
            int index = Array.IndexOf(priorities, m.Type);
            double role = index < 0 ? -7 : (priorities.Length - index) * 3.15;
            double condition = 1 - Math.Min(1, m.Hp / Math.Max(1, m.MaxHp));
            double distance = Hypot(m.C - e.C, m.R - e.Y);
            double repeatPenalty = strategicTargets.TryGetValue(Key(m.R, m.C), out var last) ? Math.Max(0, 10 - (Elapsed - last)) * 1.45 : 0;
            var assault = ActiveAssault();
            double laneBonus = assault != null ? Math.Max(0, 2.3 - Math.Abs(m.C - assault.Lane) * 1.15) : 0;
            double highValue = m.Type == ModuleType.Reactor ? 1.3 : m.Type == ModuleType.Repair ? 1.1 : m.Type == ModuleType.Relay ? .9 : 0;
            return role + condition * 2.3 + highValue + laneBonus - distance * .28 - repeatPenalty;
        }

        GameModule ChooseStrategicTarget(Enemy e, List<GameModule> candidates, EnemyKind kind, int shot = 0)
        {
            if (candidates.Count == 0) return null;
            var choices = Profiles[kind];
            var priority = choices[(shot + e.Id) % choices.Length];
            return Sorted(candidates, (a, b) =>
            {
                int s = Sign(StrategicScore(b, e, priority) - StrategicScore(a, e, priority));
                if (s != 0) return s;
                return a.R != b.R ? a.R - b.R : a.C - b.C;
            })[0];
        }

        void RememberStrategicTargets(IEnumerable<GameModule> targets)
        {
            foreach (var m in targets) strategicTargets[Key(m.R, m.C)] = Elapsed;
        }

        void BeginCoordinatedAssault()
        {
            if (Wave < 6 || Elapsed < assaultCooldownUntil) return;
            if (assaultCount >= (Wave >= 9 ? 2 : 1)) return;
            var scored = WeakestSector().Select(item =>
            {
                int repairs = modules.Count(m => Math.Abs(m.C - item.C) <= 1 && m.Type == ModuleType.Repair);
                return new LaneScore
                {
                    C = item.C,
                    Score = item.Score - repairs * .46 - breachPressure[item.C] * 1.25 + Math.Max(0, 30 - (Elapsed - recentAssaultLanes[item.C])) * .34
                };
            }).ToList();
            scored = Sorted(scored, (a, b) =>
            {
                int s = Sign(a.Score - b.Score);
                return s != 0 ? s : a.C - b.C;
            });
            int lane = scored[0].C;
            recentAssaultLanes[lane] = Elapsed;
            assaultPlan = new AssaultPlan { Lane = lane, Until = Elapsed + (Wave >= 8 ? 27 : 16), Started = Elapsed };
            assaultCount++; assaultCooldownUntil = Elapsed + 17;
            weakSector = lane; weakSectorUntil = assaultPlan.Until;
            foreach (var e in Enemies)
            {
                if (e.Hp <= 0) continue;
                if (GameData.IsVehicle(e.Kind)) e.StrikeClock = Math.Min(e.StrikeClock, Wave >= 8 ? 1.0 : 2.4);
                if (e.Kind == EnemyKind.Hunter) e.HuntClock = Math.Min(e.HuntClock, Wave >= 8 ? .8 : 2.0);
            }
            Notify($"⚠ OFENSIVA COORDINADA · PROTEGE SECTOR {lane + 1}", 3.1);
            Sfx("alert");
        }

        AssaultPlan ActiveAssault() => assaultPlan != null && Elapsed < assaultPlan.Until ? assaultPlan : null;

        bool HeavyImpact(GameModule m, double damage, EnemyKind kind, double lockSeconds)
        {
            if (m == null || m.Hp <= 0) return false;
            bool lastCore = m.Type == ModuleType.Reactor && modules.Count(x => x.Type == ModuleType.Reactor) == 1;
            m.Hp -= lastCore ? Math.Min(damage, Math.Max(0, m.Hp - 1)) : damage;
            DelayBuildOnHit(m);
            if (m.Type == ModuleType.Reactor && (kind == EnemyKind.Tank || kind == EnemyKind.Bomber || kind == EnemyKind.Commander || kind == EnemyKind.Hunter))
            {
                m.EmpUntil = Math.Max(m.EmpUntil, Elapsed + (Wave <= 7 ? 1.1 : 1.65));
                Charge = Math.Max(0, Charge - Math.Min(Wave <= 7 ? 16 : 24, Charge * (Wave <= 7 ? .04 : .06)));
                if (Elapsed - lastCoreDangerAt > 8)
                {
                    lastCoreDangerAt = Elapsed;
                    Notify("⚠ NÚCLEO BAJO ATAQUE · RED INTERRUMPIDA", 2.2);
                    Sfx("alert");
                }
            }
            m.RepairBlockedUntil = Math.Max(m.RepairBlockedUntil, Elapsed + lockSeconds);
            SpawnEffect("impact", m.C, m.R, "#ffc18f", .58, 1.45);
            if (m.Hp <= 0)
            {
                DeleteModule(m); WreckAt(m.R, m.C, 1.25); MarkBreach(m.C, 3.0);
                SpawnEffect("explode", m.C, m.R, "#ff9a6b", .88, 1.55);
                Notify($"⚠ {GameData.Modules[m.Type].Name.ToUpperInvariant()} DESTRUIDO · SECTOR {m.C + 1}", 2.7);
                UpdateNetwork(); BoardChanged();
                return true;
            }
            return false;
        }

        int LaneLoad(int lane) => Enemies.Count(e => e.Hp > 0 && e.Y < 5.5 && e.C == lane);

        int? ChooseAssaultLane(int index)
        {
            if (Wave < 5) return null;
            var plan = ActiveAssault();
            var ranked = WeakestSector();
            if (plan != null)
            {
                if (LaneLoad(plan.Lane) < 3 && (index % 4 != 0 || breachPressure[plan.Lane] > 1)) return plan.Lane;
            }
            double chance = Wave >= 7 ? .55 : .41;
            if (Rng() > chance) return null;
            if (weakSector < 0 || Elapsed >= weakSectorUntil)
            {
                weakSector = ranked[0].C; weakSectorUntil = Elapsed + 15;
            }
            int target = weakSector;
            int nearbyCount = LaneLoad(target);
            int alternative = -1;
            foreach (var item in ranked)
                if (item.C != target && Math.Abs(item.C - target) <= 2 && LaneLoad(item.C) < 3) { alternative = item.C; break; }
            int lane = nearbyCount >= 3 && alternative >= 0 ? alternative : target;
            if (Elapsed - weakSectorNotice > 16)
            {
                weakSectorNotice = Elapsed;
                Notify($"⚠ REFUERZOS DETECTAN SECTOR {lane + 1} VULNERABLE", 2.4);
            }
            return lane;
        }

        // ---- Spawning -----------------------------------------------------------------

        static readonly int[] OpeningOne = { 3, 1, 5, 3, 2, 4, 1, 5 };
        static readonly int[] OpeningTwo = { 3, 0, 6, 2, 4, 1, 5, 0, 6, 3 };
        static readonly int[] Mid = { 1, 5, 3, 0, 6, 2, 4, 5, 1, 3, 6, 0, 4, 2 };
        static readonly int[] Full = { 0, 4, 2, 6, 1, 5, 3, 0, 6, 2, 4, 5, 1, 3, 6, 0, 4, 2 };

        void SpawnEnemy()
        {
            int i = spawnIndex++;
            int wave = Wave;
            var path = wave == 1 ? OpeningOne : wave == 2 ? OpeningTwo : wave <= 5 ? Mid : Full;
            int c = path[(i + (wave <= 2 ? 0 : wave)) % path.Length];
            EnemyKind kind;
            var plan = Plan;
            const EnemyKind Sc = EnemyKind.Scout, Ru = EnemyKind.Runner, Ar = EnemyKind.Armor, Ja = EnemyKind.Jammer, Sp = EnemyKind.Splitter, Sa = EnemyKind.Sapper;
            if (wave == 1) kind = Sc;
            else if (wave == 2) kind = i % 5 == 3 ? Ru : Sc;
            else if (wave == 3) kind = i % 4 == 2 ? Ru : Sc;
            else if (wave == 4) kind = i == 7 ? Ja : i % 6 == 5 ? Ar : i % 3 == 1 ? Ru : Sc;
            else if (wave == 5) kind = i % 7 == 5 ? Ja : i % 5 == 3 ? Ar : i % 3 == 1 ? Ru : Sc;
            else if (wave == 6) kind = i == 8 ? Sa : i % 6 == 4 ? Ar : i % 8 == 2 ? Ja : i % 4 == 1 ? Sp : Ru;
            else if (wave == 7) kind = i == 10 ? Sa : i % 6 == 3 ? Ar : i % 7 == 5 ? Ja : i % 5 == 2 ? Sp : Ru;
            else if (wave == 8) kind = i == 5 || i == 14 ? Sa : i % 6 == 1 ? Ar : i % 7 == 4 ? Ja : i % 5 == 2 ? Sp : Ru;
            else kind = i == 5 || i == 16 ? Sa : i % 6 == 1 ? Ar : i % 7 == 4 ? Ja : i % 4 == 2 ? Sp : Ru;

            if (wave == 9 && i == 21) { kind = EnemyKind.Commander; c = 3; }
            const EnemyKind Tk = EnemyKind.Tank, He = EnemyKind.Helicopter, Bo = EnemyKind.Bomber;
            if (wave == 4) { if (i == 8) { kind = Tk; c = 3; } }
            if (wave == 5) { if (i == 2) { kind = Tk; c = 2; } if (i == 15) { kind = He; c = 4; } }
            if (wave == 6) { if (i == 3) { kind = He; c = 4; } if (i == 10) { kind = Tk; c = 5; } if (i == 17) { kind = Bo; c = 3; } }
            if (wave == 7) { if (i == 4) { kind = Bo; c = 3; } if (i == 11 || i == 20) { kind = He; c = i == 11 ? 1 : 4; } if (i == 15) { kind = Tk; c = 2; } }
            if (wave == 8) { if (i == 3 || i == 18) { kind = Tk; c = i == 3 ? 1 : 5; } if (i == 9) { kind = He; c = 3; } if (i == 13 || i == 23) { kind = Bo; c = i == 13 ? 2 : 5; } }
            if (wave == 9) { if (i == 3 || i == 23) { kind = Tk; c = i == 3 ? 5 : 2; } if (i == 9 || i == 18) { kind = He; c = i == 9 ? 1 : 5; } if (i == 14 || i == 24) { kind = Bo; c = i == 14 ? 3 : 4; } }

            if (wave == 3 && i == 5) kind = EnemyKind.Bulwark;
            if (wave == 4 && i == 9) kind = EnemyKind.Thermal;
            if (wave == 5 && i == 8) kind = EnemyKind.Grounder;
            if (wave >= 6 && !(kind == Sa || kind == Ja || kind == EnemyKind.Commander || GameData.IsVehicle(kind)))
            {
                if (i % 9 == 0) kind = EnemyKind.Bulwark;
                else if (i % 9 == 3) kind = EnemyKind.Thermal;
                else if (i % 9 == 6) kind = EnemyKind.Grounder;
            }
            if (GameData.HunterIndices.TryGetValue(wave, out var hunters) && Array.IndexOf(hunters, i) >= 0) kind = EnemyKind.Hunter;
            if (kind == EnemyKind.Hunter)
            {
                var exposed = Sorted(modules.Where(m => m.Hp > 0 && IsHunterPrey(m)), (a, b) =>
                {
                    if (a.Level != b.Level) return a.Level - b.Level;
                    int s = Sign(a.Hp - b.Hp);
                    return s != 0 ? s : a.R - b.R;
                });
                if (exposed.Count > 0) c = exposed[0].C;
            }
            else if (!GameData.IsVehicle(kind) && kind != EnemyKind.Commander)
            {
                int? tactical = ChooseAssaultLane(i);
                if (tactical.HasValue) c = tactical.Value;
            }

            var a0 = GameData.Enemies[kind];
            double late = (wave >= 7 && kind != Sc && kind != Ru && kind != EnemyKind.Commander) ? 1 + (wave - 6) * .07 : 1;
            double hp = JsRound(a0.Hp * plan.Hp * Conf.Hp * late);
            double spd = (a0.Speed * (1 + (wave - 1) * .014)) * Conf.Speed;
            bool isVehicle = GameData.IsVehicle(kind);
            var assault = ActiveAssault();
            var e = new Enemy
            {
                Id = ++nextId, Kind = kind, C = c,
                Y = isVehicle ? .05 : kind == EnemyKind.Hunter ? -.25 : -.7,
                TacticalCoverUntil = assault != null && wave >= 7 && (kind == EnemyKind.Hunter || kind == Ru || kind == Sa || kind == Ja) && Math.Abs(c - assault.Lane) <= 1 ? Elapsed + 4.2 : 0,
                Hp = hp, MaxHp = hp, Speed = spd,
                Attack = a0.Attack + (int)Math.Floor((wave - 1) * .65),
            };
            e.Phase = Rng() * Math.PI * 2;
            e.SapClock = 8.6;
            e.ShellClock = 5.3 + 2.4 * Rng();
            e.BossClock = 8.5;
            e.StrikeClock = kind == Tk ? .55 : kind == He ? .75 : kind == Bo ? 1.05 : 0;
            e.HuntClock = kind == EnemyKind.Hunter ? 1.1 : 0;
            e.ArrivalShieldUntil = isVehicle ? Elapsed + 3.1 : 0;
            if (isVehicle)
            {
                double armorBase = kind == Tk ? 680 : kind == He ? 480 : 660;
                e.ArmorMax = JsRound(armorBase * Conf.Hp * plan.Hp);
                e.Armor = e.ArmorMax;
            }
            Enemies.Add(e);
            SpawnRemaining--;

            if (kind == EnemyKind.Commander) { Sfx("alert"); Notify("⚠ CENTINELA · REFUERZO DE ÚLTIMA OLEADA", 3.2); }
            if (kind == EnemyKind.Hunter) { Sfx("alert"); Notify("⚠ CAZADOR DE SOPORTE · CUIDA TALLERES Y CRIOGÉNICOS", 2.8); }
            if (isVehicle)
            {
                Notify(kind == Tk ? "⚠ TANQUE · OBJETIVOS MÚLTIPLES" : kind == He ? "⚠ HELICÓPTERO · ATAQUE A SISTEMAS" : "⚠ BOMBARDERO · EVITA EL ÁREA MARCADA", 2.4);
                Sfx("alert");
            }
            if (GameData.Counters.TryGetValue(kind, out var info) && counterSeen.Add(kind))
                Notify($"⚠ {info.Label}: {info.Tip}", 3.1);
            if (kind == Ja) Notify($"INTERFERENCIA DETECTADA · columna {c + 1}", 1.2);
        }

        bool IsHunterPrey(GameModule m) =>
            m.Type == ModuleType.Repair || m.Type == ModuleType.Cryo || (Wave >= 5 && m.Type == ModuleType.Tesla);

        // ---- Combat -------------------------------------------------------------------

        static double? CounterFactor(EnemyKind kind, ModuleType weapon)
        {
            switch (kind)
            {
                case EnemyKind.Bulwark: return weapon == ModuleType.Pulse ? .53 : weapon == ModuleType.Cryo ? 1.70 : weapon == ModuleType.Tesla ? 1.08 : (double?)null;
                case EnemyKind.Thermal: return weapon == ModuleType.Pulse ? 1.0 : weapon == ModuleType.Cryo ? .62 : weapon == ModuleType.Tesla ? 1.45 : (double?)null;
                case EnemyKind.Grounder: return weapon == ModuleType.Pulse ? 1.43 : weapon == ModuleType.Cryo ? 1.08 : weapon == ModuleType.Tesla ? .47 : (double?)null;
                default: return null;
            }
        }

        void HitEnemy(Enemy e, double damage, string color, ModuleType? weapon = null)
        {
            if (e.Hp <= 0 && !Enemies.Contains(e)) return;
            bool shield = Enemies.Any(o => o != e && o.Kind == EnemyKind.Armor && o.Hp > 0 && Wave >= 6 && Math.Abs(o.C - e.C) <= 1 && Math.Abs(o.Y - e.Y) < 1.8);
            if (shield && e.Kind != EnemyKind.Armor) damage *= .82;
            if (e.Kind == EnemyKind.Armor && Wave >= 7) damage *= .91;
            if (e.Kind == EnemyKind.Tank) damage *= weapon == ModuleType.Cryo ? 1.32 : weapon == ModuleType.Pulse ? .78 : 1;
            if (e.Kind == EnemyKind.Helicopter) damage *= weapon == ModuleType.Pulse ? 1.30 : weapon == ModuleType.Cryo ? .76 : 1;
            if (e.Kind == EnemyKind.Bomber) damage *= weapon == ModuleType.Tesla ? 1.35 : weapon == ModuleType.Cryo ? .84 : 1;
            if (e.Kind == EnemyKind.Hunter) damage *= weapon == ModuleType.Cryo ? 1.26 : weapon == ModuleType.Tesla ? 1.15 : 1;
            if (e.TacticalCoverUntil > Elapsed && weapon == ModuleType.Tesla) damage *= .77;
            if (e.ArrivalShieldUntil != 0 && Elapsed < e.ArrivalShieldUntil) damage *= .48;

            var sentry = Enemies.FirstOrDefault(o => o.Kind == EnemyKind.Commander && o.Hp > 0);
            if (sentry != null && sentry != e && Math.Abs(sentry.C - e.C) <= 1 && Math.Abs(sentry.Y - e.Y) <= 2.1) damage *= .86;
            if (e.Kind == EnemyKind.Commander)
            {
                bool escorted = Enemies.Any(o => o != e && o.Hp > 0 && Math.Abs(o.C - e.C) <= 1 && Math.Abs(o.Y - e.Y) <= 1.7);
                damage *= escorted ? .72 : .91;
            }
            if (weapon.HasValue)
            {
                var factor = CounterFactor(e.Kind, weapon.Value);
                if (factor.HasValue)
                {
                    damage *= factor.Value;
                    if ((factor.Value < .7 || factor.Value > 1.25) && Elapsed - e.LastCounterHintAt > 1.8)
                    {
                        e.LastCounterHintAt = Elapsed;
                        SpawnEffect("float", e.C, e.Y, factor.Value < .7 ? "#ffd19b" : "#9bf0cf", .78, text: factor.Value < .7 ? "RESISTE" : "EFECTIVO");
                    }
                }
            }
            if (e.Armor > 0)
            {
                double absorbed = Math.Min(e.Armor, damage * .76);
                e.Armor -= absorbed;
                damage -= absorbed;
                if (e.Armor <= 0 && !e.ArmorBreakSeen)
                {
                    e.ArmorBreakSeen = true;
                    SpawnEffect("explode", e.C, e.Y, "#c7e9ff", .65, 1.02, seed: e.Id);
                    SpawnEffect("float", e.C, e.Y, "#ffddb0", 1.0, text: "BLINDAJE ROTO");
                    Sfx("destroy");
                }
            }
            e.Hp -= damage; e.Hit = .20; Sfx("hit");
            if (e.Hp > 0)
            {
                SpawnEffect("impact", e.C, e.Y, color, .25, e.Kind == EnemyKind.Armor ? .82 : .65, seed: e.Id + Elapsed);
                return;
            }
            double x = e.C, y = e.Y;
            if (e.Kind == EnemyKind.Splitter)
                for (int n = 0; n < 2; n++)
                {
                    double hp = 24 + Wave * 3;
                    Enemies.Add(new Enemy
                    {
                        Id = ++nextId, Kind = EnemyKind.Runner,
                        C = (int)Clamp(e.C + (n != 0 ? 1 : -1), 0, W - 1),
                        Y = Math.Max(0, e.Y - .3), Hp = hp, MaxHp = hp,
                        Speed = 1.18 * Conf.Speed, Attack = 13, Phase = Rng() * Math.PI * 2
                    });
                }
            Enemies.Remove(e);
            TotalKills++;
            Charge = Math.Min(EnergyCapacity, Charge + 2 + (e.Kind == EnemyKind.Armor ? 1 : 0) + (e.Kind == EnemyKind.Sapper ? 1 : 0));
            bool eliteDead = GameData.IsVehicle(e.Kind);
            SpawnEffect("explode", x, y, eliteDead ? "#ffc18b" : e.Kind == EnemyKind.Jammer ? "#d6a1ff" : e.Kind == EnemyKind.Armor ? "#ffbe86" : color,
                eliteDead ? 1.1 : .78, eliteDead ? 2.0 : e.Kind == EnemyKind.Armor ? 1.55 : 1.1, seed: e.Id);
            if (eliteDead)
            {
                SpawnEffect("wreck", x, y, "#8d989f", 5.4, .74, seed: e.Id);
                if (GameData.IsAir(e.Kind)) SpawnEffect("crash", x, y, "#b7d6e5", .8, e.Kind == EnemyKind.Bomber ? 1.25 : .9, seed: e.Id);
                Sfx("heavyDestroy");
            }
            if (e.Kind == EnemyKind.Armor) Notify("Blindado destruido · +Carga", 1);
            Sfx("destroy");
        }

        List<Enemy> ClosestEnemies(GameModule m, double range)
        {
            var near = Enemies.Where(e => e.Hp > 0 && e.Y < m.R + .25 && Math.Abs(e.C - m.C) <= range && Hypot((e.C - m.C) * .9, e.Y - m.R) <= range);
            return Sorted(near, (a, b) => Sign(Hypot(a.C - m.C, a.Y - m.R) - Hypot(b.C - m.C, b.Y - m.R)));
        }

        void Shoot(GameModule m, Enemy e, string color, double damage)
        {
            m.Fire = .18;
            Sfx(m.Type == ModuleType.Cryo ? "cryo" : "pulse");
            SpawnEffect("beam", m.C, m.R, color, .2, x2: e.C, y2: e.Y);
            HitEnemy(e, damage, color, m.Type);
        }

        // ---- Main tick ----------------------------------------------------------------

        public void Update(double dt)
        {
            if (Mode != GameMode.Playing) return;
            Elapsed += dt;
            UpdateDeployments(dt);
            AdvanceEnemyIntel(dt);
            networkClock -= dt;
            if (networkClock <= 0) { UpdateNetwork(); networkClock = .23; }

            bool previouslyOnline = gridOnline;
            consumptionThisFrame = 0;
            Charge = Math.Min(EnergyCapacity, Charge + ChargingGeneration * dt);

            if (WaveBreak > 0)
            {
                WaveBreak -= dt;
                if (WaveBreak <= 0) { Wave++; StartWave(); }
            }
            UpdateSpawning(dt);
            UpdateJammers(dt);
            UpdateHunters(dt);
            UpdateSappers(dt);
            UpdateArmorShells(dt);
            UpdateCounterAttacks(dt);
            UpdateCommander(dt);
            UpdateVehicleStrikes(dt);
            UpdateMovement(dt);
            if (Mode != GameMode.Playing) return;
            if (!modules.Any(m => m.Type == ModuleType.Reactor && m.Hp > 0)) { Core = 0; Finish(false); return; }
            UpdateModules(dt);

            double instantUse = dt > 0 ? consumptionThisFrame / dt : 0;
            ConsumptionRate += (instantUse - ConsumptionRate) * Math.Min(1, dt / 2.4);
            if (previouslyOnline && Charge < .02 && ChargingGeneration < .02) { networkClock = 0; UpdateNetwork(); }

            for (int i = Effects.Count - 1; i >= 0; i--)
            {
                Effects[i].Age += dt;
                if (Effects[i].Age >= Effects[i].Duration) Effects.RemoveAt(i);
            }

            if (SpawnRemaining == 0 && Enemies.Count == 0 && WaveBreak <= 0)
            {
                if (Wave == MaxWave) Finish(true);
                else
                {
                    WaveBreak = Wave <= 7 ? 6.0 : Wave == 8 ? 6.5 : 3.8;
                    Charge = Math.Min(EnergyCapacity, Charge + 70);
                    Notify("SECTOR DESPEJADO · +70 de carga · refuerza tu red", 2.5);
                }
            }
        }

        void UpdateSpawning(double dt)
        {
            if (SpawnRemaining <= 0) return;
            if (secondFront < 2 && spawnIndex >= waveGateAt * (secondFront + 1))
            {
                secondFront++;
                FrontDelay = Wave <= 7 ? 5.6 : 4.3;
                spawnClock = 0;
                if (secondFront == 1 || (secondFront == 2 && Wave >= 8)) BeginCoordinatedAssault();
                Notify($"OLA {Wave} · FRENTE {secondFront + 1}/3 EN CAMINO", 2.0);
            }
            if (FrontDelay > 0) { FrontDelay = Math.Max(0, FrontDelay - dt); return; }
            spawnClock -= dt;
            int activeLimit = Math.Min(9, 5 + (Wave - 1) / 2);
            if (spawnClock <= 0 && Enemies.Count < activeLimit)
            {
                var plan = Plan;
                SpawnEnemy();
                double early = spawnIndex <= 2 ? 1.22 : 1;
                spawnClock = Math.Max(1.05, plan.Gap * Conf.Pace * early);
            }
        }

        void UpdateJammers(double dt)
        {
            foreach (var e in Enemies)
            {
                if (e.Kind != EnemyKind.Jammer || e.Hp <= 0) continue;
                e.EmpClock = Math.Max(0, e.EmpClock - dt);
                e.EmpFlash = Math.Max(0, e.EmpFlash - dt);
                if (e.EmpClock > 0 || e.Y < 3.0) continue;
                var victims = modules.Where(m => m.Type != ModuleType.Reactor && m.Type != ModuleType.Wall && !Jammed(m) && Hypot(m.C - e.C, m.R - e.Y) <= 1.65).ToList();
                if (victims.Count == 0) continue;
                foreach (var m in victims) m.EmpUntil = Math.Max(m.EmpUntil, Elapsed + 1.65);
                e.EmpClock = 15.5; e.EmpFlash = .8;
                SpawnEffect("emp", e.C, e.Y, "#dfa6ff", .82, 1.75, seed: e.Id);
                SpawnEffect("impact", e.C, e.Y, "#ffffff", .42, 1.45);
                UpdateNetwork(); Sfx("emp");
                Notify($"⚠ PULSO EMP · {victims.Count} MÓDULOS PARALIZADOS", 2.1);
            }
        }

        void UpdateHunters(double dt)
        {
            foreach (var e in Enemies)
            {
                if (e.Kind != EnemyKind.Hunter || e.Hp <= 0) continue;
                if (e.HuntWind > 0)
                {
                    e.HuntWind = Math.Max(0, e.HuntWind - dt * (e.Slow > 0 ? .55 : 1));
                    if (e.HuntWind <= 0)
                    {
                        var m = ModuleByKey(e.HuntTarget);
                        if (m != null && m.Hp > 0)
                        {
                            HeavyImpact(m, 145 + Math.Max(0, Wave - 3) * 12, EnemyKind.Hunter, 5.2);
                            SpawnEffect("beam", e.C, e.Y, "#ff9978", .52, 1.1, x2: m.C, y2: m.R, seed: e.Id);
                            SpawnEffect("impact", m.C, m.R, "#ffd2aa", .62, 1.45);
                            if (m.Hp > 0) Notify($"⚠ {GameData.Modules[m.Type].Name.ToUpperInvariant()} BAJO FUEGO · REPARACIÓN EN ESPERA", 2.0);
                            UpdateNetwork(); BoardChanged(); Sfx("destroy");
                        }
                        e.HuntTarget = -1; e.HuntShots++; e.HuntClock = 9.5;
                    }
                    continue;
                }
                if (e.HuntShots >= 2) continue;
                e.HuntClock = Math.Max(0, e.HuntClock - dt);
                if (e.Y < .12 || e.Y > H - 2.4 || e.HuntClock > 0) continue;
                var supports = modules.Where(m => m.Hp > 0 && IsHunterPrey(m) && m.R > e.Y - .3 && m.R - e.Y <= 8.4 && Math.Abs(m.C - e.C) <= 2.4).ToList();
                if (supports.Count == 0) { e.HuntClock = 1.3; continue; }
                var target = ChooseStrategicTarget(e, supports, EnemyKind.Hunter, e.HuntShots);
                e.HuntTarget = Key(target.R, target.C); e.HuntWind = 1.95;
                RememberStrategicTargets(new[] { target });
                Notify($"⚠ CAZADOR APUNTANDO A {GameData.Modules[target.Type].Name.ToUpperInvariant()} · SECTOR {target.C + 1}", 2.2);
                Sfx("alert");
            }
        }

        void UpdateSappers(double dt)
        {
            foreach (var e in Enemies)
            {
                if (e.Kind != EnemyKind.Sapper || e.Hp <= 0) continue;
                e.SapClock = Math.Max(0, e.SapClock - dt);
                if (e.Y < 3.25 || e.SapClock > 0) continue;
                var reactors = modules.Where(m => m.Type == ModuleType.Reactor && m.Hp > 0).ToList();
                if (reactors.Count == 0) continue;
                var target = Sorted(reactors, (a, b) => Sign(Hypot(a.C - e.C, a.R - e.Y) - Hypot(b.C - e.C, b.R - e.Y)))[0];
                if (Hypot(target.C - e.C, target.R - e.Y) > 4.3) continue;
                e.SapClock = 11; e.EmpFlash = .72;
                target.Hp -= 12 + (Wave - 6) * 2;
                DelayBuildOnHit(target);
                target.EmpUntil = Math.Max(target.EmpUntil, Elapsed + 1.0);
                Charge = Math.Max(0, Charge - (10 + (Wave - 6) * 2));
                SpawnEffect("beam", e.C, e.Y, "#ff835f", .45, x2: target.C, y2: target.R, seed: e.Id);
                SpawnEffect("emp", target.C, target.R, "#ff8d6d", .85, 1.5, seed: e.Id);
                Sfx("sapper"); UpdateNetwork();
                Notify("⚠ SABOTAJE CERCANO · Núcleo inactivo 1s", 1.7);
                if (target.Hp <= 0)
                {
                    DeleteModule(target); WreckAt(target.R, target.C);
                    SpawnEffect("explode", target.C, target.R, "#ff997e", .85, 1.6);
                    UpdateNetwork(); BoardChanged();
                }
            }
        }

        static int ShellRank(ModuleType t)
        {
            switch (t)
            {
                case ModuleType.Pulse: return 0;
                case ModuleType.Tesla: return 1;
                case ModuleType.Cryo: return 2;
                case ModuleType.Relay: return 3;
                case ModuleType.Generator: return 4;
                case ModuleType.Repair: return 5;
                case ModuleType.Reactor: return 6;
                default: return 7;
            }
        }

        void UpdateArmorShells(double dt)
        {
            foreach (var e in Enemies)
            {
                if (e.Kind != EnemyKind.Armor || e.Hp <= 0 || Wave < 4) continue;
                e.ShellClock = Math.Max(0, e.ShellClock - dt);
                if (e.Y < .25 || e.ShellClock > 0) continue;
                var candidates = modules.Where(m => m.Hp > 0 && m.Type != ModuleType.Wall && m.R > e.Y && m.R - e.Y <= 6.3 && Math.Abs(m.C - e.C) <= 1.8).ToList();
                if (candidates.Count == 0) continue;
                var m0 = Sorted(candidates, (a, b) =>
                {
                    int rank = ShellRank(a.Type) - ShellRank(b.Type);
                    return rank != 0 ? rank : Sign((a.R - e.Y) - (b.R - e.Y));
                })[0];
                double amount = 12 + Math.Max(0, Wave - 4) * 1.5 + (Wave >= 8 ? 2 : 0);
                m0.Hp -= amount;
                DelayBuildOnHit(m0);
                e.ShellClock = 5.7 + 1.7 * Rng();
                SpawnEffect("beam", e.C, e.Y, "#ffad77", .45, .85, x2: m0.C, y2: m0.R);
                SpawnEffect("impact", m0.C, m0.R, "#ffab75", .42, 1.1);
                Sfx("hit");
                if (!armorShotNotice)
                {
                    armorShotNotice = true;
                    Notify("⚠ FUEGO DE SUPRESIÓN · el Taller puede reparar daños", 2.6);
                }
                if (m0.Hp <= 0)
                {
                    DeleteModule(m0);
                    SpawnEffect("explode", m0.C, m0.R, "#ffb080", .75, 1.2);
                    UpdateNetwork(); BoardChanged();
                }
            }
        }

        void UpdateCounterAttacks(double dt)
        {
            foreach (var e in Enemies)
            {
                if (!GameData.Counters.TryGetValue(e.Kind, out var info) || e.Hp <= 0 || Wave < 5) continue;
                if (e.CounterWind > 0)
                {
                    e.CounterWind = Math.Max(0, e.CounterWind - dt);
                    if (e.CounterWind <= 0)
                    {
                        var target = ModuleByKey(e.CounterTarget);
                        if (target != null && target.Hp > 0)
                        {
                            target.Hp -= Math.Min(25, 11 + (Wave - 5) * 3.2);
                            DelayBuildOnHit(target);
                            SpawnEffect("beam", e.C, e.Y, info.Color, .3, .8, x2: target.C, y2: target.R);
                            SpawnEffect("impact", target.C, target.R, info.Color, .46, 1.25);
                            if (target.Hp <= 0)
                            {
                                DeleteModule(target); WreckAt(target.R, target.C);
                                SpawnEffect("explode", target.C, target.R, info.Color, .68, 1.18);
                                UpdateNetwork(); BoardChanged();
                            }
                        }
                        e.CounterFired = true; e.CounterTarget = -1;
                        Sfx("hit");
                    }
                    continue;
                }
                if (e.CounterFired || e.Y < 1.3 || Elapsed - lastCounterAttackAt < 3.45) continue;
                var candidates = modules.Where(m => m.Type == info.Target && m.Hp > 0 && m.R > e.Y && m.R - e.Y <= 6.5 && Math.Abs(m.C - e.C) <= 2.1).ToList();
                if (candidates.Count == 0) continue;
                var nearest = Sorted(candidates, (a, b) => Sign(Hypot(a.C - e.C, a.R - e.Y) - Hypot(b.C - e.C, b.R - e.Y)))[0];
                e.CounterTarget = Key(nearest.R, nearest.C);
                e.CounterWind = 1.55; e.CounterFired = false; lastCounterAttackAt = Elapsed;
                Notify($"⚠ {info.Label}: preparando ataque a {GameData.Modules[info.Target].Name}", 1.65);
            }
        }

        void UpdateCommander(double dt)
        {
            foreach (var e in Enemies)
            {
                if (e.Kind != EnemyKind.Commander || e.Hp <= 0) continue;
                if (e.BossWind > 0)
                {
                    e.BossWind = Math.Max(0, e.BossWind - dt);
                    if (e.BossWind <= 0)
                    {
                        var target = ModuleByKey(e.BossTarget);
                        if (target != null && target.Hp > 0)
                        {
                            HeavyImpact(target, target.Type == ModuleType.Reactor ? 43 : 30, EnemyKind.Commander, 2.8);
                            if (ModuleAt(target.R, target.C) != null) target.EmpUntil = Math.Max(target.EmpUntil, Elapsed + 1.75);
                            SpawnEffect("beam", e.C, e.Y, "#ffac75", .55, 1.2, x2: target.C, y2: target.R, seed: e.Id);
                            SpawnEffect("impact", target.C, target.R, "#ffd19a", .6, 1.6);
                        }
                        e.BossClock = 8.8; e.BossTarget = -1;
                        UpdateNetwork(); Sfx("emp");
                    }
                    continue;
                }
                e.BossClock = Math.Max(0, e.BossClock - dt);
                if (e.BossClock > 0 || e.Y < 1.65) continue;
                var candidates = modules.Where(m => m.Hp > 0 && m.R > e.Y && m.R - e.Y < 5.8 && Math.Abs(m.C - e.C) <= 2.2).ToList();
                if (candidates.Count == 0) continue;
                e.BossPhase++;
                var chosen = ChooseStrategicTarget(e, candidates, EnemyKind.Commander, e.BossPhase);
                e.BossTarget = Key(chosen.R, chosen.C); e.BossWind = 2.0;
                RememberStrategicTargets(new[] { chosen });
                Notify("⚠ CENTINELA · ATAQUE ANUNCIADO", 1.8);
                Sfx("alert");
            }
        }

        static double BombRoleValue(ModuleType t)
        {
            switch (t)
            {
                case ModuleType.Reactor: return 2.1;
                case ModuleType.Repair: return 1.7;
                case ModuleType.Cryo: return 1.2;
                case ModuleType.Relay: return 1.5;
                case ModuleType.Generator: return .9;
                case ModuleType.Tesla: return 1.4;
                default: return .35;
            }
        }

        void UpdateVehicleStrikes(double dt)
        {
            foreach (var e in Enemies)
            {
                if (!GameData.IsVehicle(e.Kind) || e.Hp <= 0) continue;
                bool isBomb = e.Kind == EnemyKind.Bomber;
                if (e.StrikeWind > 0)
                {
                    e.StrikeWind = Math.Max(0, e.StrikeWind - dt);
                    if (e.StrikeWind <= 0)
                    {
                        var hits = e.StrikeTargets.Select(ModuleByKey).Where(m => m != null && m.Hp > 0).ToList();
                        double damage = e.Kind == EnemyKind.Tank ? 145 + Wave * 3 : e.Kind == EnemyKind.Helicopter ? 118 + Wave * 3 : 126 + Wave * 2;
                        for (int h = 0; h < hits.Count; h++)
                        {
                            var m = hits[h];
                            double strikeDamage = isBomb ? (h == 0 ? damage : damage * .76) : damage;
                            HeavyImpact(m, strikeDamage, e.Kind, isBomb ? 6.5 : e.Kind == EnemyKind.Tank ? 5.9 : 5.2);
                            SpawnEffect("explode", m.C, m.R, "#ffbf85", .58, .6, seed: e.Id + m.R);
                        }
                        if (hits.Count > 0) { Sfx("destroy"); UpdateNetwork(); BoardChanged(); }
                        if (isBomb) SpawnEffect("ring", e.C, e.Y, "#ffba88", .6, 2.5);
                        e.StrikeTargets = new List<int>();
                        e.StrikeCount++;
                        e.StrikeClock = e.Kind == EnemyKind.Tank ? 9.3 : e.Kind == EnemyKind.Helicopter ? 10.1 : 14.2;
                        if (isBomb) e.StrikeDone = e.StrikeCount >= 2;
                    }
                    continue;
                }
                if (e.StrikeDone || e.Y < .15 || e.Y > H - 2.5) continue;
                e.StrikeClock = Math.Max(0, e.StrikeClock - dt);
                if (e.StrikeClock > 0) continue;
                if (Wave <= 7 && Elapsed < nextEliteStrikeAt)
                {
                    e.StrikeClock = Math.Max(.4, nextEliteStrikeAt - Elapsed);
                    continue;
                }
                var picked = new List<GameModule>();
                if (e.Kind == EnemyKind.Tank)
                {
                    var candidates = modules.Where(m => m.Hp > 0 && m.R > e.Y && m.R - e.Y <= 11.0 &&
                        (m.Type == ModuleType.Generator || m.Type == ModuleType.Reactor || m.Type == ModuleType.Pulse || m.Type == ModuleType.Tesla || m.Type == ModuleType.Relay || m.Type == ModuleType.Wall)).ToList();
                    var target = ChooseStrategicTarget(e, candidates, EnemyKind.Tank, e.StrikeCount);
                    if (target != null) picked.Add(target);
                }
                else if (e.Kind == EnemyKind.Helicopter)
                {
                    var candidates = modules.Where(m => m.Hp > 0 && m.R > e.Y && m.R - e.Y <= 11.0 &&
                        (m.Type == ModuleType.Relay || m.Type == ModuleType.Repair || m.Type == ModuleType.Cryo || m.Type == ModuleType.Reactor || m.Type == ModuleType.Tesla)).ToList();
                    var target = ChooseStrategicTarget(e, candidates, EnemyKind.Helicopter, e.StrikeCount);
                    if (target != null) picked.Add(target);
                }
                else
                {
                    picked = ChooseBombArea(e);
                }
                if (picked.Count == 0) { e.StrikeClock = 2.0; continue; }
                e.StrikeTargets = picked.Select(m => Key(m.R, m.C)).ToList();
                e.StrikeWind = isBomb ? 2.6 : 2.0;
                if (Wave <= 7) nextEliteStrikeAt = Elapsed + (Wave <= 5 ? 7.5 : 5.4);
                RememberStrategicTargets(picked);
                Notify(isBomb ? "⚠ BOMBARDERO · EVITA LAS CASILLAS MARCADAS"
                    : $"⚠ {(e.Kind == EnemyKind.Tank ? "TANQUE" : "HELICÓPTERO")} · {GameData.Modules[picked[0].Type].Name.ToUpperInvariant()} EN PELIGRO", 2.5);
                Sfx("alert");
            }
        }

        List<GameModule> ChooseBombArea(Enemy e)
        {
            var local = modules.Where(m => m.Hp > 0 && m.R >= 1 && m.R <= H - 2).ToList();
            List<GameModule> best = null;
            double bestScore = 0;
            int bestRow = 0, bestCol = 0;
            var focus = ActiveAssault();
            for (int row = 1; row < H - 2; row++)
                for (int col = 0; col < W - 1; col++)
                {
                    var group = local.Where(m => m.R >= row && m.R <= row + 1 && m.C >= col && m.C <= col + 1).ToList();
                    if (group.Count == 0) continue;
                    double nearest = Hypot(col + .5 - e.C, row + .5 - e.Y);
                    if (nearest > 11.0 || row < e.Y - .5) continue;
                    double roleValue = 0, recentlyAttacked = 0;
                    foreach (var m in group)
                    {
                        roleValue += BombRoleValue(m.Type);
                        double last = strategicTargets.TryGetValue(Key(m.R, m.C), out var t) ? t : -100;
                        recentlyAttacked += Math.Max(0, 9 - (Elapsed - last)) * .8;
                    }
                    double laneBonus = focus != null ? Math.Max(0, 4.4 - 2.4 * Math.Abs(col + .5 - focus.Lane)) : 0;
                    double score = group.Count * 8.5 - nearest * .55 + laneBonus + roleValue * 2.3 - recentlyAttacked;
                    if (best == null || score > bestScore) { best = group; bestScore = score; bestRow = row; bestCol = col; } // First wins ties, like a stable sort.
                }
            if (best == null) return new List<GameModule>();
            e.HasBombArea = true; e.BombRow = bestRow; e.BombColumn = bestCol;
            return best;
        }

        void UpdateMovement(double dt)
        {
            foreach (var e in Enemies.ToList())
            {
                e.Hit = Math.Max(0, e.Hit - dt);
                e.Slow = Math.Max(0, e.Slow - dt);
                double speed = e.Speed * (e.Slow > 0 ? (e.Kind == EnemyKind.Thermal ? .76 : e.Kind == EnemyKind.Bulwark ? .38 : .46) : 1);
                double next = e.Y + speed * dt;
                GameModule encountered = null;
                if (!GameData.IsAir(e.Kind))
                    for (int r = Math.Max(1, (int)Math.Ceiling(e.Y - .15)); r <= H - 2; r++)
                    {
                        var m = ModuleAt(r, e.C);
                        if (m == null) continue;
                        double boundary = r - .51;
                        if (next >= boundary && e.Y <= boundary + .05) { encountered = m; next = boundary; break; }
                    }
                e.Y = next;
                if (encountered != null)
                {
                    encountered.Hp -= e.Attack * dt * (e.Kind == EnemyKind.Armor ? 1.4 : 1) * (Wave >= 6 ? 1 + .075 * (Wave - 5) : 1);
                    if (encountered.IsDeploying && Elapsed >= encountered.LastBuildImpact + 1.1)
                    {
                        encountered.LastBuildImpact = Elapsed;
                        DelayBuildOnHit(encountered);
                    }
                    if (encountered.Type == ModuleType.Wall) e.Hp -= 5 * dt;
                    if (encountered.Hp <= 0)
                    {
                        DeleteModule(encountered); WreckAt(encountered.R, encountered.C); MarkBreach(encountered.C, 1.6);
                        SpawnEffect("explode", encountered.C, encountered.R, "#ff997e", .82, 1.4);
                        Sfx("destroy"); UpdateNetwork(); BoardChanged();
                        Notify($"{GameData.Modules[encountered.Type].Name} destruido · la red puede desconectarse", 1.6);
                    }
                }
                if (e.Hp <= 0) { HitEnemy(e, 1, "#ff9d86"); continue; }
                if (e.Y >= H - .58)
                {
                    Enemies.Remove(e);
                    Core -= e.Kind == EnemyKind.Armor || e.Kind == EnemyKind.Sapper ? 2 : 1;
                    Sfx("core");
                    SpawnEffect("explode", e.C, H - .58, "#ff8580", .75, 1.55);
                    Notify($"BRECHA EN COLUMNA {e.C + 1} · daño al núcleo", 1.6);
                    if (Core <= 0) { Core = 0; Finish(false); break; }
                }
            }
        }

        void UpdateModules(double dt)
        {
            foreach (var m in modules)
            {
                m.Cool = Math.Max(0, m.Cool - dt);
                m.Fire = Math.Max(0, m.Fire - dt);
                if (!m.Powered || Jammed(m)) continue;
                int lv = m.Level;
                if (m.Cool > 0) continue;
                switch (m.Type)
                {
                    case ModuleType.Pulse:
                    {
                        var candidates = Sorted(Enemies.Where(e => e.C == m.C && e.Y < m.R - .15 && e.Y > m.R - 5.9), (a, b) => Sign(b.Y - a.Y));
                        if (candidates.Count > 0 && SpendActionEnergy(DrawRate(m)))
                        {
                            Shoot(m, candidates[0], "#ffe29d", 28 + lv * 9);
                            m.Cool = .87 - (lv - 1) * .105;
                        }
                        break;
                    }
                    case ModuleType.Cryo:
                    {
                        var targets = Sorted(Enemies.Where(e => Math.Abs(e.C - m.C) <= 1 && e.Y < m.R + .8 && e.Y > m.R - 4.3), (a, b) => Sign(b.Y - a.Y));
                        if (targets.Count > 0 && SpendActionEnergy(DrawRate(m)))
                        {
                            var e = targets[0];
                            e.Slow = e.Kind == EnemyKind.Thermal ? .65 : e.Kind == EnemyKind.Bulwark ? 3.7 + (lv - 1) * .8 : 2.6 + (lv - 1) * .8;
                            Shoot(m, e, "#91e8ff", 7 + lv * 3);
                            SpawnEffect("ring", e.C, e.Y, "#98e8ff");
                            m.Cool = 1.12;
                        }
                        break;
                    }
                    case ModuleType.Tesla:
                    {
                        var targets = ClosestEnemies(m, 3.7);
                        int count = Math.Min(targets.Count, Math.Min(4, 1 + lv));
                        if (count > 0 && SpendActionEnergy(DrawRate(m)))
                        {
                            m.Fire = .24;
                            Sfx("tesla");
                            double px = m.C, py = m.R;
                            for (int j = 0; j < count; j++)
                            {
                                var e = targets[j];
                                SpawnEffect("beam", px, py, "#c8adff", .43, x2: e.C, y2: e.Y, seed: m.R * 11 + m.C * 9 + e.Id);
                                HitEnemy(e, Math.Max(10, (40 + lv * 8) - j * 10), "#d1b2ff", ModuleType.Tesla);
                                px = e.C; py = e.Y;
                            }
                            m.Cool = 1.78 - (lv - 1) * .14;
                        }
                        break;
                    }
                    case ModuleType.Repair:
                    {
                        var candidates = Sorted(modules.Where(v => !v.IsDeploying && Math.Abs(v.R - m.R) + Math.Abs(v.C - m.C) <= 2 && v.RepairBlockedUntil <= Elapsed && v.Hp < v.MaxHp - .01),
                            (a, b) =>
                            {
                                int s = Sign(a.Hp / a.MaxHp - b.Hp / b.MaxHp);
                                return s != 0 ? s : a.R - b.R;
                            });
                        if (candidates.Count > 0 && SpendActionEnergy(DrawRate(m)))
                        {
                            int heals = Math.Min(candidates.Count, lv >= 3 ? 2 : 1);
                            for (int j = 0; j < heals; j++)
                            {
                                var v = candidates[j];
                                double before = v.Hp;
                                v.Hp = Math.Min(v.MaxHp, v.Hp + (v == m ? 9 : 12 + lv * 8));
                                m.HealedTotal += Math.Max(0, v.Hp - before);
                                SpawnEffect("heal", v.C, v.R, "#9cecac", .75);
                            }
                            SpawnEffect("ring", m.C, m.R, "#9cecac");
                            Sfx("heal");
                        }
                        m.Cool = 1.18;
                        break;
                    }
                }
            }
        }

        // ---- Texts for the HUD --------------------------------------------------------

        public string ModuleStatus(GameModule m)
        {
            if (m.IsDeploying)
                return m.DeployPausedUntil > Elapsed ? "MONTAJE INTERRUMPIDO" : $"EN MONTAJE {Math.Ceiling(m.DeployRemaining)}s";
            if (Jammed(m)) return $"EMP · {Math.Ceiling(m.EmpUntil - Elapsed)}s";
            if (m.RepairBlockedUntil > Elapsed && m.Hp < m.MaxHp - .1) return $"REPARACIÓN BLOQUEADA {Math.Ceiling(m.RepairBlockedUntil - Elapsed)}s";
            if (m.Type == ModuleType.Generator && !m.Linked) return "SIN DESTINO";
            if (m.Type == ModuleType.Wall) return "AUTÓNOMA";
            if (m.Powered) return "OPERATIVO";
            return string.IsNullOrEmpty(m.OfflineReason) ? "SIN SUMINISTRO" : m.OfflineReason;
        }

        public string ModuleDetail(GameModule m)
        {
            if (m.IsDeploying)
                return $"Montaje {Math.Ceiling(100 * (1 - m.DeployRemaining / m.DeployTotal))}% · {Math.Ceiling(m.DeployRemaining)}s restantes" +
                       (m.DeployPausedUntil > Elapsed ? " · IMPACTO: montaje detenido" : "");
            var lines = new List<string> { $"{Math.Ceiling(m.Hp)}/{Math.Ceiling(m.MaxHp)} PV" };
            if (m.Type == ModuleType.Generator)
            {
                lines.Add($"Produce {(Jammed(m) ? 0 : GeneratorRate(m)):0.00} energía/s");
                if (m.Level < 3) lines.Add($"Nivel {m.Level + 1} desde ola {UpgradeUnlock(m)}");
                lines.Add(m.Linked ? "Entrega energía al Núcleo" : "ACÉRCALO AL NÚCLEO");
            }
            else if (m.Type == ModuleType.Reactor)
            {
                lines.Add($"Batería común {Math.Floor(Charge)}/{EnergyCapacity}");
                lines.Add($"Capacidad aportada +{StorageCap(m)}");
            }
            else if (m.Type == ModuleType.Relay)
            {
                lines.Add("Conecta hasta 2 casillas");
                lines.Add("No produce ni almacena energía");
            }
            else if (m.Type == ModuleType.Repair) lines.Add($"Reparó {Math.Floor(m.HealedTotal)} PV");
            if (m.RepairBlockedUntil > Elapsed) lines.Add($"Reparación en {Math.Ceiling(m.RepairBlockedUntil - Elapsed)}s");
            if (!m.Powered && m.Type != ModuleType.Wall) lines.Add($"⚠ {ModuleStatus(m)}");
            return string.Join(" · ", lines);
        }

        public string StatusText()
        {
            if (Mode == GameMode.Paused) return "SIMULACIÓN EN PAUSA";
            int jam = modules.Count(Jammed);
            if (jam > 0) return $"⚠ EMP · {jam} MÓDULOS AFECTADOS";
            var assault = ActiveAssault();
            if (assault != null) return $"⚠ OFENSIVA COORDINADA · SECTOR {assault.Lane + 1}";
            if (Enemies.Any(e => e.Kind == EnemyKind.Hunter && e.Hp > 0 && e.HuntWind > 0)) return "⚠ CAZADOR APUNTANDO · PROTEGE SOPORTE";
            var special = Enemies.FirstOrDefault(e => e.Hp > 0 && GameData.IsVehicle(e.Kind));
            if (special != null)
                return special.Kind == EnemyKind.Tank ? "⚠ TANQUE EN CAMPO · INFRAESTRUCTURA"
                    : special.Kind == EnemyKind.Helicopter ? "⚠ HELICÓPTERO · RED Y SOPORTE" : "⚠ BOMBARDERO EN CAMPO · CUIDA LA BASE";
            bool Weapon(GameModule m) => m.Type == ModuleType.Pulse || m.Type == ModuleType.Cryo || m.Type == ModuleType.Tesla || m.Type == ModuleType.Repair;
            int lost = modules.Count(m => Weapon(m) && !m.Powered && !Jammed(m) && !m.Linked);
            if (lost > 0) return $"⚠ {lost} SIN CONEXIÓN · ACERCA UN RELÉ";
            if (modules.Any(m => Weapon(m) && !m.Powered && !Jammed(m) && m.Linked)) return "⚠ BATERÍA VACÍA · CONECTA UN GENERADOR";
            if (ChargingGeneration <= .01) return "CONECTA UN GENERADOR AL NÚCLEO";
            if (WaveBreak > 0) return $"SECTOR SEGURO · {Math.Ceiling(WaveBreak)}s";
            if (FrontDelay > 0) return $"REFUERZOS EN {Math.Ceiling(FrontDelay)}s";
            return $"⚡ {Math.Floor(Charge)}/{EnergyCapacity} · PRODUCCIÓN +{ChargingGeneration:0.0}/s · GASTO −{ConsumptionRate:0.0}/s";
        }
    }
}
