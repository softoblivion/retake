using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>Undo of a removed, edited or moved note: the note, the data file and the image bytes come back as they were.</summary>
    public static class UndoTests
    {
        private static readonly byte[] PngB = SessionCase.Png.Concat(new byte[] { (byte)'b' }).ToArray();
        private const string FixImage = "0-00-12-07_FIX.png";

        private static string DataFile(SessionCase c) => c.Path_("dummy_notes", "dummy_notes.txt");

        private static string Image(SessionCase c, string name) => c.Path_("dummy_notes", "frames", name);

        private static NoteState Only(SessionState state, string id) => state.Notes.Single(n => n.Id == id);

        [Test("undo.a_removed_note_comes_back_with_its_image_byte_for_byte")]
        public static void ARemovedNoteComesBack(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            c.Session.AddNote(100, "ADD", "first", null, null);
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), PngB).Notes.Single(n => n.Frame == 307).Id;
            c.Session.AddNote(900, "LIKE", "last", null, null);
            byte[] dataBefore = File.ReadAllBytes(DataFile(c));
            Check.Null(c.Session.GetState().UndoNoteId, "adding a note is not undone");

            c.Session.RemoveNote(id);
            Check.False(File.Exists(Image(c, FixImage)), "the image of the removed note is deleted");
            SessionState removed = c.Session.GetState();
            Check.Equal(id, removed.UndoNoteId);
            Check.True(removed.UndoRestoresRemoved);

            SessionState data = c.Session.Undo();
            NoteState back = Only(data, id);
            Check.Equal(307, back.Frame);
            Check.Equal("FIX", back.Kind);
            Check.Equal("logo", back.Text);
            Check.Equal(SessionCase.Rect().ToText(), back.Region!.ToText());
            Check.Equal(FixImage, back.Image);
            Check.Equal(PngB, File.ReadAllBytes(Image(c, FixImage)));
            Check.Equal(dataBefore, File.ReadAllBytes(DataFile(c)), "the data file is as it was before the removal");
            Check.Null(data.UndoNoteId);
            SessionException ex = Check.Throws<SessionException>(() => c.Session.Undo());
            Check.Equal("There is nothing to undo", ex.Message);
        }

        [Test("undo.an_edit_of_text_kind_region_and_image_is_undone_step_by_step")]
        public static void AnEditIsUndone(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            byte[] dataBefore = File.ReadAllBytes(DataFile(c));

            // 1: new region and bytes under the same name; 2: new kind, text and bytes under a new name; 3: region removed.
            Region point = Region.CreatePoint(0.25, 0.5);
            c.Session.UpdateNote(id, region: point, imagePng: PngB);
            Check.Equal(PngB, File.ReadAllBytes(Image(c, FixImage)));
            byte[] dataAfterOne = File.ReadAllBytes(DataFile(c));
            c.Session.UpdateNote(id, text: "logo moved", kind: "add", region: SessionCase.Rect(), imagePng: SessionCase.Png);
            Check.True(File.Exists(Image(c, "0-00-12-07_ADD.png")));
            Check.False(File.Exists(Image(c, FixImage)));
            byte[] dataAfterTwo = File.ReadAllBytes(DataFile(c));
            c.Session.UpdateNote(id, clearRegion: true);
            Check.False(Directory.Exists(c.Path_("dummy_notes", "frames")));
            Check.False(c.Session.GetState().UndoRestoresRemoved);

            NoteState n = Only(c.Session.Undo(), id);
            Check.Equal(SessionCase.Rect().ToText(), n.Region!.ToText());
            Check.Equal("0-00-12-07_ADD.png", n.Image);
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Image(c, "0-00-12-07_ADD.png")));
            Check.Equal(dataAfterTwo, File.ReadAllBytes(DataFile(c)));

            n = Only(c.Session.Undo(), id);
            Check.Equal("FIX", n.Kind);
            Check.Equal("logo", n.Text);
            Check.Equal(point.ToText(), n.Region!.ToText());
            Check.Equal(FixImage, n.Image);
            Check.Equal(PngB, File.ReadAllBytes(Image(c, FixImage)));
            Check.False(File.Exists(Image(c, "0-00-12-07_ADD.png")), "the image the undone edit gave the note is deleted");
            Check.Equal(dataAfterOne, File.ReadAllBytes(DataFile(c)));

            n = Only(c.Session.Undo(), id);
            Check.Equal(SessionCase.Rect().ToText(), n.Region!.ToText());
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Image(c, FixImage)), "the old bytes are back under the same name");
            Check.Equal(dataBefore, File.ReadAllBytes(DataFile(c)));
            Check.Equal(new[] { FixImage }, Fx.Names(c.Path_("dummy_notes", "frames")));
        }

        [Test("undo.a_moved_note_goes_back_to_its_frame_and_image")]
        public static void AMovedNoteGoesBack(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            byte[] dataBefore = File.ReadAllBytes(DataFile(c));
            c.Session.MoveNote(id, 500, PngB);
            Check.Equal(new[] { "0-00-20-00_FIX.png" }, Fx.Names(c.Path_("dummy_notes", "frames")));
            c.Session.MoveNote(id, null, null);
            Check.True(Only(c.Session.GetState(), id).IsGeneral);

            NoteState n = Only(c.Session.Undo(), id);
            Check.Equal(500, n.Frame);
            Check.Equal(PngB, File.ReadAllBytes(Image(c, "0-00-20-00_FIX.png")));
            n = Only(c.Session.Undo(), id);
            Check.Equal(307, n.Frame);
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Image(c, FixImage)));
            Check.Equal(new[] { FixImage }, Fx.Names(c.Path_("dummy_notes", "frames")));
            Check.Equal(dataBefore, File.ReadAllBytes(DataFile(c)));
        }

        [Test("undo.a_name_another_note_took_meanwhile_gives_the_restored_image_a_name_of_its_own")]
        public static void ATakenNameGetsANewName(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "old", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            c.Session.RemoveNote(id);
            string other = c.Session.AddNote(307, "FIX", "new", SessionCase.Rect(), PngB).Notes[0].Id;
            Check.Equal(FixImage, Only(c.Session.GetState(), other).Image);
            SessionState data = c.Session.Undo();
            Check.Equal("0-00-12-07_FIX_2.png", Only(data, id).Image);
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Image(c, "0-00-12-07_FIX_2.png")));
            Check.Equal(PngB, File.ReadAllBytes(Image(c, FixImage)), "the other note keeps its bytes");
        }

        [Test("undo.after_a_failed_write_the_note_comes_back_in_memory_and_save_writes_it")]
        public static void UndoAfterAFailedWrite(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), PngB).Notes[0].Id;
            byte[] dataBefore = File.ReadAllBytes(DataFile(c));
            using (new FileStream(DataFile(c), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check.True(Check.Throws<SessionException>(() => c.Session.RemoveNote(id)).NotSaved);
                Check.True(File.Exists(Image(c, FixImage)), "the data file on disk still names the image");
                SessionException ex = Check.Throws<SessionException>(() => c.Session.Undo());
                Check.True(ex.NotSaved);
                Check.Equal("logo", Only(c.Session.GetState(), id).Text);
            }
            SessionState saved = c.Session.Save();
            Check.False(saved.Unsaved);
            Check.Equal(dataBefore, File.ReadAllBytes(DataFile(c)));
            Check.Equal(PngB, File.ReadAllBytes(Image(c, FixImage)));
        }

        [Test("undo.a_change_that_changes_nothing_is_not_recorded_and_open_close_and_rate_change_clear_the_history")]
        public static void TheHistoryIsCleared(TestEnv t)
        {
            var c = new SessionCase(t);
            string video = c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            c.Session.UpdateNote(id, text: "logo");
            c.Session.MoveNote(id, 307, null);
            Check.Null(c.Session.GetState().UndoNoteId);

            c.Session.UpdateNote(id, text: "logo 2");
            Check.Equal(id, c.Session.GetState().UndoNoteId);
            id = c.Session.Open(video).Notes[0].Id;
            Check.Null(c.Session.GetState().UndoNoteId, "a reopen clears the history");

            c.Session.UpdateNote(id, text: "logo 3");
            c.Session.SetFps(25);
            Check.NotNull(c.Session.GetState().UndoNoteId, "a rate that stays keeps the history");
            c.Session.SetFps(30);
            Check.Null(c.Session.GetState().UndoNoteId, "a new rate clears the history");

            c.Session.UpdateNote(id, text: "logo 4");
            c.Session.Close();
            c.Session.Open(video);
            Check.Null(c.Session.GetState().UndoNoteId);
        }

        [Test("undo.any_other_rate_clears_the_history_even_on_the_same_timecode_digits")]
        public static void AnyOtherRateClearsTheHistory(TestEnv t)
        {
            // 23.976 and 24 write the same image names, but the rate is part of the timecode base: the steps go.
            var c = new SessionCase(t);
            c.OpenUnknown(23.976);
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            c.Session.UpdateNote(id, text: "logo 2");
            c.Session.SetFps(23.976);
            Check.Equal(id, c.Session.GetState().UndoNoteId, "the rate in use, set again, keeps the history");
            c.Session.SetFps(24);
            Check.Null(c.Session.GetState().UndoNoteId);
            Check.Equal(0, c.Session.HistoryCount);
        }

        [Test("undo.the_history_keeps_the_last_hundred_steps")]
        public static void TheHistoryKeepsAHundredSteps(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            for (int i = 0; i < 105; i++) c.Session.UpdateNote(id, text: "edit " + i);
            Check.Equal(100, c.Session.HistoryCount);
            SessionState state = c.Session.GetState();
            for (int i = 0; i < 100; i++) state = c.Session.Undo();
            Check.Equal("edit 4", Only(state, id).Text, "the five oldest steps are gone");
            Check.Equal("There is nothing to undo", Check.Throws<SessionException>(() => c.Session.Undo()).Message);
        }

        /// <summary>A file of the size of a UHD frame PNG (24 MB), different for each <paramref name="mark"/>.</summary>
        private static byte[] BigPng(byte mark, int megabytes = 24)
        {
            var bytes = new byte[megabytes * 1024 * 1024];
            SessionCase.Png.CopyTo(bytes, 0);
            bytes[bytes.Length - 1] = mark;
            return bytes;
        }

        [Test("undo.a_hundred_text_edits_of_a_note_with_a_4k_image_hold_no_copy_of_the_image")]
        public static void TextEditsHoldNoImage(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            byte[] big = BigPng(1);
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), big).Notes[0].Id;
            long before = GC.GetTotalMemory(true);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) c.Session.UpdateNote(id, text: "logo " + i);
            clock.Stop();
            long growth = GC.GetTotalMemory(true) - before;
            Check.Equal(100, c.Session.HistoryCount);
            Check.Equal(0L, c.Session.HistoryBytes, "a text-only step keeps no image bytes");
            Check.True(growth < big.Length, "the heap grew by " + growth + " bytes, less than one image");
            if (Environment.GetEnvironmentVariable("RETAKE_TEST_MEASURE") == "1")
            {
                Console.WriteLine("measure: image " + big.Length + " B; 100 text edits in " + clock.ElapsedMilliseconds
                    + " ms; steps " + c.Session.HistoryCount + "; history image bytes " + c.Session.HistoryBytes
                    + "; managed heap growth " + growth + " B; cap " + Session.HistoryBytesCap + " B");
            }
            SessionState state = c.Session.GetState();
            for (int i = 0; i < 100; i++) state = c.Session.Undo();
            Check.Equal("logo", Only(state, id).Text);
            Check.Equal(big, File.ReadAllBytes(Image(c, FixImage)), "the image is untouched");
        }

        [Test("undo.image_changes_share_equal_bytes_and_the_cap_drops_the_oldest_steps")]
        public static void ImageStepsShareAndAreCapped(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            Region point = Region.CreatePoint(0.25, 0.5);
            byte[] a = BigPng(1, 1), b = BigPng(2, 1);
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), a).Notes[0].Id;
            for (int i = 0; i < 6; i++) c.Session.UpdateNote(id, region: i % 2 == 0 ? point : SessionCase.Rect(), imagePng: i % 2 == 0 ? b : a);
            Check.Equal(6, c.Session.HistoryCount);
            Check.Equal((long)(a.Length + b.Length), c.Session.HistoryBytes, "six steps hold two arrays");
            for (int i = 0; i < 6; i++) c.Session.Undo();
            Check.Equal(a, File.ReadAllBytes(Image(c, FixImage)));

            // Ten changes to 30 MB images: the steps would hold 1 + 9 x 30 MB, so the two oldest go (8 x 30 = 240 MB stay).
            for (int i = 0; i < 10; i++)
            {
                c.Session.UpdateNote(id, region: i % 2 == 0 ? point : SessionCase.Rect(), imagePng: BigPng((byte)(10 + i), 30));
            }
            Check.Equal(8, c.Session.HistoryCount);
            Check.Equal(8L * 30 * 1024 * 1024, c.Session.HistoryBytes);
            Check.True(c.Session.HistoryBytes <= Session.HistoryBytesCap);
            for (int i = 0; i < 8; i++) c.Session.Undo();
            Check.Equal(BigPng(11, 30), File.ReadAllBytes(Image(c, FixImage)), "the oldest kept step restores the bytes it recorded");
            Check.Equal("There is nothing to undo", Check.Throws<SessionException>(() => c.Session.Undo()).Message);
        }

        [Test("undo.one_edit_that_moves_the_note_and_changes_its_text_and_image_is_one_step")]
        public static void OneEditIsOneStep(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "logo", SessionCase.Rect(), SessionCase.Png).Notes[0].Id;
            byte[] dataBefore = File.ReadAllBytes(DataFile(c));
            Views.NoteEdit.Save(c.Session, id, true, 500, "logo moved", SessionCase.Rect(), false, PngB, null);
            NoteState moved = Only(c.Session.GetState(), id);
            Check.Equal(500, moved.Frame);
            Check.Equal("logo moved", moved.Text);
            Check.Equal(1, c.Session.HistoryCount);

            NoteState n = Only(c.Session.Undo(), id);
            Check.Equal(307, n.Frame);
            Check.Equal("logo", n.Text);
            Check.Equal(FixImage, n.Image);
            Check.Equal(SessionCase.Png, File.ReadAllBytes(Image(c, FixImage)));
            Check.Equal(new[] { FixImage }, Fx.Names(c.Path_("dummy_notes", "frames")));
            Check.Equal(dataBefore, File.ReadAllBytes(DataFile(c)));
            Check.Null(c.Session.GetState().UndoNoteId, "one Undo reverts the whole edit");
        }
    }
}
