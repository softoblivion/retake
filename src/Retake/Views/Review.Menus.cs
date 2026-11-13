using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Retake.Core;
using Retake.Design;
using Retake.Design.Controls;
using Retake.Player;

namespace Retake.Views
{
    /// <summary>The review: menus, the actions of the recent list and the clipboard.</summary>
    public sealed partial class Review
    {
        // --- menus ----------------------------------------------------------------------------------------------

        void OpenMenu(PopMenu menu, FrameworkElement anchor)
        {
            if (edit != null) return;
            window.Root.Overlay.OpenMenu(menu, anchor);
        }

        /// <summary>A context menu at the pointer (a point of <paramref name="relativeTo"/>); none while a note is written.</summary>
        void OpenMenuAt(PopMenu menu, FrameworkElement relativeTo, Point at)
        {
            if (edit != null || !menu.HasEntries) return;
            window.Root.Overlay.OpenMenuAt(menu, relativeTo, at);
        }

        /// <summary>
        /// Ctrl+G: a field under the timecode readout, filled with the current position and selected, takes a timecode,
        /// a frame number or, while the rate is unknown, the clock, as the readouts show them; Enter goes there. A line
        /// under the field says what to type, and what is wrong after a refused Enter.
        /// </summary>
        void OpenGoTo()
        {
            if (!videoLoaded || player == null || edit != null) return;
            var menu = new PopMenu(220);
            var field = new TextField(Copy.GoToPlaceholder, 200) { Margin = new Thickness(4, 2, 4, 2), Text = Format(frame) };
            Tip.Set(field, Copy.GoToTip);
            AutomationProperties.SetAutomationId(field, "GoTo");
            AutomationProperties.SetName(field, Copy.GoToName);
            TextBlock line = Typography.Text(Copy.GoToHint, Typography.Caption, "ink-3");
            line.TextWrapping = TextWrapping.Wrap;
            line.MaxWidth = 200;
            line.Margin = new Thickness(9, 4, 9, 5);
            AutomationProperties.SetAutomationId(line, "GoToMessage");
            AutomationProperties.SetName(line, Copy.GoToHint);
            field.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                try
                {
                    int target = Timecode.ParseGoTo(field.Text, tb, rate, lastFrame);
                    Keyboard.ClearFocus();
                    menu.Close();
                    if (player != null && videoLoaded) player.SeekFrame(target);
                }
                catch (FormatException ex)
                {
                    line.Text = Typography.Caption.Text(ex.Message);
                    line.SetResourceReference(TextBlock.ForegroundProperty, "T.error-ink");
                    AutomationProperties.SetName(line, ex.Message);
                    field.SelectAll();
                }
            };
            field.Loaded += (s, e) => { field.Focus(); Keyboard.Focus(field); field.SelectAll(); };
            // Esc or a click outside closes the menu with the field focused: the window keys work again at once.
            field.Unloaded += (s, e) => { if (Keyboard.FocusedElement == field) Keyboard.ClearFocus(); };
            menu.AddElement(field);
            menu.AddElement(line);
            OpenMenu(menu, Transport.Time);
        }

        PopMenu RevealMenu()
        {
            var menu = new PopMenu(200);
            VideoState? v = state.Video;
            if (v == null) return menu;
            menu.Add(Copy.RevealVideo, Icons.Video, null, v.FilePath, () => Reveal(v.FilePath));
            MenuEntry notes = menu.Add(Copy.RevealNotes, Icons.Folder, null, v.NotesDir, () => Reveal(v.NotesPath, v.NotesDir));
            notes.IsEnabled = Directory.Exists(v.NotesDir);
            MenuEntry frames = menu.Add(Copy.RevealFrames, Icons.File, null, v.ImagesDir, () => Reveal(v.ImagesDir));
            frames.IsEnabled = Directory.Exists(v.ImagesDir);
            return menu;
        }

        PopMenu CopyMenu()
        {
            var menu = new PopMenu(260);
            menu.Add(Copy.CopyReadable, Icons.Note, "Ctrl+Shift+C", Copy.CopyReadableTip, () => CopyNotes(true));
            menu.Add(Copy.CopyNotes, Icons.File, "Ctrl+C", Copy.CopyNotesTip, () => CopyNotes(false));
            return menu;
        }

