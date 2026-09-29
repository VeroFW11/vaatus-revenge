using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // An editable copy of the procedural animation library (every martial-arts clip's keyframes, the blending and
    // secondary-motion settings, the walk/run cycle). Fighters use the built-in library unless one of these is
    // assigned to their BodyAnimatorDriver, so tweaking poses in the Inspector never needs code.
    // Create > Vaatu's Revenge > Animation > Pose Library starts from the current built-in poses.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Animation/Pose Library")]
    public class PoseLibraryAsset : ScriptableObject
    {
        public Core.PoseLibrary Library = new Core.PoseLibrary();

        // Inspector edits: make the library re-index its clips.
        void OnValidate()
        {
            if (Library != null) Library.Invalidate();
        }
    }
}
