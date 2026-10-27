using System;
using System.Collections.Generic;

namespace Retake.Core
{
    /// <summary>Note counts per kind.</summary>
    public sealed class KindCounts
    {
        public int Fix { get; set; }
        public int Add { get; set; }
        public int Remove { get; set; }
        public int Like { get; set; }
        public int Dislike { get; set; }

        /// <summary>The count of a kind (one of <see cref="Note.Kinds"/>); an unknown kind throws <see cref="ArgumentException"/>.</summary>
        public int this[string kind]
        {
            get
            {
                switch (kind)
                {
                    case "FIX": return Fix;
                    case "ADD": return Add;
                    case "REMOVE": return Remove;
                    case "LIKE": return Like;
                    case "DISLIKE": return Dislike;
                    default: throw new ArgumentException("Unknown note type");
                }
            }
            set
            {
                switch (kind)
                {
                    case "FIX": Fix = value; break;
                    case "ADD": Add = value; break;
                    case "REMOVE": Remove = value; break;
                    case "LIKE": Like = value; break;
                    case "DISLIKE": Dislike = value; break;
                    default: throw new ArgumentException("Unknown note type");
                }
            }
        }
    }

    /// <summary>Timecode rules of the current frame rate.</summary>
    public sealed class TimecodeInfo
    {
        /// <summary>Frames per timecode second.</summary>
        public int Base { get; set; }

        /// <summary>True for drop-frame rates.</summary>
        public bool Drop { get; set; }

        /// <summary>Digits of the frame part.</summary>
        public int Digits { get; set; }

        /// <summary><c>:</c> or <c>;</c>.</summary>
        public string Separator { get; set; } = ":";
    }

    /// <summary>The open video as the UI shows it.</summary>
    public sealed class VideoState
    {
        /// <summary>Full path of the video.</summary>
        public string FilePath { get; set; } = "";

        /// <summary>File name of the video.</summary>
        public string Name { get; set; } = "";

        /// <summary>Folder of the video.</summary>
        public string Dir { get; set; } = "";

        public int? Width { get; set; }
        public int? Height { get; set; }

        /// <summary>Duration in seconds (frames / exact rate when the frame count is known), or null.</summary>
        public double? Duration { get; set; }

        /// <summary>Duration in frames, or null.</summary>
        public int? DurationFrames { get; set; }

        /// <summary>Exact frame rate, or null while it is unknown.</summary>
        public double? Fps { get; set; }

        /// <summary>The frame rate as text (<c>25</c>, <c>29.97</c>), "" while it is unknown.</summary>
        public string FpsText { get; set; } = "";

        /// <summary><c>file</c> (read from the container), <c>override</c> (chosen by the user or taken from the notes file) or <c>unknown</c>.</summary>
        public string FpsSource { get; set; } = "unknown";

        public string NotesDir { get; set; } = "";
        public string NotesPath { get; set; } = "";
        public string PackagePath { get; set; } = "";
        public string ImagesDir { get; set; } = "";

        /// <summary>Last playback position, a frame number.</summary>
        public int LastFrame { get; set; }
    }

    /// <summary>One note as the UI shows it.</summary>
    public sealed class NoteState
    {
        public string Id { get; set; } = "";

        /// <summary>The frame number, or null for a general note.</summary>
        public int? Frame { get; set; }

        /// <summary>True for a general note (no frame, no region, no image).</summary>
        public bool IsGeneral { get { return Frame == null; } }

        /// <summary>The timecode of the frame ("" for a general note and while the frame rate is unknown).</summary>
        public string Timecode { get; set; } = "";

        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";
        public Region? Region { get; set; }

        /// <summary>File name of the frame image, or null.</summary>
        public string? Image { get; set; }

        /// <summary>Full path of the frame image; null for no image or a name that is not a plain file name.</summary>
        public string? ImagePath { get; set; }

        public int Order { get; set; }
    }

    /// <summary>What <see cref="Session.Pack"/> wrote.</summary>
    public sealed class PackResult
    {
        /// <summary>Full path of the zip file.</summary>
        public string Path { get; set; } = "";

        /// <summary>Number of notes in the package.</summary>
        public int Notes { get; set; }

        /// <summary>Number of frame images in the package.</summary>
        public int Frames { get; set; }
    }

    /// <summary>The state published after every change: the video, the timecode rules, the notes (general notes first, then by frame and creation order) and the counts.</summary>
    public sealed class SessionState
    {
        /// <summary>The open video, or null.</summary>
        public VideoState? Video { get; set; }

        /// <summary>Timecode rules; null while no video is open or the frame rate is unknown.</summary>
        public TimecodeInfo? Timecode { get; set; }

        public List<NoteState> Notes { get; set; } = new List<NoteState>();

        public KindCounts Counts { get; set; } = new KindCounts();
    }

    /// <summary>A failure the reviewer can act on; the message is a user-facing English sentence.</summary>
    public sealed class SessionException : Exception
    {
        public SessionException(string message) : base(message) { }
    }
}
