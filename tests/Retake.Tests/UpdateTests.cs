using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Retake.Design.Controls;
using Retake.Update;
using SelfUpdate;
using ToastLevel = Retake.Design.Controls.ToastLevel;

namespace Retake.Tests
{
    /// <summary>
    /// The pure parts of the self-update wiring: the application's values for the library, the tooltips of the
    /// update views, the toast timing, the presenter driven by a stubbed status (the sheet, the pill, the one action
    /// button, the sticky ready toast) and the release asset script. The library has its own tests; the swap and the
    /// network are covered by build\e2e-update.ps1 and build\e2e-handoff.ps1.
    /// </summary>
    public static class UpdateTests
    {
        static UpdateStatus Status(UpdateStage stage, string latest = "2.0.1", bool canApply = true)
        {
            return new UpdateStatus
            {
                Stage = stage,
                Current = AppVersion.Version,
                Latest = latest,
                Notes = "## What is new\n- Faster **start**\n- A fix",
                Size = 12582912,
                PublishedAt = 1792000000,
                CanApply = canApply,
                Progress = stage == UpdateStage.Downloading ? 0.42 : 0,
                Message = "",
            };
        }

        static UpdatePresenter Presenter(TestEnv t, List<UpdateToast> toasts, out Updater updater)
        {
            updater = new Updater(UpdateSetup.Config(t.Combine("updates"), t.Combine("Retake.exe")), null);
            var presenter = new UpdatePresenter(updater, a => a());
            presenter.Zone = TimeZoneInfo.Utc;
            presenter.OnToast = toasts.Add;
            return presenter;
        }

        [Test("update.setup.values")]
        public static void SetupValues(TestEnv t)
        {
            UpdateConfig c = UpdateSetup.Config(t.Combine("updates"), t.Combine("Retake.exe"));
            Check.Equal("softoblivion/retake", c.Repo);
            Check.Equal("Retake.exe", c.AssetName);
            Check.Equal("Retake", c.AppName);
            Check.Equal(AppVersion.Version, c.Version);
            Check.Equal(t.Combine("updates"), c.UpdatesDir);
            Check.Equal(t.Combine("Retake.exe"), c.ExePath);
            Check.Equal("RETAKE_RELEASE_URL", c.ReleaseUrlVariable);
            Check.Equal("SHA256SUMS", c.SumsName);
            Check.Equal("Retake", c.Stem);
            Check.Equal("", UpdateSetup.RelaunchArguments, "the swapped-in version starts with no arguments");
        }

        [Test("update.setup.exe_path_is_the_running_file")]
        public static void ExePathIsTheRunningFile(TestEnv t)
        {
            // A 1.x copy that was updated into Retake keeps its file name; the asset name is only what the release is searched for.
            string kept = t.Combine("Frame Notes", "FrameNotes.exe");
            UpdateConfig c = UpdateSetup.Config(t.Combine("updates"), kept);
            Check.Equal(kept, c.ExePath);
            Check.Equal("Retake.exe", c.AssetName);
            Check.Equal("", UpdateSetup.Config(t.Combine("updates"), null).ExePath, "from source: downloadable, not installable");
        }

        [Test("update.setup.download_names")]
        public static void DownloadNames(TestEnv t)
        {
            var updater = new Updater(UpdateSetup.Config(t.Combine("updates"), t.Combine("Retake.exe")), null);
            Check.Equal(t.Combine("updates", "Retake-2.0.1.exe"), updater.FileFor("2.0.1"));
            Check.Equal("https://github.com/softoblivion/retake/releases/latest", updater.ReleasePage);
            Check.Equal(AppVersion.Version, updater.Status.Current);
        }

