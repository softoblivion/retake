using System;
using System.Globalization;
namespace SelfUpdate {
// The copy and the number formats of the update notice (en-US), shared by every view the presenter feeds. Sizes are
// 1024 based: "512 B", "48 KB", "12.1 MB", "1.25 GB"; dates "Oct 21, 2026".
static class UpdateText {
    internal const string Install="Install",Later="Later",RestartToUpdate="Restart to update",Restart="Restart",RestartAnyway="Restart anyway",
        View="View",TryAgain="Try again",CheckForUpdates="Check for updates",Checking="Checking…",Download="Download",Downloading="Downloading…",
        NoNotes="No release notes for this version.",AutoLabel="Download updates automatically",AutoHint="Checked every 5 minutes; installing still needs a restart",
        AutoLine="New versions download in the background",NotChecked="Not checked yet",PillTip="Version and updates",Installed="Installed",Latest="Latest",
        DownloadedTag="downloaded",LinkFailed="The link could not be opened.",ReleaseNotes="Release notes",
        RestartWarning="Work in progress stops when the app restarts",PackagedOnly="Installs from the packaged app only";
    static readonly CultureInfo English=CultureInfo.GetCultureInfo("en-US");
    static readonly DateTime Epoch=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc);

    // One line of status for the updates panel and the settings row.
    internal static string StatusLine(UpdateStatus s) {
        switch(s.Stage) {
            case UpdateStage.Idle: return "Updates come from GitHub Releases";
            case UpdateStage.Checking: return "Checking for updates…";
            case UpdateStage.UpToDate: return "You have the latest version";
            case UpdateStage.Available: return "Version "+s.Latest+" is available";
            case UpdateStage.Downloading: return "Downloading version "+s.Latest+"…";
            case UpdateStage.Ready: return "Version "+s.Latest+" is ready to install";
            default: return string.IsNullOrEmpty(s.Message)?(s.Latest!=null?"The download did not finish":"Could not check for updates"):s.Message;
        }
    }
    internal static string Bytes(long n) {
        double a=Math.Abs((double)n);
        if(a<1024)return n.ToString("0",English)+" B";
        if(a<1048576)return (n/1024.0).ToString("#,0",English)+" KB";
        if(a<1073741824)return (n/1048576.0).ToString("#,0.0",English)+" MB";
        return (n/1073741824.0).ToString("#,0.00",English)+" GB";
    }
    // Whole percent of a 0..1 fraction, rounded down, so 100% appears only at the end.
    internal static int Percent(double fraction) { return (int)Math.Floor(Math.Max(0,Math.Min(1,fraction))*100+1e-9); }
    // "5.1 MB of 12.1 MB"; empty when the size is unknown.
    internal static string ProgressDetail(double fraction,long size) {
        if(size<=0)return ""; return Bytes((long)(Math.Max(0,Math.Min(1,fraction))*size))+" of "+Bytes(size);
    }
    // "42% · 5.1 MB of 12.1 MB", or "42%" when the size is unknown.
    internal static string Progress(double fraction,long size) {
        string detail=ProgressDetail(fraction,size); return Percent(fraction)+"%"+(detail.Length>0?" · "+detail:"");
    }
    static DateTime Local(long epoch,TimeZoneInfo zone) { return TimeZoneInfo.ConvertTimeFromUtc(Epoch.AddSeconds(epoch),zone??TimeZoneInfo.Local); }
    // "Oct 21, 2026"; empty for an unknown date (-1).
    internal static string ShortDate(long epoch,TimeZoneInfo zone) { return epoch<0?"":Local(epoch,zone).ToString("MMM d, yyyy",English); }
    // "Oct 21, 4:05 PM"; empty for an unknown time.
    internal static string ShortDateTime(long epoch,TimeZoneInfo zone) { return epoch<0?"":Local(epoch,zone).ToString("MMM d, h:mm tt",English); }
    // "just now", "5 min ago", "3 h ago", "yesterday", "Oct 3"; empty for an unknown time.
    internal static string TimeAgo(long epoch,long now,TimeZoneInfo zone) {
        if(epoch<0)return ""; long d=now-epoch;
        if(d<45)return "just now";
        if(d<3600)return Math.Max(1,(long)Math.Floor(d/60.0+0.5))+" min ago";
        if(d<86400)return (long)Math.Floor(d/3600.0+0.5)+" h ago";
        if(d<172800)return "yesterday";
        return Local(epoch,zone).ToString("MMM d",English);
    }
    internal static long EpochNow() { return (long)(DateTime.UtcNow-Epoch).TotalSeconds; }
}
}
