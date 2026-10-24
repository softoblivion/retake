using System;
using System.Collections.Generic;

namespace Retake.Views
{
    /// <summary>The visible part of the timeline in frames: from <see cref="Start"/>, <see cref="Span"/> frames wide.</summary>
    public struct TimelineView
    {
        public double Start, Span;
        public TimelineView(double start, double span) { Start = start; Span = span; }
        public double End => Start + Span;
    }

    /// <summary>One frame that carries notes, with the screen position of its stem and what its tags can show.</summary>
    public sealed class MarkerSlot
    {
        public int Frame;
        public double X;
        /// <summary>Room for the labels: up to the next stem, minus a 6 px gap.</summary>
        public double Room;
        /// <summary>False when the next marker is closer than <see cref="TimelineMath.MinLabelPx"/>: the tags shrink to chips.</summary>
        public bool ShowLabels;
        /// <summary>Indexes (into the note list) of the notes on this frame, in list order.</summary>
        public readonly List<int> Notes = new List<int>();
    }

    /// <summary>
    /// Timeline rules, unit = frames. Ruler step: the smallest of 1, 2, 5, 10 frames and 1, 2, 5, 10, 30 s,
    /// 1, 2, 5, 10, 30, 60 min (multiples of the timecode base) whose spacing fits a label plus 22 px; minor ticks
    /// at a fifth or a half of the step when they are at least 6 px apart. Zoom keeps the frame under the pointer
    /// in place and never shows fewer than 12 frames. Markers share a stem per frame; a frame closer than 30 px to
    /// the next one shows colour chips instead of labels (collision hiding); at most three tags per stem, the
    /// third says how many more. Pure functions.
    /// </summary>
    public static class TimelineMath
    {
        public const double MinSpanFrames = 12, LabelPad = 22, MinorPx = 6, MinLabelPx = 30, LabelGap = 6;
        public const int MaxRows = 3, LabelWords = 6;

        /// <summary>The ruler step in frames for a zoom (pixels per frame) and a label width.</summary>
        public static int PickStep(double pxPerFrame, double labelWidth, int baseRate)
        {
            int b = Math.Max(1, baseRate);
            int[] steps = { 1, 2, 5, 10, b, 2 * b, 5 * b, 10 * b, 30 * b, 60 * b, 120 * b, 300 * b, 600 * b, 1800 * b, 3600 * b };
            double min = labelWidth + LabelPad;
            foreach (int s in steps) if (s * pxPerFrame >= min) return s;
            return steps[steps.Length - 1];
        }

        /// <summary>The minor tick step for a ruler step, or 0 when there is none or it would be closer than 6 px.</summary>
        public static int MinorStep(int step, double pxPerFrame)
        {
            int sub = step % 5 == 0 ? step / 5 : step % 2 == 0 ? step / 2 : 0;
            return sub > 0 && sub * pxPerFrame >= MinorPx ? sub : 0;
        }

        /// <summary>A view clamped to the video: at least <see cref="MinSpanFrames"/> (or the whole video when shorter), inside 0 .. total.</summary>
        public static TimelineView Clamp(TimelineView v, double total)
        {
            if (total <= 0) return new TimelineView(0, 0);
            double minSpan = Math.Min(total, MinSpanFrames);
            double span = Math.Max(minSpan, Math.Min(total, v.Span));
            double start = Math.Max(0, Math.Min(total - span, v.Start));
            return new TimelineView(start, span);
        }

        /// <summary>Zooms by a factor (below 1 = in) keeping <paramref name="anchor"/> (a frame position) in place.</summary>
        public static TimelineView ZoomAt(TimelineView v, double anchor, double factor, double total)
        {
            double span = Math.Max(Math.Min(total, MinSpanFrames), Math.Min(total, v.Span * factor));
            double start = anchor - (anchor - v.Start) * (span / v.Span);
            return Clamp(new TimelineView(start, span), total);
        }

        /// <summary>Wheel zoom factor of the 1.x timeline: exp(delta * 0.0015) with a wheel notch of 120 inverted (up = in).</summary>
        public static double WheelFactor(int wheelDelta) => Math.Exp(-wheelDelta * 0.0015);

        /// <summary>Pans by a fraction of the span (a wheel notch moves 12 %).</summary>
        public static TimelineView Pan(TimelineView v, double fraction, double total) => Clamp(new TimelineView(v.Start + fraction * v.Span, v.Span), total);

        /// <summary>A view that shows <paramref name="frame"/>: unchanged when visible; else paged (playing) or centred.</summary>
        public static TimelineView Follow(TimelineView v, double frame, bool playing, double total)
        {
            if (frame >= v.Start && frame <= v.End) return v;
            return Clamp(new TimelineView(playing ? frame - v.Span * 0.05 : frame - v.Span / 2, v.Span), total);
        }

        public static double X(double frame, TimelineView v, double width) => v.Span <= 0 ? 0 : (frame - v.Start) / v.Span * width;
        public static double FrameAtX(double x, TimelineView v, double width) => width <= 0 ? v.Start : v.Start + x / width * v.Span;

        /// <summary>
        /// Groups notes (sorted by frame) into stems and decides the label room of each; frames outside the view
        /// (with a 2 px margin) are left out.
        /// </summary>
        public static List<MarkerSlot> Layout(IList<int> noteFrames, TimelineView v, double width)
        {
            var groups = new List<MarkerSlot>();
            for (int i = 0; i < noteFrames.Count; i++)
            {
                MarkerSlot? last = groups.Count > 0 ? groups[groups.Count - 1] : null;
                if (last != null && last.Frame == noteFrames[i]) last.Notes.Add(i);
                else
                {
                    var g = new MarkerSlot { Frame = noteFrames[i], X = X(noteFrames[i], v, width) };
                    g.Notes.Add(i);
                    groups.Add(g);
                }
            }
            var shown = new List<MarkerSlot>();
            for (int i = 0; i < groups.Count; i++)
            {
                MarkerSlot g = groups[i];
                double next = i + 1 < groups.Count ? groups[i + 1].X : width + 400;
                if (next < -2 || g.X > width + 2) continue;
                g.Room = Math.Max(0, next - g.X - LabelGap);
                g.ShowLabels = g.Room >= MinLabelPx;
                shown.Add(g);
            }
            return shown;
        }

        /// <summary>The first words of a note for its tag (an ellipsis when cut); empty for an empty text.</summary>
        public static string ShortText(string? text, int words = LabelWords)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string[] parts = text!.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= words) return string.Join(" ", parts);
            return string.Join(" ", parts, 0, words) + "…";
        }
    }
}
