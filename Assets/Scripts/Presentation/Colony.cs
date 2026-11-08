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

        /// <summary>Buildings saved before the isometric map used numbered lots; give them a tile in their zone.</summary>
        static void Migrate(SaveData data)
        {
            foreach (var b in data.buildings)
            {
                if (CampaignData.ZoneAt(b.x, b.y)?.Id == b.sector) continue;
                var sector = CampaignData.Find(b.sector);
                if (sector == null) continue;
                var free = FreeTiles(data, sector);
                if (free.Count > 0) (b.x, b.y) = free[0];
            }
        }

        static List<(int x, int y)> FreeTiles(SaveData data, CampaignSector sector)
        {
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            var free = new List<(int, int)>();
            for (int y = cy - CampaignData.ZoneRadius; y <= cy + CampaignData.ZoneRadius; y++)
                for (int x = cx - CampaignData.ZoneRadius; x <= cx + CampaignData.ZoneRadius; x++)
                    if (!CampaignData.Reserved(x, y) && data.buildings.Find(b => b.x == x && b.y == y && b.sector == sector.Id) == null) free.Add((x, y));
            return free;
        }

        public static IEnumerable<PlacedBuilding> Buildings()
        {
            foreach (var b in Data.buildings)
                if (Enum.TryParse(b.type, out BuildingType type))
                    yield return new PlacedBuilding(b.sector, type, b.level);
        }

        public static IReadOnlyList<BuildingSave> Saved => Data.buildings;
        public static BuildingSave At(int x, int y) => Data.buildings.Find(b => b.x == x && b.y == y);
        public static int CountIn(string sector) => Data.buildings.FindAll(b => b.sector == sector).Count;

        /// <summary>Optional clock for tests; real time otherwise.</summary>
        public static Func<DateTime> Now = () => DateTime.UtcNow;

        /// <summary>Adds what the buildings produced since the last call (up to 8 hours). Returns the amounts added.</summary>
        public static void Collect(out double materials, out double data)
        {
            long now = Now().Ticks;
            materials = data = 0;
            if (Data.productionTicks == 0 || Data.buildings.Count == 0)
            {
                Data.productionTicks = now;
                return;
            }
            double seconds = (now - Data.productionTicks) / (double)TimeSpan.TicksPerSecond;
            ColonyData.Produced(Buildings(), seconds, out materials, out data);
            Data.materials += materials;
            Data.data += data;
            Data.productionTicks = now;  // A clock moved backward simply restarts from now.
        }

        public static bool CanAfford(BuildingType type, int toLevel) =>
            Data.materials >= ColonyData.UpgradeMaterials(type, toLevel) && Data.data >= ColonyData.UpgradeData(type, toLevel);

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

        public static bool Build(string sector, int x, int y, BuildingType type)
        {
            if (RefuseTile(x, y) != null || CampaignData.ZoneAt(x, y)?.Id != sector || !CanAfford(type, 1)) return false;
            Collect(out _, out _);  // Production up to now is counted at the old rate.
            Pay(type, 1);
            Data.buildings.Add(new BuildingSave { sector = sector, x = x, y = y, type = type.ToString(), level = 1 });
            SaveGame.Save();
            return true;
        }

        public static bool Upgrade(int x, int y)
        {
            var b = At(x, y);
            if (b == null || b.level >= ColonyData.MaxLevel || !Enum.TryParse(b.type, out BuildingType type) || !CanAfford(type, b.level + 1)) return false;
            Collect(out _, out _);
            Pay(type, b.level + 1);
            b.level++;
            SaveGame.Save();
            return true;
        }

        static void Pay(BuildingType type, int level)
        {
            Data.materials -= ColonyData.UpgradeMaterials(type, level);
            Data.data -= ColonyData.UpgradeData(type, level);
        }

        public static void AddReward(int materials, int data)
        {
            Data.materials += materials;
            Data.data += data;
            SaveGame.Save();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public static void ResetForTests() => migrated = false;
#endif
    }
}
