//------------------------------------------------------------------------------
// stringops2.cs -- System.String DEEP e2e reference program #2 (net48 oracle,
// byte-for-byte). Created in e2e batch C57 (2026-08-12), AFTER the string
// static surface (stringops: CompareOrdinal / Concat / Join(string, string[])
// / IsNullOr* / Copy) AND the split family (strcomp2 / RT-SPL2:
// icall_string_split / icall_string_split_options). This case expands two
// axes:
//
//   A. JOIN FAMILY (icall_string_join / JoinFromArray, il2cpp_string_ext2.h):
//      string[] elements with null / empty, multi-char separators, single /
//      empty arrays, OBJECT[] elements (int / double / bool / string / null
//      -- converted via icall_object_to_string), and the GENERIC
//      Join<T>(string, IEnumerable<T>) value-array shapes (int[] / long[] /
//      double[] -- randomcase precedent).
//   B. SPLIT FAMILY (icall_string_split_options): char[] multi-separator,
//      leading/trailing/consecutive separators (None keeps empty segments),
//      whitespace split (null / empty char[] / null string[]), char[] and
//      string[] with StringSplitOptions.None / RemoveEmptyEntries, multi-char
//      string[] separators, null/empty string[] separator elements skipped,
//      all-empty RemoveEmptyEntries -> [], empty receiver -> [""] / [].
//   C. COMPARISON FAMILY: CompareOrdinal null + raw-difference semantics,
//      static Compare(a,b) (ASCII-only -- the ordinal vs net48-culture
//      deviation), Compare(a,b,StringComparison.Ordinal), instance CompareTo
//      (clamped sign, null -> 1), static/instance Equals, Equals(string,
//      StringComparison) Ordinal / OrdinalIgnoreCase (ASCII fold), and
//      StartsWith/EndsWith(string, StringComparison) Ordinal / OrdinalIgnoreCase.
//
// RECORDED GAPS -- NOT exercised (see GAPS.md):
//   * Join(char, ...) / Join(string, ..., int, int) overloads -- net48 has no
//     char-separator Join; the startIndex/count overload is unwired (LOUD).
//   * Split(char) / Split(string) / Split with count params -- unwired.
//   * newarr double[] / float[] / bool[] / short[] (audit C4: the transpiler
//     only allocates Int32/Byte/Char/Int64/UInt64/reference-element arrays) --
//     the double[] Join probe was dropped; double formatting IS covered via the
//     object[] { 1, 2.5, true, "s" } boxed-double element.
//   * Compare with OrdinalIgnoreCase / culture modes -- the comparison arg is
//     DROPPED at emission (ordinal-only); only StringComparison.Ordinal is safe.
//   * CompareTo on non-ASCII / mixed-case -- net48 is culture-aware, the
//     runtime is ordinal; all CompareTo probes stay lowercase ASCII.
//   * Contains/IndexOf/LastIndexOf comparisons with StringComparison -- LOUD.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class StringOps2 {
    public static void Main() {
        // ============ A. Join family ============
        Console.WriteLine(string.Join("-", new string[] { "a", "b", "c" }) == "a-b-c");
        Console.WriteLine(string.Join(",", new string[] { "a", null, "c" }) == "a,,c");
        Console.WriteLine(string.Join("", new string[] { "a", "b" }) == "ab");
        Console.WriteLine(string.Join("--", new string[] { "a", "b", "c" }) == "a--b--c");
        Console.WriteLine(string.Join(",", new string[] { "x" }) == "x");
        Console.WriteLine(string.Join(",", new string[] { }) == "");
        Console.WriteLine(string.Join(",", new string[] { "", "", "" }) == ",,");
        Console.WriteLine(string.Join(",", new string[] { null, null }) == ",");
        Console.WriteLine(string.Join("|", new object[] { 1, 2.5, true, "s" }) == "1|2.5|True|s");
        Console.WriteLine(string.Join(",", new object[] { 42 }) == "42");
        Console.WriteLine(string.Join(",", new object[] { }) == "");
        Console.WriteLine(string.Join(",", new object[] { "p", null, "q" }) == "p,,q");
        Console.WriteLine(string.Join(",", new object[] { null, 42 }) == "");
        Console.WriteLine(string.Join(",", new object[] { null, 2.5, true }) == "");
        Console.WriteLine(string.Join(",", new object[] { null }) == "");
        Console.WriteLine(string.Join(",", new string[] { null, "x" }) == ",x");
        Console.WriteLine(string.Join(",", new int[] { 1, 2, 3 }) == "1,2,3");
        Console.WriteLine(string.Join("-", new int[] { }) == "");
        Console.WriteLine(string.Join(",", new long[] { 10, 20 }) == "10,20");
        Console.WriteLine(string.Join("-", new long[] { 100, 200 }) == "100-200");

        // ============ B. Split family ============
        string[] sp = "a,b,c".Split(new char[] { ',' });
        Console.WriteLine(sp.Length == 3);
        Console.WriteLine(sp[0] == "a");
        Console.WriteLine(sp[1] == "b");
        Console.WriteLine(sp[2] == "c");
        string[] sp2 = "a,b;c".Split(new char[] { ',', ';' });
        Console.WriteLine(sp2.Length == 3);
        Console.WriteLine(sp2[1] == "b");
        string[] sp3 = ",a,,b,".Split(new char[] { ',' });
        Console.WriteLine(sp3.Length == 5);
        Console.WriteLine(sp3[0] == "");
        Console.WriteLine(sp3[1] == "a");
        Console.WriteLine(sp3[2] == "");
        Console.WriteLine(sp3[3] == "b");
        Console.WriteLine(sp3[4] == "");
        string[] sp4 = "a b\tc".Split(null);
        Console.WriteLine(sp4.Length == 3);
        string[] sp5 = "a  b\nc".Split(new char[0]);
        Console.WriteLine(sp5.Length == 4);
        Console.WriteLine(sp5[0] == "a");
        Console.WriteLine(sp5[1] == "");
        Console.WriteLine(sp5[2] == "b");
        Console.WriteLine(sp5[3] == "c");
        string[] sp6 = ",a,,b,".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(sp6.Length == 2);
        Console.WriteLine(sp6[0] == "a");
        Console.WriteLine(sp6[1] == "b");
        string[] sp7 = "a b  c".Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(sp7.Length == 3);
        string[] sp8 = "a,b".Split(new char[] { ',' }, StringSplitOptions.None);
        Console.WriteLine(sp8.Length == 2);
        string[] sp9 = ",,".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(sp9.Length == 0);
        string[] sp10 = "".Split(new char[] { ',' });
        Console.WriteLine(sp10.Length == 1);
        Console.WriteLine(sp10[0] == "");
        string[] sp11 = "".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(sp11.Length == 0);
        string[] sp12 = "axxbxxc".Split(new string[] { "xx" }, StringSplitOptions.None);
        Console.WriteLine(sp12.Length == 3);
        Console.WriteLine(sp12[0] == "a");
        Console.WriteLine(sp12[1] == "b");
        Console.WriteLine(sp12[2] == "c");
        string[] sp13 = "abc".Split(new string[] { null }, StringSplitOptions.None);
        Console.WriteLine(sp13.Length == 1);
        Console.WriteLine(sp13[0] == "abc");
        string[] sp14 = "abc".Split(new string[] { "" }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(sp14.Length == 1);
        Console.WriteLine(sp14[0] == "abc");
        string[] sp15 = "axx".Split(new string[] { "xx" }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(sp15.Length == 1);
        Console.WriteLine(sp15[0] == "a");
        string[] sp16 = "a b".Split((string[])null, StringSplitOptions.None);
        Console.WriteLine(sp16.Length == 2);
        Console.WriteLine(sp16[0] == "a");
        Console.WriteLine(sp16[1] == "b");
        string[] sp17 = "a  b  c".Split((string[])null, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine(sp17.Length == 3);

        // ============ C. Comparison family ============
        Console.WriteLine(string.CompareOrdinal("abc", "abc") == 0);
        Console.WriteLine(string.CompareOrdinal("abc", "abd") == -1);
        Console.WriteLine(string.CompareOrdinal("abd", "abc") == 1);
        Console.WriteLine(string.CompareOrdinal("abc", "ab") == 1);
        Console.WriteLine(string.CompareOrdinal("ab", "abc") == -1);
        Console.WriteLine(string.CompareOrdinal(null, "x") == -1);
        Console.WriteLine(string.CompareOrdinal("x", null) == 1);
        Console.WriteLine(string.CompareOrdinal(null, null) == 0);
        Console.WriteLine(string.Compare("abc", "abc") == 0);
        Console.WriteLine(string.Compare("abc", "abd") == -1);
        Console.WriteLine(string.Compare("abd", "abc") == 1);
        Console.WriteLine(string.Compare("abc", "ab") == 1);
        Console.WriteLine(string.Compare(null, "x") == -1);
        Console.WriteLine(string.Compare("x", null) == 1);
        Console.WriteLine(string.Compare("abc", "abc", StringComparison.Ordinal) == 0);
        Console.WriteLine(string.Compare("abc", "abd", StringComparison.Ordinal) == -1);
        Console.WriteLine(string.Compare("abd", "abc", StringComparison.Ordinal) == 1);
        Console.WriteLine("abc".CompareTo("abc") == 0);
        Console.WriteLine("abc".CompareTo("abd") == -1);
        Console.WriteLine("abd".CompareTo("abc") == 1);
        Console.WriteLine("abc".CompareTo("ab") == 1);
        Console.WriteLine("ab".CompareTo("abc") == -1);
        Console.WriteLine("abc".CompareTo(null) == 1);
        Console.WriteLine(string.Equals("abc", "abc") == true);
        Console.WriteLine(string.Equals("abc", "abd") == false);
        Console.WriteLine(string.Equals(null, null) == true);
        Console.WriteLine(string.Equals("abc", null) == false);
        Console.WriteLine("abc".Equals("abc") == true);
        Console.WriteLine("abc".Equals("abd") == false);
        Console.WriteLine("abc".Equals("ABC", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".Equals("ABC", StringComparison.OrdinalIgnoreCase) == true);
        Console.WriteLine("abc".Equals("abc", StringComparison.Ordinal) == true);
        Console.WriteLine("AbC".Equals("aBc", StringComparison.OrdinalIgnoreCase) == true);
        Console.WriteLine("abc".StartsWith("ab", StringComparison.Ordinal) == true);
        Console.WriteLine("abc".StartsWith("bc", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".StartsWith("AB", StringComparison.OrdinalIgnoreCase) == true);
        Console.WriteLine("aBc".StartsWith("Ab", StringComparison.OrdinalIgnoreCase) == true);
        Console.WriteLine("abc".EndsWith("bc", StringComparison.Ordinal) == true);
        Console.WriteLine("abc".EndsWith("ab", StringComparison.Ordinal) == false);
        Console.WriteLine("abc".EndsWith("BC", StringComparison.OrdinalIgnoreCase) == true);
        Console.WriteLine("aBc".EndsWith("Bc", StringComparison.OrdinalIgnoreCase) == true);
    }
}}
