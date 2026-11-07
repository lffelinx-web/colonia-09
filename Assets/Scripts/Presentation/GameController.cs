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

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Sim = new GameSimulation();
            Board = gameObject.AddComponent<BoardView>();
            Ui = gameObject.AddComponent<GameUI>();
            Board.Init(this);
            Ui.Init(this);

            Sim.OnNotify = (text, duration) => Ui.ShowToast(text, (float)duration);
            Sim.OnShake = power => Board.Shake((float)power);
            Sim.OnBoardChanged = () => Ui.MarkDeckDirty();
            Sim.OnFinished = win => Ui.ShowModal(win ? ModalKind.Win : ModalKind.Lost);

            Sim.Reset();
            Ui.ShowModal(ModalKind.Intro);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DevAutotest.StartIfRequested(this);
#endif
        }

        void Update()
        {
            double dt = Math.Min(.06, Time.unscaledDeltaTime);
            for (int i = 0; i < StepsPerFrame; i++) Sim.Update(dt);
            if (Selected != null && Sim.ModuleAt(Selected.R, Selected.C) != Selected) Deselect();
            Board.Render(Ui.FieldPixels);
            Ui.Refresh();
        }

        void OnApplicationPause(bool paused) { if (paused) Pause(); }
        void OnApplicationFocus(bool focused) { if (!focused) Pause(); }

        // ---- Player actions -----------------------------------------------------------

        public void StartGame(Difficulty difficulty)
        {
            Sim.Difficulty = difficulty;
            SelectedType = ModuleType.Generator;
            Selected = null;
            Sim.Start();
            Ui.HideModal();
            Ui.MarkDeckDirty();
        }

        public void ReturnToMenu()
        {
            Selected = null;
            Sim.Reset();
            Ui.ShowModal(ModalKind.Intro);
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

        public void SelectType(ModuleType type)
        {
            SelectedType = type;
            Selected = null;
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
