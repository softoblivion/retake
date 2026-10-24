using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Retake.Core;

namespace Retake.Player
{
    /// <summary>
    /// The libmpv player, rendering into a native child window (<c>wid</c>). Options are the ones measured in the
    /// engine spike: exact seeks (<c>hr-seek=yes</c>, <c>hr-seek-demuxer-offset=1</c> for HEVC open GOP),
    /// <c>hwdec=auto-safe</c>, <c>vo=gpu-next</c>, paused at load, kept open at the end, no OSD, OSC or input
    /// bindings. Frame 0 is the position right after loading (<c>t0</c>); seeks target <c>t0 + f / fps</c>; the frame
    /// shown is <c>round((time-pos - t0) * fps)</c>.
    /// One seek is in flight at a time: a later target waits and replaces any other waiting target, so a held
    /// key or a scrub ends on the last frame asked for. A one-frame step with nothing pending uses
    /// <c>frame-step</c> / <c>frame-back-step</c>; anything else is an exact seek.
    /// </summary>
    public sealed class Player : IPlayer
    {
        enum Op { None, Seek, StepForward, StepBack }

        const int PendingTimeoutMs = 6000;

        readonly Dispatcher ui;
        readonly MpvClient mpv;
        readonly DispatcherTimer watchdog;
        bool disposed;

        // UI-thread state.
        bool loading, loaded;
        MediaInfo? info;
        double fps = 25, t0;
        int lastFrame, frame;
        bool playing;
        double speed = 1;
        Op op;
        int opTarget;
        int? queued;
        DateTime opStarted;
        readonly List<TaskCompletionSource<int>> settleWaiters = new List<TaskCompletionSource<int>>();

        // Written by the event thread, read on the UI thread.
        double lastTimePos = double.NaN;
        int timePosPosted;

        public event Action<MediaInfo>? Loaded;
        public event Action<int>? FrameChanged;
        public event Action<bool>? PlayingChanged;
        public event Action<string>? Failed;

        public static IEnumerable<KeyValuePair<string, string>> Options(IntPtr wid)
        {
            return new[]
            {
                Pair("config", "no"), Pair("terminal", "no"), Pair("idle", "yes"), Pair("force-window", "yes"),
                Pair("keep-open", "always"), Pair("pause", "yes"),
                Pair("hr-seek", "yes"), Pair("hr-seek-demuxer-offset", "1"), Pair("hwdec", "auto-safe"),
                Pair("vo", "gpu-next"), Pair("wid", wid.ToInt64().ToString(CultureInfo.InvariantCulture)),
                Pair("input-default-bindings", "no"), Pair("input-vo-keyboard", "no"), Pair("input-cursor", "no"),
                Pair("osc", "no"), Pair("osd-level", "0"), Pair("cursor-autohide", "no"), Pair("background-color", "#000000"),
                Pair("audio-display", "no"),
            };
        }

        static KeyValuePair<string, string> Pair(string k, string v) => new KeyValuePair<string, string>(k, v);

        public Player(IntPtr wid, Dispatcher ui)
        {
            this.ui = ui;
            mpv = new MpvClient(Options(wid));
            mpv.Observe("time-pos", MpvNative.FormatDouble);
            mpv.Observe("pause", MpvNative.FormatFlag);
            mpv.Observe("eof-reached", MpvNative.FormatFlag);
            mpv.Event += OnEvent;
            watchdog = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
            watchdog.Tick += (s, e) => CheckPending();
            Log.Info("player: libmpv client API " + MpvClient.Version() + ", " + string.Join(" ", Describe(Options(wid))));
        }

        static IEnumerable<string> Describe(IEnumerable<KeyValuePair<string, string>> options)
        {
            foreach (var kv in options) if (kv.Key != "wid") yield return kv.Key + "=" + kv.Value;
        }

        public bool IsLoaded => loaded;
        public bool Playing => playing;
        public double Speed => speed;
        public int Frame => op != Op.None ? (queued ?? opTarget) : frame;
        public int LastFrame => lastFrame;
        public MediaInfo? Info => info;

        // --- events from the player thread ---------------------------------------------------------------------

        void OnEvent(MpvEvent e)
        {
            if (e.Id == MpvNative.EventPropertyChange && e.Name == "time-pos")
            {
                Volatile.Write(ref lastTimePos, e.Number);
                if (Interlocked.Exchange(ref timePosPosted, 1) == 0)
                    ui.BeginInvoke(DispatcherPriority.Render, new Action(() => { Interlocked.Exchange(ref timePosPosted, 0); OnTimePos(Volatile.Read(ref lastTimePos)); }));
                return;
            }
            ui.BeginInvoke(DispatcherPriority.Normal, new Action(() => Handle(e)));
        }

