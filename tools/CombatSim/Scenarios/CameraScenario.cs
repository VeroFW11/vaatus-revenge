using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Camera and lock-on, measured through the real OrbitCameraModel / LockOnSelector / StickFlickDetector with
    // the ThirdPersonCameraRig + LockOnController logic mirrored (SimCameraRig). The over-the-shoulder tuning
    // (default) is the primary; CameraTuning.CreateCentred() is shown for comparison where it matters.
    public static class CameraScenario
    {
        sealed class CamStats
        {
            public readonly List<float> YawDeltas = new List<float>();
            public readonly List<float> PitchDeltas = new List<float>();
            public readonly List<float> DistDeltas = new List<float>();
            public readonly List<float> ScreenX = new List<float>();
            public readonly List<float> ScreenY = new List<float>();
            public readonly List<float> PlayerScreenX = new List<float>();
            public int Reversals, LongWay, OffScreen, NaNs, Frames;
            float lastYawDelta;

            public void Add(SimCameraRig rig, float prevYaw, float prevPitch, float prevDist, Vector3? target, Vector3 player)
            {
                Frames++;
                OrbitCameraModel o = rig.Orbit;
                if (float.IsNaN(o.Yaw) || float.IsNaN(o.Pitch) || float.IsNaN(o.Distance)) { NaNs++; return; }
                float dy = Angles.Delta(prevYaw, o.Yaw);
                YawDeltas.Add(Math.Abs(dy));
                PitchDeltas.Add(Math.Abs(o.Pitch - prevPitch));
                DistDeltas.Add(Math.Abs(o.Distance - prevDist));
                if (Math.Abs(dy) > 0.05f && Math.Abs(lastYawDelta) > 0.05f && Math.Sign(dy) != Math.Sign(lastYawDelta)) Reversals++;
                if (Math.Abs(dy) > 45f) LongWay++;
                if (Math.Abs(dy) > 1e-4f) lastYawDelta = dy;
                if (target.HasValue)
                {
                    Vector3 sp = rig.ScreenPoint(target.Value);
                    if (sp.Z <= 0f || Math.Abs(sp.X) > 1f || Math.Abs(sp.Y) > 1f) OffScreen++;
                    ScreenX.Add(sp.X);
                    ScreenY.Add(sp.Y);
                }
                Vector3 pp = rig.ScreenPoint(player);
                PlayerScreenX.Add(pp.X);
            }

            public double ReversalsPerSecond(float fps) => Frames > 0 ? Reversals / (Frames / fps) : 0;
        }

        public static void Run(Options o)
        {
            Out.Heading("Camera and lock-on");
            Out.Line("Measured on the over-the-shoulder camera (CameraTuning defaults; 60cb8ee added LockOnMaxYawSpeed 540°/s and ShoulderSwapHoldTime 0.25 s: shoulder 0.55 m / 0.35 m locked, distance 3.2 / 4.0, combat pull-back 0.9 m) "
                     + "unless a row says 'centred' (CameraTuning.CreateCentred(), the first prototype's framing, same code). "
                     + "Screen positions: x, y in −1..1 across a 16:9 screen with a 60° vertical FOV. 'Reversals' = the yaw's direction of travel flipping "
                     + "between frames (a jitter measure); 'max step' = the largest yaw change in one frame.");
            Circling(o);
            DashPast(o);
            Overhead(o);
            Elevated(o);
            LockOnFraming(o);
            Retarget(o);
            Switching(o);
            LineOfSight(o);
            Walls(o);
            ShoulderSwap(o);
            CombatPullback(o);
            LookInput(o);
            InCombat(o);
            Pops(o);
        }

        // Playtest report 02: a replay render showed the camera "collapsing" near the low corridor. Walk, orbit and fight
        // around the corridor and the pillar field and record every one-frame change the viewer would see.
        static void Pops(Options o)
        {
            Out.Sub("Camera pops near the low corridor and the pillars (sandbox arena, Fluid)");
            Out.Line("Pull-in = one-frame drop in the camera's distance behind the shoulder point. Jump = one-frame move of the camera "
                     + "position itself (walking at 4.8 m/s plus orbiting at full stick is ~0.2 m per frame, so anything well above that is a visible pop). "
                     + "Lift = the combat rise above the pivot. Fights: anticipate bot, locked on, one Dao Soldier, 10 seeds × 30 s.");
            var t = new Table("Camera", "Case", "Min distance", "Largest pull-in", "Frames pulling in > 0.3 m", "Largest camera jump", "Largest lift drop", "Inside geometry (frames)");
            var cases = new (string name, Vector3 at, float yaw, Func<Session, int, Pad> pad, int frames, bool fight)[]
            {
                ("stand at the corridor mouth, orbit 1.3 turns", new Vector3(18f, 0f, 4.6f), 0f, (s, f) => new Pad { Look = new Vector2(0.6f, 0f) }, 240, false),
                ("stand inside the corridor, orbit 1.3 turns", new Vector3(18f, 0f, 12f), 0f, (s, f) => new Pad { Look = new Vector2(0.6f, 0f) }, 240, false),
                ("walk in diagonally from the south-west", new Vector3(13.5f, 0f, 1.5f), 45f, (s, f) => new Pad { Move = Session.StickFor(
                    s.Player.Feet.Z < 6.5f ? new Vector3(18f, 0f, 6.5f) - s.Player.Feet : new Vector3(0f, 0f, 1f), s.World.CameraYaw) }, 300, false),
                ("walk in, then turn round and walk out (camera trails in)", new Vector3(18f, 0f, 2f), 0f, (s, f) => new Pad { Move = Session.StickFor(
                    f < 150 ? new Vector3(0f, 0f, 1f) : new Vector3(0f, 0f, -1f), s.World.CameraYaw), Look = f >= 150 && f < 190 ? new Vector2(1f, 0f) : Vector2.Zero }, 330, false),
                ("walk north along the outside of the corridor's west wall", new Vector3(15.3f, 0f, 2f), 0f, (s, f) => new Pad { Move = Session.StickFor(new Vector3(0f, 0f, 1f), s.World.CameraYaw) }, 260, false),
                ("walk north through the pillar field, orbiting", new Vector3(14.5f, 0f, -27f), 0f, (s, f) => new Pad { Move = Session.StickFor(new Vector3(0f, 0f, 1f), s.World.CameraYaw), Look = new Vector2(0.35f, 0f) }, 300, false),
                ("strafe east across the pillar rows", new Vector3(9f, 0f, -16.5f), 0f, (s, f) => new Pad { Move = Session.StickFor(new Vector3(1f, 0f, 0f), s.World.CameraYaw) }, 300, false),
                ("fight a soldier at the corridor mouth", new Vector3(18f, 0f, 3f), 0f, null, 1800, true),
                ("fight a soldier in the pillar field", new Vector3(14.5f, 0f, -21.5f), 0f, null, 1800, true),
            };
            foreach (bool centred in new[] { false, true })
            {
                foreach (var c in cases)
                {
                    float minD = float.MaxValue, maxIn = 0f, maxJump = 0f, maxLiftDrop = 0f;
                    int bigIn = 0, inside = 0;
                    int runs = c.fight ? 10 : 1;
                    for (int run = 0; run < runs; run++)
                    {
                        var s = CamSession(o, centred ? CameraTuning.CreateCentred() : new CameraTuning(), SimLevel.SandboxArena(), c.at, c.yaw);
                        Bot bot = null;
                        if (c.fight)
                        {
                            Vector3 foe = c.at + new Vector3(0f, 0f, 5f);
                            s.World.AddEnemy(EnemyTuning.CreateDaoSoldier(), foe, 180f, (o.Seed + run) * 31 + 1);
                            bot = Bots.Create("anticipate");
                            bot.Attach(s, (o.Seed + run) * 977 + 13);
                        }
                        for (int f = 0; f < 20; f++) s.Step(new Pad());
                        OrbitCameraModel orb = s.World.LockOn.Orbit;
                        for (int f = 0; f < c.frames; f++)
                        {
                            float d0 = orb.Distance, l0 = orb.Lift;
                            Vector3 p0 = s.World.LockOn.CameraPosition;
                            s.Step(bot != null ? bot.NextPad() : c.pad(s, f));
                            float dd = d0 - orb.Distance;
                            maxIn = Math.Max(maxIn, dd);
                            if (dd > 0.3f) bigIn++;
                            maxLiftDrop = Math.Max(maxLiftDrop, l0 - orb.Lift);
                            maxJump = Math.Max(maxJump, Vector3.Distance(p0, s.World.LockOn.CameraPosition));
                            minD = Math.Min(minD, orb.Distance);
                            inside += Inside(s) ? 1 : 0;
                            if (bot != null && (!s.Model.IsAlive || s.World.AllEnemiesDead)) break;
                        }
                    }
                    t.Row(centred ? "centred" : "shoulder", c.name, Out.N(minD, 2) + " m", Out.N(maxIn, 2) + " m", bigIn, Out.N(maxJump, 2) + " m",
                        Out.N(maxLiftDrop, 2) + " m", inside);
                }
            }
            t.Print();
        }

        static Session CamSession(Options o, CameraTuning tuning, SimLevel level = null, Vector3? playerAt = null, float yaw = 0f)
        {
            var s = new Session(Preset.Fluid, o.Fps, level ?? SimLevel.Empty(), camera: false, playerAt: playerAt, playerYaw: yaw);
            s.World.AddCameraRig(tuning);
            return s;
        }

        static SimEnemy Puppet(Session s, Vector3 at, bool killable = false)
        {
            EnemyTuning t = EnemyTuning.CreateSparringDummy();
            t.AggroRange = 0f;
            t.MaxHealth = killable ? 10f : 1e6f;
            t.Unkillable = !killable;
            t.HealthRefillDelay = 0f;
            return s.World.AddEnemy(t, at, 0f, 1);
        }

        // Steps the world once, recording the camera. The puppet target is moved kinematically before the step.
        static void Step(Session s, CamStats st, Pad pad, SimFighter target)
        {
            OrbitCameraModel orbit = s.World.LockOn.Orbit;
            float y = orbit.Yaw, p = orbit.Pitch, d = orbit.Distance;
            s.Step(pad);
            st.Add(s.World.LockOn, y, p, d, target != null ? target.AimPoint : (Vector3?)null, s.Player.Feet + new Vector3(0f, 1.3f, 0f));
        }

        static void Circling(Options o)
        {
            Out.Sub("Locked-on target circling the player (5 s after 1 s to settle)");
            var t = new Table("Camera", "Radius", "Speed", "Target off-screen", "Mean |target x|", "Max yaw step", "Reversals / s", "Player x (mean)");
            foreach (bool centred in new[] { false, true })
            {
                foreach (float r in new[] { 1.2f, 2f, 4f, 8f })
                {
                    foreach (float w in new[] { 45f, 180f, 360f })
                    {
                        if (centred && r != 2f) continue;
                        var s = CamSession(o, centred ? CameraTuning.CreateCentred() : new CameraTuning());
                        SimEnemy target = Puppet(s, new Vector3(0f, 0f, r));
                        s.World.LockOn.ForceLock(target);
                        var st = new CamStats();
                        int frames = (int)(6 * o.Fps);
                        for (int f = 0; f < frames; f++)
                        {
                            float ang = w * f / o.Fps;
                            target.Controller.Position = Directions.FromYaw(ang) * r;
                            if (f < o.Fps) { s.Step(new Pad()); continue; }
                            Step(s, st, new Pad(), target);
                        }
                        t.Row(centred ? "centred" : "shoulder", r + " m", w + "°/s", Out.Pct(st.OffScreen / (double)st.Frames),
                            Out.N(st.ScreenX.Average(x => Math.Abs(x)), 2), Out.N(st.YawDeltas.Max(), 2) + "°", Out.N(st.ReversalsPerSecond(o.Fps), 1),
                            Out.N(st.PlayerScreenX.Average(), 2));
                    }
                }
            }
            t.Print();
        }

        static void DashPast(Options o)
        {
            Out.Sub("Player dashes (Flame Step) past a locked target standing 1 m to the side of the path");
            var t = new Table("Camera", "Max yaw step", "Yaw swept", "Target off-screen frames", "Long-way flips", "Reversals");
            foreach (bool centred in new[] { false, true })
            {
                var s = CamSession(o, centred ? CameraTuning.CreateCentred() : new CameraTuning(), playerAt: new Vector3(0f, 0f, -2f));
                SimEnemy target = Puppet(s, new Vector3(1.0f, 0f, 0.5f));
                s.World.LockOn.ForceLock(target);
                var st = new CamStats();
                for (int f = 0; f < 30; f++) s.Step(new Pad());
                float startYaw = s.World.LockOn.Orbit.Yaw;
                float swept = 0f;
                for (int f = 0; f < 90; f++)
                {
                    float before = s.World.LockOn.Orbit.Yaw;
                    // Dash forward past the target (world +Z = away from the camera at the start).
                    Step(s, st, new Pad { Dodge = f < 2 || (f > 20 && f < 22), Move = s.StickToward(new Vector3(0f, 0f, 1f)) }, target);
                    swept += Angles.Delta(before, s.World.LockOn.Orbit.Yaw);
                }
                t.Row(centred ? "centred" : "shoulder", Out.N(st.YawDeltas.Max(), 1) + "°", Out.N(swept, 0) + "°", st.OffScreen, st.LongWay, st.Reversals);
            }
            t.Print();
        }

        static void Overhead(Options o)
        {
            Out.Sub("Target passing straight over the player (2.5 m up, 3 m/s), and a target standing on the player's spot");
            var t = new Table("Case", "NaN frames", "Max yaw step", "Max pitch step", "Pitch range", "Long-way flips");
            foreach (string c in new[] { "overhead pass", "on top of the player" })
            {
                var s = CamSession(o, new CameraTuning());
                SimEnemy target = Puppet(s, new Vector3(0f, 2.5f, -3f));
                target.Controller.Enabled = false;   // kinematic puppet: nothing collides with it
                s.World.LockOn.ForceLock(target);
                var st = new CamStats();
                float minP = 999, maxP = -999;
                for (int f = 0; f < 150; f++)
                {
                    if (c == "overhead pass") target.Controller.Position = new Vector3(0f, 2.5f, -3f + 3f * f / o.Fps);
                    else target.Controller.Position = new Vector3(0f, 0f, 0f);
                    Step(s, st, new Pad(), target);
                    minP = Math.Min(minP, s.World.LockOn.Orbit.Pitch);
                    maxP = Math.Max(maxP, s.World.LockOn.Orbit.Pitch);
                }
                t.Row(c, st.NaNs, Out.N(st.YawDeltas.Max(), 2) + "°", Out.N(st.PitchDeltas.Max(), 2) + "°", Out.N(minP, 0) + "° .. " + Out.N(maxP, 0) + "°", st.LongWay);
            }
            t.Print();
        }

        static void Elevated(Options o)
        {
            Out.Sub("Locked onto the platform Crossbowman (feet 2.5 m up) from the ground");
            var t = new Table("Camera", "Horizontal distance", "Target on screen (y)", "Player on screen (y)", "Pitch");
            foreach (bool centred in new[] { false, true })
            {
                foreach (float d in new[] { 1.0f, 2f, 4f, 8f })
                {
                    // Target standing at the platform's south edge (z 9.5); the player below it on the ground.
                    var s = CamSession(o, centred ? CameraTuning.CreateCentred() : new CameraTuning(), SimLevel.SandboxArena(), new Vector3(-13f, 0f, 9.5f - d), 0f);
                    SimEnemy target = Puppet(s, new Vector3(-13f, 2.5f, 9.5f));
                    s.World.LockOn.ForceLock(target);
                    for (int f = 0; f < 120; f++) s.Step(new Pad());
                    Vector3 sp = s.World.LockOn.ScreenPoint(target.AimPoint);
                    Vector3 pp = s.World.LockOn.ScreenPoint(s.Player.Feet + new Vector3(0f, 1.0f, 0f));
                    float hd = Directions.Flatten(target.Feet - s.Player.Feet).Length();
                    t.Row(centred ? "centred" : "shoulder", Out.N(hd, 1) + " m", Visible(sp) + " (" + Out.N(sp.Y, 2) + ")", Visible(pp) + " (" + Out.N(pp.Y, 2) + ")", Out.N(s.World.LockOn.Orbit.Pitch, 0) + "°");
                }
            }
            t.Print();
        }

        static string Visible(Vector3 sp) => sp.Z > 0f && Math.Abs(sp.X) <= 1f && Math.Abs(sp.Y) <= 1f ? "yes" : "NO";

        static void LockOnFraming(Options o)
        {
            Out.Sub("Lock-on framing vs distance (target straight ahead, settled)");
            var t = new Table("Distance", "Target x (shoulder)", "Player x (shoulder)", "Target x (centred)", "Player x (centred)");
            foreach (float d in new[] { 1.0f, 1.5f, 2f, 3f, 5f, 10f })
            {
                var row = new List<object> { d + " m" };
                var cells = new List<string>();
                foreach (bool centred in new[] { false, true })
                {
                    var s = CamSession(o, centred ? CameraTuning.CreateCentred() : new CameraTuning());
                    SimEnemy target = Puppet(s, new Vector3(0f, 0f, d + 0.8f));
                    s.World.LockOn.ForceLock(target);
                    for (int f = 0; f < 120; f++) s.Step(new Pad());
                    Vector3 sp = s.World.LockOn.ScreenPoint(target.AimPoint);
                    Vector3 pp = s.World.LockOn.ScreenPoint(s.Player.Feet + new Vector3(0f, 1.3f, 0f));
                    cells.Add(Out.N(sp.X, 2));
                    cells.Add(Out.N(pp.X, 2));
                }
                row.AddRange(cells);
                t.Row(row.ToArray());
            }
            t.Print();
        }

        static void Retarget(Options o)
        {
            Out.Sub("Target killed while locked: where does the lock go?");
            var t = new Table("Layout (other enemies relative to camera forward)", "Lock after the kill");
            foreach (var layout in new[] { new[] { 20f, -35f }, new[] { 70f, -80f }, new[] { 150f }, new[] { 50f, 55f } })
            {
                var s = CamSession(o, new CameraTuning());
                SimEnemy victim = Puppet(s, new Vector3(0f, 0f, 4f), killable: true);
                victim.Name = "victim";
                var others = new List<SimEnemy>();
                foreach (float ang in layout)
                {
                    SimEnemy e = Puppet(s, Directions.FromYaw(ang) * 5f);
                    e.Name = "enemy at " + ang + "°";
                    others.Add(e);
                }
                s.World.LockOn.ForceLock(victim);
                for (int f = 0; f < 60; f++) s.Step(new Pad());
                victim.ReceiveHit(new DamageInfo { Damage = 1e7f, SourceTeam = Team.Player, SourceId = s.Player.Id, AttackId = CombatIds.Next() });
                for (int f = 0; f < 3; f++) s.Step(new Pad());
                SimFighter tgt = s.World.LockOn.Target;
                t.Row(string.Join(", ", layout.Select(a => a + "°")), tgt != null ? tgt.Name : "none (lock dropped)");
            }
            t.Print();
        }

        static void Switching(Options o)
        {
            Out.Sub("Switching targets: enemies at screen angles −40°, −15°, +10°, +35° (5 m away), locked on the −15° one");
            var t = new Table("Input", "New target", "Expected");
            foreach (var (label, pad, expect) in new[] {
                ("stick flick right", new Pad { Look = new Vector2(0.95f, 0f) }, "+10°"),
                ("stick flick left", new Pad { Look = new Vector2(-0.95f, 0f) }, "−40°"),
                ("mouse wheel down (right)", new Pad { SwitchTarget = 1 }, "+10°"),
                ("stick pushed diagonally up-right (0.7, 0.7)", new Pad { Look = new Vector2(0.7f, 0.7f) }, "no switch"),
                ("stick at 0.6 sideways (under the 0.75 threshold)", new Pad { Look = new Vector2(0.6f, 0f) }, "no switch") })
            {
                var s = CamSession(o, new CameraTuning());
                var map = new Dictionary<SimFighter, string>();
                SimEnemy start = null;
                foreach (float a in new[] { -40f, -15f, 10f, 35f })
                {
                    SimEnemy e = Puppet(s, Directions.FromYaw(a) * 5f);
                    map[e] = (a > 0 ? "+" : "−") + Math.Abs(a) + "°";
                    if (a == -15f) start = e;
                }
                s.World.LockOn.ForceLock(start);
                for (int f = 0; f < 60; f++) s.Step(new Pad());
                // Let the camera settle facing the target, then apply the input (the stick starts centred).
                s.Step(new Pad());
                for (int f = 0; f < 3; f++) s.Step(pad);
                SimFighter tgt = s.World.LockOn.Target;
                t.Row(label, tgt != null ? map[tgt] : "none", expect);
            }
            t.Print();
            Out.Line("Note: screen angles here are measured from the camera; with the shoulder offset the camera looks slightly left of the locked target, "
                     + "which is what the switching maths uses too.");
        }

        static void LineOfSight(Options o)
        {
            Out.Sub("Line of sight: a locked target steps behind a pillar (sandbox pillar field) for a while");
            Out.Line("Hidden = from the player's eyes. The lock also counts the camera's view, and the camera (behind and to the side) keeps seeing the "
                     + "target for ~0.25 s after the eyes lose it, so the 1.2 s grace effectively runs to ~1.45 s here.");
            var t = new Table("Hidden from the eyes for", "Lock kept?");
            foreach (float hide in new[] { 0.8f, 1.3f, 1.5f, 2.0f })
            {
                var s = CamSession(o, new CameraTuning(), SimLevel.SandboxArena(), new Vector3(17f, 0f, -24f), 0f);
                // Pillar_1_0 at (17, -19) 1.6 m wide. Target starts visible at (19.5, -16) then hides behind it at (17, -16).
                SimEnemy target = Puppet(s, new Vector3(19.5f, 0f, -16f));
                s.World.LockOn.ForceLock(target);
                for (int f = 0; f < 30; f++) s.Step(new Pad());
                target.Controller.Position = new Vector3(17f, 0f, -16f);
                int hideFrames = (int)(hide * o.Fps);
                for (int f = 0; f < hideFrames; f++) s.Step(new Pad());
                target.Controller.Position = new Vector3(19.5f, 0f, -16f);
                for (int f = 0; f < 5; f++) s.Step(new Pad());
                t.Row(Out.N(hide, 1) + " s", s.World.LockOn.Target == target ? "yes" : "no (" + s.World.LockOn.LastBreak + ")");
            }
            t.Print();
        }

        static void Walls(Options o)
        {
            Out.Sub("Walls: backing into the arena wall, then walking away; running through the low corridor");
            var t = new Table("Camera", "Case", "Min distance", "Largest pull-in in one frame", "Frames to ease back out (90%)", "Largest ease-out step", "Camera inside geometry (frames)");
            foreach (bool centred in new[] { false, true })
            {
                {
                    // Player 1.5 m from the south wall's inner face (z = -30), facing north, walks back into it, then away.
                    var s = CamSession(o, centred ? CameraTuning.CreateCentred() : new CameraTuning(), SimLevel.SandboxArena(), new Vector3(0f, 0f, -26f), 0f);
                    var dists = new List<float>();
                    int inside = 0;
                    for (int f = 0; f < 60; f++) s.Step(new Pad());
                    float settled = s.World.LockOn.Orbit.Distance;
                    for (int f = 0; f < 90; f++) { s.Step(new Pad { Move = new Vector2(0f, -0.4f) }); dists.Add(s.World.LockOn.Orbit.Distance); inside += Inside(s) ? 1 : 0; }
                    int awayStart = dists.Count;
                    for (int f = 0; f < 120; f++) { s.Step(new Pad { Move = new Vector2(0f, 1f) }); dists.Add(s.World.LockOn.Orbit.Distance); inside += Inside(s) ? 1 : 0; }
                    float min = dists.Min();
                    float maxIn = 0f, maxOut = 0f;
                    for (int i = 1; i < dists.Count; i++)
                    {
                        float dd = dists[i] - dists[i - 1];
                        if (dd < 0f) maxIn = Math.Max(maxIn, -dd); else maxOut = Math.Max(maxOut, dd);
                    }
                    int ease = -1;
                    float target90 = min + 0.9f * (settled - min);
                    for (int i = awayStart; i < dists.Count; i++) if (dists[i] >= target90) { ease = i - awayStart; break; }
                    t.Row(centred ? "centred" : "shoulder", "back into the wall, then away", Out.N(min, 2) + " m", Out.N(maxIn, 2) + " m", ease >= 0 ? ease + " f" : "never",
                        Out.N(maxOut, 3) + " m", inside);
                }
                {
                    // Low corridor: centre (18, 12), 3 m wide, roof at 2.6 m, z 6..18. Run through it along +Z.
                    var s = CamSession(o, centred ? CameraTuning.CreateCentred() : new CameraTuning(), SimLevel.SandboxArena(), new Vector3(18f, 0f, 2f), 0f);
                    var dists = new List<float>();
                    int inside = 0;
                    for (int f = 0; f < 30; f++) s.Step(new Pad());
                    for (int f = 0; f < 240; f++) { s.Step(new Pad { Move = new Vector2(0f, 1f) }); dists.Add(s.World.LockOn.Orbit.Distance); inside += Inside(s) ? 1 : 0; }
                    float maxIn = 0f, maxOut = 0f;
                    for (int i = 1; i < dists.Count; i++)
                    {
                        float dd = dists[i] - dists[i - 1];
                        if (dd < 0f) maxIn = Math.Max(maxIn, -dd); else maxOut = Math.Max(maxOut, dd);
                    }
                    t.Row(centred ? "centred" : "shoulder", "run through the low corridor", Out.N(dists.Min(), 2) + " m", Out.N(maxIn, 2) + " m", "-", Out.N(maxOut, 3) + " m", inside);
                }
            }
            t.Print();
        }

        // Is the camera's near-clip sphere inside level geometry this frame?
        static bool Inside(Session s)
        {
            return s.World.Level.IsOverlapping(s.World.LockOn.CameraPosition, 0.08f);
        }

        static void ShoulderSwap(Options o)
        {
            Out.Sub("Shoulder auto-swap: walking along the east arena wall with it on the camera's right, then turning away; and the pillar field");
            var t = new Table("Case", "Auto swaps", "Swap-backs", "Offset range", "Largest offset step / frame");
            foreach (float gap in new[] { 0.8f, 0.45f })
            {
                var s = CamSession(o, new CameraTuning(), SimLevel.SandboxArena(), new Vector3(30f - gap, 0f, -20f), 0f);
                var offs = new List<float>();
                int swaps = 0, backs = 0;
                bool was = false;
                for (int f = 0; f < 60; f++) s.Step(new Pad());
                for (int f = 0; f < 360; f++)
                {
                    Vector2 stick = f < 150 ? new Vector2(0f, 1f) : new Vector2(-1f, 0f);
                    s.Step(new Pad { Move = stick });
                    bool now = s.World.LockOn.Orbit.IsShoulderAutoSwapped;
                    if (now && !was) swaps++;
                    if (!now && was) backs++;
                    was = now;
                    offs.Add(s.World.LockOn.Orbit.ShoulderOffset);
                }
                float step = 0f;
                for (int i = 1; i < offs.Count; i++) step = Math.Max(step, Math.Abs(offs[i] - offs[i - 1]));
                t.Row("along the wall (" + gap + " m from the player's centre) for 2.5 s, then 3.5 s away from it", swaps, backs, Out.N(offs.Min(), 2) + " .. " + Out.N(offs.Max(), 2) + " m", Out.N(step, 3) + " m");
            }
            {
                var s = CamSession(o, new CameraTuning(), SimLevel.SandboxArena(), new Vector3(14.5f, 0f, -27f), 0f);
                var offs = new List<float>();
                int swaps = 0, backs = 0;
                bool was = false;
                for (int f = 0; f < 30; f++) s.Step(new Pad());
                for (int f = 0; f < 300; f++)
                {
                    // Weave north through the pillar field (pillars at x 12/17/22, z -24..-9).
                    float x = (float)Math.Sin(f / 25.0) * 0.8f;
                    s.Step(new Pad { Move = new Vector2(x, 1f) });
                    bool now = s.World.LockOn.Orbit.IsShoulderAutoSwapped;
                    if (now && !was) swaps++;
                    if (!now && was) backs++;
                    was = now;
                    offs.Add(s.World.LockOn.Orbit.ShoulderOffset);
                }
                float step = 0f;
                for (int i = 1; i < offs.Count; i++) step = Math.Max(step, Math.Abs(offs[i] - offs[i - 1]));
                t.Row("weaving through the pillar field for 5 s", swaps, backs, Out.N(offs.Min(), 2) + " .. " + Out.N(offs.Max(), 2) + " m", Out.N(step, 3) + " m");
            }
            t.Print();

            // CTRL-04 (60cb8ee): the swap button (L3 / V) must be held CameraTuning.ShoulderSwapHoldTime; one swap per hold.
            var h = new Table("Swap button held for", "Shoulder side after", "Swaps");
            foreach (int frames in new[] { 1, 5, 10, 14, 15, 16, 30, 120 })
            {
                var s = CamSession(o, new CameraTuning());
                int side0 = s.World.LockOn.Orbit.ShoulderSide, changes = 0, last = side0;
                for (int f = 0; f < frames + 30; f++)
                {
                    s.Step(new Pad { SwapShoulder = f < frames });
                    int now = s.World.LockOn.Orbit.ShoulderSide;
                    if (now != last) changes++;
                    last = now;
                }
                h.Row(frames + " f (" + Out.N(frames / o.Fps, 2) + " s)", last == side0 ? "same" : "swapped", changes);
            }
            h.Print();
        }

        static void CombatPullback(Options o)
        {
            Out.Sub("Combat pull-back (not locked on): a foe walks in to 5 m, then circles at the 8 m edge, then leaves");
            var s = CamSession(o, new CameraTuning());
            SimEnemy foe = Puppet(s, new Vector3(0f, 0f, 20f));
            var d = new List<float>();
            int frame = 0;
            for (; frame < 60; frame++) { s.Step(new Pad()); d.Add(s.World.LockOn.Orbit.DesiredDistance); }
            for (int f = 0; f < 120; f++, frame++) { foe.Controller.Position = new Vector3(0f, 0f, 20f - 15f * f / 120f); s.Step(new Pad()); d.Add(s.World.LockOn.Orbit.DesiredDistance); }
            float atClose = s.World.LockOn.Orbit.DesiredDistance;
            int pumps = 0;
            float prevBlend = s.World.LockOn.Orbit.CombatFraming;
            bool rising = true;
            for (int f = 0; f < 360; f++, frame++)
            {
                float r = 8f + 0.5f * (float)Math.Sin(f / 20.0);   // 7.5 .. 8.5 m
                foe.Controller.Position = Directions.FromYaw(f * 1.5f) * r;
                s.Step(new Pad());
                float b = s.World.LockOn.Orbit.CombatFraming;
                bool nowRising = b > prevBlend + 1e-4f;
                bool nowFalling = b < prevBlend - 1e-4f;
                if ((rising && nowFalling) || (!rising && nowRising)) { pumps++; rising = nowRising; }
                prevBlend = b;
                d.Add(s.World.LockOn.Orbit.DesiredDistance);
            }
            float edgeMin = d.Skip(180).Take(360).Min(), edgeMax = d.Skip(180).Take(360).Max();
            for (int f = 0; f < 240; f++) { foe.Controller.Position = new Vector3(0f, 0f, 20f); s.Step(new Pad()); d.Add(s.World.LockOn.Orbit.DesiredDistance); }
            var t = new Table("Phase", "Camera distance");
            t.Row("no foe", Out.N(d[59], 2) + " m");
            t.Row("foe at 5 m", Out.N(atClose, 2) + " m");
            t.Row("foe circling 7.5–8.5 m for 6 s", Out.N(edgeMin, 2) + " .. " + Out.N(edgeMax, 2) + " m (" + pumps + " in/out direction changes)");
            t.Row("4 s after the foe left", Out.N(d[d.Count - 1], 2) + " m");
            t.Print();
        }

        static void LookInput(Options o)
        {
            Out.Sub("Look input at different frame rates (free camera, 1 s of input)");
            var t = new Table("Input", "30 fps", "60 fps", "144 fps", "30 fps with 5% hitches");
            {
                var cells = new List<object> { "Mouse 600 px/s to the right" };
                foreach (string mode in new[] { "30", "60", "144", "hitch" })
                {
                    float fps = mode == "hitch" ? 30f : float.Parse(mode);
                    var s = new Session(Preset.Fluid, fps, SimLevel.Empty(), camera: false);
                    s.World.AddCameraRig(new CameraTuning());
                    float y0 = s.World.LockOn.Orbit.Yaw;
                    var rng = new DeterministicRandom(3);
                    double time = 0;
                    float total = 0f;
                    while (time < 1.0)
                    {
                        float dt = 1f / fps;
                        if (mode == "hitch" && rng.NextFloat() < 0.05f) dt = 0.15f;
                        if (time + dt > 1.0) dt = (float)(1.0 - time);
                        var pad = new Pad { Look = new Vector2(600f * dt, 0f), LookIsMouse = true };
                        s.Step(pad, dt);
                        time += dt;
                    }
                    total = Angles.Delta(y0, s.World.LockOn.Orbit.Yaw);
                    cells.Add(Out.N(total, 1) + "°");
                }
                t.Row(cells.ToArray());
            }
            {
                var cells = new List<object> { "Right stick fully right" };
                foreach (string mode in new[] { "30", "60", "144", "hitch" })
                {
                    float fps = mode == "hitch" ? 30f : float.Parse(mode);
                    var s = new Session(Preset.Fluid, fps, SimLevel.Empty(), camera: false);
                    s.World.AddCameraRig(new CameraTuning());
                    float y0 = s.World.LockOn.Orbit.Yaw;
                    var rng = new DeterministicRandom(3);
                    double time = 0;
                    float unwrapped = 0f, last = y0;
                    while (time < 1.0)
                    {
                        float dt = 1f / fps;
                        if (mode == "hitch" && rng.NextFloat() < 0.05f) dt = 0.15f;
                        if (time + dt > 1.0) dt = (float)(1.0 - time);
                        s.Step(new Pad { Look = new Vector2(1f, 0f) }, dt);
                        unwrapped += Angles.Delta(last, s.World.LockOn.Orbit.Yaw);
                        last = s.World.LockOn.Orbit.Yaw;
                        time += dt;
                    }
                    cells.Add(Out.N(unwrapped, 1) + "°");
                }
                t.Row(cells.ToArray());
            }
            t.Print();
            Out.Line("Mouse turns the same at any frame rate (the delta is never scaled by time). The stick is time-scaled, and a hitch longer than 0.1 s is capped "
                     + "(MaxStickStep), so heavy hitches turn slightly less, by design.");
            {
                // Hitstop: does the camera still turn while the game is frozen?
                var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty(), camera: false);
                s.World.AddCameraRig(new CameraTuning());
                s.World.Time.Hitstop(0.2f);
                float y0 = s.World.LockOn.Orbit.Yaw;
                for (int f = 0; f < 10; f++) s.Step(new Pad { Look = new Vector2(1f, 0f) });
                Out.Line("During a 0.2 s hitstop the stick still turns the camera " + Out.N(Angles.Delta(y0, s.World.LockOn.Orbit.Yaw), 1) + "° in 10 frames (real time), as intended.");
            }
        }

        static void InCombat(Options o)
        {
            Out.Sub("Camera during real fights (anticipate bot, locked on, 2 soldiers + crossbowman, 10 seeds × 60 s)");
            var t = new Table("Camera", "Max yaw step", "99th pct yaw step", "Yaw reversals / s", "Max pitch step", "Max distance step", "Target off-screen", "Long-way flips");
            foreach (bool centred in new[] { false, true })
            {
                var all = new CamStats();
                for (int seed = o.Seed; seed < o.Seed + 10; seed++)
                {
                    var s = new Session(Preset.Fluid, o.Fps, SimLevel.SandboxArena(), camera: false, playerAt: new Vector3(0f, 0f, -5f));
                    s.World.AddCameraRig(centred ? CameraTuning.CreateCentred() : new CameraTuning());
                    s.World.AddEnemy(EnemyTuning.CreateDaoSoldier(), new Vector3(-2.5f, 0f, 2f), 180f, seed * 31 + 1);
                    s.World.AddEnemy(EnemyTuning.CreateDaoSoldier(), new Vector3(2.5f, 0f, 2f), 180f, seed * 31 + 8);
                    s.World.AddEnemy(EnemyTuning.CreateCrossbowman(), new Vector3(8f, 0f, 9f), -140f, seed * 31 + 17);
                    Bot bot = Bots.Create("anticipate");
                    bot.Attach(s, seed * 977 + 13);
                    for (int f = 0; f < (int)(60 * o.Fps); f++)
                    {
                        OrbitCameraModel orb = s.World.LockOn.Orbit;
                        float y = orb.Yaw, p = orb.Pitch, d = orb.Distance;
                        s.Step(bot.NextPad());
                        SimFighter tgt = s.World.LockOn.Target;
                        all.Add(s.World.LockOn, y, p, d, tgt != null ? tgt.AimPoint : (Vector3?)null, s.Player.AimPoint);
                        if (!s.Model.IsAlive || s.World.AllEnemiesDead) break;
                    }
                }
                t.Row(centred ? "centred" : "shoulder", Out.N(all.YawDeltas.Max(), 1) + "°", Out.N(Stats.Percentile(all.YawDeltas.Select(x => (double)x), 0.99), 2) + "°",
                    Out.N(all.ReversalsPerSecond(o.Fps), 1), Out.N(all.PitchDeltas.Max(), 2) + "°", Out.N(all.DistDeltas.Max(), 3) + " m",
                    Out.Pct(all.ScreenX.Count > 0 ? all.OffScreen / (double)all.ScreenX.Count : 0), all.LongWay);
            }
            t.Print();
        }
    }
}
