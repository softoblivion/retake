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
using Retake.Design.Controls;

namespace Retake.Design
{
    public enum PillStage { Idle, Available, Downloading, Ready }

    /// <summary>
    /// The version pill of the title bar: a 24 px capsule (--tint-clear, --lens-hairline rim, bevel,
    /// --pill-shadow) with the version in mono-s. A 6 px dot with a soft glow marks a newer version: accent while
    /// available or downloading (pulsing over 1.4 s), mint when ready; the text then takes --accent-ink. It is
    /// both the status and the way to act on it (it opens the update sheet once the updater exists).
    /// </summary>
    public sealed class VersionPill : Pressable
    {
        readonly SurfaceVisual face = new SurfaceVisual(-1, "lens-hairline", "btn-bevel", "tint-clear") { HoverFill = "fill-1" };
        readonly Painted dot;
        readonly DropShadowEffect glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 8, Opacity = 0.9 };
        readonly TextBlock text;
        PillStage stage;

        public VersionPill(string version)
        {
            Height = 24;
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
            GlassButton.To(face, SurfaceVisual.HoverProperty, hover, animate, Motion.D1);
            GlassButton.To(face, SurfaceVisual.RingProperty, focus, animate, Motion.D1);
            SetResourceReference(ForegroundProperty, stage != PillStage.Idle ? "T.accent-ink" : hover > 0 ? "T.ink-1" : "T.ink-3");
        }
    }

    /// <summary>The placeholder app glyph: a rounded square in --accent-grad with a play mark, glowing in --glyph-glow.</summary>
    sealed class AppGlyph : FrameworkElement, IThemed
    {
        static readonly Geometry Mark = Freeze(Geometry.Parse("M8.2 6.4v7.2a.6.6 0 0 0 .9.5l5.6-3.6a.6.6 0 0 0 0-1L9.1 5.9a.6.6 0 0 0-.9.5Z"));

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
            dc.DrawRoundedRectangle(Themes.Brush("accent-grad"), null, new Rect(1.5, 1.5, 17, 17), 5.5, 5.5);
            dc.DrawGeometry(Themes.Brush("hero-ink"), null, Mark);
        }
    }

    /// <summary>
    /// The standard title bar (LIQUID_UI.md 9.3), 44 px: on the left the app glyph, the name and the version pill;
    /// a centre slot for the app's own controls, centred on the window; on the right an optional aside slot and
    /// the grouped capsule settings | minimize, close (34 px, --tint-clear, rim, --shadow-s). The empty bar drags
    /// the window (the window's caption area); the slots and buttons take their clicks back.
    /// </summary>
    public sealed class TitleBar : Grid
    {
        public const double Height44 = 44;
        public readonly StackPanel Centre = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        public readonly StackPanel Aside = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        public readonly VersionPill Pill;
        public readonly IconButton Gear, MinimizeButton, CloseButton;

        public const string GearTip = "Settings (Ctrl+,)", MinimizeTip = "Minimize", CloseTip = "Close (Alt+F4)", PillTip = "Version and updates";

        public TitleBar(string name, string version)
        {
            Height = Height44;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(10, 0, 10, 0);
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            left.Children.Add(new AppGlyph());
            TextBlock title = Typography.Text(name, Typography.Label);
            title.Margin = new Thickness(8, 0, 0, 0);
            title.IsHitTestVisible = false;
            left.Children.Add(title);
            Pill = new VersionPill("v" + version) { Margin = new Thickness(10, 0, 0, 0) };
            Tip.Set(Pill, PillTip);
            WindowChrome.SetIsHitTestVisibleInChrome(Pill, true);
            left.Children.Add(Pill);
            Children.Add(left);

            SetColumn(Centre, 1);
            WindowChrome.SetIsHitTestVisibleInChrome(Centre, true);
            Children.Add(Centre);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Aside.Margin = new Thickness(0, 0, 8, 0);
            WindowChrome.SetIsHitTestVisibleInChrome(Aside, true);
            right.Children.Add(Aside);
            var cluster = new Grid { Height = 34 };
            cluster.Children.Add(new ShadowLayer("shadow-s"));
            cluster.Children.Add(new SurfaceVisual(-1, "lens-hairline", "btn-bevel", "tint-clear"));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
            Gear = new IconButton(Icons.Gear, "Settings", IconKind.Title) { TurnsOnHover = true };
            Tip.Set(Gear, GearTip);
            buttons.Children.Add(Gear);
            var sep = new Border { Width = 1, Height = 15, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            sep.SetResourceReference(Border.BackgroundProperty, "T.stroke-2");
            buttons.Children.Add(sep);
            MinimizeButton = new IconButton(Icons.Minimize, "Minimize", IconKind.Title) { Margin = new Thickness(0, 0, 2, 0) };
            Tip.Set(MinimizeButton, MinimizeTip);
            buttons.Children.Add(MinimizeButton);
            CloseButton = new IconButton(Icons.Close, "Close", IconKind.TitleClose);
            Tip.Set(CloseButton, CloseTip);
            buttons.Children.Add(CloseButton);
            cluster.Children.Add(buttons);
            WindowChrome.SetIsHitTestVisibleInChrome(cluster, true);
            right.Children.Add(cluster);
            SetColumn(right, 2);
            Children.Add(right);
        }

        /// <summary>Shows the aside slot when it holds something.</summary>
        public void RefreshAside() => Aside.Visibility = Aside.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The root of a window, in layer order: scene, body (the content slot), overlay (veil, sheet, menus, hover
    /// cards), title bar, toasts, tooltips. Its LayoutTransform carries the Scale setting.
    /// </summary>
    public sealed class ShellRoot : Grid, IOverlayRoot
    {
        public readonly Scene Scene = new Scene();
        public readonly Grid Body = new Grid { Margin = new Thickness(0, TitleBar.Height44, 0, 0) };
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
    }

    /// <summary>
    /// The application window: fixed size (minimize and close only; no maximize by button, double click, system
    /// menu or keyboard), custom chrome through WindowChrome with the 44 px title bar as the caption, no layered
    /// window (AllowsTransparency false keeps ClearType and allows a native child window), rounded corners and
    /// the --bg-mid border on Windows 11. The Scale step is applied before the first paint and again on every
    /// change, keeping the window's centre.
    /// </summary>
    public class ShellWindow : Window
    {
        public readonly ShellRoot Root;
        public readonly Prefs Prefs;
        readonly WindowChrome chrome = new WindowChrome { CaptionHeight = TitleBar.Height44, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false };
        SettingsSheet? settings;
        IntPtr hwnd;

        public int AppliedScale { get; private set; } = Scale.Default;
        public Size LogicalSize { get; private set; } = Scale.Ladder[0];
        public event Action? ScaleApplied;

        public ShellWindow(Prefs prefs, string title = "Retake")
        {
            Prefs = prefs;
            Title = title;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanMinimize;
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
            bar.CloseButton.Click += (s, e) => Close();
            bar.Pill.Click += (s, e) => Root.Toasts.Show(ToastLevel.Info, "Updates are not set up in this build");
            Root.Overlay.SheetChanged += open => bar.Gear.IsSource = open;
            PreviewKeyDown += OnKey;
            PreviewMouseDown += (s, e) => Root.Overlay.PressAnywhere(e.OriginalSource as DependencyObject);
            Themes.Changed += UpdateFrame;
            // A first guess from the primary work area, so the window is created at about its size;
            // the real size for the window's own monitor is set in SourceInitialized, before the first paint.
            Rect primary = SystemParameters.WorkArea;
            Place(prefs.UiScale, new Size(primary.Width, primary.Height), new Point(primary.Left + primary.Width / 2, primary.Top + primary.Height / 2));
            SourceInitialized += OnSourceInitialized;
            DpiChanged += (s, e) => ApplyScale(AppliedScale);
        }

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            int style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
            Native.SetWindowLong(hwnd, Native.GWL_STYLE, style & ~Native.WS_MAXIMIZEBOX & ~Native.WS_THICKFRAME);
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
            Native.Dwm(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
            UpdateFrame();
            Size work = WorkArea();
            Rect primary = SystemParameters.WorkArea;
            Place(Prefs.UiScale, work, new Point(primary.Left + primary.Width / 2, primary.Top + primary.Height / 2));
        }

        static IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_NCLBUTTONDBLCLK) { handled = true; return IntPtr.Zero; }
            if (msg == Native.WM_SYSCOMMAND)
            {
                int cmd = (int)((long)w & 0xFFF0);
                if (cmd == Native.SC_MAXIMIZE || cmd == Native.SC_SIZE) { handled = true; return IntPtr.Zero; }
            }
            return IntPtr.Zero;
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

        /// <summary>Applies a Scale step now, keeping the window's centre; a step that does not fit falls back to the largest that does.</summary>
        public void ApplyScale(int step)
        {
            Place(step, WorkArea(), new Point(Left + Width / 2, Top + Height / 2));
        }

        void Place(int step, Size work, Point centre)
        {
            int fitting = Scale.Fitting(Scale.Normalize(step), work);
            Size logical = Scale.LogicalSize(fitting, work) ?? Scale.Ladder[Scale.Ladder.Length - 1];
            double f = fitting / 100.0;
            AppliedScale = fitting;
            LogicalSize = logical;
            Prefs.UiScale = fitting;
            Root.Width = logical.Width;
            Root.Height = logical.Height;
            Root.LayoutTransform = fitting == 100 ? Transform.Identity : new ScaleTransform(f, f);
            chrome.CaptionHeight = TitleBar.Height44 * f;
            double w = Math.Round(logical.Width * f), h = Math.Round(logical.Height * f);
            // Drop the old limits first: a smaller size would otherwise be coerced up to the previous minimum.
            MinWidth = MinHeight = 0;
            MaxWidth = MaxHeight = double.PositiveInfinity;
            Width = w;
            Height = h;
            MinWidth = MaxWidth = w;
            MinHeight = MaxHeight = h;
            Rect area = hwnd != IntPtr.Zero ? AreaRect(work) : SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(area.Right - w, centre.X - w / 2));
            Top = Math.Max(area.Top, Math.Min(area.Bottom - h, centre.Y - h / 2));
            if (Root.Overlay.CurrentSheet is Sheet s) s.MaxHeight = Math.Max(120, logical.Height - OverlayHost.TopInset - OverlayHost.BottomInset);
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
