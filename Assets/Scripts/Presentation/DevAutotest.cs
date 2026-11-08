#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Colonia09.Presentation
{
    /// <summary>
    /// Plays the real game through its real buttons and taps, saving screenshots and a report.
    /// Only in development builds, and only when launched with: -colonia-autotest "output folder"
    /// </summary>
    public sealed class DevAutotest : MonoBehaviour
    {
        GameController game;
        string dir;
        readonly List<string> report = new List<string>();
        int failures, loggedErrors;

        public static void StartIfRequested(GameController controller)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-colonia-autotest");
            if (i < 0 || i + 1 >= args.Length) return;
            var test = controller.gameObject.AddComponent<DevAutotest>();
            test.game = controller;
            test.dir = args[i + 1];
            Directory.CreateDirectory(test.dir);
            Application.logMessageReceived += test.OnLog;
            test.StartCoroutine(test.Run());
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            loggedErrors++;
            if (loggedErrors <= 10) report.Add("LOG ERROR: " + message);
        }

        void Check(string name, bool ok)
        {
            report.Add((ok ? "PASS " : "FAIL ") + name);
            if (!ok) failures++;
        }

        IEnumerator Shot(string name)
        {
            yield return null;
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        Touchscreen touchscreen;

        /// <summary>Sends a real touch through the Input System and the UI event system, as on a phone.</summary>
        IEnumerator RealTouch(Vector2 position)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = position });
            yield return Frames(3);
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = position });
            yield return Frames(3);
        }

        static Vector2 ScreenCenter(Component c)
        {
            var corners = new Vector3[4];
            ((RectTransform)c.transform).GetWorldCorners(corners);
            return (corners[0] + corners[2]) / 2;
        }

        static float FloatArg(string name, float fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture) : fallback;
        }

        IEnumerator Tap(int r, int c)
        {
            game.TapScreen(game.Board.CellToScreen(r, c));
            yield return null;
        }

        static readonly (ModuleType type, int r, int c, int lvl)[] Build =
        {
            (ModuleType.Generator, 8, 3, 1), (ModuleType.Relay, 9, 1, 1), (ModuleType.Relay, 9, 5, 1), (ModuleType.Pulse, 7, 1, 1), (ModuleType.Pulse, 7, 5, 1),
            (ModuleType.Generator, 9, 4, 1), (ModuleType.Relay, 8, 0, 1), (ModuleType.Relay, 8, 6, 1), (ModuleType.Pulse, 6, 0, 1), (ModuleType.Pulse, 6, 6, 1),
            (ModuleType.Relay, 8, 1, 1), (ModuleType.Relay, 8, 5, 1), (ModuleType.Relay, 6, 1, 1), (ModuleType.Relay, 6, 5, 1), (ModuleType.Cryo, 6, 2, 1),
            (ModuleType.Tesla, 6, 4, 1), (ModuleType.Repair, 7, 4, 1), (ModuleType.Generator, 9, 2, 2), (ModuleType.Generator, 9, 4, 2), (ModuleType.Pulse, 7, 3, 2),
            (ModuleType.Pulse, 7, 1, 2), (ModuleType.Pulse, 7, 5, 2), (ModuleType.Wall, 4, 3, 1), (ModuleType.Wall, 4, 1, 1), (ModuleType.Wall, 4, 5, 1), (ModuleType.Tesla, 5, 3, 1),
            (ModuleType.Reactor, 7, 2, 1), (ModuleType.Tesla, 6, 4, 2), (ModuleType.Cryo, 6, 2, 2),
        };

        void BotStep()
        {
            var sim = game.Sim;
            foreach (var it in Build)
            {
                var m = sim.ModuleAt(it.r, it.c);
                if (m == null)
                {
                    if (sim.Charge >= GameSimulation.BuildCost(it.type)) sim.TryBuild(it.type, it.r, it.c);
                    return;
                }
                if (m.IsDeploying || m.Type != it.type) continue;
                if (m.Level < it.lvl && sim.Wave >= sim.UpgradeUnlock(m))
                {
                    if (sim.Charge >= sim.UpgradeCost(m)) sim.TryUpgrade(m);
                    return;
                }
            }
        }

        /// <summary>A campaign-style mission: 5 x 7 board, two modules, three waves. Then back to the main menu.</summary>
        IEnumerator SmallBoard()
        {
            var sim = game.Sim;
            var ui = game.Ui;
            game.StepsPerFrame = 1;
            var level = new LevelConfig
            {
                Id = "test-5x7", Name = "Prueba", Width = 5, Height = 7, Waves = 3, CoreMax = 10, CoreRow = 5, CoreColumn = 2,
                StartCharge = 220, Modules = new[] { ModuleType.Generator, ModuleType.Pulse },
            };
            game.StartLevel(level, Difficulty.Explore);
            yield return Frames(3);
            Check("small board starts", sim.Mode == GameMode.Playing && sim.Width == 5 && sim.Height == 7 && sim.ModuleAt(5, 2)?.Type == ModuleType.Reactor);
            Check("only the level's modules are offered", ui.CardButtons[ModuleType.Pulse].gameObject.activeSelf && !ui.CardButtons[ModuleType.Tesla].gameObject.activeSelf);
            Check("modules outside the level cannot be built", !sim.TryBuild(ModuleType.Tesla, 3, 2) && sim.ModuleAt(3, 2) == null);
            yield return Tap(4, 2);
            Check("tap builds on the small board", sim.ModuleAt(4, 2)?.Type == ModuleType.Generator);
            game.SelectType(ModuleType.Pulse);
            yield return Tap(3, 1);
            yield return Tap(3, 3);
            game.StepsPerFrame = 4;
            bool inside = true;
            for (int f = 0; f < 600 && sim.Mode == GameMode.Playing; f++)
            {
                foreach (var e in sim.Enemies) inside &= e.C >= 0 && e.C < 5;
                if (f == 300) { game.StepsPerFrame = 1; yield return Shot("09_small_board"); game.StepsPerFrame = 4; }
                yield return null;
            }
            game.StepsPerFrame = 1;
            Check("enemies stay inside the small board", inside);
            report.Add($"Small board: mode {sim.Mode}, wave {sim.Wave}/{sim.MaxWave}, kills {sim.TotalKills}");
            game.ShowMainMenu();
            yield return null;
            Check("main menu restores the full board", ui.MainMenuOpen && sim.Width == 7 && sim.Height == 11);
        }

        IEnumerator Run()
        {
            var sim = game.Sim;
            var ui = game.Ui;
            ui.DebugSafeInsetTop = FloatArg("-colonia-inset-top", 0);
            ui.DebugSafeInsetBottom = FloatArg("-colonia-inset-bottom", 0);
            ui.ReapplySafeArea();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            touchscreen = InputSystem.AddDevice<Touchscreen>();
            bool quick = Array.IndexOf(Environment.GetCommandLineArgs(), "-colonia-quick") >= 0;
            yield return Frames(5);
            report.Add($"Screen {Screen.width}x{Screen.height}, safe area {Screen.safeArea}, insets {ui.DebugSafeInsetTop}/{ui.DebugSafeInsetBottom}");
            Check("sprites load", game.Board.ModuleSprite(ModuleType.Pulse) != null);
            Check("main menu opens first", ui.MainMenuOpen && !ui.ModalOpen);
            Check("campaign and multiplayer are marked unavailable", !ui.CampaignButton.interactable && !ui.MultiplayerButton.interactable);
            yield return Shot("00_menu");

            yield return RealTouch(ScreenCenter(ui.OptionsButton));
            Check("options open from the menu", ui.OptionsOpen);
            bool musicBefore = game.Audio.MusicOn;
            yield return RealTouch(ScreenCenter(ui.MusicButton));
            Check("music toggle switches", game.Audio.MusicOn != musicBefore);
            yield return Shot("00_options");
            yield return RealTouch(ScreenCenter(ui.MusicButton));
            Check("music toggle switches back", game.Audio.MusicOn == musicBefore);
            yield return RealTouch(ScreenCenter(ui.OptionsBackButton));
            Check("options close back to the menu", ui.MainMenuOpen && !ui.OptionsOpen);

            yield return RealTouch(ScreenCenter(ui.QuickPlayButton));
            Check("quick play opens its setup screen", !ui.MainMenuOpen && ui.ModalOpen && ui.CurrentModal == ModalKind.Intro);
            ui.SecondaryButton.onClick.Invoke();
            yield return null;
            Check("setup screen goes back to the menu", ui.MainMenuOpen && !ui.ModalOpen);
            ui.QuickPlayButton.onClick.Invoke();
            yield return null;
            Check("intro modal open", ui.ModalOpen && ui.CurrentModal == ModalKind.Intro);
            yield return Shot("01_intro");

            ui.PrimaryButton.onClick.Invoke();
            yield return null;
            Check("game starts from button", sim.Mode == GameMode.Playing && !ui.ModalOpen);

            ui.CardButtons[ModuleType.Generator].onClick.Invoke();
            yield return Tap(9, 2);
            var generator = sim.ModuleAt(9, 2);
            Check("tap builds generator on the tapped cell", generator != null && generator.Type == ModuleType.Generator);
            ui.CardButtons[ModuleType.Pulse].onClick.Invoke();
            Check("card selects type", game.SelectedType == ModuleType.Pulse);
            yield return Tap(7, 3);
            Check("tap builds pulse on the tapped cell", sim.ModuleAt(7, 3) != null && sim.ModuleAt(7, 3).Type == ModuleType.Pulse);
            yield return Tap(0, 2);
            Check("no building in the enemy row", sim.ModuleAt(0, 2) == null);
            yield return Tap(10, 2);
            Check("no building in the base row", sim.ModuleAt(10, 2) == null);
            yield return Shot("02_building");

            yield return Tap(9, 3);
            Check("tap selects existing module", game.Selected != null && game.Selected.Type == ModuleType.Reactor);
            yield return null;
            Check("inspector opens", ui.InspectorOpen);
            yield return Shot("03_inspector");
            ui.SellButton.onClick.Invoke();
            yield return null;
            Check("last core cannot be recycled", sim.ModuleAt(9, 3) != null);
            ui.CloseButton.onClick.Invoke();
            yield return null;
            Check("inspector closes", !ui.InspectorOpen);

            double chargeBefore = sim.Charge;
            ui.MenuButton.onClick.Invoke();
            yield return Frames(30);
            Check("pause stops the simulation", sim.Mode == GameMode.Paused && ui.ModalOpen);
            yield return Shot("04_pause");
            ui.PrimaryButton.onClick.Invoke();
            yield return null;
            Check("resume works", sim.Mode == GameMode.Playing && !ui.ModalOpen);
            report.Add($"Charge before pause {chargeBefore:0.0}, after {sim.Charge:0.0}");

            // Real touches: card first, then the board, then a module, then the close button.
            yield return RealTouch(ScreenCenter(ui.CardButtons[ModuleType.Relay]));
            Check("real touch on a card selects it", game.SelectedType == ModuleType.Relay);
            yield return RealTouch(game.Board.CellToScreen(9, 1));
            var touched = sim.ModuleAt(9, 1);
            Check("real touch on the board builds there", touched != null && touched.Type == ModuleType.Relay);
            yield return RealTouch(game.Board.CellToScreen(9, 3));
            Check("real touch on a module selects it", game.Selected != null && game.Selected.Type == ModuleType.Reactor);
            yield return RealTouch(ScreenCenter(ui.CloseButton));
            Check("real touch on close button works", game.Selected == null && !ui.InspectorOpen);

            if (quick)
            {
                yield return Shot("05_touch");
                yield return SmallBoard();
                Check("no errors logged", loggedErrors == 0);
                report.Add(failures == 0 && loggedErrors == 0 ? "AUTOTEST PASS" : $"AUTOTEST FAIL ({failures} checks, {loggedErrors} log errors)");
                File.WriteAllLines(Path.Combine(dir, "report.txt"), report);
                Application.Quit(failures == 0 && loggedErrors == 0 ? 0 : 1);
                yield break;
            }

            var wanted = new Queue<int>(new[] { 3, 5, 7 });
            int shots = 5;
            game.StepsPerFrame = 6;
            float started = Time.unscaledTime;
            int lastBotFrame = 0;
            while (sim.Mode == GameMode.Playing && Time.unscaledTime - started < 240)
            {
                if (Time.frameCount - lastBotFrame >= 5) { lastBotFrame = Time.frameCount; BotStep(); }
                if (wanted.Count > 0 && sim.Wave >= wanted.Peek() && sim.Enemies.Count >= 3)
                {
                    wanted.Dequeue();
                    game.StepsPerFrame = 1;
                    yield return Frames(2);
                    yield return Shot($"{shots++:00}_wave{sim.Wave}");
                    game.StepsPerFrame = 6;
                }
                yield return null;
            }
            game.StepsPerFrame = 1;
            report.Add($"Run ended: mode {sim.Mode}, wave {sim.Wave}, kills {sim.TotalKills}, elapsed {sim.Elapsed:0}s");
            Check("run reached at least wave 3", sim.Wave >= 3);
            Check("end modal appears", sim.Mode == GameMode.Playing || ui.ModalOpen);
            yield return Shot("08_end");
            yield return SmallBoard();
            Check("no errors logged", loggedErrors == 0);

            report.Add(failures == 0 && loggedErrors == 0 ? "AUTOTEST PASS" : $"AUTOTEST FAIL ({failures} checks, {loggedErrors} log errors)");
            File.WriteAllLines(Path.Combine(dir, "report.txt"), report);
            Application.Quit(failures == 0 && loggedErrors == 0 ? 0 : 1);
        }
    }
}
#endif
