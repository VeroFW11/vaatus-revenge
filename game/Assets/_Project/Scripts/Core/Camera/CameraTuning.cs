using System;

namespace VaatusRevenge.Core
{
    // Every number the third-person camera uses. It lives inside a CameraTuningAsset, so it can be tweaked
    // in the Inspector while playing (the camera reads it every frame, so changes show up immediately).
    // Defaults are the starting numbers from the Fire combat prototype spec: first guesses to tune by playtesting.
    // Angles are degrees, times are seconds, distances are metres.
    [Serializable]
    public class CameraTuning
    {
        // How far the camera sits behind the pivot (a point at the player's neck).
        public float FreeDistance = 4.0f;
        public float LockedDistance = 4.6f;      // a little further back when locked on, to fit both fighters in
        public float MinDistance = 1.0f;         // the free/locked distances are never closer than this. Walls can still
                                                 // push the camera closer: a close camera beats looking through a wall
        public float DistanceSmoothTime = 0.3f;  // easing between the free and locked distances

        // The pivot the camera orbits, measured up from the player's feet.
        public float PivotHeight = 1.55f;
        public float PivotHorizontalSmoothTime = 0.05f; // short, so the player stays centred while running and dashing
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

        // Lock-on framing. The camera swings behind the player to face the target.
        public float LockOnYawSmoothTime = 0.12f;  // how quickly it swings round
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
        public float NearClipPlane = 0.1f;         // keep this well under CollisionRadius

        // Field of view (vertical, degrees).
        public float BaseFov = 60f;
        public float SprintFovBoost = 5f;          // the player asks for this while sprinting (ThirdPersonCameraRig.SetFovBoost)
        public float FovSmoothTime = 0.25f;

        // Camera shake (CameraShake.Add takes amplitudes of 0..1; 0.05 to 0.3 is typical).
        public float ShakeMaxAngle = 8f;           // degrees of wobble at amplitude 1
        public float ShakeFrequency = 18f;         // wobbles per second
    }
}
