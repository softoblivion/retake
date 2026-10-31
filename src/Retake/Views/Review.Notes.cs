using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using Retake.Core;
using Retake.Design.Controls;
using Retake.Player;

namespace Retake.Views
{
    /// <summary>The review: selection and the note flow (start, edit, type, region, commit, move, remove, package).</summary>
    public sealed partial class Review
    {
        // --- selection ------------------------------------------------------------------------------------------

        NoteState? Note(string? id) => id == null ? null : state.Notes.FirstOrDefault(n => n.Id == id);

        /// <summary>Selects a note (or nothing); a frame note with <paramref name="seek"/> moves the playhead to its frame.</summary>
        void Select(string? id, bool seek)
        {
            if (edit != null) return;
            selectedId = id;
            Notes.Select(id);
            Timeline.SetSelected(id);
            NoteState? n = Note(id);
            if (seek && n != null && n.Frame.HasValue && player != null)
            {
                if (playing) player.Pause();
                player.SeekFrame(n.Frame.Value);
            }
            UpdateRegionDisplay();
        }

        /// <summary>↑ / ↓: the previous or next note of the list; without a selection the nearest frame note before / at or after the playhead.</summary>
        void SelectNeighbour(int dir)
        {
            List<NoteState> list = state.Notes;
            if (list.Count == 0) return;
            int index = list.FindIndex(n => n.Id == selectedId);
            if (index < 0)
            {
                int playhead = player?.Frame ?? frame;
                index = dir < 0 ? list.FindLastIndex(n => n.Frame.HasValue && n.Frame.Value < playhead) : list.FindIndex(n => n.Frame.HasValue && n.Frame.Value >= playhead);
                if (index < 0) return;
            }
            else index = Math.Max(0, Math.Min(list.Count - 1, index + dir));
            Select(list[index].Id, true);
        }

        void UpdateRegionDisplay()
        {
            if (edit != null) return;
            NoteState? n = Note(selectedId);
            RegionTool tool = Video.Overlay.Region;
            if (n != null && n.Region != null && n.Frame.HasValue && !playing && frame == n.Frame.Value && (player == null || player.Frame == n.Frame.Value)) tool.Show(n.Region, n.Kind);
            else tool.Show(null, tool.Shown == null ? "FIX" : n?.Kind ?? "FIX");
        }

        // --- notes ----------------------------------------------------------------------------------------------

        bool Busy => !videoLoaded || player == null || edit != null || starting || saving;

        /// <summary>Notes need a known frame rate: without one the rate menu opens with a toast, and the caller stops.</summary>
        bool RateMissing()
        {
            if (state.Timecode != null) return false;
            window.Root.Toasts.Show(ToastLevel.Warning, Copy.NeedRate);
            OpenMenu(rateButton.BuildMenu(), rateButton);
            return true;
        }

        /// <summary>
        /// The keys 1 to 6 and the note bar: a new note of a type at the frame on screen (or at <paramref name="at"/>, from
        /// the timeline's menu); a null <paramref name="kind"/> (4) is a general note about the whole video, of the type
        /// FIX until the editor changes it. A note being written is saved first (a click on the bar presses the window
        /// before it clicks the button), so the new note never waits for a second press.
        /// </summary>
        async Task StartNote(string? kind, int? at = null)
        {
            if (commitTask != null && !commitTask.IsCompleted) await commitTask;
            if (edit != null && !saving) await Commit(true);
            if (Busy || RateMissing()) return;
            starting = true;
            int f;
            try
            {
                if (at.HasValue)
                {
                    if (playing) player!.Pause();
                    player!.SeekFrame(at.Value);
                    await player.Settle();
                }
                f = await player!.PauseAndSettle();
            }
            finally { starting = false; }
            if (edit != null || !videoLoaded) return;
            Select(null, false);
            string k = kind ?? "FIX";
            edit = new Edit { Create = true, Kind = k, OrigKind = k, Frame = kind != null ? f : (int?)null, AtFrame = f, OrigGeneral = kind == null };
            BeginEdit();
        }

        async Task EditNote(string id)
        {
            if (!videoLoaded || player == null || starting || saving) return;
            if (edit != null)
            {
                if (edit.Id == id) return;
                await Commit();
            }
            NoteState? n = Note(id);
            if (n == null) return;
            Select(id, false);
            starting = true;
            try
            {
                if (playing) player.Pause();
                if (n.Frame.HasValue)
                {
                    if (player.Frame != n.Frame.Value) player.SeekFrame(n.Frame.Value);
                    await player.Settle();
                }
            }
            finally { starting = false; }
            if (edit != null) return;
            edit = new Edit { Create = false, Id = n.Id, Kind = n.Kind, OrigKind = n.Kind, Frame = n.Frame, AtFrame = n.Frame ?? player.Frame, OrigGeneral = n.IsGeneral, Region = n.Region, OrigRegion = n.Region, OrigText = n.Text };
            BeginEdit();
        }