        /// <summary>The speed ladder under the speed control, the current step checked; a pick is the next play's speed.</summary>
        PopMenu SpeedMenu()
        {
            var menu = new PopMenu(120);
            int current = SpeedLadder.IndexOf(speed);
            for (int i = 0; i < SpeedLadder.Steps.Length; i++)
            {
                double s = SpeedLadder.Steps[i];
                MenuEntry e = menu.Add(SpeedLadder.Caption(s), i == current ? Icons.Check : "", null, null, () => ChooseSpeed(s));
                AutomationProperties.SetAutomationId(e, "Speed-" + s.ToString(CultureInfo.InvariantCulture));
            }
            return menu;
        }

        PopMenu RecentList()
        {
            var menu = new PopMenu(380);
            foreach (RecentItem item in RecentItem.Read(settings, openPath))
            {
                var row = new RecentRow(item, 370);
                row.Click += (s, e) => { menu.Close(); if (item.Exists && !item.IsOpen) Open(item.Path); };
                row.Menu += r => OpenMenu(RecentMenu.Build(r.Item, this), r);
                menu.AddElement(row);
            }
            return menu;
        }

        /// <summary>The menu of a note (its row in the list, its tag on the timeline): edit, copy, move, delete.</summary>
        PopMenu NoteMenu(NoteState n)
        {
            var menu = new PopMenu(230);
            menu.Add(Copy.MenuEdit, Icons.Pen, "F2", null, () => _ = EditNote(n.Id));
            if (!n.IsGeneral) menu.Add(Copy.MenuCopyTimecode, Icons.Clock, null, null, () => CopyText(n.Timecode));
            if (n.Text.Length > 0) menu.Add(Copy.MenuCopyText, Icons.Copy, null, null, () => CopyText(n.Text));
            if (n.Frame.HasValue)
            {
                MenuEntry move = menu.Add(Copy.MenuMoveHere, Icons.Move, null, Copy.MenuMoveHereTip, () => _ = MoveNote(n.Id, frame));
                move.IsEnabled = videoLoaded && frame != n.Frame.Value;
            }
            menu.AddSeparator();
            menu.Add(Copy.MenuRemoveNote, Icons.Trash, "Delete", null, () => RemoveNote(n.Id), danger: true);
            return menu;
        }

        /// <summary>The menu of the video: play or pause, the new notes in the order of their keys.</summary>
        PopMenu VideoMenu()
        {
            var menu = new PopMenu(240);
            menu.Add(playing ? Copy.MenuPause : Copy.MenuPlay, playing ? Icons.Pause : Icons.Play, "Space", null, TogglePlay);
            menu.AddSeparator();
            AddNewNotes(menu, null);
            return menu;
        }

        /// <summary>The menu of a frame of the timeline (a right click on the ruler or the empty track).</summary>
        PopMenu TrackMenu(int at)
        {
            var menu = new PopMenu(250);
            AddNewNotes(menu, at);
            menu.AddSeparator();
            menu.Add(Copy.MenuFit, Icons.Expand, "0", null, Timeline.ResetZoom);
            return menu;
        }

        /// <summary>The menu of the notes list outside its rows: a general note, the copies, Done.</summary>
        PopMenu ListMenu()
        {
            var menu = new PopMenu(260);
            if (!videoLoaded) return menu;
            menu.Add(Copy.MenuNewGeneral, Icons.Whole, NoteKinds.GeneralKey, null, () => _ = StartNote(null));
            menu.AddSeparator();
            menu.Add(Copy.MenuCopyReadable, Icons.Note, "Ctrl+Shift+C", Copy.CopyReadableTip, () => CopyNotes(true));
            menu.Add(Copy.MenuCopyNotes, Icons.File, "Ctrl+C", Copy.CopyNotesTip, () => CopyNotes(false));
            MenuEntry done = menu.Add(Copy.MenuDone, Icons.Package, "Ctrl+E", null, () => _ = Pack());
            done.IsEnabled = doneButton.IsEnabled;
            return menu;
        }

