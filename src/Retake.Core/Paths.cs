using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Retake.Core
{
    /// <summary>
    /// Location of the runtime data folder and atomic file writes. Nothing outside this class builds a path inside the
    /// data folder. The environment is read when the object is created.
    /// </summary>
    public sealed class Paths
    {
        /// <summary>Name of the data folder beside the executable.</summary>
        public const string AppDirName = "retake_data";

        /// <summary>Name of the data folder of the 1.x line, whose settings file is imported once.</summary>
        public const string LegacyAppDirName = "frame-notes_data";

        /// <summary>Environment variable that replaces the data folder.</summary>
        public const string EnvHome = "RETAKE_HOME";

        /// <summary>Creates the paths from <c>RETAKE_HOME</c> when it is set and not blank, else from <c>retake_data\</c> beside the executable.</summary>
        public Paths() : this(null) { }

        /// <summary>Creates the paths of an explicit data folder; null reads the environment as the parameterless constructor does.</summary>
        public Paths(string? home)
        {
            if (home == null)
            {
                string env = (Environment.GetEnvironmentVariable(EnvHome) ?? "").Trim();
                if (env.Length > 0) { home = env; UsesEnvironmentOverride = true; }
                else home = Path.Combine(BaseDir, AppDirName);
            }
            Home = home;
            SettingsFile = Path.Combine(Home, "settings.json");
            LogDir = Path.Combine(Home, "logs");
            LogFile = Path.Combine(LogDir, "app.log");
            UpdatesDir = Path.Combine(Home, "updates");
            BinDir = Path.Combine(Home, "bin");
        }

        /// <summary>The data folder.</summary>
        public string Home { get; private set; }

        /// <summary><c>settings.json</c> in the data folder.</summary>
        public string SettingsFile { get; private set; }

        /// <summary>The <c>logs</c> folder.</summary>
        public string LogDir { get; private set; }

        /// <summary><c>logs\app.log</c>.</summary>
        public string LogFile { get; private set; }

        /// <summary>The <c>updates</c> folder.</summary>
        public string UpdatesDir { get; private set; }

        /// <summary>The <c>bin</c> folder that holds the extracted native components.</summary>
        public string BinDir { get; private set; }

        /// <summary>True when the data folder came from <c>RETAKE_HOME</c>.</summary>
        public bool UsesEnvironmentOverride { get; private set; }

        /// <summary>The folder that holds <c>retake_data\</c>: the executable folder, or the project root when the executable runs from a <c>bin</c> folder.</summary>
        public static string BaseDir
        {
            get { return ResolveBaseDir(AppDomain.CurrentDomain.BaseDirectory); }
        }

        /// <summary>
        /// Maps an executable folder to the folder that holds the data folder: the folder itself, or the parent of the
        /// <c>bin</c> folder when the folder is named <c>bin</c> or sits directly inside one (<c>bin\Release</c>).
        /// </summary>
        public static string ResolveBaseDir(string exeDir)
        {
            string dir = exeDir.TrimEnd('\\', '/');
            if (dir.Length == 0) return exeDir;
            if (IsBinName(Path.GetFileName(dir)))
            {
                string up = Path.GetDirectoryName(dir) ?? dir;
                return up;
            }
            string? parent = Path.GetDirectoryName(dir);
            if (parent != null && IsBinName(Path.GetFileName(parent)))
            {
                string? up = Path.GetDirectoryName(parent);
                if (up != null) return up;
            }
            return dir;
        }

        private static bool IsBinName(string name)
        {
            return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Creates the data folder and its <c>logs</c> and <c>updates</c> sub-folders.</summary>
        public void Ensure()
        {
            Directory.CreateDirectory(Home);
            Directory.CreateDirectory(LogDir);
            Directory.CreateDirectory(UpdatesDir);
        }

        /// <summary>
        /// Copies <c>settings.json</c> of the 1.x data folder (<c>frame-notes_data\</c> beside the executable) into the data
        /// folder once. Does nothing when the data folder came from <c>RETAKE_HOME</c>.
        /// </summary>
        public bool ImportLegacySettings()
        {
            if (UsesEnvironmentOverride) return false;
            return ImportLegacySettings(Path.Combine(BaseDir, LegacyAppDirName));
        }

        /// <summary>
        /// Copies <c>settings.json</c> of the given 1.x data folder into the data folder when the data folder does not
        /// exist yet and the legacy file does. True when the file was copied. The legacy folder is never changed.
        /// </summary>
        public bool ImportLegacySettings(string legacyHome)
        {
            string legacyFile = Path.Combine(legacyHome, "settings.json");
            if (Directory.Exists(Home) || !File.Exists(legacyFile)) return false;
            try
            {
                Directory.CreateDirectory(Home);
                AtomicWriteBytes(SettingsFile, File.ReadAllBytes(legacyFile));
                Log.Info("imported the settings of " + legacyFile);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning("the settings of " + legacyFile + " were not imported: " + ex.Message);
                return false;
            }
        }

        /// <summary>Writes <paramref name="data"/> to <c>&lt;name&gt;.part</c> next to <paramref name="path"/>, then replaces <paramref name="path"/> with it. A failure removes the part file and rethrows.</summary>
        public static void AtomicWriteBytes(string path, byte[] data)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string part = path + ".part";
            try
            {
                using (var stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                ReplaceFile(part, path);
            }
            catch
            {
                try { File.Delete(part); } catch (Exception) { }
                throw;
            }
        }

        /// <summary>Writes UTF-8 text without a byte order mark; line endings are kept exactly as given.</summary>
        public static void AtomicWriteText(string path, string text)
        {
            AtomicWriteBytes(path, new UTF8Encoding(false).GetBytes(text));
        }

        /// <summary>Writes a JSON value as indented UTF-8 with a trailing LF.</summary>
        public static void AtomicWriteJson(string path, object? value)
        {
            AtomicWriteText(path, Json.Write(value) + "\n");
        }

        /// <summary>
        /// Moves <paramref name="source"/> over <paramref name="target"/> with a short retry: Windows refuses while another
        /// process (a scanner, an indexer) holds the target.
        /// </summary>
        public static void ReplaceFile(string source, string target)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    NativeFile.Replace(source, target);
                    return;
                }
                catch (Exception ex) when (attempt < 5 && IsRetryable(ex))
                {
                    Thread.Sleep(50 * (attempt + 1));
                }
            }
        }

        private static bool IsRetryable(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return true;
            return ex is IOException && !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException);
        }
    }
}
