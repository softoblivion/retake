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

        /// <summary>Chooses a speed: the next play uses it, and a running playback takes it at once.</summary>
        void ChooseSpeed(double s)
        {
            SetSpeed(s);
            if (playing && videoLoaded) player?.Play(speed);
            Trace();
        }

        void StepSpeed(int direction) => ChooseSpeed(SpeedLadder.Step(speed, direction));

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

        /// <summary>A shown field has the keyboard: keys are text. The closed note field types nothing (<see cref="KeyRoute.ReviewGets"/>).</summary>
        static bool IsTyping() => Keyboard.FocusedElement is TextBox t && t.IsVisible;

        static bool IsModifier(Key key) => key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl
            || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin;

        /// <summary>A key pressed with its modifiers, kept while a note is saved.</summary>
        readonly struct KeyPress
        {
            public KeyPress(Key key, ModifierKeys mods) { Key = key; Mods = mods; }
            public Key Key { get; }
            public ModifierKeys Mods { get; }
        }

        /// <summary>
        /// Keys pressed during a save wait for it; the editor's own keys (Enter, Esc, Backspace, Ctrl+type, Ctrl+4) and
        /// J, which acts only while held, do not.
        /// </summary>
        readonly PendingKeys<KeyPress> keysDuringSave = new PendingKeys<KeyPress>(k =>
            !IsModifier(k.Key) && k.Key != Key.Enter && k.Key != Key.Escape && k.Key != Key.Back && k.Key != Key.J
            && !((k.Mods & ModifierKeys.Control) != 0 && (NoteKinds.OfKey(k.Key) != null || NoteKinds.IsGeneralKey(k.Key))));

        void OnKey(KeyEventArgs e)
        {
            if (e.Handled || closed) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Route(key, Keyboard.Modifiers, e.IsRepeat)) e.Handled = true;
        }

        /// <summary>Runs the keys pressed during the save that just ended, in order; the editor that came back drops them.</summary>
        void ReplayKeysAfterSave()
        {
            IList<KeyPress> keys = keysDuringSave.Take();
            if (edit != null) return;
            foreach (KeyPress k in keys)
            {
                if (closed || edit != null) return;
                Route(k.Key, k.Mods, false);
            }
        }

        /// <summary>Does the work of a review key; true when the key was used.</summary>
        bool Route(Key key, ModifierKeys mods, bool repeat)
        {
            if (closed) return false;
            OverlayHost overlay = window.Root.Overlay;
            if (overlay.SheetOpen || overlay.MenuOpen) return false;
            if (IsTyping()) return false;
            if (Timeline.IsMoving)
            {
                if (key == Key.Escape) { Timeline.CancelMove(); return true; }
                return false;
            }
            if (quick != null && !IsModifier(key))
            {
                // The type bar: a type key saves, Enter goes on to the text, Esc drops; any other key drops the region
                // and then does its own work.
                if (mods == ModifierKeys.None && NoteKinds.OfKey(key) is string quickKind) { SaveQuick(quickKind); return true; }
                if (mods == ModifierKeys.None && key == Key.Enter) { QuickToText(); return true; }
                if (mods == ModifierKeys.None && key == Key.Escape) { DropQuick(); return true; }
                DropQuick();
            }
            bool handled = false;
            if (edit != null)
            {
                if (!KeyRoute.OpensField(true, Video.Overlay.Composer.IsOpen))
                {
                    // The note is being saved and its field is closed: no key reopens the field or acts on the note;
                    // the other keys wait for the save (KeyPress, ReplayKeysAfterSave).
                    keysDuringSave.Add(new KeyPress(key, mods), repeat);
                    return true;
                }
                if (key == Key.Enter) { handled = true; _ = Commit(); }
                else if (key == Key.Escape) { handled = true; CancelEdit(); }
                else if (mods == ModifierKeys.Control && NoteKinds.OfKey(key) is string kind)
                {
                    handled = true;
                    Video.Overlay.Composer.ChangeKind(kind);
                }
                else if (mods == ModifierKeys.Control && NoteKinds.IsGeneralKey(key))
                {
                    handled = true;
                    Video.Overlay.Composer.TogglePlace();
                }
                else if (key == Key.Back)
                {
                    handled = true;
                    TextField field = Video.Overlay.Composer.Field;
                    if (field.Text.Length == 0) { if (edit.Region != null) SetEditRegion(null); }
                    else
                    {
                        // The selection goes, else the character before the caret; the caret stays where the edit was.
                        int start = field.SelectionStart, length = field.SelectionLength;
                        if (length == 0 && start > 0) { start--; length = 1; }
                        if (length > 0)
                        {
                            field.Text = field.Text.Remove(start, length);
                            field.CaretIndex = start;
                        }
                    }
                }
                else Video.Overlay.SetTyping(true);
                return handled;
            }
            if ((mods & ModifierKeys.Control) != 0 && (mods & ModifierKeys.Alt) == 0)
            {
                if (key == Key.O) { handled = true; OpenDialog(); }
                else if (key == Key.C && videoLoaded) { handled = true; CopyNotes((mods & ModifierKeys.Shift) != 0); }
                else if (key == Key.E && videoLoaded) { handled = true; _ = Pack(); }
                else if (key == Key.Z && mods == ModifierKeys.Control && videoLoaded) { handled = true; UndoNote(); }
                else if (key == Key.G && mods == ModifierKeys.Control && videoLoaded) { handled = true; OpenGoTo(); }
                return handled;
            }
            if ((mods & ModifierKeys.Alt) != 0) return handled;
            if (key == Key.OemQuestion && mods == ModifierKeys.Shift)
            {
                // "?" on layouts that put it on Shift+/; other layouts reach OnText.
                handled = true;
                keyMapToggled = DateTime.UtcNow;
                ToggleKeyMap();
                return handled;
            }
            bool nav = key == Key.Space || key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down || key == Key.Home || key == Key.End;
            if (nav) handled = true;
            if (!videoLoaded || player == null) return handled;
            bool shift = (mods & ModifierKeys.Shift) != 0;
            if (!shift && NoteKinds.OfKey(key) is string newKind) { handled = true; _ = StartNote(newKind); return handled; }
            if (!shift && NoteKinds.IsGeneralKey(key)) { handled = true; _ = StartNote(null); return handled; }
            switch (key)
            {
                case Key.Space: TogglePlay(); break;
                case Key.Left: player.Step(shift ? -10 : -1); break;
                case Key.Right: player.Step(shift ? 10 : 1); break;
                case Key.Home: player.SeekFrame(0); break;
                case Key.End: player.SeekFrame(lastFrame); break;
                case Key.Up: SelectNeighbour(-1); break;
                case Key.Down: SelectNeighbour(1); break;
                case Key.F2: handled = true; if (selectedId != null) _ = EditNote(selectedId); else { NoteState? n = state.Notes.FirstOrDefault(x => x.Frame == frame); if (n != null) _ = EditNote(n.Id); } break;
                case Key.Delete: handled = true; if (selectedId != null) RemoveNote(selectedId); break;
                case Key.D0: case Key.NumPad0: if (!shift) { handled = true; Timeline.ResetZoom(); } break;
                case Key.J: handled = true; if (!repeat) ReverseWhileHeld(); break;
                case Key.K: handled = true; player.Pause(); SetSpeed(1); break;
                case Key.L:
                    handled = true;
                    if (repeat) break;
                    SetSpeed(playing ? SpeedLadder.ShuttleUp(speed) : 1);
                    player.Play(speed);
                    break;
                case Key.OemOpenBrackets: if (mods == ModifierKeys.None) { handled = true; StepSpeed(-1); } break;
                case Key.OemCloseBrackets: if (mods == ModifierKeys.None) { handled = true; StepSpeed(1); } break;
            }
            return handled;
        }

        void OnKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.J) reverseHeld = false;
        }

        void OnText(TextCompositionEventArgs e)
        {
            if (KeyRoute.OpensField(edit != null, Video.Overlay.Composer.IsOpen) && !IsTyping() && e.Text.Length > 0 && !char.IsControl(e.Text[0]))
            {
                // The overlay could not take the activation (another program held the foreground): the typing
                // reaches the main window, so it goes into the note field here instead of being lost.
                TextField field = Video.Overlay.Composer.Field;
                string typed = Retake.Core.Note.OneLine(e.Text);
                int at = field.SelectionStart, length = field.SelectionLength;
                field.Text = field.Text.Remove(at, length).Insert(at, typed);
                field.CaretIndex = at + typed.Length;
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
