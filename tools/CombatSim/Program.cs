using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    public enum Preset { Fluid, Punishing }

    public sealed class Options
    {
        public string Scenario = "help";
        public int Seed = 1;
        public int Seeds = 40;
        public List<Preset> Presets = new List<Preset> { Preset.Fluid, Preset.Punishing };
        public string Record;
        public float Fps = 60f;
        public string OutFile;
        public string Bot = "anticipate";
        public string Enemies = "soldier";
        public double Seconds = 60;
        public bool Quick;
        public string Bots;            // duels only: comma list of bot styles (null = all nine)
        public string Groups;          // duels only: enemy groups separated by '/', e.g. "soldier/soldier,soldier" (null = all five)
    }

    // Headless playtest harness for the Fire combat prototype. See README.md.
    public static class Program
    {
        static readonly Dictionary<string, (string help, Action<Options> run)> Scenarios = new Dictionary<string, (string, Action<Options>)>
        {
            { "framedata", ("Frame data of every move, measured from the model (startup, active, recovery, cancel points)", FrameDataScenario.Run) },
            { "buffer", ("Input buffer: when buffered presses fire, stale presses, chain locking", BufferScenario.Run) },
            { "controls", ("Tap vs hold, dead zone, sprint attack and plunge conditions, guard/deflect windows, heal", ControlsScenario.Run) },
            { "abilities", ("Fa jin timing, plunge area, fire blast, Momentum loop, perfect dodge windows, stagger immunity", AbilitiesScenario.Run) },
            { "fairness", ("Telegraphs vs human reaction, unavoidable damage (oracle bot), attack tokens", FairnessScenario.Run) },
            { "duels", ("Bots vs 1-2 soldiers, a crossbowman, a mixed group and the full sandbox ring, many seeds, both presets", DuelsScenario.Run) },
            { "duel", ("One duel (--bot --enemies --seed --preset), optionally --record replay.json", DuelsScenario.RunOne) },
            { "fuzz", ("Long random-input runs at 30/60/144 fps with dt spikes; invariants", FuzzScenario.Run) },
            { "camera", ("Camera and lock-on: circling, overhead, elevated target, retarget, switching, walls, shoulder swap, pull-back", CameraScenario.Run) },
            { "rhythm", ("Build 05: on-beat rates, mashing, pauses and string DPS for timing bots against a passive partner", RhythmScenario.Run) },
            { "switch", ("Build 05: element switching mid-string (cooldown denials, the air cap) and MIX 4 finishers", SwitchScenario.Run) },
            { "danger", ("Build 05: danger sense cue timing and resolution in real fights, sense vs react, delayed-thrust panic dodges", DangerScenario.Run) },
            { "elements", ("Build 05: each element's string dps at 1.0x, poise budgets, beat data validation and move tables (for move-set data)", ElementsScenario.Run) },
            { "dodgeflow", ("Build 05: dodge facing, slip-in gap, spam invulnerability, exit speed, the string across a dodge / zip / ability", DodgeFlowScenario.Run) },
            { "all", ("Everything above except 'duel' (use --quick for fewer seeds)", RunAll) },
            { "anim", ("Animation: a scripted fight through the real core and procedural animator, frames as JSON to --out (render with tools/render/render_fight.py)", AnimationScenario.Run) },
        };

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Options o;
            try
            {
                o = Parse(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                PrintHelp();
                return 2;
            }
            if (!Scenarios.TryGetValue(o.Scenario, out var entry))
            {
                PrintHelp();
                return o.Scenario == "help" ? 0 : 2;
            }
            Out.Tee(o.OutFile);
            var sw = Stopwatch.StartNew();
            Out.Line("# CombatSim: " + o.Scenario);
            Out.Line();
            Out.Line("Core sources: " + CoreFingerprint() + " · seeds " + o.Seed + ".." + (o.Seed + o.Seeds - 1) + " · " + o.Fps + " fps"
                     + (SimEnemy.NotifyStrikes ? "" : " · --no-notify-strikes (the 60cb8ee Unity behaviour, NOT what Unity does now)")
                     + (TuningOverrides.Any ? " · what-if: " + TuningOverrides.Describe() : ""));
            try
            {
                entry.run(o);
            }
            catch (ArgumentException e)
            {
                // A bad --bot or --enemies value: say so plainly instead of a stack trace.
                Out.Line();
                Out.Line("Error: " + e.Message);
                return 2;
            }
            finally
            {
                Out.Line();
                if (Out.Misses > 0) Out.Line("**Targets missed: " + Out.Misses + "** (cells marked MISS).");
                Out.Line("_Finished in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s._");
                Out.Close();
            }
            return Out.Misses > 0 ? 1 : 0;
        }

        static void RunAll(Options o)
        {
            if (o.Quick) o.Seeds = Math.Min(o.Seeds, 12);
            FrameDataScenario.Run(o);
            BufferScenario.Run(o);
            ControlsScenario.Run(o);
            AbilitiesScenario.Run(o);
            FairnessScenario.Run(o);
            DuelsScenario.Run(o);
            FuzzScenario.Run(o);
            CameraScenario.Run(o);
            RhythmScenario.Run(o);
            SwitchScenario.Run(o);
            DangerScenario.Run(o);
            ElementsScenario.Run(o);
            DodgeFlowScenario.Run(o);
        }

        static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("missing value after " + a);
                switch (a)
                {
                    case "--seed": o.Seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--seeds": o.Seeds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--fps": o.Fps = float.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--record": o.Record = Next(); break;
                    case "--out": o.OutFile = Next(); break;
                    case "--bot": o.Bot = Next(); break;
                    case "--enemies": o.Enemies = Next(); break;
                    case "--seconds": o.Seconds = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--quick": o.Quick = true; break;
                    case "--groups": o.Groups = Next(); break;
                    case "--bots": o.Bots = Next(); break;
                    case "--notify-strikes": SimEnemy.NotifyStrikes = true; break;      // the default now; kept so old commands still run
                    case "--no-notify-strikes": SimEnemy.NotifyStrikes = false; break;
                    case "--set": TuningOverrides.Add(Next()); break;
                    case "--preset":
                        string p = Next().ToLowerInvariant();
                        o.Presets = p == "both" ? new List<Preset> { Preset.Fluid, Preset.Punishing }
                            : p.StartsWith("p") ? new List<Preset> { Preset.Punishing } : new List<Preset> { Preset.Fluid };
                        break;
                    case "-h":
                    case "--help": o.Scenario = "help"; break;
                    default:
                        if (a.StartsWith("--")) throw new ArgumentException("unknown option " + a);
                        o.Scenario = a.ToLowerInvariant();
                        break;
                }
            }
            return o;
        }

        static void PrintHelp()
        {
            Console.WriteLine("Usage: dotnet run --project tools/CombatSim -- <scenario> [options]");
            Console.WriteLine();
            Console.WriteLine("Scenarios:");
            foreach (var kv in Scenarios) Console.WriteLine("  " + kv.Key.PadRight(10) + " " + kv.Value.help);
            Console.WriteLine();
            Console.WriteLine("Options: --seed N  --seeds N  --preset fluid|punishing|both  --fps N  --record file.json  --out results.md");
            Console.WriteLine("         --bot masher|react|anticipate|guard|aggressive|fajin|rhythm|switcher|sense|oracle|idle  --enemies soldier,soldier,crossbow,platform,dummy  --seconds N  --quick");
            Console.WriteLine("         --no-notify-strikes  enemies don't call PlayerCombatModel.NotifyEnemyStrike (the 60cb8ee Unity behaviour; on by default, as Unity does now)");
            Console.WriteLine("         --set target.Field=value  what-if tuning (targets: player, dodge, charge, soldier, crossbow, camera, rhythm, combo, mix, switch, danger, "
                              + "loadout.<element>; nested fields with dots), repeatable");
            Console.WriteLine("         --groups soldier/soldier,soldier  --bots masher,react  duels only: just these enemy groups ('/'-separated) and bots");
        }

        // A short hash of the core sources measured, so results can be matched to a version of the code.
        static string CoreFingerprint()
        {
            try
            {
                string root = FindRepoRoot();
                if (root == null) return "(repo not found)";
                string core = Path.Combine(root, "game", "Assets", "_Project", "Scripts", "Core");
                var files = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
                using var sha = SHA1.Create();
                var all = new StringBuilder();
                foreach (var f in files) all.Append(Path.GetFileName(f)).Append(File.ReadAllText(f));
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(all.ToString()))).Replace("-", "").Substring(0, 10).ToLowerInvariant();
                string cam = Path.Combine(core, "Camera", "OrbitCameraModel.cs");
                string camHash = File.Exists(cam)
                    ? BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(cam))).Replace("-", "").Substring(0, 10).ToLowerInvariant()
                    : "?";
                return "sha1 " + hash + " (" + files.Count + " files; OrbitCameraModel.cs " + camHash + ")";
            }
            catch (Exception e)
            {
                return "(" + e.Message + ")";
            }
        }

        public static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "game", "Assets", "_Project"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "game", "Assets", "_Project"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }

    // A standard session: world, player with a preset, a camera rig, fixed dt.
    public sealed class Session
    {
        public readonly SimWorld World;
        public readonly PadInput Input = new PadInput();
        public readonly float Dt;
        public readonly Preset Preset;

        public Session(Preset preset, float fps = 60f, SimLevel level = null, bool camera = true, Vector3? playerAt = null, float playerYaw = 0f)
        {
            Preset = preset;
            Dt = 1f / fps;
            World = new SimWorld(level);
            MakePreset(preset, out PlayerTuning t, out ElementLoadout loadout);
            World.AddPlayer(t, loadout, playerAt ?? Vector3.Zero, playerYaw);
            if (camera) World.AddCameraRig();
            else World.FixedCameraYaw = 0f;
        }

        public SimPlayer Player => World.Player;
        public PlayerCombatModel Model => World.Player.Model;

        // The preset with all four elements (Build 05): --set overrides applied.
        public static void MakePreset(Preset p, out PlayerTuning tuning, out ElementLoadout loadout)
        {
            if (p == Preset.Punishing)
            {
                tuning = PlayerTuning.CreatePunishing();
                loadout = ElementLoadout.CreatePunishing();
            }
            else
            {
                tuning = PlayerTuning.CreateFluid();
                loadout = ElementLoadout.CreateFluid();
            }
            TuningOverrides.ApplyPlayer(tuning, loadout);
        }

        // Fire only (the scenarios written before Build 05 that look at one move set).
        public static void MakePreset(Preset p, out PlayerTuning tuning, out ElementMoveSet moves)
        {
            MakePreset(p, out tuning, out ElementLoadout loadout);
            moves = loadout.Fire;
        }

        public void Step(in Pad pad)
        {
            World.Step(Input.Build(pad, Dt), Dt);
        }

        public void Step(in Pad pad, float dt)
        {
            World.Step(Input.Build(pad, dt), dt);
        }

        public void Idle(int frames)
        {
            var pad = new Pad();
            for (int i = 0; i < frames; i++) Step(pad);
        }

        // World direction -> stick, relative to the current camera yaw (what a human does with the stick).
        public Vector2 StickToward(Vector3 worldDirection, float magnitude = 1f)
        {
            return StickFor(worldDirection, World.CameraYaw, magnitude);
        }

        public static Vector2 StickFor(Vector3 worldDirection, float cameraYaw, float magnitude = 1f)
        {
            Vector3 d = Directions.SafeNormalize(Directions.Flatten(worldDirection), Vector3.Zero);
            if (d == Vector3.Zero) return Vector2.Zero;
            float x = Vector3.Dot(d, Directions.RightFromYaw(cameraYaw));
            float y = Vector3.Dot(d, Directions.FromYaw(cameraYaw));
            return new Vector2(x, y) * magnitude;
        }
    }
}
