//------------------------------------------------------------------------------
// stringops3.cs -- System.String DEEP e2e reference program #3 (net48 oracle,
// byte-for-byte). Created in e2e batch C71 (2026-08-12), AFTER the string
// static surface (stringops: CompareOrdinal/Concat/Join/IsNullOr*), the split
// family (strcomp2/RT-SPL2), the pad + trim family (strproc/strdeep/strpath:
// basic Trim/TrimStart/TrimEnd + PadLeft/Right + Substring compositions), and
// stringops2 (join/split/compare). THIS case is dedicated to the TRIM family
// COMBINATIONS and REMOVAL axis -- the two dimensions the earlier cases only
// touched singly:
//
//   A. TRIMSTART / TRIMEND COMBINATIONS -- the no-arg forms (net48 has only
//      the params char[] overload; Roslyn lowers a no-arg call to
//      TrimStart(Array.Empty<char>()), the wired "drop" shape) exercised as
//      pure LEADING removal (TrimStart), pure TRAILING removal (TrimEnd),
//      and their sequences: TrimStart then TrimEnd, TrimEnd then TrimStart,
//      TrimStart twice, TrimEnd twice, nested Trim(TrimStart(x)), etc. The
//      whitespace set is the FULL .NET Char.IsWhiteSpace set (space / \t / \n
//      / \r / \v / \f / NEL / NBSP / the Unicode Zs spaces -- stringext.cpp
//      IsWhitespace, net48-identical).
//   B. REMOVAL SEMANTICS -- all-whitespace input -> ""; leading-only /
//      trailing-only / both sides removed; inner whitespace PRESERVED;
//      nothing-to-trim identity (ReferenceEquals the same instance); the
//      trimmed result is a NEW instance when anything was removed.
//   C. COMBINED CHAINS -- TrimStart/TrimEnd feeding the other wired string
//      instance ops (Substring, Replace, PadLeft/Right, ToUpper/ToLower,
//      StartsWith/EndsWith, Contains, IndexOf, get_Length, indexer) so the
//      trim result is consumed downstream.
//
// Every line is a labeled bool; net48 is the byte reference. All probes stay
// on the wired Trim surface (MethodMap.String.cs Trim/TrimStart/TrimEnd ->
// icall_string_trim_*, il2cpp_string_ext.h:112-121).
//
// DELIBERATELY NOT PROBED (documented deviation -- do NOT call here): a
// NON-EMPTY char[] trim set (e.g. TrimStart('x') / TrimEnd(',')). net48 trims
// EXACTLY those chars; the runtime icalls are whitespace-only (the char[]
// argument is DROPPED at emission, MethodMap.String.cs TrimStart/TrimEnd
// "drop" casts) -- a non-empty set would silently behave as the whitespace
// overload and diverge. Probed forms all use the empty-set (no-arg) shape.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class StringOps3 {
    public static void Main() {
        // ============ A1. TrimStart: pure leading removal ============
        Console.WriteLine("ts0=" + ("  abc".TrimStart() == "abc"));
        Console.WriteLine("ts1=" + ("\tabc".TrimStart() == "abc"));
        Console.WriteLine("ts2=" + ("\nabc".TrimStart() == "abc"));
        Console.WriteLine("ts3=" + ("\rabc".TrimStart() == "abc"));
        Console.WriteLine("ts4=" + (" \t\nabc".TrimStart() == "abc"));
        Console.WriteLine("ts5=" + ("\u00A0abc".TrimStart() == "abc"));      // NBSP
        Console.WriteLine("ts6=" + ("\u3000abc".TrimStart() == "abc"));      // ideographic space
        Console.WriteLine("ts7=" + ("abc".TrimStart() == "abc"));            // nothing to trim
        Console.WriteLine("ts8=" + ("  a b  ".TrimStart() == "a b  "));      // inner ws preserved
        Console.WriteLine("ts9=" + ("   ".TrimStart() == ""));               // all-ws -> empty
        Console.WriteLine("ts10=" + ("".TrimStart() == ""));

        // ============ A2. TrimEnd: pure trailing removal ============
        Console.WriteLine("te0=" + ("abc  ".TrimEnd() == "abc"));
        Console.WriteLine("te1=" + ("abc\t".TrimEnd() == "abc"));
        Console.WriteLine("te2=" + ("abc\n".TrimEnd() == "abc"));
        Console.WriteLine("te3=" + ("abc\r".TrimEnd() == "abc"));
        Console.WriteLine("te4=" + ("abc \t\n".TrimEnd() == "abc"));
        Console.WriteLine("te5=" + ("abc\u00A0".TrimEnd() == "abc"));        // NBSP
        Console.WriteLine("te6=" + ("abc\u2003".TrimEnd() == "abc"));        // em space
        Console.WriteLine("te7=" + ("abc".TrimEnd() == "abc"));              // nothing to trim
        Console.WriteLine("te8=" + ("  a b  ".TrimEnd() == "  a b"));        // inner ws preserved
        Console.WriteLine("te9=" + ("   ".TrimEnd() == ""));                 // all-ws -> empty
        Console.WriteLine("te10=" + ("".TrimEnd() == ""));

        // ============ A3. TrimStart/TrimEnd combinations ============
        Console.WriteLine("c0=" + ("  abc  ".TrimStart().TrimEnd() == "abc"));
        Console.WriteLine("c1=" + ("  abc  ".TrimEnd().TrimStart() == "abc"));
        Console.WriteLine("c2=" + (" \t x \r ".TrimStart().TrimEnd() == "x"));
        Console.WriteLine("c3=" + ("  a b  ".TrimStart().TrimEnd() == "a b"));
        Console.WriteLine("c4=" + ("  abc".TrimStart().TrimStart() == "abc"));    // idempotent
        Console.WriteLine("c5=" + ("abc  ".TrimEnd().TrimEnd() == "abc"));        // idempotent
        Console.WriteLine("c6=" + ("  abc  ".Trim().TrimStart() == "abc"));
        Console.WriteLine("c7=" + ("  abc  ".Trim().TrimEnd() == "abc"));
        Console.WriteLine("c8=" + ("   ".TrimStart().TrimEnd() == ""));
        Console.WriteLine("c9=" + ("".TrimStart().TrimEnd() == ""));

        // ============ B. removal semantics / identity ============
        Console.WriteLine("r0=" + ("   ".TrimStart() == ""));
        Console.WriteLine("r1=" + ("   ".TrimEnd() == ""));
        Console.WriteLine("r2=" + ("   ".Trim() == ""));
        Console.WriteLine("r3=" + (" \t\n\r\v\f ".TrimStart() == ""));       // full control-ws set
        Console.WriteLine("r4=" + (" \t\n\r\v\f ".TrimEnd() == ""));
        string clean = "abc";
        Console.WriteLine("id0=" + (ReferenceEquals(clean, clean.TrimStart())));   // no leading ws -> SAME
        Console.WriteLine("id1=" + (ReferenceEquals(clean, clean.TrimEnd())));     // no trailing ws -> SAME
        string dirty = "  abc";
        Console.WriteLine("id2=" + (!ReferenceEquals(dirty, dirty.TrimStart())));  // trimmed -> NEW
        Console.WriteLine("id3=" + (!ReferenceEquals("abc  ", "abc  ".TrimEnd())));
        Console.WriteLine("r5=" + ("abc\t ".TrimEnd().Length == 3));
        Console.WriteLine("r6=" + (" \tabc".TrimStart().Length == 3));

        // ============ C. trim result consumed by downstream string ops ============
        Console.WriteLine("ch0=" + ("  abcdef  ".TrimStart().Substring(0, 3) == "abc"));
        Console.WriteLine("ch1=" + ("  abcdef  ".TrimEnd().Substring(3) == "bcdef"));
        Console.WriteLine("ch2=" + ("  abc  ".TrimStart().Replace('a', 'A') == "Abc  "));
        Console.WriteLine("ch3=" + ("  abc  ".TrimEnd().ToUpper() == "  ABC"));
        Console.WriteLine("ch4=" + ("  abc  ".TrimStart().ToLower() == "abc  "));
        Console.WriteLine("ch5=" + ("  abc  ".TrimStart().PadLeft(6, '-') == "-abc  "));
        Console.WriteLine("ch6=" + ("  abc  ".TrimEnd().PadRight(6, '-') == "  abc-"));
        Console.WriteLine("ch7=" + ("  abc  ".TrimStart().StartsWith("abc")));
        Console.WriteLine("ch8=" + ("  abc  ".TrimEnd().EndsWith("abc")));
        Console.WriteLine("ch9=" + ("  abc  ".TrimStart().Contains("bc")));
        Console.WriteLine("ch10=" + ("  abc  ".TrimEnd().IndexOf('b') == 3));
        Console.WriteLine("ch11=" + ("  abc  ".TrimStart().Length == 5));
        Console.WriteLine("ch12=" + ((int)"  abc  ".TrimEnd()[0] == (int)' '));
        Console.WriteLine("ch13=" + ("  abc  ".TrimStart().TrimEnd().Substring(1, 2) == "bc"));
        Console.WriteLine("ch14=" + ("  \t hi \r  ".TrimStart().TrimEnd().PadLeft(7, '*') == "*****hi"));
    }
}}
