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
    /// list, the timeline, the empty state, the title-bar controls, the keyboard, opening (argument, dialog, drop,
    /// recent list), the error banner. The core's <see cref="Session"/> holds the notes and writes every change;
    /// this class keeps only the playhead, the selection, the zoom and the note being written.
    /// </summary>
    public sealed class Review : IRecentActions
    {
        sealed class Edit
        {
            public bool Create;
            public string Id = "", Kind = "FIX", OrigText = "";
            public int Frame;
            public Region? Region, OrigRegion;
            public bool RegionDirty;
        }

        public const double PanelWidth = 300, Gap = 10, Side = 12;
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
        readonly Border asideSep = new Border { Width = 1, Height = 15, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
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
        bool starting, saving, closing, closed, reverseHeld, freezing;
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

            openButton = TitleIcon(Icons.Folder, "Open", Copy.OpenTip, "OpenButton");
            recentButton = TitleIcon(Icons.Clock, "Recent", Copy.RecentTip, "RecentButton");
            copyButton = TitleIcon(Icons.Copy, "Copy", Copy.CopyTip, "CopyButton");
            keysButton = TitleIcon(Icons.Keyboard, "Keyboard", Copy.KeysTip, "KeysButton");
            panelButton = TitleIcon(Icons.Panel, "Notes list", Copy.PanelHideTip, "PanelButton");
            asideSep.SetResourceReference(Border.BackgroundProperty, "T.stroke-2");
            Build();
            Wire();
        }

        static IconButton TitleIcon(string icon, string name, string tip, string id)
        {
            var b = new IconButton(icon, name, IconKind.Bare, ButtonSize.M) { Margin = new Thickness(1, 0, 1, 0) };
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

            review.Margin = new Thickness(Side, 8, Side, Side);
            review.RowDefinitions.Add(new RowDefinition());
            review.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var stage = new Grid();
            stage.RowDefinitions.Add(new RowDefinition());
            stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            stage.Children.Add(Video);
            Grid.SetRow(Transport, 1);
            Transport.Margin = new Thickness(0, 4, 0, 0);
            stage.Children.Add(Transport);
            top.Children.Add(stage);
            panelHost.Child = Notes;
            Grid.SetColumn(panelHost, 1);
            top.Children.Add(panelHost);
            review.Children.Add(top);
            Grid.SetRow(Timeline, 1);
            Timeline.Margin = new Thickness(0, Gap, 0, 0);
            review.Children.Add(Timeline);
            Grid.SetRow(review, 1);
            layout.Children.Add(review);
            Grid.SetRow(Empty, 1);
            layout.Children.Add(Empty);
            window.Root.Body.Children.Add(layout);

            TitleBar bar = window.Root.TitleBar;
            nameButton.Margin = new Thickness(0, 0, 4, 0);
            bar.Centre.Children.Add(nameButton);
            bar.Centre.Children.Add(rateButton);
            foreach (UIElement e in new UIElement[] { openButton, recentButton, copyButton, asideSep, keysButton, panelButton }) bar.Aside.Children.Add(e);
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
            openButton.Visibility = recentButton.Visibility = copyButton.Visibility = asideSep.Visibility = panelButton.Visibility = v;
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

        // --- wiring -------------------------------------------------------------------------------------------

        void Wire()
        {
            window.PreviewKeyDown += (s, e) => OnKey(e);
            window.PreviewKeyUp += (s, e) => OnKeyUp(e);
            window.PreviewTextInput += (s, e) => OnText(e);
            window.PreviewMouseDown += (s, e) => { if (edit != null && !saving) _ = Commit(); };
            window.AllowDrop = true;
            window.DragOver += OnDragOver;
            window.Drop += OnDrop;
            Video.Overlay.AllowDrop = true;
            Video.Overlay.DragOver += OnDragOver;
            Video.Overlay.Drop += OnDrop;
            Video.Overlay.KeyForward += OnKey;
            Video.Overlay.PreviewKeyUp += (s, e) => OnKeyUp(e);
            Video.Overlay.Region.Drawn += OnRegionDrawn;
            Video.Overlay.Composer.Commit += () => _ = Commit();
            Video.Overlay.Composer.Cancel += CancelEdit;
            Video.Overlay.Composer.ClearRegion += () => { if (edit != null && edit.Region != null) SetEditRegion(null); };
            Video.FrameChanged += () => { if (edit != null) PlaceComposer(); };
            Themes.Changed += () => { Video.Overlay.Region.Retint(); Timeline.InvalidateVisual(); };

            Empty.OpenButton.Click += (s, e) => OpenDialog();
            Empty.OpenPath += path => Open(path);
            Empty.RowMenu += row => OpenMenu(RecentMenu.Build(row.Item, this), row);

            Transport.First.Click += (s, e) => player?.SeekFrame(0);
            Transport.Back.Click += (s, e) => player?.Step(-1);
            Transport.Forward.Click += (s, e) => player?.Step(1);
            Transport.Last.Click += (s, e) => player?.SeekFrame(lastFrame);
            Transport.PlayPause.Click += (s, e) => TogglePlay();
            Transport.SpeedButton.Click += (s, e) => CycleSpeed();
            Transport.Time.Copied += CopyText;
            Transport.Duration.Copied += CopyText;
            Transport.FrameNumber.Copied += CopyText;
            Transport.Fix.Click += (s, e) => _ = StartNote("FIX");
            Transport.Add.Click += (s, e) => _ = StartNote("ADD");
            Transport.Remove.Click += (s, e) => _ = StartNote("REMOVE");
            Transport.SetEnabled(false);

            Timeline.Scrub += f => player?.SeekFrame(f);
            Timeline.Scrubbing += on => { if (on && playing) player?.Pause(); };
            Timeline.Pick += n => Select(n.Id, true);
            Timeline.Edit += n => _ = EditNote(n.Id);

            Notes.Pick += n => Select(n.Id, true);
            Notes.Edit += n => _ = EditNote(n.Id);
            Notes.Copied += CopyText;

            nameButton.Click += (s, e) => OpenMenu(RevealMenu(), nameButton);
            rateButton.Click += (s, e) => OpenMenu(rateButton.BuildMenu(), rateButton);
            rateButton.Chosen += SetRate;
            openButton.Click += (s, e) => OpenDialog();
            recentButton.Click += (s, e) => OpenMenu(RecentList(), recentButton);
            copyButton.Click += (s, e) => OpenMenu(CopyMenu(), copyButton);
            keysButton.Click += (s, e) => ToggleKeyMap();
            panelButton.Click += (s, e) =>
            {
                settings.NotesPanelOpen = panelHost.Visibility != Visibility.Visible;
                SaveSettings();
                SetPanel(settings.NotesPanelOpen);
            };

            settle.Tick += (s, e) => { settle.Stop(); if (videoLoaded && !playing) session.SetLastFrame(frame); };

            OverlayHost overlay = window.Root.Overlay;
            overlay.SheetChanged += open => { if (open && playing) player?.Pause(); UpdateCover(); };
            overlay.MenuChanged += open => { if (open && playing) player?.Pause(); UpdateCover(); };
            window.Root.LayoutUpdated += (s, e) => { if (!Video.IsFake && videoLoaded) UpdateCover(); };
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
            if (edit != null) await Commit();
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

        // --- selection ------------------------------------------------------------------------------------------

        NoteState? Note(string? id) => id == null ? null : state.Notes.FirstOrDefault(n => n.Id == id);

        void Select(string? id, bool seek)
        {
            if (edit != null) return;
            selectedId = id;
            Notes.Select(id);
            Timeline.SetSelected(id);
            NoteState? n = Note(id);
            if (seek && n != null && player != null)
            {
                if (playing) player.Pause();
                player.SeekFrame(n.Frame);
            }
            UpdateRegionDisplay();
        }

        void SelectNeighbour(int dir)
        {
            List<NoteState> list = state.Notes;
            if (list.Count == 0) return;
            int index = list.FindIndex(n => n.Id == selectedId);
            if (index < 0)
            {
                int playhead = player?.Frame ?? frame;
                index = dir < 0 ? list.FindLastIndex(n => n.Frame < playhead) : list.FindIndex(n => n.Frame >= playhead);
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
            if (n != null && n.Region != null && !playing && frame == n.Frame && (player == null || player.Frame == n.Frame)) tool.Show(n.Region, n.Kind);
            else tool.Show(null, tool.Shown == null ? "FIX" : n?.Kind ?? "FIX");
        }

        // --- notes ----------------------------------------------------------------------------------------------

        async Task StartNote(string kind)
        {
            if (!videoLoaded || player == null || edit != null || starting || saving) return;
            if (state.Timecode == null)
            {
                window.Root.Toasts.Show(ToastLevel.Warning, Copy.NeedRate);
                OpenMenu(rateButton.BuildMenu(), rateButton);
                return;
            }
            starting = true;
            int f;
            try { f = await player.PauseAndSettle(); }
            finally { starting = false; }
            if (edit != null || !videoLoaded) return;
            Select(null, false);
            edit = new Edit { Create = true, Kind = kind, Frame = f };
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
                if (player.Frame != n.Frame) player.SeekFrame(n.Frame);
                await player.Settle();
            }
            finally { starting = false; }
            if (edit != null) return;
            edit = new Edit { Create = false, Id = n.Id, Kind = n.Kind, Frame = n.Frame, Region = n.Region, OrigRegion = n.Region, OrigText = n.Text };
            BeginEdit();
        }

        void BeginEdit()
        {
            Edit e = edit!;
            Trace();
            OverlayWindow o = Video.Overlay;
            o.Region.Show(e.Region, e.Kind);
            o.Region.SetDrawing(true);
            if (e.Create) o.Hint.Show(Copy.HintCreate, Copy.KeysCreate);
            else o.Hint.Show(Copy.HintEdit, Copy.KeysEdit);
            o.Composer.Open(e.Kind, e.OrigText);
            PlaceComposer();
            o.SetTyping(true);
            AutomationProperties.SetItemStatus(o.Composer, e.Create ? "create" : "edit");
        }

        void PlaceComposer()
        {
            if (edit == null) return;
            OverlayWindow o = Video.Overlay;
            Box frameBox = Video.FrameBox;
            double areaW = Video.Area.ActualWidth, areaH = Video.Area.ActualHeight;
            o.Hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            o.Composer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size hint = o.Hint.DesiredSize, box = o.Composer.DesiredSize;
            OverlayWindow.Put(o.Hint, Math.Max(8, frameBox.X + (frameBox.W - hint.Width) / 2), Math.Max(8, frameBox.Y + 10));
            RegionMath.PlaceEditor(edit.Region, frameBox, areaW, areaH, box.Width, box.Height, 0, out double left, out double topY);
            if (edit.Region == null) topY = Math.Max(8, frameBox.Bottom - box.Height - 16);
            OverlayWindow.Put(o.Composer, left, topY);
        }

        void OnRegionDrawn(Region r)
        {
            if (edit == null) return;
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

        /// <summary>Saves the note being written: the frame image first (when the region is new), then the core.</summary>
        async Task Commit()
        {
            if (edit == null || saving || player == null) return;
            saving = true;
            Edit e = edit;
            string text = Video.Overlay.Composer.Text;
            Box frameBox = Video.FrameBox;
            bool needImage = e.Region != null && (e.Create || e.RegionDirty);
            EndEditUi();
            Video.Overlay.Region.Show(e.Region, e.Kind);
            byte[]? png = null;
            string? imageError = null;
            try
            {
                if (needImage)
                {
                    try
                    {
                        if (playing) player.Pause();
                        if (player.Frame != e.Frame) player.SeekFrame(e.Frame);
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
                    SessionState st = session.AddNote(e.Frame, e.Kind, text, e.Region, png);
                    NoteState? added = st.Notes.OrderByDescending(n => n.Order).FirstOrDefault();
                    edit = null;
                    selectedId = added?.Id;
                    Apply(st);
                }
                else
                {
                    bool textChanged = text != e.OrigText;
                    edit = null;
                    if (textChanged || e.RegionDirty)
                    {
                        SessionState st = session.UpdateNote(e.Id, textChanged ? text : null,
                            e.RegionDirty ? e.Region : null, e.RegionDirty && e.Region == null, png);
                        Apply(st);
                    }
                }
                if (imageError != null) Banner.Show(string.Format(CultureInfo.InvariantCulture, Copy.ImageFailed, imageError));
            }
            catch (SessionException ex)
            {
                edit = null;
                Banner.Show(ex.Message);
                Apply(session.GetState());
            }
            finally
            {
                edit = null;
                saving = false;
                Select(selectedId, false);
                Trace();
            }
        }

        void RemoveSelected()
        {
            NoteState? n = Note(selectedId);
            if (n == null) return;
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
                Banner.Show(ex.Message);
                Apply(session.GetState());
            }
        }

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
            if (edit != null)
            {
                if (key == Key.Enter) { e.Handled = true; _ = Commit(); }
                else if (key == Key.Escape) { e.Handled = true; CancelEdit(); }
                else Video.Overlay.Composer.FocusField();
                return;
            }
            if ((mods & ModifierKeys.Control) != 0 && (mods & ModifierKeys.Alt) == 0)
            {
                if (key == Key.O) { e.Handled = true; OpenDialog(); }
                else if (key == Key.C && videoLoaded) { e.Handled = true; CopyNotes((mods & ModifierKeys.Shift) != 0); }
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
            switch (key)
            {
                case Key.Space: TogglePlay(); break;
                case Key.Left: player.Step(shift ? -10 : -1); break;
                case Key.Right: player.Step(shift ? 10 : 1); break;
                case Key.Home: player.SeekFrame(0); break;
                case Key.End: player.SeekFrame(lastFrame); break;
                case Key.Up: SelectNeighbour(-1); break;
                case Key.Down: SelectNeighbour(1); break;
                case Key.D1: case Key.NumPad1: if (!shift) { e.Handled = true; _ = StartNote("FIX"); } break;
                case Key.D2: case Key.NumPad2: if (!shift) { e.Handled = true; _ = StartNote("ADD"); } break;
                case Key.D3: case Key.NumPad3: if (!shift) { e.Handled = true; _ = StartNote("REMOVE"); } break;
                case Key.F2: e.Handled = true; if (selectedId != null) _ = EditNote(selectedId); else { NoteState? n = state.Notes.FirstOrDefault(x => x.Frame == frame); if (n != null) _ = EditNote(n.Id); } break;
                case Key.Delete: e.Handled = true; RemoveSelected(); break;
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

        // --- menus ----------------------------------------------------------------------------------------------

        void OpenMenu(PopMenu menu, FrameworkElement anchor)
        {
            if (edit != null) return;
            window.Root.Overlay.OpenMenu(menu, anchor);
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
            var menu = new PopMenu(240);
            menu.Add(Copy.CopyNotes, Icons.File, "Ctrl+C", null, () => CopyNotes(false));
            menu.Add(Copy.CopyReadable, Icons.Note, "Ctrl+Shift+C", Copy.CopyReadableTip, () => CopyNotes(true));
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

        // --- covering the video ---------------------------------------------------------------------------------

        /// <summary>
        /// A native window covers the main window's tree, so whatever of that tree has to show over the video (a
        /// sheet, a menu; a tooltip or hover card while paused) gets a still of the video under it instead of the
        /// live picture. Sheets and menus also hide the overlay window and pause playback.
        /// </summary>
        void UpdateCover()
        {
            if (Video.IsFake || !videoLoaded || mpv == null) return;
            OverlayHost overlay = window.Root.Overlay;
            bool modal = overlay.SheetOpen || overlay.MenuOpen;
            bool floating = !playing && FloatsOverVideo();
            Video.Overlay.Suspended = modal;
            bool cover = modal || floating;
            if (cover && !Video.Frozen && !freezing) _ = Freeze();
            else if (!cover && Video.Frozen) Video.Thaw();
        }

        async Task Freeze()
        {
            freezing = true;
            RawFrame? raw = null;
            var clock = Stopwatch.StartNew();
            try { raw = await mpv!.GrabWindow(); }
            catch (Exception ex) { Log.Warning("still of the video failed: " + ex.Message); }
            Log.Info("still: " + (raw == null ? "none" : raw.Width + "x" + raw.Height + " " + raw.Format + " stride " + raw.Stride) + " in " + clock.ElapsedMilliseconds + " ms");
            freezing = false;
            OverlayHost overlay = window.Root.Overlay;
            bool still = overlay.SheetOpen || overlay.MenuOpen || (!playing && FloatsOverVideo());
            if (still && videoLoaded) Video.Freeze(VideoView.Picture(raw));
        }

        bool FloatsOverVideo()
        {
            if (!Video.Area.IsVisible) return false;
            ShellRoot root = window.Root;
            Rect area;
            try { area = Video.Area.TransformToAncestor(root).TransformBounds(new Rect(Video.Area.RenderSize)); }
            catch (InvalidOperationException) { return false; }
            foreach (Panel layer in new Panel[] { root.TipLayer, root.Overlay.FloatLayer })
                foreach (UIElement child in layer.Children)
                {
                    if (!(child is FrameworkElement fe) || fe.Visibility != Visibility.Visible) continue;
                    try
                    {
                        Rect r = fe.TransformToAncestor(root).TransformBounds(new Rect(fe.RenderSize));
                        if (r.IntersectsWith(area) && r.Width > 0) return true;
                    }
                    catch (InvalidOperationException) { }
                }
            return false;
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
                if (edit != null) await Commit();
                if (window.WindowState == WindowState.Normal && WindowPlace.Savable(window.Left, window.Top))
                {
                    settings.WindowX = (int)Math.Round(window.Left);
                    settings.WindowY = (int)Math.Round(window.Top);
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
