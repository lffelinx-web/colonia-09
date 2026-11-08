using System;
using System.Collections.Generic;
using Colonia09.Simulation;

namespace Colonia09.Presentation
{
    /// <summary>Colony actions on the saved progress: collect production, build, upgrade, and mission rewards.</summary>
    public static class Colony
    {
        static SaveData Data => SaveGame.Data;

        public static IEnumerable<PlacedBuilding> Buildings()
        {
            foreach (var b in Data.buildings)
                if (Enum.TryParse(b.type, out BuildingType type))
                    yield return new PlacedBuilding(b.sector, type, b.level);
        }

        public static IEnumerable<PlacedBuilding> BuildingsIn(string sector)
        {
            foreach (var b in Buildings()) if (b.Sector == sector) yield return b;
        }

        public static BuildingSave At(string sector, int slot) => Data.buildings.Find(b => b.sector == sector && b.slot == slot);

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

        public static bool Build(string sector, int slot, BuildingType type)
        {
            if (At(sector, slot) != null || slot >= ColonyData.LotsFor(sector) || !SaveGame.Completed(sector) || !CanAfford(type, 1)) return false;
            Collect(out _, out _);  // Production up to now is counted at the old rate.
            Pay(type, 1);
            Data.buildings.Add(new BuildingSave { sector = sector, slot = slot, type = type.ToString(), level = 1 });
            SaveGame.Save();
            return true;
        }

        public static bool Upgrade(string sector, int slot)
        {
            var b = At(sector, slot);
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
    }
}
