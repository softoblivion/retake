using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
namespace SelfUpdate {
// Release notes from Markdown into plain data that any UI draws in its own look (no UI types here). Blocks:
// headings (# .. ######), paragraphs, "-" / "*" / "+" bullets and "1." / "1)" items with their indent and indented
// continuation lines, "> " quotes, fenced code, horizontal rules (---, ***, ___). Inline: **bold** / __bold__,
// *italic*, `code`, [text](https://...) links, bare https:// links, backslash escapes. A link target that is not
// https:// stays its plain text; anything else (HTML included) is plain text.
enum NoteBlockKind { Heading, Paragraph, Bullet, Numbered, Quote, Code, Rule }
class NoteSpan {
    // Url: an https:// target to open in the browser, null for text.
    internal string Text,Url; internal bool Bold,Italic,Code;
}
class NoteBlock {
    // Level: 1..6 for a heading. Indent: nesting step of a list item (two leading spaces per step, up to 4).
    // Marker: "•" for a bullet, "3." for a numbered item. Text: the raw text of a code block (no spans).
    internal NoteBlockKind Kind; internal int Level,Indent; internal string Marker,Text; internal List<NoteSpan> Spans=new List<NoteSpan>();
    internal string Plain { get { if(Kind==NoteBlockKind.Code)return Text; StringBuilder b=new StringBuilder(); foreach(NoteSpan s in Spans)b.Append(s.Text); return b.ToString(); } }
}
static class ReleaseNotes {
    static readonly Regex Heading=new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)(?:\s+#+)?\s*$"),Rule=new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$"),
        Bullet=new Regex(@"^(\s*)[-*+]\s+(.*)$"),Numbered=new Regex(@"^(\s*)(\d{1,3})[.)]\s+(.*)$"),Quote=new Regex(@"^\s{0,3}>\s?(.*)$"),
        Continuation=new Regex(@"^\s{2,}\S"),
        Token=new Regex(@"\*\*(.+?)\*\*|__(.+?)__|`([^`]+)`|\[([^\]]+)\]\(\s*([^)\s]+)[^)]*\)|(https?://[^\s<>()]*[^\s<>().,;:!?'""])|(?<![\w*])\*(?![\s*])([^*]+?)\*(?![\w*])|\\([\\`*_{}\[\]()#+\-.!>])");

    // Blocks of the notes; an empty list for null or blank text (the UI then shows its "no release notes" line).
    internal static List<NoteBlock> Parse(string markdown) {
        List<NoteBlock> blocks=new List<NoteBlock>(); List<string> raw=new List<string>();
        if(string.IsNullOrWhiteSpace(markdown))return blocks;
        StringBuilder paragraph=new StringBuilder(); StringBuilder code=null; bool inList=false;
        Action flush=delegate { if(paragraph.Length==0)return; Add(blocks,raw,NoteBlockKind.Paragraph,0,0,null,paragraph.ToString()); paragraph.Length=0; };
        foreach(string source in markdown.Replace("\r\n","\n").Replace('\r','\n').Split('\n')) {
            string line=source.Replace("\t","    ").TrimEnd(); Match m;
            if(code!=null) {
                if(line.TrimStart().StartsWith("```",StringComparison.Ordinal)) { Add(blocks,raw,NoteBlockKind.Code,0,0,null,null).Text=code.ToString().TrimEnd('\n'); code=null; }
                else code.Append(source.Replace("\t","    ").TrimEnd()).Append('\n');
                continue;
            }
            if(line.TrimStart().StartsWith("```",StringComparison.Ordinal)) { flush(); inList=false; code=new StringBuilder(); continue; }
            if(line.Trim().Length==0) { flush(); inList=false; continue; }
            if((m=Heading.Match(line)).Success) { flush(); inList=false; Add(blocks,raw,NoteBlockKind.Heading,m.Groups[1].Length,0,null,m.Groups[2].Value); continue; }
            if(Rule.IsMatch(line)) { flush(); inList=false; Add(blocks,raw,NoteBlockKind.Rule,0,0,null,""); continue; }
            if((m=Bullet.Match(line)).Success) { flush(); inList=true; Add(blocks,raw,NoteBlockKind.Bullet,0,IndentOf(m.Groups[1].Value),"•",m.Groups[2].Value.Trim()); continue; }
            if((m=Numbered.Match(line)).Success) { flush(); inList=true; Add(blocks,raw,NoteBlockKind.Numbered,0,IndentOf(m.Groups[1].Value),m.Groups[2].Value+".",m.Groups[3].Value.Trim()); continue; }
            if(inList&&Continuation.IsMatch(line)) { raw[raw.Count-1]=raw[raw.Count-1]+" "+line.Trim(); continue; }
            inList=false;
            if((m=Quote.Match(line)).Success) { flush(); Add(blocks,raw,NoteBlockKind.Quote,0,0,null,m.Groups[1].Value.Trim()); continue; }
            if(paragraph.Length>0)paragraph.Append(' '); paragraph.Append(line.Trim());
        }
        if(code!=null)Add(blocks,raw,NoteBlockKind.Code,0,0,null,null).Text=code.ToString().TrimEnd('\n');
        flush();
        for(int i=0;i<blocks.Count;i++)if(blocks[i].Kind!=NoteBlockKind.Code&&blocks[i].Kind!=NoteBlockKind.Rule)blocks[i].Spans=Inline(raw[i]);
        return blocks;
    }
    static NoteBlock Add(List<NoteBlock> blocks,List<string> raw,NoteBlockKind kind,int level,int indent,string marker,string text) {
        NoteBlock b=new NoteBlock { Kind=kind,Level=level,Indent=indent,Marker=marker }; blocks.Add(b); raw.Add(text??""); return b;
    }
    static int IndentOf(string spaces) { return Math.Min(spaces.Length,8)/2; }

    // Inline spans of one line of text; adjacent spans never share all their styles and target.
    internal static List<NoteSpan> Inline(string text) {
        List<NoteSpan> spans=new List<NoteSpan>(); Inline(spans,text??"",false,false); return spans;
    }
    static void Inline(List<NoteSpan> into,string text,bool bold,bool italic) {
        int at=0;
        foreach(Match m in Token.Matches(text)) {
            if(m.Index>at)Append(into,text.Substring(at,m.Index-at),bold,italic,false,null);
            GroupCollection g=m.Groups;
            if(g[1].Success||g[2].Success)Inline(into,g[1].Success?g[1].Value:g[2].Value,true,italic);
            else if(g[3].Success)Append(into,g[3].Value,bold,italic,true,null);
            else if(g[4].Success)Append(into,g[4].Value,bold,italic,false,Https(g[5].Value));
            else if(g[6].Success)Append(into,g[6].Value,bold,italic,false,Https(g[6].Value));
            else if(g[7].Success)Inline(into,g[7].Value,bold,true);
            else Append(into,g[8].Value,bold,italic,false,null);
            at=m.Index+m.Length;
        }
        if(at<text.Length)Append(into,text.Substring(at),bold,italic,false,null);
    }
    static void Append(List<NoteSpan> into,string text,bool bold,bool italic,bool code,string url) {
        if(text.Length==0)return;
        NoteSpan last=into.Count>0?into[into.Count-1]:null;
        if(last!=null&&last.Bold==bold&&last.Italic==italic&&!last.Code&&!code&&last.Url==null&&url==null) { last.Text+=text; return; }
        into.Add(new NoteSpan { Text=text,Bold=bold,Italic=italic,Code=code,Url=url });
    }
    // Only https:// targets become links; http:, file:, javascript: and relative targets stay plain text.
    internal static string Https(string url) {
        Uri uri; if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps)return null;
        return uri.AbsoluteUri;
    }
}
}
