using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using Retake.Core;
using Retake.Player;
using Retake.Views;

namespace Retake.Tests
{
    /// <summary>
    /// The pure parts of the player and the views: the frame formulas measured on libmpv in the engine spike, the
    /// timeline's ruler step and marker collisions, the region geometry at every Scale step, the window place.
    /// </summary>
    public static class ViewsTests
    {
        static readonly double[] ExactRates = { 24000.0 / 1001, 24, 25, 30000.0 / 1001, 50, 60000.0 / 1001, 60 };

        [Test("views.frames.seek_target_is_frame_start")]
        public static void SeekTargetIsFrameStart(TestEnv t)
        {
            Check.Near(12.28, FrameMath.SeekTarget(0, 307, 25), 9);
            Check.Near(22.28, FrameMath.SeekTarget(10, 307, 25), 9, "a MOV whose video starts at 10 s");
            Check.Near(0, FrameMath.SeekTarget(0, 0, 60000.0 / 1001), 9);
            Check.Near(1001.0 / 24000 * 24, FrameMath.SeekTarget(0, 24, 24000.0 / 1001), 9);
            Check.Throws<ArgumentException>(() => FrameMath.SeekTarget(0, 1, 0));
        }

        [Test("views.frames.round_trip_every_frame")]
        public static void RoundTripEveryFrame(TestEnv t)
        {
            foreach (double fps in ExactRates)
                foreach (double t0 in new[] { 0.0, 10.0 })
                    for (int f = 0; f < 1200; f++)
                    {
                        double pos = FrameMath.SeekTarget(t0, f, fps);
                        Check.Equal(f, FrameMath.FrameAt(pos, t0, fps), "fps " + fps + " t0 " + t0);
                        // WebM stores whole milliseconds: the shown position is the timestamp rounded to 1 ms.
                        double ms = Math.Round(pos * 1000) / 1000;
                        Check.Equal(f, FrameMath.FrameAt(ms, t0, fps), "millisecond timestamp, fps " + fps);
                    }
        }

        [Test("views.frames.round_not_floor")]
        public static void RoundNotFloor(TestEnv t)
        {
            // The spike's failures of the 1.x floor rule: WebM milliseconds and a float underrun on the last 4K frame.
            Check.Equal(5, FrameMath.FrameAt(0.083, 0, 60));
            Check.Equal(4, (int)Math.Floor(0.083 * 60 + 1e-4), "the 1.x floor rule misses this frame");
            Check.Equal(2, FrameMath.FrameAt(0.083, 0, 24000.0 / 1001));
            Check.Equal(499, FrameMath.FrameAt(19.959999999999997, 0, 25));
            Check.Equal(0, FrameMath.FrameAt(9.99, 10, 25), "before frame 0 clamps to 0");
            Check.Equal(0, FrameMath.FrameAt(double.NaN, 0, 25));
        }

        [Test("views.frames.player_rate")]
        public static void PlayerRate(TestEnv t)
        {
            Check.Equal(23.976, FrameMath.SnapPlayerRate(23.976025));
            Check.Equal(29.97, FrameMath.SnapPlayerRate(29.97003));
            Check.Equal(60.0, FrameMath.SnapPlayerRate(60.000004));
            Check.Null(FrameMath.SnapPlayerRate(double.NaN));
            Check.Null(FrameMath.SnapPlayerRate(0));
            Check.Equal(RateAgreement.Agree, FrameMath.Compare(25, 25));
            Check.Equal(RateAgreement.Agree, FrameMath.Compare(29.97, 29.975));
            Check.Equal(RateAgreement.Differ, FrameMath.Compare(25, 30));
            Check.Equal(RateAgreement.ProbeOnly, FrameMath.Compare(25, null));
            Check.Equal(RateAgreement.PlayerOnly, FrameMath.Compare(null, 25));
            Check.Equal(RateAgreement.Unknown, FrameMath.Compare(null, null));
            Check.Equal(25.0, FrameMath.WorkingRate(25, 30));
            Check.Equal(30.0, FrameMath.WorkingRate(null, 30));
            Check.Equal(25.0, FrameMath.WorkingRate(null, null));
        }

