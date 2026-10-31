using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Retake.Design.Controls
{
    /// <summary>
    /// The text field of the kit: 32 px, radius 10, the --well fill with an inner top shade and a hairline rim,
    /// text in the mono role. Focus shows the 2 px ring flush with the edge. An optional placeholder in ink-4
    /// shows while the field is empty; it must not restate the row label or the hint. <see cref="Wrap"/> makes a
    /// wrapping field (Retake's option, not in the kit): one line high at first, it grows with the text up to a
    /// number of lines and then scrolls; the text stays one line (Enter is not taken, a typed or pasted line break
    /// becomes a space).
    /// </summary>
    public sealed class TextField : TextBox
    {
        /// <summary>Inner padding of a wrapping field, top and bottom: a one-line wrapping field is as high as a plain one.</summary>
        public const double WrapPad = 7;
        static readonly ControlTemplate FieldTemplate = MakeTemplate(false);
        static readonly ControlTemplate WrapTemplate = MakeTemplate(true);
        readonly WellVisual well = new WellVisual(10, "well", "inset-field", "well-rim") { FocusOffset = 0 };
        readonly TextBlock hint;
        bool showFocus, wraps;

        public TextField(string placeholder = "", double width = 220)
        {
            Template = FieldTemplate;
            Width = width;
            Height = 32;
            FocusVisualStyle = null;
            Design.Typography.Mono.Apply(this);
            SetResourceReference(ForegroundProperty, "T.ink-1");
            SetResourceReference(CaretBrushProperty, "T.ink-1");
            SetResourceReference(SelectionBrushProperty, "T.accent");
            SelectionOpacity = 0.35;
            VerticalContentAlignment = VerticalAlignment.Center;
            hint = Design.Typography.Text(placeholder, Design.Typography.Mono, "ink-4");
            hint.Margin = new Thickness(11, 0, 11, 0);
            hint.IsHitTestVisible = false;
            TextChanged += (s, e) => hint.Visibility = Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            IsEnabledChanged += (s, e) => Opacity = IsEnabled ? 1 : 0.4;
        }

        static ControlTemplate MakeTemplate(bool wrap)
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            var chrome = new FrameworkElementFactory(typeof(Border), "chrome");
            var hintHost = new FrameworkElementFactory(typeof(Border), "hintHost");
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(MarginProperty, wrap ? new Thickness(9, WrapPad, 9, WrapPad) : new Thickness(9, 0, 9, 0));
            host.SetValue(VerticalAlignmentProperty, wrap ? VerticalAlignment.Stretch : VerticalAlignment.Center);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden);
            // A wrapping field scrolls to the caret past its last line without showing a bar.
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, wrap ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled);
            root.AppendChild(chrome);
            root.AppendChild(hintHost);
            root.AppendChild(host);
            var t = new ControlTemplate(typeof(TextBox)) { VisualTree = root };
            t.Seal();
            return t;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if (GetTemplateChild("chrome") is Border chrome) chrome.Child = well;
            if (GetTemplateChild("hintHost") is Border hintHost) hintHost.Child = hint;
        }

        public bool ShowFocus { get => showFocus; set { showFocus = value; Ring(false); } }

        /// <summary>True for a wrapping field (<see cref="Wrap"/>).</summary>
        public bool Wraps => wraps;

        /// <summary>The height of one line of a wrapping field's text.</summary>
        public static double LineHeight => Design.Typography.Mono.LineHeight;

        /// <summary>The height of a wrapping field that shows <paramref name="lines"/> lines.</summary>
        public static double WrapHeight(int lines) => Math.Ceiling(lines * LineHeight + 2 * WrapPad);

        /// <summary>
        /// Makes the field wrap its text: at least 32 px, it grows line by line up to <paramref name="maxLines"/> lines,
        /// then scrolls to the caret. Enter is left to the owner (not accepted as text); line breaks that are typed or
        /// pasted become spaces, so the whole pasted text arrives on one line.
        /// </summary>
        public void Wrap(int maxLines)
        {
            if (wraps) return;
            wraps = true;
            Template = WrapTemplate;
            TextWrapping = TextWrapping.Wrap;
            AcceptsReturn = false;
            AcceptsTab = false;
            VerticalContentAlignment = VerticalAlignment.Top;
            Height = double.NaN;
            MinHeight = 32;
            MaxHeight = WrapHeight(Math.Max(1, maxLines));
            TextBlock.SetLineHeight(this, LineHeight);
            TextBlock.SetLineStackingStrategy(this, LineStackingStrategy.BlockLineHeight);
            hint.VerticalAlignment = VerticalAlignment.Top;
            hint.Margin = new Thickness(11, WrapPad, 11, 0);
            hint.TextTrimming = TextTrimming.CharacterEllipsis;
            DataObject.AddPastingHandler(this, OnPaste);
        }

        // WPF cuts a paste at the first line break when Enter is not accepted; the text goes in whole, on one line.
        void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true)) return;
            if (!(e.SourceDataObject.GetData(DataFormats.UnicodeText, true) is string text)) return;
            string line = Retake.Core.Note.OneLine(text);
            if (line == text) return;
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, line);
            data.SetData(DataFormats.Text, line);
            e.DataObject = data;
        }

        /// <summary>The placeholder shown while the field is empty.</summary>
        public string Placeholder { get => hint.Text; set => hint.Text = value ?? ""; }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); Ring(true); }
        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); Ring(true); }

        void Ring(bool animate) => GlassButton.To(well, WellVisual.RingProperty, showFocus || IsKeyboardFocused ? 1 : 0, animate && Motion.Enabled, Motion.D1);
    }

    /// <summary>
    /// The switch of the kit: a 40 x 24 track (--switch-track, inner shade, rim) that fills with --mint when on,
    /// and a white 20 px thumb with --thumb-shadow that travels 16 px over 300 ms; pressed, the thumb takes
    /// --thumb-shadow-active. The label sits in the settings row, so the tooltip states the effect, not the name.
    /// </summary>
    public sealed class Switch : ToggleButton
    {
        static readonly ControlTemplate PresenterOnly = MakeTemplate();
        readonly WellVisual track = new WellVisual(-1, "switch-track", "inset-field", "well-rim") { OnFill = "mint" };
        readonly ShadowLayer rest = new ShadowLayer("thumb-shadow"), active = new ShadowLayer("thumb-shadow-active") { Opacity = 0 };
        readonly TranslateTransform slide = new TranslateTransform();
        bool showPressed, showFocus, focusVisible;

        public event Action<bool>? Toggled;

        public Switch(bool on, string name)
        {
            Template = PresenterOnly;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
            Width = 40;
            Height = 24;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(this, name);
            var thumb = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), RenderTransform = slide };
            thumb.Children.Add(rest);
            thumb.Children.Add(active);
            thumb.Children.Add(new Painted(20, 20, (dc, s) => dc.DrawEllipse(Themes.Solid(Themes.Tone("rim-tone", 1)), null, new Point(10, 10), 10, 10)));
            Content = new Grid { Children = { track, thumb } };
            IsChecked = on;
            Refresh(false);
            Checked += (s, e) => { Refresh(true); Toggled?.Invoke(true); };
            Unchecked += (s, e) => { Refresh(true); Toggled?.Invoke(false); };
            IsEnabledChanged += (s, e) => Refresh(true);
        }

        static ControlTemplate MakeTemplate()
        {
            var t = new ControlTemplate(typeof(ButtonBase)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            t.Seal();
            return t;
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);

        public bool IsOn { get => IsChecked == true; set => IsChecked = value; }
        public bool ShowPressed { get => showPressed; set { showPressed = value; Refresh(false); } }
        public bool ShowFocus { get => showFocus; set { showFocus = value; Refresh(false); } }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e) { base.OnIsPressedChanged(e); Refresh(true); }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            focusVisible = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
            Refresh(true);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); focusVisible = false; Refresh(true); }

        void Refresh(bool animate)
        {
            animate = animate && Motion.Enabled;
            bool on = IsOn, pressed = showPressed || IsPressed;
            if (animate) Motion.To(slide, TranslateTransform.XProperty, on ? 16 : 0, Motion.D3);
            else Motion.Set(slide, TranslateTransform.XProperty, on ? 16 : 0);
            GlassButton.To(track, WellVisual.OnProperty, on ? 1 : 0, animate, Motion.D2);
            GlassButton.To(track, WellVisual.RingProperty, showFocus || (focusVisible && IsKeyboardFocused) ? 1 : 0, animate, Motion.D1);
            GlassButton.To(rest, OpacityProperty, pressed ? 0 : 1, animate, Motion.D1);
            GlassButton.To(active, OpacityProperty, pressed ? 1 : 0, animate, Motion.D1);
            GlassButton.To(this, OpacityProperty, IsEnabled ? 1 : 0.4, animate, Motion.D2);
        }
    }

    /// <summary>
    /// The segmented control of the kit: options on a sunken pill track, the chosen one under a raised thumb
    /// that slides to it over 300 ms. Regular (28 px, label role) or compact (26 px, label-s; settings rows).
    /// An option can be switched off with a reason (dimmed, its tooltip says why); the whole control can be
    /// disabled. Arrow keys move the choice while the control has keyboard focus.
    /// </summary>
    public sealed class Segmented : Grid
    {
        readonly WellVisual track = new WellVisual(-1, "well-deep", "inset-well", "well-rim");
        readonly Grid thumb;
        readonly TranslateTransform thumbX = new TranslateTransform();
        readonly StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3) };
        readonly List<SegOption> options = new List<SegOption>();
        readonly string?[] tips;
        int index = -1;
        bool showFocus, focusVisible, placed;

        public event Action<int>? Changed;

        public Segmented(string name, string[] labels, string?[] tips, int selected, bool compact = false)
        {
            this.tips = tips;
            Focusable = true;
            FocusVisualStyle = null;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(this, name);
            double h = compact ? 26 : 28;
            Height = h + 6;
            Children.Add(track);
            thumb = new Grid { Height = h, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3), RenderTransform = thumbX, Opacity = 0, IsHitTestVisible = false };
            thumb.Children.Add(new ShadowLayer(() => new[] { new ShadowSpec { Y = 3, Blur = 10, Color = Themes.Tone("shade-tone", Themes.Number("drop-shade-a")) } }));
            thumb.Children.Add(new ShadowLayer("drop-ring"));
            thumb.Children.Add(new ThumbFace());
            Children.Add(thumb);
            for (int i = 0; i < labels.Length; i++)
            {
                int at = i;
                var o = new SegOption(labels[i], compact, h);
                if (i < tips.Length && tips[i] != null) Tip.Set(o, tips[i]);
                o.Click += (s, e) => Choose(at);
                options.Add(o);
                row.Children.Add(o);
            }
            Children.Add(row);
            row.SizeChanged += (s, e) => Place(false);
            Select(selected, false);
            IsEnabledChanged += (s, e) => Opacity = IsEnabled ? 1 : 0.42;
        }

        /// <summary>The fill of the thumb: the rim tone at --drop-tint-a, a top bevel and a hairline.</summary>
        sealed class ThumbFace : FrameworkElement
        {
            public ThumbFace() { IsHitTestVisible = false; Themes.Track(this); }

            protected override void OnRender(DrawingContext dc)
            {
                double w = ActualWidth, h = ActualHeight;
                if (w <= 0 || h <= 0) return;
                var rect = new Rect(0, 0, w, h);
                double r = h / 2;
                dc.DrawRoundedRectangle(Themes.Solid(Themes.Tone("rim-tone", Themes.Number("drop-tint-a"))), null, rect, r, r);
                dc.DrawGeometry(Themes.Brush("btn-bevel"), null, Draw.Crescent(rect, r, 1));
                Draw.Hairline(dc, rect, r, Themes.Color("lens-hairline"));
            }
        }

        public int Index => index;
        public IReadOnlyList<Button> Options => options;

        public bool ShowFocus { get => showFocus; set { showFocus = value; RingUpdate(false); } }

        /// <summary>Pins the hover look on one option (gallery).</summary>
        public void ShowHover(int i) { if (i >= 0 && i < options.Count) options[i].PinHover = true; }

        public void Select(int i, bool animate)
        {
            if (i < 0 || i >= options.Count) return;
            index = i;
            for (int k = 0; k < options.Count; k++) options[k].On = k == i;
            Place(animate);
        }

        /// <summary>Switches an option off (dimmed, not choosable) with the reason as its tooltip, or back on.</summary>
        public void SetOff(int i, bool off, string? reason)
        {
            options[i].Off = off;
            Tip.Set(options[i], off ? reason : (i < tips.Length ? tips[i] : null));
        }

        public bool IsOff(int i) => options[i].Off;

        void Choose(int i)
        {
            if (options[i].Off || i == index) return;
            Select(i, true);
            Changed?.Invoke(i);
        }

        void Place(bool animate)
        {
            if (index < 0 || row.ActualWidth <= 0) return;
            SegOption o = options[index];
            double x = o.TranslatePoint(new Point(0, 0), row).X, w = o.ActualWidth;
            if (animate && placed && Motion.Enabled)
            {
                Motion.To(thumbX, TranslateTransform.XProperty, x, Motion.D3);
                Motion.To(thumb, WidthProperty, w, Motion.D3);
            }
            else
            {
                Motion.Set(thumbX, TranslateTransform.XProperty, x);
                Motion.Set(thumb, WidthProperty, w);
                thumb.Opacity = 1;
                placed = true;
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); Focus(); }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            int step = e.Key == Key.Right || e.Key == Key.Down ? 1 : e.Key == Key.Left || e.Key == Key.Up ? -1 : 0;
            if (step == 0) return;
            e.Handled = true;
            for (int i = index + step; i >= 0 && i < options.Count; i += step)
                if (!options[i].Off) { Choose(i); break; }
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            focusVisible = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
            RingUpdate(true);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); focusVisible = false; RingUpdate(true); }

        void RingUpdate(bool animate) => GlassButton.To(track, WellVisual.RingProperty, showFocus || (focusVisible && IsKeyboardFocused) ? 1 : 0, animate && Motion.Enabled, Motion.D1);

        /// <summary>One option: text only (the thumb is the control's); ink-3 at rest, ink-2 on hover, ink-1 when chosen.</summary>
        sealed class SegOption : Pressable
        {
            bool on, off, pinHover;

            public SegOption(string label, bool compact, double h)
            {
                Focusable = false;
                Height = h;
                AutomationProperties.SetName(this, label);
                var text = Typography.Text(label, compact ? Typography.LabelS : Typography.Label, null);
                text.Margin = new Thickness(compact ? 10 : 12, 0, compact ? 10 : 12, 0);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                Content = new Grid { Background = Brushes.Transparent, Children = { text } };
                Refresh(false);
            }

            public bool On { get => on; set { on = value; AutomationProperties.SetItemStatus(this, value ? "selected" : ""); Refresh(false); } }
            public bool Off { get => off; set { off = value; Cursor = value ? Cursors.Arrow : Cursors.Hand; Refresh(false); } }
            public bool PinHover { get => pinHover; set { pinHover = value; Refresh(false); } }

            protected override void Apply(double hover, double press, double focus, bool animate)
            {
                bool lit = (hover > 0 || pinHover) && !off;
                SetResourceReference(ForegroundProperty, on ? "T.ink-1" : lit ? "T.ink-2" : "T.ink-3");
                Opacity = off ? 0.34 : 1;
            }
        }
    }
}
