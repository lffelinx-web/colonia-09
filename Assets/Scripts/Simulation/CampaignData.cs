using System.Collections.Generic;

namespace Colonia09.Simulation
{
    /// <summary>One place on the colony map: a mission to secure it, and what it teaches.</summary>
    public sealed class CampaignSector
    {
        public int Number;
        public string Id, Name, Brief;
        /// <summary>Position on the map, from 0 to 1, with y measured from the bottom.</summary>
        public float MapX, MapY;
        /// <summary>Null while the sector is not playable yet.</summary>
        public LevelConfig Level;
        public Difficulty Difficulty = Difficulty.Tactical;
        public ModuleType[] NewModules = new ModuleType[0];
        public EnemyKind[] NewEnemies = new EnemyKind[0];
        public bool Playable => Level != null;
    }

    public sealed class EnemyInfo
    {
        public string Name, Description, Tip;
    }

    /// <summary>
    /// The campaign: the first sectors are a tutorial on small boards that add one element at a time.
    /// </summary>
    public static class CampaignData
    {
        const EnemyKind Sc = EnemyKind.Scout, Ru = EnemyKind.Runner, Ar = EnemyKind.Armor;
        const ModuleType Ge = ModuleType.Generator, Re = ModuleType.Reactor, Rl = ModuleType.Relay, Pu = ModuleType.Pulse, Wa = ModuleType.Wall;

        public static readonly CampaignSector[] Sectors =
        {
            new CampaignSector
            {
                Number = 1, Id = "landing", Name = "Zona de aterrizaje", MapX = .3f, MapY = .14f,
                Brief = "La nave de la colonia acaba de aterrizar. Aprende a cargar energía y a disparar antes de que llegue la primera patrulla.",
                NewModules = new[] { Ge, Pu }, NewEnemies = new[] { Sc },
                Level = new LevelConfig
                {
                    Id = "landing", Name = "Zona de aterrizaje", Width = 3, Height = 7, Waves = 2, CoreMax = 10, CoreRow = 5, CoreColumn = 1,
                    TargetOnlyOnBoard = true, EnemySpeed = .85, StartCharge = 150, Modules = new[] { Ge, Pu },
                    WaveSpecs = new[]
                    {
                        new WaveSpec(5, 3.8, .75, Sc),
                        new WaveSpec(8, 3.3, .85, Sc),
                    },
                },
            },
            new CampaignSector
            {
                Number = 2, Id = "camp", Name = "Campamento", MapX = .66f, MapY = .25f,
                Brief = "El campamento queda lejos del Núcleo. Usa Relés para llevar la conexión hasta las defensas del frente.",
                NewModules = new[] { Rl }, NewEnemies = new[] { Ru },
                Level = new LevelConfig
                {
                    Id = "camp", Name = "Campamento", Width = 5, Height = 8, Waves = 3, CoreMax = 10, CoreRow = 6, CoreColumn = 2,
                    TargetOnlyOnBoard = true, EnemySpeed = .72, StartCharge = 200, Modules = new[] { Ge, Rl, Pu },
                    WaveSpecs = new[]
                    {
                        new WaveSpec(7, 3.5, .8, Sc),  // Runners come from the second wave, once the line is built.
                        new WaveSpec(9, 3.2, .86, Sc, Ru),
                        new WaveSpec(11, 3.0, .93, Sc, Ru, Ru),
                    },
                },
            },
            new CampaignSector
            {
                Number = 3, Id = "well", Name = "Pozo de agua", MapX = .36f, MapY = .37f,
                Brief = "El pozo abastece a toda la colonia. Un segundo Núcleo amplía la batería y las Barreras frenan al Blindado.",
                NewModules = new[] { Re, Wa }, NewEnemies = new[] { Ar },
                Level = new LevelConfig
                {
                    Id = "well", Name = "Pozo de agua", Width = 5, Height = 9, Waves = 4, CoreMax = 12, CoreRow = 7, CoreColumn = 2,
                    TargetOnlyOnBoard = true, EnemySpeed = .8, StartCharge = 230, Modules = new[] { Ge, Re, Rl, Pu, Wa },
                    WaveSpecs = new[]
                    {
                        new WaveSpec(8, 3.3, .85, Sc, Ru, Sc),
                        new WaveSpec(10, 3.1, .9, Sc, Ru, Sc, Ru, Ar),
                        new WaveSpec(12, 2.9, .95, Ru, Sc, Ar, Ru),
                        new WaveSpec(14, 2.8, 1.0, Ru, Ar, Sc, Ru, Ar),
                    },
                },
            },
            new CampaignSector { Number = 4, Id = "greenhouse", Name = "Invernaderos", MapX = .68f, MapY = .5f,
                Brief = "Criogénico y enemigos que resisten ciertas armas.", NewModules = new[] { ModuleType.Cryo }, NewEnemies = new[] { EnemyKind.Bulwark, EnemyKind.Splitter } },
            new CampaignSector { Number = 5, Id = "mines", Name = "Minas", MapX = .3f, MapY = .62f,
                Brief = "La Tesla y los enemigos que bloquean tu red.", NewModules = new[] { ModuleType.Tesla }, NewEnemies = new[] { EnemyKind.Thermal, EnemyKind.Jammer } },
            new CampaignSector { Number = 6, Id = "workshop", Name = "Taller central", MapX = .64f, MapY = .72f,
                Brief = "Repara tus módulos y protégelos del Cazador.", NewModules = new[] { ModuleType.Repair }, NewEnemies = new[] { EnemyKind.Sapper, EnemyKind.Grounder, EnemyKind.Hunter } },
            new CampaignSector { Number = 7, Id = "port", Name = "Puerto", MapX = .34f, MapY = .82f,
                Brief = "Todo el arsenal contra tanques y aeronaves.", NewEnemies = new[] { EnemyKind.Tank, EnemyKind.Helicopter, EnemyKind.Bomber } },
            new CampaignSector { Number = 8, Id = "core", Name = "Núcleo de la colonia", MapX = .6f, MapY = .91f,
                Brief = "La defensa final contra el Centinela.", NewEnemies = new[] { EnemyKind.Commander } },
        };

