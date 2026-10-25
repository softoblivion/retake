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

        /// <summary>The menu of the video: play or pause, the new notes, full screen.</summary>
        PopMenu VideoMenu()
        {
            var menu = new PopMenu(230);
            menu.Add(playing ? Copy.MenuPause : Copy.MenuPlay, playing ? Icons.Pause : Icons.Play, "Space", null, TogglePlay);
            menu.AddSeparator();
            AddNewNotes(menu, null);
            menu.Add(Copy.MenuNewGeneral, Icons.Note, "G", null, StartGeneralNote);
            menu.AddSeparator();
            menu.Add(window.IsFullScreen ? Copy.MenuFullExit : Copy.MenuFull, window.IsFullScreen ? Icons.Collapse : Icons.Expand, "F11", null, window.ToggleFullScreen);
            return menu;
        }

        /// <summary>The menu of a frame of the timeline (a right click on the ruler or the empty track).</summary>
        PopMenu TrackMenu(int at)
        {
            var menu = new PopMenu(240);
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
            menu.Add(Copy.MenuNewGeneral, Icons.Note, "G", null, StartGeneralNote);
            menu.AddSeparator();
            menu.Add(Copy.MenuCopyReadable, Icons.Note, "Ctrl+Shift+C", Copy.CopyReadableTip, () => CopyNotes(true));
            menu.Add(Copy.MenuCopyNotes, Icons.File, "Ctrl+C", Copy.CopyNotesTip, () => CopyNotes(false));
            MenuEntry done = menu.Add(Copy.MenuDone, Icons.Package, "Ctrl+E", null, () => _ = Pack());
            done.IsEnabled = doneButton.IsEnabled;
            return menu;
        }

        /// <summary>The three new-note entries; with <paramref name="at"/> the note goes on that frame.</summary>
        void AddNewNotes(PopMenu menu, int? at)
        {
            menu.Add(at.HasValue ? Copy.MenuNewFixHere : Copy.MenuNewFix, "", "1", null, () => _ = StartNote("FIX", at));
            menu.Add(at.HasValue ? Copy.MenuNewAddHere : Copy.MenuNewAdd, "", "2", null, () => _ = StartNote("ADD", at));
            menu.Add(at.HasValue ? Copy.MenuNewRemoveHere : Copy.MenuNewRemove, "", "3", null, () => _ = StartNote("REMOVE", at));
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

        public bool DeleteNotes(RecentItem item)
        {
            bool isOpen = openPath != null && string.Equals(Path.GetFullPath(openPath), Path.GetFullPath(item.Path), StringComparison.OrdinalIgnoreCase);
            if (isOpen && edit != null) CancelEdit();
            try
            {
                DeleteResult r = NotesLayout.DeleteNotes(item.Path);
                string message = r.Notes == 0 && r.Frames == 0 ? Copy.DeletedEmpty
                    : string.Format(CultureInfo.InvariantCulture, Copy.Deleted, Copy.Notes(r.Notes), Copy.Frames(r.Frames));
                window.Root.Toasts.Show(ToastLevel.Success, message);
                if (isOpen)
                {
                    selectedId = null;
                    Apply(session.Open(openPath!));
                }
                if (openPath == null) Empty.SetRecent(RecentItem.Read(settings, null));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SessionException)
            {
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
