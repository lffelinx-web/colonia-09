using System.Collections.Generic;

namespace Colonia09.Simulation
{
    /// <summary>One build or upgrade a test bot wants: module, cell and the level to reach.</summary>
    public struct BotItem
    {
        public ModuleType Type;
        public int R, C, Level;
    }

    /// <summary>
    /// Build plans the balance checks play every campaign sector with: a sensible one that must win, a lazy one
    /// that should not, and what a player builds while following the guide. Shared by Tools/Parity (headless)
    /// and DevAutotest (in Unity), so both play exactly the same game.
    /// </summary>
    public static class CampaignBots
    {
        // The full arsenal on the 7 x 11 decks: sector 6's plan one row lower, upgraded further.
        const string Port = "generator 9 2 1; pulse 8 3 1; relay 8 2 1; pulse 8 1 1; repair 7 3 1; generator 9 4 1; tesla 7 2 1; relay 8 4 1; pulse 8 5 1; relay 8 0 1; pulse 7 0 1; relay 8 6 1; pulse 7 6 1; pulse 6 2 1; pulse 7 4 1; cryo 6 3 1; reactor 9 5 1; generator 9 6 1; wall 4 2 1; wall 4 4 1; "
            + "pulse 8 3 2; pulse 8 1 2; pulse 8 5 2; pulse 7 0 2; pulse 7 6 2; pulse 6 2 2; pulse 7 4 2; generator 9 2 2; generator 9 4 2; tesla 7 2 2; generator 9 6 2; cryo 6 3 2; repair 7 3 2; tesla 7 5 1; pulse 6 4 1; pulse 6 4 2";

        public static readonly Dictionary<string, (string good, string lazy)> Plans = new Dictionary<string, (string, string)>
        {
            { "landing", ("generator 5 0 1; pulse 4 1 1; pulse 4 0 1; pulse 4 2 1; generator 5 2 1; pulse 4 1 2; pulse 4 0 2; pulse 4 2 2",
                          "generator 5 0 1; pulse 4 1 1") },
            { "camp", ("generator 6 1 1; pulse 5 2 1; generator 6 3 1; relay 5 1 1; pulse 5 0 1; relay 5 3 1; pulse 5 4 1; pulse 4 1 1; pulse 4 3 1; pulse 5 2 2; pulse 5 0 2; pulse 5 4 2; pulse 4 1 2; pulse 4 3 2",
                       "generator 6 1 1; pulse 5 2 1; pulse 5 0 1") },
            { "well", ("generator 7 1 1; pulse 6 2 1; generator 7 4 1; relay 6 1 1; pulse 6 0 1; relay 6 3 1; pulse 6 4 1; reactor 5 2 1; pulse 5 1 1; pulse 5 3 1; wall 3 2 1; wall 3 0 1; wall 3 4 1; pulse 6 2 2; pulse 6 0 2; pulse 6 4 2; pulse 5 1 2; pulse 5 3 2; generator 7 1 2; generator 7 4 2",
                       "generator 7 1 1; pulse 6 2 1; pulse 6 0 1; pulse 6 4 1") },
            { "greenhouse", ("generator 7 1 1; pulse 6 2 1; relay 6 1 1; pulse 6 0 1; cryo 5 2 1; generator 7 3 1; relay 6 3 1; pulse 6 4 1; relay 6 5 1; pulse 5 5 1; pulse 5 1 1; pulse 5 3 1; cryo 5 4 1; reactor 7 4 1; generator 7 5 1; wall 3 1 1; wall 3 3 1; "
                             + "pulse 6 2 2; pulse 6 0 2; pulse 6 4 2; pulse 5 5 2; pulse 5 1 2; pulse 5 3 2; generator 7 1 2; generator 7 3 2; cryo 5 2 2; cryo 5 4 2",
                             "generator 7 1 1; pulse 6 2 1; pulse 6 1 1; pulse 6 3 1") },
            { "mines", ("generator 8 1 1; pulse 7 2 1; relay 7 1 1; pulse 7 0 1; tesla 6 2 1; generator 8 3 1; relay 7 3 1; pulse 7 4 1; relay 7 5 1; pulse 6 5 1; pulse 6 1 1; pulse 6 3 1; cryo 6 4 1; reactor 8 4 1; generator 8 5 1; tesla 6 0 1; wall 4 1 1; wall 4 3 1; "
                        + "pulse 7 2 2; pulse 7 0 2; pulse 7 4 2; pulse 6 5 2; pulse 6 1 2; pulse 6 3 2; generator 8 1 2; generator 8 3 2; tesla 6 2 2; cryo 6 4 2; generator 8 5 2; tesla 6 0 2",
                        "generator 8 1 1; pulse 7 2 1; pulse 7 1 1; pulse 7 3 1") },
            { "workshop", ("generator 8 2 1; pulse 7 3 1; relay 7 2 1; pulse 7 1 1; repair 6 3 1; generator 8 4 1; tesla 6 2 1; relay 7 4 1; pulse 7 5 1; relay 7 0 1; pulse 6 0 1; relay 7 6 1; pulse 6 6 1; pulse 5 2 1; pulse 6 4 1; cryo 5 3 1; reactor 8 5 1; generator 8 6 1; wall 3 2 1; wall 3 4 1; "
                           + "pulse 7 3 2; pulse 7 1 2; pulse 7 5 2; pulse 6 0 2; pulse 6 6 2; pulse 5 2 2; pulse 6 4 2; generator 8 2 2; generator 8 4 2; tesla 6 2 2; generator 8 6 2; cryo 5 3 2; repair 6 3 2",
                           "generator 8 2 1; pulse 7 3 1; pulse 7 2 1; pulse 7 4 1") },
            { "port", (Port, "generator 9 2 1; pulse 8 3 1; pulse 8 2 1; pulse 8 4 1") },
            { "core", (Port + "; reactor 9 1 1; generator 9 0 1; tesla 7 1 1; pulse 6 0 1; pulse 6 6 1; wall 4 3 1; generator 9 0 2; tesla 7 1 2; tesla 7 5 2; pulse 6 0 2; pulse 6 6 2; pulse 8 3 3; pulse 8 1 3; pulse 8 5 3; pulse 7 0 3; pulse 7 6 3; tesla 7 2 3; generator 9 2 3; generator 9 4 3; generator 9 6 3",
                       "generator 9 2 1; pulse 8 3 1; pulse 8 2 1; pulse 8 4 1; generator 9 4 1; relay 8 2 1") },
        };

