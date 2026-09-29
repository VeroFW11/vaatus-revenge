using UnityEngine;
using UnityEngine.InputSystem;

namespace VaatusRevenge
{
    // Gamepad rumble with a real-time timer. A rumble motor keeps spinning until it's told to stop, even
    // through a pause, a death screen or quitting, so every start here has a guaranteed stop: when its timer
    // runs out, and whenever PlayerController is disabled, dies, loses focus or quits (they call Stop).
    // The timer uses real time so hitstop and slow motion don't stretch a buzz into a long drone.
    public sealed class PlayerRumble
    {
        Gamepad pad;       // the pad whose motors we started (Gamepad.current may change while it buzzes)
        float remaining;
        float low;
        float high;

        public bool IsPlaying => pad != null;

        public void Play(FeedbackPulse pulse)
        {
            if (pulse != null) Play(pulse.RumbleLow, pulse.RumbleHigh, pulse.RumbleDuration);
        }

        // Starts a buzz on the current gamepad. A weaker buzz doesn't cut a stronger one short.
        public void Play(float lowFrequency, float highFrequency, float duration)
        {
            lowFrequency = Mathf.Clamp01(lowFrequency);
            highFrequency = Mathf.Clamp01(highFrequency);
            if (!(duration > 0f) || (lowFrequency <= 0f && highFrequency <= 0f)) return;
            Gamepad current = Gamepad.current;
            if (current == null) return;
            if (pad == current && Mathf.Max(lowFrequency, highFrequency) < Mathf.Max(low, high)) return;
            if (pad != null && pad != current) Stop(); // switched pads mid-buzz: never leave the old one running

            pad = current;
            low = lowFrequency;
            high = highFrequency;
            remaining = duration;
            pad.SetMotorSpeeds(low, high);
        }

        // Call every frame with Time.unscaledDeltaTime.
        public void Tick(float unscaledDeltaTime)
        {
            if (pad == null) return;
            remaining -= Mathf.Max(0f, unscaledDeltaTime);
            if (remaining <= 0f) Stop();
        }

        // Stops the motors now. Safe to call any time, as often as you like.
        public void Stop()
        {
            Gamepad target = pad;
            pad = null;
            remaining = 0f;
            low = 0f;
            high = 0f;
            // A pad that was unplugged meanwhile has nothing left to stop.
            if (target != null && target.added) target.ResetHaptics();
        }
    }
}
