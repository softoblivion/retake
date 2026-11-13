using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Retake.Core
{
    /// <summary>The way files leave for the Recycle Bin: the shell in the app, a stand-in in the tests.</summary>
    public interface IRecycler
    {
        /// <summary>The items among <paramref name="paths"/> that the Recycle Bin cannot take, so that a move there would delete them for good.</summary>
        IList<string> NotRecyclable(IList<string> paths);

        /// <summary>Moves the items to the Recycle Bin and returns the ones that are gone afterwards.</summary>
        IList<string> Recycle(IList<string> paths);
    }

    /// <summary>
    /// The Windows Recycle Bin. An item counts as recyclable only when every sign says the bin takes it: no policy turns
    /// the bin off, the item is on a fixed drive (a network share, a mapped drive and a removable drive have no bin of
    /// their own), the shell reports a bin for the drive, the drive's bin is not set to remove files at once, and the
    /// item is no larger than the bin's maximum size (a larger item is deleted for good). The move itself also asks the
    /// shell to warn before any permanent delete, for a case these signs miss.
    /// </summary>
    public sealed class ShellRecycler : IRecycler
    {
        public static readonly ShellRecycler Instance = new ShellRecycler();

        private const string BinKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\";
        private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        public IList<string> NotRecyclable(IList<string> paths)
        {
            return paths.Where(p => !CanRecycle(p)).ToList();
        }

        public IList<string> Recycle(IList<string> paths)
        {
            return NativeFile.Recycle(paths);
        }

        /// <summary>True when the Recycle Bin takes <paramref name="path"/> (see the class summary); false on any doubt.</summary>
        public static bool CanRecycle(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return false; }
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            try
            {
                if (PolicySet(Registry.CurrentUser) || PolicySet(Registry.LocalMachine)) return false;
                string? root = NativeFile.VolumeRoot(full);
                if (root == null || !NativeFile.IsFixedDrive(root) || !NativeFile.HasRecycleBin(root)) return false;
                string? guid = NativeFile.VolumeGuid(root);
                if (guid == null) return true;
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(BinKey + guid))
                {
                    if (key == null) return true;
                    if (key.GetValue("NukeOnDelete") is int nuke && nuke != 0) return false;
                    if (key.GetValue("MaxCapacity") is int megabytes && SizeOf(full) > megabytes * 1024L * 1024L) return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                return false;
            }
        }

        private static bool PolicySet(RegistryKey hive)
        {
            using (RegistryKey? key = hive.OpenSubKey(PolicyKey))
            {
                return key?.GetValue("NoRecycleFiles") is int value && value != 0;
            }
        }

        /// <summary>The bytes of a file, or of every file in a folder and its sub-folders; 0 when it cannot be read.</summary>
        private static long SizeOf(string full)
        {
            try
            {
                if (File.Exists(full)) return new FileInfo(full).Length;
                if (!Directory.Exists(full)) return 0;
                return new DirectoryInfo(full).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
