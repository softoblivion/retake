using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Retake.Design.Controls;

namespace Retake.Design
{
    /// <summary>
    /// The control gallery (--gallery): every control of the layer in every state and size, the title bar, an
    /// open sheet over the veil with a toast, menus, hover card, toasts, the type scale and the main colours,
    /// in the current theme and scale. Each control kind shows its tooltip as a static bubble beside it, so a
    /// capture also reviews the copy. Labels marked as samples are placeholders for the review views.
    /// --capture renders the same content (with a title bar on top) to a PNG at the device scale.
    /// </summary>
    public static class Gallery
    {
        public const double PageWidth = 1280;
        static readonly string[] States = { "Rest", "Hover", "Pressed", "Focus", "Disabled", "Busy" };

        public static void Fill(ShellWindow window)
        {
            var scroller = new ScrollViewer { Content = Build(window.Prefs, window.StepFits), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            window.Root.Body.Children.Add(scroller);
        }

        /// <summary>Renders the gallery to a PNG and returns the process exit code.</summary>
        public static int Capture(LaunchOptions options)
        {
            Motion.Enabled = false;
            Rect primary = SystemParameters.WorkArea;
            var work = new Size(primary.Width, primary.Height);
            var page = new Grid { Width = PageWidth };
            page.Children.Add(new Scene());
            var column = new StackPanel();
            column.Children.Add(new TitleBar("Retake", AppVersion.Version));
            column.Children.Add(Build(options.Prefs, step => Scale.Fits(step, work)));
            page.Children.Add(column);
            double f = options.Prefs.UiScale / 100.0;
            var scaled = new Border { Child = page, LayoutTransform = f == 1 ? Transform.Identity : new ScaleTransform(f, f), UseLayoutRounding = true };
            var frame = new Border { Child = scaled };
            frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            frame.Arrange(new Rect(frame.DesiredSize));
            frame.UpdateLayout();
            frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            frame.Arrange(new Rect(frame.DesiredSize));
            frame.UpdateLayout();
            double k = Native.SystemDpi() / 96.0;
            int pw = (int)Math.Ceiling(frame.ActualWidth * k), ph = (int)Math.Ceiling(frame.ActualHeight * k);
            var bitmap = new RenderTargetBitmap(pw, ph, 96 * k, 96 * k, PixelFormats.Pbgra32);
            bitmap.Render(frame);
            string path = System.IO.Path.GetFullPath(options.Capture!);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream s = File.Create(path)) png.Save(s);
            if (options.Diag != null) File.WriteAllText(options.Diag, Diagnostics(pw, ph));
            return 0;
        }

        static string Diagnostics(int pw, int ph)
        {
            var b = new StringBuilder();
            b.AppendLine("capture " + pw + " x " + ph + " px, theme " + Themes.Current.Name);
            foreach (int w in new[] { 450, 550, 600, 650 })
                b.AppendLine("Sora " + w + ": embedded=" + Typography.Embedded(Typography.TextFamily, "Sora", FontWeight.FromOpenTypeWeight(w)) + " " + Typography.Describe(Typography.TextFamily, FontWeight.FromOpenTypeWeight(w)));
            b.AppendLine("Geist Mono 450: embedded=" + Typography.Embedded(Typography.MonoFamily, "Geist Mono", FontWeight.FromOpenTypeWeight(450)) + " " + Typography.Describe(Typography.MonoFamily, FontWeight.FromOpenTypeWeight(450)));
            foreach (ThemeSpec t in Themes.All)
                b.AppendLine("theme " + t.Name + ": " + t.Parsed.Count + " tokens read, unread: " + (t.Unread.Count == 0 ? "none" : string.Join(", ", t.Unread.ToArray())));
            return b.ToString();
        }

