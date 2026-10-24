using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Retake.Core;

namespace Retake.Views
{
    /// <summary>
    /// The region tool over the video (it lives in the overlay window): it shows a region in its note colour and,
    /// in drawing mode, turns the pointer into a crosshair over the whole video area. A drag draws a rectangle
    /// live (clamped to the frame, so it may start in the letterbox); a click puts a point; each finished drag
    /// replaces the region (one per note). Outside drawing mode it takes no clicks.
    /// </summary>
    public sealed class RegionTool : FrameworkElement
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        static readonly Brush Catcher = MakeCatcher();
        Box frame;
        Region? shown;
        string kind = "FIX";
        RegionPainter.Ink ink = new RegionPainter.Ink("FIX");
        bool drawing;
        Point? start;
        Region? live;

        /// <summary>A finished drag or click, in fractions of the frame.</summary>
        public event Action<Region>? Drawn;

        public RegionTool()
        {
            AutomationProperties.SetAutomationId(this, "RegionTool");
            Focusable = false;
        }

        static Brush MakeCatcher()
        {
            // Alpha 1 / 255: invisible, but a layered window takes the pointer only where a pixel is not fully clear.
            var b = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            b.Freeze();
            return b;
        }

        public Box Frame { get => frame; set { frame = value; InvalidateVisual(); } }
        public bool Drawing => drawing;
        public Region? Shown => shown;

        /// <summary>Shows a region (or nothing) in the colour of a note type.</summary>
        public void Show(Region? region, string noteKind)
        {
            shown = region;
            if (noteKind != kind) { kind = noteKind; ink = new RegionPainter.Ink(kind); }
            AutomationProperties.SetItemStatus(this, region == null ? "" : region.ToText());
            InvalidateVisual();
        }

        /// <summary>Re-reads the colours after a theme change.</summary>
        public void Retint() { ink = new RegionPainter.Ink(kind); InvalidateVisual(); }

        public void SetDrawing(bool on)
        {
            drawing = on;
            start = null;
            live = null;
            Cursor = on ? Cursors.Cross : null;
            IsHitTestVisible = on;
            if (!on && IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (drawing) dc.DrawRectangle(Catcher, null, new Rect(RenderSize));
            Region? r = live ?? shown;
            if (r == null || frame.IsEmpty) return;
            dc.PushClip(new RectangleGeometry(new Rect(frame.X, frame.Y, frame.W, frame.H)));
            RegionPainter.Draw(dc, r, ink, frame, 1);
            dc.Pop();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!drawing || frame.IsEmpty) return;
            start = e.GetPosition(this);
            live = null;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (start == null) return;
            Point p = e.GetPosition(this);
            Region r = RegionMath.FromDrag(start.Value.X, start.Value.Y, p.X, p.Y, frame);
            live = r.Shape == Region.Rect ? r : null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (start == null) return;
            Point p = e.GetPosition(this);
            Region r = RegionMath.FromDrag(start.Value.X, start.Value.Y, p.X, p.Y, frame);
            start = null;
            live = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            shown = r;
            InvalidateVisual();
            e.Handled = true;
            Drawn?.Invoke(r);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (start != null) { start = null; live = null; InvalidateVisual(); }
        }
    }
}
