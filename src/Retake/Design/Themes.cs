using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Retake.Core;

namespace Retake.Design
{
    /// <summary>One theme of themes.json with its effective token values (overrides over derived over tokens).</summary>
    public sealed class ThemeSpec
    {
        public string Name = "", Label = "", Scheme = "dark";
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Parsed values by token name without the leading dashes: Brush, Color (tones), double or ShadowSpec[].</summary>
        public readonly Dictionary<string, object> Parsed = new Dictionary<string, object>(StringComparer.Ordinal);
        /// <summary>Tokens whose value has no WPF reading (kept as text, never used).</summary>
        public readonly List<string> Unread = new List<string>();
        public bool IsDark => Scheme != "light";
    }

    /// <summary>An element that rebuilds parts of itself (not only repaints) when the theme changes.</summary>
    public interface IThemed
    {
        void OnThemeChanged();
    }

    /// <summary>
    /// The theme registry. Reads the embedded themes.json once; <see cref="Apply"/> registers every token of the
    /// chosen theme on the application resources under "T.&lt;token&gt;" (the CSS name without "--"): colours and
    /// gradients as frozen brushes (plus "T.&lt;token&gt;.Color" for colours), numbers as double, shadow lists as
    /// ShadowSpec[], "r, g, b" tones as Color. Elements that reference the keys with DynamicResource retint at
    /// once; elements that draw in OnRender register through <see cref="Track"/> and repaint.
    /// </summary>
    public static class Themes
    {
        static readonly List<ThemeSpec> all = new List<ThemeSpec>();
        static readonly List<WeakReference> tracked = new List<WeakReference>();
        static ResourceDictionary? target;

        public static IReadOnlyList<ThemeSpec> All => all;
        public static string DefaultName { get; private set; } = "black";
        public static ThemeSpec Current { get; private set; } = new ThemeSpec();
        public static event Action? Changed;

        public static void Load()
        {
            if (all.Count > 0) return;
            using (Stream? stream = typeof(Themes).Assembly.GetManifestResourceStream("Retake.Design.themes.json"))
            {
                if (stream == null) throw new InvalidOperationException("themes.json is not embedded");
                using (var reader = new StreamReader(stream)) Load(reader.ReadToEnd());
            }
        }

        static void Load(string json)
        {
            var root = (JsonObject)Retake.Core.Json.Parse(json)!;
            if (root.TryGetValue("default", out object? d) && d is string name) DefaultName = name;
            foreach (object? item in (List<object?>)root["themes"]!)
            {
                var t = (JsonObject)item!;
                var spec = new ThemeSpec { Name = (string)t["name"]!, Label = (string)t["label"]!, Scheme = t.TryGetValue("scheme", out object? s) && s is string sc ? sc : "dark" };
                foreach (string layer in new[] { "tokens", "derived", "overrides" })
                    if (t.TryGetValue(layer, out object? block) && block is JsonObject map)
                        foreach (KeyValuePair<string, object?> kv in map) spec.Values[kv.Key] = Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                foreach (KeyValuePair<string, string> kv in spec.Values)
                {
                    string key = kv.Key.TrimStart('-');
                    object? value = Read(Resolve(spec, kv.Value, 0));
                    if (value == null) spec.Unread.Add(key);
                    else spec.Parsed[key] = value;
                }
                all.Add(spec);
            }
            Current = ByName(DefaultName);
        }

        static string Resolve(ThemeSpec spec, string value, int depth)
        {
            string v = value.Trim();
            if (depth > 8 || !v.StartsWith("var(", StringComparison.Ordinal) || !v.EndsWith(")", StringComparison.Ordinal)) return v;
            string name = v.Substring(4, v.Length - 5).Trim();
            return spec.Values.TryGetValue(name, out string? next) ? Resolve(spec, next, depth + 1) : v;
        }

        static object? Read(string v)
        {
            if (Css.Num(v, out double number) && !v.Contains(",")) return number;
            if (Css.TryTone(v, out Color tone)) return tone;
            Brush? brush = Css.TryBrush(v);
            if (brush != null) return brush;
            ShadowSpec[]? shadows = Css.TryShadows(v);
            return shadows;
        }

        public static ThemeSpec ByName(string? name)
        {
            foreach (ThemeSpec t in all) if (t.Name == name) return t;
            foreach (ThemeSpec t in all) if (t.Name == DefaultName) return t;
            return all.Count > 0 ? all[0] : new ThemeSpec();
        }

        public static bool IsKnown(string? name) => all.Exists(t => t.Name == name);

        /// <summary>Makes the named theme current and registers its tokens on the resource dictionary (the application's).</summary>
        public static void Apply(string name, ResourceDictionary resources)
        {
            Current = ByName(name);
            target = resources;
            foreach (KeyValuePair<string, object> kv in Current.Parsed)
            {
                resources["T." + kv.Key] = kv.Value;
                if (kv.Value is SolidColorBrush b) resources["T." + kv.Key + ".Color"] = b.Color;
            }
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                if (!(tracked[i].Target is FrameworkElement el)) { tracked.RemoveAt(i); continue; }
                if (el is IThemed themed) themed.OnThemeChanged();
                el.InvalidateVisual();
            }
            Changed?.Invoke();
        }

        public static void Apply(string name)
        {
            Apply(name, target ?? Application.Current.Resources);
        }

        /// <summary>Repaints (and, for IThemed, rebuilds) the element on every theme change; held weakly.</summary>
        public static void Track(FrameworkElement element)
        {
            tracked.Add(new WeakReference(element));
        }

        // Accessors on the current theme. Token names are given without the leading dashes ("ink-1").

        public static Brush Brush(string token) => Current.Parsed.TryGetValue(token, out object? v) && v is Brush b ? b : Brushes.Transparent;

        public static Color Color(string token)
        {
            if (!Current.Parsed.TryGetValue(token, out object? v)) return Colors.Transparent;
            if (v is SolidColorBrush b) return b.Color;
            if (v is Color c) return c;
            return Colors.Transparent;
        }

        public static double Number(string token, double fallback = 0) => Current.Parsed.TryGetValue(token, out object? v) && v is double d ? d : fallback;

        public static ShadowSpec[] Shadows(string token) => Current.Parsed.TryGetValue(token, out object? v) && v is ShadowSpec[] s ? s : new ShadowSpec[0];

        /// <summary>A tone token ("rim-tone", "shade-tone") as a colour with the given alpha (0..1).</summary>
        public static Color Tone(string token, double alpha) => WithAlpha(Color(token), alpha);

        public static Color WithAlpha(Color c, double alpha) => System.Windows.Media.Color.FromArgb((byte)Math.Max(0, Math.Min(255, Math.Round(alpha * 255))), c.R, c.G, c.B);

        /// <summary>The colour with its alpha multiplied.</summary>
        public static Color Fade(Color c, double k) => System.Windows.Media.Color.FromArgb((byte)Math.Max(0, Math.Min(255, Math.Round(c.A * k))), c.R, c.G, c.B);

        /// <summary>Straight interpolation of two colours, alpha included.</summary>
        public static Color Mix(Color a, Color b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            // A fully transparent end keeps the other end's RGB so the fade does not pass through black.
            if (a.A == 0) a = WithAlpha(b, 0);
            if (b.A == 0) b = WithAlpha(a, 0);
            return System.Windows.Media.Color.FromArgb(Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
        }

        static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);

        public static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