        void Handle(MpvEvent e)
        {
            if (disposed) return;
            switch (e.Id)
            {
                case MpvNative.EventFileLoaded:
                    if (loading) ReadInfo();
                    break;
                case MpvNative.EventPlaybackRestart:
                    if (loading && info != null) FinishLoad();
                    else if (loaded) OnRestart();
                    break;
                case MpvNative.EventEndFile:
                    if (e.Reason == MpvNative.EndFileReasonError && (loading || loaded))
                    {
                        loading = loaded = false;
                        Failed?.Invoke("This file cannot be played (" + MpvNative.Error(e.Error) + ")");
                    }
                    break;
                case MpvNative.EventPropertyChange:
                    if (e.Name == "pause" && !double.IsNaN(e.Number)) OnPause(e.Number != 0);
                    break;
            }
        }

        void ReadInfo()
        {
            var i = new MediaInfo
            {
                Width = (int)mpv.GetLong("width", 0),
                Height = (int)mpv.GetLong("height", 0),
                Rate = FrameMath.SnapPlayerRate(mpv.GetDouble("container-fps")),
                Duration = mpv.GetDouble("duration"),
                FrameCount = Math.Max(0, mpv.GetLong("estimated-frame-count", 0)),
                Decoder = mpv.GetString("hwdec-current") ?? "",
            };
            if (i.Width <= 0 || i.Height <= 0)
            {
                loading = false;
                Failed?.Invoke("This file has no video to show");
                return;
            }
            info = i;
        }

        void FinishLoad()
        {
            if (info == null) return;
            double tp = mpv.GetDouble("time-pos");
            t0 = double.IsNaN(tp) ? 0 : tp;
            info.T0 = t0;
            info.Decoder = mpv.GetString("hwdec-current") ?? info.Decoder;
            loading = false;
            loaded = true;
            frame = 0;
            op = Op.None;
            queued = null;
            Log.Info("player: loaded " + info.Width + "x" + info.Height + ", rate " + (info.Rate?.ToString(CultureInfo.InvariantCulture) ?? "unknown")
                + ", t0 " + t0.ToString("R", CultureInfo.InvariantCulture) + ", decoder " + (info.Decoder.Length > 0 ? info.Decoder : "software"));
            Loaded?.Invoke(info);
        }

        void OnTimePos(double tp)
        {
            if (disposed || !loaded || double.IsNaN(tp)) return;
            int f = FrameMath.Clamp(FrameMath.FrameAt(tp, t0, fps), lastFrame);
            if (op == Op.StepForward)
            {
                if (f != frame) Complete(f);
                return;
            }
            if (op != Op.None) return; // a seek reports through the restart
            if (f != frame)
            {
                frame = f;
                FrameChanged?.Invoke(frame);
            }
        }

        void OnRestart()
        {
            if (op == Op.Seek || op == Op.StepBack) Complete(ReadFrame());
        }

        void OnPause(bool paused)
        {
            if (op == Op.StepForward || op == Op.StepBack) return; // frame-step unpauses for one frame
            bool now = !paused;
            if (now == playing) return;
            playing = now;
            PlayingChanged?.Invoke(playing);
        }

        int ReadFrame()
        {
            double tp = mpv.GetDouble("time-pos");
            return double.IsNaN(tp) ? frame : FrameMath.Clamp(FrameMath.FrameAt(tp, t0, fps), lastFrame);
        }

        void Complete(int shown)
        {
            op = Op.None;
            frame = shown;
            if (queued.HasValue)
            {
                int next = queued.Value;
                queued = null;
                if (next != frame) { StartSeek(next); return; }
            }
            watchdog.Stop();
            FrameChanged?.Invoke(frame);
            ReleaseWaiters();
        }

        void CheckPending()
        {
            if (op == Op.None) { watchdog.Stop(); return; }
            if ((DateTime.UtcNow - opStarted).TotalMilliseconds < PendingTimeoutMs && op != Op.StepForward) return;
            if (op == Op.StepForward && (DateTime.UtcNow - opStarted).TotalMilliseconds < 1500) return;
            Log.Warning("player: no answer to " + op + " " + opTarget + "; using the frame on screen");
            Complete(ReadFrame());
        }

        void ReleaseWaiters()
        {
            if (op != Op.None || settleWaiters.Count == 0) return;
            var list = settleWaiters.ToArray();
            settleWaiters.Clear();
            foreach (var w in list) w.TrySetResult(frame);
        }

        // --- commands -----------------------------------------------------------------------------------------

        public void Load(string path)
        {
            if (disposed) return;
            loaded = false;
            loading = true;
            info = null;
            op = Op.None;
            queued = null;
            frame = 0;
            playing = false;
            speed = 1;
            mpv.Set("pause", "yes");
            mpv.Set("speed", "1");
            int r = mpv.Command("loadfile", path, "replace");
            if (r < 0)
            {
                loading = false;
                Failed?.Invoke("This file cannot be played (" + MpvNative.Error(r) + ")");
            }
        }

