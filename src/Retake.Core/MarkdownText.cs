using System.Text;
using System.Text.RegularExpressions;

namespace Retake.Core
{
    /// <summary>
    /// Escaping of user text for the Markdown notes file, so a note reads as typed in CommonMark, GitHub, VS Code,
    /// Obsidian and Typora: no emphasis, link, code, HTML, table, maths, highlight, comment or emoji is formed by it.
    /// </summary>
    internal static class MarkdownText
    {
        private static readonly Regex Url = new Regex(@"https?://[^\s<>]+", RegexOptions.CultureInvariant);
        // The rest of an emoji shortcode after its first colon: a name with a letter (:tada:, :+1:, :-1:), then a colon.
        private static readonly Regex Shortcode = new Regex(@"\G(?:[+-]1|[A-Za-z0-9_+-]*[A-Za-z][A-Za-z0-9_+-]*):", RegexOptions.CultureInvariant);
        private static readonly Regex OrderedStart = new Regex(@"\A([0-9]{1,9})([.)])", RegexOptions.CultureInvariant);

        /// <summary>
        /// The escaped form of one line of text. Web addresses become <c>&lt;url&gt;</c> autolinks; every other
        /// character Markdown gives a meaning is escaped. <paramref name="lineStart"/>: the text begins a line, so the
        /// forms that only count there (a list marker, a numbered item) are escaped too.
        /// </summary>
        public static string Escape(string text, bool lineStart)
        {
            var sb = new StringBuilder(text.Length + 16);
            int at = 0;
            foreach (Match m in Url.Matches(text))
            {
                string url = TrimUrl(m.Value);
                EscapePlain(sb, text.Substring(at, m.Index - at), lineStart && at == 0);
                sb.Append('<').Append(url).Append('>');
                at = m.Index + url.Length;
            }
            EscapePlain(sb, text.Substring(at), lineStart && at == 0);
            return sb.ToString();
        }

        /// <summary>
        /// A web address without the punctuation that ends the sentence around it ("see https://x.com." links x.com): the
        /// sentence marks that GFM's extended autolinks also leave out, and a closing parenthesis without an opening one.
        /// </summary>
        private static string TrimUrl(string url)
        {
            int end = url.Length;
            while (end > 0)
            {
                char c = url[end - 1];
                if (TrailingMarks.IndexOf(c) >= 0) { end--; continue; }
                if (c == ')')
                {
                    string head = url.Substring(0, end);
                    if (Count(head, ')') > Count(head, '(')) { end--; continue; }
                }
                break;
            }
            return url.Substring(0, end);
        }

        private const string TrailingMarks = ".,;:!?'\"";

        private static int Count(string s, char c)
        {
            int n = 0;
            foreach (char x in s) if (x == c) n++;
            return n;
        }

        private static void EscapePlain(StringBuilder sb, string text, bool lineStart)
        {
            if (text.Length == 0) return;
            int start = 0;
            if (lineStart)
            {
                Match ordered = OrderedStart.Match(text);
                if (ordered.Success)
                {
                    sb.Append(ordered.Groups[1].Value).Append('\\').Append(ordered.Groups[2].Value);
                    start = ordered.Length;
                }
                else if (text[0] == '-' || text[0] == '+')
                {
                    sb.Append('\\').Append(text[0]);
                    start = 1;
                }
            }
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '\\':
                    case '`':
                    case '*':
                    case '[':
                    case ']':
                    case '<':
                    case '>':
                    case '|':
                    case '~':
                    case '$':
                    case '#':
                        sb.Append('\\').Append(c);
                        break;
                    case '&':
                        sb.Append("&amp;");
                        break;
                    case '_':
                        // Inside a word an underscore never emphasises (promo_v3 stays as it is); at a word edge it may.
                        bool inside = i > 0 && i < text.Length - 1 && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);
                        if (!inside) sb.Append('\\');
                        sb.Append(c);
                        break;
                    case ':':
                        // GitHub turns :name: into an emoji and ignores a backslash there; a word joiner (invisible,
                        // no line break) after the first colon keeps the text as typed. Colons of timecodes stay plain.
                        sb.Append(c);
                        if (Shortcode.IsMatch(text, i + 1)) sb.Append('\u2060');
                        break;
                    case '=':
                    case '%':
                        // Highlight (==x==) and comment (%%x%%) marks of Obsidian and Typora.
                        if (i + 1 < text.Length && text[i + 1] == c && (i == 0 || text[i - 1] != c)) sb.Append('\\');
                        sb.Append(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
        }

        /// <summary>A file name as a link target: the characters that end or change a link are percent-encoded.</summary>
        public static string EncodeLink(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                switch (c)
                {
                    case ' ': sb.Append("%20"); break;
                    case '(': sb.Append("%28"); break;
                    case ')': sb.Append("%29"); break;
                    case '<': sb.Append("%3C"); break;
                    case '>': sb.Append("%3E"); break;
                    case '%': sb.Append("%25"); break;
                    case '#': sb.Append("%23"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