        public static CampaignSector Find(string id) => System.Array.Find(Sectors, s => s.Id == id);

        // ---- Isometric colony layout (must match Tools/Render/render_colony.py) ----------

        public const int ColonySize = 26;
        /// <summary>Each zone is 5 x 5 tiles around its center.</summary>
        public const int ZoneRadius = 2;
        static readonly int[,] ZoneCenters = { { 22, 22 }, { 23, 15 }, { 13, 21 }, { 17, 11 }, { 8, 15 }, { 11, 7 }, { 4, 9 }, { 4, 3 } };

        public static (int x, int y) ZoneCenter(CampaignSector sector) => (ZoneCenters[sector.Number - 1, 0], ZoneCenters[sector.Number - 1, 1]);

        /// <summary>The sector whose zone holds this tile, or null for open ground.</summary>
        public static CampaignSector ZoneAt(int x, int y)
        {
            foreach (var s in Sectors)
            {
                var (cx, cy) = ZoneCenter(s);
                if (System.Math.Abs(x - cx) <= ZoneRadius && System.Math.Abs(y - cy) <= ZoneRadius) return s;
            }
            return null;
        }

        /// <summary>Tiles kept for decoration: lamps on the zone corners and the zone's landmark on the 3 x 3 center.</summary>
        public static bool Reserved(int x, int y)
        {
            var zone = ZoneAt(x, y);
            if (zone == null) return false;
            var (cx, cy) = ZoneCenter(zone);
            bool corner = System.Math.Abs(x - cx) == ZoneRadius && System.Math.Abs(y - cy) == ZoneRadius;
            bool landmark = System.Math.Abs(x - cx) <= 1 && System.Math.Abs(y - cy) <= 1;
            return corner || landmark;
        }

