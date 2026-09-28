using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The player's numbers as an asset you edit in the Inspector:
    //   Tuning   - the rules: health, stamina, movement, jumping, input buffer, poise, healing, soft lock
    //   Feedback - the feel: screen shake, rumble, flashes, fire effects, grey-box poses
    //   Body     - the size, colour and name the player is built with
    // PlayerController hands Tuning to the combat model by reference, so edits made while playing apply at
    // once. Unity keeps play-mode edits to assets after you stop, which is handy for tuning; just be deliberate.
    // Fluid and Punishing are two of these (F5 / F6 swap them live). Create one via
    // Assets > Create > Vaatu's Revenge > Tuning > Player.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Player", fileName = "PlayerTuning")]
    public class PlayerTuningAsset : ScriptableObject
    {
        [Tooltip("Shown on the HUD (F5 = Fluid, F6 = Punishing).")]
        public string PresetName = "Fluid";

        [Tooltip("Health, stamina, movement, jumping, input feel, poise, healing and soft-lock numbers.")]
        public PlayerTuning Tuning = PlayerTuning.CreateFluid();

        [Tooltip("Screen shake, gamepad rumble, flashes, fire effects and grey-box poses. Feel only: no gameplay numbers.")]
        public PlayerFeedbackSettings Feedback = new PlayerFeedbackSettings();

        [Tooltip("Size, colour and name the player is built with. Only read when the player is spawned (the sandbox "
                 + "builder), so rebuild the sandbox after changing it.")]
        public PlayerBodySettings Body = new PlayerBodySettings();

        // Code-side helpers (the sandbox builder, tests). Assets made in the editor start as Fluid.
        public static PlayerTuningAsset CreateFluid()
        {
            var asset = CreateInstance<PlayerTuningAsset>();
            asset.name = "PlayerTuning_Fluid";
            return asset;
        }

        public static PlayerTuningAsset CreatePunishing()
        {
            var asset = CreateInstance<PlayerTuningAsset>();
            asset.name = "PlayerTuning_Punishing";
            asset.Tuning = PlayerTuning.CreatePunishing();
            asset.PresetName = asset.Tuning.PresetName;
            return asset;
        }
    }
}
