using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Screen shake as plain maths. Each shake fades out over its duration; overlapping shakes add up, capped
    // at full strength so a flurry of hits can't throw the camera around. The wobble is smooth noise, so it
    // looks like a jolt rather than random flicker, and it's deterministic (same inputs, same shake).
    // Shake rotates the camera rather than moving it: a moved camera could be pushed into a wall.
    public sealed class CameraShakeModel
    {
        const int MaxShakes = 8;

        readonly float[] amplitudes = new float[MaxShakes];
        readonly float[] durations = new float[MaxShakes];
        readonly float[] ages = new float[MaxShakes];
        float time;

        // Combined strength right now, 0..1.
        public float Strength { get; private set; }

        // amplitude: 0..1 (0.05 = a light tap, 0.3 = a big impact). duration: seconds.
        public void Add(float amplitude, float duration)
        {
            if (!(amplitude > 0f) || !(duration > 0f) || float.IsInfinity(amplitude) || float.IsInfinity(duration)) return;

            // Use the slot with the least shake left in it (a free slot has none).
            int slot = 0;
            float weakest = float.MaxValue;
            for (int i = 0; i < MaxShakes; i++)
            {
                float remaining = Remaining(i);
                if (remaining < weakest)
                {
                    weakest = remaining;
                    slot = i;
                }
            }
            amplitudes[slot] = Math.Min(1f, amplitude);
            durations[slot] = duration;
            ages[slot] = 0f;
            Strength = Combined();
        }

        public void Clear()
        {
            Array.Clear(amplitudes, 0, MaxShakes);
            Array.Clear(ages, 0, MaxShakes);
            Array.Clear(durations, 0, MaxShakes);
            Strength = 0f;
            time = 0f;
        }

        // Advance by deltaTime seconds (use real time, so shake still plays during hitstop freezes).
        public void Tick(float deltaTime)
        {
            float dt = CameraMath.SafeDeltaTime(deltaTime);
            for (int i = 0; i < MaxShakes; i++)
            {
                if (amplitudes[i] <= 0f) continue;
                ages[i] += dt;
                if (ages[i] >= durations[i]) amplitudes[i] = 0f;
            }
            Strength = Combined();
            // The noise clock restarts when nothing is shaking, so it never grows large enough to lose precision.
            time = Strength > 0f ? time + dt : 0f;
        }

        // Rotation offsets in degrees: X = pitch, Y = yaw, Z = roll.
        public Vector3 SampleAngles(float frequency, float maxAngle)
        {
            if (Strength <= 0f || !CameraMath.IsFinite(frequency) || !CameraMath.IsFinite(maxAngle)) return Vector3.Zero;
            float t = time * Math.Max(0f, frequency);
            float scale = Strength * maxAngle;
            return new Vector3(Noise(t, 0), Noise(t, 1), Noise(t, 2)) * scale;
        }

        // Smooth pseudo-random wobble in -1..1 (1D value noise). Each channel is an independent stream.
        public static float Noise(float x, int channel)
        {
            if (!CameraMath.IsFinite(x)) return 0f;
            float floor = (float)Math.Floor(x);
            int cell = (int)floor + channel * 7919;
            float t = x - floor;
            float s = t * t * (3f - 2f * t); // smoothstep: eases between the random values instead of snapping
            float a = Hash(cell);
            float b = Hash(cell + 1);
            return a + (b - a) * s;
        }

        float Remaining(int i)
        {
            if (amplitudes[i] <= 0f || durations[i] <= 0f) return 0f;
            float left = 1f - ages[i] / durations[i];
            // Squared, so it hits hard and dies away quickly with a smooth end.
            return left > 0f ? amplitudes[i] * left * left : 0f;
        }

        float Combined()
        {
            float total = 0f;
            for (int i = 0; i < MaxShakes; i++) total += Remaining(i);
            return Math.Min(1f, total);
        }

        // Classic integer hash to a value in -1..1.
        static float Hash(int n)
        {
            unchecked
            {
                n = (n << 13) ^ n;
                int m = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
                return 1f - m / 1073741824f;
            }
        }
    }
}
