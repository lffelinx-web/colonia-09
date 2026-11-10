using System;
using System.Collections.Generic;

namespace Colonia09.Simulation
{
    public enum BuildingType { Extractor, Laboratory, Refinery, Fortification, Housing, Solar }

    public sealed class BuildingDef
    {
        public string Name, Description;
        /// <summary>Indexed by level - 1. Level 1 is the construction cost.</summary>
        public int[] MaterialCost, DataCost;
        public double[] MaterialsPerHour = { 0, 0, 0 }, DataPerHour = { 0, 0, 0 }, EnergyPerHour = { 0, 0, 0 };
        /// <summary>Colonists the building can house, by level.</summary>
        public int[] Housing = { 0, 0, 0 };
        /// <summary>Mission bonuses by level: starting energy and base integrity.</summary>
        public int[] StartCharge = { 0, 0, 0 }, CoreBonus = { 0, 0, 0 };
        /// <summary>Energy the building needs per hour to run at full output.</summary>
        public double Upkeep;
    }

    /// <summary>What a zone's landmark gives once the zone is liberated and connected.</summary>
    public sealed class ZoneOutput
    {
        public double Materials, Data, Water, Food, Energy;
        public int Housing;
        public string Summary;
    }

    /// <summary>A building standing on a tile of a liberated zone.</summary>
    public struct PlacedBuilding
    {
        public string Sector;
        public BuildingType Type;
        public int Level;
        public PlacedBuilding(string sector, BuildingType type, int level) { Sector = sector; Type = type; Level = level; }
    }

    /// <summary>Six resources, as amounts or as rates per hour.</summary>
    public struct ResourceSet
    {
        public double Materials, Data, Water, Food, Energy, Population;
    }

    /// <summary>
    /// The colony's state for the economy: stocks, connected zones and buildings. The presentation
    /// copies it from the save, advances it by the real hours that passed, and copies it back.
    /// </summary>
    public sealed class ColonyState
    {
        public ResourceSet Stock;
        public HashSet<string> Connected = new HashSet<string>();
        public List<PlacedBuilding> Buildings = new List<PlacedBuilding>();

        /// <summary>Only buildings of connected zones work.</summary>
        public IEnumerable<PlacedBuilding> Working()
        {
            foreach (var b in Buildings) if (Connected.Contains(b.Sector)) yield return b;
        }

        public int Capacity
        {
            get
            {
                int cap = 0;
                foreach (var id in Connected) if (ColonyData.Zones.TryGetValue(id, out var z)) cap += z.Housing;
                foreach (var b in Working()) cap += ColonyData.Buildings[b.Type].Housing[b.Level - 1];
                return cap;
            }
        }

        /// <summary>Production minus consumption per hour, at the current stocks.</summary>
        public ResourceSet Rates()
        {
            var r = new ResourceSet();
            double upkeep = 0, energyIn = 0;
            foreach (var id in Connected)
                if (ColonyData.Zones.TryGetValue(id, out var z))
                {
                    r.Materials += z.Materials; r.Data += z.Data; r.Water += z.Water; r.Food += z.Food; energyIn += z.Energy;
                }
            // Colonists boost everything they work on, up to +50 %.
            double crew = 1 + Math.Min(.5, Stock.Population * .01);
            // Without stored energy, buildings run at half output.
            double power = Stock.Energy > .01 ? 1 : .5;
            foreach (var b in Working())
            {
                var def = ColonyData.Buildings[b.Type];
                r.Materials += def.MaterialsPerHour[b.Level - 1] * crew * power;
                r.Data += def.DataPerHour[b.Level - 1] * crew * power;
                energyIn += def.EnergyPerHour[b.Level - 1];
                upkeep += def.Upkeep;
            }
            r.Energy = energyIn - upkeep;
            // Colonists drink and eat; they arrive while there is water and food and room, and leave when there is none.
            r.Water -= Stock.Population * .1;
            r.Food -= Stock.Population * .1;
            bool supplied = Stock.Water > .01 && Stock.Food > .01;
            int cap = Capacity;
            r.Population = supplied ? (Stock.Population < cap ? 2 : 0) : (Stock.Population > 0 ? -1 : 0);
            return r;
        }

        /// <summary>Runs the colony forward by real hours (capped at the offline limit), in small steps.</summary>
        public ResourceSet Advance(double hours)
        {
            hours = Math.Max(0, Math.Min(ColonyData.OfflineCapHours, hours));
            var start = Stock;
            const double Step = .05;  // Three minutes: fine enough for stocks that run out mid-way.
            for (double done = 0; done < hours - 1e-9; done += Step)
            {
                double dt = Math.Min(Step, hours - done);
                var r = Rates();
                Stock.Materials = Math.Max(0, Stock.Materials + r.Materials * dt);
                Stock.Data = Math.Max(0, Stock.Data + r.Data * dt);
                Stock.Water = Math.Max(0, Stock.Water + r.Water * dt);
                Stock.Food = Math.Max(0, Stock.Food + r.Food * dt);
                Stock.Energy = Math.Max(0, Stock.Energy + r.Energy * dt);
                Stock.Population = Math.Max(0, Math.Min(Capacity, Stock.Population + r.Population * dt));
            }
            return new ResourceSet
            {
                Materials = Stock.Materials - start.Materials, Data = Stock.Data - start.Data, Water = Stock.Water - start.Water,
                Food = Stock.Food - start.Food, Energy = Stock.Energy - start.Energy, Population = Stock.Population - start.Population,
            };
        }

