namespace Retake.Views
{
    /// <summary>Paths as short display text (pure).</summary>
    public static class PathText
    {
        /// <summary>The last two parts of a folder (<c>Review\review-3</c>), with <c>…\</c> in front when there are more.</summary>
        public static string ShortFolder(string folder)
        {
            string[] parts = folder.TrimEnd('\\', '/').Split('\\', '/');
            if (parts.Length <= 3) return folder;
            return "…\\" + parts[parts.Length - 2] + "\\" + parts[parts.Length - 1];
        }
    }
}
