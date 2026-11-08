using System.Collections.Generic;

namespace Colonia09.Simulation
{
    public enum ModuleType { Generator, Reactor, Relay, Pulse, Cryo, Tesla, Wall, Repair }

    public enum EnemyKind
    {
        Scout, Runner, Armor, Jammer, Splitter, Sapper, Bulwark, Thermal, Grounder,
        Hunter, Commander, Tank, Helicopter, Bomber
    }

    public enum Difficulty { Explore, Tactical, Extreme }

    public enum GameMode { Idle, Playing, Paused, Won, Lost }

    public sealed class ModuleDef
    {
        public string Name, Color, Desc, BaseInfo, UpgradeInfo;
        public int Cost, Hp, Power;
        public double DeploySeconds;
    }

    public sealed class DifficultyDef
    {
        public string Label;
        public double Hp, Speed, Pace, Size;
        public int Start;
    }

    public sealed class WavePlan
    {
        public int Count;
        public double Gap, Hp;
    }

    public sealed class EnemyBase
    {
        public double Hp, Speed;
        public int Attack;
    }

    public sealed class CounterInfo
    {
        public ModuleType Target;
        public string Color, Label, Tip;
    }

    /// <summary>
    /// Board and rules for one mission. The defaults are exactly the prototype's game, which
    /// Quick Play uses unchanged; campaign sectors use smaller boards and fewer modules.
    /// </summary>
    public sealed class LevelConfig
    {
        public string Id = "quick", Name = "Partida rápida";
        public int Width = GameData.Width, Height = GameData.Height, Waves = GameData.MaxWave, CoreMax = GameData.CoreMax;
        public int CoreRow = GameData.StartCoreRow, CoreColumn = GameData.StartCoreColumn;
        /// <summary>Starting energy; null keeps the difficulty's value.</summary>
        public int? StartCharge;
        public ModuleType[] Modules = GameData.ModuleTypes;
        /// <summary>Campaign waves. Null keeps the prototype's scripted waves (Quick Play).</summary>
        public WaveSpec[] WaveSpecs;
        /// <summary>Defenses ignore enemies still above the board. Off in Quick Play, as in the prototype.</summary>
        public bool TargetOnlyOnBoard;
        /// <summary>Enemy speed multiplier; campaign tutorial sectors slow enemies down.</summary>
        public double EnemySpeed = 1;

        public bool Allows(ModuleType type) => System.Array.IndexOf(Modules, type) >= 0;
        public bool IsQuickPlay => Id == "quick";
        public static LevelConfig QuickPlay => new LevelConfig();
        public LevelConfig Clone() => (LevelConfig)MemberwiseClone();  // Arrays are shared; they are never modified.

        /// <summary>Throws if the board cannot be played: too small, or the core outside the buildable rows.</summary>
        public void Validate()
        {
            if (Width < 3 || Width > GameData.Width || Height < 5 || Height > GameData.Height)
                throw new System.ArgumentException($"Board {Width}x{Height} is outside 3x5 to {GameData.Width}x{GameData.Height}");
            if (CoreRow < 1 || CoreRow > Height - 2 || CoreColumn < 0 || CoreColumn >= Width)
                throw new System.ArgumentException($"Core at row {CoreRow}, column {CoreColumn} is outside the buildable area");
            if (Waves < 1 || Waves > GameData.MaxWave || CoreMax < 1 || Modules.Length == 0)
                throw new System.ArgumentException("Level needs at least one wave, one core point and one module");
            if (WaveSpecs != null && WaveSpecs.Length != Waves)
                throw new System.ArgumentException($"Level has {Waves} waves but {WaveSpecs.Length} wave specs");
        }
    }

    /// <summary>One campaign wave: how many enemies, how fast they come, their strength, and the order of kinds.</summary>
    public sealed class WaveSpec
    {
        public int Count;
        public double Gap, Hp;
        /// <summary>Kinds cycle in this order: { Scout, Scout, Runner } gives every third enemy as a Runner.</summary>
        public EnemyKind[] Pattern;

        public WaveSpec(int count, double gap, double hp, params EnemyKind[] pattern)
        {
            Count = count; Gap = gap; Hp = hp; Pattern = pattern;
        }
    }

