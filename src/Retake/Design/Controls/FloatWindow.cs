using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Retake.Design.Controls
{
    public enum FloatKind { Tip, Card }

    /// <summary>
    /// A transparent window owned by the main window that shows one element at a time (a tooltip, a hover card)
    /// above everything of the main window, the native video child window included. It never takes the pointer
    /// or the activation (click-through, no-activate, tool window), it is placed in the main window's logical
    /// coordinates and scaled by its Scale step, and it leaves room around the element for the shadow. The video
    /// therefore keeps playing or stays as it is under a tooltip instead of being swapped for a still.
    /// </summary>
    public sealed class FloatWindow : Window
    {
        const double Pad = 24;
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

        readonly Window owner;
        readonly FrameworkElement root;
        readonly Grid frame = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        readonly Border slot = new Border { Margin = new Thickness(Pad) };
        IntPtr hwnd;
        bool gone;

        /// <param name="owner">The main window.</param>
        /// <param name="root">The scaled root of the main window: positions are given in its logical coordinates.</param>
        public FloatWindow(Window owner, FrameworkElement root)
        {
            this.owner = owner;
            this.root = root;
            Title = "Retake float";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Focusable = false;
            IsHitTestVisible = false;
            UseLayoutRounding = true;
            frame.Children.Add(slot);
            Content = frame;
            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
            };
            Closed += (s, e) => gone = true;
            owner.Closed += (s, e) => { if (!gone) Close(); };
        }

        /// <summary>Creates the native window ahead of the first use, so the first tooltip shows without a delay.</summary>
        public void Prepare()
        {
            if (gone) return;
            if (Owner == null && owner.IsLoaded) Owner = owner;
            new WindowInteropHelper(this).EnsureHandle();
        }

        /// <summary>The element shown now, or null.</summary>
        public FrameworkElement? Shown => IsVisible ? slot.Child as FrameworkElement : null;

        /// <summary>
        /// Shows an element with its top-left corner at a point of a root's logical layout: the main window's root,
        /// or <paramref name="coordinates"/> (the content of the video overlay window, scaled the same way).
        /// </summary>
        public void ShowAt(FrameworkElement content, Point topLeft, FrameworkElement? coordinates = null)
        {
            FrameworkElement root = coordinates ?? this.root;
            if (gone || !owner.IsVisible || owner.WindowState == WindowState.Minimized || PresentationSource.FromVisual(root) == null) return;
            if (Owner == null) Owner = owner;
            slot.Child = content;
            double f = root.LayoutTransform is ScaleTransform st ? st.ScaleX : 1;
            frame.LayoutTransform = f == 1 ? Transform.Identity : new ScaleTransform(f, f);
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = content.DesiredSize;
            Point tl = root.PointToScreen(new Point(topLeft.X - Pad, topLeft.Y - Pad));
            Point br = root.PointToScreen(new Point(topLeft.X + size.Width + Pad, topLeft.Y + size.Height + Pad));
            Matrix m = PresentationSource.FromVisual(owner)!.CompositionTarget.TransformToDevice;
            Left = tl.X / m.M11;
            Top = tl.Y / m.M22;
            Width = (br.X - tl.X) / m.M11;
            Height = (br.Y - tl.Y) / m.M22;
            if (!IsVisible) Show();
            // Above the other owned windows (the video overlay), without taking the activation.
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>Hides the element (when it is still the one shown).</summary>
        public void HideIf(FrameworkElement? content)
        {
            if (gone || (content != null && slot.Child != content)) return;
            if (IsVisible) Hide();
            slot.Child = null;
        }
    }

    /// <summary>Where a floating element goes relative to its anchor, in the root's logical coordinates.</summary>
    public static class FloatPlacement
    {
        /// <summary>A tooltip: centred below the anchor, above it when there is no room, kept 6 px inside the root.</summary>
        public static Point Below(Rect anchor, Size size, Size root, out bool flipped)
        {
            double x = Math.Max(6, Math.Min(root.Width - size.Width - 6, anchor.Left + anchor.Width / 2 - size.Width / 2));
            double y = anchor.Bottom + 6;
            flipped = y + size.Height > root.Height - 6;
            if (flipped) y = anchor.Top - 6 - size.Height;
            return new Point(Math.Round(x), Math.Round(y));
        }

        /// <summary>A hover card: centred above the anchor, below it when the title bar is in the way.</summary>
        public static Point Above(Rect anchor, Size size, Size root, double top)
        {
            double x = Math.Max(8, Math.Min(root.Width - size.Width - 8, anchor.Left + anchor.Width / 2 - size.Width / 2));
            double y = anchor.Top - 8 - size.Height;
            if (y < top) y = anchor.Bottom + 8;
            return new Point(Math.Round(x), Math.Round(y));
        }
    }
}
