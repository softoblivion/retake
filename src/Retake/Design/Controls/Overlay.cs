using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Retake.Design.Controls
{
    /// <summary>A floating panel of the overlay (a menu, a popover): it appears, closes itself on a pick and disappears.</summary>
    public interface IPopup
    {
        Action? Picked { set; }
        void Appear(bool up);
        void Disappear(Action done);
    }

    /// <summary>
    /// The overlay layer of a window, between the content and the title bar: the dim veil (--scrim), the modal
    /// sheet and the floating layer for menus and popovers. Esc closes the top overlay; a click on the veil
    /// closes the sheet; a click outside a menu closes the menu. With nothing open the layer lets every click
    /// through. The sheet's top edge sits 6 px below the title bar (<see cref="TopInset"/>).
    /// <para>
    /// A window whose content holds a native child window (the video) sets <see cref="Prepare"/>: a sheet or a
    /// menu then opens only once the host has put a still under it, and <see cref="Covering"/> stays true until
    /// the last close animation has ended, so the native window comes back after the overlay is gone.
    /// </para>
    /// </summary>
    public sealed class OverlayHost : Grid
    {
        public const double TopInset = TitleBar.BarHeight + 6, BottomInset = 14;
        readonly Rectangle veil = new Rectangle { Opacity = 0, Visibility = Visibility.Collapsed };
        readonly Grid sheetLayer = new Grid { Margin = new Thickness(0, TopInset, 0, BottomInset) };
        readonly Canvas floatLayer = new Canvas();
        Sheet? sheet;
        FrameworkElement? sheetOrigin;
        FrameworkElement? menu;
        int serial, closing;
        bool covering;

        public event Action<bool>? SheetChanged;
        /// <summary>Raised when a menu or a popover opens (true) or closes (false).</summary>
        public event Action<bool>? MenuChanged;
        /// <summary>Raised when <see cref="Covering"/> changes.</summary>
        public event Action<bool>? CoverChanged;

        /// <summary>
        /// Called before a sheet or a menu opens while nothing covers the content yet. It returns true when it takes
        /// over: it prepares what has to lie under the overlay and then runs the action that opens it; false opens
        /// at once.
        /// </summary>
        public Func<Action, bool>? Prepare;

        public OverlayHost()
        {
            veil.SetResourceReference(Shape.FillProperty, "T.scrim");
            veil.MouseLeftButtonDown += (s, e) => { CloseSheet(); e.Handled = true; };
            Children.Add(veil);
            Children.Add(sheetLayer);
            Children.Add(floatLayer);
        }

        public bool SheetOpen => sheet != null;
        public Sheet? CurrentSheet => sheet;
        public Canvas FloatLayer => floatLayer;
        public bool MenuOpen => menu != null;
        public FrameworkElement? CurrentMenu => menu;

        /// <summary>True from the moment a sheet or a menu opens until the last one has finished closing.</summary>
        public bool Covering => covering;

        void UpdateCovering()
        {
            bool now = sheet != null || menu != null || closing > 0;
            if (now == covering) return;
            covering = now;
            CoverChanged?.Invoke(now);
        }

        /// <summary>Runs an opening now, or after <see cref="Prepare"/> has made the content ready; a later request wins.</summary>
        void Begin(Action open)
        {
            int mine = ++serial;
            if (!covering && Prepare != null && Prepare(() => { if (mine == serial) open(); })) return;
            open();
        }

        public void OpenSheet(Sheet next, FrameworkElement? origin) => Begin(() => OpenSheetNow(next, origin));

        void OpenSheetNow(Sheet next, FrameworkElement? origin)
        {
            if (sheet != null) RemoveSheet(sheet);
            CloseMenu();
            sheet = next;
            sheetOrigin = origin;
            next.CloseRequested = () => CloseSheet();
            next.MaxHeight = Math.Max(120, ActualHeight - TopInset - BottomInset);
            sheetLayer.Children.Add(next);
            veil.Visibility = Visibility.Visible;
            Motion.To(veil, OpacityProperty, 1, Motion.D3);
            UpdateCovering();
            UpdateLayout();
            next.Open(OriginRect(next, origin));
            next.FocusFirst();
            SheetChanged?.Invoke(true);
        }

        public void CloseSheet()
        {
            Sheet? s = sheet;
            if (s == null) return;
            sheet = null;
            closing++;
            Motion.To(veil, OpacityProperty, 0, Motion.D2, null, () => { if (sheet == null) veil.Visibility = Visibility.Collapsed; });
            s.Close(OriginRect(s, sheetOrigin), () => { RemoveSheet(s); closing--; UpdateCovering(); });
            sheetOrigin?.Focus();
            SheetChanged?.Invoke(false);
        }

        void RemoveSheet(Sheet s) => sheetLayer.Children.Remove(s);

        Rect? OriginRect(Sheet s, FrameworkElement? origin)
        {
            if (origin == null || !origin.IsVisible || PresentationSource.FromVisual(origin) == null && PresentationSource.FromVisual(s) != null) return null;
            try { return origin.TransformToVisual(s).TransformBounds(new Rect(origin.RenderSize)); }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>Opens a menu or popover under (or above, when there is no room) the anchor, aligned to its left edge.</summary>
        public void OpenMenu(FrameworkElement next, FrameworkElement anchor) => Begin(() =>
        {
            if (!anchor.IsVisible) return;
            Rect at = anchor.TransformToVisual(floatLayer).TransformBounds(new Rect(anchor.RenderSize));
            Place(next, at.Left, at.Bottom + 6, at.Top - 6);
        });

        /// <summary>Opens a context menu at a point of an element (the pointer): below and right of it, above it when there is no room.</summary>
        public void OpenMenuAt(FrameworkElement next, FrameworkElement relativeTo, Point at) => Begin(() =>
        {
            if (!relativeTo.IsVisible) return;
            Point p = relativeTo.TransformToVisual(floatLayer).Transform(at);
            Place(next, p.X + 2, p.Y + 2, p.Y - 2);
        });

        void Place(FrameworkElement next, double left, double below, double above)
        {
            CloseMenu();
            menu = next;
            var popup = (IPopup)next;
            popup.Picked = () => { if (menu == next) CloseMenu(); };
            floatLayer.Children.Add(next);
            next.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = next.DesiredSize;
            double x = Math.Max(8, Math.Min(ActualWidth - size.Width - 8, left));
            double y = below;
            bool up = y + size.Height > ActualHeight - 8;
            if (up) y = Math.Max(TopInset, above - size.Height);
            Canvas.SetLeft(next, Math.Round(x));
            Canvas.SetTop(next, Math.Round(y));
            popup.Appear(up);
            UpdateCovering();
            MenuChanged?.Invoke(true);
        }

        public void CloseMenu()
        {
            FrameworkElement? m = menu;
            if (m == null) return;
            menu = null;
            closing++;
            ((IPopup)m).Disappear(() => { floatLayer.Children.Remove(m); closing--; UpdateCovering(); });
            MenuChanged?.Invoke(false);
        }

        /// <summary>Esc: closes the menu, else the sheet. Returns true when something closed.</summary>
        public bool Escape()
        {
            if (menu != null) { CloseMenu(); return true; }
            if (sheet != null) { CloseSheet(); return true; }
            return false;
        }

        /// <summary>Called for every press in the window: a press outside the open menu closes it.</summary>
        public void PressAnywhere(DependencyObject? target)
        {
            if (menu == null || target == null) return;
            for (DependencyObject? d = target; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : null)
                if (d == menu) return;
            CloseMenu();
        }
    }

    /// <summary>
    /// A modal sheet (LIQUID_UI.md 9.1): 560 px wide, radius 28, the near-opaque --solid-tint under --tint-sheet,
    /// hairline and bevel, --shadow-l. Head: the title in the display role and a close glyph button; the body
    /// scrolls. It grows out of the control that opened it (its clip runs from the control's bounds to the
    /// sheet over 300 ms, ease-out) and shrinks back into it over 180 ms when it closes.
    /// </summary>
    public sealed class Sheet : Grid
    {
        readonly Grid panel = new Grid { Background = Brushes.Transparent };
        readonly RectangleGeometry clip = new RectangleGeometry();
        readonly ShadowLayer shadow = new ShadowLayer("shadow-l", 28);
        readonly DockPanel content = new DockPanel();
        public readonly IconButton CloseButton;
        public readonly ScrollViewer Scroller;
        internal Action? CloseRequested;

        public Sheet(string title, UIElement body, string closeTip, double width = 560)
        {
            Width = width;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
            AutomationProperties.SetName(this, title);
            Children.Add(shadow);
            panel.Children.Add(new SurfaceVisual(28, "stroke-2", "btn-bevel", "solid-tint", "tint-sheet"));
            var head = new Grid { Margin = new Thickness(22, 16, 14, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Typography.Text(title, Typography.Display));
            CloseButton = new IconButton(Icons.X, closeTip, IconKind.Bare, ButtonSize.L) { Width = 30, Height = 30 };
            Tip.Set(CloseButton, closeTip);
            CloseButton.Click += (s, e) => CloseRequested?.Invoke();
            SetColumn(CloseButton, 1);
            head.Children.Add(CloseButton);
            DockPanel.SetDock(head, Dock.Top);
            content.Children.Add(head);
            Scroller = new ScrollViewer { Content = body, Padding = new Thickness(14, 0, 14, 22), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            content.Children.Add(Scroller);
            panel.Children.Add(content);
            panel.Clip = clip;
            Children.Add(panel);
            SizeChanged += (s, e) => { clip.BeginAnimation(RectangleGeometry.RectProperty, null); clip.Rect = new Rect(e.NewSize); clip.RadiusX = clip.RadiusY = 28; };
        }

        internal void FocusFirst() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => MoveFocus(new TraversalRequest(FocusNavigationDirection.First))));

        internal void Open(Rect? from)
        {
            IsHitTestVisible = true;
            var full = new Rect(RenderSize);
            clip.RadiusX = clip.RadiusY = 28;
            if (!Motion.Enabled) { clip.Rect = full; return; }
            Rect start = from ?? Shrink(full, 0.94);
            clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(start, full, Motion.D3) { EasingFunction = Motion.EaseOut });
            content.Opacity = 0;
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D2) { BeginTime = TimeSpan.FromMilliseconds(100) });
            shadow.Opacity = 0;
            shadow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.D2) { BeginTime = TimeSpan.FromMilliseconds(120) });
        }

        internal void Close(Rect? to, Action done)
        {
            IsHitTestVisible = false;
            if (!Motion.Enabled) { done(); return; }
            var full = new Rect(RenderSize);
            Rect end = to ?? Shrink(full, 0.94);
            var a = new RectAnimation(clip.Rect, end, Motion.D2) { EasingFunction = Motion.EaseInOut };
            a.Completed += (s, e) => done();
            clip.BeginAnimation(RectangleGeometry.RectProperty, a);
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Motion.D1));
            shadow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Motion.D1));
        }

        static Rect Shrink(Rect r, double k) => new Rect(r.X + r.Width * (1 - k) / 2, r.Y + r.Height * (1 - k) / 2, r.Width * k, r.Height * k);
    }

    /// <summary>
    /// A menu: a near-opaque --menu-tint capsule (radius 14, padding 5, --shadow-m) with 30 px entries (icon,
    /// label in the body role, key in the caption role), separators, a danger entry in --rose-ink and disabled
    /// entries at 0.36. It appears with a 6 px rise and a 0.96 -> 1 scale over 180 ms. For rare actions only:
    /// a primary function never hides behind a menu.
    /// </summary>
    public sealed class PopMenu : Grid, IPopup
    {
        readonly StackPanel list = new StackPanel { Margin = new Thickness(5) };
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly TranslateTransform rise = new TranslateTransform();
        Action? picked;

        public Action? Picked { set => picked = value; }

        public PopMenu(double minWidth = 190)
        {
            MinWidth = minWidth;
            Background = Brushes.Transparent;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Children.Add(new ShadowLayer("shadow-m", 14));
            Children.Add(new SurfaceVisual(14, "stroke-2", null, "menu-tint"));
            Children.Add(list);
            RenderTransform = new TransformGroup { Children = { scale, rise } };
            RenderTransformOrigin = new Point(0.2, 0);
        }

        public MenuEntry Add(string label, string? icon, string? key, string? tip, Action? run, bool danger = false)
        {
            var e = new MenuEntry(label, icon, key, danger);
            if (tip != null) Tip.Set(e, tip);
            e.Click += (s, a) => { if (!e.KeepsMenuOpen) picked?.Invoke(); run?.Invoke(); };
            if (list.Children.Count > 0 && !(list.Children[list.Children.Count - 1] is Border)) e.Margin = new Thickness(0, 1, 0, 0);
            list.Children.Add(e);
            return e;
        }

        /// <summary>Adds an element of the caller's own (a row with two lines, a field) to the list.</summary>
        public void AddElement(FrameworkElement element)
        {
            if (list.Children.Count > 0 && !(list.Children[list.Children.Count - 1] is Border)) element.Margin = new Thickness(0, 1, 0, 0);
            list.Children.Add(element);
        }

        public bool HasEntries => list.Children.Count > 0;

        /// <summary>Closes the menu as a pick of an entry does.</summary>
        public void Close() => picked?.Invoke();

        public void AddSeparator()
        {
            var line = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
            line.SetResourceReference(Border.BackgroundProperty, "T.line-soft");
            list.Children.Add(line);
        }

        public void Appear(bool up)
        {
            RenderTransformOrigin = new Point(0.2, up ? 1 : 0);
            if (!Motion.Enabled) return;
            Opacity = 0;
            scale.ScaleX = scale.ScaleY = 0.96;
            rise.Y = up ? -6 : 6;
            Motion.To(this, OpacityProperty, 1, Motion.D2);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.D2);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1, Motion.D2);
            Motion.To(rise, TranslateTransform.YProperty, 0, Motion.D2);
        }

        public void Disappear(Action done)
        {
            IsHitTestVisible = false;
            Motion.To(scale, ScaleTransform.ScaleXProperty, 0.96, Motion.D1);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 0.96, Motion.D1);
            Motion.To(this, OpacityProperty, 0, Motion.D1, null, done);
        }
    }

    /// <summary>One entry of a <see cref="PopMenu"/>: hover fill --fill-3 on a radius-10 plate, no press scale.</summary>
    public sealed class MenuEntry : Pressable
    {
        readonly PlateVisual plate = new PlateVisual(PlateKind.Bare) { Radius = 10 };
        readonly TextBlock text;
        bool danger, pinHover;

        /// <summary>True for an entry that asks first: its click leaves the menu open (the caller changes the label).</summary>
        public bool KeepsMenuOpen;

        public MenuEntry(string label, string? icon, string? key, bool danger)
        {
            this.danger = danger;
            ScalesOnPress = false;
            Height = 30;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(this, label);
            var row = new Grid { Margin = new Thickness(9, 0, 9, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null && icon.Length == 0)
                row.Children.Add(new Border { Width = 15, Margin = new Thickness(0, 0, 8, 0) }); // keeps the label in line with the entries that have an icon
            else if (icon != null)
            {
                FrameworkElement ic = Icons.Make(icon, 15);
                ic.Margin = new Thickness(0, 0, 8, 0);
                ic.SetResourceReference(TextBlock.ForegroundProperty, danger ? "T.rose-ink" : "T.ink-3");
                row.Children.Add(ic);
            }
            text = Typography.Text(label, Typography.Body, null);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (key != null)
            {
                TextBlock k = Typography.Text(key, Typography.Caption, "ink-4");
                k.Margin = new Thickness(16, 0, 0, 0);
                Grid.SetColumn(k, 2);
                row.Children.Add(k);
            }
            SetResourceReference(ForegroundProperty, danger ? "T.rose-ink" : "T.ink-1");
            Content = new Grid { Children = { plate, row } };
            Refresh(false);
        }

        public bool PinHover { get => pinHover; set { pinHover = value; Refresh(false); } }

        /// <summary>The visible label (an entry that asks first shows its question here).</summary>
        public string Label
        {
            get => text.Text;
            set { text.Text = value; AutomationProperties.SetName(this, value); }
        }

        /// <summary>Danger ink (rose) on or off.</summary>
        public bool Danger
        {
            get => danger;
            set { danger = value; SetResourceReference(ForegroundProperty, danger ? "T.rose-ink" : "T.ink-1"); }
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            GlassButton.To(plate, PlateVisual.HoverProperty, Math.Max(hover, Math.Max(focus, pinHover ? 1 : 0)), animate, Motion.D1);
            Opacity = IsEnabled ? 1 : 0.36;
        }
    }

    /// <summary>
    /// A hover card: a near-opaque card (radius 14, --menu-tint, hairline, bevel, --shadow-m) shown above an
    /// element after the tooltip delay, for content richer than one line (a marker's frame and note). It is drawn
    /// in the window's float window, so the video under it stays as it is.
    /// </summary>
    public sealed class HoverCard : Grid
    {
        public HoverCard(UIElement content, double width = 240)
        {
            Width = width;
            IsHitTestVisible = false;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Children.Add(new ShadowLayer("shadow-m", 14));
            Children.Add(new SurfaceVisual(14, "stroke-2", "btn-bevel", "menu-tint"));
            Children.Add(new Border { Padding = new Thickness(10), Child = content });
        }

        static readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Tip.DelayMs) };
        static FrameworkElement? pending;
        static Func<UIElement>? pendingBuild;
        static HoverCard? shown;

        static HoverCard()
        {
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (pending == null || pendingBuild == null) return;
                Show(new HoverCard(pendingBuild()), pending, new Rect(pending.RenderSize));
            };
        }

        /// <summary>Shows a card built by <paramref name="build"/> while the pointer rests on the element.</summary>
        public static void Attach(FrameworkElement element, Func<UIElement> build)
        {
            element.MouseEnter += (s, e) => { pending = element; pendingBuild = build; timer.Stop(); timer.Start(); };
            element.MouseLeave += (s, e) => { timer.Stop(); pending = null; Hide(); };
        }

        /// <summary>
        /// Shows a card above an area of an element (below it when the title bar is in the way), for content drawn
        /// by the element itself (the markers of a timeline). One card at a time.
        /// </summary>
        public static void Show(HoverCard card, FrameworkElement relativeTo, Rect area)
        {
            Hide();
            if (!(Tip.RootOf(relativeTo) is FrameworkElement root) || !(root is IOverlayRoot overlay) || !relativeTo.IsVisible) return;
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Rect at = relativeTo.TransformToVisual(root).TransformBounds(area);
            Point p = FloatPlacement.Above(at, card.DesiredSize, new Size(root.ActualWidth, root.ActualHeight), OverlayHost.TopInset);
            FloatWindow? window = overlay.Float(FloatKind.Card);
            shown = card;
            if (window != null) window.ShowAt(card, p, root);
            else
            {
                overlay.TipLayer.Children.Add(card);
                Canvas.SetLeft(card, p.X);
                Canvas.SetTop(card, p.Y);
            }
            card.Opacity = 0;
            Motion.To(card, OpacityProperty, 1, Motion.D1);
            card.Tag = window;
        }

        public static void Hide()
        {
            HoverCard? card = shown;
            if (card == null) return;
            shown = null;
            if (card.Parent is Panel p) p.Children.Remove(card);
            (card.Tag as FloatWindow)?.HideIf(card);
        }
    }

    /// <summary>
    /// A popover: a near-opaque card (radius 22, padding 14, --menu-tint, hairline, bevel, --shadow-m) anchored to
    /// the control that opened it, for a small view with its own controls (the updates popover of the version
    /// pill). It opens and closes like a menu: Esc or a press outside closes it.
    /// </summary>
    public sealed class Popover : Grid, IPopup
    {
        readonly Border body = new Border { Padding = new Thickness(14) };
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly TranslateTransform rise = new TranslateTransform();
        Action? picked;

        public Popover(double width = 352)
        {
            Width = width;
            Background = Brushes.Transparent;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Children.Add(new ShadowLayer("shadow-m", 22));
            Children.Add(new SurfaceVisual(22, "stroke-2", "btn-bevel", "menu-tint"));
            Children.Add(body);
            RenderTransform = new TransformGroup { Children = { scale, rise } };
            RenderTransformOrigin = new Point(0.15, 0);
        }

        public Action? Picked { set => picked = value; }

        /// <summary>The content of the card.</summary>
        public UIElement? Body { get => body.Child; set => body.Child = value; }

        /// <summary>Closes the popover.</summary>
        public void Close() => picked?.Invoke();

        public void Appear(bool up)
        {
            RenderTransformOrigin = new Point(0.15, up ? 1 : 0);
            if (!Motion.Enabled) return;
            Opacity = 0;
            scale.ScaleX = scale.ScaleY = 0.97;
            rise.Y = up ? -6 : 6;
            Motion.To(this, OpacityProperty, 1, Motion.D2);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.D3);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1, Motion.D3);
            Motion.To(rise, TranslateTransform.YProperty, 0, Motion.D3);
        }

        public void Disappear(Action done)
        {
            IsHitTestVisible = false;
            Motion.To(scale, ScaleTransform.ScaleXProperty, 0.97, Motion.D1);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 0.97, Motion.D1);
            Motion.To(this, OpacityProperty, 0, Motion.D1, null, done);
        }
    }
}
