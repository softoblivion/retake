using System.Globalization;
using Retake.Core;

namespace Retake.Views
{
    /// <summary>
    /// What the banner says after a note was saved with a new frame image (a save, a move): the frame image that could
    /// not be made and the refusal of the session, both when both happened. A change kept in memory without its write
    /// (<see cref="SessionException.NotSaved"/>) has its own notice with Retry, so the banner keeps only the image;
    /// a change the session refused took nothing, so no note is "saved without" its image.
    /// </summary>
    public static class SaveNotice
    {
        /// <param name="error">The session's refusal, or null when the save went in.</param>
        /// <param name="imageError">Why the frame image could not be made, or null.</param>
        /// <returns>The banner text, or null when the banner has nothing to say.</returns>
        public static string? Banner(SessionException? error, string? imageError)
        {
            string? image = imageError == null ? null : string.Format(CultureInfo.InvariantCulture, Copy.ImageFailed, imageError);
            if (error == null) return image;
            if (error.NotSaved) return image;
            if (!error.Changed || image == null) return error.Message;
            return string.Format(CultureInfo.InvariantCulture, Copy.TwoMessages, error.Message, image);
        }
    }
}
