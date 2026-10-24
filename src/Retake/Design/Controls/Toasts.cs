using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Retake.Design.Controls
{
    /// <summary>
    /// A toast: a pill (min 38 px) on the near-opaque --solid-tint under --toast-tint (--toast-error-tint for
    /// errors), hairline, bevel, --shadow-m; a 24 px round icon in the level's fill and ink, the text in the
    /// body role at medium weight, an optional action. A click anywhere on it closes it (the action runs first).
    /// Info and success close after 3 s, warning and error after 10 s; the timer waits while the pointer rests
    /// on the toast. A sticky toast has no timer and stays until it is clicked (see <see cref="ToastTiming"/>).
    /// </summary>
    public sealed class Toast : Grid
    {
        public readonly ToastLevel Level;
        public readonly string Message;
        public readonly bool Sticky;
        readonly TranslateTransform shift = new TranslateTransform();
        readonly DispatcherTimer? timer;
        internal Action<Toast>? Dismissed;

        public Toast(ToastLevel level, string text, string? actionLabel = null, Action? action = null, bool sticky = false)
        {
            Level = level;
            Message = text;
            Sticky = sticky;
            MinHeight = 38;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            HorizontalAlignment = HorizontalAlignment.Center;
            RenderTransform = shift;
            AutomationProperties.SetName(this, text);
            AutomationProperties.SetAutomationId(this, "Toast");
            string tint = level == ToastLevel.Error ? "toast-error-tint" : "toast-tint";
            Children.Add(new ShadowLayer("shadow-m"));
            Children.Add(new SurfaceVisual(-1, "stroke-2", "btn-bevel", "solid-tint", tint));
            string fill = level == ToastLevel.Success ? "ok-fill" : level == ToastLevel.Warning ? "warn-fill" : level == ToastLevel.Error ? "error-fill" : "info-fill";
            string ink = level == ToastLevel.Success ? "ok-ink" : level == ToastLevel.Warning ? "warn-ink" : level == ToastLevel.Error ? "error-ink" : "info-ink";
            string icon = level == ToastLevel.Success ? Icons.Check : level == ToastLevel.Warning ? Icons.Warn : level == ToastLevel.Error ? Icons.Error : Icons.Info;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, actionLabel != null ? 6 : 16, 6) };
            var badge = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            badge.Children.Add(new Painted(24, 24, (dc, s) => dc.DrawEllipse(Themes.Brush(fill), null, new Point(12, 12), 12, 12)));
            FrameworkElement ic = Icons.Make(icon, 14);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            badge.SetResourceReference(TextBlock.ForegroundProperty, "T." + ink);
            badge.Children.Add(ic);
            row.Children.Add(badge);
            TextBlock t = Typography.Text(text, Typography.BodyMedium);
            t.Margin = new Thickness(9, 0, 0, 0);
            row.Children.Add(t);
            if (actionLabel != null)
            {
                var b = new GlassButton(actionLabel, null, ButtonKind.Plain, ButtonSize.XS) { Height = 26, Margin = new Thickness(13, 0, 0, 0) };
                b.Click += (s, e) => { action?.Invoke(); Close(); };
                row.Children.Add(b);
            }
            Children.Add(row);
            TimeSpan? lifetime = ToastTiming.Lifetime(level, sticky);
            if (lifetime.HasValue)
            {
                timer = new DispatcherTimer { Interval = lifetime.Value };
                timer.Tick += (s, e) => Close();
                MouseEnter += (s, e) => timer.Stop();
                MouseLeave += (s, e) => { if (IsHitTestVisible) timer.Start(); };
            }
            MouseLeftButtonUp += (s, e) => Close();
        }

        /// <summary>How long the toast stays; null for a sticky toast.</summary>
        public TimeSpan? Lifetime => timer?.Interval;

        internal void Enter()
        {
            timer?.Start();
            if (!Motion.Enabled) return;
            Opacity = 0;
            shift.Y = 16;
            Motion.To(this, OpacityProperty, 1, Motion.D3);
            Motion.To(shift, TranslateTransform.YProperty, 0, Motion.D3);
        }

        /// <summary>Restarts the timer (the same text raised again).</summary>
        internal void Renew() { if (timer == null) return; timer.Stop(); timer.Start(); }

        public void Close()
        {
            if (!IsHitTestVisible) return;
            IsHitTestVisible = false;
            timer?.Stop();
            Motion.To(shift, TranslateTransform.YProperty, 8, Motion.D2);
            Motion.To(this, OpacityProperty, 0, Motion.D2, null, () => Dismissed?.Invoke(this));
        }
    }

    /// <summary>The toast stack: bottom centre of the window, 16 px from the edge, newest at the bottom, at most three.</summary>
    public sealed class ToastHost : StackPanel
    {
        public const int Max = 3;

        public ToastHost()
        {
            VerticalAlignment = VerticalAlignment.Bottom;
            HorizontalAlignment = HorizontalAlignment.Center;
            Margin = new Thickness(16, 0, 16, 16);
        }

        /// <param name="sticky">No timer: the toast stays until it is clicked (a notice the user must not miss, such as "ready to restart").</param>
        public Toast Show(ToastLevel level, string text, string? actionLabel = null, Action? action = null, bool sticky = false)
        {
            foreach (UIElement child in Children)
                if (child is Toast same && same.Message == text && same.IsHitTestVisible) { same.Renew(); return same; }
            var toast = new Toast(level, text, actionLabel, action, sticky) { Margin = new Thickness(0, 6, 0, 0) };
            toast.Dismissed = t => Children.Remove(t);
            Children.Add(toast);
            int live = 0;
            for (int i = Children.Count - 1; i >= 0; i--)
                if (Children[i] is Toast t && t.IsHitTestVisible && ++live > Max) t.Close();
            toast.Enter();
            return toast;
        }
    }
}
