using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // One headless play session: the level, the player, the enemies, the shared attack tokens, projectiles,
    // the time-scale owner and the camera rig, stepped in Unity's script execution order (spec section 4):
    //   deltaTime = unscaled x the time scale set by the end of the previous frame
    //   -200 TimeScaleController  -50 LockOnController  0 PlayerController  10 enemies  20 FireProjectile
    //   LateUpdate 100 ThirdPersonCameraRig
    public sealed class SimWorld
    {
        public const float MaximumDeltaTime = 1f / 3f;    // Unity's Time.maximumDeltaTime default clamps deltaTime

        public readonly SimLevel Level;
        public readonly SimTime Time = new SimTime();
        public readonly SimHits Hits;
        public readonly SimProjectiles Projectiles;
        public readonly AttackTokenPool Tokens;
        public readonly List<SimFighter> fighters = new List<SimFighter>();
        public readonly List<SimEnemy> Enemies = new List<SimEnemy>();
        public SimPlayer Player;
        public SimCameraRig LockOn;       // camera rig + lock-on (null = fixed camera yaw)
        public float FixedCameraYaw;      // used when there's no rig
        public int Frame = -1;
        public double RealTime;
        public double GameTime;
        public float LastGameDt;
        public float LastRealDt;
        public Recorder Recorder;
        public Metrics Metrics = new Metrics();
        public Invariants Invariants;
        public PlayerInputFrame LastInput;

        public SimWorld(SimLevel level = null, int maxAttackers = 2)
        {
            Level = level ?? SimLevel.Empty();
            Hits = new SimHits(this);
            Projectiles = new SimProjectiles(this);
            Tokens = new AttackTokenPool(maxAttackers);
        }

        public IReadOnlyList<SimFighter> Fighters => fighters;
        public float CameraYaw => LockOn != null ? LockOn.Orbit.Yaw : FixedCameraYaw;

        public SimPlayer AddPlayer(PlayerTuning tuning, ElementMoveSet moves, Vector3 position, float yaw = 0f)
        {
            Player = new SimPlayer(this, tuning, moves, position, yaw);
            fighters.Insert(0, Player);
            return Player;
        }

        public SimEnemy AddEnemy(EnemyTuning tuning, Vector3 position, float yaw, int seed, bool dummySwings = false)
        {
            TuningOverrides.ApplyEnemy(tuning);
            var e = new SimEnemy(this, tuning, Tokens, position, yaw, seed, dummySwings);
            fighters.Add(e);
            Enemies.Add(e);
            return e;
        }

        public SimCameraRig AddCameraRig(CameraTuning camera = null, LockOnTuning lockOn = null)
        {
            LockOn = new SimCameraRig(this, camera, lockOn);
            if (Player != null) LockOn.SnapBehindPlayer();
            return LockOn;
        }

        // One rendered frame.
        public void Step(in PlayerInputFrame input, float unscaledDt)
        {
            LastInput = input;
            float gameDt = Math.Min(unscaledDt, MaximumDeltaTime) * Time.TimeScale;
            Time.Update(unscaledDt);
            Frame++;
            RealTime += unscaledDt;
            GameTime += gameDt;
            LastGameDt = gameDt;
            LastRealDt = unscaledDt;
            Metrics.BeginFrame(this, gameDt, unscaledDt);

            LockOn?.UpdateLockOn(in input, unscaledDt, gameDt);
            Player?.Update(gameDt, in input);
            for (int i = 0; i < Enemies.Count; i++) Enemies[i].Update(gameDt);
            Projectiles.Update(gameDt);
            LockOn?.LateUpdate(in input, unscaledDt, gameDt);

            Metrics.EndFrame(this, gameDt, unscaledDt);
            Invariants?.Check(this);
            Recorder?.Capture(this);
        }

        public bool AllEnemiesDead
        {
            get
            {
                bool any = false;
                for (int i = 0; i < Enemies.Count; i++)
                {
                    if (Enemies[i].Tuning.Archetype == EnemyArchetype.Dummy) continue;
                    any = true;
                    if (Enemies[i].IsAlive) return false;
                }
                return any;
            }
        }

        // ---------------------------------------------------------------- hooks for metrics, bots and the recorder
        public event Action<PlayerEvent> PlayerEvent;
        public event Action<SimEnemy, EnemyEvent> EnemyEvent;

        public void OnPlayerEvent(in PlayerEvent e)
        {
            Metrics.OnPlayerEvent(this, in e);
            Invariants?.OnPlayerEvent(this, in e);
            Recorder?.OnPlayerEvent(this, in e);
            PlayerEvent?.Invoke(e);
        }

        public void OnEnemyEvent(SimEnemy enemy, in EnemyEvent e)
        {
            Metrics.OnEnemyEvent(this, enemy, in e);
            Invariants?.OnEnemyEvent(this, enemy, in e);
            Recorder?.OnEnemyEvent(this, enemy, in e);
            EnemyEvent?.Invoke(enemy, e);
        }

        public void OnHitResolved(SimFighter target, in DamageInfo hit, in HitResult result)
        {
            Metrics.OnHitResolved(this, target, in hit, in result);
            Recorder?.OnHit(this, target, in hit, in result);
        }

        public void OnPlayerHitLanded(MoveData move, in SimHitReport report)
        {
        }

        public void OnEnemyHitLanded(SimEnemy enemy, MoveData move, in HitResult result)
        {
        }

        public void Record(int fighterIndex, string type, string detail)
        {
            Recorder?.Note(this, fighterIndex, type, detail);
        }

        public int IndexOf(SimFighter f)
        {
            return fighters.IndexOf(f);
        }
    }
}
