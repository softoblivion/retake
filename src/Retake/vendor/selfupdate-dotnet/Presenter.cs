using System;
using System.Collections.Generic;
namespace SelfUpdate {
// What a window shows for the updater, without any UI type: the version pill, the update sheet (the notice of a new
// version), the updates panel (a popover or a settings section) and the toasts. The host draws these models in its
// own look and sends the user's clicks back. The presenter lives on the host's UI thread: it takes over
// Updater.OnStatus and Updater.OnToast and runs both through the host's post function (Dispatcher.BeginInvoke in
// WPF); every method is called on that thread.
//
// Rules of the update notice:
// - The sheet opens by itself the first time in a run that a newer version is reported (Available, or Downloading /
//   Ready for a version not announced yet); never for an up-to-date or failed check. Later (and Esc, the close button,
//   Alt+F4 of a sheet window) closes it and disarms a pending Install; it does not open again by itself for the same
//   version; the pill opens it again while a newer version is known.
// - Install at Available starts the download and arms the install: when that version is Ready the presenter restarts
//   into it at once (through the restart warning when the host reports work in progress).
// - Ready raises one sticky toast "<App> X.Y.Z is ready" with Restart (View when the copy cannot apply) when the sheet
//   is closed at that moment and the user did not ask for the install.
// - A re-check that ends up to date closes an open sheet.
enum PillState { Idle, Available, Downloading, Ready }
enum UpdateCommand { None, Check, Download, Install, Restart, Retry }
enum ToastLevel { Info, Success, Warning, Error }
class UpdateButton {
    // Accent: the tinted (primary) look. Tooltip: why a disabled button is disabled; null otherwise.
    internal string Label,Tooltip; internal bool Enabled,Accent; internal UpdateCommand Command;
}
class UpdateToast {
    // Sticky: stays until clicked (no timer). ActionLabel / Action: one button, null when none.
    internal ToastLevel Level; internal string Text,ActionLabel; internal Action Action; internal bool Sticky;
}
class PillView {
    // State: the dot (none / accent / pulsing accent / mint). OpensSheet: a click opens the sheet (else the host's
    // updates panel).
    internal string Text,Tooltip; internal PillState State; internal bool OpensSheet;
}
class SheetView {
    // Notes: the same list instance while the notes are unchanged (a view may keep its scroll position); empty when the
    // release has none (then EmptyNotes is shown). Progress: 0..1, shown while ShowProgress.
    internal bool Open,Busy,ShowProgress; internal string Title,Subtitle,EmptyNotes,ProgressPercent,ProgressDetail,ProgressText,Error,LaterLabel;
    internal List<NoteBlock> Notes; internal double Progress; internal UpdateButton Action;
}
class PanelRow { internal string Name,Version; internal List<string> Tags=new List<string>(); internal bool IsNew,IsReady; }
class PanelView {
    // The updates popover and the "Updates" rows of the settings: StatusLine with Action (popover) or RowAction
    // (settings row); Indeterminate while checking, ShowProgress while downloading; AutoLine null when automatic
    // download is off; Checked "Checked 5 min ago" (popover footer), CheckedAt "Checked Oct 21, 4:05 PM" (settings row).
    internal string Title,StatusLine,AutoLine,Checked,CheckedAt,Error,ProgressPercent,ProgressDetail; internal bool Indeterminate,ShowProgress;
    internal double Progress; internal List<PanelRow> Rows=new List<PanelRow>(); internal List<NoteBlock> Notes; internal UpdateButton Action,RowAction;
}
class UpdateView { internal UpdateStatus Status; internal PillView Pill; internal SheetView Sheet; internal PanelView Panel; }

class UpdatePresenter {
    internal Action<UpdateView> Changed=null;   // every change of what the views show
    internal Action<UpdateToast> OnToast=null;  // the "updated to" notice, the ready notice, the restart warning
    internal Action OnQuit=null;           // the update helper started: save state and exit the way Quit does
    internal Func<bool> HasActiveWork=null; // true: Restart first shows RestartWarning with "Restart anyway"
    internal string RestartWarning=UpdateText.RestartWarning;
    internal bool AutoOpenSheet=true;
    internal TimeZoneInfo Zone=null;       // dates; null: the local time zone
    internal Func<long> Clock=UpdateText.EpochNow; // epoch seconds now, for "Checked 5 min ago"
    // Seams for tests: the default calls go to the updater.
    internal Action StartCheck,StartDownload; internal Func<bool> ApplyUpdate;

