using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Retake.Core
{
    /// <summary>File operations that the BCL lacks: a move that replaces an existing target in one step, the move to the Recycle Bin, the link test.</summary>
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

        private const uint FileOpDelete = 3;
        // FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOF_NOERRORUI: no window, an error comes back
        // as the result. FOF_WANTNUKEWARNING overrides FOF_NOCONFIRMATION for an item the shell would delete for good
        // instead of recycling: the shell asks first, so a permanent delete never passes silently.
        private const ushort RecycleFlags = 0x0004 | 0x0010 | 0x0040 | 0x0200 | 0x0400 | 0x4000;

        // SHFILEOPSTRUCTW is packed to 1 byte in a 32-bit process and naturally aligned in a 64-bit one.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
        private struct FileOp32
        {
            public IntPtr Window;
            public uint Func;
            public string From;
            public string? To;
            public ushort Flags;
            public int Aborted;
            public IntPtr NameMappings;
            public string? ProgressTitle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileOp64
        {
            public IntPtr Window;
            public uint Func;
            public string From;
            public string? To;
            public ushort Flags;
            public int Aborted;
            public IntPtr NameMappings;
            public string? ProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        private static extern int FileOperation32(ref FileOp32 op);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
        private static extern int FileOperation64(ref FileOp64 op);

        /// <summary>
        /// Moves files and folders to the Recycle Bin with the shell's own operation in one call, with no window unless
        /// the shell would delete an item for good (then it asks). Returns the items that are gone afterwards; an item
        /// the shell kept, or a call it refused or the person cancelled, leaves its items out of the list.
        /// </summary>
        public static IList<string> Recycle(IList<string> paths)
        {
            if (paths.Count == 0) return new List<string>();
            // The list is double-NUL terminated: one NUL between the full paths, the marshaller adds the last one.
            string from = string.Join("\0", paths.Select(Path.GetFullPath)) + "\0";
            int result;
            bool aborted;
            if (IntPtr.Size == 8)
            {
                var op = new FileOp64 { Func = FileOpDelete, From = from, Flags = RecycleFlags };
                result = FileOperation64(ref op);
                aborted = op.Aborted != 0;
            }
            else
            {
                var op = new FileOp32 { Func = FileOpDelete, From = from, Flags = RecycleFlags };
                result = FileOperation32(ref op);
                aborted = op.Aborted != 0;
            }
            if (result != 0 || aborted) Log.Warning("the shell did not move every item to the Recycle Bin (code 0x" + result.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + (aborted ? ", aborted" : "") + ")");
            return paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
        }

        private const uint DriveFixed = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveTypeW(string root);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumePathNameW(string path, System.Text.StringBuilder volume, int length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, System.Text.StringBuilder name, int length);

        // SHQUERYRBINFO: naturally aligned in a 64-bit process, packed to 4 bytes (no gap after the size) in a 32-bit one.
        [StructLayout(LayoutKind.Sequential)]
        private struct BinInfo64
        {
            public int Size;
            public long Bytes;
            public long Items;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct BinInfo32
        {
            public int Size;
            public long Bytes;
            public long Items;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        private static extern int QueryRecycleBin64(string root, ref BinInfo64 info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        private static extern int QueryRecycleBin32(string root, ref BinInfo32 info);

        /// <summary>
        /// The mount point of the volume that holds <paramref name="fullPath"/> (<c>C:\</c>, or a folder a volume is
        /// mounted in), or null when there is none, such as on a drive letter with no volume behind it.
        /// </summary>
        internal static string? VolumeRoot(string fullPath)
        {
            var volume = new System.Text.StringBuilder(1024);
            return GetVolumePathNameW(fullPath, volume, volume.Capacity) ? volume.ToString() : null;
        }

        /// <summary>True when the volume at <paramref name="root"/> is a fixed drive (not removable, not a network drive).</summary>
        internal static bool IsFixedDrive(string root)
        {
            return GetDriveTypeW(root) == DriveFixed;
        }

        /// <summary>True when the shell reports a Recycle Bin for the volume at <paramref name="root"/>.</summary>
        internal static bool HasRecycleBin(string root)
        {
            if (IntPtr.Size == 8)
            {
                var info = new BinInfo64 { Size = Marshal.SizeOf(typeof(BinInfo64)) };
                return QueryRecycleBin64(root, ref info) == 0;
            }
            var info32 = new BinInfo32 { Size = Marshal.SizeOf(typeof(BinInfo32)) };
            return QueryRecycleBin32(root, ref info32) == 0;
        }

        /// <summary>The <c>{guid}</c> of the volume at <paramref name="root"/>, the key of its Recycle Bin settings; null when unknown.</summary>
        internal static string? VolumeGuid(string root)
        {
            var name = new System.Text.StringBuilder(64);
            if (!GetVolumeNameForVolumeMountPointW(root, name, name.Capacity)) return null;
            string text = name.ToString();
            int open = text.IndexOf('{'), close = text.IndexOf('}');
            return open >= 0 && close > open ? text.Substring(open, close - open + 1) : null;
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