        /// <summary>Zone development from 0 to 1: building levels against what the zone can hold.</summary>
        public double Development(string sector)
        {
            int levels = 0;
            foreach (var b in Buildings) if (b.Sector == sector) levels += b.Level;
            return Math.Min(1, levels / (double)(ColonyData.LotsFor(sector) * ColonyData.MaxLevel));
        }
    }

    /// <summary>
    /// The colony layer of the campaign: zone landmarks, buildings, their costs and production per
    /// real hour, connections between zones and the bonuses the colony gives in missions. Pure rules.
    /// </summary>
    public static class ColonyData
    {
        public const int MaxLevel = 3;
        /// <summary>Production keeps running while the game is closed, up to this many hours.</summary>
        public const double OfflineCapHours = 8;
        /// <summary>At most this many works (buildings, upgrades and links) run at the same time.</summary>
        public const int MaxWorks = 2;

        /// <summary>Real seconds to raise a building to this level: 1 minute, 5 minutes, 20 minutes.</summary>
        public static int BuildSeconds(int level) => level <= 1 ? 60 : level == 2 ? 300 : 1200;

        /// <summary>Real seconds to lay a zone's energy link: 2 minutes for the camp, up to 10 for the colony core.</summary>
        public static int ConnectSeconds(CampaignSector sector) => sector.Number <= 1 ? 0 : 120 + (sector.Number - 2) * 80;

        public static readonly BuildingType[] Types =
            { BuildingType.Extractor, BuildingType.Laboratory, BuildingType.Solar, BuildingType.Housing, BuildingType.Refinery, BuildingType.Fortification };

        public static readonly Dictionary<BuildingType, BuildingDef> Buildings = new Dictionary<BuildingType, BuildingDef>
        {
            { BuildingType.Extractor, new BuildingDef { Name = "Extractor", Description = "Extrae materiales del subsuelo, también con el juego cerrado.",
                MaterialCost = new[] { 40, 90, 180 }, DataCost = new[] { 0, 10, 30 }, MaterialsPerHour = new double[] { 12, 24, 40 }, Upkeep = 2 } },
            { BuildingType.Laboratory, new BuildingDef { Name = "Laboratorio", Description = "Analiza muestras y genera datos de investigación.",
                MaterialCost = new[] { 60, 120, 220 }, DataCost = new[] { 0, 15, 35 }, DataPerHour = new double[] { 4, 8, 14 }, Upkeep = 2 } },
            { BuildingType.Solar, new BuildingDef { Name = "Planta solar", Description = "Paneles que dan la energía que gastan los demás edificios.",
                MaterialCost = new[] { 50, 100, 180 }, DataCost = new[] { 0, 10, 25 }, EnergyPerHour = new double[] { 8, 16, 26 } } },
            { BuildingType.Housing, new BuildingDef { Name = "Vivienda", Description = "Más espacio para colonos: cada uno suma producción a toda la colonia.",
                MaterialCost = new[] { 50, 110, 200 }, DataCost = new[] { 0, 10, 30 }, Housing = new[] { 8, 16, 28 }, Upkeep = 1 } },
            { BuildingType.Refinery, new BuildingDef { Name = "Refinería", Description = "Prepara combustible: empiezas cada misión con más energía.",
                MaterialCost = new[] { 80, 150, 260 }, DataCost = new[] { 0, 20, 45 }, StartCharge = new[] { 15, 30, 50 }, Upkeep = 1 } },
            { BuildingType.Fortification, new BuildingDef { Name = "Fortificación", Description = "Refuerza la base: más integridad en cada misión.",
                MaterialCost = new[] { 70, 140, 240 }, DataCost = new[] { 0, 20, 40 }, CoreBonus = new[] { 1, 2, 4 }, Upkeep = 1 } },
        };

        /// <summary>Each zone's landmark: its own resource, once the zone is connected.</summary>
        public static readonly Dictionary<string, ZoneOutput> Zones = new Dictionary<string, ZoneOutput>
        {
            { "landing", new ZoneOutput { Energy = 8, Water = 3, Food = 3, Housing = 10, Summary = "Energía, provisiones y espacio para 10 colonos" } },
            { "camp", new ZoneOutput { Data = 2, Housing = 25, Summary = "Espacio para 25 colonos" } },
            { "well", new ZoneOutput { Water = 12, Summary = "Agua para la colonia" } },
            { "greenhouse", new ZoneOutput { Food = 10, Summary = "Comida para la colonia" } },
            { "mines", new ZoneOutput { Materials = 25, Summary = "Materiales en abundancia" } },
            { "workshop", new ZoneOutput { Data = 8, Summary = "Datos de investigación" } },
            { "port", new ZoneOutput { Materials = 10, Data = 4, Summary = "Comercio: materiales y datos" } },
            { "core", new ZoneOutput { Energy = 25, Summary = "Mucha energía" } },
        };

        /// <summary>How many buildings a liberated zone can hold. Later zones are larger colonies.</summary>
        public static int LotsFor(string sectorId)
        {
            switch (sectorId)
            {
                case "landing": return 4;
                case "camp": return 5;
                default: return 6;
            }
        }

        public static int UpgradeMaterials(BuildingType type, int toLevel) => Buildings[type].MaterialCost[toLevel - 1];
        public static int UpgradeData(BuildingType type, int toLevel) => Buildings[type].DataCost[toLevel - 1];

        /// <summary>Price to lay the energy link from a liberated zone to the colony. The landing zone is the hub and needs none.</summary>
        public static void ConnectionCost(CampaignSector sector, out int materials, out int data)
        {
            materials = sector.Number <= 1 ? 0 : 40 * (sector.Number - 1) + 20;
            data = sector.Number <= 1 ? 0 : 5 * (sector.Number - 1);
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
