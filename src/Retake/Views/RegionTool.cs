using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using Retake.Core;

namespace Retake.Views
{
    /// <summary>
    /// The region tool over the video (it lives in the overlay window): it shows a region in its note colour and
    /// takes the pointer in two modes. In drawing mode (a note is written) the pointer is a crosshair over the whole
    /// video area; a drag draws a rectangle live (clamped to the frame, so it may start in the letterbox), a click puts
    /// a point, and each finished drag replaces the region (one per note). In free mode (no note is written) a drag
    /// marks a rectangle for a new note: <see cref="DragStarted"/> once the pointer has moved
    /// <see cref="RegionMath.DragPx"/>, <see cref="Marked"/> at its end; a click marks nothing. In neither mode it
    /// takes no clicks. UI Automation reads the region as its value, and setting the value (the notes file's region
    /// text) acts as a finished drag does in either mode.
    /// </summary>
    public sealed class RegionTool : FrameworkElement
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new RegionPeer(this);

        static readonly Brush Catcher = MakeCatcher();
        Box frame;
        Region? shown;
        string kind = "FIX";
        RegionPainter.Ink ink = new RegionPainter.Ink("FIX");
        bool drawing, free, dragged;
        Point? start;
        Region? live;

        /// <summary>A finished drag or click in drawing mode, in fractions of the frame.</summary>
        public event Action<Region>? Drawn;
        /// <summary>A drag in free mode has begun (the pointer moved past the click distance).</summary>
        public event Action? DragStarted;
        /// <summary>A finished drag in free mode: a rectangle, in fractions of the frame.</summary>
        public event Action<Region>? Marked;

        public RegionTool()
        {
            AutomationProperties.SetAutomationId(this, "RegionTool");
            Focusable = false;
            IsHitTestVisible = false;
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
        public bool Free => free;
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
            Reset();
        }

        /// <summary>Free mode: a drag marks a region for a new note (outside drawing mode).</summary>
        public void SetFree(bool on)
        {
            free = on;
            Reset();
        }

        void Reset()
        {
            start = null;
            live = null;
            dragged = false;
            Cursor = drawing ? Cursors.Cross : null;
            IsHitTestVisible = drawing || free;
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
        }

        bool FreeOnly => free && !drawing;

        protected override void OnRender(DrawingContext dc)
        {
            if (drawing || free) dc.DrawRectangle(Catcher, null, new Rect(RenderSize));
            Region? r = live ?? shown;
            if (r == null || frame.IsEmpty) return;
            dc.PushClip(new RectangleGeometry(new Rect(frame.X, frame.Y, frame.W, frame.H)));
            RegionPainter.Draw(dc, r, ink, frame, 1);
            dc.Pop();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if ((!drawing && !free) || frame.IsEmpty) return;
            start = e.GetPosition(this);
            live = null;
            dragged = false;
            CaptureMouse();
            // A press in free mode stays unhandled: the video's own click handling still sees it.
            if (drawing) e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (start == null) return;
            Point p = e.GetPosition(this);
            Region r = RegionMath.FromDrag(start.Value.X, start.Value.Y, p.X, p.Y, frame);
            live = r.Shape == Region.Rect ? r : null;
            if (FreeOnly && live != null && !dragged)
            {
                dragged = true;
                Cursor = Cursors.Cross;
                DragStarted?.Invoke();
            }
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
            dragged = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (FreeOnly)
            {
                Cursor = null;
                InvalidateVisual();
                // A click marks nothing outside a note.
                if (r.Shape != Region.Rect) return;
                e.Handled = true;
                Mark(r);
                return;
            }
            e.Handled = true;
            Mark(r);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (start != null)
            {
                start = null;
                live = null;
                dragged = false;
                Cursor = drawing ? Cursors.Cross : null;
                InvalidateVisual();
            }
        }

        /// <summary>A region from a drag or from UI Automation: drawn in drawing mode, marked in free mode.</summary>
        void Mark(Region r)
        {
            shown = r;
            InvalidateVisual();
            if (drawing) Drawn?.Invoke(r);
            else if (free) Marked?.Invoke(r);
        }

        /// <summary>The automation view: a pane whose value is the region text; setting it marks or draws that region.</summary>
        sealed class RegionPeer : FrameworkElementAutomationPeer, IValueProvider
        {
            public RegionPeer(RegionTool owner) : base(owner) { }

            RegionTool Tool => (RegionTool)Owner;
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
            protected override string GetClassNameCore() => nameof(RegionTool);
            protected override bool IsControlElementCore() => true;
            public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Value ? this : base.GetPattern(pattern);

            public bool IsReadOnly => !Tool.drawing && !Tool.free;
            public string Value => Tool.shown == null ? "" : Tool.shown.ToText();

            public void SetValue(string value)
            {
                if (IsReadOnly) throw new ElementNotEnabledException();
                Region r;
                try { r = Region.FromText(value); }
                catch (ArgumentException ex) { throw new ArgumentException(ex.Message, nameof(value), ex); }
                Tool.Dispatcher.BeginInvoke(new Action(() => Tool.Mark(r)));
            }
        }
    }
}
