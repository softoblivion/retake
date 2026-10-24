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
    /// <summary>One note of the list: its timecode (copies itself), the type, up to three lines of text, the frame image.</summary>
    public sealed class NoteRow : Pressable
    {
        readonly PlateVisual plate = new PlateVisual(PlateKind.Bare) { Radius = 12 };
        public readonly NoteState Note;
        public readonly CopyText Timecode;
        bool selected;

        public event Action<NoteRow>? Edit;

        public NoteRow(NoteState note)
        {
            Note = note;
            ScalesOnPress = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetAutomationId(this, "Note-" + note.Id);
            AutomationProperties.SetName(this, note.Timecode + " " + note.Kind + " " + note.Text);
            Tip.Set(this, Copy.NoteRowTip);
            var grid = new Grid { Margin = new Thickness(6, 6, 8, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var body = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            Timecode = new CopyText(Typography.Mono, "ink-1", Copy.TimecodeTip, "NoteTimecode") { Value = note.Timecode };
            head.Children.Add(Timecode);
            Color c = NoteKinds.Color(note.Kind);
            head.Children.Add(new Painted(8, 8, (dc, s) => dc.DrawEllipse(new SolidColorBrush(c), null, new Point(4, 4), 4, 4)) { Margin = new Thickness(6, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Typography.Text(note.Kind, Typography.LabelS, "ink-2"));
            body.Children.Add(head);
            TextBlock text = Typography.Text(note.Text.Length > 0 ? note.Text : Copy.NoText, Typography.Body, note.Text.Length > 0 ? "ink-1" : "ink-4");
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.MaxHeight = Typography.Body.LineHeight * 3;
            text.Margin = new Thickness(6, 2, 0, 0);
            body.Children.Add(text);
            grid.Children.Add(body);
            BitmapSource? thumb = Thumbs.Load(note.ImagePath, 144);
            if (thumb != null)
            {
                var img = new Border { Width = 72, Height = 41, CornerRadius = new CornerRadius(6), Margin = new Thickness(8, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
                img.SetResourceReference(Border.BackgroundProperty, "T.video-bg");
                img.Child = new Image { Source = thumb, Stretch = Stretch.Uniform };
                Grid.SetColumn(img, 1);
                grid.Children.Add(img);
            }
            Content = new Grid { Children = { plate, grid } };
            MouseDoubleClick += (s, e) => { if (!Timecode.IsMouseOver) { e.Handled = true; Edit?.Invoke(this); } };
            Refresh(false);
        }

        public bool Selected
        {
            get => selected;
            set { selected = value; AutomationProperties.SetItemStatus(this, value ? "selected" : ""); Refresh(false); }
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            GlassButton.To(plate, PlateVisual.HoverProperty, selected ? 1 : hover * 0.6, animate, Motion.D1);
            GlassButton.To(plate, PlateVisual.RingProperty, selected ? 0.6 : focus, animate, Motion.D1);
        }
    }

    /// <summary>
    /// The notes list on the right (collapsible): a heading with the count per type, then one row per note in
    /// timecode order. The selection follows the timeline and the playhead keys; it scrolls into view.
    /// </summary>
    public sealed class NotesList : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        readonly StackPanel rows = new StackPanel();
        readonly ScrollViewer scroller;
        readonly TextBlock empty;
        readonly StackPanel counts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Dictionary<string, NoteRow> byId = new Dictionary<string, NoteRow>();
        string? selectedId;

        public event Action<NoteState>? Pick, Edit;
        public event Action<string>? Copied;

        public NotesList()
        {
            AutomationProperties.SetAutomationId(this, "NotesList");
            Children.Add(new ShadowLayer("card-drop", 18));
            Children.Add(new SurfaceVisual(18, "card-rim", null, "card-fill"));
            var dock = new DockPanel();
            var head = new DockPanel { Margin = new Thickness(14, 12, 12, 8) };
            head.Children.Add(Typography.Text(Copy.NotesTitle, Typography.Title));
            DockPanel.SetDock(counts, Dock.Right);
            head.Children.Insert(0, counts);
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            empty = Typography.Text(Copy.NotesEmpty, Typography.Caption, "ink-3");
            empty.Margin = new Thickness(14, 4, 14, 0);
            empty.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(empty, Dock.Top);
            dock.Children.Add(empty);
            scroller = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(6, 0, 4, 8) };
            dock.Children.Add(scroller);
            Children.Add(dock);
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
                row.Timecode.Copied += t => Copied?.Invoke(t);
                byId[n.Id] = row;
                rows.Children.Add(row);
            }
            empty.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            counts.Children.Clear();
            foreach (string kind in NoteKinds.All)
            {
                Color c = NoteKinds.Color(kind);
                counts.Children.Add(new Painted(7, 7, (dc, s) => dc.DrawEllipse(new SolidColorBrush(c), null, new Point(3.5, 3.5), 3.5, 3.5)) { Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
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
                row.BringIntoView();
            }
        }
    }
}
