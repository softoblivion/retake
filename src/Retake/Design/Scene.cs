using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Retake.Design
{
    /// <summary>
    /// The static background of the window, the kit's "deep" treatment painted once: the vertical base gradient
    /// (--bg-top, --bg-mid at 52 %, --bg-bottom), two soft fields of --scene-hue (a large one low left, a smaller
    /// one top right) and a small field of --scene-accent in the bottom right corner, scaled by
    /// --scene-fields-o, then the theme's --scene-vignette. No drift, specks, lines, sweep, parallax or grain.
    /// A theme change crossfades the old picture into the new one over 300 ms.
    /// </summary>
    public sealed class Scene : Grid, IThemed
    {
        // x, y: centre in % of the scene; s: diameter in % of the longer side; a: alpha (LIQUID_UI.md 7.1).
        static readonly (string token, double x, double y, double s, double a)[] Fields =
        {
            ("scene-hue", 26, 72, 128, 0.44),
            ("scene-hue", 84, 16, 76, 0.2),
            ("scene-accent", 90, 90, 60, 0.15),
        };

        Grid picture;

        public Scene()
        {
            IsHitTestVisible = false;
            ClipToBounds = true;
            picture = Paint();
            Children.Add(picture);
            SizeChanged += (s, e) => { foreach (UIElement c in Children) Place((Grid)c); };
            Themes.Track(this);
        }

        public void OnThemeChanged()
        {
            Grid old = picture;
            picture = Paint();
            Place(picture);
            Children.Add(picture);
            if (!Motion.Enabled) { Children.Remove(old); return; }
            picture.Opacity = 0;
            Motion.To(picture, OpacityProperty, 1, Motion.D3, Motion.EaseInOut, () => Children.Remove(old));
        }

        static Grid Paint()
        {
            var g = new Grid();
            var stops = new GradientStopCollection
            {
                new GradientStop(Themes.Color("bg-top"), 0),
                new GradientStop(Themes.Color("bg-mid"), 0.52),
                new GradientStop(Themes.Color("bg-bottom"), 1),
            };
            var bg = new LinearGradientBrush(stops, new Point(0, 0), new Point(0, 1));
            bg.Freeze();
            g.Children.Add(new Rectangle { Fill = bg });
            var canvas = new Canvas();
            double k = Themes.Number("scene-fields-o", 1);
            foreach (var f in Fields) canvas.Children.Add(new Ellipse { Fill = FieldBrush(Themes.Color(f.token), f.a * k), Tag = f });
            g.Children.Add(canvas);
            g.Children.Add(new Rectangle { Fill = Themes.Brush("scene-vignette") });
            return g;
        }

        /// <summary>
        /// The CSS field is a circle gradient sized to the farthest corner of its square box (radius = side x 0.707):
        /// alpha x 0.9 at 0 %, x 0.52 at 30 %, x 0.16 at 56 %, clear at 70 %. On an ellipse that fills the box,
        /// WPF's radius is half the side, so the offsets are multiplied by 1.414.
        /// </summary>
        static Brush FieldBrush(Color c, double a)
        {
            var stops = new GradientStopCollection
            {
                new GradientStop(Themes.WithAlpha(c, a * 0.9), 0),
                new GradientStop(Themes.WithAlpha(c, a * 0.52), 0.30 * 1.4142),
                new GradientStop(Themes.WithAlpha(c, a * 0.16), 0.56 * 1.4142),
                new GradientStop(Themes.WithAlpha(c, 0), 0.70 * 1.4142),
            };
            var b = new RadialGradientBrush(stops);
            b.Freeze();
            return b;
        }

        void Place(Grid g)
        {
            double w = ActualWidth, h = ActualHeight, vmax = Math.Max(w, h);
            if (w <= 0 || h <= 0) return;
            var canvas = (Canvas)g.Children[1];
            foreach (UIElement child in canvas.Children)
            {
                var e = (Ellipse)child;
                var f = ((string token, double x, double y, double s, double a))e.Tag;
                double size = f.s * vmax / 100;
                e.Width = e.Height = size;
                Canvas.SetLeft(e, f.x * w / 100 - size / 2);
                Canvas.SetTop(e, f.y * h / 100 - size / 2);
            }
        }
    }
}
