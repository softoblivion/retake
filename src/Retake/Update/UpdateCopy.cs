using SelfUpdate;

namespace Retake.Update
{
    /// <summary>
    /// The tooltips of the update views. The visible labels and messages come from the library's UpdateText; a
    /// tooltip says what a click does, never repeats the label, and a disabled button shows the library's reason
    /// instead (<see cref="Tip(UpdateButton)"/>).
    /// </summary>
    internal static class UpdateCopy
    {
        internal const string SheetClose = "Close, update later (Esc)";
        internal const string PopoverName = "Version and updates";
        internal const string Later = "Keep this version for now; the version pill opens this again";

        internal const string Install = "Download the new version, then restart into it";
        internal const string Restart = "Close Retake and start the new version";
        internal const string Retry = "Repeat the step that failed";
        internal const string Check = "Look for a newer version now";
        internal const string Download = "Fetch the new version; installing still needs a restart";

        /// <summary>The tooltip of a button: what its command does, or why the button is disabled (null when neither is known).</summary>
        internal static string? Tip(UpdateButton button)
        {
            if (!button.Enabled) return button.Tooltip;
            switch (button.Command)
            {
                case UpdateCommand.Install: return Install;
                case UpdateCommand.Restart: return Restart;
                case UpdateCommand.Retry: return Retry;
                case UpdateCommand.Check: return Check;
                case UpdateCommand.Download: return Download;
                default: return button.Tooltip;
            }
        }
    }
}