        /// <summary>The gallery content: sections on glass cards, 24 px from the edges.</summary>
        public static FrameworkElement Build(Prefs prefs, Func<int, bool> fits)
        {
            var page = new StackPanel { Margin = new Thickness(24, 8, 24, 32) };
            page.Children.Add(Section("Buttons · kinds × states", ButtonsTable()));
            page.Children.Add(Section("Sizes", SizesBlock()));
            page.Children.Add(Section("Inputs", InputsBlock(fits)));
            page.Children.Add(Section("Title bar", TitleBarBlock()));
            page.Children.Add(Section("Overlays", OverlaysBlock(prefs, fits)));
            page.Children.Add(Section("Review parts", ReviewBlock()));
            page.Children.Add(Section("Type and colour", TypeBlock()));
            return page;
        }

        // ---- Layout helpers -------------------------------------------------------------------------------

        static FrameworkElement Section(string title, FrameworkElement content)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            TextBlock head = Typography.Text(title, Typography.Section, "ink-3");
            head.Margin = new Thickness(6, 8, 0, 8);
            s.Children.Add(head);
            var card = new Grid();
            card.Children.Add(new ShadowLayer("shadow-m", 22));
            card.Children.Add(new SurfaceVisual(22, "stroke-1", "btn-bevel", "tint-regular"));
            content.Margin = new Thickness(18, 14, 18, 16);
            card.Children.Add(content);
            s.Children.Add(card);
            return s;
        }

        static TextBlock Caption(string text) => Typography.Text(text, Typography.Caption, "ink-3");

        static TextBlock Head(string text)
        {
            TextBlock t = Typography.Text(text, Typography.Section, "ink-4");
            t.Margin = new Thickness(0, 0, 18, 4);
            return t;
        }