        [Test("update.toast.timing")]
        public static void ToastTimingRules(TestEnv t)
        {
            Check.Equal(TimeSpan.FromSeconds(3), ToastTiming.Lifetime(ToastLevel.Info, false));
            Check.Equal(TimeSpan.FromSeconds(3), ToastTiming.Lifetime(ToastLevel.Success, false));
            Check.Equal(TimeSpan.FromSeconds(10), ToastTiming.Lifetime(ToastLevel.Warning, false));
            Check.Equal(TimeSpan.FromSeconds(10), ToastTiming.Lifetime(ToastLevel.Error, false));
            foreach (ToastLevel level in Enum.GetValues(typeof(ToastLevel)))
                Check.Null(ToastTiming.Lifetime(level, true), "a sticky " + level + " toast has no timer");
        }

        [Test("update.copy.tooltips")]
        public static void Tooltips(TestEnv t)
        {
            var toasts = new List<UpdateToast>();
            UpdatePresenter p = Presenter(t, toasts, out Updater _);
            var seen = new List<UpdateButton>();
            foreach (UpdateStage stage in new[] { UpdateStage.Available, UpdateStage.Downloading, UpdateStage.Ready, UpdateStage.Error, UpdateStage.UpToDate })
            {
                p.Show(Status(stage));
                seen.Add(p.View.Sheet.Action);
                seen.Add(p.View.Panel.RowAction);
            }
            p.Show(Status(UpdateStage.Ready, canApply: false));
            seen.Add(p.View.Sheet.Action);
            foreach (UpdateButton b in seen)
            {
                string? tip = UpdateCopy.Tip(b);
                if (b.Enabled)
                {
                    Check.NotNull(tip, "an enabled button explains its click: " + b.Label);
                    Check.NotEqual(b.Label, tip, "a tooltip never repeats the label");
                }
                else Check.Equal(b.Tooltip, tip, "a disabled button shows the library's reason");
            }
            UpdateButton restart = p.View.Sheet.Action;
            Check.False(restart.Enabled, "a run from source cannot restart");
            Check.NotNull(UpdateCopy.Tip(restart), "and says why");
        }

        [Test("update.wiring.sheet_opens_for_a_new_version")]
        public static void SheetOpensForANewVersion(TestEnv t)
        {
            var toasts = new List<UpdateToast>();
            UpdatePresenter p = Presenter(t, toasts, out Updater _);
            Check.False(p.View.Sheet.Open);
            Check.Equal(PillState.Idle, p.View.Pill.State);
            Check.Equal("v2.0.0", p.View.Pill.Text);
            p.Show(Status(UpdateStage.UpToDate, latest: null!));
            Check.False(p.View.Sheet.Open, "never for an up-to-date check");

            p.Show(Status(UpdateStage.Available));
            SheetView s = p.View.Sheet;
            Check.True(s.Open);
            Check.Equal("Retake 2.0.1", s.Title);
            Check.Equal("Installed v2.0.0 · 12.0 MB · " + UpdateText.ShortDate(1792000000, TimeZoneInfo.Utc), s.Subtitle);
            Check.Equal(UpdateCommand.Install, s.Action.Command);
            Check.True(s.Action.Accent && s.Action.Enabled);
            Check.Equal("Install", s.Action.Label);
            Check.Equal("Later", s.LaterLabel);
            Check.Equal(3, s.Notes.Count, "a heading and two bullets");
            Check.Equal(PillState.Available, p.View.Pill.State);
            Check.Equal("Retake 2.0.1 is available", p.View.Pill.Tooltip);
            Check.Equal(0, toasts.Count, "the sheet is the notice, no toast");
        }

        [Test("update.wiring.later_and_pill")]
        public static void LaterAndPill(TestEnv t)
        {
            var toasts = new List<UpdateToast>();
            UpdatePresenter p = Presenter(t, toasts, out Updater _);
            p.Show(Status(UpdateStage.Available));
            p.Later();
            Check.False(p.View.Sheet.Open, "Later closes the sheet");
            Check.False(p.SheetOpen);
            p.Show(Status(UpdateStage.Available));
            Check.False(p.View.Sheet.Open, "the same version does not open it again by itself");
            Check.True(p.PillClick(), "the pill opens it");
            Check.True(p.View.Sheet.Open);
            p.Later();
            p.Show(Status(UpdateStage.UpToDate, latest: null!));
            Check.False(p.PillClick(), "no newer version: the pill leaves the click to the settings");
        }

