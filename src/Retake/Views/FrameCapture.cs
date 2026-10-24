using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Retake.Core;
using Retake.Player;

namespace Retake.Views
{
    /// <summary>
    /// The frame image of a note: the frame at native size (from the player's <c>screenshot-raw video</c>) with
    /// the region drawn the way it was shown, scaled to native pixels, encoded as a 24-bit PNG. Runs on its own
    /// STA thread so a 4K frame does not hold the window.
    /// </summary>
    public static class FrameCapture
    {
        /// <param name="scale">Native pixels per logical pixel of the frame on screen.</param>
        public static Task<byte[]> EncodeAsync(RawFrame frame, Region region, RegionPainter.Ink ink, double scale)
        {
            var done = new TaskCompletionSource<byte[]>();
            var thread = new Thread(() =>
            {
                try { done.SetResult(Encode(frame, region, ink, scale)); }
                catch (Exception ex) { done.SetException(ex); }
            }) { IsBackground = true, Name = "frame-capture" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return done.Task;
        }

        public static byte[] Encode(RawFrame frame, Region region, RegionPainter.Ink ink, double scale)
        {
            int w = frame.Width, h = frame.Height;
            if (w <= 0 || h <= 0 || frame.Data.Length < (long)frame.Stride * h) throw new InvalidDataException("The frame from the player is empty");
            BitmapSource source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, frame.Data, frame.Stride);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(source, new Rect(0, 0, w, h));
                RegionPainter.Draw(dc, region, ink, new Box(0, 0, w, h), scale);
            }
            var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0)));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }
    }
}
