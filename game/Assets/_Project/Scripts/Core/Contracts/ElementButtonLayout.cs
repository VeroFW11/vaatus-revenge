using System;

namespace VaatusRevenge.Core
{
    // Which element each button picks (PlayerInputFrame.ElementSelect). On the pad you hold RB and press a face button;
    // the layout is colour-matched to the Xbox buttons so the button tells you the element (Build 05, decided 1 Oct):
    // B (red) Fire, X (blue) Water, A (green) Earth, Y (yellow) Air. The keyboard keeps 1-4 = Fire, Water, Earth, Air.
    // Data, not code: the input reader reads it, and the HUD and tutorial show the same layout.
    [Serializable]
    public class ElementButtonLayout
    {
        // Face-button slots, in the reader's order: Up (Y / Triangle), Right (B / Circle), Down (A / Cross), Left (X / Square).
        public const int SlotCount = 4;

        public ElementId North = ElementId.Air;      // Y (yellow)
        public ElementId East = ElementId.Fire;      // B (red)
        public ElementId South = ElementId.Earth;    // A (green)
        public ElementId West = ElementId.Water;     // X (blue)

        // Keyboard number keys 1-4.
        public ElementId Key1 = ElementId.Fire;
        public ElementId Key2 = ElementId.Water;
        public ElementId Key3 = ElementId.Earth;
        public ElementId Key4 = ElementId.Air;

        // slot: 0 Up (Y), 1 Right (B), 2 Down (A), 3 Left (X). None outside 0-3.
        public ElementId PadSlot(int slot)
        {
            switch (slot)
            {
                case 0: return North;
                case 1: return East;
                case 2: return South;
                case 3: return West;
                default: return ElementId.None;
            }
        }

        // index: 0 = key 1 ... 3 = key 4. None outside 0-3.
        public ElementId KeySlot(int index)
        {
            switch (index)
            {
                case 0: return Key1;
                case 1: return Key2;
                case 2: return Key3;
                case 3: return Key4;
                default: return ElementId.None;
            }
        }

        // The face-button slot that picks an element (-1 if none does).
        public int PadSlotOf(ElementId element)
        {
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (PadSlot(slot) == element) return slot;
            }
            return -1;
        }
    }
}
