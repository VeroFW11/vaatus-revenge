using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Build 05 dodge (spec 2.3, Spider-Man 2 style), measured for every element and both presets:
    //   facing      - how far the player's facing strays from the enemy during a dodge (a live Dao Soldier, random dodges)
    //   slip-in     - where a dodge toward the enemy stops (radii + SlipInStopGap, never through it)
    //   spam        - the share of time a dodge masher is invulnerable (the chain limit and i-frame gap keep it under 60 %)
    //   exit speed  - how long after a dodge ends, stick held, until you're back at run speed; and the biggest one-frame
    //                 speed drop across the exit, stick held and let go (no dead stop: at most RunSpeed, verify J-08)
    //   side-slip   - a side-slip circles the enemy: the distance to it stays within 0.3 m, two in a row (verify J-10)
    //   string      - X X, then a dodge / zip strike / ability, then X: the string's third hit comes out (the dodge strike lands,
    //                 and its body is within 0.8 m of the enemy's as it strikes: no hit from metres away, verify J-06)
    // Cells marked OK / MISS are checked targets: a miss makes CombatSim exit with code 1.
    public static class DodgeFlowScenario
    {
        static readonly ElementId[] Elements = { ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air };

        public static void Run(Options o)
        {
            Out.Heading("Dodge flow: facing, slip-in, spam, exit speed, the string across a dodge (" + o.Seeds + " seeds)");
            Facing(o);
            SlipIn(o);
            Spam(o);
            ExitSpeed(o);
            ExitDrop(o);
            SideSlipCircle(o);
            StringAcross(o);
            WalkBackIn(o);

            // The same dodge measurements on the Xbox path (J5-01): every press through the game's PadChordReader, the
            // element picked with hold RB, then its face button. No added latency, so the targets are the same.
            Out.Heading("Dodge flow on the Xbox pad (real PadChordReader path): facing, slip-in, spam, the string across a dodge");
            realPad = true;
            try
            {
                Facing(o);
                SlipIn(o);
                Spam(o);
                StringAcross(o);
            }
            finally
            {
                realPad = false;
            }
        }

        static bool realPad;
        static int padSeed;

        // A session with all four elements, switched to 'element' (a plain switch while free is instant; on the pad the
        // chord takes a few frames).
        static Session Start(Preset p, ElementId element, float fps, Vector3 playerAt, float playerYaw = 0f)
        {
            var s = new Session(p, fps, SimLevel.Empty(), camera: true, playerAt: playerAt, playerYaw: playerYaw);
            if (realPad) s.Input.UseRealPad(++padSeed * 7 + 3);
            if (element != ElementId.Fire) s.Step(new Pad { Element = element });
            s.Step(new Pad());
            for (int i = 0; i < 30 && s.Input.ChordInFlight; i++) s.Step(new Pad());
            if (s.Model.ActiveElement != element) throw new InvalidOperationException("couldn't switch to " + element);
            return s;
        }

        // The tutorial's sparring partner, rooted: never attacks, moves, breaks out or dies.
        static SimEnemy Partner(Session s, Vector3 at, int seed)
        {
            EnemyTuning t = EnemyTuning.CreateTutorialPartner();
            t.BreakOut.Enabled = false;
            t.WalkSpeed = t.ChaseSpeed = t.StrafeSpeed = t.RetreatSpeed = 0f;
            SimEnemy e = s.World.AddEnemy(t, at, 180f, seed);
            e.Brain.Passive = true;
            return e;
        }

        static float YawTo(SimFighter from, SimFighter to) => Directions.YawOf(Directions.Flatten(to.Feet - from.Feet), 0f);

        static Vector3 OnCircle(float yawDegrees, float radius) => Directions.FromYaw(yawDegrees) * radius;

        // ---------------------------------------------------------------- facing

        // A live Dao Soldier (it chases, circles and attacks); the player dodges every 0.35-0.9 s with the stick in one of
        // eight directions around the line to it, or neutral. Half the seeds are locked on. Every dodging frame of a dodge
        // that had a focus (not a Traverse) is sampled: |facing - direction to the soldier|.
        static void Facing(Options o)
        {
            Out.Sub("Facing error to the enemy during a dodge (target: P95 <= 30°)");
            var t = new Table("Preset", "Element", "Dodges", "With a focus", "Mean error", "P95", "Max");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    var errors = new List<double>();
                    int dodges = 0, focused = 0;
                    for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                    {
                        var rng = new DeterministicRandom(seed * 7919 + (int)el * 131);
                        Session s = Start(p, el, o.Fps, Vector3.Zero, rng.Range(-180f, 180f));
                        SimEnemy soldier = s.World.AddEnemy(EnemyTuning.CreateDaoSoldier(), OnCircle(rng.Range(-180f, 180f), rng.Range(2f, 5f)),
                            0f, seed * 31 + 7);
                        if (seed % 2 == 1) s.World.LockOn.ForceLock(soldier);
                        bool sampling = false;
                        s.World.PlayerEvent += e =>
                        {
                            if (e.Type == PlayerEventType.DodgeStarted)
                            {
                                dodges++;
                                // A backstep with the soldier beyond FocusRadius has no focus either (it keeps your facing).
                                bool unfocusedBackstep = e.DodgeKind == DodgeKind.Backstep && !s.World.LockOn.IsLocked
                                    && Directions.Flatten(soldier.Feet - s.Player.Feet).Length() > s.Model.MoveSet.Dodge.FocusRadius;
                                sampling = e.DodgeKind != DodgeKind.Traverse && e.DodgeKind != DodgeKind.AirDash && !unfocusedBackstep;
                                if (sampling) focused++;
                            }
                        };
                        double nextDodge = 0.3;
                        int pressFrames = 0;
                        Vector2 stick = Vector2.Zero;
                        int frames = (int)(20f * o.Fps);
                        for (int f = 0; f < frames && s.Model.IsAlive; f++)
                        {
                            var pad = new Pad();
                            Vector3 toSoldier = Directions.SafeNormalize(Directions.Flatten(soldier.Feet - s.Player.Feet), Vector3.UnitZ);
                            if (s.World.GameTime >= nextDodge)
                            {
                                int k = rng.Range(0, 9);
                                stick = k == 8 ? Vector2.Zero
                                    : s.StickToward(Directions.FromYaw(Directions.YawOf(toSoldier) + 45f * k));
                                pressFrames = Math.Max(2, (int)Math.Round(0.05f * o.Fps)) + 3;
                                nextDodge = s.World.GameTime + rng.Range(0.35f, 0.9f);
                            }
                            if (pressFrames > 0)
                            {
                                pad.Dodge = pressFrames > 3;   // the stick stays past the release (a Punishing dodge goes off then)
                                pad.Move = stick;
                                pressFrames--;
                            }
                            else if (Directions.Flatten(soldier.Feet - s.Player.Feet).Length() > 4.5f) pad.Move = s.StickToward(toSoldier, 0.6f);
                            s.Step(pad);
                            if (sampling && s.Model.State == PlayerState.Dodging)
                                errors.Add(Math.Abs(Angles.Delta(s.Model.FacingYaw, YawTo(s.Player, soldier))));
                        }
                    }
                    t.Row(p, el, dodges, Out.Pct(focused / Math.Max(1.0, dodges)), Out.N(Stats.Mean(errors), 1) + "°",
                        Out.N(Stats.Percentile(errors, 0.95), 1) + "°", Out.N(errors.Count > 0 ? errors.Max() : 0, 1) + "°");
                }
            }
            t.Print();
        }

        // ---------------------------------------------------------------- slip-in

        // The rooted partner at a random distance (1.5-6.5 m, centre to centre) and bearing; stick toward it; dodge. In reach, the dodge
        // stops SlipInStopGap short of its body (target radii + [0.5, 0.7] m); out of reach it travels SlipInMaxDistance.
        static void SlipIn(Options o)
        {
            Out.Sub("Slip-in: where a dodge toward the enemy stops (target: gap 0.5-0.7 m between the bodies, 100 %)");
            var t = new Table("Preset", "Element", "In reach", "Gap in [0.5, 0.7]", "Gap min / max", "Out of reach", "Travel (out of reach)", "Not a slip-in");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    int inReach = 0, inBand = 0, outOfReach = 0, wrongKind = 0;
                    var gaps = new List<double>();
                    var travels = new List<double>();
                    for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                    {
                        var rng = new DeterministicRandom(seed * 104729 + (int)el * 17);
                        for (int trial = 0; trial < 5; trial++)
                        {
                            float bearing = rng.Range(-180f, 180f), distance = rng.Range(1.5f, 6.5f);   // from 0.7 m apart (closer: it stays put)
                            Session s = Start(p, el, o.Fps, Vector3.Zero, rng.Range(-180f, 180f));
                            SimEnemy partner = Partner(s, OnCircle(bearing, distance), seed);
                            DodgeProfile dodge = s.Model.MoveSet.Dodge;
                            float radii = s.Player.Radius + partner.Radius;
                            DodgeKind kind = DodgeKind.Traverse;
                            Vector3 start = s.Player.Feet;
                            s.World.PlayerEvent += e =>
                            {
                                if (e.Type != PlayerEventType.DodgeStarted) return;
                                kind = e.DodgeKind;
                                start = s.Player.Feet;   // (a Punishing dodge goes off on the release: the stick walked you first)
                            };
                            Vector2 stick = s.StickToward(partner.Feet - s.Player.Feet);
                            for (int f = 0; f < 6; f++) s.Step(new Pad { Dodge = f < 3, Move = stick });   // held past the release (Punishing)
                            for (int f = 0; f < 120 && s.Model.State == PlayerState.Dodging; f++) s.Step(new Pad());
                            if (kind != DodgeKind.SlipIn) { wrongKind++; continue; }
                            float gap = Directions.Flatten(partner.Feet - s.Player.Feet).Length() - radii;
                            bool reachable = distance - radii - dodge.SlipInStopGap <= dodge.SlipInMaxDistance - 0.02f;
                            if (reachable)
                            {
                                inReach++;
                                gaps.Add(gap);
                                if (gap >= 0.5f - 1e-3f && gap <= 0.7f + 1e-3f) inBand++;
                            }
                            else
                            {
                                outOfReach++;
                                travels.Add(Directions.Flatten(s.Player.Feet - start).Length());
                            }
                        }
                    }
                    t.Row(p, el, inReach, Out.Pct(inBand / Math.Max(1.0, inReach)),
                        gaps.Count > 0 ? Out.N(gaps.Min(), 2) + " / " + Out.N(gaps.Max(), 2) + " m" : "-", outOfReach,
                        travels.Count > 0 ? Out.N(travels.Average(), 2) + " m" : "-", wrongKind);
                }
            }
            t.Print();
        }

        // ---------------------------------------------------------------- spam

        // Dodge pressed as often as the spammer likes (every other frame, or every 0.10-0.25 s) for 3 s, stick neutral,
        // toward, away from or beside the rooted partner 3 m ahead, and with nobody there. The worst case over the press
        // intervals and stick directions is shown: the share of the 3 s spent invulnerable and the longest unbroken stretch.
        static void Spam(Options o)
        {
            Out.Sub("Dodge spam: invulnerable share over 3 s, worst case over press interval and stick (target: <= 60 % every element)");
            var t = new Table("Preset", "Element", "30 fps", "60 fps", "144 fps", "Longest i-frame stretch", "Worst case at 60 fps");
            float[] intervals = { 0f, 0.10f, 0.15f, 0.18f, 0.20f, 0.25f };
            string[] sticks = { "neutral", "toward", "away", "side", "alone" };
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    var cells = new List<object> { p, el };
                    double longest = 0;
                    string worst60 = "";
                    foreach (float fps in new[] { 30f, 60f, 144f })
                    {
                        double worst = 0;
                        foreach (float interval in intervals)
                        {
                            foreach (string stick in sticks)
                            {
                                (double share, double stretch) = SpamTrial(p, el, fps, interval, stick);
                                longest = Math.Max(longest, stretch);
                                if (share <= worst) continue;
                                worst = share;
                                if (fps == 60f) worst60 = (interval > 0f ? Out.N(interval, 2) + " s" : "every other frame") + ", " + stick;
                            }
                        }
                        cells.Add(Out.Pct(worst));
                    }
                    cells.Add(Out.N(longest, 2) + " s");
                    cells.Add(worst60);
                    t.Row(cells.ToArray());
                }
            }
            t.Print();
        }

        static (double share, double stretch) SpamTrial(Preset p, ElementId el, float fps, float interval, string stickCase)
        {
            Session s = Start(p, el, fps, Vector3.Zero);
            SimEnemy partner = stickCase == "alone" ? null : Partner(s, new Vector3(0f, 0f, 3f), 1);
            float dt = 1f / fps;
            int frames = (int)Math.Round(3f * fps);
            int every = interval > 0f ? Math.Max(2, (int)Math.Round(interval * fps)) : 2;
            double invulnerable = 0, stretch = 0, longest = 0;
            for (int f = 0; f < frames; f++)
            {
                Vector3 to = partner != null ? Directions.SafeNormalize(Directions.Flatten(partner.Feet - s.Player.Feet), Vector3.UnitZ) : Vector3.UnitZ;
                Vector3 dir = stickCase switch
                {
                    "toward" => to,
                    "away" => -to,
                    "side" => Directions.RightFromYaw(Directions.YawOf(to)),
                    "alone" => Vector3.UnitZ,
                    _ => Vector3.Zero
                };
                s.Step(new Pad { Dodge = f % every == 0, Move = s.StickToward(dir) });
                if (s.Model.IsInvulnerable)
                {
                    invulnerable += dt;
                    stretch += dt;
                    longest = Math.Max(longest, stretch);
                }
                else stretch = 0;
            }
            return (invulnerable / (frames * dt), longest);
        }

        // ---------------------------------------------------------------- exit speed

        // A dodge with the stick held the whole way (alone: a Traverse; the partner ahead: a side-slip with the stick to the
        // side, an evade-out with it back). Time from the dodge's last frame until the player moves at >= 95 % of run speed.
        static void ExitSpeed(Options o)
        {
            Out.Sub("Exit speed: time to run speed after a dodge, stick held (target: <= 0.05 s, Fluid)");
            var t = new Table("Preset", "Element", "Traverse", "Side-slip", "Evade-out", "Speed on the first frame after");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    var cells = new List<object> { p, el };
                    double firstSpeed = double.MaxValue;
                    foreach (string c in new[] { "alone", "side", "away" })
                    {
                        (double time, double speed) = ExitTrial(p, el, o.Fps, c);
                        firstSpeed = Math.Min(firstSpeed, speed);
                        cells.Add(time < 0 ? "never" : Out.N(time, 3) + " s");
                    }
                    cells.Add(Out.N(firstSpeed, 2) + " m/s");
                    t.Row(cells.ToArray());
                }
            }
            t.Print();
            Out.Line("Run speed is " + Out.N(PlayerTuning.CreateFluid().RunSpeed, 1) + " m/s; \"speed on the first frame after\" is the slowest of the three cases.");
        }

        static (double time, double speed) ExitTrial(Preset p, ElementId el, float fps, string stickCase)
        {
            Session s = Start(p, el, fps, Vector3.Zero);
            if (stickCase != "alone") Partner(s, new Vector3(0f, 0f, 3f), 1);
            Vector3 dir = stickCase == "alone" ? Vector3.UnitZ : stickCase == "side" ? Vector3.UnitX : -Vector3.UnitZ;
            float run = s.Model.Tuning.RunSpeed * 0.95f;
            int ended = -1;
            double firstSpeed = 0;
            for (int f = 0; f < (int)(2f * fps); f++)
            {
                s.Step(new Pad { Dodge = f < 2, Move = s.StickToward(dir) });
                bool dodging = s.Model.State == PlayerState.Dodging;
                if (f < 2 || dodging) continue;
                float speed = Directions.Flatten(s.Model.Velocity).Length();
                if (ended < 0)
                {
                    ended = f;
                    firstSpeed = speed;
                }
                if (speed >= run) return ((f - ended) / (double)fps, firstSpeed);
            }
            return (-1, firstSpeed);
        }

        // ---------------------------------------------------------------- the exit: no dead stop

        // Evade-out from the partner 3 m ahead with the stick held back the whole way, and with it let go after the press;
        // and a side-slip with the stick let go. The biggest drop in horizontal speed between two frames from the dodge's
        // last frames to 10 frames after it.
        static void ExitDrop(Options o)
        {
            Out.Sub("Exit: biggest one-frame speed drop across a dodge's end (target: <= run speed, no dead stop)");
            var t = new Table("Preset", "Element", "Evade-out, stick held", "Evade-out, stick let go", "Side-slip, stick let go", "Target");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    float run = PlayerTuning.CreateFluid().RunSpeed;
                    double a = DropTrial(p, el, o.Fps, -Vector3.UnitZ, true);
                    double b = DropTrial(p, el, o.Fps, -Vector3.UnitZ, false);
                    double c = DropTrial(p, el, o.Fps, Vector3.UnitX, false);
                    double worst = Math.Max(a, Math.Max(b, c));
                    t.Row(p, el, Out.N(a, 2) + " m/s", Out.N(b, 2) + " m/s", Out.N(c, 2) + " m/s", Out.Target(worst <= run + 1e-3));
                }
            }
            t.Print();
        }

        static double DropTrial(Preset p, ElementId el, float fps, Vector3 dir, bool hold)
        {
            Session s = Start(p, el, fps, Vector3.Zero);
            Partner(s, new Vector3(0f, 0f, 3f), 1);
            double previous = 0, worst = 0;
            int after = -1;
            for (int f = 0; f < (int)(2f * fps) && after < 10; f++)
            {
                s.Step(new Pad { Dodge = f < 2, Move = hold || f < 2 ? s.StickToward(dir) : Vector2.Zero });
                double speed = Directions.Flatten(s.Model.Velocity).Length();
                if (f >= 2 && s.Model.State != PlayerState.Dodging) after++;
                if (after >= 0) worst = Math.Max(worst, previous - speed);
                previous = speed;
            }
            return worst;
        }

        // ---------------------------------------------------------------- evade-out, stop, walk back in (J6-02)

        // Evade out from the rooted partner, let go and stand a moment, then walk (and run) straight back at it, with the
        // game's procedural animator on the body. The gait must face the way it goes: no 'strafe' key while moving within
        // 30 degrees of the facing, and never 4+ frames with both feet 0.1 m off the floor (it used to crab sideways, feet
        // 0.93 m apart and off the floor for 13-17 frames, on every in-and-out).
        static void WalkBackIn(Options o)
        {
            Out.Sub("Evade-out, stop, walk back in: the gait faces the way it goes (target: no strafe crab, feet down; J6-02)");
            var t = new Table("Preset", "Element", "Strafe frames moving straight", "Longest both-feet-up run", "Widest stance across (m)", "Target");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    (int strafe, int lift, double spread) = WalkBackInTrial(p, el, o.Fps);
                    t.Row(p, el, strafe, lift + " frames", Out.N(spread, 2), Out.Target(strafe == 0 && lift < 4 && spread <= 0.6));
                }
            }
            t.Print();
        }

        static (int strafe, int lift, double spread) WalkBackInTrial(Preset p, ElementId el, float fps)
        {
            Session s = Start(p, el, fps, Vector3.Zero);
            SimEnemy partner = Partner(s, new Vector3(0f, 0f, 2.5f), 1);
            var feed = new PlayerAnimationFeed();
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create();
            var animator = new FighterAnimator(PoseLibrary.Default, skeleton);
            var fk = new ForwardKinematics(skeleton);
            s.World.PlayerEvent += e => feed.OnEvent(e);
            int strafe = 0, run = 0, longest = 0;
            double spread = 0;
            Vector3 last = s.Player.Feet;
            int total = (int)(2.4f * fps);
            for (int f = 0; f < total; f++)
            {
                Vector3 toward = Directions.Flatten(partner.Feet - s.Player.Feet);
                Pad pad;
                if (f < 2) pad = new Pad { Dodge = true, Move = s.StickToward(-toward) };          // evade out
                else if (f < (int)(0.9f * fps)) pad = new Pad();                                  // stop, stand
                else pad = new Pad { Move = s.StickToward(toward) };                               // walk back in
                s.Step(pad);
                animator.Update(feed.Build(s.Model, s.Dt, true, s.Player.Feet, partner.Feet + new Vector3(0f, 1.2f, 0f)));
                fk.Compute(animator.Pose, Vector3.Zero, 0f);
                Vector3 travel = Directions.Flatten(s.Player.Feet - last);
                last = s.Player.Feet;
                bool loco = (s.Model.State == PlayerState.Locomotion || s.Model.State == PlayerState.Sprinting) && s.Model.IsGrounded;
                if (!loco || f < (int)(0.9f * fps)) continue;
                float speed = travel.Length() / s.Dt;
                // Facing-relative: the FK body is built facing +Z.
                Vector3 local = Vector3.Transform(travel, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -s.Player.Yaw * (float)Math.PI / 180f));
                bool straight = speed > 1f && local.Z > 0f && Math.Abs(local.X) <= local.Z * (float)Math.Tan(30.0 * Math.PI / 180.0);
                string key = animator.LocomotionCue.Key;
                if (straight && key == AnimationKeys.Strafe) strafe++;
                Vector3 lf = fk[BodyJoint.LeftFoot], rf = fk[BodyJoint.RightFoot];
                if (straight && (key == AnimationKeys.Run || key == AnimationKeys.Strafe)) spread = Math.Max(spread, Math.Abs(lf.X - rf.X));
                float lowest = Math.Min(Math.Min(lf.Y - 0.08f, rf.Y - 0.08f), Math.Min(fk[BodyJoint.LeftToes].Y - 0.02f, fk[BodyJoint.RightToes].Y - 0.02f));
                run = lowest > 0.1f ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
            return (strafe, longest, spread);
        }

        // ---------------------------------------------------------------- side-slip: a circle round the enemy

        // From 1.1 m (point blank) and 2.5 m (centre to centre), two side-slips in a row to the right as you face the rooted
        // partner. Every dodging frame: |distance to it - the circle's radius| (the start distance, or the closest a
        // slip-in stops if that is further).
        static void SideSlipCircle(Options o)
        {
            Out.Sub("Side-slip: the distance to the enemy while circling it, two in a row (target: within 0.3 m)");
            var t = new Table("Preset", "Element", "From 1.1 m", "From 2.5 m", "Degrees round (1.1 m, two slips)", "Target");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    (double near, double round) = SlipTrial(p, el, o.Fps, 1.1f);
                    (double far, _) = SlipTrial(p, el, o.Fps, 2.5f);
                    t.Row(p, el, Out.N(near, 2) + " m", Out.N(far, 2) + " m", Out.N(round, 0) + "°", Out.Target(Math.Max(near, far) <= 0.3));
                }
            }
            t.Print();
        }

        static (double error, double degrees) SlipTrial(Preset p, ElementId el, float fps, float distance)
        {
            Session s = Start(p, el, fps, Vector3.Zero);
            SimEnemy partner = Partner(s, new Vector3(0f, 0f, distance), 1);
            DodgeProfile dodge = s.Model.MoveSet.Dodge;
            float radius = Math.Max(distance, s.Player.Radius + partner.Radius + dodge.SlipInStopGap);
            double worst = 0;
            Vector3 startOffset = s.Player.Feet - partner.Feet;
            for (int n = 0; n < 2; n++)
            {
                Vector3 to = Directions.Flatten(partner.Feet - s.Player.Feet);
                Vector3 right = Directions.RightFromYaw(Directions.YawOf(to));
                int guard = 0;
                // Punishing dodges on release: hold the button two frames with the stick to the side, then let go.
                s.Step(new Pad { Dodge = true, Move = s.StickToward(right) });
                s.Step(new Pad { Dodge = true, Move = s.StickToward(right) });
                s.Step(new Pad { Move = s.StickToward(right) });
                while (s.Model.State == PlayerState.Dodging && guard++ < 120)
                {
                    s.Step(new Pad());
                    worst = Math.Max(worst, Math.Abs(Directions.Flatten(partner.Feet - s.Player.Feet).Length() - radius));
                }
                for (int i = 0; i < (int)(0.15f * fps); i++) s.Step(new Pad());
            }
            Vector3 endOffset = s.Player.Feet - partner.Feet;
            double degrees = Math.Abs(Angles.Delta(Directions.YawOf(Directions.Flatten(startOffset)), Directions.YawOf(Directions.Flatten(endOffset))));
            return (worst, degrees);
        }

        // ---------------------------------------------------------------- the string across a dodge, zip strike or ability

        // X, X on the beat, then (at a random moment from hit 2's strike until the string would lapse) a dodge away from the
        // rooted partner, a zip strike or an ability (LB + Y); then X at a random moment once it has done its work (a dodge:
        // once the dash is over, before its strike grace runs out; the others: from their strike's end until three quarters
        // of StringMemoryAfterAction after they finish; a press earlier than that expires in the buffer, by design). The string's next move must
        // be hit 3 (ChainIndex 2); after the dodge it must be the dodge strike, and it must land.
        static void StringAcross(Options o)
        {
            Out.Sub("The string across a dodge, zip strike or ability (target: 100 %; the dodge strike lands after the evade-out)");
            var t = new Table("Preset", "Element", "Dodge (evade-out) then X", "Evade-out distance", "Dodge strike: body gap as it strikes",
                "Zip strike then X", "Ability then X");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    var cells = new List<object> { p, el };
                    var evades = new List<double>();
                    var gaps = new List<double>();
                    foreach (string via in new[] { "dodge", "zip", "ability" })
                    {
                        int ok = 0, n = 0;
                        var failures = new Dictionary<string, int>();
                        for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                        {
                            string why = StringTrial(p, el, via, seed, o.Fps, out double evade, out double strikeGap);
                            n++;
                            if (via == "dodge" && evade > 0) evades.Add(evade);
                            if (via == "dodge" && strikeGap >= 0) gaps.Add(strikeGap);
                            if (why == null) ok++;
                            else failures[why] = failures.TryGetValue(why, out int c) ? c + 1 : 1;
                        }
                        string cell = Out.Pct(ok / (double)n);
                        if (failures.Count > 0) cell += " (" + string.Join(", ", failures.Select(kv => kv.Key + " x" + kv.Value)) + ")";
                        cells.Add(cell);
                        if (via == "dodge")
                        {
                            cells.Add(evades.Count > 0 ? Out.N(evades.Min(), 2) + "-" + Out.N(evades.Max(), 2) + " m" : "-");
                            cells.Add(gaps.Count > 0
                                ? Out.N(gaps.Min(), 2) + "-" + Out.N(gaps.Max(), 2) + " m " + Out.Target(gaps.Max() <= 0.8)
                                : "-");
                        }
                    }
                    t.Row(cells.ToArray());
                }
            }
            t.Print();
        }

        // Null = the string went on as it should; otherwise what went wrong.
        static string StringTrial(Preset p, ElementId el, string via, int seed, float fps, out double evade, out double strikeGap)
        {
            double evaded = 0;
            double gapAtStrike = -1;
            var rng = new DeterministicRandom(seed * 7907 + (int)el * 53 + via.Length * 11 + (p == Preset.Punishing ? 5 : 0));
            Session s = Start(p, el, fps, Vector3.Zero);
            SimEnemy partner = Partner(s, new Vector3(0f, 0f, 2f), seed);
            s.World.LockOn.SnapBehindPlayer();
            double lightAt = 0, viaAt = -1, light2At = -1, dodgeHitBy = -1;
            int stage = 0;          // 0: hit 1 / 2; 1: waiting for the interruption; 2: waiting for hit 3; 3: waiting for the dodge strike to land
            string result = "never got there", fault = null;
            Vector3 dodgeFrom = Vector3.Zero;
            int dodgeStrikeId = 0;
            double now() => s.World.GameTime;
            s.World.PlayerEvent += e =>
            {
                bool stringMove = e.Type == PlayerEventType.AttackStarted && (e.AttackKind == PlayerAttackKind.Light
                    || e.AttackKind == PlayerAttackKind.DodgeStrike);
                float rate = Math.Max(0.01f, e.PlaybackRate);
                if (stage == 0 && stringMove && e.ChainIndex == 0) lightAt = now() + e.Move.ActiveStart / rate + rng.Range(-0.03f, 0.03f);
                else if (stage == 0 && stringMove && e.ChainIndex == 1)
                {
                    stage = 1;
                    // Any time from the strike until the string would lapse (its combo window, if that reaches past the end).
                    float live = Math.Max(e.Move.TotalDuration, e.Move.ComboWindowEnd) / rate;
                    float earliest = e.Move.ActiveStart / rate + 0.02f;
                    // A dodge press only keeps for the input buffer, so it must come within that of the move's dodge cancel
                    // (late in Punishing, e.g. Earth's Tiger Claw Rake at 0.53 s): earlier, no dodge happens at all and the
                    // trial would measure a dropped press instead of the string across a dodge.
                    if (via == "dodge")
                        earliest = Math.Max(earliest, e.Move.DodgeCancelAt / rate - s.Model.Tuning.InputBufferWindow + 0.02f);
                    viaAt = now() + rng.Range(earliest, live - 0.02f);
                }
                else if (stage == 1 && via == "dodge" && e.Type == PlayerEventType.DodgeStarted)
                {
                    if (e.DodgeKind != DodgeKind.EvadeOut) fault = "dodge was a " + e.DodgeKind;
                    dodgeFrom = s.Player.Feet;
                    DodgeProfile d = s.Model.MoveSet.Dodge;
                    light2At = now() + rng.Range(d.Duration, d.TotalDuration + s.Model.Tuning.DodgeStrikeGrace * 0.75f);
                    stage = 2;
                }
                else if (stage == 1 && e.Type == PlayerEventType.AttackStarted
                         && (via == "zip" ? e.AttackKind == PlayerAttackKind.ZipStrike : via == "ability" && e.AttackKind == PlayerAttackKind.Ability))
                {
                    light2At = now() + rng.Range(e.Move.ActiveEnd, e.Move.TotalDuration + s.Model.Tuning.StringMemoryAfterAction * 0.75f);
                    stage = 2;
                }
                else if (e.Type == PlayerEventType.DodgeEnded && via == "dodge" && stage >= 2 && evaded == 0)
                    evaded = Directions.Flatten(s.Player.Feet - dodgeFrom).Length();
                else if (stage == 2 && stringMove)
                {
                    if (e.ChainIndex != 2) result = "hit " + (e.ChainIndex + 1) + " came out";
                    else if (via == "dodge" && e.AttackKind != PlayerAttackKind.DodgeStrike) result = "not a dodge strike";
                    else if (via == "dodge")
                    {
                        stage = 3;
                        dodgeStrikeId = e.AttackId;
                        dodgeHitBy = now() + 1.5;
                        result = "dodge strike missed";
                        return;
                    }
                    else result = null;
                    stage = 4;
                }
                else if (stage == 3 && e.Type == PlayerEventType.AttackActiveStart && e.AttackId == dodgeStrikeId && gapAtStrike < 0)
                {
                    gapAtStrike = Directions.Flatten(partner.Feet - s.Player.Feet).Length() - s.Player.Radius - partner.Radius;
                }
                else if (stage == 3 && e.Type == PlayerEventType.ComboHit && e.AttackKind == PlayerAttackKind.DodgeStrike)
                {
                    result = null;
                    stage = 4;
                }
                else if (stage == 1 && e.Type == PlayerEventType.ComboEnded) result = "combo ended";
            };
            int press = Math.Max(2, (int)Math.Round(0.05f * fps));
            int lightFrames = 0, viaFrames = 0, stickFrames = 0;
            Vector3 away = Vector3.Zero;
            int frames = (int)(6f * fps);
            for (int f = 0; f < frames && stage < 4; f++)
            {
                var pad = new Pad();
                double next = now() + 1.0 / fps;    // one frame of input lag: a press made now lands on the next frame
                if (f == 0 || (lightAt > 0 && next >= lightAt)) { lightFrames = press; lightAt = -1; }
                if (light2At > 0 && next >= light2At) { lightFrames = press; light2At = -1; }
                if (viaAt > 0 && next >= viaAt)
                {
                    viaFrames = press;
                    stickFrames = (int)fps;     // held until the dodge goes (a Punishing dodge goes off when the button comes up, or at the cancel point)
                    away = s.Player.Feet - partner.Feet;
                    viaAt = -1;
                }
                if (lightFrames > 0) { pad.Light = true; lightFrames--; }
                if (viaFrames > 0)
                {
                    viaFrames--;
                    if (via == "dodge") pad.Dodge = true;
                    else if (via == "zip") pad.ZipStrike = true;
                    else pad.AbilityNorth = true;
                }
                if (stickFrames > 0 && via == "dodge" && stage == 1)
                {
                    stickFrames--;
                    pad.Move = s.StickToward(away);
                }
                s.Step(pad);
                if (stage == 3 && now() > dodgeHitBy) break;
            }
            evade = evaded;
            strikeGap = gapAtStrike;
            return fault ?? (stage == 4 ? result : result ?? "never got there");
        }
    }
}
