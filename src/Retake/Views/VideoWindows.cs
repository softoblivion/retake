using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Retake.Views
{
    /// <summary>
    /// Which window has which video open, across the processes of the machine: a window that opens a video marks its
    /// native window with a property named after the video (<see cref="Key"/>), and another window that is about to
    /// open the same video finds that mark and brings the window to the front instead. The mark goes when the video
    /// closes or the window closes; Windows drops it with the window when a process ends.
    /// </summary>
    public static class VideoWindows
    {
        const string Prefix = "Retake.Video.";
        const int SwRestore = 9;
        static string? claimed;

        delegate bool EnumProc(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetProp(IntPtr hwnd, string name, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetProp(IntPtr hwnd, string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr RemoveProp(IntPtr hwnd, string name);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc proc, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);

        /// <summary>
        /// The name of the mark of a video: the prefix and a 64-bit hash of its full path in lower case, so two spellings
        /// of one path (case, <c>/</c>, a relative form) give one name. A path with no full form keys as written.
        /// </summary>
        public static string Key(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { full = path; }
            ulong hash = 14695981039346656037UL;
            foreach (char c in full.Replace('/', '\\').ToLowerInvariant())
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return Prefix + hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Marks <paramref name="hwnd"/> as the window of <paramref name="path"/> (null: of no video), replacing its previous mark.</summary>
        public static void Claim(IntPtr hwnd, string? path)
        {
            if (hwnd == IntPtr.Zero) return;
            if (claimed != null) RemoveProp(hwnd, claimed);
            claimed = null;
            if (path == null) return;
            string key = Key(path);
            if (SetProp(hwnd, key, new IntPtr(1))) claimed = key;
        }

        /// <summary>The top-level window other than <paramref name="except"/> that has <paramref name="path"/> open, or <see cref="IntPtr.Zero"/>.</summary>
        public static IntPtr Find(string path, IntPtr except)
        {
            string key = Key(path);
            IntPtr found = IntPtr.Zero;
            EnumWindows((hwnd, param) =>
            {
                if (hwnd == except || GetProp(hwnd, key) == IntPtr.Zero) return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Restores the window when it is minimized and brings it to the front.</summary>
        public static void BringToFront(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        }
    }
}
