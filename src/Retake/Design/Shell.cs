using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using Retake.Design.Controls;

namespace Retake.Design
{
    public enum PillStage { Idle, Available, Downloading, Ready }

    /// <summary>
    /// The version pill of the title bar: a 26 px capsule (--tint-clear, --lens-hairline rim, bevel,
    /// --pill-shadow) with the version in mono-s. A 6 px dot with a soft glow marks a newer version: accent while
    /// available or downloading (pulsing over 1.4 s), mint when ready; the text then takes --accent-ink. It is
    /// both the status and the way to act on it: it opens the update sheet while a newer version is known, else
    /// the updates popover.
    /// </summary>
    public sealed class VersionPill : Pressable
    {
        readonly SurfaceVisual face = new SurfaceVisual(-1, "lens-hairline", "btn-bevel", "tint-clear") { HoverFill = "fill-1" };
        readonly Painted dot;
        readonly DropShadowEffect glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 8, Opacity = 0.9 };
        readonly TextBlock text;
        PillStage stage;
        bool open;

        public VersionPill(string version)
        {
            Height = 26;
            AutomationProperties.SetName(this, version);
            dot = new Painted(6, 6, (dc, s) => dc.DrawEllipse(Themes.Brush(stage == PillStage.Ready ? "mint" : "accent"), null, new Point(3, 3), 3, 3))
            {
                Margin = new Thickness(0, 0, 5, 0),
                Visibility = Visibility.Collapsed,
                Effect = glow,
            };
            text = Typography.Text(version, Typography.MonoS, null);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center, Children = { dot, text } };
            Content = new Grid { Children = { new ShadowLayer("pill-shadow"), face, row } };
            Themes.Changed += () => SetStage(stage);
            SetStage(PillStage.Idle);
        }

        public PillStage Stage => stage;

        /// <summary>Lit while the popover or the sheet it opened is shown.</summary>
        public bool IsSource
        {
            get => open;
            set { open = value; Refresh(Motion.Enabled && IsLoaded); }
        }

        public void SetStage(PillStage next)
        {
            stage = next;
            bool newer = next != PillStage.Idle;
            dot.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
            glow.Color = Themes.Color(next == PillStage.Ready ? "mint" : "accent");
            dot.InvalidateVisual();
            if (next == PillStage.Downloading && Motion.Enabled)
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Motion.EaseInOut });
            else { dot.BeginAnimation(OpacityProperty, null); dot.Opacity = 1; }
            Refresh(false);
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            double lit = Math.Max(hover, open ? 1 : 0);
            GlassButton.To(face, SurfaceVisual.HoverProperty, lit, animate, Motion.D1);
            GlassButton.To(face, SurfaceVisual.RingProperty, focus, animate, Motion.D1);
            SetResourceReference(ForegroundProperty, stage != PillStage.Idle ? "T.accent-ink" : lit > 0 ? "T.ink-1" : "T.ink-3");
        }
    }

    /// <summary>
    /// The Retake mark on a 20 x 20 grid: a rounded square in --accent-grad, a play mark in --hero-ink and a note dot
    /// in --note-fix, glowing in --glyph-glow in the title bar. The one source of the app icon: build\make-icon.ps1
    /// reads the three paths and their tokens from here (Crystal Black values) to write build\icon.ico and
    /// docs\assets\icon.svg.
    /// </summary>
    sealed class AppGlyph : FrameworkElement, IThemed
    {
        public const string PlatePath = "M7 1.5h6a5.5 5.5 0 0 1 5.5 5.5v6a5.5 5.5 0 0 1-5.5 5.5H7A5.5 5.5 0 0 1 1.5 13V7A5.5 5.5 0 0 1 7 1.5Z";
        public const string MarkPath = "M7.3 6.9v7.4a.65.65 0 0 0 1 .55l5.75-3.7a.65.65 0 0 0 0-1.1L8.3 6.35a.65.65 0 0 0-1 .55Z";
        public const string DotPath = "M12.3 5.7a2.15 2.15 0 1 0 4.3 0a2.15 2.15 0 1 0 -4.3 0Z";
        public const string PlateToken = "accent-grad", MarkToken = "hero-ink", DotToken = "note-fix";

        static readonly Geometry Plate = Freeze(Geometry.Parse(PlatePath));
        static readonly Geometry Mark = Freeze(Geometry.Parse(MarkPath));
        static readonly Geometry Dot = Freeze(Geometry.Parse(DotPath));

        public AppGlyph()
        {
            Width = Height = 20;
            IsHitTestVisible = false;
            Themes.Track(this);
            OnThemeChanged();
        }

        static Geometry Freeze(Geometry g) { g.Freeze(); return g; }

        public void OnThemeChanged()
        {
            var e = new DropShadowEffect { Color = Themes.Color("glyph-glow"), Opacity = Themes.Color("glyph-glow").A / 255.0, BlurRadius = 8, ShadowDepth = 2, Direction = 270 };
            e.Freeze();
            Effect = e;
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawGeometry(Themes.Brush(PlateToken), null, Plate);
            dc.DrawGeometry(Themes.Brush(MarkToken), null, Mark);
            dc.DrawGeometry(Themes.Brush(DotToken), null, Dot);
        }
    }

    /// <summary>
    /// The standard title bar (LIQUID_UI.md 9.3) at Retake's size, 52 px: on the left the app glyph (24 px), the
    /// name in the title role and the version pill; a centre slot for the app's own controls, centred on the
    /// window; on the right an optional aside slot and the grouped capsule settings | minimize, maximize, close
    /// (38 px, --tint-clear, rim, --shadow-s). The empty bar drags the window (the window's caption area) and a double
    /// click on it maximizes or restores; the slots and buttons take their clicks back.
    /// </summary>
    public sealed class TitleBar : Grid
    {
        public const double BarHeight = 52;
        public readonly StackPanel Centre = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        public readonly StackPanel Aside = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        public readonly VersionPill Pill;
        public readonly IconButton Gear, MinimizeButton, MaximizeButton, CloseButton;

        public const string GearTip = "Settings (Ctrl+,)", MinimizeTip = "Minimize", MaximizeTip = "Maximize", RestoreTip = "Restore down",
            CloseTip = "Close (Alt+F4)", PillTip = "Version and updates";

        public TitleBar(string name, string version)
        {
            Height = BarHeight;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(12, 0, 10, 0);
            var row = new BarRow();
            Children.Add(row);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            left.Children.Add(new AppGlyph { LayoutTransform = new ScaleTransform(1.2, 1.2) });
            TextBlock title = Typography.Text(name, Typography.Title);
            title.Margin = new Thickness(9, 0, 0, 1);
            title.IsHitTestVisible = false;
            left.Children.Add(title);
            Pill = new VersionPill("v" + version) { Margin = new Thickness(11, 0, 0, 0) };
            Tip.Set(Pill, PillTip);
            WindowChrome.SetIsHitTestVisibleInChrome(Pill, true);
            left.Children.Add(Pill);
            row.Children.Add(left);

            WindowChrome.SetIsHitTestVisibleInChrome(Centre, true);
            row.Children.Add(Centre);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Aside.Margin = new Thickness(0, 0, 8, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(Aside, true);
            right.Children.Add(Aside);
            var cluster = new Grid { Height = 38 };
            cluster.Children.Add(new ShadowLayer("shadow-s"));
            cluster.Children.Add(new SurfaceVisual(-1, "lens-hairline", "btn-bevel", "tint-clear"));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
            Gear = new IconButton(Icons.Gear, "Settings", IconKind.Title) { TurnsOnHover = true };
            Tip.Set(Gear, GearTip);
            buttons.Children.Add(Gear);
            var sep = new Border { Width = 1, Height = 17, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            sep.SetResourceReference(Border.BackgroundProperty, "T.stroke-2");
            buttons.Children.Add(sep);
            MinimizeButton = new IconButton(Icons.Minimize, "Minimize", IconKind.Title) { Margin = new Thickness(0, 0, 2, 0) };
            Tip.Set(MinimizeButton, MinimizeTip);
            buttons.Children.Add(MinimizeButton);
            MaximizeButton = new IconButton(Icons.Maximize, "Maximize", IconKind.Title) { Margin = new Thickness(0, 0, 2, 0) };
            Tip.Set(MaximizeButton, MaximizeTip);
            buttons.Children.Add(MaximizeButton);
            CloseButton = new IconButton(Icons.Close, "Close", IconKind.TitleClose);
            Tip.Set(CloseButton, CloseTip);
            buttons.Children.Add(CloseButton);
            cluster.Children.Add(buttons);
            WindowChrome.SetIsHitTestVisibleInChrome(cluster, true);
            right.Children.Add(cluster);
            row.Children.Add(right);
        }

        /// <summary>Shows the aside slot when it holds something.</summary>
        public void RefreshAside() => Aside.Visibility = Aside.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>The maximize button follows the window: Maximize, or Restore down while maximized.</summary>
        public void SetMaximized(bool on)
        {
            var root = (Grid)MaximizeButton.Content;
            for (int i = root.Children.Count - 1; i >= 0; i--)
                if (root.Children[i] is Viewbox) root.Children.RemoveAt(i);
            FrameworkElement glyph = Icons.Make(on ? Icons.Restore : Icons.Maximize, 17);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(glyph);
            AutomationProperties.SetName(MaximizeButton, on ? "Restore down" : "Maximize");
            Tip.Set(MaximizeButton, on ? RestoreTip : MaximizeTip);
        }
    }

    /// <summary>
    /// The three parts of the title bar: the left group at the start, the right group at the end, the centre on the
    /// window's centre line. When the window is too narrow for that, the centre moves aside to stay clear of both
    /// groups and, with less room still, gets only the width between them (its text trims).
    /// </summary>
    sealed class BarRow : Panel
    {
        const double Clear = 12;

        protected override Size MeasureOverride(Size available)
        {
            UIElement left = Children[0], centre = Children[1], right = Children[2];
            var any = new Size(double.PositiveInfinity, available.Height);
            left.Measure(any);
            right.Measure(any);
            double room = double.IsInfinity(available.Width) ? double.PositiveInfinity
                : Math.Max(0, available.Width - left.DesiredSize.Width - right.DesiredSize.Width - 2 * Clear);
            centre.Measure(new Size(room, available.Height));
            double h = Math.Max(left.DesiredSize.Height, Math.Max(centre.DesiredSize.Height, right.DesiredSize.Height));
            return new Size(double.IsInfinity(available.Width) ? left.DesiredSize.Width + centre.DesiredSize.Width + right.DesiredSize.Width : available.Width, h);
        }

        protected override Size ArrangeOverride(Size size)
        {
            UIElement left = Children[0], centre = Children[1], right = Children[2];
            double lw = left.DesiredSize.Width, rw = right.DesiredSize.Width, cw = centre.DesiredSize.Width;
            left.Arrange(new Rect(0, 0, lw, size.Height));
            right.Arrange(new Rect(Math.Max(0, size.Width - rw), 0, rw, size.Height));
            double min = lw + Clear, max = size.Width - rw - Clear - cw;
            double x = (size.Width - cw) / 2;
            x = Math.Max(min, Math.Min(max, x));
            centre.Arrange(new Rect(x, 0, Math.Max(0, Math.Min(cw, size.Width - rw - Clear - x)), size.Height));
            return size;
        }
    }

    /// <summary>
    /// The root of a window, in layer order: scene, body (the content slot), overlay (veil, sheet, menus, hover
    /// cards), title bar, toasts, tooltips. Its LayoutTransform carries the Scale setting.
    /// </summary>
    public sealed class ShellRoot : Grid, IOverlayRoot
    {
        public readonly Scene Scene = new Scene();
        public readonly Grid Body = new Grid { Margin = new Thickness(0, TitleBar.BarHeight, 0, 0) };
        public readonly TitleBar TitleBar;
        public readonly ToastHost Toasts = new ToastHost();
        readonly OverlayHost overlay = new OverlayHost();
        readonly Canvas tips = new Canvas { IsHitTestVisible = false };

        public ShellRoot(string name, string version)
        {
            TitleBar = new TitleBar(name, version);
            Children.Add(Scene);
            Children.Add(Body);
            Children.Add(overlay);
            Children.Add(TitleBar);
            Children.Add(Toasts);
            Children.Add(tips);
        }

        public Canvas TipLayer => tips;
        public OverlayHost Overlay => overlay;

        readonly FloatWindow?[] floats = new FloatWindow?[2];

        /// <summary>The float window of a kind, created on first use; null outside a window (a capture).</summary>
        public FloatWindow? Float(FloatKind kind)
        {
            int i = (int)kind;
            if (floats[i] != null) return floats[i];
            if (!(Window.GetWindow(this) is ShellWindow owner) || !owner.IsLoaded) return null;
            floats[i] = new FloatWindow(owner, this);
            return floats[i];
        }

        /// <summary>Creates the float windows ahead of their first use.</summary>
        public void PrepareFloats()
        {
            Float(FloatKind.Tip)?.Prepare();
            Float(FloatKind.Card)?.Prepare();
        }
    }

    /// <summary>
    /// The application window: resizable from its edges, with minimize, maximize and close (the buttons, a double
    /// click on the bar, the system menu, Snap); custom chrome through WindowChrome with the 52 px title bar as the
    /// caption, no layered window (AllowsTransparency false keeps ClearType and allows a native child window), rounded
    /// corners and the --bg-mid border on Windows 11. The content is laid out at the window's size divided by the
    /// Scale step, so a larger window shows more, not larger, content. The smallest size is the last step of the
    /// ladder (960 x 600) times the Scale step; the first start opens at the largest step of the ladder that fits.
    /// A maximized window fills the work area of its monitor.
    /// </summary>
    public class ShellWindow : Window
    {
        public readonly ShellRoot Root;
        public readonly Prefs Prefs;
        public const double ResizeBorder = 6;
        readonly WindowChrome chrome = new WindowChrome { CaptionHeight = TitleBar.BarHeight, ResizeBorderThickness = new Thickness(ResizeBorder), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false };
        SettingsSheet? settings;
        IntPtr hwnd;

        public int AppliedScale { get; private set; } = Scale.Default;
        public event Action? ScaleApplied;

        /// <summary>The size the content is laid out at: the client size divided by the Scale step.</summary>
        public Size LogicalSize
        {
            get
            {
                double f = AppliedScale / 100.0;
                return Root.ActualWidth > 0 ? new Size(Root.ActualWidth, Root.ActualHeight) : new Size(Width / f, Height / f);
            }
        }

        /// <summary>Raised when the version pill is clicked; the updater decides what it opens.</summary>
        public event Action? PillClicked;

        /// <summary>The status row of the Updates group of Settings (status line, last check, action), set by the updater before Settings first opens.</summary>
        public FrameworkElement? UpdatesRow { get; set; }

        public ShellWindow(Prefs prefs, string title = "Retake")
        {
            Prefs = prefs;
            Title = title;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            SetResourceReference(BackgroundProperty, "T.bg-mid");
            WindowChrome.SetWindowChrome(this, chrome);
            Root = new ShellRoot("Retake", AppVersion.Version);
            Content = Root;
            TitleBar bar = Root.TitleBar;
            bar.Gear.Click += (s, e) => ToggleSettings();
            bar.MinimizeButton.Click += (s, e) => WindowState = WindowState.Minimized;
            bar.MaximizeButton.Click += (s, e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            StateChanged += (s, e) => bar.SetMaximized(WindowState == WindowState.Maximized);
            Loaded += (s, e) => bar.SetMaximized(WindowState == WindowState.Maximized);
            bar.CloseButton.Click += (s, e) => Close();
            bar.Pill.Click += (s, e) => PillClicked?.Invoke();
            Root.Overlay.SheetChanged += open => bar.Gear.IsSource = open && settings != null && Root.Overlay.CurrentSheet == settings.Sheet;
            PreviewKeyDown += OnKey;
            PreviewMouseDown += (s, e) => Root.Overlay.PressAnywhere(e.OriginalSource as DependencyObject);
            Themes.Changed += UpdateFrame;
            // A first guess from the primary work area, so the window is created at about its size;
            // the real size for the window's own monitor is set in SourceInitialized, before the first paint.
            Rect primary = SystemParameters.WorkArea;
            Place(prefs.UiScale, new Size(primary.Width, primary.Height), new Point(primary.Left + primary.Width / 2, primary.Top + primary.Height / 2), null);
            SourceInitialized += OnSourceInitialized;
            DpiChanged += (s, e) => ApplyScale(AppliedScale);
            // Settings are built while the window is idle after its first paint, so the first open is only the animation.
            ContentRendered += (s, e) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                settings ??= new SettingsSheet(this);
                // Laying it out once loads its faces and builds its text, the slow part of a first open.
                settings.Sheet.Measure(new Size(settings.Sheet.Width, LogicalSize.Height));
                Root.PrepareFloats();
            }));
        }

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.Dwm(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            UpdateFrame();
            Size work = WorkArea();
            Rect primary = SystemParameters.WorkArea;
            Place(Prefs.UiScale, work, new Point(primary.Left + primary.Width / 2, primary.Top + primary.Height / 2), null);
        }

        void UpdateFrame()
        {
            if (hwnd == IntPtr.Zero) return;
            Color mid = Themes.Color("bg-mid");
            Native.Dwm(hwnd, Native.DWMWA_BORDER_COLOR, mid.R | mid.G << 8 | mid.B << 16);
            Native.Dwm(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, Themes.Current.IsDark ? 1 : 0);
        }

        /// <summary>The work area of the window's monitor in device-independent pixels.</summary>
        public Size WorkArea()
        {
            Native.RECT? r = hwnd != IntPtr.Zero ? Native.WorkArea(hwnd) : null;
            if (r == null) return new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
            double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return new Size((r.Value.Right - r.Value.Left) / k, (r.Value.Bottom - r.Value.Top) / k);
        }

        public bool StepFits(int step) => Scale.Fits(step, WorkArea());

        /// <summary>
        /// Applies a Scale step now: the window keeps its place and size, grown to the smallest size of the step when it
        /// is below it; a step whose smallest size does not fit falls back to the largest that does.
        /// </summary>
        public void ApplyScale(int step)
        {
            if (WindowState != WindowState.Normal)
            {
                Apply(Scale.Fitting(Scale.Normalize(step), WorkArea()), null);
                return;
            }
            Place(step, WorkArea(), new Point(Left + Width / 2, Top + Height / 2), new Size(Width, Height));
        }

        /// <summary>
        /// Puts the window back where it was (device-independent pixels; a size below the step's smallest grows to it,
        /// one larger than the work area shrinks to it), maximized when it was. Called before the first paint.
        /// </summary>
        public void RestorePlacement(Rect bounds, bool maximized)
        {
            Size work = WorkArea();
            int fitting = Scale.Fitting(Scale.Normalize(AppliedScale), work);
            Size min = MinSize(fitting, work);
            double w = Math.Max(min.Width, Math.Min(work.Width, bounds.Width)), h = Math.Max(min.Height, Math.Min(work.Height, bounds.Height));
            Width = w;
            Height = h;
            Left = bounds.Left;
            Top = bounds.Top;
            if (maximized) WindowState = WindowState.Maximized;
        }

        /// <summary>
        /// Sizes the window for a step: <paramref name="size"/> when given (the size it has), else the largest size of
        /// the ladder that fits; centred on <paramref name="centre"/> and kept inside the work area.
        /// </summary>
        void Place(int step, Size work, Point centre, Size? size)
        {
            int fitting = Scale.Fitting(Scale.Normalize(step), work);
            double f = fitting / 100.0;
            Size min = MinSize(fitting, work);
            double w, h;
            if (size.HasValue)
            {
                w = Math.Max(min.Width, Math.Min(work.Width, size.Value.Width));
                h = Math.Max(min.Height, Math.Min(work.Height, size.Value.Height));
            }
            else
            {
                Size logical = Scale.LogicalSize(fitting, work) ?? Scale.Ladder[Scale.Ladder.Length - 1];
                w = Math.Round(logical.Width * f);
                h = Math.Round(logical.Height * f);
            }
            Rect area = hwnd != IntPtr.Zero ? AreaRect(work) : SystemParameters.WorkArea;
            Apply(fitting, new Rect(
                Math.Max(area.Left, Math.Min(area.Right - w, centre.X - w / 2)),
                Math.Max(area.Top, Math.Min(area.Bottom - h, centre.Y - h / 2)), w, h));
        }

        /// <summary>The smallest window of a step: the last size of the ladder scaled, never larger than the work area.</summary>
        static Size MinSize(int step, Size work)
        {
            Size last = Scale.Ladder[Scale.Ladder.Length - 1];
            double f = step / 100.0;
            return new Size(Math.Min(work.Width, Math.Round(last.Width * f)), Math.Min(work.Height, Math.Round(last.Height * f)));
        }

        void Apply(int step, Rect? bounds)
        {
            double f = step / 100.0;
            AppliedScale = step;
            Prefs.UiScale = step;
            Root.LayoutTransform = step == 100 ? Transform.Identity : new ScaleTransform(f, f);
            chrome.CaptionHeight = TitleBar.BarHeight * f;
            chrome.ResizeBorderThickness = new Thickness(ResizeBorder * f);
            Size min = MinSize(step, WorkArea());
            MinWidth = min.Width;
            MinHeight = min.Height;
            if (bounds.HasValue)
            {
                Width = bounds.Value.Width;
                Height = bounds.Value.Height;
                Left = bounds.Value.Left;
                Top = bounds.Value.Top;
            }
            UpdateLayout();
            if (Root.Overlay.CurrentSheet is Sheet s) s.MaxHeight = Math.Max(120, LogicalSize.Height - OverlayHost.TopInset - OverlayHost.BottomInset);
            ScaleApplied?.Invoke();
        }

        Rect AreaRect(Size work)
        {
            Native.RECT? r = Native.WorkArea(hwnd);
            if (r == null) return SystemParameters.WorkArea;
            double k = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return new Rect(r.Value.Left / k, r.Value.Top / k, work.Width, work.Height);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && Root.Overlay.Escape()) { e.Handled = true; return; }
            if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control) { ToggleSettings(); e.Handled = true; }
        }

        /// <summary>Opens the Settings sheet out of the gear, or closes it.</summary>
        public void ToggleSettings()
        {
            if (Root.Overlay.SheetOpen) { Root.Overlay.CloseSheet(); return; }
            settings ??= new SettingsSheet(this);
            settings.Refresh();
            Root.Overlay.OpenSheet(settings.Sheet, Root.TitleBar.Gear);
        }
    }
}
