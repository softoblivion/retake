using System;
using System.Globalization;
using Retake.Core;
using Retake.Design;

namespace Retake
{
    /// <summary>
    /// The command line of the application: an optional video path (the first argument that is not an option);
    /// --theme &lt;black|sky&gt; and --scale &lt;n&gt; override the settings for the run; --fake-player uses generated
    /// frames instead of libmpv; --gallery opens the control gallery; --capture &lt;png&gt; renders the gallery to a
    /// file and exits; --diag &lt;txt&gt; writes a font and theme check beside a capture.
    /// </summary>
    public sealed class LaunchOptions
    {
        public Prefs Prefs { get; private set; } = new Prefs();
        public bool Gallery, FakePlayer;
        public string? Capture, Diag, Video, Theme;
        public int? UiScale;

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string? next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--theme" when next != null: o.Theme = next; i++; break;
                    case "--scale" when next != null:
                        if (int.TryParse(next.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) o.UiScale = s;
                        i++;
                        break;
                    case "--gallery": o.Gallery = true; break;
                    case "--fake-player": o.FakePlayer = true; break;
                    case "--capture" when next != null: o.Capture = next; o.Gallery = true; i++; break;
                    case "--diag" when next != null: o.Diag = next; i++; break;
                    default:
                        if (!a.StartsWith("--", StringComparison.Ordinal) && o.Video == null) o.Video = a;
                        break;
                }
            }
            o.ApplyOverrides();
            return o;
        }

        /// <summary>Makes the preferences a view of the settings file, keeping the command-line overrides for the run.</summary>
        public void UseSettings(Settings settings)
        {
            Prefs = new Prefs(settings);
            ApplyOverrides();
        }

        void ApplyOverrides()
        {
            if (Theme != null) Prefs.Theme = Theme;
            if (UiScale.HasValue) Prefs.UiScale = UiScale.Value;
        }
    }
}
