using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Retake.Core;

namespace Retake.Player
{
    /// <summary>
    /// The player of <c>--fake-player</c>: no libmpv, generated frames with the same interface, for checks of the
    /// views on a machine or build without the DLL. A file is read by the core's probe (size, rate, frame count;
    /// 1280 x 720 at 25 fps for 20 s when the container does not say); a file the probe does not recognise fails
    /// like an unplayable file. A frame shows the frame number in large digits and the frame index as the barcode
    /// of the engine test clips (the top 96 rows: row A 16 blocks, white for a set bit; row B the
    /// complement). Playback advances by the wall clock times the speed.
    /// </summary>
    public sealed class FakePlayer : IPlayer
    {
        readonly Dispatcher ui;
        readonly DispatcherTimer clock;
        readonly DrawingImage surface = new DrawingImage();
        MediaInfo? info;
        bool loaded, playing;
        double speed = 1, fps = 25;
        int frame, lastFrame;
        double playStartFrame;
        DateTime playStart;

        public event Action<MediaInfo>? Loaded;
        public event Action<int>? FrameChanged;
        public event Action<bool>? PlayingChanged;
        public event Action<string>? Failed;

        public FakePlayer(Dispatcher ui)
        {
            this.ui = ui;
            clock = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = TimeSpan.FromMilliseconds(10) };
            clock.Tick += (s, e) => Advance();
        }

        /// <summary>The picture to show in the video area.</summary>
        public ImageSource Surface => surface;

        public bool IsLoaded => loaded;
        public bool Playing => playing;
        public double Speed => speed;
        public int Frame => frame;
        public int LastFrame => lastFrame;
        public MediaInfo? Info => info;

        public void Load(string path)
        {
            Unload();
            ProbeResult p;
            try { p = Probe.Read(path); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Failed?.Invoke("This file cannot be played (" + ex.Message + ")");
                return;
            }
            if (p.Container == "unknown")
            {
                ui.BeginInvoke(new Action(() => Failed?.Invoke("This file cannot be played (unrecognized file format)")));
                return;
            }
            double rate = p.Fps.HasValue ? Timecode.NominalRate(p.Fps.Value) : 25;
            var i = new MediaInfo
            {
                Width = p.Width ?? 1280,
                Height = p.Height ?? 720,
                Rate = p.Fps.HasValue ? rate : (double?)null,
                Duration = p.Duration ?? 20,
                FrameCount = p.Frames ?? (long)Math.Round((p.Duration ?? 20) * rate),
                Decoder = "fake",
            };
            fps = rate;
            lastFrame = (int)Math.Max(0, i.FrameCount - 1);
            ui.BeginInvoke(new Action(() =>
            {
                info = i;
                loaded = true;
                frame = 0;
                Paint();
                Loaded?.Invoke(i);
            }));
        }

        public void Unload()
        {
            clock.Stop();
            loaded = false;
            info = null;
            surface.Drawing = null;
            if (playing) { playing = false; PlayingChanged?.Invoke(false); }
        }

        public void SetRate(double rate, int last)
        {
            if (rate > 0) fps = rate;
            lastFrame = Math.Max(0, last);
            if (frame > lastFrame) Show(lastFrame);
        }

        public void SeekFrame(int target)
        {
            if (!loaded) return;
            if (playing) { playStartFrame = FrameMath.Clamp(target, lastFrame); playStart = DateTime.UtcNow; }
            Show(FrameMath.Clamp(target, lastFrame));
        }

        public void Step(int frames)
        {
            if (!loaded) return;
            if (playing) Pause();
            Show(FrameMath.Clamp(frame + frames, lastFrame));
        }

        public void Play(double rate = 1)
        {
            if (!loaded) return;
            speed = rate <= 0 ? 1 : rate;
            if (frame >= lastFrame) Show(0);
            playStartFrame = frame;
            playStart = DateTime.UtcNow;
            clock.Start();
            if (!playing) { playing = true; PlayingChanged?.Invoke(true); }
        }

        public void Pause()
        {
            clock.Stop();
            if (playing) { playing = false; PlayingChanged?.Invoke(false); }
        }

        public Task<int> PauseAndSettle() { Pause(); return Task.FromResult(frame); }
        public Task<int> Settle() => Task.FromResult(frame);

        public Task<RawFrame> GrabFrame()
        {
            if (!loaded || info == null) return Task.FromException<RawFrame>(new InvalidOperationException("No video is loaded"));
            int w = info.Width, h = info.Height;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen()) dc.DrawDrawing(Picture(frame, w, h));
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var raw = new RawFrame { Width = w, Height = h, Stride = w * 4, Format = "bgr0", Data = new byte[w * h * 4] };
            bmp.CopyPixels(raw.Data, raw.Stride, 0);
            return Task.FromResult(raw);
        }

        public void Dispose() => Unload();

        void Advance()
        {
            double elapsed = (DateTime.UtcNow - playStart).TotalSeconds;
            int f = (int)Math.Floor(playStartFrame + elapsed * fps * speed);
            if (f >= lastFrame)
            {
                Show(lastFrame);
                Pause();
                return;
            }
            if (f != frame) Show(f);
        }

        void Show(int f)
        {
            frame = f;
            Paint();
            FrameChanged?.Invoke(frame);
        }

        void Paint()
        {
            if (info == null) return;
            surface.Drawing = Picture(frame, info.Width, info.Height);
        }

        /// <summary>The generated picture of a frame at w x h.</summary>
        public static Drawing Picture(int index, int w, int h)
        {
            var g = new DrawingGroup();
            using (DrawingContext dc = g.Open())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1C, 0x26, 0x38), Color.FromRgb(0x38, 0x24, 0x30), 45), null, new Rect(0, 0, w, h));
                double band = Math.Min(96, h / 8.0), row = band / 2, bw = w / 16.0;
                for (int b = 0; b < 16; b++)
                {
                    bool set = ((index >> b) & 1) == 1;
                    dc.DrawRectangle(set ? Brushes.White : Brushes.Black, null, new Rect(b * bw, 0, bw, row));
                    dc.DrawRectangle(set ? Brushes.Black : Brushes.White, null, new Rect(b * bw, row, bw, row));
                }
                var text = new FormattedText(index.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), h * 0.22, Brushes.White, 1.0);
                dc.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), Math.Max(1, h / 540.0)), new Rect(w * 0.25, h * 0.6, w * 0.5, h * 0.25));
            }
            g.Freeze();
            return g;
        }
    }
}
