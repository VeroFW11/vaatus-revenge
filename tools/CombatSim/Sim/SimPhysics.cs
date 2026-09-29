using System;
using System.Collections.Generic;
using System.Numerics;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // An axis-aligned box of level geometry (walls, pillars, the platform block, the corridor roof).
    public struct Aabb
    {
        public string Name;
        public Vector3 Min;
        public Vector3 Max;

        public Aabb(string name, Vector3 center, Vector3 size)
        {
            Name = name;
            Min = center - size * 0.5f;
            Max = center + size * 0.5f;
        }

        public static Aabb OnFloor(string name, Vector3 bottomCenter, Vector3 size)
        {
            return new Aabb(name, bottomCenter + new Vector3(0f, size.Y * 0.5f, 0f), size);
        }

        public bool FootprintContains(float x, float z)
        {
            return x >= Min.X && x <= Max.X && z >= Min.Z && z <= Max.Z;
        }

        public bool OverlapsSphere(Vector3 c, float r)
        {
            Vector3 closest = Vector3.Clamp(c, Min, Max);
            return Vector3.DistanceSquared(closest, c) <= r * r;
        }

        // Slab test: does the segment a->b cross the box? t = fraction along the segment of the entry point.
        public bool Segment(Vector3 a, Vector3 b, out float t)
        {
            t = 0f;
            Vector3 d = b - a;
            float tMin = 0f, tMax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? a.X : axis == 1 ? a.Y : a.Z;
                float dir = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
                float lo = axis == 0 ? Min.X : axis == 1 ? Min.Y : Min.Z;
                float hi = axis == 0 ? Max.X : axis == 1 ? Max.Y : Max.Z;
                if (Math.Abs(dir) < 1e-9f)
                {
                    if (o < lo || o > hi) return false;
                    continue;
                }
                float t1 = (lo - o) / dir, t2 = (hi - o) / dir;
                if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                tMin = Math.Max(tMin, t1);
                tMax = Math.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            t = tMin;
            return true;
        }
    }

    // The grey-box arena from Editor/Sandbox/ArenaBuilder.cs, rebuilt as boxes: floor, 4 walls, the pillar
    // field, the low corridor, the raised platform (with a ramp as a height field and the stairs as boxes).
    // Layers.EnvironmentMask = everything here (Default layer). Fighters are not in this list.
    public sealed class SimLevel
    {
        public readonly List<Aabb> Solids = new List<Aabb>();
        public bool HasRamp;
        // Ramp from ArenaBuilder: top (-10, 2.5, 12) down to (-3.8, 0, 12), 3 m wide.
        public float RampTopX = -10f, RampBottomX = -3.8f, RampZ = 12f, RampHalfWidth = 1.5f, RampHeight = 2.5f;

        public static SimLevel Empty()
        {
            var level = new SimLevel();
            level.Solids.Add(new Aabb("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(400f, 1f, 400f)));
            return level;
        }

        // Mirror of ArenaBuilder.Build (positions and sizes copied from the builder's constants).
        public static SimLevel SandboxArena()
        {
            var level = new SimLevel();
            const float floorSize = 60f, wallHeight = 4f, wallThickness = 1f;
            level.Solids.Add(new Aabb("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(floorSize, 1f, floorSize)));
            float half = floorSize * 0.5f + wallThickness * 0.5f;
            float span = floorSize + wallThickness * 2f;
            level.Solids.Add(Aabb.OnFloor("Wall_North", new Vector3(0f, 0f, half), new Vector3(span, wallHeight, wallThickness)));
            level.Solids.Add(Aabb.OnFloor("Wall_South", new Vector3(0f, 0f, -half), new Vector3(span, wallHeight, wallThickness)));
            level.Solids.Add(Aabb.OnFloor("Wall_East", new Vector3(half, 0f, 0f), new Vector3(wallThickness, wallHeight, floorSize)));
            level.Solids.Add(Aabb.OnFloor("Wall_West", new Vector3(-half, 0f, 0f), new Vector3(wallThickness, wallHeight, floorSize)));
            float[] xs = { 12f, 17f, 22f };
            float[] zs = { -24f, -19f, -14f, -9f };
            for (int i = 0; i < xs.Length; i++)
            for (int j = 0; j < zs.Length; j++)
            {
                float w = (i + j) % 2 == 0 ? 1f : 1.6f;
                level.Solids.Add(Aabb.OnFloor("Pillar_" + i + "_" + j, new Vector3(xs[i], 0f, zs[j]), new Vector3(w, wallHeight, w)));
            }
            var corridor = new Vector3(18f, 0f, 12f);
            const float corridorWidth = 3f, corridorLength = 12f, corridorHeight = 2.6f, cWall = 0.5f, roof = 0.3f;
            float side = corridorWidth * 0.5f + cWall * 0.5f;
            level.Solids.Add(Aabb.OnFloor("Corridor_West", corridor + new Vector3(-side, 0f, 0f), new Vector3(cWall, corridorHeight, corridorLength)));
            level.Solids.Add(Aabb.OnFloor("Corridor_East", corridor + new Vector3(side, 0f, 0f), new Vector3(cWall, corridorHeight, corridorLength)));
            level.Solids.Add(Aabb.OnFloor("Corridor_Roof", corridor + new Vector3(0f, corridorHeight, 0f), new Vector3(corridorWidth + cWall * 2f, roof, corridorLength)));
            var platform = new Vector3(-13f, 0f, 12f);
            const float platformSize = 6f, platformHeight = 2.5f, stepRise = 0.25f, stepRun = 0.4f, stairWidth = 2.5f;
            level.Solids.Add(Aabb.OnFloor("Platform", platform, new Vector3(platformSize, platformHeight, platformSize)));
            int steps = (int)Math.Round(platformHeight / stepRise) - 1;
            for (int k = 1; k <= steps; k++)
            {
                float h = platformHeight - stepRise * k;
                float z = platform.Z + platformSize * 0.5f + stepRun * (k - 0.5f);
                level.Solids.Add(Aabb.OnFloor("Step_" + k, new Vector3(platform.X, 0f, z), new Vector3(stairWidth, h, stepRun)));
            }
            level.HasRamp = true;
            return level;
        }

        // Height of the walkable surface under (x, z) that a foot at footY can stand on (step offset allowed).
        public float GroundHeight(float x, float z, float footY, float stepOffset)
        {
            float best = float.NegativeInfinity;
            for (int i = 0; i < Solids.Count; i++)
            {
                Aabb b = Solids[i];
                if (!b.FootprintContains(x, z)) continue;
                if (b.Max.Y <= footY + stepOffset + 1e-4f && b.Max.Y > best) best = b.Max.Y;
            }
            if (HasRamp && Math.Abs(z - RampZ) <= RampHalfWidth && x >= RampTopX && x <= RampBottomX)
            {
                float u = (x - RampTopX) / (RampBottomX - RampTopX);
                float h = RampHeight * (1f - u);
                if (h <= footY + stepOffset + 1e-4f && h > best) best = h;
            }
            return best;
        }

        public bool IsBlocked(Vector3 from, Vector3 to)
        {
            if (Vector3.DistanceSquared(from, to) < 1e-8f) return false;
            for (int i = 0; i < Solids.Count; i++)
            {
                if (Solids[i].Segment(from, to, out _)) return true;
            }
            return false;
        }

        public bool IsOverlapping(Vector3 c, float r)
        {
            for (int i = 0; i < Solids.Count; i++)
            {
                if (Solids[i].OverlapsSphere(c, r)) return true;
            }
            return false;
        }

        // Nearest distance along dir at which a sphere of radius r touches geometry, like Physics.SphereCast
        // (geometry the sphere already overlaps at the start is ignored by the caller's CheckSphere branch).
        public bool SphereCast(Vector3 origin, float r, Vector3 dir, float maxDistance, out float distance)
        {
            distance = maxDistance;
            if (maxDistance <= 0f) return false;
            float step = Math.Max(0.005f, Math.Min(0.05f, r * 0.5f));
            float prev = 0f;
            for (float s = step; ; s += step)
            {
                if (s > maxDistance) s = maxDistance;
                if (IsOverlappingExcludingStart(origin + dir * s, r, origin))
                {
                    float lo = prev, hi = s;
                    for (int k = 0; k < 12; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (IsOverlappingExcludingStart(origin + dir * mid, r, origin)) hi = mid;
                        else lo = mid;
                    }
                    distance = lo;
                    return true;
                }
                prev = s;
                if (s >= maxDistance) break;
            }
            return false;
        }

        bool IsOverlappingExcludingStart(Vector3 c, float r, Vector3 start)
        {
            for (int i = 0; i < Solids.Count; i++)
            {
                if (!Solids[i].OverlapsSphere(c, r)) continue;
                if (Solids[i].OverlapsSphere(start, r)) continue; // SphereCast can't see what it starts inside
                return true;
            }
            return false;
        }
    }

    // A kinematic CharacterController stand-in: a vertical capsule (feet pivot) that slides along boxes and
    // other enabled controllers, steps up to StepOffset, and reports isGrounded after each Move like Unity.
    // Simplifications (listed in the README): boxes only, circle-vs-rectangle push-out, no slope limit.
    public sealed class SimController
    {
        public Vector3 Position;
        public float Radius = 0.4f;
        public float Height = 1.8f;
        public float StepOffset = 0.3f;
        public bool Enabled = true;
        public bool IsGrounded = true;

        public void Move(Vector3 delta, SimLevel level, IReadOnlyList<SimFighter> fighters, SimFighter self)
        {
            if (!Enabled) return;
            if (!(Finite(delta))) return;
            Vector3 p = Position;
            // Horizontal first, in sub-steps so a fast dash can't tunnel through a thin wall or a body.
            Vector3 flat = new Vector3(delta.X, 0f, delta.Z);
            int subSteps = Math.Max(1, (int)Math.Ceiling(flat.Length() / (Radius * 0.5f)));
            for (int s = 0; s < subSteps; s++)
            {
                p += flat / subSteps;
                for (int iter = 0; iter < 3; iter++)
                {
                    p = PushOutOfBoxes(p, level);
                    p = PushOutOfFighters(p, fighters, self);
                }
            }
            // Vertical.
            float ground = level.GroundHeight(p.X, p.Z, p.Y, StepOffset);
            float newY = p.Y + delta.Y;
            // Ceiling (corridor roof, platform underside...).
            if (delta.Y > 0f)
            {
                for (int i = 0; i < level.Solids.Count; i++)
                {
                    Aabb b = level.Solids[i];
                    if (!CircleOverlapsRect(p, Radius * 0.9f, b)) continue;
                    if (b.Min.Y >= p.Y + Height - 1e-3f && newY + Height > b.Min.Y) newY = Math.Min(newY, b.Min.Y - Height);
                }
            }
            if (newY <= ground + 1e-4f && (delta.Y <= 0f || ground > p.Y))
            {
                newY = ground;
                IsGrounded = true;
            }
            else
            {
                IsGrounded = false;
            }
            p.Y = newY;
            Position = p;
        }

        Vector3 PushOutOfBoxes(Vector3 p, SimLevel level)
        {
            for (int i = 0; i < level.Solids.Count; i++)
            {
                Aabb b = level.Solids[i];
                // Stand on / step onto boxes whose top is within the step offset; others block sideways.
                if (b.Max.Y <= p.Y + StepOffset + 1e-4f) continue;
                if (b.Min.Y >= p.Y + Height) continue;
                float cx = Math.Clamp(p.X, b.Min.X, b.Max.X);
                float cz = Math.Clamp(p.Z, b.Min.Z, b.Max.Z);
                float dx = p.X - cx, dz = p.Z - cz;
                float d2 = dx * dx + dz * dz;
                if (d2 >= Radius * Radius) continue;
                if (d2 > 1e-10f)
                {
                    float d = (float)Math.Sqrt(d2);
                    float push = Radius - d;
                    p.X += dx / d * push;
                    p.Z += dz / d * push;
                }
                else
                {
                    // Centre inside the box: leave by the nearest face.
                    float left = p.X - b.Min.X, right = b.Max.X - p.X, back = p.Z - b.Min.Z, front = b.Max.Z - p.Z;
                    float m = Math.Min(Math.Min(left, right), Math.Min(back, front));
                    if (m == left) p.X = b.Min.X - Radius;
                    else if (m == right) p.X = b.Max.X + Radius;
                    else if (m == back) p.Z = b.Min.Z - Radius;
                    else p.Z = b.Max.Z + Radius;
                }
            }
            return p;
        }

        Vector3 PushOutOfFighters(Vector3 p, IReadOnlyList<SimFighter> fighters, SimFighter self)
        {
            if (fighters == null) return p;
            for (int i = 0; i < fighters.Count; i++)
            {
                SimFighter other = fighters[i];
                if (other == self || !other.Active || !other.Controller.Enabled) continue;
                Vector3 o = other.Controller.Position;
                if (o.Y >= p.Y + Height || o.Y + other.Controller.Height <= p.Y) continue;
                float dx = p.X - o.X, dz = p.Z - o.Z;
                float min = Radius + other.Controller.Radius;
                float d2 = dx * dx + dz * dz;
                if (d2 >= min * min) continue;
                if (d2 < 1e-10f)
                {
                    dx = 1f; dz = 0f; d2 = 1f;
                }
                float d = (float)Math.Sqrt(d2);
                p.X = o.X + dx / d * min;
                p.Z = o.Z + dz / d * min;
            }
            return p;
        }

        static bool CircleOverlapsRect(Vector3 p, float r, Aabb b)
        {
            float cx = Math.Clamp(p.X, b.Min.X, b.Max.X);
            float cz = Math.Clamp(p.Z, b.Min.Z, b.Max.Z);
            float dx = p.X - cx, dz = p.Z - cz;
            return dx * dx + dz * dz < r * r;
        }

        static bool Finite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }
    }

    // Mirror of Combat/TimeScaleController.cs: the one owner of the time scale. Hitstop = 0.02x (not 0),
    // slow motion = slowest request, pause = 0. Timers run on real time. A change made during frame N only
    // affects frame N+1's deltaTime (Unity computes deltaTime at the start of the frame).
    public sealed class SimTime
    {
        public const float HitstopScale = 0.02f;
        const int MaxSlowRequests = 4;
        const float MaxRequestSeconds = 5f;

        float hitstopRemaining;
        readonly float[] slowScale = new float[MaxSlowRequests];
        readonly float[] slowRemaining = new float[MaxSlowRequests];
        public bool Paused;
        public bool DebugSlow;
        public float DebugSlowScale = 0.25f;
        public float TimeScale { get; private set; } = 1f;
        public bool IsHitstopActive => hitstopRemaining > 0f;
        public bool IsSlowMotionActive => SlowestRequest() < 1f;

        public void Hitstop(float seconds)
        {
            if (!(seconds > 0f)) return;
            hitstopRemaining = Math.Max(hitstopRemaining, Math.Min(seconds, MaxRequestSeconds));
            Apply();
        }

        public void SlowMotion(float seconds, float scale)
        {
            if (!(seconds > 0f) || !(scale < 1f)) return;
            int slot = 0;
            for (int i = 0; i < MaxSlowRequests; i++)
            {
                if (slowRemaining[i] <= 0f) { slot = i; break; }
                if (slowRemaining[i] < slowRemaining[slot]) slot = i;
            }
            slowScale[slot] = Math.Clamp(scale, 0.01f, 1f);
            slowRemaining[slot] = Math.Min(seconds, MaxRequestSeconds);
            Apply();
        }

        public void ClearEffects()
        {
            hitstopRemaining = 0f;
            for (int i = 0; i < MaxSlowRequests; i++) slowRemaining[i] = 0f;
            Apply();
        }

        // Order -200: the first script of every frame.
        public void Update(float unscaledDt)
        {
            if (!Paused && unscaledDt > 0f)
            {
                hitstopRemaining -= unscaledDt;
                if (hitstopRemaining <= unscaledDt * 0.5f) hitstopRemaining = 0f;
                for (int i = 0; i < MaxSlowRequests; i++)
                {
                    if (slowRemaining[i] > 0f) slowRemaining[i] = Math.Max(0f, slowRemaining[i] - unscaledDt);
                }
            }
            Apply();
        }

        float SlowestRequest()
        {
            float slowest = 1f;
            for (int i = 0; i < MaxSlowRequests; i++)
            {
                if (slowRemaining[i] > 0f && slowScale[i] < slowest) slowest = slowScale[i];
            }
            return slowest;
        }

        void Apply()
        {
            float scale;
            if (Paused) scale = 0f;
            else if (hitstopRemaining > 0f) scale = HitstopScale;
            else
            {
                scale = SlowestRequest();
                if (DebugSlow) scale = Math.Min(scale, DebugSlowScale);
            }
            TimeScale = scale;
        }
    }
}
