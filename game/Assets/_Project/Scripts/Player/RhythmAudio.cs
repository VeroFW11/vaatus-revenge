using UnityEngine;

namespace VaatusRevenge
{
    // The rhythm's sounds, made in code when play starts (no audio files to import or lose): a bright chime when a
    // press lands on the beat, a short tick on every hit of a combo, a rising three-note finisher for a perfect string or
    // a MIX finisher, and a soft whoosh for an element switch. Hearing the beat is half of keeping it: the tick on each
    // hit is the metronome, the chime tells you that you were on it.
    //
    // PlayerFeedback creates one on the player when the first sound is needed (never in edit mode) and destroys its
    // clips with the player. Playing a sound allocates nothing: PlayOneShot on clips made once.
    public sealed class RhythmAudio
    {
        const float ChimeHz = 880f;
        const float ChimeSeconds = 0.06f;
        const float TickHz = 440f;
        const float TickSeconds = 0.03f;
        const float FinisherSeconds = 0.18f;    // three notes of a third each
        const float SwitchSeconds = 0.15f;
        const int FallbackSampleRate = 48000;

        readonly AudioSource source;
        AudioClip chime;
        AudioClip tick;
        AudioClip finisher;
        AudioClip whoosh;

        public RhythmAudio(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;   // a UI sound: the same in both ears wherever the camera is
            source.priority = 32;
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : FallbackSampleRate;
            chime = Tone("RhythmChime", rate, ChimeSeconds, ChimeHz, 0f, 0f);
            tick = Tone("RhythmTick", rate, TickSeconds, TickHz, 0f, 0f);
            finisher = Tone("RhythmFinisher", rate, FinisherSeconds, 660f, 880f, 1320f);
            whoosh = Whoosh("RhythmSwitch", rate, SwitchSeconds);
        }

        public void PlayChime(float volume) { Play(chime, volume); }
        public void PlayTick(float volume) { Play(tick, volume); }
        public void PlayFinisher(float volume) { Play(finisher, volume); }
        public void PlaySwitch(float volume) { Play(whoosh, volume); }

        public void Stop()
        {
            if (source != null) source.Stop();
        }

        // The clips are not assets: free them with the player.
        public void Dispose()
        {
            GreyboxShapes.SafeDestroy(chime);
            GreyboxShapes.SafeDestroy(tick);
            GreyboxShapes.SafeDestroy(finisher);
            GreyboxShapes.SafeDestroy(whoosh);
            chime = tick = finisher = whoosh = null;
            if (source != null) GreyboxShapes.SafeDestroy(source);
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip == null || source == null || !(volume > 0f)) return;
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        // A sine tone with a fast attack and an exponential fade (a bell-like ping). With secondHz and thirdHz above zero
        // the clip is split into three equal notes (the finisher's rising arpeggio).
        static AudioClip Tone(string name, int rate, float seconds, float firstHz, float secondHz, float thirdHz)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[samples];
            bool arpeggio = secondHz > 0f && thirdHz > 0f;
            int noteLength = arpeggio ? Mathf.Max(1, samples / 3) : samples;
            const float attack = 0.004f;
            double phase = 0.0;
            for (int i = 0; i < samples; i++)
            {
                int note = arpeggio ? Mathf.Min(2, i / noteLength) : 0;
                float hz = note == 0 ? firstHz : note == 1 ? secondHz : thirdHz;
                float t = (i - note * noteLength) / (float)rate;
                float noteSeconds = noteLength / (float)rate;
                float envelope = Mathf.Min(1f, t / attack) * Mathf.Exp(-5f * t / noteSeconds);
                phase += 2.0 * System.Math.PI * hz / rate;
                // A touch of the octave makes it ring rather than beep.
                data[i] = envelope * (0.8f * (float)System.Math.Sin(phase) + 0.2f * (float)System.Math.Sin(phase * 2.0));
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Filtered noise swelling and fading, its brightness sweeping down: air rushing past.
        static AudioClip Whoosh(string name, int rate, float seconds)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[samples];
            var random = new System.Random(5);     // the same whoosh every time
            float low = 0f;
            for (int i = 0; i < samples; i++)
            {
                float u = i / (float)samples;
                float envelope = Mathf.Sin(u * Mathf.PI);              // swell and fade
                float smoothing = Mathf.Lerp(0.35f, 0.06f, u);         // a one-pole low-pass that closes as it goes
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * smoothing;
                data[i] = envelope * low * 0.9f;
            }
            AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
