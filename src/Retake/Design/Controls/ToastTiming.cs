using System;

namespace Retake.Design.Controls
{
    public enum ToastLevel { Info, Success, Warning, Error }

    /// <summary>
    /// How long a toast stays: info and success 3 s, warning and error 10 s, a sticky toast until it is clicked
    /// (no timer). Kept free of WPF so the rule can be tested on its own.
    /// </summary>
    public static class ToastTiming
    {
        public static readonly TimeSpan Short = TimeSpan.FromSeconds(3), Long = TimeSpan.FromSeconds(10);

        /// <summary>The lifetime of a toast, or null for a sticky toast.</summary>
        public static TimeSpan? Lifetime(ToastLevel level, bool sticky)
        {
            if (sticky) return null;
            return level == ToastLevel.Info || level == ToastLevel.Success ? Short : Long;
        }
    }
}
