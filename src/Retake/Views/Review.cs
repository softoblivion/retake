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
    /// <summary>
    /// The review window's content and its behaviour: the video with the region tool, the transport, the notes
    /// list, the timeline, the empty state, the title-bar controls, the keyboard, the context menus, opening
    /// (argument, dialog, drop, recent list), the notes package, the error banner. The core's
    /// <see cref="Session"/> holds the notes and writes every change; this class keeps only the playhead, the
    /// selection, the zoom and the note being written. Its parts: this file (layout, wiring, engine, opening,
    /// state), <c>Review.Notes.cs</c>, <c>Review.Input.cs</c>, <c>Review.Menus.cs</c>, <c>Review.Window.cs</c>.
    /// </summary>
    public sealed partial class Review : IRecentActions
    {
        /// <summary>
        /// The note being written: a new one or an edit; a null frame is a general note. <see cref="AtFrame"/> is the
        /// frame the note takes when the editor switches it from the whole video to a frame.
        /// </summary>
        sealed class Edit
        {
            public bool Create;
            public string Id = "", Kind = "FIX", OrigKind = "FIX", OrigText = "";
            public int? Frame;
            public int AtFrame;
            public bool OrigGeneral;
            public Region? Region, OrigRegion;
            public bool RegionDirty;
            public bool General => Frame == null;
        }

        public const double PanelWidth = 320, Gap = 10, Side = 12, CardRadius = 14;
        static readonly string[] VideoExtensions = { ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".mxf", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".wmv", ".flv", ".ogv", ".3gp" };
        static readonly double[] Speeds = { 1, 2, 4, 8 };

        readonly ShellWindow window;
        readonly Settings settings;
        readonly Paths paths;
        readonly Session session;
        readonly bool fake;
        readonly Dispatcher ui;

        // Views.
        readonly Grid layout = new Grid();
        readonly Grid review = new Grid();
        readonly Grid top = new Grid();
        readonly Grid dock = new Grid();
        readonly Border panelHost = new Border { Width = PanelWidth, Margin = new Thickness(Gap, 0, 0, 0) };
        public readonly ErrorBanner Banner = new ErrorBanner();
        public readonly VideoView Video;
        public readonly Transport Transport = new Transport();
        public readonly Timeline Timeline = new Timeline();
        public readonly NotesList Notes = new NotesList();
        public readonly EmptyState Empty = new EmptyState();
        readonly VideoName nameButton = new VideoName();
        readonly FrameRateControl rateButton = new FrameRateControl();
        readonly IconButton openButton, recentButton, copyButton, keysButton, panelButton;
        readonly GlassButton doneButton = new GlassButton(Copy.DoneLabel, Icons.Package, ButtonKind.Tinted, ButtonSize.S) { Margin = new Thickness(10, 0, 0, 0) };
        readonly Border asideSep = new Border { Width = 1, Height = 17, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        Sheet? keyMap;

        // State.
        IPlayer? player;
        Retake.Player.Player? mpv;
        bool engineReady;
        string? engineError, pendingOpen;
        SessionState state = new SessionState();
        string? openPath;
        bool videoLoaded;
        MediaInfo? media;
        double? probedRate;
        double rate = 25;
        TimecodeBase? tb;
        int lastFrame, frame;
        bool playing;
        double speed = 1;
        string? selectedId;
        Edit? edit;
        bool starting, saving, closing, closed, reverseHeld, freezing, packing;
        Task? commitTask;
        DateTime keyMapToggled;
        readonly DispatcherTimer settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };

        public Review(ShellWindow window, Settings settings, Paths paths, bool fakePlayer)
        {
            this.window = window;
            this.settings = settings;
            this.paths = paths;
            fake = fakePlayer;
            ui = window.Dispatcher;
            session = new Session(settings, s => { });
            Video = new VideoView(window, fake);

            openButton = TitleIcon(Icons.Video, "Open", Copy.OpenTip, "OpenButton");
            recentButton = TitleIcon(Icons.Clock, "Recent", Copy.RecentTip, "RecentButton");
            copyButton = TitleIcon(Icons.Copy, "Copy", Copy.CopyTip, "CopyButton");
            keysButton = TitleIcon(Icons.Keyboard, "Keyboard", Copy.KeysTip, "KeysButton");
            panelButton = TitleIcon(Icons.Panel, "Notes list", Copy.PanelHideTip, "PanelButton");
            Tip.Set(doneButton, Copy.DoneTip);
            AutomationProperties.SetAutomationId(doneButton, "DoneButton");
            asideSep.SetResourceReference(Border.BackgroundProperty, "T.stroke-2");
            Build();
            Wire();
        }

        static IconButton TitleIcon(string icon, string name, string tip, string id)
        {
            var b = new IconButton(icon, name, IconKind.Bare, ButtonSize.L) { Margin = new Thickness(1, 0, 1, 0) };
            Tip.Set(b, tip);
            AutomationProperties.SetAutomationId(b, id);
            return b;
        }

        // --- layout -------------------------------------------------------------------------------------------

        void Build()
        {
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.Children.Add(Banner);

            // The video and the notes list share the top; the dock under them spans the window: the control row
            // (frame steps, time, the note bar) over the timeline, on one card.
            review.Margin = new Thickness(Side, 6, Side, Side);
            review.RowDefinitions.Add(new RowDefinition());
            review.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(Video);
            panelHost.Child = Notes;
            Grid.SetColumn(panelHost, 1);
            top.Children.Add(panelHost);
            review.Children.Add(top);
            dock.Margin = new Thickness(0, Gap, 0, 0);
            dock.Children.Add(new ShadowLayer("card-drop", CardRadius));
            dock.Children.Add(new SurfaceVisual(CardRadius, "card-rim", null, "card-fill"));
            var dockRows = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
            Transport.Margin = new Thickness(2, 0, 2, 0);
            dockRows.Children.Add(Transport);
            dockRows.Children.Add(Timeline);
            dock.Children.Add(dockRows);
            Grid.SetRow(dock, 1);
            review.Children.Add(dock);
            Grid.SetRow(review, 1);
            layout.Children.Add(review);
            Grid.SetRow(Empty, 1);
            layout.Children.Add(Empty);
            window.Root.Body.Children.Add(layout);

            TitleBar bar = window.Root.TitleBar;
            nameButton.Margin = new Thickness(0, 0, 4, 0);
            bar.Centre.Children.Add(nameButton);
            bar.Centre.Children.Add(rateButton);
            foreach (UIElement e in new UIElement[] { openButton, recentButton, copyButton, asideSep, keysButton, panelButton, doneButton }) bar.Aside.Children.Add(e);
            bar.RefreshAside();
            SetPanel(settings.NotesPanelOpen);
            ShowReview(false);
        }

        void ShowReview(bool on)
        {
            // The review grid stays in the tree (Hidden, not Collapsed) so the video's child window exists from the start.
            review.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            Empty.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            Visibility v = on ? Visibility.Visible : Visibility.Collapsed;
            nameButton.Visibility = rateButton.Visibility = v;
            openButton.Visibility = recentButton.Visibility = copyButton.Visibility = asideSep.Visibility = panelButton.Visibility = doneButton.Visibility = v;
            window.Root.TitleBar.RefreshAside();
            if (!on)
            {
                Video.SetActive(false);
                Empty.SetRecent(RecentItem.Read(settings, null));
                window.Title = "Retake";
            }
        }

        void SetPanel(bool open)
        {
            panelHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            panelButton.IsSource = open;
            Tip.Set(panelButton, open ? Copy.PanelHideTip : Copy.PanelShowTip);
        }

        void TogglePanel()
        {
            settings.NotesPanelOpen = panelHost.Visibility != Visibility.Visible;
            SaveSettings();
            SetPanel(settings.NotesPanelOpen);
        }

        /// <summary>Done is on while there are notes to pack.</summary>
        void ShowDone()
        {
            bool any = state.Notes.Count > 0 && state.Timecode != null;
            doneButton.IsEnabled = any && !packing;
            Tip.Set(doneButton, any ? Copy.DoneTip : Copy.DoneOffTip);
        }

        // --- wiring -------------------------------------------------------------------------------------------

        void Wire()
        {
            window.PreviewKeyDown += (s, e) => OnKey(e);
            window.PreviewKeyUp += (s, e) => OnKeyUp(e);
            window.PreviewTextInput += (s, e) => OnText(e);
            window.PreviewMouseDown += (s, e) => { if (edit != null && !saving) _ = Commit(true); };
            window.AllowDrop = true;
            window.DragOver += OnDragOver;
            window.Drop += OnDrop;
            Video.Overlay.AllowDrop = true;
            Video.Overlay.DragOver += OnDragOver;
            Video.Overlay.Drop += OnDrop;
            Video.Overlay.KeyForward += OnKey;
            Video.Overlay.PreviewKeyUp += (s, e) => OnKeyUp(e);
            Video.Overlay.PreviewTextInput += (s, e) => OnText(e);
            Video.Overlay.Region.Drawn += OnRegionDrawn;
            Video.Overlay.ContextRequested += p => { if (videoLoaded) OpenMenuAt(VideoMenu(), Video.Area, p); };
            Video.Overlay.Composer.Commit += () => _ = Commit();
            Video.Overlay.Composer.Cancel += CancelEdit;
            Video.Overlay.Composer.ClearRegion += () => { if (edit != null && edit.Region != null) SetEditRegion(null); };
            Video.Overlay.Composer.KindChanged += SetEditKind;
            Video.Overlay.Composer.PlaceChanged += SetEditPlace;
            Video.FrameChanged += () => { if (edit != null) PlaceComposer(); };
            Themes.Changed += () => { Video.Overlay.Region.Retint(); Timeline.InvalidateVisual(); };

            Empty.OpenButton.Click += (s, e) => OpenDialog();
            Empty.OpenPath += path => Open(path);
            Empty.RowMenu += row => OpenMenu(RecentMenu.Build(row.Item, this), row);

            Transport.Back.Click += (s, e) => player?.Step(-1);
            Transport.Forward.Click += (s, e) => player?.Step(1);
            Transport.PlayPause.Click += (s, e) => TogglePlay();
            Transport.SpeedButton.Click += (s, e) => CycleSpeed();
            Transport.Time.Copied += CopyText;
            Transport.Duration.Copied += CopyText;
            Transport.FrameNumber.Copied += CopyText;
            foreach (NoteButton b in Transport.Kinds)
            {
                string kind = b.Kind!;
                b.Click += (s, e) => _ = StartNote(kind);
            }
            Transport.General.Click += (s, e) => _ = StartNote(null);
            Transport.SetEnabled(false);

            Timeline.Scrub += f => player?.SeekFrame(f);
            Timeline.Scrubbing += on => { if (on && playing) player?.Pause(); };
            Timeline.Pick += n => Select(n.Id, true);
            Timeline.Edit += n => _ = EditNote(n.Id);
            Timeline.Moving += (n, f) => { if (playing) player?.Pause(); player?.SeekFrame(f); };
            Timeline.Moved += (n, f) => _ = MoveNote(n.Id, f);
            Timeline.MoveCancelled += n => { if (n.Frame.HasValue) player?.SeekFrame(n.Frame.Value); };
            Timeline.TagMenu += (n, p) => OpenMenuAt(NoteMenu(n), Timeline, p);
            Timeline.TrackMenu += (f, p) => OpenMenuAt(TrackMenu(f), Timeline, p);

            Notes.Pick += n => Select(n.Id, true);
            Notes.Edit += n => _ = EditNote(n.Id);
            Notes.Copied += CopyText;
            Notes.RowMenu += (n, row, p) => { Select(n.Id, true); OpenMenuAt(NoteMenu(n), row, p); };
            Notes.ListMenu += p => OpenMenuAt(ListMenu(), Notes, p);

            nameButton.Click += (s, e) => OpenMenu(RevealMenu(), nameButton);
            rateButton.Click += (s, e) => OpenMenu(rateButton.BuildMenu(), rateButton);
            rateButton.Chosen += SetRate;
            openButton.Click += (s, e) => OpenDialog();
            recentButton.Click += (s, e) => OpenMenu(RecentList(), recentButton);
            copyButton.Click += (s, e) => OpenMenu(CopyMenu(), copyButton);
            keysButton.Click += (s, e) => ToggleKeyMap();
            panelButton.Click += (s, e) => TogglePanel();
            doneButton.Click += (s, e) => _ = Pack();

            settle.Tick += (s, e) => { settle.Stop(); if (videoLoaded && !playing) session.SetLastFrame(frame); };

            OverlayHost overlay = window.Root.Overlay;
            overlay.Prepare = PrepareCover;
            overlay.SheetChanged += open => { if (open && playing) player?.Pause(); };
            overlay.MenuChanged += open => { if (open && playing) player?.Pause(); };
            overlay.CoverChanged += on => UpdateCover();
            window.StateChanged += (s, e) => { if (window.WindowState == WindowState.Minimized && edit != null) CancelEdit(); };

            window.Activated += (s, e) => Video.ReclaimFocus();
            window.Deactivated += (s, e) => reverseHeld = false;
            window.SourceInitialized += (s, e) => RestorePosition();
            window.Loaded += (s, e) =>
            {
                window.Prefs.StartSaving();
                _ = StartEngine();
            };
            window.Closing += OnClosing;
            ShowDone();
        }

        // --- engine -------------------------------------------------------------------------------------------

        async Task StartEngine()
        {
            if (fake)
            {
                var f = new FakePlayer(ui);
                Video.SetFakeSource(f.Surface);
                Attach(f);
            }
            else
            {
                Empty.SetBusy(true);
                try
                {
                    string bin = paths.BinDir;
                    await Task.Run(() => LibmpvLoader.Load(bin));
                    IntPtr handle = Video.Handle;
                    if (handle == IntPtr.Zero)
                    {
                        var ready = new TaskCompletionSource<IntPtr>();
                        Video.HandleReady += h => ready.TrySetResult(h);
                        handle = Video.Handle != IntPtr.Zero ? Video.Handle : await ready.Task;
                    }
                    mpv = new Retake.Player.Player(handle, ui);
                    Attach(mpv);
                }
                catch (Exception ex)
                {
                    engineError = ex is InvalidOperationException ? ex.Message : string.Format(CultureInfo.InvariantCulture, Copy.PlayerFailed, ex.Message);
                    Log.Error("player could not start", ex);
                    Banner.Show(engineError);
                }
                Empty.SetBusy(false);
            }
            if (pendingOpen != null)
            {
                string p = pendingOpen;
                pendingOpen = null;
                Open(p);
            }
        }

        void Attach(IPlayer p)
        {
            player = p;
            engineReady = true;
            p.Loaded += OnLoaded;
            p.Failed += OnFailed;
            p.FrameChanged += OnFrame;
            p.PlayingChanged += OnPlaying;
        }

        /// <summary>Opens a video now, or once the player is ready.</summary>
        public void OpenWhenReady(string path)
        {
            if (engineReady || engineError != null) Open(path);
            else pendingOpen = path;
        }

        // --- opening ------------------------------------------------------------------------------------------

        public void OpenPath(string path) => Open(path);

        async void Open(string path)
        {
            if (!engineReady)
            {
                if (engineError != null) Banner.Show(engineError);
                else pendingOpen = path;
                return;
            }
            if (edit != null) await Commit(true);
            if (edit != null) return;
            SessionState st;
            try { st = session.Open(path); }
            catch (SessionException ex)
            {
                Banner.Show(ex.Message);
                if (openPath == null) Empty.SetRecent(RecentItem.Read(settings, null));
                return;
            }
            Banner.Hide();
            player!.Unload();
            videoLoaded = false;
            media = null;
            openPath = st.Video!.FilePath;
            selectedId = null;
            frame = 0;
            speed = 1;
            Transport.SetSpeed(1);
            probedRate = ReadProbedRate(openPath);
            Apply(st);
            Timeline.SetVideo(0, 25, null);
            Transport.SetEnabled(false);
            Transport.SetTime("", "", null);
            Video.SetActive(false);
            ShowReview(true);
            window.Title = st.Video.Name + " · Retake";
            nameButton.Caption = st.Video.Name;
            player.Load(openPath);
        }

        static double? ReadProbedRate(string path)
        {
            try
            {
                ProbeResult p = Probe.Read(path);
                return p.Fps.HasValue ? FrameMath.SnapPlayerRate(p.Fps.Value) : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
        }

        void OnLoaded(MediaInfo info)
        {
            if (openPath == null) return;
            media = info;
            videoLoaded = true;
            Video.SetVideoSize(info.Width, info.Height);
            ApplyRate();
            Timeline.SetVideo(lastFrame + 1, rate, tb);
            Timeline.SetNotes(state.Notes, selectedId);
            Video.SetActive(true);
            Transport.SetEnabled(true);
            int start = FrameMath.Clamp(state.Video?.LastFrame ?? 0, lastFrame);
            frame = 0;
            ShowTime();
            if (start > 0) player!.SeekFrame(start);
            AutomationProperties.SetItemStatus(Video, Video.FrameBox.ToString());
            UpdateCover();
        }

        void OnFailed(string message)
        {
            string name = state.Video?.Name ?? (openPath != null ? Path.GetFileName(openPath) : "");
            Banner.Show(name.Length > 0 ? string.Format(CultureInfo.InvariantCulture, Copy.CannotPlay, name, message) : message);
            CloseVideo();
        }

        void CloseVideo()
        {
            if (edit != null) EndEditUi();
            edit = null;
            session.Close();
            player?.Unload();
            videoLoaded = false;
            openPath = null;
            media = null;
            state = session.GetState();
            ShowReview(false);
        }

        public void OpenDialog()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open a video",
                Filter = "Videos|" + string.Join(";", VideoExtensions.Select(x => "*" + x)) + "|All files|*.*",
                CheckFileExists = true,
            };
            if (Directory.Exists(settings.LastDir)) dialog.InitialDirectory = settings.LastDir;
            if (dialog.ShowDialog(window) == true) Open(dialog.FileName);
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0) return;
            Log.Info("dropped " + files.Length + " file(s)");
            e.Handled = true;
            string pick = files.FirstOrDefault(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())) ?? files[0];
            window.Activate();
            Open(pick);
        }

        // --- state ----------------------------------------------------------------------------------------------

        void Apply(SessionState st)
        {
            state = st;
            if (selectedId != null && !st.Notes.Any(n => n.Id == selectedId)) selectedId = null;
            Notes.SetNotes(st.Notes, st.Counts, selectedId);
            Timeline.SetNotes(st.Notes, selectedId);
            if (videoLoaded) ApplyRate();
            else
            {
                double? fps = st.Video?.Fps;
                rateButton.Set(fps, st.Video?.FpsSource ?? "unknown", probedRate, null);
            }
            UpdateRegionDisplay();
            ShowDone();
            AutomationProperties.SetItemStatus(Notes, st.Notes.Count.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The rate of the playhead and of the timecodes: the session's, else the player's (stepping only).</summary>
        void ApplyRate()
        {
            VideoState? v = state.Video;
            if (v == null || player == null) return;
            double? sessionRate = v.Fps;
            double? playerRate = media?.Rate;
            RateAgreement agreement = FrameMath.Compare(probedRate, playerRate);
            double? suggestion = (agreement == RateAgreement.Differ || agreement == RateAgreement.PlayerOnly)
                && playerRate.HasValue && (!sessionRate.HasValue || Math.Abs(sessionRate.Value - playerRate.Value) >= FrameMath.RateTolerance) ? playerRate : null;
            rate = FrameMath.WorkingRate(sessionRate, playerRate);
            tb = sessionRate.HasValue ? Timecode.BaseFor(sessionRate.Value) : null;
            int? frames = v.DurationFrames ?? (media != null && media.FrameCount > 0 ? (int?)media.FrameCount : null);
            double? duration = v.Duration ?? (media != null && !double.IsNaN(media.Duration) ? (double?)media.Duration : null);
            lastFrame = FrameMath.LastFrame(frames, duration, rate);
            player.SetRate(rate, lastFrame);
            Timeline.SetRate(lastFrame + 1, rate, tb);
            rateButton.Set(sessionRate, v.FpsSource, probedRate, suggestion);
            ShowTime();
        }

        void SetRate(double? fps)
        {
            if (state.Video == null) return;
            try { Apply(session.SetFps(fps)); }
            catch (SessionException ex) { Banner.Show(ex.Message); Apply(session.GetState()); }
        }

        string Format(int f) => tb != null ? Timecode.FrameToTimecode(Math.Max(0, f), tb) : Clock(f / rate);

        static string Clock(double seconds)
        {
            int ms = (int)Math.Round(seconds * 1000);
            return (ms / 60000).ToString(CultureInfo.InvariantCulture) + ":" + (ms / 1000 % 60).ToString("00", CultureInfo.InvariantCulture) + "." + (ms % 1000).ToString("000", CultureInfo.InvariantCulture);
        }

        void ShowTime()
        {
            if (!videoLoaded) { Transport.SetTime("", "", null); return; }
            Transport.SetTime(Format(frame), Format(lastFrame + 1), frame);
        }

        /// <summary>The window's automation status: the state a UI check reads (playhead, playback, the note being written).</summary>
        void Trace()
        {
            AutomationProperties.SetItemStatus(window, "frame " + frame + (playing ? " playing" : " paused") + " speed " + speed.ToString(CultureInfo.InvariantCulture)
                + (edit != null ? " editing" : "") + (starting ? " starting" : "") + (saving ? " saving" : "") + (reverseHeld ? " reverse" : "")
                + " selected " + (selectedId ?? "-"));
        }

        void OnFrame(int f)
        {
            frame = f;
            Trace();
            ShowTime();
            Timeline.SetPlayhead(f, playing);
            UpdateRegionDisplay();
            if (!playing) { settle.Stop(); settle.Start(); }
        }

        void OnPlaying(bool on)
        {
            playing = on;
            Trace();
            Transport.SetPlaying(on);
            Timeline.SetPlayhead(frame, on);
            UpdateRegionDisplay();
            if (!on) { settle.Stop(); settle.Start(); }
            else if (Video.Frozen) Video.Thaw();
        }
    }
}
