using System;

namespace VaatusRevenge.Core
{
    // Every number the third-person camera uses. It lives inside a CameraTuningAsset, so it can be tweaked
    // in the Inspector while playing (the camera reads it every frame, so changes show up immediately).
    // Defaults are the over-the-shoulder camera (like Marvel's Spider-Man 2) from the Fire combat prototype spec:
    // first guesses to tune by playtesting. CreateCentred() gives the older centred, Elden Ring-style camera
    // with the same code, so the two framings can be compared side by side.
    // Angles are degrees, times are seconds, distances are metres.
    [Serializable]
    public class CameraTuning
    {
        // How far the camera sits behind the shoulder point (the pivot at the player's neck, moved to one side).
        public float FreeDistance = 3.2f;        // close, so the fighter reads big on screen, as in Spider-Man 2
        public float LockedDistance = 4.0f;      // wider when locked on, to fit both fighters in. About FreeDistance +
                                                 // CombatPullback, so locking on mid-fight doesn't jump in or out
        public float MinDistance = 1.0f;         // the free/locked distances are never closer than this. Walls can still
                                                 // push the camera closer: a close camera beats looking through a wall
        public float DistanceSmoothTime = 0.3f;  // easing between the free and locked distances

        // Over the shoulder. The camera looks past one shoulder instead of straight at the fighter's back, so the
        // fighter sits to one side and the space they're running and fighting into stays open. 0 on both offsets
        // puts the fighter back in the middle of the screen (the centred framing).
        public float ShoulderOffset = 0.55f;          // metres to the side; + = over the right shoulder (fighter left of centre)
        public float LockedShoulderOffset = 0.35f;    // smaller while locked on, so the target stays readable near the centre
        public float ShoulderSmoothTime = 0.2f;       // easing whenever the offset changes: swapping shoulders, locking on/off
        public float ShoulderSwapHoldTime = 0.25f;    // seconds the swap button (L3 / V) must be held; one swap per hold.
                                                      // L3 is the stick you push hard while sprinting, so a plain click
                                                      // swapped sides by accident. 0 = swap on press
        public bool AutoSwapShoulderWhenBlocked = true; // a wall hugging the shoulder side swaps to the other shoulder
                                                        // until it clears, instead of squashing the view against it
        public float ShoulderBlockedFraction = 0.5f;  // that side counts as blocked when less than this share of the offset fits
        public float ShoulderSwapBackDelay = 1.5f;    // the original side must stay clear this long before swapping back,
                                                      // so running past a row of pillars or a broken wall doesn't flip the
                                                      // camera back and forth
        public float LockOnMaxShoulderAim = 15f;      // degrees: most the lock-on turn is corrected for the offset. Up close
                                                      // the full correction grows fast and would swing the camera around

        // Combat pull-back: with a foe nearby and no lock-on, the view widens so the whole fight fits, like Spider-Man 2.
        public float CombatFramingRadius = 8f;        // a living foe this close counts as "in a fight"
        public float CombatPullback = 0.9f;           // extra distance in a fight (0 turns the pull-back off)
        public float CombatPullbackHeight = 0.2f;     // and this much higher, to see over the fighters
        public float CombatFramingSmoothTime = 0.4f;  // how gently it widens and narrows (settles in about three times this)
        public float CombatFramingReleaseDelay = 1.5f; // stays wide this long after the last foe leaves the radius, so an
                                                       // enemy circling at the edge doesn't pump the camera in and out

        // The pivot the camera orbits, measured up from the player's feet.
        public float PivotHeight = 1.6f;
        public float PivotHorizontalSmoothTime = 0.05f; // short, so the player stays put on screen while running and dashing
        public float PivotVerticalSmoothTime = 0.18f;   // longer, so jumps and stair steps don't bob the camera
        public float PivotMaxHorizontalLag = 1.5f;      // safety net: the pivot never trails the player by more than
        public float PivotMaxVerticalLag = 1.5f;        // this, so a long fall can't drop them off screen. Hitting the
                                                        // limit jolts the camera, so keep it above what jumps, dodges
                                                        // and lunges reach (about 0.6 to 0.95 m with these smooth times)
        public float TeleportSnapDistance = 5f;         // the player moved further than this in one frame (a respawn):
                                                        // cut straight there instead of gliding across the arena

        // Pitch: positive looks down, negative looks up (Unity's convention).
        public float MinPitch = -40f;
        public float MaxPitch = 65f;
        public float DefaultPitch = 12f;

        // Free look.
        public float StickYawSpeed = 200f;         // degrees per second at full right-stick tilt
        public float StickPitchSpeed = 140f;
        public float StickResponseExponent = 1.6f; // above 1, small tilts turn slowly for fine aiming; full tilt is full speed
        public float MouseDegreesPerPixel = 0.12f; // mouse sensitivity. Never multiplied by frame time (see OrbitCameraModel)
        public bool InvertY = false;

