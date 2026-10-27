using System;
using System.Windows;

namespace Retake.Design
{
    /// <summary>
    /// The Scale setting (LIQUID_UI.md 9.4): five steps on top of the Windows scaling. The root element gets a
    /// LayoutTransform of step / 100, so text and controls grow with the step. The window ladder (1280 x 800,
    /// 1152 x 720, 1024 x 640, 960 x 600) gives the first size of the window: the largest whose scaled size fits the
    /// work area of the window's monitor; its last size, scaled, is the smallest the window can be. A step for which
    /// not even the smallest size fits cannot be chosen.
    /// </summary>
    public static class Scale
    {
        public static readonly int[] Steps = { 100, 125, 150, 175, 200 };
        public const int Default = 100;

        public static readonly Size[] Ladder = { new Size(1280, 800), new Size(1152, 720), new Size(1024, 640), new Size(960, 600) };

        /// <summary>The nearest step; 100 for nonsense.</summary>
        public static int Normalize(int value)
        {
            if (value <= 0) return Default;
            int best = Default;
            foreach (int s in Steps) if (Math.Abs(s - value) < Math.Abs(best - value)) best = s;
            return best;
        }

        /// <summary>The logical window size for a step in a work area (device-independent pixels); null when none fits.</summary>
        public static Size? LogicalSize(int step, Size work)
        {
            double f = step / 100.0;
            foreach (Size s in Ladder)
                if (s.Width * f <= work.Width && s.Height * f <= work.Height) return s;
            return null;
        }

        public static bool Fits(int step, Size work) => LogicalSize(step, work) != null;

        /// <summary>The largest step not above the wanted one that fits; 100 when none does.</summary>
        public static int Fitting(int wanted, Size work)
        {
            for (int i = Steps.Length - 1; i >= 0; i--)
                if (Steps[i] <= wanted && Fits(Steps[i], work)) return Steps[i];
            return Default;
        }

        /// <summary>The menu label of a step, with the no-break space of the kit: "125 %".</summary>
        public static string Label(int step) => step + " %";
    }
}