        [Test("views.frames.last_frame")]
        public static void LastFrame(TestEnv t)
        {
            Check.Equal(499, FrameMath.LastFrame(500, 30.0, 25), "the frame count wins over a duration that includes an empty edit");
            Check.Equal(479, FrameMath.LastFrame(null, 20.02, 24000.0 / 1001));
            Check.Equal(0, FrameMath.LastFrame(null, null, 25));
            Check.Equal(7, FrameMath.Clamp(7, 10));
            Check.Equal(10, FrameMath.Clamp(12, 10));
            Check.Equal(0, FrameMath.Clamp(-3, 10));
        }

        [Test("views.timeline.ruler_step")]
        public static void RulerStep(TestEnv t)
        {
            // 500 frames in 1000 px, a 60 px label: the step needs 82 px, 2 px per frame -> 50 frames (two seconds at 25).
            Check.Equal(50, TimelineMath.PickStep(2, 60, 25));
            Check.Equal(10, TimelineMath.MinorStep(50, 2));
            Check.Equal(5, TimelineMath.MinorStep(25, 2));
            Check.Equal(0, TimelineMath.MinorStep(1, 0.5), "no minor ticks for a one-frame step");
            Check.Equal(0, TimelineMath.MinorStep(5, 1), "minor ticks closer than 6 px are left out");
            Check.Equal(1, TimelineMath.PickStep(100, 60, 25), "zoomed in to 100 px per frame");
            Check.Equal(2, TimelineMath.PickStep(50, 60, 25));
            Check.Equal(30, TimelineMath.PickStep(3, 60, 30), "29.97 uses base 30");
            Check.Equal(3600 * 25, TimelineMath.PickStep(1e-6, 60, 25), "the largest step when nothing fits");
        }

        [Test("views.timeline.zoom_and_pan")]
        public static void ZoomAndPan(TestEnv t)
        {
            var v = new TimelineView(0, 500);
            TimelineView z = TimelineMath.ZoomAt(v, 200, 0.5, 500);
            Check.Near(250, z.Span, 9);
            Check.Near(TimelineMath.X(200, v, 1000), TimelineMath.X(200, z, 1000), 9, "the frame under the pointer stays in place");
            TimelineView tiny = TimelineMath.ZoomAt(z, 200, 1e-6, 500);
            Check.Near(12, tiny.Span, 9, "never fewer than 12 frames");
            TimelineView wide = TimelineMath.ZoomAt(z, 200, 100, 500);
            Check.Near(0, wide.Start, 9);
            Check.Near(500, wide.Span, 9);
            TimelineView panned = TimelineMath.Pan(z, 10, 500);
            Check.Near(250, panned.Start, 9, "a pan stops at the end");
            Check.Near(0, TimelineMath.Clamp(new TimelineView(-40, 100), 500).Start, 9);
            Check.Near(8, TimelineMath.Clamp(new TimelineView(0, 100), 8).Span, 9, "a video shorter than 12 frames shows whole");
            Check.Near(WheelIn(), 0.835, 3);
        }

        static double WheelIn() => TimelineMath.WheelFactor(120);

        [Test("views.timeline.follow_playhead")]
        public static void FollowPlayhead(TestEnv t)
        {
            var v = new TimelineView(100, 100);
            Check.Near(100, TimelineMath.Follow(v, 150, true, 1000).Start, 9, "visible: unchanged");
            Check.Near(245, TimelineMath.Follow(v, 250, true, 1000).Start, 9, "playing pages forward");
            Check.Near(450, TimelineMath.Follow(v, 500, false, 1000).Start, 9, "a jump centres");
        }

