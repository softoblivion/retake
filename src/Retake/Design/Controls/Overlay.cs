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
    /// <summary>
    /// The overlay layer of a window, between the content and the title bar: the dim veil (--scrim), the modal
    /// sheet and the floating layer for menus and hover cards. Esc closes the top overlay; a click on the veil
    /// closes the sheet; a click outside a menu closes the menu. With nothing open the layer lets every click
    /// through. The sheet's top edge sits 6 px below the title bar (<see cref="TopInset"/>).
    /// </summary>
    public sealed class OverlayHost : Grid
    {
        public const double TopInset = 44 + 6, BottomInset = 14;
        readonly Rectangle veil = new Rectangle { Opacity = 0, Visibility = Visibility.Collapsed };
        readonly Grid sheetLayer = new Grid { Margin = new Thickness(0, TopInset, 0, BottomInset) };
        readonly Canvas floatLayer = new Canvas();
        Sheet? sheet;
        FrameworkElement? sheetOrigin;
        PopMenu? menu;
        HoverCard? card;

        public event Action<bool>? SheetChanged;

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

        public void OpenSheet(Sheet next, FrameworkElement? origin)
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
            Motion.To(veil, OpacityProperty, 0, Motion.D2, null, () => { if (sheet == null) veil.Visibility = Visibility.Collapsed; });
            s.Close(OriginRect(s, sheetOrigin), () => RemoveSheet(s));
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

        /// <summary>Opens a menu under (or above, when there is no room) the anchor, aligned to its left edge.</summary>
        public void OpenMenu(PopMenu next, FrameworkElement anchor)
        {
            CloseMenu();
            menu = next;
            next.Picked = () => CloseMenu();
            floatLayer.Children.Add(next);
            next.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Rect at = anchor.TransformToVisual(floatLayer).TransformBounds(new Rect(anchor.RenderSize));
            Size size = next.DesiredSize;
            double x = Math.Max(8, Math.Min(ActualWidth - size.Width - 8, at.Left));
            double y = at.Bottom + 6;
            bool up = y + size.Height > ActualHeight - 8;
            if (up) y = at.Top - 6 - size.Height;
            Canvas.SetLeft(next, Math.Round(x));
            Canvas.SetTop(next, Math.Round(y));
            next.Appear(up);
        }

        public void CloseMenu()
        {
            PopMenu? m = menu;
            if (m == null) return;
            menu = null;
            m.Disappear(() => floatLayer.Children.Remove(m));
        }

        public bool MenuOpen => menu != null;

        internal void ShowCard(HoverCard next, FrameworkElement anchor)
        {
            HideCard();
            card = next;
            floatLayer.Children.Add(next);
            next.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Rect at = anchor.TransformToVisual(floatLayer).TransformBounds(new Rect(anchor.RenderSize));
            Size size = next.DesiredSize;
            double x = Math.Max(8, Math.Min(ActualWidth - size.Width - 8, at.Left + at.Width / 2 - size.Width / 2));
            double y = at.Top - 8 - size.Height;
            if (y < TopInset) y = at.Bottom + 8;
            Canvas.SetLeft(next, Math.Round(x));
            Canvas.SetTop(next, Math.Round(y));
            next.Opacity = 0;
            Motion.To(next, OpacityProperty, 1, Motion.D1);
        }

        internal void HideCard()
        {
            if (card == null) return;
            floatLayer.Children.Remove(card);
            card = null;
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
    public sealed class PopMenu : Grid
    {
        readonly StackPanel list = new StackPanel { Margin = new Thickness(5) };
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly TranslateTransform rise = new TranslateTransform();
        internal Action? Picked;

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
            e.Click += (s, a) => { Picked?.Invoke(); run?.Invoke(); };
            if (list.Children.Count > 0 && !(list.Children[list.Children.Count - 1] is Border)) e.Margin = new Thickness(0, 1, 0, 0);
            list.Children.Add(e);
            return e;
        }

        public void AddSeparator()
        {
            var line = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
            line.SetResourceReference(Border.BackgroundProperty, "T.line-soft");
            list.Children.Add(line);
        }

        internal void Appear(bool up)
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

        internal void Disappear(Action done)
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
        readonly bool danger;
        bool pinHover;

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
            if (icon != null)
            {
                FrameworkElement ic = Icons.Make(icon, 15);
                ic.Margin = new Thickness(0, 0, 8, 0);
                ic.SetResourceReference(TextBlock.ForegroundProperty, danger ? "T.rose-ink" : "T.ink-3");
                row.Children.Add(ic);
            }
            TextBlock text = Typography.Text(label, Typography.Body, null);
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

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            GlassButton.To(plate, PlateVisual.HoverProperty, Math.Max(hover, Math.Max(focus, pinHover ? 1 : 0)), animate, Motion.D1);
            Opacity = IsEnabled ? 1 : 0.36;
        }
    }

    /// <summary>
    /// A hover card: a near-opaque card (radius 14, --menu-tint, hairline, bevel, --shadow-m) shown above an
    /// element after the tooltip delay, for content richer than one line (a marker's frame and note).
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

        static HoverCard()
        {
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (pending == null || pendingBuild == null) return;
                Tip.RootOf(pending)?.Overlay.ShowCard(new HoverCard(pendingBuild()), pending);
            };
        }

        /// <summary>Shows a card built by <paramref name="build"/> while the pointer rests on the element.</summary>
        public static void Attach(FrameworkElement element, Func<UIElement> build)
        {
            element.MouseEnter += (s, e) => { pending = element; pendingBuild = build; timer.Stop(); timer.Start(); };
            element.MouseLeave += (s, e) => { timer.Stop(); pending = null; Tip.RootOf(element)?.Overlay.HideCard(); };
        }
    }
}
