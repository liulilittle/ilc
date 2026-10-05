//------------------------------------------------------------------------------
// strproc.cs -- System.String processing-family DEEP reference program (net48
// oracle). Combines the four string-manipulation domains in one program:
// PadLeft/PadRight, Trim/TrimStart/TrimEnd, and Substring -- plus the
// cross-domain compositions (padded-then-sliced, trimmed-then-padded, sliced-
// then-padded) and the error paths each family exposes. Every line is a BOOL
// (net48 is the byte reference; the runtime's string ext3 family reproduces
// the same result).
//
// WIRED SURFACE (MethodMap.String.cs + il2cpp_string_ext3.h):
//   * PadLeft/PadRight (1-arg space pad + 2-arg explicit pad char)
//   * Trim() / TrimStart() / TrimEnd() (whitespace-only trim set)
//   * Substring(start) / Substring(start, length)
//   * new string(char, count)          -- ctor(char, int32) repeat
//   * get_Length on the results        -- downstream member resolution
//
// PAD IDENTITY RULE (csc-probe-verified on net48, stringext3.cpp PadString):
//   totalWidth < Length  -> returns the ORIGINAL instance (ReferenceEquals
//   true, zero allocations); totalWidth == Length -> a NEW string with equal
//   content (ReferenceEquals false); totalWidth < 0 ->
//   ArgumentOutOfRangeException.
//
// TRIM SET: the runtime trim family is whitespace-only (space, \t, \n, \r
// and the full Char.IsWhiteSpace set) -- identical to net48's default trim
// on the ASCII whitespace used below; inner whitespace is preserved.
//
// SUBSTRING BOUNDS (net48): start in [0, Length]; start == Length -> the
// empty string; start + length must not exceed Length; negative start or
// length throws ArgumentOutOfRangeException.
//------------------------------------------------------------------------------
using System;

