using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The combat tutorial as an asset you edit in the Inspector: every step's title, button prompt, hint, what counts as
    // passing it and how many times. The sandbox builder makes Assets/_Project/Tuning/Tutorial.asset from
    // TutorialScript.CreateDefault(); TutorialDirector runs it. Prompts use button tags: "{RB}+{X}" draws the RB pill
    // and a blue X (HudGlyphs lists the names). Create one via Assets > Create > Vaatu's Revenge > Tuning > Tutorial.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Tutorial", fileName = "Tutorial")]
    public class TutorialScriptAsset : ScriptableObject
    {
        [Tooltip("The steps in order, the success flash and how long View is held to quit.")]
        public TutorialScript Script = TutorialScript.CreateDefault();
    }
}
