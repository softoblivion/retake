using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>The data folder and the settings (test names after the 1.x suite), plus the JSON layer, the new settings keys, the legacy import and the log.</summary>
    public static class PathsSettingsTests
    {
        private static readonly string E9 = ((char)0xE9).ToString();

        private static Settings Make(TestEnv t)
        {
            return new Settings(t.Combine("home", "settings.json"));
        }

        private static void AssertDefaults(Settings s)
        {
            Check.Null(s.WindowX);
            Check.Null(s.WindowY);
            Check.Equal("", s.LastDir);
            Check.Equal(0, s.Recent.Count);
            Check.Equal(0, s.FpsOverrides.Count);
            Check.Equal(true, s.AutoDownloadUpdates);
            Check.Equal(true, s.NotesPanelOpen);
            Check.Equal(100, s.UiScale);
            Check.Equal("black", s.Theme);
            Check.Equal("", s.Author);
        }

        private static JsonObject ReadJson(string path)
        {
            return (JsonObject)Json.Parse(Fx.ReadText(path))!;
        }

        // --- paths --------------------------------------------------------------------------------------------------

        [Test("test_core_paths_settings.PathsTest.test_home_from_environment")]
        public static void HomeFromEnvironment(TestEnv t)
        {
            var p = new Paths();
            Check.Equal(t.Combine("home"), p.Home);
            Check.Equal(t.Combine("home", "settings.json"), p.SettingsFile);
            Check.Equal(t.Combine("home", "logs"), p.LogDir);
            Check.Equal(t.Combine("home", "logs", "app.log"), p.LogFile);
            Check.Equal(t.Combine("home", "updates"), p.UpdatesDir);
            Check.Equal(t.Combine("home", "bin"), p.BinDir);
            Check.True(p.UsesEnvironmentOverride);
            Check.False(Directory.Exists(p.Home));
            p.Ensure();
            Check.True(Directory.Exists(p.LogDir) && Directory.Exists(p.UpdatesDir));
        }

        [Test("test_core_paths_settings.PathsTest.test_home_without_environment_is_beside_the_project_root")]
        public static void HomeWithoutEnvironmentIsBesideTheExe(TestEnv t)
        {
            Environment.SetEnvironmentVariable(Paths.EnvHome, null);
            var p = new Paths();
            Check.Equal(Path.Combine(Paths.BaseDir, Paths.AppDirName), p.Home);
            Check.Equal("retake_data", Path.GetFileName(p.Home));
            Check.False(p.UsesEnvironmentOverride);
            Environment.SetEnvironmentVariable(Paths.EnvHome, "   ");
            Check.Equal(p.Home, new Paths().Home);
        }

        [Test("test_core_paths_settings.PathsTest.test_frozen_home_is_beside_the_exe")]
        public static void HomeBesideTheExe(TestEnv t)
        {
            Check.Equal(@"D:\app", Paths.ResolveBaseDir(@"D:\app\"));
            Check.Equal(@"D:\app", Paths.ResolveBaseDir(@"D:\app"));
            Check.Equal(@"D:\proj\src\Retake", Paths.ResolveBaseDir(@"D:\proj\src\Retake\bin\Release\"));
            Check.Equal(@"D:\proj", Paths.ResolveBaseDir(@"D:\proj\bin"));
            Check.Equal(@"D:\proj\bin\tools\x", Paths.ResolveBaseDir(@"D:\proj\bin\tools\x"));
            var p = new Paths(t.Combine("explicit"));
            Check.Equal(t.Combine("explicit", "settings.json"), p.SettingsFile);
            Check.False(p.UsesEnvironmentOverride);
        }

        [Test("test_core_paths_settings.PathsTest.test_atomic_writes")]
        public static void AtomicWrites(TestEnv t)
        {
            string target = t.Combine("out", "a.txt");
            string text = "line1\nline2\n" + E9;
            Paths.AtomicWriteText(target, text);
            Check.Equal(new UTF8Encoding(false).GetBytes(text), File.ReadAllBytes(target));
            Paths.AtomicWriteText(target, "second");
            Check.Equal("second", File.ReadAllText(target));
            Check.Equal(new[] { "a.txt" }, Fx.Names(t.Combine("out")));
            Paths.AtomicWriteBytes(t.Combine("b.bin"), new byte[] { 0, 1 });
            Check.Equal(new byte[] { 0, 1 }, File.ReadAllBytes(t.Combine("b.bin")));
            var obj = new JsonObject();
            obj["name"] = E9;
            obj["n"] = new List<object?> { 1L, 2L };
            Paths.AtomicWriteJson(t.Combine("c.json"), obj);
            byte[] raw = File.ReadAllBytes(t.Combine("c.json"));
            Check.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, "no byte order mark");
            string json = Encoding.UTF8.GetString(raw);
            Check.Equal("{\n  \"name\": \"" + E9 + "\",\n  \"n\": [\n    1,\n    2\n  ]\n}\n", json);
            var back = (JsonObject)Json.Parse(json)!;
            Check.Equal(E9, back["name"]);
            Check.Contains(E9, json);
        }

        [Test("test_core_paths_settings.PathsTest.test_failed_write_leaves_no_part_file")]
        public static void FailedWriteLeavesNoPartFile(TestEnv t)
        {
            string target = t.Combine("x.txt");
            Directory.CreateDirectory(target); // a folder in the way makes the final replace fail
            Check.ThrowsFileError(() => Paths.AtomicWriteText(target, "data"));
            Check.Equal(new[] { "x.txt" }, Fx.Names(t.Tmp).Where(n => n.StartsWith("x.txt", StringComparison.Ordinal)).ToList());
        }

        [Test("paths.legacy_settings_are_imported_once")]
        public static void LegacySettingsImportedOnce(TestEnv t)
        {
            string legacy = t.Combine("legacy");
            Fx.WriteRaw(Path.Combine(legacy, "settings.json"), "{\n  \"last_dir\": \"clips\"\n}\n");
            var p = new Paths(t.Combine("fresh"));
            Check.True(p.ImportLegacySettings(legacy));
            Check.Equal("{\n  \"last_dir\": \"clips\"\n}\n", File.ReadAllText(p.SettingsFile));
            Check.True(File.Exists(Path.Combine(legacy, "settings.json")), "the legacy file stays");
            Check.False(p.ImportLegacySettings(legacy), "the data folder exists now");
            Check.False(new Paths(t.Combine("other")).ImportLegacySettings(t.Combine("nothing")));
            Check.False(new Paths().ImportLegacySettings(), "RETAKE_HOME disables the import");
        }

        [Test("paths.log_rotates_by_size")]
        public static void LogRotatesBySize(TestEnv t)
        {
            string dir = t.Combine("logs");
            var seen = new List<string>();
            Log.Listener = (level, message) => seen.Add(level + " " + message);
            Log.Initialize(dir);
            string filler = new string('x', 600);
            for (int i = 0; i < 6000; i++) Log.Info("line " + i + " " + filler);
            Log.Warning("warn");
            Log.Error("err");
            string main = Path.Combine(dir, "app.log");
            Check.True(File.Exists(main));
            Check.True(File.Exists(main + ".1"), "rotated once");
            Check.True(File.Exists(main + ".2"), "rotated twice");
            Check.False(File.Exists(main + ".4"), "at most three rotated files");
            Check.True(new FileInfo(main).Length < Log.MaxBytes + 2000);
            string last = File.ReadAllText(main);
            Check.Contains(" WARNING warn\n", last);
            Check.Contains(" ERROR err\n", last);
            Check.Equal("ERROR err", seen[seen.Count - 1]);
            Log.Close();
            Log.Info("after close");
            Check.NotContains("after close", File.ReadAllText(main));
        }

        // --- settings -----------------------------------------------------------------------------------------------

        [Test("test_core_paths_settings.SettingsTest.test_missing_file_gives_defaults")]
        public static void MissingFileGivesDefaults(TestEnv t)
        {
            Settings s = Make(t);
            s.Load();
            AssertDefaults(s);
        }

        [Test("test_core_paths_settings.SettingsTest.test_save_and_load")]
        public static void SaveAndLoad(TestEnv t)
        {
            Settings s = Make(t);
            s.Load();
            s.LastDir = "D:\\clips";
            s.WindowX = 40;
            s.TouchRecent(t.Combine("a.mp4"), 42);
            s.SetFpsOverride(t.Combine("a.mp4"), 29.97);
            s.Save();
            Settings u = Make(t);
            u.Load();
            Check.Equal("D:\\clips", u.LastDir);
            Check.Equal(40, u.WindowX);
            Check.Null(u.WindowY);
            Check.Equal(42, u.Recent[0].LastFrame);
            Check.Equal(29.97, u.FpsOverride(t.Combine("a.mp4")));
            Check.Null(u.FpsOverride(t.Combine("b.mp4")));
        }

        [Test("test_core_paths_settings.SettingsTest.test_corrupt_file_is_moved_aside")]
        public static void CorruptFileIsMovedAside(TestEnv t)
        {
            string folder = t.Combine("home");
            Fx.WriteText(Path.Combine(folder, "settings.json"), "{not json");
            Settings s = Make(t);
            s.Load();
            AssertDefaults(s);
            Check.False(File.Exists(Path.Combine(folder, "settings.json")));
            Check.Equal("{not json", Fx.ReadText(Path.Combine(folder, "settings.json.bad")));
        }

        [Test("test_core_paths_settings.SettingsTest.test_wrong_types_fall_back_per_key")]
        public static void WrongTypesFallBackPerKey(TestEnv t)
        {
            string folder = t.Combine("home");
            Fx.WriteText(Path.Combine(folder, "settings.json"),
                "{\"window\": {\"x\": \"left\", \"y\": 10}, \"recent\": [{\"path\": 5}, {\"path\": \"p\", \"last_frame\": 3, "
                + "\"opened_at\": 1}], \"fps_overrides\": {\"a\": 25, \"b\": -1, \"c\": \"x\"}, \"extra_key\": 1, "
                + "\"auto_download_updates\": \"yes\"}");
            Settings s = Make(t);
            s.Load();
            Check.Null(s.WindowX);
            Check.Equal(10, s.WindowY);
            Check.Equal(1, s.Recent.Count);
            Check.Equal("p", s.Recent[0].Path);
            Check.Equal(3, s.Recent[0].LastFrame);
            Check.Equal(1L, s.Recent[0].OpenedAt);
            Check.Equal(1, s.FpsOverrides.Count);
            Check.Equal("a", s.FpsOverrides[0].Key);
            Check.Equal(25.0, s.FpsOverrides[0].Value);
            s.Save();
            Check.Equal(1L, ReadJson(Path.Combine(folder, "settings.json"))["extra_key"], "unknown keys are kept");
            Check.Equal(true, s.AutoDownloadUpdates);
        }

        [Test("test_core_paths_settings.SettingsTest.test_old_window_size_keys_are_dropped")]
        public static void OldWindowSizeKeysAreDropped(TestEnv t)
        {
            string folder = t.Combine("home");
            Fx.WriteText(Path.Combine(folder, "settings.json"),
                "{\"window\": {\"x\": 30, \"y\": 20, \"width\": 900, \"height\": 700, \"maximized\": true}}");
            Settings s = Make(t);
            s.Load();
            Check.Equal(30, s.WindowX);
            Check.Equal(20, s.WindowY);
            s.Save();
            var window = (JsonObject)ReadJson(Path.Combine(folder, "settings.json"))["window"]!;
            Check.Equal(new[] { "x", "y" }, window.Keys.ToList());
            Check.Equal(30L, window["x"]);
            Check.Equal(20L, window["y"]);
        }

        [Test("test_core_paths_settings.SettingsTest.test_recent_list_is_newest_first_unique_and_capped")]
        public static void RecentListIsNewestFirstUniqueAndCapped(TestEnv t)
        {
            Settings s = Make(t);
            s.Load();
            for (int i = 0; i < 25; i++) s.TouchRecent(t.Combine("v" + i + ".mp4"), i);
            Check.Equal(20, s.Recent.Count);
            s.TouchRecent(t.Combine("v10.mp4"), 99);
            Check.Equal(t.Combine("v10.mp4"), s.Recent[0].Path);
            Check.Equal(99, s.Recent[0].LastFrame);
            Check.Equal(1, s.Recent.Count(r => r.Path.EndsWith("v10.mp4", StringComparison.Ordinal)));
            Check.Equal(20, s.Recent.Count);
            Check.Equal(99, s.LastFrame(t.Combine("v10.mp4")));
            Check.Equal(0, s.LastFrame(t.Combine("nope.mp4")));
        }

        [Test("test_core_paths_settings.SettingsTest.test_auto_download_defaults_to_on")]
        public static void AutoDownloadDefaultsToOn(TestEnv t)
        {
            Fx.WriteText(t.Combine("home", "settings.json"), "{\"notes_panel_open\": false}");
            Settings s = Make(t);
            s.Load();
            Check.Equal(true, s.AutoDownloadUpdates);
            Check.Equal(false, s.NotesPanelOpen);
        }

        [Test("test_core_paths_settings.SettingsTest.test_retired_interval_key_is_dropped_silently")]
        public static void RetiredIntervalKeyIsDroppedSilently(TestEnv t)
        {
            string folder = t.Combine("home");
            Fx.WriteText(Path.Combine(folder, "settings.json"),
                "{\"update_check_interval\": \"6h\", \"auto_download_updates\": false, \"last_dir\": \"clips\"}");
            Settings s = Make(t);
            s.Load();
            Check.Equal(false, s.AutoDownloadUpdates);
            Check.False(File.Exists(Path.Combine(folder, "settings.json.bad")));
            s.Save();
            JsonObject written = ReadJson(Path.Combine(folder, "settings.json"));
            Check.False(written.ContainsKey("update_check_interval"));
            Check.Equal("clips", written["last_dir"]);
        }

        [Test("test_core_paths_settings.SettingsTest.test_forget_recent")]
        public static void ForgetRecent(TestEnv t)
        {
            Settings s = Make(t);
            s.Load();
            s.TouchRecent(t.Combine("a.mp4"), 1);
            s.TouchRecent(t.Combine("b.mp4"), 2);
            Check.True(s.ForgetRecent(t.Combine("A.MP4")));
            Check.False(s.ForgetRecent(t.Combine("a.mp4")));
            Check.Equal(new[] { "b.mp4" }, s.Recent.Select(r => Path.GetFileName(r.Path)).ToList());
        }

        [Test("test_core_paths_settings.SettingsTest.test_fps_override_set_and_clear")]
        public static void FpsOverrideSetAndClear(TestEnv t)
        {
            Settings s = Make(t);
            s.Load();
            string video = t.Combine("clip.mp4");
            s.SetFpsOverride(video, 24);
            Check.Equal(24.0, s.FpsOverride(video));
            s.SetFpsOverride(video, null);
            Check.Null(s.FpsOverride(video));
            Check.Throws<ArgumentException>(() => s.SetFpsOverride(video, 0));
        }

        [Test("settings.new_keys_ui_scale_theme_author")]
        public static void NewKeys(TestEnv t)
        {
            Fx.WriteText(t.Combine("home", "settings.json"), "{\"ui_scale\": 150, \"theme\": \"sky\", \"author\": \"Ann\"}");
            Settings s = Make(t);
            s.Load();
            Check.Equal(150, s.UiScale);
            Check.Equal("sky", s.Theme);
            Check.Equal("Ann", s.Author);
            s.Save();
            JsonObject written = ReadJson(t.Combine("home", "settings.json"));
            Check.Equal(150L, written["ui_scale"]);
            Check.Equal("sky", written["theme"]);
            Check.Equal("Ann", written["author"]);

            Fx.WriteText(t.Combine("home", "settings.json"), "{\"ui_scale\": 130, \"theme\": \"neon\", \"author\": 5}");
            s.Load();
            Check.Equal(100, s.UiScale);
            Check.Equal("black", s.Theme);
            Check.Equal("", s.Author);
            Fx.WriteText(t.Combine("home", "settings.json"), "{\"ui_scale\": 125.0, \"theme\": null}");
            s.Load();
            Check.Equal(100, s.UiScale);
            Check.Equal("black", s.Theme);

            foreach (int step in Settings.UiScaleSteps) s.UiScale = step;
            Check.Throws<ArgumentOutOfRangeException>(() => s.UiScale = 110);
            s.Theme = "sky";
            Check.Throws<ArgumentException>(() => s.Theme = "neon");
        }

        [Test("settings.unknown_keys_and_window_extras_are_kept_in_order")]
        public static void UnknownKeysAreKept(TestEnv t)
        {
            Fx.WriteText(t.Combine("home", "settings.json"),
                "{\"zeta\": {\"a\": [1, 2.5, null, \"s\"]}, \"window\": {\"x\": 1, \"y\": 2, \"dock\": \"left\"}, \"alpha\": true}");
            Settings s = Make(t);
            s.Load();
            s.Save();
            JsonObject written = ReadJson(t.Combine("home", "settings.json"));
            Check.Equal(new[] { "window", "last_dir", "recent", "fps_overrides", "auto_download_updates", "notes_panel_open", "ui_scale", "theme", "author", "zeta", "alpha" },
                written.Keys.ToList());
            Check.Equal(new[] { "x", "y", "dock" }, ((JsonObject)written["window"]!).Keys.ToList());
        }

        // --- JSON ---------------------------------------------------------------------------------------------------

        [Test("json.float_text_matches_python_repr")]
        public static void FloatTextMatchesRepr(TestEnv t)
        {
            Check.Equal("29.97", Json.Write(29.97));
            Check.Equal("25.0", Json.Write(25.0));
            Check.Equal("0.1", Json.Write(0.1));
            Check.Equal("100.0", Json.Write(100.0));
            Check.Equal("1e+16", Json.Write(1e16));
            Check.Equal("1.5e-05", Json.Write(1.5e-5));
            Check.Equal("1.2345678901234568e+17", Json.Write(123456789012345678.0));
            Check.Equal("123456.789", Json.Write(123456.789));
            Check.Equal("0.0001", Json.Write(0.0001));
            Check.Equal("-2.5", Json.Write(-2.5));
            Check.Equal("23.976023976023978", Json.Write(24000.0 / 1001.0));
            Check.Equal("0.30000000000000004", Json.Write(0.1 + 0.2));
        }

        [Test("json.parse_and_write_round_trip")]
        public static void ParseAndWrite(TestEnv t)
        {
            string text = "{\n  \"s\": \"a\\\"b\\\\c\\n\\t\\u0001" + E9 + "\",\n  \"n\": -12,\n  \"f\": 2.5,\n  \"e\": [],\n  \"o\": {},\n  \"l\": [\n    true,\n    false,\n    null\n  ]\n}";
            object? value = Json.Parse(text);
            Check.Equal(text, Json.Write(value));
            var obj = (JsonObject)value!;
            Check.Equal(-12L, obj["n"]);
            Check.Equal(2.5, obj["f"]);
            Check.Equal("a\"b\\c\n\t\u0001" + E9, obj["s"]);
            Check.Equal(1.0, Json.Parse("1e0"));
            Check.Equal(10L, Json.Parse(" 10 "));
            Check.Equal("\ud83d\ude00", Json.Parse("\"\\ud83d\\ude00\""));
            foreach (string bad in new[] { "", "{", "[1,]", "{\"a\" 1}", "{\"a\": 1,}", "01", "1.", "\"a\nb\"", "tru", "{} x", "\"\\q\"", "[1 2]", "-" })
            {
                Check.Throws<JsonFormatException>(() => Json.Parse(bad), "text '" + bad + "'");
            }
        }
    }
}