        /// <param name="text">The text the field opens with: the note's own (null), or the text of a save that was refused.</param>
        void BeginEdit(string? text = null)
        {
            Edit e = edit!;
            Trace();
            OverlayWindow o = Video.Overlay;
            o.Composer.Open(e.Kind, Format(e.AtFrame), e.General, text ?? e.OrigText);
            ShowPlace();
            o.SetTyping(true);
        }

        /// <summary>The parts of the editor that follow the note's place: the region tool, the hint, the editor's spot.</summary>
        void ShowPlace()
        {
            Edit e = edit!;
            OverlayWindow o = Video.Overlay;
            o.Region.Show(e.General ? null : e.Region, e.Kind);
            o.Region.SetDrawing(!e.General);
            if (e.General) o.Hint.Show(Copy.HintGeneral, Copy.KeysCreate);
            else if (e.Create || e.OrigGeneral) o.Hint.Show(Copy.HintCreate, Copy.KeysCreate);
            else o.Hint.Show(Copy.HintEdit, Copy.KeysEdit);
            PlaceComposer();
            AutomationProperties.SetItemStatus(o.Composer, (e.Create ? "create" : "edit") + (e.General ? " general" : ""));
        }

        /// <summary>
        /// Ctrl+4 or the place chips: the note moves between its frame and the whole video. To the whole video it loses
        /// its region; back on a frame it is the frame it came from (or the one on screen when it started as general).
        /// </summary>
        void SetEditPlace(bool general)
        {
            Edit? e = edit;
            if (e == null || e.General == general) return;
            e.Frame = general ? (int?)null : e.AtFrame;
            if (general && e.Region != null)
            {
                e.Region = null;
                e.RegionDirty = !Same(null, e.OrigRegion);
            }
            if (!general && player != null && player.Frame != e.AtFrame) player.SeekFrame(e.AtFrame);
            ShowPlace();
            Video.Overlay.Composer.FocusField();
            Trace();
        }

        void PlaceComposer()
        {
            if (edit == null) return;
            OverlayWindow o = Video.Overlay;
            Box frameBox = Video.FrameBox;
            double areaW = Video.Area.ActualWidth, areaH = Video.Area.ActualHeight;
            o.Hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // A fixed width: the field wraps a long text and the card grows downward, never past the area's edges.
            double width = Composer.CardWidth(areaW);
            if (o.Composer.Width != width) o.Composer.Width = width;
            o.Composer.Measure(new Size(width, double.PositiveInfinity));
            Size hint = o.Hint.DesiredSize, box = o.Composer.DesiredSize;
            OverlayWindow.Put(o.Hint, Math.Max(8, frameBox.X + (frameBox.W - hint.Width) / 2), Math.Max(8, frameBox.Y + 10));
            double left, topY;
            if (edit.General)
            {
                // A general note belongs to no place on the frame: the editor sits in the middle of the area.
                left = Math.Max(8, (areaW - box.Width) / 2);
                topY = Math.Max(8, (areaH - box.Height) / 2);
            }
            else
            {
                RegionMath.PlaceEditor(edit.Region, frameBox, areaW, areaH, box.Width, box.Height, 0, out left, out topY);
                if (edit.Region == null) topY = Math.Max(8, frameBox.Bottom - box.Height - 16);
            }
            OverlayWindow.Put(o.Composer, left, topY);
        }

        void OnRegionDrawn(Region r)
        {
            if (edit == null || edit.General) return;
            SetEditRegion(r);
        }

        void SetEditRegion(Region? r)
        {
            Edit e = edit!;
            e.Region = r;
            e.RegionDirty = !Same(r, e.OrigRegion);
            Video.Overlay.Region.Show(r, e.Kind);
            PlaceComposer();
            Video.Overlay.Composer.FocusField();
        }

        /// <summary>The type was changed in the editor: the region takes the new colour; the note keeps it on save.</summary>
        void SetEditKind(string kind)
        {
            if (edit == null) return;
            edit.Kind = kind;
            if (!edit.General) Video.Overlay.Region.Show(edit.Region, kind);
            Trace();
        }

        static bool Same(Region? a, Region? b) => a == null || b == null ? a == null && b == null : a.Equals(b);

        void EndEditUi()
        {
            OverlayWindow o = Video.Overlay;
            o.Region.SetDrawing(false);
            o.Hint.Hide();
            o.Composer.Close();
            o.SetTyping(false);
        }

        void CancelEdit()
        {
            if (edit == null || saving) return;
            edit = null;
            EndEditUi();
            UpdateRegionDisplay();
            Trace();
        }

