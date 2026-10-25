using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        public DeleteResult(int notes, int frames, string path)
        {
            Notes = notes;
            Frames = frames;
            Path = path;
        }

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
        private static readonly string[] LeftoverSuffixes = { ".part", ".rename" };

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

        /// <summary>The Markdown export <c>&lt;stem&gt;_notes\&lt;stem&gt;_notes.md</c> written by Frame Notes 1.x and Retake 2.0; Retake only removes it.</summary>
        public static string LegacyExportPathFor(string video)
        {
            return Path.Combine(NotesDirFor(video), StemOf(video) + "_notes.md");
        }

        /// <summary>The package <c>&lt;stem&gt;_notes\&lt;stem&gt;_notes.zip</c> written by <see cref="Session.Pack"/>.</summary>
        public static string PackagePathFor(string video)
        {
            return Path.Combine(NotesDirFor(video), StemOf(video) + "_notes.zip");
        }

        /// <summary>
        /// Deletes the stale legacy export of the video when its data file exists. True when a file was removed. A failed
        /// delete is logged as a warning and never thrown; a folder that holds only the export is left alone.
        /// </summary>
        internal static bool RemoveLegacyExport(string video)
        {
            string export = LegacyExportPathFor(video);
            try
            {
                if (!File.Exists(NotesPathFor(video)) || !File.Exists(export)) return false;
                File.Delete(export);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning("cannot delete " + export + ": " + ex.Message);
                return false;
            }
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

        private static bool SameName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
        /// Moves a 1.0 layout into the notes folder: images into <c>frames\</c> first, then the data file. True when
        /// something moved. Every step is one rename on the same volume, so a crash midway leaves readable files and the
        /// next call finishes the move. A legacy data file that does not parse or names another video is left alone (the
        /// caller reports it). Throws <see cref="NotesLayoutException"/> when a move fails.
        /// </summary>
        public static bool MigrateLayout(string video)
        {
            string folder = NotesDirFor(video);
            string legacy = LegacyNotesPathFor(video);
            string target = NotesPathFor(video);
            string videoName = Path.GetFileName(video);
            List<string> loose = LooseImages(folder);
            bool hasLegacy = File.Exists(legacy);
            if (!hasLegacy && loose.Count == 0) return false;
            if (Exists(target))
            {
                Log.Warning("1.0 notes files of " + videoName + " are left in place: " + target + " already exists");
                return false;
            }
            if (hasLegacy)
            {
                NotesFile? legacyNf = ReadNotesFile(legacy);
                if (legacyNf == null || !SameName(legacyNf.Source, videoName))
                {
                    Log.Info(legacy + " is not moved: it is not a notes file of " + videoName);
                    return false;
                }
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
                if (hasLegacy)
                {
                    Directory.CreateDirectory(folder);
                    NativeFile.Replace(legacy, target);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("moving the notes of " + video + " into " + folder + " failed: " + ex.Message);
                throw new NotesLayoutException("The notes of " + videoName + " could not be moved into the " + Path.GetFileName(folder) + " folder.", ex);
            }
            Log.Info("moved the notes of " + videoName + " into " + folder + " (data file: " + (hasLegacy ? "moved" : "none") + ", images: " + moved + ")");
            return hasLegacy || moved > 0;
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

        private static bool AllowedLeftover(string name, string? baseName = null)
        {
            string lower = name.ToLowerInvariant();
            if (lower == "thumbs.db" || lower == "desktop.ini") return true;
            if (lower.EndsWith(".rename", StringComparison.Ordinal)) return true;
            if (lower.EndsWith(".part", StringComparison.Ordinal)) lower = lower.Substring(0, lower.Length - ".part".Length);
            if (baseName != null && lower == baseName.ToLowerInvariant()) return true;
            return lower.EndsWith(".png", StringComparison.Ordinal);
        }

        private static bool IsPng(string name)
        {
            return string.Equals(Py.Suffix(name), ".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Removes the notes folder of the video (and a 1.0 data file beside it) after checking that it holds only files
        /// the app wrote. Throws <see cref="NotesLayoutException"/> when there is nothing to delete, when the folder
        /// belongs to another video or holds other files, or when a removal fails; nothing is removed by a refusal.
        /// </summary>
        public static DeleteResult DeleteNotes(string video)
        {
            string folder = NotesDirFor(video);
            string dataFile = NotesPathFor(video);
            string export = LegacyExportPathFor(video);
            string package = PackagePathFor(video);
            string legacy = LegacyNotesPathFor(video);
            string frames = ImagesDirFor(video);
            string videoName = Path.GetFileName(video);
            string folderName = Path.GetFileName(folder);
            string dataName = Path.GetFileName(dataFile);
            string exportName = Path.GetFileName(export);
            string packageName = Path.GetFileName(package);

            bool folderExists = Exists(folder);
            if (folderExists && !IsPlainDir(folder))
                throw new NotesLayoutException("The notes folder " + folderName + " holds other files. Delete it by hand.");
            NotesFile? legacyNf = File.Exists(legacy) ? ReadNotesFile(legacy) : null;
            bool legacyOurs = legacyNf != null && SameName(legacyNf.Source, videoName);
            if (!folderExists && !legacyOurs) throw new NotesLayoutException("There are no notes for " + videoName + ".");

            var files = new List<string>();
            int pngs = 0;
            int notesCount = 0;
            if (folderExists)
            {
                NotesFile? nf = File.Exists(dataFile) ? ReadNotesFile(dataFile) : null;
                if (nf != null && !SameName(nf.Source, videoName))
                    throw new NotesLayoutException("The notes folder " + folderName + " belongs to " + nf.Source + ".");
                if (nf != null) notesCount = nf.Notes.Count;
                else if (legacyOurs && legacyNf != null) notesCount = legacyNf.Notes.Count;
                string foreign = "The notes folder " + folderName + " holds other files. Delete it by hand.";
                try
                {
                    foreach (string entry in Directory.GetFileSystemEntries(folder))
                    {
                        string name = Path.GetFileName(entry);
                        if (name.ToLowerInvariant() == NotesFile.FramesDirName)
                        {
                            if (!IsPlainDir(entry)) throw new NotesLayoutException(foreign);
                            foreach (string item in Directory.GetFileSystemEntries(entry))
                            {
                                string itemName = Path.GetFileName(item);
                                if (!IsPlainFile(item) || !AllowedLeftover(itemName)) throw new NotesLayoutException(foreign);
                                files.Add(item);
                                if (IsPng(itemName)) pngs++;
                            }
                            continue;
                        }
                        if (!IsPlainFile(entry)) throw new NotesLayoutException(foreign);
                        string lower = name.ToLowerInvariant();
                        if (lower == dataName.ToLowerInvariant() || lower == exportName.ToLowerInvariant() || lower == packageName.ToLowerInvariant()
                            || AllowedLeftover(name, dataName) || AllowedLeftover(name, exportName) || AllowedLeftover(name, packageName))
                        {
                            files.Add(entry);
                            if (IsPng(name)) pngs++;
                            continue;
                        }
                        throw new NotesLayoutException(foreign);
                    }
                }
                catch (NotesLayoutException)
                {
                    throw;
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

            // Images first, then the package and the export, the data file last: an interrupted delete keeps the notes readable.
            string dataLower = dataName.ToLowerInvariant();
            string exportLower = exportName.ToLowerInvariant();
            string packageLower = packageName.ToLowerInvariant();
            files = files.OrderBy(p =>
            {
                string n = Path.GetFileName(p).ToLowerInvariant();
                return n == dataLower ? 3 : n == exportLower ? 2 : n == packageLower ? 1 : 0;
            }).ToList();
            try
            {
                foreach (string path in files) File.Delete(path);
                if (folderExists)
                {
                    if (IsPlainDir(frames)) Directory.Delete(frames, false);
                    Directory.Delete(folder, false);
                }
                if (legacyOurs) File.Delete(legacy);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("deleting the notes of " + video + " failed: " + ex.Message);
                throw new NotesLayoutException("The notes folder " + folderName + " could not be deleted completely.", ex);
            }
            Log.Info("deleted the notes of " + videoName + ": " + folder + " (" + notesCount + " notes, " + pngs + " frame images)");
            return new DeleteResult(notesCount, pngs, folder);
        }
    }
}
