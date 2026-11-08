using System;
using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;

namespace Colonia09.Presentation
{
    /// <summary>One instruction of the guided tutorial.</summary>
    public sealed class TutorialStep
    {
        public string Text;
        /// <summary>Card to point at in the dock, if any.</summary>
        public ModuleType? Card;
        /// <summary>Cell to point at on the board (row, column), if any.</summary>
        public Vector2Int? Cell;
        /// <summary>Steps without a goal wait for the player to press "Entendido".</summary>
        public Func<CampaignDirector, bool> Done;
        /// <summary>Keeps enemies from arriving until this step is completed.</summary>
        public bool Hold = true;
    }

    /// <summary>
    /// Runs a campaign mission around the normal game: the guided tutorial, the cards for new
    /// enemies, stars at the end and saving progress. Quick Play never touches it.
    /// </summary>
    public sealed class CampaignDirector
    {
        readonly GameController game;
        List<TutorialStep> steps = new List<TutorialStep>();
        int stepIndex, picksAtStepStart;
        bool confirmed;

        public CampaignSector Current { get; private set; }
        public bool Active => Current != null;
        public TutorialStep Step => Active && stepIndex < steps.Count ? steps[stepIndex] : null;
        public int LastStars { get; private set; }
        public bool LastWasBest { get; private set; }

        public CampaignDirector(GameController controller) => game = controller;

        public static bool Unlocked(CampaignSector sector)
        {
            if (!sector.Playable) return false;
            if (sector.Number == 1) return true;
            var previous = Array.Find(CampaignData.Sectors, s => s.Number == sector.Number - 1);
            return previous != null && SaveGame.Completed(previous.Id);
        }

        public void Begin(CampaignSector sector)
        {
            Current = sector;
            steps = SaveGame.Data.tutorials.Contains(sector.Id) ? new List<TutorialStep>() : Tutorials.For(sector);
            stepIndex = 0;
            StartStep();
            var progress = SaveGame.Sector(sector.Id);
            progress.attempts++;
            SaveGame.Save();
        }

        public void End()
        {
            Current = null;
            steps.Clear();
            game.Sim.HoldSpawns = false;
            game.Board.HighlightCell = null;
            game.Ui.ShowCoach(null);
        }

        void StartStep()
        {
            confirmed = false;
            picksAtStepStart = game.CardPicks;
            var step = Step;
            game.Sim.HoldSpawns = step != null && step.Hold;
            game.Board.HighlightCell = step?.Cell;
            game.Ui.ShowCoach(step);
            if (step == null && steps.Count > 0)
            {
                SaveGame.Data.tutorials.Add(Current.Id);
                SaveGame.Save();
                steps.Clear();
            }
        }

        /// <summary>The coach's "Entendido" button.</summary>
        public void Confirm() => confirmed = true;

        // Conditions used by the tutorial steps.
        public bool Picked(ModuleType type) => game.CardPicks > picksAtStepStart && game.SelectedType == type;
        public bool Built(ModuleType type, int atLeast = 1)
        {
            int n = 0;
            foreach (var m in game.Sim.Modules) if (m.Type == type) n++;
            return n >= atLeast + (type == ModuleType.Reactor ? 1 : 0);  // The starting core does not count.
        }
        public bool Confirmed => confirmed;

        /// <summary>Called every frame while a campaign mission is on screen.</summary>
        public void Update()
        {
            if (!Active || game.Sim.Mode != GameMode.Playing) return;
            var step = Step;
            if (step != null && (step.Done != null ? step.Done(this) : confirmed))
            {
                stepIndex++;
                game.Audio.Play("upgrade", true);
                StartStep();
            }
            IntroduceNewEnemies();
        }

        void IntroduceNewEnemies()
        {
            foreach (var e in game.Sim.Enemies)
            {
                if (e.Y < 0) continue;  // Wait until it is on the board, so the player can see it.
                string key = e.Kind.ToString();
                if (SaveGame.Data.seenEnemies.Contains(key)) continue;
                SaveGame.Data.seenEnemies.Add(key);
                SaveGame.Save();
                game.ShowEnemyIntro(e.Kind);
                return;
            }
        }

