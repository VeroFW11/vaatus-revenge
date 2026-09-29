using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // How an enemy's attacks actually reach the player, and what the enemy does with the answer.
    //
    // Melee: when a strike's active window opens, an arc query runs straight away, then again every frame
    // while the window stays open (the enemy may still be lunging). MeleeHitQuery remembers who each AttackId
    // already hit, so one swing hits the player at most once. The window's end clears that record.
    // Bolts: each one is a FireProjectile with its own AttackId.
    //
    // The PLAYER decides what a hit did (hit, blocked, dodged, deflected); the attacker reacts to the report:
    // hitstop on a clean hit (the brief freeze sells the weight of the blow), a little extra screen shake for
    // its weapon, and a stagger for itself when its swing was deflected. A deflected BOLT doesn't stagger the
    // archer: it's standing far away and the deflect only snuffed the bolt.
    public sealed class EnemyStrikes
    {
        // More per-attack bolt handlers than this means the attack list was edited a lot while playing: start over.
        const int MaxBoltHandlers = 8;

        readonly List<HitReport> reports = new List<HitReport>(4);
        readonly List<BoltHitHandler> boltHandlers = new List<BoltHitHandler>(2);
        int openAttackId;

        // The strike's active window just opened: sweep its arc from where it was aimed.
        // attackerFeet: where the enemy stands (its pivot), so the player's perfect-dodge reward knows which way
        // "toward the attacker" is.
        public void OpenMelee(in EnemyEvent e, EnemyBrain brain, Vector3 attackerFeet, EnemyFeedbackSettings feedback)
        {
            MoveData move = e.Move;
            if (brain == null || move == null || move.LaunchesProjectile) return;
            if (openAttackId != 0 && openAttackId != e.AttackId) MeleeHitQuery.EndAttack(openAttackId); // never leave a record behind
            openAttackId = e.AttackId;
            NotifyPlayerOfStrike(in e, move, attackerFeet);
            DamageInfo damage = brain.BuildDamage(in e);
            reports.Clear();
            MeleeHitQuery.Arc(e.Origin.ToUnity(), e.Direction.ToUnity(), move.Range, move.ArcDegrees, move.VerticalReach, damage, reports);
            React(brain, move, damage, feedback);
        }

        // Tell the player a strike just opened, BEFORE the arc query. Why: a dodge that carries you out of the
        // swing makes the arc miss, so the hit query alone would never see it and a backward or sideways dodge
        // could never be "perfect". The player's rules (PlayerCombatModel.NotifyEnemyStrike) ask instead whether
        // the swing would have landed where the dodge started, and award the perfect dodge if so. Doing it before
        // the query means a strike that still touches the dodging player then counts as an ordinary evade, so the
        // reward is given once. Bolts never call this: they're judged when they actually arrive.
        static void NotifyPlayerOfStrike(in EnemyEvent e, MoveData move, Vector3 attackerFeet)
        {
            PlayerController player = PlayerController.Instance;
            if (player == null || player.IsDead) return;
            PlayerCombatModel model = player.Model;
            if (model == null) return;
            model.NotifyEnemyStrike(e.Origin, e.Direction, move, attackerFeet.ToNumerics());
        }

        // Every later frame of the active window: same swing, from where the (lunging) enemy is now.
        public void ContinueMelee(EnemyBrain brain, Vector3 feet, EnemyFeedbackSettings feedback)
        {
            if (brain == null || !brain.IsAttackActive) return;
            MoveData move = brain.CurrentMove;
            if (move == null || move.LaunchesProjectile) return;
            DamageInfo damage = brain.BuildCurrentDamage();
            if (damage.AttackId == 0) return;
            reports.Clear();
            Vector3 origin = brain.GetStrikeOrigin(feet.ToNumerics()).ToUnity();
            MeleeHitQuery.Arc(origin, brain.Forward.ToUnity(), move.Range, move.ArcDegrees, move.VerticalReach, damage, reports);
            React(brain, move, damage, feedback);
        }

        // The window closed: forget who it hit, so the record doesn't linger.
        public void CloseMelee(int attackId)
        {
            if (attackId != 0) MeleeHitQuery.EndAttack(attackId);
            if (openAttackId == attackId) openAttackId = 0;
        }

        // Safety net for death, reset and disable: no open strike record may outlive the swing.
        public void EndAll()
        {
            if (openAttackId != 0) MeleeHitQuery.EndAttack(openAttackId);
            openAttackId = 0;
        }

        public void LaunchBolt(in EnemyEvent e, EnemyBrain brain, EnemyFighter owner)
        {
            MoveData move = e.Move;
            if (brain == null || move == null) return;
            BoltHitHandler handler = HandlerFor(owner, e.Attack);
            FireProjectile.Launch(e.Origin.ToUnity(), e.Direction.ToUnity(), move.Projectile, brain.BuildDamage(in e),
                ProjectileVisual.Bolt, handler.Callback);
        }

        // Called by a bolt when it touches the player (FireProjectile's onHit).
        static void ReactToBolt(in HitReport report, EnemyAttackData attack, EnemyFeedbackSettings feedback)
        {
            switch (report.Result.Outcome)
            {
                case HitOutcome.Hit:
                    TimeScaleController.Hitstop(attack != null && attack.Move != null ? attack.Move.Hitstop : 0f);
                    CameraShake.Add(feedback.BoltHitShake, feedback.BoltHitShakeTime);
                    break;
                case HitOutcome.Blocked:
                case HitOutcome.GuardBroken:
                    CameraShake.Add(feedback.BlockShake, feedback.BlockShakeTime);
                    break;
                // Parried: the bolt fizzled and the archer carries on. Evaded / PerfectEvade: it flew past.
            }
        }

        // The player's own feedback already sparks, flashes and shakes where it was hit or blocked; the attacker
        // adds the freeze-frame and the weight of its weapon.
        void React(EnemyBrain brain, MoveData move, in DamageInfo damage, EnemyFeedbackSettings feedback)
        {
            bool landed = false;
            bool clanged = false;
            bool parried = false;
            for (int i = 0; i < reports.Count; i++)
            {
                switch (reports[i].Result.Outcome)
                {
                    case HitOutcome.Hit: landed = true; break;
                    case HitOutcome.Blocked:
                    case HitOutcome.GuardBroken: clanged = true; break;
                    case HitOutcome.Parried: parried = true; break;
                }
            }
            if (landed)
            {
                // Once per swing, however many it touched.
                TimeScaleController.Hitstop(damage.Hitstop);
                if (move.Kind == HitKind.Heavy) CameraShake.Add(feedback.HeavyHitShake, feedback.HeavyHitShakeTime);
                else CameraShake.Add(feedback.HitShake, feedback.HitShakeTime);
                brain.OnStrikeLanded();   // the anti-mash break-out tells a trade from an earned punish with this
            }
            else if (clanged)
            {
                CameraShake.Add(feedback.BlockShake, feedback.BlockShakeTime);
            }
            // Deflected: stagger ourselves. The brain ends the swing, hands back its token and reports the
            // window's end next frame (which clears the hit record).
            if (parried) brain.OnParried();
        }

        // One handler per attack type (not per bolt), created once and reused, so launching bolts allocates
        // nothing. It reads the attack's hitstop live when the bolt lands.
        BoltHitHandler HandlerFor(EnemyFighter owner, EnemyAttackData attack)
        {
            for (int i = 0; i < boltHandlers.Count; i++)
            {
                if (boltHandlers[i].Attack == attack) return boltHandlers[i];
            }
            if (boltHandlers.Count >= MaxBoltHandlers) boltHandlers.Clear(); // bolts in flight keep their own handler
            var handler = new BoltHitHandler(owner, attack);
            boltHandlers.Add(handler);
            return handler;
        }

        // The onHit callback a bolt carries. A bolt keeps flying after its archer dies (it was already loosed),
        // so the callback checks the archer still exists before reading its feedback settings.
        sealed class BoltHitHandler
        {
            readonly EnemyFighter owner;
            public readonly EnemyAttackData Attack;
            public readonly System.Action<HitReport> Callback;

            public BoltHitHandler(EnemyFighter owner, EnemyAttackData attack)
            {
                this.owner = owner;
                Attack = attack;
                Callback = OnHit;
            }

            void OnHit(HitReport report)
            {
                if (owner == null) return; // archer destroyed (Unity's null check covers destroyed objects)
                ReactToBolt(in report, Attack, owner.Feedback);
            }
        }
    }
}
