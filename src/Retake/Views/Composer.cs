using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using TextElement = System.Windows.Documents.TextElement;
using System.Windows.Media;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>
    /// The note editor over the video: a near-opaque card (--menu-tint, radius 14). The top row names where the note
    /// belongs (the timecode, or General for a note about the whole video) and the three note types as chips, the
    /// current one lit; a click on a chip or Ctrl+1 / 2 / 3 changes the type. Below it the text field. Enter saves,
    /// Esc cancels, Backspace in the empty field removes the region (while editing). A note needs text: Enter with
    /// an empty field keeps the editor open and says so under the field. It sits beside the region, or above the
    /// bottom of the frame without one.
    /// </summary>
    public sealed class Composer : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        public readonly TextField Field = new TextField("", 330);
        readonly TextBlock where = Typography.Text("", Typography.Mono, "ink-2");
        readonly TextBlock warning = Typography.Text(Copy.NeedText, Typography.Caption, "warn-ink");
        readonly KindChip[] chips = new KindChip[NoteKinds.All.Length];
        string kind = "FIX";

        public event Action? Commit, Cancel, ClearRegion;
        /// <summary>The type was changed with a chip or Ctrl+1 / 2 / 3.</summary>
        public event Action<string>? KindChanged;

        public Composer()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(this, "Composer");
            Children.Add(new ShadowLayer("shadow-m", 14));
            Children.Add(new SurfaceVisual(14, "stroke-2", "btn-bevel", "menu-tint"));
            var stack = new StackPanel { Margin = new Thickness(10, 8, 8, 8) };
            var head = new DockPanel { Margin = new Thickness(2, 0, 0, 6) };
            var kinds = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < chips.Length; i++)
            {
                string k = NoteKinds.All[i];
                var chip = new KindChip(k) { Margin = new Thickness(i > 0 ? 4 : 0, 0, 0, 0) };
                chip.Click += (s, e) => { SetKind(k, true); FocusField(); };
                chips[i] = chip;
                kinds.Children.Add(chip);
            }
            DockPanel.SetDock(kinds, Dock.Right);
            head.Children.Add(kinds);
            where.Margin = new Thickness(0, 0, 14, 0);
            head.Children.Add(where);
            stack.Children.Add(head);
            AutomationProperties.SetAutomationId(Field, "NoteText");
            Tip.Set(Field, Copy.FieldTip);
            Field.PreviewKeyDown += OnKey;
            Field.TextChanged += (s, e) => { if (Field.Text.Length > 0) warning.Visibility = Visibility.Collapsed; };
            stack.Children.Add(Field);
            warning.Margin = new Thickness(4, 5, 0, 0);
            warning.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(warning, "NeedText");
            stack.Children.Add(warning);
            Children.Add(stack);
        }

        public string Kind => kind;
        public string Text { get => Field.Text; set { Field.Text = value; Field.CaretIndex = value.Length; } }
        public bool IsOpen => Visibility == Visibility.Visible;

        /// <param name="noteKind">The type of the note.</param>
        /// <param name="place">The timecode of the note's frame, or null for a general note.</param>
        /// <param name="text">The text to edit ("" for a new note).</param>
        public void Open(string noteKind, string? place, string text)
        {
            where.Text = place ?? Copy.GeneralLabel;
            (place != null ? Typography.Mono : Typography.LabelS).Apply(where);
            SetKind(noteKind, false);
            Text = text;
            warning.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Visible;
        }

        public void Close()
        {
            Visibility = Visibility.Collapsed;
            warning.Visibility = Visibility.Collapsed;
            Field.Text = "";
        }

        /// <summary>Shows that the note needs text (Enter on an empty field).</summary>
        public void AskForText()
        {
            warning.Visibility = Visibility.Visible;
            FocusField();
        }

        /// <summary>Changes the type as a click on its chip does (Ctrl+1 / 2 / 3 outside the field).</summary>
        public void ChangeKind(string noteKind) => SetKind(noteKind, true);

        void SetKind(string noteKind, bool user)
        {
            bool changed = noteKind != kind;
            kind = noteKind;
            foreach (KindChip c in chips) c.Lit = c.Kind == noteKind;
            Field.Placeholder = NoteKinds.Placeholder(noteKind);
            AutomationProperties.SetName(Field, Field.Placeholder);
            if (user && changed) KindChanged?.Invoke(noteKind);
        }

        public void FocusField()
        {
            Field.Focus();
            Keyboard.Focus(Field);
            Field.CaretIndex = Field.Text.Length;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; Commit?.Invoke(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Cancel?.Invoke(); }
            else if (e.Key == Key.Back && Field.Text.Length == 0) { e.Handled = true; ClearRegion?.Invoke(); }
            else if (Keyboard.Modifiers == ModifierKeys.Control && KindKey(e.Key) is string k) { e.Handled = true; SetKind(k, true); }
        }

        /// <summary>The note type of Ctrl+1 / 2 / 3 (digit row or keypad), else null.</summary>
        public static string? KindKey(Key key)
        {
            switch (key)
            {
                case Key.D1: case Key.NumPad1: return "FIX";
                case Key.D2: case Key.NumPad2: return "ADD";
                case Key.D3: case Key.NumPad3: return "REMOVE";
                default: return null;
            }
        }
    }

    /// <summary>A note type in the editor: a dot and the word on a small plate, lit in the type's colour when it is the note's type.</summary>
    public sealed class KindChip : Pressable
    {
        readonly Painted plate;
        readonly TextBlock word;
        public readonly string Kind;
        bool lit;

        public KindChip(string kind)
        {
            Kind = kind;
            ScalesOnPress = false;
            Height = 24;
            Focusable = false;
            AutomationProperties.SetName(this, kind);
            AutomationProperties.SetAutomationId(this, "Kind-" + kind);
            Tip.Set(this, string.Format(System.Globalization.CultureInfo.InvariantCulture, Copy.KindChipTip, Array.IndexOf(NoteKinds.All, kind) + 1));
            Color c = NoteKinds.Color(kind);
            plate = new Painted(0, 24, (dc, s) =>
            {
                double h = s.Height, w = s.Width;
                if (lit) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x48, c.R, c.G, c.B)), null, new Rect(0, 0, w, h), h / 2, h / 2);
                else if (IsMouseOver) dc.DrawRoundedRectangle(Themes.Brush("fill-2"), null, new Rect(0, 0, w, h), h / 2, h / 2);
            }) { Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Painted(7, 7, (dc, s) => dc.DrawEllipse(new SolidColorBrush(c), null, new Point(3.5, 3.5), 3.5, 3.5)) { Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            word = Typography.Text(kind, Typography.LabelS, null);
            row.Children.Add(word);
            Content = new Grid { Children = { plate, row } };
            Refresh(false);
        }

        public bool Lit { get => lit; set { lit = value; Refresh(false); } }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            SetResourceReference(ForegroundProperty, lit ? "T.ink-1" : hover > 0 ? "T.ink-2" : "T.ink-3");
            plate.InvalidateVisual();
        }
    }

    /// <summary>The hint line over the top of the frame while a note is written: what the pointer does, then the keys in a quieter ink.</summary>
    public sealed class HintPill : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        readonly TextBlock text;

        public HintPill()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(this, "Hint");
            Children.Add(new ShadowLayer("shadow-s"));
            Children.Add(new SurfaceVisual(-1, "stroke-2", null, "menu-tint"));
            text = new TextBlock { Margin = new Thickness(12, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center };
            Typography.Caption.Apply(text);
            Children.Add(text);
        }

        public void Show(string action, string keys)
        {
            text.Inlines.Clear();
            var a = new System.Windows.Documents.Run(action);
            a.SetResourceReference(TextElement.ForegroundProperty, "T.ink-1");
            var k = new System.Windows.Documents.Run("  ·  " + keys);
            k.SetResourceReference(TextElement.ForegroundProperty, "T.ink-3");
            text.Inlines.Add(a);
            text.Inlines.Add(k);
            AutomationProperties.SetName(this, action + " · " + keys);
            Visibility = Visibility.Visible;
        }

        public void Hide() => Visibility = Visibility.Collapsed;
    }
}