        public void Unload()
        {
            if (disposed) return;
            loading = loaded = false;
            info = null;
            op = Op.None;
            queued = null;
            watchdog.Stop();
            ReleaseAll();
            mpv.Command("stop");
            if (playing) { playing = false; PlayingChanged?.Invoke(false); }
        }

        void ReleaseAll()
        {
            var list = settleWaiters.ToArray();
            settleWaiters.Clear();
            foreach (var w in list) w.TrySetResult(frame);
        }

        public void SetRate(double rate, int last)
        {
            if (rate > 0 && !double.IsNaN(rate) && !double.IsInfinity(rate)) fps = rate;
            lastFrame = Math.Max(0, last);
            if (loaded && op == Op.None)
            {
                int f = ReadFrame();
                if (f != frame) { frame = f; FrameChanged?.Invoke(frame); }
            }
        }

        public void SeekFrame(int target)
        {
            if (!loaded) return;
            target = FrameMath.Clamp(target, lastFrame);
            if (op != Op.None)
            {
                queued = target;
                FrameChanged?.Invoke(target);
                return;
            }
            if (target == frame && !playing) { FrameChanged?.Invoke(frame); return; }
            StartSeek(target);
        }

        void StartSeek(int target)
        {
            op = Op.Seek;
            opTarget = target;
            opStarted = DateTime.UtcNow;
            watchdog.Start();
            FrameChanged?.Invoke(target);
            double t = FrameMath.SeekTarget(t0, target, fps);
            int r = mpv.Command("seek", t.ToString("R", CultureInfo.InvariantCulture), "absolute+exact");
            if (r < 0) { Log.Warning("player: seek failed: " + MpvNative.Error(r)); Complete(ReadFrame()); }
        }

        public void Step(int frames)
        {
            if (!loaded || frames == 0) return;
            if (playing) Pause();
            int from = Frame;
            int target = FrameMath.Clamp(from + frames, lastFrame);
            if (target == from) return;
            if (op != Op.None) { queued = target; FrameChanged?.Invoke(target); return; }
            if (frames == 1 || frames == -1)
            {
                op = frames > 0 ? Op.StepForward : Op.StepBack;
                opTarget = target;
                opStarted = DateTime.UtcNow;
                watchdog.Start();
                FrameChanged?.Invoke(target);
                int r = mpv.Command(frames > 0 ? "frame-step" : "frame-back-step");
                if (r < 0) { op = Op.None; StartSeek(target); }
                return;
            }
            StartSeek(target);
        }

        public void Play(double rate = 1.0)
        {
            if (!loaded) return;
            if (rate <= 0) rate = 1;
            if (Math.Abs(rate - speed) > 1e-9)
            {
                speed = rate;
                mpv.Set("speed", rate.ToString("R", CultureInfo.InvariantCulture));
            }
            if (op != Op.None) { op = Op.None; queued = null; watchdog.Stop(); ReleaseAll(); }
            if (frame >= lastFrame)
            {
                frame = 0;
                mpv.Command("seek", FrameMath.SeekTarget(t0, 0, fps).ToString("R", CultureInfo.InvariantCulture), "absolute+exact");
                FrameChanged?.Invoke(0);
            }
            mpv.Set("pause", "no");
            if (!playing) { playing = true; PlayingChanged?.Invoke(true); }
        }

        public void Pause()
        {
            if (!loaded) return;
            mpv.Set("pause", "yes");
            if (playing)
            {
                playing = false;
                int f = ReadFrame();
                if (f != frame) { frame = f; FrameChanged?.Invoke(frame); }
                PlayingChanged?.Invoke(false);
            }
        }

        public Task<int> PauseAndSettle()
        {
            Pause();
            return Settle();
        }

        public Task<int> Settle()
        {
            if (!loaded || op == Op.None) return Task.FromResult(frame);
            var w = new TaskCompletionSource<int>();
            settleWaiters.Add(w);
            return w.Task;
        }

        public Task<RawFrame> GrabFrame()
        {
            if (!loaded) return Task.FromException<RawFrame>(new MpvException("No video is loaded"));
            return Task.Run(() => mpv.ScreenshotRaw("video"));
        }

        /// <summary>The picture as it is in the video window (letterbox included), for a still that stands in for the video; off the UI thread.</summary>
        public Task<RawFrame> GrabWindow()
        {
            if (!loaded) return Task.FromException<RawFrame>(new MpvException("No video is loaded"));
            return Task.Run(() =>
            {
                try { return mpv.ScreenshotRaw("window"); }
                catch (MpvException) { return mpv.ScreenshotRaw("video"); }
            });
        }

        /// <summary>Destroys the player off the UI thread (its video window sends messages to ours while it closes).</summary>
        public Task ShutdownAsync()
        {
            if (disposed) return Task.FromResult(0);
            disposed = true;
            watchdog.Stop();
            ReleaseAll();
            return Task.Run(() => mpv.Dispose());
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            watchdog.Stop();
            mpv.Dispose();
        }
    }
}
