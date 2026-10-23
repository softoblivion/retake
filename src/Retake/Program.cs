using System;
using System.Windows;

namespace Retake
{
    /// <summary>Entry point. Placeholder until the window and the design layer exist.</summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new Window { Title = "Retake " + AppVersion.Version, Width = 1280, Height = 800, ResizeMode = ResizeMode.CanMinimize };
            return app.Run(window);
        }
    }
}
