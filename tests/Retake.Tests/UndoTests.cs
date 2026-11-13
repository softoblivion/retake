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
    }
}
