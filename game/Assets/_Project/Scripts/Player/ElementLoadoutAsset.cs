using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The four elements the player switches between (hold RB, then press a face button), one MoveSetAsset each, and which of
    // them have been learned. PlayerController hands the model an ElementLoadout built from it (ToLoadout), which
    // points at the move set assets' data by reference, so Inspector edits to a move set apply live. Fluid and
    // Punishing are two of these (F5 / F6). Create one via Assets > Create > Vaatu's Revenge > Tuning > Element Loadout.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Element Loadout", fileName = "Loadout")]
    public class ElementLoadoutAsset : ScriptableObject
    {
        [Tooltip("Which feel these move sets belong to (Fluid or Punishing), shown in the debug panel.")]
        public string PresetName = "Fluid";

        [Tooltip("Each element's moves. An empty slot can't be switched to.")]
        public MoveSetAsset Fire, Water, Earth, Air;

        [Tooltip("Untick to lock an element (the sandbox can try a partly learned Avatar). A locked element shows a message when picked.")]
        public bool LearnedFire = true, LearnedWater = true, LearnedEarth = true, LearnedAir = true;

        [Tooltip("The element the player starts in (if learned).")]
        public ElementId Starting = ElementId.Fire;

        // A fresh loadout pointing at the move sets' data (by reference: the model reads them live).
        public ElementLoadout ToLoadout()
        {
            return new ElementLoadout
            {
                Fire = SetOf(Fire), Water = SetOf(Water), Earth = SetOf(Earth), Air = SetOf(Air),
                Starting = Starting,
                LearnedFire = LearnedFire, LearnedWater = LearnedWater, LearnedEarth = LearnedEarth, LearnedAir = LearnedAir
            };
        }

        // Does this loadout still hold exactly what the asset holds? (PlayerController rebuilds it only when not.)
        public bool Matches(ElementLoadout loadout)
        {
            return loadout != null
                   && ReferenceEquals(loadout.Fire, SetOf(Fire)) && ReferenceEquals(loadout.Water, SetOf(Water))
                   && ReferenceEquals(loadout.Earth, SetOf(Earth)) && ReferenceEquals(loadout.Air, SetOf(Air))
                   && loadout.Starting == Starting
                   && loadout.LearnedFire == LearnedFire && loadout.LearnedWater == LearnedWater
                   && loadout.LearnedEarth == LearnedEarth && loadout.LearnedAir == LearnedAir;
        }

        static ElementMoveSet SetOf(MoveSetAsset asset)
        {
            return asset != null ? asset.MoveSet : null;
        }
    }
}
