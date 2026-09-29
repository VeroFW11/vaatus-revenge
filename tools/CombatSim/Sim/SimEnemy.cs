using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Mirror of Enemies/EnemyFighter.cs Update (execution order 10) + EnemyStrikes: world state (player feet,
    // chest aim point, line of sight chest-to-chest), brain.Tick, one Move (not for planted dummies), facing,
    // events (arc query when a strike opens, EndAttack when it closes, bolts as projectiles), keep sweeping an
    // open strike on later frames, corpse stops blocking once grounded. A deflected melee strike staggers the
    // attacker; a deflected bolt doesn't (EnemyStrikes.ReactToBolt).
    public sealed class SimEnemy : SimFighter
    {
        const float MaxCorpseFallSeconds = 3f;
        // EnemyFighter ledge check (60cb8ee): probe the ground LedgeProbeAhead beyond the end of each step.
        const float LedgeProbeAhead = 0.2f;
        const float LedgeProbeRadius = 0.05f;
        const float SkinWidth = 0.08f;        // CharacterController.skinWidth default (EnemyBuilder doesn't set it)

        // PlayerCombatModel.NotifyEnemyStrike on every melee AttackActiveStart, before the hit query, as
        // EnemyStrikes.OpenMelee does in Unity since the round-2 fixes (report 02, NEW-01). The core's WouldHaveLanded
        // perfect-dodge rule needs it. --no-notify-strikes turns it off to reproduce the 60cb8ee Unity behaviour.
        public static bool NotifyStrikes = true;
        public int LedgeStops;

        readonly SimWorld world;
        readonly List<SimHitReport> reports = new List<SimHitReport>(4);
        public EnemyBrain Brain;
        public readonly EnemyTuning Tuning;
        public bool Planted;
        public Vector3 SpawnPosition;
        public float SpawnYaw;
        public readonly int Seed;
        int openAttackId;
        float deadTime;
        float yaw;
        public readonly List<EnemyEvent> FrameEvents = new List<EnemyEvent>(8);
        public EnemyWorldState LastWorld;

        public SimEnemy(SimWorld world, EnemyTuning tuning, AttackTokenPool tokens, Vector3 position, float yawDegrees, int seed,
            bool dummySwings = false)
        {
            this.world = world;
            Tuning = tuning;
            Id = CombatIds.Next();
            Name = tuning.DisplayName;
            Team = Team.Enemy;
            Controller.Position = position;
            SpawnPosition = position;
            SpawnYaw = yawDegrees;
            yaw = yawDegrees;
            Seed = seed;
            if (tuning.Archetype == EnemyArchetype.Dummy)
            {
                Planted = true;
                // TrainingDummy.ResolveTokens: the encounter's pool (its tuning doesn't use tokens anyway).
                Brain = new SparringDummyBrain(tuning, tokens, Id, seed, yawDegrees) { SwingEnabled = dummySwings };
            }
            else
            {
                Brain = EnemyBrain.Create(tuning, tokens, Id, seed, yawDegrees);
            }
        }

        public override bool IsAlive => Active && Brain.IsAlive;
        public override float Yaw => yaw;
        public Vector3 Forward => Directions.FromYaw(yaw);

        public override HitResult ReceiveHit(in DamageInfo hit)
        {
            if (!Active) return HitResult.Ignored;
            return Brain.ReceiveHit(hit, Brain.Forward);
        }

        public void ResetEnemy()
        {
            EndAll();
            Controller.Enabled = false;
            Controller.Position = SpawnPosition;
            yaw = SpawnYaw;
            Controller.Enabled = true;
            Brain.Reset(SpawnYaw);
            deadTime = 0f;
        }

        public void Update(float dt)
        {
            FrameEvents.Clear();
            EnemyWorldState ws = BuildWorldState();
            LastWorld = ws;
            EnemyTickResult result = Brain.Tick(dt, in ws);
            if (dt > 0f)
            {
                if (!float.IsNaN(result.FacingYaw) && !float.IsInfinity(result.FacingYaw)) yaw = result.FacingYaw;
                if (!Planted && Controller.Enabled)
                {
                    Vector3 v = result.Velocity;
                    // EnemyFighter.ApplyMotion: the brain's own steps stop at a ledge (knockback while staggered doesn't).
                    if (Brain.IsAlive && Brain.State != EnemyState.Staggered && WouldStepOffLedge(new Vector3(v.X * dt, 0f, v.Z * dt)))
                    {
                        v.X = 0f;
                        v.Z = 0f;
                        LedgeStops++;
                    }
                    Controller.Move(v * dt, world.Level, world.Fighters, this);
                }
            }
            bool strikeOpened = false;
            EventList<EnemyEvent> events = result.Events;
            for (int i = 0; i < events.Count; i++)
            {
                EnemyEvent e = events[i];
                FrameEvents.Add(e);
                world.OnEnemyEvent(this, in e);
                switch (e.Type)
                {
                    case EnemyEventType.AttackActiveStart:
                        if (NotifyStrikes && e.Move != null && !e.Move.LaunchesProjectile && world.Player != null)
                            world.Player.Model.NotifyEnemyStrike(e.Origin, e.Direction, e.Move, Feet);
                        OpenMelee(in e);
                        strikeOpened = true;
                        break;
                    case EnemyEventType.AttackActiveEnd:
                        if (e.AttackId != 0) world.Hits.EndAttack(e.AttackId);
                        if (openAttackId == e.AttackId) openAttackId = 0;
                        break;
                    case EnemyEventType.ProjectileLaunched:
                        LaunchBolt(in e);
                        break;
                    case EnemyEventType.Died:
                        if (!Brain.IsAlive)
                        {
                            EndAll();
                            deadTime = 0f;
                            if (Planted) Controller.Enabled = false;
                        }
                        break;
                    case EnemyEventType.Reset:
                        deadTime = 0f;
                        Controller.Enabled = true;
                        break;
                }
            }
            if (dt > 0f && !strikeOpened && Brain.IsAttackActive) ContinueMelee();
            if (!Brain.IsAlive && Controller.Enabled)
            {
                deadTime += dt;
                if (Planted || Controller.IsGrounded || deadTime >= MaxCorpseFallSeconds) Controller.Enabled = false;
            }
        }

        // Mirror of EnemyFighter.WouldStepOffLedge.
        bool WouldStepOffLedge(Vector3 step)
        {
            float length = step.Length();
            if (length < 1e-5f) return false;
            Vector3 ahead = Feet + step * ((length + LedgeProbeAhead) / length);
            float stepHeight = Controller.StepOffset + SkinWidth;
            Vector3 probeTop = ahead + new Vector3(0f, stepHeight, 0f);
            // The ray down from probeTop: boxes and the ramp's height field (the ramp isn't a box here).
            if (world.Level.GroundHeight(ahead.X, ahead.Z, ahead.Y, stepHeight) >= ahead.Y - stepHeight) return false;
            return !world.Level.IsOverlapping(probeTop, LedgeProbeRadius);
        }

        EnemyWorldState BuildWorldState()
        {
            var ws = new EnemyWorldState
            {
                Position = Feet,
                Grounded = Planted || !Controller.Enabled || Controller.IsGrounded,
                SelfRadius = Radius
            };
            if (!Brain.IsAlive) return ws;
            SimPlayer target = world.Player;
            if (target == null || !target.Active || target.Team == Team.Enemy || !target.IsAlive) return ws;
            ws.HasTarget = true;
            ws.TargetPosition = target.Feet;
            ws.TargetAimPoint = target.AimPoint;
            ws.TargetRadius = target.Radius;
            ws.TargetHidden = world.Level.IsBlocked(AimPoint, target.AimPoint);
            return ws;
        }

        void OpenMelee(in EnemyEvent e)
        {
            MoveData move = e.Move;
            if (move == null || move.LaunchesProjectile) return;
            if (openAttackId != 0 && openAttackId != e.AttackId) world.Hits.EndAttack(openAttackId);
            openAttackId = e.AttackId;
            DamageInfo damage = Brain.BuildDamage(in e);
            reports.Clear();
            world.Hits.Arc(e.Origin, e.Direction, move.Range, move.ArcDegrees, move.VerticalReach, damage, reports);
            React(move, damage);
        }

        void ContinueMelee()
        {
            MoveData move = Brain.CurrentMove;
            if (move == null || move.LaunchesProjectile) return;
            DamageInfo damage = Brain.BuildCurrentDamage();
            if (damage.AttackId == 0) return;
            reports.Clear();
            Vector3 origin = Brain.GetStrikeOrigin(Feet);
            world.Hits.Arc(origin, Brain.Forward, move.Range, move.ArcDegrees, move.VerticalReach, damage, reports);
            React(move, damage);
        }

        void React(MoveData move, in DamageInfo damage)
        {
            bool landed = false, parried = false;
            for (int i = 0; i < reports.Count; i++)
            {
                HitResult r = reports[i].Result;
                world.OnEnemyHitLanded(this, move, in r);
                if (r.Outcome == HitOutcome.Hit) landed = true;
                else if (r.Outcome == HitOutcome.Parried) parried = true;
            }
            if (landed)
            {
                world.Time.Hitstop(damage.Hitstop);
                Brain.OnStrikeLanded();
            }
            if (parried) Brain.OnParried();
        }

        void LaunchBolt(in EnemyEvent e)
        {
            MoveData move = e.Move;
            if (move == null) return;
            EnemyAttackData attack = e.Attack;
            world.Projectiles.Launch(e.Origin, e.Direction, move.Projectile, Brain.BuildDamage(in e), false, report =>
            {
                world.OnEnemyHitLanded(this, move, in report.Result);
                if (report.Result.Outcome == HitOutcome.Hit)
                    world.Time.Hitstop(attack != null && attack.Move != null ? attack.Move.Hitstop : 0f);
            });
        }

        void EndAll()
        {
            if (openAttackId != 0) world.Hits.EndAttack(openAttackId);
            openAttackId = 0;
        }
    }
}
