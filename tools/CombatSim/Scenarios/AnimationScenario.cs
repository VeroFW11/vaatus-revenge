using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // `anim`: plays a scripted fight through the real combat core and the real procedural animator (the same
    // PlayerAnimationFeed / EnemyAnimationFeed / FighterAnimator the Unity game uses) and writes every frame's
    // joint positions, states, animation keys and fire effects to JSON (--out). tools/render/render_fight.py turns
    // that into a video or a contact sheet, so the animation can be checked without Unity.
    //
    // The script covers stance and footwork, the 5-hit chain, launcher -> air string -> slam and axe kick, air
    // dash, zip strike, fire whip, flame wheel, fire blast, fa jin, parry, dodge, getting hit, the soldier's sword
    // attacks, an enemy launched / knocked down / getting up, deaths, and finally a gallery of any animation key
    // the fight didn't reach. Since Build 05 it plays all four elements: each element's stance and the switch
    // flourish, then per element its string, pause branch, the dodge kinds and the dodge strike ("<Element> chain"
    // scenes, so render_fight.py --scene "water chain" picks one), its launcher, air string and plunge, and its
    // abilities. Every player frame records the element, the string branch, the beat grade and the dodge kind, and
    // every effect its element, so the renderer colours them by element. The console gets a short report: coverage,
    // strike extension at the first active frame, and whether each sword strike's blade tip lands on the edge of
    // its reach.
    public static class AnimationScenario
    {
        const float Dt = 1f / 60f;

        public static void Run(Options o)
        {
            string path = string.IsNullOrEmpty(o.OutFile) ? "anim.json" : o.OutFile;
            Out.Close();   // --out is this scenario's JSON, not the markdown report (that goes to the console)
            var rec = new AnimRecorder();

            Stance(rec);
            Chain(rec);
            ElementStances(rec);
            foreach (ElementId element in new[] { ElementId.Water, ElementId.Earth, ElementId.Air })
            {
                ElementChain(rec, element);
                ElementAerial(rec, element);
                ElementAbilities(rec, element);
            }
            SwitchesAndDodgeChains(rec);
            BehindYou(rec);
            Aerial(rec);
            AirDashAndZip(rec);
            Abilities(rec);
            Defence(rec);
            Finisher(rec);
            PlayerDeath(rec);
            Gallery(rec);

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, rec.ToJson(), new UTF8Encoding(false));

            Out.Heading("Animation scenario");
            Out.Line("Wrote " + rec.FrameCount + " frames (" + Out.N(rec.FrameCount / 60.0, 1) + " s at 60 fps) to " + path);
            rec.Report();
        }

        // ------------------------------------------------------------------ scenes

        static void Stance(AnimRecorder rec)
        {
            var s = new Scene(rec, "Stance and footwork");
            s.AddDummy(new Vector3(-3f, 0f, 9f), 180f);
            s.Run(StanceScript(s));
        }

        static IEnumerable<int> StanceScript(Scene s)
        {
            foreach (int f in s.Idle(70)) yield return f;
            foreach (int f in s.Move(new Vector2(0f, 0.35f), 55)) yield return f;
            foreach (int f in s.Move(new Vector2(0f, 1f), 60)) yield return f;
            s.Pad.Dodge = true;   // hold to sprint
            foreach (int f in s.Move(new Vector2(0f, 1f), 65)) yield return f;
            s.Pad.Dodge = false;
            foreach (int f in s.Idle(30)) yield return f;
            foreach (int f in s.Move(new Vector2(0.6f, 0f), 45)) yield return f;
            foreach (int f in s.Idle(20)) yield return f;
            foreach (int f in s.Tap(Btn.Jump, 6)) yield return f;
            foreach (int f in s.Idle(55)) yield return f;
            foreach (int f in s.Tap(Btn.Dodge, 3)) yield return f;   // backstep (no stick)
            foreach (int f in s.Idle(40)) yield return f;
        }

        static void Chain(AnimRecorder rec)
        {
            var s = new Scene(rec, "Fire chain: string, pause branch, dodges, dodge strike (Northern Shaolin)");
            s.AddDummy(new Vector3(0f, 0f, 2.3f), 180f);
            s.Run(ElementChainScript(s, ElementId.Fire));
        }

        // ------------------------------------------------------------------ Build 05: the four elements

        static readonly Dictionary<ElementId, string> Arts = new Dictionary<ElementId, string>
        {
            { ElementId.Fire, "Northern Shaolin" }, { ElementId.Water, "Tai Chi" }, { ElementId.Earth, "Hung Gar" }, { ElementId.Air, "Baguazhang" },
        };

        // Each element's stance in turn, switched to from standing (the switch flourish, then the stance breathing), and a
        // few steps in it.
        static void ElementStances(AnimRecorder rec)
        {
            var s = new Scene(rec, "Element stances and the switch flourish");
            s.AddDummy(new Vector3(0f, 0f, 3.5f), 180f);
            s.Run(ElementStancesScript(s));
        }

        static IEnumerable<int> ElementStancesScript(Scene s)
        {
            foreach (int f in s.Idle(40)) yield return f;
            foreach (ElementId element in new[] { ElementId.Water, ElementId.Earth, ElementId.Air, ElementId.Fire })
            {
                foreach (int f in s.Switch(element)) yield return f;
                foreach (int f in s.Idle(70)) yield return f;
                foreach (int f in s.Move(new Vector2(0.5f, 0f), 30)) yield return f;
                foreach (int f in s.Idle(20)) yield return f;
            }
        }

        static void ElementChain(AnimRecorder rec, ElementId element)
        {
            var s = new Scene(rec, element + " chain: string, pause branch, dodges, dodge strike (" + Arts[element] + ")");
            s.AddDummy(new Vector3(0f, 0f, 2.3f), 180f);
            s.Run(ElementChainScript(s, element));
        }

        // The element's five-hit string, then X X (pause) X X for the pause branch, then the dodge kinds (slip in, side-slip,
        // evade out) and an evade out with X late in it: the dodge strike dashing back in.
        static IEnumerable<int> ElementChainScript(Scene s, ElementId element)
        {
            foreach (int f in s.Idle(10)) yield return f;
            if (element != ElementId.Fire)
            {
                foreach (int f in s.Switch(element)) yield return f;
                foreach (int f in s.Idle(30)) yield return f;
            }
            foreach (int f in ChainScript(s, 5)) yield return f;

            // The pause branch: two hits, let the second one's combo window close, then press again (twice). The press goes
            // in just after the window: for Water and Air only a frame or two of the band falls inside the move.
            MoveData[] chain = s.Model.MoveSet.LightChain;
            int after = Math.Min(s.Model.MoveSet.Rhythm.PauseAfterIndex, chain.Length - 1);
            for (int i = 0; i <= after; i++)
            {
                foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
                MoveData move = chain[i];
                foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == move, 90)) yield return f;
                if (i < after)
                    foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == move && s.Model.ActionTime >= move.ComboWindowStart + 0.02f, 90)) yield return f;
                else
                    foreach (int f in s.WaitUntil(() => s.Model.CurrentMove != move || s.Model.ActionTime > move.ComboWindowEnd + 0.005f, 90)) yield return f;
            }
            MoveData[] pause = s.Model.MoveSet.PauseChain;
            for (int i = 0; i < pause.Length; i++)
            {
                foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
                MoveData move = pause[i];
                foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == move && s.Model.ActionTime >= move.ComboWindowStart + 0.02f, 90)) yield return f;
            }
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(40)) yield return f;

            // The dodge kinds, facing the dummy throughout: slip in, side-slip right and left, evade out (the stick is
            // pushed relative to where the dummy is, as a player would).
            foreach (float angle in new[] { 0f, 90f, -90f, 180f })
            {
                s.Pad.Move = s.StickTowardFoe(angle);
                foreach (int f in s.Tap(Btn.Dodge, 3)) yield return f;
                s.Pad.Move = Vector2.Zero;
                foreach (int f in s.Idle(35)) yield return f;
            }
            // Back in close, then evade out and X late in the dodge: the dodge strike dashes back in.
            foreach (int f in s.WalkToFoe(2.4f)) yield return f;
            foreach (int f in s.Idle(10)) yield return f;
            s.Pad.Move = s.StickTowardFoe(180f);
            foreach (int f in s.Tap(Btn.Dodge, 3)) yield return f;
            s.Pad.Move = Vector2.Zero;
            foreach (int f in s.Idle(5)) yield return f;
            foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(50)) yield return f;
        }

        static void ElementAerial(AnimRecorder rec, ElementId element)
        {
            var s = new Scene(rec, element + " launcher, air string, plunge (" + Arts[element] + ")");
            s.AddSoldier(new Vector3(0f, 0f, 2.1f), 180f, passive: true);
            s.Run(ElementAerialScript(s, element));
        }

        static IEnumerable<int> ElementAerialScript(Scene s, ElementId element)
        {
            foreach (int f in s.Idle(5)) yield return f;
            foreach (int f in s.Switch(element)) yield return f;
            foreach (int f in s.Idle(25)) yield return f;
            foreach (int f in AerialScript(s)) yield return f;
        }

        static void ElementAbilities(AnimRecorder rec, ElementId element)
        {
            var s = new Scene(rec, element + " abilities, ranged skill, heavy, zip and sprint (" + Arts[element] + ")");
            s.AddDummy(new Vector3(-1.2f, 0f, 3.2f), 180f);
            s.AddDummy(new Vector3(1.8f, 0f, 2.6f), 200f);
            s.AddDummy(new Vector3(0.5f, 0f, 10f), 180f);
            s.Run(ElementAbilitiesScript(s, element));
        }

        static IEnumerable<int> ElementAbilitiesScript(Scene s, ElementId element)
        {
            foreach (int f in s.Idle(5)) yield return f;
            foreach (int f in s.Switch(element)) yield return f;
            foreach (int f in s.Idle(20)) yield return f;
            foreach (int f in AbilitiesScript(s)) yield return f;
            // A zip strike to the far dummy, then a sprint attack back at the near ones.
            s.Pad.Move = new Vector2(0.05f, 1f);
            foreach (int f in s.Tap(Btn.Zip, 3)) yield return f;
            s.Pad.Move = Vector2.Zero;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(30)) yield return f;
            s.Pad.Move = new Vector2(0f, -1f);
            s.Pad.Dodge = true;   // hold to sprint away, then turn and kick
            foreach (int f in s.Idle(40)) yield return f;
            s.Pad.Move = new Vector2(0f, 1f);
            foreach (int f in s.Idle(30)) yield return f;
            s.Pad.Light = true;
            foreach (int f in s.Idle(3)) yield return f;
            s.Pad.Light = false;
            s.Pad.Dodge = false;
            s.Pad.Move = Vector2.Zero;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(40)) yield return f;
        }

        // R2-S20: the headline Build 05 moves the other scenes don't play: a string switching element mid-combo (X X, RB+X
        // Water, X, RB+A Earth), a chain of dodges (side-slip, side-slip, evade out), and a switch to Earth in the air string.
        static void SwitchesAndDodgeChains(AnimRecorder rec)
        {
            var s = new Scene(rec, "Mid-string switches, chained dodges, air switch to Earth");
            s.AddSoldier(new Vector3(0f, 0f, 2.1f), 180f, passive: true);
            s.Run(SwitchesScript(s));
        }

        static IEnumerable<int> SwitchesScript(Scene s)
        {
            foreach (int f in s.Idle(20)) yield return f;
            // A three-element string: each press goes in once the running move's combo window is open.
            ElementId[] presses = { ElementId.None, ElementId.None, ElementId.Water, ElementId.None, ElementId.Earth };
            foreach (ElementId pick in presses)
            {
                MoveData before = s.Model.CurrentMove;
                if (pick == ElementId.None) foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
                else foreach (int f in s.Switch(pick)) yield return f;
                foreach (int f in s.WaitUntil(() => s.Model.CurrentMove != null && s.Model.CurrentMove != before
                                                    && s.Model.ActionTime >= s.Model.CurrentMove.ComboWindowStart + 0.02f, 90)) yield return f;
            }
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(30)) yield return f;

            // Chained dodges in Air (Earth allows two in a row, Air four): side-slip right, side-slip left, evade out, each
            // pressed as the last one allows another.
            foreach (int f in s.Switch(ElementId.Air)) yield return f;
            foreach (int f in s.Idle(20)) yield return f;
            foreach (float angle in new[] { 90f, -90f, 180f })
            {
                s.Pad.Move = s.StickTowardFoe(angle);
                foreach (int f in s.Tap(Btn.Dodge, 3)) yield return f;
                foreach (int f in s.WaitUntil(() => s.Model.State != PlayerState.Dodging || s.Model.ActionTime >= 0.2f, 40)) yield return f;
            }
            s.Pad.Move = Vector2.Zero;
            foreach (int f in s.Idle(40)) yield return f;
            foreach (int f in s.WalkToFoe(2.1f)) yield return f;
            foreach (int f in s.Switch(ElementId.Earth)) yield return f;
            foreach (int f in s.Idle(20)) yield return f;

            // Launch in Earth, one air hit, switch to Water in the air and back to Earth: dust only.
            foreach (int f in s.Hold(Btn.Light, 22)) yield return f;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentAttackKind == PlayerAttackKind.Launcher && s.Model.ActionTime > 0.3f, 60)) yield return f;
            foreach (ElementId pick in new[] { ElementId.None, ElementId.Water, ElementId.Earth })
            {
                MoveData before = s.Model.CurrentMove;
                if (pick == ElementId.None) foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
                else foreach (int f in s.Switch(pick)) yield return f;
                foreach (int f in s.WaitUntil(() => s.Model.IsGrounded || (s.Model.CurrentMove != null && s.Model.CurrentMove != before
                                                    && s.Model.ActionTime >= s.Model.CurrentMove.ComboWindowStart + 0.04f), 60)) yield return f;
            }
            foreach (int f in s.WaitUntil(() => s.Model.IsGrounded && s.Model.CurrentMove == null, 120)) yield return f;
            foreach (int f in s.Idle(40)) yield return f;
        }

        static IEnumerable<int> ChainScript(Scene s, int hits)
        {
            foreach (int f in s.Idle(30)) yield return f;
            MoveData[] chain = s.Model.MoveSet.LightChain;
            for (int i = 0; i < hits && i < chain.Length; i++)
            {
                foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
                MoveData move = chain[i];
                // Next press inside this move's combo window.
                int guard = 0;
                while (guard++ < 90 && !(s.Model.CurrentMove == move && s.Model.ActionTime >= move.ComboWindowStart + 0.02f))
                {
                    s.Frame();
                    yield return 0;
                }
            }
            foreach (int f in s.Idle(60)) yield return f;
        }

        // A jab at a foe standing behind you: the combat rules snap the facing round at once (free-flow), so the body
        // has to whip round on its planted feet into the strike rather than flip in one frame.
        static void BehindYou(AnimRecorder rec)
        {
            var s = new Scene(rec, "Jab at a foe behind you");
            s.AddDummy(new Vector3(0.3f, 0f, -2f), 0f);
            s.Run(BehindYouScript(s));
        }

        static IEnumerable<int> BehindYouScript(Scene s)
        {
            foreach (int f in s.Idle(30)) yield return f;
            foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
            foreach (int f in s.Idle(50)) yield return f;
        }

        static void Aerial(AnimRecorder rec)
        {
            var s = new Scene(rec, "Launcher, air string, axe kick");
            s.AddSoldier(new Vector3(0f, 0f, 2.1f), 180f, passive: true);
            s.Run(AerialScript(s));
        }

        static IEnumerable<int> AerialScript(Scene s)
        {
            foreach (int f in s.Idle(30)) yield return f;
            // Hold attack on the ground: the first chain hit turns into the Rising Dragon Kick.
            foreach (int f in s.Hold(Btn.Light, 22)) yield return f;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentAttackKind == PlayerAttackKind.Launcher && s.Model.ActionTime > 0.3f, 60)) yield return f;
            MoveData[] air = s.Model.MoveSet.AirChain;
            for (int i = 0; i < air.Length; i++)
            {
                foreach (int f in s.Tap(Btn.Light, 3)) yield return f;
                MoveData move = air[i];
                foreach (int f in s.WaitUntil(() => (s.Model.CurrentMove == move && s.Model.ActionTime >= move.ComboWindowStart + 0.04f)
                                                     || (s.Model.CurrentMove == move && i == air.Length - 1 && s.Model.ActionTime >= move.ActiveEnd), 60)) yield return f;
            }
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 60)) yield return f;
            // Still airborne? Falling axe kick; otherwise jump and do it.
            if (s.Model.IsGrounded)
            {
                foreach (int f in s.Tap(Btn.Jump, 5)) yield return f;
                foreach (int f in s.Idle(22)) yield return f;
            }
            foreach (int f in s.Tap(Btn.Heavy, 4)) yield return f;
            foreach (int f in s.Idle(120)) yield return f;
        }

        static void AirDashAndZip(AnimRecorder rec)
        {
            var s = new Scene(rec, "Air dash and Flame Step Strike");
            s.AddCrossbowman(new Vector3(0f, 0f, 11f), 180f, passive: true);
            s.Run(AirDashScript(s));
        }

        static IEnumerable<int> AirDashScript(Scene s)
        {
            foreach (int f in s.Idle(20)) yield return f;
            foreach (int f in s.Tap(Btn.Jump, 5)) yield return f;
            foreach (int f in s.Idle(12)) yield return f;
            s.Pad.Move = new Vector2(0f, 1f);
            foreach (int f in s.Tap(Btn.Dodge, 3)) yield return f;
            s.Pad.Move = Vector2.Zero;
            foreach (int f in s.Idle(50)) yield return f;
            s.Pad.Move = new Vector2(0f, 1f);
            foreach (int f in s.Tap(Btn.Zip, 3)) yield return f;
            s.Pad.Move = Vector2.Zero;
            foreach (int f in s.Idle(70)) yield return f;
        }

        static void Abilities(AnimRecorder rec)
        {
            var s = new Scene(rec, "Fire Whip, Flame Wheel, Fire Blast, Fa Jin");
            s.AddDummy(new Vector3(-1.2f, 0f, 3.2f), 180f);
            s.AddDummy(new Vector3(1.8f, 0f, 2.6f), 200f);
            s.Run(AbilitiesScript(s));
        }

        static IEnumerable<int> AbilitiesScript(Scene s)
        {
            foreach (int f in s.Idle(25)) yield return f;
            foreach (int f in s.Hold(Btn.AbilityNorth, 5)) yield return f;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(20)) yield return f;
            foreach (int f in s.Hold(Btn.AbilityEast, 5)) yield return f;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(20)) yield return f;
            foreach (int f in s.Tap(Btn.Skill, 6)) yield return f;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 90)) yield return f;
            foreach (int f in s.Idle(20)) yield return f;
            // Fa jin: hold the heavy into the middle of the sweet spot, then let go.
            ChargeSettings charge = s.Model.MoveSet.Charge;
            int hold = (int)MathF.Round((charge.SweetSpotStart + charge.SweetSpotEnd) * 0.5f * 60f);
            foreach (int f in s.Hold(Btn.Heavy, hold)) yield return f;
            foreach (int f in s.WaitUntil(() => s.Model.CurrentMove == null, 120)) yield return f;
            foreach (int f in s.Idle(40)) yield return f;
        }

        static void Defence(AnimRecorder rec)
        {
            var s = new Scene(rec, "Defence vs Dao Soldier: parry, dodge, hit");
            SimEnemy soldier = s.AddSoldier(new Vector3(0f, 0f, 3.0f), 180f, passive: false);
            s.Run(DefenceScript(s, soldier));
        }

        static IEnumerable<int> DefenceScript(Scene s, SimEnemy soldier)
        {
            // React to the soldier's telegraphs: the first swing is parried, the second dodged, the third taken.
            int handled = 0;
            int frames = 0;
            while (frames++ < 60 * 12 && handled < 4)
            {
                if (soldier.Brain.State == EnemyState.Attacking && soldier.Brain.CurrentAttack != null && s.EnemyAttackStartedThisFrame(soldier))
                {
                    EnemyAttackData attack = soldier.Brain.CurrentAttack;
                    float strikeIn = attack.Move.Startup;
                    int waitFrames = Math.Max(0, (int)MathF.Round((strikeIn - 0.08f) * 60f) - 1);
                    for (int w = 0; w < waitFrames; w++)
                    {
                        s.Frame();
                        yield return 0;
                    }
                    if (handled == 0) foreach (int f in s.Tap(Btn.Guard, 4)) yield return f;
                    else if (handled == 1)
                    {
                        s.Pad.Move = new Vector2(-1f, 0f);
                        foreach (int f in s.Tap(Btn.Dodge, 3)) yield return f;
                        s.Pad.Move = Vector2.Zero;
                    }
                    else if (handled == 2) foreach (int f in s.Idle(3)) yield return f;   // take the hit
                    else
                    {
                        // Punish the recovery with the first two chain hits.
                        foreach (int f in s.WaitUntil(() => soldier.Brain.Phase == AttackPhase.Recovery, 60)) yield return f;
                        foreach (int f in ChainScript(s, 2)) yield return f;
                    }
                    handled++;
                }
                s.Frame();
                yield return 0;
            }
            foreach (int f in s.Idle(40)) yield return f;
        }

        static void Finisher(AnimRecorder rec)
        {
            var s = new Scene(rec, "Take down a crossbowman");
            SimEnemy bowman = s.AddCrossbowman(new Vector3(0f, 0f, 2.2f), 180f, passive: true);
            s.Run(FinisherScript(s, bowman));
        }

        static IEnumerable<int> FinisherScript(Scene s, SimEnemy bowman)
        {
            foreach (int f in s.Idle(15)) yield return f;
            int rounds = 0;
            while (bowman.Brain.IsAlive && rounds++ < 6)
            {
                foreach (int f in ChainScript(s, 5)) yield return f;
                s.Pad.Move = new Vector2(0f, 0.5f);
                foreach (int f in s.Idle(10)) yield return f;
                s.Pad.Move = Vector2.Zero;
            }
            foreach (int f in s.Idle(80)) yield return f;
        }

        static void PlayerDeath(AnimRecorder rec)
        {
            var s = new Scene(rec, "The Avatar falls", playerHealth: 25f);
            s.AddSoldier(new Vector3(0f, 0f, 2.4f), 180f, passive: false);
            s.Run(DeathScript(s));
        }

        static IEnumerable<int> DeathScript(Scene s)
        {
            foreach (int f in s.WaitUntil(() => !s.Model.IsAlive, 60 * 12)) yield return f;
            foreach (int f in s.Idle(90)) yield return f;
        }

        // Any animation key the fight above never showed, played on its own so every key can be looked at.
        static void Gallery(AnimRecorder rec)
        {
            var keys = typeof(AnimationKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
            var timings = new Dictionary<string, MoveData>();
            var styles = new Dictionary<string, string>();   // a key's own element's style, so it plays from that stance
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            foreach (ElementId element in new[] { ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air })
            {
                ElementMoveSet moves = loadout.Get(element);
                foreach (MoveData m in AllMoves(moves))
                {
                    if (string.IsNullOrEmpty(m.AnimationKey)) continue;
                    timings[m.AnimationKey] = m;
                    styles[m.AnimationKey] = moves.AnimationStyle;
                }
            }
            foreach (EnemyTuning t in new[] { EnemyTuning.CreateDaoSoldier(), EnemyTuning.CreateCrossbowman(), EnemyTuning.CreateSparringDummy() })
            {
                foreach (EnemyAttackData a in t.Attacks) if (a.Move != null && !string.IsNullOrEmpty(a.Move.AnimationKey)) timings[a.Move.AnimationKey] = a.Move;
                if (t.BreakOut != null && t.BreakOut.Attack != null && t.BreakOut.Attack.Move != null) timings[t.BreakOut.Attack.Move.AnimationKey] = t.BreakOut.Attack.Move;
            }
            var enemyKinds = new Dictionary<string, string>
            {
                { AnimationKeys.SwordSlash, "soldier" }, { AnimationKeys.SwordOverhead, "soldier" }, { AnimationKeys.SwordDoubleSlash, "soldier" },
                { AnimationKeys.SwordThrust, "soldier" }, { AnimationKeys.Shove, "soldier" }, { AnimationKeys.CrossbowShot, "crossbow" },
                { AnimationKeys.CrossbowBurst, "crossbow" }, { AnimationKeys.PracticeSwing, "dummy" },
            };
            foreach (string key in keys)
            {
                if (rec.Covered.Contains(key)) continue;
                string kind = enemyKinds.TryGetValue(key, out string k) ? k : "player";
                timings.TryGetValue(key, out MoveData move);
                styles.TryGetValue(key, out string style);
                rec.GalleryClip(key, kind, move, style);
            }
        }

        internal static IEnumerable<MoveData> AllMoves(ElementMoveSet m)
        {
            foreach (MoveData x in m.LightChain) yield return x;
            foreach (MoveData x in m.PauseChain) yield return x;
            yield return m.DodgeStrike;
            foreach (MoveData x in m.AirChain) yield return x;
            yield return m.Launcher;
            yield return m.AbilityNorth;
            yield return m.AbilityEast;
            yield return m.Heavy;
            yield return m.SprintAttack;
            yield return m.PlungeAttack;
            yield return m.Skill;
            yield return m.ZipStrike;
        }

        // ------------------------------------------------------------------ one scene: a world, fighters, a script

        internal enum Btn { Light, Heavy, Dodge, Jump, Guard, Skill, Zip, AbilityNorth, AbilityEast }

        internal struct AnimPad
        {
            public bool Light, Heavy, Dodge, Jump, Guard, Skill, Zip, AbilityNorth, AbilityEast;
            public Vector2 Move;
            public ElementId Element;   // picked this frame (RB + a face button), None otherwise

            public void Set(Btn b, bool down)
            {
                switch (b)
                {
                    case Btn.Light: Light = down; break;
                    case Btn.Heavy: Heavy = down; break;
                    case Btn.Dodge: Dodge = down; break;
                    case Btn.Jump: Jump = down; break;
                    case Btn.Guard: Guard = down; break;
                    case Btn.Skill: Skill = down; break;
                    case Btn.Zip: Zip = down; break;
                    case Btn.AbilityNorth: AbilityNorth = down; break;
                    case Btn.AbilityEast: AbilityEast = down; break;
                }
            }
        }

        internal sealed class Scene
        {
            readonly AnimRecorder rec;
            public readonly string Title;
            public readonly SimWorld World;
            public readonly List<AnimRig> Rigs = new List<AnimRig>();
            public AnimPad Pad;
            AnimPad last;

            public Scene(AnimRecorder rec, string title, float playerHealth = 0f)
            {
                this.rec = rec;
                Title = title;
                World = new SimWorld(SimLevel.Empty());
                Session.MakePreset(Preset.Fluid, out PlayerTuning tuning, out ElementLoadout loadout);
                if (playerHealth > 0f) tuning.MaxHealth = playerHealth;
                World.AddPlayer(tuning, loadout, Vector3.Zero, 0f);
                World.FixedCameraYaw = 0f;
                Rigs.Add(new AnimRig(World.Player, "player", 1f));
            }

            public PlayerCombatModel Model => World.Player.Model;

            public SimEnemy AddDummy(Vector3 at, float yaw)
            {
                SimEnemy e = World.AddEnemy(EnemyTuning.CreateSparringDummy(), at, yaw, 11 + Rigs.Count);
                Rigs.Add(new AnimRig(e, "dummy", 1f));
                return e;
            }

            // passive: slow to attack, so a scripted combo isn't interrupted (it still turns, flinches, falls, gets up).
            public SimEnemy AddSoldier(Vector3 at, float yaw, bool passive)
            {
                EnemyTuning t = EnemyTuning.CreateDaoSoldier();
                if (passive) Passive(t);
                t.AttackIntervalMin = Math.Min(t.AttackIntervalMin, passive ? 99f : 0.7f);
                t.AttackIntervalMax = passive ? 99f : 1.1f;
                SimEnemy e = World.AddEnemy(t, at, yaw, 21 + Rigs.Count);
                Rigs.Add(new AnimRig(e, "soldier", 1.04f));
                return e;
            }

            public SimEnemy AddCrossbowman(Vector3 at, float yaw, bool passive)
            {
                EnemyTuning t = EnemyTuning.CreateCrossbowman();
                if (passive) Passive(t);
                SimEnemy e = World.AddEnemy(t, at, yaw, 31 + Rigs.Count);
                Rigs.Add(new AnimRig(e, "crossbow", 1f));
                return e;
            }

            static void Passive(EnemyTuning t)
            {
                t.AttackIntervalMin = 99f;
                t.AttackIntervalMax = 99f;
                t.BreakOut.Enabled = false;
                t.ChaseSpeed = 0.5f;
                t.WalkSpeed = 0.5f;
                t.StrafeSpeed = 0.3f;
                t.RetreatSpeed = 0.5f;
            }

            public void Run(IEnumerable<int> script)
            {
                rec.BeginScene(Title);
                foreach (int _ in script)
                {
                }
            }

            public bool EnemyAttackStartedThisFrame(SimEnemy e)
            {
                for (int i = 0; i < e.FrameEvents.Count; i++) if (e.FrameEvents[i].Type == EnemyEventType.TelegraphStarted) return true;
                return false;
            }

            // One rendered frame: input -> the combat world -> the animation feeds -> the animators -> the recorder.
            public void Frame()
            {
                var input = new PlayerInputFrame
                {
                    Move = Pad.Move,
                    Light = ButtonState.From(Pad.Light, last.Light),
                    Heavy = ButtonState.From(Pad.Heavy, last.Heavy),
                    Dodge = ButtonState.From(Pad.Dodge, last.Dodge),
                    Jump = ButtonState.From(Pad.Jump, last.Jump),
                    Guard = ButtonState.From(Pad.Guard, last.Guard),
                    Skill = ButtonState.From(Pad.Skill, last.Skill),
                    ZipStrike = ButtonState.From(Pad.Zip, last.Zip),
                    AbilityNorth = ButtonState.From(Pad.AbilityNorth, last.AbilityNorth),
                    AbilityEast = ButtonState.From(Pad.AbilityEast, last.AbilityEast),
                    ElementSelect = Pad.Element,
                };
                Pad.Element = ElementId.None;   // a pick is one frame
                last = Pad;
                World.Step(input, Dt);
                float dt = World.LastGameDt;
                for (int i = 0; i < Rigs.Count; i++) Rigs[i].Update(World, dt);
                rec.Capture(this);
            }

            public IEnumerable<int> Idle(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    Frame();
                    yield return 0;
                }
            }

            public IEnumerable<int> Move(Vector2 stick, int frames)
            {
                Pad.Move = stick;
                foreach (int f in Idle(frames)) yield return f;
                Pad.Move = Vector2.Zero;
            }

            public IEnumerable<int> Tap(Btn b, int frames)
            {
                Pad.Set(b, true);
                foreach (int f in Idle(frames)) yield return f;
                Pad.Set(b, false);
            }

            public IEnumerable<int> Hold(Btn b, int frames)
            {
                return Tap(b, frames);
            }

            // The stick pushed 'degrees' round from straight at the nearest living enemy (0 = toward it, 180 = away, +90 = to
            // its right as you face it). The scenes use a fixed camera yaw of 0, so stick x/y are world x/z.
            public Vector2 StickTowardFoe(float degrees)
            {
                SimEnemy foe = World.Enemies.Where(e => e.IsAlive).OrderBy(e => Vector3.DistanceSquared(e.Feet, World.Player.Feet)).FirstOrDefault();
                Vector3 to = foe != null ? Directions.Flatten(foe.Feet - World.Player.Feet) : Vector3.UnitZ;
                float yaw = MathF.Atan2(to.X, to.Z) + degrees * AnimMath.Deg2Rad;
                return new Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
            }

            // Walks toward the nearest living enemy until within 'distance' (feet to feet), at most two seconds.
            public IEnumerable<int> WalkToFoe(float distance)
            {
                for (int i = 0; i < 120; i++)
                {
                    SimEnemy foe = World.Enemies.Where(e => e.IsAlive).OrderBy(e => Vector3.DistanceSquared(e.Feet, World.Player.Feet)).FirstOrDefault();
                    if (foe == null || Directions.Flatten(foe.Feet - World.Player.Feet).Length() <= distance) break;
                    Pad.Move = StickTowardFoe(0f) * 0.6f;
                    Frame();
                    yield return 0;
                }
                Pad.Move = Vector2.Zero;
            }

            // Switch element (hold RB + the element's button for a frame).
            public IEnumerable<int> Switch(ElementId element)
            {
                Pad.Element = element;
                return Idle(1);
            }

            public IEnumerable<int> WaitUntil(Func<bool> done, int maxFrames)
            {
                for (int i = 0; i < maxFrames && !done(); i++)
                {
                    Frame();
                    yield return 0;
                }
            }
        }

        // One fighter's animation: its feed, animator and forward kinematics.
        internal sealed class AnimRig
        {
            public readonly SimFighter Fighter;
            public readonly string Kind;
            public readonly FighterAnimator Animator;
            public readonly ForwardKinematics Fk;
            public readonly PlayerAnimationFeed PlayerFeed;
            public readonly EnemyAnimationFeed EnemyFeed;
            public readonly int Id;
            static int nextId;
            public FighterAnimInput LastInput;
            // Build 05 trace fields (player only): the running move's string branch and the grade of the press that started
            // it, and the running dodge's kind.
            public ComboBranch Branch;
            public BeatGrade Grade;
            public DodgeKind Dodge;

            public AnimRig(SimFighter fighter, string kind, float scale)
            {
                Fighter = fighter;
                Kind = kind;
                Id = nextId++;
                ByFighter[fighter] = this;
                HumanoidSkeleton skeleton = HumanoidSkeleton.Create(null, scale);
                Animator = new FighterAnimator(PoseLibrary.Default, skeleton, StyleOf(kind)) { PropLength = AnimRecorder.PropLength(kind) };
                Fk = new ForwardKinematics(skeleton);
                if (fighter is SimPlayer) PlayerFeed = new PlayerAnimationFeed();
                else EnemyFeed = new EnemyAnimationFeed();
            }

            // Same rule as PlayerController: the lock-on target, else the soft lock, else the nearest living enemy.
            public static SimFighter StrikeTarget(SimWorld world, SimPlayer p)
            {
                if (p.LockTarget != null && p.LockTarget.IsAlive) return p.LockTarget;
                if (p.SoftTarget != null && p.SoftTarget.IsAlive) return p.SoftTarget;
                SimFighter best = null;
                float bestDistance = NearestTargetRange;
                foreach (SimEnemy e in world.Enemies)
                {
                    if (!e.IsAlive) continue;
                    float d = Vector3.Distance(e.Feet, p.Feet);
                    if (d < bestDistance)
                    {
                        best = e;
                        bestDistance = d;
                    }
                }
                return best;
            }

            public const float NearestTargetRange = 8f;   // PlayerController uses the same

            public static readonly Dictionary<SimFighter, AnimRig> ByFighter = new Dictionary<SimFighter, AnimRig>();

            static Vector3 ChestOf(SimFighter f)
            {
                return ByFighter.TryGetValue(f, out AnimRig rig) && rig.HasPose ? rig.Fk[BodyJoint.Chest] : f.AimPoint;
            }

            public bool HasPose;

            public static string StyleOf(string kind)
            {
                switch (kind)
                {
                    case "soldier": return "sword";
                    case "crossbow": return "crossbow";
                    case "dummy": return "dummy";
                    default: return "";
                }
            }

            public void Update(SimWorld world, float dt)
            {
                if (Fighter is SimPlayer p)
                {
                    for (int i = 0; i < p.FrameEvents.Count; i++)
                    {
                        PlayerEvent e = p.FrameEvents[i];
                        PlayerFeed.OnEvent(e);
                        if (e.Type == PlayerEventType.AttackStarted)
                        {
                            Branch = e.Branch;
                            Grade = e.Grade;
                        }
                        else if (e.Type == PlayerEventType.DodgeStarted) Dodge = e.DodgeKind;
                    }
                    SimFighter target = StrikeTarget(world, p);
                    // Aim at where the target's body really is (a launched enemy lies flat, well below its capsule's
                    // chest height): its chest as last animated, like PlayerController reading HumanoidBody.ChestAnchor.
                    Vector3 chest = target != null ? ChestOf(target) : Vector3.Zero;
                    // The floor under the feet (PlayerController casts a ray down): the legs reach for it as it comes (J6-01).
                    float ground = world.Level.GroundHeight(p.Feet.X, p.Feet.Z, p.Feet.Y, 0f);
                    float floorBelow = float.IsNegativeInfinity(ground) ? -1f : Math.Max(0f, p.Feet.Y - ground);
                    LastInput = PlayerFeed.Build(p.Model, dt, target != null, p.Feet, chest, floorBelow);
                }
                else if (Fighter is SimEnemy e)
                {
                    for (int i = 0; i < e.FrameEvents.Count; i++) EnemyFeed.OnEvent(e.FrameEvents[i]);
                    float aim = 0f;
                    if (world.Player != null)
                    {
                        Vector3 to = world.Player.AimPoint - (e.Feet + new Vector3(0f, 1.42f, 0f));
                        aim = Directions.PitchOf(to);
                    }
                    LastInput = EnemyFeed.Build(e.Brain, dt, e.Planted || e.Controller.IsGrounded, aim, e.Planted);
                }
                Animator.Update(LastInput);
                Fk.Compute(Animator.Pose, Fighter.Feet, Fighter.Yaw);
                HasPose = true;
            }
        }

        // ------------------------------------------------------------------ recording and the JSON file

        internal sealed class AnimRecorder
        {
            readonly StringBuilder frames = new StringBuilder(1 << 22);
            public int FrameCount;
            string scene = "";
            double time;
            public readonly HashSet<string> Covered = new HashSet<string>();
            readonly List<string> fx = new List<string>();
            readonly List<(string move, float ext)> extensions = new List<(string, float)>();
            readonly List<(string move, float tip, float reach)> swordReach = new List<(string, float, float)>();
            static readonly CultureInfo C = CultureInfo.InvariantCulture;

            public void BeginScene(string title)
            {
                scene = title;
            }

            // Build 05 verify round 2 (R2-02): a lunge must not drag a planted rear foot out behind at hip height and then
            // snap it to the floor. Player only, root on the floor, kicks (a leg in reach mode) left out.
            const float DragHeight = 0.35f, DragBehind = 0.35f, SnapDrop = 0.30f;
            const int DragFramesAllowed = 3;
            static readonly HashSet<string> LandingKeys = new HashSet<string>
            {
                AnimationKeys.Land, AnimationKeys.AirLanding, AnimationKeys.Launched, AnimationKeys.Knockdown,
                AnimationKeys.Death, AnimationKeys.GetUp,
            };
            readonly List<(string scene, int frame, string key, string what, float amount)> footIssues
                = new List<(string, int, string, string, float)>();
            readonly float[] prevFootY = new float[2];
            readonly int[] dragRun = new int[2];
            readonly float[] dragMax = new float[2];
            readonly string[] dragKey = new string[2];
            readonly int[] dragStart = new int[2];
            bool prevFootValid;
            string prevFootScene;
            int footFrames;

            // Build 05 verify round 6 (J6-01): landings are no longer exempt from the snap check. For TouchdownFrames frames
            // after the body touches down (after TouchdownAirFrames+ frames in the air), in a plunge, and in the land
            // poses, a foot may drop at most LandingDrop relative to the root in one frame (the touchdown frame itself
            // included: it used to pull the legs 0.26-1.22 m down at once). Only a body lying down (knockdown, death,
            // get-up, launched) keeps the exemption.
            const float LandingDrop = 0.15f;
            const int TouchdownFrames = 12, TouchdownAirFrames = 3;
            static readonly HashSet<string> LyingKeys = new HashSet<string>
            {
                AnimationKeys.Launched, AnimationKeys.Knockdown, AnimationKeys.Death, AnimationKeys.GetUp,
            };
            int footAirRun, touchdownLeft;
            // Build 05 verify round 7 (J7-02): a foot near the floor never jumps across it. Grounded, not lying, not a
            // kick, a foot below TeleportHeight in this frame or the last may move at most TeleportStep horizontally
            // (world) in one frame (every dodge start used to leave both feet planted, then move them 0.4-0.7 m in the next frame).
            const float TeleportHeight = 0.2f, TeleportStep = 0.3f;
            readonly Vector3[] prevFootWorld = new Vector3[2];
            readonly float[] prevFootHeight = new float[2];
            int teleportChecked;

            void CheckFeet(AnimRig rig, string key)
            {
                bool grounded = rig.LastInput.Grounded && !rig.LastInput.Dead;
                bool plunging = rig.Fighter is SimPlayer pl && pl.Model.State == PlayerState.Plunging;
                bool lying = LyingKeys.Contains(key);
                bool sameScene = prevFootValid && prevFootScene == scene;
                if (!sameScene)
                {
                    footAirRun = 0;
                    touchdownLeft = 0;
                }
                if (!grounded) footAirRun++;
                else
                {
                    if (footAirRun >= TouchdownAirFrames) touchdownLeft = TouchdownFrames;
                    footAirRun = 0;
                }
                bool landing = touchdownLeft > 0 || plunging || LandingKeys.Contains(key);
                Vector3 hips = rig.Fk.Positions[(int)BodyJoint.Hips];
                Vector3 fwd = Directions.FromYaw(rig.Fighter.Yaw);
                PoseSpec spec = rig.Animator.CurrentSpec;
                for (int i = 0; i < 2; i++)
                {
                    BodySide side = i == 0 ? BodySide.Left : BodySide.Right;
                    Vector3 foot = rig.Fk.Positions[(int)(i == 0 ? BodyJoint.LeftFoot : BodyJoint.RightFoot)];
                    float height = foot.Y - rig.Fighter.Feet.Y;
                    Vector3 rel = foot - hips;
                    float behind = -(rel.X * fwd.X + rel.Z * fwd.Z);
                    bool kick = spec[PoseSpec.Leg(side, 7)] > 0.5f;
                    bool drag = grounded && !kick && height > DragHeight && behind > DragBehind;
                    if (drag)
                    {
                        if (dragRun[i] == 0)
                        {
                            dragKey[i] = key;
                            dragStart[i] = FrameCount;
                            dragMax[i] = 0f;
                        }
                        dragRun[i]++;
                        dragMax[i] = Math.Max(dragMax[i], behind);
                    }
                    else
                    {
                        if (dragRun[i] > DragFramesAllowed)
                            footIssues.Add((scene, dragStart[i], dragKey[i], "rear foot dragged " + dragRun[i] + " frames", dragMax[i]));
                        dragRun[i] = 0;
                    }
                    if (sameScene && grounded && !lying && !kick && Math.Min(prevFootHeight[i], height) < TeleportHeight)
                    {
                        teleportChecked++;
                        float step = new Vector2(foot.X - prevFootWorld[i].X, foot.Z - prevFootWorld[i].Z).Length();
                        if (step > TeleportStep) footIssues.Add((scene, FrameCount, key, "foot slid along the floor in one frame", step));
                    }
                    prevFootWorld[i] = foot;
                    prevFootHeight[i] = grounded && !lying ? height : 10f;
                    if (sameScene && grounded && !lying && prevFootY[i] - height > (landing ? LandingDrop : SnapDrop))
                        footIssues.Add((scene, FrameCount, key, landing ? "foot dropped in one frame landing" : "foot dropped in one frame",
                            prevFootY[i] - height));
                    // A frame in the air counts as the "before" of a touchdown (root-relative), and of nothing else.
                    prevFootY[i] = grounded || touchdownLeft > 0 || footAirRun > 0 ? height : -10f;
                }
                if (grounded && touchdownLeft > 0) touchdownLeft--;
                prevFootValid = true;
                prevFootScene = scene;
                footFrames++;
            }

            // Build 05 verify round 3. While the player attacks:
            //   J3-02: no joint jumps more than JointJumpMax in one frame (measured against the body's own travel), and
            //          a clip change during a fast spin (the hip line turning over SpinBefore degrees in the 3 frames
            //          before) doesn't turn it back more than SpinReverseMax degrees in the next 5 frames;
            //   J3-03: no glide: both feet under GlideHeight and each moving faster than GlideSpeed, with the body
            //          travelling, for GlideFrames frames or more on the ground (a clip that Glides, a surf, excepted).
            const float JointJumpMax = 0.8f, SpinBefore = 45f, SpinReverseMax = 60f;
            const float GlideHeight = 0.16f, GlideSpeed = 2f, GlideRootSpeed = 1f;
            const int GlideFrames = 3;
            // Build 05 verify round 4 (J4-02), whatever the feet's height:
            //   no statue slide: a grounded strike covering ground faster than SlideSpeed for SlideFrames frames or more
            //          while neither foot moves SlideFootTravel relative to the hips (one frozen pose sliding along);
            //   no whiplash: the body's horizontal velocity changing more than ReversalMax m/s in one frame as a strike
            //          runs (the dodge strike flipping from -17.6 to +20 m/s, or a 20 m/s dash stopping dead).
            const float SlideSpeed = 12f, SlideFootTravel = 0.2f, ReversalMax = 20f;
            // Build 05 verify round 5 (J5-04), any state: no floating on the ground. The body grounded (alive, not lying)
            // with both feet more than FloatHeight above the floor for FloatFrames frames or more, outside a clip that
            // Leaps on purpose (zip kick, sprint kick, wind leap, wind runner kick): an air finisher or plunge landing in
            // its air pose. (The touchdown frame itself is one frame: the model reports grounded on the next tick.)
            const float FloatHeight = 0.2f;
            const int FloatFrames = 3;
            int floatRun, floatStart, floatChecked;
            // (MV-03) walking, running and standing: the pelvis height above the feet' floor changes at most HipStepMax
            // in one frame (Earth used to drop 9.5 cm into its horse stance on every stop).
            const float HipStepMax = 0.05f;
            int hipChecked;
            float prevHipHeight = float.NaN;
            string prevHipKey;
            string floatKeys;
            float floatWorst;
            const int SlideFrames = 6;
            int slideRun, slideStart;
            string slideKey;
            Vector3 slideLeft0, slideRight0;
            float slideLeftMax, slideRightMax;
            Vector3 prevVelocity;
            bool prevVelocityValid;
            readonly List<(string scene, int frame, string key, string what, float amount)> motionIssues
                = new List<(string, int, string, string, float)>();
            readonly Vector3[] prevJoints = new Vector3[BodyJoints.Count];
            Vector3 prevRoot;
            bool prevAttacking;
            string prevMotionScene, prevMotionKey;
            readonly List<float> hipHistory = new List<float>();
            int motionFrames, glideRun, glideStart, spinCheckUntil = -1, spinSign;
            float spinTurned, spinWorst;
            string glideKey, spinKeys;

            static float HipLine(Vector3[] joints)
            {
                Vector3 l = joints[(int)BodyJoint.LeftUpperLeg], r = joints[(int)BodyJoint.RightUpperLeg];
                return (float)(Math.Atan2(r.Z - l.Z, r.X - l.X) * 180.0 / Math.PI);
            }

            static float Wrap180(float a)
            {
                a %= 360f;
                if (a > 180f) a -= 360f;
                if (a < -180f) a += 360f;
                return a;
            }

            void CheckMotion(AnimRig rig, string key)
            {
                var player = (SimPlayer)rig.Fighter;
                bool attacking = player.Model.State == PlayerState.Attacking;
                bool same = prevMotionScene == scene;
                Vector3[] joints = rig.Fk.Positions;
                Vector3 root = rig.Fighter.Feet;
                float hip = HipLine(joints);
                if (!same) hipHistory.Clear();
                if (same && attacking && prevAttacking)
                {
                    motionFrames++;
                    Vector3 travel = root - prevRoot;
                    float worst = 0f;
                    for (int j = 0; j < joints.Length; j++) worst = Math.Max(worst, Vector3.Distance(joints[j] - travel, prevJoints[j]));
                    if (worst > JointJumpMax) motionIssues.Add((scene, FrameCount, key, "joint jumped in one frame", worst));

                    // A clip change in a fast spin: watch the next frames for the spin turning back.
                    if (key != prevMotionKey && hipHistory.Count >= 4)
                    {
                        float before = 0f;
                        for (int k = hipHistory.Count - 3; k < hipHistory.Count; k++) before += Wrap180(hipHistory[k] - hipHistory[k - 1]);
                        if (Math.Abs(before) >= SpinBefore)
                        {
                            spinCheckUntil = FrameCount + 5;
                            spinSign = Math.Sign(before);
                            spinTurned = 0f;
                            spinWorst = 0f;
                            spinKeys = prevMotionKey + " -> " + key;
                        }
                    }
                    if (FrameCount < spinCheckUntil && hipHistory.Count > 0)
                    {
                        spinTurned += Wrap180(hip - hipHistory[hipHistory.Count - 1]);
                        spinWorst = Math.Max(spinWorst, -spinSign * spinTurned);
                        if (FrameCount == spinCheckUntil - 1 && spinWorst > SpinReverseMax)
                            motionIssues.Add((scene, FrameCount, spinKeys, "spin turned back (degrees)", spinWorst));
                    }

                    // Gliding: both feet low and sliding with the body.
                    PoseClip clip = rig.Animator.Clip(key);
                    float dt = Dt;
                    Vector3 lf = joints[(int)BodyJoint.LeftFoot], rf = joints[(int)BodyJoint.RightFoot];
                    float lv = Vector3.Distance(lf, prevJoints[(int)BodyJoint.LeftFoot]) / dt;
                    float rv = Vector3.Distance(rf, prevJoints[(int)BodyJoint.RightFoot]) / dt;
                    float rootSpeed = new Vector2(travel.X, travel.Z).Length() / dt;
                    bool glide = rig.LastInput.Grounded && (clip == null || !clip.Glides)
                                 && lf.Y - root.Y < GlideHeight && rf.Y - root.Y < GlideHeight
                                 && lv > GlideSpeed && rv > GlideSpeed && rootSpeed > GlideRootSpeed;
                    if (glide && glideRun > 0 && key == glideKey) glideRun++;
                    else
                    {
                        if (glideRun >= GlideFrames) motionIssues.Add((scene, glideStart, glideKey, "both feet glided " + glideRun + " frames", 0f));
                        glideRun = glide ? 1 : 0;
                        glideKey = key;
                        glideStart = FrameCount;
                    }
                }
                else
                {
                    if (glideRun >= GlideFrames) motionIssues.Add((scene, glideStart, glideKey, "both feet glided " + glideRun + " frames", 0f));
                    glideRun = 0;
                    spinCheckUntil = -1;
                }
                CheckGait(rig, key, same, joints, root);
                CheckSlide(rig, key, attacking, same, joints, root);
                CheckFloating(rig, key, same, joints, root);
                CheckHipHeight(rig, key, same, joints, root);
                hipHistory.Add(hip);
                if (hipHistory.Count > 8) hipHistory.RemoveAt(0);
                Array.Copy(joints, prevJoints, prevJoints.Length);
                prevRoot = root;
                prevAttacking = attacking;
                prevMotionScene = scene;
                prevMotionKey = key;
            }

            // Build 05 verify round 6 (J6-02), walking and running on the ground (Locomotion / Sprinting): travelling within
            // GaitStraightAngle of the facing at over GaitMinSpeed, no 'strafe' key and (run or strafe) no stance wider than
            // GaitMaxSpread across the body (walking back in after an evade-out used to crab sideways with the feet 0.93 m
            // apart); and never GaitLiftFrames+ frames with both feet over GaitLiftHeight off the floor (measured at the
            // sole: the ankle sits 8 cm up on a flat foot).
            const float GaitStraightAngle = 30f, GaitMinSpeed = 1f, GaitMaxSpread = 0.6f, GaitLiftHeight = 0.1f;
            const int GaitLiftFrames = 4;
            int gaitChecked, gaitLiftRun, gaitLiftStart;
            string gaitLiftKeys;

            void CheckGait(AnimRig rig, string key, bool same, Vector3[] joints, Vector3 root)
            {
                var player = (SimPlayer)rig.Fighter;
                bool loco = (player.Model.State == PlayerState.Locomotion || player.Model.State == PlayerState.Sprinting)
                            && rig.LastInput.Grounded && same;
                Vector3 lf = joints[(int)BodyJoint.LeftFoot], rf = joints[(int)BodyJoint.RightFoot];
                if (loco)
                {
                    gaitChecked++;
                    Vector3 travel = root - prevRoot;
                    travel.Y = 0f;
                    float speed = travel.Length() / Dt;
                    Vector3 fwd = Directions.FromYaw(rig.Fighter.Yaw);
                    if (speed > GaitMinSpeed)
                    {
                        float cos = Vector3.Dot(Vector3.Normalize(travel), fwd);
                        bool straight = cos >= (float)Math.Cos(GaitStraightAngle * Math.PI / 180.0);
                        if (straight && key == AnimationKeys.Strafe)
                            motionIssues.Add((scene, FrameCount, key, "gait: strafe key while moving straight (m/s)", speed));
                        var right = new Vector3(fwd.Z, 0f, -fwd.X);
                        float spread = Math.Abs(Vector3.Dot(lf - rf, right));
                        if (straight && (key == AnimationKeys.Strafe || key == AnimationKeys.Run) && spread > GaitMaxSpread)
                            motionIssues.Add((scene, FrameCount, key, "gait: feet spread across the body while moving straight (m)", spread));
                    }
                }
                float lowest = Math.Min(Math.Min(lf.Y - 0.08f, rf.Y - 0.08f),
                                        Math.Min(joints[(int)BodyJoint.LeftToes].Y - 0.02f, joints[(int)BodyJoint.RightToes].Y - 0.02f)) - root.Y;
                if (loco && lowest > GaitLiftHeight)
                {
                    if (gaitLiftRun == 0)
                    {
                        gaitLiftStart = FrameCount;
                        gaitLiftKeys = key;
                    }
                    else if (!gaitLiftKeys.EndsWith(key)) gaitLiftKeys += " -> " + key;
                    gaitLiftRun++;
                    return;
                }
                if (gaitLiftRun >= GaitLiftFrames)
                    motionIssues.Add((scene, gaitLiftStart, gaitLiftKeys, "gait: both feet off the floor for " + gaitLiftRun + " frames", 0f));
                gaitLiftRun = 0;
            }

            void CheckHipHeight(AnimRig rig, string key, bool same, Vector3[] joints, Vector3 root)
            {
                bool loco = key == AnimationKeys.Idle || FighterAnimator.IsGaitKey(key);
                float height = joints[(int)BodyJoint.Hips].Y - root.Y;
                bool grounded = rig.LastInput.Grounded;
                if (same && loco && grounded && prevHipKey != null && !float.IsNaN(prevHipHeight))
                {
                    hipChecked++;
                    float step = Math.Abs(height - prevHipHeight);
                    if (step > HipStepMax) motionIssues.Add((scene, FrameCount, prevHipKey + " -> " + key, "hips moved up or down in one frame (m)", step));
                }
                prevHipHeight = height;
                prevHipKey = loco && grounded ? key : null;
            }

            void CheckFloating(AnimRig rig, string key, bool same, Vector3[] joints, Vector3 root)
            {
                FighterAnimInput input = rig.LastInput;
                PoseClip clip = rig.Animator.Clip(key);
                bool lying = key == AnimationKeys.Knockdown || key == AnimationKeys.GetUp || key == AnimationKeys.Death || key == AnimationKeys.Launched;
                bool check = input.Grounded && !input.Dead && rig.Fighter.IsAlive && !lying && (clip == null || !clip.Leaps);
                float lowest = Math.Min(Math.Min(joints[(int)BodyJoint.LeftFoot].Y, joints[(int)BodyJoint.RightFoot].Y),
                                        Math.Min(joints[(int)BodyJoint.LeftToes].Y, joints[(int)BodyJoint.RightToes].Y)) - root.Y;
                if (check) floatChecked++;
                bool floating = check && same && lowest > FloatHeight;
                if (floating)
                {
                    if (floatRun == 0)
                    {
                        floatStart = FrameCount;
                        floatKeys = key;
                        floatWorst = 0f;
                    }
                    else if (!floatKeys.EndsWith(key)) floatKeys += " -> " + key;
                    floatRun++;
                    floatWorst = Math.Max(floatWorst, lowest);
                    return;
                }
                if (floatRun >= FloatFrames) motionIssues.Add((scene, floatStart, floatKeys, "floated: both feet up for " + floatRun + " frames, highest (m)", floatWorst));
                floatRun = 0;
            }

            void CheckSlide(AnimRig rig, string key, bool attacking, bool same, Vector3[] joints, Vector3 root)
            {
                Vector3 velocity = same ? (root - prevRoot) / Dt : Vector3.Zero;
                velocity.Y = 0f;
                if (same && attacking && prevVelocityValid)
                {
                    float change = Vector3.Distance(velocity, prevVelocity);
                    if (change > ReversalMax) motionIssues.Add((scene, FrameCount, key, "speed changed in one frame (m/s)", change));
                }
                prevVelocity = velocity;
                prevVelocityValid = same;

                Vector3 hips = joints[(int)BodyJoint.Hips];
                Vector3 left = joints[(int)BodyJoint.LeftFoot] - hips, right = joints[(int)BodyJoint.RightFoot] - hips;
                bool sliding = same && attacking && rig.LastInput.Grounded && velocity.Length() > SlideSpeed;
                if (sliding && slideRun > 0 && key == slideKey)
                {
                    slideRun++;
                    slideLeftMax = Math.Max(slideLeftMax, Vector3.Distance(left, slideLeft0));
                    slideRightMax = Math.Max(slideRightMax, Vector3.Distance(right, slideRight0));
                    return;
                }
                EndSlide();
                if (!sliding) return;
                slideRun = 1;
                slideKey = key;
                slideStart = FrameCount;
                slideLeft0 = left;
                slideRight0 = right;
                slideLeftMax = slideRightMax = 0f;
            }

            void EndSlide()
            {
                if (slideRun >= SlideFrames && slideLeftMax < SlideFootTravel && slideRightMax < SlideFootTravel)
                    motionIssues.Add((scene, slideStart, slideKey, "slid " + slideRun + " frames over " + Out.N(SlideSpeed, 0)
                        + " m/s with frozen legs (feet moved, m)", Math.Max(slideLeftMax, slideRightMax)));
                slideRun = 0;
            }

            // R2-03: no Earth rock effect (a burst or a spark) traced while the player is off the ground.
            int earthAirFx, earthAirRockFx;

            void CheckEarthInAir(Scene s)
            {
                foreach (AnimRig rig in s.Rigs)
                {
                    if (!(rig.Fighter is SimPlayer) || rig.LastInput.Grounded) continue;
                    string tag = "\"fighter\":" + rig.Id + ",";
                    string earth = "\"el\":\"earth\"";
                    foreach (string f in fx)
                    {
                        // The player's own effects, and the hit sparks it makes on enemies.
                        bool hitSpark = f.Contains("\"key\":\"spark\"") || f.Contains("\"key\":\"dust\"");
                        if (!(f.Contains(tag) || hitSpark) || !f.Contains(earth)) continue;
                        earthAirFx++;
                        if (f.Contains("\"key\":\"burst\"") || f.Contains("\"key\":\"spark\"")) earthAirRockFx++;
                    }
                }
            }

            public void Capture(Scene s)
            {
                fx.Clear();
                string caption = CaptionFor(s);
                foreach (AnimRig rig in s.Rigs)
                {
                    if (rig.Fighter is SimPlayer p)
                    {
                        foreach (PlayerEvent e in p.FrameEvents) PlayerEffect(rig, e, s);
                    }
                    else if (rig.Fighter is SimEnemy en)
                    {
                        foreach (EnemyEvent e in en.FrameEvents) EnemyEffect(rig, en, e, s.World.Player);
                    }
                }
                CheckEarthInAir(s);
                CheckEarthOnBody(s);
                var sb = frames;
                if (FrameCount > 0) sb.Append(",\n");
                sb.Append("{\"t\":").Append(F(time)).Append(",\"scene\":").Append(Q(scene)).Append(",\"caption\":").Append(Q(caption));
                sb.Append(",\"fighters\":[");
                for (int i = 0; i < s.Rigs.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendFighter(sb, s.Rigs[i]);
                }
                sb.Append("],\"fx\":[").Append(string.Join(",", fx)).Append("],\"proj\":[");
                bool first = true;
                foreach (SimProjectiles.Projectile pr in s.World.Projectiles.Flying)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('[').Append(F(pr.Position.X)).Append(',').Append(F(pr.Position.Y)).Append(',').Append(F(pr.Position.Z)).Append(',').Append(pr.IsFire ? 1 : 0)
                        .Append(',').Append(Q(pr.IsFire ? ElementName(pr.Damage.Element) : "")).Append(']');
                }
                sb.Append("]}");
                FrameCount++;
                time += Dt;
            }

            void AppendFighter(StringBuilder sb, AnimRig rig)
            {
                AnimationCue action = rig.Animator.ActionCue;
                AnimationCue loco = rig.Animator.LocomotionCue;
                string key = action.IsValid ? action.Key : loco.Key;
                Covered.Add(key);
                if (loco.IsValid) Covered.Add(loco.Key);
                if (rig.Fighter is SimPlayer)
                {
                    CheckFeet(rig, key);
                    CheckMotion(rig, key);
                }
                string state = rig.Fighter is SimPlayer p ? p.Model.State.ToString() : ((SimEnemy)rig.Fighter).Brain.State.ToString();
                sb.Append("{\"id\":").Append(rig.Id).Append(",\"kind\":").Append(Q(rig.Kind)).Append(",\"state\":").Append(Q(state));
                sb.Append(",\"key\":").Append(Q(key)).Append(",\"alive\":").Append(rig.Fighter.IsAlive ? "true" : "false");
                if (rig.Fighter is SimPlayer player)
                {
                    PlayerCombatModel m = player.Model;
                    sb.Append(",\"el\":").Append(Q(ElementName(m.ActiveElement)));
                    bool attacking = m.State == PlayerState.Attacking;
                    sb.Append(",\"branch\":").Append(Q(attacking ? rig.Branch.ToString() : ""));
                    sb.Append(",\"grade\":").Append(Q(attacking ? rig.Grade.ToString() : ""));
                    sb.Append(",\"dodge\":").Append(Q(m.State == PlayerState.Dodging ? rig.Dodge.ToString() : ""));
                    sb.Append(",\"combo\":").Append(m.ComboCount).Append(",\"mix\":").Append(m.MixLevel);
                }
                sb.Append(",\"yaw\":").Append(F(rig.Fighter.Yaw));
                Vector3 feet = rig.Fighter.Feet;
                sb.Append(",\"pos\":[").Append(F(feet.X)).Append(',').Append(F(feet.Y)).Append(',').Append(F(feet.Z)).Append(']');
                sb.Append(",\"j\":[");
                for (int j = 0; j < BodyJoints.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    Vector3 v = rig.Fk.Positions[j];
                    sb.Append(F(v.X)).Append(',').Append(F(v.Y)).Append(',').Append(F(v.Z));
                }
                sb.Append(']');
                if (rig.Kind != "player")
                {
                    rig.Fk.Prop(rig.Animator.Pose, out Vector3 grip, out Vector3 dir);
                    sb.Append(",\"prop\":[").Append(F(grip.X)).Append(',').Append(F(grip.Y)).Append(',').Append(F(grip.Z)).Append(',')
                        .Append(F(dir.X)).Append(',').Append(F(dir.Y)).Append(',').Append(F(dir.Z)).Append(',').Append(F(PropLength(rig.Kind))).Append(']');
                }
                sb.Append('}');
            }

            public static float PropLength(string kind)
            {
                switch (kind)
                {
                    case "soldier": return SwordLength;
                    case "dummy": return 1.1f;
                    case "crossbow": return 0.55f;
                    default: return 0f;
                }
            }

            // The dao's hand-to-tip length (EnemyPoseSettings.WeaponLength in Unity).
            public const float SwordLength = EnemyPoseSettingsWeaponLength;
            const float EnemyPoseSettingsWeaponLength = 1.3f;   // keep equal to EnemyPoseSettings.DefaultWeaponLength (Unity side)

            void PlayerEffect(AnimRig rig, in PlayerEvent e, Scene scene)
            {
                switch (e.Type)
                {
                    case PlayerEventType.AttackActiveStart:
                    {
                        MoveData m = e.Move;
                        if (m == null) break;
                        string key = string.IsNullOrEmpty(m.EffectKey) ? EffectKeys.Burst : m.EffectKey;
                        Vector3 dir = e.Direction;
                        float duration = Math.Max(0.2f, m.Active + 0.15f);
                        // As PlayerFeedback: the launcher's column rises under the launched enemy (EnemyEffect), the slam's
                        // ring appears where the enemy lands; at the strike itself they're a burst from the limb.
                        string el = ElementName(e.Element);
                        // As ElementMoveEffects: Earth in the air pushes dust from the limb, no rock (ElementFxRules).
                        if (ElementFxRules.DustOnly(e.Element, ElementFxRules.IsAirborne(in e))) key = "dust";
                        // As ElementBurst (J3-07): Earth's burst from a limb is dust there, its rock rises from the floor under it.
                        if (key == EffectKeys.Pillar || key == EffectKeys.Slam)
                            BodyBurst(EffectKeys.Burst, e.Element, e.Origin, key == EffectKeys.Slam ? -Vector3.UnitY : dir, m.Range, m.ArcDegrees, duration, LimbJoint(m.Limb), rig.Id);
                        else if (key == EffectKeys.Burst) BodyBurst(key, e.Element, e.Origin, dir, m.Range, m.ArcDegrees, duration, LimbJoint(m.Limb), rig.Id);
                        else
                        {
                            fx.Add(Fx(key, e.Origin, dir, m.Range, m.ArcDegrees, duration, LimbJoint(m.Limb), rig.Id, el));
                            if (key == EffectKeys.Trail) BodyBurst(null, e.Element, e.Origin, dir, m.Range, m.ArcDegrees, duration, LimbJoint(m.Limb), rig.Id);
                        }
                        if (key != EffectKeys.Burst && key != EffectKeys.Trail && key != "dust")
                            fx.Add(Fx(EffectKeys.Trail, e.Origin, dir, m.Range, m.ArcDegrees, m.Active + 0.05f, LimbJoint(m.Limb), rig.Id, el));
                        // A flurry's later sub-hits change hands (Air's palm changes): the striking hand is whichever is out.
                        bool laterSubHit = e.MoveInstanceId != 0 && e.AttackId != e.MoveInstanceId;
                        float ext = laterSubHit && m.Limb != Limb.LeftFoot && m.Limb != Limb.RightFoot
                            ? Math.Max(StrikeExtension(rig, Limb.LeftFist), StrikeExtension(rig, Limb.RightFist))
                            : StrikeExtension(rig, m.Limb);
                        extensions.Add((m.DisplayName, ext));
                        MeasureAim(rig, m, scene);
                        break;
                    }
                    case PlayerEventType.ProjectileLaunched:
                        fx.Add(Fx("muzzle", e.Origin, e.Direction, 1f, 0f, 0.15f, (int)BodyJoint.RightHand, rig.Id, ElementName(e.Element)));
                        if (ElementFxRules.StoneFromFloor(e.Element) && !ElementFxRules.IsAirborne(in e) && rig.LastInput.Grounded)
                        {
                            // J4-03: a grounded Earth throw's boulder must have risen out of the floor under it over the
                            // wind-up (PlayerFeedback -> ElementVfx.RaiseStone), and the floor it left kicks up grit (the
                            // Earth muzzle's FloorStone).
                            earthThrows++;
                            if (raiseStoneFrame >= 0 && raiseStoneFighter == rig.Id && e.Move != null
                                && FrameCount - raiseStoneFrame <= (int)Math.Ceiling((e.Move.Startup / Math.Max(0.05f, e.PlaybackRate) + 0.25f) / Dt)
                                && Directions.Flatten(raiseStonePoint - e.Origin).Length() < 0.6f)
                                earthThrowsFromFloor++;
                            BodyBurst(null, e.Element, e.Origin, e.Direction, 1f, 0f, 0.3f, (int)BodyJoint.RightHand, rig.Id);
                            raiseStoneFrame = -1;
                        }
                        extensions.Add((e.Move != null ? e.Move.DisplayName : "projectile", StrikeExtension(rig, e.Move != null ? e.Move.Limb : Limb.BothFists)));
                        break;
                    case PlayerEventType.PlungeImpact:
                    {
                        // As PlayerFeedback.PlungeImpact (round 7, S7-18): a ring and an explosion at the feet (drawn as the
                        // slam: ring + ball) and a burst down from the right foot, whatever the move's EffectKey says.
                        string el = ElementName(e.Element != ElementId.None ? e.Element : ((SimPlayer)rig.Fighter).Model.ActiveElement);
                        fx.Add(Fx(EffectKeys.Slam, e.Origin, -Vector3.UnitY, e.Radius, 360f, 0.45f, (int)BodyJoint.RightFoot, rig.Id, el));
                        fx.Add(Fx(EffectKeys.Burst, e.Origin, -Vector3.UnitY, Math.Max(0.5f, e.Radius * 0.3f), 0f, 0.3f, (int)BodyJoint.RightFoot, rig.Id, el));
                        break;
                    }
                    case PlayerEventType.DodgeStarted:
                        // As PlayerFeedback.DodgeStarted: a push from the feet (BurstOrDust), then jets; nothing for a dodge
                        // that doesn't travel (a slip-in from contact range, J3-S04).
                        if (!e.InAir && e.Amount < 0.3f) break;
                        fx.Add(Fx(ElementFxRules.DustOnly(e.Element, e.InAir) ? "dust" : EffectKeys.Burst, rig.Fighter.Feet, -e.Direction, 1f, 0f, 0.2f,
                            (int)BodyJoint.RightFoot, rig.Id, ElementName(e.Element)));
                        fx.Add(Fx("jet", rig.Fighter.Feet, -e.Direction, 1f, 0f, 0.3f, (int)BodyJoint.RightFoot, rig.Id, ElementName(e.Element)));
                        break;
                    case PlayerEventType.AttackStarted:
                        if (e.AttackKind == PlayerAttackKind.ZipStrike && e.Move != null)
                            fx.Add(Fx("jet", rig.Fighter.Feet, -Directions.FromYaw(rig.Fighter.Yaw), 1f, 0f, e.Move.Startup + e.Move.Active, (int)BodyJoint.RightFoot, rig.Id,
                                ElementName(e.Element)));
                        // As PlayerFeedback.DodgeStrikeDashStarted (J4-02): a push off the floor and the element trailing
                        // from the feet for the dash back in (in the air: the air dash's jets).
                        if (e.AttackKind == PlayerAttackKind.DodgeStrike && e.Move != null)
                        {
                            bool air = ElementFxRules.IsAirborne(in e) || !rig.LastInput.Grounded;
                            Vector3 back = -Directions.FromYaw(rig.Fighter.Yaw);
                            fx.Add(Fx(ElementFxRules.DustOnly(e.Element, air) ? "dust" : EffectKeys.Burst, rig.Fighter.Feet, back, 1f, 0f, 0.2f,
                                (int)BodyJoint.RightFoot, rig.Id, ElementName(e.Element)));
                            fx.Add(Fx(air ? "jet" : EffectKeys.Trail, rig.Fighter.Feet, back, 1f, 0f, e.Move.Startup, (int)BodyJoint.RightFoot, rig.Id,
                                ElementName(e.Element)));
                        }
                        // As PlayerFeedback (J4-03): Earth's boulder rises out of the floor under its launch point over the
                        // throw's wind-up, at the size of the flying boulder.
                        if (e.Move != null && e.Move.LaunchesProjectile && e.Move.Projectile != null && ElementFxRules.StoneFromFloor(e.Element)
                            && !ElementFxRules.IsAirborne(in e) && rig.LastInput.Grounded)
                        {
                            var player = (SimPlayer)rig.Fighter;
                            Vector3 launch = player.Feet + new Vector3(0f, e.Move.OriginHeight, 0f) + Directions.FromYaw(player.Model.FacingYaw) * e.Move.OriginForward;
                            float size = e.Move.Projectile.Radius * 2f * (e.Move.Projectile.VisualScale > 0f ? e.Move.Projectile.VisualScale : 1f);
                            fx.Add(Fx("raise_stone", launch, Vector3.UnitY, size, 0f, e.Move.Startup / Math.Max(0.05f, e.PlaybackRate), (int)BodyJoint.RightHand,
                                rig.Id, ElementName(e.Element)));
                            raiseStoneFrame = FrameCount;
                            raiseStoneFighter = rig.Id;
                            raiseStonePoint = launch;
                            boulderSizes.Add((e.Move.DisplayName, size * 0.9f, e.Move.Projectile.Radius * 2f));
                        }
                        break;
                    case PlayerEventType.ElementSwitched:
                        fx.Add(Fx("switch", rig.Fighter.AimPoint, Vector3.UnitY, 0.6f, 0f, 0.35f, (int)BodyJoint.Chest, rig.Id, ElementName(e.Element)));
                        // As SwitchFlourish: the new element's burst (Earth: rock from the floor, or dust in the air).
                        fx.Add(Fx(ElementFxRules.DustOnly(e.Element, e.InAir) ? "dust" : EffectKeys.Burst,
                            e.Element == ElementId.Earth && !e.InAir ? rig.Fighter.Feet : rig.Fighter.AimPoint, Vector3.UnitY, 0.6f, 0f, 0.3f,
                            e.Element == ElementId.Earth && !e.InAir ? (int)BodyJoint.RightFoot : (int)BodyJoint.Chest, rig.Id, ElementName(e.Element)));
                        break;
                    case PlayerEventType.Deflected:
                        fx.Add(Fx("spark", rig.Fighter.AimPoint, Vector3.UnitZ, 0.5f, 0f, 0.2f, (int)BodyJoint.RightHand, rig.Id));
                        break;
                }
            }

            // An effect made at a point on a body (a strike's limb, a hit on a foe), as the Unity runner draws it: the
            // element's own key there, except Earth (J3-07: ElementFxRules.StoneFromFloor), which is dust at the point and
            // its rock (a burst) rising from the floor under it, if the floor is near enough. key null: Earth's part only.
            void BodyBurst(string key, ElementId element, Vector3 at, Vector3 dir, float range, float arc, float duration, int joint, int fighter)
            {
                string el = ElementName(element);
                if (!ElementFxRules.StoneFromFloor(element))
                {
                    if (key != null) fx.Add(Fx(key, at, dir, range, arc, duration, joint, fighter, el));
                    return;
                }
                if (key != null) fx.Add(Fx("dust", at, dir, range, arc, duration, joint, fighter, el));
                if (ElementFxRules.FloorStoneUnder(element, at.Y - ArenaFloorY))
                    fx.Add(Fx(EffectKeys.Burst, new Vector3(at.X, ArenaFloorY, at.Z), Vector3.UnitY, range, arc, duration, joint, fighter, el));
            }

            const float ArenaFloorY = 0f;   // every anim scene is on flat ground at y = 0

            // J4-03: grounded Earth throws, and how many had their boulder rise out of the floor first.
            int earthThrows, earthThrowsFromFloor, raiseStoneFrame = -1, raiseStoneFighter;
            Vector3 raiseStonePoint;
            readonly List<(string move, float drawn, float hit)> boulderSizes = new List<(string, float, float)>();

            // J3-07: no Earth rock effect (a burst or a spark) made on or out of a body while the player is on the ground:
            // its origin must be on the floor (within BodyRockHeight of it).
            const float BodyRockHeight = 0.5f;
            int earthGroundFx, earthBodyRockFx;
            static readonly System.Text.RegularExpressions.Regex FxOriginY =
                new System.Text.RegularExpressions.Regex(@"""o"":\[[^,]+,([^,]+),");

            void CheckEarthOnBody(Scene s)
            {
                foreach (AnimRig rig in s.Rigs)
                {
                    if (!(rig.Fighter is SimPlayer) || !rig.LastInput.Grounded) continue;
                    foreach (string f in fx)
                    {
                        if (!f.Contains("\"el\":\"earth\"")) continue;
                        earthGroundFx++;
                        if (!(f.Contains("\"key\":\"burst\"") || f.Contains("\"key\":\"spark\""))) continue;
                        var match = FxOriginY.Match(f);
                        if (match.Success && float.Parse(match.Groups[1].Value, C) - ArenaFloorY > BodyRockHeight) earthBodyRockFx++;
                    }
                }
            }

            // The element an enemy's launch, landing and hit spark are drawn in: as EnemyFighter.EffectElement, the player's
            // element (the sim's only attacker), so a render shows Earth launches in Earth's colour (R2-S16).
            void EnemyEffect(AnimRig rig, SimEnemy enemy, in EnemyEvent e, SimPlayer player)
            {
                ElementId element = player != null ? player.Model.ActiveElement : ElementId.Fire;
                string el = ElementName(element);
                switch (e.Type)
                {
                    case EnemyEventType.Launched:
                        fx.Add(Fx(EffectKeys.Pillar, enemy.Feet, Vector3.UnitY, 3f, 0f, 0.6f, (int)BodyJoint.Hips, rig.Id, el));
                        fx.Add(Fx("embers", enemy.Feet, Vector3.UnitY, 1f, 0f, 1.2f, (int)BodyJoint.Chest, rig.Id, el));
                        break;
                    case EnemyEventType.KnockedDown:
                        fx.Add(Fx(EffectKeys.Slam, enemy.Feet, -Vector3.UnitY, SlamRingRadius, 360f, 0.45f, (int)BodyJoint.Hips, rig.Id, el));
                        break;
                    case EnemyEventType.Damaged:
                    {
                        // As PlayerFeedback.OnHitReport: Earth hitting from the air sparks dust, never rock (R2-03).
                        bool airborne = player != null && ElementFxRules.IsAirborneAttacker(player.Model.IsGrounded, player.Model.CurrentAttackKind);
                        if (ElementFxRules.DustOnly(element, airborne)) fx.Add(Fx("dust", enemy.AimPoint, Vector3.UnitZ, 0.4f, 0f, 0.16f, (int)BodyJoint.Chest, rig.Id, el));
                        else BodyBurst("spark", element, enemy.AimPoint, Vector3.UnitZ, 0.4f, 0f, 0.16f, (int)BodyJoint.Chest, rig.Id);
                        break;
                    }
                    case EnemyEventType.AttackActiveStart:
                        if (e.Move != null && !e.Move.LaunchesProjectile)
                        {
                            rig.Fk.Prop(rig.Animator.Pose, out Vector3 grip, out Vector3 dir);
                            Vector3 tip = grip + dir * SwordLength;
                            Vector3 flat = new Vector3(tip.X - enemy.Feet.X, 0f, tip.Z - enemy.Feet.Z);
                            if (rig.Kind == "soldier") swordReach.Add((e.Move.DisplayName, flat.Length(), e.Move.Range + e.Move.OriginForward));
                        }
                        break;
                }
            }

            public const float SlamRingRadius = 1.6f;   // EnemyRigPresenter's knockdown ring (visual)

            // V-22: at the first active frame, how far the striking limb points away from the nearest living enemy's
            // chest (degrees, shoulder/hip -> hand/toes vs shoulder/hip -> chest), and how far its tip is from that body.
            void MeasureAim(AnimRig rig, MoveData m, Scene scene)
            {
                Vector3[] j = rig.Fk.Positions;
                int root, tip;
                switch (m.Limb)
                {
                    case Limb.LeftFist: root = (int)BodyJoint.LeftUpperArm; tip = (int)BodyJoint.LeftHand; break;
                    case Limb.RightFoot: root = (int)BodyJoint.RightUpperLeg; tip = (int)BodyJoint.RightToes; break;
                    case Limb.LeftFoot: root = (int)BodyJoint.LeftUpperLeg; tip = (int)BodyJoint.LeftToes; break;
                    default: root = (int)BodyJoint.RightUpperArm; tip = (int)BodyJoint.RightHand; break;
                }
                AnimRig best = null;
                float bestDistance = float.MaxValue;
                foreach (AnimRig other in scene.Rigs)
                {
                    if (other == rig || !other.Fighter.IsAlive || other.Fighter is SimPlayer) continue;
                    float d = Vector3.Distance(other.Fk[BodyJoint.Chest], j[tip]);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = other;
                    }
                }
                if (best == null) return;
                Vector3 limb = j[tip] - j[root];
                Vector3 to = best.Fk[BodyJoint.Chest] - j[root];
                float cos = Vector3.Dot(limb, to) / Math.Max(1e-6f, limb.Length() * to.Length());
                float angle = MathF.Acos(Math.Clamp(cos, -1f, 1f)) * AnimMath.Rad2Deg;
                float body = float.MaxValue;
                BodyJoint[,] segments = { { BodyJoint.Hips, BodyJoint.Chest }, { BodyJoint.Chest, BodyJoint.Head }, { BodyJoint.LeftUpperLeg, BodyJoint.LeftFoot }, { BodyJoint.RightUpperLeg, BodyJoint.RightFoot } };
                for (int i = 0; i < segments.GetLength(0); i++)
                    body = Math.Min(body, HitGeometry.DistanceToSegment(j[tip], best.Fk[segments[i, 0]], best.Fk[segments[i, 1]]));
                aims.Add((m.DisplayName, angle, body));
            }

            readonly List<(string move, float angle, float tip)> aims = new List<(string, float, float)>();

            // How straight the striking limb is right now: 1 = fully extended.
            static float StrikeExtension(AnimRig rig, Limb limb)
            {
                HumanoidSkeleton sk = rig.Animator.Skeleton;
                Vector3[] j = rig.Fk.Positions;
                float Arm(BodySide side) => Vector3.Distance(j[(int)BodyJoints.UpperArm(side)], j[(int)BodyJoints.Hand(side)]) / sk.ArmLength;
                float Leg(BodySide side) => Vector3.Distance(j[(int)BodyJoints.UpperLeg(side)], j[(int)BodyJoints.Foot(side)]) / sk.LegLength;
                switch (limb)
                {
                    case Limb.LeftFist: return Arm(BodySide.Left);
                    case Limb.RightFoot: return Leg(BodySide.Right);
                    case Limb.LeftFoot: return Leg(BodySide.Left);
                    case Limb.BothFists: return Math.Min(Arm(BodySide.Left), Arm(BodySide.Right));
                    default: return Arm(BodySide.Right);
                }
            }

            static int LimbJoint(Limb limb)
            {
                switch (limb)
                {
                    case Limb.LeftFist: return (int)BodyJoint.LeftHand;
                    case Limb.RightFoot: return (int)BodyJoint.RightFoot;
                    case Limb.LeftFoot: return (int)BodyJoint.LeftFoot;
                    default: return (int)BodyJoint.RightHand;
                }
            }

            // el: the element whose look the effect takes ("fire", "water", "earth", "air"; enemies' effects say "").
            static string Fx(string key, Vector3 o, Vector3 d, float range, float arc, float duration, int joint, int fighter, string el = "")
            {
                return "{\"key\":" + Q(key) + ",\"o\":[" + F(o.X) + "," + F(o.Y) + "," + F(o.Z) + "],\"d\":[" + F(d.X) + "," + F(d.Y) + "," + F(d.Z)
                       + "],\"range\":" + F(range) + ",\"arc\":" + F(arc) + ",\"dur\":" + F(duration) + ",\"joint\":" + joint + ",\"fighter\":" + fighter
                       + ",\"el\":" + Q(el) + "}";
            }

            public static string ElementName(ElementId element)
            {
                return element == ElementId.None ? "" : element.ToString().ToLowerInvariant();
            }

            static string CaptionFor(Scene s)
            {
                PlayerCombatModel m = s.Model;
                MoveData move = m.CurrentMove;
                if (move != null) return move.DisplayName;
                if (m.State == PlayerState.Dodging && !m.IsAirDashing) return m.MoveSet.Dodge.DisplayName;
                switch (m.State)
                {
                    case PlayerState.Dodging: return m.IsAirDashing ? "Air Dash" : m.MoveSet.Dodge.DisplayName;
                    case PlayerState.Guarding: return "Parry";
                    case PlayerState.Staggered: return "Staggered";
                    case PlayerState.Dead: return "Defeated";
                    case PlayerState.Healing: return "Spirit Water";
                    case PlayerState.Sprinting: return "Sprint";
                    case PlayerState.Airborne: return "Jump";
                }
                return "";
            }

            // A clip on its own, on a fighter standing at the origin: 0.3 s of guard, the clip, 0.4 s of guard.
            public void GalleryClip(string key, string kind, MoveData move, string style = null)
            {
                BeginScene("Gallery: " + key);
                var rig = new GalleryRig(kind, style);
                ClipTiming timing = move != null ? ClipTiming.FromMove(move) : default;
                bool frameData = move != null;
                float length = frameData ? Math.Max(0.6f, timing.Total + 0.2f) : 1.6f;
                if (key == AnimationKeys.CrossbowBurst) timing = new ClipTiming { Startup = move.Startup, Active = move.Active, Recovery = move.Recovery, HitCount = 3, HitInterval = 0.3f };
                if (key == AnimationKeys.SwordDoubleSlash) timing = new ClipTiming { Startup = move.Startup, Active = move.Active, Recovery = move.Recovery, HitCount = 2, HitInterval = 0.35f };
                int serial = 1;
                int total = (int)(length * 60f) + 45;
                for (int f = 0; f < total; f++)
                {
                    float t = (f - 18) / 60f;
                    var input = new FighterAnimInput { DeltaTime = Dt, Grounded = true, ActionSerial = serial };
                    if (t >= 0f && t < length)
                    {
                        input.ActionKey = key;
                        input.ActionTime = t;
                        input.HasFrameData = frameData;
                        input.Timing = timing;
                        input.ActionDuration = frameData ? 0f : 1.2f;
                        if (key == AnimationKeys.Charge) input.ChargeLevel = Math.Min(1f, t / 1.2f);
                    }
                    rig.Animator.Update(input);
                    rig.Fk.Compute(rig.Animator.Pose, Vector3.Zero, 0f);
                    var sb = frames;
                    if (FrameCount > 0) sb.Append(",\n");
                    sb.Append("{\"t\":").Append(F(time)).Append(",\"scene\":").Append(Q(scene)).Append(",\"caption\":").Append(Q(key));
                    sb.Append(",\"fighters\":[");
                    AppendGallery(sb, rig, key);
                    sb.Append("],\"fx\":[],\"proj\":[]}");
                    FrameCount++;
                    time += Dt;
                }
                Covered.Add(key);
            }

            void AppendGallery(StringBuilder sb, GalleryRig rig, string key)
            {
                AnimationCue action = rig.Animator.ActionCue;
                sb.Append("{\"id\":").Append(900).Append(",\"kind\":").Append(Q(rig.Kind)).Append(",\"state\":\"Gallery\"");
                if (rig.Kind == "player") sb.Append(",\"el\":").Append(Q(string.IsNullOrEmpty(rig.Animator.Style) ? "fire" : rig.Animator.Style));
                sb.Append(",\"key\":").Append(Q(action.IsValid ? action.Key : rig.Animator.LocomotionCue.Key)).Append(",\"alive\":true,\"yaw\":0,\"pos\":[0,0,0],\"j\":[");
                for (int j = 0; j < BodyJoints.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    Vector3 v = rig.Fk.Positions[j];
                    sb.Append(F(v.X)).Append(',').Append(F(v.Y)).Append(',').Append(F(v.Z));
                }
                sb.Append(']');
                if (rig.Kind != "player")
                {
                    rig.Fk.Prop(rig.Animator.Pose, out Vector3 grip, out Vector3 dir);
                    sb.Append(",\"prop\":[").Append(F(grip.X)).Append(',').Append(F(grip.Y)).Append(',').Append(F(grip.Z)).Append(',')
                        .Append(F(dir.X)).Append(',').Append(F(dir.Y)).Append(',').Append(F(dir.Z)).Append(',').Append(F(PropLength(rig.Kind))).Append(']');
                }
                sb.Append('}');
            }

            sealed class GalleryRig
            {
                public readonly string Kind;
                public readonly FighterAnimator Animator;
                public readonly ForwardKinematics Fk;

                public GalleryRig(string kind, string style)
                {
                    Kind = kind;
                    HumanoidSkeleton skeleton = HumanoidSkeleton.Create(null, kind == "soldier" ? 1.04f : 1f);
                    Animator = new FighterAnimator(PoseLibrary.Default, skeleton, style ?? AnimRig.StyleOf(kind));
                    Fk = new ForwardKinematics(skeleton);
                }
            }

            public string ToJson()
            {
                var sb = new StringBuilder(frames.Length + 4096);
                sb.Append("{\"fps\":60,\"joints\":[");
                for (int j = 0; j < BodyJoints.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(Q(BodyJoints.Name((BodyJoint)j)));
                }
                sb.Append("],\"parents\":[");
                for (int j = 0; j < BodyJoints.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(BodyJoints.Parent(j));
                }
                sb.Append("],\"frames\":[\n").Append(frames).Append("\n]}");
                return sb.ToString();
            }

            public void Report()
            {
                Out.Sub("Strikes at the first active frame: extension (1.00 = limb straight), aim (degrees between the limb and the nearest enemy's chest) and the tip's distance from that body");
                var t = new Table("Move", "Times", "Extension min", "Aim off mean", "Aim off max", "Tip to body (m)");
                foreach (var g in extensions.GroupBy(x => x.move))
                {
                    var a = aims.Where(x => x.move == g.Key).ToList();
                    t.Row(g.Key, g.Count(), Out.N(g.Min(x => x.ext), 3),
                        a.Count > 0 ? Out.N(a.Average(x => x.angle), 0) : "-", a.Count > 0 ? Out.N(a.Max(x => x.angle), 0) : "-",
                        a.Count > 0 ? Out.N(a.Average(x => x.tip), 2) : "-");
                }
                t.Print();
                Out.Sub("Dao blade tip at each strike vs the attack's reach (" + Out.N(SwordLength, 2) + " m blade)");
                var t2 = new Table("Strike", "Times", "Tip distance (m)", "Reach (m)");
                foreach (var g in swordReach.GroupBy(x => x.move))
                    t2.Row(g.Key, g.Count(), Out.N(g.Average(x => x.tip), 2), Out.N(g.First().reach, 2));
                t2.Print();
                Out.Sub("Planted feet on lunges (R2-02): no grounded foot above " + Out.N(DragHeight, 2) + " m and more than "
                        + Out.N(DragBehind, 2) + " m behind the hips for more than " + DragFramesAllowed
                        + " frames outside kicks, and no drop over " + Out.N(SnapDrop, 2) + " m in one frame (" + Out.N(LandingDrop, 2)
                        + " m root-relative at a touchdown, in a plunge or a landing; J6-01)");
                var t3 = new Table("Check", "Frames checked", "Problems", "Result");
                int drags = footIssues.Count(x => x.what.StartsWith("rear"));
                int teleports = footIssues.Count(x => x.what.StartsWith("foot slid"));
                int drops = footIssues.Count - drags - teleports;
                t3.Row("Rear foot dragged", footFrames, drags, Out.Target(drags == 0));
                t3.Row("Foot snapped down", footFrames, drops, Out.Target(drops == 0));
                t3.Row("Foot below " + Out.N(TeleportHeight, 1) + " m moving over " + Out.N(TeleportStep, 1) + " m along the floor in one frame (J7-02)",
                    teleportChecked + " foot-frames", teleports, Out.Target(teleports == 0));
                t3.Row("Earth rock effects in the air (R2-03)", earthAirFx + " Earth fx in the air", earthAirRockFx, Out.Target(earthAirRockFx == 0));
                int jumps = motionIssues.Count(x => x.what.StartsWith("joint"));
                int spins = motionIssues.Count(x => x.what.StartsWith("spin"));
                int glides = motionIssues.Count(x => x.what.StartsWith("both"));
                t3.Row("Joint jumps over " + Out.N(JointJumpMax, 1) + " m while attacking (J3-02)", motionFrames, jumps, Out.Target(jumps == 0));
                t3.Row("Spin turned back over " + Out.N(SpinReverseMax, 0) + " deg at a clip change (J3-02)", motionFrames, spins, Out.Target(spins == 0));
                t3.Row("Gliding strikes, both feet sliding " + GlideFrames + "+ frames (J3-03)", motionFrames, glides, Out.Target(glides == 0));
                int slides = motionIssues.Count(x => x.what.StartsWith("slid"));
                int whips = motionIssues.Count(x => x.what.StartsWith("speed changed"));
                t3.Row("Statue slides: over " + Out.N(SlideSpeed, 0) + " m/s for " + SlideFrames + "+ frames, legs frozen (J4-02)", motionFrames, slides, Out.Target(slides == 0));
                t3.Row("Speed change over " + Out.N(ReversalMax, 0) + " m/s in one frame in a strike (J4-02)", motionFrames, whips, Out.Target(whips == 0));
                int floats = motionIssues.Count(x => x.what.StartsWith("floated"));
                int hipSteps = motionIssues.Count(x => x.what.StartsWith("hips moved"));
                t3.Row("Hips up or down over " + Out.N(HipStepMax, 2) + " m in one frame, standing or moving (MV-03)", hipChecked, hipSteps, Out.Target(hipSteps == 0));
                t3.Row("Grounded, both feet over " + Out.N(FloatHeight, 1) + " m up for " + FloatFrames + "+ frames (J5-04)", floatChecked, floats, Out.Target(floats == 0));
                int strafes = motionIssues.Count(x => x.what.StartsWith("gait: strafe"));
                int spreads = motionIssues.Count(x => x.what.StartsWith("gait: feet spread"));
                int lifts = motionIssues.Count(x => x.what.StartsWith("gait: both feet"));
                t3.Row("Strafe key while moving within " + Out.N(GaitStraightAngle, 0) + " deg of facing (J6-02)", gaitChecked, strafes, Out.Target(strafes == 0));
                t3.Row("Run / strafe feet over " + Out.N(GaitMaxSpread, 1) + " m apart across the body moving straight (J6-02)", gaitChecked, spreads, Out.Target(spreads == 0));
                t3.Row("Walking or running with both feet over " + Out.N(GaitLiftHeight, 1) + " m up for " + GaitLiftFrames + "+ frames (J6-02)", gaitChecked, lifts, Out.Target(lifts == 0));
                t3.Row("Grounded Earth rock from the body (J3-07)", earthGroundFx + " grounded Earth fx", earthBodyRockFx, Out.Target(earthBodyRockFx == 0));
                int fromNowhere = earthThrows - earthThrowsFromFloor;
                t3.Row("Earth boulders thrown without rising from the floor (J4-03)", earthThrows + " grounded Earth throws", fromNowhere,
                    Out.Target(earthThrows > 0 && fromNowhere == 0));
                int oversize = boulderSizes.Count(b => b.drawn > b.hit + 1e-3f);
                t3.Row("Earth boulders drawn bigger than their hit (J4-03)", boulderSizes.Count + " throws ("
                    + string.Join(", ", boulderSizes.Select(b => b.move).Distinct()) + ")", oversize, Out.Target(oversize == 0));
                t3.Print();
                if (teleports > 0)
                    Out.Line("Feet slid along the floor, by key: " + string.Join(", ", footIssues.Where(x => x.what.StartsWith("foot slid"))
                        .GroupBy(x => x.key).Select(g => g.Key + " " + g.Count())));
                foreach (var issue in footIssues.Take(20))
                    Out.Line("- frame " + issue.frame + " (" + issue.scene + ", " + issue.key + "): " + issue.what + ", " + Out.N(issue.amount, 2) + " m");
                foreach (var issue in motionIssues.Take(20))
                    Out.Line("- frame " + issue.frame + " (" + issue.scene + ", " + issue.key + "): " + issue.what + ", " + Out.N(issue.amount, 2));
                var keys = typeof(AnimationKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();
                Out.Line("Animation keys shown: " + keys.Count(k => Covered.Contains(k)) + " / " + keys.Count
                         + (keys.All(k => Covered.Contains(k)) ? "" : " (missing: " + string.Join(", ", keys.Where(k => !Covered.Contains(k))) + ")"));
            }

            static string F(double v)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
                return v.ToString("0.###", C);
            }

            static string Q(string s)
            {
                return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
        }
    }
}
