using System;

namespace VaatusRevenge.Core
{
    // The four elements the player can switch between (hold RB + a face button), each a full move set, and which of
    // them have been learned. The active element's move set is PlayerCombatModel.MoveSet. Lives in an
    // ElementLoadoutAsset in Unity (four MoveSetAssets); the model keeps references and never writes to them.
    [Serializable]
    public class ElementLoadout
    {
        public ElementMoveSet Fire, Water, Earth, Air;
        public ElementId Starting = ElementId.Fire;
        public bool LearnedFire = true, LearnedWater = true, LearnedEarth = true, LearnedAir = true;

        // The move set for an element (null for None or an empty slot).
        public ElementMoveSet Get(ElementId element)
        {
            switch (element)
            {
                case ElementId.Fire: return Fire;
                case ElementId.Water: return Water;
                case ElementId.Earth: return Earth;
                case ElementId.Air: return Air;
                default: return null;
            }
        }

        // Learned and present (an empty slot is never learned).
        public bool IsLearned(ElementId element)
        {
            if (Get(element) == null) return false;
            switch (element)
            {
                case ElementId.Fire: return LearnedFire;
                case ElementId.Water: return LearnedWater;
                case ElementId.Earth: return LearnedEarth;
                case ElementId.Air: return LearnedAir;
                default: return false;
            }
        }

        // The element to start with: Starting if learned, else the first learned one in Fire, Water, Earth, Air order.
        public ElementId FirstUsable()
        {
            if (IsLearned(Starting)) return Starting;
            for (ElementId e = ElementId.Fire; e <= ElementId.Air; e++)
            {
                if (IsLearned(e)) return e;
            }
            return ElementId.None;
        }

        public static ElementLoadout CreateFluid()
        {
            return new ElementLoadout
            {
                Fire = ElementMoveSet.CreateFireFluid(), Water = ElementMoveSet.CreateWaterFluid(),
                Earth = ElementMoveSet.CreateEarthFluid(), Air = ElementMoveSet.CreateAirFluid()
            };
        }

        public static ElementLoadout CreatePunishing()
        {
            return new ElementLoadout
            {
                Fire = ElementMoveSet.CreateFirePunishing(), Water = ElementMoveSet.CreateWaterPunishing(),
                Earth = ElementMoveSet.CreateEarthPunishing(), Air = ElementMoveSet.CreateAirPunishing()
            };
        }

        // One element only (the old single-move-set setup): the set goes in its element's slot, the rest stay empty
        // (not learned). A set with no element counts as Fire.
        public static ElementLoadout FromSingle(ElementMoveSet set)
        {
            var loadout = new ElementLoadout();
            ElementId element = set != null && set.Element != ElementId.None ? set.Element : ElementId.Fire;
            switch (element)
            {
                case ElementId.Water: loadout.Water = set; break;
                case ElementId.Earth: loadout.Earth = set; break;
                case ElementId.Air: loadout.Air = set; break;
                default: loadout.Fire = set; break;
            }
            loadout.Starting = element;
            return loadout;
        }
    }
}
