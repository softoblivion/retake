using System;
using System.Globalization;
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
    /// The note editor over the video: a near-opaque card (--menu-tint, radius 14). The first row says where the
    /// note belongs: its frame (the timecode) or the whole video (General); a click or Ctrl+4 switches. The second row
    /// holds the five types as chips with their glyphs, the current one lit; a click or Ctrl+1 2 3 5 6 changes the
    /// type. Below them the text field. Enter saves, Esc cancels, Backspace in the empty field removes the region. A
    /// note needs text: Enter with an empty field keeps the editor open and says so under the field.
    /// </summary>
    public sealed class Composer : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        public readonly TextField Field = new TextField("", 420);
        readonly TextBlock warning = Typography.Text(Copy.NeedText, Typography.Caption, "warn-ink");
        readonly Chip[] chips = new Chip[NoteKinds.All.Length];
        readonly Chip atFrame, atWhole;
        string kind = "FIX";
        bool general;

        public event Action? Commit, Cancel, ClearRegion;
        /// <summary>The type was changed with a chip or Ctrl+1 2 3 5 6.</summary>
        public event Action<string>? KindChanged;
        /// <summary>The place was switched with a chip or Ctrl+4: true for the whole video.</summary>
        public event Action<bool>? PlaceChanged;

        public Composer()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(this, "Composer");
            Children.Add(new ShadowLayer("shadow-m", 14));
            Children.Add(new SurfaceVisual(14, "stroke-2", "btn-bevel", "menu-tint"));
            var stack = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };

            var place = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            atFrame = new Chip("", Icons.Clock, null, "Place-Frame", Copy.PlaceFrameTip, Typography.Mono);
            atWhole = new Chip(Copy.GeneralLabel, Icons.Whole, null, "Place-General", Copy.PlaceGeneralTip, Typography.LabelS) { Margin = new Thickness(4, 0, 0, 0) };
            atFrame.Click += (s, e) => { SetPlace(false, true); FocusField(); };
            atWhole.Click += (s, e) => { SetPlace(true, true); FocusField(); };
            place.Children.Add(atFrame);
            place.Children.Add(atWhole);
            stack.Children.Add(place);

            var kinds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < chips.Length; i++)
            {
                string k = NoteKinds.All[i];
                var chip = new Chip(NoteKinds.Label(k), NoteKinds.Glyph(k), k, "Kind-" + k,
                    string.Format(CultureInfo.InvariantCulture, Copy.KindChipTip, NoteKinds.Key(k)), Typography.LabelS) { Margin = new Thickness(i > 0 ? 4 : 0, 0, 0, 0) };
                chip.Click += (s, e) => { SetKind(k, true); FocusField(); };
                chips[i] = chip;
                kinds.Children.Add(chip);
            }
            stack.Children.Add(kinds);

            AutomationProperties.SetAutomationId(Field, "NoteText");
            Field.HorizontalAlignment = HorizontalAlignment.Stretch;
            Field.Width = double.NaN;
            Tip.Set(Field, Copy.FieldTip);
            Field.PreviewKeyDown += OnKey;
            Field.TextChanged += (s, e) => { if (Field.Text.Length > 0) warning.Visibility = Visibility.Collapsed; };
            stack.Children.Add(Field);
            warning.Margin = new Thickness(4, 6, 0, 0);
            warning.Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(warning, "NeedText");
            stack.Children.Add(warning);
            Children.Add(stack);
        }

        public string Kind => kind;
        public bool General => general;
        public string Text { get => Field.Text; set { Field.Text = value; Field.CaretIndex = value.Length; } }
        public bool IsOpen => Visibility == Visibility.Visible;

        /// <param name="noteKind">The type of the note.</param>
        /// <param name="frameTimecode">The timecode of the frame the note has, or would have when switched to a frame.</param>
        /// <param name="isGeneral">True for a note about the whole video.</param>
        /// <param name="text">The text to edit ("" for a new note).</param>
        public void Open(string noteKind, string frameTimecode, bool isGeneral, string text)
        {
            atFrame.Caption = frameTimecode;
            SetKind(noteKind, false);
            SetPlace(isGeneral, false);
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

        /// <summary>Changes the type as a click on its chip does (Ctrl+1 2 3 5 6 outside the field).</summary>
        public void ChangeKind(string noteKind) => SetKind(noteKind, true);

        /// <summary>Switches between the frame and the whole video as the chips do (Ctrl+4 outside the field).</summary>
        public void TogglePlace() => SetPlace(!general, true);

        void SetKind(string noteKind, bool user)
        {
            bool changed = noteKind != kind;
            kind = noteKind;
            foreach (Chip c in chips) c.Lit = c.Kind == noteKind;
            Field.Placeholder = NoteKinds.Placeholder(noteKind);
            AutomationProperties.SetName(Field, Field.Placeholder);
            if (user && changed) KindChanged?.Invoke(noteKind);
        }

        void SetPlace(bool whole, bool user)
        {
            bool changed = whole != general;
            general = whole;
            atFrame.Lit = !whole;
            atWhole.Lit = whole;
            if (user && changed) PlaceChanged?.Invoke(whole);
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
            else if (Keyboard.Modifiers == ModifierKeys.Control && NoteKinds.OfKey(e.Key) is string k) { e.Handled = true; SetKind(k, true); }
            else if (Keyboard.Modifiers == ModifierKeys.Control && NoteKinds.IsGeneralKey(e.Key)) { e.Handled = true; TogglePlace(); }
        }
    }

    /// <summary>
    /// A choice in the note editor: a glyph and a word on a small capsule. A type chip draws its glyph in the type's
    /// colour and lights in a tint of it; a place chip lights in the neutral fill.
    /// </summary>
    public sealed class Chip : Pressable
    {
        readonly Painted plate;
        readonly TextBlock word;
        readonly FrameworkElement glyph;
        /// <summary>The note type of a type chip; null for a place chip.</summary>
        public readonly string? Kind;
        bool lit;

        public Chip(string label, string icon, string? kind, string automationId, string tip, TypeRole role)
        {
            Kind = kind;
            ScalesOnPress = false;
            Height = 26;
            Focusable = false;
            AutomationProperties.SetName(this, label);
            AutomationProperties.SetAutomationId(this, automationId);
            Tip.Set(this, tip);
            plate = new Painted(0, 26, (dc, s) =>
            {
                double h = s.Height, w = s.Width, r = h / 2;
                if (lit && kind != null)
                {
                    Color c = NoteKinds.Color(kind);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x38, c.R, c.G, c.B)), new Pen(new SolidColorBrush(Color.FromArgb(0x70, c.R, c.G, c.B)), 1), new Rect(0.5, 0.5, w - 1, h - 1), r, r);
                }
                else if (lit) dc.DrawRoundedRectangle(Themes.Brush("fill-3"), new Pen(Themes.Brush("stroke-2"), 1), new Rect(0.5, 0.5, w - 1, h - 1), r, r);
                else if (IsMouseOver) dc.DrawRoundedRectangle(Themes.Brush("fill-2"), null, new Rect(0, 0, w, h), r, r);
            }) { Width = double.NaN, HorizontalAlignment = HorizontalAlignment.Stretch };
            Themes.Track(plate);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            glyph = Icons.Make(icon, 14);
            glyph.Margin = new Thickness(0, 0, 5, 0);
            if (kind != null) glyph.SetResourceReference(TextBlock.ForegroundProperty, NoteKinds.Ink(kind));
            row.Children.Add(glyph);
            word = Typography.Text(label, role, null);
            word.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(word);
            Content = new Grid { Children = { plate, row } };
            Refresh(false);
        }

        public string Caption
        {
            get => word.Text;
            set { word.Text = value; AutomationProperties.SetName(this, value); }
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