        /// <summary>Records the result and returns the stars earned (0 when lost).</summary>
        public int Finish(bool win)
        {
            var sector = Current;
            LastStars = win ? CampaignData.Stars(game.Sim.Core, sector.Level.CoreMax) : 0;
            var progress = SaveGame.Sector(sector.Id);
            LastWasBest = LastStars > progress.stars;
            if (win) progress.wins++;
            progress.stars = Math.Max(progress.stars, LastStars);
            SaveGame.Save();
            game.Board.HighlightCell = null;
            game.Ui.ShowCoach(null);
            return LastStars;
        }
    }

    /// <summary>The guided steps of the first sectors. Each one shows a single new idea.</summary>
    public static class Tutorials
    {
        public static List<TutorialStep> For(CampaignSector sector)
        {
            var level = sector.Level;
            int cr = level.CoreRow, cc = level.CoreColumn;
            switch (sector.Id)
            {
                case "landing":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "Este es tu <b>Núcleo</b>. Guarda la energía con la que construyes y disparas. Si cae, pierdes.", Cell = new Vector2Int(cr, cc) },
                        new TutorialStep { Text = "Toca el <b>Generador</b> en la barra de abajo.", Card = ModuleType.Generator, Done = d => d.Picked(ModuleType.Generator) },
                        new TutorialStep { Text = "Ahora toca la casilla marcada, junto al Núcleo. El Generador lo recarga.", Cell = new Vector2Int(cr, cc - 1), Done = d => d.Built(ModuleType.Generator) },
                        new TutorialStep { Text = "Elige el <b>Pulso</b>: es tu primera arma.", Card = ModuleType.Pulse, Done = d => d.Picked(ModuleType.Pulse) },
                        new TutorialStep { Text = "Colócalo en la casilla marcada. Dispara hacia arriba, a lo largo de su columna.", Cell = new Vector2Int(cr - 1, cc), Done = d => d.Built(ModuleType.Pulse) },
                        new TutorialStep { Text = "Los enemigos bajan desde la <b>zona roja</b>. Pon un Pulso en cada columna. ¡Llegan 3 oleadas!", Hold = true },
                    };
                case "camp":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "El Núcleo solo conecta las defensas que tiene <b>al lado</b>. Este tablero es más ancho." },
                        new TutorialStep { Text = "El <b>Relé</b> extiende la conexión dos casillas. Elígelo.", Card = ModuleType.Relay, Done = d => d.Picked(ModuleType.Relay) },
                        new TutorialStep { Text = "Colócalo en la casilla marcada.", Cell = new Vector2Int(cr - 1, cc - 1), Done = d => d.Built(ModuleType.Relay) },
                        new TutorialStep { Text = "Ahora un <b>Pulso</b> en la columna del borde: el Relé lo conecta.", Card = ModuleType.Pulse, Cell = new Vector2Int(cr - 1, 0), Done = d => d.Built(ModuleType.Pulse) },
                        new TutorialStep { Text = "Ojo: los <b>Generadores</b> no se conectan con Relés. Ponlos siempre junto al Núcleo." },
                    };
                case "well":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "Primero arma tu defensa: un <b>Generador</b> junto al Núcleo y al menos <b>dos Pulsos</b>.", Done = d => d.Built(ModuleType.Generator) && d.Built(ModuleType.Pulse, 2) },
                        new TutorialStep { Text = "Un segundo <b>Núcleo</b> amplía la batería y da otro punto de conexión. Elígelo.", Card = ModuleType.Reactor, Done = d => d.Picked(ModuleType.Reactor) },
                        new TutorialStep { Text = "Colócalo en la casilla marcada, en la fila de la base.", Cell = new Vector2Int(cr, cc + 1), Done = d => d.Built(ModuleType.Reactor) },
                        new TutorialStep { Text = "La <b>Barrera</b> frena a los enemigos y no gasta energía. Ponla al frente.", Card = ModuleType.Wall, Cell = new Vector2Int(2, cc), Done = d => d.Built(ModuleType.Wall) },
                        new TutorialStep { Text = "Se acerca el <b>Blindado</b>: lento, resistente y dispara. Junta varios Pulsos en su camino." },
                    };
                default:
                    return new List<TutorialStep>();
            }
        }
    }
}