        /// <summary>
        /// The new-note entries in the order of their keys; with <paramref name="at"/> the frame notes go on that frame
        /// and the general note (which has no frame) is left out.
        /// </summary>
        void AddNewNotes(PopMenu menu, int? at)
        {
            for (int i = 0; i < NoteKinds.All.Length; i++)
            {
                string kind = NoteKinds.All[i];
                if (i == 3 && !at.HasValue) menu.Add(Copy.MenuNewGeneral, Icons.Whole, NoteKinds.GeneralKey, null, () => _ = StartNote(null));
                string label = string.Format(CultureInfo.InvariantCulture, at.HasValue ? Copy.MenuNewHere : Copy.MenuNew, kind);
                menu.Add(label, NoteKinds.Glyph(kind), NoteKinds.Key(kind), null, () => _ = StartNote(kind, at));
            }
        }

        // --- recent actions ---------------------------------------------------------------------------------------

        public void Reveal(RecentItem item)
        {
            if (item.HasNotesFolder) Reveal(NotesLayout.NotesPathFor(item.Path), NotesLayout.NotesDirFor(item.Path));
            else Reveal(item.Path);
        }

        void Reveal(string path, string? fallback = null)
        {
            string target = File.Exists(path) || Directory.Exists(path) ? path : fallback ?? path;
            if (!File.Exists(target) && !Directory.Exists(target)) { window.Root.Toasts.Show(ToastLevel.Info, Copy.RevealMissing); return; }
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"") { UseShellExecute = false }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { Banner.Show(ex.Message); }
        }

        public async Task<bool> DeleteNotes(RecentItem item)
        {
            bool isOpen = PathText.Same(openPath, item.Path);
            if (isOpen && edit != null) CancelEdit();
            try
            {
                // When the Recycle Bin cannot take the notes nothing goes without the explicit answer to delete for good.
                DeleteResult? r = await NotesLayout.DeleteNotesAsking(item.Path, ShellRecycler.Instance,
                    plan => DeleteQuestion.Ask(window.Root.Overlay, Copy.DeletePermanentText(plan.Notes, plan.Frames, plan.Kept, plan.Folder)));
                if (r == null) return false;
                isOpen = PathText.Same(openPath, item.Path);
                window.Root.Toasts.Show(r.Failed > 0 ? ToastLevel.Warning : ToastLevel.Success, Copy.DeletedNotice(r.Notes, r.Frames, r.Kept, r.Permanently, r.Failed));
                if (isOpen)
                {
                    selectedId = null;
                    Apply(unsaved.Open(openPath!));
                }
                if (openPath == null) Empty.SetRecent(RecentItem.Read(settings, null));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SessionException
                || ex is ArgumentException || ex is NotSupportedException)
            {
                // A path that is not valid (a hand-edited settings file) reaches the banner, never the UI thread's handler.
                Banner.Show(ex.Message);
                return false;
            }
        }

        public void Forget(RecentItem item)
        {
            settings.ForgetRecent(item.Path);
            SaveSettings();
            if (openPath == null) Empty.SetRecent(RecentItem.Read(settings, null));
        }

        void SaveSettings()
        {
            try { settings.Save(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Log.Warning("settings were not saved: " + ex.Message); }
        }

        // --- clipboard ------------------------------------------------------------------------------------------

        void CopyNotes(bool readable)
        {
            if (state.Video == null) return;
            if (Clip(readable ? session.ReadableText() : session.NotesText()))
                window.Root.Toasts.Show(ToastLevel.Success, readable ? Copy.ReadableCopied : Copy.NotesCopied);
        }

        void CopyText(string text)
        {
            if (Clip(text)) window.Root.Toasts.Show(ToastLevel.Info, string.Format(CultureInfo.InvariantCulture, Copy.Copied, text));
            if (edit != null) Video.Overlay.Composer.FocusField();
        }

        bool Clip(string text)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    System.Threading.Thread.Sleep(40);
                }
            }
            Banner.Show(Copy.ClipboardBusy);
            return false;
        }
    }
}
