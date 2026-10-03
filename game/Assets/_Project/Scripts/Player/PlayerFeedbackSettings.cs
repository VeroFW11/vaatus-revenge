using System;
using UnityEngine;

namespace VaatusRevenge
{
    // The player's "game feel" numbers: screen shake, gamepad rumble, flashes, fire effects and grey-box
    // poses. None of this changes the fight (damage, timing and hitstop per move live in the move set);
    // it's what makes a hit feel like a hit. Lives in PlayerTuningAsset and is read live, so tweaks made in
    // the Inspector while playing show up on the next hit.
    [Serializable]
    public class PlayerFeedbackSettings
    {
        [Header("Hits you land (clean hits only; the move's Hitstop freezes the frame)")]
        [Tooltip("Light chain punches and kicks, and the ranged skill.")]
        public FeedbackPulse LightHit = new FeedbackPulse(0.05f, 0.1f, 0.1f, 0.3f, 0.08f);
        [Tooltip("Heavy, sprint and jump attacks, and the last move of the light chain.")]
        public FeedbackPulse HeavyHit = new FeedbackPulse(0.12f, 0.16f, 0.35f, 0.55f, 0.14f);
        [Tooltip("A heavy released in the sweet spot (fa jin).")]
        public FeedbackPulse FaJinHit = new FeedbackPulse(0.3f, 0.3f, 0.8f, 1f, 0.25f);
        [Tooltip("Releasing a fa jin, whether or not it connects: the burst of power itself.")]
        public FeedbackPulse FaJinRelease = new FeedbackPulse(0.06f, 0.12f, 0.2f, 0.4f, 0.1f);

        [Header("Hits you take")]
        [Tooltip("A clean hit on you.")]
        public FeedbackPulse Hurt = new FeedbackPulse(0.16f, 0.2f, 0.55f, 0.35f, 0.18f);
        [Tooltip("A hit absorbed by your guard.")]
        public FeedbackPulse GuardBlock = new FeedbackPulse(0.04f, 0.08f, 0.15f, 0.35f, 0.07f);
        [Tooltip("Your guard ran out of stamina and broke.")]
        public FeedbackPulse GuardBreak = new FeedbackPulse(0.2f, 0.25f, 0.75f, 0.5f, 0.25f);
        [Tooltip("You deflected a hit (guard pressed just before it landed).")]
        public FeedbackPulse Deflect = new FeedbackPulse(0.07f, 0.1f, 0.25f, 0.8f, 0.1f);
        [Tooltip("Freeze-frame on a deflect, in real seconds (0 = none). The clash of a good parry.")]
        public float DeflectHitstop = 0.06f;
        [Tooltip("An enemy deflected your attack.")]
        public FeedbackPulse GotParried = new FeedbackPulse(0.12f, 0.15f, 0.5f, 0.3f, 0.15f);

        [Header("Moves")]
        [Tooltip("A perfect dodge (the slow motion itself comes from the dodge's tuning).")]
        public FeedbackPulse PerfectDodge = new FeedbackPulse(0.04f, 0.12f, 0.2f, 0.6f, 0.12f);
        [Tooltip("The falling axe kick hitting the ground.")]
        public FeedbackPulse PlungeLanding = new FeedbackPulse(0.14f, 0.2f, 0.6f, 0.4f, 0.15f);
        [Tooltip("A short buzz when the heavy's charge reaches the fa jin sweet spot: a timing cue you can feel.")]
        public FeedbackPulse SweetSpot = new FeedbackPulse(0f, 0f, 0f, 0.45f, 0.06f);
        [Tooltip("A light tick at the heavy's \"get ready\" point, the move set's Charge.ReadyCueLead seconds before the "
                 + "sweet spot. Reacting to the sweet-spot cue itself is usually too late, so this one says: let go soon.")]
        public FeedbackPulse ReadyCue = new FeedbackPulse(0f, 0f, 0f, 0.2f, 0.035f);
        [Tooltip("Landing from a big fall (see HardLandingSpeed).")]
        public FeedbackPulse HardLanding = new FeedbackPulse(0.06f, 0.12f, 0.3f, 0.2f, 0.1f);
        [Tooltip("Landings at least this fast (metres per second, downwards) count as hard.")]
        public float HardLandingSpeed = 14f;
        [Tooltip("Spirit water taking effect.")]
        public FeedbackPulse HealApplied = new FeedbackPulse(0f, 0f, 0.1f, 0.2f, 0.12f);

