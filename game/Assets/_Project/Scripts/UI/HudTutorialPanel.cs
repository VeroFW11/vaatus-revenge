using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The tutorial's panel at the top centre of the screen, drawn by TutorialDirector.OnGUI:
    //   8b / 11                Switch mid-combo: Earth
    //            [X][X], [RB]+[A] (Earth), keep going        <- the buttons as pictures, in their Xbox colours
    //            Green A is Earth: slow, heavy and rooted.    <- the hint (or "you're already in Earth" when you are)
    //                      ( ) ( )  ----------              <- a pip per success; the bar is the attempt so far
    //            View: skip  ·  hold View: quit
    // When a step is passed the panel flashes green for the success pause. Keyboard players see the keyboard keys and
    // the keyboard wording. Every text is parsed once per script (Bind) and again only when edited, so drawing allocates
    // nothing.
    public sealed class HudTutorialPanel
    {
        static readonly Color PanelColor = new Color(0.035f, 0.035f, 0.045f, 0.86f);
        static readonly Color OutlineColor = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color DimText = new Color(1f, 1f, 1f, 0.68f);
        static readonly Color PipEmpty = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color BarBack = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color SuccessColor = new Color(0.30f, 0.86f, 0.36f);
        static readonly Color QuitColor = new Color(0.86f, 0.30f, 0.24f);

        // Layout in 1080p reference pixels.
        const float Width = 820f;
        const float MinWidth = 560f;     // narrower than this, the panel goes below the player's bars instead
        const float AvoidGap = 12f;
        const float TopMargin = 22f;
        const float Pad = 18f;
        const float HeaderHeight = 38f;
        const float PromptHeight = 36f;
        const float HintLineHeight = 24f;     // one line of hint text; long hints wrap onto more lines (verify R2-04)
        const float HintPad = 3f;
        const float PipSize = 14f;
        const float PipGap = 8f;
        const float BarWidth = 160f;
        const float BarHeight = 5f;
        const float FooterHeight = 22f;
        const float RowGap = 6f;
        const float FlashAlpha = 0.35f;

        const string PadFooter = "View: skip  ·  hold View: quit";
        const string KeyFooter = "F8: skip  ·  F7: quit";

        TutorialScript boundScript;
        string[] counters = new string[0];
        HudGlyphText[] padPrompts = new HudGlyphText[0], keyPrompts = new HudGlyphText[0];
        HudGlyphText[] padHints = new HudGlyphText[0], keyHints = new HudGlyphText[0];
        HudGlyphText[] padAlready = new HudGlyphText[0], keyAlready = new HudGlyphText[0];

        // Parses every step's texts once. Call when the script changes (cheap to call every frame: it checks first).
        public void Bind(TutorialScript script)
        {
            if (ReferenceEquals(script, boundScript) && script != null && counters.Length == script.StepCount) return;
            boundScript = script;
            int count = script != null ? script.StepCount : 0;
            counters = new string[count];
            padPrompts = new HudGlyphText[count];
            keyPrompts = new HudGlyphText[count];
            padHints = new HudGlyphText[count];
            keyHints = new HudGlyphText[count];
            padAlready = new HudGlyphText[count];
            keyAlready = new HudGlyphText[count];
            for (int i = 0; i < count; i++) ParseStep(i, script.StepAt(i) ?? new TutorialStepData());
        }

        void ParseStep(int i, TutorialStepData step)
        {
            counters[i] = step.Id + " / " + boundScript.DisplayTotal;
            padPrompts[i] = HudGlyphText.Parse(step.PromptFor(true));
            keyPrompts[i] = HudGlyphText.Parse(step.PromptFor(false));
            padHints[i] = HudGlyphText.Parse(step.HintFor(true));
            keyHints[i] = HudGlyphText.Parse(step.HintFor(false));
            padAlready[i] = HudGlyphText.Parse(step.AlreadyInElementHintFor(true));
            keyAlready[i] = HudGlyphText.Parse(step.AlreadyInElementHintFor(false));
        }

        // Texts edited in the Inspector during Play are new string objects: re-parse the step on screen when one changed
        // (a reference check, so nothing is allocated otherwise).
        void RefreshIfEdited(int i, TutorialStepData step)
        {
            if (ReferenceEquals(padPrompts[i].Source, step.PromptFor(true)) && ReferenceEquals(keyPrompts[i].Source, step.PromptFor(false))
                && ReferenceEquals(padHints[i].Source, step.HintFor(true)) && ReferenceEquals(keyHints[i].Source, step.HintFor(false))
                && ReferenceEquals(padAlready[i].Source, step.AlreadyInElementHintFor(true))
                && ReferenceEquals(keyAlready[i].Source, step.AlreadyInElementHintFor(false))) return;
            ParseStep(i, step);
        }

        // quitHold01: how far View has been held toward quitting (0 = not held).
        // Where the panel ended on screen last time it was drawn (pixels from the top): the HUD's target panel goes below it.
        public float Bottom { get; private set; }

        public void Draw(HudPainter p, TutorialTracker tracker, bool gamepad, float quitHold01, Color accent)
        {
            Draw(p, tracker, gamepad, quitHold01, accent, Rect.zero);
        }

        // avoid: the player's bars at the top left (CombatHud.PlayerBarsRect). On a narrow screen (4:3, 16:10) the panel
        // narrows to stay clear of them, or, if that would make it too narrow, goes below them (round 7, S7-17).
        public void Draw(HudPainter p, TutorialTracker tracker, bool gamepad, float quitHold01, Color accent, Rect avoid)
        {
            if (p == null || tracker == null || !tracker.IsRunning) return;
            Bind(tracker.Script);
            int index = tracker.StepIndex;
            TutorialStepData step = tracker.Step;
            if (step == null || index < 0 || index >= counters.Length) return;
            RefreshIfEdited(index, step);

            HudGlyphText hint = tracker.AlreadyInTargetElement
                ? (gamepad ? padAlready[index] : keyAlready[index])
                : (gamepad ? padHints[index] : keyHints[index]);
            bool hasHint = hint.Source.Length > 0;

            float width = Mathf.Min(p.U(Width), Screen.width - p.U(TopMargin) * 2f);
            float top = p.U(TopMargin);
            if (avoid.width > 0f && (Screen.width - width) * 0.5f < avoid.xMax + p.U(AvoidGap) && top < avoid.yMax)
            {
                float clear = Screen.width - 2f * (avoid.xMax + p.U(AvoidGap));
                if (clear >= p.U(MinWidth)) width = clear;
                else top = avoid.yMax + p.U(AvoidGap);
            }
            float inner = width - p.U(Pad) * 2f;
            float hintLine = p.U(HintLineHeight);
            float hintHeight = hasHint ? hint.WrappedHeight(p, hintLine, gamepad, inner) + p.U(HintPad) * 2f : 0f;
            float height = p.U(Pad * 2f + HeaderHeight + PromptHeight + RowGap + PipSize + RowGap + FooterHeight)
                           + (hasHint ? hintHeight + p.U(RowGap) : 0f);
            var panel = new Rect((Screen.width - width) * 0.5f, top, width, height);
            Bottom = panel.yMax;
            p.Fill(panel, PanelColor);
            p.Outline(panel, Mathf.Max(1f, p.U(1.5f)), OutlineColor);

            float flash = tracker.SuccessPause01;
            if (flash > 0f) p.Fill(panel, WithAlpha(SuccessColor, FlashAlpha * flash));

            float x = panel.x + p.U(Pad);
            float y = panel.y + p.U(Pad);

            // Header: the step number on the left, the title in the middle.
            var header = new Rect(x, y, inner, p.U(HeaderHeight));
            p.Text(new Rect(header.x, header.y + (header.height - p.Measure(counters[index], p.Body).y) * 0.5f, inner, header.height),
                counters[index], p.Body, accent);
            p.Text(header, step.Title, p.Heading, flash > 0f ? SuccessColor : Color.white);
            y += p.U(HeaderHeight);

            // The prompt, centred, with its buttons as pictures.
            HudGlyphText prompt = gamepad ? padPrompts[index] : keyPrompts[index];
            float promptHeight = p.U(PromptHeight);
            float promptWidth = Mathf.Min(inner, prompt.Measure(p, promptHeight * 0.8f, gamepad));
            prompt.Draw(p, new Rect(panel.center.x - promptWidth * 0.5f, y + promptHeight * 0.1f, promptWidth, promptHeight * 0.8f), gamepad);
            y += promptHeight + p.U(RowGap);

            if (hasHint)
            {
                // Wrapped at the panel's inner width, each line centred, so a long hint never runs off the panel.
                hint.DrawWrapped(p, new Rect(x, y + p.U(HintPad), inner, hintHeight), hintLine, gamepad,
                    tracker.AlreadyInTargetElement ? accent : DimText);
                y += hintHeight + p.U(RowGap);
            }

            DrawProgress(p, tracker, panel.center.x, y, accent);
            y += p.U(PipSize + RowGap);

            // Footer: how to skip and quit, with the hold-to-quit bar filling under it.
            var footer = new Rect(panel.x, y, width, p.U(FooterHeight));
            p.Text(footer, gamepad ? PadFooter : KeyFooter, p.SmallCenter, DimText);
            if (quitHold01 > 0f)
            {
                float barWidth = p.U(BarWidth);
                p.Bar(new Rect(panel.center.x - barWidth * 0.5f, footer.yMax - p.U(BarHeight * 0.5f), barWidth, p.U(BarHeight * 0.6f)),
                    quitHold01, QuitColor, BarBack);
            }
        }

        // One pip per success needed (lit as they're earned); beside them, a bar for the attempt so far when the goal has a
        // running total (on-beat presses, combo hits, time).
        static void DrawProgress(HudPainter p, TutorialTracker tracker, float centerX, float y, Color accent)
        {
            int pips = tracker.Required;
            float size = p.U(PipSize);
            float gap = p.U(PipGap);
            float attempt = tracker.AttemptProgress01;
            bool showBar = attempt > 0f && !tracker.InSuccessPause;
            float barWidth = showBar ? p.U(BarWidth) : 0f;
            float total = pips * size + (pips - 1) * gap + (showBar ? gap * 2f + barWidth : 0f);
            float x = centerX - total * 0.5f;
            Color lit = tracker.InSuccessPause ? SuccessColor : accent;
            for (int i = 0; i < pips; i++)
            {
                p.Dot(new Rect(x, y, size, size), i < tracker.Progress ? lit : PipEmpty);
                x += size + gap;
            }
            if (!showBar) return;
            x += gap;
            p.Bar(new Rect(x, y + (size - p.U(BarHeight)) * 0.5f, barWidth, p.U(BarHeight)), attempt, accent, BarBack);
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