        /// <summary>Road tiles from one zone to the next: along x, then along y, outside both zones.</summary>
        public static List<(int x, int y)> Road(CampaignSector from, CampaignSector to)
        {
            var road = new List<(int, int)>();
            var (x, y) = ZoneCenter(from);
            var (tx, ty) = ZoneCenter(to);
            while (x != tx) { x += System.Math.Sign(tx - x); if (ZoneAt(x, y) == null) road.Add((x, y)); }
            while (y != ty) { y += System.Math.Sign(ty - y); if (ZoneAt(x, y) == null) road.Add((x, y)); }
            return road;
        }

        /// <summary>Stars for a won mission, from the base integrity left: 3 above 80 %, 2 above 45 %, otherwise 1.</summary>
        public static int Stars(double core, int coreMax)
        {
            double share = core / coreMax;
            return share >= .8 ? 3 : share >= .45 ? 2 : 1;
        }

        public static readonly Dictionary<EnemyKind, EnemyInfo> Enemies = new Dictionary<EnemyKind, EnemyInfo>
        {
            { EnemyKind.Scout, new EnemyInfo { Name = "Explorador", Description = "Patrulla ligera que avanza en línea recta y golpea lo que encuentra.", Tip = "Un Pulso en su columna lo detiene." } },
            { EnemyKind.Runner, new EnemyInfo { Name = "Corredor", Description = "Muy rápido pero frágil. Llega antes que los demás.", Tip = "Coloca los Pulsos con tiempo: no da margen para reaccionar." } },
            { EnemyKind.Armor, new EnemyInfo { Name = "Blindado", Description = "Lento y muy resistente. Dispara proyectiles contra tus módulos.", Tip = "Concentra varios Pulsos y frénalo con Barreras." } },
            { EnemyKind.Jammer, new EnemyInfo { Name = "Interferente", Description = "Bloquea los módulos de su columna mientras avanza.", Tip = "Elimínalo primero: apaga tus defensas." } },
            { EnemyKind.Splitter, new EnemyInfo { Name = "Divisor", Description = "Al ser destruido se parte en dos Corredores.", Tip = "Deja defensas detrás para los fragmentos." } },
            { EnemyKind.Sapper, new EnemyInfo { Name = "Saboteador", Description = "Busca tus módulos para dañarlos desde cerca.", Tip = "Protege la red con Barreras." } },
            { EnemyKind.Bulwark, new EnemyInfo { Name = "Coraza", Description = "Un gran escudo frontal resiste los disparos del Pulso.", Tip = "Usa el Criogénico." } },
            { EnemyKind.Thermal, new EnemyInfo { Name = "Térmico", Description = "Su núcleo caliente resiste el frío.", Tip = "Usa la Tesla." } },
            { EnemyKind.Grounder, new EnemyInfo { Name = "Aislante", Description = "Aislado contra descargas eléctricas.", Tip = "Usa el Pulso." } },
            { EnemyKind.Hunter, new EnemyInfo { Name = "Cazador", Description = "Francotirador que elige Talleres y Criogénicos.", Tip = "Sus disparos se anuncian: protege el objetivo marcado." } },
            { EnemyKind.Commander, new EnemyInfo { Name = "Centinela", Description = "Fortaleza con escudo que protege a su escolta.", Tip = "Prepara toda tu defensa para el final." } },
            { EnemyKind.Tank, new EnemyInfo { Name = "Tanque", Description = "Vehículo blindado que bombardea varias casillas.", Tip = "Las casillas marcadas recibirán el impacto." } },
            { EnemyKind.Helicopter, new EnemyInfo { Name = "Helicóptero", Description = "Ataca tus sistemas desde el aire.", Tip = "El Pulso le hace más daño." } },
            { EnemyKind.Bomber, new EnemyInfo { Name = "Bombardero", Description = "Marca hasta cuatro casillas y las bombardea.", Tip = "Evita concentrar módulos en el área marcada." } },
        };
    }
}