        [Header("Flashes (the whole body glows briefly)")]
        public Color HurtFlashColor = new Color(1f, 0.12f, 0.05f);
        public float HurtFlashTime = 0.15f;
        public Color PerfectDodgeFlashColor = new Color(1f, 0.95f, 0.75f);
        public float PerfectDodgeFlashTime = 0.3f;
        public Color DeflectFlashColor = new Color(1f, 0.85f, 0.35f);
        public float DeflectFlashTime = 0.15f;
        public Color GuardBreakFlashColor = new Color(1f, 1f, 1f);
        public float GuardBreakFlashTime = 0.2f;
        [Tooltip("An attack started inside the counter window after a perfect dodge (it deals bonus damage).")]
        public Color CounterFlashColor = new Color(1f, 0.5f, 0.1f);
        public float CounterFlashTime = 0.2f;
        [Tooltip("The faint glow at the heavy's \"get ready\" point. Keep it much dimmer than the sweet-spot flash.")]
        public Color ReadyCueFlashColor = new Color(0.32f, 0.2f, 0.07f);
        public float ReadyCueFlashTime = 0.1f;
        public Color HealFlashColor = new Color(0.35f, 0.85f, 1f);
        public float HealFlashTime = 0.35f;
        [Tooltip("Pressed heal with no charges left.")]
        public Color EmptyFlaskFlashColor = new Color(0.5f, 0.5f, 0.55f);
        public float EmptyFlaskFlashTime = 0.15f;
        [Tooltip("A fourth dodge in a row refused (DodgeProfile.ChainMax): a short breath. A dull flash and a light thud; the HUD "
                 + "flashes the stamina bar.")]
        public Color DodgeChainLimitedFlashColor = new Color(0.35f, 0.35f, 0.4f);
        public float DodgeChainLimitedFlashTime = 0.12f;
        public FeedbackPulse DodgeChainLimitedPulse = new FeedbackPulse(0f, 0f, 0.25f, 0f, 0.08f);

        [Header("Counter window glow (fists glow while a counter is ready after a perfect dodge)")]
        public Color CounterGlowColor = new Color(1f, 0.55f, 0.15f);
        [Tooltip("0 = off. Around 1-3 reads well with bloom.")]
        public float CounterGlowIntensity = 1.5f;

        [Header("Sparks where hits connect")]
        public Color HitSparkColor = new Color(1f, 0.55f, 0.15f);
        public Color BlockSparkColor = new Color(0.9f, 0.92f, 1f);
        public Color DeflectSparkColor = new Color(1f, 0.85f, 0.4f);
        public Color HurtSparkColor = new Color(1f, 0.25f, 0.1f);

        [Header("Fire effects")]
        [Tooltip("Strike bursts scale with the move's range: range x this (a 2.6 m jab gives about 1).")]
        public float BurstScalePerMetre = 0.38f;
        [Tooltip("A fa jin strike's burst is this many times bigger.")]
        public float FaJinBurstMultiplier = 1.8f;
        [Tooltip("Wide spinning strikes also throw a ring of fire: its radius is the move's range x this.")]
        public float WideArcRingShare = 0.8f;
        [Tooltip("A flame ribbon follows the striking fist or foot during active frames.")]
        public bool StrikeTrails = true;
        [Tooltip("Flame jets from the feet when dashing (Flame Step). 0 = none.")]
        public float DodgeBurstScale = 0.5f;
        [Tooltip("Flame ribbons follow the feet during a dash.")]
        public bool DodgeTrails = true;
        [Tooltip("A puff of flame under the feet when jumping. 0 = none.")]
        public float JumpBurstScale = 0.3f;
        public float PerfectDodgeBurstScale = 1.2f;
        public float DeflectBurstScale = 0.6f;
        [Tooltip("The axe kick's landing also bursts into flame: radius = the landing ring's radius x this. 0 = ring only.")]
        public float PlungeExplosionShare = 0.35f;
        [Tooltip("Foot effects (dash jets, jump puff, landing burst) start this far above the feet, in metres.")]
        public float EffectFootHeight = 0.2f;