class Program
{
    static void Main()
    {
        // ================= PadLeft / PadRight: width and pad char =================
        string s = "abc";
        Console.WriteLine(s.PadLeft(6) == "   abc");      // 3 spaces left
        Console.WriteLine(s.PadRight(6) == "abc   ");     // 3 spaces right
        Console.WriteLine(s.PadLeft(6, '*') == "***abc"); // explicit pad char
        Console.WriteLine(s.PadRight(6, '-') == "abc---");
        Console.WriteLine(s.PadLeft(5, '0') == "00abc");
        Console.WriteLine(s.PadRight(7, '.') == "abc....");
        // width == Length -> equal content (fresh string)
        Console.WriteLine(s.PadLeft(3) == "abc");
        Console.WriteLine(s.PadRight(3) == "abc");
        // width < Length -> original content (identity instance)
        Console.WriteLine(s.PadLeft(2) == "abc");
        Console.WriteLine(s.PadRight(1) == "abc");
        Console.WriteLine(s.PadLeft(0) == "abc");
        // width > Length with no pad char -> spaces
        Console.WriteLine(s.PadLeft(4) == " abc");
        Console.WriteLine(s.PadRight(4) == "abc ");
        // longer width across a growth boundary
        Console.WriteLine(s.PadLeft(20) == new string(' ', 17) + "abc");
        Console.WriteLine(s.PadRight(20) == "abc" + new string(' ', 17));

        // ================= Pad on empty / single-char strings =================
        Console.WriteLine("".PadLeft(3) == "   ");        // empty -> all pad
        Console.WriteLine("".PadRight(2, '.') == "..");
        Console.WriteLine("x".PadRight(1, '.') == "x");   // width == 1
        Console.WriteLine("x".PadLeft(2, 'y') == "yx");
        Console.WriteLine("".PadLeft(0) == "");           // empty stays empty
        Console.WriteLine("".PadRight(0) == "");

        // ================= Pad identity (ReferenceEquals) =================
        string orig = "xy";
        Console.WriteLine(ReferenceEquals(orig, orig.PadLeft(1)));  // width < len -> same
        Console.WriteLine(ReferenceEquals(orig, orig.PadRight(2))); // width == len -> NEW
        Console.WriteLine(ReferenceEquals(orig, orig.PadLeft(0)));  // width < len -> same
        Console.WriteLine(ReferenceEquals("ab", "ab".PadRight(3))); // width > len -> NEW

        // ================= Trim / TrimStart / TrimEnd =================
        Console.WriteLine("  hello  ".Trim() == "hello");
        Console.WriteLine("\t\n hello \r\n".Trim() == "hello");     // full ASCII ws set
        Console.WriteLine("hello".Trim() == "hello");               // no-op
        Console.WriteLine("   ".Trim() == "");                      // all-ws -> empty
        Console.WriteLine("".Trim() == "");                         // empty stays empty
        Console.WriteLine("  hello  ".TrimStart() == "hello  ");    // left only
        Console.WriteLine("  hello  ".TrimEnd() == "  hello");      // right only
        Console.WriteLine("  a  b  ".Trim() == "a  b");             // inner ws preserved
        Console.WriteLine(" \t x \n ".Trim() == "x");               // mixed ws edges
        Console.WriteLine("ab cd".TrimStart() == "ab cd");          // no leading ws

        // ================= Substring: start and length forms =================
        string t = "abcdefgh";
        Console.WriteLine(t.Substring(3) == "defgh");
        Console.WriteLine(t.Substring(0) == "abcdefgh");            // full
        Console.WriteLine(t.Substring(8) == "");                    // start == Length
        Console.WriteLine(t.Substring(2, 3) == "cde");
        Console.WriteLine(t.Substring(2, 0) == "");                 // zero length
        Console.WriteLine(t.Substring(0, 8) == "abcdefgh");         // full window
        Console.WriteLine(t.Substring(5, 3) == "fgh");              // tail window
        Console.WriteLine(t.Substring(1, 7) == "bcdefgh");
        Console.WriteLine(t.Substring(2).Length == 6);              // length of result

        // ================= Pad + Substring compositions =================
        Console.WriteLine("abc".PadLeft(6, 'x').Substring(2) == "xabc");  // pad then slice
        Console.WriteLine("abc".PadRight(6, 'x').Substring(0, 3) == "abc");
        Console.WriteLine("abc".PadRight(6, 'x').Substring(3) == "xxx");
        Console.WriteLine("  hello  ".Trim().Substring(1, 4) == "ello");  // trim then slice
        Console.WriteLine("abcdef".Substring(2).PadLeft(6, '0') == "00cdef"); // slice then pad
        Console.WriteLine("abcdef".Substring(1, 3).PadRight(6, '0') == "bcd000");
        Console.WriteLine("abcdef".Substring(4).PadLeft(4, '.') == "..ef"); // pad short fallback
        Console.WriteLine("abcdef".Substring(0, 2).PadRight(4, '-') == "ab--");

        // ================= Trim + Pad compositions =================
        Console.WriteLine("  hi ".Trim().PadLeft(5, '-') == "---hi");
        Console.WriteLine("  hi ".Trim().PadRight(5, '-') == "hi---");
        Console.WriteLine("   x  ".Trim().PadLeft(3, '*') == "**x");
        Console.WriteLine("abc".PadLeft(5, ' ').Trim() == "abc");   // pad then trim back
        Console.WriteLine("abc".PadRight(5, ' ').Trim() == "abc");
        Console.WriteLine("abc".PadLeft(6, 'x').Trim() == "xxxabc"); // non-ws pad kept

        // ================= Substring + Trim compositions =================
        Console.WriteLine("  abcdef  ".Trim().Substring(2, 2) == "cd");
        Console.WriteLine("abcdef".Substring(2).Trim() == "cdef");
        Console.WriteLine("abc   ".TrimEnd().Substring(1) == "bc");
        Console.WriteLine("   abc".TrimStart().Substring(0, 2) == "ab");

        // ================= new string(char, count) =================
        Console.WriteLine(new string('z', 3) == "zzz");
        Console.WriteLine(new string(' ', 2) == "  ");
        Console.WriteLine(new string('a', 0) == "");                // count 0
        Console.WriteLine("abc".PadLeft(6, new string('*', 1)[0]) == "***abc"); // pad char from string

        // ================= error paths (ArgumentOutOfRangeException) =================
        bool ok = false;
        try { "abc".Substring(4); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);
        ok = false;
        try { "abc".Substring(-1); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);
        ok = false;
        try { "abc".Substring(2, 2); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);                                     // window overruns
        ok = false;
        try { "abc".Substring(1, -1); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);
        ok = false;
        try { "abc".PadLeft(-1); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);
        ok = false;
        try { "abc".PadRight(-2, '.'); } catch (ArgumentOutOfRangeException) { ok = true; }
        Console.WriteLine(ok);
        // legal boundaries do NOT throw
        ok = true;
        try { "abc".Substring(3); } catch (Exception) { ok = false; }
        Console.WriteLine(ok);
        ok = true;
        try { "abc".Substring(0, 0); } catch (Exception) { ok = false; }
        Console.WriteLine(ok);
    }
}
