using System;
using System.Windows;
using System.Windows.Media;
using Retake.Core;
using Retake.Design;

namespace Retake.Views
{
    /// <summary>The three note types: key, label, colour token, placeholder of the editor.</summary>
    public static class NoteKinds
    {
        public static readonly string[] All = { "FIX", "ADD", "REMOVE" };

        public static string Token(string kind) => kind == "ADD" ? "note-add" : kind == "REMOVE" ? "note-remove" : "note-fix";
        public static Color Color(string kind) => Themes.Color(Token(kind));
        public static string Label(string kind) => kind == "ADD" ? Copy.AddLabel : kind == "REMOVE" ? Copy.RemoveLabel : Copy.FixLabel;
        public static string Placeholder(string kind) => kind == "ADD" ? Copy.AddPlaceholder : kind == "REMOVE" ? Copy.RemovePlaceholder : Copy.FixPlaceholder;
        public static string Key(string kind) => kind == "ADD" ? "2" : kind == "REMOVE" ? "3" : "1";
    }

    /// <summary>
    /// Draws a region the same way on screen (the region tool, scale 1 in logical pixels) and on the saved frame
    /// (native pixels, scale = native / shown width): a rectangle is a 2 px line in the note colour with the frame
    /// outside it dimmed by --note-dim; a point is an 8 px ring with a 12 px tail to the upper right and a centre
    /// dot, over a darker halo (--note-halo) so it reads on any picture.
    /// </summary>
    public static class RegionPainter
    {
        /// <summary>The frozen brushes of a note type in the current theme (usable on any thread).</summary>
        public sealed class Ink
        {
            public readonly Brush Line, Dim, Halo;

            public Ink(string kind)
            {
                Line = Freeze(new SolidColorBrush(NoteKinds.Color(kind)));
                Dim = Freeze(new SolidColorBrush(Themes.Color("note-dim")));
                Halo = Freeze(new SolidColorBrush(Themes.Color("note-halo")));
            }

            static Brush Freeze(Brush b) { b.Freeze(); return b; }
        }

        public static void Draw(DrawingContext dc, Region r, Ink paint, Box frame, double scale)
        {
            Brush ink = paint.Line;
            Box b = RegionMath.RegionBox(r, frame);
            if (r.Shape == Region.Rect)
            {
                var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(frame.X, frame.Y, frame.W, frame.H)),
                    new RectangleGeometry(new Rect(b.X, b.Y, b.W, b.H)));
                dc.DrawGeometry(paint.Dim, null, outside);
                var pen = new Pen(ink, RegionMath.Stroke * scale);
                pen.Freeze();
                double w = Math.Max(scale, b.W), h = Math.Max(scale, b.H);
                dc.DrawRectangle(null, pen, new Rect(b.X, b.Y, w, h));
                return;
            }
            double rad = RegionMath.PointRadius * scale, k = Math.Sqrt(0.5), tail = RegionMath.PointTail * scale;
            var c = new Point(b.X, b.Y);
            var t1 = new Point(c.X + rad * k, c.Y - rad * k);
            var t2 = new Point(c.X + (rad + tail) * k, c.Y - (rad + tail) * k);
            var halo = new Pen(paint.Halo, (RegionMath.Stroke + 2) * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var line = new Pen(ink, RegionMath.Stroke * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            halo.Freeze();
            line.Freeze();
            foreach (Pen p in new[] { halo, line })
            {
                dc.DrawEllipse(null, p, c, rad, rad);
                dc.DrawLine(p, t1, t2);
            }
            dc.DrawEllipse(ink, null, c, RegionMath.PointDot * scale, RegionMath.PointDot * scale);
        }
    }
}
