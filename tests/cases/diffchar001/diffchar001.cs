using System;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void B(string id, char c, bool v) { Console.WriteLine("[" + id + "." + ((int)c).ToString("X4") + "] " + (v ? 1 : 0)); }
    static void N(string id, char c) { double d = Char.GetNumericValue(c); Console.WriteLine("[" + id + "." + ((int)c).ToString("X4") + "] " + (d < 0 ? -1 : (int)d)); }
    static void CV(string id, char c) { int r = Char.ToUpperInvariant(c); Console.WriteLine("[" + id + "." + ((int)c).ToString("X4") + "] " + r.ToString("X4")); }
    static void CL(string id, char c) { int r = Char.ToLowerInvariant(c); Console.WriteLine("[" + id + "." + ((int)c).ToString("X4") + "] " + r.ToString("X4")); }
    static readonly char[] Set = new char[] {
        '7', 'a', 'Z', ' ', '\t', '\n', '\u00A0', '\u3000', '!', '\u3002', '+', '$', '_',
        '\u0660', '\u0966', '\uFF10', '\u4E2D', '\u00E9', '\u00DF', '\u0130', '\u0131',
        '\u0001', '\u007F', '\uD83D', '\uDE00', '\uD800', '\uDC00', '\u0000', '\uFFFD'
    };
    static void Digits() {
        for (int i = 0; i < Set.Length; i++) B("char.isdigit", Set[i], Char.IsDigit(Set[i]));
        N("char.numval", '7'); N("char.numval", '\u0660'); N("char.numval", '\u0966');
        N("char.numval", '\uFF10'); N("char.numval", 'a'); N("char.numval", '\u00BD');
        N("char.numval", '\u2160'); N("char.numval", '\u4E2D');
    }
    static void Letters() {
        for (int i = 0; i < Set.Length; i++) B("char.isletter", Set[i], Char.IsLetter(Set[i]));
        for (int i = 0; i < Set.Length; i++) B("char.isletterordigit", Set[i], Char.IsLetterOrDigit(Set[i]));
        B("char.islower", 'a', Char.IsLower('a')); B("char.islower", 'A', Char.IsLower('A'));
        B("char.islower", '\u00E9', Char.IsLower('\u00E9')); B("char.islower", '\u4E2D', Char.IsLower('\u4E2D'));
        B("char.isupper", 'A', Char.IsUpper('A')); B("char.isupper", 'a', Char.IsUpper('a'));
        B("char.isupper", '\u00C9', Char.IsUpper('\u00C9')); B("char.isupper", '\u4E2D', Char.IsUpper('\u4E2D'));
    }
    static void Others() {
        for (int i = 0; i < Set.Length; i++) B("char.iswhite", Set[i], Char.IsWhiteSpace(Set[i]));
        for (int i = 0; i < Set.Length; i++) B("char.iscontrol", Set[i], Char.IsControl(Set[i]));
        B("char.ispunct", '!', Char.IsPunctuation('!')); B("char.ispunct", '\u3002', Char.IsPunctuation('\u3002'));
        B("char.ispunct", 'a', Char.IsPunctuation('a')); B("char.ispunct", '$', Char.IsPunctuation('$'));
        B("char.issymbol", '$', Char.IsSymbol('$')); B("char.issymbol", '+', Char.IsSymbol('+'));
        B("char.issymbol", 'a', Char.IsSymbol('a'));
        B("char.issep", ' ', Char.IsSeparator(' ')); B("char.issep", ',', Char.IsSeparator(','));
        B("char.issurrogate", '\uD83D', Char.IsSurrogate('\uD83D')); B("char.issurrogate", 'a', Char.IsSurrogate('a'));
        B("char.ishigh", '\uD83D', Char.IsHighSurrogate('\uD83D')); B("char.ishigh", '\uDE00', Char.IsHighSurrogate('\uDE00'));
        B("char.islow", '\uDE00', Char.IsLowSurrogate('\uDE00')); B("char.islow", '\uD83D', Char.IsLowSurrogate('\uD83D'));
    }
    static void CaseConv() {
        char[] cs = new char[] { 'a', 'z', 'A', 'Z', '\u00E9', '\u00C9', '\u00DF', '\u0130', '\u0131', '\u4E2D', '\u0660' };
        for (int i = 0; i < cs.Length; i++) CV("char.toupper", cs[i]);
        for (int i = 0; i < cs.Length; i++) CL("char.tolower", cs[i]);
    }
    public static void Main() {
        Console.WriteLine("-- shard char001 start --");
        Digits();
        Letters();
        Others();
        CaseConv();
        Console.WriteLine("-- shard char001 end --");
    }
}}