        /// <summary>What a new player builds while following each sector's guide.</summary>
        public static readonly Dictionary<string, string> Tutorial = new Dictionary<string, string>
        {
            { "landing", "generator 5 0 1; pulse 4 1 1; pulse 4 0 1; pulse 4 2 1" },
            { "camp", "relay 5 1 1; pulse 5 0 1" },
            { "well", "generator 7 1 1; pulse 6 2 1; pulse 6 0 1; reactor 7 3 1; wall 2 2 1" },
            { "greenhouse", "generator 7 1 1; pulse 6 2 1; relay 6 1 1; pulse 6 0 1; cryo 5 2 1" },
            { "mines", "generator 8 1 1; pulse 7 2 1; relay 7 1 1; pulse 7 0 1; tesla 6 2 1" },
            { "workshop", "generator 8 2 1; pulse 7 3 1; relay 7 2 1; pulse 7 1 1; repair 6 3 1" },
            { "port", "generator 9 2 1; pulse 8 3 1; relay 8 2 1; pulse 8 1 1; tesla 7 2 1; relay 8 4 1; pulse 8 5 1; pulse 7 4 1" },
            { "core", "generator 9 2 1; pulse 8 3 1; relay 8 2 1; pulse 8 1 1; generator 9 4 1" },
        };

        public static List<BotItem> Parse(string plan)
        {
            var items = new List<BotItem>();
            foreach (var part in plan.Split(';'))
            {
                var t = part.Trim().Split(' ');
                items.Add(new BotItem { Type = (ModuleType)System.Enum.Parse(typeof(ModuleType), t[0], true), R = int.Parse(t[1]), C = int.Parse(t[2]), Level = int.Parse(t[3]) });
            }
            return items;
        }

        /// <summary>One bot decision: build the first missing item if it can pay, or upgrade the first one below its level.</summary>
        public static void Step(GameSimulation sim, List<BotItem> plan)
        {
            foreach (var it in plan)
            {
                var m = sim.ModuleAt(it.R, it.C);
                if (m == null)
                {
                    if (sim.Charge >= GameSimulation.BuildCost(it.Type)) sim.TryBuild(it.Type, it.R, it.C);
                    return;
                }
                if (m.IsDeploying || m.Type != it.Type) continue;
                if (m.Level < it.Level && sim.Wave >= sim.UpgradeUnlock(m))
                {
                    if (sim.Charge >= sim.UpgradeCost(m)) sim.TryUpgrade(m);
                    return;
                }
            }
        }
    }
}