    readonly Updater updater; readonly string product;
    UpdateStatus status,shown; string announced,armed,readyToasted,notesSource; bool sheetOpen,armedStarted; List<NoteBlock> notes=new List<NoteBlock>();
    UpdateView view;

    internal UpdatePresenter(Updater updater,Action<Action> post) {
        this.updater=updater; product=updater.Config.AppName; status=updater.Status;
        StartCheck=updater.CheckInBackground; StartDownload=updater.DownloadInBackground; ApplyUpdate=updater.Apply;
        if(post==null)post=delegate(Action a) { a(); };
        updater.OnStatus=delegate(UpdateStatus s) { post(delegate { Show(s); }); };
        updater.OnToast=delegate(string text) { post(delegate { Toast(ToastLevel.Success,text,null,null,false); }); };
        view=Build();
    }
    internal UpdateView View { get { return view; } }
    internal bool SheetOpen { get { return sheetOpen; } }
    internal string Armed { get { return armed; } }
    static bool Newer(UpdateStatus s) {
        return s.Latest!=null&&SemVer.IsNewer(s.Latest,s.Current)&&(s.Stage==UpdateStage.Available||s.Stage==UpdateStage.Downloading||s.Stage==UpdateStage.Ready);
    }
    // While a newer version is known, or its download failed, the sheet is the place of the update.
    bool SheetAvailable { get { return shown!=null&&(Newer(status)||(status.Stage==UpdateStage.Error&&status.Latest!=null)); } }

    // Every updater status, on the UI thread.
    internal void Show(UpdateStatus s) {
        if(s==null)return; status=s; bool newer=Newer(s),restart=false;
        if(newer)shown=s;
        if(s.Stage==UpdateStage.UpToDate) { shown=null; armed=null; sheetOpen=false; }
        if(armed!=null&&newer&&s.Latest==armed) {
            if(s.Stage==UpdateStage.Downloading)armedStarted=true;
            else if(s.Stage==UpdateStage.Ready) { armed=null; readyToasted=s.Latest; restart=s.CanApply; }
            else if(s.Stage==UpdateStage.Available&&!armedStarted)StartDownload(); // the first request met a running check
        }
        if(newer&&AutoOpenSheet&&announced!=s.Latest) { announced=s.Latest; sheetOpen=true; }
        if(s.Stage==UpdateStage.Ready&&s.Latest!=null&&readyToasted!=s.Latest&&!sheetOpen) {
            readyToasted=s.Latest;
            if(s.CanApply)Toast(ToastLevel.Success,product+" "+s.Latest+" is ready",UpdateText.Restart,Restart,true);
            else Toast(ToastLevel.Success,product+" "+s.Latest+" is ready",UpdateText.View,delegate { OpenSheet(); },true);
        }
        Publish();
        if(restart)Restart();
    }

    // ---- user actions ----
    // A click on the version pill: true when it opened the sheet; false means the host shows its updates panel.
    internal bool PillClick() { return OpenSheet(); }
    internal bool OpenSheet() { if(!SheetAvailable)return false; if(!sheetOpen) { sheetOpen=true; Publish(); } return true; }
    internal void Later() { armed=null; if(sheetOpen) { sheetOpen=false; Publish(); } }
    internal void Run(UpdateCommand command) {
        switch(command) {
            case UpdateCommand.Check: StartCheck(); break;
            case UpdateCommand.Download: StartDownload(); break;
            case UpdateCommand.Install: Install(); break;
            case UpdateCommand.Restart: Restart(); break;
            case UpdateCommand.Retry: Retry(); break;
        }
    }
    // Install: at Ready the restart; otherwise the download, armed to restart when the file is verified.
    internal void Install() {
        if(status.Stage==UpdateStage.Ready) { Restart(); return; }
        if(status.Latest==null)return;
        armed=status.Latest; armedStarted=false; StartDownload(); Publish();
    }
    // Try again: the download when the failed step was a download of a known version, otherwise the check.
    internal void Retry() { if(status.Stage==UpdateStage.Error&&status.Latest!=null)StartDownload(); else StartCheck(); }
    internal void Restart() {
        Func<bool> busy=HasActiveWork;
        if(busy!=null&&busy()) { Toast(ToastLevel.Warning,RestartWarning,UpdateText.RestartAnyway,RestartNow,false); return; }
        RestartNow();
    }
    void RestartNow() { if(ApplyUpdate()) { Action quit=OnQuit; if(quit!=null)quit(); } }
    // The automatic-download switch changed (after the host saved it): turning it on at Available starts the download.
    internal void AutoDownloadChanged(bool on) { if(on&&status.Stage==UpdateStage.Available)StartDownload(); Publish(); }
    // Rebuild the views (relative times such as "Checked 5 min ago" when a panel opens).
    internal void Refresh() { Publish(); }

