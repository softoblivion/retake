using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Retake.Core
{
    /// <summary>One entry of the recent list.</summary>
    public sealed class RecentEntry
    {
        public RecentEntry(string path, int lastFrame, long openedAt)
        {
            Path = path;
            LastFrame = lastFrame;
            OpenedAt = openedAt;
        }

        /// <summary>Absolute path of the video.</summary>
        public string Path { get; private set; }

        /// <summary>Last playback position, a zero-based frame number.</summary>
        public int LastFrame { get; private set; }

        /// <summary>Unix time in seconds when the video was last opened or closed.</summary>
        public long OpenedAt { get; private set; }
    }

    /// <summary>
    /// Persistent application settings (<c>settings.json</c> in the data folder). Owns the defaults, tolerant loading
    /// (a corrupt file is moved aside), the recent list and the per-video frame rate overrides. Keys this version does
    /// not know are kept and written back. Methods lock; the property values themselves are plain fields, so a caller that
    /// shares the object between threads reads and writes them from one thread or under its own lock.
    /// </summary>
    public sealed class Settings
    {
        /// <summary>Longest recent list.</summary>
        public const int MaxRecent = 20;

        /// <summary>The values of <see cref="UiScale"/>, in percent.</summary>
        public static readonly int[] UiScaleSteps = { 100, 125, 150, 175, 200 };

        /// <summary>The values of <see cref="Theme"/>.</summary>
        public static readonly string[] Themes = { "black", "sky" };

        private static readonly string[] RetiredKeys = { "update_check_interval" };
        private static readonly string[] RetiredWindowKeys = { "width", "height", "maximized" };
        private static readonly string[] KnownKeys =
        {
            "window", "last_dir", "recent", "fps_overrides", "auto_download_updates", "notes_panel_open", "ui_scale", "theme", "author"
        };

        private readonly object _lock = new object();
        private readonly List<RecentEntry> _recent = new List<RecentEntry>();
        private readonly List<KeyValuePair<string, double>> _overrides = new List<KeyValuePair<string, double>>();
        private JsonObject _windowExtra = new JsonObject();
        private JsonObject _extra = new JsonObject();
        private int _uiScale = 100;
        private string _theme = "black";

        /// <summary>Creates settings with the default values for the file at <paramref name="filePath"/>; call <see cref="Load"/> to read it.</summary>
        public Settings(string filePath)
        {
            FilePath = filePath;
            SetDefaults();
        }

        /// <summary>The settings file.</summary>
        public string FilePath { get; private set; }

        /// <summary>Window left edge in screen pixels, or null for the default position.</summary>
        public int? WindowX { get; set; }

        /// <summary>Window top edge in screen pixels, or null for the default position.</summary>
        public int? WindowY { get; set; }

        /// <summary>Folder of the last opened video ("" when none).</summary>
        public string LastDir { get; set; } = "";

        /// <summary>Whether updates are downloaded as soon as they are found. Default true.</summary>
        public bool AutoDownloadUpdates { get; set; } = true;

        /// <summary>Whether the notes panel is shown. Default true.</summary>
        public bool NotesPanelOpen { get; set; } = true;

        /// <summary>Author name written into exports; empty by default.</summary>
        public string Author { get; set; } = "";

        /// <summary>Interface scale in percent, one of <see cref="UiScaleSteps"/> (default 100). A value outside the steps throws <see cref="ArgumentOutOfRangeException"/>.</summary>
        public int UiScale
        {
            get { return _uiScale; }
            set
            {
                if (Array.IndexOf(UiScaleSteps, value) < 0) throw new ArgumentOutOfRangeException("value", "The scale must be 100, 125, 150, 175 or 200");
                _uiScale = value;
            }
        }

        /// <summary>Colour theme, <c>black</c> (default) or <c>sky</c>. Another value throws <see cref="ArgumentException"/>.</summary>
        public string Theme
        {
            get { return _theme; }
            set
            {
                if (Array.IndexOf(Themes, value) < 0) throw new ArgumentException("The theme must be black or sky");
                _theme = value;
            }
        }

        /// <summary>The recent videos, newest first, at most <see cref="MaxRecent"/>.</summary>
        public IReadOnlyList<RecentEntry> Recent
        {
            get { lock (_lock) { return _recent.ToArray(); } }
        }

        /// <summary>The frame rate overrides in insertion order: normalised absolute video path (lower case) and rate.</summary>
        public IReadOnlyList<KeyValuePair<string, double>> FpsOverrides
        {
            get { lock (_lock) { return _overrides.ToArray(); } }
        }

        private void SetDefaults()
        {
            WindowX = null;
            WindowY = null;
            LastDir = "";
            AutoDownloadUpdates = true;
            NotesPanelOpen = true;
            Author = "";
            _uiScale = 100;
            _theme = "black";
            _recent.Clear();
            _overrides.Clear();
            _windowExtra = new JsonObject();
            _extra = new JsonObject();
        }

        private static string PathKey(string path)
        {
            return Path.GetFullPath(path).Replace('/', '\\').ToLowerInvariant();
        }

        private static bool IsInt(object? value) { return value is long; }

        /// <summary>Reads the file. A missing file gives the defaults; an unreadable or corrupt file gives the defaults and becomes <c>settings.json.bad</c>.</summary>
        public void Load()
        {
            lock (_lock)
            {
                SetDefaults();
                string raw;
                try
                {
                    raw = TextFile.ReadAllText(FilePath);
                }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DecoderFallbackException)
                {
                    MoveAside();
                    return;
                }
                object? loaded;
                try { loaded = Json.Parse(raw); }
                catch (JsonFormatException) { loaded = null; }
                JsonObject? root = loaded as JsonObject;
                if (root == null)
                {
                    MoveAside();
                    return;
                }
                Apply(root);
            }
        }

        private void Apply(JsonObject root)
        {
            foreach (KeyValuePair<string, object?> pair in root)
            {
                object? value = pair.Value;
                switch (pair.Key)
                {
                    case "window":
                        var window = value as JsonObject;
                        if (window == null) break;
                        foreach (KeyValuePair<string, object?> w in window)
                        {
                            if (w.Key == "x") WindowX = ToInt(w.Value);
                            else if (w.Key == "y") WindowY = ToInt(w.Value);
                            else if (Array.IndexOf(RetiredWindowKeys, w.Key) < 0) _windowExtra[w.Key] = w.Value;
                        }
                        break;
                    case "last_dir":
                        if (value is string dir) LastDir = dir;
                        break;
                    case "recent":
                        var list = value as List<object?>;
                        if (list != null) CleanRecent(list);
                        break;
                    case "fps_overrides":
                        var map = value as JsonObject;
                        if (map != null) CleanOverrides(map);
                        break;
                    case "auto_download_updates":
                        if (value is bool auto) AutoDownloadUpdates = auto;
                        break;
                    case "notes_panel_open":
                        if (value is bool panel) NotesPanelOpen = panel;
                        break;
                    case "ui_scale":
                        if (value is long scale && Array.IndexOf(UiScaleSteps, (int)Math.Max(Math.Min(scale, int.MaxValue), int.MinValue)) >= 0) _uiScale = (int)scale;
                        break;
                    case "theme":
                        if (value is string theme && Array.IndexOf(Themes, theme) >= 0) _theme = theme;
                        break;
                    case "author":
                        if (value is string author) Author = author;
                        break;
                    default:
                        if (Array.IndexOf(RetiredKeys, pair.Key) < 0) _extra[pair.Key] = value;
                        break;
                }
            }
        }

        private static int? ToInt(object? value)
        {
            if (!IsInt(value)) return null;
            long l = (long)value!;
            return (l < int.MinValue || l > int.MaxValue) ? (int?)null : (int)l;
        }

        private void CleanRecent(List<object?> items)
        {
            foreach (object? item in items)
            {
                var entry = item as JsonObject;
                if (entry == null) continue;
                object? path;
                if (!entry.TryGetValue("path", out path) || !(path is string) || ((string)path).Length == 0) continue;
                object? lastFrame;
                object? openedAt;
                entry.TryGetValue("last_frame", out lastFrame);
                entry.TryGetValue("opened_at", out openedAt);
                int frame = 0;
                if (lastFrame is long lf && lf >= 0 && lf <= int.MaxValue) frame = (int)lf;
                long opened = openedAt is long oa ? oa : 0;
                _recent.Add(new RecentEntry((string)path, frame, opened));
            }
            if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        }

        private void CleanOverrides(JsonObject map)
        {
            foreach (KeyValuePair<string, object?> pair in map)
            {
                double value;
                if (pair.Value is long l) value = l;
                else if (pair.Value is double d) value = d;
                else continue;
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) continue;
                SetOverrideKey(pair.Key, value);
            }
        }

        private void SetOverrideKey(string key, double value)
        {
            int index = _overrides.FindIndex(p => p.Key == key);
            var pair = new KeyValuePair<string, double>(key, value);
            if (index >= 0) _overrides[index] = pair;
            else _overrides.Add(pair);
        }

        private void MoveAside()
        {
            try { Paths.ReplaceFile(FilePath, FilePath + ".bad"); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        /// <summary>Writes the file atomically.</summary>
        public void Save()
        {
            lock (_lock)
            {
                Paths.AtomicWriteJson(FilePath, ToJson());
            }
        }

        private JsonObject ToJson()
        {
            var window = new JsonObject();
            window["x"] = WindowX.HasValue ? (object)(long)WindowX.Value : null;
            window["y"] = WindowY.HasValue ? (object)(long)WindowY.Value : null;
            foreach (KeyValuePair<string, object?> pair in _windowExtra) window[pair.Key] = pair.Value;

            var recent = new List<object?>();
            foreach (RecentEntry entry in _recent)
            {
                var item = new JsonObject();
                item["path"] = entry.Path;
                item["last_frame"] = (long)entry.LastFrame;
                item["opened_at"] = entry.OpenedAt;
                recent.Add(item);
            }
            var overrides = new JsonObject();
            foreach (KeyValuePair<string, double> pair in _overrides) overrides[pair.Key] = pair.Value;

            var root = new JsonObject();
            root["window"] = window;
            root["last_dir"] = LastDir;
            root["recent"] = recent;
            root["fps_overrides"] = overrides;
            root["auto_download_updates"] = AutoDownloadUpdates;
            root["notes_panel_open"] = NotesPanelOpen;
            root["ui_scale"] = (long)_uiScale;
            root["theme"] = _theme;
            root["author"] = Author;
            foreach (KeyValuePair<string, object?> pair in _extra) root[pair.Key] = pair.Value;
            return root;
        }

        /// <summary>Moves <paramref name="path"/> to the front of the recent list with the given last frame (negative frames count as 0).</summary>
        public void TouchRecent(string path, int lastFrame)
        {
            string key = PathKey(path);
            lock (_lock)
            {
                _recent.RemoveAll(item => PathKey(item.Path) == key);
                _recent.Insert(0, new RecentEntry(Path.GetFullPath(path), Math.Max(0, lastFrame), DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
                if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
            }
        }

        /// <summary>Removes <paramref name="path"/> from the recent list (compared case-insensitively). True when it was listed.</summary>
        public bool ForgetRecent(string path)
        {
            string key = PathKey(path);
            lock (_lock)
            {
                return _recent.RemoveAll(item => PathKey(item.Path) == key) > 0;
            }
        }

        /// <summary>Last frame remembered for <paramref name="path"/>, 0 when unknown.</summary>
        public int LastFrame(string path)
        {
            string key = PathKey(path);
            lock (_lock)
            {
                foreach (RecentEntry item in _recent)
                {
                    if (PathKey(item.Path) == key) return item.LastFrame;
                }
            }
            return 0;
        }

        /// <summary>Remembers a frame rate for a video; null removes the override. Throws <see cref="ArgumentException"/> for a rate that is not a positive finite number.</summary>
        public void SetFpsOverride(string path, double? fps)
        {
            string key = PathKey(path);
            lock (_lock)
            {
                if (fps == null)
                {
                    _overrides.RemoveAll(p => p.Key == key);
                    return;
                }
                double value = fps.Value;
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new ArgumentException("The frame rate must be a positive number");
                SetOverrideKey(key, value);
            }
        }

        /// <summary>Frame rate remembered for a video, or null.</summary>
        public double? FpsOverride(string path)
        {
            string key = PathKey(path);
            lock (_lock)
            {
                int index = _overrides.FindIndex(p => p.Key == key);
                return index >= 0 ? (double?)_overrides[index].Value : null;
            }
        }
    }

    /// <summary>Text file reading with the semantics of Python's <c>read_text(encoding="utf-8-sig")</c>.</summary>
    internal static class TextFile
    {
        /// <summary>Reads a UTF-8 file strictly, drops a leading byte order mark and turns CRLF and lone CR into LF.</summary>
        public static string ReadAllText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int offset = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
            string text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            if (text.IndexOf('\r') >= 0) text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text;
        }
    }
}
