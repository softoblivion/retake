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

    /// <summary>A note-type button of the transport: a dot in the type's colour and the type word on a ghost plate.</summary>
    public sealed class KindButton : Pressable
    {
        readonly PlateVisual plate = new PlateVisual(PlateKind.Ghost);
        public readonly string Kind;
        readonly Painted dot;

        public KindButton(string kind, string tip)
        {
            Kind = kind;
            Height = 28;
            AutomationProperties.SetName(this, NoteKinds.Label(kind));
            AutomationProperties.SetAutomationId(this, "New" + kind);
            dot = new Painted(8, 8, (dc, s) => dc.DrawEllipse(new SolidColorBrush(NoteKinds.Color(kind)), null, new Point(4, 4), 4, 4)) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            Themes.Changed += () => dot.InvalidateVisual();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot);
            row.Children.Add(Typography.Text(NoteKinds.Label(kind), Typography.Label, null));
            Content = new Grid { Children = { plate, row } };
            SetResourceReference(ForegroundProperty, "T.ink-1");
            Tip.Set(this, tip);
            Refresh(false);
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
    /// The row under the video, left to right: the frame buttons (first, previous, play / pause, next, last) with
    /// the playback speed; the time display (timecode of the playhead, the duration, the frame number; each copies
    /// itself); on the right the three note types.
    /// </summary>
    public sealed class Transport : Grid
    {
        public readonly IconButton First, Back, PlayPause, Forward, Last;
        public readonly GlassButton SpeedButton;
        public readonly CopyText Time, Duration, FrameNumber;
        public readonly KindButton Fix, Add, Remove;
        readonly TextBlock slash;
        bool playing;

        public Transport()
        {
            Height = 44;
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            First = Icon(Icons.First, "First frame", Copy.FirstTip, "First");
            Back = Icon(Icons.StepBack, "Previous frame", Copy.BackTip, "Back");
            PlayPause = new IconButton(Icons.Play, "Play", IconKind.Plate, ButtonSize.L) { Margin = new Thickness(4, 0, 4, 0) };
            Tip.Set(PlayPause, Copy.PlayTip);
            AutomationProperties.SetAutomationId(PlayPause, "PlayPause");
            Forward = Icon(Icons.StepForward, "Next frame", Copy.ForwardTip, "Forward");
            Last = Icon(Icons.Last, "Last frame", Copy.LastTip, "Last");
            SpeedButton = new GlassButton("1×", null, ButtonKind.Ghost, ButtonSize.XS) { Margin = new Thickness(8, 0, 0, 0), MinWidth = 34 };
            Tip.Set(SpeedButton, Copy.SpeedTip);
            AutomationProperties.SetAutomationId(SpeedButton, "Speed");
            foreach (UIElement e in new UIElement[] { First, Back, PlayPause, Forward, Last, SpeedButton }) left.Children.Add(e);
            Children.Add(left);

            var time = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            Time = new CopyText(Typography.Mono, "ink-1", Copy.TimecodeTip, "Timecode");
            slash = Typography.Text("/", Typography.Mono, "ink-4");
            Duration = new CopyText(Typography.Mono, "ink-3", Copy.DurationTip, "Duration");
            FrameNumber = new CopyText(Typography.Caption, "ink-3", Copy.FrameTip, "FrameNumber") { Margin = new Thickness(8, 0, 0, 0) };
            time.Children.Add(Time);
            time.Children.Add(slash);
            time.Children.Add(Duration);
            time.Children.Add(FrameNumber);
            SetColumn(time, 1);
            Children.Add(time);

            var kinds = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Fix = new KindButton("FIX", Copy.FixTip);
            Add = new KindButton("ADD", Copy.AddTip) { Margin = new Thickness(4, 0, 0, 0) };
            Remove = new KindButton("REMOVE", Copy.RemoveTip) { Margin = new Thickness(4, 0, 0, 0) };
            kinds.Children.Add(Fix);
            kinds.Children.Add(Add);
            kinds.Children.Add(Remove);
            SetColumn(kinds, 2);
            Children.Add(kinds);
            SetTime("", "", null);
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
        }

        public void SetEnabled(bool on)
        {
            foreach (UIElement e in new UIElement[] { First, Back, PlayPause, Forward, Last, SpeedButton, Fix, Add, Remove }) e.IsEnabled = on;
        }
    }
}
