using System.Globalization;

namespace VaatusRevenge
{
    // A number on the HUD, such as "x1.24" or "72 / 100", whose string is only rebuilt when the value shown
    // actually changes. OnGUI runs every frame, and building a new string every frame creates garbage that
    // the garbage collector has to clean up later, which shows up as small hitches while playing.
    // Numbers are always written the same way (1.24, never 1,24), whatever the PC's language settings.
    // Use one form of Get per instance: either single values or value / max pairs.
    public sealed class HudNumberText
    {
        const long Limit = 1000000000L; // keeps the pair key below from overflowing on absurd values
        const long PairFactor = 2 * Limit + 1;

        readonly string prefix;
        readonly string format;
        readonly float step;
        readonly string suffix;
        readonly string separator;
        long lastKey = long.MinValue;
        string text = "";

        // step: the smallest change worth showing (0.01 for "x1.24", 1 for whole numbers).
        // format: a .NET number format for the value, e.g. "0.00" or "0".
        // separator: goes between the two numbers of Get(value, max).
        public HudNumberText(string prefix, string format, float step, string suffix = "", string separator = " / ")
        {
            this.prefix = prefix ?? "";
            this.format = string.IsNullOrEmpty(format) ? "0" : format;
            this.step = step > 0f ? step : 1f;
            this.suffix = suffix ?? "";
            this.separator = separator ?? "";
        }

        public string Get(float value)
        {
            long key = Quantize(value);
            if (key == lastKey) return text;
            lastKey = key;
            text = prefix + Format(key) + suffix;
            return text;
        }

        public string Get(float value, float max)
        {
            long a = Quantize(value);
            long b = Quantize(max);
            long key = a * PairFactor + b;
            if (key == lastKey) return text;
            lastKey = key;
            text = prefix + Format(a) + separator + Format(b) + suffix;
            return text;
        }

        long Quantize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0L;
            double steps = System.Math.Round(value / (double)step);
            if (steps > Limit) return Limit;
            if (steps < -Limit) return -Limit;
            return (long)steps;
        }

        string Format(long steps)
        {
            return (steps * (double)step).ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
