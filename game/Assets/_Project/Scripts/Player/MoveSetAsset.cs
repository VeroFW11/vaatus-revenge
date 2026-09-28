using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // One element's moves as an asset you edit in the Inspector: the light chain, the charged heavy (and its
    // fa jin sweet spot), sprint and jump attacks, the ranged skill, the dodge, guard/deflect and Momentum.
    // Move names, frame data, damage, reach and costs all live here. The combat model reads it live, so
    // edits made while playing apply to the next move. Create one via
    // Assets > Create > Vaatu's Revenge > Tuning > Move Set.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Move Set", fileName = "MoveSet")]
    public class MoveSetAsset : ScriptableObject
    {
        [Tooltip("Which feel this move set belongs to (Fluid or Punishing), shown in the debug panel.")]
        public string PresetName = "Fluid";

        [Tooltip("Every move of this element and the numbers behind it.")]
        public ElementMoveSet MoveSet = ElementMoveSet.CreateFireFluid();

        // Code-side helpers (the sandbox builder, tests). Assets made in the editor start as Fire, Fluid.
        public static MoveSetAsset CreateFireFluid()
        {
            var asset = CreateInstance<MoveSetAsset>();
            asset.name = "MoveSet_Fire_Fluid";
            return asset;
        }

        public static MoveSetAsset CreateFirePunishing()
        {
            var asset = CreateInstance<MoveSetAsset>();
            asset.name = "MoveSet_Fire_Punishing";
            asset.PresetName = "Punishing";
            asset.MoveSet = ElementMoveSet.CreateFirePunishing();
            return asset;
        }
    }
}
