using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // A whole fight with no Unity: a seeded bot player against two Dao Soldiers (sharing two attack tokens),
    // a Crossbowman and a swinging Sparring Dummy. Hits are resolved with HitGeometry the way the Unity hit
    // query will, projectiles are swept each frame. It checks the rules hold up under messy play: nothing
    // gets stuck, tokens stay within the limit, every active window closes, resources stay in range, no NaN,
    // and the same seed replays exactly. It's also a worked example of wiring the core together.
    public class CombatSoakTests
    {
        const float Dt = 1f / 60f;
        const float BodyHeight = 1.8f;
        const float BodyRadius = 0.4f;

        sealed class Fighter
        {
            public EnemyDriver Driver;
            public Vector3 Spawn;
            public int DeadFrames;
            public int ActiveBalance;
            public float StateTime;
            public float SinceStaggered;
            public float LastStaggerRemaining;
            public int StrikeFrame = -1;          // when its current telegraph will turn into a strike (the bot reads it)
            public EnemyState LastState;
        }

        sealed class Projectile
        {
            public Vector3 Position;
            public Vector3 Direction;
            public ProjectileSpec Spec;
            public DamageInfo Damage;
            public bool FromPlayer;
            public Fighter Owner;
            public float Travelled;
        }

        sealed class Stats
        {
            public int PlayerHits, EnemyHits, PerfectDodges, Deflects, Blocks, Kills, Deaths, Projectiles;
            public double Checksum;
        }

        [Test]
        public void TwoMinuteBotFightKeepsEveryRule()
        {
            Stats a = RunFight(2024, 60 * 120, false);
            TestContext.WriteLine("player hits {0}, enemy hits {1}, perfect dodges {2}, deflects {3}, blocks {4}, kills {5}, deaths {6}, projectiles {7}",
                a.PlayerHits, a.EnemyHits, a.PerfectDodges, a.Deflects, a.Blocks, a.Kills, a.Deaths, a.Projectiles);
            Assert.Greater(a.PlayerHits, 20, "the bot lands hits");
            Assert.Greater(a.EnemyHits, 5, "enemies land hits");
            Assert.Greater(a.Projectiles, 5, "the crossbowman and Fire Blast fire");
            Assert.Greater(a.Kills, 0);
            Assert.Greater(a.PerfectDodges, 0, "perfect dodges happen");
            Assert.Greater(a.Deflects, 0, "deflects happen");
        }

        [Test]
        public void PunishingPresetFightKeepsEveryRule()
        {
            Stats s = RunFight(77, 60 * 90, true);
            TestContext.WriteLine("player hits {0}, enemy hits {1}, perfect dodges {2}, deflects {3}, blocks {4}, kills {5}, deaths {6}, projectiles {7}",
                s.PlayerHits, s.EnemyHits, s.PerfectDodges, s.Deflects, s.Blocks, s.Kills, s.Deaths, s.Projectiles);
            Assert.Greater(s.PlayerHits, 10);
        }

        [Test]
        public void SameSeedReplaysTheSameFight()
        {
            Stats a = RunFight(99, 60 * 40, false);
            Stats b = RunFight(99, 60 * 40, false);
            Assert.AreEqual(a.Checksum, b.Checksum);
            Assert.AreEqual(a.PlayerHits, b.PlayerHits);
            Assert.AreEqual(a.EnemyHits, b.EnemyHits);
        }

        static Stats RunFight(int seed, int frames, bool punishing)
        {
            var stats = new Stats();
            var bot = new DeterministicRandom(seed);
            PlayerDriver player = punishing ? PlayerDriver.Punishing() : new PlayerDriver();
            var pool = new AttackTokenPool(2);
            var enemies = new List<Fighter>
            {
                MakeEnemy(EnemyTuning.CreateDaoSoldier(), pool, new Vector3(3f, 0f, 6f), 1, seed),
                MakeEnemy(EnemyTuning.CreateDaoSoldier(), pool, new Vector3(-3f, 0f, 6f), 2, seed),
                MakeEnemy(EnemyTuning.CreateCrossbowman(), pool, new Vector3(0f, 0f, 16f), 3, seed),
                MakeEnemy(EnemyTuning.CreateSparringDummy(), pool, new Vector3(12f, 0f, -6f), 4, seed)
            };
            ((SparringDummyBrain)enemies[3].Driver.Brain).SwingEnabled = true;
            var projectiles = new List<Projectile>();
            int playerActiveBalance = 0, playerDeadFrames = 0;
            float playerStateTime = 0f;
            PlayerState lastPlayerState = PlayerState.Locomotion;
            Pad holdButtons = Pad.None;
            int holdFrames = 0;
            Pad lastPad = Pad.None;
            bool dodgeNextFrame = false;
            Vector2 stick = Vector2.Zero;

            for (int frame = 0; frame < frames; frame++)
            {
                PlayerCombatModel model = player.Model;
                Fighter target = NearestLiving(enemies, player.World.Position);
                player.World.HasLockTarget = target != null;
                if (target != null) player.LockOn(target.Driver.World.Position, BodyRadius);

                // ---- bot input (decides every 6 frames, like a quick human)
                Pad pad = Pad.None;
                if (holdFrames > 0)
                {
                    holdFrames--;
                    pad |= holdButtons;
                }
                Pad defence = model.IsAlive ? ChooseDefence(bot, frame, player, enemies, ref holdButtons, ref holdFrames) : Pad.None;
                if (dodgeNextFrame)
                {
                    pad = Pad.Dodge;                             // the fresh press, one frame after letting go
                    dodgeNextFrame = false;
                }
                else if (defence == Pad.Dodge && (lastPad & Pad.Dodge) != 0)
                {
                    pad = Pad.None;                              // was holding dodge to sprint: let go first, press next frame
                    dodgeNextFrame = true;
                }
                else if (defence != Pad.None)
                {
                    pad |= defence;
                }
                else if (frame % 6 == 0 && model.IsAlive && (holdButtons & Pad.Guard) == 0)
                {
                    stick = ChooseStick(bot, player, target);
                    pad |= ChooseButtons(bot, player, target, ref holdButtons, ref holdFrames);
                }
                if (holdFrames == 0) holdButtons = Pad.None;
                player.Step(pad, stick);
                lastPad = pad;

                // ---- player events -> hits and projectiles
                foreach (PlayerEvent e in player.Last.Events)
                {
                    switch (e.Type)
                    {
                        case PlayerEventType.AttackActiveStart:
                            playerActiveBalance++;
                            PlayerMelee(player, enemies, e.Origin, e.Direction, e.Move, model.BuildDamage(e), stats);
                            break;
                        case PlayerEventType.AttackActiveEnd:
                            playerActiveBalance--;
                            break;
                        case PlayerEventType.PlungeImpact:
                            PlayerSphere(player, enemies, e.Origin, e.Radius, model.BuildDamage(e), stats);
                            break;
                        case PlayerEventType.ProjectileLaunched:
                            projectiles.Add(new Projectile { Position = e.Origin, Direction = e.Direction, Spec = e.Move.Projectile, Damage = model.BuildDamage(e), FromPlayer = true });
                            stats.Projectiles++;
                            break;
                        case PlayerEventType.PerfectDodge: stats.PerfectDodges++; break;
                        case PlayerEventType.Deflected: stats.Deflects++; break;
                        case PlayerEventType.Blocked: stats.Blocks++; break;
                        case PlayerEventType.Died: stats.Deaths++; break;
                    }
                }
                Assert.That(playerActiveBalance, Is.InRange(0, 1), "player active windows pair up");
                if (model.IsAttackActive && !FrameHas(player.Last, PlayerEventType.AttackActiveStart))
                    PlayerMelee(player, enemies, model.GetStrikeOrigin(player.World.Position), model.Forward, model.CurrentMove, model.BuildCurrentDamage(), stats);

                // ---- enemies
                foreach (Fighter f in enemies)
                {
                    EnemyDriver d = f.Driver;
                    d.World.HasTarget = model.IsAlive;
                    d.SetTarget(player.World.Position);
                    d.Step();
                    foreach (EnemyEvent e in d.Last.Events)
                    {
                        if (e.Type == EnemyEventType.TelegraphStarted)
                        {
                            f.StrikeFrame = frame + (int)Math.Ceiling(e.Duration / Dt);
                        }
                        else if (e.Type == EnemyEventType.AttackActiveStart)
                        {
                            f.ActiveBalance++;
                            // As EnemyStrikes.OpenMelee does in Unity: report every melee strike before its hit query, so a dodge away
                            // can still be perfect (the "would have landed" rule).
                            player.Model.NotifyEnemyStrike(e.Origin, e.Direction, e.Move, d.World.Position);
                            EnemyMelee(f, player, e.Origin, e.Direction, e.Move, d.Brain.BuildDamage(e), stats);
                        }
                        else if (e.Type == EnemyEventType.AttackActiveEnd)
                        {
                            f.ActiveBalance--;
                        }
                        else if (e.Type == EnemyEventType.ProjectileLaunched)
                        {
                            projectiles.Add(new Projectile { Position = e.Origin, Direction = e.Direction, Spec = e.Move.Projectile, Damage = d.Brain.BuildDamage(e), Owner = f });
                            stats.Projectiles++;
                        }
                        else if (e.Type == EnemyEventType.Died)
                        {
                            stats.Kills++;
                        }
                    }
                    Assert.That(f.ActiveBalance, Is.InRange(0, 1), d.Brain.Tuning.DisplayName + " active windows pair up");
                    if (d.Brain.IsAttackActive && !EnemyFrameHas(d.Last, EnemyEventType.AttackActiveStart))
                        EnemyMelee(f, player, d.Brain.GetStrikeOrigin(d.World.Position), d.Brain.Forward, d.Brain.CurrentMove, d.Brain.BuildCurrentDamage(), stats);

                    // Nothing stays in a busy state for longer than its longest legitimate duration. A fresh poise break
                    // (or deflect) while staggered restarts the stagger, so each stagger is timed from its own start.
                    float remaining = d.Brain.StaggerRemaining;
                    bool staggered = d.Brain.State == EnemyState.Staggered;
                    bool newStagger = staggered && (f.LastState != EnemyState.Staggered || remaining > f.LastStaggerRemaining + 1e-4f);
                    f.SinceStaggered = newStagger ? 0f : f.SinceStaggered + Dt;
                    f.LastStaggerRemaining = remaining;
                    float longestStagger = Math.Max(d.Brain.Tuning.StaggerDuration, d.Brain.Tuning.ParriedStaggerDuration);
                    if (staggered) Assert.Less(f.SinceStaggered, longestStagger + 2f * Dt, "enemy stagger stuck");
                    // A break-out can follow straight on from another attack, so each attack is timed from its own telegraph.
                    bool newAttack = EnemyFrameHas(d.Last, EnemyEventType.TelegraphStarted);
                    f.StateTime = d.Brain.State == f.LastState && !newAttack ? f.StateTime + Dt : 0f;
                    f.LastState = d.Brain.State;
                    if (d.Brain.State == EnemyState.Attacking) Assert.Less(f.StateTime, 2.5f, "enemy attack stuck");
                    Assert.That(d.Brain.Health, Is.InRange(0f, d.Brain.MaxHealth));

                    if (!d.Brain.IsAlive && ++f.DeadFrames > 180)
                    {
                        d.Brain.Reset();
                        d.World.Position = f.Spawn;
                        f.DeadFrames = 0;
                        f.ActiveBalance = 0;
                    }
                }
                Assert.LessOrEqual(pool.Count, 2);
                int soldiersAttacking = 0;
                for (int i = 0; i < 2; i++) if (enemies[i].Driver.Brain.State == EnemyState.Attacking) soldiersAttacking++;
                Assert.LessOrEqual(soldiersAttacking, 2);

                StepProjectiles(projectiles, player, enemies, stats);

                // ---- player invariants and respawn
                // Busy actions end on their own (a light chain may loop Attacking -> Attacking, so time each action).
                Assert.Less(model.ActionTime, 2.5f, "player stuck in " + model.State);
                playerStateTime = model.State == lastPlayerState ? playerStateTime + Dt : 0f;
                lastPlayerState = model.State;
                if (model.State == PlayerState.Airborne) Assert.Less(playerStateTime, 3f, "never lands");
                Assert.That(model.Stamina, Is.InRange(0f, model.MaxStamina));
                Assert.That(model.Health, Is.InRange(0f, model.MaxHealth));
                Assert.That(model.Momentum, Is.InRange(0f, 100f));
                Assert.That(model.HealCharges, Is.InRange(0, model.MaxHealCharges));
                if (!model.IsAlive && ++playerDeadFrames > 120)
                {
                    model.Respawn(0f);
                    player.World.Position = Vector3.Zero;
                    playerDeadFrames = 0;
                    playerActiveBalance = 0;
                }

                stats.Checksum += player.World.Position.X * 3.1 + player.World.Position.Z * 1.7 + model.Health + model.Stamina * 0.5;
                foreach (Fighter f in enemies) stats.Checksum += f.Driver.World.Position.X + f.Driver.World.Position.Z * 0.3 + f.Driver.Brain.Health;
            }
            return stats;
        }

        static Fighter MakeEnemy(EnemyTuning tuning, AttackTokenPool pool, Vector3 spawn, int id, int seed)
        {
            var driver = new EnemyDriver(tuning, pool, 1000 + id, seed * 31 + id);
            driver.World.Position = spawn;
            return new Fighter { Driver = driver, Spawn = spawn };
        }

        static Fighter NearestLiving(List<Fighter> enemies, Vector3 from)
        {
            Fighter best = null;
            float bestDistance = 22f;
            foreach (Fighter f in enemies)
            {
                if (!f.Driver.Brain.IsAlive || f.Driver.Brain.Tuning.Archetype == EnemyArchetype.Dummy) continue;
                float distance = Directions.Flatten(f.Driver.World.Position - from).Length();
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = f;
                }
            }
            return best;
        }

        static Vector2 ChooseStick(DeterministicRandom bot, PlayerDriver player, Fighter target)
        {
            if (target == null) return new Vector2(bot.Range(-1f, 1f), bot.Range(-1f, 1f));
            Vector3 to = Directions.Flatten(target.Driver.World.Position - player.World.Position);
            float distance = to.Length();
            if (distance < 1e-3f) return Vector2.Zero;
            to /= distance;
            if (distance < 1.6f) return new Vector2(-to.Z, to.X) * (bot.Chance(0.5f) ? 1f : -1f);   // circle
            return new Vector2(to.X, to.Z) * (distance > 2.4f ? 1f : 0.4f);
        }

        // Reads telegraphs like a player would: a few frames before a nearby strike lands, it sometimes
        // deflects (guard press, held a moment) or dodges, with some timing noise. Otherwise it may ignore it.
        static Pad ChooseDefence(DeterministicRandom bot, int frame, PlayerDriver player, List<Fighter> enemies,
            ref Pad holdButtons, ref int holdFrames)
        {
            foreach (Fighter f in enemies)
            {
                if (!f.Driver.Brain.IsTelegraphing) continue;
                float distance = Directions.Flatten(f.Driver.World.Position - player.World.Position).Length();
                int framesToStrike = f.StrikeFrame - frame;
                if (distance > 4.5f || framesToStrike != bot.Range(1, 10)) continue;
                if (bot.Chance(0.4f)) return Pad.None;
                if (bot.Chance(0.5f))
                {
                    holdButtons = Pad.None;                      // stop sprinting or charging to get out
                    holdFrames = 0;
                    return Pad.Dodge;
                }
                holdButtons = Pad.Guard;
                holdFrames = bot.Range(4, 30);
                return Pad.Guard;
            }
            return Pad.None;
        }

        static Pad ChooseButtons(DeterministicRandom bot, PlayerDriver player, Fighter target, ref Pad holdButtons, ref int holdFrames)
        {
            PlayerCombatModel model = player.Model;
            if (model.Health < 40f && model.HealCharges > 0 && bot.Chance(0.05f)) return Pad.Heal;
            if (model.Stamina < 25f) return Pad.None;           // like a person: keep enough stamina to dodge
            float distanceToTarget = target == null ? 99f : Directions.Flatten(target.Driver.World.Position - player.World.Position).Length();
            float roll = bot.NextFloat();
            if (distanceToTarget > 5f)
            {
                if (roll < 0.1f) return Pad.Skill;
                if (roll < 0.5f)
                {
                    holdButtons = Pad.Dodge;                     // sprint in
                    holdFrames = bot.Range(10, 60);
                }
                return Pad.None;
            }
            if (model.State == PlayerState.Sprinting && roll < 0.5f) return Pad.Light | Pad.Dodge;
            if (roll < 0.55f) return Pad.Light;
            if (roll < 0.75f)
            {
                holdButtons = Pad.Heavy;
                holdFrames = bot.Range(1, 75);
                return Pad.Heavy;
            }
            if (roll < 0.82f) return Pad.Jump;
            if (roll < 0.88f && model.State == PlayerState.Airborne) return Pad.Light;
            if (roll < 0.92f) return Pad.Skill;
            return Pad.None;
        }

        static void PlayerMelee(PlayerDriver player, List<Fighter> enemies, Vector3 origin, Vector3 forward, MoveData move,
            DamageInfo damage, Stats stats)
        {
            if (move == null) return;
            foreach (Fighter f in enemies)
            {
                Vector3 feet = f.Driver.World.Position;
                if (!f.Driver.Brain.IsAlive) continue;
                if (!HitGeometry.InArc(origin, forward, move.Range, move.ArcDegrees, move.VerticalReach, feet, BodyHeight, BodyRadius)) continue;
                LandPlayerHit(player, f, damage, stats);
            }
        }

        static void PlayerSphere(PlayerDriver player, List<Fighter> enemies, Vector3 centre, float radius, DamageInfo damage, Stats stats)
        {
            foreach (Fighter f in enemies)
            {
                if (!f.Driver.Brain.IsAlive) continue;
                if (HitGeometry.SurfaceDistance(centre, 0f, f.Driver.World.Position, BodyRadius) > radius) continue;
                LandPlayerHit(player, f, damage, stats);
            }
        }

        static void LandPlayerHit(PlayerDriver player, Fighter f, DamageInfo damage, Stats stats)
        {
            damage.Direction = Directions.SafeNormalize(Directions.Flatten(f.Driver.World.Position - player.World.Position), Vector3.Zero);
            HitResult result = f.Driver.Brain.ReceiveHit(damage, f.Driver.Brain.Forward);
            player.Model.OnAttackLanded(result, damage.AttackId);
            if (result.Outcome == HitOutcome.Hit) stats.PlayerHits++;
        }

        static void EnemyMelee(Fighter f, PlayerDriver player, Vector3 origin, Vector3 forward, MoveData move, DamageInfo damage, Stats stats)
        {
            if (move == null || !player.Model.IsAlive) return;
            if (!HitGeometry.InArc(origin, forward, move.Range, move.ArcDegrees, move.VerticalReach, player.World.Position, BodyHeight, BodyRadius)) return;
            damage.Direction = Directions.SafeNormalize(Directions.Flatten(player.World.Position - f.Driver.World.Position), Vector3.Zero);
            HitResult result = player.Model.ReceiveHit(damage, player.Model.Forward);
            if (result.Outcome == HitOutcome.Parried) f.Driver.Brain.OnParried();
            if (result.Outcome == HitOutcome.Hit) stats.EnemyHits++;
        }

        static void StepProjectiles(List<Projectile> projectiles, PlayerDriver player, List<Fighter> enemies, Stats stats)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                Projectile p = projectiles[i];
                Vector3 from = p.Position;
                Vector3 to = from + p.Direction * (p.Spec.Speed * Dt);
                bool hit = false;
                if (p.FromPlayer)
                {
                    foreach (Fighter f in enemies)
                    {
                        if (!f.Driver.Brain.IsAlive) continue;
                        if (HitGeometry.SweepSphereVsCapsule(from, to, p.Spec.Radius, f.Driver.World.Position, BodyHeight, BodyRadius) < 0f) continue;
                        LandPlayerHit(player, f, p.Damage, stats);
                        hit = true;
                        break;
                    }
                }
                else if (player.Model.IsAlive
                    && HitGeometry.SweepSphereVsCapsule(from, to, p.Spec.Radius, player.World.Position, BodyHeight, BodyRadius) >= 0f)
                {
                    DamageInfo damage = p.Damage;
                    damage.Direction = Directions.SafeNormalize(Directions.Flatten(p.Direction), Vector3.Zero);
                    HitResult result = player.Model.ReceiveHit(damage, player.Model.Forward);
                    // A dodged bolt flies on; anything else stops it. A deflected bolt doesn't stagger the archer.
                    hit = result.Outcome != HitOutcome.Evaded && result.Outcome != HitOutcome.PerfectEvade;
                    if (result.Outcome == HitOutcome.Hit) stats.EnemyHits++;
                }
                p.Position = to;
                p.Travelled += p.Spec.Speed * Dt;
                if (hit || p.Travelled > p.Spec.MaxRange || p.Position.Y < -1f) projectiles.RemoveAt(i);
            }
        }

        static bool FrameHas(PlayerTickResult result, PlayerEventType type)
        {
            foreach (PlayerEvent e in result.Events) if (e.Type == type) return true;
            return false;
        }

        static bool EnemyFrameHas(EnemyTickResult result, EnemyEventType type)
        {
            foreach (EnemyEvent e in result.Events) if (e.Type == type) return true;
            return false;
        }
    }
}
