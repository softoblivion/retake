using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Retake.Core
{
    /// <summary>One note: a frame, a kind, a one-line text, an optional region and its frame image.</summary>
    public sealed class Note
    {
        /// <summary>The note kinds, in the order the summaries list them.</summary>
        public static readonly string[] Kinds = { "FIX", "ADD", "REMOVE", "LIKE", "DISLIKE" };

        public Note(string id, int? frame, string kind, string text)
        {
            Id = id;
            Frame = frame;
            Kind = kind;
            Text = text;
        }

        /// <summary>8 lowercase hex characters; not written to the file.</summary>
        public string Id { get; set; }

        /// <summary>Zero-based frame number; null for a general note about the whole video.</summary>
        public int? Frame { get; set; }

        /// <summary>True for a general note (no frame, no region, no image).</summary>
        public bool IsGeneral { get { return Frame == null; } }

        /// <summary>One of <see cref="Kinds"/>.</summary>
        public string Kind { get; set; }

        /// <summary>One line of text.</summary>
        public string Text { get; set; }

        /// <summary>The marked region, or null.</summary>
        public Region? Region { get; set; }

        /// <summary>File name of the frame image inside the frames folder (no path), or null.</summary>
        public string? Image { get; set; }

        /// <summary>Creation sequence; orders the general notes and breaks ties between notes on one frame.</summary>
        public int Order { get; set; }

        /// <summary>One-line form of a note text: tabs, line breaks and the Unicode line separators become spaces, ends stripped.</summary>
        public static string NormalizeText(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\t':
                    case '\r':
                    case '\n':
                    case '\u000b':
                    case '\u000c':
                    case '\u0085':
                    case (char)0x2028:
                    case (char)0x2029:
                        sb.Append(' ');
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return Py.Strip(sb.ToString());
        }

        /// <summary>A random note id: 8 lowercase hex characters, not in <paramref name="taken"/>.</summary>
        public static string NewId(ISet<string>? taken)
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
            {
                while (true)
                {
                    rng.GetBytes(bytes);
                    var sb = new StringBuilder(8);
                    foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                    string value = sb.ToString();
                    if (taken == null || !taken.Contains(value)) return value;
                }
            }
        }
    }
}
