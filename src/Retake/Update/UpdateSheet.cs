using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Retake.Design;
using Retake.Design.Controls;
using SelfUpdate;

namespace Retake.Update
{
    /// <summary>
    /// The update sheet: the notice of a new version as a <see cref="Sheet"/> of the overlay layer, growing out of
    /// the version pill. Title "Retake X.Y.Z"; one line with the installed version, the size and the date; the
    /// release notes (scrolling inside the sheet, so the buttons stay in view); the progress while downloading;
    /// the error; Later and the one action button. It draws the presenter's <see cref="SheetView"/> and sends the
    /// clicks back; what it shows and when it opens is the presenter's decision. Esc, the veil and the close
    /// button close the sheet through the overlay, which the host turns into <c>Later()</c>.
    /// </summary>
    internal sealed class UpdateSheet
    {
        const double NotesHeight = 250;
        readonly OverlayHost overlay;
        readonly UpdatePresenter presenter;
        readonly FrameworkElement origin;
        readonly Func<string, bool> openLink;
        readonly StackPanel body = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
        readonly TextBlock subtitle = Typography.Text("", Typography.Caption, "ink-3");
        readonly StackPanel notes = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        readonly StackPanel progressBox = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
        readonly ProgressLine bar = new ProgressLine();
        readonly TextBlock progressText = Typography.Text("", Typography.Caption, "ink-3");
        readonly TextBlock error = Typography.Text("", Typography.Body, "error-ink");
        readonly GlassButton later = new GlassButton(UpdateText.Later, null, ButtonKind.Plain, ButtonSize.M) { MinWidth = 88 };
        readonly ActionButton action;
        Sheet? sheet;
        string? shownTitle;
        List<NoteBlock>? rendered;

        internal UpdateSheet(OverlayHost overlay, FrameworkElement origin, UpdatePresenter presenter, Func<string, bool> openLink)
        {
            this.overlay = overlay;
            this.origin = origin;
            this.presenter = presenter;
            this.openLink = openLink;
            action = new ActionButton(ButtonSize.M, 150, presenter.Run) { Margin = new Thickness(8, 0, 0, 0) };

            body.Children.Add(subtitle);
            var scroll = new ScrollViewer { Content = notes, MaxHeight = NotesHeight, Margin = new Thickness(0, 12, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            AutomationProperties.SetName(scroll, UpdateText.ReleaseNotes);
            body.Children.Add(scroll);
            progressText.Margin = new Thickness(0, 6, 0, 0);
            progressBox.Children.Add(bar);
            progressBox.Children.Add(progressText);
            body.Children.Add(progressBox);
            error.TextWrapping = TextWrapping.Wrap;
            error.Margin = new Thickness(0, 12, 0, 0);
            error.Visibility = Visibility.Collapsed;
            body.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            Tip.Set(later, UpdateCopy.Later);
            later.Click += (s, e) => presenter.Later();
            buttons.Children.Add(later);
            buttons.Children.Add(action);
            body.Children.Add(buttons);
        }

        /// <summary>The Sheet while it is the one the overlay shows.</summary>
        internal Sheet? Shown => sheet != null && overlay.CurrentSheet == sheet ? sheet : null;

        internal GlassButton ActionButton => action.Current;

        /// <summary>Every presenter change: open, close or update the sheet.</summary>
        internal void Render(SheetView v)
        {
            if (!v.Open)
            {
                if (Shown != null) overlay.CloseSheet();
                return;
            }
            subtitle.Text = v.Subtitle;
            subtitle.Visibility = v.Subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // The notes list is the same instance while the release is the same: draw it again only on a change,
            // so the scroll position survives the progress updates.
            if (!ReferenceEquals(rendered, v.Notes))
            {
                rendered = v.Notes;
                NotesView.Render(notes, v.Notes, v.EmptyNotes, openLink);
            }
            progressBox.Visibility = v.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
            if (v.ShowProgress)
            {
                bar.Value = v.Progress;
                progressText.Text = v.ProgressText;
            }
            error.Visibility = v.Error != null ? Visibility.Visible : Visibility.Collapsed;
            error.Text = v.Error ?? "";
            later.Caption = v.LaterLabel;
            action.Show(v.Action);
            if (Shown == null || shownTitle != v.Title) Open(v.Title);
        }

        void Open(string title)
        {
            if (sheet != null) sheet.Scroller.Content = null; // the body belongs to one sheet at a time
            sheet = new Sheet(title, body, UpdateCopy.SheetClose);
            shownTitle = title;
            overlay.OpenSheet(sheet, origin);
            overlay.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (Shown == null) return;
                GlassButton go = action.Current;
                (go.IsEnabled ? go : later).Focus();
            }));
        }
    }
}
