using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Rhythm combos (Build 05, spec 2.4): a follow-up press is judged against the running move's beat (the moment it
    // strikes). On the beat: the next move plays faster and hits harder; early (a mash) or mashed: slower and dearer;
    // late: neutral. Poise is never scaled. A whole string on the beat earns the perfect-string finisher.
    public class CombatRhythmTests
    {
        static float BaseDamage(PlayerDriver d)
        {
            return d.Model.CurrentMove.Damage * d.Model.MomentumMultiplier;
        }

        [Test]
        public void OnBeatPressRaisesNextPlaybackRateAndDamage()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(1);
            PlayerEvent second = d.LastStarted;
            Assert.AreEqual(BeatGrade.OnBeat, second.Grade);
            Assert.AreEqual(BeatGrade.OnBeat, d.LastOf(PlayerEventType.BeatJudged).Grade);
            Assert.AreEqual(d.Model.Tuning.Rhythm.OnBeatPlaybackRate, second.PlaybackRate, 1e-5f);
            Assert.AreEqual(second.PlaybackRate, d.Model.Rhythm.PlaybackRate, 1e-5f);
            DamageInfo damage = d.Model.BuildCurrentDamage();
            Assert.AreEqual(BaseDamage(d) * d.Model.Tuning.Rhythm.OnBeatDamageMultiplier, damage.Damage, 1e-3f);
        }

        [Test]
        public void PoiseNotScaledByOnBeat()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(3);
            Assert.AreEqual(BeatGrade.OnBeat, d.LastStarted.Grade);
            Assert.AreEqual(d.Model.CurrentMove.PoiseDamage, d.Model.BuildCurrentDamage().PoiseDamage, 1e-5f);
        }

        [Test]
        public void EarlyPressGradesEarlyAndSlowsNext()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Light);
            d.Step();
            d.Step(Pad.Light);                                   // 2 frames in: long before the jab strikes
            Assert.AreEqual(BeatGrade.Early, d.LastOf(PlayerEventType.BeatJudged).Grade);
            d.RunUntilStarted(2);
            Assert.AreEqual(BeatGrade.Early, d.LastStarted.Grade);
            Assert.AreEqual(d.Model.Tuning.Rhythm.OffBeatPlaybackRate, d.LastStarted.PlaybackRate, 1e-5f);
            Assert.AreEqual(BaseDamage(d), d.Model.BuildCurrentDamage().Damage, 1e-3f, "no on-beat bonus");
        }

        [Test]
        public void SecondPressBeforeNextMoveDowngradesToMashed()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Light);
            d.PressOnBeat();
            Assert.AreEqual(BeatGrade.OnBeat, d.LastOf(PlayerEventType.BeatJudged).Grade);
            d.Step();
            d.Step(Pad.Light);                                   // again, before the cross has started
            Assert.AreEqual(1, d.Started);
            Assert.AreEqual(BeatGrade.Mashed, d.LastOf(PlayerEventType.BeatJudged).Grade, "re-raised as a downgrade");
            d.RunUntilStarted(2);
            Assert.AreEqual(BeatGrade.Mashed, d.LastStarted.Grade);
            Assert.AreEqual(d.Model.Tuning.Rhythm.OffBeatPlaybackRate, d.LastStarted.PlaybackRate, 1e-5f);
            Assert.AreEqual(0, d.Model.Rhythm.Streak);
        }

        [Test]
        public void LatePressNeutral()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Light);
            MoveData jab = d.Model.CurrentMove;
            RhythmTuning r = d.Model.Tuning.Rhythm;
            // After the window (beat + BeatLate) but inside the combo window.
            d.RunUntil(x => x.Model.ActionTime > jab.ActiveStart + r.BeatLate + 0.02f, 60);
            Assert.Less(d.Model.ActionTime, jab.ComboWindowEnd);
            d.Step(Pad.Light);
            Assert.AreEqual(BeatGrade.Late, d.LastOf(PlayerEventType.BeatJudged).Grade);
            d.RunUntilStarted(2);
            Assert.AreEqual(BeatGrade.Late, d.LastStarted.Grade);
            Assert.AreEqual(1f, d.LastStarted.PlaybackRate, 1e-5f);
            Assert.AreEqual(0, d.Model.Rhythm.Streak);
        }

        [Test]
        public void MashSurchargeChargesExtraStamina()
        {
            PlayerTuning t = PlayerTuning.CreateFluid();
            t.StaminaRegen = 0f;
            PlayerDriver d = PlayerDriver.Elements(t);
            d.Step(Pad.Light);
            d.Step();
            d.Step(Pad.Light);                                   // early: a mash
            float before = d.Model.Stamina;
            d.RunUntilStarted(2);
            MoveData cross = d.Model.CurrentMove;
            Assert.AreEqual(before - cross.StaminaCost - t.Rhythm.MashStaminaSurcharge, d.Model.Stamina, 1e-3f);

            PlayerDriver onBeat = PlayerDriver.Elements(t);
            onBeat.Step(Pad.Light);
            onBeat.PressOnBeat();
            float beforeBeat = onBeat.Model.Stamina;
            onBeat.RunUntilStarted(2);
            Assert.AreEqual(beforeBeat - cross.StaminaCost, onBeat.Model.Stamina, 1e-3f, "on the beat: no surcharge");
        }

        [Test]
        public void PressDuringHitstopCountsAtFrozenClock()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.Rhythm.TimeToBeat <= 0f, 60);
            float offset = -d.Model.Rhythm.TimeToBeat;
            // A full freeze-frame (dt 0): nothing advances, but the press is judged at the frozen moment.
            d.Step(Pad.None, default, 0f);
            d.Step(Pad.Light, default, 0f);
            PlayerEvent judged = d.LastOf(PlayerEventType.BeatJudged);
            Assert.AreEqual(BeatGrade.OnBeat, judged.Grade);
            Assert.AreEqual(offset, judged.Amount, 1e-4f, "judged at the clock the freeze stopped on");
            d.Run(3, Pad.None);
            d.RunUntilStarted(2);
            Assert.AreEqual(BeatGrade.OnBeat, d.LastStarted.Grade);
        }

        [Test]
        public void BeatWindowScalesWithPlaybackRate()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(1);
            PlayerEvent cross = d.LastStarted;
            float rate = cross.PlaybackRate;
            Assert.Greater(rate, 1f);
            // The move just started this frame: its beat is ActiveStart / rate away, on the game clock.
            Assert.AreEqual(cross.Move.ActiveStart / rate, d.Model.Rhythm.TimeToBeat, 1e-4f);
            int startFrame = d.Frame;
            d.RunUntil(x => x.Model.Phase == AttackPhase.Active, 60);
            Assert.AreEqual((int)Math.Ceiling(cross.Move.ActiveStart / rate / d.Dt - 1e-3), d.Frame - startFrame,
                "the strike lands when the scaled timeline reaches it");
        }

        [Test]
        public void JudgedPressNotExpiredBeforeScaledCancel()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(4);                                   // Phoenix Palm, the finisher
            PlayerEvent palm = d.LastStarted;
            Assert.AreEqual(4, palm.ChainIndex);
            int start = d.Frame;
            d.PressOnBeat();                                     // ~0.19 s in: long before its combo window (0.50)
            Assert.AreEqual(BeatGrade.OnBeat, d.LastOf(PlayerEventType.BeatJudged).Grade);
            Assert.IsTrue(d.Model.BufferedCommandQueued, "queued at once");
            d.RunUntilStarted(6, 90);
            float cancel = palm.Move.ChainCancelAt / palm.PlaybackRate;
            Assert.AreEqual(0, d.LastStarted.ChainIndex, "the string loops on the press it got");
            Assert.AreEqual(d.FramesToReach(cancel), d.Frame - start, 1, "at the (scaled) cancel point, never expired");
        }

        [Test]
        public void OneJudgementPerMoveAndEventOrder()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(2);
            var order = new List<PlayerEventType>();
            foreach (PlayerEvent e in d.Log)
            {
                if (e.Type == PlayerEventType.ComboBeatOpened || e.Type == PlayerEventType.BeatJudged || e.Type == PlayerEventType.AttackStarted)
                    order.Add(e.Type);
            }
            CollectionAssert.AreEqual(new[]
            {
                PlayerEventType.AttackStarted, PlayerEventType.ComboBeatOpened, PlayerEventType.BeatJudged,
                PlayerEventType.AttackStarted, PlayerEventType.ComboBeatOpened, PlayerEventType.BeatJudged,
                PlayerEventType.AttackStarted
            }, order);
        }

        [Test]
        public void PunishingWindowsNarrower()
        {
            RhythmTuning fluid = PlayerTuning.CreateFluid().Rhythm, punishing = PlayerTuning.CreatePunishing().Rhythm;
            Assert.Less(punishing.BeatEarly, fluid.BeatEarly);
            Assert.Less(punishing.BeatLate, fluid.BeatLate);
            PlayerDriver d = PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            d.Step(Pad.Light);
            Assert.AreEqual(punishing.BeatEarly, d.Model.Rhythm.EarlyWindow, 1e-5f);
            Assert.AreEqual(punishing.BeatLate, d.Model.Rhythm.LateWindow, 1e-5f);
            // A press 0.06 s before the beat is on the beat in Fluid and early in Punishing.
            foreach (bool isPunishing in new[] { false, true })
            {
                PlayerDriver p = isPunishing ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing())
                    : PlayerDriver.Elements();
                p.Step(Pad.Light);
                p.RunUntil(x => x.Model.Rhythm.TimeToBeat - x.Dt <= 0.06f, 60);
                Assert.That(p.Model.Rhythm.TimeToBeat - p.Dt, Is.InRange(0.041f, 0.06f), "pressed between the two presets' early windows");
                p.Step(Pad.Light);
                Assert.AreEqual(isPunishing ? BeatGrade.Early : BeatGrade.OnBeat, p.LastOf(PlayerEventType.BeatJudged).Grade);
            }
        }

        [Test]
        public void PerfectStringFinisherBonusAndLaunch()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(4);
            Assert.AreEqual(1, d.Count(PlayerEventType.PerfectString));
            Assert.AreSame(d.Model.CurrentMove, d.LastOf(PlayerEventType.PerfectString).Move);
            RhythmTuning r = d.Model.Tuning.Rhythm;
            DamageInfo damage = d.Model.BuildCurrentDamage();
            Assert.AreEqual(BaseDamage(d) * r.OnBeatDamageMultiplier * r.PerfectStringDamageMultiplier, damage.Damage, 1e-3f);
            Assert.AreEqual(r.PerfectStringLaunchSpeed, damage.LaunchSpeed, 1e-5f, "knocks a launchable foe up");
            Assert.AreEqual(d.Model.CurrentMove.PoiseDamage, damage.PoiseDamage, 1e-5f);

            // One late press anywhere and the finisher is ordinary.
            PlayerDriver late = PlayerDriver.Elements();
            late.Step(Pad.Light);
            late.RunUntil(x => x.Model.ActionTime > 0.25f, 60);
            late.Step(Pad.Light);
            late.RunUntilStarted(2);
            for (int n = 3; n <= 5; n++)
            {
                late.PressOnBeat();
                late.RunUntilStarted(n);
            }
            Assert.AreEqual(4, late.LastStarted.ChainIndex);
            Assert.AreEqual(0, late.Count(PlayerEventType.PerfectString));
            Assert.AreEqual(late.Model.CurrentMove.LaunchSpeed, late.Model.BuildCurrentDamage().LaunchSpeed, 1e-5f);
        }

        // The rule from report 01: one full string plus a jab never staggers a fresh Dao Soldier, so its break-out gets its
        // turn. With rhythm (poise is never scaled) and with the dodge strike in any slot, for every element and preset.
        [Test]
        public void SingleElementOnBeatStringNeverStaggersFreshSoldier()
        {
            float soldierPoise = EnemyTuning.CreateDaoSoldier().MaxPoise;
            foreach (ElementLoadout loadout in new[] { ElementLoadout.CreateFluid(), ElementLoadout.CreatePunishing() })
            {
                for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
                {
                    ElementMoveSet set = loadout.Get(element);
                    string context = set.DisplayName + " " + (loadout.Fire.Dodge.RequiresStamina ? "Punishing" : "Fluid");
                    float hit1 = StringPoise(set.LightChain[0]);
                    float main = 0f;
                    foreach (MoveData m in set.LightChain) main += StringPoise(m);
                    Assert.Less(main + hit1, soldierPoise, context + ": light string + hit 1");
                    float pause = 0f;
                    for (int i = 0; i <= set.Rhythm.PauseAfterIndex; i++) pause += StringPoise(set.LightChain[i]);
                    foreach (MoveData m in set.PauseChain) pause += StringPoise(m);
                    Assert.Less(pause + hit1, soldierPoise, context + ": pause path + hit 1");
                    for (int slot = 0; slot < set.LightChain.Length - 1; slot++)
                    {
                        float substituted = main - StringPoise(set.LightChain[slot]) + StringPoise(set.DodgeStrike);
                        Assert.Less(substituted + hit1, soldierPoise, context + ": dodge strike in slot " + (slot + 1));
                    }
                }
            }

            // And the model agrees: an all on-beat Fire string adds up the same poise.
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(4);
            float total = 0f;
            foreach (PlayerEvent e in d.All(PlayerEventType.AttackStarted)) total += d.Model.BuildDamage(e.Move, e.AttackId).PoiseDamage;
            Assert.Less(total + d.Model.MoveSet.LightChain[0].PoiseDamage, soldierPoise);
        }

        static float StringPoise(MoveData move)
        {
            return move.PoiseDamage * Math.Max(1, move.HitCount);
        }

        // Spec 2.4.3: every string move's beat window fits its move (both presets, every element).
        [Test]
        public void AllStringMovesPassBeatDataValidation()
        {
            foreach (bool punishing in new[] { false, true })
            {
                PlayerTuning tuning = punishing ? PlayerTuning.CreatePunishing() : PlayerTuning.CreateFluid();
                ElementLoadout loadout = punishing ? ElementLoadout.CreatePunishing() : ElementLoadout.CreateFluid();
                RhythmTuning r = tuning.Rhythm;
                for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
                {
                    ElementMoveSet set = loadout.Get(element);
                    ElementRhythm er = set.Rhythm;
                    float rOn = er.OnBeatPlaybackRate > 0f ? er.OnBeatPlaybackRate : r.OnBeatPlaybackRate;
                    float early = r.BeatEarly + er.BeatEarlyDelta;
                    float late = r.BeatLate + er.BeatLateDelta + tuning.ElementSwitch.SwitchStrikeBeatLateBonus;
                    var moves = new List<(MoveData move, bool chains)>();
                    foreach (MoveData m in set.LightChain) moves.Add((m, true));
                    foreach (MoveData m in set.PauseChain) moves.Add((m, true));
                    for (int i = 0; i < set.AirChain.Length; i++) moves.Add((set.AirChain[i], i < set.AirChain.Length - 1));
                    moves.Add((set.DodgeStrike, true));
                    foreach ((MoveData m, bool chains) in moves)
                    {
                        string context = (punishing ? "Punishing " : "Fluid ") + set.DisplayName + " " + m.DisplayName;
                        Assert.GreaterOrEqual(m.ActiveStart / rOn - early, 0f, context + ": the window opens after the move starts");
                        if (chains) Assert.LessOrEqual(m.ActiveStart + late, m.ComboWindowEnd + 1e-4f, context + ": the window closes inside the combo window");
                        Assert.Less(m.ComboWindowEnd, m.TotalDuration + r.PauseGrace, context + ": combo window before the pause band ends");
                        Assert.GreaterOrEqual(m.ChainCancelAt, m.ActiveEnd - 1e-4f, context + ": no chaining before the strike is over");
                        Assert.LessOrEqual(m.ComboWindowStart, m.ChainCancelAt + 1e-4f, context + ": the window opens by the cancel point");
                    }
                }
            }
        }

        // RhythmTuning.Enabled = false: mashing chains exactly at each move's cancel point, at normal speed, no judgement.
        [Test]
        public void RhythmDisabledRestoresOldChainTiming()
        {
            PlayerTuning t = PlayerTuning.CreateFluid();
            t.Rhythm.Enabled = false;
            t.StaminaRegen = 1000f;
            PlayerDriver d = PlayerDriver.Elements(t);
            for (int f = 0; f < 200 && d.Started < 6; f++) d.Step(f % 6 == 0 ? Pad.Light : Pad.None);   // mash at 10 Hz
            List<PlayerEvent> starts = d.All(PlayerEventType.AttackStarted);
            Assert.GreaterOrEqual(starts.Count, 6);
            Assert.AreEqual(0, d.Count(PlayerEventType.BeatJudged));
            Assert.AreEqual(0, d.Count(PlayerEventType.ComboBeatOpened));
            int previousFrame = -1;
            int index = 0;
            for (int i = 0; i < d.Log.Count; i++)
            {
                if (d.Log[i].Type != PlayerEventType.AttackStarted) continue;
                PlayerEvent e = d.Log[i];
                Assert.AreEqual(1f, e.PlaybackRate, 1e-6f);
                Assert.AreEqual(index % 5, e.ChainIndex, "the old loop");
                if (previousFrame >= 0)
                {
                    MoveData previous = starts[index - 1].Move;
                    Assert.AreEqual(d.FramesToReach(previous.ChainCancelAt), d.LogFrames[i] - previousFrame, "chains at the cancel point");
                }
                previousFrame = d.LogFrames[i];
                index++;
            }
        }

        // The element's martial art on top of the beat (spec 2.4.4): Earth's on-beat hit is armoured, Water's gives stamina
        // back, Air's dodge keeps the streak, Fire's feeds Momentum.
        [Test]
        public void ElementRhythmDeltasApply()
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            loadout.Water.Rhythm = new ElementRhythm { BeatLateDelta = 0.04f, OnBeatPlaybackRate = 1.10f, OnBeatStaminaRefund = 3f };
            loadout.Earth.Rhythm = new ElementRhythm { BeatEarlyDelta = -0.02f, BeatLateDelta = -0.03f, OnBeatPlaybackRate = 1.10f,
                OnBeatDamageBonus = 0.10f, OnBeatHyperArmor = true };
            loadout.Air.Rhythm = new ElementRhythm { OnBeatPlaybackRate = 1.20f, DodgeKeepsBeat = true };

            // Fire: an on-beat press feeds Momentum.
            PlayerDriver fire = PlayerDriver.Elements(null, loadout);
            fire.Step(Pad.Light);
            fire.PressOnBeat();
            float momentum = fire.Model.Momentum;
            fire.RunUntilStarted(2);
            Assert.AreEqual(momentum + loadout.Fire.Rhythm.OnBeatMomentumBonus, fire.Model.Momentum, 1e-3f);

            // Water: stamina back, its own rate, a wider late window.
            PlayerTuning t = PlayerTuning.CreateFluid();
            t.StaminaRegen = 0f;
            PlayerDriver water = PlayerDriver.Elements(t, loadout);
            water.Select = ElementId.Water;
            water.Step();
            water.Step(Pad.Light);
            Assert.AreEqual(t.Rhythm.BeatLate + 0.04f, water.Model.Rhythm.LateWindow, 1e-5f);
            water.PressOnBeat();
            float before = water.Model.Stamina;
            water.RunUntilStarted(2);
            Assert.AreEqual(before - water.Model.CurrentMove.StaminaCost + 3f, water.Model.Stamina, 1e-3f);
            Assert.AreEqual(1.10f, water.LastStarted.PlaybackRate, 1e-5f);

            // Earth: armoured from the start of the on-beat hit, and harder.
            PlayerDriver earth = PlayerDriver.Elements(null, loadout);
            earth.Select = ElementId.Earth;
            earth.Step();
            earth.Step(Pad.Light);
            earth.PressOnBeat();
            earth.RunUntilStarted(2);
            Assert.AreEqual(earth.Model.CurrentMove.Damage * earth.Model.MomentumMultiplier * (t.Rhythm.OnBeatDamageMultiplier + 0.10f),
                earth.Model.BuildCurrentDamage().Damage, 1e-3f);
            earth.HitFromFront(5f, 999f);
            earth.Step();
            Assert.AreEqual(PlayerState.Attacking, earth.Model.State, "the on-beat hit can't be knocked out of");

            // Air: a dodge between hits keeps the streak, and the dodge strike is always on the beat.
            PlayerDriver air = PlayerDriver.Elements(null, loadout);
            air.Target(new Vector3(0f, 0f, 2.2f));
            air.Select = ElementId.Air;
            air.Step();
            air.OnBeatString(1);
            Assert.AreEqual(1, air.Model.Rhythm.Streak);
            air.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60);
            air.Step(Pad.Dodge, new Vector2(-1f, 0f));
            air.Step(Pad.Light, new Vector2(-1f, 0f));
            air.RunUntilStarted(3, 60);
            Assert.AreEqual(ComboBranch.DodgeStrike, air.LastStarted.Branch);
            Assert.AreEqual(BeatGrade.Auto, air.LastStarted.Grade);
            Assert.AreEqual(2, air.Model.Rhythm.Streak, "the step is the beat");

            // Fire (no DodgeKeepsBeat): the same dodge starts the streak over.
            PlayerDriver fireDodge = PlayerDriver.Elements(null, loadout);
            fireDodge.Target(new Vector3(0f, 0f, 2.2f));
            fireDodge.OnBeatString(1);
            fireDodge.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60);
            fireDodge.Step(Pad.Dodge, new Vector2(-1f, 0f));
            fireDodge.Step(Pad.Light, new Vector2(-1f, 0f));
            fireDodge.RunUntilStarted(3, 60);
            Assert.AreEqual(BeatGrade.None, fireDodge.LastStarted.Grade);
            Assert.AreEqual(0, fireDodge.Model.Rhythm.Streak);
        }
    }
}
