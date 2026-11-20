using System;
using System.Collections.Generic;
using Colonia09.Simulation;

namespace Colonia09.Presentation
{
    /// <summary>Colony actions on the saved progress: collect production, build on a tile, upgrade, and mission rewards.</summary>
    public static class Colony
    {
        static SaveData Data
        {
            get
            {
                var data = SaveGame.Data;
                if (!migrated) { migrated = true; Migrate(data); }
                return data;
            }
        }
        static bool migrated;

        /// <summary>
        /// Buildings saved on single tiles (or, earlier still, numbered lots) move to a 2 x 2 lot of their zone: the lot
        /// under their tile when it is free, otherwise the first free one.
        /// </summary>
        static void Migrate(SaveData data)
        {
            var taken = new HashSet<(int, int)>();
            var moved = new List<BuildingSave>();
            foreach (var b in data.buildings)
            {
                var anchor = CampaignData.ZoneAt(b.x, b.y)?.Id == b.sector && !CampaignData.Reserved(b.x, b.y) ? CampaignData.LotAnchor(b.x, b.y) : null;
                if (anchor != null && taken.Add(anchor.Value)) (b.x, b.y) = anchor.Value;
                else moved.Add(b);
            }
            foreach (var b in moved)
            {
                var sector = CampaignData.Find(b.sector);
                if (sector == null) continue;
                foreach (var lot in CampaignData.Lots(sector))
                    if (taken.Add(lot)) { (b.x, b.y) = lot; break; }
            }
        }

        /// <summary>Finished buildings at their current level; one still rising for its first level does not count yet.</summary>
        public static IEnumerable<PlacedBuilding> Buildings()
        {
            foreach (var b in Data.buildings)
                if (b.level >= 1 && Enum.TryParse(b.type, out BuildingType type))
                    yield return new PlacedBuilding(b.sector, type, b.level);
        }

        // ---- Works: buildings, upgrades and links take real time ----------------------

        /// <summary>A work that ended during the last collection, for the map to celebrate.</summary>
        public struct Completion
        {
            public string Sector;
            public bool Link;
            public int X, Y, Level;
            public string Type;
        }

        /// <summary>Works finished since the interface last looked; it empties the list as it shows them.</summary>
        public static readonly List<Completion> Finished = new List<Completion>();

        public static int WorkCount()
        {
            int n = Data.links.Count;
            foreach (var b in Data.buildings) if (b.UnderWork) n++;
            return n;
        }

        public static bool WorkersFree => WorkCount() < ColonyData.MaxWorks;
        static string BusyText => $"Ya hay {ColonyData.MaxWorks} obras en curso: espera a que termine una";

        public static LinkSave LinkWork(string sector) => Data.links.Find(l => l.sector == sector);
        public static bool IsConnecting(string sector) => LinkWork(sector) != null;

        /// <summary>From 0 to 1, how far a work has come.</summary>
        public static float Progress(long start, long ready)
        {
            if (ready <= start) return 1;
            return (float)Math.Max(0, Math.Min(1, (Now().Ticks - start) / (double)(ready - start)));
        }

        public static double SecondsLeft(long ready) => Math.Max(0, (ready - Now().Ticks) / (double)TimeSpan.TicksPerSecond);

