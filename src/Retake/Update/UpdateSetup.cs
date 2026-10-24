using SelfUpdate;

namespace Retake.Update
{
    /// <summary>
    /// The application's values for the self-update library, in one place. No WPF in this file, so the test runner
    /// compiles it together with the library. The build script reads <see cref="AssetName"/> from here.
    /// </summary>
    internal static class UpdateSetup
    {
        /// <summary>GitHub owner/name whose Releases are checked.</summary>
        // The repository's name before its rename to softoblivion/retake; switched together with the rename.
        internal const string Repository = "softoblivion/frame-notes";

        /// <summary>The release asset a build is published as; also what the 1.x line searches a release for.</summary>
        internal const string AssetName = "Retake.exe";

        /// <summary>The name in messages, the sheet title and toasts.</summary>
        internal const string ProductName = "Retake";

        /// <summary>Environment variable that replaces the release source (a release document at that URL) in tests.</summary>
        internal const string ReleaseUrlVariable = "RETAKE_RELEASE_URL";

        /// <summary>Command line of the swapped-in version: none, the review window opens as on any launch.</summary>
        internal const string RelaunchArguments = "";

        /// <summary>
        /// The library configuration. <paramref name="exePath"/> is the running exe's real path (the file may carry
        /// another name than <see cref="AssetName"/>, as a 1.x copy that was updated into Retake does); null or empty
        /// makes an update downloadable but not installable.
        /// </summary>
        internal static UpdateConfig Config(string updatesDir, string? exePath)
        {
            return new UpdateConfig
            {
                Repo = Repository,
                AssetName = AssetName,
                AppName = ProductName,
                Version = AppVersion.Version,
                UpdatesDir = updatesDir,
                ExePath = exePath ?? "",
                ReleaseUrlVariable = ReleaseUrlVariable,
            };
        }
    }
}
