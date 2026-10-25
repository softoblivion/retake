using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Retake.Design;
using Retake.Design.Controls;
using SelfUpdate;

namespace Retake.Update
{
    /// <summary>
    /// The updates popover of the version pill (LIQUID_UI.md 10, the popover of the Python apps): the product name
    /// with the status line ("You have the latest version", "Version 2.1.1 is available"), the line about automatic
    /// downloads, the Installed and Latest rows with their tags, the download bar, the error, the release notes of a
    /// newer version, and a footer with the time of the last check and the one action. It draws the presenter's
    /// <see cref="PanelView"/> and follows it live while open; Esc or a press outside closes it.
    /// </summary>
    internal sealed class UpdatePopover
    {
        const double NotesHeight = 200;
        readonly OverlayHost overlay;
        readonly VersionPill pill;
        readonly UpdatePresenter presenter;
        readonly Func<string, bool> openLink;
        readonly StackPanel body = new StackPanel();
        readonly TextBlock title = Typography.Text("", Typography.Title);
        readonly TextBlock status = Typography.Text("", Typography.BodyMedium);
        readonly Spinner checking = new Spinner(14) { Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
        readonly TextBlock auto = Typography.Text("", Typography.Caption, "ink-3");
        readonly Grid rows = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        readonly StackPanel progressBox = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        readonly ProgressLine bar = new ProgressLine();
        readonly TextBlock progressText = Typography.Text("", Typography.Caption, "ink-3");
        readonly Border errorBox = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        readonly TextBlock error = Typography.Text("", Typography.Body, "error-ink");
        readonly StackPanel notes = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        readonly ScrollViewer notesScroll;
        readonly TextBlock checkedLine = Typography.Text("", Typography.Caption, "ink-3");
        readonly ActionButton action;
        Popover? shown;
        List<NoteBlock>? rendered;
        DateTime closedAt = DateTime.MinValue;

        internal UpdatePopover(OverlayHost overlay, VersionPill pill, UpdatePresenter presenter, Func<string, bool> openLink)
        {
            this.overlay = overlay;
            this.pill = pill;
            this.presenter = presenter;
            this.openLink = openLink;
            action = new ActionButton(ButtonSize.S, 132, presenter.Run);

            body.Children.Add(title);
            var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            status.TextWrapping = TextWrapping.Wrap;
            statusRow.Children.Add(status);
            statusRow.Children.Add(checking);
            body.Children.Add(statusRow);
            auto.Margin = new Thickness(0, 3, 0, 0);
            auto.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(auto);
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rows.ColumnDefinitions.Add(new ColumnDefinition());
            body.Children.Add(rows);
            progressText.Margin = new Thickness(0, 6, 0, 0);
            progressBox.Children.Add(bar);
            progressBox.Children.Add(progressText);
            body.Children.Add(progressBox);
            error.TextWrapping = TextWrapping.Wrap;
            errorBox.Child = error;
            errorBox.SetResourceReference(Border.BackgroundProperty, "T.error-wash");
            body.Children.Add(errorBox);
            notesScroll = new ScrollViewer { Content = notes, MaxHeight = NotesHeight, Margin = new Thickness(0, 12, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(notesScroll, UpdateText.ReleaseNotes);
            body.Children.Add(notesScroll);
            var line = new Border { Height = 1, Margin = new Thickness(-14, 14, -14, 10) };
            line.SetResourceReference(Border.BackgroundProperty, "T.line-soft");
            body.Children.Add(line);
            var footer = new DockPanel();
            DockPanel.SetDock(action, Dock.Right);
            footer.Children.Add(action);
            checkedLine.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(checkedLine);
            body.Children.Add(footer);
            overlay.MenuChanged += open =>
            {
                if (open || shown == null || overlay.CurrentMenu == shown) return;
                shown = null;
                closedAt = DateTime.UtcNow;
                pill.IsSource = false;
            };
        }

        internal bool IsOpen => shown != null && overlay.CurrentMenu == shown;

        /// <summary>The pill's click: opens the popover, or closes it when this click's press has just closed it.</summary>
        internal void Toggle()
        {
            if (IsOpen) { shown!.Close(); return; }
            if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 350) return;
            presenter.Refresh();
            var popover = new Popover();
            AutomationProperties.SetName(popover, UpdateCopy.PopoverName);
            AutomationProperties.SetAutomationId(popover, "UpdatesPopover");
            if (body.Parent is Border old) old.Child = null;
            popover.Body = body;
            shown = popover;
            pill.IsSource = true;
            overlay.OpenMenu(popover, pill);
        }

        internal void Close()
        {
            if (IsOpen) shown!.Close();
        }

        /// <summary>Every presenter change; the view is drawn even while closed, so it opens up to date.</summary>
        internal void Render(PanelView v)
        {
            title.Text = v.Title;
            status.Text = v.StatusLine;
            checking.Visibility = v.Indeterminate ? Visibility.Visible : Visibility.Collapsed;
            auto.Text = v.AutoLine ?? "";
            auto.Visibility = v.AutoLine != null ? Visibility.Visible : Visibility.Collapsed;
            RenderRows(v.Rows);
            progressBox.Visibility = v.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
            if (v.ShowProgress)
            {
                bar.Value = v.Progress;
                progressText.Text = v.ProgressPercent + (string.IsNullOrEmpty(v.ProgressDetail) ? "" : " · " + v.ProgressDetail);
            }
            // A failed check names its reason in the status line already: the box shows only what that line does not say.
            bool box = v.Error != null && v.Error != v.StatusLine;
            errorBox.Visibility = box ? Visibility.Visible : Visibility.Collapsed;
            error.Text = box ? v.Error! : "";
            status.SetResourceReference(TextBlock.ForegroundProperty, v.Error != null && !box ? "T.error-ink" : "T.ink-1");
            notesScroll.Visibility = v.Notes != null && v.Notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!ReferenceEquals(rendered, v.Notes))
            {
                rendered = v.Notes;
                NotesView.Render(notes, v.Notes, UpdateText.NoNotes, openLink);
            }
            checkedLine.Text = v.Checked;
            action.Show(v.Action);
        }

        void RenderRows(List<PanelRow> list)
        {
            rows.Children.Clear();
            rows.RowDefinitions.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                PanelRow r = list[i];
                rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock name = Typography.Text(r.Name, Typography.Caption, "ink-3");
                name.Margin = new Thickness(0, i > 0 ? 6 : 0, 14, 0);
                Grid.SetRow(name, i);
                rows.Children.Add(name);
                var value = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, i > 0 ? 6 : 0, 0, 0) };
                value.Children.Add(Typography.Text(r.Version, Typography.Mono, r.IsNew ? "accent-ink" : r.IsReady ? "ok-ink" : "ink-1"));
                foreach (string tag in r.Tags)
                {
                    TextBlock t = Typography.Text(tag, Typography.Caption, "ink-3");
                    t.Margin = new Thickness(8, 0, 0, 0);
                    value.Children.Add(t);
                }
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                rows.Children.Add(value);
            }
        }
    }
}
