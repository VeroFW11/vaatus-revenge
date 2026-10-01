using System.Text;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Hands the element effects their library when play starts (one in the scene, on Systems). Without it, or with an
    // empty library, every effect still draws with its primitive stand-in and the HUD uses the default colours.
    //
    // When play starts it also reports, once, each picture slot that has no picture yet: one log line per slot, so you
    // can see what is waiting for art (Art/VFX/README.md lists the slots and the generated images that fill them).
    // Nothing is logged per frame.
    [DisallowMultipleComponent]
    public class ElementVfxBootstrap : MonoBehaviour
    {
        [Tooltip("Colours and textures per element. Empty = every effect uses its primitive fallback.")]
        public ElementVfxLibraryAsset Library;

        [Tooltip("Log one line per picture slot that has no picture yet, once when play starts.")]
        [SerializeField] private bool reportMissingPictures = true;

        bool reported;

        void OnEnable()
        {
            ElementVfx.SetLibrary(Library);
            if (reportMissingPictures && !reported)
            {
                reported = true;
                ReportMissing();
            }
        }

        void OnDisable()
        {
            if (ElementVfx.Library == Library) ElementVfx.SetLibrary(null);
        }

        // One line per empty slot, in the folder order of the README (Common last).
        void ReportMissing()
        {
            var line = new StringBuilder(160);
            for (int i = 0; i < 5; i++)
            {
                ElementId folder = i < 4 ? (ElementId)(i + 1) : ElementId.None;
                int slots = ElementVfx.SlotCount(folder);
                for (int s = 0; s < slots; s++)
                {
                    VfxSlot slot = ElementVfx.SlotAt(folder, s);
                    if (ElementVfx.TryGetTexture(folder, slot, out _, out _)) continue;
                    line.Length = 0;
                    line.Append("VFX: no picture for Art/VFX/").Append(ElementVfx.FolderName(folder)).Append('/')
                        .Append(ElementVfx.SlotName(slot)).Append(".png");
                    line.Append(folder == ElementId.Fire ? " (optional: Fire has its own look)" : "; using the primitive stand-in");
                    if (Library == null) line.Append(" (no Element VFX Library assigned)");
                    Debug.Log(line.ToString(), this);
                }
            }
        }
    }
}
