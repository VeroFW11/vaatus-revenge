using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Stand-in clips for animation keys that don't have their own poses yet: each borrows the nearest Fire clip under
    // its own key, so every key plays something sensible from the day it exists (and AnimationTests stays green). A
    // real clip for the key always wins: a placeholder is only added when no clip with that key was built. Entries are
    // removed from this table as the real clips arrive; when it is empty, every key has its own animation.
    public static partial class PoseLibraryDefaults
    {
        // { new key, the existing clip it borrows }. A method, not a static field: Core keeps no static state.
        static string[,] PlaceholderTable()
        {
            return new[,]
            {
                // Fire
                { AnimationKeys.SweepKick, AnimationKeys.FlameWheel },
                { AnimationKeys.RisingPhoenixKick, AnimationKeys.Launcher },
                { AnimationKeys.SpinBackKick, AnimationKeys.SpinKick },
                // Dodges and the element switch
                { AnimationKeys.DodgeSlip, AnimationKeys.Dodge },
                { AnimationKeys.DodgeSideLeft, AnimationKeys.Dodge },
                { AnimationKeys.DodgeSideRight, AnimationKeys.Dodge },
                { AnimationKeys.DodgeEvade, AnimationKeys.Backstep },
                { AnimationKeys.ElementSwitch, AnimationKeys.ParrySuccess },
                // Water
                { AnimationKeys.PalmWard, AnimationKeys.Jab },
                { AnimationKeys.PalmRollback, AnimationKeys.Cross },
                { AnimationKeys.ForearmPress, AnimationKeys.Cross },
                { AnimationKeys.TwoPalmPush, AnimationKeys.PhoenixPalm },
                { AnimationKeys.SingleWhip, AnimationKeys.FireWhip },
                { AnimationKeys.CloudHands, AnimationKeys.FlameWheel },
                { AnimationKeys.SplitMane, AnimationKeys.PhoenixPalm },
                { AnimationKeys.ReturnTide, AnimationKeys.SpinKick },
                { AnimationKeys.CraneRise, AnimationKeys.Launcher },
                { AnimationKeys.AirBrushPalm, AnimationKeys.AirJab },
                { AnimationKeys.AirShuttle, AnimationKeys.AirCrescent },
                { AnimationKeys.AirNeedle, AnimationKeys.AirTornado },
                { AnimationKeys.SnakeDrop, AnimationKeys.AxeKick },
                { AnimationKeys.TidePalm, AnimationKeys.FaJinPalm },
                { AnimationKeys.WaterLash, AnimationKeys.FireWhip },
                { AnimationKeys.TideRing, AnimationKeys.FlameWheel },
                { AnimationKeys.DartFlick, AnimationKeys.FireBlast },
                { AnimationKeys.WaveRide, AnimationKeys.ZipKick },
                { AnimationKeys.RushPalm, AnimationKeys.SprintKick },
                // Earth
                { AnimationKeys.HorsePunch, AnimationKeys.Jab },
                { AnimationKeys.TigerClaw, AnimationKeys.Cross },
                { AnimationKeys.StompLine, AnimationKeys.SnapKick },
                { AnimationKeys.ButterflyPalms, AnimationKeys.PhoenixPalm },
                { AnimationKeys.QuakeSlam, AnimationKeys.FaJinPalm },
                { AnimationKeys.BoulderRaise, AnimationKeys.Launcher },
                { AnimationKeys.BoulderHurl, AnimationKeys.FireBlast },
                { AnimationKeys.PivotElbow, AnimationKeys.SpinKick },
                { AnimationKeys.PillarUppercut, AnimationKeys.Launcher },
                { AnimationKeys.AirHammer, AnimationKeys.AirJab },
                { AnimationKeys.AirBackKick, AnimationKeys.AirCrescent },
                { AnimationKeys.AirMeteor, AnimationKeys.AirTornado },
                { AnimationKeys.QuakeDrop, AnimationKeys.AxeKick },
                { AnimationKeys.RootFaJin, AnimationKeys.FaJinPalm },
                { AnimationKeys.SpikeLine, AnimationKeys.FireBlast },
                { AnimationKeys.StoneTent, AnimationKeys.FlameWheel },
                { AnimationKeys.BoulderToss, AnimationKeys.FireBlast },
                { AnimationKeys.EarthSurf, AnimationKeys.ZipKick },
                { AnimationKeys.ShoulderCharge, AnimationKeys.SprintKick },
                // Air
                { AnimationKeys.PiercingPalm, AnimationKeys.Jab },
                { AnimationKeys.TurningPalm, AnimationKeys.Cross },
                { AnimationKeys.SwimSweep, AnimationKeys.FlameWheel },
                { AnimationKeys.DoublePalmChange, AnimationKeys.SpinKick },
                { AnimationKeys.GalePalm, AnimationKeys.PhoenixPalm },
                { AnimationKeys.CircleWalk, AnimationKeys.SpinKick },
                { AnimationKeys.Whirlwind, AnimationKeys.FlameWheel },
                { AnimationKeys.CircleStepPalm, AnimationKeys.SpinKick },
                { AnimationKeys.UpdraftPalm, AnimationKeys.Launcher },
                { AnimationKeys.AirSwipe, AnimationKeys.AirJab },
                { AnimationKeys.AirSpiralKick, AnimationKeys.AirCrescent },
                { AnimationKeys.AirDownburst, AnimationKeys.AirTornado },
                { AnimationKeys.AirLanding, AnimationKeys.AxeKick },
                { AnimationKeys.HurricanePalm, AnimationKeys.FaJinPalm },
                { AnimationKeys.AirBlade, AnimationKeys.FireWhip },
                { AnimationKeys.AirShield, AnimationKeys.FlameWheel },
                { AnimationKeys.AirBlast, AnimationKeys.FireBlast },
                { AnimationKeys.WindLeap, AnimationKeys.ZipKick },
                { AnimationKeys.WindRunnerKick, AnimationKeys.SprintKick },
            };
        }

        static void AddPlaceholders(List<PoseClip> clips)
        {
            var byKey = new Dictionary<string, PoseClip>(clips.Count);
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null && clips[i].IsValid) byKey[clips[i].Key] = clips[i];
            }
            string[,] table = PlaceholderTable();
            for (int i = 0; i < table.GetLength(0); i++)
            {
                string key = table[i, 0];
                if (byKey.ContainsKey(key) || !byKey.TryGetValue(table[i, 1], out PoseClip source)) continue;
                PoseClip copy = CopyClip(source, key);
                clips.Add(copy);
                byKey[key] = copy;
            }
        }

        // A deep copy under another key (the keyframes are copied too, so editing one clip never changes the other).
        static PoseClip CopyClip(PoseClip source, string key)
        {
            var keys = new PoseKeyframe[source.Keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                PoseKeyframe k = source.Keys[i];
                keys[i] = k == null ? null : new PoseKeyframe { Phase = k.Phase, At = k.At, Ease = k.Ease, Pose = k.Pose != null ? k.Pose.Clone() : null };
            }
            return new PoseClip
            {
                Key = key, Mode = source.Mode, LoopPeriod = source.LoopPeriod, DefaultDuration = source.DefaultDuration,
                StartupShare = source.StartupShare, ActiveShare = source.ActiveShare, FadeIn = source.FadeIn,
                UpperBodyOnly = source.UpperBodyOnly, Aims = source.Aims, ArmSwing = source.ArmSwing, Keys = keys
            };
        }
    }
}
