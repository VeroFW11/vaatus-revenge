using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Screen shake for hits and explosions. Anyone can call CameraShake.Add; the ThirdPersonCameraRig plays it.
    //   CameraShake.Add(amplitude, duration);
    // Amplitude is 0..1: about 0.05 for a light hit up to 0.3 for a fa jin burst. Overlapping shakes add up,
    // capped at 1. Keep the actual amplitudes in tuning data (e.g. on the move), not written at the call site.
    // It runs on real time, so a shake still plays while hitstop has frozen the game.
    public static class CameraShake
    {
        static readonly CameraShakeModel model = new CameraShakeModel();

        // Current combined strength, 0..1.
        public static float Strength => model.Strength;

        public static void Add(float amplitude, float duration)
        {
            model.Add(amplitude, duration);
        }

        public static void Clear()
        {
            model.Clear();
        }

        // Advances the shake and returns this frame's rotation offset in degrees (x = pitch, y = yaw, z = roll).
        // Called once per frame by ThirdPersonCameraRig.
        internal static Vector3 Advance(float unscaledDeltaTime, float frequency, float maxAngle)
        {
            model.Tick(unscaledDeltaTime);
            return model.SampleAngles(frequency, maxAngle).ToUnity();
        }

        // Static data survives between play sessions when domain reload is turned off; start each session calm.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlaySession()
        {
            model.Clear();
        }
    }
}