        // Lock-on framing. The camera swings round behind the player to face the target.
        public float LockOnYawSmoothTime = 0.12f;  // how quickly it swings round
        public float LockOnMaxYawSpeed = 540f;     // degrees per second it may swing at most (9 per frame at 60 fps), so a
                                                   // target passing overhead can't whip the view round. 0 = no cap
        public float LockOnPitch = 18f;            // resting pitch while locked on
        public float LockOnPitchSmoothTime = 0.25f;
        public float LockOnFramingAbove = 20f;     // the target may sit this far above the screen centre before the camera
                                                   // tilts up to keep it in frame (tall or elevated targets)...
        public float LockOnFramingBelow = 25f;     // ...or this far below before it tilts down (a target under a ledge)
        public float LockOnYawDeadZone = 0.5f;     // metres: a target this close horizontally (e.g. straight above) has no
                                                   // clear direction, so it doesn't turn the camera (it would spin wildly)

        // Recentre: lock-on pressed with nothing to lock onto swings the camera behind the player (like Elden Ring).
        public float RecenterSmoothTime = 0.1f;

        // Collision with walls, pillars and ceilings.
        public float CollisionRadius = 0.25f;      // size of the camera's collision sphere. Must stay bigger than the
                                                   // near-clip corners (NearClipPlane) or walls get cut open up close
        public float CollisionEaseOutTime = 0.35f; // walls pull the camera in instantly; it eases back out over about this long
        public float OcclusionGraceTime = 0.1f;    // something between the camera and the player that isn't touching the camera
                                                   // (a pillar edge while orbiting, a roof edge) is ignored this long before the
                                                   // camera moves in front of it. Many clear by themselves, so the view doesn't
                                                   // hop in and out
        public float CollisionPullInTime = 0.15f;  // ...then it glides in over about this long, up to the pillar's far side, and
                                                   // only skips the pillar's own thickness in one frame. 0 = jump straight in
        public float CollisionLookAhead = 0.2f;    // metres: a second, fatter probe (radius + this) spots a pillar about to touch the
                                                   // camera (sliding in from the side as you strafe), so it glides in early
                                                   // instead of jumping once it touches. 0 = no look-ahead
        public float CollisionSweepTime = 0.3f;    // seconds: while the view is swinging round (a lock-on turn), extra probes look
                                                   // where the camera will be this far ahead, so a pillar face the swing is about to
                                                   // sweep into the camera is seen early and the camera glides in instead of jumping
                                                   // (report 02, round 3). 0 = off
        public float CollisionSweepMaxAngle = 60f; // degrees: the look-ahead along the swing never reaches further round than this
        public int CollisionSweepSamples = 3;      // probes spread along that arc (a thin pillar can sit between two of them)
        public float CollisionSweepMinDistance = 0.6f; // the sweep may start the glide down to this close (a real contact is coming, unlike
                                                   // the fat look-ahead's grazes); closer than that, contact still moves it in
        public float MinCollisionDistance = 1.1f;  // with a wall closer behind than this (a pillar at your back), the camera rises
                                                   // and looks down over the player's head instead of sliding into it. 0 = off
        public float MaxCollisionRisePitch = 85f;  // how steeply (degrees down) it may look while rising over the head (nearly top-down
                                                   // with your back to a wall in the low corridor, where there is no other room)
        public float CollisionRiseSmoothTime = 0.05f; // how quickly it rises and settles back...
        public float CollisionRiseMaxSpeed = 8f;   // ...but the camera never moves faster than this (m/s) while doing it, so a
                                                   // camera still far out swings up gently
        public float NearClipPlane = 0.1f;         // keep this well under CollisionRadius

        // Field of view (vertical, degrees).
        public float BaseFov = 60f;
        public float SprintFovBoost = 5f;          // the player asks for this while sprinting (ThirdPersonCameraRig.SetFovBoost)
        public float FovSmoothTime = 0.25f;

        // Camera shake (CameraShake.Add takes amplitudes of 0..1; 0.05 to 0.3 is typical).
        public float ShakeMaxAngle = 8f;           // degrees of wobble at amplitude 1
        public float ShakeFrequency = 18f;         // wobbles per second

        // The default: over the shoulder, like Marvel's Spider-Man 2.
        public static CameraTuning CreateOverTheShoulder()
        {
            return new CameraTuning();
        }

        // The first prototype's centred, Elden Ring-style camera: fighter in the middle of the screen, further back,
        // no combat pull-back. Same code, just these numbers, so Jeremy can compare the two in the same fight.
        public static CameraTuning CreateCentred()
        {
            return new CameraTuning
            {
                FreeDistance = 4.0f,
                LockedDistance = 4.6f,
                PivotHeight = 1.55f,
                ShoulderOffset = 0f,
                LockedShoulderOffset = 0f,
                CombatPullback = 0f,
                CombatPullbackHeight = 0f,
            };
        }
    }
}
