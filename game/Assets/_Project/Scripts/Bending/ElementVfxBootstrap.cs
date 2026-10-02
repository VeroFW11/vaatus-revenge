using System.Text;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Hands the element effects their library when play starts (one in the scene, on Systems). Without it, or with an
    // empty library, every effect still draws with its primitive stand-in and the HUD uses the default colours.
    //
    // When play starts it also reports, once, the picture slots that have no picture yet: one log message listing every
    // missing slot (one line each inside it), so you can see what is waiting for art (Art/VFX/README.md lists the slots and the generated images that fill them).
    // Nothing is logged per frame.
    [DisallowMultipleComponent]
    public class ElementVfxBootstrap : MonoBehaviour
    {
        [Tooltip("Colours and textures per element. Empty = every effect uses its primitive fallback.")]
        public ElementVfxLibraryAsset Library;

        [Tooltip("Log one message listing the picture slots that have no picture yet, once when play starts.")]
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

        // One log entry listing every empty slot, in the folder order of the README (Common last), instead of one line each
        // (dozens of lines on every Play hid real messages).
        void ReportMissing()
        {
            var line = new StringBuilder(1024);
            int missing = 0;
            for (int i = 0; i < 5; i++)
            {
                ElementId folder = i < 4 ? (ElementId)(i + 1) : ElementId.None;
                int slots = ElementVfx.SlotCount(folder);
                for (int s = 0; s < slots; s++)
                {
                    VfxSlot slot = ElementVfx.SlotAt(folder, s);
                    if (ElementVfx.TryGetTexture(folder, slot, out _, out _)) continue;
                    missing++;
                    line.Append("\n  Art/VFX/").Append(ElementVfx.FolderName(folder)).Append('/').Append(ElementVfx.SlotName(slot)).Append(".png");
                    if (folder == ElementId.Fire) line.Append(" (optional: Fire has its own look)");
                }
            }
            if (missing == 0) return;
            string header = "VFX: " + missing + " effect pictures missing; using the primitive stand-ins"
                            + (Library == null ? " (no Element VFX Library assigned)" : "") + ":";
            Debug.Log(header + line, this);
        }
    }
}