        /// <summary>"0:45", "4:10" or "1:05:00".</summary>
        public static string TimeText(double seconds)
        {
            int s = (int)Math.Ceiling(seconds);
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        static long NextReady()
        {
            long next = long.MaxValue;
            foreach (var l in Data.links) next = Math.Min(next, l.readyTicks);
            foreach (var b in Data.buildings) if (b.UnderWork) next = Math.Min(next, b.readyTicks);
            return next;
        }

        /// <summary>Completes every work due by this time.</summary>
        static void FinishWorks(long at)
        {
            for (int i = Data.links.Count - 1; i >= 0; i--)
            {
                var l = Data.links[i];
                if (l.readyTicks > at) continue;
                Data.links.RemoveAt(i);
                if (!Data.connected.Contains(l.sector)) Data.connected.Add(l.sector);
                Finished.Add(new Completion { Sector = l.sector, Link = true });
            }
            foreach (var b in Data.buildings)
            {
                if (!b.UnderWork || b.readyTicks > at) continue;
                b.level = b.pending;
                b.pending = 0;
                Finished.Add(new Completion { Sector = b.sector, X = b.x, Y = b.y, Level = b.level, Type = b.type });
            }
        }

        static long Start(int seconds, out long ready)
        {
            long now = Now().Ticks;
            ready = now + seconds * TimeSpan.TicksPerSecond;
            return now;
        }

        /// <summary>The economy state built from the save. The landing zone, once liberated, is always connected.</summary>
        public static ColonyState State()
        {
            var d = Data;
            var state = new ColonyState
            {
                Stock = new ResourceSet { Materials = d.materials, Data = d.data, Water = d.water, Food = d.food, Energy = d.energy, Population = d.population },
            };
            foreach (var id in d.connected) state.Connected.Add(id);
            if (SaveGame.Completed("landing")) state.Connected.Add("landing");
            state.Buildings.AddRange(Buildings());
            return state;
        }

        static void Store(ColonyState state)
        {
            var d = Data;
            d.materials = state.Stock.Materials; d.data = state.Stock.Data; d.water = state.Stock.Water;
            d.food = state.Stock.Food; d.energy = state.Stock.Energy; d.population = state.Stock.Population;
        }

        public static ResourceSet Rates() => State().Rates();
        public static bool IsConnected(string sector) => State().Connected.Contains(sector);
        public static double Development(string sector) => State().Development(sector);

        /// <summary>Buildings that work: those in connected zones. Only they help in missions.</summary>
        public static IEnumerable<PlacedBuilding> Working() => State().Working();

        /// <summary>Why a liberated zone cannot be connected yet, or null when it can.</summary>
        public static string RefuseConnection(CampaignSector sector)
        {
            if (!SaveGame.Completed(sector.Id)) return "Primero libera la zona en su tablero de defensa";
            if (IsConnected(sector.Id)) return "Ya está conectada";
            if (IsConnecting(sector.Id)) return "El enlace ya se está tendiendo";
            var previous = Array.Find(CampaignData.Sectors, s => s.Number == sector.Number - 1);
            if (previous != null && !IsConnected(previous.Id)) return $"Primero conecta {previous.Name}";
            ColonyData.ConnectionCost(sector, out int m, out int dt);
            if (Data.materials < m || Data.data < dt) return $"Faltan recursos: {m} materiales y {dt} datos";
            if (!WorkersFree) return BusyText;
            return null;
        }

        /// <summary>Pays for the link and starts laying it; the zone joins the colony when it is done.</summary>
        public static bool Connect(CampaignSector sector)
        {
            if (RefuseConnection(sector) != null) return false;
            Collect(out _);  // Production up to now is counted before the zone joins.
            ColonyData.ConnectionCost(sector, out int m, out int dt);
            Data.materials -= m;
            Data.data -= dt;
            long start = Start(LinkTime(sector), out long ready);
            Data.links.Add(new LinkSave { sector = sector.Id, startTicks = start, readyTicks = ready });
            SaveGame.Save();
            return true;
        }

        public static IReadOnlyList<BuildingSave> Saved => Data.buildings;
        /// <summary>The building on the lot that holds this tile.</summary>
        public static BuildingSave At(int x, int y)
        {
            var (ax, ay) = CampaignData.LotAnchor(x, y) ?? (x, y);
            return Data.buildings.Find(b => b.x == ax && b.y == ay);
        }
        public static int CountIn(string sector) => Data.buildings.FindAll(b => b.sector == sector).Count;

        /// <summary>Optional clock for tests; real time otherwise.</summary>
        public static Func<DateTime> Now = () => DateTime.UtcNow;

        /// <summary>Runs the colony forward by the real time since the last call (up to 8 hours). Returns what changed.</summary>
        /// <summary>
        /// Also completes the works that ended meanwhile: production runs at the old rates up to each finish
        /// time and at the new ones after it, all within the 8-hour limit.
        /// </summary>
        public static void Collect(out ResourceSet change)
        {
            long now = Now().Ticks;
            change = default;
            if (Data.productionTicks == 0 || now < Data.productionTicks)
            {
                Data.productionTicks = now;  // First visit, or a clock moved backward: start from now.
                FinishWorks(now);
                return;
            }
            double budget = ColonyData.OfflineCapHours;
            while (true)
            {
                long next = NextReady();
                long until = Math.Min(next, now);
                double hours = Math.Min(budget, Math.Max(0, (until - Data.productionTicks) / (double)TimeSpan.TicksPerHour));
                Data.productionTicks = Math.Max(Data.productionTicks, until);
                if (hours > 0)
                {
                    budget -= hours;
                    var state = State();
                    var part = state.Advance(hours);
                    Store(state);
                    change.Materials += part.Materials; change.Data += part.Data; change.Water += part.Water;
                    change.Food += part.Food; change.Energy += part.Energy; change.Population += part.Population;
                }
                if (next > now) break;
                FinishWorks(next);
            }
        }

        public static bool CanAfford(BuildingType type, int toLevel) =>
            Data.materials >= Materials(type, toLevel) && Data.data >= ColonyData.UpgradeData(type, toLevel);

        /// <summary>Materials for a level. A new building costs more for each one of its kind already in the colony.</summary>
        public static int Materials(BuildingType type, int toLevel)
        {
            int cost = ColonyData.UpgradeMaterials(type, toLevel);
            return toLevel > 1 ? cost : ColonyData.FirstCost(cost, Data.buildings.FindAll(b => b.type == type.ToString()).Count);
        }

        /// <summary>Why this tile cannot take a building, or null when it can.</summary>
        public static string RefuseTile(int x, int y)
        {
            var zone = CampaignData.ZoneAt(x, y);
            if (zone == null) return "Solo se construye dentro de las zonas liberadas";
            if (!SaveGame.Completed(zone.Id)) return $"Primero libera {zone.Name}";
            if (CampaignData.Reserved(x, y)) return "Esa casilla está ocupada";
            if (At(x, y) != null) return "Esa casilla está ocupada";
            if (CountIn(zone.Id) >= ColonyData.LotsFor(zone.Id)) return $"{zone.Name} ya no tiene espacio";
            return null;
        }

        /// <summary>Seconds a building level takes now, with the works yard's help.</summary>
        public static int BuildTime(int level) => (int)Math.Round(ColonyData.BuildSeconds(level) * (1 - ColonyData.WorkSpeed(Working())));
        public static int LinkTime(CampaignSector sector) => (int)Math.Round(ColonyData.ConnectSeconds(sector) * (1 - ColonyData.WorkSpeed(Working())));

        /// <summary>Why this kind of building cannot be started, or null: some may stand only once.</summary>
        public static string RefuseType(BuildingType type) =>
            ColonyData.Buildings[type].Unique && Data.buildings.Exists(b => b.type == type.ToString()) ? $"Solo puede haber un {ColonyData.Buildings[type].Name}" : null;

        /// <summary>Why no new work can start now, or null.</summary>
        public static string RefuseWork() => WorkersFree ? null : BusyText;

        /// <summary>Pays and starts the building; it works once its first level is done.</summary>
        public static bool Build(string sector, int x, int y, BuildingType type)
        {
            if (RefuseTile(x, y) != null || CampaignData.ZoneAt(x, y)?.Id != sector || !CanAfford(type, 1) || !WorkersFree || RefuseType(type) != null) return false;
            Collect(out _);  // Production up to now is counted at the old rate.
            Pay(type, 1);
            long start = Start(BuildTime(1), out long ready);
            // Each new building of a kind takes the next look, so a street of houses is not all the same.
            int style = Data.buildings.FindAll(b => b.type == type.ToString()).Count % ColonyData.Buildings[type].Styles;
            Data.buildings.Add(new BuildingSave { sector = sector, x = x, y = y, type = type.ToString(), level = 0, pending = 1, style = style, startTicks = start, readyTicks = ready });
            SaveGame.Save();
            return true;
        }

        /// <summary>Pays and starts the upgrade; the building keeps working at its level until it is done.</summary>
        public static bool Upgrade(int x, int y)
        {
            var b = At(x, y);
            if (b == null || b.UnderWork || !Enum.TryParse(b.type, out BuildingType type) ||
                b.level >= ColonyData.Buildings[type].MaxLevel || !CanAfford(type, b.level + 1) || !WorkersFree) return false;
            Collect(out _);
            Pay(type, b.level + 1);
            b.pending = b.level + 1;
            b.startTicks = Start(BuildTime(b.pending), out b.readyTicks);
            SaveGame.Save();
            return true;
        }

        static void Pay(BuildingType type, int level)
        {
            Data.materials -= Materials(type, level);
            Data.data -= ColonyData.UpgradeData(type, level);
        }

        public static void AddReward(int materials, int data)
        {
            Data.materials += materials;
            Data.data += data;
            SaveGame.Save();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public static void ResetForTests()
        {
            migrated = false;
            Finished.Clear();
        }
#endif
    }
}
