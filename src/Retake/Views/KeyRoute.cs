namespace Retake.Views
{
    /// <summary>The window that holds the Win32 focus of the app's thread.</summary>
    public enum KeysAt { None, Main, Overlay, Video, Other }

    /// <summary>The element with the WPF keyboard focus: none, a control (a button, a window), or a text field.</summary>
    public enum KeysOn { None, Control, Text }

    /// <summary>
    /// Where the keyboard must be for the review keys (Space, the arrows, the note keys), and when it must be moved
    /// back after a note ends. Pure functions; the review applies them to the live focus.
    /// </summary>
    public static class KeyRoute
    {
        /// <summary>True when a key press reaches the review keys from where the keyboard is.</summary>
        /// <param name="textShown">The focused text field is on screen (false for the closed note field).</param>
        public static bool ReviewGets(KeysAt at, KeysOn on, bool textShown)
        {
            // A field off screen (the closed note field) types nothing: the key is a review key.
            if (at == KeysAt.Main || at == KeysAt.Overlay) return on == KeysOn.Control || (on == KeysOn.Text && !textShown);
            return false;
        }

        /// <summary>True when the keyboard must go back to the main window: no note is written, the app is in front
        /// (the Win32 focus is in one of its windows), and the keyboard is elsewhere than on a control of the main window
        /// or a field the user can see there.</summary>
        public static bool MustReturn(bool writing, KeysAt at, KeysOn on, bool textShown)
        {
            if (writing) return false;
            if (at == KeysAt.Overlay || at == KeysAt.Video) return true;
            return at == KeysAt.Main && (on == KeysOn.None || (on == KeysOn.Text && !textShown));
        }

        /// <summary>True when a key that is no review key goes on into the note field (the overlay could not take
        /// the activation while a note is written); never while the note is saved and its field is closed.</summary>
        public static bool OpensField(bool editing, bool composerOpen) => editing && composerOpen;
    }
}
