using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    public static class AbilitiesScenario
    {
        public static void Run(Options o)
        {
            Out.Heading("Abilities");
            FaJin(o);
            FaJinAfterPerfectDodge(o);
            DamageLoops(o);
            StaminaAtZero(o);
            FireBlast(o);
            Momentum(o);
            PerfectDodge(o);
            StaggerImmunity(o);
        }

        // ---------------------------------------------------------------- fa jin
        static void FaJin(Options o)
        {
            Out.Sub("Fa jin: hold length (frames) vs charge tier");
            var tiers = new SortedDictionary<ChargeTier, List<int>>();
            for (int n = 1; n <= 80; n++)
            {
                int hold = n;
                Trace tr = Trace.Run(Preset.Fluid, f => new Pad { Heavy = f < hold }, hold + 5, null, o.Fps);
                var a = tr.Events.FirstOrDefault(e => e.e.Type == PlayerEventType.AttackStarted);
                if (a.e.Move == null) continue;
                if (!tiers.TryGetValue(a.e.ChargeTier, out var list)) tiers[a.e.ChargeTier] = list = new List<int>();
                list.Add(hold);
            }
            var t = new Table("Tier", "Held for (frames)", "Seconds");
            foreach (var kv in tiers) t.Row(kv.Key, kv.Value.Min() + "–" + kv.Value.Max(), Out.N(kv.Value.Min() / o.Fps, 3) + "–" + Out.N(kv.Value.Max() / o.Fps, 3));
            t.Print();

            Out.Sub("Fa jin success rate with human timing (" + Math.Max(o.Seeds * 25, 1000) + " trials each)");
            Out.Line("Rhythm = the player times the hold from the press, aiming at 0.80 s, with a normal timing error (σ). "
                     + "Flash = the player waits for the sweet-spot flash and releases after a visual reaction time (plus ~1 frame of display latency); Ready cue = the same, reacting to the earlier get-ready cue. "
                     + "Real humans: visual reaction ≈ 0.20–0.25 s (σ ≈ 0.03–0.05 s); timing a learned 0.8 s rhythm ≈ σ 0.04–0.08 s.");
            int trials = Math.Max(o.Seeds * 25, 1000);
            var t2 = new Table("Strategy", "Fa jin", "Released too early (Partial)", "Too late (Charged)");
            var human = new Human(o.Seed * 7919);
            foreach (float sd in new[] { 0.03f, 0.05f, 0.08f, 0.12f })
                t2.Row(RateRow("Rhythm, aim 0.80 s, σ " + Out.N(sd, 2) + " s", trials, () => 0.80f + human.Normal(0f, sd), o.Fps));
            ChargeSettings charge = ElementMoveSet.CreateFireFluid().Charge;
            float flashAt = charge.SweetSpotStart, cueAt = charge.SweetSpotStart - charge.ReadyCueLead;
            foreach (float mean in new[] { 0.17f, 0.20f, 0.25f })
                t2.Row(RateRow("Flash reaction (" + Out.N(flashAt, 2) + " s), mean " + Out.N(mean, 2) + " s (σ 0.03)", trials, () => flashAt + 1f / o.Fps + Math.Max(0.1f, human.Normal(mean, 0.03f)), o.Fps));
            // The ChargeReadyCue / HUD "get ready" mark comes ReadyCueLead before the sweet spot.
            foreach (float mean in new[] { 0.17f, 0.18f, 0.20f, 0.22f, 0.25f, 0.30f, 0.35f })
                t2.Row(RateRow("Ready-cue reaction (" + Out.N(cueAt, 2) + " s), mean " + Out.N(mean, 2) + " s (σ 0.03)", trials, () => cueAt + 1f / o.Fps + Math.Max(0.1f, human.Normal(mean, 0.03f)), o.Fps));
            t2.Print();
        }

        static object[] RateRow(string name, int trials, Func<float> holdSeconds, float fps)
        {
            int fajin = 0, early = 0, late = 0;
            // Tier by frame count (the model's own quantisation, measured above): build a lookup once.
            var tierFor = TierLookup(fps);
            for (int i = 0; i < trials; i++)
            {
                int frames = Math.Max(1, (int)Math.Round(holdSeconds() * fps));
                ChargeTier tier = frames < tierFor.Length ? tierFor[frames] : ChargeTier.Charged;
                if (tier == ChargeTier.FaJin) fajin++;
                else if (tier == ChargeTier.Charged) late++;
                else early++;
            }
            return new object[] { name, Out.Pct(fajin / (double)trials), Out.Pct(early / (double)trials), Out.Pct(late / (double)trials) };
        }

        static ChargeTier[] tierCache;
        static ChargeTier[] TierLookup(float fps)
        {
            if (tierCache != null) return tierCache;
            var arr = new ChargeTier[100];
            for (int n = 1; n < arr.Length; n++)
            {
                int hold = n;
                Trace tr = Trace.Run(Preset.Fluid, f => new Pad { Heavy = f < hold }, hold + 5, null, fps);
                var a = tr.Events.FirstOrDefault(e => e.e.Type == PlayerEventType.AttackStarted);
                arr[n] = a.e.Move != null ? a.e.ChargeTier : ChargeTier.Charged;
            }
            tierCache = arr;
            return arr;
        }


        // ---------------------------------------------------------------- fa jin inside the perfect-dodge slow motion
        static void FaJinAfterPerfectDodge(Options o)
        {
            Out.Sub("Fa jin as a counter: heavy held for a fixed real time, starting right after a perfect dodge (slow motion 0.35x for 0.35 s)");
            var t = new Table("Held for (real time)", "Tier from idle", "Tier right after a perfect dodge");
            foreach (float hold in new[] { 0.8f, 0.9f, 1.0f, 1.1f })
            {
                var cells = new List<object> { Out.N(hold, 2) + " s" };
                foreach (bool afterPerfect in new[] { false, true })
                {
                    var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty(), camera: false);
                    if (afterPerfect) s.World.Time.SlowMotion(0.35f, 0.35f);
                    int holdFrames = (int)Math.Round(hold * o.Fps);
                    ChargeTier tier = ChargeTier.None;
                    s.World.PlayerEvent += e => { if (e.Type == PlayerEventType.AttackStarted && e.AttackKind == PlayerAttackKind.Heavy) tier = e.ChargeTier; };
                    for (int f = 0; f < holdFrames + 30; f++) s.Step(new Pad { Heavy = f < holdFrames });
                    cells.Add(tier);
                }
                t.Row(cells.ToArray());
            }
            t.Print();
            Out.Line("Since 60cb8ee the charge clock runs on PlayerWorldState.RealDeltaTime, so the two columns should match.");
        }

        // ---------------------------------------------------------------- stamina at zero
        static void StaminaAtZero(Options o)
        {
            Out.Sub("What a move really costs once stamina is empty (any stamina above 0 lets a move start)");
            Out.Line("Each move is spammed for 8 s starting from 0 stamina, against a planted dummy. 'Full-stamina rate' = the same spam with stamina never running out.");
            var t = new Table("Preset", "Move", "Moves per 8 s from empty", "Moves per 8 s with full stamina", "Rate kept when empty");
            foreach (Preset p in o.Presets)
            {
                foreach (var (name, button) in new[] { ("Light (chain)", Button.Light), ("Heavy tap", Button.Heavy), ("Fire Blast", Button.Skill), ("Dodge", Button.Dodge) })
                {
                    int Count(bool infinite)
                    {
                        var s = new Session(p, o.Fps, SimLevel.Empty(), camera: false);
                        EnemyTuning dt = EnemyTuning.CreateSparringDummy();
                        dt.MaxHealth = 1e7f;
                        s.World.AddEnemy(dt, new Vector3(0f, 0f, 1.8f), 180f, 1);
                        if (infinite) s.Model.Tuning.MaxStamina = 1e6f;
                        if (infinite) s.Model.Respawn(0f);
                        // Empty the bar first (mash dodge), then start counting.
                        int guard = 0;
                        while (!infinite && s.Model.Stamina > 0f && guard++ < 2000) s.Step(new Pad { Dodge = guard % 4 < 2, Move = new Vector2(0.1f, 0.9f) });
                        while (s.Model.State != PlayerState.Locomotion && guard++ < 4000) s.Step(new Pad());
                        int n = 0;
                        s.World.PlayerEvent += e =>
                        {
                            if (button == Button.Dodge && e.Type == PlayerEventType.DodgeStarted) n++;
                            else if (button != Button.Dodge && e.Type == PlayerEventType.AttackStarted) n++;
                        };
                        for (int f = 0; f < (int)(8 * o.Fps); f++)
                        {
                            var pad = new Pad();
                            Trace.Set(ref pad, button, f % 4 < 2);
                            if (button == Button.Dodge) pad.Move = new Vector2(0f, 1f);
                            s.Step(pad);
                        }
                        return n;
                    }
                    int empty = Count(false), full = Count(true);
                    t.Row(p, name, empty, full, Out.Pct(empty / (double)Math.Max(1, full)));
                }
            }
            t.Print();
        }

        // ---------------------------------------------------------------- damage loops (plunge spam etc.)
        static void DamageLoops(Options o)
        {
            Out.Sub("Damage loops against planted dummies (no enemy pressure): 10 s of each pattern, Fluid");
            Out.Line("One dummy 1.8 m ahead, or three dummies around the player at 1.8 m. Damage is before Momentum growth is removed, so the "
                     + "Momentum column shows how much each loop builds.");
            var t = new Table("Pattern", "Targets", "Damage in 10 s", "Damage / stamina", "Hits", "Stamina empty", "Avg Momentum");
            var patterns = new (string name, Func<Session, int, Pad> script)[]
            {
                ("Light mash (3-hit chain)", (s, f) => new Pad { Light = f % 6 < 2, Move = Aim(s) }),
                ("Jump + plunge spam", (s, f) => PlungeSpam(s, f)),
                ("Heavy tap spam", (s, f) => new Pad { Heavy = f % 8 < 1, Move = Aim(s) }),
                ("Fa jin (hold 0.8 s) loop", (s, f) => FaJinLoop(s, f)),
                ("Fire Blast spam", (s, f) => new Pad { Skill = f % 6 < 2, Move = Aim(s) }),
            };
            foreach (var pat in patterns)
            {
                foreach (int targets in new[] { 1, 3 })
                {
                    var s = new Session(Preset.Fluid, o.Fps, camera: false, playerYaw: 0f);
                    var dummies = new List<SimEnemy>();
                    for (int i = 0; i < targets; i++)
                    {
                        float ang = i * 360f / targets;
                        Vector3 pos = Directions.FromYaw(ang) * 1.8f;
                        EnemyTuning dt = EnemyTuning.CreateSparringDummy();
                        dt.MaxHealth = 100000f;
                        dt.HealthRefillDelay = 0f;
                        dummies.Add(s.World.AddEnemy(dt, pos, ang + 180f, i + 1));
                    }
                    float staminaSpent = 0f, lastSt = s.Model.Stamina;
                    for (int f = 0; f < (int)(10 * o.Fps); f++)
                    {
                        s.Step(pat.script(s, f));
                        float st = s.Model.Stamina;
                        if (st < lastSt) staminaSpent += lastSt - st;
                        lastSt = st;
                    }
                    Metrics m = s.World.Metrics;
                    t.Row(pat.name, targets, Out.N(m.DamageDealt, 0), Out.N(m.DamageDealt / Math.Max(1f, staminaSpent), 2), m.HitsLanded,
                        Out.N(m.StaminaEmptySeconds, 1) + " s", Out.N(m.MomentumAverage, 0));
                }
            }
            t.Print();
        }

        static Vector2 Aim(Session s)
        {
            return Vector2.Zero;
        }

        static Pad PlungeSpam(Session s, int f)
        {
            PlayerCombatModel m = s.Model;
            var pad = new Pad();
            if (m.State == PlayerState.Locomotion) pad.Jump = f % 2 == 0;
            if (m.State == PlayerState.Airborne) pad.Light = f % 2 == 0;
            return pad;
        }

        static Pad FaJinLoop(Session s, int f)
        {
            PlayerCombatModel m = s.Model;
            var pad = new Pad();
            if (m.State == PlayerState.Charging) pad.Heavy = m.ChargeTime < 0.8f - 1e-3f;
            else if (m.State == PlayerState.Locomotion) pad.Heavy = true;
            return pad;
        }

        // ---------------------------------------------------------------- fire blast
        static void FireBlast(Options o)
        {
            Out.Sub("Fire Blast vs a Crossbowman (locked on, player standing still, 30 casts per row)");
            Out.Line("The blast flies at 30 m/s straight at where the target's chest was at launch (no lead).");
            var t = new Table("Distance", "Target behaviour", "Hit rate", "Flight time");
            foreach (float d in new[] { 6f, 10f, 14f, 20f, 25f })
            {
                foreach (bool strafing in new[] { false, true })
                {
                    int hits = 0, casts = 0;
                    for (int trial = 0; trial < 30; trial++)
                    {
                        var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty());
                        EnemyTuning cb = EnemyTuning.CreateCrossbowman();
                        cb.AttackIntervalMin = cb.AttackIntervalMax = 1000f;   // never shoots back
                        cb.AggroRange = 40f;                                  // aware at every distance tested
                        cb.KeepAwayMin = d - 3f;
                        cb.KeepAwayMax = d + 3f;
                        if (!strafing) cb.StrafeSpeed = 0f;
                        cb.CircleTimeMin = 0.4f;
                        cb.CircleTimeMax = 1.2f;
                        SimEnemy e = s.World.AddEnemy(cb, new Vector3(0f, 0f, d), 180f, 100 + trial);
                        s.World.LockOn.ForceLock(e);
                        int castFrame = 60 + trial % 30;
                        float before = e.Brain.Health;
                        for (int f = 0; f < castFrame + 90; f++) s.Step(new Pad { Skill = f == castFrame });
                        casts++;
                        if (e.Brain.Health < before) hits++;
                    }
                    t.Row(d + " m", strafing ? "strafing 1.6 m/s" : "standing", Out.Pct(hits / (double)casts), Out.N(d / 30f, 2) + " s");
                }
            }
            t.Print();
        }

        // ---------------------------------------------------------------- momentum
        static void Momentum(Options o)
        {
            Out.Sub("Momentum: build-up, decay and the back-off drain (Fluid, dummy 1.8 m ahead)");
            var t = new Table("Pattern", "Momentum after", "Damage multiplier");
            {
                var s = DummySession(o, out SimEnemy d);
                for (int f = 0; f < (int)(6 * o.Fps); f++) s.Step(new Pad { Light = f % 6 < 2 });
                t.Row("6 s of light mashing", Out.N(s.Model.Momentum, 0), "x" + Out.N(s.Model.MomentumMultiplier, 2));
                float peak = s.Model.Momentum;
                for (int f = 0; f < (int)(1.5 * o.Fps); f++) s.Step(new Pad());
                t.Row("...then 1.5 s idle", Out.N(s.Model.Momentum, 0), "x" + Out.N(s.Model.MomentumMultiplier, 2));
                for (int f = 0; f < (int)(2.0 * o.Fps); f++) s.Step(new Pad());
                t.Row("...then 2.0 s more idle", Out.N(s.Model.Momentum, 0), "x" + Out.N(s.Model.MomentumMultiplier, 2));
            }
            foreach (bool locked in new[] { true, false })
            {
                var s = DummySession(o, out SimEnemy d);
                if (locked) s.World.LockOn.ForceLock(d);
                for (int f = 0; f < (int)(6 * o.Fps); f++) s.Step(new Pad { Light = f % 6 < 2 });
                int settle = 0;
                while (s.Model.State != PlayerState.Locomotion && settle < 120) { s.Step(new Pad()); settle++; }
                float peak = s.Model.Momentum;
                for (int f = 0; f < (int)(1.0 * o.Fps); f++) s.Step(new Pad { Move = s.StickToward(-Directions.Flatten(d.Feet - s.Player.Feet)) });
                t.Row("6 s mashing, wait for the move to end (" + settle + " f), then walk away 1.0 s (" + (locked ? "locked on" : "not locked on") + ")", Out.N(s.Model.Momentum, 0) + " (from " + Out.N(peak, 0) + ")", "x" + Out.N(s.Model.MomentumMultiplier, 2));
            }
            {
                var s = DummySession(o, out SimEnemy d);
                s.World.LockOn.ForceLock(d);
                for (int f = 0; f < (int)(6 * o.Fps); f++) s.Step(new Pad { Light = f % 6 < 2 });
                int settle = 0;
                while (s.Model.State != PlayerState.Locomotion && settle < 120) { s.Step(new Pad()); settle++; }
                float peak = s.Model.Momentum;
                for (int f = 0; f < 20; f++) s.Step(new Pad { Dodge = f < 2 });
                t.Row("6 s mashing, then one backstep while locked on", Out.N(s.Model.Momentum, 0) + " (from " + Out.N(peak, 0) + ")", "x" + Out.N(s.Model.MomentumMultiplier, 2));
            }
            {
                // Time for the chain to fill Momentum from zero with nothing in the way.
                var s = DummySession(o, out SimEnemy d);
                int f = 0;
                for (; f < 1200 && s.Model.Momentum < 99.9f; f++) s.Step(new Pad { Light = f % 6 < 2 });
                t.Row("Time to fill Momentum to 100 by light mashing", Out.N(f / o.Fps, 1) + " s", "x" + Out.N(s.Model.MomentumMultiplier, 2));
            }
            t.Print();
        }

        static Session DummySession(Options o, out SimEnemy dummy)
        {
            var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty());
            EnemyTuning dt = EnemyTuning.CreateSparringDummy();
            dt.MaxHealth = 100000f;
            dummy = s.World.AddEnemy(dt, new Vector3(0f, 0f, 1.8f), 180f, 1);
            return s;
        }

        // ---------------------------------------------------------------- perfect dodge windows
        static void PerfectDodge(Options o)
        {
            bool saved = SimEnemy.NotifyStrikes;
            foreach (bool notify in new[] { false, true })
            {
            SimEnemy.NotifyStrikes = notify;
            Out.Sub("Dodge timing vs each Dao Soldier attack: which leads give a perfect dodge (Fluid, "
                    + (notify ? "enemies call NotifyEnemyStrike, as Unity does since the round-2 fixes" : "without NotifyEnemyStrike, as Unity ran at 60cb8ee") + ")");
            Out.Line("The soldier is forced to use one attack; the player stands still (not locked on) at the distance the soldier "
                     + "starts that attack from, and dodges 'lead' frames before the strike's first active frame. "
                     + "P = perfect dodge, e = evaded (i-frames), . = whiffed (out of reach), H = hit.");
            EnemyTuning baseT = EnemyTuning.CreateDaoSoldier();
            string[] dirs = { "back", "side", "toward" };
            int[] leads = { 12, 10, 8, 7, 6, 5, 4, 3, 2, 1, 0 };
            var t = new Table(new[] { "Attack", "Dodge" }.Concat(leads.Select(l => l + "f")).Concat(new[] { "Perfect window" }).ToArray());
            for (int ai = 0; ai < baseT.Attacks.Length; ai++)
            {
                foreach (string dir in dirs)
                {
                    var cells = new List<object> { baseT.Attacks[ai].Move.DisplayName, dir };
                    int perfect = 0;
                    foreach (int lead in leads)
                    {
                        string r = DodgeTrial(o, ai, lead, dir);
                        if (r == "P") perfect++;
                        cells.Add(r);
                    }
                    cells.Add(perfect + " f (" + Out.N(perfect / o.Fps * 1000, 0) + " ms)");
                    t.Row(cells.ToArray());
                }
            }
            t.Print();
            Out.Line("Spec: perfect window 0.12 s (7 f) and i-frames 0.02–0.24 s. Without NotifyEnemyStrike a dodge that carries the player out of "
                     + "the swing's reach before the first active frame gets nothing (the report-01 rule); with it, a strike that would have hit "
                     + "where the dodge started counts.");
            }
            SimEnemy.NotifyStrikes = saved;
        }

        // Returns P, e, ., H for one trial.
        static string DodgeTrial(Options o, int attackIndex, int leadFrames, string dir)
        {
            var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty(), camera: false);
            EnemyTuning t = EnemyTuning.CreateDaoSoldier();
            t.Attacks = new[] { t.Attacks[attackIndex] };
            t.Attacks[0].Cooldown = 0f;
            t.AttackIntervalMin = t.AttackIntervalMax = 0.2f;
            float startDistance = t.Attacks[0].MaxRange + 0.4f - 0.05f;   // centre to centre, just inside its reach
            SimEnemy e = s.World.AddEnemy(t, new Vector3(0f, 0f, 0f), 0f, 7);
            s.Player.Controller.Position = new Vector3(0f, 0f, startDistance);
            int teleFrame = -1, activeFrame = -1;
            string result = null;
            s.World.EnemyEvent += (en, ev) =>
            {
                if (ev.Type == EnemyEventType.TelegraphStarted && teleFrame < 0) teleFrame = s.World.Frame;
                if (ev.Type == EnemyEventType.AttackActiveStart && activeFrame < 0) activeFrame = s.World.Frame;
            };
            int expectedActive = -1;
            for (int f = 0; f < 400 && result == null; f++)
            {
                var pad = new Pad();
                if (teleFrame >= 0 && expectedActive < 0)
                {
                    float startup = t.Attacks[0].Move.Startup;
                    expectedActive = teleFrame + FramesToReach(startup, 1f / o.Fps);
                }
                if (expectedActive >= 0 && f == expectedActive - leadFrames)
                {
                    Vector3 toEnemy = Directions.SafeNormalize(Directions.Flatten(e.Feet - s.Player.Feet), new Vector3(0f, 0f, -1f));
                    Vector3 d = dir == "back" ? -toEnemy : dir == "toward" ? toEnemy : Directions.RightFromYaw(Directions.YawOf(toEnemy));
                    pad.Dodge = true;
                    pad.Move = Session.StickFor(d, 0f);
                }
                if (expectedActive >= 0 && f > expectedActive - leadFrames && f < expectedActive - leadFrames + 12) pad.Move = Vector2.Zero;
                s.Step(pad);
                Metrics m = s.World.Metrics;
                // Decided once the strike is well over: a PerfectDodge event raised inside ReceiveHit (outside the player's
                // Tick) is only delivered on the player's next frame, and a WouldHaveLanded award has no hit outcome at all.
                if (activeFrame >= 0 && f > activeFrame + 40)
                    result = m.PerfectDodges > 0 ? "P" : m.HitsTaken > 0 ? "H" : m.Evades > 0 ? "e" : ".";
            }
            return result ?? "?";
        }

        public static int FramesToReach(float mark, float dt)
        {
            float t = 0f;
            int frames = 0;
            while (t < mark) { t += dt; frames++; }
            return frames;
        }

        // ---------------------------------------------------------------- stagger immunity
        static void StaggerImmunity(Options o)
        {
            Out.Sub("Stagger immunity: a player who never stops attacking a Dao Soldier (Fluid, 20 s, 10 seeds)");
            var t = new Table("Seed", "Soldier staggers", "Player hits landed", "Hits during stagger/immunity", "Soldier attacks started", "Soldier dead at", "Player damage taken");
            for (int seed = o.Seed; seed < o.Seed + 10; seed++)
            {
                var s = new Session(Preset.Fluid, o.Fps, SimLevel.Empty());
                EnemyTuning st = EnemyTuning.CreateDaoSoldier();
                st.MaxHealth = 100000f;   // survive the whole 20 s so the rhythm shows
                SimEnemy e = s.World.AddEnemy(st, new Vector3(0f, 0f, 2.2f), 180f, seed);
                s.World.LockOn.ForceLock(e);
                int attacks = 0;
                s.World.EnemyEvent += (en, ev) => { if (ev.Type == EnemyEventType.TelegraphStarted) attacks++; };
                for (int f = 0; f < (int)(20 * o.Fps); f++)
                {
                    Vector3 to = Directions.Flatten(e.Feet - s.Player.Feet);
                    var pad = new Pad { Light = f % 6 < 2, Move = to.Length() > 2.0f ? s.StickToward(to) : Vector2.Zero };
                    s.Step(pad);
                }
                Metrics m = s.World.Metrics;
                t.Row(seed, m.EnemyStaggers, m.HitsLanded, m.EnemyStaggerImmuneHits, attacks, e.IsAlive ? "alive" : Out.N(m.FirstKillTime, 1) + " s", Out.N(m.DamageTaken, 0));
            }
            t.Print();
            Out.Line("With 1.0 s of stagger + 1.5 s of immunity, a relentless light chain staggers the soldier at most every 2.5 s; in between it swings back.");
        }
    }
}
