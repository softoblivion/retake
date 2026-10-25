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
    /// <summary>The review: covering the video, the window position and shutdown.</summary>
    public sealed partial class Review
    {
        // --- covering the video ---------------------------------------------------------------------------------

        /// <summary>
        /// A native window covers the main window's tree, so a sheet or a menu of that tree gets a still of the video
        /// under it instead of the live picture. The overlay asks first (<see cref="PrepareCover"/>): the still is
        /// put in place before the sheet or the menu starts to appear, and the video comes back only after the last
        /// one has finished closing (<see cref="OverlayHost.Covering"/>). Tooltips and hover cards float in windows
        /// of their own above the video and need no still. A sheet or a menu also hides the overlay window and
        /// pauses playback.
        /// </summary>
        void UpdateCover()
        {
            // Nothing covers: the overlay shows and the video comes back, also when the video changed meanwhile.
            if (!window.Root.Overlay.Covering)
            {
                Video.Overlay.Suspended = false;
                if (Video.Frozen) Video.Thaw();
                return;
            }
            if (Video.IsFake || !videoLoaded || mpv == null) return;
            Video.Overlay.Suspended = true;
            if (!Video.Frozen && !freezing) _ = Freeze(null);
        }

        /// <summary>
        /// Before a sheet or a menu opens over the live video: pause, take the still, then let it open. Returns false
        /// when nothing has to be prepared (no video, the fake player, a still already in place).
        /// </summary>
        bool PrepareCover(Action open)
        {
            if (Video.IsFake || !videoLoaded || mpv == null || Video.Frozen) return false;
            if (playing) player?.Pause();
            Video.Overlay.Suspended = true;
            _ = Freeze(open);
            return true;
        }

        /// <summary>
        /// Takes the still of the video window off the UI thread and puts it in place; <paramref name="then"/> runs
        /// once it is there, or after 300 ms at the latest (a busy player), so a sheet never waits long. When the
        /// opening it was taken for did not happen after all (its anchor went away), the video comes back.
        /// </summary>
        async Task Freeze(Action? then)
        {
            if (freezing)
            {
                then?.Invoke();
                return;
            }
            freezing = true;
            RawFrame? raw = null;
            bool late = false;
            var clock = Stopwatch.StartNew();
            try
            {
                Task<RawFrame> grab = mpv!.GrabWindow();
                if (then != null && await Task.WhenAny(grab, Task.Delay(300)) != grab)
                {
                    late = true;
                    Action open = then;
                    then = null;
                    open();
                }
                raw = await grab;
            }
            catch (Exception ex) { Log.Warning("still of the video failed: " + ex.Message); }
            finally { freezing = false; }
            Log.Info("still: " + (raw == null ? "none" : raw.Width + "x" + raw.Height + " " + raw.Format + " stride " + raw.Stride) + " in " + clock.ElapsedMilliseconds + " ms" + (late ? " (late)" : ""));
            bool wanted = then != null || window.Root.Overlay.Covering;
            if (wanted && videoLoaded && !Video.Frozen) Video.Freeze(VideoView.Picture(raw));
            then?.Invoke();
            if (!window.Root.Overlay.Covering) UpdateCover();
        }

        // --- window ---------------------------------------------------------------------------------------------

        void RestorePosition()
        {
            Win32.VirtualScreen(out double left, out double topY, out double width, out double height);
            double dpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            if (!WindowPlace.Usable(settings.WindowX, settings.WindowY, window.Width, left, topY, width, height, dpi)) return;
            window.Left = settings.WindowX!.Value;
            window.Top = settings.WindowY!.Value;
        }

        async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (closed) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            try
            {
                await Commit(true);
                // Full screen keeps the place the window returns to; that place is what is saved.
                Point place = window.NormalTopLeft;
                if (window.WindowState == WindowState.Normal && WindowPlace.Savable(place.X, place.Y))
                {
                    settings.WindowX = (int)Math.Round(place.X);
                    settings.WindowY = (int)Math.Round(place.Y);
                }
                if (videoLoaded) session.SetLastFrame(frame);
                session.Close();
                SaveSettings();
                Video.Overlay.Releasing = true;
                Video.Overlay.Close();
                if (mpv != null) await Task.WhenAny(mpv.ShutdownAsync(), Task.Delay(3000));
                else player?.Dispose();
            }
            catch (Exception ex) { Log.Error("shutdown failed", ex); }
            closed = true;
            // Closing again from inside the first Closing event is refused: close once that event has returned.
            _ = ui.BeginInvoke(new Action(window.Close));
        }
    }
}
