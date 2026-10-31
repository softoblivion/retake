using Retake.Core;

namespace Retake.Views
{
    /// <summary>
    /// The save of an edit of an existing note as session calls, without the views: the place first (a note that comes
    /// to a frame can take a region only once it has the frame), then the text, the region and the type. A move whose
    /// data file could not be written is in memory all the same, so the rest of the edit still goes in, and the next
    /// write saves both; an edit that changes nothing writes nothing.
    /// </summary>
    public static class NoteEdit
    {
        /// <param name="session">The open session.</param>
        /// <param name="id">The note being edited.</param>
        /// <param name="moveTo">True when the place changed: <paramref name="frame"/> is the new frame, null for the whole video.</param>
        /// <param name="frame">The note's frame after the edit.</param>
        /// <param name="text">The new text, or null when it did not change.</param>
        /// <param name="region">The new region, or null.</param>
        /// <param name="clearRegion">True when the region was removed.</param>
        /// <param name="png">The new frame image, or null.</param>
        /// <param name="kind">The new type, or null when it did not change.</param>
        /// <returns>The state after the edit.</returns>
        /// <exception cref="SessionException">A refused change, or (with <see cref="SessionException.NotSaved"/>) a change kept in memory that could not be written.</exception>
        public static SessionState Save(Session session, string id, bool moveTo, int? frame, string? text, Region? region, bool clearRegion, byte[]? png, string? kind)
        {
            SessionState state = session.GetState();
            SessionException? notSaved = null;
            if (moveTo)
            {
                try
                {
                    state = session.MoveNote(id, frame, null);
                }
                catch (SessionException ex) when (ex.NotSaved)
                {
                    notSaved = ex;
                }
            }
            if (text != null || region != null || clearRegion || kind != null || png != null)
            {
                try
                {
                    state = session.UpdateNote(id, text, region, clearRegion, png, kind);
                    if (!state.Unsaved) notSaved = null;
                }
                catch (SessionException ex) when (ex.NotSaved)
                {
                    notSaved = ex;
                }
            }
            if (notSaved != null && session.GetState().Unsaved) throw notSaved;
            return session.GetState();
        }
    }
}
