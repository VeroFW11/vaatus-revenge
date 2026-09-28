using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VaatusRevenge.CombatSim
{
    // Markdown tables and headings to stdout (and optionally a results file), so outputs paste into the report.
    public static class Out
    {
        static TextWriter extra;

        public static void Tee(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            extra = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        }

        public static void Close()
        {
            extra?.Dispose();
            extra = null;
        }

        public static void Line(string s = "")
        {
            Console.WriteLine(s);
            extra?.WriteLine(s);
        }

        public static void Heading(string s)
        {
            Line();
            Line("## " + s);
            Line();
        }

        public static void Sub(string s)
        {
            Line();
            Line("### " + s);
            Line();
        }

        public static string N(double v, int decimals = 2)
        {
            if (double.IsNaN(v)) return "-";
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public static string Pct(double v)
        {
            return (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        public static string Frames(double seconds, double dt = 1.0 / 60.0)
        {
            return N(seconds, 3) + " s (" + Math.Round(seconds / dt).ToString(CultureInfo.InvariantCulture) + " f)";
        }
    }

    public sealed class Table
    {
        readonly string[] headers;
        readonly List<string[]> rows = new List<string[]>();

        public Table(params string[] headers)
        {
            this.headers = headers;
        }

        public Table Row(params object[] cells)
        {
            rows.Add(cells.Select(c => c == null ? "" : Convert.ToString(c, CultureInfo.InvariantCulture)).ToArray());
            return this;
        }

        public void Print()
        {
            Out.Line("| " + string.Join(" | ", headers) + " |");
            Out.Line("|" + string.Join("|", headers.Select(_ => "---")) + "|");
            foreach (var r in rows) Out.Line("| " + string.Join(" | ", r) + " |");
            Out.Line();
        }
    }

    public static class Stats
    {
        public static double Mean(IEnumerable<double> xs)
        {
            var list = xs.ToList();
            return list.Count == 0 ? double.NaN : list.Average();
        }

        public static double Percentile(IEnumerable<double> xs, double p)
        {
            var list = xs.OrderBy(x => x).ToList();
            if (list.Count == 0) return double.NaN;
            double idx = p * (list.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            return list[lo] + (list[hi] - list[lo]) * (idx - lo);
        }
    }
}
