using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Retake.Design;
using Retake.Design.Controls;
using SelfUpdate;
using Typography = Retake.Design.Typography;

namespace Retake.Update
{
    /// <summary>
    /// The one action button of a view (Install, Restart to update, Try again, Check for updates, Download). The
    /// library's <see cref="UpdateButton"/> says label, enabled, tinted or plain, and which command a click runs;
    /// the glass button has a fixed kind, so a plain and a tinted one sit in the same slot and the matching one
    /// shows.
    /// </summary>
    internal sealed class ActionButton : Grid
    {
        readonly GlassButton plain, tinted;
        UpdateCommand command = UpdateCommand.None;

        internal ActionButton(ButtonSize size, double minWidth, Action<UpdateCommand> run)
        {
            plain = new GlassButton("", null, ButtonKind.Plain, size) { MinWidth = minWidth };
            tinted = new GlassButton("", null, ButtonKind.Tinted, size) { MinWidth = minWidth, Visibility = Visibility.Collapsed };
            foreach (GlassButton b in new[] { plain, tinted })
            {
                b.Click += (s, e) => { if (command != UpdateCommand.None) run(command); };
                Children.Add(b);
            }
        }

        internal GlassButton Current => tinted.Visibility == Visibility.Visible ? tinted : plain;

        internal void Show(UpdateButton button)
        {
            command = button.Command;
            GlassButton on = button.Accent ? tinted : plain, off = button.Accent ? plain : tinted;
            off.Visibility = Visibility.Collapsed;
            on.Visibility = Visibility.Visible;
            on.Caption = button.Label;
            on.IsEnabled = button.Enabled;
            Tip.Set(on, UpdateCopy.Tip(button));
        }
    }

    /// <summary>A 6 px download bar: a --well-track lane with an --accent fill of <see cref="Value"/> (0..1).</summary>
    internal sealed class ProgressLine : FrameworkElement
    {
        double value;

        internal ProgressLine()
        {
            Height = 6;
            IsHitTestVisible = false;
            Themes.Track(this);
        }

        internal double Value
        {
            get => value;
            set { this.value = Math.Max(0, Math.Min(1, value)); InvalidateVisual(); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRoundedRectangle(Themes.Brush("well-track"), null, new Rect(0, 0, w, h), h / 2, h / 2);
            if (value > 0) dc.DrawRoundedRectangle(Themes.Brush("accent"), null, new Rect(0, 0, Math.Max(h, w * value), h), h / 2, h / 2);
        }
    }

    /// <summary>
    /// The release notes (the library's <see cref="NoteBlock"/>s) as text of the type roles: headings in the title
    /// role (levels 1 and 2) or the medium body role, paragraphs and list items in the body role, quotes with a
    /// soft rule, code in the mono role on a well, rules as a soft line, links in the accent ink.
    /// </summary>
    internal static class NotesView
    {
        internal static void Render(Panel target, List<NoteBlock>? blocks, string empty, Func<string, bool> openLink)
        {
            target.Children.Clear();
            if (blocks == null || blocks.Count == 0)
            {
                TextBlock none = Typography.Text(empty, Typography.Body, "ink-3");
                none.TextWrapping = TextWrapping.Wrap;
                target.Children.Add(none);
                return;
            }
            foreach (NoteBlock b in blocks)
            {
                switch (b.Kind)
                {
                    case NoteBlockKind.Heading:
                        target.Children.Add(Text(b, b.Level <= 2 ? Typography.Title : Typography.BodyMedium, "ink-1", new Thickness(0, 12, 0, 4), openLink));
                        break;
                    case NoteBlockKind.Paragraph:
                        target.Children.Add(Text(b, Typography.Body, "ink-2", new Thickness(0, 4, 0, 6), openLink));
                        break;
                    case NoteBlockKind.Bullet:
                    case NoteBlockKind.Numbered:
                        target.Children.Add(Item(b, openLink));
                        break;
                    case NoteBlockKind.Quote:
                        var quote = new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 1, 0, 1), Margin = new Thickness(0, 3, 0, 3), Child = Text(b, Typography.Body, "ink-3", new Thickness(0), openLink) };
                        quote.SetResourceReference(Border.BorderBrushProperty, "T.stroke-2");
                        target.Children.Add(quote);
                        break;
                    case NoteBlockKind.Code:
                        var code = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 4, 0, 6), Child = Typography.Text(b.Text, Typography.Mono, "ink-1") };
                        ((TextBlock)code.Child).TextWrapping = TextWrapping.Wrap;
                        code.SetResourceReference(Border.BackgroundProperty, "T.well");
                        target.Children.Add(code);
                        break;
                    case NoteBlockKind.Rule:
                        var rule = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
                        rule.SetResourceReference(Border.BackgroundProperty, "T.line-soft");
                        target.Children.Add(rule);
                        break;
                }
            }
            if (target.Children.Count > 0 && target.Children[0] is FrameworkElement first)
            {
                Thickness t = first.Margin;
                first.Margin = new Thickness(t.Left, 0, t.Right, t.Bottom);
            }
        }

        static TextBlock Text(NoteBlock b, TypeRole role, string ink, Thickness margin, Func<string, bool> openLink)
        {
            TextBlock block = Typography.Text("", role, ink);
            block.Margin = margin;
            block.TextWrapping = TextWrapping.Wrap;
            foreach (NoteSpan s in b.Spans) block.Inlines.Add(Span(s, role, openLink));
            return block;
        }

        static Grid Item(NoteBlock b, Func<string, bool> openLink)
        {
            var row = new Grid { Margin = new Thickness(2 + b.Indent * 14, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 16 });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock marker = Typography.Text(b.Marker, Typography.Body, "ink-3");
            marker.VerticalAlignment = VerticalAlignment.Top;
            marker.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(marker);
            TextBlock body = Text(b, Typography.Body, "ink-2", new Thickness(0), openLink);
            body.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            return row;
        }

        static Inline Span(NoteSpan s, TypeRole role, Func<string, bool> openLink)
        {
            var run = new Run(s.Text);
            if (s.Bold) run.FontWeight = FontWeights.SemiBold;
            if (s.Italic) run.FontStyle = FontStyles.Italic;
            if (s.Code)
            {
                run.FontFamily = Typography.MonoFamily;
                run.SetResourceReference(TextElement.BackgroundProperty, "T.well");
            }
            if (s.Url == null) return run;
            string url = s.Url;
            var link = new Hyperlink(run) { Cursor = Cursors.Hand, TextDecorations = null };
            link.SetResourceReference(TextElement.ForegroundProperty, "T.accent-ink");
            link.Click += (a, b) => openLink(url);
            return link;
        }
    }
}
