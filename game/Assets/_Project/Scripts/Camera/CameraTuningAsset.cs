using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The camera and lock-on numbers as an asset, so they can be tuned in the Inspector without touching code.
    // Edits made while playing apply immediately (the camera reads the values every frame), but Unity keeps
    // play-mode edits to assets, so they stick after you stop: handy for tuning, just be deliberate.
    // Create one via Assets > Create > Vaatu's Revenge > Tuning > Camera.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Camera", fileName = "CameraTuning")]
    public class CameraTuningAsset : ScriptableObject
    {
        [Tooltip("Orbit distances, look speeds, lock-on framing, wall collision, field of view and shake.")]
        public CameraTuning Camera = new CameraTuning();

        [Tooltip("Which enemy lock-on picks, how switching works and when the lock breaks.")]
        public LockOnTuning LockOn = new LockOnTuning();
    }
}
