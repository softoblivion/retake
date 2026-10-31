using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Retake.Core
{
    /// <summary>Frame-count rules of one frame rate.</summary>
    public sealed class TimecodeBase : IEquatable<TimecodeBase>
    {
        public TimecodeBase(double fps, int baseRate, bool drop, int digits, string separator)
        {
            Fps = fps;
            Base = baseRate;
            Drop = drop;
            Digits = digits;
            Separator = separator;
        }

        /// <summary>Exact rate used for frame / seconds conversion (23.976 is 24000/1001, 29.97 is 30000/1001, 47.952 is 48000/1001, 59.94 is 60000/1001, 119.88 is 120000/1001).</summary>
        public double Fps { get; private set; }

        /// <summary>Frames per timecode second.</summary>
        public int Base { get; private set; }

        /// <summary>True only for 29.97 and 59.94.</summary>
        public bool Drop { get; private set; }

        /// <summary>Frame digits: 2, or 3 when <see cref="Base"/> is 100 or more.</summary>
        public int Digits { get; private set; }

        /// <summary><c>:</c> for non-drop, <c>;</c> for drop-frame.</summary>
        public string Separator { get; private set; }

        /// <summary>Frame numbers skipped at the start of a non-tenth minute (0 for non-drop bases).</summary>
        public int DropPerMinute { get { return Drop ? Base / 15 : 0; } }

        public bool Equals(TimecodeBase? other)
        {
            return other != null && Fps == other.Fps && Base == other.Base && Drop == other.Drop
                && Digits == other.Digits && Separator == other.Separator;
        }

        public override bool Equals(object? obj) { return Equals(obj as TimecodeBase); }

        public override int GetHashCode() { return Fps.GetHashCode() ^ (Base * 397) ^ (Drop ? 1 : 0); }
    }

    /// <summary>
    /// Frame, seconds and timecode conversion; the only class that does frame math. Timecodes follow the After Effects
    /// display: <c>h:mm:ss:ff</c>, drop-frame (<c>;</c>) at 29.97 and 59.94.
    /// </summary>
    public static class Timecode
    {
        /// <summary>The frame rates a rate within 0.5 % of is snapped to.</summary>
        public static readonly double[] NominalRates = { 23.976, 24.0, 25.0, 29.97, 30.0, 47.952, 48.0, 50.0, 59.94, 60.0, 100.0, 119.88, 120.0 };

        /// <summary>The 1001-based rates of <see cref="NominalRates"/>: <c>n * 1000 / 1001</c> for n = 24, 30, 48, 60, 120.</summary>
        public static readonly double[] RationalRates = { 23.976, 29.97, 47.952, 59.94, 119.88 };

        private const double SnapTolerance = 0.005;

        /// <summary>A rate this close (relative) to <c>n * 1000 / 1001</c> of a table rate n that has no 1001-based entry keeps its own value.</summary>
        private const double RationalTolerance = 0.0002;

        private static readonly Regex TimecodePattern = new Regex(@"\A[0-9]+(?:[:;][0-9]+){1,3}\z", RegexOptions.CultureInvariant);
        private static readonly Regex FpsPattern = new Regex(@"\A(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\z", RegexOptions.CultureInvariant);

        private static double ExactRate(double nominal)
        {
            foreach (double rate in RationalRates)
            {
                if (nominal == rate) return Math.Round(rate * 1.001) * 1000.0 / 1001.0;
            }
            return nominal;
        }

        /// <summary>
        /// Snaps <paramref name="fps"/> to the nearest entry of <see cref="NominalRates"/> when within 0.5 %, else returns it
        /// unchanged. A rate that lies within 0.02 % of <c>n * 1000 / 1001</c> for a table rate n without a 1001-based entry
        /// (25, 50, 100: 24.975, 49.95, 99.9) is a rate of its own and is returned unchanged. Throws
        /// <see cref="ArgumentException"/> for a value that is not a positive finite number.
        /// </summary>
        public static double NominalRate(double fps)
        {
            if (double.IsNaN(fps) || double.IsInfinity(fps) || fps <= 0) throw new ArgumentException("The frame rate must be a positive number");
            double best = 0;
            double bestDistance = -1;
            foreach (double rate in NominalRates)
            {
                double distance = Math.Abs(fps - rate) / rate;
                if (distance <= SnapTolerance && (bestDistance < 0 || distance < bestDistance))
                {
                    best = rate;
                    bestDistance = distance;
                }
            }
            if (bestDistance < 0) return fps;
            if (Array.IndexOf(RationalRates, best) < 0 && !HasRationalEntry(best))
            {
                double rational = best * 1000.0 / 1001.0;
                if (Math.Abs(fps - rational) / rational <= RationalTolerance) return fps;
            }
            return best;
        }

        /// <summary>True when the table holds the 1001-based rate of the integer rate <paramref name="rate"/>.</summary>
        private static bool HasRationalEntry(double rate)
        {
            foreach (double r in RationalRates)
            {
                if (Math.Round(r * 1.001) == rate) return true;
            }
            return false;
        }

        /// <summary>Builds the <see cref="TimecodeBase"/> of a frame rate (snapped with <see cref="NominalRate"/>).</summary>
        public static TimecodeBase BaseFor(double fps)
        {
            double nominal = NominalRate(fps);
            double exact = ExactRate(nominal);
            int baseRate = Math.Max(1, (int)Math.Floor(nominal + 0.5));
            bool drop = nominal == 29.97 || nominal == 59.94;
            int digits = Math.Max(2, (baseRate - 1).ToString(CultureInfo.InvariantCulture).Length);
            return new TimecodeBase(exact, baseRate, drop, digits, drop ? ";" : ":");
        }

        /// <summary>Formats a zero-based frame number as <c>h:mm:ss:ff</c> (<c>;</c> separators for drop-frame). Throws <see cref="ArgumentOutOfRangeException"/> for a negative frame.</summary>
        public static string FrameToTimecode(int frame, TimecodeBase tb)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException("frame", "The frame number cannot be negative");
            long shown = frame;
            long d = tb.DropPerMinute;
            if (d > 0)
            {
                long perMinute = (long)tb.Base * 60 - d;
                long perTenMinutes = perMinute * 10 + d;
                long q = shown / perTenMinutes;
                long m = shown % perTenMinutes;
                shown = shown + 9 * d * q + (m > d ? d * ((m - d) / perMinute) : 0);
            }
            long ff = shown % tb.Base;
            long totalSeconds = shown / tb.Base;
            long ss = totalSeconds % 60;
            long mm = (totalSeconds / 60) % 60;
            long hh = totalSeconds / 3600;
            string sep = tb.Separator;
            return hh.ToString(CultureInfo.InvariantCulture) + sep + mm.ToString("D2", CultureInfo.InvariantCulture) + sep
                + ss.ToString("D2", CultureInfo.InvariantCulture) + sep + ff.ToString("D" + tb.Digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        /// <summary>Parses <c>h:mm:ss:ff</c>, <c>mm:ss:ff</c> or <c>ss:ff</c> (either separator) into a zero-based frame number. Throws <see cref="FormatException"/> for anything else, for a frame part not below the base, and for a dropped frame number.</summary>
        public static int TimecodeToFrame(string text, TimecodeBase tb)
        {
            if (text == null) throw new FormatException("The timecode must be text");
            string cleaned = Py.Strip(text);
            if (!TimecodePattern.IsMatch(cleaned)) throw new FormatException("Not a timecode: '" + text + "'");
            string[] pieces = cleaned.Split(':', ';');
            long[] numbers = new long[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                if (!long.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    throw new FormatException("Not a timecode: '" + text + "'");
            }
            long ff = numbers[numbers.Length - 1];
            long ss = numbers[numbers.Length - 2];
            long mm = numbers.Length >= 3 ? numbers[numbers.Length - 3] : 0;
            long hh = numbers.Length == 4 ? numbers[numbers.Length - 4] : 0;
            if (ff >= tb.Base) throw new FormatException("The frame part " + ff + " is not below " + tb.Base);
            if (ss >= 60 || mm >= 60) throw new FormatException("Minutes and seconds must be below 60: '" + text + "'");
            long d = tb.DropPerMinute;
            if (d > 0 && ss == 0 && mm % 10 != 0 && ff < d) throw new FormatException("'" + text + "' is a dropped frame number");
            if (hh > 100000000L) throw new FormatException("The hour part is too large: '" + text + "'");
            long frame = (hh * 3600 + mm * 60 + ss) * tb.Base + ff;
            if (d > 0)
            {
                long totalMinutes = hh * 60 + mm;
                frame -= d * (totalMinutes - totalMinutes / 10);
            }
            if (frame > int.MaxValue) throw new FormatException("The timecode is too large: '" + text + "'");
            return (int)frame;
        }

        /// <summary>Frame shown at <paramref name="seconds"/>: <c>floor(seconds * fps + 1e-4)</c>, never negative.</summary>
        public static int SecondsToFrame(double seconds, double fps)
        {
            double value = Math.Floor(seconds * fps + 1e-4);
            if (value < 0) return 0;
            if (value > int.MaxValue) return int.MaxValue;
            return (int)value;
        }

        /// <summary>Centre of a frame in seconds, used for seeking: <c>(frame + 0.5) / fps</c>.</summary>
        public static double FrameToSeconds(int frame, double fps)
        {
            return (frame + 0.5) / fps;
        }

        /// <summary>Frame count of a duration: <c>round(seconds * fps)</c> with ties to even.</summary>
        public static int DurationFrames(double seconds, double fps)
        {
            double value = Math.Round(seconds * fps, MidpointRounding.ToEven);
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)value;
        }

        /// <summary>Text form of a frame rate: up to three decimals, trailing zeros cut (<c>25</c>, <c>29.97</c>, <c>23.976</c>).</summary>
        public static string FormatFps(double fps)
        {
            string text = Py.Fixed3(fps);
            if (text.IndexOf('.') >= 0) text = text.TrimEnd('0').TrimEnd('.');
            return text;
        }

        /// <summary>Inverse of <see cref="FormatFps"/>. Throws <see cref="FormatException"/> for junk or a non-positive value.</summary>
        public static double ParseFps(string text)
        {
            if (text == null) throw new FormatException("The frame rate must be text");
            string cleaned = Py.Strip(text);
            if (!FpsPattern.IsMatch(cleaned)) throw new FormatException("Not a frame rate: '" + text + "'");
            double value;
            if (!double.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                || double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new FormatException("Not a frame rate: '" + text + "'");
            return value;
        }
    }
}
