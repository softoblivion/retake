using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Retake.Design.Controls
{
    /// <summary>Shared geometry helpers of the drawn visuals.</summary>
    static class Draw
    {
        /// <summary>The radius of a shape: a negative value means a pill (half the height).</summary>
        public static double Radius(double radius, double w, double h) => radius < 0 ? Math.Min(w, h) / 2 : Math.Min(radius, Math.Min(w, h) / 2);

        /// <summary>
        /// The band a CSS inset shadow "0 ±1px 0 colour" paints: the shape minus the shape moved by one pixel.
        /// dir +1 gives the top bevel, -1 the bottom shade.
        /// </summary>
        public static Geometry Crescent(Rect rect, double r, int dir)
        {
            var shape = new RectangleGeometry(rect, r, r);
            var moved = new RectangleGeometry(new Rect(rect.X, rect.Y + dir, rect.Width, rect.Height), r, r);
            var g = new CombinedGeometry(GeometryCombineMode.Exclude, shape, moved);
            g.Freeze();
            return g;
        }

        /// <summary>A soft inner shade along the top edge, for "inset 0 1px 2px colour" (press, wells, fields).</summary>
        public static void InsetTop(DrawingContext dc, Rect rect, double r, Color color)
        {
            if (color.A == 0) return;
            var brush = new LinearGradientBrush(color, Themes.WithAlpha(color, 0), new Point(0, rect.Top), new Point(0, rect.Top + 3.5)) { MappingMode = BrushMappingMode.Absolute };
            brush.Freeze();
            dc.PushClip(new RectangleGeometry(rect, r, r));
            dc.DrawRectangle(brush, null, rect);
            dc.Pop();
        }

        /// <summary>The specular streak of the plate: a vertical fade of the rim tone over the top 46 %.</summary>
        public static Brush Streak(double alpha)
        {
            Color tone = Themes.Color("rim-tone");
            var b = new LinearGradientBrush(Themes.WithAlpha(tone, alpha), Themes.WithAlpha(tone, 0), new Point(0, 0), new Point(0, 0.46));
            b.Freeze();
            return b;
        }

        /// <summary>The first colour of a shadow or inset token ("inset 0 1px 2px rgba(...)").</summary>
        public static Color ShadowColor(string token)
        {
            ShadowSpec[] s = Themes.Shadows(token);
            return s.Length > 0 ? s[0].Color : Colors.Transparent;
        }

        public static void Hairline(DrawingContext dc, Rect rect, double r, Color color)
        {
            if (color.A == 0) return;
            // The kit draws a 0.5 px hairline; one device pixel at a reduced alpha reads the same.
            var pen = new Pen(Themes.Solid(Themes.Fade(color, 0.6)), 1);
            pen.Freeze();
            Rect inner = new Rect(rect.X + 0.5, rect.Y + 0.5, Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));
            dc.DrawRoundedRectangle(null, pen, inner, Math.Max(0, r - 0.5), Math.Max(0, r - 0.5));
        }

        /// <summary>The focus ring: 2 px of --focus-ring at a 2 px offset (outset 0 for fields).</summary>
        public static void FocusRing(DrawingContext dc, Rect rect, double r, double amount, double offset = 2)
        {
            if (amount <= 0) return;
            var pen = new Pen(Themes.Solid(Themes.Fade(Themes.Color("focus-ring"), amount)), 2);
            pen.Freeze();
            double o = offset + 1;
            dc.DrawRoundedRectangle(null, pen, new Rect(rect.X - o, rect.Y - o, rect.Width + 2 * o, rect.Height + 2 * o), r + o, r + o);
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }

    public enum PlateKind { Plain, Ghost, Tinted, Danger, Bare, TitleClose }

    /// <summary>
    /// The plate every button stands on (LIQUID_UI.md 6), drawn in one pass: fill, specular streak over the top
    /// 46 %, top bevel, bottom shade, hairline, press inset and focus ring. Hover, Press and Ring (focus) are 0..1 and
    /// animated by the owning control; colours come from the current theme. The drop shadow is a separate
    /// <see cref="ShadowLayer"/> beneath (text above a shadow effect would lose ClearType).
    /// </summary>
    public sealed class PlateVisual : FrameworkElement
    {
        public static readonly DependencyProperty HoverProperty = DependencyProperty.Register(nameof(Hover), typeof(double), typeof(PlateVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PressProperty = DependencyProperty.Register(nameof(Press), typeof(double), typeof(PlateVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RingProperty = DependencyProperty.Register(nameof(Ring), typeof(double), typeof(PlateVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
        public double Press { get => (double)GetValue(PressProperty); set => SetValue(PressProperty, value); }
        public double Ring { get => (double)GetValue(RingProperty); set => SetValue(RingProperty, value); }

        public PlateKind Kind;
        /// <summary>Corner radius; negative = pill.</summary>
        public double Radius = -1;

        public PlateVisual(PlateKind kind)
        {
            Kind = kind;
            IsHitTestVisible = false;
            IsEnabledChanged += (s, e) => InvalidateVisual();
            Themes.Track(this);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Draw.Radius(Radius, w, h), hv = Hover, pr = Press;
            var rect = new Rect(0, 0, w, h);
            var shape = new RectangleGeometry(rect, r, r);
            Color clear = Colors.Transparent, fill = clear, stroke = clear, bevel = clear, shade = clear, overlay = clear, inset = clear;
            Brush? gradient = null;
            double streak = 0;
            double sRest = Themes.Number("btn-streak-rest"), sHover = Themes.Number("btn-streak-hover"), sPress = Themes.Number("btn-streak-press");
            switch (Kind)
            {
                case PlateKind.Plain:
                case PlateKind.Danger:
                    bool danger = Kind == PlateKind.Danger;
                    fill = Themes.Mix(Themes.Mix(Themes.Color("btn-fill"), Themes.Color(danger ? "danger-fill" : "btn-fill-hover"), hv), Themes.Color(danger ? "danger-fill-press" : "btn-fill-press"), pr);
                    streak = Draw.Lerp(Draw.Lerp(sRest, sHover, hv), sPress, pr);
                    stroke = danger ? Themes.Mix(Themes.Color("btn-stroke"), Themes.Color("danger-stroke"), Math.Max(hv, pr)) : Themes.Color("btn-stroke");
                    bevel = Themes.Fade(Themes.Color("btn-bevel"), 1 - pr);
                    shade = Themes.Fade(Themes.Color("btn-shade"), 1 - pr);
                    inset = Themes.Fade(Draw.ShadowColor("inset-press"), pr);
                    break;
                case PlateKind.Ghost:
                    Color f2 = Themes.Color("fill-2");
                    fill = Themes.Mix(Themes.Mix(Themes.WithAlpha(f2, 0), f2, hv), Themes.Color("fill-3"), pr);
                    streak = sRest * Math.Max(hv, pr);
                    stroke = Themes.Fade(Themes.Color("stroke-1"), Math.Max(hv, pr));
                    break;
                case PlateKind.Tinted:
                    gradient = Themes.Brush("accent-grad");
                    double tinted = Themes.Number("btn-streak-tinted");
                    // CSS brightens on hover (1.07) and darkens when pressed (0.96); a thin overlay of the tones stands in.
                    overlay = pr > 0 ? Themes.Tone("shade-tone", 0.05 * pr) : Themes.Tone("rim-tone", 0.07 * hv);
                    streak = Math.Max(0, tinted + 0.08 * hv - 0.1 * pr);
                    stroke = Themes.Tone("rim-tone", 0.28);
                    bevel = Themes.Tone("rim-tone", 0.34 * (1 - pr));
                    shade = Themes.Tone("shade-tone", 0.16 * (1 - pr));
                    inset = Themes.Fade(Draw.ShadowColor("inset-press"), pr);
                    break;
                case PlateKind.Bare:
                    Color f3 = Themes.Color("fill-3");
                    fill = Themes.Mix(Themes.Mix(Themes.WithAlpha(f3, 0), f3, hv), Themes.Color("fill-4"), pr);
                    break;
                case PlateKind.TitleClose:
                    Color rose = Themes.Color("rose");
                    fill = Themes.Mix(Themes.Mix(Themes.WithAlpha(rose, 0), rose, hv), Themes.Mix(rose, Themes.Tone("shade-tone", 1), 0.16), pr);
                    break;
            }
            if (!IsEnabled) streak = 0;
            if (gradient != null) dc.DrawGeometry(gradient, null, shape);
            if (fill.A > 0) dc.DrawGeometry(Themes.Solid(fill), null, shape);
            if (overlay.A > 0) dc.DrawGeometry(Themes.Solid(overlay), null, shape);
            if (streak > 0) dc.DrawGeometry(Draw.Streak(streak), null, shape);
            Draw.InsetTop(dc, rect, r, inset);
            if (bevel.A > 0) dc.DrawGeometry(Themes.Solid(bevel), null, Draw.Crescent(rect, r, 1));
            if (shade.A > 0) dc.DrawGeometry(Themes.Solid(shade), null, Draw.Crescent(rect, r, -1));
            Draw.Hairline(dc, rect, r, stroke);
            Draw.FocusRing(dc, rect, r, Ring);
        }
    }

    /// <summary>
    /// A glass surface without refraction or blur: one or more tint layers (bottom first), a hairline rim and a
    /// top bevel. Used by the sheet, menu, toast, tooltip, hover card, the title-bar cluster and the version pill.
    /// </summary>
    public sealed class SurfaceVisual : FrameworkElement
    {
        public static readonly DependencyProperty HoverProperty = DependencyProperty.Register(nameof(Hover), typeof(double), typeof(SurfaceVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RingProperty = DependencyProperty.Register(nameof(Ring), typeof(double), typeof(SurfaceVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
        public double Ring { get => (double)GetValue(RingProperty); set => SetValue(RingProperty, value); }

        public string[] Fills;
        public string? Rim, Bevel, HoverFill;
        public double Radius;

        public SurfaceVisual(double radius, string? rim, string? bevel, params string[] fills)
        {
            Radius = radius;
            Rim = rim;
            Bevel = bevel;
            Fills = fills;
            IsHitTestVisible = false;
            Themes.Track(this);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Draw.Radius(Radius, w, h);
            var rect = new Rect(0, 0, w, h);
            var shape = new RectangleGeometry(rect, r, r);
            foreach (string f in Fills) dc.DrawGeometry(Themes.Brush(f), null, shape);
            if (HoverFill != null && Hover > 0) dc.DrawGeometry(Themes.Solid(Themes.Fade(Themes.Color(HoverFill), Hover)), null, shape);
            if (Bevel != null) dc.DrawGeometry(Themes.Brush(Bevel), null, Draw.Crescent(rect, r, 1));
            if (Rim != null) Draw.Hairline(dc, rect, r, Themes.Color(Rim));
            Draw.FocusRing(dc, rect, r, Ring);
        }
    }

    /// <summary>
    /// A sunken well: the fill, a soft inner shade along the top, a hairline rim; optionally a coloured "on"
    /// fill (the switch) and the focus ring. Used by the text field, the segmented track and the switch track.
    /// </summary>
    public sealed class WellVisual : FrameworkElement
    {
        public static readonly DependencyProperty OnProperty = DependencyProperty.Register(nameof(On), typeof(double), typeof(WellVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RingProperty = DependencyProperty.Register(nameof(Ring), typeof(double), typeof(WellVisual), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double On { get => (double)GetValue(OnProperty); set => SetValue(OnProperty, value); }
        public double Ring { get => (double)GetValue(RingProperty); set => SetValue(RingProperty, value); }

        readonly string fill, inset, rim;
        public string? OnFill;
        public double Radius, FocusOffset = 2;

        public WellVisual(double radius, string fill, string inset, string rim)
        {
            Radius = radius;
            this.fill = fill;
            this.inset = inset;
            this.rim = rim;
            IsHitTestVisible = false;
            Themes.Track(this);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Draw.Radius(Radius, w, h);
            var rect = new Rect(0, 0, w, h);
            var shape = new RectangleGeometry(rect, r, r);
            dc.DrawGeometry(Themes.Brush(fill), null, shape);
            if (OnFill != null && On > 0) dc.DrawGeometry(Themes.Solid(Themes.Fade(Themes.Color(OnFill), On)), null, shape);
            Draw.InsetTop(dc, rect, r, Themes.Fade(Draw.ShadowColor(inset), 1 - On * 0.5));
            Draw.Hairline(dc, rect, r, Themes.Color(rim));
            Draw.FocusRing(dc, rect, r, Ring, FocusOffset);
        }
    }

    /// <summary>
    /// The outer shadows of a shape (a CSS box-shadow list from a theme token, inset entries skipped). Each entry
    /// is an opaque copy of the shape, grown by the spread, carrying a DropShadowEffect; the layer clips the
    /// shape itself away, so only the shadow outside it shows, as in CSS (a translucent plate stays clean).
    /// A spread-only entry (no offset, no blur) is drawn as an outline ring.
    /// </summary>
    public sealed class ShadowLayer : Grid, IThemed
    {
        readonly Func<ShadowSpec[]> source;
        public double Radius;

        public ShadowLayer(string token, double radius = -1) : this(() => Themes.Shadows(token), radius) { }

        public ShadowLayer(Func<ShadowSpec[]> source, double radius = -1)
        {
            this.source = source;
            Radius = radius;
            IsHitTestVisible = false;
            SizeChanged += (s, e) => Layout();
            Themes.Track(this);
            Rebuild();
        }

        public void OnThemeChanged() => Rebuild();

        public void Rebuild()
        {
            Children.Clear();
            foreach (ShadowSpec spec in source())
            {
                if (spec.Inset) continue;
                Color solid = Color.FromRgb(spec.Color.R, spec.Color.G, spec.Color.B);
                var b = new Border { Tag = spec, Margin = new Thickness(-spec.Spread) };
                if (spec.IsRing)
                {
                    b.BorderBrush = Themes.Solid(spec.Color);
                    b.BorderThickness = new Thickness(spec.Spread);
                }
                else
                {
                    b.Background = Themes.Solid(solid);
                    double dist = Math.Sqrt(spec.X * spec.X + spec.Y * spec.Y);
                    double dir = Math.Atan2(-spec.Y, spec.X) * 180 / Math.PI;
                    var effect = new DropShadowEffect
                    {
                        Color = solid,
                        Opacity = spec.Color.A / 255.0,
                        BlurRadius = spec.Blur,
                        ShadowDepth = dist,
                        Direction = dir < 0 ? dir + 360 : dir,
                        RenderingBias = RenderingBias.Performance,
                    };
                    effect.Freeze();
                    b.Effect = effect;
                }
                Children.Add(b);
            }
            Layout();
        }

        void Layout()
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Draw.Radius(Radius, w, h);
            foreach (UIElement child in Children)
            {
                var b = (Border)child;
                var spec = (ShadowSpec)b.Tag;
                b.CornerRadius = new CornerRadius(Math.Max(0, r + spec.Spread));
            }
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-200, -200, w + 400, h + 400)),
                new RectangleGeometry(new Rect(0, 0, w, h), r, r));
            outside.Freeze();
            Clip = outside;
        }
    }

    /// <summary>A small element painted by a callback with theme colours (dots, thumbs, the app glyph); repaints on theme change.</summary>
    public sealed class Painted : FrameworkElement
    {
        readonly Action<DrawingContext, Size> paint;

        public Painted(double width, double height, Action<DrawingContext, Size> paint)
        {
            Width = width;
            Height = height;
            this.paint = paint;
            IsHitTestVisible = false;
            Themes.Track(this);
        }

        protected override void OnRender(DrawingContext dc) => paint(dc, RenderSize);
    }

    /// <summary>The busy ring: a track and a quarter arc that turns once per 0.8 s.</summary>
    public sealed class Spinner : FrameworkElement
    {
        readonly RotateTransform turn = new RotateTransform(-40);
        readonly string track, head;
        readonly double trackAlpha;

        /// <param name="track">Token of the ring, or a tone token with trackAlpha &gt; 0.</param>
        public Spinner(double size, string track = "spin-track", string head = "spin-head", double trackAlpha = 0)
        {
            Width = Height = size;
            this.track = track;
            this.head = head;
            this.trackAlpha = trackAlpha;
            IsHitTestVisible = false;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = turn;
            Themes.Track(this);
            IsVisibleChanged += (s, e) => Spin();
        }

        void Spin()
        {
            if (IsVisible && Motion.Enabled)
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-40, 320, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
            else turn.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double s = Width, r = s / 2 - 1;
            Color t = trackAlpha > 0 ? Themes.Tone(track, trackAlpha) : Themes.Color(track);
            var trackPen = new Pen(Themes.Solid(t), 2);
            dc.DrawEllipse(null, trackPen, new Point(s / 2, s / 2), r, r);
            var arc = new StreamGeometry();
            using (StreamGeometryContext g = arc.Open())
            {
                g.BeginFigure(new Point(s / 2, s / 2 - r), false, false);
                g.ArcTo(new Point(s / 2 + r, s / 2), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawGeometry(null, new Pen(Themes.Brush(head), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        }
    }
}
