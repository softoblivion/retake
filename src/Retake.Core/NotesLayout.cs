using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Retake.Core
{
    /// <summary>Moving or deleting the notes folder was refused or failed; the message is a user-facing English sentence.</summary>
    public class NotesLayoutException : IOException
    {
        public NotesLayoutException(string message) : base(message) { }

        public NotesLayoutException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>The notes counted in a data file.</summary>
    public sealed class NotesSummary
    {
        public NotesSummary(int notes, int frames)
        {
            Notes = notes;
            Frames = frames;
        }

        /// <summary>Number of notes.</summary>
        public int Notes { get; private set; }

        /// <summary>Number of notes with a frame image name.</summary>
        public int Frames { get; private set; }
    }

    /// <summary>What <see cref="NotesLayout.DeleteNotes"/> removed.</summary>
    public sealed class DeleteResult
    {
        public DeleteResult(int notes, int frames, string path, int kept = 0)
        {
            Notes = notes;
            Frames = frames;
            Path = path;
            Kept = kept;
        }

        /// <summary>Number of other files (a sub-folder counts as one) left where they were, with the folders that hold them.</summary>
        public int Kept { get; private set; }

        /// <summary>Number of notes in the deleted data file.</summary>
        public int Notes { get; private set; }

        /// <summary>Number of PNG files removed.</summary>
        public int Frames { get; private set; }

        /// <summary>The notes folder that was removed.</summary>
        public string Path { get; private set; }
    }

    /// <summary>
    /// The notes folder <c>&lt;video stem&gt;_notes\</c> beside a video: path rules, image names, the migration of the 1.0
    /// layout and the deletion of a review.
    /// </summary>
    public static class NotesLayout
    {
        private static string Beside(string video, string name)
        {
            string dir = Path.GetDirectoryName(video) ?? "";
            return Path.Combine(dir, name);
        }

        private static string StemOf(string video)
        {
            return Py.Stem(Path.GetFileName(video));
        }

        /// <summary>The <c>&lt;stem&gt;_notes</c> folder beside the video: everything of a review lives inside it.</summary>
        public static string NotesDirFor(string video)
        {
            return Beside(video, StemOf(video) + "_notes");
        }

        /// <summary>The data file <c>&lt;stem&gt;_notes\&lt;stem&gt;_notes.txt</c>.</summary>
        public static string NotesPathFor(string video)
        {
            return Path.Combine(NotesDirFor(video), StemOf(video) + "_notes.txt");
        }

        /// <summary>The Markdown notes file <c>&lt;stem&gt;_notes\&lt;stem&gt;_notes.md</c>: a readable view of the data file, written by <see cref="Session"/>, never read back.</summary>
        public static string MarkdownPathFor(string video)
        {
            return Path.Combine(NotesDirFor(video), StemOf(video) + "_notes.md");
        }

        /// <summary>The package <c>&lt;stem&gt;_notes\&lt;stem&gt;_notes.zip</c> written by <see cref="Session.Pack"/>.</summary>
        public static string PackagePathFor(string video)
        {
            return Path.Combine(NotesDirFor(video), StemOf(video) + "_notes.zip");
        }

        /// <summary>The frame images folder <c>&lt;stem&gt;_notes\frames</c>.</summary>
        public static string ImagesDirFor(string video)
        {
            return Path.Combine(NotesDirFor(video), NotesFile.FramesDirName);
        }

        /// <summary>The 1.0 data file <c>&lt;stem&gt;_notes.txt</c> beside the video.</summary>
        public static string LegacyNotesPathFor(string video)
        {
            return Beside(video, StemOf(video) + "_notes.txt");
        }

        /// <summary>True for a bare file name that is safe to join to a folder: no path component, no drive, not "." or "..".</summary>
        public static bool IsPlainName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name == "." || name == "..") return false;
            if (name!.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return false;
            if (name.Length >= 2 && name[1] == ':') return false;
            // A name from a hand-edited file may hold characters no file name can have (Path.Combine would throw).
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        /// <summary>
        /// <c>&lt;timecode with '-'&gt;_&lt;KIND&gt;.png</c>, with <c>_2</c>, <c>_3</c>... added while the name is in
        /// <paramref name="taken"/> (compared case-insensitively).
        /// </summary>
        public static string ImageName(Note note, TimecodeBase tb, IEnumerable<string> taken)
        {
            if (note.Frame == null) throw new ArgumentException("A general note has no frame image");
            string stem = Timecode.FrameToTimecode(note.Frame.Value, tb).Replace(':', '-').Replace(';', '-');
            var used = new HashSet<string>(taken.Select(n => n.ToLowerInvariant()));
            string name = stem + "_" + note.Kind + ".png";
            int counter = 2;
            while (used.Contains(name.ToLowerInvariant()))
            {
                name = stem + "_" + note.Kind + "_" + counter + ".png";
                counter++;
            }
            return name;
        }

        private static NotesFile? ReadNotesFile(string path)
        {
            try
            {
                return NotesFile.Parse(TextFile.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.DecoderFallbackException || ex is NotesFormatException)
            {
                return null;
            }
        }

        /// <summary>A real directory: not a file, a symbolic link or a junction.</summary>
        private static bool IsPlainDir(string path)
        {
            try
            {
                var info = new DirectoryInfo(path);
                if (!info.Exists) return false;
                return !NativeFile.IsLink(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsPlainFile(string path)
        {
            var info = new FileInfo(path);
            return info.Exists && !NativeFile.IsLink(path);
        }

        private static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>The source line of a data file names this video: both in their one-line form (the file keeps the name stripped), case-insensitive.</summary>
        internal static bool SameName(string source, string videoName)
        {
            return string.Equals(Note.NormalizeText(source), Note.NormalizeText(videoName), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>*.png</c> files directly inside the folder (the 1.0 images folder), sorted by name.</summary>
        private static List<string> LooseImages(string folder)
        {
            if (!IsPlainDir(folder)) return new List<string>();
            try
            {
                return Directory.GetFiles(folder)
                    .Where(p => string.Equals(Py.Suffix(Path.GetFileName(p)), ".png", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => Path.GetFileName(p).ToLowerInvariant(), StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Moves a 1.0 layout into the notes folder: the images of its notes into <c>frames\</c> first, then the data
        /// file. True when something moved. Every step is one rename on the same volume, so a crash midway leaves
        /// readable files and the next call finishes the move. Without a 1.0 data file nothing moves; a PNG that no note
        /// names and that has no image name of the app stays where it is. A legacy data file that does not parse or
        /// names another video is left alone (the caller reports it). Throws <see cref="NotesLayoutException"/> when a
        /// move fails.
        /// </summary>
        public static bool MigrateLayout(string video)
        {
            string folder = NotesDirFor(video);
            string legacy = LegacyNotesPathFor(video);
            string target = NotesPathFor(video);
            string videoName = Path.GetFileName(video);
            // Without a 1.0 data file there is no 1.0 layout: pictures in the folder are the reviewer's own.
            if (!File.Exists(legacy)) return false;
            if (Exists(target))
            {
                Log.Warning("1.0 notes files of " + videoName + " are left in place: " + target + " already exists");
                return false;
            }
            NotesFile? legacyNf = ReadNotesFile(legacy);
            if (legacyNf == null || !SameName(legacyNf.Source, videoName))
            {
                Log.Info(legacy + " is not moved: it is not a notes file of " + videoName);
                return false;
            }
            // Only the images of the notes move (named by the data file, or in the app's image-name form).
            var named = new HashSet<string>(legacyNf.Notes.Where(n => IsPlainName(n.Image)).Select(n => n.Image!), StringComparer.OrdinalIgnoreCase);
            var loose = new List<string>();
            foreach (string image in LooseImages(folder))
            {
                string name = Path.GetFileName(image);
                if (named.Contains(name) || AppImageName.IsMatch(name)) loose.Add(image);
                else Log.Info(image + " is left in place: no note names it");
            }
            int moved = 0;
            try
            {
                if (loose.Count > 0)
                {
                    string frames = ImagesDirFor(video);
                    Directory.CreateDirectory(frames);
                    foreach (string image in loose)
                    {
                        string destination = Path.Combine(frames, Path.GetFileName(image));
                        if (Exists(destination))
                        {
                            Log.Warning(image + " is left in place: " + destination + " already exists");
                            continue;
                        }
                        NativeFile.Replace(image, destination);
                        moved++;
                    }
                }
                Directory.CreateDirectory(folder);
                NativeFile.Replace(legacy, target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("moving the notes of " + video + " into " + folder + " failed: " + ex.Message);
                throw new NotesLayoutException("The notes of " + videoName + " could not be moved into the " + Path.GetFileName(folder) + " folder.", ex);
            }
            Log.Info("moved the notes of " + videoName + " into " + folder + " (data file: moved, images: " + moved + ")");
            return true;
        }

        /// <summary>The note and frame image counts from the data file of the video (new layout, else 1.0); null when there is none, it cannot be read or it names another video.</summary>
        public static NotesSummary? NotesSummaryFor(string video)
        {
            string videoName = Path.GetFileName(video);
            foreach (string path in new[] { NotesPathFor(video), LegacyNotesPathFor(video) })
            {
                if (!File.Exists(path)) continue;
                NotesFile? nf = ReadNotesFile(path);
                if (nf == null || !SameName(nf.Source, videoName)) return null;
                return new NotesSummary(nf.Notes.Count, nf.Notes.Count(n => IsPlainName(n.Image)));
            }
            return null;
        }

        /// <summary>An image name as <see cref="ImageName"/> makes it: <c>&lt;timecode with '-'&gt;_&lt;KIND&gt;[_n].png</c>.</summary>
        private static readonly Regex AppImageName = new Regex(
            "^[0-9]+-[0-9]{2}-[0-9]{2}-[0-9]{2,}_(" + string.Join("|", Note.Kinds) + ")(_[0-9]+)?\\.png$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The temporary name of an image while <see cref="Session"/> renames images: <c>&lt;note id&gt;.rename</c>.</summary>
        private static readonly Regex RenameLeftover = new Regex("^[0-9a-f]{8}\\.rename$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string WithoutPart(string name)
        {
            return name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - ".part".Length) : name;
        }

        /// <summary>
        /// A frame image the app wrote (a name in <paramref name="named"/>, the image names of the data file, or a name
        /// in the app's own form), its part file, an image rename's temporary file, or a file Windows puts in folders.
        /// </summary>
        private static bool AppImageOrLeftover(string name, ISet<string> named)
        {
            string lower = name.ToLowerInvariant();
            if (lower == "thumbs.db" || lower == "desktop.ini") return true;
            if (RenameLeftover.IsMatch(name)) return true;
            string bare = WithoutPart(name);
            return named.Contains(bare) || AppImageName.IsMatch(bare);
        }

        private static bool IsPng(string name)
        {
            return string.Equals(Py.Suffix(name), ".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes the files the app wrote in the notes folder of the video (and a 1.0 data file beside it): the data
        /// file, the Markdown file and the package, the images the data file names or that carry a name in the app's own
        /// form, the part and rename files of those writes, <c>Thumbs.db</c> and <c>desktop.ini</c>. Every other file,
        /// sub-folder or link stays where it is, with the folders that hold it, and is counted in
        /// <see cref="DeleteResult.Kept"/>; with none, the folder goes as one item. Throws
        /// <see cref="NotesLayoutException"/> when there is nothing to delete, when the folder is a link or belongs to
        /// another video, or when a removal fails; nothing is removed by a refusal.
        /// With <paramref name="toRecycleBin"/> the files go to the Recycle Bin (on a drive without one, such as a network
        /// share, the shell deletes them); without it they are deleted.
        /// </summary>
        public static DeleteResult DeleteNotes(string video, bool toRecycleBin = true)
        {
            string folder = NotesDirFor(video);
            string dataFile = NotesPathFor(video);
            string export = MarkdownPathFor(video);
            string package = PackagePathFor(video);
            string legacy = LegacyNotesPathFor(video);
            string videoName = Path.GetFileName(video);
            string folderName = Path.GetFileName(folder);
            string dataName = Path.GetFileName(dataFile);
            string exportName = Path.GetFileName(export);
            string packageName = Path.GetFileName(package);

            bool folderExists = Exists(folder);
            if (folderExists && !IsPlainDir(folder))
                throw new NotesLayoutException("The notes folder " + folderName + " is a link or a file, not a folder. Delete it by hand.");
            NotesFile? legacyNf = File.Exists(legacy) ? ReadNotesFile(legacy) : null;
            bool legacyOurs = legacyNf != null && SameName(legacyNf.Source, videoName);
            if (!folderExists && !legacyOurs) throw new NotesLayoutException("There are no notes for " + videoName + ".");

            var files = new List<string>();
            int pngs = 0;
            int notesCount = 0;
            var frameFiles = new List<string>();
            int kept = 0;
            string? framesWhole = null;
            if (folderExists)
            {
                NotesFile? nf = File.Exists(dataFile) ? ReadNotesFile(dataFile) : null;
                if (nf != null && !SameName(nf.Source, videoName))
                    throw new NotesLayoutException("The notes folder " + folderName + " belongs to " + nf.Source + ".");
                if (nf != null) notesCount = nf.Notes.Count;
                else if (legacyOurs && legacyNf != null) notesCount = legacyNf.Notes.Count;
                var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (NotesFile? source in new[] { nf, legacyOurs ? legacyNf : null })
                {
                    if (source == null) continue;
                    foreach (Note note in source.Notes)
                    {
                        if (IsPlainName(note.Image)) named.Add(note.Image!);
                    }
                }
                try
                {
                    foreach (string entry in Directory.GetFileSystemEntries(folder))
                    {
                        string name = Path.GetFileName(entry);
                        if (name.ToLowerInvariant() == NotesFile.FramesDirName && Directory.Exists(entry))
                        {
                            // A linked frames folder is not entered: it and what it points to stay.
                            if (!IsPlainDir(entry)) { kept++; continue; }
                            int framesKept = 0;
                            foreach (string item in Directory.GetFileSystemEntries(entry))
                            {
                                string itemName = Path.GetFileName(item);
                                if (!IsPlainFile(item) || !AppImageOrLeftover(itemName, named)) { framesKept++; continue; }
                                frameFiles.Add(item);
                                if (IsPng(itemName)) pngs++;
                            }
                            kept += framesKept;
                            // With nothing else in it the frames folder goes as one item; else only its own files go.
                            if (framesKept == 0) framesWhole = entry;
                            continue;
                        }
                        // The data file, the Markdown file, the package (each also as a part file), and the images of the 1.0 layout.
                        string bare = WithoutPart(name);
                        if (IsPlainFile(entry) && (string.Equals(bare, dataName, StringComparison.OrdinalIgnoreCase) || string.Equals(bare, exportName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(bare, packageName, StringComparison.OrdinalIgnoreCase) || AppImageOrLeftover(name, named)))
                        {
                            files.Add(entry);
                            if (IsPng(name)) pngs++;
                            continue;
                        }
                        // Anything else (a file of the reviewer's, a sub-folder, a link) stays where it is.
                        kept++;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new NotesLayoutException("The notes folder " + folderName + " cannot be read.", ex);
                }
            }
            else if (legacyNf != null)
            {
                notesCount = legacyNf.Notes.Count;
            }

            // A folder of only other files, with nothing of the app's in it or beside it, has no notes to delete.
            if (kept > 0 && files.Count == 0 && frameFiles.Count == 0 && framesWhole == null && !legacyOurs)
                throw new NotesLayoutException("There are no notes for " + videoName + ".");

            // Images first, then the package and the Markdown file, the data file last: an interrupted delete keeps the notes readable.
            string dataLower = dataName.ToLowerInvariant();
            string exportLower = exportName.ToLowerInvariant();
            string packageLower = packageName.ToLowerInvariant();
            files = files.OrderBy(p =>
            {
                string n = Path.GetFileName(p).ToLowerInvariant();
                return n == dataLower ? 3 : n == exportLower ? 2 : n == packageLower ? 1 : 0;
            }).ToList();
            string keptNote = kept > 0 ? ", " + kept + " other entries kept" : "";

            if (toRecycleBin)
            {
                // With nothing else in it the checked folder goes as one item, so a restore from the Recycle Bin brings the
                // whole review back; with other entries kept only the app's own items go, and the folder stays.
                var targets = new List<string>();
                if (folderExists && kept == 0) targets.Add(folder);
                else if (folderExists)
                {
                    if (framesWhole != null) targets.Add(framesWhole);
                    else targets.AddRange(frameFiles);
                    targets.AddRange(files);
                }
                if (legacyOurs) targets.Add(legacy);
                try
                {
                    NativeFile.Recycle(targets);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Error("deleting the notes of " + video + " failed: " + ex.Message);
                    throw new NotesLayoutException("The notes folder " + folderName + " could not be deleted completely.", ex);
                }
                Log.Info("moved the notes of " + videoName + " to the Recycle Bin: " + folder + " (" + notesCount + " notes, " + pngs + " frame images" + keptNote + ")");
                return new DeleteResult(notesCount, pngs, folder, kept);
            }
            try
            {
                foreach (string path in frameFiles) File.Delete(path);
                foreach (string path in files) File.Delete(path);
                if (folderExists)
                {
                    if (framesWhole != null) Directory.Delete(framesWhole, false);
                    if (kept == 0) Directory.Delete(folder, false);
                }
                if (legacyOurs) File.Delete(legacy);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("deleting the notes of " + video + " failed: " + ex.Message);
                throw new NotesLayoutException("The notes folder " + folderName + " could not be deleted completely.", ex);
            }
            Log.Info("deleted the notes of " + videoName + ": " + folder + " (" + notesCount + " notes, " + pngs + " frame images" + keptNote + ")");
            return new DeleteResult(notesCount, pngs, folder, kept);
        }
    }
}
