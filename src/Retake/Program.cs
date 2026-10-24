using System;
using System.Windows;
using Retake.Design;

namespace Retake
{
    /// <summary>
    /// Entry point. The normal run opens the shell window with an empty content slot; --gallery opens the
    /// control gallery; --gallery --capture &lt;png&gt; renders the gallery to a file and exits (see LaunchOptions).
    /// </summary>
    public static class Program
    {
        public const string EmptyBody = "Nothing to review yet";

        [STAThread]
        public static int Main(string[] args)
        {
            LaunchOptions options = LaunchOptions.Parse(args);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            Look.Init(app, options.Prefs);
            if (options.Capture != null) return Gallery.Capture(options);
            var window = new ShellWindow(options.Prefs, options.Gallery ? "Retake gallery" : "Retake");
            if (options.Gallery) Gallery.Fill(window);
            else
            {
                var placeholder = Design.Typography.Text(EmptyBody, Design.Typography.Body, "ink-3");
                placeholder.HorizontalAlignment = HorizontalAlignment.Center;
                window.Root.Body.Children.Add(placeholder);
            }
            return app.Run(window);
        }
    }
}
