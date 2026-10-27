using System;
using System.Windows;
using System.Windows.Media;
using Retake.Core;
using Retake.Design;

namespace Retake.Views
{
    /// <summary>
    /// The note types (the core's kinds) and their key, label, glyph, colour token and the editor's placeholder. The
    /// digit keys run 1 to 6 over the note bar: 1 to 3 the corrections, 4 the general note (<see cref="GeneralKey"/>),
    /// 5 and 6 the reactions.
    /// </summary>
    public static class NoteKinds
    {
        public static readonly string[] All = { "FIX", "ADD", "REMOVE", "LIKE", "DISLIKE" };
        public const string GeneralKey = "4";

        static int Index(string kind) => Math.Max(0, Array.IndexOf(All, kind));

        public static string Token(string kind) => new[] { "note-fix", "note-add", "note-remove", "note-like", "note-dislike" }[Index(kind)];
        public static Color Color(string kind) => Themes.Color(Token(kind));

        /// <summary>The resource key of the type's ink for text and glyphs of the window (readable on the theme's surfaces); the drawings on the video and the timeline use <see cref="Color"/>.</summary>
        public static string Ink(string kind) => "T." + Token(kind) + "-ink";
        public static string Label(string kind) => new[] { Copy.FixLabel, Copy.AddLabel, Copy.RemoveLabel, Copy.LikeLabel, Copy.DislikeLabel }[Index(kind)];
        public static string Placeholder(string kind) => new[] { Copy.FixPlaceholder, Copy.AddPlaceholder, Copy.RemovePlaceholder, Copy.LikePlaceholder, Copy.DislikePlaceholder }[Index(kind)];
        public static string Tip(string kind) => new[] { Copy.FixTip, Copy.AddTip, Copy.RemoveTip, Copy.LikeTip, Copy.DislikeTip }[Index(kind)];
        public static string Glyph(string kind) => new[] { Icons.Wrench, Icons.PlusCircle, Icons.MinusCircle, Icons.ThumbUp, Icons.ThumbDown }[Index(kind)];
        public static string Key(string kind) => new[] { "1", "2", "3", "5", "6" }[Index(kind)];

        /// <summary>The type of a digit key (top row or keypad): 1 2 3 5 6; null for any other key (4 is the general note).</summary>
        public static string? OfKey(System.Windows.Input.Key key)
        {
            switch (key)
            {
                case System.Windows.Input.Key.D1: case System.Windows.Input.Key.NumPad1: return "FIX";
                case System.Windows.Input.Key.D2: case System.Windows.Input.Key.NumPad2: return "ADD";
                case System.Windows.Input.Key.D3: case System.Windows.Input.Key.NumPad3: return "REMOVE";
                case System.Windows.Input.Key.D5: case System.Windows.Input.Key.NumPad5: return "LIKE";
                case System.Windows.Input.Key.D6: case System.Windows.Input.Key.NumPad6: return "DISLIKE";
                default: return null;
            }
        }

        public static bool IsGeneralKey(System.Windows.Input.Key key) => key == System.Windows.Input.Key.D4 || key == System.Windows.Input.Key.NumPad4;
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
