using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>The fixtures and helpers of tests/test_core_session.py.</summary>
    public sealed class SessionCase
    {
        public static readonly byte[] Png = new byte[] { 0x89 }.Concat(Encoding.ASCII.GetBytes("PNG fake image bytes")).ToArray();

        public static Region Rect() { return Region.CreateRect(0.54, 0.36, 0.88, 0.8); }

        public readonly TestEnv T;
        public readonly Paths Paths;
        public readonly Settings Settings;
        public readonly List<SessionState> Events = new List<SessionState>();
        public readonly Session Session;
        public readonly string Clips;

        public SessionCase(TestEnv t)
        {
            T = t;
            Paths = new Paths();
            Settings = new Settings(Paths.SettingsFile);
            Settings.Load();
            Session = new Session(Settings, Events.Add);
            Clips = t.Combine("clips");
            Directory.CreateDirectory(Clips);
        }

        public string MakeVideo(string name = "promo.mp4", bool real = true)
        {
            string path = Path.Combine(Clips, name);
            if (real)
            {
                File.WriteAllBytes(path, Media.BuildMp4(new[] { Media.E(2310, 512) }, 12800, movieDuration: 92400));
            }
            else
            {
                var sb = new StringBuilder();
                for (int i = 0; i < 4; i++) sb.Append("not a real video, the probe sees unknown\n");
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes(sb.ToString()));
            }
            return path;
        }

        public string OpenUnknown(double fps = 25.0, string name = "dummy.mp4")
        {
            string video = MakeVideo(name, false);
            Session.Open(video);
            Session.SetFps(fps);
            Events.Clear();
            return video;
        }

        public string Path_(params string[] parts)
        {
            return Path.Combine(new[] { Clips }.Concat(parts).ToArray());
        }

        public List<string> ClipNames()
        {
            return Fx.Names(Clips);
        }

        public string Text(params string[] parts)
        {
            return Fx.ReadText(Path_(parts));
        }
    }

    /// <summary>The session; the test names follow the 1.x suite they match.</summary>
    public static class SessionTests
    {
        private static readonly string Em = " " + (char)0x2014 + " ";

        private static void AssertCounts(KindCounts c, int fix, int add, int remove)
        {
            Check.Equal(fix, c.Fix);
            Check.Equal(add, c.Add);
            Check.Equal(remove, c.Remove);
        }

        // --- OpenTest -----------------------------------------------------------------------------------------------

        [Test("test_core_session.OpenTest.test_open_real_probe")]
        public static void OpenRealProbe(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            SessionState data = c.Session.Open(video);
            VideoState v = data.Video!;
            Check.Equal("promo.mp4", v.Name);
            Check.Equal(1920, v.Width);
            Check.Equal(1080, v.Height);
            Check.Equal(25.0, v.Fps);
            Check.Equal("25", v.FpsText);
            Check.Equal("file", v.FpsSource);
            Check.Equal(2310, v.DurationFrames);
            Check.Near(92.4, v.Duration ?? 0);
            Check.Equal(video, v.FilePath);
            Check.Equal(c.Clips, v.Dir);
            Check.Equal(c.Path_("promo_notes", "promo_notes.txt"), v.NotesPath);
            Check.Equal(c.Path_("promo_notes", "promo_notes.zip"), v.PackagePath);
            Check.Equal(c.Path_("promo_notes", "frames"), v.ImagesDir);
            Check.Equal(25, data.Timecode!.Base);
            Check.Equal(false, data.Timecode.Drop);
            Check.Equal(2, data.Timecode.Digits);
            Check.Equal(":", data.Timecode.Separator);
            Check.Equal(0, data.Notes.Count);
            AssertCounts(data.Counts, 0, 0, 0);
            Check.Equal(new[] { "promo.mp4" }, c.ClipNames());
        }

        [Test("test_core_session.OpenTest.test_empty_payload_without_video")]
        public static void EmptyPayloadWithoutVideo(TestEnv t)
        {
            var c = new SessionCase(t);
            SessionState state = c.Session.GetState();
            Check.Null(state.Video);
            Check.Null(state.Timecode);
            Check.Equal(0, state.Notes.Count);
            AssertCounts(state.Counts, 0, 0, 0);
        }

        [Test("test_core_session.OpenTest.test_missing_video")]
        public static void MissingVideo(TestEnv t)
        {
            var c = new SessionCase(t);
            Check.Throws<SessionException>(() => c.Session.Open(c.Path_("nope.mp4")));
        }

        [Test("test_core_session.OpenTest.test_unknown_fps_blocks_notes")]
        public static void UnknownFpsBlocksNotes(TestEnv t)
        {
            var c = new SessionCase(t);
            c.Session.Open(c.MakeVideo("dummy.mp4", false));
            SessionState data = c.Session.GetState();
            Check.Equal("unknown", data.Video!.FpsSource);
            Check.Null(data.Video.Fps);
            Check.Null(data.Timecode);
            Check.Throws<SessionException>(() => c.Session.AddNote(0, "FIX", "x", null, null));
            Check.False(File.Exists(c.Path_("dummy_notes", "dummy_notes.txt")));
        }

        [Test("test_core_session.OpenTest.test_notes_file_of_another_source_is_refused")]
        public static void NotesFileOfAnotherSourceIsRefused(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes.txt"), "# Frame Notes 1\n# source: promo.mov\n# fps: 25\n0:00:01:00\tFIX\tx\n");
            SessionException ex = Check.Throws<SessionException>(() => c.Session.Open(video));
            Check.Contains("promo.mov", ex.Message);
            Check.Null(c.Session.GetState().Video);
        }

        [Test("test_core_session.OpenTest.test_newer_format_is_refused")]
        public static void NewerFormatIsRefused(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes.txt"), "# Frame Notes 9\n# source: promo.mp4\n# fps: 25\n");
            Check.Throws<SessionException>(() => c.Session.Open(video));
        }

        [Test("test_core_session.OpenTest.test_existing_notes_are_loaded_and_extra_lines_kept")]
        public static void ExistingNotesAreLoadedAndExtraLinesKept(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes.txt"),
                "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n"
                + "0:00:40:00\tADD\tsecond\n0:00:12:07\tFIX\tfirst\trect 0.100,0.100 0.200,0.200\n"
                + "hand written line\n");
            SessionState data = c.Session.Open(video);
            Check.Equal(new[] { 307, 1000 }, data.Notes.Select(n => n.Frame).ToList());
            Check.Equal("0:00:12:07", data.Notes[0].Timecode);
            AssertCounts(data.Counts, 1, 1, 0);
            c.Session.AddNote(10, "REMOVE", "third", null, null);
            string saved = c.Text("promo_notes", "promo_notes.txt");
            Check.EndsWith("hand written line\n", saved);
            Check.Contains("0:00:00:10\tREMOVE\tthird\n", saved);
            Check.Contains("# duration: 0:01:32:10\n", saved);
        }

        [Test("test_core_session.OpenTest.test_last_frame_is_remembered")]
        public static void LastFrameIsRemembered(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            c.Session.Open(video);
            c.Session.SetLastFrame(123);
            c.Session.Close();
            var reloaded = new Settings(c.Paths.SettingsFile);
            reloaded.Load();
            Check.Equal(123, reloaded.Recent[0].LastFrame);
            Check.Equal(c.Clips, reloaded.LastDir);
            var again = new Session(reloaded, d => { });
            Check.Equal(123, again.Open(video).Video!.LastFrame);
        }

        [Test("test_core_session.OpenTest.test_close_writes_no_notes_file")]
        public static void CloseWritesNoNotesFile(TestEnv t)
        {
            var c = new SessionCase(t);
            c.Session.Open(c.MakeVideo());
            c.Session.Close();
            Check.Equal(new[] { "promo.mp4" }, c.ClipNames());
            Check.Null(c.Session.GetState().Video);
        }

        // --- NotesTest ----------------------------------------------------------------------------------------------

        [Test("test_core_session.NotesTest.test_add_note_writes_file_and_calls_back")]
        public static void AddNoteWritesFileAndCallsBack(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.OpenUnknown();
            SessionState data = c.Session.AddNote(307, "FIX", "logo\tflickers\nnow ", null, null);
            string notesFile = c.Path_("dummy_notes", "dummy_notes.txt");
            Check.Equal("# Frame Notes 1\n# source: dummy.mp4\n# fps: 25\n0:00:12:07\tFIX\tlogo flickers now\n", c.Text("dummy_notes", "dummy_notes.txt"));
            Check.Equal(1, c.Events.Count);
            Check.True(ReferenceEquals(c.Events[0], data));
            Check.Equal("logo flickers now", data.Notes[0].Text);
            Check.Equal("0:00:12:07", data.Notes[0].Timecode);
            Check.True(Check.IsHex8(data.Notes[0].Id));
            Check.False(Directory.Exists(c.Path_("dummy_notes", "frames")));
            Check.Equal(c.Text("dummy_notes", "dummy_notes.txt"), c.Session.NotesText());
            Check.True(File.Exists(video));
            Check.Equal("dummy.mp4\n\n0:00:12:07 FIX" + Em + "logo flickers now\n", c.Session.ReadableText());
            Check.Equal(new[] { "dummy_notes.md", "dummy_notes.txt" }, Fx.Names(c.Path_("dummy_notes")));
            Check.Equal(c.Path_("dummy_notes"), data.Video!.NotesDir);
            Check.Equal(c.Path_("dummy_notes", "dummy_notes.zip"), data.Video.PackagePath);
            Check.Equal(c.Path_("dummy_notes", "frames"), data.Video.ImagesDir);
            Check.True(notesFile.Length > 0);
        }

        [Test("test_core_session.NotesTest.test_note_with_region_and_image")]
        public static void NoteWithRegionAndImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            SessionState data = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png);
            NoteState note = data.Notes[0];
            Check.Equal("0-00-12-07_FIX.png", note.Image);
            Check.Equal(c.Path_("dummy_notes", "frames", "0-00-12-07_FIX.png"), note.ImagePath);
            Check.Equal(SessionCase.Rect(), note.Region);
            Check.Equal(SessionCase.Png, File.ReadAllBytes(c.Path_("dummy_notes", "frames", "0-00-12-07_FIX.png")));
            Check.Contains("0:00:12:07\tFIX\tlogo\trect 0.540,0.360 0.880,0.800\t0-00-12-07_FIX.png\n", c.Text("dummy_notes", "dummy_notes.txt"));
        }

        [Test("test_core_session.NotesTest.test_image_name_collision_and_no_image_without_region")]
        public static void ImageNameCollisionAndNoImageWithoutRegion(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(307, "FIX", "a", SessionCase.Rect(), SessionCase.Png);
            SessionState data = c.Session.AddNote(307, "FIX", "b", Region.CreatePoint(0.5, 0.5), SessionCase.Png.Concat(new byte[] { (byte)'2' }).ToArray());
            Check.Equal(new[] { "0-00-12-07_FIX.png", "0-00-12-07_FIX_2.png" }, data.Notes.Select(n => n.Image).ToList());
            data = c.Session.AddNote(308, "ADD", "c", null, SessionCase.Png);
            Check.Null(data.Notes[2].Image);
            Check.Equal(new[] { "0-00-12-07_FIX.png", "0-00-12-07_FIX_2.png" }, Fx.Names(c.Path_("dummy_notes", "frames")));
        }

        [Test("test_core_session.NotesTest.test_notes_are_sorted_with_order_tie_break")]
        public static void NotesAreSortedWithOrderTieBreak(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(100, "ADD", "late first", null, null);
            c.Session.AddNote(50, "FIX", "early", null, null);
            SessionState data = c.Session.AddNote(100, "REMOVE", "late second", null, null);
            Check.Equal(new[] { "early", "late first", "late second" }, data.Notes.Select(n => n.Text).ToList());
            Check.Equal(new[] { 2, 1, 3 }, data.Notes.Select(n => n.Order).ToList());
        }

        [Test("test_core_session.NotesTest.test_remove_note_deletes_image_and_empty_folder_keeps_header_file")]
        public static void RemoveNoteDeletesImageAndEmptyFolderKeepsHeaderFile(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string first = c.Session.AddNote(307, "FIX", "a", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            string second = c.Session.AddNote(400, "ADD", "b", SessionCase.Rect(), SessionCase.Png).Notes[1].Id;
            c.Events.Clear();
            c.Session.RemoveNote(first);
            Check.False(File.Exists(c.Path_("dummy_notes", "frames", "0-00-12-07_FIX.png")));
            Check.True(Directory.Exists(c.Path_("dummy_notes", "frames")));
            Check.Equal(1, c.Events.Count);
            Check.Equal(1, c.Events[0].Notes.Count);
            c.Session.RemoveNote(second);
            Check.False(Directory.Exists(c.Path_("dummy_notes", "frames")));
            Check.Equal("# Frame Notes 1\n# source: dummy.mp4\n# fps: 25\n", c.Text("dummy_notes", "dummy_notes.txt"));
            Check.Equal(0, c.Events[c.Events.Count - 1].Notes.Count);
            Check.Equal(2, c.Events.Count);
            Check.Equal(new[] { "dummy_notes.md", "dummy_notes.txt" }, Fx.Names(c.Path_("dummy_notes")));
            Check.Equal("dummy.mp4\n\nNo notes.\n", c.Session.ReadableText());
        }

        [Test("test_core_session.NotesTest.test_update_text_region_and_image")]
        public static void UpdateTextRegionAndImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string noteId = c.Session.AddNote(307, "FIX", "old", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            string image = c.Path_("dummy_notes", "frames", "0-00-12-07_FIX.png");
            SessionState data = c.Session.UpdateNote(noteId, text: "new\ntext");
            Check.Equal("new text", data.Notes[0].Text);
            Check.Equal(SessionCase.Rect(), data.Notes[0].Region);
            // a new region without a new image keeps the old image file
            Region moved = Region.CreateRect(0.1, 0.1, 0.2, 0.2);
            data = c.Session.UpdateNote(noteId, region: moved);
            Check.Equal(moved, data.Notes[0].Region);
            Check.Equal("0-00-12-07_FIX.png", data.Notes[0].Image);
            Check.Equal(SessionCase.Png, File.ReadAllBytes(image));
            // a new image replaces the bytes under the same name
            c.Session.UpdateNote(noteId, region: moved, imagePng: Encoding.ASCII.GetBytes("new bytes"));
            Check.Equal(Encoding.ASCII.GetBytes("new bytes"), File.ReadAllBytes(image));
            Check.Equal(new[] { Path.GetFileName(image) }, Fx.Names(Path.GetDirectoryName(image)!));
            // removing the region removes the image
            data = c.Session.UpdateNote(noteId, clearRegion: true);
            Check.Null(data.Notes[0].Region);
            Check.Null(data.Notes[0].Image);
            Check.False(Directory.Exists(Path.GetDirectoryName(image)!));
            Check.EndsWith("0:00:12:07\tFIX\tnew text\n", c.Text("dummy_notes", "dummy_notes.txt"));
            // a region added later gets an image name when an image comes with it
            data = c.Session.UpdateNote(noteId, region: SessionCase.Rect(), imagePng: SessionCase.Png);
            Check.Equal("0-00-12-07_FIX.png", data.Notes[0].Image);
            Check.True(File.Exists(image));
        }

        [Test("test_core_session.NotesTest.test_errors")]
        public static void Errors(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            SessionException ex = Check.Throws<SessionException>(() => c.Session.AddNote(1, "BUG", "x", null, null));
            Check.Equal("Unknown note type", ex.Message);
            Check.Throws<SessionException>(() => c.Session.AddNote(1, "FIX", "x", new Region("rect", 2, 0, 1, 1), null));
            Check.Throws<SessionException>(() => c.Session.AddNote(-1, "FIX", "x", null, null));
            Check.Throws<SessionException>(() => c.Session.UpdateNote("deadbeef", text: "x"));
            Check.Throws<SessionException>(() => c.Session.RemoveNote("deadbeef"));
            Check.False(Directory.Exists(c.Path_("dummy_notes")));
            Check.Equal(0, c.Events.Count);
        }

        [Test("test_core_session.NotesTest.test_mutation_without_video_is_refused")]
        public static void MutationWithoutVideoIsRefused(TestEnv t)
        {
            var c = new SessionCase(t);
            Check.Equal("No video is open", Check.Throws<SessionException>(() => c.Session.AddNote(1, "FIX", "x", null, null)).Message);
            Check.Throws<SessionException>(() => c.Session.SetFps(25));
        }

        [Test("test_core_session.NotesTest.test_write_failure_keeps_note_in_memory_and_reports")]
        public static void WriteFailureKeepsNoteInMemoryAndReports(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string target = c.Path_("dummy_notes", "dummy_notes.txt");
            Directory.CreateDirectory(target); // a folder in the way makes every save of the data file fail
            SessionException ex = Check.Throws<SessionException>(() => c.Session.AddNote(10, "FIX", "kept", null, null));
            Check.Contains("could not be saved", ex.Message);
            Check.Contains(target, ex.Message);
            Check.Equal(1, c.Events.Count);
            Check.Equal("kept", c.Events[0].Notes[0].Text);
            Directory.Delete(target);
            c.Session.AddNote(20, "ADD", "next", null, null);
            string saved = c.Text("dummy_notes", "dummy_notes.txt");
            Check.Contains("kept", saved);
            Check.Contains("next", saved);
        }

        [Test("test_core_session.NotesTest.test_unsafe_image_name_from_a_file_is_never_deleted")]
        public static void UnsafeImageNameFromAFileIsNeverDeleted(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            string victim = c.Path_("victim.txt");
            Fx.WriteText(victim, "keep me");
            Fx.WriteText(c.Path_("promo_notes.txt"),
                "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n0:00:01:00\tFIX\tx\t..\\victim.txt\n");
            SessionState data = c.Session.Open(video);
            Check.Null(data.Notes[0].ImagePath);
            c.Session.RemoveNote(data.Notes[0].Id);
            Check.Equal("keep me", Fx.ReadText(victim));
        }

        // --- FpsTest ------------------------------------------------------------------------------------------------

        [Test("test_core_session.FpsTest.test_precedence_override_then_probe")]
        public static void PrecedenceOverrideThenProbe(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            c.Settings.SetFpsOverride(video, 30);
            SessionState data = c.Session.Open(video);
            Check.Equal(30.0, data.Video!.Fps);
            Check.Equal("override", data.Video.FpsSource);
            data = c.Session.SetFps(null);
            Check.Equal(25.0, data.Video!.Fps);
            Check.Equal("file", data.Video.FpsSource);
            Check.Null(c.Settings.FpsOverride(video));
        }

        [Test("test_core_session.FpsTest.test_set_fps_records_override_only_when_different_from_probe")]
        public static void SetFpsRecordsOverrideOnlyWhenDifferentFromProbe(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            c.Session.Open(video);
            SessionState data = c.Session.SetFps(29.97);
            Check.Equal("29.97", data.Video!.FpsText);
            Check.Equal("override", data.Video.FpsSource);
            Check.Equal(true, data.Timecode!.Drop);
            Check.Equal(29.97, c.Settings.FpsOverride(video));
            data = c.Session.SetFps(25.0);
            Check.Equal("file", data.Video!.FpsSource);
            Check.Null(c.Settings.FpsOverride(video));
            Check.Throws<SessionException>(() => c.Session.SetFps(0));
            Check.Throws<SessionException>(() => c.Session.SetFps(double.NaN));
        }

        [Test("test_core_session.FpsTest.test_set_fps_rewrites_timecodes_and_image_names")]
        public static void SetFpsRewritesTimecodesAndImageNames(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown(25.0);
            c.Session.AddNote(307, "FIX", "a", SessionCase.Rect(), SessionCase.Png);
            c.Session.AddNote(1800, "ADD", "b", SessionCase.Rect(), SessionCase.Png.Concat(new byte[] { (byte)'b' }).ToArray());
            string images = c.Path_("dummy_notes", "frames");
            Check.Equal(new[] { "0-00-12-07_FIX.png", "0-01-12-00_ADD.png" }, Fx.Names(images));
            c.Events.Clear();
            SessionState data = c.Session.SetFps(29.97);
            Check.Equal(1, c.Events.Count);
            Check.Equal(new[] { 307, 1800 }, data.Notes.Select(n => n.Frame).ToList());
            Check.Equal(new[] { "0;00;10;07", "0;01;00;02" }, data.Notes.Select(n => n.Timecode).ToList());
            Check.Equal(new[] { "0-00-10-07_FIX.png", "0-01-00-02_ADD.png" }, data.Notes.Select(n => n.Image).ToList());
            Check.Equal(new[] { "0-00-10-07_FIX.png", "0-01-00-02_ADD.png" }, Fx.Names(images));
            Check.Equal(SessionCase.Png.Concat(new byte[] { (byte)'b' }).ToArray(), File.ReadAllBytes(Path.Combine(images, "0-01-00-02_ADD.png")));
            string text = c.Text("dummy_notes", "dummy_notes.txt");
            Check.Contains("# fps: 29.97\n", text);
            Check.Contains("0;01;00;02\tADD\tb\t", text);
            Check.Contains("0-01-00-02_ADD.png", text);
            // the new file loads again with the same frames
            var other = new Session(c.Settings, d => { });
            SessionState again = other.Open(c.Path_("dummy.mp4"));
            Check.Equal(new[] { 307, 1800 }, again.Notes.Select(n => n.Frame).ToList());
        }

        [Test("test_core_session.FpsTest.test_set_fps_without_notes_writes_no_file")]
        public static void SetFpsWithoutNotesWritesNoFile(TestEnv t)
        {
            var c = new SessionCase(t);
            c.Session.Open(c.MakeVideo("dummy.mp4", false));
            c.Session.SetFps(24);
            Check.False(File.Exists(c.Path_("dummy_notes", "dummy_notes.txt")));
        }

        [Test("test_core_session.FpsTest.test_unknown_probe_uses_fps_of_existing_notes_file")]
        public static void UnknownProbeUsesFpsOfExistingNotesFile(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo("dummy.mp4", false);
            Fx.WriteText(c.Path_("dummy_notes.txt"), "# Frame Notes 1\n# source: dummy.mp4\n# fps: 30\n0:00:01:00\tFIX\tx\n");
            SessionState data = c.Session.Open(video);
            Check.Equal(30.0, data.Video!.Fps);
            Check.Equal(30, data.Notes[0].Frame);
        }

        // --- LayoutTest ---------------------------------------------------------------------------------------------

        private const string Legacy =
            "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n"
            + "0:00:12:07\tFIX\tlogo\trect 0.100,0.100 0.200,0.200\t0-00-12-07_FIX.png\n"
            + "0:00:40:00\tADD\tsubtitle\n";

        private static string MakeLegacy(SessionCase c, bool images = true)
        {
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes.txt"), Legacy);
            if (images) Fx.WriteBytes(c.Path_("promo_notes", "0-00-12-07_FIX.png"), SessionCase.Png);
            return video;
        }

        [Test("test_core_session.LayoutTest.test_open_moves_the_1_0_layout_once")]
        public static void OpenMovesThe10LayoutOnce(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = MakeLegacy(c);
            var logs = new List<string>();
            Log.Listener = (level, message) => logs.Add(level + " " + message);
            SessionState data = c.Session.Open(video);
            Check.True(logs.Any(l => l.StartsWith("INFO moved the notes of promo.mp4", StringComparison.Ordinal)));
            string folder = c.Path_("promo_notes");
            Check.Equal(new[] { "promo.mp4", "promo_notes" }, c.ClipNames());
            Check.Equal(new[] { "frames", "promo_notes.md", "promo_notes.txt" }, Fx.Names(folder));
            Check.Equal(Legacy, Fx.ReadText(Path.Combine(folder, "promo_notes.txt")));
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Path.Combine(folder, "frames", "0-00-12-07_FIX.png")));
            Check.Equal(Path.Combine(folder, "frames", "0-00-12-07_FIX.png"), data.Notes[0].ImagePath);
            // a second open finds the new layout and moves nothing
            Check.True(c.Session.Open(video).Notes.Count > 0);
        }

        [Test("test_core_session.LayoutTest.test_data_file_only_and_images_only")]
        public static void DataFileOnlyAndImagesOnly(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = MakeLegacy(c, false);
            c.Session.Open(video);
            Check.Equal(new[] { "promo_notes.md", "promo_notes.txt" }, Fx.Names(c.Path_("promo_notes")));
            string other = c.MakeVideo("other.mp4");
            Fx.WriteBytes(c.Path_("other_notes", "a.png"), SessionCase.Png);
            SessionState data = c.Session.Open(other);
            Check.Equal(0, data.Notes.Count);
            Check.True(File.Exists(c.Path_("other_notes", "frames", "a.png")));
            Check.False(File.Exists(c.Path_("other_notes", "a.png")));
        }

        [Test("test_core_session.LayoutTest.test_interrupted_migration_is_finished_on_the_next_open")]
        public static void InterruptedMigrationIsFinishedOnTheNextOpen(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = MakeLegacy(c);
            // A reader that does not share delete access makes the rename of the data file fail after the images moved.
            using (var hold = new FileStream(c.Path_("promo_notes.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SessionException ex = Check.Throws<SessionException>(() => c.Session.Open(video));
                Check.Contains("could not be moved into the promo_notes folder", ex.Message);
            }
            // readable state: images already in frames\, data file still beside the video
            Check.True(File.Exists(c.Path_("promo_notes", "frames", "0-00-12-07_FIX.png")));
            Check.True(File.Exists(c.Path_("promo_notes.txt")));
            SessionState data = c.Session.Open(video);
            Check.Equal(2, data.Notes.Count);
            Check.False(File.Exists(c.Path_("promo_notes.txt")));
            Check.True(File.Exists(c.Path_("promo_notes", "promo_notes.txt")));
        }

        [Test("test_core_session.LayoutTest.test_new_layout_wins_over_a_legacy_file")]
        public static void NewLayoutWinsOverALegacyFile(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes", "promo_notes.txt"), "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n0:00:01:00\tFIX\tnew layout\n");
            Fx.WriteText(c.Path_("promo_notes.txt"), Legacy);
            SessionState data = c.Session.Open(video);
            Check.Equal(new[] { "new layout" }, data.Notes.Select(n => n.Text).ToList());
            Check.Equal(Legacy, c.Text("promo_notes.txt"));
        }

        [Test("test_core_session.LayoutTest.test_legacy_file_of_another_video_is_left_and_refused")]
        public static void LegacyFileOfAnotherVideoIsLeftAndRefused(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes.txt"), Legacy.Replace("promo.mp4", "promo.mov"));
            Fx.WriteBytes(c.Path_("promo_notes", "0-00-12-07_FIX.png"), SessionCase.Png);
            SessionException ex = Check.Throws<SessionException>(() => c.Session.Open(video));
            Check.Contains("promo.mov", ex.Message);
            Check.True(File.Exists(c.Path_("promo_notes.txt")));
            Check.True(File.Exists(c.Path_("promo_notes", "0-00-12-07_FIX.png")));
        }

        [Test("session.readable_text_is_plain_text_without_images")]
        public static void ReadableTextIsPlain(TestEnv t)
        {
            var c = new SessionCase(t);
            Check.Equal("", c.Session.ReadableText());
            c.Session.Open(MakeLegacy(c));
            Check.Equal("promo.mp4\n\n0:00:12:07 FIX" + Em + "logo\n0:00:40:00 ADD" + Em + "subtitle\n", c.Session.ReadableText());
        }

        [Test("session.set_last_frame_saves_the_settings_at_most_every_two_seconds")]
        public static void SetLastFrameThrottlesSettingsWrites(TestEnv t)
        {
            var c = new SessionCase(t);
            c.Session.Open(c.MakeVideo());
            File.Delete(c.Paths.SettingsFile);
            c.Session.SetLastFrame(5);
            Check.False(File.Exists(c.Paths.SettingsFile), "written less than two seconds after the last save");
            c.Session.SetLastFrame(-7);
            Check.Equal(0, c.Session.GetState().Video!.LastFrame);
            c.Session.Close();
            Check.True(File.Exists(c.Paths.SettingsFile), "close saves");
        }
    

        [Test("session.a_video_name_with_a_leading_space_opens_again_after_a_save")]
        public static void LeadingSpaceInTheName(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo(" intro.mp4");
            c.Session.Open(video);
            c.Session.AddNote(5, "FIX", "x", null, null);
            Check.Contains("# source: intro.mp4\n", c.Text(" intro_notes", " intro_notes.txt"));
            c.Session.Close();
            Check.Equal(1, c.Session.Open(video).Notes.Count, "the stripped source names this video");
            Check.Equal(1, NotesLayout.NotesSummaryFor(video)!.Notes);
            c.Session.Close();
            Check.Equal(1, NotesLayout.DeleteNotes(video).Notes);
        }

        [Test("session.open_refuses_a_path_without_a_full_form")]
        public static void OpenRefusesAnInvalidPath(TestEnv t)
        {
            var c = new SessionCase(t);
            Check.Equal("The video path is not valid", Check.Throws<SessionException>(() => c.Session.Open(@"C:\a|b.mp4")).Message);
            Check.Equal("The video path is not valid", Check.Throws<SessionException>(() => c.Session.Open(@"C:\a\b:c.mp4")).Message);
        }

        [Test("session.pack_takes_the_notes_under_the_lock_and_writes_after_it")]
        public static void PackWritesOutsideTheLock(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(307, "FIX", "held", SessionCase.Rect(), SessionCase.Png);
            string zip = c.Path_("dummy_notes", "dummy_notes.zip");
            c.Session.Pack();
            // While the package cannot replace the old zip (another program holds it; the replace retries for most of a
            // second), the session answers other calls: the archive is written after the session lock is released.
            Exception? failure = null;
            var worker = new System.Threading.Thread(() =>
            {
                try { c.Session.Pack(); }
                catch (Exception ex) { failure = ex; }
            });
            long waited;
            using (new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                worker.Start();
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (!File.Exists(zip + ".part") && wait.ElapsedMilliseconds < 5000) System.Threading.Thread.Sleep(5);
                Check.True(File.Exists(zip + ".part"), "the package is being written");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                c.Session.GetState();
                c.Session.SetLastFrame(12);
                waited = clock.ElapsedMilliseconds;
            }
            worker.Join();
            Check.True(waited < 150, "the session waited " + waited + " ms for the package");
            Check.True(failure == null || failure is SessionException, failure?.ToString() ?? "");
        }
    }
}
