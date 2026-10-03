using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // The body flash when switching into an element and the MIX accent ring (ElementVfxStyle.SwitchFlashColor takes its
    // default from here, so a test can check the four apart without Unity). Two rules: the four are about equally bright
    // (relative luminance ~0.6 on the plain RGB values, so no element's flash drowns another's), and their hues are at
    // least MinHueGap degrees apart, so each reads as its own element at a glance (round 7, S7-12: Fire and Earth were
    // 12 degrees apart, Water and Air 14). Each leans to its button's colour: B red Fire, X blue Water, A green Earth;
    // Air, whose Y is yellow, can't take yellow (it would sit on Fire's and Earth's), so it's a pale spirit lavender.
    public static class ElementSwitchPalette
    {
        public const float MinHueGap = 40f;

        public static readonly Vector3 Fire = new Vector3(1f, 0.5f, 0.18f);     // orange-red, hue ~23, luminance ~0.58
        public static readonly Vector3 Water = new Vector3(0.35f, 0.66f, 1f);   // blue, hue ~211, ~0.62
        public static readonly Vector3 Earth = new Vector3(0.66f, 0.7f, 0.22f); // olive green, hue ~65, ~0.66
        public static readonly Vector3 Air = new Vector3(0.68f, 0.56f, 0.92f);  // pale lavender, hue ~260, ~0.61

        public static Vector3 For(ElementId element)
        {
            switch (element)
            {
                case ElementId.Water: return Water;
                case ElementId.Earth: return Earth;
                case ElementId.Air: return Air;
                default: return Fire;
            }
        }

        // Hue in degrees (0..360) of a plain RGB colour.
        public static float Hue(Vector3 rgb)
        {
            float max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z)), min = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
            float range = max - min;
            if (range < 1e-5f) return 0f;
            float h;
            if (max == rgb.X) h = 60f * (((rgb.Y - rgb.Z) / range) % 6f);
            else if (max == rgb.Y) h = 60f * ((rgb.Z - rgb.X) / range + 2f);
            else h = 60f * ((rgb.X - rgb.Y) / range + 4f);
            return h < 0f ? h + 360f : h;
        }

        // Relative luminance (Rec. 709 weights) of the plain RGB values.
        public static float Luminance(Vector3 rgb)
        {
            return 0.2126f * rgb.X + 0.7152f * rgb.Y + 0.0722f * rgb.Z;
        }

        // The smaller angle between two hues, in degrees.
        public static float HueGap(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }
    }
}
