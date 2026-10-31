using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Retake.Core;
using Retake.Design;
using Retake.Design.Controls;

namespace Retake.Views
{
    /// <summary>One entry of the recent list as the views show it, read from the settings and the notes folder.</summary>
    public sealed class RecentItem
    {
        public string Path = "", Name = "", Folder = "";
        public int LastFrame;
        public bool Exists, IsOpen;
        /// <summary>Note and image counts of its data file; null when it has no notes folder.</summary>
        public NotesSummary? Summary;
        public bool HasNotesFolder;

        public static List<RecentItem> Read(Settings settings, string? openPath)
        {
            var list = new List<RecentItem>();
            foreach (RecentEntry e in settings.Recent)
            {
                var item = new RecentItem { Path = e.Path, LastFrame = e.LastFrame };
                try
                {
                    item.Name = System.IO.Path.GetFileName(e.Path);
                    item.Folder = System.IO.Path.GetDirectoryName(e.Path) ?? "";
                    item.Exists = File.Exists(e.Path);
                    item.IsOpen = openPath != null && string.Equals(System.IO.Path.GetFullPath(openPath), System.IO.Path.GetFullPath(e.Path), StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    // A path that is not valid (a hand-edited settings file) is not listed.
                    continue;
                }
                try
                {
                    item.Summary = NotesLayout.NotesSummaryFor(e.Path);
                    item.HasNotesFolder = Directory.Exists(NotesLayout.NotesDirFor(e.Path));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    item.Summary = null;
                }
                list.Add(item);
            }
            return list;
        }

        /// <summary>"frame 307 · 3 notes · 2 frames", "frame 0 · no notes", "Video not found".</summary>
        public string Meta()
        {
            string where = Exists ? Copy.FrameLabel + LastFrame.ToString(CultureInfo.InvariantCulture) : Copy.RecentMissing;
            if (Summary == null) return where;
            string notes = Summary.Notes == 0 ? "no notes" : Copy.Notes(Summary.Notes);
            return where + " · " + notes + (Summary.Frames > 0 ? " · " + Copy.Frames(Summary.Frames) : "");
        }
    }

    /// <summary>
    /// A row of the recent list: a video tile, the name with the folder under it (its last two parts), the position
    /// and counts on the right. Right click opens its menu.
    /// </summary>
    public sealed class RecentRow : Pressable
    {
        readonly PlateVisual plate = new PlateVisual(PlateKind.Bare) { Radius = 10 };
        public readonly RecentItem Item;

        public event Action<RecentRow>? Menu;

