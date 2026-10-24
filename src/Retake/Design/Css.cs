using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Retake.Design
{
    /// <summary>One entry of a CSS box-shadow list, in device-independent pixels.</summary>
    public sealed class ShadowSpec
    {
        public double X, Y, Blur, Spread;
        public Color Color;
        public bool Inset;

        /// <summary>A ring: no offset, no blur, only a spread (drawn as an outline, not a shadow).</summary>
        public bool IsRing => X == 0 && Y == 0 && Blur == 0 && Spread > 0;
    }

    /// <summary>
    /// Reads the CSS values of the theme file into WPF values: colours (#rgb, #rrggbb, rgb(), rgba(),
    /// transparent), "r, g, b" tones, linear-gradient(angle, stops), radial-gradient(size at position,
    /// stops), comma-separated gradient layers, and box-shadow lists. Everything returned is frozen.
    /// </summary>
    public static class Css
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool TryColor(string text, out Color color)
        {
            string s = text.Trim();
            color = Colors.Transparent;
            if (s == "transparent") return true;
            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                string h = s.Substring(1);
                if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
                if (h.Length != 6 && h.Length != 8) return false;
                if (!uint.TryParse(h, NumberStyles.HexNumber, Inv, out uint v)) return false;
                if (h.Length == 6) color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
                else color = Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));
                return true;
            }
            if (s.StartsWith("rgb", StringComparison.Ordinal))
            {
                int open = s.IndexOf('('), close = s.LastIndexOf(')');
                if (open < 0 || close < open) return false;
                string[] parts = s.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length < 3) return false;
                if (!Num(parts[0], out double r) || !Num(parts[1], out double g) || !Num(parts[2], out double b)) return false;
                double a = 1;
                if (parts.Length > 3 && !Num(parts[3], out a)) return false;
                color = Color.FromArgb(Byte(a * 255), Byte(r), Byte(g), Byte(b));
                return true;
            }
            return false;
        }

        /// <summary>A tone is three channel values without a function: "255, 255, 255".</summary>
        public static bool TryTone(string text, out Color color)
        {
            color = Colors.Transparent;
            string[] parts = text.Split(',');
            if (parts.Length != 3) return false;
            if (!Num(parts[0], out double r) || !Num(parts[1], out double g) || !Num(parts[2], out double b)) return false;
            color = Color.FromRgb(Byte(r), Byte(g), Byte(b));
            return true;
        }

        /// <summary>A brush for a colour, a gradient or a list of gradient layers; null when the value is none of these.</summary>
        public static Brush? TryBrush(string text)
        {
            string s = text.Trim();
            if (TryColor(s, out Color c)) return Freeze(new SolidColorBrush(c));
            List<string> layers = SplitTop(s, ',');
            if (layers.Count > 1 && layers.TrueForAll(IsGradient))
            {
                var group = new DrawingGroup();
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    Brush? layer = TryGradient(layers[i]);
                    if (layer == null) return null;
                    group.Children.Add(new GeometryDrawing(layer, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
                }
                return Freeze(new DrawingBrush(group) { Stretch = Stretch.Fill });
            }
            return TryGradient(s);
        }

        static bool IsGradient(string s) => s.TrimStart().StartsWith("linear-gradient(", StringComparison.Ordinal) || s.TrimStart().StartsWith("radial-gradient(", StringComparison.Ordinal);

        static Brush? TryGradient(string text)
        {
            string s = text.Trim();
            bool linear = s.StartsWith("linear-gradient(", StringComparison.Ordinal);
            bool radial = s.StartsWith("radial-gradient(", StringComparison.Ordinal);
            if (!linear && !radial || !s.EndsWith(")", StringComparison.Ordinal)) return null;
            int open = s.IndexOf('(');
            List<string> parts = SplitTop(s.Substring(open + 1, s.Length - open - 2), ',');
            if (parts.Count < 2) return null;
            string first = parts[0].Trim();
            bool firstIsShape = linear ? (first.EndsWith("deg", StringComparison.Ordinal) || first.StartsWith("to ", StringComparison.Ordinal)) : first.Contains(" at ") || first.StartsWith("at ", StringComparison.Ordinal) || first.StartsWith("circle", StringComparison.Ordinal) || first.StartsWith("ellipse", StringComparison.Ordinal);
            GradientStopCollection? stops = Stops(parts, firstIsShape ? 1 : 0);
            if (stops == null) return null;
            if (linear)
            {
                double angle = 180;
                if (firstIsShape)
                {
                    if (first == "to bottom") angle = 180;
                    else if (first == "to top") angle = 0;
                    else if (first == "to right") angle = 90;
                    else if (first == "to left") angle = 270;
                    else if (!Num(first.Substring(0, first.Length - 3), out angle)) return null;
                }
                // CSS angles: 0deg points up, clockwise. The line runs through the centre; scaling the direction
                // so that |dx| + |dy| = 1 makes the end points the corners for a square box, as in CSS.
                double rad = angle * Math.PI / 180, dx = Math.Sin(rad), dy = -Math.Cos(rad), k = Math.Abs(dx) + Math.Abs(dy);
                dx /= k; dy /= k;
                return Freeze(new LinearGradientBrush(stops, new Point(0.5 - dx / 2, 0.5 - dy / 2), new Point(0.5 + dx / 2, 0.5 + dy / 2)));
            }
            // Radial: "<rx>% <ry>% at <x>% <y>%"; radii relative to the box width and height.
            double rx = 0.5, ry = 0.5, cx = 0.5, cy = 0.5;
            if (firstIsShape)
            {
                string[] halves = first.Split(new[] { " at " }, StringSplitOptions.None);
                string[] size = halves[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (size.Length == 2 && Percent(size[0], out double a) && Percent(size[1], out double b)) { rx = a; ry = b; }
                if (halves.Length > 1)
                {
                    string[] at = halves[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (at.Length == 2 && Percent(at[0], out double x) && Percent(at[1], out double y)) { cx = x; cy = y; }
                }
            }
            return Freeze(new RadialGradientBrush(stops) { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = rx, RadiusY = ry });
        }

        static GradientStopCollection? Stops(List<string> parts, int from)
        {
            int n = parts.Count - from;
            var colors = new Color[n];
            var offsets = new double?[n];
            var clear = new bool[n];
            for (int i = 0; i < n; i++)
            {
                string p = parts[from + i].Trim();
                string colorText = p, rest = "";
                int close = p.IndexOf(')');
                if (close > 0) { colorText = p.Substring(0, close + 1); rest = p.Substring(close + 1).Trim(); }
                else
                {
                    int sp = p.IndexOf(' ');
                    if (sp > 0) { colorText = p.Substring(0, sp); rest = p.Substring(sp + 1).Trim(); }
                }
                if (!TryColor(colorText, out colors[i])) return null;
                clear[i] = colorText.Trim() == "transparent";
                if (rest.Length > 0)
                {
                    if (!Percent(rest, out double o)) return null;
                    offsets[i] = o;
                }
            }
            if (!offsets[0].HasValue) offsets[0] = 0;
            if (!offsets[n - 1].HasValue) offsets[n - 1] = 1;
            for (int i = 1; i < n - 1; i++)
            {
                if (offsets[i].HasValue) continue;
                int j = i + 1;
                while (!offsets[j].HasValue) j++;
                double a = offsets[i - 1]!.Value, b = offsets[j]!.Value;
                offsets[i] = a + (b - a) / (j - i + 1);
            }
            // CSS interpolates in premultiplied space, so "transparent" next to a colour fades that colour out;
            // WPF does not, so a transparent stop takes the RGB of its neighbour with alpha 0.
            for (int i = 0; i < n; i++)
            {
                if (!clear[i]) continue;
                int k = i + 1 < n && !clear[i + 1] ? i + 1 : i - 1;
                if (k >= 0 && k < n) colors[i] = Color.FromArgb(0, colors[k].R, colors[k].G, colors[k].B);
            }
            var stops = new GradientStopCollection();
            for (int i = 0; i < n; i++) stops.Add(new GradientStop(colors[i], offsets[i]!.Value));
            return stops;
        }

        /// <summary>A box-shadow list; "none", "0 0 0 0 transparent" and empty entries give an empty array.</summary>
        public static ShadowSpec[]? TryShadows(string text)
        {
            var list = new List<ShadowSpec>();
            foreach (string part in SplitTop(text, ','))
            {
                string p = part.Trim();
                if (p.Length == 0 || p == "none") continue;
                var spec = new ShadowSpec();
                if (p.StartsWith("inset ", StringComparison.Ordinal)) { spec.Inset = true; p = p.Substring(6).Trim(); }
                int colorAt = p.IndexOf("rgb", StringComparison.Ordinal);
                if (colorAt < 0) colorAt = p.IndexOf('#');
                if (colorAt < 0) colorAt = p.IndexOf("transparent", StringComparison.Ordinal);
                if (colorAt < 0) return null;
                if (!TryColor(p.Substring(colorAt), out spec.Color)) return null;
                string[] lengths = p.Substring(0, colorAt).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (lengths.Length < 2 || lengths.Length > 4) return null;
                var v = new double[4];
                for (int i = 0; i < lengths.Length; i++)
                    if (!Num(lengths[i].Replace("px", ""), out v[i])) return null;
                spec.X = v[0]; spec.Y = v[1]; spec.Blur = v[2]; spec.Spread = v[3];
                if (spec.Color.A == 0) continue;
                list.Add(spec);
            }
            return list.ToArray();
        }

        /// <summary>Splits at the separator outside parentheses.</summary>
        public static List<string> SplitTop(string s, char sep)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                else if (s[i] == sep && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(s.Substring(start));
            return parts;
        }

        public static bool Num(string s, out double v) => double.TryParse(s.Trim(), NumberStyles.Float, Inv, out v);

        static bool Percent(string s, out double v)
        {
            v = 0;
            s = s.Trim();
            if (!s.EndsWith("%", StringComparison.Ordinal)) return false;
            if (!Num(s.Substring(0, s.Length - 1), out double p)) return false;
            v = p / 100;
            return true;
        }

        static byte Byte(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

        static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }
    }
}
