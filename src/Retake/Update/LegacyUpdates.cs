using System;
using System.IO;
using System.Threading;
using SelfUpdate;

namespace Retake.Update
{
    /// <summary>
    /// The first start after an update from the 1.x line. The 1.x updater keeps its download, its marker and its log
    /// in <c>frame-notes_data\updates\</c> beside the exe and starts the downloaded exe in apply mode; the library's
    /// own cleanup and "Updated to" notice work in <c>retake_data\updates\</c>. This class hands the marker over (so
    /// the notice appears and the marker is removed by the library) and removes the 1.x downloads that are not newer
    /// than the running version. No WPF; the work is plain file operations that tolerate a file still held by the
    /// exiting apply helper.
    /// </summary>
    internal static class LegacyUpdates
    {
        const string Marker = "applied.json";

        /// <summary>The 1.x updates folder beside the exe's data folder, or null when the data folder was redirected by the environment.</summary>
        internal static string? FolderOf(string baseDir, bool environmentOverride)
        {
            return environmentOverride ? null : Path.Combine(baseDir, Retake.Core.Paths.LegacyAppDirName, "updates");
        }

        /// <summary>
        /// Moves the 1.x marker into <paramref name="updatesDir"/> unless a marker is already there. Run before the
        /// library's startup, which reads and removes the marker. True when a marker was moved.
        /// </summary>
        internal static bool AdoptMarker(string? legacyDir, string updatesDir)
        {
            if (legacyDir == null) return false;
            string from = Path.Combine(legacyDir, Marker);
            try
            {
                if (!File.Exists(from)) return false;
                string to = Path.Combine(updatesDir, Marker);
                if (File.Exists(to)) { File.Delete(from); return false; }
                Directory.CreateDirectory(updatesDir);
                File.Move(from, to);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Deletes the downloads (<c>Retake-X.Y.Z.exe</c>, <c>.part</c>) of the 1.x updates folder that are not newer
        /// than <paramref name="version"/>. Returns how many could not be deleted yet (a file held by the exiting helper).
        /// </summary>
        internal static int CleanDownloads(string? legacyDir, string stem, string extension, string version)
        {
            if (legacyDir == null || !Directory.Exists(legacyDir)) return 0;
            int left = 0;
            foreach (string file in Directory.GetFiles(legacyDir, stem + "-*"))
            {
                string name = Path.GetFileName(file);
                string tail = name.Substring(stem.Length + 1);
                if (tail.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) tail = tail.Substring(0, tail.Length - 5);
                if (!tail.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                string v = tail.Substring(0, tail.Length - extension.Length);
                if (SemVer.Parse(v) == null || SemVer.IsNewer(v, version)) continue;
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { left++; }
            }
            return left;
        }

        /// <summary>Repeats <see cref="CleanDownloads"/> in the background for about 10 s while the apply helper may still hold its file.</summary>
        internal static void CleanInBackground(string? legacyDir, string stem, string extension, string version)
        {
            if (legacyDir == null) return;
            var thread = new Thread(() =>
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    int left;
                    try { left = CleanDownloads(legacyDir, stem, extension, version); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return; }
                    if (left == 0) return;
                    Thread.Sleep(500);
                }
            }) { IsBackground = true, Name = "legacy updates cleanup" };
            thread.Start();
        }
    }
}
