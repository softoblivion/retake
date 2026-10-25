using System;
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
}
