using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>
    /// The type bar beside a region marked outside a note: the five note types as chips, in their order and colours,
    /// and one more chip that opens the note editor for a text. A click on a type (or its number key, which the view
    /// routes) saves a note of that type with the region and no text; the text chip goes on to the editor. The bar
    /// is the editor's card in small (--menu-tint, radius 14) and wraps its chips when the area is narrow.
    /// </summary>
    public sealed class KindBar : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        readonly WrapPanel row = new WrapPanel { Margin = new Thickness(8, 8, 4, 4) };

        /// <summary>A type chip was clicked: save a note of this type without text.</summary>
        public event Action<string>? Picked;
        /// <summary>The text chip was clicked: write the note in the editor.</summary>
        public event Action? TextChosen;

        public KindBar()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Visibility = Visibility.Collapsed;
            AutomationProperties.SetAutomationId(this, "KindBar");
            AutomationProperties.SetName(this, Copy.KindBarName);
            Children.Add(new ShadowLayer("shadow-m", 14));
            Children.Add(new SurfaceVisual(14, "stroke-2", "btn-bevel", "menu-tint"));
            foreach (string k in NoteKinds.All)
            {
                string kind = k;
                var chip = new Chip(NoteKinds.Label(k), NoteKinds.Glyph(k), k, "Quick-" + k,
                    string.Format(CultureInfo.InvariantCulture, Copy.QuickKindTip, NoteKinds.Label(k), NoteKinds.Key(k)), Typography.LabelS)
                { Margin = new Thickness(0, 0, 4, 4) };
                chip.Click += (s, e) => Picked?.Invoke(kind);
                row.Children.Add(chip);
            }
            var text = new Chip(Copy.QuickText, Icons.Pen, null, "Quick-Text", Copy.QuickTextTip, Typography.LabelS) { Margin = new Thickness(0, 0, 4, 4) };
            text.Click += (s, e) => TextChosen?.Invoke();
            row.Children.Add(text);
            Children.Add(row);
        }

        public bool IsOpen => Visibility == Visibility.Visible;

        /// <summary>Shows the bar; its chips wrap inside the MaxWidth its place gives it.</summary>
        public void Open() => Visibility = Visibility.Visible;

        public void Close() => Visibility = Visibility.Collapsed;
    }
}
