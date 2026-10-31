using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>General notes, empty-text refusal, kind change, move, the Markdown notes file, failed writes and the package.</summary>
    public static class SessionNotesTests
    {
        private static readonly string Em = " " + (char)0x2014 + " ";
        private static readonly byte[] PngB = SessionCase.Png.Concat(new byte[] { (byte)'b' }).ToArray();
        private const string Header = "# Frame Notes 1\n# source: dummy.mp4\n# fps: 25\n";

        private static string NotesTxt(SessionCase c)
        {
            return c.Text("dummy_notes", "dummy_notes.txt");
        }

        private static List<string> Texts(SessionState state)
        {
            return state.Notes.Select(n => n.Text).ToList();
        }

        // --- general notes -------------------------------------------------------------------------------------------

        [Test("session.general_note_is_added_listed_first_counted_and_written")]
        public static void GeneralNoteIsAddedListedFirstCountedAndWritten(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png);
            SessionState data = c.Session.AddNote(null, "add", "  music is\tloud  ", null, SessionCase.Png);
            Check.Equal(2, c.Events.Count);
            Check.Equal(new[] { "music is loud", "logo" }, Texts(data));
            NoteState general = data.Notes[0];
            Check.True(general.IsGeneral);
            Check.Null(general.Frame);
            Check.Equal("", general.Timecode);
            Check.Equal("ADD", general.Kind);
            Check.Null(general.Region);
            Check.Null(general.Image);
            Check.Null(general.ImagePath);
            Check.Equal(2, general.Order);
            Check.False(data.Notes[1].IsGeneral);
            Check.Equal(307, data.Notes[1].Frame);
            Check.Equal("0:00:12:07", data.Notes[1].Timecode);
            Check.Equal(1, data.Counts.Fix);
            Check.Equal(1, data.Counts.Add);
            Check.Equal(0, data.Counts.Remove);
            Check.Equal(Header + "-\tADD\tmusic is loud\n0:00:12:07\tFIX\tlogo\trect 0.540,0.360 0.880,0.800\t0-00-12-07_FIX.png\n", NotesTxt(c));
            Check.Equal("dummy.mp4\n\nGeneral\nADD" + Em + "music is loud\n\n0:00:12:07 FIX" + Em + "logo\n", c.Session.ReadableText());
            Check.Equal(new[] { "0-00-12-07_FIX.png" }, Fx.Names(c.Path_("dummy_notes", "frames")), "no image for the general note");
        }

        [Test("session.general_note_only_creates_the_file_and_survives_reopen_and_rate_change")]
        public static void GeneralNoteSurvivesReopenAndRateChange(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.OpenUnknown();
            c.Session.AddNote(null, "REMOVE", "cut the outro", null, null);
            Check.Equal(Header + "-\tREMOVE\tcut the outro\n", NotesTxt(c));
            Check.False(Directory.Exists(c.Path_("dummy_notes", "frames")));
            c.Session.AddNote(1800, "FIX", "frame", SessionCase.Rect(), SessionCase.Png);
            SessionState data = c.Session.SetFps(29.97);
            Check.Null(data.Notes[0].Frame);
            Check.Equal("", data.Notes[0].Timecode);
            Check.Equal("0;01;00;02", data.Notes[1].Timecode);
            var other = new Session(c.Settings, d => { });
            SessionState again = other.Open(video);
            Check.Equal(new[] { "cut the outro", "frame" }, Texts(again));
            Check.True(again.Notes[0].IsGeneral);
        }

        [Test("session.open_reads_a_general_line_that_follows_the_notes_of_frames")]
        public static void OpenReadsAGeneralLineAfterTheFrameNotes(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes", "promo_notes.txt"),
                "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n0:00:12:07\tFIX\tlogo\n-\tADD\tgeneral\nhand written\n");
            SessionState data = c.Session.Open(video);
            Check.Equal(new[] { "general", "logo" }, Texts(data));
            c.Session.AddNote(null, "FIX", "another", null, null);
            string saved = Fx.ReadText(c.Path_("promo_notes", "promo_notes.txt"));
            Check.Contains("# duration: 0:01:32:10\n-\tADD\tgeneral\n-\tFIX\tanother\n0:00:12:07\tFIX\tlogo\nhand written\n", saved);
        }

        [Test("session.general_note_refuses_a_region_and_ignores_an_image")]
        public static void GeneralNoteRefusesARegion(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            SessionException ex = Check.Throws<SessionException>(() => c.Session.AddNote(null, "FIX", "x", SessionCase.Rect(), SessionCase.Png));
            Check.Equal("A general note has no frame to mark", ex.Message);
            Check.False(Directory.Exists(c.Path_("dummy_notes")));
            Check.Equal(0, c.Events.Count);
            string id = c.Session.AddNote(null, "FIX", "x", null, null).Notes[0].Id;
            ex = Check.Throws<SessionException>(() => c.Session.UpdateNote(id, region: Region.CreatePoint(0.5, 0.5), imagePng: SessionCase.Png));
            Check.Equal("A general note has no frame to mark", ex.Message);
            Check.Null(c.Session.GetState().Notes[0].Region);
            // clearing a region that does not exist changes nothing
            int events = c.Events.Count;
            c.Session.UpdateNote(id, clearRegion: true, imagePng: SessionCase.Png);
            Check.Equal(events, c.Events.Count);
        }

        [Test("session.general_note_changes_text_and_kind")]
        public static void GeneralNoteChangesTextAndKind(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(null, "FIX", "first", null, null).Notes[0].Id;
            SessionState data = c.Session.UpdateNote(id, text: "second", kind: "remove");
            Check.Equal("second", data.Notes[0].Text);
            Check.Equal("REMOVE", data.Notes[0].Kind);
            Check.Equal(0, data.Counts.Fix);
            Check.Equal(1, data.Counts.Remove);
            Check.Equal(Header + "-\tREMOVE\tsecond\n", NotesTxt(c));
        }

        // --- empty text ------------------------------------------------------------------------------------------------

        [Test("session.empty_text_is_refused_on_add_and_update")]
        public static void EmptyTextIsRefused(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            foreach (string? bad in new string?[] { null, "", "   ", "\t\r\n" })
            {
                Check.Equal("The note has no text", Check.Throws<SessionException>(() => c.Session.AddNote(5, "FIX", bad, null, null)).Message);
                Check.Equal("The note has no text", Check.Throws<SessionException>(() => c.Session.AddNote(null, "FIX", bad, null, null)).Message);
            }
            Check.False(Directory.Exists(c.Path_("dummy_notes")));
            Check.Equal(0, c.Events.Count);
            string id = c.Session.AddNote(5, "FIX", "kept", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            c.Events.Clear();
            foreach (string bad in new[] { "", " \t ", "\n" })
            {
                Check.Equal("The note has no text", Check.Throws<SessionException>(() => c.Session.UpdateNote(id, text: bad)).Message);
            }
            Check.Equal(0, c.Events.Count);
            Check.Equal("kept", c.Session.GetState().Notes[0].Text);
            // a null text keeps the text while another field changes
            SessionState data = c.Session.UpdateNote(id, text: null, kind: "ADD");
            Check.Equal("kept", data.Notes[0].Text);
            Check.Equal("ADD", data.Notes[0].Kind);
        }

        [Test("session.a_note_with_empty_text_from_a_file_loads_and_stays")]
        public static void EmptyTextFromAFileLoads(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes", "promo_notes.txt"),
                "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n-\tFIX\t\n0:00:01:00\tADD\t\n");
            SessionState data = c.Session.Open(video);
            Check.Equal(2, data.Notes.Count);
            Check.Equal("promo.mp4\n\nGeneral\nFIX\n\n0:00:01:00 ADD\n", c.Session.ReadableText());
            c.Session.AddNote(50, "FIX", "new", null, null);
            Check.Contains("-\tFIX\t\n0:00:01:00\tADD\t\n", Fx.ReadText(c.Path_("promo_notes", "promo_notes.txt")));
        }

        // --- UpdateNote: nothing changed, kind change -----------------------------------------------------------------

        [Test("session.update_that_changes_nothing_writes_and_publishes_nothing")]
        public static void UpdateThatChangesNothing(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            string path = c.Path_("dummy_notes", "dummy_notes.txt");
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            c.Events.Clear();
            SessionState data = c.Session.UpdateNote(id, text: "logo", region: SessionCase.Rect(), kind: "fix");
            Check.Equal(0, c.Events.Count);
            Check.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
            Check.Equal("logo", data.Notes[0].Text);
            c.Session.UpdateNote(id);
            Check.Equal(0, c.Events.Count);
            c.Session.UpdateNote(id, text: "logo 2");
            Check.Equal(1, c.Events.Count);
        }

        [Test("session.unknown_kind_on_update_is_refused")]
        public static void UnknownKindOnUpdateIsRefused(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", null, null).Notes[0].Id;
            c.Events.Clear();
            Check.Equal("Unknown note type", Check.Throws<SessionException>(() => c.Session.UpdateNote(id, kind: "BUG")).Message);
            Check.Equal(0, c.Events.Count);
            Check.Equal("FIX", c.Session.GetState().Notes[0].Kind);
        }

        [Test("session.kind_change_renames_the_image")]
        public static void KindChangeRenamesTheImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            // an ADD note already owns the name the change would take: the image gets _2
            c.Session.AddNote(307, "ADD", "other", SessionCase.Rect(), PngB);
            string frames = c.Path_("dummy_notes", "frames");
            Check.Equal(new[] { "0-00-12-07_ADD.png", "0-00-12-07_FIX.png" }, Fx.Names(frames));
            SessionState data = c.Session.UpdateNote(id, kind: "ADD");
            NoteState moved = data.Notes.First(n => n.Id == id);
            Check.Equal("ADD", moved.Kind);
            Check.Equal("0-00-12-07_ADD_2.png", moved.Image);
            Check.Equal(Path.Combine(frames, "0-00-12-07_ADD_2.png"), moved.ImagePath);
            Check.Equal(new[] { "0-00-12-07_ADD.png", "0-00-12-07_ADD_2.png" }, Fx.Names(frames));
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Path.Combine(frames, "0-00-12-07_ADD_2.png")));
            Check.Contains("0:00:12:07\tADD\tlogo\trect 0.540,0.360 0.880,0.800\t0-00-12-07_ADD_2.png\n", NotesTxt(c));
            // with new bytes the image is written under the new name and the old file is deleted
            data = c.Session.UpdateNote(id, kind: "REMOVE", region: SessionCase.Rect(), imagePng: Encoding.ASCII.GetBytes("fresh"));
            Check.Equal("0-00-12-07_REMOVE.png", data.Notes.First(n => n.Id == id).Image);
            Check.Equal(new[] { "0-00-12-07_ADD.png", "0-00-12-07_REMOVE.png" }, Fx.Names(frames));
            Check.Equal(Encoding.ASCII.GetBytes("fresh"), File.ReadAllBytes(Path.Combine(frames, "0-00-12-07_REMOVE.png")));
        }

        [Test("session.kind_change_of_a_note_without_an_image_only_changes_the_kind")]
        public static void KindChangeWithoutImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", null, null).Notes[0].Id;
            SessionState data = c.Session.UpdateNote(id, kind: "ADD");
            Check.Equal("ADD", data.Notes[0].Kind);
            Check.Null(data.Notes[0].Image);
            Check.False(Directory.Exists(c.Path_("dummy_notes", "frames")));
            Check.Equal(Header + "0:00:12:07\tADD\tlogo\n", NotesTxt(c));
        }

        // --- MoveNote --------------------------------------------------------------------------------------------------

        [Test("session.move_note_changes_the_frame_and_renames_the_image")]
        public static void MoveNoteChangesTheFrameAndRenamesTheImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(10, "ADD", "first", null, null);
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[1].Id;
            c.Session.AddNote(2000, "REMOVE", "last", null, null);
            c.Events.Clear();
            string frames = c.Path_("dummy_notes", "frames");
            SessionState data = c.Session.MoveNote(id, 1800, null);
            Check.Equal(1, c.Events.Count);
            Check.Equal(new[] { "first", "logo", "last" }, Texts(data));
            NoteState note = data.Notes[1];
            Check.Equal(id, note.Id);
            Check.Equal(1800, note.Frame);
            Check.Equal("0:01:12:00", note.Timecode);
            Check.Equal("FIX", note.Kind);
            Check.Equal(SessionCase.Rect(), note.Region);
            Check.Equal(2, note.Order);
            Check.Equal("0-01-12-00_FIX.png", note.Image);
            Check.Equal(new[] { "0-01-12-00_FIX.png" }, Fx.Names(frames));
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Path.Combine(frames, "0-01-12-00_FIX.png")), "bytes kept");
            Check.Contains("0:01:12:00\tFIX\tlogo\trect 0.540,0.360 0.880,0.800\t0-01-12-00_FIX.png\n", NotesTxt(c));
            // past the last note: the order in the list follows the frame; a name taken by another note gets _2
            c.Session.AddNote(2500, "FIX", "other", SessionCase.Rect(), PngB);
            data = c.Session.MoveNote(id, 2500, null);
            Check.Equal(new[] { "first", "last", "logo", "other" }, Texts(data), "same frame: the creation order decides");
            Check.Equal("0-01-40-00_FIX_2.png", data.Notes[2].Image);
            Check.Equal("0-01-40-00_FIX.png", data.Notes[3].Image);
            Check.Equal(new[] { "0-01-40-00_FIX.png", "0-01-40-00_FIX_2.png" }, Fx.Names(frames));
        }

        [Test("session.move_note_with_new_bytes_replaces_the_image")]
        public static void MoveNoteWithNewBytes(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            string frames = c.Path_("dummy_notes", "frames");
            c.Session.MoveNote(id, 400, PngB);
            Check.Equal(new[] { "0-00-16-00_FIX.png" }, Fx.Names(frames));
            Check.Equal(PngB, File.ReadAllBytes(Path.Combine(frames, "0-00-16-00_FIX.png")));
            // the same frame with new bytes rewrites the image in place
            c.Events.Clear();
            byte[] third = Encoding.ASCII.GetBytes("third");
            c.Session.MoveNote(id, 400, third);
            Check.Equal(1, c.Events.Count);
            Check.Equal(third, File.ReadAllBytes(Path.Combine(frames, "0-00-16-00_FIX.png")));
            // a note without an image takes no bytes
            string plain = c.Session.AddNote(50, "ADD", "plain", null, null).Notes[0].Id;
            SessionState data = c.Session.MoveNote(plain, 60, third);
            Check.Null(data.Notes[0].Image);
            Check.Equal(new[] { "0-00-16-00_FIX.png" }, Fx.Names(frames));
        }

        [Test("session.move_note_to_the_same_frame_is_a_no_op")]
        public static void MoveNoteToTheSameFrameIsANoOp(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            string path = c.Path_("dummy_notes", "dummy_notes.txt");
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            c.Events.Clear();
            SessionState data = c.Session.MoveNote(id, 307, null);
            Check.Equal(0, c.Events.Count);
            Check.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
            Check.Equal(307, data.Notes[0].Frame);
        }

        [Test("session.move_note_refuses_a_general_note_a_negative_frame_and_unknown_notes")]
        public static void MoveNoteRefusals(TestEnv t)
        {
            var c = new SessionCase(t);
            Check.Equal("No video is open", Check.Throws<SessionException>(() => c.Session.MoveNote("deadbeef", 1, null)).Message);
            c.OpenUnknown();
            string general = c.Session.AddNote(null, "FIX", "g", null, null).Notes[0].Id;
            string framed = c.Session.AddNote(5, "FIX", "f", null, null).Notes[1].Id;
            c.Events.Clear();
            Check.Equal("The frame number is not valid", Check.Throws<SessionException>(() => c.Session.MoveNote(framed, -1, null)).Message);
            Check.Equal("The frame number is not valid", Check.Throws<SessionException>(() => c.Session.MoveNote(general, -1, null)).Message);
            Check.Equal("The note was not found", Check.Throws<SessionException>(() => c.Session.MoveNote("deadbeef", 5, null)).Message);
            Check.Equal(0, c.Events.Count);
            Check.Equal(5, c.Session.GetState().Notes[1].Frame);
        }

        [Test("session.move_note_to_the_whole_video_drops_the_region_and_image")]
        public static void MoveNoteToTheWholeVideo(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(10, "ADD", "first", null, null);
            string id = c.Session.AddNote(307, "LIKE", "logo", SessionCase.Rect(), SessionCase.Png).Notes[1].Id;
            string frames = c.Path_("dummy_notes", "frames");
            Check.Equal(new[] { "0-00-12-07_LIKE.png" }, Fx.Names(frames));
            c.Events.Clear();
            SessionState data = c.Session.MoveNote(id, null, null);
            Check.Equal(1, c.Events.Count);
            Check.Equal(new[] { "logo", "first" }, Texts(data), "a general note comes first");
            NoteState note = data.Notes[0];
            Check.Equal(id, note.Id);
            Check.True(note.IsGeneral);
            Check.Equal("LIKE", note.Kind);
            Check.True(note.Region == null);
            Check.True(note.Image == null);
            Check.False(Directory.Exists(frames), "the empty frames folder is removed");
            Check.Contains("-\tLIKE\tlogo\n", NotesTxt(c));
            c.Events.Clear();
            Check.Equal(new[] { "logo", "first" }, Texts(c.Session.MoveNote(id, null, null)), "already general: nothing changes");
            Check.Equal(0, c.Events.Count);
            data = c.Session.MoveNote(id, 400, null);
            Check.Equal(new[] { "first", "logo" }, Texts(data), "back on a frame: sorted by frame again");
            Check.Equal(400, data.Notes[1].Frame);
            Check.True(data.Notes[1].Region == null);
            Check.Contains("0:00:16:00\tLIKE\tlogo\n", NotesTxt(c));
        }

        [Test("session.like_and_dislike_notes_are_written_counted_and_read_back")]
        public static void LikeAndDislikeNotes(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(25, "LIKE", "great cut", null, null);
            SessionState data = c.Session.AddNote(50, "DISLIKE", "plain font", SessionCase.Rect(), SessionCase.Png);
            Check.Equal(1, data.Counts.Like);
            Check.Equal(1, data.Counts.Dislike);
            Check.Equal(1, data.Counts["DISLIKE"]);
            Check.Equal("0-00-02-00_DISLIKE.png", data.Notes[1].Image);
            string text = NotesTxt(c);
            Check.Contains("0:00:01:00\tLIKE\tgreat cut\n", text);
            Check.Contains("0:00:02:00\tDISLIKE\tplain font\trect 0.540,0.360 0.880,0.800\t0-00-02-00_DISLIKE.png\n", text);
            Check.Contains("0:00:01:00 LIKE" + Em + "great cut", c.Session.ReadableText());
            data = c.Session.UpdateNote(data.Notes[0].Id, kind: "FIX");
            Check.Equal("FIX", data.Notes[0].Kind);
            Check.Equal(0, data.Counts.Like);
            c.Session.Close();
            c.OpenUnknown();
            Check.Equal(new[] { "FIX", "DISLIKE" }, c.Session.GetState().Notes.Select(n => n.Kind).ToList(), "read back");
        }

        [Test("session.move_note_write_failure_publishes_the_state_then_throws")]
        public static void MoveNoteWriteFailure(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", null, null).Notes[0].Id;
            c.Events.Clear();
            string target = c.Path_("dummy_notes", "dummy_notes.txt");
            File.Delete(target);
            Directory.CreateDirectory(target);
            SessionException ex = Check.Throws<SessionException>(() => c.Session.MoveNote(id, 9, null));
            Check.Contains("could not be saved", ex.Message);
            Check.Equal(1, c.Events.Count);
            Check.Equal(9, c.Events[0].Notes[0].Frame);
        }

        // --- the Markdown notes file ---------------------------------------------------------------------------------

        private static string Md(SessionCase c, string stem = "dummy")
        {
            return Fx.ReadText(c.Path_(stem + "_notes", stem + "_notes.md"));
        }

        [Test("session.open_rewrites_a_stale_or_1x_markdown_file_beside_a_data_file")]
        public static void OpenRewritesAStaleMarkdownFile(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteRaw(c.Path_("promo_notes", "promo_notes.txt"), "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n0:00:01:00\tFIX\tx\n");
            Fx.WriteText(c.Path_("promo_notes", "promo_notes.md"), "# promo.mp4\n\n- **0:00:01:00** · FIX · x\n");
            c.Session.Open(video);
            Check.Equal(new[] { "promo_notes.md", "promo_notes.txt" }, Fx.Names(c.Path_("promo_notes")));
            string md = Md(c, "promo");
            Check.Contains("## `0:00:01:00` FIX\n\nx\n", md);
            Check.Equal("# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n0:00:01:00\tFIX\tx\n", Fx.ReadText(c.Path_("promo_notes", "promo_notes.txt")), "the data file is not written on open");
        }

        [Test("session.open_leaves_an_equal_markdown_file_untouched")]
        public static void OpenLeavesAnEqualMarkdownFile(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            c.Session.Open(video);
            c.Session.AddNote(5, "FIX", "x", null, null);
            string md = c.Path_("promo_notes", "promo_notes.md");
            var old = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(md, old);
            c.Session.Close();
            c.Session.Open(video);
            Check.Equal(old, File.GetLastWriteTimeUtc(md), "an equal file is not written again");
            // a changed author changes the file: the next open writes it
            c.Settings.Author = "Ann";
            c.Session.Close();
            c.Session.Open(video);
            Check.Contains("Reviewer: Ann", Md(c, "promo"));
        }

        [Test("session.a_folder_with_only_a_markdown_file_is_left_alone_until_the_first_note")]
        public static void OpenLeavesAMarkdownFileWithoutDataFileAlone(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes", "promo_notes.md"), "# promo.mp4\n");
            c.Session.Open(video);
            Check.Equal("# promo.mp4\n", Md(c, "promo"));
            c.Session.AddNote(5, "FIX", "x", null, null);
            Check.Equal(new[] { "promo_notes.md", "promo_notes.txt" }, Fx.Names(c.Path_("promo_notes")));
            Check.Contains("FIX 1", Md(c, "promo"));
        }

        [Test("session.every_write_of_the_data_file_writes_the_markdown_file")]
        public static void EveryWriteWritesTheMarkdownFile(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            Check.False(Directory.Exists(c.Path_("dummy_notes")), "no notes, no folder");
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            Check.Contains("## `0:00:12:07` FIX\n\nlogo\n\n![0:00:12:07 FIX](frames/0-00-12-07_FIX.png)\n", Md(c));
            c.Session.UpdateNote(id, text: "logo flickers");
            Check.Contains("\n\nlogo flickers\n", Md(c));
            c.Session.UpdateNote(id, kind: "ADD");
            Check.Contains("## `0:00:12:07` ADD\n\nlogo flickers\n\n![0:00:12:07 ADD](frames/0-00-12-07_ADD.png)\n", Md(c));
            c.Session.MoveNote(id, 25, null);
            Check.Contains("![0:00:01:00 ADD](frames/0-00-01-00_ADD.png)", Md(c));
            c.Session.SetFps(50);
            Check.Contains("## `0:00:00:25` ADD", Md(c));
            Check.Contains("![0:00:00:25 ADD](frames/0-00-00-25_ADD.png)", Md(c));
            c.Session.MoveNote(id, null, null);
            Check.Contains("## General\n\n- **ADD**" + Em + "logo flickers\n", Md(c));
            Check.NotContains("frames/", Md(c));
            c.Session.RemoveNote(id);
            Check.Equal("# dummy.mp4\n\n50 fps\n\nNo notes.\n", Md(c));
        }

        [Test("session.markdown_file_layout_meta_reviewer_general_notes_and_notes_on_frames")]
        public static void MarkdownLayout(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            c.Settings.Author = "  Ann Lee ";
            c.Session.Open(video);
            c.Session.AddNote(null, "FIX", "music is too loud overall", null, null);
            c.Session.AddNote(2000, "ADD", "subtitle for the second speaker", null, null);
            c.Session.AddNote(307, "FIX", "logo flickers", SessionCase.Rect(), SessionCase.Png);
            c.Session.AddNote(null, "LIKE", "the colour", null, null);
            string md = Md(c, "promo");
            string expected = "# promo.mp4\n\n25 fps · 0:01:32:10 · 1920 × 1080 · 4 notes: FIX 2, ADD 1, LIKE 1\n\nReviewer: Ann Lee\n\n"
                + "## General\n\n- **FIX**" + Em + "music is too loud overall\n- **LIKE**" + Em + "the colour\n\n"
                + "## `0:00:12:07` FIX\n\nlogo flickers\n\n![0:00:12:07 FIX](frames/0-00-12-07_FIX.png)\n\n"
                + "## `0:01:20:00` ADD\n\nsubtitle for the second speaker\n";
            Check.Equal(expected, md);
            // the image link goes when the file is gone, at the next write
            File.Delete(c.Path_("promo_notes", "frames", "0-00-12-07_FIX.png"));
            c.Session.AddNote(5, "REMOVE", "x", null, null);
            Check.NotContains("frames/", Md(c, "promo"));
        }

        [Test("session.a_locked_markdown_file_is_a_warning_only")]
        public static void ALockedMarkdownFileIsAWarningOnly(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(5, "FIX", "x", null, null);
            string md = c.Path_("dummy_notes", "dummy_notes.md");
            var logs = new List<string>();
            Log.Listener = (level, message) => logs.Add(level + " " + message);
            using (new FileStream(md, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SessionState data = c.Session.AddNote(6, "ADD", "y", null, null);
                Check.Equal(2, data.Notes.Count);
                Check.False(data.Unsaved);
            }
            Check.True(logs.Any(l => l.StartsWith("WARNING cannot write", StringComparison.Ordinal) && l.Contains("dummy_notes.md")), string.Join("|", logs));
            Check.Contains("\ty", NotesTxt(c));
        }

        // --- failed writes ---------------------------------------------------------------------------------------------

        private static void BlockDataFile(SessionCase c)
        {
            string target = c.Path_("dummy_notes", "dummy_notes.txt");
            if (File.Exists(target)) File.Delete(target);
            Directory.CreateDirectory(target);
        }

        private static void UnblockDataFile(SessionCase c)
        {
            Directory.Delete(c.Path_("dummy_notes", "dummy_notes.txt"), true);
        }

        [Test("session.a_failed_write_marks_the_session_unsaved_and_save_writes_it_later")]
        public static void FailedWriteIsUnsavedUntilSave(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(5, "FIX", "first", null, null);
            BlockDataFile(c);
            SessionException ex = Check.Throws<SessionException>(() => c.Session.AddNote(6, "ADD", "second", null, null));
            Check.True(ex.NotSaved && ex.Changed);
            Check.True(c.Session.GetState().Unsaved);
            Check.Equal(2, c.Session.GetState().Notes.Count, "the note stays in memory");
            Check.True(Check.Throws<SessionException>(() => c.Session.Save()).NotSaved, "still blocked");
            UnblockDataFile(c);
            SessionState saved = c.Session.Save();
            Check.False(saved.Unsaved);
            Check.Equal(Header + "0:00:00:05\tFIX\tfirst\n0:00:00:06\tADD\tsecond\n", NotesTxt(c));
            Check.Contains("second", Md(c));
            Check.Equal(saved.Notes.Count, c.Session.Save().Notes.Count, "nothing to save: no write");
        }

        [Test("session.a_refused_call_is_not_a_change")]
        public static void RefusedCallIsNotAChange(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            SessionException ex = Check.Throws<SessionException>(() => c.Session.AddNote(5, "FIX", "  ", null, null));
            Check.False(ex.Changed || ex.NotSaved);
            Check.False(c.Session.GetState().Unsaved);
        }

        [Test("session.a_failed_write_keeps_the_images_of_the_data_file_on_disk")]
        public static void FailedWriteKeepsImagesOnDisk(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string a = c.Session.AddNote(307, "FIX", "a", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            string b = c.Session.AddNote(500, "ADD", "b", SessionCase.Rect(), PngB).Notes.First(n => n.Text == "b").Id;
            string frames = c.Path_("dummy_notes", "frames");
            BlockDataFile(c);
            // a kind change, a move, a removal and a rate change: the files the data file on disk names stay
            Check.Throws<SessionException>(() => c.Session.UpdateNote(a, kind: "REMOVE"));
            Check.Throws<SessionException>(() => c.Session.MoveNote(b, 900, null));
            Check.Equal(new[] { "0-00-12-07_FIX.png", "0-00-20-00_ADD.png" }, Fx.Names(frames));
            Check.Throws<SessionException>(() => c.Session.RemoveNote(a));
            Check.Throws<SessionException>(() => c.Session.SetFps(50));
            Check.Equal(new[] { "0-00-12-07_FIX.png", "0-00-20-00_ADD.png" }, Fx.Names(frames));
            Check.Equal(new[] { "0-00-20-00_ADD.png" }, c.Session.GetState().Notes.Select(n => n.Image).ToList(), "memory names the files that exist");
            // the next successful write saves everything and removes what no note names
            UnblockDataFile(c);
            c.Session.Save();
            Check.Equal(new[] { "0-00-20-00_ADD.png" }, Fx.Names(frames));
            Check.Equal("# Frame Notes 1\n# source: dummy.mp4\n# fps: 50\n0:00:18:00\tADD\tb\trect 0.540,0.360 0.880,0.800\t0-00-20-00_ADD.png\n", NotesTxt(c));
        }

        [Test("session.new_images_of_notes_that_were_never_saved_go_when_the_notes_are_dropped")]
        public static void DroppedNotesTakeTheirNewImages(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(307, "FIX", "saved", SessionCase.Rect(), SessionCase.Png);
            string frames = c.Path_("dummy_notes", "frames");
            BlockDataFile(c);
            string id = c.Session.GetState().Notes[0].Id;
            Check.Throws<SessionException>(() => c.Session.AddNote(500, "ADD", "dropped", SessionCase.Rect(), PngB));
            Check.Throws<SessionException>(() => c.Session.UpdateNote(id, imagePng: PngB));
            Check.Equal(new[] { "0-00-12-07_FIX.png", "0-00-20-00_ADD.png" }, Fx.Names(frames), "written before the data file");
            c.Session.Close();
            Check.Equal(new[] { "0-00-12-07_FIX.png" }, Fx.Names(frames), "a file the saved notes name stays, a new one goes");
            UnblockDataFile(c);

            // a retry that saves keeps the new image
            c.OpenUnknown();
            BlockDataFile(c);
            Check.Throws<SessionException>(() => c.Session.AddNote(500, "ADD", "kept", SessionCase.Rect(), PngB));
            UnblockDataFile(c);
            c.Session.Save();
            c.Session.Close();
            Check.Equal(new[] { "0-00-12-07_FIX.png", "0-00-20-00_ADD.png" }, Fx.Names(frames));
        }

        // --- Pack ------------------------------------------------------------------------------------------------------

        private static List<ZipArchiveEntry> Entries(ZipArchive archive)
        {
            return archive.Entries.ToList();
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            using (Stream s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        [Test("session.pack_writes_notes_text_and_the_existing_frames")]
        public static void PackWritesNotesTextAndTheExistingFrames(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(2000, "ADD", "second", SessionCase.Rect(), PngB);
            c.Session.AddNote(307, "FIX", "first", SessionCase.Rect(), SessionCase.Png);
            c.Session.AddNote(500, "REMOVE", "gone image", SessionCase.Rect(), SessionCase.Png);
            c.Session.AddNote(null, "FIX", "overall", null, null);
            c.Session.AddNote(900, "FIX", "no image", null, null);
            File.Delete(c.Path_("dummy_notes", "frames", "0-00-20-00_REMOVE.png"));
            c.Events.Clear();
            string before = NotesTxt(c);

            PackResult result = c.Session.Pack();
            string zip = c.Path_("dummy_notes", "dummy_notes.zip");
            Check.Equal(zip, result.Path);
            Check.Equal(5, result.Notes);
            Check.Equal(2, result.Frames);
            Check.Equal(0, c.Events.Count);
            Check.Equal(before, NotesTxt(c));
            Check.Equal(new[] { "dummy_notes.md", "dummy_notes.txt", "dummy_notes.zip", "frames" }, Fx.Names(c.Path_("dummy_notes")));

            using (var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read))
            {
                List<ZipArchiveEntry> entries = Entries(archive);
                Check.Equal(new[] { "notes.txt", "notes.md", "frames/0-00-12-07_FIX.png", "frames/0-01-20-00_ADD.png" }, entries.Select(e => e.FullName).ToList());
                byte[] text = ReadEntry(entries[0]);
                Check.False(text.Length >= 3 && text[0] == 0xEF && text[1] == 0xBB && text[2] == 0xBF, "no byte order mark");
                string expected = "dummy.mp4\r\n\r\nGeneral\r\nFIX" + Em + "overall\r\n\r\n"
                    + "0:00:12:07 FIX" + Em + "first\r\n0:00:20:00 REMOVE" + Em + "gone image\r\n0:00:36:00 FIX" + Em + "no image\r\n0:01:20:00 ADD" + Em + "second\r\n";
                Check.Equal(expected, new UTF8Encoding(false).GetString(text));
                Check.Equal(c.Session.ReadableText().Replace("\n", "\r\n"), expected);
                // notes.md is the Markdown notes file, with the links of the frames that are packed (the deleted one is left out)
                string md = new UTF8Encoding(false).GetString(ReadEntry(entries[1]));
                Check.Contains("## `0:00:20:00` REMOVE\n\ngone image\n\n## ", md);
                Check.Contains("![0:00:12:07 FIX](frames/0-00-12-07_FIX.png)", md);
                Check.NotContains("0-00-20-00_REMOVE.png", md);
                Check.Equal(SessionCase.Png, ReadEntry(entries[2]));
                Check.Equal(PngB, ReadEntry(entries[3]));
                // NoCompression writes deflate "stored" blocks: a few bytes of block header on top of the data, never less.
                Check.True(entries[2].CompressedLength >= entries[2].Length && entries[2].CompressedLength - entries[2].Length <= 16, "frames are stored");
            }
        }

        [Test("session.pack_replaces_an_existing_package_and_leaves_no_part_file")]
        public static void PackReplacesAnExistingPackage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(null, "FIX", "one", null, null);
            string zip = c.Path_("dummy_notes", "dummy_notes.zip");
            Check.Equal(0, c.Session.Pack().Frames);
            long firstLength = new FileInfo(zip).Length;
            c.Session.AddNote(307, "FIX", "two", SessionCase.Rect(), SessionCase.Png);
            PackResult result = c.Session.Pack();
            Check.Equal(2, result.Notes);
            Check.Equal(1, result.Frames);
            Check.True(new FileInfo(zip).Length > firstLength);
            Check.Equal(new[] { "dummy_notes.md", "dummy_notes.txt", "dummy_notes.zip", "frames" }, Fx.Names(c.Path_("dummy_notes")));
            using (var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read))
            {
                Check.Equal(new[] { "notes.txt", "notes.md", "frames/0-00-12-07_FIX.png" }, archive.Entries.Select(e => e.FullName).ToList());
            }
        }

        [Test("session.pack_refuses_no_video_and_no_notes")]
        public static void PackRefusals(TestEnv t)
        {
            var c = new SessionCase(t);
            Check.Equal("No video is open", Check.Throws<SessionException>(() => c.Session.Pack()).Message);
            c.OpenUnknown();
            Check.Equal("There are no notes to pack", Check.Throws<SessionException>(() => c.Session.Pack()).Message);
            Check.False(Directory.Exists(c.Path_("dummy_notes")));
            string id = c.Session.AddNote(null, "FIX", "x", null, null).Notes[0].Id;
            c.Session.RemoveNote(id);
            Check.Equal("There are no notes to pack", Check.Throws<SessionException>(() => c.Session.Pack()).Message);
            Check.Equal(new[] { "dummy_notes.md", "dummy_notes.txt" }, Fx.Names(c.Path_("dummy_notes")));
        }

        [Test("session.pack_leaves_out_an_image_it_cannot_read")]
        public static void PackLeavesOutAnUnreadableImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(307, "FIX", "held", SessionCase.Rect(), SessionCase.Png);
            c.Session.AddNote(2000, "ADD", "free", SessionCase.Rect(), PngB);
            string zip = c.Path_("dummy_notes", "dummy_notes.zip");
            using (new FileStream(c.Path_("dummy_notes", "frames", "0-00-12-07_FIX.png"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                PackResult result = c.Session.Pack();
                Check.Equal(2, result.Notes);
                Check.Equal(1, result.Frames, "the held image is left out");
            }
            using (var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read))
            {
                Check.Equal(new[] { "notes.txt", "notes.md", "frames/0-01-20-00_ADD.png" }, archive.Entries.Select(e => e.FullName).ToList());
            }
        }

        // --- review fixes ------------------------------------------------------------------------------------------------

        [Test("session.an_image_name_with_characters_no_file_can_have_is_not_a_plain_name")]
        public static void ImageNameWithInvalidCharacters(TestEnv t)
        {
            Check.False(NotesLayout.IsPlainName("a|b.png"));
            Check.False(NotesLayout.IsPlainName("a\"b.png"));
            Check.False(NotesLayout.IsPlainName("a<b>.png"));
            Check.True(NotesLayout.IsPlainName("0-00-12-07_FIX.png"));
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes", "promo_notes.txt"),
                "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n0:00:12:07\tFIX\tlogo\trect 0.1,0.1 0.2,0.2\tbad|name.png\n");
            SessionState data = c.Session.Open(video);
            Check.Equal("bad|name.png", data.Notes[0].Image);
            Check.Null(data.Notes[0].ImagePath, "a name that is not plain is never joined to a folder");
            Check.Equal(0, c.Session.Pack().Frames);
        }

        [Test("session.add_note_keeps_the_note_when_its_image_cannot_be_written")]
        public static void AddNoteKeepsTheNoteWithoutItsImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(null, "FIX", "first", null, null);
            Directory.CreateDirectory(c.Path_("dummy_notes", "frames", "0-00-12-07_FIX.png")); // a folder in the place of the image
            c.Events.Clear();
            SessionException ex = Check.Throws<SessionException>(() => c.Session.AddNote(307, "FIX", "typed text", SessionCase.Rect(), SessionCase.Png));
            Check.Contains("The image could not be saved", ex.Message);
            Check.Equal(1, c.Events.Count, "the state is published before the error");
            NoteState kept = c.Events[0].Notes[1];
            Check.Equal("typed text", kept.Text);
            Check.Null(kept.Image);
            Check.NotNull(kept.Region);
            Check.Contains("0:00:12:07\tFIX\ttyped text\trect 0.540,0.360 0.880,0.800\n", NotesTxt(c));
        }

        [Test("session.update_note_names_a_first_image_only_once_it_is_written")]
        public static void UpdateNoteNamesAFirstImageOnlyWhenWritten(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", null, null).Notes[0].Id;
            Directory.CreateDirectory(c.Path_("dummy_notes", "frames", "0-00-12-07_FIX.png"));
            Check.Throws<SessionException>(() => c.Session.UpdateNote(id, null, SessionCase.Rect(), false, SessionCase.Png));
            NoteState n = c.Session.GetState().Notes[0];
            Check.NotNull(n.Region, "the region is kept");
            Check.Null(n.Image, "no name of a file that does not exist");
            Check.Contains("0:00:12:07\tFIX\tlogo\trect 0.540,0.360 0.880,0.800\n", NotesTxt(c));
        }

        [Test("session.an_image_two_notes_share_stays_for_the_other_note")]
        public static void SharedImageStaysForTheOtherNote(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo();
            Fx.WriteText(c.Path_("promo_notes", "promo_notes.txt"),
                "# Frame Notes 1\n# source: promo.mp4\n# fps: 25\n"
                + "0:00:12:07\tFIX\tone\trect 0.1,0.1 0.2,0.2\tshared.png\n"
                + "0:00:13:00\tADD\ttwo\trect 0.1,0.1 0.2,0.2\tshared.png\n"
                + "0:00:14:00\tREMOVE\tthree\trect 0.1,0.1 0.2,0.2\tshared.png\n");
            Fx.WriteBytes(c.Path_("promo_notes", "frames", "shared.png"), SessionCase.Png);
            SessionState data = c.Session.Open(video);
            string one = data.Notes[0].Id, two = data.Notes[1].Id, three = data.Notes[2].Id;
            c.Session.MoveNote(one, 400, null);
            Check.True(File.Exists(c.Path_("promo_notes", "frames", "shared.png")), "a move copies a shared image");
            Check.True(File.Exists(c.Path_("promo_notes", "frames", "0-00-16-00_FIX.png")));
            c.Session.UpdateNote(two, kind: "FIX");
            Check.True(File.Exists(c.Path_("promo_notes", "frames", "shared.png")), "a kind change copies a shared image");
            c.Session.RemoveNote(two);
            Check.True(File.Exists(c.Path_("promo_notes", "frames", "shared.png")), "removing one user keeps the file for the last one");
            Check.Equal("shared.png", c.Session.GetState().Notes.First(n => n.Id == three).Image);
            c.Session.RemoveNote(three);
            Check.False(File.Exists(c.Path_("promo_notes", "frames", "shared.png")), "the last user takes the file with it");
        }

        [Test("session.remove_note_needs_a_known_rate")]
        public static void RemoveNoteNeedsAKnownRate(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.MakeVideo("raw.avi", real: false);
            Fx.WriteText(c.Path_("raw_notes", "raw_notes.txt"), "# Frame Notes 1\n# source: raw.avi\n# fps: 5000\n0:00:00:01\tFIX\tkept\n");
            SessionState data = c.Session.Open(video);
            Check.Null(data.Timecode, "the rate of the file is not usable");
            Check.Throws<SessionException>(() => c.Session.RemoveNote(data.Notes[0].Id));
            Check.Equal(1, c.Session.GetState().Notes.Count);
        }

        [Test("session.pack_failure_is_reported_and_removes_the_part_file")]
        public static void PackFailure(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(null, "FIX", "x", null, null);
            string zip = c.Path_("dummy_notes", "dummy_notes.zip");
            Directory.CreateDirectory(zip); // a folder in the place of the package
            SessionException ex = Check.Throws<SessionException>(() => c.Session.Pack());
            Check.Equal("The notes could not be packed into " + zip, ex.Message);
            Check.Equal(new[] { "dummy_notes.md", "dummy_notes.txt", "dummy_notes.zip" }, Fx.Names(c.Path_("dummy_notes")));
            Check.True(Directory.Exists(zip));
        }
    }
}
