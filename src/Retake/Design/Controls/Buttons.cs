using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Retake.Design.Controls
{
    public enum ButtonKind { Plain, Ghost, Tinted, Danger }
    public enum ButtonSize { L, M, S, XS }

    /// <summary>
    /// Base of every pressable control of the layer: tracks hover, press and keyboard focus (the ring shows for
    /// keyboard focus only, as CSS :focus-visible), plays the press scale, and lets a gallery pin a state
    /// (ShowHover / ShowPressed / ShowFocus) without a pointer. The visual itself is the subclass's.
    /// </summary>
    public abstract class Pressable : Button
    {
        static readonly ControlTemplate PresenterOnly = MakeTemplate();
        protected readonly ScaleTransform PressScale = new ScaleTransform(1, 1);
        bool showHover, showPressed, showFocus, focusVisible, wasPressed;

        protected Pressable()
        {
            Template = PresenterOnly;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = PressScale;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Center;
            IsEnabledChanged += (s, e) => Refresh(true);
        }

        static ControlTemplate MakeTemplate()
        {
            var t = new ControlTemplate(typeof(ButtonBase)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            t.Seal();
            return t;
        }

        /// <summary>The whole bounds take the pointer (the drawn plate itself is not hit-testable).</summary>
        protected override HitTestResult HitTestCore(PointHitTestParameters p) => new PointHitTestResult(this, p.HitPoint);

        public bool ShowHover { get => showHover; set { showHover = value; Refresh(false); } }
        public bool ShowPressed { get => showPressed; set { showPressed = value; Refresh(false); } }
        public bool ShowFocus { get => showFocus; set { showFocus = value; Refresh(false); } }

        /// <summary>False for controls that do not shrink when pressed (menu entries).</summary>
        protected bool ScalesOnPress = true;

        protected bool Hovered => showHover || (IsMouseOver && IsEnabled);
        protected bool Pressed => showPressed || IsPressed;
        protected bool FocusShown => showFocus || (focusVisible && IsKeyboardFocused);

        protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); Refresh(true); }
        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); Refresh(true); }
        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e) { base.OnIsPressedChanged(e); Refresh(true); }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            focusVisible = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
            Refresh(true);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            focusVisible = false;
            Refresh(true);
        }

        /// <summary>Re-reads the state and moves the visual towards it.</summary>
        protected void Refresh(bool animate)
        {
            bool pressed = Pressed;
            if (pressed != wasPressed)
            {
                wasPressed = pressed;
                if (!ScalesOnPress) { }
                else if (animate) Motion.Press(PressScale, pressed);
                else { Motion.Set(PressScale, ScaleTransform.ScaleXProperty, pressed ? Motion.PressScale : 1); Motion.Set(PressScale, ScaleTransform.ScaleYProperty, pressed ? Motion.PressScale : 1); }
            }
            Apply(Hovered ? 1 : 0, pressed ? 1 : 0, FocusShown ? 1 : 0, animate && Motion.Enabled);
        }

        /// <summary>Moves the visual to hover / press / focus amounts (0 or 1).</summary>
        protected abstract void Apply(double hover, double press, double focus, bool animate);

    }


    /// <summary>
    /// The button of the kit: a label with an optional leading icon on the plate, in four kinds (plain, ghost,
    /// tinted = the one primary action of a view, danger) and four sizes (l 40, m 32, s 28, xs 24). States:
    /// rest, hover, pressed, keyboard focus, disabled (opacity 0.38), busy (label hidden, ring turning).
    /// </summary>
    public class GlassButton : Pressable
    {
        public readonly ButtonKind Kind;
        public readonly ButtonSize Size;
        readonly PlateVisual plate;
        readonly ShadowLayer? drop, pressGlow;
        readonly StackPanel label;
        readonly Spinner spinner;
        readonly TextBlock text;
        bool busy;

        public GlassButton(string caption, string? icon = null, ButtonKind kind = ButtonKind.Plain, ButtonSize size = ButtonSize.M)
        {
            Kind = kind;
            Size = size;
            double h = size == ButtonSize.L ? 40 : size == ButtonSize.M ? 32 : size == ButtonSize.S ? 28 : 24;
            double pad = size == ButtonSize.L ? 18 : size == ButtonSize.M ? 14 : size == ButtonSize.S ? 12 : 9;
            double iconSize = size == ButtonSize.L ? 16 : size == ButtonSize.M ? 15 : size == ButtonSize.S ? 14 : 13;
            double gap = size == ButtonSize.L ? 7 : size == ButtonSize.M ? 6 : size == ButtonSize.S ? 5 : 4;
            TypeRole role = size == ButtonSize.L ? Typography.BodyMedium : size == ButtonSize.XS ? Typography.LabelS : Typography.Label;
            Height = h;
            SetResourceReference(ForegroundProperty, kind == ButtonKind.Tinted ? "T.hero-ink" : kind == ButtonKind.Danger ? "T.rose-ink" : "T.ink-1");
            AutomationProperties.SetName(this, caption);

            var root = new Grid { Height = h };
            if (kind == ButtonKind.Tinted)
            {
                drop = new ShadowLayer(() => new[]
                {
                    new ShadowSpec { Y = 8, Blur = 22, Spread = -8, Color = Themes.Color("btn-glow") },
                    new ShadowSpec { Y = 1, Blur = 2, Color = Themes.Tone("shade-tone", 0.2) },
                });
                pressGlow = new ShadowLayer(() => new[] { new ShadowSpec { Y = 3, Blur = 10, Spread = -6, Color = Themes.Color("btn-glow") } }) { Opacity = 0 };
                root.Children.Add(drop);
                root.Children.Add(pressGlow);
            }
            else if (kind != ButtonKind.Ghost)
            {
                drop = new ShadowLayer("btn-drop");
                root.Children.Add(drop);
            }
            plate = new PlateVisual(kind == ButtonKind.Ghost ? PlateKind.Ghost : kind == ButtonKind.Tinted ? PlateKind.Tinted : kind == ButtonKind.Danger ? PlateKind.Danger : PlateKind.Plain);
            root.Children.Add(plate);

            label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(pad, 0, pad, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (icon != null)
            {
                FrameworkElement ic = Icons.Make(icon, iconSize);
                ic.Margin = new Thickness(0, 0, gap, 0);
                label.Children.Add(ic);
            }
            text = Typography.Text(caption, role, null);
            label.Children.Add(text);
            root.Children.Add(label);

            spinner = kind == ButtonKind.Tinted ? new Spinner(h >= 32 ? 15 : 13, "rim-tone", "hero-ink", 0.35) : new Spinner(h >= 32 ? 15 : 13);
            spinner.Visibility = Visibility.Collapsed;
            root.Children.Add(spinner);
            Content = root;
            Refresh(false);
        }

        public string Caption { get => text.Text; set { text.Text = value; AutomationProperties.SetName(this, value); } }

        /// <summary>Busy: the label turns transparent (the width is kept) and the ring turns; input is ignored.</summary>
        public bool Busy
        {
            get => busy;
            set
            {
                busy = value;
                IsHitTestVisible = !value;
                AutomationProperties.SetItemStatus(this, value ? "Busy" : "");
                spinner.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                if (Motion.Enabled && IsLoaded) Motion.To(label, OpacityProperty, value ? 0 : 1, Motion.D2);
                else Motion.Set(label, OpacityProperty, value ? 0 : 1);
            }
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            To(plate, PlateVisual.HoverProperty, hover, animate, Motion.D1);
            To(plate, PlateVisual.PressProperty, press, animate, press > 0 ? Motion.D1 : Motion.D2);
            To(plate, PlateVisual.RingProperty, focus, animate, Motion.D1);
            if (Kind == ButtonKind.Tinted)
            {
                if (drop != null) To(drop, OpacityProperty, 1 - press, animate, Motion.D1);
                if (pressGlow != null) To(pressGlow, OpacityProperty, press, animate, Motion.D1);
            }
            else if (drop != null) To(drop, OpacityProperty, 1 - press, animate, Motion.D1);
            To(this, OpacityProperty, IsEnabled ? 1 : 0.38, animate, Motion.D2);
        }

        internal static void To(UIElement target, DependencyProperty p, double to, bool animate, Duration d)
        {
            if (animate) Motion.To(target, p, to, d); else Motion.Set(target, p, to);
        }
    }

    public enum IconKind { Bare, Plate, Title, TitleClose }

    /// <summary>
    /// A round glyph button (l 32, m 28, s 24; icons 18 / 16 / 14). Bare: transparent at rest, a fill on hover;
    /// Plate: on the plate like a button. Title / TitleClose: the 30 x 28 buttons of the title-bar cluster
    /// (close turns rose on hover). <see cref="Turn"/> rotates the icon by 60 degrees (the gear on hover and
    /// while its sheet is open). Every glyph button needs a tooltip (<see cref="Tip"/>): it has no label.
    /// </summary>
    public class IconButton : Pressable
    {
        public readonly IconKind Kind;
        readonly PlateVisual plate;
        readonly ShadowLayer? drop;
        readonly FrameworkElement glyph;
        bool open;

        public bool TurnsOnHover;

        public IconButton(string icon, string name, IconKind kind = IconKind.Bare, ButtonSize size = ButtonSize.M)
        {
            Kind = kind;
            double box = size == ButtonSize.L ? 32 : size == ButtonSize.M ? 28 : 24;
            double iconSize = size == ButtonSize.L ? 18 : size == ButtonSize.M ? 16 : 14;
            if (kind == IconKind.Title || kind == IconKind.TitleClose) { Width = 30; Height = 28; iconSize = icon == Icons.Gear ? 17 : 15; }
            else Width = Height = box;
            AutomationProperties.SetName(this, name);
            var root = new Grid();
            if (kind == IconKind.Plate)
            {
                drop = new ShadowLayer("btn-drop");
                root.Children.Add(drop);
            }
            plate = new PlateVisual(kind == IconKind.Plate ? PlateKind.Plain : kind == IconKind.TitleClose ? PlateKind.TitleClose : PlateKind.Bare);
            root.Children.Add(plate);
            glyph = Icons.Make(icon, iconSize);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(glyph);
            Content = root;
            Refresh(false);
        }

        /// <summary>Marks the button as the source of an open overlay (the gear while Settings is open).</summary>
        public bool IsSource
        {
            get => open;
            set { open = value; Refresh(Motion.Enabled && IsLoaded); }
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            double lit = Math.Max(hover, open ? 1 : 0);
            GlassButton.To(plate, PlateVisual.HoverProperty, lit, animate, Motion.D1);
            GlassButton.To(plate, PlateVisual.PressProperty, press, animate, press > 0 ? Motion.D1 : Motion.D2);
            GlassButton.To(plate, PlateVisual.RingProperty, focus, animate, Motion.D1);
            if (drop != null) GlassButton.To(drop, OpacityProperty, 1 - press, animate, Motion.D1);
            string ink = Kind == IconKind.Plate ? "T.ink-1" : Kind == IconKind.TitleClose && (hover > 0 || press > 0) ? "T.hero-ink" : lit > 0 || press > 0 ? "T.ink-1" : "T.ink-2";
            SetResourceReference(ForegroundProperty, ink);
            if (TurnsOnHover)
            {
                var turn = (RotateTransform)glyph.RenderTransform;
                double angle = lit > 0 ? 60 : 0;
                if (animate) Motion.To(turn, RotateTransform.AngleProperty, angle, Motion.D4, Motion.EaseTurn);
                else Motion.Set(turn, RotateTransform.AngleProperty, angle);
            }
            GlassButton.To(this, OpacityProperty, IsEnabled ? 1 : 0.35, animate, Motion.D2);
        }
    }
}
