using System;
using System.IO;

namespace Retake.Views
{
    /// <summary>Paths as short display text, and the test whether two spellings name one file (pure).</summary>
    public static class PathText
    {
        /// <summary>The last two parts of a folder (<c>Review\review-3</c>), with <c>…\</c> in front when there are more.</summary>
        public static string ShortFolder(string folder)
        {
            string[] parts = folder.TrimEnd('\\', '/').Split('\\', '/');
            if (parts.Length <= 3) return folder;
            return "…\\" + parts[parts.Length - 2] + "\\" + parts[parts.Length - 1];
        }

        /// <summary>
        /// True when both paths name one file: their full forms, <c>/</c> as <c>\</c>, case ignored. A path that has no
        /// full form (a hand-edited settings file: invalid characters, a colon inside) is compared as written and
        /// never throws; null is the same as nothing.
        /// </summary>
        public static bool Same(string? a, string? b)
        {
            if (a == null || b == null) return false;
            return string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
        }

        static string Key(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException) { full = path; }
            return full.Replace('/', '\\');
        }
    }
}
