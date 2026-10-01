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
    //   exit speed  - how long after a dodge ends, stick held, until you're back at run speed
    //   string      - X X, then a dodge / zip strike / ability, then X: the string's third hit comes out (the dodge strike lands)
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
            StringAcross(o);
        }

        // A session with all four elements, switched to 'element' (a plain switch while free is instant).
        static Session Start(Preset p, ElementId element, float fps, Vector3 playerAt, float playerYaw = 0f)
        {
            var s = new Session(p, fps, SimLevel.Empty(), camera: true, playerAt: playerAt, playerYaw: playerYaw);
            if (element != ElementId.Fire) s.Step(new Pad { Element = element });
            s.Step(new Pad());
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
                                sampling = e.DodgeKind != DodgeKind.Traverse && e.DodgeKind != DodgeKind.AirDash;
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

        // ---------------------------------------------------------------- the string across a dodge, zip strike or ability

        // X, X on the beat, then (at a random moment from hit 2's strike until the string would lapse) a dodge away from the
        // rooted partner, a zip strike or an ability (LB + Y); then X at a random moment once it has done its work (a dodge:
        // once the dash is over, before its strike grace runs out; the others: from their strike's end until three quarters
        // of StringMemoryAfterAction after they finish; a press earlier than that expires in the buffer, by design). The string's next move must
        // be hit 3 (ChainIndex 2); after the dodge it must be the dodge strike, and it must land.
        static void StringAcross(Options o)
        {
            Out.Sub("The string across a dodge, zip strike or ability (target: 100 %; the dodge strike lands after the evade-out)");
            var t = new Table("Preset", "Element", "Dodge (evade-out) then X", "Evade-out distance", "Zip strike then X", "Ability then X");
            foreach (Preset p in o.Presets)
            {
                foreach (ElementId el in Elements)
                {
                    var cells = new List<object> { p, el };
                    var evades = new List<double>();
                    foreach (string via in new[] { "dodge", "zip", "ability" })
                    {
                        int ok = 0, n = 0;
                        var failures = new Dictionary<string, int>();
                        for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                        {
                            string why = StringTrial(p, el, via, seed, o.Fps, out double evade);
                            n++;
                            if (via == "dodge" && evade > 0) evades.Add(evade);
                            if (why == null) ok++;
                            else failures[why] = failures.TryGetValue(why, out int c) ? c + 1 : 1;
                        }
                        string cell = Out.Pct(ok / (double)n);
                        if (failures.Count > 0) cell += " (" + string.Join(", ", failures.Select(kv => kv.Key + " x" + kv.Value)) + ")";
                        cells.Add(cell);
                        if (via == "dodge") cells.Add(evades.Count > 0 ? Out.N(evades.Min(), 2) + "-" + Out.N(evades.Max(), 2) + " m" : "-");
                    }
                    t.Row(cells.ToArray());
                }
            }
            t.Print();
        }

        // Null = the string went on as it should; otherwise what went wrong.
        static string StringTrial(Preset p, ElementId el, string via, int seed, float fps, out double evade)
        {
            double evaded = 0;
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
                    viaAt = now() + rng.Range(e.Move.ActiveStart / rate + 0.02f, live - 0.02f);
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
            return fault ?? (stage == 4 ? result : result ?? "never got there");
        }
    }
}
