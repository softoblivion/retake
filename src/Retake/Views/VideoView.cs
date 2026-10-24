using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Retake.Design;

namespace Retake.Views
{
    /// <summary>The native child window the player renders into (its <c>wid</c>): black, no input of its own.</summary>
    public sealed class VideoHost : HwndHost
    {
        public IntPtr Child { get; private set; }
        public event Action<IntPtr>? Created;

        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            Win32.RegisterVideoClass();
            Child = Win32.CreateWindowExW(0, Win32.VideoClass, "", Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPSIBLINGS | Win32.WS_CLIPCHILDREN,
                0, 0, 16, 16, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Created?.Invoke(Child);
            return new HandleRef(this, Child);
        }

        protected override void DestroyWindowCore(HandleRef hwnd) => Win32.DestroyWindow(hwnd.Handle);
    }

    /// <summary>
    /// The transparent window over the video rectangle (approach A of the engine spike): a native child window
    /// covers everything of the main window's visual tree, so the region tool, the note editor and the hint are
    /// drawn here. Owned by the main window (no taskbar entry, above it, hidden with it), placed on the video
    /// area in device pixels and scaled by the Scale step, so its logical layout is the area's. It never takes
    /// activation, except while the note field is typed into.
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        readonly ShellWindow owner;
        readonly FrameworkElement target;
        readonly Grid content = new Grid();
        readonly Canvas floating = new Canvas();
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        public readonly RegionTool Region = new RegionTool();
        public readonly Composer Composer = new Composer();
        public readonly HintPill Hint = new HintPill();
        IntPtr hwnd;
        bool wanted, hidden, typing, gone;

        /// <summary>Keys that reach the overlay outside the note field (the window's own key handler takes them).</summary>
        public event Action<KeyEventArgs>? KeyForward;

