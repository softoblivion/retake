using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Retake.Core;

namespace Retake.Player
{
    /// <summary>
    /// Brings libmpv into the process. The exe carries <c>libmpv-2.dll</c> as an embedded GZip resource (when the
    /// build found <c>build\libmpv\libmpv-2.dll</c>) together with the SHA-256 of the DLL. At start the DLL is
    /// written to <c>retake_data\bin\libmpv-2.dll</c> when it is missing or its SHA-256 differs, then loaded from
    /// that full path with <c>LoadLibrary</c> before any P/Invoke call. Slow (hashing 121 MB, unpacking on the
    /// first start): call it off the UI thread.
    /// </summary>
    public static class LibmpvLoader
    {
        public const string DllName = "libmpv-2.dll";
        public const string ResourceDll = "Retake.libmpv-2.dll.gz", ResourceHash = "Retake.libmpv-2.dll.sha256";
        public const string NotBundled = "libmpv is not bundled in this build";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);

        static IntPtr module;

        /// <summary>True when the exe carries the DLL.</summary>
        public static bool Bundled => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ResourceDll) != null;

        /// <summary>Extracts (when needed) and loads the DLL. Throws with a user-facing message on failure.</summary>
        public static void Load(string binDir)
        {
            if (module != IntPtr.Zero) return;
            Assembly asm = Assembly.GetExecutingAssembly();
            string? expected = ReadHash(asm);
            if (expected == null || !Bundled) throw new InvalidOperationException(NotBundled);
            string target = Path.Combine(binDir, DllName);
            bool current = File.Exists(target) && Sha256(target) == expected;
            if (!current)
            {
                Directory.CreateDirectory(binDir);
                string part = target + ".part";
                using (Stream? packed = asm.GetManifestResourceStream(ResourceDll))
                {
                    if (packed == null) throw new InvalidOperationException(NotBundled);
                    using (var gz = new GZipStream(packed, CompressionMode.Decompress))
                    using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                        gz.CopyTo(output, 1 << 20);
                }
                if (Sha256(part) != expected)
                {
                    File.Delete(part);
                    throw new InvalidOperationException("The bundled libmpv is damaged");
                }
                Paths.ReplaceFile(part, target);
                Log.Info("libmpv extracted to " + target);
            }
            module = LoadLibraryW(target);
            if (module == IntPtr.Zero)
                throw new InvalidOperationException("libmpv could not be loaded (" + new Win32Exception(Marshal.GetLastWin32Error()).Message + ")");
        }

        static string? ReadHash(Assembly asm)
        {
            using (Stream? s = asm.GetManifestResourceStream(ResourceHash))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s)) return r.ReadToEnd().Trim().ToLowerInvariant();
            }
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                byte[] hash = sha.ComputeHash(f);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
