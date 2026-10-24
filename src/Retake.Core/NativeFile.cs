using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Retake.Core
{
    /// <summary>File operations that the BCL lacks: a move that replaces an existing target in one step.</summary>
    internal static class NativeFile
    {
        private const int MoveReplaceExisting = 0x1;
        private const int MoveWriteThrough = 0x8;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        /// <summary>
        /// Renames <paramref name="source"/> to <paramref name="target"/>, replacing the target when it exists
        /// (<c>os.replace</c>). Throws <see cref="UnauthorizedAccessException"/>, <see cref="FileNotFoundException"/>,
        /// <see cref="DirectoryNotFoundException"/> or <see cref="IOException"/> from the Win32 error.
        /// </summary>
        public static void Replace(string source, string target)
        {
            if (MoveFileEx(source, target, MoveReplaceExisting | MoveWriteThrough)) return;
            int error = Marshal.GetLastWin32Error();
            Exception? mapped = Marshal.GetExceptionForHR(unchecked((int)0x80070000) | error);
            if (mapped == null) mapped = new IOException("The file could not be moved (Win32 error " + error + ")");
            throw mapped;
        }
    }
}