        /// <summary>
        /// Saves the note being written: the frame image first (a new region, or a region drawn in a new type's
        /// colour), then the core. A note needs text: Enter on an empty field keeps the editor open and asks for it;
        /// <paramref name="dropEmpty"/> (a press elsewhere, another video, closing) drops an empty new note and reverts
        /// an emptied edit. A save already running is returned, so every action that saves first waits for it.
        /// </summary>
        Task Commit(bool dropEmpty = false)
        {
            if (commitTask != null && !commitTask.IsCompleted) return commitTask;
            if (edit == null || saving || player == null) return Task.CompletedTask;
            commitTask = CommitNow(dropEmpty);
            return commitTask;
        }

        async Task CommitNow(bool dropEmpty)
        {
            Edit e = edit!;
            IPlayer player = this.player!;
            string text = Video.Overlay.Composer.Text;
            if (Retake.Core.Note.NormalizeText(text).Length == 0)
            {
                if (dropEmpty) CancelEdit();
                else Video.Overlay.Composer.AskForText();
                return;
            }
            saving = true;
            Box frameBox = Video.FrameBox;
            bool kindChanged = e.Kind != e.OrigKind;
            bool placeChanged = e.General != e.OrigGeneral;
            bool needImage = !e.General && e.Region != null && (e.Create || e.RegionDirty || kindChanged || placeChanged);
            EndEditUi();
            Video.Overlay.Region.Show(e.General ? null : e.Region, e.Kind);
            byte[]? png = null;
            string? imageError = null;
            Edit? reopen = null;
            try
            {
                if (needImage)
                {
                    try
                    {
                        if (playing) player.Pause();
                        if (player.Frame != e.Frame!.Value) player.SeekFrame(e.Frame.Value);
                        await player.Settle();
                        RawFrame raw = await player.GrabFrame();
                        png = await FrameCapture.EncodeAsync(raw, e.Region!, new RegionPainter.Ink(e.Kind), RegionMath.CaptureScale(raw.Width, frameBox));
                    }
                    catch (Exception ex)
                    {
                        imageError = ex.Message;
                        Log.Error("frame image failed", ex);
                    }
                }
                if (e.Create)
                {
                    int before = state.Notes.Count == 0 ? 0 : state.Notes.Max(n => n.Order);
                    try
                    {
                        session.AddNote(e.Frame, e.Kind, text, e.General ? null : e.Region, png);
                    }
                    finally
                    {
                        // A note kept in memory after a failed write is the selected one all the same.
                        NoteState? added = session.GetState().Notes.Where(n => n.Order > before).OrderByDescending(n => n.Order).FirstOrDefault();
                        if (added != null) selectedId = added.Id;
                    }
                    edit = null;
                    Apply(session.GetState());
                }
                else
                {
                    bool textChanged = text != e.OrigText;
                    bool regionChanged = !e.General && (e.RegionDirty || (placeChanged && e.Region != null));
                    edit = null;
                    if (placeChanged || textChanged || regionChanged || kindChanged)
                    {
                        Apply(NoteEdit.Save(session, e.Id, placeChanged, e.Frame, textChanged ? text : null,
                            regionChanged ? e.Region : null, regionChanged && e.Region == null, png, kindChanged ? e.Kind : null));
                    }
                }
                if (imageError != null) Banner.Show(string.Format(CultureInfo.InvariantCulture, Copy.ImageFailed, imageError));
            }
            catch (SessionException ex)
            {
                edit = null;
                Apply(session.GetState());
                // A change the session refused took nothing: the editor comes back with the text, so nothing typed is lost.
                // A change kept in memory without its write is in the list; the notice offers to write it again.
                Report(ex);
                if (!ex.Changed && videoLoaded && !closing && (e.Create || Note(e.Id) != null)) reopen = e;
            }
            finally
            {
                edit = null;
                saving = false;
                if (reopen != null)
                {
                    edit = reopen;
                    BeginEdit(text);
                }
                else Select(selectedId, false);
                Trace();
            }
        }

        /// <summary>Reports a failed session call: a change kept in memory without its write gets the notice with Retry, anything else the banner.</summary>
        void Report(SessionException ex)
        {
            unsaved.Failed(ex);
            if (ex.NotSaved) ShowUnsaved(ex.Message);
            else Banner.Show(ex.Message);
        }

        /// <summary>The notice that the notes are not saved, with Retry; it stays until it is clicked, and its tooltip has the reason.</summary>
        void ShowUnsaved(string reason)
        {
            Toast t = window.Root.Toasts.Show(ToastLevel.Error, Copy.NotSaved, Copy.Retry, () => _ = RetrySave(), sticky: true);
            Tip.Set(t, reason);
        }

