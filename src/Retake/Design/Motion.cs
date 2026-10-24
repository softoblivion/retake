using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Retake.Design
{
    /// <summary>A CSS cubic-bezier(x1, y1, x2, y2) timing function as a WPF easing.</summary>
    public sealed class CubicBezierEase : EasingFunctionBase
    {
        readonly double x1, y1, x2, y2;

        public CubicBezierEase(double x1, double y1, double x2, double y2)
        {
            this.x1 = x1; this.y1 = y1; this.x2 = x2; this.y2 = y2;
            EasingMode = EasingMode.EaseIn; // EaseInCore is the curve itself
        }

        protected override double EaseInCore(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            // Solve x(u) = t by bisection (monotonic in u for valid CSS curves), return y(u).
            double lo = 0, hi = 1, u = t;
            for (int i = 0; i < 30; i++)
            {
                u = (lo + hi) / 2;
                if (Bezier(u, x1, x2) < t) lo = u; else hi = u;
            }
            return Bezier(u, y1, y2);
        }

        static double Bezier(double u, double p1, double p2)
        {
            double v = 1 - u;
            return 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u;
        }

        protected override Freezable CreateInstanceCore() => new CubicBezierEase(x1, y1, x2, y2);
    }

    /// <summary>
    /// The motion template of this layer: the kit's four durations with fixed easings, no springs.
    /// D1 120 ms hover colour and fill; D2 180 ms state fades; D3 300 ms layout and travel (thumbs, sheets,
    /// toasts); D4 520 ms long reveals and icon turns. Press = scale 0.97 over 120 ms, back over 180 ms.
    /// With <see cref="Enabled"/> false (captures) every change lands at once.
    /// </summary>
    public static class Motion
    {
        public static bool Enabled = true;

        public static readonly Duration D1 = new Duration(TimeSpan.FromMilliseconds(120));
        public static readonly Duration D2 = new Duration(TimeSpan.FromMilliseconds(180));
        public static readonly Duration D3 = new Duration(TimeSpan.FromMilliseconds(300));
        public static readonly Duration D4 = new Duration(TimeSpan.FromMilliseconds(520));

        /// <summary>--ease-out: cubic-bezier(0.2, 0.8, 0.2, 1).</summary>
        public static readonly IEasingFunction EaseOut = Frozen(new CubicBezierEase(0.2, 0.8, 0.2, 1));
        /// <summary>--ease-io: cubic-bezier(0.65, 0, 0.35, 1).</summary>
        public static readonly IEasingFunction EaseInOut = Frozen(new CubicBezierEase(0.65, 0, 0.35, 1));
        /// <summary>--ease-spring: cubic-bezier(0.2, 1.3, 0.4, 1), a fixed curve with a small overshoot for icon turns.</summary>
        public static readonly IEasingFunction EaseTurn = Frozen(new CubicBezierEase(0.2, 1.3, 0.4, 1));

        public const double PressScale = 0.97;

        static IEasingFunction Frozen(CubicBezierEase e)
        {
            e.Freeze();
            return e;
        }

        /// <summary>Animates a double property to a value; lands at once when motion is off.</summary>
        public static void To(IAnimatable target, DependencyProperty property, double to, Duration duration, IEasingFunction? ease = null, Action? done = null)
        {
            if (!Enabled || !duration.HasTimeSpan || duration.TimeSpan == TimeSpan.Zero)
            {
                target.BeginAnimation(property, null);
                ((DependencyObject)target).SetValue(property, to);
                done?.Invoke();
                return;
            }
            var a = new DoubleAnimation(to, duration) { EasingFunction = ease ?? EaseOut };
            if (done != null) a.Completed += (s, e) => done();
            target.BeginAnimation(property, a);
        }

        /// <summary>Sets a double property at once, dropping a running animation.</summary>
        public static void Set(IAnimatable target, DependencyProperty property, double value)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, value);
        }

        /// <summary>The press feel of every control: scale down over 120 ms, back over 180 ms.</summary>
        public static void Press(ScaleTransform scale, bool down)
        {
            double to = down ? PressScale : 1;
            Duration d = down ? D1 : D2;
            To(scale, ScaleTransform.ScaleXProperty, to, d);
            To(scale, ScaleTransform.ScaleYProperty, to, d);
        }
    }
}
