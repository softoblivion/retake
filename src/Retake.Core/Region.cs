using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Retake.Core
{
    /// <summary>A rectangle or a point on the frame, in fractions (0..1) of its width and height.</summary>
    public sealed class Region : IEquatable<Region>
    {
        /// <summary>Shape value of a rectangle.</summary>
        public const string Rect = "rect";

        /// <summary>Shape value of a point.</summary>
        public const string Point = "point";

        private static readonly Regex NumberPattern = new Regex(@"\A(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\z", RegexOptions.CultureInvariant);

        /// <summary>Creates a region as given; <see cref="ToText"/>, <see cref="ToJson"/> and <see cref="Normalized"/> validate, round to three decimals and order the corners.</summary>
        public Region(string shape, double x0, double y0, double x1, double y1)
        {
            Shape = shape;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        /// <summary>A rectangle.</summary>
        public static Region CreateRect(double x0, double y0, double x1, double y1)
        {
            return new Region(Rect, x0, y0, x1, y1);
        }

        /// <summary>A point (both corners are the point).</summary>
        public static Region CreatePoint(double x, double y)
        {
            return new Region(Point, x, y, x, y);
        }

        /// <summary><c>rect</c> or <c>point</c>.</summary>
        public string Shape { get; private set; }

        public double X0 { get; private set; }
        public double Y0 { get; private set; }
        public double X1 { get; private set; }
        public double Y1 { get; private set; }

        private static double Fraction(double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number) || number < 0.0 || number > 1.0)
                throw new ArgumentException("Region values must be between 0 and 1");
            return Py.Round3(number);
        }

        private static Region Make(string shape, double x0, double y0, double x1, double y1)
        {
            if (shape == Point) return new Region(Point, x0, y0, x0, y0);
            if (shape == Rect) return new Region(Rect, Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
            throw new ArgumentException("Unknown region shape: '" + shape + "'");
        }

        /// <summary>The validated form: every value rounded to three decimals, rectangle corners ordered, a point with equal corners. Throws <see cref="ArgumentException"/> for an unknown shape or a value outside 0..1.</summary>
        public Region Normalized()
        {
            return Make(Shape, Fraction(X0), Fraction(Y0), Fraction(X1), Fraction(Y1));
        }

        /// <summary>The text of the notes file: <c>rect 0.540,0.360 0.880,0.800</c> or <c>point 0.512,0.300</c>. Throws like <see cref="Normalized"/>.</summary>
        public string ToText()
        {
            Region r = Normalized();
            if (r.Shape == Point) return "point " + Py.Fixed3(r.X0) + "," + Py.Fixed3(r.Y0);
            return "rect " + Py.Fixed3(r.X0) + "," + Py.Fixed3(r.Y0) + " " + Py.Fixed3(r.X1) + "," + Py.Fixed3(r.Y1);
        }

        /// <summary>Parses the text form. Throws <see cref="ArgumentException"/> for anything else and for values outside 0..1.</summary>
        public static Region FromText(string text)
        {
            var parts = Py.SplitWhitespace(text);
            if (parts.Count == 0) throw new ArgumentException("Empty region");
            string shape = parts[0];
            int expected = shape == Rect ? 3 : shape == Point ? 2 : 0;
            if (expected == 0 || parts.Count != expected) throw new ArgumentException("Not a region: '" + text + "'");
            var values = new double[4];
            int count = 0;
            for (int i = 1; i < parts.Count; i++)
            {
                string[] pieces = parts[i].Split(',');
                if (pieces.Length != 2 || !NumberPattern.IsMatch(pieces[0]) || !NumberPattern.IsMatch(pieces[1]))
                    throw new ArgumentException("Not a region: '" + text + "'");
                foreach (string piece in pieces)
                {
                    double number;
                    if (!double.TryParse(piece, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number))
                        throw new ArgumentException("Not a region: '" + text + "'");
                    values[count++] = Fraction(number);
                }
            }
            if (shape == Point) return Make(Point, values[0], values[1], values[0], values[1]);
            return Make(Rect, values[0], values[1], values[2], values[3]);
        }

        /// <summary>The interchange object <c>{shape, x0, y0, x1, y1}</c> of the normalised region.</summary>
        public JsonObject ToJson()
        {
            Region r = Normalized();
            var obj = new JsonObject();
            obj["shape"] = r.Shape;
            obj["x0"] = r.X0;
            obj["y0"] = r.Y0;
            obj["x1"] = r.X1;
            obj["y1"] = r.Y1;
            return obj;
        }

        private static double Number(JsonObject d, string field)
        {
            object? value;
            if (!d.TryGetValue(field, out value)) throw new ArgumentException("The region has no field '" + field + "'");
            if (value is long l) return Fraction(l);
            if (value is double x) return Fraction(x);
            throw new ArgumentException("Region values must be numbers");
        }

        /// <summary>Reads the interchange object (a <see cref="JsonObject"/>); a point needs only <c>x0</c> and <c>y0</c>. Throws <see cref="ArgumentException"/> for a non-object, an unknown shape, a missing field, a non-number or a value outside 0..1.</summary>
        public static Region FromJson(object? value)
        {
            var d = value as JsonObject;
            if (d == null) throw new ArgumentException("A region must be an object");
            object? shapeValue;
            d.TryGetValue("shape", out shapeValue);
            string? shape = shapeValue as string;
            if (shape != Rect && shape != Point) throw new ArgumentException("Unknown region shape: '" + shapeValue + "'");
            double x0 = Number(d, "x0");
            double y0 = Number(d, "y0");
            if (shape == Point) return Make(Point, x0, y0, x0, y0);
            return Make(Rect, x0, y0, Number(d, "x1"), Number(d, "y1"));
        }

        public bool Equals(Region? other)
        {
            return other != null && Shape == other.Shape && X0 == other.X0 && Y0 == other.Y0 && X1 == other.X1 && Y1 == other.Y1;
        }

        public override bool Equals(object? obj) { return Equals(obj as Region); }

        public override int GetHashCode() { return Shape.GetHashCode() ^ X0.GetHashCode() ^ (Y0.GetHashCode() * 31) ^ (X1.GetHashCode() * 17) ^ (Y1.GetHashCode() * 13); }
    }
}
