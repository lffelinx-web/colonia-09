using System.Collections.Generic;

namespace Colonia09.Simulation
{
    public sealed class GameModule
    {
        public ModuleType Type;
        public int C, R, Level = 1;
        public double Hp;
        public bool Powered, Linked, Starter;
        public double Cool, Fire, EmpUntil, HealedTotal;
        public double DeployTotal, DeployRemaining, DeployPausedUntil;
        public double RepairBlockedUntil, LastBuildImpact;
        public string OfflineReason = "";

        public bool IsDeploying => DeployRemaining > 0;
        public double MaxHp => GameData.Modules[Type].Hp * (1 + (Level - 1) * .65);
    }

    public sealed class Enemy
    {
        public int Id, C, Attack;
        public EnemyKind Kind;
        public double Y, Hp, MaxHp, Speed, Slow, Hit, Phase;
        public double EmpClock, EmpFlash, SapClock, ShellClock;
        public bool CounterFired;
        public double CounterWind;
        public int CounterTarget = -1;
        public double BossClock, BossWind;
        public int BossTarget = -1, BossPhase;
        public double StrikeClock, StrikeWind;
        public List<int> StrikeTargets = new List<int>();
        public int StrikeCount;
        public bool StrikeDone;
        public double HuntClock, HuntWind;
        public int HuntTarget = -1, HuntShots;
        public double ArrivalShieldUntil, TacticalCoverUntil;
        public double ArmorMax, Armor;
        public bool ArmorBreakSeen;
        public double LastCounterHintAt = -10;
        /// <summary>Module this enemy is ramming right now. Read by the view only.</summary>
        public GameModule ContactTarget;
        public bool HasBombArea;
        public int BombRow, BombColumn;
    }

    /// <summary>Visual-only data: never read back by gameplay rules except wreck cleanup on build.</summary>
    public sealed class FxEvent
    {
        public string Type, Color, Text;
        public double X, Y, X2, Y2, Age, Duration, Power, Seed;
        public bool HasTarget;
    }

    public struct NetworkLink
    {
        public GameModule A, B;
    }

    public sealed class AssaultPlan
    {
        public int Lane;
        public double Until, Started;
    }
}
