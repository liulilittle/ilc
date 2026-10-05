//------------------------------------------------------------------------------
// strdeep.cs -- System.String deep-chain reference program (net48 oracle).
//
// Covers the stringext3 OVERLOAD family (positional searches + comparison
// modes) plus the deep search/pad/trim/compare surface:
//
//   * IndexOf(char, startIndex) / IndexOf(char, startIndex, count)
//   * IndexOf(string, startIndex) / IndexOf(string, startIndex, count)
//   * LastIndexOf family -- char/string x {(), (start), (start, count)}
//   * StartsWith(string, StringComparison) / EndsWith(string, StringComparison)
//   * Contains, PadLeft, PadRight, Trim, TrimStart, TrimEnd, CompareOrdinal
//   * Edge cases: empty strings, startIndex == Length, out-of-range start
//     (ArgumentOutOfRangeException), null value (ArgumentNullException),
//     invalid StringComparison value (ArgumentException).
//
// Every line prints a BOOL (predicate against the net48-verified result),
// so a diff failure shows exactly which assertion flipped.
//
// net48 BASELINE (differs from .NET 8!): LastIndexOf with an EMPTY value
// returns Length - 1 when startIndex >= Length (i.e. min(startIndex,
// Length - 1)); .NET 8 returns the startIndex instead. The lines below
// assert the net48 shape because this project's reference runtime is
// .NET Framework 4.8.
//------------------------------------------------------------------------------

using System;

