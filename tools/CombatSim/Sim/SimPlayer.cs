using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Mirror of Player/PlayerController.cs Update (execution order 0), minus visuals:
    //   world state (camera yaw, ground, lock-on target, soft-lock candidate) -> model.Tick -> one Move ->
    //   facing -> events (arc query on AttackActiveStart, EndAttack, projectiles, landing ring, slow motion on a
    //   perfect dodge, hitstop on a deflect) -> keep querying the open hitbox every later frame.
    public sealed class SimPlayer : SimFighter
    {
        public int AirHitsLanded;                      // clean hits by air-string moves (scenario stats)
        public const float DeflectHitstop = 0.06f;    // PlayerFeedbackSettings.DeflectHitstop default
        const float BodyCentreHeight = 0.9f;          // CharacterController.center.y

        readonly SimWorld world;
        readonly List<SimHitReport> hits = new List<SimHitReport>(8);
        public PlayerCombatModel Model;
        float facingYaw;
        int openedAttackId;
        public PlayerTickResult LastResult;
        public PlayerWorldState LastWorld;
        public SimFighter SoftTarget;
        public SimFighter LockTarget;
        public readonly List<PlayerEvent> FrameEvents = new List<PlayerEvent>(16);

        public SimPlayer(SimWorld world, PlayerTuning tuning, ElementMoveSet moves, Vector3 position, float yaw)
        {
            this.world = world;
            Id = CombatIds.Next();
            Name = "Player";
            Team = Team.Player;
            Controller.Position = position;
            facingYaw = yaw;
            Model = new PlayerCombatModel(tuning, moves, Id, yaw);
        }

        public override bool IsAlive => Model.IsAlive;
        public override float Yaw => facingYaw;
        public Vector3 Forward => Directions.FromYaw(facingYaw);

        public override HitResult ReceiveHit(in DamageInfo hit)
        {
            if (!Active) return HitResult.Ignored;
            return Model.ReceiveHit(in hit, Directions.FromYaw(facingYaw));
        }

        public void Respawn(Vector3 position, float yaw)
        {
            Controller.Position = position;
            Controller.IsGrounded = false;   // an enabled/disabled controller forgets isGrounded until its next Move
            facingYaw = yaw;
            Model.Respawn(yaw);
            openedAttackId = 0;
            world.LockOn?.ClearLock();
            world.Time.ClearEffects();
        }

        public void Update(float dt, in PlayerInputFrame input)
        {
            FrameEvents.Clear();
            float cameraYaw = world.CameraYaw;
            PlayerWorldState ws = BuildWorldState(in input, cameraYaw);
            LastWorld = ws;
            PlayerTickResult result = Model.Tick(dt, in input, in ws);
            LastResult = result;
            if (dt > 0f) Controller.Move(result.Velocity * dt, world.Level, world.Fighters, this);
            facingYaw = result.FacingYaw;

            openedAttackId = 0;
            EventList<PlayerEvent> events = result.Events;
            for (int i = 0; i < events.Count; i++)
            {
                PlayerEvent e = events[i];
                FrameEvents.Add(e);
                world.OnPlayerEvent(in e);
                HandleGameplayEvent(in e);
            }
            if (dt > 0f) QueryActiveAttack();
        }

        PlayerWorldState BuildWorldState(in PlayerInputFrame input, float cameraYaw)
        {
            var ws = new PlayerWorldState
            {
                Position = Feet,
                Grounded = Controller.Enabled && Controller.IsGrounded,
                CameraYaw = cameraYaw,
                SelfRadius = Radius,
                SelfHeight = Height,
                RealDeltaTime = world.LastRealDt,   // Time.unscaledDeltaTime: the heavy's charge clock runs on it
            };
            SimFighter nearest = NearestEnemy();    // PlayerController.NearestEnemy: Momentum drains backing away from it
            if (nearest != null)
            {
                ws.HasNearestEnemy = true;
                ws.NearestEnemyPosition = nearest.Feet;
            }
            LockTarget = Usable(world.LockOn != null ? world.LockOn.Target : null);
            SoftTarget = null;
            float aimYaw = Model.GetAimYaw(input.Move, cameraYaw);
            SimFighter zip = FindZipTarget(aimYaw, LockTarget);   // PlayerController.FindZipTarget
            if (zip != null)
            {
                ws.HasZipTarget = true;
                ws.ZipTargetPosition = zip.Feet;
                ws.ZipTargetAimPoint = zip.AimPoint;
                ws.ZipTargetRadius = zip.Radius;
            }
            if (LockTarget != null)
            {
                ws.HasLockTarget = true;
                ws.LockTargetPosition = LockTarget.Feet;
                ws.LockTargetAimPoint = LockTarget.AimPoint;
                ws.LockTargetRadius = LockTarget.Radius;
                return ws;
            }
            SoftTarget = FindSoftTarget(aimYaw);
            if (SoftTarget != null)
            {
                ws.HasSoftTarget = true;
                ws.SoftTargetPosition = SoftTarget.Feet;
                ws.SoftTargetAimPoint = SoftTarget.AimPoint;
                ws.SoftTargetRadius = SoftTarget.Radius;
            }
            return ws;
        }

        SimFighter FindZipTarget(float aimYaw, SimFighter locked)
        {
            ElementMoveSet moves = Model.MoveSet;
            ZipStrikeSettings zip = moves != null ? moves.Zip : null;
            if (zip == null || moves.ZipStrike == null) return null;
            if (locked != null)
            {
                bool inReach = SoftLockSelector.TryScore(Feet, aimYaw, locked.Feet, zip.Range, 360f, zip.MaxHeightDifference, out _);
                return inReach && !world.Level.IsBlocked(AimPoint, locked.AimPoint) ? locked : null;
            }
            SimFighter best = null;
            float bestScore = float.MaxValue;
            IReadOnlyList<SimFighter> all = world.Fighters;
            for (int i = 0; i < all.Count; i++)
            {
                SimFighter c = Usable(all[i]);
                if (c == null) continue;
                if (!SoftLockSelector.TryScore(Feet, aimYaw, c.Feet, zip.Range, zip.AngleDegrees, zip.MaxHeightDifference, out float score)
                    || score >= bestScore) continue;
                if (world.Level.IsBlocked(AimPoint, c.AimPoint)) continue;
                best = c;
                bestScore = score;
            }
            return best;
        }

        SimFighter FindSoftTarget(float aimYaw)
        {
            SimFighter best = null;
            float bestScore = float.MaxValue;
            IReadOnlyList<SimFighter> all = world.Fighters;
            for (int i = 0; i < all.Count; i++)
            {
                SimFighter c = Usable(all[i]);
                if (c == null) continue;
                if (!SoftLockSelector.TryScore(Feet, aimYaw, c.Feet, Model.Tuning, out float score) || score >= bestScore) continue;
                if (world.Level.IsBlocked(AimPoint, c.AimPoint)) continue;
                best = c;
                bestScore = score;
            }
            return best;
        }

        SimFighter NearestEnemy()
        {
            SimFighter best = null;
            float bestSq = float.MaxValue;
            IReadOnlyList<SimFighter> all = world.Fighters;
            for (int i = 0; i < all.Count; i++)
            {
                SimFighter c = Usable(all[i]);
                if (c == null) continue;
                float sq = Vector3.DistanceSquared(c.Feet, Feet);
                if (sq >= bestSq) continue;
                best = c;
                bestSq = sq;
            }
            return best;
        }

        SimFighter Usable(SimFighter c)
        {
            if (c == null || c == this || !c.Active || !c.IsAlive) return null;
            if (c.Team == Team) return null;
            return c;
        }

        void HandleGameplayEvent(in PlayerEvent e)
        {
            switch (e.Type)
            {
                case PlayerEventType.AttackActiveStart:
                {
                    openedAttackId = e.AttackId;
                    MoveData move = e.Move;
                    if (move == null) break;
                    DamageInfo damage = Model.BuildDamage(in e);
                    world.Hits.Arc(e.Origin, e.Direction, move.Range, move.ArcDegrees, move.VerticalReach, damage, hits);
                    ResolveHits(move, e.AttackId, damage.Hitstop);
                    break;
                }
                case PlayerEventType.AttackActiveEnd:
                    world.Hits.EndAttack(e.AttackId);
                    break;
                case PlayerEventType.ProjectileLaunched:
                {
                    MoveData move = e.Move;
                    if (move == null) break;
                    DamageInfo damage = Model.BuildDamage(in e);
                    world.Projectiles.Launch(e.Origin, e.Direction, move.Projectile, damage, true, r => OnProjectileHit(r, damage));
                    break;
                }
                case PlayerEventType.PlungeImpact:
                {
                    MoveData move = e.Move;
                    if (move != null)
                    {
                        DamageInfo damage = Model.BuildDamage(in e);
                        world.Hits.Sphere(e.Origin + new Vector3(0f, BodyCentreHeight, 0f), e.Radius, damage, hits);
                        ResolveHits(move, e.AttackId, damage.Hitstop);
                    }
                    world.Hits.EndAttack(e.AttackId);
                    break;
                }
                case PlayerEventType.PerfectDodge:
                    if (Model.IsAlive) world.Time.SlowMotion(e.Duration, e.TimeScale);
                    break;
                case PlayerEventType.Deflected:
                    if (Model.IsAlive && DeflectHitstop > 0f) world.Time.Hitstop(DeflectHitstop);
                    break;
            }
        }

        void ResolveHits(MoveData move, int attackId, float hitstop)
        {
            if (hits.Count == 0) return;
            bool clean = false, parried = false;
            for (int i = 0; i < hits.Count; i++)
            {
                SimHitReport report = hits[i];
                Model.OnAttackLanded(in report.Result, attackId);
                world.OnPlayerHitLanded(move, in report);
                if (report.Result.Outcome == HitOutcome.Hit)
                {
                    clean = true;
                    if (move != null && move.AirLift + move.SlamSpeed > 0f && Model.CurrentAttackKind == PlayerAttackKind.Air) AirHitsLanded++;
                }
                else if (report.Result.Outcome == HitOutcome.Parried) parried = true;
            }
            hits.Clear();
            if (clean) world.Time.Hitstop(hitstop);
            if (parried) Model.OnParried();
        }

        void OnProjectileHit(SimHitReport report, DamageInfo damage)
        {
            if (!Active || !Model.IsAlive) return;
            Model.OnAttackLanded(in report.Result, damage.AttackId);
            world.OnPlayerHitLanded(null, in report);
            if (report.Result.Outcome != HitOutcome.Hit) return;
            world.Time.Hitstop(damage.Hitstop);
        }

        void QueryActiveAttack()
        {
            if (!Model.IsAttackActive) return;
            int attackId = Model.ActiveAttackId;
            MoveData move = Model.CurrentMove;
            if (move == null || attackId == 0 || attackId == openedAttackId) return;
            Vector3 origin = Model.GetStrikeOrigin(Feet);
            DamageInfo damage = Model.BuildCurrentDamage();
            world.Hits.Arc(origin, Model.Forward, move.Range, move.ArcDegrees, move.VerticalReach, damage, hits);
            ResolveHits(move, attackId, damage.Hitstop);
        }
    }
}
