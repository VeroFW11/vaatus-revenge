namespace VaatusRevenge.Core
{
    // The gamepad's shoulder chords, worked out from raw button states (pure C#, so EditMode tests can play exact
    // frame sequences; PlayerInputReader feeds it the real pad once per frame):
    //   hold RB + a face button = pick the element in that slot (ElementButtonLayout); the face press does nothing else
    //   and the face reads as untouched until it's let go.
    //   tap RB on its own        = the ranged skill, fired as RB is let go (until then it might become an element pick).
    //   hold LB + X / Y / B      = Heavy / AbilityNorth / AbilityEast (only early in the LB hold, AbilityChordWindow).
    //
    // Human timings this has to survive (Build 05 verify rounds 2 and 4):
    //   * RB and the face button in the SAME frame (a "simultaneous" chord usually lands between two polls). The face
    //     is swallowed first and the RB hold that starts this frame is marked as used, so its release fires no skill.
    //   * Y, B or A a few frames BEFORE RB (thumb first, then the finger: how most people press a chord). With RB up,
    //     a fresh Y / B / A press is held back for FaceChordLatency before it's reported. If RB goes down in that time
    //     the press only picks the element (no zip, dodge or jump ever starts, so nothing has to be taken back);
    //     otherwise it's reported then, carrying its true age (ButtonState.PressDelay) so the buffer and the dodge's
    //     tap/hold timer still count from the real press. Let go sooner (a quick tap), it's reported at once.
    //     Jeremy can set the latency to 0 (PlayerInputReader) to trade the chord tolerance for zero added delay; the
    //     pick then falls back to the grace below, which can only take back a press still waiting in the buffer.
    //   * X a few frames before RB (ElementChordGrace). X is never held back (its beat grade must not move): the X
    //     press is upgraded to the element pick, RetractPress names it so the combat model can turn a queued X into
    //     the switch strike (or keep an X that already ran as that one hit), and RB's release fires no skill.
    //   * a face held a little longer than the grace when RB goes down (ChordSkillGuard): too late to be a pick, but
    //     RB's release fires no skill either, so a slow chord is never a stray Ice Dart.
    public sealed class PadChordReader
    {
        // Face buttons, in element-slot order (Up, Right, Down, Left): Y/Triangle, B/Circle, A/Cross, X/Square.
        public const int FaceNorth = 0, FaceEast = 1, FaceSouth = 2, FaceWest = 3;
        const float Epsilon = 1e-4f;

        // RB held no longer than this, with no face button picked, fires the skill when let go.
        public float SkillTapMaxTime = 0.35f;
        // A face button counts as an LB ability chord only this long after LB went down.
        public float AbilityChordWindow = 0.5f;
        // X pressed at most this long before RB (and still held) becomes the element pick (5 frames at 60 fps). Also used
        // for Y / B / A when FaceChordLatency is 0.
        public float ElementChordGrace = 0.08f;
        // With RB up, a fresh Y / B / A press is held back this long in case RB follows (0 = report at once).
        public float FaceChordLatency = 0.08f;
        // A face held at most this long when RB goes down (but too old to be a pick) still stops RB's release firing
        // the skill. Held longer (sprinting on B, say), an RB tap is a deliberate skill.
        public float ChordSkillGuard = 0.15f;

        readonly bool[] faceSwallowed = new bool[4];
        readonly float[] faceAge = new float[4];        // real seconds since this face physically went down (-1 = up)
        readonly bool[] pending = new bool[4];          // Y / B / A held back, waiting to see if RB follows
        readonly float[] pendingAge = new float[4];
        bool skillPadDown;
        float skillPadHeldTime;
        bool skillPadChordUsed;
        float guardHeldTime;
        bool heavyChord, abilityNorthChord, abilityEastChord;

        public PadChordReader()
        {
            Reset();
        }

        // This frame's result (also the face states, rewritten in place by Read).
        public struct Result
        {
            public ElementId ElementSelect;     // an element picked with RB this frame (None otherwise)
            public PlayerCommand RetractPress;  // the face press that became this frame's pick a frame or two late
            public ButtonState Skill;           // the RB tap (Pressed on release)
            public ButtonState Heavy;           // LB + X
            public ButtonState AbilityNorth;    // LB + Y
            public ButtonState AbilityEast;     // LB + B
        }

        public void Reset()
        {
            for (int i = 0; i < faceSwallowed.Length; i++)
            {
                faceSwallowed[i] = false;
                faceAge[i] = -1f;
                pending[i] = false;
                pendingAge[i] = 0f;
            }
            skillPadDown = false;
            skillPadHeldTime = 0f;
            skillPadChordUsed = false;
            guardHeldTime = 0f;
            heavyChord = false;
            abilityNorthChord = false;
            abilityEastChord = false;
        }

        // A Y / B / A press is being held back right now (for tests and the HUD's debug readout).
        public bool IsHoldingBack(int face)
        {
            return face >= 0 && face < pending.Length && pending[face];
        }

        // north = Y (zip), east = B (dodge), south = A (jump), west = X (attack): rewritten in place (a swallowed face
        // reads as untouched, a face used by an LB chord too, a held-back face too until it's reported). rb / lb are the
        // raw shoulder buttons. realDt is unscaled.
        public Result Read(ref ButtonState north, ref ButtonState east, ref ButtonState south, ref ButtonState west,
                           ButtonState rb, ButtonState lb, float realDt, ElementButtonLayout layout)
        {
            var result = default(Result);
            float dt = realDt > 0f ? realDt : 0f;

            // 0. How long each face has physically been down (before anything below rewrites it). The grace compares
            //    the age as of the previous frame, so "pressed k frames before RB" means the same here and in the hold-back.
            TrackFaceAge(FaceNorth, north, dt);
            TrackFaceAge(FaceEast, east, dt);
            TrackFaceAge(FaceSouth, south, dt);
            TrackFaceAge(FaceWest, west, dt);
            guardHeldTime = lb.Held ? (lb.Pressed ? 0f : guardHeldTime + dt) : 0f;
            bool chordModifier = lb.Held && guardHeldTime <= AbilityChordWindow + Epsilon;

            // 1. The RB hold: a new press starts a fresh hold BEFORE any face is swallowed, so a face swallowed on the
            //    same frame marks this hold as used (the round-1 order reset it afterwards and the release fired the skill).
            bool rbArrived = rb.Pressed && rb.Held;
            if (rb.Pressed)
            {
                skillPadDown = true;
                skillPadHeldTime = 0f;
                skillPadChordUsed = false;
            }

            // 2. Y / B / A held back while RB is up. RB arriving in time turns the youngest into the pick.
            ElementId picked = ElementId.None;
            int heldBackPick = -1;
            if (rbArrived) heldBackPick = YoungestPending();
            HoldBack(FaceNorth, ref north, rb.Held, chordModifier, dt, heldBackPick);
            HoldBack(FaceEast, ref east, rb.Held, chordModifier, dt, heldBackPick);
            HoldBack(FaceSouth, ref south, rb.Held, chordModifier, dt, heldBackPick);
            if (heldBackPick >= 0)
            {
                faceSwallowed[heldBackPick] = true;
                skillPadChordUsed = true;
                picked = layout != null ? layout.PadSlot(heldBackPick) : ElementId.None;
            }

            // 3. Element picks: a face pressed while RB is held, or (grace) pressed just before this RB press.
            Swallow(FaceNorth, ref north, rb.Held, ref picked, layout);
            Swallow(FaceEast, ref east, rb.Held, ref picked, layout);
            Swallow(FaceSouth, ref south, rb.Held, ref picked, layout);
            Swallow(FaceWest, ref west, rb.Held, ref picked, layout);
            if (picked == ElementId.None && rbArrived)
            {
                int late = LatestGraceFace(north, east, south, west, dt);
                if (late >= 0)
                {
                    faceSwallowed[late] = true;
                    skillPadChordUsed = true;
                    picked = layout != null ? layout.PadSlot(late) : ElementId.None;
                    result.RetractPress = FaceCommand(late);
                    if (late == FaceNorth) north = default(ButtonState);
                    else if (late == FaceEast) east = default(ButtonState);
                    else if (late == FaceSouth) south = default(ButtonState);
                    else west = default(ButtonState);
                }
            }
            // Too slow for a pick, but a face only just pressed: RB was meant as the chord, so its release fires nothing.
            if (picked == ElementId.None && rbArrived && AnyFaceYoungerThan(ChordSkillGuard)) skillPadChordUsed = true;
            result.ElementSelect = picked;

            // 4. The RB tap: fires on release when nothing was picked during this hold.
            if (skillPadDown)
            {
                skillPadHeldTime += dt;
                if (!rb.Held)
                {
                    if (!skillPadChordUsed && skillPadHeldTime <= SkillTapMaxTime + Epsilon) result.Skill.Pressed = true;
                    skillPadDown = false;
                }
            }

            // 5. LB ability chords (after the element picks, so a face RB took isn't also an ability).
            result.Heavy = ReadAbilityChord(ref west, chordModifier, ref heavyChord);
            result.AbilityNorth = ReadAbilityChord(ref north, chordModifier, ref abilityNorthChord);
            result.AbilityEast = ReadAbilityChord(ref east, chordModifier, ref abilityEastChord);
            return result;
        }

        // The command a face button's normal action pushes (what RetractPress names).
        public static PlayerCommand FaceCommand(int face)
        {
            switch (face)
            {
                case FaceNorth: return PlayerCommand.ZipStrike;
                case FaceEast: return PlayerCommand.Dodge;
                case FaceSouth: return PlayerCommand.Jump;
                case FaceWest: return PlayerCommand.Light;
                default: return PlayerCommand.None;
            }
        }

        // One of Y / B / A: start holding a fresh press back, keep it back, or report it (late, with its true age).
        void HoldBack(int face, ref ButtonState button, bool rbHeld, bool chordModifier, float dt, int pickedFace)
        {
            if (pending[face])
            {
                pendingAge[face] += dt;
                if (face == pickedFace)
                {
                    // RB arrived in time: the press only ever picks the element. (Let go on RB's own frame still counts:
                    // the thumb was down until the finger landed.)
                    pending[face] = false;
                    button = button.Held ? button : default(ButtonState);
                    return;
                }
                if (!button.Held)
                {
                    // A quick tap let go before the wait ran out: report it now, press and release together.
                    pending[face] = false;
                    button = new ButtonState { Pressed = true, Released = true, PressDelay = pendingAge[face] };
                    return;
                }
                if (pendingAge[face] + Epsilon >= FaceChordLatency || rbHeld)
                {
                    // No RB in time (or RB came down in a way that isn't this chord): the face does its own job now.
                    pending[face] = false;
                    button = new ButtonState { Held = true, Pressed = true, PressDelay = pendingAge[face] };
                    return;
                }
                button = default(ButtonState);
                return;
            }
            if (!button.Pressed || !button.Held || rbHeld || chordModifier || faceSwallowed[face] || FaceChordLatency <= Epsilon) return;
            pending[face] = true;
            pendingAge[face] = 0f;
            button = default(ButtonState);
        }

        int YoungestPending()
        {
            int best = -1;
            float bestAge = float.MaxValue;
            for (int face = 0; face < pending.Length; face++)
            {
                if (!pending[face] || pendingAge[face] >= bestAge) continue;
                bestAge = pendingAge[face];
                best = face;
            }
            return best;
        }

        void Swallow(int face, ref ButtonState button, bool rbHeld, ref ElementId picked, ElementButtonLayout layout)
        {
            if (rbHeld && button.Pressed)
            {
                faceSwallowed[face] = true;
                skillPadChordUsed = true;
                if (picked == ElementId.None && layout != null) picked = layout.PadSlot(face);
            }
            if (!faceSwallowed[face]) return;
            if (!button.Held) faceSwallowed[face] = false;
            button = default(ButtonState);
        }

        // The face pressed most recently within the grace, still held, and not already taken by a chord. With the
        // hold-back on, only X goes this way (a Y / B / A this old was already reported and has started its action).
        int LatestGraceFace(ButtonState north, ButtonState east, ButtonState south, ButtonState west, float dt)
        {
            int best = -1;
            float bestAge = float.MaxValue;
            bool holdBackOn = FaceChordLatency > Epsilon;
            for (int face = 0; face < 4; face++)
            {
                if (holdBackOn && face != FaceWest) continue;
                ButtonState button = face == FaceNorth ? north : face == FaceEast ? east : face == FaceSouth ? south : west;
                if (!button.Held || faceSwallowed[face] || IsAbilityLatched(face)) continue;
                float age = faceAge[face];
                // Pressed k frames before RB: its age a frame ago was (k - 1) frames, and the grace covers that.
                if (age < 0f || age - dt >= ElementChordGrace - Epsilon) continue;
                if (age < bestAge)
                {
                    bestAge = age;
                    best = face;
                }
            }
            return best;
        }

        bool AnyFaceYoungerThan(float limit)
        {
            for (int face = 0; face < faceAge.Length; face++)
            {
                if (faceAge[face] >= 0f && faceAge[face] <= limit + Epsilon && !IsAbilityLatched(face)) return true;
            }
            return false;
        }

        bool IsAbilityLatched(int face)
        {
            return (face == FaceWest && heavyChord) || (face == FaceNorth && abilityNorthChord) || (face == FaceEast && abilityEastChord);
        }

        // How long each face has physically been down. Ages from 0 on the frame it went down.
        void TrackFaceAge(int face, ButtonState button, float dt)
        {
            if (!button.Held)
            {
                faceAge[face] = -1f;
                return;
            }
            if (button.Pressed || faceAge[face] < 0f) faceAge[face] = 0f;
            else faceAge[face] += dt;
        }

        // Hold LB and press a face button: that press, and everything until it's let go, is the ability in that slot.
        static ButtonState ReadAbilityChord(ref ButtonState faceButton, bool modifierActive, ref bool latched)
        {
            if (faceButton.Pressed && modifierActive) latched = true;
            if (!latched) return default(ButtonState);
            ButtonState abilityButton = faceButton;
            faceButton = default(ButtonState);
            if (!abilityButton.Held) latched = false;   // let go (or a sub-frame tap): the chord is over
            return abilityButton;
        }
    }
}
