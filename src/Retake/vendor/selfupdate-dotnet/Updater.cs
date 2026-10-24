using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace SelfUpdate {
// Self-update of a portable one-file Windows exe from the GitHub Releases of its repository: check, size and
// SHA-256 verified download, apply through the downloaded exe (UpdateApply), cleanup in the new instance. No UI
// here: the host subscribes to OnStatus / OnToast (worker threads) and marshals them to its own UI thread.
enum UpdateStage { Idle, Checking, UpToDate, Available, Downloading, Ready, Error }
class UpdateStatus {
    // Notes: Markdown body of the release (null when the source has none); PublishedAt: epoch seconds, -1 unknown;
    // CheckedAt: epoch seconds of the last check that reached GitHub (up to date, available or ready), -1 none yet.
    internal UpdateStage Stage=UpdateStage.Idle; internal string Current,Latest,Notes,Message="Updates are not checked yet"; internal long Size=-1,PublishedAt=-1,CheckedAt=-1; internal double Progress; internal bool CanApply;
    internal UpdateStatus Copy() { return (UpdateStatus)MemberwiseClone(); }
}
// Everything app-specific comes in here. Repo "owner/name"; AssetName the release asset and the running exe's file
// name ("App.exe"); AppName for messages; Version "X.Y.Z" of the running build; UpdatesDir a writable folder for
// downloads and the apply marker; ExePath the running exe (empty: download works, apply is refused);
// ReleaseUrlVariable an environment variable that replaces the release source in tests (empty: none).
class UpdateConfig {
    internal string Repo,AssetName,AppName,Version,UpdatesDir,ExePath,ReleaseUrlVariable,SumsName="SHA256SUMS";
    internal string Stem { get { return Path.GetFileNameWithoutExtension(AssetName); } }
    internal string Extension { get { return Path.GetExtension(AssetName); } }
}
class Release { internal string Tag,Version,AssetUrl,SumsUrl,Notes; internal long Size=-1,PublishedAt=-1; }
class UpdateError : Exception { internal UpdateError(string message) : base(message) { } }
class Updater {
    internal static class Messages {
        internal const string Checking="Checking for updates…",Offline="No internet connection",Limit="GitHub limited the request, try again later",
            NoChecksum="The release has no checksum file",Damaged="The downloaded file is damaged (checksum mismatch)",BadVersion="Could not read the release version",
            NoRelease="No release has been published yet",SaveFailed="Could not save the update file",StartFailed="Could not start the update helper",
            NotReady="The update is not ready to install",Missing="The downloaded update file is missing",Failed="The update check failed";
    }
    internal Action<UpdateStatus> OnStatus=null; internal Action<string> OnToast=null; internal Func<bool> AutoDownload=null; // read once per check that finds a newer release
    internal Func<string,string,bool> Spawn=DefaultSpawn; // (exe, arguments) -> started
    readonly UpdateConfig config; readonly IUpdateNet net; readonly object gate=new object(); readonly List<Action<UpdateStatus>> watchers=new List<Action<UpdateStatus>>();
    UpdateStatus status=new UpdateStatus(); Release release; string expectedSha; bool busy,checking,downloadWanted;
    internal Updater(UpdateConfig config,IUpdateNet net) {
        this.config=config; this.net=net??new WebNet(config.Stem+"/"+config.Version); status.Current=config.Version;
    }
    internal UpdateConfig Config { get { return config; } }
    internal UpdateStatus Status { get { lock(gate)return status.Copy(); } }
    internal string ReleasePage { get { return "https://github.com/"+config.Repo+"/releases/latest"; } }
    internal string FileFor(string version) { return Path.Combine(config.UpdatesDir,config.Stem+"-"+version+config.Extension); }
    string MarkerPath { get { return Path.Combine(config.UpdatesDir,"applied.json"); } }
    // Library-side listeners (UpdateScheduler); called before OnStatus, on the same worker thread.
    internal void Watch(Action<UpdateStatus> watcher) { lock(gate)watchers.Add(watcher); }

    void Set(UpdateStage stage,string message,double progress) {
        UpdateStatus copy; Action<UpdateStatus>[] listeners;
        lock(gate) {
            status.Stage=stage; status.Message=message; status.Progress=progress;
            status.Latest=release!=null?release.Version:null; status.Size=release!=null?release.Size:-1;
            status.Notes=release!=null?release.Notes:null; status.PublishedAt=release!=null?release.PublishedAt:-1;
            status.CanApply=stage==UpdateStage.Ready&&!string.IsNullOrEmpty(config.ExePath); copy=status.Copy(); listeners=watchers.ToArray();
        }
        foreach(Action<UpdateStatus> watcher in listeners) { try { watcher(copy.Copy()); } catch { } }
        Action<UpdateStatus> handler=OnStatus; if(handler!=null) { try { handler(copy); } catch { } }
    }
    // One operation at a time. A download requested while a check runs is remembered and starts when that check ends
    // at Available (an Install click that lands in the moment between "available" and the end of the check); any
    // other request while an operation runs is ignored.
    bool Enter(bool check) {
        lock(gate) {
            if(busy) { if(checking&&!check)downloadWanted=true; return false; }
            busy=true; checking=check; downloadWanted=false; return true;
        }
    }
    void Leave() { lock(gate) { busy=false; checking=false; downloadWanted=false; } }
    // Ends a check; true when a download was requested meanwhile and the check found a release to download.
    bool LeaveCheck() {
        lock(gate) { bool next=downloadWanted&&release!=null&&status.Stage==UpdateStage.Available; busy=false; checking=false; downloadWanted=false; return next; }
    }
    internal bool Busy { get { lock(gate)return busy; } }
    internal void CheckInBackground() { if(Enter(true))ThreadPool.QueueUserWorkItem(delegate { RunCheck(); }); }
    internal void DownloadInBackground() { if(Enter(false))ThreadPool.QueueUserWorkItem(delegate { try { DownloadCore(); } finally { Leave(); } }); }
    internal void Check() { if(Enter(true))RunCheck(); }
    internal void Download() { if(!Enter(false))return; try { DownloadCore(); } finally { Leave(); } }
    void RunCheck() {
        bool next=false;
        try { CheckCore(); } finally { next=LeaveCheck(); }
        if(next&&Enter(false)) { try { DownloadCore(); } finally { Leave(); } }
    }

    static string MessageFor(NetException e) { return e.Failure==NetFailure.Limit?Messages.Limit:e.Failure==NetFailure.Offline?Messages.Offline:Messages.Failed; }
    string ReadyMessage(string version) {
        string message="Version "+version+" is ready to install";
        return string.IsNullOrEmpty(config.ExePath)?message+" (applies to the packaged "+config.AssetName+" only)":message;
    }
    long Now() { return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds; }
    void Checked() { lock(gate)status.CheckedAt=Now(); }

    void CheckCore() {
        lock(gate) { release=null; expectedSha=null; }
        Set(UpdateStage.Checking,Messages.Checking,0);
        try {
            Release found=Resolve();
            if(!SemVer.IsNewer(found.Version,config.Version)) { Checked(); Set(UpdateStage.UpToDate,config.AppName+" is up to date",0); return; }
            if(found.Size<0&&found.AssetUrl!=null) {
                try { found.Size=net.ContentLength(found.AssetUrl); }
                catch(NetException e) { if(e.Failure==NetFailure.NotFound)throw new UpdateError("The release has no "+config.AssetName); throw; }
            }
            if(found.AssetUrl==null)throw new UpdateError("The release has no "+config.AssetName);
            lock(gate)release=found;
            if(AlreadyDownloaded(found)) { Checked(); Set(UpdateStage.Ready,ReadyMessage(found.Version),1); return; }
            Checked(); Set(UpdateStage.Available,"Version "+found.Version+" is available",0);
            Func<bool> auto=AutoDownload; if(auto!=null&&auto())DownloadCore();
        }
        catch(UpdateError e) { Set(UpdateStage.Error,e.Message,0); }
        catch(NetException e) { Set(UpdateStage.Error,MessageFor(e),0); }
        catch(Exception) { Set(UpdateStage.Error,Messages.Failed,0); }
    }
    Release Resolve() {
        string overrideUrl=string.IsNullOrEmpty(config.ReleaseUrlVariable)?null:Environment.GetEnvironmentVariable(config.ReleaseUrlVariable);
        Release r=string.IsNullOrEmpty(overrideUrl)?(FromApi()??FromRedirect()):FromDocument(overrideUrl);
        int[] v=SemVer.Parse(r.Tag); if(v==null)throw new UpdateError(Messages.BadVersion);
        r.Version=SemVer.Format(v); return r;
    }
    internal string ApiUrl { get { return "https://api.github.com/repos/"+config.Repo+"/releases/latest"; } }
    static readonly Dictionary<string,string> ApiHeaders=new Dictionary<string,string> { { "Accept","application/vnd.github+json" },{ "X-GitHub-Api-Version","2022-11-28" } };
    // First source: the latest-release API document (tag, assets with sizes, release notes, date). Any failure (403 /
    // 429 of the unauthenticated quota, no answer, a document without a tag) returns null and the redirect is used.
    Release FromApi() {
        string text;
        try { text=net.GetText(ApiUrl,ApiHeaders); } catch(Exception) { return null; }
        return Parse(text);
    }
    // Fallback source: HEAD /releases/latest redirects to /releases/tag/<tag>; no API quota is used, no notes, no date.
    Release FromRedirect() {
        string location;
        try { location=net.RedirectLocation("https://github.com/"+config.Repo+"/releases/latest"); }
        catch(NetException e) { if(e.Failure==NetFailure.NotFound)throw new UpdateError(Messages.NoRelease); throw; }
        const string marker="/releases/tag/"; int at=location==null?-1:location.IndexOf(marker,StringComparison.Ordinal);
        if(at<0)throw new UpdateError(Messages.NoRelease);
        string tag=location.Substring(at+marker.Length); int cut=tag.IndexOfAny(new char[]{'?','#','/'}); if(cut>=0)tag=tag.Substring(0,cut);
        tag=Uri.UnescapeDataString(tag); if(tag.Length==0)throw new UpdateError(Messages.NoRelease);
        string baseUrl="https://github.com/"+config.Repo+"/releases/download/"+Uri.EscapeDataString(tag)+"/";
        Release r=new Release { Tag=tag,AssetUrl=baseUrl+config.AssetName,SumsUrl=baseUrl+config.SumsName }; return r;
    }
    // Test source (the ReleaseUrlVariable): a JSON document in the GitHub "latest release" format.
    Release FromDocument(string url) {
        string text;
        try { text=net.GetText(url); }
        catch(NetException e) { if(e.Failure==NetFailure.Offline)throw; throw new UpdateError(Messages.NoRelease); }
        Release r=Parse(text); if(r==null)throw new UpdateError(Messages.NoRelease); return r;
    }
    // The GitHub "latest release" document: tag_name, body (notes), published_at, assets[].name / size /
    // browser_download_url. Null when the text is not such a document.
    Release Parse(string text) {
        try {
            Dictionary<string,object> doc=Json.Parse(text) as Dictionary<string,object>;
            object tag; if(doc==null||!doc.TryGetValue("tag_name",out tag)||!(tag is string))return null;
            Release r=new Release { Tag=(string)tag }; object value;
            if(doc.TryGetValue("body",out value)) { string notes=value as string; if(!string.IsNullOrWhiteSpace(notes))r.Notes=notes; }
            if(doc.TryGetValue("published_at",out value))r.PublishedAt=EpochSeconds(value as string);
            if(doc.TryGetValue("assets",out value)&&value is IEnumerable&&!(value is string)) {
                foreach(object item in (IEnumerable)value) {
                    Dictionary<string,object> a=item as Dictionary<string,object>; if(a==null)continue;
                    object name,link,size; if(!a.TryGetValue("name",out name)||!a.TryGetValue("browser_download_url",out link))continue;
                    if(string.Equals(name as string,config.AssetName,StringComparison.OrdinalIgnoreCase)) { r.AssetUrl=link as string; if(a.TryGetValue("size",out size)&&size!=null)r.Size=Convert.ToInt64(size,CultureInfo.InvariantCulture); }
                    else if(string.Equals(name as string,config.SumsName,StringComparison.OrdinalIgnoreCase))r.SumsUrl=link as string;
                }
            }
            return r;
        } catch(Exception) { return null; }
    }
    // ISO 8601 ("2026-10-21T10:00:00Z") -> seconds since 1970-01-01 UTC; -1 when missing or unreadable.
    internal static long EpochSeconds(string iso) {
        DateTime at; if(string.IsNullOrEmpty(iso)||!DateTime.TryParse(iso,CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out at))return -1;
        return (long)(at-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
    }
    string ExpectedSha(Release r) {
        if(r.SumsUrl==null)throw new UpdateError(Messages.NoChecksum);
        string text;
        try { text=net.GetText(r.SumsUrl); }
        catch(NetException e) { if(e.Failure==NetFailure.NotFound)throw new UpdateError(Messages.NoChecksum); throw; }
        string sha=Checksums.Find(text,config.AssetName); if(sha==null)throw new UpdateError(Messages.NoChecksum); return sha;
    }
    bool AlreadyDownloaded(Release r) {
        string file=FileFor(r.Version); if(!File.Exists(file))return false;
        if(r.Size>=0&&new FileInfo(file).Length!=r.Size)return false;
        string sha=ExpectedSha(r); if(Checksums.Sha256File(file)!=sha)return false;
        lock(gate)expectedSha=sha; return true;
    }
    void DownloadCore() {
        Release r; UpdateStage stage;
        lock(gate) { r=release; stage=status.Stage; }
        if(r==null||(stage!=UpdateStage.Available&&stage!=UpdateStage.Error))return;
        string label="Downloading "+config.AppName+" "+r.Version+"… ";
        Set(UpdateStage.Downloading,label+"0%",0);
        string file=FileFor(r.Version),part=file+".part";
        try {
            string sha=ExpectedSha(r);
            try { Directory.CreateDirectory(config.UpdatesDir); } catch(Exception) { throw new UpdateError(Messages.SaveFailed); }
            int lastPercent=0; string actual;
            try {
                actual=net.Download(r.AssetUrl,part,delegate(long done,long total) {
                    long size=r.Size>=0?r.Size:total; if(size<=0)return; int percent=(int)Math.Min(100,done*100/size);
                    if(percent>lastPercent) { lastPercent=percent; Set(UpdateStage.Downloading,label+percent+"%",percent/100.0); }
                });
            }
            catch(NetException e) { TryDelete(part); if(e.Failure==NetFailure.NotFound)throw new UpdateError("The release has no "+config.AssetName); throw; }
            catch(IOException) { TryDelete(part); throw new UpdateError(Messages.SaveFailed); }
            catch(UnauthorizedAccessException) { TryDelete(part); throw new UpdateError(Messages.SaveFailed); }
            if((r.Size>=0&&new FileInfo(part).Length!=r.Size)||actual!=sha) { TryDelete(part); throw new UpdateError(Messages.Damaged); }
            try { if(File.Exists(file))File.Delete(file); File.Move(part,file); } catch(Exception) { TryDelete(part); throw new UpdateError(Messages.SaveFailed); }
            lock(gate)expectedSha=sha;
            Set(UpdateStage.Ready,ReadyMessage(r.Version),1);
        }
        catch(UpdateError e) { Set(UpdateStage.Error,e.Message,0); }
        catch(NetException e) { Set(UpdateStage.Error,MessageFor(e),0); }
        catch(Exception) { TryDelete(part); Set(UpdateStage.Error,Messages.Failed,0); }
    }
    // Starts the downloaded exe in apply mode. True: the caller must now exit the way its Quit command does.
    internal bool Apply() {
        Release r; string sha; UpdateStage stage;
        lock(gate) { r=release; sha=expectedSha; stage=status.Stage; }
        if(stage!=UpdateStage.Ready||r==null||sha==null) { Set(UpdateStage.Error,Messages.NotReady,0); return false; }
        if(string.IsNullOrEmpty(config.ExePath)) { Set(UpdateStage.Error,Messages.NotReady,0); return false; }
        string file=FileFor(r.Version);
        if(!File.Exists(file)) { Set(UpdateStage.Error,Messages.Missing,0); return false; }
        try { if(Checksums.Sha256File(file)!=sha) { TryDelete(file); Set(UpdateStage.Error,Messages.Damaged,0); return false; } }
        catch(Exception) { Set(UpdateStage.Error,Messages.Missing,0); return false; }
        try { WriteMarker(r.Version); } catch(Exception) { Set(UpdateStage.Error,Messages.SaveFailed,0); return false; }
        string arguments=UpdateApply.Flag+" "+Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)+" "+UpdateApply.Quote(config.ExePath);
        bool started; try { started=Spawn(file,arguments); } catch { started=false; }
        if(!started) { TryDelete(MarkerPath); Set(UpdateStage.Error,Messages.StartFailed,0); return false; }
        return true;
    }
    void WriteMarker(string version) {
        Directory.CreateDirectory(config.UpdatesDir);
        long at=(long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
        string tmp=MarkerPath+".tmp"; File.WriteAllText(tmp,"{\"version\": \""+version+"\", \"at\": "+at+"}",new UTF8Encoding(false));
        if(File.Exists(MarkerPath))File.Delete(MarkerPath); File.Move(tmp,MarkerPath);
    }
    static bool DefaultSpawn(string exe,string arguments) {
        ProcessStartInfo info=new ProcessStartInfo(exe,arguments) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(exe) };
        using(Process p=Process.Start(info))return p!=null;
    }
    // Run by the new instance: toast once after an update, remove <exe>.old and stale downloads (with retries, the
    // apply-mode copy and the old exe may still be closing). Returns the background thread for tests.
    internal Thread Startup() {
        string toast=null;
        try {
            if(File.Exists(MarkerPath)) {
                Match m=Regex.Match(File.ReadAllText(MarkerPath),"\"version\"\\s*:\\s*\"([^\"]+)\"");
                if(m.Success&&m.Groups[1].Value==config.Version)toast="Updated to v"+config.Version;
                TryDelete(MarkerPath);
            }
        } catch { TryDelete(MarkerPath); }
        if(toast!=null) { Action<string> handler=OnToast; if(handler!=null) { try { handler(toast); } catch { } } }
        Thread worker=new Thread(Cleanup) { IsBackground=true,Name="update cleanup" }; worker.Start(); return worker;
    }
    void Cleanup() {
        for(int attempt=0;attempt<20;attempt++) {
            bool pending=false;
            if(!string.IsNullOrEmpty(config.ExePath)) { string old=config.ExePath+".old"; if(File.Exists(old)&&!TryDelete(old))pending=true; }
            try {
                if(Directory.Exists(config.UpdatesDir)) foreach(string path in Directory.GetFiles(config.UpdatesDir,config.Stem+"-*")) {
                    string name=Path.GetFileName(path); if(name.EndsWith(".part"))name=name.Substring(0,name.Length-5);
                    if(!name.EndsWith(config.Extension,StringComparison.OrdinalIgnoreCase))continue;
                    string version=name.Substring(config.Stem.Length+1,name.Length-config.Stem.Length-1-config.Extension.Length);
                    if(SemVer.IsNewer(version,config.Version))continue;
                    if(!TryDelete(path))pending=true;
                }
            } catch { pending=true; }
            if(!pending)return; Thread.Sleep(500);
        }
    }
    internal static bool TryDelete(string path) { try { if(File.Exists(path))File.Delete(path); return true; } catch { return false; } }
}
}
