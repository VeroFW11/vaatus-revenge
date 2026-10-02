using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // String memory (Build 05, spec 2.2): the combo survives dodges, zips, abilities and switches. X X, dodge, X is hit 3;
    // a dodge before a hit has struck retries that hit; the memory runs out StringMemoryAfterAction after the dodge, and a
    // clean hit taken, a stagger or a preset change wipes it.
    public class CombatStringMemoryTests
    {
        static readonly Vector2 Left = new Vector2(-1f, 0f);

        // X, X on the beat, then wait until hit 2 has struck.
        static PlayerDriver TwoHitsStruck(bool target)
        {
            PlayerDriver d = PlayerDriver.Elements();
            if (target) d.Target(new Vector3(0f, 0f, 2.2f));
            d.OnBeatString(1);
            d.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60);
            return d;
        }

        [Test]
        public void DodgeMidStringKeepsChainIndex()
        {
            foreach (bool target in new[] { false, true })
            {
                PlayerDriver d = TwoHitsStruck(target);
                d.Step(Pad.Dodge, Left);
                Assert.AreEqual(PlayerState.Dodging, d.Model.State, "Fluid chain hits are dodge-cancellable");
                Assert.AreEqual(2, d.Model.StringNextIndex, "the string waits at hit 3 while you dodge");
                d.Step(Pad.Light, Left);
                d.RunUntilStarted(3, 60);
                PlayerEvent third = d.LastStarted;
                Assert.AreEqual(2, third.ChainIndex, "X X, dodge, X is the third hit");
                Assert.AreEqual(target ? ComboBranch.DodgeStrike : ComboBranch.Main, third.Branch,
                    target ? "with an enemy near it is the dodge strike, in slot 3" : "no target: the string just carries on");
                if (!target) Assert.AreSame(d.Model.MoveSet.LightChain[2], third.Move);
            }
        }

        [Test]
        public void DodgeDuringStartupRetriesSameHit()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(1);                                   // hit 2 just started: still in its wind-up
            Assert.AreEqual(AttackPhase.Startup, d.Model.Phase);
            d.Step(Pad.Dodge, Left);
            Assert.AreEqual(1, d.Model.StringNextIndex, "it never struck: the same hit comes again");
            d.Step(Pad.Light, Left);
            d.RunUntilStarted(3, 60);
            Assert.AreEqual(1, d.LastStarted.ChainIndex);
        }

        [Test]
        public void ZipStrikeKeepsStringAndDoesNotConsumeSlot()
        {
            PlayerDriver d = TwoHitsStruck(false);
            d.World.HasZipTarget = true;
            d.World.ZipTargetPosition = new Vector3(0f, 0f, 9f);
            d.World.ZipTargetRadius = 0.4f;
            d.Step(Pad.Zip);
            d.RunUntil(x => x.Model.CurrentAttackKind == PlayerAttackKind.ZipStrike, 30);
            Assert.AreEqual(2, d.Model.StringNextIndex, "held while zipping");
            MoveData zip = d.Model.CurrentMove;
            d.RunUntil(x => x.Model.ActionTime >= zip.ChainCancelAt - 0.1f, 60);
            d.World.HasZipTarget = false;
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.CurrentAttackKind == PlayerAttackKind.Light, 90);
            Assert.AreEqual(2, d.LastStarted.ChainIndex, "the zip is a connector, not a slot");
        }

        [Test]
        public void AbilityAndSkillKeepString()
        {
            foreach (Pad connector in new[] { Pad.AbilityNorth, Pad.Skill })
            {
                PlayerDriver d = TwoHitsStruck(false);
                d.Step(connector);
                Assert.AreNotEqual(PlayerAttackKind.Light, d.Model.CurrentAttackKind, connector + " started");
                MoveData move = d.Model.CurrentMove;
                d.RunUntil(x => x.Model.ActionTime >= move.ChainCancelAt - 0.1f, 60);
                d.Step(Pad.Light);
                d.RunUntil(x => x.Model.CurrentAttackKind == PlayerAttackKind.Light, 120);
                Assert.AreEqual(2, d.LastStarted.ChainIndex, connector + " keeps the string");
            }
        }

        [Test]
        public void StringMemoryExpiresAfterStringMemoryAfterAction()
        {
            PlayerDriver d = TwoHitsStruck(false);
            d.Step(Pad.Dodge, Left);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60);
            d.Run(d.FramesToReach(d.Model.Tuning.StringMemoryAfterAction) - 2);
            Assert.AreEqual(2, d.Model.StringNextIndex, "still remembered just inside the window");
            d.Run(3);
            Assert.AreEqual(-1, d.Model.StringNextIndex, "forgotten after it");
            d.Step(Pad.Light);
            Assert.AreEqual(0, d.LastStarted.ChainIndex, "X starts the string again");
        }

        [Test]
        public void CleanHitTakenClearsString()
        {
            PlayerDriver d = TwoHitsStruck(false);
            d.Step(Pad.Dodge, Left);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60);
            Assert.AreEqual(HitOutcome.Hit, d.HitFromFront(5f).Outcome);
            Assert.AreEqual(-1, d.Model.StringNextIndex);
            d.Step(Pad.Light);
            Assert.AreEqual(0, d.LastStarted.ChainIndex);
        }

        [Test]
        public void StaggerClearsString()
        {
            PlayerDriver d = TwoHitsStruck(false);
            d.Step(Pad.Dodge, Left);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60);
            d.HitFromFront(5f, 999f);                            // breaks poise
            d.Step();
            Assert.AreEqual(PlayerState.Staggered, d.Model.State);
            Assert.AreEqual(-1, d.Model.StringNextIndex);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 120);
            d.Step(Pad.Light);
            Assert.AreEqual(0, d.LastStarted.ChainIndex);
        }

        [Test]
        public void ApplyTuningClearsString()
        {
            PlayerDriver d = TwoHitsStruck(false);
            d.Step(Pad.Dodge, Left);
            Assert.AreEqual(2, d.Model.StringNextIndex);
            d.Model.ApplyTuning(d.Model.Tuning, d.Model.Loadout);
            Assert.AreEqual(-1, d.Model.StringNextIndex);
            d.Step(Pad.Light);
            Assert.AreEqual(0, d.LastStarted.ChainIndex);
        }
        // Round 7, S7-02: a late X pressed anywhere from the end of hit 1's combo window to 3 frames after the move ends gets
        // one answer (the string starts again), never a one-frame island on the move's last frame that continues it.
        [Test]
        public void LatePressesPastTheWindowAllRestartTheString([Values(ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air)] ElementId element,
            [Values(false, true)] bool punishing)
        {
            PlayerDriver probe = punishing ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing()) : PlayerDriver.Elements();
            Assert.IsTrue(probe.Model.SetElementAtRest(element));
            MoveData jab = probe.Model.MoveSet.LightChain[0];
            if (jab.ComboWindowEnd >= jab.TotalDuration) Assert.Pass(element + ": its window reaches past the move's end (memory, not this case)");
            int windowEnd = (int)Math.Ceiling(jab.ComboWindowEnd / probe.Dt) + 1;
            int moveEnd = (int)Math.Ceiling(jab.TotalDuration / probe.Dt);
            var slots = new System.Collections.Generic.List<string>();
            for (int wait = windowEnd; wait <= moveEnd + 3; wait++)
            {
                PlayerDriver d = punishing ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing()) : PlayerDriver.Elements();
                Assert.IsTrue(d.Model.SetElementAtRest(element));
                d.Step(Pad.Light);
                d.RunUntilStarted(1);
                d.Run(wait - 1);
                d.Step(Pad.Light);
                d.RunUntilStarted(2, 60);
                slots.Add(wait + "f:" + d.LastStarted.Branch + d.LastStarted.ChainIndex);
            }
            string all = string.Join(", ", slots);
            foreach (string slot in slots) Assert.IsTrue(slot.EndsWith("Main0"), element + ": every late press restarts the string: " + all);
        }
    }
}
