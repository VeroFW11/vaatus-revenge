using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // One enemy type's numbers as an asset, so they can be tuned in the Inspector without touching code.
    // Tuning holds the rules (health, poise, attacks and their frame data, AI spacing) and is read live by the
    // enemy brains, so edits made while playing apply at once. Feedback holds the Unity-side feel (glows,
    // flashes, shake, poses, health bar). Several enemies may share one asset.
    // Unity keeps play-mode edits to assets after you stop playing: handy for tuning, just be deliberate.
    // Create one via Assets > Create > Vaatu's Revenge > Tuning > Enemy, then pick its archetype and numbers.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Enemy", fileName = "EnemyTuning")]
    public class EnemyTuningAsset : ScriptableObject
    {
        [Tooltip("Rules: display name, archetype (melee, ranged, dummy), health, poise, movement, attacks.")]
        public EnemyTuning Tuning = EnemyTuning.CreateDaoSoldier();

        [Tooltip("Feel: telegraph glows, hit flashes, screen shake, sword trail, poses, health bar.")]
        public EnemyFeedbackSettings Feedback = new EnemyFeedbackSettings();
    }
}