        [Header("Elements (each move's effect is drawn in its element: Water, Earth and Air since Build 05)")]
        [Tooltip("Each later hit of a flurry (Air's many-hit palms) bursts from the limb at this share of a normal burst; the "
                 + "first hit draws the move's whole effect.")]
        [Range(0f, 1f)] public float SubHitBurstShare = 0.45f;
        [Tooltip("The dodge strike's burst is this many times bigger: the counter out of a dodge should look like one.")]
        public float DodgeStrikeBurstMultiplier = 1.2f;
        [Tooltip("Earth's stomps and quakes shake the ground under you.")]
        public FeedbackPulse StompShake = new FeedbackPulse(0.12f, 0.18f, 0.45f, 0.2f, 0.12f);
        [Tooltip("A slip-in dodge leaves see-through afterimages of the body behind it.")]
        public bool SlipInAfterimages = true;

        [Header("Element switch (hold RB, then a face button)")]
        [Tooltip("Seconds the new element clings to both fists after a switch.")]
        public float SwitchAuraTime = 0.4f;
        [Tooltip("The body flashes the new element's HUD colour for this long (0 = no flash).")]
        public float SwitchFlashTime = 0.12f;
        [Tooltip("A short buzz on a switch.")]
        public FeedbackPulse SwitchPulse = new FeedbackPulse(0f, 0f, 0f, 0.25f, 0.05f);
        [Tooltip("The first hit after a mid-combo switch (a switch strike that lands) bursts this many times bigger, in "
                 + "the new element's colour: the MIX accent.")]
        public float MixAccentScale = 1.4f;

        [Header("Rhythm feedback (hits on the beat; sounds are made in code, no audio files)")]
        [Tooltip("Play the rhythm sounds: a chime for an on-beat press, a tick for every combo hit, a rising three-note "
                 + "finisher for a perfect string or a MIX finisher, a whoosh for a switch.")]
        public bool RhythmSounds = true;
        [Range(0f, 1f)] public float ChimeVolume = 0.45f;
        [Range(0f, 1f)] public float HitTickVolume = 0.25f;
        [Range(0f, 1f)] public float FinisherVolume = 0.5f;
        [Range(0f, 1f)] public float SwitchVolume = 0.35f;
        [Tooltip("The thin ring that flashes round an on-beat hit (HDR: it blooms).")]
        public Color BeatAccentColor = new Color(2.4f, 2.1f, 1.3f, 1f);
        [Tooltip("Seconds the on-beat ring lasts.")]
        public float BeatAccentTime = 0.12f;
        public float BeatAccentScale = 1f;
        [Tooltip("The body flashes on an on-beat hit (0 time = no flash).")]
        public Color OnBeatFlashColor = new Color(1f, 0.9f, 0.55f);
        public float OnBeatFlashTime = 0.06f;
        [Tooltip("A light tick you can feel for an on-beat press.")]
        public FeedbackPulse OnBeatTick = new FeedbackPulse(0f, 0f, 0f, 0.3f, 0.03f);
        [Tooltip("Every follow-up of the string was on the beat: the finisher flashes this colour.")]
        public Color PerfectStringFlashColor = new Color(1f, 0.85f, 0.4f);
        public float PerfectStringFlashTime = 0.2f;
        [Tooltip("A finisher with MIX 2 or more (several elements landed in the combo).")]
        public FeedbackPulse MixFinisherPulse = new FeedbackPulse(0.1f, 0.16f, 0.4f, 0.6f, 0.12f);

        [Header("Danger sense (the mark over your head; drawn by the HUD)")]
        [Tooltip("A tick when the mark appears.")]
        public FeedbackPulse DangerTick = new FeedbackPulse(0f, 0f, 0.15f, 0.25f, 0.04f);
        [Tooltip("A sharper tick when it turns white: press now.")]
        public FeedbackPulse DangerNowTick = new FeedbackPulse(0f, 0f, 0f, 0.4f, 0.03f);

        [Header("Camera")]
        [Tooltip("Widen the view while sprinting to sell speed (by the camera tuning's SprintFovBoost degrees).")]
        public bool SprintFovBoost = true;

        [Header("HUD")]
        [Tooltip("Real seconds the 'not learned yet' message stays up after picking a locked element.")]
        public float ElementMessageDuration = 2f;
        [Tooltip("Message for a locked element; {0} is replaced by the element's name.")]
        public string ElementLockedMessage = "{0} isn't learned yet";

        [Header("Fire on the martial arts (flames on the limbs, the launcher column, the whip, foot jets)")]
        public PlayerStrikePoses Poses = new PlayerStrikePoses();
    }
}
