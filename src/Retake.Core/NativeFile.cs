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
            if (MoveFileEx(LongPath(source), LongPath(target), MoveReplaceExisting | MoveWriteThrough)) return;
            int error = Marshal.GetLastWin32Error();
            Exception? mapped = Marshal.GetExceptionForHR(unchecked((int)0x80070000) | error);
            if (mapped == null) mapped = new IOException("The file could not be moved (Win32 error " + error + ")");
            throw mapped;
        }

        /// <summary>
        /// The extended-length form of a path (<c>\\?\C:\...</c>, <c>\\?\UNC\server\share\...</c>), so the native calls
        /// accept paths over 260 characters while the system's long-path policy is off. A path that already has a
        /// device prefix, or that cannot be made absolute, is returned as it is.
        /// </summary>
        internal static string LongPath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return path;
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return path; }
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + full.Substring(2);
            return @"\\?\" + full;
        }

        private const uint ReparseTagMountPoint = 0xA0000003;
        private const uint ReparseTagSymlink = 0xA000000C;
        private const uint ReparsePointAttribute = 0x400;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindData
        {
            public uint FileAttributes;
            public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint SizeHigh, SizeLow;
            public uint Reserved0;
            public uint Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string FileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string AlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string fileName, out FindData data);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr handle);

        /// <summary>
        /// True when <paramref name="path"/> is a symbolic link or a junction (not followed). Other reparse points, such
        /// as the placeholders of a cloud sync folder, count as ordinary files and folders.
        /// </summary>
        public static bool IsLink(string path)
        {
            FindData data;
            IntPtr handle = FindFirstFileW(LongPath(path.TrimEnd('\\')), out data);
            if (handle == InvalidHandle) return false;
            FindClose(handle);
            if ((data.FileAttributes & ReparsePointAttribute) == 0) return false;
            return data.Reserved0 == ReparseTagSymlink || data.Reserved0 == ReparseTagMountPoint;
        }
    }
}
