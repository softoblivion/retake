using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Retake.Design
{
    /// <summary>
    /// The kit's line icons (24 x 24 grid, 1.8 px stroke, round caps and joins). An icon takes the inherited
    /// text colour (TextElement.Foreground) of its container, so a button's ink colours its icon.
    /// </summary>
    public static class Icons
    {
        public const string Gear = "gear", Minimize = "min", Close = "close", X = "x", Plus = "plus", Minus = "minus", Check = "check",
            Warn = "warn", Info = "info", Error = "error", Retry = "retry", Folder = "folder", Open = "open", Play = "play", Stop = "stop",
            Trash = "trash", Chevron = "chevron", ChevronDown = "chevronDown", Down = "down", Clock = "clock", Video = "video",
            File = "file", Export = "export", Copy = "copy", Note = "note", Pause = "pause", StepBack = "stepBack",
            StepForward = "stepForward", Keyboard = "keyboard", Panel = "panel",
            Expand = "expand", Package = "package", Pen = "pen", Move = "move", Maximize = "maximize",
            Restore = "restore", Wrench = "wrench", PlusCircle = "plusCircle", MinusCircle = "minusCircle", ThumbUp = "thumbUp",
            ThumbDown = "thumbDown", Whole = "whole";

        static readonly Dictionary<string, string> Data = new Dictionary<string, string>
        {
            [Gear] = "M12 8.6a3.4 3.4 0 1 0 0 6.8 3.4 3.4 0 0 0 0-6.8Z M19.4 13.5a7.6 7.6 0 0 0 0-3l1.8-1.4-1.8-3.1-2.2.8a7.5 7.5 0 0 0-2.6-1.5L14.2 3h-3.6l-.4 2.3a7.5 7.5 0 0 0-2.6 1.5l-2.2-.8-1.8 3.1 1.8 1.4a7.6 7.6 0 0 0 0 3l-1.8 1.4 1.8 3.1 2.2-.8a7.5 7.5 0 0 0 2.6 1.5l.4 2.3h3.6l.4-2.3a7.5 7.5 0 0 0 2.6-1.5l2.2.8 1.8-3.1Z",
            [Minimize] = "M6 12h12",
            [Close] = "M7 7l10 10M17 7 7 17",
            [X] = "M7.5 7.5l9 9M16.5 7.5l-9 9",
            [Plus] = "M12 5.5v13M5.5 12h13",
            [Minus] = "M5.5 12h13",
            [Check] = "M5 12.5l4.5 4.5L19 7.5",
            [Warn] = "M12 4.5 2.8 19.5h18.4Z M12 10v4.2 M12 17.2v.1",
            [Info] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0 -17 0Z M12 11v5.5 M12 7.8v.1",
            [Error] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0 -17 0Z M9.2 9.2l5.6 5.6 M14.8 9.2l-5.6 5.6",
            [Retry] = "M4.5 12a7.5 7.5 0 0 1 13-5.1L19.5 9 M19.5 4.5V9H15 M19.5 12a7.5 7.5 0 0 1-13 5.1L4.5 15 M4.5 19.5V15H9",
            [Folder] = "M3.5 7.5A2 2 0 0 1 5.5 5.5h3.6l2 2.2h7.4a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2Z",
            [Open] = "M13.5 4.5h6v6 M19.5 4.5 11 13 M18 14v3.5a2 2 0 0 1-2 2H6.5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2H10",
            [Play] = "M8 5.8v12.4a1 1 0 0 0 1.5.9l9.7-6.2a1 1 0 0 0 0-1.7L9.5 4.9A1 1 0 0 0 8 5.8Z",
            [Stop] = "M8.9 6.5h6.2a2.4 2.4 0 0 1 2.4 2.4v6.2a2.4 2.4 0 0 1-2.4 2.4H8.9a2.4 2.4 0 0 1-2.4-2.4V8.9a2.4 2.4 0 0 1 2.4-2.4Z",
            [Trash] = "M4.5 7h15 M9.5 7V5.4A1.4 1.4 0 0 1 10.9 4h2.2a1.4 1.4 0 0 1 1.4 1.4V7 M6.5 7l.8 11.4A1.8 1.8 0 0 0 9.1 20h5.8a1.8 1.8 0 0 0 1.8-1.6L17.5 7",
            [Chevron] = "M9 6l6 6-6 6",
            [ChevronDown] = "M6 9l6 6 6-6",
            [Down] = "M12 4v11.5 M6.5 10.5l5.5 5.5 5.5-5.5 M5 20h14",
            [Clock] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0 -17 0Z M12 7.5V12l3 2",
            [Video] = "M6 5.5h12a3.5 3.5 0 0 1 3.5 3.5v6a3.5 3.5 0 0 1-3.5 3.5H6a3.5 3.5 0 0 1-3.5-3.5V9A3.5 3.5 0 0 1 6 5.5Z M10.3 9.6l4.2 2.4-4.2 2.4Z",
            [File] = "M7 3.5h6.5L18 8v11a1.5 1.5 0 0 1-1.5 1.5h-9.5A1.5 1.5 0 0 1 5.5 19V5A1.5 1.5 0 0 1 7 3.5Z M13 3.5V8.5h5",
            [Export] = "M12 14V3.5 M8 7.5l4-4 4 4 M4.5 13.5V17a2.5 2.5 0 0 0 2.5 2.5h10a2.5 2.5 0 0 0 2.5-2.5v-3.5",
            [Copy] = "M11.1 8.5h5.8a2.6 2.6 0 0 1 2.6 2.6v5.8a2.6 2.6 0 0 1-2.6 2.6h-5.8a2.6 2.6 0 0 1-2.6-2.6v-5.8a2.6 2.6 0 0 1 2.6-2.6Z M15.5 8.5V6.6a2.1 2.1 0 0 0-2.1-2.1H6.6a2.1 2.1 0 0 0-2.1 2.1v6.8a2.1 2.1 0 0 0 2.1 2.1h1.9",
            [Note] = "M6.5 4.5h11a2 2 0 0 1 2 2v7.2l-5.8 5.8H6.5a2 2 0 0 1-2-2v-11a2 2 0 0 1 2-2Z M13.7 19.5v-4a1.8 1.8 0 0 1 1.8-1.8h4 M8.5 9h7 M8.5 12.5h4",
            [Pause] = "M8.6 6.2v11.6 M15.4 6.2v11.6",
            [StepBack] = "M14.5 6.5 9 12l5.5 5.5",
            [StepForward] = "M9.5 6.5 15 12l-5.5 5.5",
            [Keyboard] = "M4.5 6.5h15a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 16V8a1.5 1.5 0 0 1 1.5-1.5Z M7 10.3h.1 M10.3 10.3h.1 M13.7 10.3h.1 M17 10.3h.1 M8.5 13.8h7",
            [Panel] = "M5.5 5h13a2.5 2.5 0 0 1 2.5 2.5v9a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 16.5v-9A2.5 2.5 0 0 1 5.5 5Z M15 5v14",
            [Expand] = "M4.5 9.5V6a1.5 1.5 0 0 1 1.5-1.5h3.5 M14.5 4.5H18a1.5 1.5 0 0 1 1.5 1.5v3.5 M19.5 14.5V18a1.5 1.5 0 0 1-1.5 1.5h-3.5 M9.5 19.5H6A1.5 1.5 0 0 1 4.5 18v-3.5",
            [Package] = "M4.5 8.2 12 4.3l7.5 3.9v7.6L12 19.7l-7.5-3.9Z M4.5 8.2 12 12.1l7.5-3.9 M12 12.1v7.6 M8.2 6.2l7.5 3.9",
            [Pen] = "M14.6 5.4l4 4L9.2 18.8H5.2v-4Z M12.6 7.4l4 4",
            [Move] = "M4.5 12h15 M8 8.5 4.5 12 8 15.5 M16 8.5l3.5 3.5-3.5 3.5",
            [Maximize] = "M7.5 6h9A1.5 1.5 0 0 1 18 7.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 6 16.5v-9A1.5 1.5 0 0 1 7.5 6Z",
            [Restore] = "M6.8 9h7.7a1.3 1.3 0 0 1 1.3 1.3V18a1.3 1.3 0 0 1-1.3 1.3H6.8A1.3 1.3 0 0 1 5.5 18v-7.7A1.3 1.3 0 0 1 6.8 9Z M9 6.2v-.4A1.3 1.3 0 0 1 10.3 4.5H18a1.3 1.3 0 0 1 1.3 1.3v7.7A1.3 1.3 0 0 1 18 14.8h-.4",
            [Wrench] = "M14.6 4.2a4.6 4.6 0 0 0-4.2 6.2l-5.7 5.7a2 2 0 0 0 2.8 2.8l5.7-5.7a4.6 4.6 0 0 0 6.2-4.2l-2.9 1.9-2.6-.6-.6-2.6Z",
            [PlusCircle] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0 -17 0Z M12 8.3v7.4 M8.3 12h7.4",
            [MinusCircle] = "M3.5 12a8.5 8.5 0 1 0 17 0a8.5 8.5 0 1 0 -17 0Z M8.3 12h7.4",
            [ThumbUp] = "M8 10.5 11.4 4.6a1.7 1.7 0 0 1 3.1 1.2l-.7 3.7h4.3a2 2 0 0 1 2 2.4l-1.3 6.1a2 2 0 0 1-2 1.6H8Z M4.5 10.5H8v9.1H4.5Z",
            [ThumbDown] = "M8 13.5 11.4 19.4a1.7 1.7 0 0 0 3.1-1.2l-.7-3.7h4.3a2 2 0 0 0 2-2.4l-1.3-6.1a2 2 0 0 0-2-1.6H8Z M4.5 13.5H8V4.4H4.5Z",
            [Whole] = "M5.5 5h13a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Z M3.5 9h17 M8 5v4 M16 5v4 M8.5 13h7 M8.5 16h4.5",
        };

        static readonly HashSet<string> Filled = new HashSet<string> { Play };
        static readonly Dictionary<string, Geometry> Cache = new Dictionary<string, Geometry>();

        public static Geometry Geometry(string name)
        {
            if (Cache.TryGetValue(name, out Geometry? g)) return g;
            g = System.Windows.Media.Geometry.Parse(Data.TryGetValue(name, out string? d) ? d : Data[Info]);
            g.Freeze();
            Cache[name] = g;
            return g;
        }

        /// <summary>An icon of the given size in pixels, drawn in the inherited text colour.</summary>
        public static FrameworkElement Make(string name, double size)
        {
            var path = new Path { Data = Geometry(name), Width = 24, Height = 24, Stretch = Stretch.None, IsHitTestVisible = false };
            var ink = new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self };
            if (Filled.Contains(name)) path.SetBinding(Shape.FillProperty, ink);
            else
            {
                path.StrokeThickness = 1.8;
                path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Round;
                path.StrokeLineJoin = PenLineJoin.Round;
                path.SetBinding(Shape.StrokeProperty, ink);
            }
            return new Viewbox
            {
                Width = size,
                Height = size,
                Child = new Canvas { Width = 24, Height = 24, Children = { path } },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
    }
}
