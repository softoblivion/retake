using Retake.Core;

namespace Retake.Views
{
    /// <summary>
    /// Whether notes that are not saved may leave memory without the question "Notes are not saved" first. A Discard
    /// covers only the move it was given for (closing, another video, a restart for an update): it ends when that move
    /// is done or fails (<see cref="Open"/>, <see cref="Close"/>, <see cref="Revoke"/>) and when a new write fails
    /// (<see cref="Failed"/>), so notes that become unsaved later are never dropped without asking.
    /// </summary>
    public sealed class UnsavedGate
    {
        readonly Session session;
        bool discarded;

        public UnsavedGate(Session session)
        {
            this.session = session;
        }

        /// <summary>True while notes are in memory that the data file does not have and no Discard covers them.</summary>
        public bool MustAsk => session.GetState().Unsaved && !discarded;

        /// <summary>The reviewer chose Discard for the move about to happen.</summary>
        public void Discard()
        {
            discarded = true;
            Log.Warning("the reviewer discarded notes that were not saved");
        }

        /// <summary>The move the Discard was given for did not happen (a restart that did not quit): the next one asks again.</summary>
        public void Revoke()
        {
            discarded = false;
        }

        /// <summary>A failed session call: a new failed write (<see cref="SessionException.NotSaved"/>) ends the Discard.</summary>
        public void Failed(SessionException ex)
        {
            if (ex.NotSaved) discarded = false;
        }

        /// <summary>Opens a video in the session; the Discard ends whether the open succeeds or throws.</summary>
        /// <exception cref="SessionException">The open was refused; the session keeps the video it had.</exception>
        public SessionState Open(string path)
        {
            try { return session.Open(path); }
            finally { discarded = false; }
        }

        /// <summary>Closes the video of the session (notes that are not saved are dropped); the Discard ends.</summary>
        public void Close()
        {
            session.Close();
            discarded = false;
        }
    }
}
