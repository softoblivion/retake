using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
namespace SelfUpdate {
// Apply mode: "<downloaded exe> --apply-update <pid> <target> [--no-launch]". The host routes it first thing in
// Main, before the single-instance mutex and before any window; it waits for the old app to exit, swaps the
// target, starts it with the host's relaunch arguments. Log: apply.log beside the downloaded exe.
static class UpdateApply {
    internal const string Flag="--apply-update",NoLaunchFlag="--no-launch";
    internal static string Quote(string path) { return "\""+path+"\""; }
    // The running exe: the .NET Framework exe or the apphost of a .NET single-file build alike.
    internal static string SelfPath { get { using(Process p=Process.GetCurrentProcess())return Path.GetFullPath(p.MainModule.FileName); } }
    internal static bool IsApplyMode(string[] args) { return args!=null&&args.Length>0&&args[0]==Flag; }
    // relaunchArguments: the command line of the new instance (for example "--startup" for a tray start); may be empty.
    internal static int Run(string[] args,string relaunchArguments) {
        string self=SelfPath,log=Path.Combine(Path.GetDirectoryName(self),"apply.log");
        Action<string> write=delegate(string line) { try { File.AppendAllText(log,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+"  "+line+Environment.NewLine); } catch { } };
        // Positionals are the arguments after the flag that do not start with "--" (the Python library's apply mode
        // reads them the same way, so an exe of either kind can apply an update downloaded by the other).
        List<string> positional=new List<string>(); for(int i=1;i<args.Length;i++)if(!args[i].StartsWith("--",StringComparison.Ordinal))positional.Add(args[i]);
        int pid; if(positional.Count<2||!int.TryParse(positional[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out pid)) { write("bad arguments"); return 1; }
        string target=Path.GetFullPath(positional[1]); bool launch=Array.IndexOf(args,NoLaunchFlag)<0;
        write("apply "+self+" -> "+target+", waiting for pid "+pid);
        if(!WaitForExit(pid,TimeSpan.FromSeconds(120))) { write("the app did not exit within 120 s"); return 1; }
        if(!Swap(self,target,write))return 1;
        if(launch) {
            try { using(Process p=Process.Start(new ProcessStartInfo(target,relaunchArguments??"") { UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target) })) { } write("started "+target); }
            catch(Exception e) { write("could not start the new version: "+e.Message); return 1; }
        }
        return 0;
    }
    internal static bool WaitForExit(int pid,TimeSpan timeout) {
        Process p; try { p=Process.GetProcessById(pid); } catch(ArgumentException) { return true; }
        using(p) { try { return p.WaitForExit((int)timeout.TotalMilliseconds); } catch(InvalidOperationException) { return true; } catch(Win32Exception) { return false; } }
    }
    // Rename the target (allowed for a running or just-closed image) to .old, copy the source over it, compare sizes.
    // Any failure after the rename puts .old back. Never leaves the target missing when it existed before.
    internal static bool Swap(string source,string target,Action<string> log) {
        string old=target+".old";
        if(File.Exists(old)&&!Updater.TryDelete(old)) {
            string aside=old+"."+DateTime.Now.Ticks; try { File.Move(old,aside); log("stale .old moved to "+aside); } catch(Exception e) { log("stale .old is locked: "+e.Message); return false; }
        }
        if(!File.Exists(source)) { log("update file missing: "+source); return false; }
        bool renamed=false;
        if(File.Exists(target)) {
            for(int attempt=1;attempt<=40&&!renamed;attempt++) {
                try { File.Move(target,old); renamed=true; }
                catch(Exception e) { if(attempt==40) { log("could not rename the target: "+e.Message); return false; } Thread.Sleep(500); }
            }
        }
        try {
            File.Copy(source,target,false);
            if(new FileInfo(source).Length!=new FileInfo(target).Length)throw new IOException("size mismatch after copy");
            log("swapped"+(renamed?", previous version kept as "+old:"")); return true;
        } catch(Exception e) {
            log("copy failed: "+e.Message);
            Updater.TryDelete(target);
            if(renamed) { try { File.Move(old,target); log("restored the previous version"); } catch(Exception r) { log("restore failed: "+r.Message); } }
            return false;
        }
    }
}
}
