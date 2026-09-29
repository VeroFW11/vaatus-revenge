namespace VaatusRevenge.Core
{
    // Progress curves for dashes and lunges, sampled so the total distance is exact at any frame rate.
    public static class MotionCurves
    {
        // Share of a dash or lunge covered at progress u (0..1). ease 0 = constant speed, 1 = quadratic
        // ease-out (fast start, slows to a stop). Always 0 at u = 0 and exactly 1 at u = 1, so the total
        // distance is exact at any frame rate when each frame moves by the difference between two samples.
        public static float EaseOut(float u, float ease)
        {
            u = Angles.Clamp(u, 0f, 1f);
            float eased = 1f - (1f - u) * (1f - u);
            float k = Angles.Clamp(ease, 0f, 1f);
            return u + (eased - u) * k;
        }

        // Progress of a window [start, end] at time t, eased. Zero-length windows jump straight to 1.
        public static float WindowProgress(float t, float start, float end, float ease)
        {
            if (t <= start) return end <= start && t >= start ? 1f : 0f;
            if (end <= start || t >= end) return 1f;
            return EaseOut((t - start) / (end - start), ease);
        }
    }
}
