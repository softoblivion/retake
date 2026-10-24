using System;
using System.IO;
using System.Windows;
using Retake.Core;
using Retake.Design;
using Retake.Views;

namespace Retake
{
    /// <summary>
    /// Entry point. The normal run opens the review window (a video path as the first argument opens it);
    /// --fake-player uses generated frames instead of libmpv; --gallery opens the control gallery; --gallery
    /// --capture &lt;png&gt; renders the gallery to a file and exits (see LaunchOptions). Several windows may run at
    /// once, each its own review.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            LaunchOptions options = LaunchOptions.Parse(args);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            if (options.Gallery)
            {
                Look.Init(app, options.Prefs);
                if (options.Capture != null) return Gallery.Capture(options);
                var gallery = new ShellWindow(options.Prefs, "Retake gallery");
                Gallery.Fill(gallery);
                return app.Run(gallery);
            }

            var paths = new Paths();
            try { paths.Ensure(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            paths.ImportLegacySettings();
            Log.Initialize(paths.LogDir);
            Log.Info("Retake " + AppVersion.Version + " started" + (options.FakePlayer ? " with the fake player" : ""));
            var settings = new Settings(paths.SettingsFile);
            settings.Load();
            options.UseSettings(settings);

            Look.Init(app, options.Prefs);
            var window = new ShellWindow(options.Prefs, "Retake");
            var review = new Review(window, settings, paths, options.FakePlayer);
            if (options.Video != null) review.OpenWhenReady(Path.GetFullPath(options.Video));
            app.DispatcherUnhandledException += (s, e) => Log.Error("unhandled error", e.Exception);
            int code = app.Run(window);
            Log.Info("Retake closed");
            Log.Close();
            return code;
        }
    }
}
