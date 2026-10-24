using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Retake.Design
{
    /// <summary>One role of the type scale: face, weight, size and line height in device-independent pixels.</summary>
    public sealed class TypeRole
    {
        public readonly string Name;
        public readonly double Size, LineHeight;
        public readonly FontWeight Weight;
        public readonly bool Mono, Caps;

        public TypeRole(string name, int weight, double size, double lineHeight, bool mono = false, bool caps = false)
        {
            Name = name;
            Weight = FontWeight.FromOpenTypeWeight(weight);
            Size = size;
            LineHeight = Math.Round(size * lineHeight, 2);
            Mono = mono;
            Caps = caps;
        }

        public FontFamily Family => Mono ? Typography.MonoFamily : Typography.TextFamily;

        /// <summary>The text as the role shows it (the section role is written in capitals).</summary>
        public string Text(string text) => Caps ? text.ToUpper(CultureInfo.InvariantCulture) : text;

        public void Apply(TextBlock t)
        {
            t.FontFamily = Family;
            t.FontSize = Size;
            t.FontWeight = Weight;
            t.LineHeight = LineHeight;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        public void Apply(Control c)
        {
            c.FontFamily = Family;
            c.FontSize = Size;
            c.FontWeight = Weight;
        }
    }

    /// <summary>
    /// The type scale of the kit with fixed sizes (LIQUID_UI.md 3.1): seven roles plus the size variants
    /// label-s and mono-s. Sora for text, Geist Mono for timecodes, versions and fields, both embedded as static
    /// per-weight files (fonts\*.ttf); WPF picks the file by FontWeight. Every text of the window is one of these
    /// roles; nothing sets its own size. WPF has no letter-spacing, so the kit's tracking is left out.
    /// </summary>
    public static class Typography
    {
        public static readonly TypeRole
            Display = new TypeRole("display", 650, 20, 1.2),
            Title = new TypeRole("title", 650, 16, 1.25),
            Section = new TypeRole("section", 600, 11, 1.3, caps: true),
            Body = new TypeRole("body", 450, 13, 1.45),
            BodyMedium = new TypeRole("body-medium", 550, 13, 1.45),
            Label = new TypeRole("label", 550, 12.5, 1.3),
            LabelS = new TypeRole("label-s", 550, 11.5, 1.3),
            Caption = new TypeRole("caption", 450, 11.5, 1.35),
            Mono = new TypeRole("mono", 450, 12, 1.4, mono: true),
            MonoS = new TypeRole("mono-s", 450, 11, 1.3, mono: true);

        public static readonly TypeRole[] All = { Display, Title, Section, Body, BodyMedium, Label, LabelS, Caption, Mono, MonoS };

        static FontFamily? text, mono;

        // Created on first use, after the Application object exists (the pack:// scheme is registered by then).
        public static FontFamily TextFamily => text ??= new FontFamily(new Uri("pack://application:,,,/"), "./fonts/#Sora, Segoe UI");
        public static FontFamily MonoFamily => mono ??= new FontFamily(new Uri("pack://application:,,,/"), "./fonts/#Geist Mono, Cascadia Mono, Consolas");

        /// <summary>A text block in a role, its colour a theme token (resource reference, retints with the theme).</summary>
        public static TextBlock Text(string content, TypeRole role, string? ink = "ink-1")
        {
            var t = new TextBlock { Text = role.Text(content), VerticalAlignment = VerticalAlignment.Center };
            role.Apply(t);
            if (ink != null) t.SetResourceReference(TextBlock.ForegroundProperty, "T." + ink);
            return t;
        }

        /// <summary>
        /// A tooltip-style line: the text in one ink and a trailing key in brackets in a quieter ink,
        /// e.g. "Settings (Ctrl+,)".
        /// </summary>
        public static TextBlock KeyLine(string content, TypeRole role, string ink, string keyInk)
        {
            var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            role.Apply(t);
            int open = content.EndsWith(")", StringComparison.Ordinal) ? content.LastIndexOf(" (", StringComparison.Ordinal) : -1;
            var main = new Run(open > 0 ? content.Substring(0, open) : content);
            main.SetResourceReference(TextElement.ForegroundProperty, "T." + ink);
            t.Inlines.Add(main);
            if (open > 0)
            {
                var key = new Run(content.Substring(open));
                key.SetResourceReference(TextElement.ForegroundProperty, "T." + keyInk);
                t.Inlines.Add(key);
            }
            return t;
        }

        /// <summary>True when the embedded face itself (not a fallback) serves the weight.</summary>
        public static bool Embedded(FontFamily family, string name, FontWeight weight)
        {
            var face = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            if (!face.TryGetGlyphTypeface(out GlyphTypeface glyphs)) return false;
            foreach (string n in glyphs.FamilyNames.Values)
                if (n == name) return glyphs.FontUri.AbsoluteUri.StartsWith("pack://", StringComparison.OrdinalIgnoreCase) && glyphs.Weight == weight;
            return false;
        }

        public static string Describe(FontFamily family, FontWeight weight)
        {
            if (!new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out GlyphTypeface glyphs)) return "no glyph typeface";
            return glyphs.FontUri + " " + string.Join("/", new List<string>(glyphs.FamilyNames.Values).ToArray()) + " " + glyphs.Weight;
        }
    }
}
