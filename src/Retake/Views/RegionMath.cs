using System;
using Retake.Core;

namespace Retake.Views
{
    /// <summary>A rectangle in logical (device-independent, unscaled) pixels.</summary>
    public struct Box
    {
        public double X, Y, W, H;
        public Box(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
        public double Right => X + W;
        public double Bottom => Y + H;
        public bool IsEmpty => W <= 0 || H <= 0;
        public override string ToString() => X + "," + Y + " " + W + "x" + H;
    }

    /// <summary>
    /// Geometry of the region tool. The video area is laid out in logical pixels of the window (before the Scale
    /// step and the Windows DPI); the player letterboxes the frame into it (contain, centred), so the region tool
    /// works on the same frame box and stores regions in fractions of the frame. A drag shorter than
    /// <see cref="DragPx"/> on screen is a click and puts a point. Pure functions.
    /// </summary>
    public static class RegionMath
    {
        public const double DragPx = 4;
        /// <summary>Stroke of a rectangle and of a point ring, logical pixels on screen.</summary>
        public const double Stroke = 2;
        public const double PointRadius = 8, PointTail = 12, PointDot = 2;

        /// <summary>The frame box of a video of vw x vh fitted into an area (contain, centred).</summary>
        public static Box Fit(double areaW, double areaH, int vw, int vh)
        {
            if (areaW <= 0 || areaH <= 0 || vw <= 0 || vh <= 0) return new Box();
            double s = Math.Min(areaW / vw, areaH / vh);
            double w = vw * s, h = vh * s;
            return new Box((areaW - w) / 2, (areaH - h) / 2, w, h);
        }

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>A point of the area as fractions of the frame, clamped to the frame.</summary>
        public static void ToFraction(double x, double y, Box frame, out double fx, out double fy)
        {
            fx = frame.W > 0 ? Clamp01((x - frame.X) / frame.W) : 0;
            fy = frame.H > 0 ? Clamp01((y - frame.Y) / frame.H) : 0;
        }

        /// <summary>
        /// The region of a drag from (x0, y0) to (x1, y1) in area pixels: a rectangle when the pointer moved at
        /// least <see cref="DragPx"/> and the rectangle has an extent after rounding, else a point at the start.
        /// </summary>
        public static Region FromDrag(double x0, double y0, double x1, double y1, Box frame)
        {
            ToFraction(x0, y0, frame, out double a, out double b);
            if (Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) >= DragPx)
            {
                ToFraction(x1, y1, frame, out double c, out double d);
                Region r = Region.CreateRect(Math.Min(a, c), Math.Min(b, d), Math.Max(a, c), Math.Max(b, d)).Normalized();
                if (r.X0 != r.X1 && r.Y0 != r.Y1) return r;
            }
            return Region.CreatePoint(a, b).Normalized();
        }

        /// <summary>The bounding box of a region inside a frame box (same units as the frame box).</summary>
        public static Box RegionBox(Region r, Box frame)
        {
            double x0 = frame.X + r.X0 * frame.W, y0 = frame.Y + r.Y0 * frame.H;
            double x1 = frame.X + r.X1 * frame.W, y1 = frame.Y + r.Y1 * frame.H;
            return new Box(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>
        /// The box a region takes on screen including the point marker's ring and tail (the tail goes up and to
        /// the right), so the editor can stand clear of it.
        /// </summary>
        public static Box Extent(Region r, Box frame)
        {
            Box b = RegionBox(r, frame);
            if (r.Shape != Region.Point) return b;
            double ext = PointRadius + PointTail;
            return new Box(b.X - PointRadius, b.Y - ext, PointRadius + ext, PointRadius + ext);
        }

        /// <summary>
        /// Where the note editor goes: right of the region, else left, else below, else above; with no region,
        /// centred above the bottom of the frame and the hint line. Clamped into the area with an 8 px margin.
        /// </summary>
        public static void PlaceEditor(Region? region, Box frame, double areaW, double areaH, double editorW, double editorH,
            double hintH, out double left, out double top)
        {
            const double gap = 12, m = 8;
            if (region != null && !frame.IsEmpty)
            {
                Box b = Extent(region, frame);
                if (b.Right + gap + editorW <= areaW - m) { left = b.Right + gap; top = b.Y; }
                else if (b.X - gap - editorW >= m) { left = b.X - gap - editorW; top = b.Y; }
                else if (b.Bottom + gap + editorH <= areaH - hintH - m) { left = b.X; top = b.Bottom + gap; }
                else { left = b.X; top = b.Y - gap - editorH; }
            }
            else
            {
                left = (areaW - editorW) / 2;
                top = (frame.IsEmpty ? areaH : frame.Bottom) - editorH - hintH - 16;
            }
            left = Math.Max(m, Math.Min(Math.Max(m, areaW - editorW - m), left));
            top = Math.Max(m, Math.Min(Math.Max(m, areaH - editorH - hintH - m), top));
        }

        /// <summary>Native pixels per logical pixel of the frame on screen (the factor of the saved image's strokes).</summary>
        public static double CaptureScale(int nativeWidth, Box frame) => frame.W > 0 ? nativeWidth / frame.W : 1;

        /// <summary>A logical coordinate in device pixels for a Scale step (percent) and a monitor DPI scale.</summary>
        public static double ToDevice(double logical, int scalePercent, double dpiScale) => logical * scalePercent / 100.0 * dpiScale;

        /// <summary>A device-pixel coordinate back in logical pixels.</summary>
        public static double FromDevice(double device, int scalePercent, double dpiScale) => device / (scalePercent / 100.0 * dpiScale);
    }
}
