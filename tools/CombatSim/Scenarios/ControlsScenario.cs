using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Controls: the dodge/sprint button with real press lengths, the stick dead zone, the camera turning
    // mid-dodge, sprint and jump attack conditions, guard/deflect timing against the swinging dummy, healing.
    public static class ControlsScenario
    {
        public static void Run(Options o)
        {
            Out.Heading("Controls");
            TapHold(o);
            DeadZone(o);
            CameraMidDodge(o);
            SprintAttack(o);
            Plunge(o);
            GuardDeflect(o);
            Heal(o);
        }

        static void TapHold(Options o)
        {
            Out.Sub("Dodge/Sprint button: press length vs result (stick held forward)");
            Out.Line("Each preset is shown with its own trigger mode, plus the other mode on the same numbers so the trigger's effect is visible.");
            var t = new Table("Preset / trigger", "Press 0.05 s", "0.10 s", "0.15 s", "0.20 s", "0.22 s", "0.25 s", "0.30 s", "0.40 s");
            foreach (Preset p in o.Presets)
            {
                foreach (DodgeTrigger trig in new[] { DodgeTrigger.OnPress, DodgeTrigger.OnRelease })
                {
                    var cells = new List<object> { p + " / " + trig + (IsDefault(p, trig) ? " (default)" : "") };
                    foreach (float len in new[] { 0.05f, 0.10f, 0.15f, 0.20f, 0.22f, 0.25f, 0.30f, 0.40f })
                    {
                        int frames = Math.Max(1, (int)Math.Round(len * o.Fps));
                        Trace tr = Trace.Run(p, f => new Pad { Dodge = f < frames, Move = new Vector2(0f, 1f) }, 90,
                            s => s.Model.Tuning.DodgeTrigger = trig, o.Fps);
                        int dodge = tr.First(PlayerEventType.DodgeStarted);
                        int sprint = tr.First(PlayerEventType.SprintStarted);
                        string cell = dodge >= 0 ? "dodge @" + dodge + "f" : "no dodge";
                        if (sprint >= 0) cell += ", sprint @" + sprint + "f";
                        cells.Add(cell);
                    }
                    t.Row(cells.ToArray());
                }
            }
            t.Print();
            Out.Line("Reading: \"dodge @12f\" = the dash started 12 frames after the button went down (on-release dodges wait for the release).");
        }

        static bool IsDefault(Preset p, DodgeTrigger t)
        {
            return (p == Preset.Fluid && t == DodgeTrigger.OnPress) || (p == Preset.Punishing && t == DodgeTrigger.OnRelease);
        }

        static void DeadZone(Options o)
        {
            Out.Sub("Dodge direction with a small stick tilt (camera yaw 0, stick at 45° right-forward)");
            Out.Line("The core's StickDeadzone is 0.1 of the value it receives. Unity's Input System applies its own stick dead zone first (default 0.125–0.925, rescaled), "
                     + "so in the editor the core's 0.1 corresponds to about 0.2 of physical tilt, and the walk/run split (0.5) to about 0.53.");
            var t = new Table("Stick magnitude", "Result", "Dash direction (world yaw)", "Distance");
            foreach (float mag in new[] { 0.05f, 0.09f, 0.10f, 0.11f, 0.15f, 0.3f, 1.0f })
            {
                Vector2 stick = new Vector2(0.7071f, 0.7071f) * mag;
                Trace tr = Trace.Run(Preset.Fluid, f => new Pad { Dodge = f < 2, Move = f < 40 ? stick : Vector2.Zero }, 60, null, o.Fps);
                PlayerEvent e = tr.FirstEvent(PlayerEventType.DodgeStarted);
                int end = tr.First(PlayerEventType.DodgeEnded);
                float dist = Vector3.Distance(Vector3.Zero, Directions.Flatten(tr.States[end].Position));
                t.Row(Out.N(mag, 2), e.IsBackstep ? "backstep" : "dash", Out.N(Directions.YawOf(e.Direction), 1) + "°", Out.N(dist, 2) + " m");
            }
            t.Print();
        }

        static void CameraMidDodge(Options o)
        {
            Out.Sub("Camera yaw turning during a dodge (stick right, camera spun 90° over the dash)");
            var s = new Session(Preset.Fluid, o.Fps, camera: false);
            var positions = new List<Vector3>();
            for (int f = 0; f < 30; f++)
            {
                s.World.FixedCameraYaw = f < 1 ? 0f : Math.Min(90f, f * 6f);
                s.Step(new Pad { Dodge = f < 2, Move = new Vector2(1f, 0f) });
                positions.Add(s.Player.Feet);
            }
            Vector3 end = positions[20];
            Out.Line("Dash ended at (" + Out.N(end.X) + ", " + Out.N(end.Z) + "): the dash keeps the direction chosen when it started (world +X), "
                     + "whatever the camera does afterwards. After the dash, the held stick means 'right of the new camera yaw' (it turned the player "
                     + (Math.Abs(positions[29].Z - positions[20].Z) > 0.2f ? "towards -Z" : "not at all") + ").");
        }

        static void SprintAttack(Options o)
        {
            Out.Sub("Sprint attack: how long must Dodge be held, and what if it's released just before Light?");
            var t = new Table("Preset", "Min hold before Light for a Flying Fire Kick", "Released 1 f before Light", "Released 5 f before Light", "Light 1 f after release, still moving at sprint speed?");
            foreach (Preset p in o.Presets)
            {
                int min = -1;
                for (int hold = 10; hold < 80; hold++)
                {
                    int h = hold;
                    Trace tr = Trace.Run(p, f => new Pad { Dodge = f < h + 2, Move = new Vector2(0f, 1f), Light = f == h }, h + 40, null, o.Fps);
                    PlayerEvent a = tr.FirstEvent(PlayerEventType.AttackStarted);
                    if (a.AttackKind == PlayerAttackKind.Sprint) { min = hold; break; }
                }
                string Released(int gap)
                {
                    int hold = 60;
                    Trace tr = Trace.Run(p, f => new Pad { Dodge = f < hold - gap, Move = new Vector2(0f, 1f), Light = f == hold }, hold + 40, null, o.Fps);
                    PlayerEvent a = tr.FirstEvent(PlayerEventType.AttackStarted);
                    return a.Move != null ? a.Move.DisplayName : "none";
                }
                float speedAfter;
                {
                    int hold = 60;
                    Trace tr = Trace.Run(p, f => new Pad { Dodge = f < hold - 1, Move = new Vector2(0f, 1f) }, hold + 2, null, o.Fps);
                    Vector3 v = tr.States[hold].Velocity;
                    speedAfter = new Vector2(v.X, v.Z).Length();
                }
                t.Row(p, min >= 0 ? min + " f (" + Out.N(min / o.Fps, 2) + " s)" : "never", Released(1), Released(5), Out.N(speedAfter, 2) + " m/s");
            }
            t.Print();
        }

        static void Plunge(Options o)
        {
            Out.Sub("Jump attack (Falling Axe Kick): Light pressed k frames after the jump");
            var t = new Table("Light at", "Plunge?", "Landing ring at", "Height when it started", "Free again at");
            foreach (int k in new[] { 1, 2, 4, 8, 14, 15, 16, 24, 30, 34, 36 })
            {
                float startHeight = 0f;
                Session sess = null;
                Trace tr = Trace.Run(Preset.Fluid, f => new Pad { Jump = f == 0, Light = f == k }, 120, s => sess = s, o.Fps,
                    (s, f) => { if (f == k) startHeight = s.Player.Feet.Y; });
                int impact = tr.First(PlayerEventType.PlungeImpact);
                int end = tr.First(PlayerEventType.AttackEnded);
                PlayerEvent a = tr.FirstEvent(PlayerEventType.AttackStarted);
                t.Row(k + " f", a.AttackKind == PlayerAttackKind.Plunge ? "yes" : (a.Move != null ? a.Move.DisplayName : "no"),
                    impact >= 0 ? impact + " f" : "-", Out.N(startHeight, 2) + " m", end >= 0 ? end + " f" : "-");
            }
            t.Print();

            // Jump + plunge spam against a dummy crowd: how much does the 360° landing ring deal per second?
            Out.Line("Landing ring reach (enemy capsules at distance d from the player's feet, centre to centre):");
            var r = new Table("d (m)", "1.5", "2.0", "2.4", "2.6", "2.8", "3.0");
            var cells = new List<object> { "hit?" };
            foreach (float d in new[] { 1.5f, 2.0f, 2.4f, 2.6f, 2.8f, 3.0f })
            {
                var s = new Session(Preset.Fluid, o.Fps, camera: false);
                SimEnemy dummy = s.World.AddEnemy(EnemyTuning.CreateSparringDummy(), new Vector3(0f, 0f, d), 180f, 1);
                bool hit = false;
                s.World.PlayerEvent += e => { };
                for (int f = 0; f < 60; f++)
                {
                    s.Step(new Pad { Jump = f == 0, Light = f == 16 });   // after Plunge.MinAirTime
                    if (dummy.Brain.Health < dummy.Brain.MaxHealth) hit = true;
                }
                cells.Add(hit ? "yes" : "no");
            }
            r.Row(cells.ToArray());
            r.Print();
        }

        static void GuardDeflect(Options o)
        {
            Out.Sub("Guard vs deflect against the swinging sparring dummy (swing startup 0.6 s, active 0.12 s)");
            Out.Line("Guard is tapped (1 frame) at 'lead' seconds before the swing's first active frame; negative = after it.");
            var t = new Table("Preset", "Lead 0.25 s", "0.20", "0.15", "0.12", "0.10", "0.05", "0.02", "0.00", "-0.03");
            foreach (Preset p in o.Presets)
            {
                var cells = new List<object> { p };
                foreach (float lead in new[] { 0.25f, 0.20f, 0.15f, 0.12f, 0.10f, 0.05f, 0.02f, 0f, -0.03f })
                    cells.Add(DeflectTrial(p, lead, o.Fps, false));
                t.Row(cells.ToArray());
            }
            t.Print();
            Out.Line("Same, but guard is already held and re-pressed (release 2 f, press) at the lead:");
            var t2 = new Table("Preset", "Lead 0.15 s", "0.10", "0.05", "0.00");
            foreach (Preset p in o.Presets)
            {
                var cells = new List<object> { p };
                foreach (float lead in new[] { 0.15f, 0.10f, 0.05f, 0f }) cells.Add(DeflectTrial(p, lead, o.Fps, true));
                t2.Row(cells.ToArray());
            }
            t2.Print();

            Out.Line("Whiff lockout: guard tapped with nothing coming, then tapped again k frames later just before a hit (lead 0.05 s):");
            var t3 = new Table("Second tap after", "8 f", "15 f", "20 f", "25 f", "30 f", "35 f");
            var row = new List<object> { "Outcome (Fluid)" };
            foreach (int k in new[] { 8, 15, 20, 25, 30, 35 }) row.Add(WhiffTrial(k, o.Fps));
            t3.Row(row.ToArray());
            t3.Print();
        }

        // A dummy 1.6 m in front swings every 2.5 s. Returns the outcome of its first swing.
        static string DeflectTrial(Preset p, float lead, float fps, bool heldBefore)
        {
            var s = new Session(p, fps, camera: false);
            SimEnemy dummy = s.World.AddEnemy(EnemyTuning.CreateSparringDummy(), new Vector3(0f, 0f, 2.0f), 180f, 1, dummySwings: true);
            int teleFrame = -1;
            string outcome = "?";
            s.World.EnemyEvent += (e, ev) => { if (ev.Type == EnemyEventType.TelegraphStarted && teleFrame < 0) teleFrame = s.World.Frame; };
            var hits = new List<HitOutcome>();
            s.World.PlayerEvent += e => { };
            float startup = dummy.Tuning.Attacks[0].Move.Startup;
            for (int f = 0; f < 200; f++)
            {
                var pad = new Pad();
                if (teleFrame >= 0)
                {
                    int activeFrame = teleFrame + (int)Math.Ceiling(startup * fps - 1e-3);
                    int pressFrame = activeFrame - (int)Math.Round(lead * fps);
                    if (heldBefore) pad.Guard = f < pressFrame - 2 || f >= pressFrame;
                    else pad.Guard = f == pressFrame;
                    if (heldBefore && f >= pressFrame + 30) pad.Guard = false;
                }
                else if (heldBefore) pad.Guard = true;
                int before = s.World.Metrics.Deflects + s.World.Metrics.Blocks + s.World.Metrics.HitsTaken;
                s.Step(pad);
                SimMetricsSnapshot(s, ref outcome);
                if (outcome != "?") break;
            }
            return outcome;
        }

        static void SimMetricsSnapshot(Session s, ref string outcome)
        {
            Metrics m = s.World.Metrics;
            if (m.Deflects > 0) outcome = "DEFLECT";
            else if (m.Blocks > 0) outcome = "block";
            else if (m.HitsTaken > 0) outcome = "hit";
        }

        static string WhiffTrial(int gap, float fps)
        {
            var s = new Session(Preset.Fluid, fps, camera: false);
            SimEnemy dummy = s.World.AddEnemy(EnemyTuning.CreateSparringDummy(), new Vector3(0f, 0f, 2.0f), 180f, 1, dummySwings: true);
            int teleFrame = -1;
            s.World.EnemyEvent += (e, ev) => { if (ev.Type == EnemyEventType.TelegraphStarted && teleFrame < 0) teleFrame = s.World.Frame; };
            string outcome = "?";
            for (int f = 0; f < 200; f++)
            {
                var pad = new Pad();
                if (teleFrame >= 0)
                {
                    int activeFrame = teleFrame + (int)Math.Ceiling(0.6f * fps - 1e-3);
                    int second = activeFrame - 3;
                    int first = second - gap;
                    pad.Guard = f == first || f == second;
                }
                s.Step(pad);
                SimMetricsSnapshot(s, ref outcome);
                if (outcome != "?") break;
            }
            return outcome;
        }

        static void Heal(Options o)
        {
            Out.Sub("Healing: commitment and interruption");
            var t = new Table("Case", "Result");
            {
                Trace tr = Trace.Run(Preset.Fluid, f => new Pad { Heal = f == 0, Move = new Vector2(0f, 1f) }, 70, null, o.Fps);
                float dist = tr.States[60].Position.Z;
                t.Row("Walk forward while drinking (1.0 s)", "moved " + Out.N(dist, 2) + " m (35% speed)");
            }
            {
                Trace tr = Trace.Run(Preset.Fluid, f => new Pad { Heal = f == 0, Dodge = f == 20 || f == 50 }, 90, null, o.Fps);
                t.Row("Dodge pressed at 0.33 s and 0.83 s into the drink", "dodge at frame " + tr.First(PlayerEventType.DodgeStarted) + " (the heal ends at 61)");
            }
            foreach (var (name, attack) in new[] { ("Quick Slash (poise 15)", 0), ("Heavy Overhead (poise 40)", 1) })
            {
                // Inject the soldier's hit at 0.3 s into the drink.
                var s = new Session(Preset.Fluid, o.Fps, camera: false);
                EnemyTuning soldier = EnemyTuning.CreateDaoSoldier();
                MoveData m = soldier.Attacks[attack].Move;
                bool healed = false, interrupted = false;
                s.World.PlayerEvent += e =>
                {
                    if (e.Type == PlayerEventType.HealApplied) healed = true;
                    if (e.Type == PlayerEventType.HealInterrupted) interrupted = true;
                };
                for (int f = 0; f < 80; f++)
                {
                    s.Step(new Pad { Heal = f == 0 });
                    if (f == 18)
                    {
                        var dmg = new DamageInfo { Damage = m.Damage, PoiseDamage = m.PoiseDamage, GuardStaminaDamage = m.GuardStaminaDamage, Direction = new Vector3(0f, 0f, -1f), SourceTeam = Team.Enemy, SourceId = 999, AttackId = CombatIds.Next(), Kind = m.Kind, Knockback = m.Knockback, Parryable = true };
                        s.Player.ReceiveHit(dmg);
                    }
                }
                t.Row("Hit by a " + name + " 0.3 s into the drink", interrupted ? "drink interrupted (charge kept)" : healed ? "drink continues, healed" : "?");
            }
            t.Print();
        }
    }
}
