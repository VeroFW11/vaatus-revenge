using System;
using UnityEngine;

namespace VaatusRevenge
{
    // One moment of "feel": a screen shake plus a gamepad rumble, played together when something happens
    // (a hit lands, you get hit, a deflect...). Purely sensory: nothing here changes the fight.
    //
    // Why two rumble motors: gamepads have a heavy low-frequency motor (a deep thud) and a light
    // high-frequency one (a sharp buzz). Mixing them tells impacts apart by touch: heavy hits thud, deflects buzz.
    [Serializable]
    public class FeedbackPulse
    {
        [Tooltip("Screen shake strength, 0..1. About 0.05 for a light hit up to 0.3 for a fa jin burst. 0 = no shake.")]
        public float ShakeAmplitude;
        [Tooltip("Seconds the shake lasts (real time, so it still plays during hitstop).")]
        public float ShakeDuration;
        [Tooltip("Gamepad low-frequency motor (deep thud), 0..1.")]
        public float RumbleLow;
        [Tooltip("Gamepad high-frequency motor (sharp buzz), 0..1.")]
        public float RumbleHigh;
        [Tooltip("Seconds of rumble (real time). 0 = no rumble.")]
        public float RumbleDuration;

        public FeedbackPulse()
        {
        }

        public FeedbackPulse(float shakeAmplitude, float shakeDuration, float rumbleLow, float rumbleHigh, float rumbleDuration)
        {
            ShakeAmplitude = shakeAmplitude;
            ShakeDuration = shakeDuration;
            RumbleLow = rumbleLow;
            RumbleHigh = rumbleHigh;
            RumbleDuration = rumbleDuration;
        }
    }
}