        public RecentRow(RecentItem item, double width)
        {
            Item = item;
            ScalesOnPress = false;
            Width = width;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(this, item.Name);
            AutomationProperties.SetAutomationId(this, "Recent-" + item.Name);
            AutomationProperties.SetItemStatus(this, item.Meta());
            Tip.Set(this, item.Exists ? Copy.RecentRowTip : Copy.RecentMissingTip);
            var grid = new Grid { Margin = new Thickness(8, 7, 12, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 12, 0) };
            tile.SetResourceReference(Border.BackgroundProperty, "T.fill-2");
            FrameworkElement glyph = Icons.Make(Icons.Video, 18);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, item.IsOpen ? "T.accent-ink" : "T.ink-2");
            tile.Child = glyph;
            grid.Children.Add(tile);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(names, 1);
            TextBlock name = Typography.Text(item.Name, Typography.Label, item.Exists ? "ink-1" : "ink-3");
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            TextBlock folder = Typography.Text(PathText.ShortFolder(item.Folder), Typography.Caption, "ink-3");
            folder.TextTrimming = TextTrimming.CharacterEllipsis;
            names.Children.Add(name);
            names.Children.Add(folder);
            grid.Children.Add(names);
            TextBlock meta = Typography.Text(item.Meta(), Typography.Caption, "ink-3");
            meta.Margin = new Thickness(12, 0, 0, 0);
            meta.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(meta, 2);
            grid.Children.Add(meta);
            Content = new Grid { Children = { plate, grid } };
            Opacity = item.Exists ? 1 : 0.7;
            MouseRightButtonUp += (s, e) => { e.Handled = true; Menu?.Invoke(this); };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) { e.Handled = true; Menu?.Invoke(this); }
            };
            Refresh(false);
        }

        protected override void Apply(double hover, double press, double focus, bool animate)
        {
            GlassButton.To(plate, PlateVisual.HoverProperty, Item.IsOpen ? 0.6 : hover, animate, Motion.D1);
            GlassButton.To(plate, PlateVisual.RingProperty, focus, animate, Motion.D1);
        }
    }

    /// <summary>What the recent-entry menu asks the window to do.</summary>
    public interface IRecentActions
    {
        void OpenPath(string path);
        void Reveal(RecentItem item);
        /// <summary>Deletes the notes of a video; returns false when it was refused (the reason is shown).</summary>
        bool DeleteNotes(RecentItem item);
        void Forget(RecentItem item);
    }

    /// <summary>
    /// The menu of a recent entry: Open, Show in Explorer, Delete notes (the entry turns into the question with
    /// the counts and needs a second click; leaving it for 3 s or Esc resets it), Remove from recent.
    /// </summary>
    public static class RecentMenu
    {
        public static PopMenu Build(RecentItem item, IRecentActions actions)
        {
            var menu = new PopMenu(220);
            MenuEntry open = menu.Add(Copy.MenuOpen, Icons.Video, null, null, () => actions.OpenPath(item.Path));
            open.IsEnabled = item.Exists;
            MenuEntry reveal = menu.Add(Copy.MenuReveal, Icons.Folder, null, item.HasNotesFolder ? NotesLayout.NotesDirFor(item.Path) : item.Path, () => actions.Reveal(item));
            reveal.IsEnabled = item.Exists || item.HasNotesFolder;
            MenuEntry? delete = null;
            bool asked = false;
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            delete = menu.Add(Copy.MenuDelete, Icons.Trash, null, Copy.MenuDeleteTip, () =>
            {
                if (!asked)
                {
                    asked = true;
                    delete!.Label = Question(item);
                    delete.Danger = true;
                    return;
                }
                reset.Stop();
                menu.Close();
                actions.DeleteNotes(item);
            });
            delete.KeepsMenuOpen = true;
            delete.IsEnabled = item.Summary != null || item.HasNotesFolder;
            AutomationProperties.SetAutomationId(delete, "DeleteNotes");
            reset.Tick += (s, e) =>
            {
                reset.Stop();
                asked = false;
                delete.Label = Copy.MenuDelete;
                delete.Danger = false;
            };
            delete.MouseLeave += (s, e) => { if (asked) { reset.Stop(); reset.Start(); } };
            delete.MouseEnter += (s, e) => reset.Stop();
            menu.AddSeparator();
            menu.Add(Copy.MenuForget, Icons.X, null, Copy.MenuForgetTip, () => actions.Forget(item));
            return menu;
        }

        static string Question(RecentItem item)
        {
            int n = item.Summary?.Notes ?? 0, f = item.Summary?.Frames ?? 0;
            if (n == 0 && f == 0) return Copy.DeleteAskEmpty;
            return string.Format(CultureInfo.InvariantCulture, Copy.DeleteAsk, Copy.Notes(n), Copy.Frames(f));
        }
    }

    /// <summary>
    /// The start screen without a video: a drop target card (a video glyph on a round tile, the title, the hint, the
    /// one primary button) and the recent list on a card under it. While the player starts, the button waits.
    /// </summary>
    public sealed class EmptyState : Grid
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new PanePeer(this);

        public readonly GlassButton OpenButton;
        readonly StackPanel recent = new StackPanel();
        readonly TextBlock recentHead;
        readonly Grid recentCard = new Grid();
        const double ListWidth = 560;

        public event Action<RecentRow>? RowMenu;
        public event Action<string>? OpenPath;

        public EmptyState()
        {
            AutomationProperties.SetAutomationId(this, "EmptyState");
            // Rows: the card, the heading, the recent list in the room that is left (it scrolls when it does not fit).
            var column = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 64, 0, 16), Width = ListWidth };
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition());
            var card = new Grid { Height = 232 };
            card.Children.Add(new ShadowLayer("card-drop", 22));
            card.Children.Add(new SurfaceVisual(22, "card-rim", null, "card-fill"));
            var dash = new System.Windows.Shapes.Rectangle { RadiusX = 14, RadiusY = 14, StrokeThickness = 1.5, StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 4 }, Margin = new Thickness(8), IsHitTestVisible = false };
            dash.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "T.stroke-2");
            card.Children.Add(dash);
            var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var disc = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), HorizontalAlignment = HorizontalAlignment.Center };
            disc.SetResourceReference(Border.BackgroundProperty, "T.fill-2");
            FrameworkElement glyph = Icons.Make(Icons.Video, 26);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "T.ink-2");
            disc.Child = glyph;
            inner.Children.Add(disc);
            TextBlock title = Typography.Text(Copy.EmptyTitle, Typography.Title);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.Margin = new Thickness(0, 14, 0, 0);
            inner.Children.Add(title);
            TextBlock hint = Typography.Text(Copy.EmptyHint, Typography.Caption, "ink-3");
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 4, 0, 16);
            inner.Children.Add(hint);
            OpenButton = new GlassButton(Copy.EmptyButton, Icons.Folder, ButtonKind.Tinted, ButtonSize.M) { HorizontalAlignment = HorizontalAlignment.Center };
            Tip.Set(OpenButton, Copy.EmptyButtonTip);
            AutomationProperties.SetAutomationId(OpenButton, "OpenVideo");
            inner.Children.Add(OpenButton);
            card.Children.Add(inner);
            column.Children.Add(card);
            recentHead = Typography.Text(Copy.RecentHeading, Typography.Section, "ink-3");
            recentHead.Margin = new Thickness(14, 26, 0, 8);
            Grid.SetRow(recentHead, 1);
            column.Children.Add(recentHead);
            recentCard.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(recentCard, 2);
            recentCard.Children.Add(new ShadowLayer("card-drop", Review.CardRadius));
            recentCard.Children.Add(new SurfaceVisual(Review.CardRadius, "card-rim", null, "card-fill"));
            var recentScroll = new ScrollViewer { Content = recent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, MaxHeight = 330, Padding = new Thickness(4) };
            AutomationProperties.SetAutomationId(recent, "RecentList");
            recentCard.Children.Add(recentScroll);
            column.Children.Add(recentCard);
            Children.Add(column);
        }

        public void SetBusy(bool busy)
        {
            OpenButton.Busy = busy;
            Tip.Set(OpenButton, busy ? Copy.Loading : Copy.EmptyButtonTip);
        }

        public void SetRecent(IList<RecentItem> items)
        {
            recent.Children.Clear();
            foreach (RecentItem item in items)
            {
                var row = new RecentRow(item, ListWidth - 8);
                row.Click += (s, e) => { if (item.Exists) OpenPath?.Invoke(item.Path); };
                row.Menu += r => RowMenu?.Invoke(r);
                recent.Children.Add(row);
            }
            recentHead.Visibility = recentCard.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
