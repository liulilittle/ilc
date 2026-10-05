//------------------------------------------------------------------------------
// strpath.cs -- end-to-end reference case for STRING PATH-style operations
// (net48 oracle). Covers the string-manipulation building blocks used for
// path/filename/ext directory extraction and their boundary behaviour:
//
// Coverage (active, all wired -- MethodMap.String.cs + StringExt3):
//   * IndexOf multi-overloads -- char/string x {(), (start), (start, count)}
//   * LastIndexOf multi-overloads -- char/string x {(), (start), (start,count)}
//   * Substring boundary cases (start 0/Length, computed starts, chains,
//     ArgumentOutOfRangeException paths)
//   * string.Join(string, string[]) (1-char and multi-char separators)
//   * Replace(char, char) (+ no-occurrence / empty-string cases)
//   * Trim / TrimStart / TrimEnd whitespace family (net48: TrimStart/TrimEnd
//     lower to Array.Empty<char> under the Release build -> wired icall)
//   * StartsWith / EndsWith (string, StringComparison) incl. OrdinalIgnoreCase
//     and invalid-mode ArgumentException
//   * PATH COMPOSITION: extract directory / basename / extension from
//     windows-style and posix-style path strings via LastIndexOf + Substring
//     chains, with the parent / grandparent directory walk
//
// Every line prints "label=bool" or the extracted piece. net48 is the
// reference baseline.
//
// ACCEPTANCE SURFACE (NOT wired -- do not uncomment until fixed):
//   * string.Split(...)           -- no Split case in MethodMap.String.cs
//     (strcomp2 recorded gap); the resolver reaches the generated-name
//     fallback.
//   * Replace(string, string)     -- the Replace case hard-codes char16_t
//     casts (MethodMap.String.cs:39); only the (char, char) form is wired.
//   * Trim(char[] NON-empty)      -- the trim-set overloads emit newarr char
//     (ArrayElementType has no System.Char arm, CodeGenerator.cs:3118) and
//     the runtime is whitespace-only anyway (documented deviation). Only the
//     no-arg / Array.Empty<char> shapes are asserted here.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StrPath {
    public static void Main() {
        // --- PATH COMPOSITION: windows-style ---
        string path = "C:\\work\\project\\src\\Program.cs";
        int slash = path.LastIndexOf('\\');
        Console.WriteLine("ws_slash=" + (slash == 20));
        string dir = path.Substring(0, slash);
        Console.WriteLine("ws_dir=" + dir);                              // C:\work\project\src
        string file = path.Substring(slash + 1);
        Console.WriteLine("ws_file=" + file);                            // Program.cs
        int dot = file.LastIndexOf('.');
        Console.WriteLine("ws_dot=" + (dot == 6));
        Console.WriteLine("ws_ext=" + file.Substring(dot));              // .cs
        Console.WriteLine("ws_name=" + file.Substring(0, dot));          // Program
        // --- PATH COMPOSITION: posix-style ---
        string upath = "/home/user/docs/readme.txt";
        int uslash = upath.LastIndexOf('/');
        Console.WriteLine("ps_slash=" + (uslash == 14));
        Console.WriteLine("ps_base=" + upath.Substring(uslash + 1));     // readme.txt
        // --- parent / grandparent walk ---
        string full = "dir1/dir2/dir3/file.txt";
        string fileName = full.Substring(full.LastIndexOf('/') + 1);
        Console.WriteLine("pw_file=" + fileName);                        // file.txt
        string parent = full.Substring(0, full.LastIndexOf('/'));
        Console.WriteLine("pw_parent=" + parent);                        // dir1/dir2/dir3
        string grandparent = parent.Substring(0, parent.LastIndexOf('/'));
        Console.WriteLine("pw_gp=" + grandparent);                       // dir1/dir2
        // --- IndexOf multi-overloads ---
        string s = "a;b;c;a;b";
        Console.WriteLine("io_1=" + (s.IndexOf(';') == 1));
        Console.WriteLine("io_2=" + (s.IndexOf(';', 2) == 3));
        Console.WriteLine("io_3=" + (s.IndexOf(';', 2, 2) == 3));
        Console.WriteLine("io_s=" + (s.IndexOf(";a") == 3));
        Console.WriteLine("io_none=" + (s.IndexOf("zz") == -1));
        Console.WriteLine("io_char_none=" + (s.IndexOf('x') == -1));
        Console.WriteLine("io_emptystr=" + ("abc".IndexOf("") == 0));
        bool threw = false;
        try { s.IndexOf(';', 3, 8); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("io_oob=" + threw);
        // --- LastIndexOf multi-overloads ---
        Console.WriteLine("lio_1=" + (s.LastIndexOf(';') == 7));
        Console.WriteLine("lio_2=" + (s.LastIndexOf(';', 6) == 3));
        Console.WriteLine("lio_s=" + (s.LastIndexOf(";b") == 5));
        Console.WriteLine("lio_s0=" + (s.LastIndexOf(";b", 5) == 5));
        Console.WriteLine("lio_none=" + (s.LastIndexOf('x') == -1));
        Console.WriteLine("lio_empty=" + ("abc".LastIndexOf("") == 2));  // net48: Length - 1
        threw = false;
        try { s.LastIndexOf(';', 8); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("lio_oob=" + threw);
        // --- Substring boundaries ---
        Console.WriteLine("ss_0=" + ("hello world".Substring(0) == "hello world"));
        Console.WriteLine("ss_6=" + ("hello world".Substring(6) == "world"));
        Console.WriteLine("ss_5=" + ("hello world".Substring(5) == " world"));
        Console.WriteLine("ss_len=" + ("abc".Substring(3) == ""));
        Console.WriteLine("ss_range=" + ("hello world".Substring(0, 5) == "hello"));
        Console.WriteLine("ss_chain=" + ("hello world".Substring(6).Substring(0, 3) == "wor"));
        threw = false;
        try { "abc".Substring(4); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("ss_oob=" + threw);
        threw = false;
        try { "abc".Substring(2, 2); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("ss_range_oob=" + threw);
        threw = false;
        try { "abc".Substring(-1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("ss_neg=" + threw);
        // --- Join ---
        string[] parts = new string[] { "a", "b", "c" };
        Console.WriteLine("join_c=" + (string.Join(",", parts) == "a,b,c"));
        Console.WriteLine("join_dash=" + (string.Join(" - ", parts) == "a - b - c"));
        Console.WriteLine("join_empty=" + (string.Join("", parts) == "abc"));
        Console.WriteLine("join_path=" + (string.Join("/", parts) == "a/b/c"));
        string[] one = new string[] { "solo" };
        Console.WriteLine("join_one=" + (string.Join(",", one) == "solo"));
        // --- Replace(char, char) ---
        Console.WriteLine("rep_dash=" + ("a-b-c".Replace('-', '/') == "a/b/c"));
        Console.WriteLine("rep_char=" + ("hello".Replace('l', 'L') == "heLLo"));
        Console.WriteLine("rep_none=" + ("abc".Replace('x', 'y') == "abc"));
        Console.WriteLine("rep_empty=" + ("".Replace('a', 'b') == ""));
        Console.WriteLine("rep_chain=" + ("a-b-c".Replace('-', '_').Replace('_', '/') == "a/b/c"));
        // --- Trim family (no-arg whitespace) ---
        Console.WriteLine("trim=" + ("  x  ".Trim() == "x"));
        Console.WriteLine("trim_ws=" + ("\t\n x \r".Trim() == "x"));
        Console.WriteLine("trim_all=" + ("   ".Trim() == ""));
        Console.WriteLine("trim_start=" + ("  x  ".TrimStart() == "x  "));
        Console.WriteLine("trim_end=" + ("  x  ".TrimEnd() == "  x"));
        Console.WriteLine("trim_chain=" + ("  x  ".Trim().ToUpper() == "X"));
        // --- StartsWith / EndsWith ---
        Console.WriteLine("sw=" + (path.StartsWith("C:\\work")));
        Console.WriteLine("ew=" + (path.EndsWith(".cs")));
        Console.WriteLine("ew_case=" + (path.EndsWith(".CS") == false));
        Console.WriteLine("ew_ignore=" + (path.EndsWith(".CS", StringComparison.OrdinalIgnoreCase)));
        Console.WriteLine("sw_ignore=" + (path.StartsWith("c:\\WORK", StringComparison.OrdinalIgnoreCase)));
        Console.WriteLine("sw_cur=" + (path.StartsWith("C:\\work", StringComparison.CurrentCulture)));
        threw = false;
        try { path.StartsWith("a", (StringComparison)99); } catch (ArgumentException) { threw = true; }
        Console.WriteLine("sw_badmode=" + threw);
        threw = false;
        try { path.EndsWith(null, StringComparison.Ordinal); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("ew_null=" + threw);
    }
}}
