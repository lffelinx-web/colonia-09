using System;
using System.Collections.Generic;

namespace Colonia09.Simulation
{
    public enum BuildingType { Extractor, Laboratory, Refinery, Fortification, Housing }

    public sealed class BuildingDef
    {
        public string Name, Description;
        /// <summary>Indexed by level - 1. Level 1 is the construction cost.</summary>
        public int[] MaterialCost, DataCost;
        public double[] MaterialsPerHour = { 0, 0, 0 }, DataPerHour = { 0, 0, 0 };
        /// <summary>Mission bonuses by level: starting energy, base integrity, and production boost for the sector.</summary>
        public int[] StartCharge = { 0, 0, 0 }, CoreBonus = { 0, 0, 0 };
        public double[] SectorBoost = { 0, 0, 0 };
    }

    /// <summary>A building standing on a lot of a secured sector.</summary>
    public struct PlacedBuilding
    {
        public string Sector;
        public BuildingType Type;
        public int Level;
        public PlacedBuilding(string sector, BuildingType type, int level) { Sector = sector; Type = type; Level = level; }
    }

    /// <summary>
    /// The colony layer of the campaign: buildings on the lots of secured sectors, what they cost,
    /// what they produce per real hour, and the bonuses they give in missions. Pure rules, no Unity.
    /// </summary>
    public static class ColonyData
    {
        public const int MaxLevel = 3;
        /// <summary>Production keeps running while the game is closed, up to this many hours.</summary>
        public const double OfflineCapHours = 8;

        public static readonly BuildingType[] Types =
            { BuildingType.Extractor, BuildingType.Laboratory, BuildingType.Refinery, BuildingType.Fortification, BuildingType.Housing };

        public static readonly Dictionary<BuildingType, BuildingDef> Buildings = new Dictionary<BuildingType, BuildingDef>
        {
            { BuildingType.Extractor, new BuildingDef { Name = "Extractor", Description = "Extrae materiales del subsuelo, también con el juego cerrado.",
                MaterialCost = new[] { 40, 90, 180 }, DataCost = new[] { 0, 10, 30 }, MaterialsPerHour = new double[] { 12, 24, 40 } } },
            { BuildingType.Laboratory, new BuildingDef { Name = "Laboratorio", Description = "Analiza muestras y genera datos de investigación.",
                MaterialCost = new[] { 60, 120, 220 }, DataCost = new[] { 0, 15, 35 }, DataPerHour = new double[] { 4, 8, 14 } } },
            { BuildingType.Refinery, new BuildingDef { Name = "Refinería", Description = "Prepara combustible: empiezas cada misión con más energía.",
                MaterialCost = new[] { 80, 150, 260 }, DataCost = new[] { 0, 20, 45 }, StartCharge = new[] { 15, 30, 50 } } },
            { BuildingType.Fortification, new BuildingDef { Name = "Fortificación", Description = "Refuerza la base: más integridad en cada misión.",
                MaterialCost = new[] { 70, 140, 240 }, DataCost = new[] { 0, 20, 40 }, CoreBonus = new[] { 1, 2, 4 } } },
            { BuildingType.Housing, new BuildingDef { Name = "Vivienda", Description = "Colonos que trabajan en el sector: más producción de sus edificios.",
                MaterialCost = new[] { 50, 110, 200 }, DataCost = new[] { 0, 10, 30 }, SectorBoost = new[] { .2, .4, .7 } } },
        };

        /// <summary>Building lots each sector offers once it is secured.</summary>
        public static int LotsFor(string sectorId)
        {
            switch (sectorId)
            {
                case "landing": return 3;
                case "camp": return 4;
                case "well": return 4;
                default: return 4;
            }
        }

        public static int UpgradeMaterials(BuildingType type, int toLevel) => Buildings[type].MaterialCost[toLevel - 1];
        public static int UpgradeData(BuildingType type, int toLevel) => Buildings[type].DataCost[toLevel - 1];

        /// <summary>Total production per hour. Housing multiplies the output of its own sector.</summary>
        public static void RatePerHour(IEnumerable<PlacedBuilding> buildings, out double materials, out double data)
        {
            materials = data = 0;
            var boost = new Dictionary<string, double>();
            foreach (var b in buildings)
                if (b.Type == BuildingType.Housing)
                {
                    boost.TryGetValue(b.Sector, out var current);
                    boost[b.Sector] = current + Buildings[b.Type].SectorBoost[b.Level - 1];
                }
            foreach (var b in buildings)
            {
                var def = Buildings[b.Type];
                boost.TryGetValue(b.Sector, out var extra);
                materials += def.MaterialsPerHour[b.Level - 1] * (1 + extra);
                data += def.DataPerHour[b.Level - 1] * (1 + extra);
            }
        }

        /// <summary>Resources produced over the elapsed real time, capped at the offline limit. Clock changes backward give nothing.</summary>
        public static void Produced(IEnumerable<PlacedBuilding> buildings, double elapsedSeconds, out double materials, out double data)
        {
            RatePerHour(buildings, out var mph, out var dph);
            double hours = Math.Max(0, Math.Min(OfflineCapHours, elapsedSeconds / 3600));
            materials = mph * hours;
            data = dph * hours;
        }

        public static void MissionBonuses(IEnumerable<PlacedBuilding> buildings, out int startCharge, out int core)
        {
            startCharge = core = 0;
            foreach (var b in buildings)
            {
                startCharge += Buildings[b.Type].StartCharge[b.Level - 1];
                core += Buildings[b.Type].CoreBonus[b.Level - 1];
            }
        }

        /// <summary>A copy of the sector's level with the colony's bonuses applied.</summary>
        public static LevelConfig Boosted(LevelConfig level, IEnumerable<PlacedBuilding> buildings)
        {
            MissionBonuses(buildings, out int charge, out int core);
            var copy = level.Clone();
            if (level.StartCharge.HasValue) copy.StartCharge = level.StartCharge.Value + charge;
            copy.CoreMax = level.CoreMax + core;
            return copy;
        }

        /// <summary>Resources for a won mission. The first win of a sector pays much more than replays.</summary>
        public static void MissionReward(int stars, bool firstWin, out int materials, out int data)
        {
            if (stars <= 0) { materials = data = 0; return; }
            materials = firstWin ? 60 + 30 * stars : 15 + 10 * stars;
            data = firstWin ? 10 * stars : 3 * stars;
        }
    }
}