    /// <summary>All balance data, ported as-is from the Colonia 09 v0.9.1 prototype.</summary>
    public static class GameData
    {
        public const int Width = 7;
        public const int Height = 11;
        public const int MaxWave = 9;
        public const int CoreMax = 15;
        public const int StartCoreRow = 9;
        public const int StartCoreColumn = 3;

        public static readonly ModuleType[] ModuleTypes =
        {
            ModuleType.Generator, ModuleType.Reactor, ModuleType.Relay, ModuleType.Pulse,
            ModuleType.Cryo, ModuleType.Tesla, ModuleType.Wall, ModuleType.Repair
        };

        public static readonly Dictionary<ModuleType, ModuleDef> Modules = new Dictionary<ModuleType, ModuleDef>
        {
            { ModuleType.Generator, new ModuleDef { Name = "Generador", Cost = 35, Hp = 130, Color = "#ffc981", Power = 0, DeploySeconds = 3.2,
                Desc = "Produce energía. Necesita un Núcleo a dos casillas o menos para entregarla; los Relés no sirven de entrada.",
                BaseInfo = "Produce +2.7/s · N2 desde ola 3, N3 desde ola 6", UpgradeInfo = "Mayor producción eléctrica (progresión moderada)" } },
            { ModuleType.Reactor, new ModuleDef { Name = "Núcleo", Cost = 55, Hp = 155, Color = "#9decc7", Power = 0, DeploySeconds = 3.8,
                Desc = "Recibe y guarda la energía generada. De esta misma batería se paga todo: construir, mejorar y disparar.",
                BaseInfo = "Aumenta la capacidad de energía", UpgradeInfo = "Mayor capacidad de reserva" } },
            { ModuleType.Relay, new ModuleDef { Name = "Relé", Cost = 15, Hp = 100, Color = "#8ae6ec", Power = 0, DeploySeconds = 1.4,
                Desc = "Extiende la conexión del Núcleo a las defensas. Nunca conecta Generadores.",
                BaseInfo = "Extiende la conexión 2 casillas", UpgradeInfo = "Más resistencia al daño" } },
            { ModuleType.Pulse, new ModuleDef { Name = "Pulso", Cost = 38, Hp = 115, Color = "#f7d38a", Power = 1, DeploySeconds = 1.8,
                Desc = "Dispara al enemigo más cercano de su columna. Utiliza energía de la batería central.",
                BaseInfo = "Gasta energía al disparar", UpgradeInfo = "Disparos más potentes" } },
            { ModuleType.Cryo, new ModuleDef { Name = "Criogénico", Cost = 45, Hp = 115, Color = "#81dfff", Power = 2, DeploySeconds = 2.2,
                Desc = "Ralentiza enemigos cercanos. Solo consume electricidad cuando congela.",
                BaseInfo = "Gasta energía al congelar", UpgradeInfo = "Mayor ralentización" } },
            { ModuleType.Tesla, new ModuleDef { Name = "Tesla", Cost = 62, Hp = 110, Color = "#bba1ff", Power = 3, DeploySeconds = 2.6,
                Desc = "Descargas eléctricas que saltan entre enemigos. Solo consume energía cuando dispara.",
                BaseInfo = "Gasta energía al disparar", UpgradeInfo = "Más saltos eléctricos" } },
            { ModuleType.Wall, new ModuleDef { Name = "Barrera", Cost = 25, Hp = 350, Color = "#ffa78b", Power = 0, DeploySeconds = 1.6,
                Desc = "Detiene y absorbe impactos. No necesita electricidad y protege módulos detrás.",
                BaseInfo = "Bloqueo sin consumo", UpgradeInfo = "Blindaje reforzado" } },
            { ModuleType.Repair, new ModuleDef { Name = "Taller", Cost = 42, Hp = 138, Color = "#9de6a4", Power = 2, DeploySeconds = 2.9,
                Desc = "Repara estructuras dañadas cercanas. Solo consume electricidad cuando hay algo que reparar.",
                BaseInfo = "Gasta energía al reparar", UpgradeInfo = "Reparación más potente" } },
        };

        public static readonly Dictionary<Difficulty, DifficultyDef> Difficulties = new Dictionary<Difficulty, DifficultyDef>
        {
            { Difficulty.Explore, new DifficultyDef { Label = "Exploración", Hp = .82, Speed = .91, Pace = 1.13, Size = .90, Start = 185 } },
            { Difficulty.Tactical, new DifficultyDef { Label = "Táctico", Hp = 1, Speed = 1, Pace = 1, Size = 1, Start = 170 } },
            { Difficulty.Extreme, new DifficultyDef { Label = "Extremo", Hp = 1.14, Speed = 1.07, Pace = .94, Size = 1.10, Start = 160 } },
        };