        [Test("views.timeline.marker_collisions")]
        public static void MarkerCollisions(TestEnv t)
        {
            var frames = new List<int> { 100, 100, 101, 300, 900 };
            List<MarkerSlot> slots = TimelineMath.Layout(frames, new TimelineView(0, 500), 1000);
            Check.Equal(3, slots.Count, "frame 900 is outside the view");
            Check.Equal(100, slots[0].Frame);
            Check.Equal(new List<int> { 0, 1 }, slots[0].Notes);
            Check.False(slots[0].ShowLabels, "the next stem is 2 px away: chips only");
            Check.Equal(101, slots[1].Frame);
            Check.True(slots[1].ShowLabels);
            Check.Near(392, slots[1].Room, 9);
            Check.True(slots[2].ShowLabels, "the last stem has room up to the end");
            List<MarkerSlot> zoomed = TimelineMath.Layout(frames, new TimelineView(95, 12), 1000);
            Check.True(zoomed[0].ShowLabels, "zoomed in, frames 100 and 101 are far enough apart");
        }

        [Test("views.timeline.short_text")]
        public static void ShortText(TestEnv t)
        {
            Check.Equal("", TimelineMath.ShortText(""));
            Check.Equal("logo flickers", TimelineMath.ShortText("  logo   flickers "));
            Check.Equal("one two three four five six…", TimelineMath.ShortText("one two three four five six seven"));
        }

        [Test("views.region.fit_and_drag")]
        public static void FitAndDrag(TestEnv t)
        {
            Box f = RegionMath.Fit(946, 584, 1920, 1080);
            Check.Near(946, f.W, 9);
            Check.Near(532.125, f.H, 9);
            Check.Near(25.9375, f.Y, 9, "letterboxed, centred");
            Box tall = RegionMath.Fit(946, 584, 1080, 1920);
            Check.Near(584, tall.H, 9);
            Check.Near((946 - 328.5) / 2, tall.X, 9, "pillarboxed, centred");

            Region r = RegionMath.FromDrag(f.X + 0.3 * f.W, f.Y + 0.4 * f.H, f.X + 0.6 * f.W, f.Y + 0.75 * f.H, f);
            Check.Equal("rect 0.300,0.400 0.600,0.750", r.ToText());
            Region back = RegionMath.FromDrag(f.X + 0.6 * f.W, f.Y + 0.75 * f.H, f.X + 0.3 * f.W, f.Y + 0.4 * f.H, f);
            Check.Equal(r.ToText(), back.ToText(), "the corners are ordered");
            Region click = RegionMath.FromDrag(f.X + 0.5 * f.W, f.Y + 0.3 * f.H, f.X + 0.5 * f.W + 3, f.Y + 0.3 * f.H, f);
            Check.Equal("point 0.500,0.300", click.ToText(), "a move under 4 px is a click");
            Region outside = RegionMath.FromDrag(-50, -50, f.X + 0.2 * f.W, f.Y + 0.2 * f.H, f);
            Check.Equal("rect 0.000,0.000 0.200,0.200", outside.ToText(), "a drag from the letterbox is clamped to the frame");
            Region flat = RegionMath.FromDrag(f.X + 0.5 * f.W, f.Y + 0.5 * f.H, f.X + 0.8 * f.W, f.Y + 0.5 * f.H, f);
            Check.Equal("point 0.500,0.500", flat.ToText(), "a rectangle without height becomes a point");
        }

