using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Robustness: long runs of random input (mashing, long holds, sticks near the dead zone, huge mouse deltas,
    // lock-on spam, shoulder swaps) at 30/60/144 fps and with dt spikes and paused frames, plus preset swaps
    // mid-move (F5/F6), respawns and enemy resets, against the mixed group and a swinging dummy. Every frame is
    // checked by Invariants (finite and in-range numbers, nothing stuck, balanced events, telegraphed strikes,
    // consistent attack tokens).
    public static class FuzzScenario
    {
        public static void Run(Options o)
        {
            int framesPer = o.Quick ? 60000 : 150000;
            Out.Heading("Fuzz: random input, " + framesPer + " frames per run");
            var t = new Table("Preset", "Frame timing", "Game time", "Violations", "Longest i-frames", "Invulnerable share", "Oldest buffered press run",
                "Dodges started/ended", "Player hitboxes opened/closed", "Enemy hitboxes opened/closed", "Deaths / resets", "Max token holders");
            var allViolations = new List<string>();
            foreach (Preset p in o.Presets)
            {
                foreach (string timing in new[] { "30 fps", "60 fps", "144 fps", "60 fps + spikes/pauses" })
                {
                    var r = RunOne(o, p, timing, framesPer, o.Seed);
                    Invariants inv = r.inv;
                    string viol = inv.ViolationCounts.Count == 0 ? "none" : string.Join(", ", inv.ViolationCounts.Select(kv => kv.Key + " ×" + kv.Value));
                    t.Row(p, timing, Out.N(r.gameSeconds / 60.0, 1) + " min", viol, Out.N(inv.LongestInvulnerable, 3) + " s",
                        Out.Pct(r.invulnerableShare), Out.N(inv.MaxBufferedAge, 3) + " s (" + inv.MaxBufferedAgeCommand + ")",
                        inv.DodgesStarted + "/" + inv.DodgesEnded, inv.PlayerActiveOpened + "/" + inv.PlayerActiveClosed,
                        inv.EnemyActiveOpened + "/" + inv.EnemyActiveClosed, r.deaths + " / " + r.resets, inv.MaxTokenHolders);
                    foreach (string v in inv.Violations.Take(8)) allViolations.Add(p + " " + timing + ": " + v);
                    allViolations.AddRange(r.cameraProblems.Take(5).Select(c => p + " " + timing + ": camera " + c));
                }
            }
            t.Print();
            Out.Sub("First violations (up to 8 per run)");
            if (allViolations.Count == 0) Out.Line("None.");
            foreach (string v in allViolations) Out.Line("- " + v);
        }

        sealed class RandomPad
        {
            readonly DeterministicRandom r;
            readonly double[] until = new double[9];
            double nextStick, nextLook;
            Vector2 stick, look;
            bool lookMouse;

            public RandomPad(int seed) { r = new DeterministicRandom(seed); }

            static readonly float[] StartChance = { 0.08f, 0.02f, 0.04f, 0.02f, 0.03f, 0.015f, 0.004f, 0.004f, 0.003f };

            public Pad Next(double now)
            {
                var pad = new Pad();
                for (int b = 0; b < 9; b++)
                {
                    if (now < until[b]) { Set(ref pad, b); continue; }
                    if (r.NextFloat() < StartChance[b])
                    {
                        double len = r.Chance(0.8f) ? r.Range(0.01f, 0.15f) : r.Range(0.15f, 1.6f);
                        until[b] = now + len;
                        Set(ref pad, b);
                    }
                }
                if (now >= nextStick)
                {
                    nextStick = now + r.Range(0.05f, 1.0f);
                    float mag = r.Chance(0.2f) ? r.Range(0.05f, 0.15f) : r.Chance(0.3f) ? 0f : r.Range(0.2f, 1.2f);
                    float ang = r.Range(0f, 360f) * Directions.Deg2Rad;
                    stick = new Vector2((float)Math.Sin(ang), (float)Math.Cos(ang)) * mag;
                }
                if (now >= nextLook)
                {
                    nextLook = now + r.Range(0.05f, 0.8f);
                    lookMouse = r.Chance(0.5f);
                    float ang = r.Range(0f, 360f) * Directions.Deg2Rad;
                    float mag = lookMouse ? (r.Chance(0.05f) ? r.Range(200f, 3000f) : r.Range(0f, 40f)) : r.Range(0f, 1f);
                    look = new Vector2((float)Math.Sin(ang), (float)Math.Cos(ang)) * mag;
                    if (r.Chance(0.3f)) look = Vector2.Zero;
                }
                pad.Move = stick;
                pad.Look = look;
                pad.LookIsMouse = lookMouse;
                if (r.Chance(0.003f)) pad.SwitchTarget = r.Chance(0.5f) ? 1 : -1;
                if (r.Chance(0.002f)) pad.Element = (ElementId)r.Range(1, 5);
                return pad;
            }

            static void Set(ref Pad p, int b)
            {
                switch (b)
                {
                    case 0: p.Light = true; break;
                    case 1: p.Heavy = true; break;
                    case 2: p.Dodge = true; break;
                    case 3: p.Jump = true; break;
                    case 4: p.Guard = true; break;
                    case 5: p.Skill = true; break;
                    case 6: p.Heal = true; break;
                    case 7: p.LockOn = true; break;
                    case 8: p.SwapShoulder = true; break;
                }
            }
        }

        static (Invariants inv, double gameSeconds, double invulnerableShare, int deaths, int resets, List<string> cameraProblems)
            RunOne(Options o, Preset preset, string timing, int frames, int seed)
        {
            var s = new Session(preset, 60f, SimLevel.SandboxArena(), camera: true, playerAt: new Vector3(0f, 0f, -5f));
            var inv = new Invariants();
            s.World.Invariants = inv;
            s.World.AddEnemy(EnemyTuning.CreateDaoSoldier(), new Vector3(-2.5f, 0f, 2f), 180f, seed + 1);
            s.World.AddEnemy(EnemyTuning.CreateDaoSoldier(), new Vector3(2.5f, 0f, 2f), 180f, seed + 2);
            s.World.AddEnemy(EnemyTuning.CreateCrossbowman(), new Vector3(8f, 0f, 9f), -140f, seed + 3);
            s.World.AddEnemy(EnemyTuning.CreateCrossbowman(), new Vector3(-13f, 2.5f, 12f), 135f, seed + 4);
            s.World.AddEnemy(EnemyTuning.CreateSparringDummy(), new Vector3(0f, 0f, -9f), 180f, seed + 5, dummySwings: true);
            var rp = new RandomPad(seed * 7 + timing.Length);
            var rng = new DeterministicRandom(seed * 13 + 5);
            Session.MakePreset(Preset.Fluid, out PlayerTuning ft, out ElementMoveSet fm);
            Session.MakePreset(Preset.Punishing, out PlayerTuning pt, out ElementMoveSet pm);
            int deaths = 0, resets = 0;
            double deadSince = -1;
            double invulnerable = 0;
            var cam = new List<string>();
            for (int f = 0; f < frames; f++)
            {
                float dt;
                switch (timing)
                {
                    case "30 fps": dt = 1f / 30f; break;
                    case "144 fps": dt = 1f / 144f; break;
                    case "60 fps": dt = 1f / 60f; break;
                    default:
                        dt = 1f / 60f;
                        float roll = rng.NextFloat();
                        if (roll < 0.03f) dt = rng.Range(0.05f, 0.25f);          // hitch
                        else if (roll < 0.035f) dt = 0.4f;                      // very long hitch (clamped by maximumDeltaTime)
                        break;
                }
                bool pausedFrame = timing.Contains("pauses") && rng.NextFloat() < 0.01f;
                s.World.Time.Paused = pausedFrame;
                s.Step(rp.Next(s.World.RealTime), dt);
                if (s.Model.IsInvulnerable) invulnerable += s.World.LastGameDt;

                // SandboxDirector behaviour: respawn a second after death, reset everyone when all are dead, F5/F6.
                if (!s.Model.IsAlive)
                {
                    if (deadSince < 0) { deadSince = s.World.RealTime; deaths++; }
                    else if (s.World.RealTime - deadSince > 1.0)
                    {
                        s.World.Projectiles.ClearAll();   // SandboxDirector.ResetAllEnemies clears projectiles (60cb8ee)
                        foreach (var e in s.World.Enemies) e.ResetEnemy();
                        s.Player.Respawn(new Vector3(0f, 0f, -5f), 0f);
                        deadSince = -1;
                        resets++;
                    }
                }
                if (s.World.AllEnemiesDead)
                {
                    s.World.Projectiles.ClearAll();
                    foreach (var e in s.World.Enemies) e.ResetEnemy();
                    resets++;
                }
                if (rng.NextFloat() < 0.0015f)
                {
                    bool toFluid = rng.Chance(0.5f);
                    s.Model.ApplyTuning(toFluid ? ft : pt, toFluid ? fm : pm);
                }
                if (rng.NextFloat() < 0.0005f) { s.World.Projectiles.ClearAll(); foreach (var e in s.World.Enemies) e.ResetEnemy(); resets++; }
                OrbitCameraModel orbit = s.World.LockOn.Orbit;
                if (!(Finite(orbit.Yaw) && Finite(orbit.Pitch) && Finite(orbit.Distance)) || !Finite(s.World.LockOn.CameraPosition))
                    if (cam.Count < 10) cam.Add("NaN at frame " + f);
                if (orbit.Distance < 0f || orbit.Distance > orbit.DesiredDistance + 1e-3f)
                    if (cam.Count < 10) cam.Add("distance out of range " + orbit.Distance + " at frame " + f);
            }
            double share = s.World.GameTime > 0 ? invulnerable / s.World.GameTime : 0;
            return (inv, s.World.GameTime, share, deaths, resets, cam);
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z);
    }
}
