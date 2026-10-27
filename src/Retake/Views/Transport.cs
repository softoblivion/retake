using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>
    /// A text that copies itself on click (timecodes, the frame number): ink text in a role on a bare plate that
    /// lights on hover; Enter or Space with keyboard focus copies too. The text stays selectable for the eye only.
    /// </summary>
    public sealed class CopyText : Pressable
    {
        readonly PlateVisual plate = new PlateVisual(PlateKind.Bare) { Radius = 8 };
        readonly TextBlock text;

        public event Action<string>? Copied;

        public CopyText(TypeRole role, string ink, string tip, string automationId)
        {
            ScalesOnPress = false;
            Height = 24;
            text = Typography.Text("", role, ink);
            text.Margin = new Thickness(6, 0, 6, 0);
            Content = new Grid { Children = { plate, text } };
            Tip.Set(this, tip);
            AutomationProperties.SetAutomationId(this, automationId);
            Click += (s, e) => { string v = CopyValue ?? Value; if (v.Length > 0) Copied?.Invoke(v); };
            Refresh(false);
        }

        /// <summary>The text put on the clipboard when it differs from the shown one (the bare frame number).</summary>
        public string? CopyValue;

        public string Value
        {
            get => text.Text;
            set
            {
                if (text.Text == value) return;
                text.Text = value;
                AutomationProperties.SetName(this, value);
                Visibility = value.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            GlassButton.To(plate, PlateVisual.HoverProperty, hover, animate, Motion.D1);
            GlassButton.To(plate, PlateVisual.RingProperty, focus, animate, Motion.D1);
        }
    }

    /// <summary>A key on a small well (the key of a note button, of a menu-like hint): mono-s in ink-3, 18 px high.</summary>
    public sealed class KeyCap : Border
    {
        public KeyCap(string key)
        {
            Height = 18;
            MinWidth = 18;
            CornerRadius = new CornerRadius(5);
            BorderThickness = new Thickness(1);
            SetResourceReference(BackgroundProperty, "T.fill-1");
            SetResourceReference(BorderBrushProperty, "T.stroke-1");
            TextBlock t = Typography.Text(key, Typography.MonoS, "ink-3");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(4, 0, 4, 1);
            Child = t;
            VerticalAlignment = VerticalAlignment.Center;
            IsHitTestVisible = false;
        }
    }

    /// <summary>
    /// A button of the note bar: the type's glyph in its colour (the general note's in ink-2), the label and the key
    /// on a ghost plate. The label and the key hide when the bar is short of room (<see cref="SetRoom"/>); the glyph
    /// and the tooltip stay, so the button never depends on colour alone.
    /// </summary>
    public sealed class NoteButton : Pressable
    {
        readonly PlateVisual plate = new PlateVisual(PlateKind.Ghost) { Radius = 9 };
        readonly FrameworkElement glyph;
        readonly TextBlock label;
        readonly KeyCap key;
        /// <summary>The note type, or null for the general note.</summary>
        public readonly string? Kind;

        public NoteButton(string? kind)
        {
            Kind = kind;
            Height = 30;
            string name = kind != null ? NoteKinds.Label(kind) : Copy.GeneralLabel;
            AutomationProperties.SetName(this, name);
            AutomationProperties.SetAutomationId(this, kind != null ? "New" + kind : "AddGeneral");
            glyph = Icons.Make(kind != null ? NoteKinds.Glyph(kind) : Icons.Whole, 16);
            glyph.SetResourceReference(TextBlock.ForegroundProperty, kind != null ? NoteKinds.Ink(kind) : "T.ink-2");
            label = Typography.Text(name, Typography.Label, null);
            label.Margin = new Thickness(7, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            key = new KeyCap(kind != null ? NoteKinds.Key(kind) : NoteKinds.GeneralKey) { Margin = new Thickness(8, 0, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(glyph);
            row.Children.Add(label);
            row.Children.Add(key);
            Content = new Grid { Children = { plate, row } };
            SetResourceReference(ForegroundProperty, "T.ink-1");
            Tip.Set(this, kind != null ? NoteKinds.Tip(kind) : Copy.GeneralTip);
            Refresh(false);
        }

        /// <summary>0: glyph, label and key; 1: glyph and label; 2: the glyph alone.</summary>
        public void SetRoom(int level)
        {
            label.Visibility = level < 2 ? Visibility.Visible : Visibility.Collapsed;
            key.Visibility = level < 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            GlassButton.To(plate, PlateVisual.HoverProperty, hover, animate, Motion.D1);
            GlassButton.To(plate, PlateVisual.PressProperty, press, animate, press > 0 ? Motion.D1 : Motion.D2);
            GlassButton.To(plate, PlateVisual.RingProperty, focus, animate, Motion.D1);
            GlassButton.To(this, OpacityProperty, IsEnabled ? 1 : 0.38, animate, Motion.D2);
        }
    }

    /// <summary>
    /// The control row of the dock, left to right: previous frame, play / pause, next frame and the speed; the time
    /// display (the playhead's timecode, the duration, the frame number; each copies itself); on the right the note
    /// bar, one button per key 1 to 6 in three groups (corrections, the general note, reactions). When the row is
    /// short of room the note buttons drop their keys first, then their labels.
    /// </summary>
    public sealed class Transport : Grid
    {
        public readonly IconButton Back, PlayPause, Forward;
        public readonly GlassButton SpeedButton;
        public readonly CopyText Time, Duration, FrameNumber;
        public readonly NoteButton[] Kinds;
        public readonly NoteButton General;
        readonly StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        readonly StackPanel left, time;
        readonly TextBlock slash;
        bool playing;
        int room = -1, timeLength = -1;

        public Transport()
        {
            Height = 48;
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition());

            left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Back = Icon(Icons.StepBack, "Previous frame", Copy.BackTip, "Back");
            PlayPause = new IconButton(Icons.Play, "Play", IconKind.Plate, ButtonSize.L) { Margin = new Thickness(2, 0, 2, 0) };
            Tip.Set(PlayPause, Copy.PlayTip);
            AutomationProperties.SetAutomationId(PlayPause, "PlayPause");
            Forward = Icon(Icons.StepForward, "Next frame", Copy.ForwardTip, "Forward");
            SpeedButton = new GlassButton("1×", null, ButtonKind.Ghost, ButtonSize.XS) { Margin = new Thickness(6, 0, 0, 0), MinWidth = 34 };
            Tip.Set(SpeedButton, Copy.SpeedTip);
            AutomationProperties.SetAutomationId(SpeedButton, "Speed");
            foreach (UIElement e in new UIElement[] { Back, PlayPause, Forward, SpeedButton }) left.Children.Add(e);
            Children.Add(left);

            time = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Time = new CopyText(Typography.Mono, "ink-1", Copy.TimecodeTip, "Timecode");
            slash = Typography.Text("/", Typography.Mono, "ink-4");
            slash.VerticalAlignment = VerticalAlignment.Center;
            Duration = new CopyText(Typography.Mono, "ink-3", Copy.DurationTip, "Duration");
            FrameNumber = new CopyText(Typography.Caption, "ink-3", Copy.FrameTip, "FrameNumber") { Margin = new Thickness(6, 0, 0, 0) };
            time.Children.Add(Time);
            time.Children.Add(slash);
            time.Children.Add(Duration);
            time.Children.Add(FrameNumber);
            SetColumn(time, 1);
            Children.Add(time);

            Kinds = new NoteButton[NoteKinds.All.Length];
            for (int i = 0; i < Kinds.Length; i++) Kinds[i] = new NoteButton(NoteKinds.All[i]);
            General = new NoteButton(null);
            // Keys 1 2 3 | 4 | 5 6: corrections, the whole video, reactions.
            AddToBar(Kinds[0], Kinds[1], Kinds[2]);
            AddSeparator();
            AddToBar(General);
            AddSeparator();
            AddToBar(Kinds[3], Kinds[4]);
            SetColumn(bar, 2);
            Children.Add(bar);
            SizeChanged += (s, e) => FitBar(e.NewSize.Width);
            SetTime("", "", null);
        }

        void AddToBar(params NoteButton[] buttons)
        {
            foreach (NoteButton b in buttons)
            {
                if (bar.Children.Count > 0 && !(bar.Children[bar.Children.Count - 1] is Border)) b.Margin = new Thickness(2, 0, 0, 0);
                bar.Children.Add(b);
            }
        }

        void AddSeparator()
        {
            var sep = new Border { Width = 1, Height = 18, Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            sep.SetResourceReference(Border.BackgroundProperty, "T.stroke-1");
            bar.Children.Add(sep);
        }

        /// <summary>The note bar takes the room the row leaves: keys first, then labels give way.</summary>
        void FitBar(double width)
        {
            left.Measure(new Size(double.PositiveInfinity, Height));
            time.Measure(new Size(double.PositiveInfinity, Height));
            double free = width - left.DesiredSize.Width - time.DesiredSize.Width - 16;
            int level = 0;
            for (; level < 2; level++)
            {
                SetRoom(level);
                bar.Measure(new Size(double.PositiveInfinity, Height));
                if (bar.DesiredSize.Width <= free) break;
            }
            if (level == 2) SetRoom(2);
        }

        void SetRoom(int level)
        {
            if (room == level) return;
            room = level;
            foreach (NoteButton b in Kinds) b.SetRoom(level);
            General.SetRoom(level);
        }

        static IconButton Icon(string icon, string name, string tip, string id)
        {
            var b = new IconButton(icon, name, IconKind.Bare, ButtonSize.M);
            Tip.Set(b, tip);
            AutomationProperties.SetAutomationId(b, id);
            return b;
        }

        public void SetPlaying(bool on)
        {
            if (playing == on) return;
            playing = on;
            var root = (Grid)PlayPause.Content;
            for (int i = root.Children.Count - 1; i >= 0; i--)
                if (root.Children[i] is Viewbox) root.Children.RemoveAt(i);
            FrameworkElement glyph = Icons.Make(on ? Icons.Pause : Icons.Play, 18);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(glyph);
            AutomationProperties.SetName(PlayPause, on ? "Pause" : "Play");
            Tip.Set(PlayPause, on ? Copy.PauseTip : Copy.PlayTip);
        }

        public void SetSpeed(double speed)
        {
            SpeedButton.Caption = speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";
        }

        /// <summary>The time display; empty strings hide it (no video).</summary>
        public void SetTime(string timecode, string duration, int? frame)
        {
            Time.Value = timecode;
            Duration.Value = duration;
            slash.Visibility = duration.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            FrameNumber.Value = frame.HasValue ? Copy.FrameLabel + frame.Value.ToString(CultureInfo.InvariantCulture) : "";
            FrameNumber.CopyValue = frame?.ToString(CultureInfo.InvariantCulture);
            // The time display takes more or less room (a video opened, more digits of the frame): the bar fits again.
            int length = timecode.Length + duration.Length + FrameNumber.Value.Length;
            if (length != timeLength && ActualWidth > 0)
            {
                timeLength = length;
                FitBar(ActualWidth);
            }
        }

        public void SetEnabled(bool on)
        {
            foreach (UIElement e in new UIElement[] { Back, PlayPause, Forward, SpeedButton, General }) e.IsEnabled = on;
            foreach (NoteButton b in Kinds) b.IsEnabled = on;
        }
    }
}