        [Test("views.region.every_scale_step")]
        public static void EveryScaleStep(TestEnv t)
        {
            // The pointer moves in whole device pixels; the region is kept in fractions with three decimals. At every
            // Scale step and at 100 % and 150 % Windows scaling, a drag maps to the pixels under the pointer within
            // the rounding of the stored fractions (half a thousandth of the frame) plus one device pixel.
            Box f = RegionMath.Fit(946, 584, 1920, 1080);
            foreach (int step in Settings.UiScaleSteps)
                foreach (double dpi in new[] { 1.0, 1.25, 1.5 })
                {
                    double frameDevice = RegionMath.ToDevice(f.W, step, dpi);
                    double tolerance = 0.0005 * frameDevice + 1;
                    foreach (double[] p in new[] { new[] { 0.1, 0.2, 0.9, 0.8 }, new[] { 0.333, 0.25, 0.667, 0.75 }, new[] { 0.0, 0.0, 1.0, 1.0 } })
                    {
                        double x0 = Math.Round(RegionMath.ToDevice(f.X + p[0] * f.W, step, dpi)), y0 = Math.Round(RegionMath.ToDevice(f.Y + p[1] * f.H, step, dpi));
                        double x1 = Math.Round(RegionMath.ToDevice(f.X + p[2] * f.W, step, dpi)), y1 = Math.Round(RegionMath.ToDevice(f.Y + p[3] * f.H, step, dpi));
                        Region r = RegionMath.FromDrag(RegionMath.FromDevice(x0, step, dpi), RegionMath.FromDevice(y0, step, dpi),
                            RegionMath.FromDevice(x1, step, dpi), RegionMath.FromDevice(y1, step, dpi), f);
                        Box b = RegionMath.RegionBox(r, f);
                        string at = "scale " + step + " % dpi " + dpi;
                        Check.True(Math.Abs(RegionMath.ToDevice(b.X, step, dpi) - x0) <= tolerance, at + ": left edge");
                        Check.True(Math.Abs(RegionMath.ToDevice(b.Y, step, dpi) - y0) <= tolerance, at + ": top edge");
                        Check.True(Math.Abs(RegionMath.ToDevice(b.Right, step, dpi) - x1) <= tolerance, at + ": right edge");
                        Check.True(Math.Abs(RegionMath.ToDevice(b.Bottom, step, dpi) - y1) <= tolerance, at + ": bottom edge");
                    }
                }
        }

        [Test("views.region.capture_scale")]
        public static void CaptureScale(TestEnv t)
        {
            Box f = RegionMath.Fit(946, 584, 1920, 1080);
            Check.Near(1920 / 946.0, RegionMath.CaptureScale(1920, f), 9, "a 2 px line on screen is about 4 px in the 1080p image");
            Check.Near(3840 / 946.0, RegionMath.CaptureScale(3840, f), 9);
        }

        [Test("views.region.editor_place")]
        public static void EditorPlace(TestEnv t)
        {
            var f = new Box(0, 26, 946, 532);
            RegionMath.PlaceEditor(Region.CreateRect(0.2, 0.2, 0.4, 0.4), f, 946, 584, 300, 44, 0, out double l, out double top);
            Check.Near(0.4 * 946 + 12, l, 9, "right of the region");
            Check.Near(26 + 0.2 * 532, top, 9);
            RegionMath.PlaceEditor(Region.CreateRect(0.6, 0.2, 0.9, 0.4), f, 946, 584, 300, 44, 0, out l, out top);
            Check.Near(0.6 * 946 - 12 - 300, l, 9, "left when the right has no room");
            RegionMath.PlaceEditor(Region.CreateRect(0.1, 0.1, 0.95, 0.4), f, 946, 584, 300, 44, 0, out l, out top);
            Check.Near(26 + 0.4 * 532 + 12, top, 9, "below when neither side has room");
            RegionMath.PlaceEditor(null, f, 946, 584, 300, 44, 0, out l, out top);
            Check.Near((946 - 300) / 2.0, l, 9, "centred without a region");
            Check.Near(26 + 532 - 44 - 16, top, 9, "above the bottom of the frame");
            RegionMath.PlaceEditor(Region.CreatePoint(0.99, 0.99), f, 946, 584, 300, 44, 0, out l, out top);
            Check.True(l >= 8 && l + 300 <= 946 - 8 + 1e-9 && top >= 8 && top + 44 <= 584 - 8 + 1e-9, "clamped into the area");
        }

        [Test("views.window.place")]
        public static void Place(TestEnv t)
        {
            Check.True(WindowPlace.Usable(100, 80, 1280, 0, 0, 2560, 1440, 1));
            Check.True(WindowPlace.Usable(-1000, 80, 1280, 0, 0, 2560, 1440, 1), "partly off the left edge, 280 px still on screen");
            Check.False(WindowPlace.Usable(-1200, 80, 1280, 0, 0, 2560, 1440, 1), "less than 100 px reachable");
            Check.False(WindowPlace.Usable(2500, 80, 1280, 0, 0, 2560, 1440, 1));
            Check.False(WindowPlace.Usable(100, 1400, 1280, 0, 0, 2560, 1440, 1));
            Check.False(WindowPlace.Usable(null, 80, 1280, 0, 0, 2560, 1440, 1));
            Check.True(WindowPlace.Usable(1000, 500, 1280, 0, 0, 2560, 1440, 1.5), "logical positions are scaled to physical pixels");
            Check.False(WindowPlace.Usable(1700, 500, 1280, 0, 0, 2560, 1440, 1.5));
            Check.False(WindowPlace.Savable(-32000, -32000), "the parking place of a minimized window");
            Check.True(WindowPlace.Savable(-20, 0));
        }

