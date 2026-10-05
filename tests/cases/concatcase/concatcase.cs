//------------------------------------------------------------------------------
// concatcase.cs -- end-to-end reference for the single-allocation string
// concatenation runtime domain (il2cpp_string_concat_many / _array /
// _builder_join / _repeat).
//
// This program runs on .NET Framework 4.8 and prints the REFERENCE output
// that the transpiled AOT version must reproduce byte-for-byte. It exercises
// the concat semantics the runtime implements:
//
//   * multi-segment Concat (2/3/4 segments, null segments -> empty)
//   * Concat(string[]) with null elements and an empty array
//   * string.Join semantics (separator strictly between elements, single
//     element joined unchanged, null elements -> empty)
//   * repeated concatenation (the loop shape il2cpp_string_repeat optimizes)
//   * a large concatenation (200 elements x 2 units)
//
// NOTE: net48 has no string.Join(char, string[]) overload (it arrived with
// .NET Core 2.0), so the reference uses the string-separator form; the
// runtime's il2cpp_string_builder_join takes a single char16_t separator,
// which is the one-char specialization of the same join semantics.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ConcatCase {
    public static void Main() {
        // Multi-segment Concat (the icall concat_many shape).
        Console.WriteLine(string.Concat("a", "b", "c"));             // abc
        Console.WriteLine(string.Concat("a", null, "c"));            // ac (null -> empty)
        Console.WriteLine(string.Concat("Hello, ", "World!", ""));   // Hello, World!
        Console.WriteLine(string.Concat("", "", ""));                // (empty line)
        // Concat(string[]) (the icall concat_array shape).
        string[] three = new string[] { "x", "y", "z" };
        Console.WriteLine(string.Concat(three));                     // xyz
        string[] withNull = new string[] { "a", null, "c" };
        Console.WriteLine(string.Concat(withNull));                  // ac
        Console.WriteLine(string.Concat(new string[0]));             // (empty line)
        // Join semantics (the icall builder_join shape).
        Console.WriteLine(string.Join(",", three));                  // x,y,z
        Console.WriteLine(string.Join(",", withNull));               // a,,c
        Console.WriteLine(string.Join(",", new string[] { "only" }));// only
        Console.WriteLine(string.Join(",", new string[0]));          // (empty line)
        // Repeated concatenation (the icall repeat shape).
        string s = "ab"; string r = "";
        for (int i = 0; i < 3; i++) r += s;                          // ababab
        Console.WriteLine(r);
        // Large concatenation: 200 elements x 2 units.
        string[] big = new string[200];
        for (int i = 0; i < big.Length; i++) big[i] = "ab";
        string bigConcat = string.Concat(big);
        Console.WriteLine(bigConcat.Length);                         // 400
        Console.WriteLine(bigConcat.Substring(0, 4));                // abab
    }
}}
