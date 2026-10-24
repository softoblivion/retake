namespace Retake.Views
{
    /// <summary>
    /// The window position rule of 1.x: the saved position (logical pixels) is used when at least 100 px of the
    /// window stay reachable on the virtual screen (all monitors), else the window opens in its default place.
    /// A minimized window reports about -32000; such a position is never saved. Pure functions.
    /// </summary>
    public static class WindowPlace
    {
        public const double Margin = 100;

        /// <param name="screenLeft">Virtual screen in physical pixels.</param>
        /// <param name="dpiScale">Physical pixels per logical pixel.</param>
        public static bool Usable(int? x, int? y, double width, double screenLeft, double screenTop, double screenWidth, double screenHeight, double dpiScale)
        {
            if (!x.HasValue || !y.HasValue) return false;
            double px = x.Value * dpiScale, py = y.Value * dpiScale;
            return screenLeft - width * dpiScale + Margin <= px && px <= screenLeft + screenWidth - Margin
                && screenTop <= py && py <= screenTop + screenHeight - Margin;
        }

        /// <summary>True for a position worth saving (not the parking place of a minimized window).</summary>
        public static bool Savable(double left, double top) => left > -10000 && top > -10000;
    }
}
