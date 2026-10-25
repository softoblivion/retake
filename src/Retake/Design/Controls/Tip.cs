using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Retake.Design.Controls
{
    /// <summary>The element of a window that hosts tooltips, menus, hover cards and sheets above the content.</summary>
    public interface IOverlayRoot
    {
        /// <summary>Topmost layer of the window's own tree; the fallback for tooltips where there is no float window.</summary>
        Canvas TipLayer { get; }
        OverlayHost Overlay { get; }
        /// <summary>The float window for tooltips or hover cards (above the native video window), or null without a window.</summary>
        FloatWindow? Float(FloatKind kind);
    }

    /// <summary>
    /// The kit-look tooltip: a near-opaque capsule (--menu-tint, hairline, --shadow-s) with one line in the
    /// caption role; a trailing key in brackets is drawn in a quieter ink ("Settings (Ctrl+,)").
    /// </summary>
    public sealed class TipBubble : Grid
    {
        readonly Border textHost = new Border { Padding = new Thickness(9, 5, 9, 5) };

        public TipBubble(string text)
        {
            IsHitTestVisible = false;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Children.Add(new ShadowLayer("shadow-s", 8));
            Children.Add(new SurfaceVisual(8, "stroke-2", null, "menu-tint"));
            Children.Add(textHost);
            Text = text;
        }

        public string Text
        {
            set
            {
                TextBlock t = Typography.KeyLine(value, Typography.Caption, "ink-1", "ink-3");
                AutomationProperties.SetAutomationId(t, "Tooltip");
                textHost.Child = t;
            }
        }
    }

    /// <summary>
    /// Tooltips of the layer. <see cref="Set"/> gives an element its tooltip text: shown after 400 ms of hover
    /// (at once when another tooltip closed less than 300 ms ago), centred below the element or above it when
    /// there is no room, hidden on leave or press. The text is also the element's automation help text. The
    /// bubble is drawn in the window's float window (above the native video window, so nothing under it has to
    /// change), scaled like the root, so it follows the Scale setting; without one, in the root's tooltip layer.
    /// Rule: one line that says what happens, with the key in brackets when one exists; never the visible label.
    /// </summary>
    public static class Tip
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Tip), new PropertyMetadata(null, OnText));
        static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(Tip), new PropertyMetadata(false));

        public const int DelayMs = 400;
        static readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DelayMs) };
        static FrameworkElement? pending, shownFor;
        static TipBubble? bubble;
        static FloatWindow? bubbleWindow;
        static DateTime lastHidden = DateTime.MinValue;

        static Tip()
        {
            timer.Tick += (s, e) => { timer.Stop(); if (pending != null) Show(pending); };
        }

        public static void Set(UIElement element, string? text) => element.SetValue(TextProperty, text);
        public static string? Get(UIElement element) => (string?)element.GetValue(TextProperty);

        static void OnText(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement el)) return;
            string? text = (string?)e.NewValue;
            AutomationProperties.SetHelpText(el, text ?? "");
            if (!(bool)el.GetValue(HookedProperty))
            {
                el.SetValue(HookedProperty, true);
                el.MouseEnter += Enter;
                el.MouseLeave += Leave;
                el.PreviewMouseDown += Down;
            }
            if (shownFor == el && bubble != null)
            {
                if (text == null) Hide(); else bubble.Text = text;
            }
        }

        static void Enter(object sender, MouseEventArgs e)
        {
            var el = (FrameworkElement)sender;
            if (Get(el) == null) return;
            pending = el;
            if (shownFor == null && (DateTime.UtcNow - lastHidden).TotalMilliseconds < 300) { timer.Stop(); Show(el); }
            else { timer.Stop(); timer.Start(); }
        }

        static void Leave(object sender, MouseEventArgs e)
        {
            if (pending == sender) { timer.Stop(); pending = null; }
            if (shownFor == sender) Hide();
        }

        static void Down(object sender, MouseButtonEventArgs e)
        {
            timer.Stop();
            pending = null;
            Hide();
        }

        /// <summary>Finds the overlay root of an element by walking up the visual tree.</summary>
        public static IOverlayRoot? RootOf(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is IOverlayRoot r) return r;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        static void Show(FrameworkElement el)
        {
            string? text = Get(el);
            IOverlayRoot? root = RootOf(el);
            if (text == null || root == null || !el.IsVisible || !(root is FrameworkElement rootElement)) return;
            Hide();
            bubble = new TipBubble(text);
            shownFor = el;
            bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Rect at = el.TransformToVisual(rootElement).TransformBounds(new Rect(el.RenderSize));
            Point p = FloatPlacement.Below(at, bubble.DesiredSize, new Size(rootElement.ActualWidth, rootElement.ActualHeight), out bool flipped);
            bubbleWindow = root.Float(FloatKind.Tip);
            if (bubbleWindow != null) bubbleWindow.ShowAt(bubble, p, rootElement);
            else
            {
                root.TipLayer.Children.Add(bubble);
                Canvas.SetLeft(bubble, p.X);
                Canvas.SetTop(bubble, p.Y);
            }
            var shift = new TranslateTransform(0, flipped ? -4 : 4);
            bubble.RenderTransform = shift;
            bubble.Opacity = 0;
            Motion.To(bubble, UIElement.OpacityProperty, 1, Motion.D1);
            Motion.To(shift, TranslateTransform.YProperty, 0, Motion.D2);
        }

        static void Hide()
        {
            if (bubble == null) return;
            TipBubble gone = bubble;
            bubble = null;
            shownFor = null;
            lastHidden = DateTime.UtcNow;
            if (gone.Parent is Panel p) p.Children.Remove(gone);
            bubbleWindow?.HideIf(gone);
            bubbleWindow = null;
        }

        /// <summary>The tooltip currently shown, for checks; null when none.</summary>
        public static string? Visible => shownFor == null ? null : Get(shownFor);
    }
}
