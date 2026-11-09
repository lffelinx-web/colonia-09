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
        const EnemyKind Ja = EnemyKind.Jammer, Sp = EnemyKind.Splitter, Sa = EnemyKind.Sapper, Bu = EnemyKind.Bulwark, Th = EnemyKind.Thermal,
            Gr = EnemyKind.Grounder, Hu = EnemyKind.Hunter, Tk = EnemyKind.Tank, He = EnemyKind.Helicopter, Bo = EnemyKind.Bomber;
        const ModuleType Ge = ModuleType.Generator, Re = ModuleType.Reactor, Rl = ModuleType.Relay, Pu = ModuleType.Pulse, Wa = ModuleType.Wall,
            Cr = ModuleType.Cryo, Te = ModuleType.Tesla, Rp = ModuleType.Repair;

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
            new CampaignSector
            {
                Number = 4, Id = "greenhouse", Name = "Invernaderos", MapX = .68f, MapY = .5f,
                Brief = "Los invernaderos alimentan a la colonia. El Criogénico frena a la Coraza, que resiste los disparos del Pulso.",
                NewModules = new[] { Cr }, NewEnemies = new[] { Bu, Sp },
                Level = new LevelConfig
                {
                    Id = "greenhouse", Name = "Invernaderos", Width = 6, Height = 9, Waves = 4, CoreMax = 12, CoreRow = 7, CoreColumn = 2,
                    TargetOnlyOnBoard = true, EnemySpeed = .85, StartCharge = 250, Modules = new[] { Ge, Re, Rl, Pu, Cr, Wa },
                    WaveSpecs = new[]
                    {
                        new WaveSpec(9, 3.2, .9, Sc, Ru, Sc, Sp),
                        new WaveSpec(11, 3.0, .95, Sc, Bu, Ru, Sp),
                        new WaveSpec(13, 2.9, 1.0, Ru, Bu, Sc, Sp, Ar),
                        new WaveSpec(15, 2.7, 1.05, Bu, Sp, Ru, Ar, Bu, Sc),
                    },
                },
            },
            new CampaignSector
            {
                Number = 5, Id = "mines", Name = "Minas", MapX = .3f, MapY = .62f,
                Brief = "Las minas dan materiales. La Tesla salta entre enemigos y vence al Térmico; el Interferente apaga tus módulos.",
                NewModules = new[] { Te }, NewEnemies = new[] { Th, Ja },
                Level = new LevelConfig
                {
                    Id = "mines", Name = "Minas", Width = 6, Height = 10, Waves = 5, CoreMax = 12, CoreRow = 8, CoreColumn = 2,
                    TargetOnlyOnBoard = true, EnemySpeed = .88, StartCharge = 270, Modules = new[] { Ge, Re, Rl, Pu, Cr, Te, Wa },
                    WaveSpecs = new[]
                    {
                        new WaveSpec(9, 3.1, .92, Sc, Ru, Th, Sc),
                        new WaveSpec(11, 2.9, .97, Sc, Th, Ru, Ja, Sp),
                        new WaveSpec(13, 2.8, 1.02, Th, Ru, Bu, Ja, Sc),
                        new WaveSpec(15, 2.6, 1.08, Th, Sp, Ja, Bu, Ru, Ar),
                        new WaveSpec(17, 2.5, 1.14, Th, Ja, Bu, Ru, Ar, Sp, Th),
                    },
                },
            },
            new CampaignSector
            {
                Number = 6, Id = "workshop", Name = "Taller central", MapX = .64f, MapY = .72f,
                Brief = "Aquí se reparan las máquinas de la colonia. El Taller cura tus módulos; el Cazador viene por él.",
                NewModules = new[] { Rp }, NewEnemies = new[] { Gr, Sa, Hu },
                Level = new LevelConfig
                {
                    Id = "workshop", Name = "Taller central", Width = 7, Height = 10, Waves = 5, CoreMax = 13, CoreRow = 8, CoreColumn = 3,
                    TargetOnlyOnBoard = true, EnemySpeed = .9, StartCharge = 290, Modules = new[] { Ge, Re, Rl, Pu, Cr, Te, Wa, Rp },
                    WaveSpecs = new[]
                    {
                        new WaveSpec(10, 3.0, .95, Sc, Gr, Ru, Sc),
                        new WaveSpec(12, 2.85, 1.0, Gr, Ru, Sa, Th, Sc, Ru),
                        new WaveSpec(14, 2.7, 1.06, Gr, Ru, Th, Bu, Sa, Sc, Hu),
                        new WaveSpec(16, 2.55, 1.12, Gr, Sa, Th, Bu, Ru, Ja, Ar, Ru),
                        new WaveSpec(18, 2.45, 1.2, Gr, Hu, Sa, Th, Bu, Ja, Ar, Ru, Sp),
                    },
                },
            },
            new CampaignSector
            {
                Number = 7, Id = "port", Name = "Puerto", MapX = .34f, MapY = .82f,
                Brief = "Por el puerto llegan vehículos: Tanques, Helicópteros y Bombarderos. Usa todo tu arsenal.",
                NewEnemies = new[] { Tk, He, Bo },
                Level = new LevelConfig
                {
                    Id = "port", Name = "Puerto", Width = 7, Height = 11, Waves = 6, CoreMax = 14, CoreRow = 9, CoreColumn = 3,
                    TargetOnlyOnBoard = true, EnemySpeed = .92, StartCharge = 300, Modules = new[] { Ge, Re, Rl, Pu, Cr, Te, Wa, Rp },
                    WaveSpecs = new[]
                    {
                        Wave(10, 3.0, .95, new[] { Sc, Ru, Th }, (6, Tk)),
                        Wave(12, 2.85, 1.0, new[] { Sc, Gr, Ru, Bu }, (7, He)),
                        Wave(14, 2.7, 1.05, new[] { Ru, Th, Sc, Gr, Ja }, (5, Tk), (11, He)),
                        Wave(16, 2.55, 1.1, new[] { Bu, Ru, Th, Sa, Gr, Sc }, (8, Bo)),
                        Wave(18, 2.45, 1.16, new[] { Ru, Th, Bu, Gr, Ja, Sc, Ar }, (4, Tk), (10, He), (15, Bo)),
                        Wave(20, 2.3, 1.24, new[] { Gr, Ru, Th, Sa, Bu, Ja, Ru, Ar }, (3, He), (9, Tk), (14, Bo), (18, Tk)),
                    },
                },
            },
            new CampaignSector
            {
                Number = 8, Id = "core", Name = "Núcleo de la colonia", MapX = .6f, MapY = .91f,
                Brief = "El corazón de la colonia. Resiste todas las oleadas y al Centinela, la fortaleza que protege a su escolta.",
                NewEnemies = new[] { EnemyKind.Commander },
                Level = new LevelConfig
                {
                    Id = "core", Name = "Núcleo de la colonia", Width = 7, Height = 11, Waves = 7, CoreMax = 15, CoreRow = 9, CoreColumn = 3,
                    TargetOnlyOnBoard = true, EnemySpeed = .95, StartCharge = 320, Modules = new[] { Ge, Re, Rl, Pu, Cr, Te, Wa, Rp },
                    WaveSpecs = new[]
                    {
                        Wave(11, 3.0, .95, new[] { Sc, Ru, Gr, Th }),
                        Wave(13, 2.85, 1.0, new[] { Ru, Bu, Sc, Gr, Ja }, (8, Tk)),
                        Wave(15, 2.7, 1.06, new[] { Th, Ru, Sa, Gr, Bu, Sc }, (5, He), (11, Tk)),
                        Wave(17, 2.55, 1.12, new[] { Gr, Ru, Th, Ja, Bu, Ar, Sp }, (4, Bo), (9, Hu), (13, He)),
                        Wave(19, 2.45, 1.2, new[] { Bu, Sa, Ru, Th, Gr, Ja, Ar }, (3, Tk), (8, He), (12, Hu), (16, Bo)),
                        Wave(21, 2.35, 1.26, new[] { Ru, Th, Gr, Bu, Sa, Ja, Ar, Sp }, (3, He), (7, Tk), (12, Bo), (17, Hu), (19, Tk)),
                        Wave(22, 2.25, 1.34, new[] { Gr, Th, Bu, Ru, Sa, Ja, Ar, Sp }, (4, Tk), (9, He), (13, EnemyKind.Commander), (17, Bo)),
                    },
                },
            },
        };

        /// <summary>A wave that cycles a base pattern, with single special enemies (vehicles, the boss) at fixed places.</summary>
        static WaveSpec Wave(int count, double gap, double hp, EnemyKind[] pattern, params (int index, EnemyKind kind)[] specials)
        {
            var kinds = new EnemyKind[count];
            for (int i = 0; i < count; i++) kinds[i] = pattern[i % pattern.Length];
            foreach (var (index, kind) in specials) kinds[index] = kind;
            return new WaveSpec(count, gap, hp, kinds);
        }

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
