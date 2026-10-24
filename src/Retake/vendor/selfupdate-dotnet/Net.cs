using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace SelfUpdate {
enum NetFailure { Offline, Limit, NotFound, Other }
class NetException : Exception { internal NetFailure Failure; internal NetException(NetFailure failure,string message) : base(message) { Failure=failure; } }
// The network seam of the updater; tests replace it with a fake.
interface IUpdateNet {
    string RedirectLocation(string url);   // HEAD without following redirects; null when the answer is not a redirect
    long ContentLength(string url);        // HEAD following redirects; -1 when unknown
    string GetText(string url);
    string GetText(string url,IDictionary<string,string> headers); // GET with extra request headers ("Accept" included)
    string Download(string url,string destination,Action<long,long> progress); // streams to destination, returns SHA-256 hex
}
// HttpWebRequest exists on .NET Framework 4 and .NET 8 alike. An exe built with the bare csc.exe of .NET Framework 4
// carries no target-framework attribute and runs with the 4.0 defaults (TLS 1.0), so TLS 1.2 / 1.3 are enabled first.
class WebNet : IUpdateNet {
    readonly string userAgent;
    static WebNet() { try { ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072|(SecurityProtocolType)12288; } catch(NotSupportedException) { ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072; } }
    internal WebNet(string userAgent) { this.userAgent=userAgent; }
    HttpWebRequest Create(string url,string method,bool follow) {
        HttpWebRequest r=(HttpWebRequest)WebRequest.Create(url); r.Method=method; r.AllowAutoRedirect=follow; r.UserAgent=userAgent; r.Timeout=20000; r.ReadWriteTimeout=30000; return r;
    }
    static NetException Map(WebException e) {
        HttpWebResponse response=e.Response as HttpWebResponse;
        if(response!=null) { int code=(int)response.StatusCode; response.Close(); return new NetException(code==404?NetFailure.NotFound:code==403||code==429?NetFailure.Limit:NetFailure.Other,"HTTP "+code); }
        return new NetException(e.Status==WebExceptionStatus.ProtocolError?NetFailure.Other:NetFailure.Offline,e.Status.ToString());
    }
    static T Retry<T>(Func<T> action) {
        for(int attempt=1;;attempt++) {
            try { return action(); }
            catch(WebException e) { NetException n=Map(e); if(attempt>=2||n.Failure==NetFailure.NotFound||n.Failure==NetFailure.Limit)throw n; }
            catch(IOException) { if(attempt>=2)throw new NetException(NetFailure.Offline,"connection lost"); }
            Thread.Sleep(1500);
        }
    }
    public string RedirectLocation(string url) {
        return Retry(delegate {
            HttpWebResponse r;
            // An unfollowed 3xx is normally returned as the response; one reported as a WebException that carries it counts too.
            try { r=(HttpWebResponse)Create(url,"HEAD",false).GetResponse(); }
            catch(WebException e) { r=e.Response as HttpWebResponse; if(r==null||(int)r.StatusCode<300||(int)r.StatusCode>=400)throw; }
            using(r) {
                int code=(int)r.StatusCode; string location=r.Headers["Location"];
                if(code<300||code>=400||string.IsNullOrEmpty(location))return (string)null;
                return new Uri(new Uri(url),location).AbsoluteUri;
            }
        });
    }
    public long ContentLength(string url) { return Retry(delegate { using(HttpWebResponse r=(HttpWebResponse)Create(url,"HEAD",true).GetResponse())return r.ContentLength; }); }
    public string GetText(string url) { return GetText(url,null); }
    public string GetText(string url,IDictionary<string,string> headers) {
        return Retry(delegate {
            HttpWebRequest q=Create(url,"GET",true);
            // Accept is a restricted header of HttpWebRequest: it is set through the property, the others through Headers.
            if(headers!=null)foreach(KeyValuePair<string,string> h in headers) { if(string.Equals(h.Key,"Accept",StringComparison.OrdinalIgnoreCase))q.Accept=h.Value; else q.Headers[h.Key]=h.Value; }
            using(HttpWebResponse r=(HttpWebResponse)q.GetResponse())using(StreamReader s=new StreamReader(r.GetResponseStream(),Encoding.UTF8))return s.ReadToEnd();
        });
    }
    public string Download(string url,string destination,Action<long,long> progress) {
        try {
            using(HttpWebResponse r=(HttpWebResponse)Create(url,"GET",true).GetResponse())
            using(Stream input=r.GetResponseStream())
            using(FileStream output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None))
            using(SHA256 sha=SHA256.Create()) {
                long total=r.ContentLength,done=0; byte[] buffer=new byte[65536]; int read;
                while((read=ReadSome(input,buffer))>0) { output.Write(buffer,0,read); sha.TransformBlock(buffer,0,read,null,0); done+=read; if(progress!=null)progress(done,total); }
                sha.TransformFinalBlock(buffer,0,0); return Checksums.Hex(sha.Hash);
            }
        } catch(WebException e) { throw Map(e); }
    }
    // Network read errors become NetException; disk write errors stay IOException for the caller.
    static int ReadSome(Stream input,byte[] buffer) { try { return input.Read(buffer,0,buffer.Length); } catch(IOException) { throw new NetException(NetFailure.Offline,"connection lost"); } catch(WebException e) { throw Map(e); } }
}
}
