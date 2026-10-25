namespace Retake.Views
{
    /// <summary>
    /// Every label, hint, tooltip and message of the review views, in one place (the table of docs\UI.md lists
    /// them). Rules: a tooltip says what happens, with the key in brackets, and never repeats the visible label; a
    /// hint, a placeholder, a label and a tooltip never restate each other; no text appears twice on the screen.
    /// </summary>
    public static class Copy
    {
        // Title bar: the video and its frame rate.
        public const string RevealTip = "Show in Explorer";
        public const string RevealVideo = "Video file", RevealNotes = "Notes folder", RevealFrames = "Frame images";
        public const string RateTip = "Frame rate of the timecodes", RateUnknownTip = "Frame rate unknown: choose one to add notes";
        public const string RateUnknown = "Set fps", RateSuffix = " fps";
        public const string RateFromFile = "Use the file's rate ({0})", RateFromPlayer = "Use {0}, read by the player";
        public const string RateOtherTip = "Type a rate and press Enter", RateOtherPlaceholder = "Other";
        public const string RateInvalid = "Type a rate such as 25 or 29.97";

        // Title bar: the aside buttons.
        public const string OpenTip = "Open a video (Ctrl+O)";
        public const string RecentTip = "Recent videos";
        public const string CopyTip = "Copy the notes as text";
        public const string CopyNotes = "Notes file", CopyReadable = "Text for a chat";
        public const string CopyNotesTip = "The data file as it is on disk", CopyReadableTip = "Plain lines without formatting, ready to paste into a message";
        public const string KeysTip = "Keyboard shortcuts (?)";
        public const string PanelHideTip = "Hide the notes list", PanelShowTip = "Show the notes list";
        public const string FullTip = "Full screen (F11)", FullExitTip = "Leave full screen (F11)";
        public const string DoneLabel = "Done", DoneTip = "Pack the notes and their frames into a zip to send (Ctrl+E)", DoneOffTip = "Add a note first";

        // Empty state.
        public const string EmptyTitle = "Review a video";
        public const string EmptyHint = "Drop a file anywhere on the window";
        public const string EmptyButton = "Choose a video";
        public const string EmptyButtonTip = "Opens the file dialog (Ctrl+O)";
        public const string RecentHeading = "Recent";
        public const string Loading = "Starting the player";

        // Recent entries and their menu.
        public const string RecentRowTip = "Open · right-click for more";
        public const string RecentMissingTip = "The video is no longer there · right-click for more";
        public const string RecentMissing = "Video not found";
        public const string MenuOpen = "Open", MenuReveal = "Show in Explorer", MenuDelete = "Delete notes", MenuForget = "Remove from recent";
        public const string MenuDeleteTip = "Deletes the notes folder; asks first", MenuForgetTip = "Takes it off the list; files stay";
        public const string DeleteAsk = "Delete {0} and {1}?", DeleteAskEmpty = "Delete the empty notes folder?";
        public const string Deleted = "Deleted {0} and {1}", DeletedEmpty = "Deleted the empty notes folder";

        // Transport.
        public const string FirstTip = "First frame (Home)", BackTip = "Previous frame (←)", ForwardTip = "Next frame (→)", LastTip = "Last frame (End)";
        public const string PlayTip = "Play (Space)", PauseTip = "Pause (Space)";
        public const string SpeedTip = "Playback speed (L)";
        public const string TimecodeTip = "Copy the timecode", DurationTip = "Copy the duration", FrameTip = "Copy the frame number";
        public const string FrameLabel = "frame ";
        public const string FixLabel = "Fix", AddLabel = "Add", RemoveLabel = "Remove";
        public const string FixTip = "New note: something to correct (1)", AddTip = "New note: something missing (2)", RemoveTip = "New note: something to take out (3)";

        // Composer and hint over the video.
        public const string HintCreate = "Drag to mark a region, click to put a point";
        public const string HintEdit = "Drag to replace the region";
        public const string HintGeneral = "A note about the whole video";
        public const string KeysCreate = "Enter saves · Esc cancels";
        public const string KeysEdit = "Backspace in the empty field removes it · Enter saves · Esc cancels";
        public const string FixPlaceholder = "What to fix", AddPlaceholder = "What to add", RemovePlaceholder = "What to remove";
        public const string FieldTip = "One line; Enter saves, Esc cancels";
        public const string NeedText = "Type the note first";
        public const string KindChipTip = "Change the type (Ctrl+{0})";

        // Notes list.
        public const string NotesTitle = "Notes";
        public const string NotesEmpty = "No notes yet";
        public const string NotesEmptyHint = "Pause on a frame and press 1, 2 or 3. G adds a note about the whole video.";
        public const string NoteRowTip = "Go to this frame · double-click to edit (F2) · right-click for more";
        public const string GeneralRowTip = "Select · double-click to edit (F2) · right-click for more";
        public const string GeneralLabel = "General";
        public const string AddGeneralTip = "New general note, about the whole video (G)";
        public const string NoText = "No text";

        // Timeline.
        public const string RulerTip = "Click or drag to move the playhead · wheel zooms (0 fits all)";
        public const string ScrollTip = "Drag to scroll the zoomed timeline (Shift+wheel)";
        public const string TagCardHint = "Drag to move · double-click to edit · right-click for more";

        // Context menus (the key of an entry is shown on its right).
        public const string MenuEdit = "Edit", MenuCopyTimecode = "Copy the timecode", MenuCopyText = "Copy the text";
        public const string MenuMoveHere = "Move to the playhead", MenuRemoveNote = "Delete";
        public const string MenuMoveHereTip = "Puts the note on the frame shown now";
        public const string MenuPlay = "Play", MenuPause = "Pause";
        public const string MenuNewFix = "New FIX note", MenuNewAdd = "New ADD note", MenuNewRemove = "New REMOVE note", MenuNewGeneral = "New general note";
        public const string MenuNewFixHere = "New FIX note here", MenuNewAddHere = "New ADD note here", MenuNewRemoveHere = "New REMOVE note here";
        public const string MenuFull = "Full screen", MenuFullExit = "Leave full screen", MenuFit = "Fit the whole video";
        public const string MenuCopyNotes = "Copy the notes file", MenuCopyReadable = "Copy the text for a chat", MenuDone = "Done: pack into a zip";

        // Key map.
        public const string KeysTitle = "Keyboard", KeysCloseTip = "Close (Esc)";

        // Banner and toasts.
        public const string BannerTip = "Dismiss";
        public const string NotesCopied = "Notes file copied", ReadableCopied = "Notes copied as text", Copied = "Copied {0}";
        public const string Packed = "Packed into {0}", PackedShow = "Show";
        public const string NoteRemoved = "Note removed";
        public const string NeedRate = "Choose the frame rate to add notes";
        public const string CannotPlay = "{0}: {1}";
        public const string PlayerFailed = "The player could not start: {0}";
        public const string ImageFailed = "The frame image could not be made, so the note was saved without it ({0})";
        public const string ClipboardBusy = "The clipboard is in use by another program; try again";
        public const string RevealMissing = "Nothing to show yet";

        public static string Count(int n, string one, string many) => n + " " + (n == 1 ? one : many);
        public static string Notes(int n) => Count(n, "note", "notes");
        public static string Frames(int n) => Count(n, "frame", "frames");
    }
}
