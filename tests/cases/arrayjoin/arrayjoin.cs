//------------------------------------------------------------------------------
// arrayjoin.cs -- ARRAY CONCAT/JOIN DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C84, after the array domain closure
// (MethodMap.Array.cs / MethodMap.String.cs -> il2cpp_string_concat_array +
// icall_string_join, il2cpp_string_ext2.h / concat.cpp).
//
// NOTE ON SCOPE: .NET has no System.Array.Concat / System.Array.Join statics
// (the array CONCATENATION surface is System.String.Concat(string[]) and
// System.String.Join(string, string[]), which take ARRAY arguments). The
// case drives that array-input surface plus the Concat/Join COMBINATIONS:
//
//   1. Concat(string[]) full matrix: multi-element, single, empty, null
//      array-free element edge cases (null elements -> empty), empty-string
//      elements, 10-element joins, loop-built arrays, length + content
//      checks.
//   2. Join(string, string[]) full matrix: single/multi-char separators,
//      empty separator, empty array, single element (identity), null
//      separator (= empty), null elements (empty segments), leading/trailing
//      null elements, long separators.
//   3. Concat/Join COMBINATIONS: Join nested in Concat, Concat nested in
//      Join, Join whose separator is a Concat result, Join-of-Joins, and the
//      Join("") == Concat agreement line.
//   4. Array interplay: loop-built arrays, Join over a large array, and the
//      joined result's Length/indexer reads.
//
// KNOWN GAPS (NOT exercised -- acceptance surface, see GAPS.md):
//   * string.Join/Concat object[] overloads (mixed boxed elements) -- wired
//     through the SAME icalls but the strjoin case already covers the
//     object[] element conversion; this case stays on string[].
//   * System.Array.Concat<T> does not exist; Array.FastCopy/Copy nested
//     concat is outside the Concat/Join surface.
//
// EVERY line prints "label=..."; net48 is the byte reference. Empty results
// are bracketed with <> so an empty payload is still a visible line.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ArrayJoin
    {
        static void W(string label, string value)
        {
            Console.WriteLine(label + "=<" + value + ">");
        }

        public static void Main()
        {
            string[] three = new string[] { "a", "b", "c" };
            string[] two = new string[] { "x", "y" };
            string[] withNull = new string[] { "a", null, "c" };
            string[] leadNull = new string[] { null, "b" };
            string[] trailNull = new string[] { "a", null };
            string[] empties = new string[] { "", "a", "" };
            string[] allNull = new string[] { null, null };

            // ================= 1. Concat(string[]) matrix =================
            W("c_three", string.Concat(three));            // abc
            W("c_two", string.Concat(two));                // xy
            W("c_single", string.Concat(new string[] { "only" }));   // only
            W("c_empty", string.Concat(new string[0]));    // <> (empty array)
            W("c_null_elem", string.Concat(withNull));     // ac
            W("c_lead_null", string.Concat(leadNull));     // b
            W("c_trail_null", string.Concat(trailNull));   // a
            W("c_all_null", string.Concat(allNull));       // <> (two nulls)
            W("c_empty_elems", string.Concat(empties));    // a
            W("c_single_null", string.Concat(new string[] { null })); // <>

            string[] ten = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            W("c_ten", string.Concat(ten));                // 0123456789

            // ================= 2. Join(string, string[]) matrix =================
            W("j_comma", string.Join(",", three));         // a,b,c
            W("j_pipe", string.Join("|", three));          // a|b|c
            W("j_dash", string.Join("-", three));          // a-b-c
            W("j_double", string.Join("::", three));       // a::b::c
            W("j_empty_sep", string.Join("", three));      // abc
            W("j_empty_arr", string.Join(",", new string[0])); // <> (empty array)
            W("j_single", string.Join(",", new string[] { "only" })); // only
            W("j_null_sep", string.Join(null, three));     // abc (null sep = empty)
            W("j_null_elem", string.Join(",", withNull));  // a,,c
            W("j_lead_null", string.Join(",", leadNull));  // ,b
            W("j_trail_null", string.Join(",", trailNull));// a,
            W("j_all_null", string.Join(",", allNull));    // ,
            W("j_empty_elems", string.Join("-", empties)); // -a-
            W("j_nullsep_nullelem", string.Join(null, allNull)); // <> (sep + elems all empty)
            W("j_two", string.Join("+", two));             // x+y

            // ================= 3. Concat/Join combinations =================
            W("c_of_j", string.Concat(string.Join(",", three), string.Join("-", two)));   // a,b,cx-y
            W("j_of_c", string.Join(",", new string[] { string.Concat(three), string.Concat(two) })); // abc,xy
            W("j_sep_is_c", string.Join(string.Concat(new string[] { "-", "-" }), three)); // a--b--c
            W("j_of_j", string.Join("|", new string[] { string.Join(",", three), string.Join(",", two) })); // a,b,c|x,y
            W("c_joined_suffix", string.Concat(string.Join("", three), "!"));  // abc!
            W("j_agrees_c", string.Join("", three) == string.Concat(three) ? "same" : "diff"); // same
            W("c_nested_join", string.Concat(new string[] { string.Join(",", ten), "|", string.Join(",", two) })); // 0,...,9|x,y
            W("j_nested_concat", string.Join("", new string[] { string.Concat(new string[] { "p", "q" }), string.Concat(new string[] { "r" }) })); // pqr

            // ================= 4. array interplay =================
            string[] big = new string[200];
            for (int i = 0; i < big.Length; i++) big[i] = "e" + i;
            W("j_big_len", string.Join(",", big).Length.ToString());   // 2+... content length
            W("j_big_head", string.Join(",", big).Substring(0, 6));    // e0,e1
            W("c_big_len", string.Concat(big).Length.ToString());      // concatenated length
            W("c_big_tail", string.Concat(big).Substring(string.Concat(big).Length - 5)); // e199

            string[] joined = new string[] { string.Join(",", three) };
            Console.WriteLine("j_len=" + joined[0].Length);          // 5
            Console.WriteLine("j_idx=" + (joined[0][1] == ','));     // True (comma at index 1)
        }
    }
}
