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
        /// <summary>A one-time tip shown during play: it never blocks building and never holds enemies.</summary>
        public bool IsTip;
        /// <summary>Shows the green cells where pieces connect while this step is on screen.</summary>
        public bool ShowReach;
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
        /// <summary>The tip on screen, if any.</summary>
        public TutorialStep Tip { get; private set; }
        string tipId;
        float tipUntil;
        public int LastStars { get; private set; }
        public int LastMaterials { get; private set; }
        public int LastData { get; private set; }
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
            // The guide repeats until the sector has been won once.
            steps = SaveGame.Completed(sector.Id) ? new List<TutorialStep>() : Tutorials.For(sector);
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
            Tip = null;
            game.Sim.HoldSpawns = false;
            game.Board.HighlightCell = null;
            game.Board.ForceReach = false;
            game.Ui.ShowCoach(null);
        }

        void StartStep()
        {
            confirmed = false;
            picksAtStepStart = game.CardPicks;
            var step = Step;
            game.Sim.HoldSpawns = step != null && step.Hold;
            game.Board.HighlightCell = step?.Cell;
            game.Board.ForceReach = step != null && step.ShowReach;
            game.Ui.ShowCoach(step);
            if (step == null && steps.Count > 0)
            {
                if (!SaveGame.Data.tutorials.Contains(Current.Id)) SaveGame.Data.tutorials.Add(Current.Id);
                SaveGame.Save();
                steps.Clear();
            }
        }

        /// <summary>
        /// While a step asks for something specific, only that can be built: the marked module on the
        /// marked cell. Returns the reason to show the player when a tap is refused, or null.
        /// </summary>
        public string RefuseBuild(ModuleType type, int r, int c)
        {
            var step = Step;
            if (step == null) return null;
            if (step.Done == null) return "Lee la indicación y pulsa ENTENDIDO";
            if (step.Card.HasValue && type != step.Card.Value) return $"Ahora toca: {GameData.Modules[step.Card.Value].Name}";
            if (step.Cell.HasValue && (step.Cell.Value.x != r || step.Cell.Value.y != c)) return "Construye en la casilla marcada";
            return null;
        }

        /// <summary>
        /// The player may build anywhere during a free step, even on a cell a later step marks. A build step
        /// then moves its mark to the nearest free cell where the piece would connect, so the guide never stalls.
        /// </summary>
        void KeepCellFree(TutorialStep step)
        {
            if (!step.Cell.HasValue || step.Done == null) return;
            var sim = game.Sim;
            var cell = step.Cell.Value;
            if (sim.ModuleAt(cell.x, cell.y) == null) return;
            var type = step.Card ?? game.SelectedType;
            Vector2Int? best = null;
            int bestDistance = int.MaxValue;
            for (int r = 1; r < sim.Height - 1; r++)
                for (int c = 0; c < sim.Width; c++)
                {
                    int d = Math.Abs(r - cell.x) + Math.Abs(c - cell.y);
                    if (d < bestDistance && sim.ModuleAt(r, c) == null && sim.WouldConnect(type, r, c)) { best = new Vector2Int(r, c); bestDistance = d; }
                }
            step.Cell = best;  // With no free cell left, the step accepts any cell.
            game.Board.HighlightCell = best;
        }

        /// <summary>The coach's "Entendido" button.</summary>
        public void Confirm()
        {
            if (Tip != null && Step == null) CloseTip();
            else confirmed = true;
        }

        // Conditions used by the tutorial steps.
        public bool Picked(ModuleType type) => game.CardPicks > picksAtStepStart && game.SelectedType == type;
        public bool Built(ModuleType type, int atLeast = 1)
        {
            int n = 0;
            foreach (var m in game.Sim.Modules) if (m.Type == type) n++;
            return n >= atLeast + (type == ModuleType.Reactor ? 1 : 0);  // The starting core does not count.
        }
        public bool Confirmed => confirmed;
        /// <summary>True while a tutorial step is on screen (tips do not count).</summary>
        public bool Guiding => Step != null;

        /// <summary>Called every frame while a campaign mission is on screen.</summary>
        public void Update()
        {
            if (!Active || game.Sim.Mode != GameMode.Playing) return;
            var step = Step;
            // A guided step must never be impossible: if it asks for a module the battery cannot pay for
            // and nothing is charging it, top the battery up.
            if (step != null && step.Card.HasValue && game.Sim.ChargingGeneration < .1)
            {
                int cost = GameSimulation.BuildCost(step.Card.Value);
                if (game.Sim.Charge < cost) game.Sim.DebugAddCharge(cost - game.Sim.Charge + 1);
            }
            if (step != null) KeepCellFree(step);
            if (step != null && (step.Done != null ? step.Done(this) : confirmed))
            {
                stepIndex++;
                game.Audio.Play("upgrade", true);
                StartStep();
            }
            if (Step == null) UpdateTips();
            IntroduceNewEnemies();
        }

        // ---- One-time tips ---------------------------------------------------------------
        // Each explains something the moment it first matters, then never shows again.

        void UpdateTips()
        {
            if (Tip != null)
            {
                if (Time.unscaledTime > tipUntil) CloseTip();
                return;
            }
            var sim = game.Sim;
            foreach (var (id, text, cell) in TipCandidates(sim))
            {
                if (SaveGame.Data.tips.Contains(id)) continue;
                tipId = id;
                Tip = new TutorialStep { Text = text, Cell = cell, Hold = false, IsTip = true };
                tipUntil = Time.unscaledTime + 14;
                game.Board.HighlightCell = cell;
                game.Ui.ShowCoach(Tip);
                game.Audio.Play("select", true);
                return;
            }
        }

        void CloseTip()
        {
            if (tipId != null && !SaveGame.Data.tips.Contains(tipId)) SaveGame.Data.tips.Add(tipId);
            SaveGame.Save();
            Tip = null;
            tipId = null;
            game.Board.HighlightCell = null;
            game.Ui.ShowCoach(null);
        }

        IEnumerable<(string id, string text, Vector2Int? cell)> TipCandidates(GameSimulation sim)
        {
            if (sim.WaveBreak > 0)
                yield return ("wave", "¡Oleada superada! Recibes <b>+70 de energía</b>. Aprovecha la pausa para reforzar tu defensa.", null);
            foreach (var m in sim.Modules)
            {
                if (m.IsDeploying || m.Starter) continue;
                if (sim.ModuleStatus(m).StartsWith("SIN CONEXIÓN"))
                    yield return ("connection", "El módulo marcado está <b>SIN CONEXIÓN</b> y no funciona: necesita un Núcleo al lado o un Relé cerca.", new Vector2Int(m.R, m.C));
                if (m.Type == ModuleType.Pulse && m.Level < 3 && sim.Wave >= sim.UpgradeUnlock(m) && sim.Charge >= sim.UpgradeCost(m) + 15)
                    yield return ("upgrade", "Tienes energía para <b>MEJORAR</b>. Toca el Pulso marcado y pulsa MEJORAR: disparará más fuerte y aguantará más.", new Vector2Int(m.R, m.C));
                if (m.Hp < m.MaxHp * .5)
                    yield return ("damage", "El módulo marcado está dañado (mira su barra). Si lo destruyen, tendrás que construirlo de nuevo.", new Vector2Int(m.R, m.C));
            }
            if (sim.Elapsed > 20 && sim.Charge < 15 && sim.ChargingGeneration < 3)
                yield return ("energy", "Te quedas sin <b>energía</b>. Pon otro Generador junto al Núcleo para producir más.", null);
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
            // Stars count against the base the mission actually had, Fortification bonus included.
            LastStars = win ? CampaignData.Stars(game.Sim.Core, game.Sim.Level.CoreMax) : 0;
            var progress = SaveGame.Sector(sector.Id);
            LastWasBest = LastStars > progress.stars;
            ColonyData.MissionReward(LastStars, win && progress.wins == 0, out int materials, out int data);
            LastMaterials = materials; LastData = data;
            if (win) Colony.AddReward(materials, data);
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
        /// <summary>The opening of every later sector: the player sets up the basics alone, the guide only checks.</summary>
        static TutorialStep BaseDefense() => new TutorialStep
        {
            Text = "Primero arma tu defensa: un <b>Generador</b> junto al Núcleo y al menos <b>dos Pulsos</b>.",
            Done = d => d.Built(ModuleType.Generator) && d.Built(ModuleType.Pulse, 2),
        };

        public static List<TutorialStep> For(CampaignSector sector)
        {
            var level = sector.Level;
            int cr = level.CoreRow, cc = level.CoreColumn;
            switch (sector.Id)
            {
                case "landing":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "Este es tu <b>Núcleo</b> (casilla marcada). Guarda la energía de la barra amarilla. Si lo destruyen, pierdes.", Cell = new Vector2Int(cr, cc) },
                        new TutorialStep { Text = "Paso 1 de 4 · Toca la tarjeta <b>Generador</b> en la barra de abajo.", Card = ModuleType.Generator, Done = d => d.Picked(ModuleType.Generator) },
                        new TutorialStep { Text = "Paso 1 de 4 · Ahora toca la <b>casilla marcada</b>, junto al Núcleo.", Card = ModuleType.Generator, Cell = new Vector2Int(cr, cc - 1), Done = d => d.Built(ModuleType.Generator) },
                        new TutorialStep { Text = "El <b>Generador</b> produce energía y la entrega al Núcleo si está a 2 casillas o menos. En misiones largas podrás <b>mejorarlo</b> (desde la oleada 3) para que produzca más. <b>Uno basta por ahora</b>." },
                        new TutorialStep { Text = "Paso 2 de 4 · Toca la tarjeta <b>Pulso</b>: es tu arma.", Card = ModuleType.Pulse, Done = d => d.Picked(ModuleType.Pulse) },
                        new TutorialStep { Text = "Las <b>casillas verdes</b> son las que tienen conexión con el Núcleo: solo ahí funcionan tus piezas. Aparecen cada vez que eliges una.", ShowReach = true },
                        new TutorialStep { Text = "Paso 2 de 4 · Colócalo en la <b>casilla marcada</b>. Dispara hacia arriba, solo en su columna.", Card = ModuleType.Pulse, Cell = new Vector2Int(cr - 1, cc), Done = d => d.Built(ModuleType.Pulse) },
                        new TutorialStep { Text = "Paso 3 de 4 · Cada columna necesita su Pulso. Pon otro en la <b>columna izquierda</b>.", Card = ModuleType.Pulse, Cell = new Vector2Int(cr - 1, cc - 1), Done = d => d.Built(ModuleType.Pulse, 2) },
                        new TutorialStep { Text = "Paso 4 de 4 · Y uno más en la <b>columna derecha</b>.", Card = ModuleType.Pulse, Cell = new Vector2Int(cr - 1, cc + 1), Done = d => d.Built(ModuleType.Pulse, 3) },
                        new TutorialStep { Text = "¡Defensa lista! Los enemigos bajan desde la <b>zona roja</b>. Resiste <b>2 oleadas</b>." },
                    };
                case "camp":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "El Núcleo solo conecta las defensas que tiene <b>al lado</b>. Este tablero es más ancho." },
                        new TutorialStep { Text = "El <b>Relé</b> extiende la conexión dos casillas. Toca su tarjeta.", Card = ModuleType.Relay, Done = d => d.Picked(ModuleType.Relay) },
                        new TutorialStep { Text = "Colócalo en la <b>casilla marcada</b>.", Card = ModuleType.Relay, Cell = new Vector2Int(cr - 1, cc - 1), Done = d => d.Built(ModuleType.Relay) },
                        new TutorialStep { Text = "Ahora un <b>Pulso</b> en la columna del borde: el Relé lo conecta.", Card = ModuleType.Pulse, Cell = new Vector2Int(cr - 1, 0), Done = d => d.Built(ModuleType.Pulse) },
                        new TutorialStep { Text = "Ojo: los <b>Generadores</b> no se conectan con Relés. Ponlos siempre junto al Núcleo." },
                    };
                case "well":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "Primero arma tu defensa: un <b>Generador</b> junto al Núcleo y al menos <b>dos Pulsos</b>.", Done = d => d.Built(ModuleType.Generator) && d.Built(ModuleType.Pulse, 2) },
                        new TutorialStep { Text = "Un segundo <b>Núcleo</b> amplía la batería y da otro punto de conexión. Elígelo.", Card = ModuleType.Reactor, Done = d => d.Picked(ModuleType.Reactor) },
                        new TutorialStep { Text = "Colócalo en la <b>casilla marcada</b>, junto al primero.", Card = ModuleType.Reactor, Cell = new Vector2Int(cr, cc + 1), Done = d => d.Built(ModuleType.Reactor) },
                        new TutorialStep { Text = "La <b>Barrera</b> frena a los enemigos y no gasta energía. Ponla al frente.", Card = ModuleType.Wall, Cell = new Vector2Int(2, cc), Done = d => d.Built(ModuleType.Wall) },
                        new TutorialStep { Text = "Se acerca el <b>Blindado</b>: lento, resistente y dispara. Junta varios Pulsos en su camino." },
                    };
                case "greenhouse":
                    return new List<TutorialStep>
                    {
                        BaseDefense(),
                        new TutorialStep { Text = "Nuevo: el <b>Criogénico</b> congela a los enemigos de su columna y de las dos de al lado, y los frena. Elígelo.", Card = ModuleType.Cryo, Done = d => d.Picked(ModuleType.Cryo) },
                        new TutorialStep { Text = "Colócalo en la <b>casilla marcada</b>, delante de tus Pulsos.", Card = ModuleType.Cryo, Cell = new Vector2Int(cr - 2, cc), Done = d => d.Built(ModuleType.Cryo) },
                        new TutorialStep { Text = "La <b>Coraza</b> aguanta los disparos del Pulso, pero el frío la detiene. El <b>Divisor</b> se parte en dos Corredores al caer: deja defensas detrás." },
                    };
                case "mines":
                    return new List<TutorialStep>
                    {
                        BaseDefense(),
                        new TutorialStep { Text = "Nuevo: la <b>Tesla</b> lanza descargas que saltan entre varios enemigos cercanos, en cualquier columna. Elígela.", Card = ModuleType.Tesla, Done = d => d.Picked(ModuleType.Tesla) },
                        new TutorialStep { Text = "Colócala en la <b>casilla marcada</b>.", Card = ModuleType.Tesla, Cell = new Vector2Int(cr - 2, cc), Done = d => d.Built(ModuleType.Tesla) },
                        new TutorialStep { Text = "El <b>Térmico</b> resiste el frío: usa la Tesla. El <b>Interferente</b> apaga los módulos de su columna mientras avanza: elimínalo primero." },
                    };
                case "workshop":
                    return new List<TutorialStep>
                    {
                        BaseDefense(),
                        new TutorialStep { Text = "Nuevo: el <b>Taller</b> repara los módulos dañados a dos casillas o menos. Elígelo.", Card = ModuleType.Repair, Done = d => d.Picked(ModuleType.Repair) },
                        new TutorialStep { Text = "Colócalo en la <b>casilla marcada</b>, en medio de tu defensa.", Card = ModuleType.Repair, Cell = new Vector2Int(cr - 2, cc), Done = d => d.Built(ModuleType.Repair) },
                        new TutorialStep { Text = "El <b>Cazador</b> va por tus Talleres y Criogénicos: protégelos. El <b>Aislante</b> resiste la Tesla y el <b>Saboteador</b> golpea tus módulos de cerca." },
                    };
                case "port":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "Por el puerto llegan <b>vehículos</b>: Tanques, Helicópteros y Bombarderos. Tienen blindaje y atacan varias casillas a la vez." },
                        new TutorialStep { Text = "Prepara una defensa amplia: al menos <b>cuatro Pulsos</b> y una <b>Tesla</b>.", Done = d => d.Built(ModuleType.Pulse, 4) && d.Built(ModuleType.Tesla) },
                        new TutorialStep { Text = "Cuando un vehículo marca casillas, ahí caerá su ataque. El <b>Taller</b> repara lo dañado y el <b>Criogénico</b> frena al Tanque." },
                    };
                case "core":
                    return new List<TutorialStep>
                    {
                        new TutorialStep { Text = "La batalla final. En la última oleada llega el <b>Centinela</b>: una fortaleza con escudo que protege a su escolta." },
                        new TutorialStep { Text = "Usa todo lo aprendido: al menos <b>dos Generadores</b> y <b>cuatro Pulsos</b> antes de empezar.", Done = d => d.Built(ModuleType.Generator, 2) && d.Built(ModuleType.Pulse, 4) },
                    };
                default:
                    return new List<TutorialStep>();
            }
        }
    }
}
