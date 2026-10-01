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
                    LastInput = target != null ? PlayerFeed.Build(p.Model, dt, true, p.Feet, chest) : PlayerFeed.Build(p.Model, dt);
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

            void CheckFeet(AnimRig rig, string key)
            {
                bool grounded = rig.LastInput.Grounded && !rig.LastInput.Dead;
                // A plunge (the axe kick's heel chop, the earthquake drop) is a landing: its foot comes down on purpose.
                bool landing = LandingKeys.Contains(key) || (rig.Fighter is SimPlayer pl && pl.Model.State == PlayerState.Plunging);
                bool sameScene = prevFootValid && prevFootScene == scene;
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
                    if (sameScene && grounded && prevFootY[i] - height > SnapDrop && !landing)
                        footIssues.Add((scene, FrameCount, key, "foot dropped in one frame", prevFootY[i] - height));
                    prevFootY[i] = grounded ? height : -10f;   // a frame in the air never counts as the "before" of a drop
                }
                prevFootValid = true;
                prevFootScene = scene;
                footFrames++;
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
                if (rig.Fighter is SimPlayer) CheckFeet(rig, key);
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
                        if (key == EffectKeys.Pillar || key == EffectKeys.Slam)
                            fx.Add(Fx(EffectKeys.Burst, e.Origin, key == EffectKeys.Slam ? -Vector3.UnitY : dir, m.Range, m.ArcDegrees, duration, LimbJoint(m.Limb), rig.Id, el));
                        else fx.Add(Fx(key, e.Origin, dir, m.Range, m.ArcDegrees, duration, LimbJoint(m.Limb), rig.Id, el));
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
                        extensions.Add((e.Move != null ? e.Move.DisplayName : "projectile", StrikeExtension(rig, e.Move != null ? e.Move.Limb : Limb.BothFists)));
                        break;
                    case PlayerEventType.PlungeImpact:
                    {
                        string plunge = e.Move != null && !string.IsNullOrEmpty(e.Move.EffectKey) ? e.Move.EffectKey : EffectKeys.Slam;
                        fx.Add(Fx(plunge == EffectKeys.Slam || plunge == EffectKeys.Stomp || plunge == EffectKeys.Wave ? plunge : EffectKeys.Slam,
                            e.Origin, -Vector3.UnitY, e.Radius, 360f, 0.45f, (int)BodyJoint.RightFoot, rig.Id, ElementName(((SimPlayer)rig.Fighter).Model.ActiveElement)));
                        break;
                    }
                    case PlayerEventType.DodgeStarted:
                        // As PlayerFeedback.DodgeStarted: a push from the feet (BurstOrDust), then jets.
                        fx.Add(Fx(ElementFxRules.DustOnly(e.Element, e.InAir) ? "dust" : EffectKeys.Burst, rig.Fighter.Feet, -e.Direction, 1f, 0f, 0.2f,
                            (int)BodyJoint.RightFoot, rig.Id, ElementName(e.Element)));
                        fx.Add(Fx("jet", rig.Fighter.Feet, -e.Direction, 1f, 0f, 0.3f, (int)BodyJoint.RightFoot, rig.Id, ElementName(e.Element)));
                        break;
                    case PlayerEventType.AttackStarted:
                        if (e.AttackKind == PlayerAttackKind.ZipStrike && e.Move != null)
                            fx.Add(Fx("jet", rig.Fighter.Feet, -Directions.FromYaw(rig.Fighter.Yaw), 1f, 0f, e.Move.Startup + e.Move.Active, (int)BodyJoint.RightFoot, rig.Id,
                                ElementName(e.Element)));
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
                        fx.Add(Fx(ElementFxRules.DustOnly(element, airborne) ? "dust" : "spark", enemy.AimPoint, Vector3.UnitZ, 0.4f, 0f, 0.16f,
                            (int)BodyJoint.Chest, rig.Id, el));
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
                        + " frames outside kicks, and no drop over " + Out.N(SnapDrop, 2) + " m in one frame outside landings and plunges");
                var t3 = new Table("Check", "Frames checked", "Problems", "Result");
                int drags = footIssues.Count(x => x.what.StartsWith("rear"));
                int drops = footIssues.Count - drags;
                t3.Row("Rear foot dragged", footFrames, drags, Out.Target(drags == 0));
                t3.Row("Foot snapped down", footFrames, drops, Out.Target(drops == 0));
                t3.Row("Earth rock effects in the air (R2-03)", earthAirFx + " Earth fx in the air", earthAirRockFx, Out.Target(earthAirRockFx == 0));
                t3.Print();
                foreach (var issue in footIssues.Take(20))
                    Out.Line("- frame " + issue.frame + " (" + issue.scene + ", " + issue.key + "): " + issue.what + ", " + Out.N(issue.amount, 2) + " m");
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
