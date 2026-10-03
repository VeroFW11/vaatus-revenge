namespace VaatusRevenge.Core
{
    // The gamepad's shoulder chords, worked out from raw button states (pure C#, so EditMode tests can play exact
    // frame sequences; PlayerInputReader feeds it the real pad once per frame):
    //   hold RB, then press a face button = pick the element in that slot (ElementButtonLayout); the face press does
    //                                       nothing else and the face reads as untouched until it's let go.
    //   tap RB on its own                 = the ranged skill, fired as RB is let go (until then it might become a pick).
    //   hold LB, then press X / Y / B     = Heavy / AbilityNorth / AbilityEast (only early in the LB hold, AbilityChordWindow).
    //
    // Modifier first, exactly like Spider-Man 2's L1 + face abilities (lead design decision, 1-2 Oct): every face button
    // reaches the rules on the frame it's pressed, with no added delay and in the order it was pressed, so a dodge on the
    // danger cue starts when the thumb lands. A face button pressed BEFORE RB simply does its own job (X attacks, B
    // dodges, A jumps, Y zips); that is the documented rule, not a chord pressed out of order. Two protections stay:
    //   * RB and the face in the SAME frame (a "simultaneous" chord usually lands between two polls) is the chord: the
    //     face is swallowed and the RB hold that starts this frame is marked as used, so its release fires no skill.
    //   * a face held at most ChordSkillGuard when RB goes down: RB was meant as the chord modifier, so its release fires
    //     no skill (a slow chord is never a stray Ice Dart). Held longer (sprinting on B), an RB tap is the skill.
    //   * LB pressed while RB is held (parry / block instead of the switch): the RB release fires no skill either.
    public sealed class PadChordReader
    {
        // Face buttons, in element-slot order (Up, Right, Down, Left): Y/Triangle, B/Circle, A/Cross, X/Square.
        public const int FaceNorth = 0, FaceEast = 1, FaceSouth = 2, FaceWest = 3;
        const float Epsilon = 1e-4f;

        // RB held no longer than this, with no face button picked, fires the skill when let go.
        public float SkillTapMaxTime = 0.35f;
        // A face button counts as an LB ability chord only this long after LB went down.
        public float AbilityChordWindow = 0.5f;
        // A face held at most this long when RB goes down still stops RB's release firing the skill. Held longer
        // (sprinting on B, say), an RB tap is a deliberate skill.
        public float ChordSkillGuard = 0.15f;

        readonly bool[] faceSwallowed = new bool[4];
        readonly float[] faceAge = new float[4];        // real seconds since this face physically went down (-1 = up)
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
            public bool ElementSelectOffAttack; // ...with a face that isn't the attack button (B, A or Y; X is FaceWest)
            public ButtonState Skill;           // the RB tap (Pressed on release)
            public ButtonState Heavy;           // LB + X
            public ButtonState AbilityNorth;    // LB + Y
            public ButtonState AbilityEast;     // LB + B
        }

        // PlayerInputFrame.ElementSelectOffAttack for this frame's pick: a pad pick says itself (RB + a face other than X);
        // a number key (1-4) is never the attack button, so a key pick is always off-attack: pressing the element you're
        // already in mid-string shakes the wheel and never throws a hit (round 7, S7-01).
        public static bool PickIsOffAttack(ElementId padPick, bool padPickOffAttack, ElementId keyPick)
        {
            return padPick != ElementId.None ? padPickOffAttack : keyPick != ElementId.None;
        }

        public void Reset()
        {
            for (int i = 0; i < faceSwallowed.Length; i++)
            {
                faceSwallowed[i] = false;
                faceAge[i] = -1f;
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
            float dt = realDt > 0f ? realDt : 0f;

            // 0. How long each face has physically been down (before anything below rewrites it).
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

            // 2. Element picks: a face pressed while RB is held (RB first, or both on the same frame).
            ElementId picked = ElementId.None;
            int pickedFace = -1;
            Swallow(FaceNorth, ref north, rb.Held, ref picked, ref pickedFace, layout);
            Swallow(FaceEast, ref east, rb.Held, ref picked, ref pickedFace, layout);
            Swallow(FaceSouth, ref south, rb.Held, ref picked, ref pickedFace, layout);
            Swallow(FaceWest, ref west, rb.Held, ref picked, ref pickedFace, layout);
            // A face only just pressed before RB did its own job, but RB was meant as the chord: its release fires nothing.
            if (picked == ElementId.None && rbArrived && AnyFaceYoungerThan(ChordSkillGuard)) skillPadChordUsed = true;
            result.ElementSelect = picked;
            result.ElementSelectOffAttack = picked != ElementId.None && pickedFace != FaceWest;

            // 3. The RB tap: fires on release when nothing was picked during this hold. LB pressed during the hold
            //    (a parry or Earth block with RB still down, say after reading the gold mark mid-switch) also means RB
            //    was a modifier, not a tap: its release must not fire the skill and cancel the guard (round 7, J7-01).
            if (skillPadDown && lb.Pressed) skillPadChordUsed = true;
            if (skillPadDown)
            {
                skillPadHeldTime += dt;
                if (!rb.Held)
                {
                    if (!skillPadChordUsed && skillPadHeldTime <= SkillTapMaxTime + Epsilon) result.Skill.Pressed = true;
                    skillPadDown = false;
                }
            }

            // 4. LB ability chords (after the element picks, so a face RB took isn't also an ability).
            result.Heavy = ReadAbilityChord(ref west, chordModifier, ref heavyChord);
            result.AbilityNorth = ReadAbilityChord(ref north, chordModifier, ref abilityNorthChord);
            result.AbilityEast = ReadAbilityChord(ref east, chordModifier, ref abilityEastChord);
            return result;
        }

        void Swallow(int face, ref ButtonState button, bool rbHeld, ref ElementId picked, ref int pickedFace, ElementButtonLayout layout)
        {
            if (rbHeld && button.Pressed)
            {
                faceSwallowed[face] = true;
                skillPadChordUsed = true;
                if (picked == ElementId.None && layout != null)
                {
                    picked = layout.PadSlot(face);
                    pickedFace = face;
                }
            }
            if (!faceSwallowed[face]) return;
            if (!button.Held) faceSwallowed[face] = false;
            button = default(ButtonState);
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