        public OverlayWindow(ShellWindow owner, FrameworkElement target)
        {
            this.owner = owner;
            this.target = target;
            Title = "Retake overlay";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            // An owner can be set only once it has been shown.
            if (owner.IsLoaded) Owner = owner;
            else owner.Loaded += (s, e) => { Owner = owner; Place(); };
            UseLayoutRounding = true;
            AutomationProperties.SetAutomationId(this, "Overlay");
            content.LayoutTransform = scale;
            content.HorizontalAlignment = HorizontalAlignment.Left;
            content.VerticalAlignment = VerticalAlignment.Top;
            content.Children.Add(Region);
            content.Children.Add(floating);
            floating.Children.Add(Hint);
            floating.Children.Add(Composer);
            Content = content;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                Win32.SetExStyle(hwnd, Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW, true);
            };
            PreviewKeyDown += (s, e) => { if (!Composer.Field.IsKeyboardFocused) KeyForward?.Invoke(e); };
            Closed += (s, e) => gone = true;
            // Only the main window ends the overlay (Alt+F4 or a close request aimed at it is refused).
            Closing += (s, e) => { if (!Releasing) e.Cancel = true; };
            owner.LocationChanged += (s, e) => Place();
            owner.StateChanged += (s, e) => Place();
            owner.DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Place));
            owner.ScaleApplied += () => Dispatcher.BeginInvoke(new Action(Place));
            target.LayoutUpdated += (s, e) => Place();
            target.IsVisibleChanged += (s, e) => Place();
        }

        /// <summary>Shows the overlay whenever the video area is on screen (a video is open).</summary>
        public bool Wanted { get => wanted; set { wanted = value; Place(); } }

        /// <summary>Hides the overlay for a while (a sheet or a menu covers the video) without forgetting <see cref="Wanted"/>.</summary>
        public bool Suspended { get => hidden; set { if (hidden == value) return; hidden = value; Place(); } }

        public bool Typing => typing;

        /// <summary>Set when the main window closes, so the overlay may close with it.</summary>
        public bool Releasing;

        /// <summary>Puts the overlay on the video area (device pixels) or hides it.</summary>
        public void Place()
        {
            if (gone) return;
            bool show = Owner != null && wanted && !hidden && owner.WindowState != WindowState.Minimized && owner.IsVisible && target.IsVisible
                && PresentationSource.FromVisual(target) != null && target.ActualWidth > 0;
            if (!show)
            {
                if (IsVisible) Hide();
                return;
            }
            Point tl = target.PointToScreen(new Point(0, 0));
            Point br = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
            Matrix m = PresentationSource.FromVisual(owner)!.CompositionTarget.TransformToDevice;
            double l = tl.X / m.M11, t = tl.Y / m.M22, w = (br.X - tl.X) / m.M11, h = (br.Y - tl.Y) / m.M22;
            double s = owner.AppliedScale / 100.0;
            if (scale.ScaleX != s) scale.ScaleX = scale.ScaleY = s;
            if (content.Width != target.ActualWidth) content.Width = target.ActualWidth;
            if (content.Height != target.ActualHeight) content.Height = target.ActualHeight;
            if (Left != l) Left = l;
            if (Top != t) Top = t;
            if (Width != w) Width = w;
            if (Height != h) Height = h;
            if (!IsVisible) Show();
        }

        /// <summary>Lets the note field take the keyboard (the overlay becomes the active window) or gives it back to the main window.</summary>
        public void SetTyping(bool on)
        {
            typing = on;
            if (hwnd != IntPtr.Zero) Win32.SetExStyle(hwnd, Win32.WS_EX_NOACTIVATE, !on);
            if (on)
            {
                if (!IsVisible) Place();
                Activate();
                Composer.FocusField();
            }
            else if (IsActive || owner.IsVisible) owner.Activate();
        }

        /// <summary>Positions an element of the overlay's floating layer (logical pixels of the video area).</summary>
        public static void Put(UIElement e, double left, double top)
        {
            Canvas.SetLeft(e, Math.Round(left));
            Canvas.SetTop(e, Math.Round(top));
        }
    }

    /// <summary>
    /// The video area: a black rectangle holding the player's child window (or, with the fake player, an image),
    /// the overlay window on top of it, and a still picture that stands in for the video while something of the
    /// main window (a sheet, a menu, a tooltip or hover card) has to be drawn over it. The frame box is the video
    /// letterboxed into the area (contain, centred), as the player draws it.
    /// </summary>
    public sealed class VideoView : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        public readonly Border Area = new Border { ClipToBounds = true };
        readonly Grid layers = new Grid();
        readonly VideoHost? host;
        readonly Image? fakeImage;
        readonly Image still = new Image { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        public readonly OverlayWindow Overlay;
        int videoW, videoH;
        bool active, frozen;

        /// <summary>The child window exists (the real player can be created with it).</summary>
        public event Action<IntPtr>? HandleReady;
        /// <summary>The frame box changed (size of the area or of the video).</summary>
        public event Action? FrameChanged;

        public VideoView(ShellWindow owner, bool fake)
        {
            Area.SetResourceReference(Border.BackgroundProperty, "T.video-bg");
            AutomationProperties.SetAutomationId(this, "Video");
            AutomationProperties.SetName(this, "Video");
            if (fake)
            {
                fakeImage = new Image { Stretch = Stretch.Uniform };
                layers.Children.Add(fakeImage);
            }
            else
            {
                host = new VideoHost { Visibility = Visibility.Hidden };
                host.Created += h => Dispatcher.BeginInvoke(new Action(() => HandleReady?.Invoke(h)));
                layers.Children.Add(host);
            }
            layers.Children.Add(still);
            Area.Child = layers;
            Children.Add(Area);
            Overlay = new OverlayWindow(owner, Area);
            Area.SizeChanged += (s, e) => UpdateFrame();
        }

        public bool IsFake => fakeImage != null;
        public IntPtr Handle => host?.Child ?? IntPtr.Zero;
        public bool Frozen => frozen;
        public bool Active => active;

        /// <summary>The frame box in logical pixels of the area.</summary>
        public Box FrameBox => RegionMath.Fit(Area.ActualWidth, Area.ActualHeight, videoW, videoH);

        public void SetFakeSource(ImageSource source) { if (fakeImage != null) fakeImage.Source = source; }

        public void SetVideoSize(int w, int h)
        {
            videoW = w;
            videoH = h;
            UpdateFrame();
        }

        void UpdateFrame()
        {
            Overlay.Region.Frame = FrameBox;
            AutomationProperties.SetItemStatus(this, FrameBox.ToString());
            FrameChanged?.Invoke();
        }

        /// <summary>Shows the video (a file is loaded) or the bare black area.</summary>
        public void SetActive(bool on)
        {
            active = on;
            if (host != null && !frozen) host.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            if (fakeImage != null) fakeImage.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            Overlay.Wanted = on;
        }

        /// <summary>
        /// Replaces the live video by a still of it until <see cref="Thaw"/>: the still is drawn first, the child
        /// window is hidden on the next frame, so the picture does not blink.
        /// </summary>
        public void Freeze(BitmapSource? picture)
        {
            if (host == null || frozen) return;
            frozen = true;
            still.Source = picture;
            still.Visibility = Visibility.Visible;
            EventHandler? once = null;
            once = (s, e) =>
            {
                CompositionTarget.Rendering -= once;
                if (frozen) { host.Visibility = Visibility.Hidden; ReclaimFocus(); }
            };
            CompositionTarget.Rendering += once;
        }

        public void Thaw()
        {
            if (host == null || !frozen) return;
            frozen = false;
            host.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (frozen) return;
                still.Visibility = Visibility.Collapsed;
                still.Source = null;
            }));
        }

        /// <summary>
        /// Keeps the keyboard with the window: the player's own window inside the child can hold the Win32 focus
        /// (after a click on the video), and a hidden window with the focus would swallow every key.
        /// </summary>
        public void ReclaimFocus()
        {
            IntPtr child = Handle, focus = Win32.GetFocus();
            if (child == IntPtr.Zero || focus == IntPtr.Zero) return;
            if (focus != child && !Win32.IsChild(child, focus)) return;
            if (PresentationSource.FromVisual(this) is HwndSource source) Win32.SetFocus(source.Handle);
        }

        /// <summary>A raw frame from the player as a picture for <see cref="Freeze"/>.</summary>
        public static BitmapSource? Picture(Retake.Player.RawFrame? raw)
        {
            if (raw == null || raw.Width <= 0 || raw.Height <= 0 || raw.Data.Length < (long)raw.Stride * raw.Height) return null;
            BitmapSource b = BitmapSource.Create(raw.Width, raw.Height, 96, 96, PixelFormats.Bgr32, null, raw.Data, raw.Stride);
            b.Freeze();
            return b;
        }
    }
}
