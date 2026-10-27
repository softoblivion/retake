using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Retake.Core;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>
    /// The plate of a note row, drawn inside the row's bounds so nothing of it is cut by the list's edges: a soft
    /// fill on hover; when selected a stronger fill, a hairline rim and a 3 px bar in the note's colour on the left.
    /// Radius 10: the card's 14 less its 4 px inset.
    /// </summary>
    sealed class RowPlate : FrameworkElement
    {
        public static readonly DependencyProperty HoverProperty = DependencyProperty.Register(nameof(Hover), typeof(double), typeof(RowPlate), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LitProperty = DependencyProperty.Register(nameof(Lit), typeof(double), typeof(RowPlate), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
        public double Lit { get => (double)GetValue(LitProperty); set => SetValue(LitProperty, value); }

        readonly Color mark;
        public double Ring;

        public RowPlate(Color mark)
        {
            this.mark = mark;
            IsHitTestVisible = false;
            Themes.Track(this);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, lit = Lit, hover = Hover * (1 - lit);
            if (w <= 0 || h <= 0) return;
            var rect = new Rect(0, 0, w, h);
            const double r = 10;
            if (hover > 0) dc.DrawRoundedRectangle(Themes.Solid(Themes.Fade(Themes.Color("fill-2"), hover)), null, rect, r, r);
            if (lit > 0)
            {
                dc.DrawRoundedRectangle(Themes.Solid(Themes.Fade(Themes.Color("fill-3"), lit)), null, rect, r, r);
                var pen = new Pen(Themes.Solid(Themes.Fade(Themes.Color("stroke-2"), lit)), 1);
                dc.DrawRoundedRectangle(null, pen, new Rect(0.5, 0.5, w - 1, h - 1), r - 0.5, r - 0.5);
                dc.PushClip(new RectangleGeometry(rect, r, r));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * lit), mark.R, mark.G, mark.B)), null, new Rect(0, 0, 3, h));
                dc.Pop();
            }
            if (Ring > 0)
            {
                var focus = new Pen(Themes.Solid(Themes.Fade(Themes.Color("focus-ring"), Ring)), 2);
                dc.DrawRoundedRectangle(null, focus, new Rect(1, 1, w - 2, h - 2), r - 1, r - 1);
            }
        }
    }

    /// <summary>
    /// The badge of a note type: its glyph in the type's colour on a 26 px rounded tile of a tint of it, so the type
    /// reads by shape as well as by colour.
    /// </summary>
    public sealed class KindBadge : Grid
    {
        public KindBadge(string kind, double size = 26)
        {
            Width = Height = size;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
            var tile = new Painted(size, size, (dc, s) =>
            {
                Color c = NoteKinds.Color(kind);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B)), null, new Rect(0, 0, s.Width, s.Height), 7, 7);
            });
            Themes.Track(tile);
            Children.Add(tile);
            FrameworkElement glyph = Icons.Make(NoteKinds.Glyph(kind), size * 0.58);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, NoteKinds.Ink(kind));
            Children.Add(glyph);
        }
    }

    /// <summary>
    /// One note of the list: the type's badge; a head line with the timecode (copies itself) or General and the type
    /// in its colour; up to three lines of text; the frame image on the right.
    /// </summary>
    public sealed class NoteRow : Pressable
    {
        readonly RowPlate plate;
        public readonly NoteState Note;
        public readonly CopyText? Timecode;
        bool selected;

        public event Action<NoteRow>? Edit;
        /// <summary>A right click (or the context menu key) on the row, at a point of the row.</summary>
        public event Action<NoteRow, Point>? Menu;

        public NoteRow(NoteState note)
        {
            Note = note;
            ScalesOnPress = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Margin = new Thickness(0, 0, 0, 2);
            AutomationProperties.SetAutomationId(this, "Note-" + note.Id);
            AutomationProperties.SetName(this, (note.IsGeneral ? Copy.GeneralLabel : note.Timecode) + " " + note.Kind + " " + note.Text);
            Tip.Set(this, note.IsGeneral ? Copy.GeneralRowTip : Copy.NoteRowTip);
            Color c = NoteKinds.Color(note.Kind);
            plate = new RowPlate(c);
            var grid = new Grid { Margin = new Thickness(10, 9, 10, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new KindBadge(note.Kind) { Margin = new Thickness(0, 0, 10, 0) });
            var body = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Height = 24 };
            if (note.IsGeneral)
            {
                TextBlock general = Typography.Text(Copy.GeneralLabel, Typography.LabelS, "ink-2");
                general.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(general);
            }
            else
            {
                Timecode = new CopyText(Typography.Mono, "ink-2", Copy.TimecodeTip, "NoteTimecode") { Value = note.Timecode, Margin = new Thickness(-6, 0, -2, 0) };
                head.Children.Add(Timecode);
            }
            TextBlock dot = Typography.Text("·", Typography.LabelS, "ink-4");
            dot.Margin = new Thickness(6, 0, 6, 0);
            dot.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(dot);
            TextBlock kind = Typography.Text(NoteKinds.Label(note.Kind), Typography.LabelS, null);
            kind.SetResourceReference(TextBlock.ForegroundProperty, NoteKinds.Ink(note.Kind));
            kind.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(kind);
            body.Children.Add(head);
            TextBlock text = Typography.Text(note.Text.Length > 0 ? note.Text : Copy.NoText, Typography.Body, note.Text.Length > 0 ? "ink-1" : "ink-4");
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.MaxHeight = Typography.Body.LineHeight * 3;
            text.Margin = new Thickness(0, 1, 0, 0);
            body.Children.Add(text);
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            BitmapSource? thumb = Thumbs.Load(note.ImagePath, 128);
            if (thumb != null)
            {
                var img = new Border { Width = 64, Height = 36, CornerRadius = new CornerRadius(6), Margin = new Thickness(10, 2, 0, 0), VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
                img.SetResourceReference(Border.BackgroundProperty, "T.video-bg");
                img.Child = new Image { Source = thumb, Stretch = Stretch.Uniform };
                Grid.SetColumn(img, 2);
                grid.Children.Add(img);
            }
            Content = new Grid { Children = { plate, grid } };
            MouseDoubleClick += (s, e) => { if (Timecode == null || !Timecode.IsMouseOver) { e.Handled = true; Edit?.Invoke(this); } };
            MouseRightButtonUp += (s, e) => { e.Handled = true; Menu?.Invoke(this, e.GetPosition(this)); };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) { e.Handled = true; Menu?.Invoke(this, new Point(24, ActualHeight / 2)); }
            };
            Refresh(false);
        }

        public bool Selected
        {
            get => selected;
            set { selected = value; AutomationProperties.SetItemStatus(this, value ? "selected" : ""); Refresh(Motion.Enabled && IsLoaded); }
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            GlassButton.To(plate, RowPlate.HoverProperty, hover, animate, Motion.D1);
            GlassButton.To(plate, RowPlate.LitProperty, selected ? 1 : 0, animate, Motion.D2);
            plate.Ring = focus;
            plate.InvalidateVisual();
        }
    }

    /// <summary>
    /// The notes list on the right (collapsible): a heading with the number of notes and, for every type that has
    /// notes, its glyph and count; then one row per note: general notes first, then the frame notes in timecode
    /// order. Without notes the card shows what to press. The selection
    /// follows the timeline and the playhead keys and scrolls into view with room around it. A right click on a row
    /// or on the empty list opens its menu.
    /// </summary>
    public sealed class NotesList : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        readonly StackPanel rows = new StackPanel();
        readonly ScrollViewer scroller;
        readonly StackPanel empty = new StackPanel { Margin = new Thickness(24, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        readonly StackPanel counts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock total = Typography.Text("", Typography.Caption, "ink-3");
        readonly Dictionary<string, NoteRow> byId = new Dictionary<string, NoteRow>();
        string? selectedId;

        public event Action<NoteState>? Pick, Edit;
        public event Action<string>? Copied;
        /// <summary>A right click on a note row: the note, the row and the point in it.</summary>
        public event Action<NoteState, FrameworkElement, Point>? RowMenu;
        /// <summary>A right click on the list outside the rows, at a point of the list.</summary>
        public event Action<Point>? ListMenu;

        public NotesList()
        {
            AutomationProperties.SetAutomationId(this, "NotesList");
            Background = Brushes.Transparent;
            Children.Add(new ShadowLayer("card-drop", Review.CardRadius));
            Children.Add(new SurfaceVisual(Review.CardRadius, "card-rim", null, "card-fill"));
            var dock = new DockPanel();
            var head = new DockPanel { Margin = new Thickness(16, 13, 14, 9) };
            DockPanel.SetDock(counts, Dock.Right);
            head.Children.Add(counts);
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Typography.Text(Copy.NotesTitle, Typography.Title));
            total.Margin = new Thickness(8, 0, 0, 0);
            total.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(total);
            head.Children.Add(title);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            var line = new Border { Height = 1, Margin = new Thickness(14, 0, 14, 0) };
            line.SetResourceReference(Border.BackgroundProperty, "T.line-soft");
            DockPanel.SetDock(line, Dock.Top);
            dock.Children.Add(line);
            FrameworkElement emptyGlyph = Icons.Make(Icons.Note, 28);
            emptyGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            emptyGlyph.SetResourceReference(TextBlock.ForegroundProperty, "T.ink-4");
            TextBlock emptyTitle = Typography.Text(Copy.NotesEmpty, Typography.BodyMedium, "ink-2");
            emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            emptyTitle.Margin = new Thickness(0, 10, 0, 0);
            TextBlock emptyHint = Typography.Text(Copy.NotesEmptyHint, Typography.Caption, "ink-3");
            emptyHint.TextWrapping = TextWrapping.Wrap;
            emptyHint.TextAlignment = TextAlignment.Center;
            emptyHint.Margin = new Thickness(0, 4, 0, 0);
            empty.Children.Add(emptyGlyph);
            empty.Children.Add(emptyTitle);
            empty.Children.Add(emptyHint);
            var body = new Grid();
            // The padding keeps a lit row clear of the card's edges and of the heading when it scrolls under it.
            scroller = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(4, 6, 4, 6) };
            body.Children.Add(scroller);
            body.Children.Add(empty);
            dock.Children.Add(body);
            Children.Add(dock);
            MouseRightButtonUp += (s, e) => { if (e.Handled) return; e.Handled = true; ListMenu?.Invoke(e.GetPosition(this)); };
        }

        public void SetNotes(IList<NoteState> notes, KindCounts kinds, string? selected)
        {
            rows.Children.Clear();
            byId.Clear();
            foreach (NoteState n in notes)
            {
                var row = new NoteRow(n);
                row.Click += (s, e) => Pick?.Invoke(n);
                row.Edit += r => Edit?.Invoke(r.Note);
                row.Menu += (r, p) => RowMenu?.Invoke(r.Note, r, p);
                if (row.Timecode != null) row.Timecode.Copied += t => Copied?.Invoke(t);
                byId[n.Id] = row;
                rows.Children.Add(row);
            }
            empty.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            total.Text = notes.Count > 0 ? notes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            counts.Children.Clear();
            foreach (string kind in NoteKinds.All)
            {
                if (kinds[kind] == 0) continue;
                FrameworkElement glyph = Icons.Make(NoteKinds.Glyph(kind), 13);
                glyph.SetResourceReference(TextBlock.ForegroundProperty, NoteKinds.Ink(kind));
                glyph.Margin = new Thickness(10, 0, 4, 0);
                counts.Children.Add(glyph);
                TextBlock t = Typography.Text(kinds[kind].ToString(System.Globalization.CultureInfo.InvariantCulture), Typography.Caption, "ink-2");
                AutomationProperties.SetName(t, kind + " " + kinds[kind]);
                counts.Children.Add(t);
            }
            selectedId = null;
            Select(selected);
        }

        public void Select(string? id)
        {
            if (selectedId != null && byId.TryGetValue(selectedId, out NoteRow? old)) old.Selected = false;
            selectedId = id;
            if (id != null && byId.TryGetValue(id, out NoteRow? row))
            {
                row.Selected = true;
                // After layout (a new list has no sizes yet), with some room above and below, so the lit row never
                // sits flush against the heading or the bottom edge.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    if (selectedId == id && row.IsVisible) row.BringIntoView(new Rect(0, -10, Math.Max(1, row.ActualWidth), row.ActualHeight + 20));
                }));
            }
        }
    }
}
