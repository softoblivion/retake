using System;
using System.Globalization;
using System.IO;
using Retake.Core;

namespace Retake.Design
{
    /// <summary>
    /// The settings the design layer reads: theme, Scale step, automatic update downloads and the author name.
    /// Over a <see cref="Settings"/> object it is a view of the core's settings file: the values start from the
    /// file and every change after <see cref="StartSaving"/> is written back through the core (atomic replace).
    /// Without one (the gallery) it lives in memory. Values set before <see cref="StartSaving"/> (the command
    /// line, a Scale fallback while the window is placed) last for the run only. <see cref="Changed"/> names the
    /// settings key that changed.
    /// </summary>
    public sealed class Prefs
    {
        readonly Settings? settings;
        string theme = "black", authorName = "";
        int scale = 100;
        bool autoDownload = true, saving;

        public event Action<string>? Changed;

        public Prefs() { }

        public Prefs(Settings settings)
        {
            this.settings = settings;
            theme = settings.Theme;
            scale = Scale.Normalize(settings.UiScale);
            autoDownload = settings.AutoDownloadUpdates;
            authorName = settings.Author;
        }

        /// <summary>From now on changes are saved to the settings file.</summary>
        public void StartSaving() => saving = true;

        public string Theme { get => theme; set { if (theme == value) return; theme = value; Save("theme"); } }
        public int UiScale { get => scale; set { int v = Scale.Normalize(value); if (scale == v) return; scale = v; Save("ui_scale"); } }
        public bool AutoDownloadUpdates { get => autoDownload; set { if (autoDownload == value) return; autoDownload = value; Save("auto_download_updates"); } }
        public string AuthorName { get => authorName; set { value ??= ""; if (authorName == value) return; authorName = value; Save("author"); } }

        void Save(string key)
        {
            if (settings != null && saving)
            {
                switch (key)
                {
                    case "theme": if (Array.IndexOf(Settings.Themes, theme) >= 0) settings.Theme = theme; break;
                    case "ui_scale": settings.UiScale = scale; break;
                    case "auto_download_updates": settings.AutoDownloadUpdates = autoDownload; break;
                    case "author": settings.Author = authorName; break;
                }
                try { settings.Save(); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log.Warning("settings were not saved: " + ex.Message); }
            }
            Changed?.Invoke(key);
        }
    }

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
