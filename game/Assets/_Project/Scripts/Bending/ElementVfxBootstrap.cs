using UnityEngine;

namespace VaatusRevenge
{
    // Hands the element effects their library when play starts (one in the scene, on Systems).
    // SHELL (Build 05 package A): package C fills in the behaviour.
    [DisallowMultipleComponent]
    public class ElementVfxBootstrap : MonoBehaviour
    {
        [Tooltip("Colours and textures per element. Empty = every effect uses its primitive fallback.")]
        public ElementVfxLibraryAsset Library;
    }
}
