using System;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // How an enemy LOOKS and FEELS when it attacks and gets hit: glow colours, flashes, screen shake, the sword
    // trail, poses and the health bar. Unity-side feel only: damage, timing and AI numbers live in EnemyTuning.
    // Lives inside each EnemyTuningAsset, so every enemy type can be tuned separately.
    [Serializable]
    public class EnemyFeedbackSettings
    {
        [Header("Telegraphs (the wind-up glow players learn to read)")]
        [Tooltip("Normal attacks: yellow.")]
        public Color NormalTelegraphColor = new Color(1f, 0.78f, 0.12f);
        [Tooltip("Heavy attacks (usually hyper-armoured: dodge or deflect them): red.")]
        public Color HeavyTelegraphColor = new Color(1f, 0.12f, 0.06f);
        [Tooltip("Delayed attacks (a held wind-up that punishes panic dodging): red.")]
        public Color DelayedTelegraphColor = new Color(1f, 0.12f, 0.06f);
        [Tooltip("Ranged attacks while taking aim: yellow.")]
        public Color AimedTelegraphColor = new Color(1f, 0.78f, 0.12f);
        [Tooltip("Glow brightness at the end of the wind-up. Around 1-3 reads well with bloom.")]
        public float NormalTelegraphIntensity = 2f;
        public float HeavyTelegraphIntensity = 3f;
        public float DelayedTelegraphIntensity = 3f;
        public float AimedTelegraphIntensity = 2f;
        [Tooltip("The glow starts at this share of full brightness and builds up as the strike gets closer, so its peak tells you when to dodge.")]
        [Range(0f, 1f)] public float TelegraphStartShare = 0.35f;
        [Tooltip("For this many seconds before the strike lands the glow flares brighter: the 'now!' cue for dodges and deflects. 0 = no flare.")]
        public float TelegraphFlareTime = 0.1f;
        [Tooltip("Brightness multiplier during the flare.")]
        public float TelegraphFlareBoost = 1.6f;

        [Header("Taking hits")]
        public Color HitFlashColor = new Color(1f, 0.85f, 0.7f);
        public float HitFlashTime = 0.12f;
        [Tooltip("A hit knocks the upper body away from the blow by this many degrees (not during its own attacks).")]
        public float FlinchDegrees = 7f;
        public float FlinchTime = 0.2f;
        [Tooltip("Poise broken or deflected: a bright flash on top of the rig's stagger wobble, so the punish window is obvious.")]
        public Color StaggerFlashColor = new Color(0.8f, 0.9f, 1f);
        public float StaggerFlashTime = 0.25f;
        public Color DeathFlashColor = new Color(1f, 0.3f, 0.15f);
        public float DeathFlashTime = 0.4f;
        [Tooltip("A faint flash when the enemy notices the player. 0 time = off.")]
        public Color AggroFlashColor = new Color(0.35f, 0.35f, 0.35f);
        public float AggroFlashTime = 0.15f;
        [Tooltip("Training dummy: flash when its health refills (its combo stats reset then too).")]
        public Color RefillFlashColor = new Color(0.3f, 0.9f, 0.4f);
        public float RefillFlashTime = 0.3f;

        [Header("Landing hits on the player (screen shake, 0..1)")]
        [Tooltip("Extra shake for this enemy's weapon, ON TOP of the player's own hurt shake (PlayerFeedbackSettings), so heavier weapons feel heavier. The player also makes its own hurt/block sparks and flashes.")]
        public float HitShake = 0.04f;
        public float HitShakeTime = 0.12f;
        [Tooltip("Extra shake for Heavy hits (the overhead, the thrust).")]
        public float HeavyHitShake = 0.12f;
        public float HeavyHitShakeTime = 0.22f;
        [Tooltip("Extra shake when the player blocks (or has its guard broken by) a swing or a bolt.")]
        public float BlockShake = 0.03f;
        public float BlockShakeTime = 0.1f;
        public float BoltHitShake = 0.03f;
        public float BoltHitShakeTime = 0.1f;

        [Header("Weapon swish (a pale trail on the blade as it swings in)")]
        public bool Swish = true;
        [Tooltip("Values above 1 bloom. Alpha = how strongly the trail starts before it fades.")]
        public Color SwishColor = new Color(1.3f, 1.3f, 1.4f, 1f);
        public float SwishWidth = 0.1f;
        [Tooltip("Seconds the trail lingers.")]
        public float SwishTime = 0.14f;

        [Header("Crossbow shot")]
        [Tooltip("Small pale spark at the crossbow when a bolt is loosed (not fire: these enemies aren't benders).")]
        public Color ShotSparkColor = new Color(0.9f, 0.85f, 0.7f);

        [Header("Health bar")]
        [Tooltip("Seconds the health bar stays up after a hit (it also shows while this enemy is the lock-on target).")]
        public float HealthBarShowTime = 4f;

        [Header("Look")]
        [Tooltip("Off = the standard colour for the enemy's archetype (soldiers steel blue-grey, ranged olive green, dummies straw tan).")]
        public bool UseCustomColor = false;
        public Color CustomColor = new Color(0.5f, 0.5f, 0.5f);

        [Header("Poses")]
        public EnemyPoseSettings Poses = new EnemyPoseSettings();

        public Color TelegraphColor(TelegraphKind kind)
        {
            switch (kind)
            {
                case TelegraphKind.Heavy: return HeavyTelegraphColor;
                case TelegraphKind.Delayed: return DelayedTelegraphColor;
                case TelegraphKind.Aimed: return AimedTelegraphColor;
                default: return NormalTelegraphColor;
            }
        }

        public float TelegraphIntensity(TelegraphKind kind)
        {
            switch (kind)
            {
                case TelegraphKind.Heavy: return HeavyTelegraphIntensity;
                case TelegraphKind.Delayed: return DelayedTelegraphIntensity;
                case TelegraphKind.Aimed: return AimedTelegraphIntensity;
                default: return NormalTelegraphIntensity;
            }
        }
    }
}
