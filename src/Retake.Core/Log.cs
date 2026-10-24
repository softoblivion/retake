using System;
using System.IO;
using System.Text;

namespace Retake.Core
{
    /// <summary>
    /// The application log: a size-rotating text file (<c>app.log</c>, then <c>app.log.1</c> to <c>app.log.3</c>). Without
    /// <see cref="Initialize"/> messages only reach <see cref="Listener"/>. Logging never throws.
    /// </summary>
    public static class Log
    {
        /// <summary>Size at which <c>app.log</c> is rotated.</summary>
        public const long MaxBytes = 1000000;

        /// <summary>Number of rotated files kept beside the current one.</summary>
        public const int Keep = 3;

        private static readonly object Gate = new object();
        private static string? _file;

        /// <summary>The log file, or null before <see cref="Initialize"/>.</summary>
        public static string? FilePath { get { lock (Gate) { return _file; } } }

        /// <summary>Called for every message with the level (<c>INFO</c>, <c>WARNING</c>, <c>ERROR</c>) and the text; used by tests and diagnostics.</summary>
        public static Action<string, string>? Listener { get; set; }

        /// <summary>Starts writing to <c>app.log</c> in the given folder, creating the folder.</summary>
        public static void Initialize(string directory)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    _file = Path.Combine(directory, "app.log");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _file = null;
                }
            }
        }

        /// <summary>Stops writing to a file (messages still reach <see cref="Listener"/>).</summary>
        public static void Close()
        {
            lock (Gate) { _file = null; }
        }

        public static void Info(string message) { Write("INFO", message); }

        public static void Warning(string message) { Write("WARNING", message); }

        public static void Error(string message) { Write("ERROR", message); }

        /// <summary>Logs an error with the type and message of an exception.</summary>
        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message + ": " + exception.GetType().Name + ": " + exception.Message);
        }

        private static void Write(string level, string message)
        {
            Action<string, string>? listener = Listener;
            if (listener != null)
            {
                try { listener(level, message); } catch (Exception) { }
            }
            lock (Gate)
            {
                if (_file == null) return;
                try
                {
                    Rotate();
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + message.Replace("\r", " ").Replace("\n", " ") + "\n";
                    File.AppendAllText(_file, line, new UTF8Encoding(false));
                }
                catch (Exception) { }
            }
        }

        private static void Rotate()
        {
            string file = _file!;
            var info = new FileInfo(file);
            if (!info.Exists || info.Length < MaxBytes) return;
            try { File.Delete(file + "." + Keep); } catch (Exception) { }
            for (int i = Keep - 1; i >= 1; i--)
            {
                string from = file + "." + i;
                if (File.Exists(from))
                {
                    try { NativeFile.Replace(from, file + "." + (i + 1)); } catch (Exception) { }
                }
            }
            try { NativeFile.Replace(file, file + ".1"); } catch (Exception) { }
        }
    }
}
