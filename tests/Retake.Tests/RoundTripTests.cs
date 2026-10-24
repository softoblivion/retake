using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>
    /// Round trips against fixtures written by the 1.x core: the same operations run through the C# core must produce the
    /// same files byte for byte, and files written by the 1.x core must survive a read and a save unchanged.
    /// </summary>
    public static class RoundTripTests
    {
        private static string FixtureRoot
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures"); }
        }

        private static byte[]? Png(JsonObject op)
        {
            object? label;
            if (!op.TryGetValue("image", out label) || label == null) return null;
            return new byte[] { 0x89 }.Concat(Encoding.UTF8.GetBytes("PNG " + (string)label)).ToArray();
        }

        private static double? Rate(object? value)
        {
            if (value == null) return null;
            if (value is long l) return l;
            return (double)value;
        }

        private static void Apply(Session session, string video, List<object?> ops)
        {
            foreach (object? item in ops)
            {
                var op = (JsonObject)item!;
                string kind = (string)op["op"]!;
                List<NoteState> notes = session.GetState().Notes;
                switch (kind)
                {
                    case "open":
                        session.Open(video);
                        break;
                    case "close":
                        session.Close();
                        break;
                    case "add":
                        object? region;
                        op.TryGetValue("region", out region);
                        session.AddNote((int)(long)op["frame"]!, (string)op["kind"]!, (string)op["text"]!,
                            region == null ? null : Region.FromJson(region), Png(op));
                        break;
                    case "set_fps":
                        session.SetFps(Rate(op["fps"]));
                        break;
                    case "update":
                        string id = notes[(int)(long)op["note"]!].Id;
                        object? newRegion;
                        bool hasRegion = op.TryGetValue("region", out newRegion);
                        object? text;
                        op.TryGetValue("text", out text);
                        session.UpdateNote(id, (string?)text, hasRegion && newRegion != null ? Region.FromJson(newRegion) : null,
                            hasRegion && newRegion == null, Png(op));
                        break;
                    case "remove":
                        session.RemoveNote(notes[(int)(long)op["note"]!].Id);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown operation " + kind);
                }
            }
        }

        private static string Describe(byte[] expected, byte[] actual)
        {
            int n = Math.Min(expected.Length, actual.Length);
            int i = 0;
            while (i < n && expected[i] == actual[i]) i++;
            Func<byte[], string> window = b => Encoding.UTF8.GetString(b, Math.Max(0, i - 20), Math.Min(60, b.Length - Math.Max(0, i - 20))).Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            return "first difference at byte " + i + " (expected length " + expected.Length + ", actual " + actual.Length + "): expected ..." + window(expected) + "... actual ..." + window(actual) + "...";
        }

        private static void AssertSameTree(string expectedRoot, string actualRoot)
        {
            SortedDictionary<string, byte[]> expected = Fx.Tree(expectedRoot);
            SortedDictionary<string, byte[]> actual = Fx.Tree(actualRoot);
            Check.Equal(expected.Keys, actual.Keys, "files");
            foreach (KeyValuePair<string, byte[]> file in expected)
            {
                if (!file.Value.SequenceEqual(actual[file.Key])) throw new AssertException(file.Key + ": " + Describe(file.Value, actual[file.Key]));
            }
            // Folders must match too (an empty frames folder left behind is a difference).
            Check.Equal(Directory.GetDirectories(expectedRoot, "*", SearchOption.AllDirectories).Select(d => d.Substring(expectedRoot.Length)).OrderBy(d => d, StringComparer.Ordinal).ToList(),
                Directory.GetDirectories(actualRoot, "*", SearchOption.AllDirectories).Select(d => d.Substring(actualRoot.Length)).OrderBy(d => d, StringComparer.Ordinal).ToList(), "folders");
        }

        private static void RunScenario(TestEnv t, string name)
        {
            string dir = Path.Combine(FixtureRoot, "scenarios", name);
            var spec = (JsonObject)Json.Parse(File.ReadAllText(Path.Combine(dir, "ops.json")))!;
            string clips = t.Combine("clips");
            Fx.CopyTree(Path.Combine(dir, "in"), clips);
            var paths = new Paths();
            var settings = new Settings(paths.SettingsFile);
            settings.Load();
            var session = new Session(settings, state => { });
            Apply(session, Path.Combine(clips, (string)spec["video"]!), (List<object?>)spec["ops"]!);
            AssertSameTree(Path.Combine(dir, "expected"), clips);
        }

        [Test("roundtrip.session_operations_match_the_1x_core_25fps_with_regions_and_renames")]
        public static void SessionA(TestEnv t) { RunScenario(t, "a_session"); }

        [Test("roundtrip.session_operations_match_the_1x_core_unknown_rate_drop_frame")]
        public static void SessionB(TestEnv t) { RunScenario(t, "b_session_unknown_rate"); }

        [Test("roundtrip.session_operations_match_the_1x_core_120fps")]
        public static void SessionE(TestEnv t) { RunScenario(t, "e_session_120fps"); }

        [Test("roundtrip.migration_of_a_1_0_layout_matches_the_1x_core")]
        public static void MigrationC(TestEnv t) { RunScenario(t, "c_layout10"); }

        [Test("roundtrip.migration_with_bom_crlf_and_foreign_lines_matches_the_1x_core")]
        public static void MigrationD(TestEnv t) { RunScenario(t, "d_layout10_extra_lines"); }

        [Test("roundtrip.resave_of_a_1x_folder_changes_nothing_25fps")]
        public static void ResaveA(TestEnv t) { RunScenario(t, "r_resave"); }

        [Test("roundtrip.resave_of_a_1x_folder_changes_nothing_drop_frame")]
        public static void ResaveB(TestEnv t) { RunScenario(t, "r_resave_drop_frame"); }

        [Test("roundtrip.resave_of_a_1x_folder_changes_nothing_120fps")]
        public static void ResaveE(TestEnv t) { RunScenario(t, "r_resave_120fps"); }

        [Test("roundtrip.parse_and_render_of_every_1x_data_file_and_export_are_identical")]
        public static void ParseRenderEveryFile(TestEnv t)
        {
            int checkedFiles = 0;
            foreach (string txt in Directory.GetFiles(Path.Combine(FixtureRoot, "scenarios"), "*_notes.txt", SearchOption.AllDirectories))
            {
                if (txt.Contains(@"\in\") && !txt.Contains(@"\r_resave")) continue; // hand-made 1.0 inputs may be CRLF and carry a BOM
                if (txt.Contains(@"\c_layout10\")) continue; // moved, not rewritten: it keeps its CRLF line ends
                byte[] bytes = File.ReadAllBytes(txt);
                string text = new UTF8Encoding(false, true).GetString(bytes);
                NotesFile nf = NotesFile.Parse(text);
                string rendered = nf.Render();
                if (rendered != text) throw new AssertException(txt + ": " + Describe(bytes, new UTF8Encoding(false).GetBytes(rendered)));
                string mdPath = Path.ChangeExtension(txt, ".md");
                string md = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(mdPath));
                if (nf.RenderReadable() != md) throw new AssertException(mdPath + ": export differs");
                checkedFiles++;
            }
            Check.True(checkedFiles >= 6, "files checked: " + checkedFiles);
        }

        [Test("roundtrip.settings_written_by_the_1x_core_are_kept_and_the_new_keys_appended")]
        public static void SettingsRoundTrip(TestEnv t)
        {
            string source = Path.Combine(FixtureRoot, "settings", "settings.json");
            string target = t.Combine("home", "settings.json");
            Fx.WriteBytes(target, File.ReadAllBytes(source));
            var s = new Settings(target);
            s.Load();
            Check.Equal(120, s.WindowX);
            Check.Equal(-30, s.WindowY);
            Check.Equal("D:\\clips " + (char)0xE9 + "\\sub", s.LastDir);
            Check.Equal(false, s.NotesPanelOpen);
            Check.Equal(false, s.AutoDownloadUpdates);
            Check.Equal(3, s.Recent.Count);
            Check.Equal(1760001999L, s.Recent[0].OpenedAt);
            Check.Equal(23.976, s.FpsOverride("D:\\clips\\v2.mp4"));
            s.Save();
            string original = File.ReadAllText(source, new UTF8Encoding(false));
            Check.EndsWith("\n}\n", original);
            string expected = original.Substring(0, original.Length - "\n}\n".Length)
                + ",\n  \"ui_scale\": 100,\n  \"theme\": \"black\",\n  \"author\": \"\"\n}\n";
            string actual = File.ReadAllText(target, new UTF8Encoding(false));
            if (expected != actual) throw new AssertException(Describe(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual)));
            // and a second save is stable
            s.Load();
            s.Save();
            Check.Equal(actual, File.ReadAllText(target, new UTF8Encoding(false)));
        }
    }
}
