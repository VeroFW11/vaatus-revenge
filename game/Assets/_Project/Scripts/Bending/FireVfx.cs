using UnityEngine;

namespace VaatusRevenge
{
    // Grey-box fire effects: glowing blobs, shockwave rings, trails and flashes of light, built from pooled
    // primitives (no particle assets needed). Call from anywhere during play; the first call creates a
    // hidden pool object. Calls in edit mode or while play is stopping do nothing, so nothing is ever left
    // behind in a scene. If the URP shaders can't be found, effects are skipped with a single warning.
    //
    // Effects keep animating in real time during hitstop (the impact burst blooms while the fighters are
    // frozen, like in fighting games) and slow down with slow motion.
    public static class FireVfx
    {
        const float EmberTrailFallbackSeconds = 1.5f;   // an open-ended ember trail's flame ribbon stops by itself after this

        static FireVfxRunner runner;
        static FireVfxStyle style;
        static bool quitting;

        // The look of every effect. Replace or edit it to restyle; never null.
        public static FireVfxStyle Style
        {
            get => style ?? (style = new FireVfxStyle());
            set => style = value;
        }

        // A punch of flame travelling along direction (a fire-extended strike). scale 1 = a light attack.
        public static void Burst(Vector3 position, Vector3 direction, float scale)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Burst(position, direction, scale);
        }

        // A fireball bursting out to radius, with embers and a flash of light.
        public static void Explosion(Vector3 position, float radius)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Explosion(position, radius);
        }

        // A shockwave ring spreading along the ground to radius (landing attacks). center = at the feet.
        public static void Ring(Vector3 center, float radius)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Ring(center, radius);
        }

        // A flame ribbon following a transform (a fist, a foot, the blade tip) for duration seconds of game
        // time, then fading. duration <= 0 keeps it going until handle.Stop() or the transform is destroyed.
        public static FireVfxHandle Trail(Transform follow, float duration)
        {
            FireVfxRunner r = GetRunner();
            return r != null ? r.Trail(follow, duration) : FireVfxHandle.None;
        }

        // A fire glow held at an anchor (a charging fist). Drive it with handle.SetLevel(0..1) and end it
        // with handle.Stop().
        public static FireVfxHandle ChargeGlow(Transform anchor)
        {
            FireVfxRunner r = GetRunner();
            return r != null ? r.ChargeGlow(anchor) : FireVfxHandle.None;
        }

        // A small bright pop where a hit connects. The colour is brightened for bloom (e.g. orange for a
        // clean hit, white for a block, blue for a deflect).
        public static void HitSpark(Vector3 position, Color color)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.HitSpark(position, color);
        }

        // A launch flash pointing along direction (firing a blast).
        public static void Muzzle(Vector3 position, Vector3 direction)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Muzzle(position, direction);
        }

        // A wide fan of fire from origin along direction, reaching range metres across arcDegrees (Phoenix Palm).
        public static void Cone(Vector3 origin, Vector3 direction, float range, float arcDegrees)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Cone(origin, direction, range, arcDegrees);
        }

        // A column of fire rising height metres from feet (a launched enemy).
        public static void Pillar(Vector3 feet, float height)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Pillar(feet, height);
        }

        // A ring of fire racing out along the ground to radius (Flame Wheel).
        public static void Wheel(Vector3 center, float radius)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Wheel(center, radius);
        }

        // A downward burst from position and a ring on the floor below it (tornado kick, axe kick).
        public static void Slam(Vector3 position, float radius)
        {
            FireVfxRunner r = GetRunner();
            if (r != null) r.Slam(position, radius);
        }

        // A lash of flame from the hand sweeping right to left across the arc at range (Fire Whip), for duration seconds.
        public static FireVfxHandle Whip(Transform hand, Vector3 origin, Vector3 direction, float range, float arcDegrees, float duration)
        {
            FireVfxRunner r = GetRunner();
            return r != null ? r.Whip(hand, origin, direction, range, arcDegrees, duration) : FireVfxHandle.None;
        }

        // Flames licking off a fist or foot for duration seconds (a chain strike's fire).
        public static FireVfxHandle LimbFlame(Transform limb, float duration)
        {
            FireVfxRunner r = GetRunner();
            return r != null ? r.Emit(limb, FireVfxEmitterKind.LimbFlame, Vector3.up, duration) : FireVfxHandle.None;
        }

        // A jet of fire from a foot along direction (world) for duration seconds: air dashes and the zip strike.
        public static FireVfxHandle FootJet(Transform foot, Vector3 direction, float duration)
        {
            FireVfxRunner r = GetRunner();
            return r != null ? r.Emit(foot, FireVfxEmitterKind.FootJet, direction, duration) : FireVfxHandle.None;
        }

        // Embers and a smoky flame trail following a point (a launched enemy). duration <= 0 = until stopped.
        public static FireVfxHandle EmberTrail(Transform follow, float duration)
        {
            FireVfxRunner r = GetRunner();
            if (r == null) return FireVfxHandle.None;
            r.Trail(follow, duration > 0f ? duration : EmberTrailFallbackSeconds);
            return r.Emit(follow, FireVfxEmitterKind.Embers, Vector3.up, duration);
        }

        // Ends every running effect at once (e.g. on a sandbox reset). Extra to the spec.
        public static void StopAll()
        {
            if (runner != null) runner.StopAll();
        }

        // Handle operations never create the pool: stopping a glow while play shuts down must not spawn objects.
        internal static void Stop(FireVfxHandle handle)
        {
            if (runner != null) runner.Stop(handle);
        }

        internal static void SetLevel(FireVfxHandle handle, float level)
        {
            if (runner != null) runner.SetLevel(handle, level);
        }

        internal static bool IsAlive(FireVfxHandle handle)
        {
            return runner != null && runner.IsAlive(handle);
        }

        // The shared pool for ElementVfx (every element draws through it): created on first use, null when it can't
        // render. ExistingRunner never creates it.
        internal static FireVfxRunner Runner => GetRunner();
        internal static FireVfxRunner ExistingRunner => runner;

        static FireVfxRunner GetRunner()
        {
            if (runner != null) return runner.CanRender ? runner : null;
            if (!Application.isPlaying || quitting) return null;
            runner = FireVfxRunner.Create();
            return runner.CanRender ? runner : null;
        }

        // Domain reload is off in this project, so statics survive between play sessions: reset them when
        // play starts, and stop creating pools once play is ending.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            runner = null;
            quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting()
        {
            quitting = true;
        }
    }
}
