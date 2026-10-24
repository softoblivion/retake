using System;
using Retake.Core;

namespace Retake.Player
{
    /// <summary>How the player's frame rate relates to the probed one.</summary>
    public enum RateAgreement { Agree, Differ, ProbeOnly, PlayerOnly, Unknown }

    /// <summary>
    /// The frame rules of the player (measured on libmpv in the engine spike): frame 0 sits at <c>t0</c>, the
    /// <c>time-pos</c> right after a file is loaded (10.0 for a MOV whose video starts at 10 s, else 0); an exact
    /// seek to frame f targets the start of the frame, <c>t0 + f / fps</c>; the frame shown is
    /// <c>round((time-pos - t0) * fps)</c> (WebM timestamps are whole milliseconds, so a floor would miss).
    /// Pure functions, no player state; the timecode text rules stay in <see cref="Timecode"/>.
    /// </summary>
    public static class FrameMath
    {
        /// <summary>Two rates closer than this are the same rate (the probe wins then).</summary>
        public const double RateTolerance = 0.01;

        /// <summary>The seek target of a frame: the start of the frame, relative to frame 0.</summary>
        public static double SeekTarget(double t0, int frame, double fps)
        {
            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps)) throw new ArgumentException("The frame rate is not valid");
            return t0 + Math.Max(0, frame) / fps;
        }

        /// <summary>The frame shown at a playback position; never negative.</summary>
        public static int FrameAt(double timePos, double t0, double fps)
        {
            if (fps <= 0 || double.IsNaN(timePos) || double.IsNaN(fps)) return 0;
            double f = Math.Round((timePos - t0) * fps, MidpointRounding.AwayFromZero);
            if (f <= 0) return 0;
            return f >= int.MaxValue ? int.MaxValue : (int)f;
        }

        /// <summary>A frame clamped to 0 .. last.</summary>
        public static int Clamp(int frame, int last) => Math.Max(0, Math.Min(Math.Max(0, last), frame));

        /// <summary>
        /// The index of the last frame: from the frame count when known, else from the duration at the rate
        /// (rounded, at least one frame).
        /// </summary>
        public static int LastFrame(int? durationFrames, double? duration, double fps)
        {
            if (durationFrames.HasValue && durationFrames.Value > 0) return durationFrames.Value - 1;
            if (duration.HasValue && duration.Value > 0 && fps > 0) return Math.Max(1, (int)Math.Round(duration.Value * fps, MidpointRounding.AwayFromZero)) - 1;
            return 0;
        }

        /// <summary>The player's rate snapped to the nominal table (mpv reports 23.976025 for 24000/1001); null when unusable.</summary>
        public static double? SnapPlayerRate(double rate)
        {
            if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0.1 || rate > 1000) return null;
            return Timecode.NominalRate(rate);
        }

        /// <summary>Compares the probed rate with the player's (both already snapped).</summary>
        public static RateAgreement Compare(double? probe, double? player)
        {
            if (probe.HasValue && player.HasValue) return Math.Abs(probe.Value - player.Value) < RateTolerance ? RateAgreement.Agree : RateAgreement.Differ;
            if (probe.HasValue) return RateAgreement.ProbeOnly;
            if (player.HasValue) return RateAgreement.PlayerOnly;
            return RateAgreement.Unknown;
        }

        /// <summary>
        /// The rate the playhead runs on: the session's rate when it has one, else the player's, else 25 so that
        /// stepping still moves (notes need a known rate and are refused until one is chosen).
        /// </summary>
        public static double WorkingRate(double? session, double? player) => session ?? player ?? 25.0;
    }
}
