using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Colonia09.Simulation;

// Plays the scenarios on the C# simulation with the same bot as harness_inject.js and
// compares every trace line against the reference produced from the HTML prototype.
static class Program
{
    sealed class Item { public ModuleType Type; public int R, C, Lvl; }

    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string outDir = Path.Combine(dir, "out");
        Directory.CreateDirectory(outDir);
        int failures = 0;
        foreach (var raw in File.ReadAllLines(Path.Combine(dir, "scenarios.txt")))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Trim().Split('|');
            string name = parts[0];
            var sell = parts[2].Split(' ').Select(int.Parse).ToArray();
            var items = parts[3].Split(';').Select(s =>
            {
                var t = s.Trim().Split(' ');
                return new Item { Type = (ModuleType)Enum.Parse(typeof(ModuleType), t[0], true), R = int.Parse(t[1]), C = int.Parse(t[2]), Lvl = int.Parse(t[3]) };
            }).ToList();
            var difficulty = (Difficulty)Enum.Parse(typeof(Difficulty), parts[1], true);
            int startWave = parts.Length > 4 ? int.Parse(parts[4]) : 1;
            double bonus = parts.Length > 5 ? double.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
            var lines = Play(difficulty, items, sell[0], sell[1], startWave, bonus, out var sim);
            File.WriteAllLines(Path.Combine(outDir, $"cs_{name}.txt"), lines);
            failures += Compare(name, lines, Path.Combine(outDir, $"js_{name}.txt"), sim) ? 0 : 1;
        }
        Console.WriteLine(failures == 0 ? "PARITY OK" : $"PARITY FAILED in {failures} scenario(s)");
        return failures == 0 ? 0 : 1;
    }

    static List<string> Play(Difficulty difficulty, List<Item> items, int sellR, int sellC, int startWave, double bonus, out GameSimulation sim)
    {
        var s = new GameSimulation { Difficulty = difficulty };
        sim = s;
        s.Start();
        if (startWave > 1) s.DebugStartAtWave(startWave);
        var lines = new List<string>();
        const double dt = 1.0 / 60;
        int maxTicks = 60 * 60 * 30, tick;
        for (tick = 1; tick <= maxTicks; tick++)
        {
            if (tick % 30 == 0) { if (bonus > 0) s.DebugAddCharge(bonus); BotStep(s, items); }
            if (tick % 1800 == 0) { var m = s.ModuleAt(sellR, sellC); if (m != null) s.TryRecycle(m); }
            s.Update(dt);
            if (tick % 60 == 0) lines.Add(Trace(s, tick));
            if (s.Mode != GameMode.Playing) break;
        }
        lines.Add(Trace(s, tick));
        return lines;
    }

    static void BotStep(GameSimulation s, List<Item> items)
    {
        foreach (var it in items)
        {
            var m = s.ModuleAt(it.R, it.C);
            if (m == null)
            {
                if (s.Charge >= GameSimulation.BuildCost(it.Type)) s.TryBuild(it.Type, it.R, it.C);
                return;
            }
            if (m.IsDeploying || m.Type != it.Type) continue;
            if (m.Level < it.Lvl && s.Wave >= s.UpgradeUnlock(m))
            {
                if (s.Charge >= s.UpgradeCost(m)) s.TryUpgrade(m);
                return;
            }
        }
    }

    static string Trace(GameSimulation s, int tick)
    {
        double sumHp = 0, sumY = 0, sumArmor = 0, modHp = 0;
        foreach (var e in s.Enemies) { sumHp += e.Hp; sumY += e.Y; sumArmor += e.Armor; }
        foreach (var m in s.Modules) modHp += m.Hp;
        string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
        return string.Join(" ", tick, s.Wave, s.Mode.ToString().ToLowerInvariant(), F(s.Core), F(s.Charge), F(s.EnergyCapacity), F(s.ChargingGeneration),
            s.TotalKills, s.SpawnRemaining, s.Enemies.Count, F(sumHp), F(sumY), F(sumArmor), s.Modules.Count, F(modHp), s.RandomSeed);
    }

    static readonly string[] Fields = { "tick", "wave", "mode", "core", "charge", "capacity", "generation", "kills", "spawnRemaining", "enemies", "sumHp", "sumY", "sumArmor", "modules", "modHp", "seed" };

    static bool Compare(string name, List<string> cs, string jsPath, GameSimulation sim)
    {
        var last = cs[cs.Count - 1].Split(' ');
        string summary = $"wave {last[1]}, {last[2]}, {last[7]} kills, {int.Parse(last[0]) / 60}s";
        if (!File.Exists(jsPath)) { Console.WriteLine($"{name}: no reference ({summary})"); return false; }
        var js = File.ReadAllLines(jsPath).Where(l => l.Length > 0).ToList();
        int n = Math.Min(js.Count, cs.Count);
        for (int i = 0; i < n; i++)
        {
            var a = js[i].Split(' ');
            var b = cs[i].Split(' ');
            for (int f = 0; f < a.Length; f++)
            {
                bool same = a[f] == b[f];
                if (!same && double.TryParse(a[f], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(b[f], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    same = Math.Abs(x - y) < 1e-4;
                if (!same)
                {
                    Console.WriteLine($"{name}: DIFF at line {i} (t={a[0]}) field {Fields[f]}: js={a[f]} cs={b[f]}");
                    Console.WriteLine("  js: " + js[i]);
                    Console.WriteLine("  cs: " + cs[i]);
                    if (i > 0) Console.WriteLine("  previous: " + js[i - 1]);
                    return false;
                }
            }
        }
        if (js.Count != cs.Count) { Console.WriteLine($"{name}: length differs js={js.Count} cs={cs.Count}"); return false; }
        Console.WriteLine($"{name}: identical over {n} samples ({summary})");
        return true;
    }
}