class Program
{
    static void Main()
    {
        // --- IndexOf(char, startIndex): scan [start, Length) ---
        Console.WriteLine("abcab".IndexOf('b', 1) == 1);
        Console.WriteLine("abcab".IndexOf('b', 2) == 3);
        Console.WriteLine("abcab".IndexOf('b', 4) == 4);
        Console.WriteLine("abcab".IndexOf('b', 5) == -1);       // start == Length: legal, no match
        Console.WriteLine("".IndexOf('a', 0) == -1);            // empty string
        bool threw = false;
        try { "abc".IndexOf('a', 4); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // start > Length throws
        threw = false;
        try { "abc".IndexOf('a', -1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // negative start throws

        // --- IndexOf(char, startIndex, count): scan [start, start + count) ---
        Console.WriteLine("abcab".IndexOf('a', 1, 3) == 2);
        Console.WriteLine("abcab".IndexOf('a', 1, 1) == -1);
        Console.WriteLine("abcab".IndexOf('a', 1, 0) == -1);    // count == 0 -> -1
        threw = false;
        try { "abc".IndexOf('b', 1, 3); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // start + count > Length throws
        threw = false;
        try { "abc".IndexOf('b', -1, 2); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);
        threw = false;
        try { "abc".IndexOf('b', 1, -1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);

        // --- IndexOf(string, startIndex) ---
        Console.WriteLine("abcab".IndexOf("bc", 1) == 1);
        Console.WriteLine("abcab".IndexOf("bc", 4) == 4);
        Console.WriteLine("abcab".IndexOf("bc", 5) == -1);
        Console.WriteLine("abc".IndexOf("", 2) == 2);           // empty value -> startIndex
        Console.WriteLine("abc".IndexOf("", 3) == 3);           // start == Length, empty value
        Console.WriteLine("abc".IndexOf("d", 0) == -1);
        threw = false;
        try { "abc".IndexOf((string)null, 0); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);                               // null value throws
        threw = false;
        try { "abc".IndexOf("bc", 4); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);

        // --- IndexOf(string, startIndex, count) ---
        Console.WriteLine("abcab".IndexOf("bc", 1, 3) == 1);
        Console.WriteLine("abcab".IndexOf("bc", 1, 1) == -1);   // value longer than count -> -1
        Console.WriteLine("abc".IndexOf("", 1, 2) == 1);        // empty value -> startIndex
        Console.WriteLine("abc".IndexOf("b", 1, 2) == 1);
        Console.WriteLine("abc".IndexOf("b", 3, 0) == -1);      // count == 0 -> -1
        threw = false;
        try { "abc".IndexOf("b", 1, 3); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // start + count > Length throws
        threw = false;
        try { "abc".IndexOf("b", 3, 1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);

        // --- LastIndexOf(char): backward scan [0, Length - 1] ---
        Console.WriteLine("abcab".LastIndexOf('a') == 3);
        Console.WriteLine("abcab".LastIndexOf('x') == -1);
        Console.WriteLine("".LastIndexOf('a') == -1);

        // --- LastIndexOf(char, startIndex): backward scan [0, start] ---
        Console.WriteLine("abcab".LastIndexOf('a', 2) == 0);
        Console.WriteLine("abcab".LastIndexOf('a', 4) == 3);
        Console.WriteLine("abcab".LastIndexOf('a', 0) == 0);
        Console.WriteLine("abc".LastIndexOf('c', 2) == 2);
        threw = false;
        try { "abc".LastIndexOf('c', 3); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // char form: start == Length THROWS
        threw = false;
        try { "abc".LastIndexOf('c', -1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);

        // --- LastIndexOf(char, startIndex, count): scan [start - count + 1, start] ---
        Console.WriteLine("abcab".LastIndexOf('a', 2, 2) == -1);
        Console.WriteLine("abcab".LastIndexOf('a', 4, 3) == 3);
        Console.WriteLine("baac".LastIndexOf('a', 3, 3) == 2);
        Console.WriteLine("abcab".LastIndexOf('a', 2, 0) == -1);    // count == 0 -> -1
        threw = false;
        try { "abcab".LastIndexOf('a', 1, 3); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // count > startIndex + 1 throws
        threw = false;
        try { "abcab".LastIndexOf('a', -1, 1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);

        // --- LastIndexOf(string) ---
        Console.WriteLine("abcab".LastIndexOf("ab") == 3);
        Console.WriteLine("abc".LastIndexOf("bc") == 1);
        Console.WriteLine("abc".LastIndexOf("x") == -1);
        Console.WriteLine("abc".LastIndexOf("") == 2);          // net48: Length - 1
        Console.WriteLine("".LastIndexOf("") == 0);             // net48: empty string -> 0
        threw = false;
        try { "abc".LastIndexOf((string)null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);

        // --- LastIndexOf(string, startIndex) ---
        Console.WriteLine("abcab".LastIndexOf("ab", 4) == 3);
        Console.WriteLine("abcab".LastIndexOf("ab", 3) == 3);
        Console.WriteLine("abcab".LastIndexOf("ab", 2) == 0);
        Console.WriteLine("abcab".LastIndexOf("ab", 5) == 3);   // string form: start == Length LEGAL
        Console.WriteLine("abc".LastIndexOf("", 0) == 0);
        Console.WriteLine("abc".LastIndexOf("", 2) == 2);
        Console.WriteLine("abc".LastIndexOf("", 3) == 2);       // net48: min(3, Length - 1) = 2
        threw = false;
        try { "abc".LastIndexOf("bc", 4); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);
        threw = false;
        try { "abc".LastIndexOf("bc", -1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);

        // --- LastIndexOf(string, startIndex, count) ---
        Console.WriteLine("abcab".LastIndexOf("ab", 4, 2) == 3);
        Console.WriteLine("abcab".LastIndexOf("ab", 4, 1) == -1);
        Console.WriteLine("abcab".LastIndexOf("ab", 3, 0) == -1);    // count == 0 -> -1
        Console.WriteLine("abcab".LastIndexOf("ab", 4, 4) == 3);
        Console.WriteLine("abc".LastIndexOf("", 2, 3) == 2);    // empty value, count legal
        threw = false;
        try { "abcab".LastIndexOf("ab", 4, 6); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // count > startIndex + 1 throws
        threw = false;
        try { "abc".LastIndexOf("", 2, 4); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine(threw);                               // empty value still bounds-checked

        // --- StartsWith(string, StringComparison) ---
        Console.WriteLine("abc".StartsWith("ab", StringComparison.Ordinal));
        Console.WriteLine("abc".StartsWith("AB", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("abc".StartsWith("AB", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".StartsWith("", StringComparison.Ordinal));
        Console.WriteLine("abc".StartsWith("abcd", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".StartsWith("a", StringComparison.CurrentCulture));
        Console.WriteLine("abc".StartsWith("A", StringComparison.CurrentCultureIgnoreCase));
        Console.WriteLine("abc".StartsWith("a", StringComparison.InvariantCulture));
        Console.WriteLine("abc".StartsWith("A", StringComparison.InvariantCultureIgnoreCase));
        threw = false;
        try { "abc".StartsWith("a", (StringComparison)99); } catch (ArgumentException) { threw = true; }
        Console.WriteLine(threw);                               // invalid mode throws ArgumentException
        threw = false;
        try { "abc".StartsWith(null, StringComparison.Ordinal); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);

        // --- EndsWith(string, StringComparison) ---
        Console.WriteLine("abc".EndsWith("bc", StringComparison.Ordinal));
        Console.WriteLine("abc".EndsWith("BC", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("abc".EndsWith("BC", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".EndsWith("", StringComparison.Ordinal));

        // --- Contains ---
        Console.WriteLine("abc".Contains("bc"));
        Console.WriteLine("abc".Contains("d") == false);
        Console.WriteLine("abc".Contains(""));
        Console.WriteLine("".Contains(""));
        threw = false;
        try { "abc".Contains(null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine(threw);

        // --- PadLeft / PadRight (net48 reference only; runtime gap) ---
        Console.WriteLine("5".PadLeft(3, '0') == "005");
        Console.WriteLine("12345".PadLeft(2, '0') == "12345");  // totalWidth < Length: unchanged
        Console.WriteLine("5".PadLeft(3) == "  5");
        Console.WriteLine("5".PadRight(3, '0') == "500");
        Console.WriteLine("12345".PadRight(2, '0') == "12345");
        Console.WriteLine("5".PadRight(3) == "5  ");

        // --- Trim / TrimStart / TrimEnd ---
        Console.WriteLine("  abc  ".Trim() == "abc");
        Console.WriteLine(" \t\n abc \r\n ".Trim() == "abc");
        Console.WriteLine("   ".Trim() == "");
        Console.WriteLine("".Trim() == "");
        Console.WriteLine("abc".Trim() == "abc");
        Console.WriteLine("  abc  ".TrimStart() == "abc  ");
        Console.WriteLine("  abc  ".TrimEnd() == "  abc");
        Console.WriteLine("\t abc".TrimStart() == "abc");
        Console.WriteLine("abc \t".TrimEnd() == "abc");

        // --- CompareOrdinal ---
        Console.WriteLine(string.CompareOrdinal("abc", "abc") == 0);
        Console.WriteLine(string.CompareOrdinal(null, "x") == -1);
        Console.WriteLine(string.CompareOrdinal("x", null) == 1);
        Console.WriteLine(string.CompareOrdinal(null, null) == 0);
        Console.WriteLine(string.CompareOrdinal("abc", "abd") == -1);
        Console.WriteLine(string.CompareOrdinal("abc", "ab") == 1);
        Console.WriteLine(string.CompareOrdinal("A", "a") == -32);
        Console.WriteLine(string.CompareOrdinal("", "") == 0);
    }
}
