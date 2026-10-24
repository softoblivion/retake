using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Retake.Core
{
    /// <summary>The notes file cannot be used; the message is a user-facing English sentence.</summary>
    public sealed class NotesFormatException : Exception
    {
        public NotesFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// The content of a notes data file (format version 1): the header, the notes and the lines that did not parse.
    /// <see cref="Parse"/> and <see cref="Render"/> are exact inverses for a file this core wrote.
    /// </summary>
    public sealed class NotesFile
    {
        /// <summary>The format version this core reads and writes.</summary>
        public const int FormatVersion = 1;

        /// <summary>Name of the frame images folder inside the notes folder.</summary>
        public const string FramesDirName = "frames";

        private static readonly string Separator = " " + (char)0xB7 + " ";

        private static readonly Regex VersionPattern = new Regex(@"\A#\s*Frame Notes\s+([0-9]+)\s*\z", RegexOptions.CultureInvariant);
        private static readonly Regex HeaderPattern = new Regex(@"\A#\s*([A-Za-z_]+)\s*:\s*(.*)\z", RegexOptions.CultureInvariant);

        public NotesFile(string source, double fps, int? durationFrames)
        {
            Source = source;
            Fps = fps;
            DurationFrames = durationFrames;
        }

        /// <summary>File name of the video (no path).</summary>
        public string Source { get; set; }

        /// <summary>Frame rate of the timecodes in the file.</summary>
        public double Fps { get; set; }

        /// <summary>Length of the video in frames, or null when the file has no duration line.</summary>
        public int? DurationFrames { get; set; }

        /// <summary>The notes in file order.</summary>
        public List<Note> Notes { get; private set; } = new List<Note>();

        /// <summary>Lines that did not parse, written back after the notes.</summary>
        public List<string> Extra { get; private set; } = new List<string>();

        private static IEnumerable<Note> Sorted(IEnumerable<Note> notes)
        {
            return notes.OrderBy(n => n.Frame).ThenBy(n => n.Order);
        }

        /// <summary>Parses the text of a notes file (a leading byte order mark and CRLF line ends are accepted). Throws <see cref="NotesFormatException"/> when the header, the source or the frame rate is missing or invalid, or the format is newer.</summary>
        public static NotesFile Parse(string text)
        {
            if (text.Length > 0 && text[0] == (char)0xFEFF) text = text.Substring(1);
            string[] raw = text.Split('\n');
            var lines = new List<string>(raw.Length);
            foreach (string line in raw) lines.Add(line.TrimEnd('\r'));
            int index = 0;
            while (index < lines.Count && Py.Strip(lines[index]).Length == 0) index++;
            Match versionMatch = index < lines.Count ? VersionPattern.Match(Py.Strip(lines[index])) : Match.Empty;
            if (!versionMatch.Success) throw new NotesFormatException("This is not a Retake notes file: the header line is missing");
            long version;
            if (!long.TryParse(versionMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out version) || version > FormatVersion)
            {
                throw new NotesFormatException("This notes file uses format " + versionMatch.Groups[1].Value
                    + ", which is newer than this version of Retake understands");
            }
            index++;

            var header = new Dictionary<string, string>();
            while (index < lines.Count)
            {
                string line = lines[index];
                if (Py.Strip(line).Length == 0) { index++; continue; }
                Match match = line.StartsWith("#", StringComparison.Ordinal) ? HeaderPattern.Match(Py.Strip(line)) : Match.Empty;
                string key = match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
                if (!match.Success || (key != "source" && key != "fps" && key != "duration") || header.ContainsKey(key)) break;
                header[key] = Py.Strip(match.Groups[2].Value);
                index++;
            }

            string source;
            header.TryGetValue("source", out source!);
            if (string.IsNullOrEmpty(source)) throw new NotesFormatException("The notes file has no source line");
            if (!header.ContainsKey("fps")) throw new NotesFormatException("The notes file has no frame rate line");
            double fps;
            try { fps = Timecode.ParseFps(header["fps"]); }
            catch (FormatException) { throw new NotesFormatException("The frame rate line of the notes file is not valid"); }
            TimecodeBase tb = Timecode.BaseFor(fps);
            int? duration = null;
            string durationText;
            if (header.TryGetValue("duration", out durationText!) && durationText.Length > 0)
            {
                try { duration = Timecode.TimecodeToFrame(durationText, tb); }
                catch (FormatException) { duration = null; }
            }

            var nf = new NotesFile(source, fps, duration);
            var takenIds = new HashSet<string>();
            for (int i = index; i < lines.Count; i++)
            {
                string line = lines[i];
                if (Py.Strip(line).Length == 0) continue;
                Note? note = ParseNote(line, tb, takenIds, nf.Notes.Count + 1);
                if (note == null)
                {
                    nf.Extra.Add(line);
                }
                else
                {
                    takenIds.Add(note.Id);
                    nf.Notes.Add(note);
                }
            }
            return nf;
        }

        private static Note? ParseNote(string line, TimecodeBase tb, ISet<string> takenIds, int order)
        {
            string[] columns = line.Split('\t');
            if (columns.Length < 2 || columns.Length > 5) return null;
            int frame;
            try { frame = Timecode.TimecodeToFrame(columns[0], tb); }
            catch (FormatException) { return null; }
            string kind = Py.Strip(columns[1]).ToUpperInvariant();
            if (Array.IndexOf(Note.Kinds, kind) < 0) return null;
            string body = columns.Length > 2 ? columns[2] : "";
            Region? region = null;
            string? image = null;
            if (columns.Length >= 4 && Py.Strip(columns[3]).Length > 0)
            {
                string col4 = Py.Strip(columns[3]);
                if (col4.StartsWith("rect ", StringComparison.Ordinal) || col4.StartsWith("point ", StringComparison.Ordinal))
                {
                    try { region = Region.FromText(col4); }
                    catch (ArgumentException) { return null; }
                    if (columns.Length == 5 && Py.Strip(columns[4]).Length > 0) image = Py.Strip(columns[4]);
                }
                else
                {
                    if (columns.Length == 5 && Py.Strip(columns[4]).Length > 0) return null;
                    image = col4;
                }
            }
            else if (columns.Length == 5 && Py.Strip(columns[4]).Length > 0)
            {
                image = Py.Strip(columns[4]);
            }
            var note = new Note(Note.NewId(takenIds), frame, kind, Note.NormalizeText(body));
            note.Region = region;
            note.Image = image;
            note.Order = order;
            return note;
        }

        /// <summary>The text of the notes file: header, notes sorted by (frame, order), the extra lines; LF line ends and a trailing newline.</summary>
        public string Render()
        {
            TimecodeBase tb = Timecode.BaseFor(Fps);
            var lines = new List<string>
            {
                "# Frame Notes " + FormatVersion.ToString(CultureInfo.InvariantCulture),
                "# source: " + Note.NormalizeText(Source),
                "# fps: " + Timecode.FormatFps(Fps),
            };
            if (DurationFrames.HasValue) lines.Add("# duration: " + Timecode.FrameToTimecode(DurationFrames.Value, tb));
            foreach (Note note in Sorted(Notes))
            {
                var columns = new List<string>
                {
                    Timecode.FrameToTimecode(note.Frame, tb),
                    note.Kind,
                    Note.NormalizeText(note.Text),
                };
                if (note.Region != null) columns.Add(note.Region.ToText());
                if (!string.IsNullOrEmpty(note.Image)) columns.Add(note.Image!);
                lines.Add(string.Join("\t", columns));
            }
            lines.AddRange(Extra);
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>
        /// The readable export: heading, summary line, one list item per note (Markdown). <paramref name="images"/>
        /// false leaves the image lines out (the clipboard copy). Lines that did not parse are not exported.
        /// </summary>
        public string RenderReadable(bool images = true)
        {
            TimecodeBase tb = Timecode.BaseFor(Fps);
            List<Note> notes = Sorted(Notes).ToList();
            var summary = new List<string> { Timecode.FormatFps(Fps) + " fps" };
            if (DurationFrames.HasValue) summary.Add(Timecode.FrameToTimecode(DurationFrames.Value, tb));
            foreach (string kind in Note.Kinds) summary.Add(kind + " " + notes.Count(n => n.Kind == kind).ToString(CultureInfo.InvariantCulture));
            var lines = new List<string> { "# " + Note.NormalizeText(Source), "", string.Join(Separator, summary), "" };
            if (notes.Count == 0) lines.Add("No notes.");
            foreach (Note note in notes)
            {
                var parts = new List<string> { "**" + Timecode.FrameToTimecode(note.Frame, tb) + "**", note.Kind };
                string text = Note.NormalizeText(note.Text);
                if (text.Length > 0) parts.Add(text);
                lines.Add("- " + string.Join(Separator, parts));
                if (images && !string.IsNullOrEmpty(note.Image) && NotesLayout.IsPlainName(note.Image))
                {
                    string target = FramesDirName + "/" + note.Image;
                    if (note.Image!.IndexOfAny(new[] { ' ', '(', ')', '<', '>' }) >= 0) target = "<" + target + ">";
                    lines.Add("  ![](" + target + ")");
                }
            }
            return string.Join("\n", lines) + "\n";
        }
    }
}
