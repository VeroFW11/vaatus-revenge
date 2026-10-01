namespace VaatusRevenge.Core
{
    // The gamepad's shoulder chords, worked out from raw button states (pure C#, so EditMode tests can play exact
    // frame sequences; PlayerInputReader feeds it the real pad once per frame):
    //   hold RB + a face button = pick the element in that slot (ElementButtonLayout); the face press does nothing else
    //   and the face reads as untouched until it's let go.
    //   tap RB on its own        = the ranged skill, fired as RB is let go (until then it might become an element pick).
    //   hold LB + X / Y / B      = Heavy / AbilityNorth / AbilityEast (only early in the LB hold, AbilityChordWindow).
    //
    // Two human timings this has to survive (Build 05 verify round 2):
    //   * RB and the face button in the SAME frame (a "simultaneous" chord usually lands between two polls). The face
    //     is swallowed first and the RB hold that starts this frame is marked as used, so its release fires no skill.
    //   * the face button a frame or two BEFORE RB (ElementChordGrace). The face press is upgraded to the element pick:
    //     it's swallowed from RB's frame on, RetractPress names the command it already started so the combat model can
    //     take it back if it's still waiting in the buffer, and RB's release fires no skill.
    public sealed class PadChordReader
    {
        // Face buttons, in element-slot order (Up, Right, Down, Left): Y/Triangle, B/Circle, A/Cross, X/Square.
        public const int FaceNorth = 0, FaceEast = 1, FaceSouth = 2, FaceWest = 3;
        const float Epsilon = 1e-4f;

        // RB held no longer than this, with no face button picked, fires the skill when let go.
        public float SkillTapMaxTime = 0.35f;
        // A face button counts as an LB ability chord only this long after LB went down.
        public float AbilityChordWindow = 0.5f;
        // A face button pressed at most this long before RB (and still held) becomes the element pick.
        public float ElementChordGrace = 0.05f;

        readonly bool[] faceSwallowed = new bool[4];
        readonly float[] faceHeldFor = new float[4];       // real seconds since this face went down (-1 = up)
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
                faceHeldFor[i] = -1f;
            }
            skillPadDown = false;
            skillPadHeldTime = 0f;
            skillPadChordUsed = false;
            guardHeldTime = 0f;
            heavyChord = false;
            abilityNorthChord = false;
            abilityEastChord = false;
        }

        // north = Y (zip), east = B (dodge), south = A (jump), west = X (attack): rewritten in place (a swallowed face
        // reads as untouched, a face used by an LB chord too). rb / lb are the raw shoulder buttons. realDt is unscaled.
        public Result Read(ref ButtonState north, ref ButtonState east, ref ButtonState south, ref ButtonState west,
                           ButtonState rb, ButtonState lb, float realDt, ElementButtonLayout layout)
        {
            var result = default(Result);

            // 1. The RB hold: a new press starts a fresh hold BEFORE any face is swallowed, so a face swallowed on the
            //    same frame marks this hold as used (the round-1 order reset it afterwards and the release fired the skill).
            if (rb.Pressed)
            {
                skillPadDown = true;
                skillPadHeldTime = 0f;
                skillPadChordUsed = false;
            }

            // 2. Element picks: a face pressed while RB is held, or (grace) pressed just before this RB press.
            ElementId picked = ElementId.None;
            Swallow(FaceNorth, ref north, rb.Held, ref picked, layout);
            Swallow(FaceEast, ref east, rb.Held, ref picked, layout);
            Swallow(FaceSouth, ref south, rb.Held, ref picked, layout);
            Swallow(FaceWest, ref west, rb.Held, ref picked, layout);
            if (picked == ElementId.None && rb.Pressed && rb.Held)
            {
                int late = LatestGraceFace(north, east, south, west);
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
            result.ElementSelect = picked;
            TrackFaceAge(FaceNorth, north, realDt);
            TrackFaceAge(FaceEast, east, realDt);
            TrackFaceAge(FaceSouth, south, realDt);
            TrackFaceAge(FaceWest, west, realDt);

            // 3. The RB tap: fires on release when nothing was picked during this hold.
            if (skillPadDown)
            {
                skillPadHeldTime += realDt;
                if (!rb.Held)
                {
                    if (!skillPadChordUsed && skillPadHeldTime <= SkillTapMaxTime + Epsilon) result.Skill.Pressed = true;
                    skillPadDown = false;
                }
            }

            // 4. LB ability chords (after the element picks, so a face RB took isn't also an ability).
            guardHeldTime = lb.Held ? (lb.Pressed ? 0f : guardHeldTime + realDt) : 0f;
            bool chordModifier = lb.Held && guardHeldTime <= AbilityChordWindow + Epsilon;
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

        // The face pressed most recently within the grace, still held, and not already taken by a chord.
        int LatestGraceFace(ButtonState north, ButtonState east, ButtonState south, ButtonState west)
        {
            int best = -1;
            float bestAge = float.MaxValue;
            for (int face = 0; face < 4; face++)
            {
                ButtonState button = face == FaceNorth ? north : face == FaceEast ? east : face == FaceSouth ? south : west;
                if (!button.Held || faceSwallowed[face] || IsAbilityLatched(face)) continue;
                float age = faceHeldFor[face];
                if (age < 0f || age > ElementChordGrace + Epsilon) continue;
                if (age < bestAge)
                {
                    bestAge = age;
                    best = face;
                }
            }
            return best;
        }

        bool IsAbilityLatched(int face)
        {
            return (face == FaceWest && heavyChord) || (face == FaceNorth && abilityNorthChord) || (face == FaceEast && abilityEastChord);
        }

        // How long each (unswallowed) face has been down, for the grace. Ages from 0 on the frame it went down.
        void TrackFaceAge(int face, ButtonState button, float realDt)
        {
            if (faceSwallowed[face] || !button.Held)
            {
                faceHeldFor[face] = -1f;
                return;
            }
            if (button.Pressed || faceHeldFor[face] < 0f) faceHeldFor[face] = 0f;
            else faceHeldFor[face] += realDt;
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
