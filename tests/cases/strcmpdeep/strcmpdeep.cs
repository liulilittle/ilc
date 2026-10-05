//------------------------------------------------------------------------------
// strcmpdeep.cs -- STRING COMPARISON DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C16 (2026-08-10).
//
// Exercises the comparison-mode surface AFTER the strx string-compare fix
// (batches 22c/36/42: CompareOrdinal raw-diff + CompareTo clamping, stringext
// family). Every line prints True/False -- a diff failure shows exactly which
// assertion flipped.
//
// KEY SEMANTIC SPLIT (the runtime stringext.cpp / stringext2.cpp contract,
// fresh-oracle verified 2026-08-08):
//   * INSTANCE CompareTo is CLAMPED: net48 string.CompareTo never reports the
//     raw unit/length difference -- only the sign (-1/0/+1). Even a pair
//     whose ordinal unit difference is ±32 collapses to ±1. NOTE: net48
//     CompareTo is CULTURE-AWARE, so the SIGN of a case pair ('a' vs 'A') is
//     culture-dependent; per the strcompare culture-proof-ASCII convention
//     the case asserts only the CLAMP RANGE ([-1,+1]) for such pairs, never
//     the sign.
//   * STATIC string.CompareOrdinal keeps the RAW difference: it returns the
//     first differing unit's numeric delta (e.g. -32 for 'A' vs 'a'), and at
//     a string boundary the terminator reads as 0, so the pair "ab" vs "a"
//     returns 98 ('b' - 0) and "" vs "a" returns -97 (0 - 'a'). The net48
//     CompareOrdinalHelper pair-scan is replicated EXACTLY (GAPS.md
//     DEFECT-STR2B-1).
//
// Covered families:
//   1. CompareTo CLAMPING -- sign-only on unit diffs, prefix pairs, empty
//      strings and null (CompareTo(null) == 1); NEVER the raw difference.
//   2. CompareOrdinal RAW DIFF -- exact boundary values (-32/+32, -97/98,
//      the length-difference tail), null handling (null==null->0,
//      null<x->-1, x>null->1).
//   3. EQUALITY PATTERNS -- value equality across DISTINCT instances
//      (op_Equality / instance Equals / static Equals all value-based;
//      ReferenceEquals false), case-sensitivity, null == null / null != x,
//      and the == / != inverse pairing.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class StrCmpDeep
    {
        public static void Main()
        {
            // ===== 1. CompareTo CLAMPING -- sign-only =====
            Console.WriteLine("abc".CompareTo("abc") == 0);                // True
            Console.WriteLine("abc".CompareTo("abd") == -1);               // True
            Console.WriteLine("abd".CompareTo("abc") == 1);                // True
            Console.WriteLine("abc".CompareTo("ab") == 1);                 // True (longer > prefix)
            Console.WriteLine("ab".CompareTo("abc") == -1);                // True
            Console.WriteLine("a".CompareTo("b") == -1);                   // True (unit diff, clamped)
            Console.WriteLine("b".CompareTo("a") == 1);                    // True
            Console.WriteLine("abc".CompareTo("") == 1);                   // True
            Console.WriteLine("".CompareTo("abc") == -1);                  // True
            Console.WriteLine("".CompareTo("") == 0);                      // True
            Console.WriteLine("abc".CompareTo(null) == 1);                 // True (null sorts below)
            // CLAMP PROPERTY: instance CompareTo never reports the raw unit
            // difference -- the result is always in [-1, +1], even for a pair
            // whose ordinal unit diff is ±32 (net48 culture-aware CompareTo
            // and the ordinal-clamped runtime both clamp; only the SIGN of a
            // case pair is culture-dependent, so the clamp range is asserted,
            // NOT the sign -- the strcompare culture-proof-ASCII convention).
            Console.WriteLine("a".CompareTo("A") >= -1 && "a".CompareTo("A") <= 1);   // True (clamped)
            Console.WriteLine("A".CompareTo("a") >= -1 && "A".CompareTo("a") <= 1);   // True (clamped)
            Console.WriteLine("ab".CompareTo("aB") >= -1 && "ab".CompareTo("aB") <= 1); // True (clamped)

            // ===== 2. CompareOrdinal RAW DIFF -- exact boundary values =====
            Console.WriteLine(string.CompareOrdinal("abc", "abc") == 0);   // True
            Console.WriteLine(string.CompareOrdinal("abc", "abd") == -1);  // True ('c' - 'd')
            Console.WriteLine(string.CompareOrdinal("A", "a") == -32);     // True (65 - 97)
            Console.WriteLine(string.CompareOrdinal("a", "A") == 32);      // True (97 - 65)
            Console.WriteLine(string.CompareOrdinal("", "a") == -97);      // True (0 - 'a')
            Console.WriteLine(string.CompareOrdinal("ab", "a") == 98);     // True ('b' - terminator 0)
            Console.WriteLine(string.CompareOrdinal("abc", "ab") == 1);    // True (length-difference tail)
            Console.WriteLine(string.CompareOrdinal("abc", "abcd") == -100); // True (terminator 0 - 'd')
            Console.WriteLine(string.CompareOrdinal(null, "x") == -1);     // True (null < x)
            Console.WriteLine(string.CompareOrdinal("x", null) == 1);      // True (x > null)
            Console.WriteLine(string.CompareOrdinal(null, null) == 0);     // True
            Console.WriteLine(string.CompareOrdinal("", "") == 0);         // True
            Console.WriteLine(string.CompareOrdinal("hello", "HELLO") == 32); // True ('h'(104) - 'H'(72))

            // ===== 3. EQUALITY PATTERNS -- value equality across instances =====
            Console.WriteLine(ValueEq("ab" + "c"));                        // True (op_Equality, distinct instance)
            Console.WriteLine(ValueEq("abc"));                             // True
            Console.WriteLine(ValueEq("ABC") == false);                    // True (case-sensitive)
            Console.WriteLine(InEq("abc"));                                // True (receiver-side operand)
            Console.WriteLine(ValueNeq("abd"));                            // True (!=)
            Console.WriteLine(ValueNeq("ABC"));                            // True (!= on case diff)
            Console.WriteLine(ValueNeq("abc") == false);                   // True (!= inverse)
            Console.WriteLine("abc".Equals("abc"));                        // True (instance Equals)
            Console.WriteLine(("abc".Equals("ABC")) == false);             // True
            Console.WriteLine(string.Equals("abc", "abc"));                // True (static Equals)
            Console.WriteLine(string.Equals("abc", "ABC") == false);       // True
            Console.WriteLine(ReferenceEquals(MakeAbc(), MakeAbc()) == false); // True (distinct instances)
            Console.WriteLine(ValueEq(MakeAbc()));                         // True (value equality despite distinct)
            string n = null;
            Console.WriteLine(n == null);                                  // True
            Console.WriteLine(n != "x");                                   // True
            Console.WriteLine("x" != n);                                   // True
            Console.WriteLine((n == "x") == false);                        // True
            Console.WriteLine((null == null));                             // True
        }

        static string MakeAbc() { return new string(new char[] { 'a', 'b', 'c' }); }
        static bool ValueEq(string x) { return x == "abc"; }
        static bool InEq(string x) { return "abc" == x; }
        static bool ValueNeq(string x) { return x != "abc"; }
    }
}