        [Test("update.wiring.install_downloads_then_restarts")]
        public static void InstallDownloadsThenRestarts(TestEnv t)
        {
            var toasts = new List<UpdateToast>();
            UpdatePresenter p = Presenter(t, toasts, out Updater _);
            int downloads = 0, applies = 0, quits = 0;
            p.StartDownload = () => downloads++;
            p.ApplyUpdate = () => { applies++; return true; };
            p.OnQuit = () => quits++;
            p.HasActiveWork = () => false;
            p.Show(Status(UpdateStage.Available));
            p.Run(p.View.Sheet.Action.Command);
            Check.Equal(1, downloads);
            p.Show(Status(UpdateStage.Downloading));
            Check.Equal("Downloading… 42%", p.View.Sheet.Action.Label);
            Check.False(p.View.Sheet.Action.Enabled);
            Check.True(p.View.Sheet.ShowProgress);
            Check.Equal("42% · 5.0 MB of 12.0 MB", p.View.Sheet.ProgressText);
            Check.Equal(PillState.Downloading, p.View.Pill.State);
            Check.Equal(0, applies);
            p.Show(Status(UpdateStage.Ready));
            Check.Equal(1, applies, "the install the user asked for restarts at once");
            Check.Equal(1, quits, "and leaves through the window's own close path");
            Check.Equal(0, toasts.Count, "no ready toast for an install the user asked for");
        }

        [Test("update.wiring.automatic_download_ends_in_a_sticky_toast")]
        public static void AutomaticDownloadEndsInAStickyToast(TestEnv t)
        {
            var toasts = new List<UpdateToast>();
            UpdatePresenter p = Presenter(t, toasts, out Updater _);
            int quits = 0;
            p.ApplyUpdate = () => true;
            p.OnQuit = () => quits++;
            p.Show(Status(UpdateStage.Downloading));
            p.Later();
            p.Show(Status(UpdateStage.Ready));
            Check.Equal(1, toasts.Count);
            UpdateToast toast = toasts[0];
            Check.True(toast.Sticky, "the ready notice stays until it is clicked");
            Check.Equal("Retake 2.0.1 is ready", toast.Text);
            Check.Equal("Restart", toast.ActionLabel);
            Check.Equal(PillState.Ready, p.View.Pill.State);
            Check.Equal("Restart to update", p.View.Panel.RowAction.Label);
            p.Show(Status(UpdateStage.Ready));
            Check.Equal(1, toasts.Count, "once per version");
            toast.Action();
            Check.Equal(1, quits);
        }

