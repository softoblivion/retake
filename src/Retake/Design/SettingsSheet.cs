using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Retake.Design.Controls;

namespace Retake.Design
{
    /// <summary>
    /// The Settings sheet: groups Appearance (Theme, Scale), Updates (Download updates automatically) and Review
    /// (Author name), each a card of rows with a label, a hint that adds what the label does not say, and the
    /// control on the right. Every change applies at once (theme and scale live) and lands in <see cref="Prefs"/>.
    /// </summary>
    public sealed class SettingsSheet
    {
        public const string Title = "Settings", CloseTip = "Close settings (Esc)";
        public const string ThemeLabel = "Theme", ThemeHint = "Background colour and accent of the window";
        public const string ScaleLabel = "Scale", ScaleHint = "Size of the text and controls", ScaleOff = "Too large for this screen";
        public const string AutoLabel = "Download updates automatically", AutoHint = "Checked every 5 minutes; installing still needs a restart";
        public const string AutoOnTip = "On: new versions download in the background", AutoOffTip = "Off: new versions wait until you install them";
        public const string AuthorLabel = "Author name", AuthorHint = "Shown beside the notes you write", AuthorTip = "Saved as you type";

        public static readonly Dictionary<string, string> ThemeTips = new Dictionary<string, string>
        {
            ["black"] = "Dark glass on a near-black scene",
            ["sky"] = "Light glass on a pale blue scene",
        };

        public static readonly string[] ScaleTips =
        {
            "Text and controls at the size Windows sets",
            "Text and controls a quarter larger",
            "Text and controls half as large again",
            "Text and controls three quarters larger",
            "Text and controls twice as large",
        };

        public readonly Sheet Sheet;
        public readonly Segmented ThemeControl, ScaleControl;
        public readonly Switch AutoControl;
        public readonly TextField AuthorControl;
        readonly Prefs prefs;
        readonly Func<int, bool> fits;

        public SettingsSheet(ShellWindow window) : this(window.Prefs, window.StepFits, step => window.ApplyScale(step), window.UpdatesRow)
        {
            window.ScaleApplied += () => ScaleControl.Select(Array.IndexOf(Scale.Steps, prefs.UiScale), true);
        }

        /// <param name="fits">Whether a Scale step fits the current monitor.</param>
        /// <param name="applyScale">Applies a Scale step to the window.</param>
        /// <param name="updatesRow">The updater's status row, shown above the automatic-download switch; null without an updater (the gallery).</param>
        public SettingsSheet(Prefs prefs, Func<int, bool> fits, Action<int> applyScale, FrameworkElement? updatesRow = null)
        {
            this.prefs = prefs;
            this.fits = fits;
            var themes = new List<string>();
            var labels = new List<string>();
            var tips = new List<string?>();
            foreach (ThemeSpec t in Themes.All)
            {
                themes.Add(t.Name);
                labels.Add(t.Label);
                tips.Add(ThemeTips.TryGetValue(t.Name, out string? tip) ? tip : null);
            }
            ThemeControl = new Segmented(ThemeLabel, labels.ToArray(), tips.ToArray(), Math.Max(0, themes.IndexOf(Themes.Current.Name)), compact: true);
            ThemeControl.Changed += i => { prefs.Theme = themes[i]; Themes.Apply(themes[i]); };

            var scaleLabels = Array.ConvertAll(Scale.Steps, Scale.Label);
            ScaleControl = new Segmented(ScaleLabel, scaleLabels, ScaleTips, Math.Max(0, Array.IndexOf(Scale.Steps, prefs.UiScale)), compact: true);
            ScaleControl.Changed += i => applyScale(Scale.Steps[i]);

            AutoControl = new Switch(prefs.AutoDownloadUpdates, AutoLabel);
            Tip.Set(AutoControl, prefs.AutoDownloadUpdates ? AutoOnTip : AutoOffTip);
            AutoControl.Toggled += on => { prefs.AutoDownloadUpdates = on; Tip.Set(AutoControl, on ? AutoOnTip : AutoOffTip); };

            AuthorControl = new TextField("", 200) { Text = prefs.AuthorName };
            System.Windows.Automation.AutomationProperties.SetName(AuthorControl, AuthorLabel);
            Tip.Set(AuthorControl, AuthorTip);
            AuthorControl.TextChanged += (s, e) => prefs.AuthorName = AuthorControl.Text;

            var body = new StackPanel();
            body.Children.Add(Group("Appearance", Row(ThemeLabel, ThemeHint, ThemeControl), Row(ScaleLabel, ScaleHint, ScaleControl)));
            var autoRow = Row(AutoLabel, AutoHint, AutoControl);
            body.Children.Add(updatesRow == null ? Group("Updates", autoRow) : Group("Updates", updatesRow, autoRow));
            body.Children.Add(Group("Review", Row(AuthorLabel, AuthorHint, AuthorControl)));
            Sheet = new Sheet(Title, body, CloseTip);
            Refresh();
        }

        /// <summary>Re-reads the values and which Scale steps fit the window's monitor.</summary>
        public void Refresh()
        {
            ThemeControl.Select(Math.Max(0, IndexOfTheme(prefs.Theme)), false);
            for (int i = 0; i < Scale.Steps.Length; i++) ScaleControl.SetOff(i, !fits(Scale.Steps[i]), ScaleOff);
            ScaleControl.Select(Math.Max(0, Array.IndexOf(Scale.Steps, prefs.UiScale)), false);
        }

        static int IndexOfTheme(string name)
        {
            for (int i = 0; i < Themes.All.Count; i++) if (Themes.All[i].Name == name) return i;
            return -1;
        }

        /// <summary>A settings group: a section heading and a card of rows separated by soft lines.</summary>
        public static FrameworkElement Group(string title, params FrameworkElement[] rows)
        {
            var group = new StackPanel();
            TextBlock head = Typography.Text(title, Typography.Section, "ink-3");
            head.Margin = new Thickness(12, 16, 12, 6);
            group.Children.Add(head);
            var card = new Grid();
            card.Children.Add(new ShadowLayer("card-drop", 18));
            card.Children.Add(new SurfaceVisual(18, "card-rim", null, "card-fill"));
            var list = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0)
                {
                    var line = new Border { Height = 1, Margin = new Thickness(14, 0, 14, 0) };
                    line.SetResourceReference(Border.BackgroundProperty, "T.line-soft");
                    list.Children.Add(line);
                }
                list.Children.Add(rows[i]);
            }
            card.Children.Add(list);
            group.Children.Add(card);
            return group;
        }

        /// <summary>A settings row: label (label role) over a hint (caption role, ink-3), the control on the right.</summary>
        public static FrameworkElement Row(string label, string hint, FrameworkElement control) =>
            Row(Typography.Text(label, Typography.Label), Typography.Text(hint, Typography.Caption, "ink-3"), control);

        /// <summary>A settings row over text blocks the caller keeps and changes (the updater's status line and last check).</summary>
        public static FrameworkElement Row(TextBlock label, TextBlock small, FrameworkElement control)
        {
            var row = new Grid { MinHeight = 28, Margin = new Thickness(14, 9, 14, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            text.Children.Add(label);
            small.Margin = new Thickness(0, 2, 0, 0);
            small.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(small);
            row.Children.Add(text);
            control.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }
    }
}
