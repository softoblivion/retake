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
    /// <summary>The review: playback and the keyboard.</summary>
    public sealed partial class Review
    {
        // --- playback -------------------------------------------------------------------------------------------

        void TogglePlay()
        {
            if (player == null || !videoLoaded) return;
            if (playing) player.Pause();
            else player.Play(speed);
        }

        void CycleSpeed()
        {
            int i = Array.IndexOf(Speeds, speed);
            SetSpeed(Speeds[(i + 1) % Speeds.Length]);
            if (playing) player?.Play(speed);
        }

        void SetSpeed(double s)
        {
            speed = s;
            Transport.SetSpeed(s);
        }

        async void ReverseWhileHeld()
        {
            if (player == null || reverseHeld) return;
            reverseHeld = true;
            if (playing) player.Pause();
            var clock = Stopwatch.StartNew();
            // The key's own state is read too: a release that lands in another window must not leave the steps running.
            while (reverseHeld && videoLoaded && player.Frame > 0 && Keyboard.IsKeyDown(Key.J))
            {
                clock.Restart();
                player.Step(-1);
                await player.Settle();
                int wait = (int)(1000 / rate - clock.ElapsedMilliseconds);
                if (wait > 0) await Task.Delay(wait);
            }
            reverseHeld = false;
            Trace();
        }

        // --- keyboard -------------------------------------------------------------------------------------------

        static bool IsTyping() => Keyboard.FocusedElement is TextBox;

        void OnKey(KeyEventArgs e)
        {
            if (e.Handled || closed) return;
            OverlayHost overlay = window.Root.Overlay;
            if (overlay.SheetOpen || overlay.MenuOpen) return;
            if (IsTyping()) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;
            if (Timeline.IsMoving)
            {
                if (key == Key.Escape) { e.Handled = true; Timeline.CancelMove(); }
                return;
            }
            if (edit != null)
            {
                if (key == Key.Enter) { e.Handled = true; _ = Commit(); }
                else if (key == Key.Escape) { e.Handled = true; CancelEdit(); }
                else if (mods == ModifierKeys.Control && NoteKinds.OfKey(key) is string kind)
                {
                    e.Handled = true;
                    Video.Overlay.Composer.ChangeKind(kind);
                }
                else if (mods == ModifierKeys.Control && NoteKinds.IsGeneralKey(key))
                {
                    e.Handled = true;
                    Video.Overlay.Composer.TogglePlace();
                }
                else if (key == Key.Back)
                {
                    e.Handled = true;
                    TextField field = Video.Overlay.Composer.Field;
                    if (field.Text.Length == 0) { if (edit.Region != null) SetEditRegion(null); }
                    else field.Text = field.Text.Substring(0, field.Text.Length - 1);
                }
                else Video.Overlay.SetTyping(true);
                return;
            }
            if ((mods & ModifierKeys.Control) != 0 && (mods & ModifierKeys.Alt) == 0)
            {
                if (key == Key.O) { e.Handled = true; OpenDialog(); }
                else if (key == Key.C && videoLoaded) { e.Handled = true; CopyNotes((mods & ModifierKeys.Shift) != 0); }
                else if (key == Key.E && videoLoaded) { e.Handled = true; _ = Pack(); }
                return;
            }
            if ((mods & ModifierKeys.Alt) != 0) return;
            if (key == Key.OemQuestion && mods == ModifierKeys.Shift)
            {
                // "?" on layouts that put it on Shift+/; other layouts reach OnText.
                e.Handled = true;
                keyMapToggled = DateTime.UtcNow;
                ToggleKeyMap();
                return;
            }
            bool nav = key == Key.Space || key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down || key == Key.Home || key == Key.End || key == Key.PageUp || key == Key.PageDown;
            if (nav) e.Handled = true;
            if (!videoLoaded || player == null) return;
            bool shift = (mods & ModifierKeys.Shift) != 0;
            if (!shift && NoteKinds.OfKey(key) is string newKind) { e.Handled = true; _ = StartNote(newKind); return; }
            if (!shift && NoteKinds.IsGeneralKey(key)) { e.Handled = true; _ = StartNote(null); return; }
            switch (key)
            {
                case Key.Space: TogglePlay(); break;
                case Key.Left: player.Step(shift ? -10 : -1); break;
                case Key.Right: player.Step(shift ? 10 : 1); break;
                case Key.Home: player.SeekFrame(0); break;
                case Key.End: player.SeekFrame(lastFrame); break;
                case Key.Up: SelectNeighbour(-1); break;
                case Key.Down: SelectNeighbour(1); break;
                case Key.F2: e.Handled = true; if (selectedId != null) _ = EditNote(selectedId); else { NoteState? n = state.Notes.FirstOrDefault(x => x.Frame == frame); if (n != null) _ = EditNote(n.Id); } break;
                case Key.Delete: e.Handled = true; if (selectedId != null) RemoveNote(selectedId); break;
                case Key.D0: case Key.NumPad0: if (!shift) { e.Handled = true; Timeline.ResetZoom(); } break;
                case Key.J: e.Handled = true; if (!e.IsRepeat) ReverseWhileHeld(); break;
                case Key.K: e.Handled = true; player.Pause(); SetSpeed(1); break;
                case Key.L:
                    e.Handled = true;
                    if (e.IsRepeat) break;
                    if (playing) { int i = Array.IndexOf(Speeds, speed); SetSpeed(Speeds[Math.Min(Speeds.Length - 1, i + 1)]); }
                    else SetSpeed(1);
                    player.Play(speed);
                    break;
            }
        }

        void OnKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.J) reverseHeld = false;
        }

        void OnText(TextCompositionEventArgs e)
        {
            if (edit != null && !IsTyping() && e.Text.Length > 0 && !char.IsControl(e.Text[0]))
            {
                // The overlay could not take the activation (another program held the foreground): the typing
                // reaches the main window, so it goes into the note field here instead of being lost.
                TextField field = Video.Overlay.Composer.Field;
                int at = field.CaretIndex;
                field.Text = field.Text.Insert(at, e.Text);
                field.CaretIndex = at + e.Text.Length;
                e.Handled = true;
                Video.Overlay.SetTyping(true);
                return;
            }
            if (e.Text != "?" || IsTyping() || edit != null) return;
            if ((DateTime.UtcNow - keyMapToggled).TotalMilliseconds < 500) { e.Handled = true; return; }
            OverlayHost overlay = window.Root.Overlay;
            if (overlay.SheetOpen && overlay.CurrentSheet != keyMap) return;
            e.Handled = true;
            ToggleKeyMap();
        }

        void ToggleKeyMap()
        {
            OverlayHost overlay = window.Root.Overlay;
            if (overlay.SheetOpen && overlay.CurrentSheet == keyMap) { overlay.CloseSheet(); return; }
            keyMap ??= KeyMap.Build();
            overlay.OpenSheet(keyMap, keysButton);
        }
    }
}
