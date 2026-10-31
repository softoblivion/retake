using System;
using System.Collections.Generic;
using System.IO;

namespace Retake.Views
{
    /// <summary>
    /// Values made from files (thumbnails), kept while the file stays the same: an entry is keyed by the path and a
    /// size, and is made again when the file's last write time or length changed. The least recently used entry goes
    /// when the cache is full. A missing file gives the default value and is not kept. One thread (the UI thread).
    /// </summary>
    public sealed class FileCache<T> where T : class
    {
        sealed class Entry
        {
            public string Key = "";
            public long Stamp, Length;
            public T? Value;
        }

        readonly int capacity;
        readonly Dictionary<string, LinkedListNode<Entry>> map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<Entry> order = new LinkedList<Entry>();

        public FileCache(int capacity)
        {
            this.capacity = Math.Max(1, capacity);
        }

        /// <summary>Number of entries kept.</summary>
        public int Count => map.Count;

        /// <summary>The value of <paramref name="path"/> at <paramref name="size"/>: the kept one while the file is unchanged, else <paramref name="make"/>'s (null results are kept too, so an unreadable file is not read again until it changes).</summary>
        public T? Get(string? path, int size, Func<string, T?> make)
        {
            if (string.IsNullOrEmpty(path)) return null;
            long stamp, length;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) { Forget(path!, size); return null; }
                stamp = info.LastWriteTimeUtc.Ticks;
                length = info.Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }
            string key = path + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                order.Remove(node);
                order.AddFirst(node);
                if (node.Value.Stamp == stamp && node.Value.Length == length) return node.Value.Value;
                node.Value.Value = make(path!);
                node.Value.Stamp = stamp;
                node.Value.Length = length;
                return node.Value.Value;
            }
            var entry = new Entry { Key = key, Stamp = stamp, Length = length, Value = make(path!) };
            map[key] = order.AddFirst(entry);
            while (map.Count > capacity)
            {
                LinkedListNode<Entry> last = order.Last!;
                order.RemoveLast();
                map.Remove(last.Value.Key);
            }
            return entry.Value;
        }

        void Forget(string path, int size)
        {
            string key = path + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!map.TryGetValue(key, out LinkedListNode<Entry>? node)) return;
            order.Remove(node);
            map.Remove(key);
        }
    }
}
