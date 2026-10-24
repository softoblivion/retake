using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Retake.Core;

namespace Retake.Tests
{
    /// <summary>Marks a public static method <c>void M(TestEnv env)</c> as a test; the name is the Python test case it ports.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
        public TestAttribute(string name) { Name = name; }
        public string Name { get; private set; }
    }

    public sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }

    /// <summary>Plain assertions.</summary>
    public static class Check
    {
        private static string Show(object? value)
        {
            if (value == null) return "null";
            if (value is string s) return "\"" + s.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
            if (value is IEnumerable e)
            {
                var parts = new List<string>();
                foreach (object? item in e) parts.Add(Show(item));
                return "[" + string.Join(", ", parts) + "]";
            }
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        private static bool Same(object? a, object? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is string || b is string) return a.Equals(b);
            if (a is IEnumerable ea && b is IEnumerable eb)
            {
                List<object?> la = ea.Cast<object?>().ToList();
                List<object?> lb = eb.Cast<object?>().ToList();
                if (la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++)
                {
                    if (!Same(la[i], lb[i])) return false;
                }
                return true;
            }
            return a.Equals(b);
        }

        public static void Equal(object? expected, object? actual, string? message = null)
        {
            if (!Same(expected, actual))
                throw new AssertException((message != null ? message + ": " : "") + "expected " + Show(expected) + " but was " + Show(actual));
        }

        public static void NotEqual(object? unexpected, object? actual, string? message = null)
        {
            if (Same(unexpected, actual))
                throw new AssertException((message != null ? message + ": " : "") + "did not expect " + Show(actual));
        }

        public static void True(bool condition, string? message = null)
        {
            if (!condition) throw new AssertException(message ?? "expected true");
        }

        public static void False(bool condition, string? message = null)
        {
            if (condition) throw new AssertException(message ?? "expected false");
        }

        public static void Null(object? value, string? message = null)
        {
            if (value != null) throw new AssertException((message != null ? message + ": " : "") + "expected null but was " + Show(value));
        }

        public static void NotNull(object? value, string? message = null)
        {
            if (value == null) throw new AssertException(message ?? "expected a value but was null");
        }

        public static void Near(double expected, double actual, int places = 6, string? message = null)
        {
            if (Math.Round(expected - actual, places) != 0)
                throw new AssertException((message != null ? message + ": " : "") + "expected " + expected.ToString("R") + " but was " + actual.ToString("R"));
        }

        public static void Contains(string needle, string haystack)
        {
            if (haystack.IndexOf(needle, StringComparison.Ordinal) < 0) throw new AssertException("expected " + Show(haystack) + " to contain " + Show(needle));
        }

        public static void NotContains(string needle, string haystack)
        {
            if (haystack.IndexOf(needle, StringComparison.Ordinal) >= 0) throw new AssertException("expected " + Show(haystack) + " not to contain " + Show(needle));
        }

        public static void EndsWith(string suffix, string text)
        {
            if (!text.EndsWith(suffix, StringComparison.Ordinal)) throw new AssertException("expected " + Show(text) + " to end with " + Show(suffix));
        }

        /// <summary>Runs <paramref name="action"/> and requires an exception of type T (or derived); returns it.</summary>
        public static T Throws<T>(Action action, string? message = null) where T : Exception
        {
            try
            {
                action();
            }
            catch (T ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new AssertException((message != null ? message + ": " : "") + "expected " + typeof(T).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
            }
            throw new AssertException((message != null ? message + ": " : "") + "expected " + typeof(T).Name + " but nothing was thrown");
        }

        /// <summary>Requires an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> (the C# counterparts of Python's OSError).</summary>
        public static void ThrowsFileError(Action action)
        {
            try
            {
                action();
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            throw new AssertException("expected a file error but nothing was thrown");
        }

        public static bool IsHex8(string value)
        {
            if (value.Length != 8) return false;
            return value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }
    }

    /// <summary>Per-test environment: a scratch folder, <c>RETAKE_HOME</c> pointing into it, and helpers for fixture files.</summary>
    public sealed class TestEnv : IDisposable
    {
        private readonly string? _oldHome;

        public TestEnv()
        {
            Tmp = Path.Combine(Path.GetTempPath(), "rt_" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(Tmp);
            _oldHome = Environment.GetEnvironmentVariable(Paths.EnvHome);
            Environment.SetEnvironmentVariable(Paths.EnvHome, Path.Combine(Tmp, "home"));
            Log.Listener = null;
            Log.Close();
        }

        /// <summary>The scratch folder of the test.</summary>
        public string Tmp { get; private set; }

        public string Combine(params string[] parts)
        {
            return Path.Combine(new[] { Tmp }.Concat(parts).ToArray());
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Paths.EnvHome, _oldHome);
            Log.Listener = null;
            Log.Close();
            try { ForceDelete(Tmp); } catch (Exception) { }
        }

        private static void ForceDelete(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(path, true);
        }
    }

    /// <summary>File helpers with the behaviour of Python's text mode on Windows (write LF as CRLF, read CRLF as LF).</summary>
    public static class Fx
    {
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Python <c>Path.write_text(text, encoding="utf-8")</c> on Windows: LF becomes CRLF.</summary>
        public static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Utf8.GetBytes(text.Replace("\n", "\r\n")));
        }

        /// <summary>Exact UTF-8 bytes of the text, no newline translation.</summary>
        public static void WriteRaw(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Utf8.GetBytes(text));
        }

        /// <summary>Python <c>Path.read_text(encoding="utf-8")</c>: CRLF and CR become LF.</summary>
        public static string ReadText(string path)
        {
            string text = Utf8.GetString(File.ReadAllBytes(path));
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static void WriteBytes(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        /// <summary>The sorted names of the entries directly inside a folder.</summary>
        public static List<string> Names(string folder)
        {
            return Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /// <summary>Relative path (with /) to bytes of every file below a folder, for tree comparison.</summary>
        public static SortedDictionary<string, byte[]> Tree(string root)
        {
            var map = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            if (!Directory.Exists(root)) return map;
            string prefix = root.TrimEnd('\\') + "\\";
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                map[file.Substring(prefix.Length).Replace('\\', '/')] = File.ReadAllBytes(file);
            }
            return map;
        }

        public static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(target, dir.Substring(source.TrimEnd('\\').Length + 1)));
            }
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(target, file.Substring(source.TrimEnd('\\').Length + 1)), true);
            }
        }
    }

    public static class Runner
    {
        public static int Run(string? filter)
        {
            var tests = new List<KeyValuePair<string, MethodInfo>>();
            foreach (Type type in typeof(Runner).Assembly.GetTypes())
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var attribute = (TestAttribute?)Attribute.GetCustomAttribute(method, typeof(TestAttribute));
                    if (attribute != null) tests.Add(new KeyValuePair<string, MethodInfo>(attribute.Name, method));
                }
            }
            tests.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            int passed = 0;
            int failed = 0;
            foreach (KeyValuePair<string, MethodInfo> test in tests)
            {
                if (filter != null && test.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    using (var env = new TestEnv())
                    {
                        test.Value.Invoke(null, new object[] { env });
                    }
                    passed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Exception inner = (ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException : ex;
                    Console.WriteLine("FAIL " + test.Key);
                    Console.WriteLine("     " + inner.GetType().Name + ": " + inner.Message);
                    if (!(inner is AssertException)) Console.WriteLine(inner.StackTrace);
                }
            }
            Console.WriteLine("Retake.Tests: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            return Runner.Run(args.Length > 0 ? args[0] : null);
        }
    }
}
