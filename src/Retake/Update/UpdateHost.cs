using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Retake.Core;
using Retake.Design;
using Retake.Design.Controls;
using SelfUpdate;

namespace Retake.Update
{
    /// <summary>
    /// Connects the self-update library to the window: the updater with the application's values
    /// (<see cref="UpdateSetup"/>), the presenter on the dispatcher, the periodic checks, the update sheet, the
    /// version pill, the Settings rows and the toast layer. The behaviour lives in the library
    /// (<c>Updater</c>, <c>UpdatePresenter</c>, <c>UpdateScheduler</c>); this class only connects it and draws its
    /// models with the design layer.
    /// <para>
    /// Order in <c>Program.Main</c>: the apply mode first of all; the host after the window and the review are
    /// built; <see cref="Start"/> runs once the window has rendered (cleanup, the "Updated to" toast, the launch
    /// check, then a check every 5 minutes); the window's close path disposes it first.
    /// </para>
    /// </summary>
    internal sealed class UpdateHost : IDisposable
    {
        internal readonly Updater Updater;
        internal readonly UpdatePresenter Presenter;
        readonly ShellWindow window;
        readonly Paths paths;
        readonly UpdateScheduler scheduler;
        readonly UpdateSheet sheet;
        readonly UpdatePopover popover;
        readonly UpdateRows rows;
        bool started, disposed;

        /// <param name="window">The application window.</param>
        /// <param name="paths">Where the downloads and the apply marker live (<c>retake_data\updates</c>).</param>
        /// <param name="quit">The window's own close path: settings saved, player shut down, exit.</param>
        internal UpdateHost(ShellWindow window, Paths paths, Action quit)
        {
            this.window = window;
            this.paths = paths;
            Dispatcher dispatcher = window.Dispatcher;
            Updater = new Updater(UpdateSetup.Config(paths.UpdatesDir, UpdateApply.SelfPath), null);
            // Read on the worker thread of a check that finds a newer release.
            Updater.AutoDownload = () => window.Prefs.AutoDownloadUpdates;
            // Status changes arrive on worker threads; the presenter runs them on the dispatcher.
            Presenter = new UpdatePresenter(Updater, a => dispatcher.BeginInvoke(a));
            Presenter.OnQuit = quit;
            Presenter.OnToast = Toast;
            Presenter.Changed = Render;
            scheduler = new UpdateScheduler(Updater);

            sheet = new UpdateSheet(window.Root.Overlay, window.Root.TitleBar.Pill, Presenter, OpenLink);
            popover = new UpdatePopover(window.Root.Overlay, window.Root.TitleBar.Pill, Presenter, OpenLink);
            rows = new UpdateRows(Presenter);
            window.UpdatesRow = rows.Element;
            window.PillClicked += PillClicked;
            window.Root.Overlay.SheetChanged += _ => SheetChanged();
            window.Prefs.Changed += key => { if (key == "auto_download_updates") Presenter.AutoDownloadChanged(window.Prefs.AutoDownloadUpdates); };
            window.Closing += (s, e) => Dispose();
            window.ContentRendered += (s, e) => Start();
            Render(Presenter.View);
        }

        /// <summary>Once per launch, after the window is shown: cleanup and the "Updated to" toast, the launch check, then a check every 5 minutes.</summary>
        internal void Start()
        {
            if (started || disposed) return;
            started = true;
            // An update from the 1.x line left its marker and download in the 1.x folder: hand them over first.
            string? legacy = LegacyUpdates.FolderOf(Paths.BaseDir, paths.UsesEnvironmentOverride);
            LegacyUpdates.AdoptMarker(legacy, paths.UpdatesDir);
            LegacyUpdates.CleanInBackground(legacy, Updater.Config.Stem, Updater.Config.Extension, AppVersion.Version);
            Updater.Startup();
            Updater.CheckInBackground();
            scheduler.Start();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            scheduler.Dispose();
        }

        // The version pill: the sheet while a newer version is known, else the updates popover.
        void PillClicked()
        {
            if (popover.IsOpen) { popover.Close(); return; }
            if (Presenter.PillClick()) return;
            popover.Toggle();
        }

        // The overlay closed or replaced the sheet (Esc, the veil, the close button, the gear, another sheet): that is Later.
        void SheetChanged()
        {
            if (Presenter.SheetOpen && sheet.Shown == null) Presenter.Later();
        }

        void Render(UpdateView view)
        {
            if (disposed) return;
            VersionPill pill = window.Root.TitleBar.Pill;
            pill.SetStage(Stage(view.Pill.State));
            Tip.Set(pill, view.Pill.Tooltip);
            if (view.Sheet.Open) popover.Close();
            sheet.Render(view.Sheet);
            popover.Render(view.Panel);
            rows.Show(view);
        }

        internal static PillStage Stage(PillState state)
        {
            switch (state)
            {
                case PillState.Available: return PillStage.Available;
                case PillState.Downloading: return PillStage.Downloading;
                case PillState.Ready: return PillStage.Ready;
                default: return PillStage.Idle;
            }
        }

        static Retake.Design.Controls.ToastLevel Level(SelfUpdate.ToastLevel level)
        {
            switch (level)
            {
                case SelfUpdate.ToastLevel.Success: return Retake.Design.Controls.ToastLevel.Success;
                case SelfUpdate.ToastLevel.Warning: return Retake.Design.Controls.ToastLevel.Warning;
                case SelfUpdate.ToastLevel.Error: return Retake.Design.Controls.ToastLevel.Error;
                default: return Retake.Design.Controls.ToastLevel.Info;
            }
        }

        void Toast(UpdateToast t)
        {
            if (disposed) return;
            window.Root.Toasts.Show(Level(t.Level), t.Text, t.ActionLabel, t.Action, t.Sticky);
        }

        // Release-note links (https only, the renderer drops other targets) open in the default browser.
        bool OpenLink(string url)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                Toast(new UpdateToast { Level = SelfUpdate.ToastLevel.Error, Text = UpdateText.LinkFailed });
                return false;
            }
        }
    }
}
