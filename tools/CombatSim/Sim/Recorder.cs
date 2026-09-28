using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // Writes a compact per-frame replay (JSON) so a session can be rendered as a video later.
    // Layout (see "frameFormat" in the header): one array per frame
    //   [frame, realTime, gameTime, timeScale, [fighter...], [camera], [projectile...]]
    // player fighter:  [x, y, z, yaw, stateIndex, health, stamina, momentum, invulnerable 0/1, chargeLevel]
    // enemy fighter:   [x, y, z, yaw, stateIndex, health, phaseIndex, alive 0/1]
    // camera:          [yaw, pitch, distance, camX, camY, camZ, lockTargetFighterIndex or -1]
    // projectile:      [x, y, z, isFire 0/1]
    // Events: [frame, fighterIndex (-1 = world), type, detail].
    public sealed class Recorder
    {
        readonly StringBuilder frames = new StringBuilder(1 << 20);
        readonly StringBuilder events = new StringBuilder(1 << 16);
        readonly string scenario;
        readonly Dictionary<string, string> meta = new Dictionary<string, string>();
        int frameCount;
        int eventCount;
        bool headerFighters;
        string fightersJson = "[]";

        public Recorder(string scenario)
        {
            this.scenario = scenario;
        }

        public void Meta(string key, string value)
        {
            meta[key] = value;
        }

        static string F(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "null";
            return Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string F(double v)
        {
            return Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
        }

        static string Q(string s)
        {
            return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        void EnsureFighters(SimWorld w)
        {
            if (headerFighters) return;
            headerFighters = true;
            var sb = new StringBuilder("[");
            for (int i = 0; i < w.Fighters.Count; i++)
            {
                SimFighter f = w.Fighters[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"index\":").Append(i).Append(",\"name\":").Append(Q(f.Name)).Append(",\"team\":").Append(Q(f.Team.ToString()))
                  .Append(",\"radius\":").Append(F(f.Radius)).Append(",\"height\":").Append(F(f.Height)).Append('}');
            }
            sb.Append(']');
            fightersJson = sb.ToString();
        }

        public void Capture(SimWorld w)
        {
            EnsureFighters(w);
            if (frameCount > 0) frames.Append(",\n");
            frameCount++;
            frames.Append('[').Append(w.Frame).Append(',').Append(F(w.RealTime)).Append(',').Append(F(w.GameTime)).Append(',')
                  .Append(F(w.Time.TimeScale)).Append(",[");
            for (int i = 0; i < w.Fighters.Count; i++)
            {
                SimFighter f = w.Fighters[i];
                if (i > 0) frames.Append(',');
                Vector3 p = f.Feet;
                frames.Append('[').Append(F(p.X)).Append(',').Append(F(p.Y)).Append(',').Append(F(p.Z)).Append(',').Append(F(f.Yaw)).Append(',');
                if (f is SimPlayer sp)
                {
                    PlayerCombatModel m = sp.Model;
                    frames.Append((int)m.State).Append(',').Append(F(m.Health)).Append(',').Append(F(m.Stamina)).Append(',')
                          .Append(F(m.Momentum)).Append(',').Append(m.IsInvulnerable ? 1 : 0).Append(',').Append(F(m.ChargeLevel));
                }
                else if (f is SimEnemy se)
                {
                    EnemyBrain b = se.Brain;
                    frames.Append((int)b.State).Append(',').Append(F(b.Health)).Append(',').Append((int)b.Phase).Append(',')
                          .Append(b.IsAlive ? 1 : 0);
                }
                frames.Append(']');
            }
            frames.Append("],[");
            if (w.LockOn != null)
            {
                OrbitCameraModel o = w.LockOn.Orbit;
                Vector3 c = w.LockOn.CameraPosition;
                int target = w.LockOn.Target != null ? w.IndexOf(w.LockOn.Target) : -1;
                frames.Append(F(o.Yaw)).Append(',').Append(F(o.Pitch)).Append(',').Append(F(o.Distance)).Append(',')
                      .Append(F(c.X)).Append(',').Append(F(c.Y)).Append(',').Append(F(c.Z)).Append(',').Append(target);
            }
            frames.Append("],[");
            for (int i = 0; i < w.Projectiles.Flying.Count; i++)
            {
                var pr = w.Projectiles.Flying[i];
                if (i > 0) frames.Append(',');
                frames.Append('[').Append(F(pr.Position.X)).Append(',').Append(F(pr.Position.Y)).Append(',').Append(F(pr.Position.Z))
                      .Append(',').Append(pr.IsFire ? 1 : 0).Append(']');
            }
            frames.Append("]]");
        }

        public void Note(SimWorld w, int fighter, string type, string detail)
        {
            if (eventCount > 0) events.Append(",\n");
            eventCount++;
            events.Append('[').Append(w.Frame).Append(',').Append(fighter).Append(',').Append(Q(type)).Append(',').Append(Q(detail)).Append(']');
        }

        public void OnPlayerEvent(SimWorld w, in PlayerEvent e)
        {
            string detail = "";
            switch (e.Type)
            {
                case PlayerEventType.AttackStarted:
                case PlayerEventType.AttackActiveStart:
                case PlayerEventType.AttackActiveEnd:
                case PlayerEventType.AttackEnded:
                case PlayerEventType.ChargeStarted:
                    detail = (e.Move != null ? e.Move.DisplayName : "") + (e.ChargeTier != ChargeTier.None ? " " + e.ChargeTier : "")
                             + (e.IsCounter ? " counter" : "") + " #" + e.AttackId;
                    break;
                case PlayerEventType.DodgeStarted:
                    detail = (e.IsBackstep ? "backstep " : "") + "dir(" + F(e.Direction.X) + "," + F(e.Direction.Z) + ")";
                    break;
                case PlayerEventType.Damaged:
                case PlayerEventType.HealApplied:
                case PlayerEventType.Blocked:
                case PlayerEventType.Deflected:
                    detail = F(e.Amount);
                    break;
                case PlayerEventType.Staggered:
                case PlayerEventType.GuardBroken:
                    detail = F(e.Duration) + " s";
                    break;
            }
            Note(w, 0, "player:" + e.Type, detail);
        }

        public void OnEnemyEvent(SimWorld w, SimEnemy enemy, in EnemyEvent e)
        {
            string detail = "";
            switch (e.Type)
            {
                case EnemyEventType.TelegraphStarted:
                    detail = (e.Move != null ? e.Move.DisplayName : "") + " " + e.Telegraph + " " + F(e.Duration) + " s #" + e.AttackId;
                    break;
                case EnemyEventType.AttackActiveStart:
                case EnemyEventType.AttackActiveEnd:
                case EnemyEventType.ProjectileLaunched:
                    detail = (e.Move != null ? e.Move.DisplayName : "") + " hit " + e.HitIndex + " #" + e.AttackId;
                    break;
                case EnemyEventType.Damaged:
                    detail = F(e.Amount);
                    break;
                case EnemyEventType.Staggered:
                    detail = F(e.Duration) + " s";
                    break;
            }
            Note(w, w.IndexOf(enemy), "enemy:" + e.Type, detail);
        }

        public void OnHit(SimWorld w, SimFighter target, in DamageInfo hit, in HitResult result)
        {
            if (result.Outcome == HitOutcome.Ignored) return;
            Note(w, w.IndexOf(target), "hit", result.Outcome + " " + F(result.DamageDealt) + " by #" + hit.SourceId + " " + hit.Kind
                                               + (result.PoiseBroken ? " poise-broken" : "") + (result.Killed ? " killed" : ""));
        }

        public void Save(string path, float dt)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder(frames.Length + events.Length + 4096);
            sb.Append("{\n\"format\":\"vaatus-combatsim-replay/1\",\n\"scenario\":").Append(Q(scenario)).Append(",\n\"dt\":").Append(F(dt));
            foreach (var kv in meta) sb.Append(",\n").Append(Q(kv.Key)).Append(':').Append(Q(kv.Value));
            sb.Append(",\n\"playerStates\":[");
            string[] ps = Enum.GetNames(typeof(PlayerState));
            for (int i = 0; i < ps.Length; i++) sb.Append(i > 0 ? "," : "").Append(Q(ps[i]));
            sb.Append("],\n\"enemyStates\":[");
            string[] es = Enum.GetNames(typeof(EnemyState));
            for (int i = 0; i < es.Length; i++) sb.Append(i > 0 ? "," : "").Append(Q(es[i]));
            sb.Append("],\n\"enemyPhases\":[\"None\",\"Startup\",\"Active\",\"Recovery\"]");
            sb.Append(",\n\"frameFormat\":").Append(Q("[frame, realTime, gameTime, timeScale, [player:[x,y,z,yaw,state,hp,stamina,momentum,invulnerable,charge] | enemy:[x,y,z,yaw,state,hp,phase,alive]], [camYaw,camPitch,camDist,camX,camY,camZ,lockTargetIndex], [projectile:[x,y,z,isFire]]]"));
            sb.Append(",\n\"fighters\":").Append(fightersJson);
            sb.Append(",\n\"frames\":[\n").Append(frames).Append("\n]");
            sb.Append(",\n\"events\":[\n").Append(events).Append("\n]\n}\n");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
