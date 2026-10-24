using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace SelfUpdate {
static class SemVer {
    // Numeric X.Y.Z prefix of a tag ("v1.2.3-beta.1" -> 1,2,3); missing parts are zero; null when no digits lead.
    internal static int[] Parse(string text) {
        if(text==null)return null; text=text.Trim(); if(text.StartsWith("v")||text.StartsWith("V"))text=text.Substring(1);
        Match m=Regex.Match(text,@"^(\d+)(?:\.(\d+))?(?:\.(\d+))?"); if(!m.Success)return null;
        int[] parts=new int[3];
        for(int i=0;i<3;i++) { if(!m.Groups[i+1].Success)continue; int value; if(!int.TryParse(m.Groups[i+1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out value))return null; parts[i]=value; }
        return parts;
    }
    internal static int Compare(int[] a,int[] b) { for(int i=0;i<3;i++)if(a[i]!=b[i])return a[i]<b[i]?-1:1; return 0; }
    internal static bool IsNewer(string candidate,string current) { int[] a=Parse(candidate),b=Parse(current); return a!=null&&b!=null&&Compare(a,b)>0; }
    internal static string Format(int[] v) { return v[0]+"."+v[1]+"."+v[2]; }
}
static class Checksums {
    // SHA256SUMS line: "<64 hex>  <name>" (a "*" before the name marks binary mode in sha256sum output).
    internal static string Find(string text,string asset) {
        if(text==null)return null;
        foreach(string raw in text.Split('\n')) {
            string line=raw.Trim(); if(line.Length==0||line.StartsWith("#"))continue;
            Match m=Regex.Match(line,@"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if(m.Success&&string.Equals(m.Groups[2].Value.Trim(),asset,StringComparison.OrdinalIgnoreCase))return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }
    internal static string Sha256File(string path) { using(SHA256 sha=SHA256.Create())using(FileStream f=File.OpenRead(path))return Hex(sha.ComputeHash(f)); }
    internal static string Hex(byte[] hash) { StringBuilder b=new StringBuilder(hash.Length*2); foreach(byte x in hash)b.Append(x.ToString("x2")); return b.ToString(); }
}
}
