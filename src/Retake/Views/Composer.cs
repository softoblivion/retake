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
    /// The note editor over the video: a near-opaque card (--menu-tint, radius 14) with the note type as a coloured
    /// dot and word, and one text field. Enter saves, Esc cancels, Backspace in the empty field removes the region
    /// (while editing). It sits beside the region, or above the bottom of the frame without one.
    /// </summary>
    public sealed class Composer : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        public readonly TextField Field = new TextField("", 260);
        readonly TextBlock kindText;
        readonly Painted dot;
        string kind = "FIX";

        public event Action? Commit, Cancel, ClearRegion;

        public Composer()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(this, "Composer");
            Children.Add(new ShadowLayer("shadow-m", 14));
            Children.Add(new SurfaceVisual(14, "stroke-2", "btn-bevel", "menu-tint"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 6, 6) };
            dot = new Painted(8, 8, (dc, s) => dc.DrawEllipse(new SolidColorBrush(NoteKinds.Color(kind)), null, new Point(4, 4), 4, 4)) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            kindText = Typography.Text("FIX", Typography.LabelS, "ink-2");
            kindText.Margin = new Thickness(0, 0, 10, 0);
            AutomationProperties.SetAutomationId(Field, "NoteText");
            Tip.Set(Field, Copy.FieldTip);
            Field.PreviewKeyDown += OnKey;
            row.Children.Add(dot);
            row.Children.Add(kindText);
            row.Children.Add(Field);
            Children.Add(row);
        }

        public string Kind => kind;
        public string Text { get => Field.Text; set { Field.Text = value; Field.CaretIndex = value.Length; } }
        public bool IsOpen => Visibility == Visibility.Visible;

        public void Open(string noteKind, string text)
        {
            kind = noteKind;
            kindText.Text = noteKind;
            dot.InvalidateVisual();
            SetPlaceholder(NoteKinds.Placeholder(noteKind));
            Text = text;
            Visibility = Visibility.Visible;
        }

        public void Close()
        {
            Visibility = Visibility.Collapsed;
            Field.Text = "";
        }

        void SetPlaceholder(string text)
        {
            Field.Placeholder = text;
            AutomationProperties.SetName(Field, text);
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
