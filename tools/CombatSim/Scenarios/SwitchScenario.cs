using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Build 05 element switching (spec 2.6) and MIX (2.5):
    //   denials   - 'chaosswitch' hammers RB + a face button on every other follow-up, cooldown or not, into the passive
    //               partner for 60 s: every switch the cooldown refuses must still continue the string (in the old element)
    //   air cap   - jump, then X and switches on the beat until the air string runs out: the air attacks per jump never
    //               pass the cap, however many elements took part
    //   MIX 4     - 'switcher' (a new element on every beat until all four landed) against the passive partner and in a real
    //               duel with one Dao Soldier: the share of seeds with a MIX 4 finisher
    public static class SwitchScenario
    {
        public static void Run(Options o)
        {
            Out.Heading("Element switching: cooldown denials, the air cap, MIX 4 finishers (" + o.Seeds + " seeds)");
            Denials(o);
            AirCap(o);
            MixFour(o);
        }

        static Session WithPartner(Options o, Preset p, int seed, out SimEnemy partner)
        {
            var s = new Session(p, o.Fps, SimLevel.Empty(), camera: true, playerAt: new Vector3(0f, 0f, -1.5f));
            EnemyTuning t = EnemyTuning.CreateTutorialPartner();
            t.BreakOut.Enabled = false;
            t.WalkSpeed = t.ChaseSpeed = t.StrafeSpeed = t.RetreatSpeed = 0f;
            partner = s.World.AddEnemy(t, new Vector3(0f, 0f, 1f), 180f, seed * 31 + 3);
            partner.Brain.Passive = true;
            s.World.LockOn.SnapBehindPlayer();
            return s;
        }

        static bool IsStringMove(in PlayerEvent e)
        {
            return e.Type == PlayerEventType.AttackStarted
                   && (e.AttackKind == PlayerAttackKind.Light || e.AttackKind == PlayerAttackKind.Air || e.AttackKind == PlayerAttackKind.DodgeStrike);
        }

        // ---------------------------------------------------------------- cooldown denials

        static void Denials(Options o)
        {
            Out.Sub("Switches refused by the cooldown mid-string (target: 0 that drop the string)");
            var t = new Table("Preset", "Switch strikes", "Cooldown denials mid-string", "Continued the string", "Dropped it", "Bar ran dry", "Other denials");
            foreach (Preset p in o.Presets)
            {
                int strikes = 0, denials = 0, continued = 0, dropped = 0, emptyBar = 0, other = 0;
                for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                {
                    Session s = WithPartner(o, p, seed, out _);
                    Bot bot = Bots.Create("chaosswitch");
                    bot.Attach(s, seed * 977 + 5);
                    PlayerEvent last = default;
                    bool haveLast = false;
                    int expect = -1;          // the chain index a refused switch must still give
                    bool emptyWait = false;   // the stamina bar ran dry while waiting for it
                    double expectBy = 0;
                    s.World.PlayerEvent += e =>
                    {
                        if (e.Type == PlayerEventType.ElementSwitched && e.IsSwitchStrike) strikes++;
                        if (e.Type == PlayerEventType.ElementSwitchDenied)
                        {
                            bool midString = haveLast && s.Model.State == PlayerState.Attacking && last.Branch == ComboBranch.Main;
                            if (e.DenyReason == SwitchDeniedReason.Cooldown && midString)
                            {
                                denials++;
                                if (expect < 0)
                                {
                                    emptyWait = false;
                                    expect = last.IsFinisher ? 0 : last.ChainIndex + 1;
                                    expectBy = s.World.GameTime + 1.0;
                                }
                            }
                            else other++;
                        }
                        if (!IsStringMove(e)) return;
                        if (expect >= 0)
                        {
                            if (e.ChainIndex == expect && e.Element == last.Element) continued++;
                            else dropped++;
                            expect = -1;
                        }
                        last = e;
                        haveLast = true;
                    };
                    int frames = (int)(60f * o.Fps);
                    for (int f = 0; f < frames; f++)
                    {
                        s.Step(bot.NextPad());
                        if (expect >= 0 && s.Model.Stamina <= 0f) emptyWait = true;
                        if (expect >= 0 && s.World.GameTime > expectBy)
                        {
                            // Nothing came. With the bar empty meanwhile, an X would not have continued it either.
                            if (emptyWait) emptyBar++;
                            else dropped++;
                            expect = -1;
                        }
                    }
                }
                t.Row(p, strikes, denials, continued, dropped, emptyBar, other);
            }
            t.Print();
            Out.Line("\"Continued\" = the string's next move came out at the next slot in the element it was in. Several refused presses for one "
                     + "slot count once. \"Bar ran dry\" = no next move because the stamina bar was empty (an X would not have continued either). "
                     + "\"Other denials\" = refused outside a running string move (nothing to continue).");
        }

        // ---------------------------------------------------------------- the air cap

        static readonly FieldInfo AirAttacksUsed = typeof(PlayerCombatModel).GetField("airAttacksUsed", BindingFlags.NonPublic | BindingFlags.Instance);

        static void AirCap(Options o)
        {
            Out.Sub("Air strings with switches (target: 0 jumps over the air attack cap)");
            var t = new Table("Preset", "Jumps", "Air attacks", "Switch strikes in the air", "Most in one jump", "Cap", "Over the cap");
            foreach (Preset p in o.Presets)
            {
                int jumps = 0, attacks = 0, switches = 0, most = 0, over = 0, cap = 0;
                for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                {
                    Session s = WithPartner(o, p, seed, out _);
                    var rng = new DeterministicRandom(seed * 4513 + 9);
                    double pressAt = -1;
                    int inJump = 0;
                    s.World.PlayerEvent += e =>
                    {
                        if (e.Type == PlayerEventType.Jumped)
                        {
                            jumps++;
                            inJump = 0;
                            pressAt = s.World.GameTime + 0.12;
                        }
                        if (e.Type == PlayerEventType.ElementSwitched && e.IsSwitchStrike && e.InAir) switches++;
                        if (e.Type != PlayerEventType.AttackStarted || e.AttackKind != PlayerAttackKind.Air) return;
                        attacks++;
                        inJump++;
                        most = Math.Max(most, inJump);
                        int limit = s.Model.MoveSet.Aerial.AirAttacksPerJump;
                        cap = Math.Max(cap, limit);
                        int used = (int)AirAttacksUsed.GetValue(s.Model);
                        if (used > limit || inJump > limit) over++;
                        pressAt = s.World.GameTime + e.Move.ActiveStart / Math.Max(0.01f, e.PlaybackRate) + rng.Range(-0.03f, 0.03f);
                    };
                    int frames = (int)(30f * o.Fps);
                    double nextJump = 0.2;
                    for (int f = 0; f < frames; f++)
                    {
                        var pad = new Pad();
                        bool grounded = s.Player.Controller.IsGrounded && s.Model.State == PlayerState.Locomotion;
                        if (grounded && s.World.GameTime >= nextJump && s.Model.Stamina > s.Model.MaxStamina * 0.5f)
                        {
                            pad.Jump = true;
                            nextJump = s.World.GameTime + 0.5;
                        }
                        if (pressAt > 0 && s.World.GameTime + 1.0 / o.Fps >= pressAt)
                        {
                            pressAt = -1;
                            if (rng.Chance(0.6f)) pad.Element = (ElementId)(((int)s.Model.ActiveElement - 1 + rng.Range(1, 4)) % 4 + 1);
                            else pad.Light = true;
                        }
                        s.Step(pad);
                    }
                }
                t.Row(p, jumps, attacks, switches, most, cap, over);
            }
            t.Print();
        }

        // ---------------------------------------------------------------- MIX 4

        static void MixFour(Options o)
        {
            Out.Sub("MIX 4 finishers by 'switcher' (target: >= 80 % of seeds Fluid, >= 50 % Punishing)");
            var t = new Table("Preset", "Opponent", "Seeds with a MIX 4 finisher", "MIX 4 finishers / min", "Switch strikes / min", "Won");
            foreach (Preset p in o.Presets)
            {
                foreach (string opponent in new[] { "passive partner (30 s)", "1 Dao Soldier (duel)" })
                {
                    int withFour = 0, won = 0, finishers = 0, strikes = 0;
                    double seconds = 0;
                    for (int seed = o.Seed; seed < o.Seed + o.Seeds; seed++)
                    {
                        SwitcherBot bot;
                        if (opponent.StartsWith("passive"))
                        {
                            Session s = WithPartner(o, p, seed, out _);
                            bot = (SwitcherBot)Bots.Create("switcher");
                            bot.Attach(s, seed * 977 + 13);
                            int frames = (int)(30f * o.Fps);
                            for (int f = 0; f < frames; f++) s.Step(bot.NextPad());
                            seconds += s.World.GameTime;
                        }
                        else
                        {
                            DuelResult r = DuelsScenario.Play(o, p, "switcher", "soldier", seed, 120, null, invariants: false);
                            bot = (SwitcherBot)r.Bot;
                            seconds += r.Seconds;
                            if (r.Won) won++;
                        }
                        if (bot.MixFourFinishers > 0) withFour++;
                        finishers += bot.MixFourFinishers;
                        strikes += bot.SwitchStrikes;
                    }
                    double minutes = Math.Max(1e-6, seconds / 60.0);
                    t.Row(p, opponent, Out.Pct(withFour / (double)o.Seeds), Out.N(finishers / minutes, 1), Out.N(strikes / minutes, 1),
                        opponent.StartsWith("passive") ? "-" : Out.Pct(won / (double)o.Seeds));
                }
            }
            t.Print();
        }
    }
}
