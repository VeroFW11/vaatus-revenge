using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Stagger immunity (decided 28 Sep): a combo earns one stagger, then the enemy can't be staggered again
    // until StaggerImmunity seconds after it recovers, so it always gets to fight back. Deflects still stagger.
    public class EnemyStaggerTests
    {
        static DamageInfo PlayerHit(float poise)
        {
            return new DamageInfo
            {
                Damage = 1f, PoiseDamage = poise, Direction = new Vector3(0f, 0f, 1f), SourceTeam = Team.Player,
                SourceId = 7, AttackId = CombatIds.Next(), Parryable = true, Kind = HitKind.Light
            };
        }

        static EnemyDriver NewSoldier()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            d.World.HasTarget = false; // stand still: this is about poise, not movement
            return d;
        }

        static void RunUntilStaggerEnds(EnemyDriver d)
        {
            d.RunUntil(x => x.Brain.State != EnemyState.Staggered, 600);
            Assert.AreNotEqual(EnemyState.Staggered, d.Brain.State, "stagger never ended");
        }

        [Test]
        public void ComboStaggersOnceThenCannotRestaggerUntilImmunityEnds()
        {
            EnemyDriver d = NewSoldier();
            float maxPoise = d.Brain.Poise;

            // Enough poise damage in a combo to break the soldier once.
            HitResult result = default;
            float dealt = 0f;
            while (dealt < maxPoise)
            {
                result = d.Brain.ReceiveHit(PlayerHit(13f), Vector3.Zero);
                dealt += 13f;
            }
            Assert.IsTrue(result.PoiseBroken, "the combo should stagger");
            Assert.AreEqual(EnemyState.Staggered, d.Brain.State);

            // Hitting a staggered enemy never extends or restarts the stagger.
            d.Run(10);
            Assert.IsFalse(d.Brain.ReceiveHit(PlayerHit(100f), Vector3.Zero).PoiseBroken, "re-staggered during the stagger");

            // Straight after recovering it's immune...
            RunUntilStaggerEnds(d);
            Assert.IsTrue(d.Brain.IsStaggerImmune);
            Assert.IsFalse(d.Brain.ReceiveHit(PlayerHit(100f), Vector3.Zero).PoiseBroken, "re-staggered inside the immunity window");

            // ...until the immunity window runs out; then a fresh break works again.
            float immunity = EnemyTuning.CreateDaoSoldier().StaggerImmunity;
            d.Run((int)System.Math.Ceiling(immunity / d.Dt) + 2);
            Assert.IsFalse(d.Brain.IsStaggerImmune);
            Assert.IsTrue(d.Brain.ReceiveHit(PlayerHit(100f), Vector3.Zero).PoiseBroken, "should stagger again after immunity");
        }

        [Test]
        public void DeflectStillStaggersDuringImmunity()
        {
            EnemyDriver d = NewSoldier();
            d.Brain.ReceiveHit(PlayerHit(100f), Vector3.Zero);
            RunUntilStaggerEnds(d);
            Assert.IsTrue(d.Brain.IsStaggerImmune);

            d.Brain.OnParried();
            d.Step();
            Assert.AreEqual(EnemyState.Staggered, d.Brain.State, "a deflect is a skill reward and ignores immunity");
        }

        [Test]
        public void ResetClearsImmunity()
        {
            EnemyDriver d = NewSoldier();
            d.Brain.ReceiveHit(PlayerHit(100f), Vector3.Zero);
            RunUntilStaggerEnds(d);
            Assert.IsTrue(d.Brain.IsStaggerImmune);

            d.Brain.Reset();
            Assert.IsFalse(d.Brain.IsStaggerImmune);
        }
    }
}
