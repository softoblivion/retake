using System;
using System.Threading.Tasks;

namespace Retake.Player
{
    /// <summary>What the player read from a loaded file.</summary>
    public sealed class MediaInfo
    {
        public int Width, Height;
        /// <summary>The player's rate snapped to the nominal table; null when the file reports none.</summary>
        public double? Rate;
        /// <summary>Seconds; NaN when unknown.</summary>
        public double Duration = double.NaN;
        /// <summary>Frame count estimated by the player; 0 when unknown.</summary>
        public long FrameCount;
        /// <summary>Playback position of frame 0 (non-zero for files whose video starts later).</summary>
        public double T0;
        public string Decoder = "";
    }

    /// <summary>
    /// The video engine as the views use it. Every event is raised on the UI thread. Frame numbers are zero-based
    /// and use the rate set by <see cref="SetRate"/>. Seeks are exact; a step or seek while another is pending
    /// is taken relative to the pending target, so a held key never skips or repeats a frame.
    /// </summary>
    public interface IPlayer : IDisposable
    {
        /// <summary>A file finished loading; the first frame is on screen, paused.</summary>
        event Action<MediaInfo>? Loaded;
        /// <summary>The frame shown changed (playback, seek, step).</summary>
        event Action<int>? FrameChanged;
        /// <summary>Playback started or stopped.</summary>
        event Action<bool>? PlayingChanged;
        /// <summary>The file cannot be played; the message is user-facing.</summary>
        event Action<string>? Failed;

        bool IsLoaded { get; }
        bool Playing { get; }
        double Speed { get; }
        /// <summary>The frame shown, or the target of a pending seek.</summary>
        int Frame { get; }
        int LastFrame { get; }
        MediaInfo? Info { get; }

        void Load(string path);
        void Unload();
        /// <summary>The rate of the frame numbering and the index of the last frame.</summary>
        void SetRate(double fps, int lastFrame);
        void SeekFrame(int frame);
        /// <summary>Steps by n frames (pauses first): one frame uses frame-step / frame-back-step, more an exact seek.</summary>
        void Step(int frames);
        void Play(double speed = 1.0);
        void Pause();
        /// <summary>Pauses and resolves with the frame that is on screen once the player has settled.</summary>
        Task<int> PauseAndSettle();
        /// <summary>Resolves once no seek or step is pending.</summary>
        Task<int> Settle();
        /// <summary>The frame on screen at native size (screenshot-raw video); runs off the UI thread.</summary>
        Task<RawFrame> GrabFrame();
    }
}