        [Test("update.legacy.marker_is_handed_over")]
        public static void LegacyMarkerIsHandedOver(TestEnv t)
        {
            string legacy = t.Combine("frame-notes_data", "updates");
            string updates = t.Combine("retake_data", "updates");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "applied.json"), "{\"version\": \"2.0.0\", \"at\": 1790000000.5}");
            Check.False(LegacyUpdates.AdoptMarker(null, updates), "no legacy folder (environment override)");
            Check.True(LegacyUpdates.AdoptMarker(legacy, updates));
            Check.False(File.Exists(Path.Combine(legacy, "applied.json")));
            Check.Equal("{\"version\": \"2.0.0\", \"at\": 1790000000.5}", File.ReadAllText(Path.Combine(updates, "applied.json")));
            File.WriteAllText(Path.Combine(legacy, "applied.json"), "{\"version\": \"9.9.9\"}");
            Check.False(LegacyUpdates.AdoptMarker(legacy, updates), "a marker already there wins");
            Check.False(File.Exists(Path.Combine(legacy, "applied.json")));
            Check.Contains("2.0.0", File.ReadAllText(Path.Combine(updates, "applied.json")));
            Check.False(LegacyUpdates.AdoptMarker(t.Combine("nothing"), updates), "no folder, no marker");
        }

        [Test("update.legacy.old_downloads_are_removed")]
        public static void LegacyOldDownloadsAreRemoved(TestEnv t)
        {
            string legacy = t.Combine("frame-notes_data", "updates");
            Directory.CreateDirectory(legacy);
            foreach (string name in new[] { "Retake-2.0.0.exe", "Retake-1.9.0.exe", "Retake-2.0.0.exe.part", "Retake-2.1.0.exe", "apply.log", "FrameNotes-1.4.1.exe", "Retake-latest.exe" })
                File.WriteAllText(Path.Combine(legacy, name), "x");
            Check.Equal(0, LegacyUpdates.CleanDownloads(legacy, "Retake", ".exe", "2.0.0"));
            var left = new List<string>();
            foreach (string f in Directory.GetFiles(legacy)) left.Add(Path.GetFileName(f));
            left.Sort(StringComparer.Ordinal);
            Check.Equal(new[] { "FrameNotes-1.4.1.exe", "Retake-2.1.0.exe", "Retake-latest.exe", "apply.log" }, left);
            Check.Equal(0, LegacyUpdates.CleanDownloads(null, "Retake", ".exe", "2.0.0"));
            Check.Equal(0, LegacyUpdates.CleanDownloads(t.Combine("nothing"), "Retake", ".exe", "2.0.0"));
        }

        [Test("update.legacy.held_file_is_counted")]
        public static void LegacyHeldFileIsCounted(TestEnv t)
        {
            string legacy = t.Combine("frame-notes_data", "updates");
            Directory.CreateDirectory(legacy);
            string held = Path.Combine(legacy, "Retake-2.0.0.exe");
            File.WriteAllText(held, "x");
            using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check.Equal(1, LegacyUpdates.CleanDownloads(legacy, "Retake", ".exe", "2.0.0"), "the exiting helper still holds its file");
            Check.Equal(0, LegacyUpdates.CleanDownloads(legacy, "Retake", ".exe", "2.0.0"));
            Check.False(File.Exists(held));
        }

        [Test("update.legacy.folder")]
        public static void LegacyFolder(TestEnv t)
        {
            Check.Equal(t.Combine("frame-notes_data", "updates"), LegacyUpdates.FolderOf(t.Tmp, false));
            Check.Null(LegacyUpdates.FolderOf(t.Tmp, true), "RETAKE_HOME redirects the data folder: nothing to hand over");
        }

        [Test("update.sums.script_writes_the_release_asset_line")]
        public static void SumsScript(TestEnv t)
        {
            string root = FindRoot();
            string exe = t.Combine("built", "Retake-x.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllBytes(exe, Encoding.ASCII.GetBytes("not an exe, a stand-in"));
            string outDir = t.Combine("release out");
            var start = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "build", "write-sums.ps1") + "\" -Exe \"" + exe + "\" -OutDir \"" + outDir + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using (Process p = Process.Start(start)!)
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                Check.Equal(0, p.ExitCode, output);
            }
            string asset = Path.Combine(outDir, UpdateSetup.AssetName);
            Check.True(File.Exists(asset), "the exe is copied under the asset name");
            byte[] sums = File.ReadAllBytes(Path.Combine(outDir, "SHA256SUMS"));
            Check.False(sums.Length >= 3 && sums[0] == 0xEF && sums[1] == 0xBB && sums[2] == 0xBF, "no byte order mark");
            string text = Encoding.ASCII.GetString(sums);
            string hash = Checksums.Sha256File(asset);
            Check.Equal(hash + "  Retake.exe\n", text);
            Check.Equal(hash, Checksums.Find(text, "Retake.exe"), "the library reads the line");
        }

        static string FindRoot()
        {
            for (string? dir = AppDomain.CurrentDomain.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "Retake.sln"))) return dir;
            throw new InvalidOperationException("Retake.sln not found above " + AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
