using System;
using Colonia09.Simulation;
using UnityEngine;

namespace Colonia09.Presentation
{
    /// <summary>Entry point: owns the simulation, routes player input and drives the views.</summary>
    public sealed class GameController : MonoBehaviour
    {
        public GameSimulation Sim { get; private set; }
        public ModuleType SelectedType { get; private set; } = ModuleType.Generator;
        public GameModule Selected { get; private set; }

        /// <summary>Simulation steps per frame. Stays at 1 outside automated tests.</summary>
        public int StepsPerFrame = 1;

        public BoardView Board { get; private set; }
        public GameUI Ui { get; private set; }
        public GameAudio Audio { get; private set; }
        public CampaignDirector Campaign { get; private set; }
        /// <summary>Counts taps on the module cards; the tutorial waits for the player's own choice.</summary>
        public int CardPicks { get; private set; }
        float musicClock;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Sim = new GameSimulation();
            Campaign = new CampaignDirector(this);
            Board = gameObject.AddComponent<BoardView>();
            Ui = gameObject.AddComponent<GameUI>();
            Audio = gameObject.AddComponent<GameAudio>();
            Audio.Init();
            Board.Init(this);
            Ui.Init(this);

            Sim.OnSfx = name => Audio.Play(name);
            Sim.OnNotify = (text, duration) => Ui.ShowToast(text, (float)duration);
            Sim.OnShake = power => Board.Shake((float)power);
            Sim.OnBoardChanged = () => Ui.MarkDeckDirty();
            Sim.OnFinished = win =>
            {
                if (Campaign.Active) Campaign.Finish(win);
                Ui.ShowModal(win ? ModalKind.Win : ModalKind.Lost);
            };

            ShowMainMenu();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DevAutotest.StartIfRequested(this);
#endif
        }

        void Update()
        {
            double dt = Math.Min(.06, Time.unscaledDeltaTime);
            for (int i = 0; i < StepsPerFrame; i++) Sim.Update(dt);
            Campaign.Update();
            if (Sim.Mode == GameMode.Playing || Ui.MainMenuOpen || Ui.MapOpen)
            {
                musicClock -= (float)dt;
                if (musicClock <= 0)
                {
                    // The menu plays the calm opening theme.
                    bool calm = Ui.MainMenuOpen || Ui.MapOpen;
                    bool heavy = !calm && Sim.Enemies.Exists(e => GameData.IsVehicle(e.Kind) || e.Kind == EnemyKind.Commander);
                    Audio.MusicStep(calm ? 1 : Sim.Wave, heavy);
                    musicClock = 1.48f;
                }
            }
            if (Selected != null && Sim.ModuleAt(Selected.R, Selected.C) != Selected) Deselect();
            Board.Render(Ui.FieldPixels);
            Ui.Refresh();
        }

        void OnApplicationPause(bool paused) { if (paused) Pause(); }
        void OnApplicationFocus(bool focused) { if (!focused) Pause(); }

        // ---- Player actions -----------------------------------------------------------

        /// <summary>Starts Quick Play: the prototype's full game on the 7 x 11 board.</summary>
        public void StartGame(Difficulty difficulty) => StartLevel(LevelConfig.QuickPlay, difficulty);

        /// <summary>Starts any mission: Quick Play or a campaign sector with its own board and modules.</summary>
        public void StartLevel(LevelConfig level, Difficulty difficulty)
        {
            Sim.Difficulty = difficulty;
            Sim.Configure(level);
            SelectedType = level.Allows(ModuleType.Generator) ? ModuleType.Generator : level.Modules[0];
            Selected = null;
            Sim.Start();
            Ui.HideMainMenu();
            Ui.HideModal();
            Ui.MarkDeckDirty();
        }

        /// <summary>Back to the Quick Play setup screen (difficulty and rules).</summary>
        public void ReturnToMenu()
        {
            Campaign.End();
            Selected = null;
            if (!Sim.Level.IsQuickPlay) Sim.Configure(LevelConfig.QuickPlay);
            Sim.Reset();
            Ui.ShowModal(ModalKind.Intro);
            Ui.MarkDeckDirty();
        }

        /// <summary>Leaves any game and shows the main menu.</summary>
        public void ShowMainMenu()
        {
            Campaign.End();
            Ui.HideCampaignMap();
            Selected = null;
            if (!Sim.Level.IsQuickPlay) Sim.Configure(LevelConfig.QuickPlay);
            Sim.Reset();
            Ui.HideModal();
            Ui.ShowMainMenu();
            Ui.MarkDeckDirty();
        }

        public void Pause()
        {
            if (Sim == null || Sim.Mode != GameMode.Playing) return;
            Sim.Mode = GameMode.Paused;
            Ui.ShowModal(ModalKind.Pause);
        }

        public void Resume()
        {
            if (Sim.Mode == GameMode.Paused) Sim.Mode = GameMode.Playing;
            Ui.HideModal();
        }

        // ---- Campaign -----------------------------------------------------------------

        /// <summary>Leaves any game and shows the colony map.</summary>
        public void ShowCampaignMap()
        {
            Campaign.End();
            Selected = null;
            if (!Sim.Level.IsQuickPlay) Sim.Configure(LevelConfig.QuickPlay);
            Sim.Reset();
            Ui.HideModal();
            Ui.HideMainMenu();
            Ui.ShowCampaignMap();
            Ui.MarkDeckDirty();
        }

        public void StartSector(CampaignSector sector)
        {
            if (!CampaignDirector.Unlocked(sector)) return;
            Audio.Play("wave", true);
            Ui.HideCampaignMap();
            StartLevel(ColonyData.Boosted(sector.Level, Colony.Buildings()), sector.Difficulty);  // Colony buildings help every mission.
            Campaign.Begin(sector);
        }

        /// <summary>Pauses the mission and presents an enemy the player has not met yet.</summary>
        public void ShowEnemyIntro(EnemyKind kind)
        {
            if (Sim.Mode != GameMode.Playing) return;
            Sim.Mode = GameMode.Paused;
            Audio.Play("alert", true);
            Ui.ShowEnemyIntro(kind);
        }

        public void SelectType(ModuleType type)
        {
            CardPicks++;
            SelectedType = type;
            Selected = null;
            Audio.Play("select");
            var def = GameData.Modules[type];
            Ui.ShowToast($"{def.Name}: {def.BaseInfo}", 1.6f);
            Ui.MarkDeckDirty();
        }

        public void Deselect()
        {
            Selected = null;
            Ui.MarkDeckDirty();
        }

        public void TapScreen(Vector2 screenPosition)
        {
            if (Sim.Mode != GameMode.Playing) return;
            if (!Board.ScreenToCell(screenPosition, out int r, out int c)) return;
            var existing = Sim.ModuleAt(r, c);
            if (existing != null)
            {
                Selected = existing;
                Ui.MarkDeckDirty();
                return;
            }
            var refusal = Campaign.Active ? Campaign.RefuseBuild(SelectedType, r, c) : null;
            if (refusal != null)
            {
                Audio.Play("hit", true);
                Ui.ShowToast(refusal, 1.6f);
                return;
            }
            if (Sim.TryBuild(SelectedType, r, c)) Selected = null;
            Ui.MarkDeckDirty();
        }

        public void UpgradeSelected()
        {
            Sim.TryUpgrade(Selected);
            Ui.MarkDeckDirty();
        }

        public void RecycleSelected()
        {
            if (Sim.TryRecycle(Selected)) Selected = null;
            Ui.MarkDeckDirty();
        }
    }
}
