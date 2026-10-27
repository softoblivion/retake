using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Retake.Core;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>
    /// The frame-rate pill of the title bar: "25 fps" (amber when the rate is unknown or the player reads another
    /// one). Its menu lists the nominal rates of the timecode rules with the current one checked, the file's rate
    /// to drop an override, the player's rate when it differs, and a field for any other rate.
    /// </summary>
    public sealed class FrameRateControl : GlassButton
    {
        public event Action<double?>? Chosen;

        double? rate, fileRate, playerRate;
        string source = "unknown";

        public FrameRateControl() : base(Copy.RateUnknown, null, ButtonKind.Ghost, ButtonSize.XS)
        {
            AutomationProperties.SetAutomationId(this, "FrameRate");
            Tip.Set(this, Copy.RateTip);
        }

        /// <param name="current">The session's rate (null when unknown).</param>
        /// <param name="rateSource">"file", "override" or "unknown".</param>
        /// <param name="probed">The rate the file reports (for the reset entry).</param>
        /// <param name="player">The player's rate when it differs from the session's.</param>
        public void Set(double? current, string rateSource, double? probed, double? player)
        {
            rate = current;
            source = rateSource;
            fileRate = probed;
            playerRate = player;
            bool warn = current == null || player != null;
            Caption = current.HasValue ? Timecode.FormatFps(current.Value) + Copy.RateSuffix : Copy.RateUnknown;
            SetResourceReference(ForegroundProperty, warn ? "T.warn-ink" : "T.ink-1");
            Tip.Set(this, current.HasValue ? Copy.RateTip : Copy.RateUnknownTip);
        }

        public PopMenu BuildMenu()
        {
            var menu = new PopMenu(200);
            if (playerRate.HasValue)
            {
                double p = playerRate.Value;
                menu.Add(string.Format(CultureInfo.InvariantCulture, Copy.RateFromPlayer, Timecode.FormatFps(p)), null, null, null, () => Chosen?.Invoke(p));
            }
            if (source == "override" && fileRate.HasValue)
                menu.Add(string.Format(CultureInfo.InvariantCulture, Copy.RateFromFile, Timecode.FormatFps(fileRate.Value)), null, null, null, () => Chosen?.Invoke(null));
            if (menu.HasEntries) menu.AddSeparator();
            foreach (double r in Timecode.NominalRates)
            {
                double value = r;
                bool on = rate.HasValue && Math.Abs(rate.Value - Timecode.NominalRate(r)) < 1e-6;
                MenuEntry e = menu.Add(Timecode.FormatFps(r), on ? Icons.Check : "", null, null, () => Chosen?.Invoke(value));
                AutomationProperties.SetAutomationId(e, "Rate-" + Timecode.FormatFps(r));
            }
            menu.AddSeparator();
            var field = new TextField(Copy.RateOtherPlaceholder, 176) { Margin = new Thickness(4, 2, 4, 2) };
            Tip.Set(field, Copy.RateOtherTip);
            AutomationProperties.SetAutomationId(field, "OtherRate");
            AutomationProperties.SetName(field, Copy.RateOtherPlaceholder);
            field.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                try
                {
                    double typed = Timecode.ParseFps(field.Text);
                    menu.Close();
                    Chosen?.Invoke(typed);
                }
                catch (FormatException) { Tip.Set(field, Copy.RateInvalid); }
            };
            menu.AddElement(field);
            return menu;
        }
    }

    /// <summary>The name of the open video in the title bar; a click opens the menu that shows its files in Explorer.</summary>
    public sealed class VideoName : GlassButton
    {
        public VideoName() : base("", null, ButtonKind.Ghost, ButtonSize.XS)
        {
            AutomationProperties.SetAutomationId(this, "VideoName");
            Tip.Set(this, Copy.RevealTip);
            MaxWidth = 320;
        }
    }

    /// <summary>
    /// The error banner under the title bar: the message after an error icon on the error wash, replaced by the
    /// next error; a click anywhere on it closes it.
    /// </summary>
    public sealed class ErrorBanner : Pressable
    {
        readonly TextBlock text;

        public ErrorBanner()
        {
            ScalesOnPress = false;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Visibility = Visibility.Collapsed;
            Margin = new Thickness(12, 6, 12, 0);
            MinHeight = 34;
            AutomationProperties.SetAutomationId(this, "ErrorBanner");
            Tip.Set(this, Copy.BannerTip);
            var row = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };
            FrameworkElement icon = Icons.Make(Icons.Error, 16);
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.SetResourceReference(TextBlock.ForegroundProperty, "T.error-ink");
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            FrameworkElement close = Icons.Make(Icons.X, 14);
            close.SetResourceReference(TextBlock.ForegroundProperty, "T.ink-3");
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            text = Typography.Text("", Typography.Body, "ink-1");
            text.TextWrapping = TextWrapping.Wrap;
            row.Children.Add(text);
            var surface = new SurfaceVisual(12, "error-rim", null, "solid-tint", "error-wash");
            Content = new Grid { Children = { surface, row } };
            Click += (s, e) => Hide();
        }

        public string Message => text.Text;

        public void Show(string message)
        {
            text.Text = message;
            AutomationProperties.SetName(this, message);
            Visibility = Visibility.Visible;
        }

        public void Hide()
        {
            Visibility = Visibility.Collapsed;
            text.Text = "";
            AutomationProperties.SetName(this, "");
        }

        protected override void Apply(double hover, double press, double focus, bool animate) { }
    }

    /// <summary>The key map sheet (?): every key of the window, grouped.</summary>
    public static class KeyMap
    {
        public static readonly string[][] Playback =
        {
            new[] { "Space", "Play or pause" },
            new[] { "←  →", "One frame back or forward" },
            new[] { "Shift+←  Shift+→", "Ten frames" },
            new[] { "Home  End", "First or last frame" },
            new[] { "J", "Step back while held" },
            new[] { "K", "Pause" },
            new[] { "L", "Play; again for 2×, 4×, 8×" },
        };

        public static readonly string[][] Notes =
        {
            new[] { "1  2  3", "Start a FIX, ADD or REMOVE note at this frame" },
            new[] { "4", "Start a general note, about the whole video" },
            new[] { "5  6", "Start a LIKE or DISLIKE note at this frame" },
            new[] { "Drag  Click", "Mark a region or a point on the video" },
            new[] { "Ctrl+1 … Ctrl+6", "Change the type of the note being written; Ctrl+4 moves it between its frame and the whole video" },
            new[] { "Enter", "Save the note (it needs text)" },
            new[] { "Esc", "Cancel the new note or the edit" },
            new[] { "Backspace", "Remove the region (empty field, while editing)" },
            new[] { "F2", "Edit the selected note" },
            new[] { "Delete", "Remove the selected note" },
            new[] { "↑  ↓", "Previous or next note" },
        };

        public static readonly string[][] Files =
        {
            new[] { "Wheel", "Zoom the timeline" },
            new[] { "Shift+Wheel", "Scroll the timeline" },
            new[] { "0", "Fit the whole video in the timeline" },
            new[] { "Drag", "Move a note to another frame by its tag" },
            new[] { "Right-click", "Menu of a note, the video, the notes list or the timeline" },
            new[] { "Ctrl+O", "Open a video" },
            new[] { "Ctrl+Shift+C", "Copy the notes as text" },
            new[] { "Ctrl+C", "Copy the notes file" },
            new[] { "Ctrl+E", "Done: pack the notes and frames into a zip" },
            new[] { "?", "This list" },
            new[] { "Ctrl+,", "Settings" },
        };

        public static Sheet Build()
        {
            var body = new StackPanel();
            body.Children.Add(SettingsSheet.Group("Playback", Rows(Playback)));
            body.Children.Add(SettingsSheet.Group("Notes", Rows(Notes)));
            body.Children.Add(SettingsSheet.Group("Timeline and files", Rows(Files)));
            var sheet = new Sheet(Copy.KeysTitle, body, Copy.KeysCloseTip, 560);
            AutomationProperties.SetAutomationId(sheet, "KeyMap");
            return sheet;
        }

        static FrameworkElement[] Rows(string[][] table)
        {
            var rows = new List<FrameworkElement>();
            foreach (string[] r in table)
            {
                var row = new Grid { Margin = new Thickness(14, 6, 14, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var keys = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                foreach (string k in r[0].Split(new[] { "  " }, StringSplitOptions.RemoveEmptyEntries)) keys.Children.Add(Key(k));
                row.Children.Add(keys);
                TextBlock what = Typography.Text(r[1], Typography.Body, "ink-1");
                what.TextWrapping = TextWrapping.Wrap;
                Grid.SetColumn(what, 1);
                row.Children.Add(what);
                rows.Add(row);
            }
            return rows.ToArray();
        }

        static FrameworkElement Key(string k)
        {
            var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BackgroundProperty, "T.fill-2");
            b.SetResourceReference(Border.BorderBrushProperty, "T.stroke-2");
            b.Child = Typography.Text(k, Typography.MonoS, "ink-1");
            return b;
        }
    }
}