    void Toast(ToastLevel level,string text,string actionLabel,Action action,bool sticky) {
        Action<UpdateToast> handler=OnToast; if(handler==null)return;
        handler(new UpdateToast { Level=level,Text=text,ActionLabel=actionLabel,Action=action,Sticky=sticky });
    }
    void Publish() { view=Build(); Action<UpdateView> handler=Changed; if(handler!=null)handler(view); }

    List<NoteBlock> NotesOf(string markdown) {
        if(markdown!=notesSource) { notesSource=markdown; notes=ReleaseNotes.Parse(markdown); }
        return notes;
    }
    UpdateView Build() {
        UpdateStatus s=status; bool newer=Newer(s);
        UpdateView v=new UpdateView { Status=s.Copy() };
        v.Pill=new PillView { Text=string.IsNullOrEmpty(s.Current)?"Version":"v"+s.Current,State=PillState.Idle,Tooltip=UpdateText.PillTip,OpensSheet=SheetAvailable };
        if(newer) {
            if(s.Stage==UpdateStage.Available) { v.Pill.State=PillState.Available; v.Pill.Tooltip=product+" "+s.Latest+" is available"; }
            else if(s.Stage==UpdateStage.Downloading) { v.Pill.State=PillState.Downloading; v.Pill.Tooltip="Downloading "+product+" "+s.Latest; }
            else { v.Pill.State=PillState.Ready; v.Pill.Tooltip=product+" "+s.Latest+" is ready to install"; }
        }
        v.Sheet=BuildSheet(s); v.Panel=BuildPanel(s,newer);
        return v;
    }
    SheetView BuildSheet(UpdateStatus s) {
        UpdateStatus info=shown??s;
        SheetView v=new SheetView { Open=sheetOpen,Title=product+(info.Latest!=null?" "+info.Latest:""),EmptyNotes=UpdateText.NoNotes,LaterLabel=UpdateText.Later };
        List<string> parts=new List<string>();
        if(!string.IsNullOrEmpty(info.Current))parts.Add("Installed v"+info.Current);
        if(info.Size>0)parts.Add(UpdateText.Bytes(info.Size));
        if(info.PublishedAt>=0)parts.Add(UpdateText.ShortDate(info.PublishedAt,Zone));
        v.Subtitle=string.Join(" · ",parts.ToArray());
        v.Notes=NotesOf(info.Notes);
        v.Busy=s.Stage==UpdateStage.Downloading||s.Stage==UpdateStage.Checking;
        if(s.Stage==UpdateStage.Downloading) {
            v.ShowProgress=true; v.Progress=Math.Max(0,Math.Min(1,s.Progress)); v.ProgressPercent=UpdateText.Percent(s.Progress)+"%";
            v.ProgressDetail=UpdateText.ProgressDetail(s.Progress,s.Size); v.ProgressText=UpdateText.Progress(s.Progress,s.Size);
        }
        if(s.Stage==UpdateStage.Error&&!string.IsNullOrEmpty(s.Message))v.Error=s.Message;
        switch(s.Stage) {
            case UpdateStage.Available: v.Action=Button(UpdateText.Install,true,true,UpdateCommand.Install,null); break;
            case UpdateStage.Downloading: v.Action=Button(UpdateText.Downloading+" "+UpdateText.Percent(s.Progress)+"%",false,false,UpdateCommand.None,null); break;
            case UpdateStage.Ready: v.Action=ReadyButton(s); break;
            case UpdateStage.Checking: v.Action=Button(UpdateText.Checking,false,false,UpdateCommand.None,null); break;
            case UpdateStage.Error: v.Action=Button(UpdateText.TryAgain,true,false,UpdateCommand.Retry,null); break;
            default: v.Action=Button(UpdateText.CheckForUpdates,true,false,UpdateCommand.Check,null); break;
        }
        return v;
    }
    static UpdateButton ReadyButton(UpdateStatus s) {
        return Button(UpdateText.RestartToUpdate,s.CanApply,true,UpdateCommand.Restart,s.CanApply?null:(string.IsNullOrEmpty(s.Message)?UpdateText.PackagedOnly:s.Message));
    }
    static UpdateButton Button(string label,bool enabled,bool accent,UpdateCommand command,string tooltip) {
        return new UpdateButton { Label=label,Enabled=enabled,Accent=accent,Command=enabled?command:UpdateCommand.None,Tooltip=tooltip };
    }
    PanelView BuildPanel(UpdateStatus s,bool newer) {
        Func<bool> auto=updater.AutoDownload; bool autoOn=false; try { autoOn=auto!=null&&auto(); } catch { }
        PanelView v=new PanelView { Title=product,StatusLine=UpdateText.StatusLine(s),AutoLine=autoOn?UpdateText.AutoLine:null };
        v.Checked=s.CheckedAt>=0?"Checked "+UpdateText.TimeAgo(s.CheckedAt,Clock(),Zone):UpdateText.NotChecked;
        v.CheckedAt=s.CheckedAt>=0?"Checked "+UpdateText.ShortDateTime(s.CheckedAt,Zone):null;
        v.Indeterminate=s.Stage==UpdateStage.Checking;
        if(s.Stage==UpdateStage.Downloading) {
            v.ShowProgress=true; v.Progress=Math.Max(0,Math.Min(1,s.Progress)); v.ProgressPercent=UpdateText.Percent(s.Progress)+"%"; v.ProgressDetail=UpdateText.ProgressDetail(s.Progress,s.Size);
        }
        if(s.Stage==UpdateStage.Error&&!string.IsNullOrEmpty(s.Message))v.Error=s.Message;
        v.Rows.Add(new PanelRow { Name=UpdateText.Installed,Version=string.IsNullOrEmpty(s.Current)?"—":"v"+s.Current });
        if(newer) {
            PanelRow latest=new PanelRow { Name=UpdateText.Latest,Version="v"+s.Latest,IsNew=s.Stage!=UpdateStage.Ready,IsReady=s.Stage==UpdateStage.Ready };
            if(s.Size>0)latest.Tags.Add(UpdateText.Bytes(s.Size));
            if(s.PublishedAt>=0)latest.Tags.Add(UpdateText.ShortDate(s.PublishedAt,Zone));
            if(s.Stage==UpdateStage.Ready)latest.Tags.Add(UpdateText.DownloadedTag);
            v.Rows.Add(latest);
            if(s.Notes!=null)v.Notes=NotesOf(s.Notes);
        }
        switch(s.Stage) {
            case UpdateStage.Checking: v.Action=Button(UpdateText.Checking,false,false,UpdateCommand.None,null); break;
            case UpdateStage.Available: v.Action=Button(UpdateText.Download,true,true,UpdateCommand.Download,null); break;
            case UpdateStage.Downloading: v.Action=Button(UpdateText.Downloading,false,false,UpdateCommand.None,null); break;
            case UpdateStage.Ready: v.Action=ReadyButton(s); break;
            case UpdateStage.Error: v.Action=Button(UpdateText.TryAgain,true,false,UpdateCommand.Retry,null); break;
            default: v.Action=Button(UpdateText.CheckForUpdates,true,false,UpdateCommand.Check,null); break;
        }
        switch(s.Stage) {
            case UpdateStage.Available: v.RowAction=Button(UpdateText.Download,true,true,UpdateCommand.Download,null); break;
            case UpdateStage.Ready: v.RowAction=ReadyButton(s); break;
            case UpdateStage.Downloading: v.RowAction=Button(UpdateText.Percent(s.Progress)+"%",false,false,UpdateCommand.None,null); break;
            case UpdateStage.Checking: v.RowAction=Button(UpdateText.Checking,false,false,UpdateCommand.None,null); break;
            default: v.RowAction=Button(UpdateText.CheckForUpdates,true,false,UpdateCommand.Check,null); break;
        }
        return v;
    }
}
}
