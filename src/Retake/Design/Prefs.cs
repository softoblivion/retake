using System;
using System.Globalization;

namespace Retake.Design
{
    /// <summary>
    /// The settings the design layer reads, held in memory: theme, Scale step, automatic update downloads and the
    /// author name. Nothing is written to disk here; the core's settings file takes over persistence later and
    /// fills this object at start. <see cref="Changed"/> names the key that changed.
    /// </summary>
    public sealed class Prefs
    {
        string theme = "black", authorName = "";
        int scale = 100;
        bool autoDownload = true;

        public event Action<string>? Changed;

        public string Theme { get => theme; set { if (theme == value) return; theme = value; Changed?.Invoke("theme"); } }
        public int UiScale { get => scale; set { int v = Scale.Normalize(value); if (scale == v) return; scale = v; Changed?.Invoke("ui_scale"); } }
        public bool AutoDownloadUpdates { get => autoDownload; set { if (autoDownload == value) return; autoDownload = value; Changed?.Invoke("auto_download_updates"); } }
        public string AuthorName { get => authorName; set { if (authorName == value) return; authorName = value ?? ""; Changed?.Invoke("author_name"); } }
    }

    /// <summary>
    /// The command line of the application: --theme &lt;black|sky&gt; and --scale &lt;n&gt; override the settings for
    /// the run; --gallery opens the control gallery; --capture &lt;png&gt; renders the gallery to a file and exits;
    /// --diag &lt;txt&gt; writes a font and theme check beside a capture.
    /// </summary>
    public sealed class LaunchOptions
    {
        public readonly Prefs Prefs = new Prefs();
        public bool Gallery;
        public string? Capture, Diag;

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string? next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--theme" when next != null: o.Prefs.Theme = next; i++; break;
                    case "--scale" when next != null:
                        if (int.TryParse(next.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)) o.Prefs.UiScale = s;
                        i++;
                        break;
                    case "--gallery": o.Gallery = true; break;
                    case "--capture" when next != null: o.Capture = next; o.Gallery = true; i++; break;
                    case "--diag" when next != null: o.Diag = next; i++; break;
                }
            }
            return o;
        }
    }
}