        /// <summary>A table: a header row, then rows of a caption and cells; the last column is the tooltip.</summary>
        static Grid Table(string[] headers, IList<(string label, FrameworkElement?[] cells)> rows, double labelWidth = 96)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            for (int i = 0; i < headers.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < headers.Length; i++)
            {
                TextBlock h = Head(headers[i]);
                Grid.SetColumn(h, i + 1);
                g.Children.Add(h);
            }
            for (int r = 0; r < rows.Count; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock label = Caption(rows[r].label);
                label.TextWrapping = TextWrapping.Wrap;
                Grid.SetRow(label, r + 1);
                g.Children.Add(label);
                for (int c = 0; c < rows[r].cells.Length; c++)
                {
                    FrameworkElement? cell = rows[r].cells[c];
                    if (cell == null) continue;
                    cell.Margin = new Thickness(0, 7, 18, 7);
                    cell.HorizontalAlignment = HorizontalAlignment.Left;
                    cell.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(cell, r + 1);
                    Grid.SetColumn(cell, c + 1);
                    g.Children.Add(cell);
                }
            }
            return g;
        }

        static TipBubble Bubble(string text) => new TipBubble(text) { VerticalAlignment = VerticalAlignment.Center };

        /// <summary>A control pinned in one of the six button states.</summary>
        static T State<T>(T b, int state) where T : Pressable
        {
            switch (state)
            {
                case 1: b.ShowHover = true; break;
                case 2: b.ShowHover = true; b.ShowPressed = true; break;
                case 3: b.ShowFocus = true; break;
                case 4: b.IsEnabled = false; break;
                case 5: if (b is GlassButton g) g.Busy = true; break;
            }
            return b;
        }

        // ---- Sections --------------------------------------------------------------------------------------

        static FrameworkElement ButtonsTable()
        {
            var kinds = new (string row, string label, string icon, ButtonKind kind, string tip)[]
            {
                ("Plain", "Open", Icons.Open, ButtonKind.Plain, "Choose a video to review (Ctrl+O)"),
                ("Ghost", "Export", Icons.Export, ButtonKind.Ghost, "Save the notes as a Markdown file (Ctrl+E)"),
                ("Tinted", "Add note", Icons.Plus, ButtonKind.Tinted, "Write a note at this frame (N)"),
                ("Danger", "Delete", Icons.Trash, ButtonKind.Danger, "Remove this note for good (Del)"),
            };
            var rows = new List<(string, FrameworkElement?[])>();
            foreach (var k in kinds)
            {
                var cells = new FrameworkElement?[States.Length + 1];
                for (int s = 0; s < States.Length; s++) cells[s] = State(new GlassButton(k.label, k.icon, k.kind), s);
                cells[States.Length] = Bubble(k.tip);
                rows.Add((k.row, cells));
            }
            var headers = new List<string>(States) { "Tooltip (sample)" };
            var block = new StackPanel();
            block.Children.Add(Table(headers.ToArray(), rows));
            TextBlock note = Caption("Tinted marks the one primary action of a view. Labels and tooltips here are samples for the review views.");
            note.Margin = new Thickness(0, 8, 0, 0);
            block.Children.Add(note);
            return block;
        }

        static FrameworkElement SizesBlock()
        {
            var block = new StackPanel();
            var sizes = new[] { ButtonSize.L, ButtonSize.M, ButtonSize.S, ButtonSize.XS };
            string[] names = { "L · 40", "M · 32", "S · 28", "XS · 24" };
            var plain = new FrameworkElement?[4];
            var tinted = new FrameworkElement?[4];
            for (int i = 0; i < 4; i++)
            {
                plain[i] = new GlassButton(i == 0 ? "Open video" : "Open", i == 0 ? Icons.Video : Icons.Open, ButtonKind.Plain, sizes[i]);
                tinted[i] = new GlassButton("Add note", Icons.Plus, ButtonKind.Tinted, sizes[i]);
            }
            block.Children.Add(Table(names, new List<(string, FrameworkElement?[])> { ("Plain", plain), ("Tinted", tinted) }));

            var states = new[] { "Rest", "Hover", "Pressed", "Focus", "Disabled", "Tooltip (sample)" };
            var rows = new List<(string, FrameworkElement?[])>();
            foreach (var (row, kind, icon, tip) in new[] { ("Glyph · plate", IconKind.Plate, Icons.Play, "Play from here (Space)"), ("Glyph · bare", IconKind.Bare, Icons.Copy, "Copy the timecode (Ctrl+C)") })
            {
                var cells = new FrameworkElement?[6];
                for (int s = 0; s < 5; s++) cells[s] = State(new IconButton(icon, row, kind), s);
                cells[5] = Bubble(tip);
                rows.Add((row, cells));
            }
            var sizeCells = new FrameworkElement?[6];
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (ButtonSize s in new[] { ButtonSize.L, ButtonSize.M, ButtonSize.S })
                strip.Children.Add(new IconButton(Icons.Play, "Play", IconKind.Plate, s) { Margin = new Thickness(0, 0, 10, 0) });
            foreach (ButtonSize s in new[] { ButtonSize.L, ButtonSize.M, ButtonSize.S })
                strip.Children.Add(new IconButton(Icons.Copy, "Copy", IconKind.Bare, s) { Margin = new Thickness(0, 0, 10, 0) });
            sizeCells[0] = strip;
            rows.Add(("32 · 28 · 24", sizeCells));
            Grid glyphs = Table(states, rows);
            Grid.SetColumnSpan(strip, 3);
            glyphs.Margin = new Thickness(0, 14, 0, 0);
            block.Children.Add(glyphs);
            return block;
        }

        static FrameworkElement InputsBlock(Func<int, bool> fits)
        {
            var block = new StackPanel();
            // Text field
            TextField F(string text, string hint = "Name") => new TextField(hint, 150) { Text = text };
            var fFocus = F("Alex");
            fFocus.ShowFocus = true;
            var fOff = F("Alex");
            fOff.IsEnabled = false;
            block.Children.Add(Table(new[] { "Empty", "Filled", "Focus", "Disabled", "Tooltip" },
                new List<(string, FrameworkElement?[])> { ("Text field", new FrameworkElement?[] { F(""), F("Alex"), fFocus, fOff, Bubble(SettingsSheet.AuthorTip) }) }));

            // Switch
            Switch S(bool on) => new Switch(on, "Download updates automatically");
            var sPressed = S(true);
            sPressed.ShowPressed = true;
            var sFocus = S(true);
            sFocus.ShowFocus = true;
            var sOffDis = S(false);
            sOffDis.IsEnabled = false;
            var sOnDis = S(true);
            sOnDis.IsEnabled = false;
            Grid switches = Table(new[] { "Off", "On", "Pressed", "Focus", "Disabled off", "Disabled on", "Tooltip" },
                new List<(string, FrameworkElement?[])> { ("Switch", new FrameworkElement?[] { S(false), S(true), sPressed, sFocus, sOffDis, sOnDis, Bubble(SettingsSheet.AutoOnTip) }) });
            switches.Margin = new Thickness(0, 12, 0, 0);
            block.Children.Add(switches);

            // Segmented
            string[] filter = { "All", "Open", "Done" };
            string?[] filterTips = { "Show every note", "Show notes still to do", "Show notes marked done" };
            Segmented G(bool compact = false) => new Segmented("Filter", filter, filterTips, 0, compact);
            var gHover = G();
            gHover.ShowHover(1);
            var gFocus = G();
            gFocus.ShowFocus = true;
            var gDis = G();
            gDis.IsEnabled = false;
            var scaleSeg = new Segmented("Scale", Array.ConvertAll(Scale.Steps, Scale.Label), SettingsSheet.ScaleTips, 0, compact: true);
            for (int i = 0; i < Scale.Steps.Length; i++) scaleSeg.SetOff(i, !fits(Scale.Steps[i]) || Scale.Steps[i] == 200, SettingsSheet.ScaleOff);
            Grid segs = Table(new[] { "Rest", "Hover", "Focus", "Disabled", "Tooltip (sample)" },
                new List<(string, FrameworkElement?[])>
                {
                    ("Segmented", new FrameworkElement?[] { G(), gHover, gFocus, gDis, Bubble(filterTips[1]!) }),
                    ("Compact, steps off", new FrameworkElement?[] { scaleSeg, null, null, null, Bubble(SettingsSheet.ScaleOff) }),
                });
            Grid.SetColumnSpan(scaleSeg, 3);
            segs.Margin = new Thickness(0, 12, 0, 0);
            block.Children.Add(segs);
            return block;
        }

        static FrameworkElement TitleBarBlock()
        {
            var block = new StackPanel();
            var frame = new Grid { Height = 44, ClipToBounds = true };
            frame.Children.Add(new Border { CornerRadius = new CornerRadius(14) });
            var bar = new TitleBar("Retake", AppVersion.Version) { Margin = new Thickness(0) };
            frame.Children.Add(bar);
            block.Children.Add(frame);

            IconButton Gear() => new IconButton(Icons.Gear, "Settings", IconKind.Title) { TurnsOnHover = true };
            var gearHover = Gear();
            gearHover.ShowHover = true;
            var gearOpen = Gear();
            gearOpen.IsSource = true;
            var minHover = new IconButton(Icons.Minimize, "Minimize", IconKind.Title) { ShowHover = true };
            var closeHover = new IconButton(Icons.Close, "Close", IconKind.TitleClose) { ShowHover = true };
            var closePressed = new IconButton(Icons.Close, "Close", IconKind.TitleClose) { ShowHover = true, ShowPressed = true };
            var gearFocus = Gear();
            gearFocus.ShowFocus = true;
            Grid buttons = Table(new[] { "Gear", "Gear hover", "Gear, sheet open", "Focus", "Minimize hover", "Close hover", "Close pressed" },
                new List<(string, FrameworkElement?[])> { ("Window buttons", new FrameworkElement?[] { Gear(), gearHover, gearOpen, gearFocus, minHover, closeHover, closePressed }) });
            buttons.Margin = new Thickness(0, 14, 0, 0);
            block.Children.Add(buttons);

            var tips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(96, 2, 0, 0) };
            foreach (string t in new[] { TitleBar.GearTip, TitleBar.MinimizeTip, TitleBar.CloseTip })
            {
                TipBubble b = Bubble(t);
                b.Margin = new Thickness(0, 0, 12, 0);
                tips.Children.Add(b);
            }
            block.Children.Add(tips);

            VersionPill P(PillStage s) { var p = new VersionPill("v" + AppVersion.Version); p.SetStage(s); return p; }
            var pHover = P(PillStage.Idle);
            pHover.ShowHover = true;
            var pFocus = P(PillStage.Idle);
            pFocus.ShowFocus = true;
            Grid pills = Table(new[] { "Idle", "Hover", "Focus", "Available", "Downloading", "Ready", "Tooltip" },
                new List<(string, FrameworkElement?[])> { ("Version pill", new FrameworkElement?[] { P(PillStage.Idle), pHover, pFocus, P(PillStage.Available), P(PillStage.Downloading), P(PillStage.Ready), Bubble(TitleBar.PillTip) }) });
            pills.Margin = new Thickness(0, 12, 0, 0);
            block.Children.Add(pills);
            return block;
        }

        /// <summary>The updates popover, the note editor with its type chips, and note rows (a general one, a selected one).</summary>
        static FrameworkElement ReviewBlock()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var body = new StackPanel();
            body.Children.Add(Typography.Text("Retake", Typography.Title));
            TextBlock status = Typography.Text("You have the latest version", Typography.BodyMedium);
            status.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(status);
            var installed = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            TextBlock name = Typography.Text("Installed", Typography.Caption, "ink-3");
            name.Margin = new Thickness(0, 0, 14, 0);
            installed.Children.Add(name);
            installed.Children.Add(Typography.Text("v" + AppVersion.Version, Typography.Mono));
            body.Children.Add(installed);
            var line = new Border { Height = 1, Margin = new Thickness(-14, 14, -14, 10) };
            line.SetResourceReference(Border.BackgroundProperty, "T.line-soft");
            body.Children.Add(line);
            var footer = new DockPanel();
            var check = new GlassButton("Check for updates", null, ButtonKind.Plain, ButtonSize.S);
            DockPanel.SetDock(check, Dock.Right);
            footer.Children.Add(check);
            TextBlock when = Typography.Text("Checked 2 min ago", Typography.Caption, "ink-3");
            footer.Children.Add(when);
            body.Children.Add(footer);
            row.Children.Add(Labelled("Popover (the updates popover of the version pill)", new Popover(320) { Body = body }));

            var composer = new Views.Composer();
            composer.Open("FIX", "0:00:03:12", "Logo badge sits too close to the edge");
            FrameworkElement composerCell = Labelled("Note editor: the place, the type chips, the field", composer);
            composerCell.Margin = new Thickness(36, 0, 0, 0);
            row.Children.Add(composerCell);

            var list = new StackPanel { Width = 290 };
            list.Children.Add(new Views.NoteRow(new Retake.Core.NoteState { Id = "g", Frame = null, Kind = "ADD", Text = "End card with the logo and the date" }));
            list.Children.Add(new Views.NoteRow(new Retake.Core.NoteState { Id = "f", Frame = 87, Timecode = "0:00:03:12", Kind = "FIX", Text = "Logo badge sits too close to the edge" }) { Selected = true });
            FrameworkElement listCell = Labelled("Note rows: a general note, a selected note", list);
            listCell.Margin = new Thickness(36, 0, 0, 0);
            row.Children.Add(listCell);
            return row;
        }

        static FrameworkElement OverlaysBlock(Prefs prefs, Func<int, bool> fits)
        {
            var block = new StackPanel();
            // An open sheet over the veil with a toast, in a frame the size of a small window.
            var frame = new Grid { Height = 600, ClipToBounds = true };
            frame.Children.Add(new Scene());
            TextBlock behind = Typography.Text(Views.Copy.EmptyTitle, Typography.Body, "ink-3");
            behind.HorizontalAlignment = HorizontalAlignment.Center;
            frame.Children.Add(behind);
            var veil = new Rectangle();
            veil.SetResourceReference(Shape.FillProperty, "T.scrim");
            frame.Children.Add(veil);
            var copy = new Prefs { Theme = prefs.Theme, UiScale = prefs.UiScale, AutoDownloadUpdates = prefs.AutoDownloadUpdates, AuthorName = "Alex" };
            var settings = new SettingsSheet(copy, fits, s => { });
            settings.Sheet.Margin = new Thickness(0, 18, 0, 0);
            frame.Children.Add(settings.Sheet);
            var toast = new Toast(ToastLevel.Info, "Updated to v2.0.1") { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16) };
            frame.Children.Add(toast);
            var clip = new RectangleGeometry(new Rect(0, 0, 1196, 600), 18, 18);
            frame.Clip = clip;
            frame.SizeChanged += (s, e) => frame.Clip = new RectangleGeometry(new Rect(e.NewSize), 18, 18);
            block.Children.Add(Caption("Sheet over the veil, grown from the gear; a toast at the bottom (a click closes it)"));
            frame.Margin = new Thickness(0, 6, 0, 0);
            block.Children.Add(frame);

            // Menu, hover card, tooltip.
            var row = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var menu = new PopMenu();
            MenuEntry first = menu.Add("Copy timecode", Icons.Copy, "Ctrl+C", "Put the frame's timecode on the clipboard", null);
            first.PinHover = true;
            menu.Add("Export notes", Icons.Export, "Ctrl+E", "Save the notes as a Markdown file", null);
            MenuEntry off = menu.Add("Show in folder", Icons.Folder, null, "Open the notes folder in Explorer", null);
            off.IsEnabled = false;
            menu.AddSeparator();
            menu.Add("Delete note", Icons.Trash, "Del", "Remove this note for good", null, danger: true);
            row.Children.Add(Labelled("Menu (sample entries; hovered, disabled, danger)", Stack(menu, Bubble("Put the frame's timecode on the clipboard"))));

            var cardBody = new StackPanel();
            var thumb = new Grid { Width = 218, Height = 122 };
            thumb.Children.Add(new WellVisual(10, "well", "inset-well", "well-rim"));
            TextBlock code = Typography.Text("00:01:23:12", Typography.Mono, "ink-3");
            code.HorizontalAlignment = HorizontalAlignment.Center;
            thumb.Children.Add(code);
            cardBody.Children.Add(thumb);
            TextBlock cardTitle = Typography.Text("Logo lands two frames early", Typography.Label);
            cardTitle.Margin = new Thickness(2, 8, 0, 0);
            cardBody.Children.Add(cardTitle);
            TextBlock cardMeta = Typography.Text("Alex · 00:01:23:12", Typography.Caption, "ink-3");
            cardMeta.Margin = new Thickness(2, 2, 0, 0);
            cardBody.Children.Add(cardMeta);
            var card = new HoverCard(cardBody);
            FrameworkElement cardCell = Labelled("Hover card (sample marker)", card);
            cardCell.Margin = new Thickness(36, 0, 0, 0);
            Grid.SetColumn(cardCell, 1);
            row.Children.Add(cardCell);

            FrameworkElement tipCell = Labelled("Tooltip: what happens, then the key", Bubble("Mark a region on the frame (drag)"));
            tipCell.Margin = new Thickness(36, 0, 0, 0);
            Grid.SetColumn(tipCell, 2);
            row.Children.Add(tipCell);
            block.Children.Add(row);

            // Toasts.
            var toasts = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 18, 0, 0) };
            toasts.Children.Add(Caption("Toasts: info and success close after 3 s, warning and error after 10 s; a sticky toast stays until it is clicked"));
            var list = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            list.Children.Add(new Toast(ToastLevel.Success, "Notes exported") { Margin = new Thickness(0, 0, 12, 0) });
            list.Children.Add(new Toast(ToastLevel.Warning, "The clip has no audio") { Margin = new Thickness(0, 0, 12, 0) });
            list.Children.Add(new Toast(ToastLevel.Error, "The video could not be opened", "Try again", null));
            toasts.Children.Add(list);
            toasts.Children.Add(new Toast(ToastLevel.Success, "Retake 2.0.1 is ready", "Restart", null, sticky: true) { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
            block.Children.Add(toasts);
            return block;
        }

        static FrameworkElement Stack(FrameworkElement a, FrameworkElement b)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal };
            a.VerticalAlignment = VerticalAlignment.Top;
            b.VerticalAlignment = VerticalAlignment.Top;
            b.Margin = new Thickness(14, 4, 0, 0);
            s.Children.Add(a);
            s.Children.Add(b);
            return s;
        }

        static FrameworkElement Labelled(string caption, FrameworkElement content)
        {
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            TextBlock c = Caption(caption);
            c.Margin = new Thickness(0, 0, 0, 8);
            s.Children.Add(c);
            content.HorizontalAlignment = HorizontalAlignment.Left;
            s.Children.Add(content);
            return s;
        }

        static FrameworkElement TypeBlock()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var type = new StackPanel();
            var samples = new (TypeRole role, string text)[]
            {
                (Typography.Display, "Settings"),
                (Typography.Title, "Notes"),
                (Typography.Section, "Appearance"),
                (Typography.Body, "Background colour and accent of the window"),
                (Typography.Label, "Download updates automatically"),
                (Typography.LabelS, "Try again"),
                (Typography.Caption, "Checked every 5 minutes"),
                (Typography.Mono, "00:01:23:12"),
                (Typography.MonoS, "v" + AppVersion.Version),
            };
            foreach (var s in samples)
            {
                var line = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                line.ColumnDefinitions.Add(new ColumnDefinition());
                string spec = s.role.Name + " · " + s.role.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " + s.role.Weight.ToOpenTypeWeight() + (s.role.Caps ? ", capitals" : "");
                line.Children.Add(Caption(spec));
                TextBlock t = Typography.Text(s.text, s.role);
                Grid.SetColumn(t, 1);
                line.Children.Add(t);
                type.Children.Add(line);
            }
            grid.Children.Add(type);

            var colours = new StackPanel { Margin = new Thickness(24, 0, 0, 0) };
            colours.Children.Add(Swatches("Ink", "ink-1", "ink-2", "ink-3", "ink-4"));
            colours.Children.Add(Swatches("Fills", "fill-1", "fill-2", "fill-3", "fill-4", "well"));
            colours.Children.Add(Swatches("Accent", "accent", "accent-2", "accent-ink", "accent-grad", "focus-ring"));
            colours.Children.Add(Swatches("State", "mint", "amber", "rose", "ok-ink", "warn-ink", "error-ink"));
            Grid.SetColumn(colours, 1);
            grid.Children.Add(colours);
            return grid;
        }

        static FrameworkElement Swatches(string title, params string[] tokens)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            block.Children.Add(Head(title));
            var row = new WrapPanel();
            foreach (string token in tokens)
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 0), Width = 76 };
                var chip = new Grid { Height = 28 };
                chip.Children.Add(new SurfaceVisual(8, "stroke-2", null, token));
                cell.Children.Add(chip);
                TextBlock name = Typography.Text(token, Typography.MonoS, "ink-3");
                name.Margin = new Thickness(0, 4, 0, 0);
                cell.Children.Add(name);
                row.Children.Add(cell);
            }
            block.Children.Add(row);
            return block;
        }
    }
}
