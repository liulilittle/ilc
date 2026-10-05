//------------------------------------------------------------------------------
// stringops.cs -- System.String static-method reference program (net48 oracle).
//
// Runs on the REAL .NET Framework 4.8 to capture the reference output for the
// runtime icalls in stringext2.cpp (tests/runtime/test_stringext2.cpp asserts
// the same semantics in C++):
//
//   * CompareOrdinal  -- the static ordinal compare the icall mirrors (null
//                        semantics + unit diff + length diff). NOTE: the
//                        default string.Compare(a, b) overload is
//                        CULTURE-sensitive on net48; CompareOrdinal is the
//                        deterministic ordinal form the runtime implements.
//   * Concat 2/3/4, null-as-empty, and the Concat(object, object) numeric
//     conveniences (int / bool / double).
//   * Join with a string separator: separator between elements only; a null
//     element joins as the empty string.
//   * IsNullOrEmpty / IsNullOrWhiteSpace.
//   * The string indexer (get_Chars, printed as int to show code units).
//   * string.Copy: always a new instance (ReferenceEquals == False).
//   * string.Length.
//
// Every line below is deterministic; the output must match line-for-line.
//------------------------------------------------------------------------------

using System;

class Program
{
    static void Main()
    {
        // Compare: ordinal static comparison with null semantics
        Console.WriteLine(string.CompareOrdinal("abc", "abc")); // 0
        Console.WriteLine(string.CompareOrdinal(null, "x"));    // -1 (null < non-null)
        Console.WriteLine(string.CompareOrdinal("x", null));    // 1 (non-null > null)
        Console.WriteLine(string.CompareOrdinal("abc", "abd")); // -1 (first differing unit)
        Console.WriteLine(string.CompareOrdinal("abc", "ab"));  // 1 (length diff)
        Console.WriteLine(string.CompareOrdinal("A", "a"));     // -32 (ordinal: 65 - 97)

        // Concat: fixed arities and null-as-empty
        Console.WriteLine(string.Concat("a", "b"));              // ab
        Console.WriteLine(string.Concat("a", "b", "c"));         // abc
        Console.WriteLine(string.Concat("a", "b", "c", "d"));    // abcd
        Console.WriteLine(string.Concat((string)null, "x"));     // x (null = empty)

        // Concat(object, object) numeric conveniences
        Console.WriteLine(string.Concat("x", 42));              // x42
        Console.WriteLine(string.Concat("x", true));            // xTrue
        Console.WriteLine(string.Concat("x", 1.5));             // x1.5

        // Join: separator between elements, null element = empty
        Console.WriteLine(string.Join(",", new string[] { "a", "b", "c" }));  // a,b,c
        Console.WriteLine(string.Join("-", new string[] { "a", null, "c" })); // a--c
        Console.WriteLine(string.Join("", new string[] { "a", "b" }));        // ab

        // IsNullOrEmpty / IsNullOrWhiteSpace
        Console.WriteLine(string.IsNullOrEmpty(null));          // True
        Console.WriteLine(string.IsNullOrEmpty(""));            // True
        Console.WriteLine(string.IsNullOrEmpty(" "));           // False
        Console.WriteLine(string.IsNullOrWhiteSpace(null));     // True
        Console.WriteLine(string.IsNullOrWhiteSpace(" \t\n"));  // True
        Console.WriteLine(string.IsNullOrWhiteSpace(" x "));    // False

        // Chars: the string indexer (code units, printed as int)
        string s = "abc";
        Console.WriteLine((int)s[0]);  // 97
        Console.WriteLine((int)s[2]);  // 99

        // Copy: always a new instance
        string t = string.Copy(s);
        Console.WriteLine(object.ReferenceEquals(s, t)); // False
        Console.WriteLine(t);                            // abc

        // Length: string.Length
        Console.WriteLine(s.Length); // 3
    }
}
