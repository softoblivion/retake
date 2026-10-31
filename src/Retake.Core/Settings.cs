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
    /// (a corrupt file is moved aside), the merge with the file on disk when several windows save, the recent list and
    /// the per-video frame rate overrides. Keys this version does not know are kept and written back. Methods lock; the property values themselves are plain fields, so a caller that
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
        // The file as this object last read or wrote it (normal form); null when it held none. Save merges against it.
        private JsonObject? _baseline;

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

        /// <summary>Width of the window when it is not maximized, in device-independent pixels; null for the default size.</summary>
        public int? WindowWidth { get; set; }

        /// <summary>Height of the window when it is not maximized, in device-independent pixels; null for the default size.</summary>
        public int? WindowHeight { get; set; }

        /// <summary>Whether the window was maximized when it closed. Default false.</summary>
        public bool WindowMaximized { get; set; }

        /// <summary>Folder of the last opened video ("" when none).</summary>
        public string LastDir { get; set; } = "";

        /// <summary>Whether updates are downloaded as soon as they are found. Default true.</summary>
        public bool AutoDownloadUpdates { get; set; } = true;

        /// <summary>Whether the notes panel is shown. Default true.</summary>
        public bool NotesPanelOpen { get; set; } = true;

        /// <summary>The reviewer's name, written into the Markdown notes file and the package; empty by default.</summary>
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
            WindowWidth = null;
            WindowHeight = null;
            WindowMaximized = false;
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

        /// <summary>The comparison key of a path: the full path, <c>/</c> as <c>\</c>, lower case; a path that has no full form (invalid characters) keys as written.</summary>
        private static string PathKey(string path)
        {
            return (FullPathOrNull(path) ?? path).Replace('/', '\\').ToLowerInvariant();
        }

        private static string? FullPathOrNull(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }

        private static bool IsInt(object? value) { return value is long; }

        private const int ReadAttempts = 4;

        /// <summary>The text of the settings file, read with a short retry while another program (another window's replace, a scanner) holds it.</summary>
        private string ReadFileText()
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return TextFile.ReadAllText(FilePath);
                }
                catch (Exception ex) when (attempt < ReadAttempts - 1 && !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException)
                                           && (ex is IOException || ex is UnauthorizedAccessException))
                {
                    System.Threading.Thread.Sleep(40 * (attempt + 1));
                }
            }
        }

        /// <summary>
        /// Reads the file. A missing file gives the defaults. A file that is not valid UTF-8 or JSON, or whose top level is
        /// not an object, gives the defaults and becomes <c>settings.json.bad</c>. A file that cannot be read (locked,
        /// access denied) gives the defaults for this run and stays as it is: <see cref="Save"/> does not write over it
        /// while it cannot be read.
        /// </summary>
        public void Load()
        {
            lock (_lock)
            {
                SetDefaults();
                _baseline = null;
                string raw;
                try
                {
                    raw = ReadFileText();
                }
                catch (FileNotFoundException) { return; }
                catch (DirectoryNotFoundException) { return; }
                catch (DecoderFallbackException)
                {
                    MoveAside();
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Warning("the settings file cannot be read, the defaults are used: " + ex.Message);
                    return;
                }
                JsonObject? root = ParseRoot(raw);
                if (root == null)
                {
                    MoveAside();
                    return;
                }
                Apply(root);
                _baseline = ToJson();
            }
        }

        private static JsonObject? ParseRoot(string raw)
        {
            try { return Json.Parse(raw) as JsonObject; }
            catch (JsonFormatException) { return null; }
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
                            else if (w.Key == "width") WindowWidth = ToSize(w.Value);
                            else if (w.Key == "height") WindowHeight = ToSize(w.Value);
                            else if (w.Key == "maximized") WindowMaximized = w.Value is bool max && max;
                            else _windowExtra[w.Key] = w.Value;
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

        /// <summary>A window size: a whole number from 100 to 100000, else null.</summary>
        private static int? ToSize(object? value)
        {
            int? n = ToInt(value);
            return n.HasValue && n.Value >= 100 && n.Value <= 100000 ? n : null;
        }

        private void CleanRecent(List<object?> items)
        {
            foreach (object? item in items)
            {
                var entry = item as JsonObject;
                if (entry == null) continue;
                object? path;
                if (!entry.TryGetValue("path", out path) || !(path is string) || ((string)path).Length == 0) continue;
                // A path with no full form (invalid characters, a second colon) cannot be opened or compared: skipped.
                if (FullPathOrNull((string)path) == null) continue;
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

        /// <summary>
        /// Writes the file atomically, merged with the file on disk, so several windows keep each other's changes: a value
        /// this object changed since it read or wrote the file wins; any other value is taken from the disk (and into this
        /// object). The recent lists are joined (the newer entry of a video wins, removals are kept) and the frame rate
        /// overrides are merged per video. Windows of the machine are serialised with a named mutex. Throws
        /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the file exists but cannot be read
        /// (it is not written over) or cannot be written.
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                using (var gate = new FileGate(FilePath))
                {
                    JsonObject mine = ToJson();
                    JsonObject? disk = ReadDiskForMerge();
                    JsonObject result = mine;
                    if (disk != null)
                    {
                        JsonObject merged = Merge(_baseline ?? new Settings(FilePath).ToJson(), mine, disk);
                        if (Json.Write(merged) != Json.Write(mine))
                        {
                            SetDefaults();
                            Apply(merged);
                            result = ToJson();
                        }
                    }
                    Paths.AtomicWriteJson(FilePath, result);
                    _baseline = result;
                }
            }
        }

        /// <summary>The file on disk in this object's normal form; null when it is missing or not a settings object. Throws when it exists but cannot be read.</summary>
        private JsonObject? ReadDiskForMerge()
        {
            string raw;
            try
            {
                raw = ReadFileText();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (DecoderFallbackException) { return null; }
            JsonObject? root = ParseRoot(raw);
            if (root == null) return null;
            var other = new Settings(FilePath);
            other.Apply(root);
            return other.ToJson();
        }

        private static bool SameJson(object? a, object? b)
        {
            return Json.Write(a) == Json.Write(b);
        }

        private static object? Get(JsonObject o, string key)
        {
            object? value;
            return o.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>The three-way merge of <see cref="Save"/>: <paramref name="baseline"/> is the file as this object last saw it.</summary>
        private static JsonObject Merge(JsonObject baseline, JsonObject mine, JsonObject disk)
        {
            var result = new JsonObject();
            var keys = new List<string>(disk.Keys);
            foreach (string key in mine.Keys) if (!keys.Contains(key)) keys.Add(key);
            foreach (string key in keys)
            {
                bool inMine = mine.ContainsKey(key), inDisk = disk.ContainsKey(key), inBase = baseline.ContainsKey(key);
                if (key == "recent" && inMine && inDisk)
                {
                    result[key] = MergeRecent(Get(baseline, key) as List<object?>, Get(mine, key) as List<object?>, Get(disk, key) as List<object?>);
                    continue;
                }
                if (key == "fps_overrides" && inMine && inDisk)
                {
                    result[key] = MergeMap(Get(baseline, key) as JsonObject ?? new JsonObject(), Get(mine, key) as JsonObject ?? new JsonObject(), Get(disk, key) as JsonObject ?? new JsonObject());
                    continue;
                }
                bool mineChanged = inMine != inBase || (inMine && !SameJson(Get(mine, key), Get(baseline, key)));
                if (mineChanged)
                {
                    if (inMine) result[key] = Get(mine, key);
                }
                else if (inDisk)
                {
                    result[key] = Get(disk, key);
                }
            }
            return result;
        }

        private static JsonObject MergeMap(JsonObject baseline, JsonObject mine, JsonObject disk)
        {
            var result = new JsonObject();
            var keys = new List<string>(disk.Keys);
            foreach (string key in mine.Keys) if (!keys.Contains(key)) keys.Add(key);
            foreach (string key in keys)
            {
                bool inMine = mine.ContainsKey(key), inBase = baseline.ContainsKey(key);
                bool mineChanged = inMine != inBase || (inMine && !SameJson(Get(mine, key), Get(baseline, key)));
                if (mineChanged) { if (inMine) result[key] = Get(mine, key); }
                else if (disk.ContainsKey(key)) result[key] = Get(disk, key);
            }
            return result;
        }

        private static string EntryKey(object? entry)
        {
            return entry is JsonObject o && Get(o, "path") is string p ? PathKey(p) : "";
        }

        private static long EntryTime(object? entry)
        {
            return entry is JsonObject o && Get(o, "opened_at") is long t ? t : 0;
        }

        /// <summary>Joins two recent lists: an entry removed on one side (and untouched on the other) goes, the newer entry of a video wins, newest first.</summary>
        private static List<object?> MergeRecent(List<object?>? baseline, List<object?>? mine, List<object?>? disk)
        {
            baseline = baseline ?? new List<object?>();
            mine = mine ?? new List<object?>();
            disk = disk ?? new List<object?>();
            if (SameJson(disk, baseline)) return mine;
            if (SameJson(mine, baseline)) return disk;
            var before = new Dictionary<string, object?>();
            foreach (object? e in baseline) before[EntryKey(e)] = e;
            var mineByKey = new Dictionary<string, object?>();
            foreach (object? e in mine) mineByKey[EntryKey(e)] = e;
            var diskByKey = new Dictionary<string, object?>();
            foreach (object? e in disk) diskByKey[EntryKey(e)] = e;
            var picked = new List<KeyValuePair<object?, int>>();
            var seen = new HashSet<string>();
            int index = 0;
            foreach (object? e in mine.Concat(disk))
            {
                string key = EntryKey(e);
                index++;
                if (!seen.Add(key)) continue;
                object? m = mineByKey.TryGetValue(key, out object? mv) ? mv : null;
                object? d = diskByKey.TryGetValue(key, out object? dv) ? dv : null;
                object? b = before.TryGetValue(key, out object? bv) ? bv : null;
                // Removed on one side and not touched on the other since: it stays removed.
                if (m == null && b != null && SameJson(d, b)) continue;
                if (d == null && b != null && SameJson(m, b)) continue;
                object? winner = m == null ? d : d == null ? m : (EntryTime(d) > EntryTime(m) ? d : m);
                picked.Add(new KeyValuePair<object?, int>(winner, index));
            }
            return picked.OrderByDescending(p => EntryTime(p.Key)).ThenBy(p => p.Value).Select(p => p.Key).Take(MaxRecent).ToList();
        }

        /// <summary>A machine-wide lock of one settings file (a named mutex), held while it is read, merged and written.</summary>
        private sealed class FileGate : IDisposable
        {
            private readonly System.Threading.Mutex? _mutex;
            private readonly bool _owned;

            public FileGate(string path)
            {
                uint hash = 2166136261;
                foreach (char c in PathKey(path)) { hash ^= c; hash *= 16777619; }
                try
                {
                    _mutex = new System.Threading.Mutex(false, @"Local\Retake.Settings." + hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
                    try { _owned = _mutex.WaitOne(3000); }
                    catch (System.Threading.AbandonedMutexException) { _owned = true; }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Threading.WaitHandleCannotBeOpenedException)
                {
                    _mutex = null;
                }
            }

            public void Dispose()
            {
                if (_mutex == null) return;
                if (_owned) _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }

        private JsonObject ToJson()
        {
            var window = new JsonObject();
            window["x"] = WindowX.HasValue ? (object)(long)WindowX.Value : null;
            window["y"] = WindowY.HasValue ? (object)(long)WindowY.Value : null;
            // The size keys are written only when they hold something, so a file of the 1.x line is kept as it is.
            if (WindowWidth.HasValue) window["width"] = (long)WindowWidth.Value;
            if (WindowHeight.HasValue) window["height"] = (long)WindowHeight.Value;
            if (WindowMaximized) window["maximized"] = true;
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
                _recent.Insert(0, new RecentEntry(FullPathOrNull(path) ?? path, Math.Max(0, lastFrame), DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
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
