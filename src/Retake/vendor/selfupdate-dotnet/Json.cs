using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace SelfUpdate {
// Strict JSON reader for the release document, identical on .NET Framework 4 and .NET 8 (no serializer package).
// Objects -> Dictionary<string,object>, arrays -> List<object>, strings, numbers (long when integral and in range,
// otherwise double), true / false, null. Anything else, trailing text included, throws FormatException.
static class Json {
    const int MaxDepth=64;
    static readonly Regex NumberPattern=new Regex(@"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?");
    internal static object Parse(string text) {
        if(text==null)throw new FormatException("no text");
        int at=0; object value=Value(text,ref at,0); Skip(text,ref at);
        if(at!=text.Length)throw Fail(at); return value;
    }
    static FormatException Fail(int at) { return new FormatException("invalid JSON at "+at.ToString(CultureInfo.InvariantCulture)); }
    static char Peek(string s,int i) { return i<s.Length?s[i]:'\0'; }
    static void Skip(string s,ref int i) { while(i<s.Length&&(s[i]==' '||s[i]=='\t'||s[i]=='\r'||s[i]=='\n'))i++; }
    static object Value(string s,ref int i,int depth) {
        if(depth>MaxDepth)throw Fail(i);
        Skip(s,ref i); char c=Peek(s,i);
        if(c=='{')return Object(s,ref i,depth);
        if(c=='[')return Array(s,ref i,depth);
        if(c=='"')return String(s,ref i);
        if(Word(s,ref i,"true"))return true; if(Word(s,ref i,"false"))return false; if(Word(s,ref i,"null"))return null;
        return Number(s,ref i);
    }
    static Dictionary<string,object> Object(string s,ref int i,int depth) {
        Dictionary<string,object> map=new Dictionary<string,object>(); i++; Skip(s,ref i);
        if(Peek(s,i)=='}') { i++; return map; }
        for(;;) {
            Skip(s,ref i); if(Peek(s,i)!='"')throw Fail(i);
            string key=String(s,ref i); Skip(s,ref i); if(Peek(s,i)!=':')throw Fail(i); i++;
            map[key]=Value(s,ref i,depth+1); Skip(s,ref i);
            char c=Peek(s,i); i++; if(c==',')continue; if(c=='}')return map; throw Fail(i-1);
        }
    }
    static List<object> Array(string s,ref int i,int depth) {
        List<object> list=new List<object>(); i++; Skip(s,ref i);
        if(Peek(s,i)==']') { i++; return list; }
        for(;;) {
            list.Add(Value(s,ref i,depth+1)); Skip(s,ref i);
            char c=Peek(s,i); i++; if(c==',')continue; if(c==']')return list; throw Fail(i-1);
        }
    }
    static string String(string s,ref int i) {
        StringBuilder b=new StringBuilder(); i++;
        for(;;) {
            if(i>=s.Length)throw Fail(i);
            char c=s[i++];
            if(c=='"')return b.ToString();
            if(c<0x20)throw Fail(i-1);
            if(c!='\\') { b.Append(c); continue; }
            char e=Peek(s,i); i++;
            switch(e) {
                case '"': b.Append('"'); break; case '\\': b.Append('\\'); break; case '/': b.Append('/'); break;
                case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break;
                case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                case 'u': {
                    int code; if(i+4>s.Length||!int.TryParse(s.Substring(i,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out code))throw Fail(i);
                    b.Append((char)code); i+=4; break;
                }
                default: throw Fail(i-1);
            }
        }
    }
    static bool Word(string s,ref int i,string word) {
        if(string.CompareOrdinal(s,i,word,0,word.Length)!=0)return false; i+=word.Length; return true;
    }
    static object Number(string s,ref int i) {
        Match m=NumberPattern.Match(s,i); if(!m.Success||m.Length==0)throw Fail(i);
        string text=m.Value; i+=m.Length; long whole;
        if(text.IndexOfAny(new char[]{'.','e','E'})<0&&long.TryParse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out whole))return whole;
        return double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
    }
}
}