        /// <summary>Indexed by wave number; index 0 is unused.</summary>
        public static readonly WavePlan[] WavePlans =
        {
            null,
            new WavePlan { Count = 7, Gap = 3.22, Hp = .93 }, new WavePlan { Count = 9, Gap = 3.10, Hp = 1.00 }, new WavePlan { Count = 11, Gap = 2.93, Hp = 1.08 },
            new WavePlan { Count = 13, Gap = 2.75, Hp = 1.16 }, new WavePlan { Count = 16, Gap = 2.58, Hp = 1.26 }, new WavePlan { Count = 19, Gap = 2.43, Hp = 1.41 },
            new WavePlan { Count = 21, Gap = 2.30, Hp = 1.66 }, new WavePlan { Count = 24, Gap = 2.08, Hp = 1.94 }, new WavePlan { Count = 27, Gap = 2.01, Hp = 2.16 },
        };

        public static readonly Dictionary<EnemyKind, EnemyBase> Enemies = new Dictionary<EnemyKind, EnemyBase>
        {
            { EnemyKind.Scout, new EnemyBase { Hp = 68, Speed = .53, Attack = 17 } },
            { EnemyKind.Runner, new EnemyBase { Hp = 46, Speed = .98, Attack = 20 } },
            { EnemyKind.Armor, new EnemyBase { Hp = 212, Speed = .35, Attack = 24 } },
            { EnemyKind.Jammer, new EnemyBase { Hp = 96, Speed = .50, Attack = 15 } },
            { EnemyKind.Splitter, new EnemyBase { Hp = 82, Speed = .62, Attack = 16 } },
            { EnemyKind.Sapper, new EnemyBase { Hp = 188, Speed = .57, Attack = 21 } },
            { EnemyKind.Bulwark, new EnemyBase { Hp = 125, Speed = .49, Attack = 23 } },
            { EnemyKind.Thermal, new EnemyBase { Hp = 112, Speed = .57, Attack = 21 } },
            { EnemyKind.Grounder, new EnemyBase { Hp = 114, Speed = .53, Attack = 22 } },
            { EnemyKind.Hunter, new EnemyBase { Hp = 265, Speed = .60, Attack = 20 } },
            { EnemyKind.Commander, new EnemyBase { Hp = 1100, Speed = .31, Attack = 24 } },
            { EnemyKind.Tank, new EnemyBase { Hp = 920, Speed = .31, Attack = 27 } },
            { EnemyKind.Helicopter, new EnemyBase { Hp = 680, Speed = .59, Attack = 16 } },
            { EnemyKind.Bomber, new EnemyBase { Hp = 850, Speed = .51, Attack = 19 } },
        };

        public static readonly Dictionary<EnemyKind, CounterInfo> Counters = new Dictionary<EnemyKind, CounterInfo>
        {
            { EnemyKind.Bulwark, new CounterInfo { Target = ModuleType.Pulse, Color = "#ffcd8e", Label = "CORAZA", Tip = "resiste Pulso; prueba Criogénico" } },
            { EnemyKind.Thermal, new CounterInfo { Target = ModuleType.Cryo, Color = "#ff9a88", Label = "TÉRMICO", Tip = "resiste Criogénico; prueba Tesla" } },
            { EnemyKind.Grounder, new CounterInfo { Target = ModuleType.Tesla, Color = "#a6f2cd", Label = "AISLANTE", Tip = "resiste Tesla; prueba Pulso" } },
        };

        public static readonly Dictionary<int, int[]> HunterIndices = new Dictionary<int, int[]>
        {
            { 3, new[] { 6 } }, { 4, new[] { 5 } }, { 5, new[] { 6, 14 } }, { 6, new[] { 5, 16 } },
            { 7, new[] { 7, 19 } }, { 8, new[] { 6, 19 } }, { 9, new[] { 7, 19, 25 } },
        };

        public static bool IsVehicle(EnemyKind k) => k == EnemyKind.Tank || k == EnemyKind.Helicopter || k == EnemyKind.Bomber;
        public static bool IsAir(EnemyKind k) => k == EnemyKind.Helicopter || k == EnemyKind.Bomber;
    }
}
