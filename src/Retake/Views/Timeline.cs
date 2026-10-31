using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Retake.Core;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>
    /// The mini timeline across the window: a ruler with timecodes at a step chosen from the zoom, the playhead,
    /// one stem per noted frame with a tag per note (the first words of its text in the note colour; chips when
    /// the next stem is too close; three tags at most, the third counts the rest), a scroll bar while zoomed.
    /// General notes have no frame and no tag. Wheel zooms around the pointer, Shift + wheel pans, 0 resets (the
    /// window's key); a press in the ruler or the empty track scrubs; a click on a tag selects the note, a double
    /// click edits it, a drag moves it to another frame (the playhead follows, so the video shows where it lands;
    /// Esc or a lost capture cancels); a right click opens the menu of the tag or of the frame under the pointer;
    /// resting on a tag shows a hover card with the timecode, the type, the whole text and the frame image.
    /// Everything is drawn here; the ruler, track and scroll strips are transparent children that take the
    /// pointer and carry the tooltips.
    /// </summary>
    public sealed class Timeline : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        const double RulerH = 24, RowH = 20, RowTop = 5, TagH = 17, ScrollH = 8, Radius = 10, DragStart = 4;
        static readonly double TrackH = RowTop + TimelineMath.MaxRows * RowH + 3;

        readonly Border ruler = new Border { Background = Brushes.Transparent, Height = RulerH, VerticalAlignment = VerticalAlignment.Top };
        readonly Border track = new Border { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, RulerH, 0, 0), Height = TrackH };
        readonly Border scroll = new Border { Background = Brushes.Transparent, Height = ScrollH, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
        readonly DispatcherTimer cardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Tip.DelayMs) };

        IList<NoteState> notes = new List<NoteState>();
        string? selectedId;
        double total;            // frames in the video (last + 1)
        TimecodeBase? tb;
        double rate = 25;
        int playhead;
        bool playing;
        TimelineView view;
        readonly List<KeyValuePair<Rect, int>> tagRects = new List<KeyValuePair<Rect, int>>();
        int hoverIndex = -1;
        bool scrubbing, thumbDrag, moving;
        double thumbGrab, pressX, grabOffset;
        int pressTag = -1, moveFrame;
        Rect thumbRect, scrollRect;

        /// <summary>A scrub or a click on the ruler: the frame under the pointer.</summary>
        public event Action<int>? Scrub;
        public event Action<NoteState>? Pick, Edit;
        public event Action<bool>? Scrubbing;
        /// <summary>A tag is being dragged: the note and the frame under the pointer.</summary>
        public event Action<NoteState, int>? Moving;
        /// <summary>A drag ended on another frame: the note and its new frame.</summary>
        public event Action<NoteState, int>? Moved;
        /// <summary>A drag was cancelled (Esc, a lost capture, a release on the same frame): the note stays.</summary>
        public event Action<NoteState>? MoveCancelled;
        /// <summary>A right click on a tag: the note and the point in the timeline.</summary>
        public event Action<NoteState, Point>? TagMenu;
        /// <summary>A right click on the ruler or the empty track: the frame under the pointer and the point.</summary>
        public event Action<int, Point>? TrackMenu;

        public Timeline()
        {
            Height = RulerH + TrackH + ScrollH + 2;
            ClipToBounds = true;
            Background = Brushes.Transparent;
            AutomationProperties.SetAutomationId(this, "Timeline");
            AutomationProperties.SetName(this, "Timeline");
            Children.Add(ruler);
            Children.Add(track);
            Children.Add(scroll);
            Tip.Set(ruler, Copy.RulerTip);
            Tip.Set(scroll, Copy.ScrollTip);
            foreach (Border part in new[] { ruler, track, scroll })
            {
                part.MouseLeftButtonDown += OnDown;
                part.MouseMove += OnMove;
                part.MouseLeftButtonUp += OnUp;
                part.LostMouseCapture += (s, e) => EndDrag();
                part.MouseWheel += OnWheel;
                part.MouseRightButtonUp += OnRightUp;
            }
            track.MouseLeave += (s, e) => SetHover(-1);
            cardTimer.Tick += (s, e) => { cardTimer.Stop(); ShowCard(); };
            Themes.Changed += InvalidateVisual;
            SizeChanged += (s, e) => InvalidateVisual();
        }

        public TimelineView View => view;
        public bool IsScrubbing => scrubbing;
        public bool IsMoving => moving;

        /// <summary>A new video (or none: total 0).</summary>
        public void SetVideo(int frames, double fps, TimecodeBase? timecode)
        {
            total = Math.Max(0, frames);
            rate = fps > 0 ? fps : 25;
            tb = timecode;
            view = new TimelineView(0, total);
            Publish();
            InvalidateVisual();
        }

        /// <summary>The rate or the timecode rules changed; the zoom stays.</summary>
        public void SetRate(int frames, double fps, TimecodeBase? timecode)
        {
            total = Math.Max(0, frames);
            rate = fps > 0 ? fps : 25;
            tb = timecode;
            view = TimelineMath.Clamp(view, total);
            Publish();
            InvalidateVisual();
        }

        /// <summary>The notes to draw; general notes (no frame) are left out.</summary>
        public void SetNotes(IList<NoteState> list, string? selected)
        {
            CancelMove();
            notes = list.Where(n => n.Frame.HasValue).ToList();
            selectedId = selected;
            SetHover(-1);
            InvalidateVisual();
        }

        public void SetSelected(string? id)
        {
            selectedId = id;
            InvalidateVisual();
        }

        public void SetPlayhead(int frame, bool isPlaying)
        {
            playhead = frame;
            playing = isPlaying;
            if (!scrubbing && total > 0)
            {
                TimelineView next = TimelineMath.Follow(view, frame, playing, total);
                if (next.Start != view.Start || next.Span != view.Span) { view = next; Publish(); }
            }
            InvalidateVisual();
        }

        public void ResetZoom()
        {
            view = new TimelineView(0, total);
            Publish();
            InvalidateVisual();
        }

        void Publish() => AutomationProperties.SetItemStatus(this, view.Start.ToString("0.###", CultureInfo.InvariantCulture) + " " + view.Span.ToString("0.###", CultureInfo.InvariantCulture));

        // --- drawing ------------------------------------------------------------------------------------------

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            if (w <= 0) return;
            var trackRect = new Rect(0, RulerH, w, TrackH);
            dc.DrawRoundedRectangle(Themes.Brush("fill-1"), null, new Rect(0, 0, w, RulerH + TrackH), Radius, Radius);
            tagRects.Clear();
            scrollRect = thumbRect = Rect.Empty;
            if (total <= 0 || view.Span <= 0) return;
            DrawRuler(dc, w);
            DrawMarkers(dc, w, trackRect);
            DrawPlayhead(dc, w);
            DrawScroll(dc, w);
        }

        FormattedText Label(string text, Brush ink) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Typography.MonoS.Family, FontStyles.Normal, Typography.MonoS.Weight, FontStretches.Normal),
                Typography.MonoS.Size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        string Format(int frame) => tb != null ? Timecode.FrameToTimecode(Math.Max(0, frame), tb) : Clock(frame / rate);

        static string Clock(double seconds)
        {
            int ms = (int)Math.Round(seconds * 1000);
            int m = ms / 60000, s = ms / 1000 % 60, f = ms % 1000;
            return m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture) + "." + f.ToString("000", CultureInfo.InvariantCulture);
        }

        void DrawRuler(DrawingContext dc, double w)
        {
            Brush ink = Themes.Brush("ink-3");
            var major = new Pen(Themes.Brush("stroke-2"), 1);
            var minor = new Pen(Themes.Brush("line-soft"), 1);
            double pxPerFrame = w / view.Span;
            double labelW = Label(Format((int)Math.Ceiling(view.End)), ink).Width;
            int baseRate = tb?.Base ?? (int)Math.Round(rate);
            int step = TimelineMath.PickStep(pxPerFrame, labelW, baseRate);
            int sub = TimelineMath.MinorStep(step, pxPerFrame);
            long f0 = (long)Math.Floor(view.Start / step) * step, f1 = (long)Math.Ceiling(view.End / step) * step;
            if (sub > 0)
                for (long f = f0; f <= f1; f += sub)
                {
                    if (f % step == 0) continue;
                    double x = Math.Round(TimelineMath.X(f, view, w)) + 0.5;
                    dc.DrawLine(minor, new Point(x, RulerH - 5), new Point(x, RulerH));
                }
            for (long f = f0; f <= f1; f += step)
            {
                double x = Math.Round(TimelineMath.X(f, view, w)) + 0.5;
                if (x < -200 || x > w + 10) continue;
                dc.DrawLine(major, new Point(x, RulerH - 9), new Point(x, RulerH));
                if (f < total)
                {
                    FormattedText t = Label(Format((int)f), ink);
                    dc.DrawText(t, new Point(Math.Round(x) + 4, (RulerH - t.Height) / 2 - 2));
                }
            }
        }

        void DrawMarkers(DrawingContext dc, double w, Rect trackRect)
        {
            // The order of the stems; a note being dragged takes the frame under the pointer.
            int[] order = Enumerable.Range(0, notes.Count).OrderBy(i => FrameOf(i)).ThenBy(i => i).ToArray();
            var frames = new List<int>(notes.Count);
            foreach (int i in order) frames.Add(FrameOf(i));
            List<MarkerSlot> slots = TimelineMath.Layout(frames, view, w);
            dc.PushClip(new RectangleGeometry(trackRect));
            foreach (MarkerSlot g in slots)
            {
                NoteState first = notes[order[g.Notes[0]]];
                Color stemColor = NoteKinds.Color(first.Kind);
                double x = Math.Round(g.X);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xB0, stemColor.R, stemColor.G, stemColor.B)), null, new Rect(x - 0.75, RulerH, 1.5, TrackH));
                for (int row = 0; row < Math.Min(TimelineMath.MaxRows, g.Notes.Count); row++)
                {
                    int index = order[g.Notes[row]];
                    NoteState n = notes[index];
                    Color c = NoteKinds.Color(n.Kind);
                    bool sel = n.Id == selectedId || (moving && index == pressTag), hover = index == hoverIndex;
                    double top = RulerH + RowTop + row * RowH;
                    Rect r;
                    if (g.ShowLabels)
                    {
                        string more = row == TimelineMath.MaxRows - 1 && g.Notes.Count > TimelineMath.MaxRows ? " +" + (g.Notes.Count - TimelineMath.MaxRows) : "";
                        string text = TimelineMath.ShortText(n.Text);
                        FormattedText label = new FormattedText((text.Length > 0 ? text : n.Kind) + more, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            new Typeface(Typography.Caption.Family, FontStyles.Normal, Typography.Caption.Weight, FontStretches.Normal),
                            Typography.Caption.Size, Themes.Brush("ink-1"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
                        { MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                        double maxText = Math.Max(4, g.Room - 14);
                        label.MaxTextWidth = maxText;
                        double tw = Math.Min(maxText, label.WidthIncludingTrailingWhitespace);
                        r = new Rect(x, top, Math.Min(g.Room, tw + 14), TagH);
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(sel ? 0x88 : hover ? 0x66 : 0x40), c.R, c.G, c.B)), sel ? new Pen(Themes.Brush("ink-1"), 1) : null, r, 5, 5);
                        dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(x, top + 2, 3, TagH - 4));
                        dc.DrawText(label, new Point(x + 8, top + (TagH - label.Height) / 2));
                    }
                    else
                    {
                        r = new Rect(x - 3.5, top + 4, 7, 7);
                        dc.DrawRoundedRectangle(new SolidColorBrush(c), sel ? new Pen(Themes.Brush("ink-1"), 1.5) : null, r, 2, 2);
                    }
                    tagRects.Add(new KeyValuePair<Rect, int>(r, index));
                }
            }
            dc.Pop();
        }

        int FrameOf(int index) => moving && index == pressTag ? moveFrame : notes[index].Frame ?? 0;

        void DrawPlayhead(DrawingContext dc, double w)
        {
            double x = TimelineMath.X(playhead, view, w);
            if (x < -1 || x > w + 1) return;
            x = Math.Round(x);
            Brush accent = Themes.Brush("accent");
            dc.DrawRectangle(accent, null, new Rect(x - 1, 2, 2, RulerH + TrackH - 2));
            var head = new StreamGeometry();
            using (StreamGeometryContext g = head.Open())
            {
                g.BeginFigure(new Point(x - 5, 0), true, true);
                g.LineTo(new Point(x + 5, 0), false, false);
                g.LineTo(new Point(x, 6), false, false);
            }
            head.Freeze();
            dc.DrawGeometry(accent, null, head);
        }

        void DrawScroll(DrawingContext dc, double w)
        {
            bool zoomed = view.Span < total - 1e-6;
            scroll.Visibility = zoomed ? Visibility.Visible : Visibility.Collapsed;
            if (!zoomed) return;
            double y = RulerH + TrackH + 3;
            scrollRect = new Rect(0, y, w, ScrollH - 3);
            double tw = Math.Max(24, view.Span / total * w);
            double left = total - view.Span <= 0 ? 0 : view.Start / (total - view.Span) * (w - tw);
            thumbRect = new Rect(left, y, tw, ScrollH - 3);
            dc.DrawRoundedRectangle(Themes.Brush("line-soft"), null, scrollRect, 2.5, 2.5);
            dc.DrawRoundedRectangle(Themes.Brush(thumbDrag ? "scroll-thumb-hover" : "scroll-thumb"), null, thumbRect, 2.5, 2.5);
        }

        // --- pointer ------------------------------------------------------------------------------------------

        int TagAt(Point p)
        {
            for (int i = tagRects.Count - 1; i >= 0; i--)
            {
                Rect r = tagRects[i].Key;
                r.Inflate(2, 2);
                if (r.Contains(p)) return tagRects[i].Value;
            }
            return -1;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (total <= 0) return;
            Point p = e.GetPosition(this);
            var part = (Border)sender;
            e.Handled = true;
            if (part == scroll)
            {
                if (thumbRect.Contains(p)) { thumbDrag = true; thumbGrab = p.X - thumbRect.X; part.CaptureMouse(); InvalidateVisual(); }
                else
                {
                    double centre = p.X / Math.Max(1, ActualWidth) * total;
                    view = TimelineMath.Clamp(new TimelineView(centre - view.Span / 2, view.Span), total);
                    Publish();
                    InvalidateVisual();
                }
                return;
            }
            int tag = part == track ? TagAt(p) : -1;
            if (tag >= 0)
            {
                CloseCard();
                if (e.ClickCount >= 2) { Edit?.Invoke(notes[tag]); return; }
                Pick?.Invoke(notes[tag]);
                // A press on a tag may turn into a drag that moves the note.
                pressTag = tag;
                pressX = p.X;
                moving = false;
                moveFrame = notes[tag].Frame ?? 0;
                grabOffset = TimelineMath.FrameAtX(p.X, view, ActualWidth) - moveFrame;
                part.CaptureMouse();
                return;
            }
            scrubbing = true;
            CloseCard();
            part.CaptureMouse();
            Scrubbing?.Invoke(true);
            ScrubTo(p.X);
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            if (thumbDrag)
            {
                double w = ActualWidth, tw = thumbRect.Width;
                double start = (p.X - thumbGrab) / Math.Max(1, w - tw) * (total - view.Span);
                view = TimelineMath.Clamp(new TimelineView(start, view.Span), total);
                Publish();
                InvalidateVisual();
                return;
            }
            if (scrubbing) { ScrubTo(p.X); return; }
            if (pressTag >= 0)
            {
                if (!moving && Math.Abs(p.X - pressX) < DragStart) return;
                if (!moving) { moving = true; SetHover(-1); track.Cursor = Cursors.SizeWE; }
                double f = TimelineMath.FrameAtX(Math.Max(0, Math.Min(ActualWidth, p.X)), view, ActualWidth) - grabOffset;
                int frame = (int)Math.Max(0, Math.Min(total - 1, Math.Round(f)));
                if (frame != moveFrame)
                {
                    moveFrame = frame;
                    Moving?.Invoke(notes[pressTag], frame);
                    InvalidateVisual();
                }
                return;
            }
            if (sender == track) SetHover(TagAt(p));
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (pressTag >= 0)
            {
                NoteState n = notes[pressTag];
                bool dragged = moving;
                int frame = moveFrame;
                pressTag = -1;
                moving = false;
                track.Cursor = null;
                if (sender is UIElement held && held.IsMouseCaptured) held.ReleaseMouseCapture();
                InvalidateVisual();
                if (dragged && frame != n.Frame) Moved?.Invoke(n, frame);
                else if (dragged) MoveCancelled?.Invoke(n);
                return;
            }
            if (sender is UIElement u && u.IsMouseCaptured) u.ReleaseMouseCapture();
            EndDrag();
        }

        void EndDrag()
        {
            if (thumbDrag) { thumbDrag = false; InvalidateVisual(); }
            if (scrubbing) { scrubbing = false; Scrubbing?.Invoke(false); }
            CancelMove();
        }

        /// <summary>Drops a drag in progress: the note stays on its frame.</summary>
        public void CancelMove()
        {
            if (pressTag < 0) return;
            NoteState? n = pressTag < notes.Count ? notes[pressTag] : null;
            bool dragged = moving;
            pressTag = -1;
            moving = false;
            track.Cursor = null;
            if (track.IsMouseCaptured) track.ReleaseMouseCapture();
            InvalidateVisual();
            if (dragged && n != null) MoveCancelled?.Invoke(n);
        }

        void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            if (total <= 0 || sender == scroll || pressTag >= 0) return;
            e.Handled = true;
            Point p = e.GetPosition(this);
            int tag = sender == track ? TagAt(p) : -1;
            CloseCard();
            if (tag >= 0) { TagMenu?.Invoke(notes[tag], p); return; }
            double f = TimelineMath.FrameAtX(Math.Max(0, Math.Min(ActualWidth, p.X)), view, ActualWidth);
            TrackMenu?.Invoke((int)Math.Max(0, Math.Min(total - 1, Math.Floor(f + 1e-4))), p);
        }

        void ScrubTo(double x)
        {
            double f = TimelineMath.FrameAtX(Math.Max(0, Math.Min(ActualWidth, x)), view, ActualWidth);
            int frame = (int)Math.Max(0, Math.Min(total - 1, Math.Floor(f + 1e-4)));
            Scrub?.Invoke(frame);
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (total <= 0) return;
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) view = TimelineMath.Pan(view, -e.Delta / 120.0 * 0.12, total);
            else
            {
                double anchor = TimelineMath.FrameAtX(e.GetPosition(this).X, view, ActualWidth);
                view = TimelineMath.ZoomAt(view, anchor, TimelineMath.WheelFactor(e.Delta), total);
            }
            Publish();
            InvalidateVisual();
        }

        // --- hover card ---------------------------------------------------------------------------------------

        void SetHover(int index)
        {
            if (index == hoverIndex) return;
            hoverIndex = index;
            InvalidateVisual();
            CloseCard();
            if (index >= 0) cardTimer.Start();
        }

        void CloseCard()
        {
            cardTimer.Stop();
            HoverCard.Hide();
        }

        void ShowCard()
        {
            if (hoverIndex < 0 || hoverIndex >= notes.Count || moving) return;
            Rect at = Rect.Empty;
            foreach (var kv in tagRects) if (kv.Value == hoverIndex) at = kv.Key;
            if (at.IsEmpty) return;
            NoteState n = notes[hoverIndex];
            HoverCard.Show(new HoverCard(CardContent(n, true), 260), this, at);
        }

        /// <summary>The most lines of note text a hover card shows.</summary>
        public const int CardLines = 12;

        /// <summary>The hover card of a note: type and timecode, the text (at most <see cref="CardLines"/> lines), the frame image; on the timeline also what the pointer can do.</summary>
        public static UIElement CardContent(NoteState n, bool onTimeline = false)
        {
            var panel = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            Color c = NoteKinds.Color(n.Kind);
            head.Children.Add(new Painted(8, 8, (dc, s) => dc.DrawEllipse(new SolidColorBrush(c), null, new Point(4, 4), 4, 4)) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Typography.Text(n.Kind, Typography.LabelS, "ink-2"));
            TextBlock tc = Typography.Text(n.Timecode, Typography.Mono, "ink-1");
            tc.Margin = new Thickness(10, 0, 0, 0);
            head.Children.Add(tc);
            panel.Children.Add(head);
            TextBlock text = Typography.Text(n.Text.Length > 0 ? n.Text : Copy.NoText, Typography.Body, n.Text.Length > 0 ? "ink-1" : "ink-4");
            text.TextWrapping = TextWrapping.Wrap;
            // A very long note stops at CardLines lines with an ellipsis, so the card stays on the screen; the list row
            // of the selected note shows the whole text.
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.MaxHeight = Typography.Body.LineHeight * CardLines;
            text.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(text);
            BitmapSource? thumb = Thumbs.Load(n.ImagePath, 228);
            if (thumb != null) panel.Children.Add(new Image { Source = thumb, Margin = new Thickness(0, 8, 0, 0), Stretch = Stretch.Uniform, MaxHeight = 128 });
            if (onTimeline)
            {
                TextBlock how = Typography.Text(Copy.TagCardHint, Typography.Caption, "ink-3");
                how.TextWrapping = TextWrapping.Wrap;
                how.Margin = new Thickness(0, 8, 0, 0);
                panel.Children.Add(how);
            }
            return panel;
        }
    }

    /// <summary>
    /// Thumbnails of frame images, decoded small and without locking the file (it is replaced in place on edits), kept
    /// while the file is unchanged, so a list rebuilt after every change does not decode every image again.
    /// </summary>
    public static class Thumbs
    {
        static readonly FileCache<BitmapSource> Cache = new FileCache<BitmapSource>(400);

        public static BitmapSource? Load(string? path, int width) => Cache.Get(path, width, p => Decode(p, width));

        static BitmapSource? Decode(string path, int width)
        {
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.UriSource = new Uri(path, UriKind.Absolute);
                b.DecodePixelWidth = width;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is NotSupportedException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return null;
            }
        }
    }
}