        [Test("views.recent.short_folder")]
        public static void ShortFolder(TestEnv t)
        {
            Check.Equal(@"C:\clips", PathText.ShortFolder(@"C:\clips"));
            Check.Equal(@"C:\a\b", PathText.ShortFolder(@"C:\a\b"));
            Check.Equal(@"…\Review\review-3", PathText.ShortFolder(@"C:\Users\me\Review\review-3"));
            Check.Equal(@"…\b\c", PathText.ShortFolder(@"\\server\share\a\b\c\"));
        }
    

        [Test("views.frames.rational_rates_do_not_drift")]
        public static void RationalRatesDoNotDrift(TestEnv t)
        {
            foreach (int n in new[] { 24, 30, 48, 60, 120 })
            {
                double player = FrameMath.SnapPlayerRate(n * 1000.0 / 1001.0 + 0.00002)!.Value;
                double fps = Timecode.BaseFor(player).Fps;
                Check.Equal(n * 1000.0 / 1001.0, fps, "rate " + n);
                foreach (int f in new[] { 500, 1000, 3000, 100000, 1000000 })
                {
                    // the timestamp of frame f of a file at n * 1000 / 1001, in whole milliseconds as WebM keeps it
                    double pts = Math.Round(f * 1001.0 / (n * 1000.0) * 1000) / 1000;
                    Check.Equal(f, FrameMath.FrameAt(pts, 0, fps), "rate " + n + " frame " + f);
                    Check.Equal(f, FrameMath.FrameAt(FrameMath.SeekTarget(0, f, fps), 0, fps));
                }
            }
        }

        [Test("views.note_edit.move_and_text_both_stay_when_the_move_cannot_be_saved")]
        public static void NoteEditKeepsTextWhenTheMoveIsNotSaved(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            string id = c.Session.AddNote(307, "FIX", "old text", null, null).Notes[0].Id;
            string target = c.Path_("dummy_notes", "dummy_notes.txt");
            File.Delete(target);
            Directory.CreateDirectory(target);
            SessionException ex = Check.Throws<SessionException>(() => NoteEdit.Save(c.Session, id, true, null, "new text", null, false, null, "ADD"));
            Check.True(ex.NotSaved);
            NoteState n = c.Session.GetState().Notes[0];
            Check.True(n.IsGeneral, "the move is in memory");
            Check.Equal("new text", n.Text, "and so is the text");
            Check.Equal("ADD", n.Kind);
            Directory.Delete(target);
            c.Session.Save();
            Check.Equal("# Frame Notes 1\n# source: dummy.mp4\n# fps: 25\n-\tADD\tnew text\n", c.Text("dummy_notes", "dummy_notes.txt"));
            // an edit that changes nothing writes nothing
            Check.Equal(1, NoteEdit.Save(c.Session, id, false, null, null, null, false, null, null).Notes.Count);
        }

        [Test("views.thumbs.cache_keeps_a_value_while_the_file_is_unchanged")]
        public static void FileCacheKeepsValues(TestEnv t)
        {
            string a = t.Combine("a.png"), b = t.Combine("b.png");
            File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(b, new byte[] { 4 });
            int made = 0;
            var cache = new FileCache<string>(2);
            Func<string, string?> make = p => { made++; return p + "#" + made; };
            string? first = cache.Get(a, 128, make);
            Check.Equal(first, cache.Get(a, 128, make), "the same file: the kept value");
            Check.Equal(1, made);
            cache.Get(a, 228, make);
            Check.Equal(2, made, "another size is another entry");
            File.WriteAllBytes(a, new byte[] { 9, 9, 9, 9 });
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(1));
            Check.NotEqual(first, cache.Get(a, 128, make), "a changed file is made again");
            cache.Get(b, 128, make);
            Check.Equal(2, cache.Count, "the least recently used entry goes");
            File.Delete(b);
            Check.Null(cache.Get(b, 128, make), "a missing file has no value");
            Check.Null(cache.Get(null, 128, make));
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowEx(int exStyle, string className, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hwnd);

        [Test("views.unsaved.a_discard_covers_only_the_move_it_was_given_for")]
        public static void DiscardCoversOneMove(TestEnv t)
        {
            var c = new SessionCase(t);
            c.OpenUnknown();
            var gate = new UnsavedGate(c.Session);
            c.Session.AddNote(5, "FIX", "first", null, null);
            string target = c.Path_("dummy_notes", "dummy_notes.txt");
            File.Delete(target);
            Directory.CreateDirectory(target);
            SessionException ex = Check.Throws<SessionException>(() => c.Session.AddNote(6, "ADD", "second", null, null));
            gate.Failed(ex);
            Check.True(gate.MustAsk, "a failed write asks before the notes leave memory");
            gate.Discard();
            Check.False(gate.MustAsk, "the move the reviewer discarded for goes on without asking");
            // the open fails: the old video and its notes stay, and the next move asks again
            Check.Throws<SessionException>(() => gate.Open(c.Path_("missing.mp4")));
            Check.True(gate.MustAsk, "after a failed open");
            Check.Equal("dummy.mp4", c.Session.GetState().Video!.Name);
            // a restart that did not quit
            gate.Discard();
            gate.Revoke();
            Check.True(gate.MustAsk, "after a restart that did not quit");
            // a newer failed write after a Discard: closing must ask
            gate.Discard();
            ex = Check.Throws<SessionException>(() => c.Session.AddNote(7, "ADD", "third", null, null));
            gate.Failed(ex);
            Check.True(gate.MustAsk, "a newer failed write asks again");
            Check.Equal(3, c.Session.GetState().Notes.Count);
            // a refused call (not a failed write) leaves a Discard in force
            gate.Discard();
            gate.Failed(Check.Throws<SessionException>(() => c.Session.AddNote(8, "FIX", "  ", null, null)));
            Check.False(gate.MustAsk);
            gate.Close();
            Check.False(gate.MustAsk, "closed: nothing in memory");
            Directory.Delete(target);
        }

        [Test("views.video_windows.a_window_that_has_a_video_open_is_found_by_its_path")]
        public static void VideoWindowsFindTheOwner(TestEnv t)
        {
            string video = t.Combine("clips", "Promo.mp4");
            Check.Equal(VideoWindows.Key(video), VideoWindows.Key(video.ToLowerInvariant().Replace('\\', '/')), "one key for every spelling");
            Check.NotEqual(VideoWindows.Key(video), VideoWindows.Key(t.Combine("clips", "other.mp4")));
            // a hidden top-level window of this process stands for another review window (it is never shown)
            IntPtr hwnd = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Check.True(hwnd != IntPtr.Zero, "window created");
            try
            {
                Check.Equal(IntPtr.Zero, VideoWindows.Find(video, IntPtr.Zero));
                VideoWindows.Claim(hwnd, video);
                Check.Equal(hwnd, VideoWindows.Find(video.ToUpperInvariant(), IntPtr.Zero));
                Check.Equal(IntPtr.Zero, VideoWindows.Find(video, hwnd), "the window itself does not count");
                VideoWindows.Claim(hwnd, t.Combine("clips", "other.mp4"));
                Check.Equal(IntPtr.Zero, VideoWindows.Find(video, IntPtr.Zero), "a new video replaces the mark");
                VideoWindows.Claim(hwnd, null);
                Check.Equal(IntPtr.Zero, VideoWindows.Find(t.Combine("clips", "other.mp4"), IntPtr.Zero));
            }
            finally
            {
                DestroyWindow(hwnd);
            }
        }
    }
}
