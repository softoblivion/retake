using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Retake.Core
{
    /// <summary>
    /// The review session: one open video, its notes in memory and the notes folder on disk. Owns probing the video,
    /// loading and saving the data file and the readable export, the note images and the frame rate choice; the 1.0
    /// layout is moved into the notes folder on open. Every mutation writes the files and then calls the change callback
    /// with the new state. All public methods are serialised with one lock; the callback runs while the lock is held, so
    /// it must only hand the state over (for example enqueue it).
    /// </summary>
    public sealed class Session
    {
        private const double SettingsSaveIntervalSeconds = 2.0;
        private const double MinFps = 0.1;
        private const double MaxFps = 1000.0;

        private readonly Settings _settings;
        private readonly Action<SessionState> _onChange;
        private readonly object _lock = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastSettingsSave;

        private string? _video;
        private ProbeResult? _probe;
        private double? _probeFps;
        private double? _fileFps;
        private double? _fps;
        private string _fpsSource = "unknown";
        private List<Note> _notes = new List<Note>();
        private List<string> _extra = new List<string>();
        private int? _fileDuration;
        private bool _notesFileExists;
        private int _lastFrame;
        private int _nextOrder = 1;

        /// <summary>Creates a session over the settings; <paramref name="onChange"/> receives the state after every successful or failed change.</summary>
        public Session(Settings settings, Action<SessionState> onChange)
        {
            _settings = settings;
            _onChange = onChange;
            Reset();
        }

        private void Reset()
        {
            _video = null;
            _probe = null;
            _probeFps = null;
            _fileFps = null;
            _fps = null;
            _fpsSource = "unknown";
            _notes = new List<Note>();
            _extra = new List<string>();
            _fileDuration = null;
            _notesFileExists = false;
            _lastFrame = 0;
            _nextOrder = 1;
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFileError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException;
        }

        // --- opening and closing ---------------------------------------------------------------------------------

        /// <summary>Probes the video, loads its notes file (moving a 1.0 layout first) and makes it the current session. Throws <see cref="SessionException"/> when the video or the notes file cannot be used; the previous session is kept then.</summary>
        public SessionState Open(string video)
        {
            lock (_lock)
            {
                string path = Path.GetFullPath(video);
                string name = Path.GetFileName(path);
                if (!File.Exists(path)) throw new SessionException("The video file was not found");
                ProbeResult info;
                try
                {
                    info = Probe.Read(path);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    Log.Warning("probe failed for " + path + ": " + ex.Message);
                    throw new SessionException("The video file cannot be read");
                }

                try
                {
                    NotesLayout.MigrateLayout(path);
                }
                catch (NotesLayoutException ex)
                {
                    throw new SessionException(ex.Message);
                }

                List<Note> notes = new List<Note>();
                List<string> extra = new List<string>();
                double? fileFps = null;
                int? fileDuration = null;
                string notesPath = NotesLayout.NotesPathFor(path);
                bool exists = File.Exists(notesPath);
                bool legacy = false;
                if (!exists && File.Exists(NotesLayout.LegacyNotesPathFor(path)))
                {
                    // Left in place by the migration: report it the way 1.0 did (another source, newer format).
                    notesPath = NotesLayout.LegacyNotesPathFor(path);
                    exists = true;
                    legacy = true;
                }
                if (exists)
                {
                    string text;
                    try
                    {
                        text = TextFile.ReadAllText(notesPath);
                    }
                    catch (Exception ex) when (IsFileError(ex) || ex is DecoderFallbackException)
                    {
                        throw new SessionException("The notes file " + Path.GetFileName(notesPath) + " cannot be read");
                    }
                    NotesFile parsed;
                    try
                    {
                        parsed = NotesFile.Parse(text);
                    }
                    catch (NotesFormatException ex)
                    {
                        throw new SessionException(ex.Message);
                    }
                    if (!SameName(parsed.Source, name))
                    {
                        throw new SessionException("The notes file " + Path.GetFileName(notesPath) + " belongs to " + parsed.Source + ", not to " + name);
                    }
                    if (legacy)
                    {
                        // Readable but not movable (e.g. the folder could not be created): refuse rather than split.
                        throw new SessionException("The notes of " + name + " could not be moved into the "
                            + Path.GetFileName(NotesLayout.NotesDirFor(path)) + " folder.");
                    }
                    notes = parsed.Notes;
                    extra = parsed.Extra;
                    fileFps = UsableFps(parsed.Fps);
                    fileDuration = parsed.DurationFrames;
                }

                Reset();
                _video = path;
                _probe = info;
                _probeFps = UsableFps(info.Fps);
                _fileFps = fileFps;
                _notes = notes;
                _extra = extra;
                _fileDuration = fileDuration;
                _notesFileExists = exists;
                _nextOrder = (notes.Count == 0 ? 0 : notes.Max(n => n.Order)) + 1;
                ResolveFps();
                _lastFrame = _settings.LastFrame(path);
                _settings.TouchRecent(path, _lastFrame);
                _settings.LastDir = Path.GetDirectoryName(path) ?? "";
                SaveSettings();
                if (exists) NotesLayout.RemoveLegacyExport(path);
                Log.Info("opened " + path + " (fps " + (_fps.HasValue ? Timecode.FormatFps(_fps.Value) : "unknown") + ", " + notes.Count + " notes)");
                return GetState();
            }
        }

        /// <summary>Saves the settings (recent list, last frame) and forgets the video. No other file is written.</summary>
        public void Close()
        {
            lock (_lock)
            {
                if (_video != null)
                {
                    _settings.TouchRecent(_video, _lastFrame);
                    SaveSettings();
                }
                Reset();
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.Save();
                _lastSettingsSave = _clock.Elapsed.TotalSeconds;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Warning("settings were not saved: " + ex.Message);
            }
        }

        // --- frame rate -------------------------------------------------------------------------------------------

        /// <summary>Snaps a frame rate to the nominal table; null when it is missing or absurd.</summary>
        private static double? UsableFps(double? value)
        {
            if (value == null) return null;
            double snapped;
            try
            {
                snapped = Timecode.NominalRate(value.Value);
            }
            catch (ArgumentException)
            {
                return null;
            }
            return (snapped >= MinFps && snapped <= MaxFps) ? (double?)snapped : null;
        }

        /// <summary>Picks the frame rate: settings override, else the probe, else the rate in an existing notes file.</summary>
        private void ResolveFps()
        {
            double? over = UsableFps(_settings.FpsOverride(_video!));
            if (over != null) { _fps = over; _fpsSource = "override"; }
            else if (_probeFps != null) { _fps = _probeFps; _fpsSource = "file"; }
            else if (_fileFps != null) { _fps = _fileFps; _fpsSource = "override"; }
            else { _fps = null; _fpsSource = "unknown"; }
        }

        /// <summary>Changes the frame rate (null clears the override). Frame numbers stay, timecodes and image names are rewritten. Throws <see cref="SessionException"/> for no open video or a rate outside 0.1..1000.</summary>
        public SessionState SetFps(double? fps)
        {
            lock (_lock)
            {
                string video = RequireVideo();
                if (fps == null)
                {
                    _settings.SetFpsOverride(video, null);
                }
                else
                {
                    double value = fps.Value;
                    double? snapped = (double.IsNaN(value) || double.IsInfinity(value)) ? null : UsableFps(value);
                    if (snapped == null) throw new SessionException("The frame rate is not valid");
                    if (_probeFps != null && snapped == _probeFps) _settings.SetFpsOverride(video, null);
                    else _settings.SetFpsOverride(video, snapped);
                }
                SaveSettings();
                TimecodeBase? oldTb = Tb();
                ResolveFps();
                TimecodeBase? newTb = Tb();
                if (oldTb != null && newTb != null && !oldTb.Equals(newTb)) RenameImages(newTb);
                string? error = WriteNotes();
                return Finish(error);
            }
        }

        // --- queries -----------------------------------------------------------------------------------------------

        private TimecodeBase? Tb()
        {
            return _fps.HasValue ? Timecode.BaseFor(_fps.Value) : null;
        }

        private int? DurationFrames()
        {
            if (_probe != null && _probe.Frames.HasValue && _probe.Frames.Value != 0)
                return (int)Math.Min(_probe.Frames.Value, int.MaxValue);
            if (_probe != null && _probe.Duration.HasValue && _probe.Duration.Value != 0 && _fps.HasValue && _fps.Value != 0)
                return Math.Max(1, Timecode.DurationFrames(_probe.Duration.Value, _fps.Value));
            return _fileDuration;
        }

        private static IEnumerable<Note> SortedNotes(IEnumerable<Note> notes)
        {
            return NotesFile.Sorted(notes);
        }

        /// <summary>The current state (the same object shape as the change callback receives).</summary>
        public SessionState GetState()
        {
            lock (_lock)
            {
                var state = new SessionState();
                if (_video == null) return state;
                string video = _video;
                ProbeResult? info = _probe;
                TimecodeBase? tb = Tb();
                int? durationFrames = DurationFrames();
                double? duration = info != null ? info.Duration : null;
                if (info != null && info.Frames.HasValue && info.Frames.Value != 0 && tb != null) duration = info.Frames.Value / tb.Fps;
                foreach (Note note in SortedNotes(_notes))
                {
                    state.Counts[note.Kind] = state.Counts[note.Kind] + 1;
                    string? imageFile = ImageFile(note.Image);
                    state.Notes.Add(new NoteState
                    {
                        Id = note.Id,
                        Frame = note.Frame,
                        Timecode = (tb != null && note.Frame.HasValue) ? Retake.Core.Timecode.FrameToTimecode(note.Frame.Value, tb) : "",
                        Kind = note.Kind,
                        Text = note.Text,
                        Region = note.Region != null ? note.Region.Normalized() : null,
                        Image = note.Image,
                        ImagePath = imageFile,
                        Order = note.Order,
                    });
                }
                state.Video = new VideoState
                {
                    FilePath = video,
                    Name = Path.GetFileName(video),
                    Dir = Path.GetDirectoryName(video) ?? "",
                    Width = info != null ? info.Width : null,
                    Height = info != null ? info.Height : null,
                    Duration = duration,
                    DurationFrames = durationFrames,
                    Fps = tb != null ? (double?)tb.Fps : null,
                    FpsText = _fps.HasValue ? Retake.Core.Timecode.FormatFps(_fps.Value) : "",
                    FpsSource = _fpsSource,
                    NotesDir = NotesLayout.NotesDirFor(video),
                    NotesPath = NotesLayout.NotesPathFor(video),
                    PackagePath = NotesLayout.PackagePathFor(video),
                    ImagesDir = NotesLayout.ImagesDirFor(video),
                    LastFrame = _lastFrame,
                };
                if (tb != null) state.Timecode = new TimecodeInfo { Base = tb.Base, Drop = tb.Drop, Digits = tb.Digits, Separator = tb.Separator };
                return state;
            }
        }

        /// <summary>Text of the notes file for the current state ("" when no video is open or the frame rate is unknown).</summary>
        public string NotesText()
        {
            lock (_lock)
            {
                if (_video == null || _fps == null) return "";
                return BuildNotesFile().Render();
            }
        }

        /// <summary>The readable text of the notes, for the clipboard and the package ("" like <see cref="NotesText"/>).</summary>
        public string ReadableText()
        {
            lock (_lock)
            {
                if (_video == null || _fps == null) return "";
                return BuildNotesFile().RenderReadable();
            }
        }

        /// <summary>Remembers the playback position; the settings file is written at most once per two seconds.</summary>
        public void SetLastFrame(int frame)
        {
            lock (_lock)
            {
                if (_video == null) return;
                _lastFrame = Math.Max(0, frame);
                _settings.TouchRecent(_video, _lastFrame);
                if (_clock.Elapsed.TotalSeconds - _lastSettingsSave >= SettingsSaveIntervalSeconds) SaveSettings();
            }
        }

        // --- mutations ----------------------------------------------------------------------------------------------

        private const string RateUnknownAdding = "The frame rate is unknown. Choose it before adding notes";
        private const string RateUnknownEditing = "The frame rate is unknown. Choose it before editing notes";
        private const string NoText = "The note has no text";
        private const string GeneralHasNoFrameToMark = "A general note has no frame to mark";
        private const string FrameNotValid = "The frame number is not valid";

        /// <summary>
        /// Adds a note at <paramref name="frame"/>; a null frame makes a general note about the whole video (no region, no
        /// image). A region may come with the PNG of the frame (without a region the image is ignored). Throws
        /// <see cref="SessionException"/> for no open video, an unknown frame rate, an unknown kind, a negative frame, an
        /// empty text, an invalid region or a region on a general note. A failed write keeps the note in memory, publishes
        /// the state, then throws.
        /// </summary>
        public SessionState AddNote(int? frame, string kind, string? text, Region? region, byte[]? imagePng)
        {
            lock (_lock)
            {
                RequireVideo();
                TimecodeBase? tb = Tb();
                if (tb == null) throw new SessionException(RateUnknownAdding);
                kind = CheckKind(kind);
                if (frame.HasValue && frame.Value < 0) throw new SessionException(FrameNotValid);
                if (!frame.HasValue && region != null) throw new SessionException(GeneralHasNoFrameToMark);
                string clean = Note.NormalizeText(text ?? "");
                if (clean.Length == 0) throw new SessionException(NoText);
                var note = new Note(Note.NewId(new HashSet<string>(_notes.Select(n => n.Id))), frame, kind, clean);
                note.Order = _nextOrder;
                if (frame.HasValue)
                {
                    note.Region = CheckRegion(region);
                    if (note.Region != null && imagePng != null && imagePng.Length > 0)
                    {
                        note.Image = NotesLayout.ImageName(note, tb, TakenImages(null));
                        WriteImage(note.Image, imagePng);
                    }
                }
                _nextOrder++;
                _notes.Add(note);
                return Finish(WriteNotes());
            }
        }

        /// <summary>
        /// Changes the text, region and / or kind of a note. <paramref name="region"/> replaces the region (a new region
        /// normally comes with a new image); <paramref name="clearRegion"/> removes the region and its image; neither keeps
        /// the region. A null <paramref name="text"/> keeps the text; a text that is empty after normalising is refused. A
        /// new <paramref name="kind"/> renames the image of the note to the name of that kind. A call that changes nothing
        /// writes nothing and publishes nothing. Throws <see cref="SessionException"/> for no open video, an unknown note,
        /// an unknown frame rate, an unknown kind, an empty text, an invalid region or a region on a general note.
        /// </summary>
        public SessionState UpdateNote(string noteId, string? text = null, Region? region = null, bool clearRegion = false,
                                       byte[]? imagePng = null, string? kind = null)
        {
            lock (_lock)
            {
                RequireVideo();
                Note note = Find(noteId);
                TimecodeBase? tb = Tb();
                if (tb == null) throw new SessionException(RateUnknownEditing);
                bool general = note.IsGeneral;
                if (general && region != null) throw new SessionException(GeneralHasNoFrameToMark);
                if (clearRegion && region != null) throw new SessionException("The region is not valid");
                string? newText = null;
                if (text != null)
                {
                    newText = Note.NormalizeText(text);
                    if (newText.Length == 0) throw new SessionException(NoText);
                }
                string? newKind = kind != null ? CheckKind(kind) : null;
                Region? newRegion = general || clearRegion ? null : (region != null ? CheckRegion(region) : note.Region);
                bool kindChanged = newKind != null && newKind != note.Kind;
                bool textChanged = newText != null && newText != note.Text;
                bool regionChanged = !SameRegion(newRegion, note.Region);
                bool imageGiven = newRegion != null && imagePng != null && imagePng.Length > 0;
                if (!kindChanged && !textChanged && !regionChanged && !imageGiven) return GetState();

                string? oldImage = note.Image;
                bool dropOldImage = false;
                string? error = null;
                bool regionRemoved = clearRegion || (newRegion == null && note.Region != null);
                if (kindChanged) note.Kind = newKind!;
                if (regionRemoved)
                {
                    dropOldImage = oldImage != null;
                    note.Image = null;
                }
                else if (newRegion == null)
                {
                    // An image without a region (a hand-edited file) stays, and follows a change of kind.
                    if (oldImage != null && kindChanged && NotesLayout.IsPlainName(oldImage)) error = RelocateImage(note, tb, null);
                }
                else if (oldImage == null)
                {
                    if (imageGiven)
                    {
                        note.Image = NotesLayout.ImageName(note, tb, TakenImages(note));
                        error = TryWriteImage(note.Image, imagePng!);
                    }
                }
                else if (kindChanged && NotesLayout.IsPlainName(oldImage))
                {
                    error = RelocateImage(note, tb, imageGiven ? imagePng : null);
                }
                else if (imageGiven)
                {
                    error = TryWriteImage(oldImage, imagePng!);
                }
                note.Region = newRegion;
                if (newText != null) note.Text = newText;
                string? writeError = WriteNotes();
                if (dropOldImage)
                {
                    DeleteImage(oldImage);
                    PruneImagesDir();
                }
                return Finish(error ?? writeError);
            }
        }

        /// <summary>
        /// Moves a frame note to <paramref name="frame"/>; the note keeps its id, kind, text, region and order. An image
        /// with a plain name gets the name of the new timecode: the file is renamed, or <paramref name="imagePng"/> (when
        /// not empty) is written under the new name and the old file deleted. The same frame without a new image changes
        /// nothing. Throws <see cref="SessionException"/> for no open video, an unknown note, an unknown frame rate, a
        /// general note or a negative frame. A failed write keeps the move in memory, publishes the state, then throws.
        /// </summary>
        public SessionState MoveNote(string noteId, int frame, byte[]? imagePng)
        {
            lock (_lock)
            {
                RequireVideo();
                Note note = Find(noteId);
                TimecodeBase? tb = Tb();
                if (tb == null) throw new SessionException(RateUnknownEditing);
                if (note.IsGeneral) throw new SessionException("A general note has no frame");
                if (frame < 0) throw new SessionException(FrameNotValid);
                bool hasImage = NotesLayout.IsPlainName(note.Image);
                bool hasBytes = hasImage && imagePng != null && imagePng.Length > 0;
                if (frame == note.Frame && !hasBytes) return GetState();
                note.Frame = frame;
                string? imageError = hasImage ? RelocateImage(note, tb, hasBytes ? imagePng : null) : null;
                string? writeError = WriteNotes();
                return Finish(imageError ?? writeError);
            }
        }

        /// <summary>Deletes a note, its image and the images folder when it became empty. Throws <see cref="SessionException"/> for no open video or an unknown note.</summary>
        public void RemoveNote(string noteId)
        {
            lock (_lock)
            {
                RequireVideo();
                Note note = Find(noteId);
                _notes.Remove(note);
                string? error = WriteNotes();
                DeleteImage(note.Image);
                PruneImagesDir();
                Finish(error);
            }
        }

        /// <summary>
        /// Writes <c>&lt;stem&gt;_notes\&lt;stem&gt;_notes.zip</c>: <c>notes.txt</c> (the readable text, CRLF) and
        /// <c>frames/&lt;image&gt;</c> for every note image that exists. Streams into a part file and replaces the zip in
        /// one step. Not a mutation: the state does not change and nothing is published. Throws
        /// <see cref="SessionException"/> for no open video, an unknown frame rate, no notes or a failed write.
        /// </summary>
        public PackResult Pack()
        {
            lock (_lock)
            {
                string video = RequireVideo();
                if (Tb() == null) throw new SessionException(RateUnknownEditing);
                if (_notes.Count == 0) throw new SessionException("There are no notes to pack");
                string zip = NotesLayout.PackagePathFor(video);
                string part = zip + ".part";
                var images = new List<KeyValuePair<string, string>>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Note note in SortedNotes(_notes))
                {
                    string? file = ImageFile(note.Image);
                    if (file == null || !File.Exists(file) || !seen.Add(note.Image!)) continue;
                    images.Add(new KeyValuePair<string, string>(NotesFile.FramesDirName + "/" + note.Image, file));
                }
                byte[] text = new UTF8Encoding(false).GetBytes(BuildNotesFile().RenderReadable().Replace("\n", "\r\n"));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
                    using (var stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                        {
                            ZipArchiveEntry notesEntry = archive.CreateEntry("notes.txt", CompressionLevel.Optimal);
                            using (Stream target = notesEntry.Open()) target.Write(text, 0, text.Length);
                            foreach (KeyValuePair<string, string> image in images)
                            {
                                ZipArchiveEntry entry = archive.CreateEntry(image.Key, CompressionLevel.NoCompression);
                                using (Stream target = entry.Open())
                                using (var source = new FileStream(image.Value, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                                {
                                    source.CopyTo(target);
                                }
                            }
                        }
                        stream.Flush(true);
                    }
                    Paths.ReplaceFile(part, zip);
                }
                catch (Exception ex) when (IsFileError(ex) || ex is InvalidDataException)
                {
                    try { File.Delete(part); } catch (Exception) { }
                    Log.Error("cannot write " + zip, ex);
                    throw new SessionException("The notes could not be packed into " + zip);
                }
                Log.Info("packed " + _notes.Count + " notes and " + images.Count + " frames into " + zip);
                return new PackResult { Path = zip, Notes = _notes.Count, Frames = images.Count };
            }
        }

        // --- helpers --------------------------------------------------------------------------------------------------

        private string RequireVideo()
        {
            if (_video == null) throw new SessionException("No video is open");
            return _video;
        }

        private static string CheckKind(string kind)
        {
            string value = Py.Strip(kind ?? "").ToUpperInvariant();
            if (Array.IndexOf(Note.Kinds, value) < 0) throw new SessionException("Unknown note type");
            return value;
        }

        private static Region? CheckRegion(Region? region)
        {
            if (region == null) return null;
            try
            {
                return region.Normalized();
            }
            catch (ArgumentException)
            {
                throw new SessionException("The marked region is not valid");
            }
        }

        private Note Find(string noteId)
        {
            foreach (Note note in _notes)
            {
                if (note.Id == noteId) return note;
            }
            throw new SessionException("The note was not found");
        }

        private NotesFile BuildNotesFile()
        {
            var nf = new NotesFile(Path.GetFileName(_video!), _fps!.Value, DurationFrames());
            nf.Notes.AddRange(_notes);
            nf.Extra.AddRange(_extra);
            return nf;
        }

        /// <summary>Writes the notes file when it exists or has notes. Returns an error sentence or null.</summary>
        private string? WriteNotes()
        {
            if (_video == null || _fps == null) return null;
            if (_notes.Count == 0 && !_notesFileExists) return null;
            string path = NotesLayout.NotesPathFor(_video);
            try
            {
                Paths.AtomicWriteText(path, BuildNotesFile().Render());
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Error("cannot write " + path, ex);
                return "The notes could not be saved to " + path;
            }
            _notesFileExists = true;
            NotesLayout.RemoveLegacyExport(_video);
            return null;
        }

        private static bool SameRegion(Region? a, Region? b)
        {
            return a == null ? b == null : a.Equals(b);
        }

        private string? TryWriteImage(string name, byte[] data)
        {
            try
            {
                WriteImage(name, data);
                return null;
            }
            catch (SessionException ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// Gives the image of the note the name its current kind and frame call for (names of other notes taken): the bytes
        /// are written under the new name and the old file deleted, or, without bytes, the file is renamed. A failed rename
        /// is logged and the old name stays. Returns an error sentence for a failed write, else null.
        /// </summary>
        private string? RelocateImage(Note note, TimecodeBase tb, byte[]? bytes)
        {
            string old = note.Image!;
            string newName = NotesLayout.ImageName(note, tb, TakenImages(note));
            bool sameFile = string.Equals(old, newName, StringComparison.OrdinalIgnoreCase);
            if (bytes != null && bytes.Length > 0)
            {
                string? error = TryWriteImage(newName, bytes);
                if (error != null) return error;
                note.Image = newName;
                if (!sameFile) DeleteImage(old);
                return null;
            }
            if (string.Equals(old, newName, StringComparison.Ordinal)) return null;
            string? source = ImageFile(old);
            string? target = ImageFile(newName);
            if (source == null || target == null) return null;
            try
            {
                if (File.Exists(source)) NativeFile.Replace(source, target);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Warning("cannot rename image " + old + " to " + newName + ": " + ex.Message);
                return null;
            }
            note.Image = newName;
            return null;
        }

        /// <summary>Publishes the new state; a failed write is reported after the UI has seen the in-memory notes.</summary>
        private SessionState Finish(string? error)
        {
            SessionState data = GetState();
            try
            {
                _onChange(data);
            }
            catch (Exception ex)
            {
                Log.Error("on_change callback failed", ex);
            }
            if (error != null) throw new SessionException(error);
            return data;
        }

        private List<string> TakenImages(Note? exclude)
        {
            return _notes.Where(n => !string.IsNullOrEmpty(n.Image) && !ReferenceEquals(n, exclude)).Select(n => n.Image!).ToList();
        }

        /// <summary>Path of an image in the images folder; null for no image or a name that is not a plain file name.</summary>
        private string? ImageFile(string? name)
        {
            if (string.IsNullOrEmpty(name) || _video == null || !NotesLayout.IsPlainName(name)) return null;
            return Path.Combine(NotesLayout.ImagesDirFor(_video), name!);
        }

        private void WriteImage(string name, byte[] data)
        {
            string? target = ImageFile(name);
            if (target == null) throw new SessionException("The image name is not valid");
            try
            {
                Paths.AtomicWriteBytes(target, data);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Error("cannot write " + target, ex);
                throw new SessionException("The image could not be saved to " + target);
            }
        }

        private void DeleteImage(string? name)
        {
            string? target = ImageFile(name);
            if (target == null) return;
            try
            {
                File.Delete(target);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Warning("cannot delete " + target + ": " + ex.Message);
            }
        }

        private void PruneImagesDir()
        {
            if (_video == null) return;
            string folder = NotesLayout.ImagesDirFor(_video);
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder, false);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Warning("cannot remove " + folder + ": " + ex.Message);
            }
        }

        /// <summary>Renames the image files to the timecodes of the new frame rate (two steps, all or nothing).</summary>
        private void RenameImages(TimecodeBase tb)
        {
            var plan = new List<KeyValuePair<Note, string>>();
            var taken = new List<string>();
            foreach (Note note in SortedNotes(_notes))
            {
                if (string.IsNullOrEmpty(note.Image) || ImageFile(note.Image) == null) continue;
                string newName = NotesLayout.ImageName(note, tb, taken);
                taken.Add(newName);
                if (newName != note.Image) plan.Add(new KeyValuePair<Note, string>(note, newName));
            }
            if (plan.Count == 0) return;
            var moved = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (KeyValuePair<Note, string> item in plan)
                {
                    string source = ImageFile(item.Key.Image)!;
                    if (!File.Exists(source) && !Directory.Exists(source)) continue;
                    string temp = Path.Combine(Path.GetDirectoryName(source)!, item.Key.Id + ".rename");
                    NativeFile.Replace(source, temp);
                    moved.Add(new KeyValuePair<string, string>(temp, source));
                }
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Warning("images were not renamed: " + ex.Message);
                foreach (KeyValuePair<string, string> pair in moved)
                {
                    try { NativeFile.Replace(pair.Key, pair.Value); }
                    catch (Exception inner) when (IsFileError(inner)) { }
                }
                return;
            }
            foreach (KeyValuePair<Note, string> item in plan)
            {
                string old = item.Key.Image!;
                string target = ImageFile(item.Value)!;
                string temp = Path.Combine(Path.GetDirectoryName(ImageFile(old)!)!, item.Key.Id + ".rename");
                try
                {
                    if (File.Exists(temp) || Directory.Exists(temp)) NativeFile.Replace(temp, target);
                    item.Key.Image = item.Value;
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    Log.Warning("cannot rename image " + old + " to " + item.Value + ": " + ex.Message);
                }
            }
        }
    }
}