        /// <summary>Writes the notes again after a failed write: a toast confirms, a new failure shows the notice again.</summary>
        Task RetrySave()
        {
            if (state.Video == null) return Task.CompletedTask;
            try
            {
                Apply(session.Save());
                window.Root.Toasts.Show(ToastLevel.Success, Copy.Saved);
            }
            catch (SessionException ex)
            {
                Apply(session.GetState());
                Report(ex);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Before the notes leave memory (closing, another video, a restart for an update): while they are not saved, the
        /// question "Notes are not saved" with Retry, Discard and Cancel. True to go on (saved, or discarded), false to stay.
        /// </summary>
        async Task<bool> ResolveUnsaved()
        {
            string? reason = null;
            while (unsaved.MustAsk)
            {
                UnsavedChoice choice = await UnsavedQuestion.Ask(window.Root.Overlay, reason);
                if (choice == UnsavedChoice.Cancel) return false;
                if (choice == UnsavedChoice.Discard)
                {
                    unsaved.Discard();
                    break;
                }
                try { Apply(session.Save()); }
                catch (SessionException ex) { reason = ex.Message; Apply(session.GetState()); }
            }
            return true;
        }

        /// <summary>True while notes are in memory that the data file does not have.</summary>
        public bool HasUnsaved => state.Video != null && unsaved.MustAsk;

        /// <summary>The update's restart asks first, as closing does; true when it may go on.</summary>
        public Task<bool> ConfirmRestart() => ResolveUnsaved();

        /// <summary>The restart that a Discard was given for did not quit: the next close, open or restart asks again.</summary>
        public void RestartDropped() => unsaved.Revoke();

        /// <summary>
        /// Moves a frame note to another frame (a drag on the timeline, Move to the playhead). A note with a region
        /// gets a new frame image taken at the new frame, so the image keeps showing what the note is about.
        /// </summary>
        async Task MoveNote(string id, int target)
        {
            NoteState? n = Note(id);
            if (n == null || !n.Frame.HasValue || player == null || target == n.Frame.Value) return;
            if (saving || edit != null)
            {
                // Refused while another note is saved or written: the playhead goes back to where the note stays.
                player.SeekFrame(n.Frame.Value);
                return;
            }
            saving = true;
            byte[]? png = null;
            string? imageError = null;
            try
            {
                if (n.Region != null)
                {
                    try
                    {
                        if (playing) player.Pause();
                        if (player.Frame != target) player.SeekFrame(target);
                        await player.Settle();
                        RawFrame raw = await player.GrabFrame();
                        png = await FrameCapture.EncodeAsync(raw, n.Region, new RegionPainter.Ink(n.Kind), RegionMath.CaptureScale(raw.Width, Video.FrameBox));
                    }
                    catch (Exception ex)
                    {
                        imageError = ex.Message;
                        Log.Error("frame image failed", ex);
                    }
                }
                SessionState st = session.MoveNote(id, target, png);
                selectedId = id;
                Apply(st);
                if (imageError != null) Banner.Show(string.Format(CultureInfo.InvariantCulture, Copy.ImageFailed, imageError));
            }
            catch (SessionException ex)
            {
                Apply(session.GetState());
                Report(ex);
            }
            finally
            {
                saving = false;
                Select(selectedId, true);
                Trace();
            }
        }

        void RemoveNote(string id)
        {
            NoteState? n = Note(id);
            if (n == null || saving) return;
            if (edit != null) CancelEdit();
            int index = state.Notes.FindIndex(x => x.Id == n.Id);
            try
            {
                session.RemoveNote(n.Id);
                SessionState st = session.GetState();
                string? next = st.Notes.Count == 0 ? null : st.Notes[Math.Min(index, st.Notes.Count - 1)].Id;
                selectedId = next;
                Apply(st);
                Select(next, false);
                window.Root.Toasts.Show(ToastLevel.Info, Copy.NoteRemoved);
            }
            catch (SessionException ex)
            {
                Apply(session.GetState());
                Report(ex);
            }
        }

        /// <summary>
        /// Done: the notes as plain text and their frame images in one zip inside the notes folder, made off the UI
        /// thread; a toast names the file and its Show button selects it in Explorer.
        /// </summary>
        async Task Pack()
        {
            if (state.Video == null || packing || state.Timecode == null) return;
            await Commit(true);
            if (state.Notes.Count == 0 || edit != null) return;
            packing = true;
            doneButton.Busy = true;
            ShowDone();
            try
            {
                PackResult r = await Task.Run(() => session.Pack());
                string path = r.Path;
                window.Root.Toasts.Show(ToastLevel.Success, string.Format(CultureInfo.InvariantCulture, Copy.Packed, Path.GetFileName(path)), Copy.PackedShow, () => Reveal(path));
            }
            catch (SessionException ex)
            {
                Banner.Show(ex.Message);
            }
            finally
            {
                packing = false;
                doneButton.Busy = false;
                ShowDone();
            }
        }
    }
}
