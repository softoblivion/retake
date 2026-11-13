using System;
using System.Globalization;

namespace Retake.Player
{
    /// <summary>
    /// The playback speeds: the ladder that <c>[</c>, <c>]</c>, the speed list and the wheel over the speed control
    /// step through, and the shuttle steps of <c>L</c>. Pure functions; the chosen speed lives in the review and is the
    /// one the next play uses.
    /// </summary>
    public static class SpeedLadder
    {
        /// <summary>The ladder, slowest first.</summary>
        public static readonly double[] Steps = { 0.25, 0.5, 1, 1.5, 2, 4, 8 };

        /// <summary>The steps of <c>L</c> while playing (each press goes to the next one above the current speed).</summary>
        public static readonly double[] Shuttle = { 1, 2, 4, 8 };

        /// <summary>The index of the ladder step nearest to a speed (a speed off the ladder counts as its nearest step).</summary>
        public static int IndexOf(double speed)
        {
            int best = 0;
            for (int i = 1; i < Steps.Length; i++)
                if (Math.Abs(Steps[i] - speed) < Math.Abs(Steps[best] - speed)) best = i;
            return best;
        }

        /// <summary>One step faster (<paramref name="direction"/> 1) or slower (-1); the ends hold.</summary>
        public static double Step(double speed, int direction)
        {
            int i = IndexOf(speed) + Math.Sign(direction);
            return Steps[Math.Max(0, Math.Min(Steps.Length - 1, i))];
        }

        /// <summary>The speed of <c>L</c> while playing: the next shuttle step above the current speed, 8 at most.</summary>
        public static double ShuttleUp(double speed)
        {
            foreach (double s in Shuttle)
                if (s > speed + 1e-9) return s;
            return Shuttle[Shuttle.Length - 1];
        }

        /// <summary>A speed as shown: "0.25×", "1×", "1.5×".</summary>
        public static string Caption(double speed) => speed.ToString("0.##", CultureInfo.InvariantCulture) + "×";
    }
}
