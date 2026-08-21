//------------------------------------------------------------------------------
// stringequal.cs -- STRING EQUALITY DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C73, on the string-equality surface of
// MethodMap.String.cs: instance Equals(string/object), static Equals(a,b),
// op_Equality / op_Inequality, and the comparison-mode instance
// Equals(string, StringComparison) -> icall_string_equals_culture
// (il2cpp_string_ext2.h:96 -- the mode-aware primitive).
//
// SCOPE -- the Equals FAMILY (stringcompare / strcmpdeep / strcomp2 covered
// the CompareTo / CompareOrdinal / Compare / Comparer / Sort / BinarySearch
// families; THIS case drives equality end-to-end):
//
//   1. INSTANCE Equals(string) -- equal content, differing case, prefix,
//      empty, and the null-'value' contract (Equals(null) -> false, net48;
//      the comparison form is null-aware for value).
//   2. INSTANCE Equals(object) -- a string arg boxed to object (content-
//      compared, true on equal content), and a NON-string object arg
//      (false -- string.Equals(object) is false for any non-string).
//   3. STATIC Equals(a,b) -- the ordinal static comparator: equal/diff, the
//      null triad (null,null->true; null,x->false; x,null->false).
//   4. op_Equality / op_Inequality -- string == / != over equal, diff,
//      null-left, null-right, both-null operands (the null-aware static
//      comparator under the emitted ==/!).
//   5. Equals(string, StringComparison) -- ALL SIX net48 modes over ASCII
//      pairs: the culture modes (0/2) are the runtime's documented ordinal
//      equivalents, the ignore-case modes (1/3/5) fold ASCII 'A'-'Z'<->
//      'a'-'z'; a case-different pair flips false/true exactly on the
//      ignore-case boundary; a null 'value' stays false; empty/equal
//      content stays true across every mode.
//   6. ROUND-TRIP -- equality surviving ToLower/ToUpper (ASCII), the
//      equality-after-interning reference identity (two content-equal string
//      literals are the SAME pooled instance -- ReferenceEquals true),
//      ==-after-concat (a runtime concat produces equal content, == true).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class StringEqual
    {
        public static void Main()
        {
            string s = "abc";
            string t = "abc";
            string diff = "abd";
            string empty = "";

            // ===== 1. instance Equals(string) =====
            Console.WriteLine("eq_same=" + s.Equals(t));
            Console.WriteLine("eq_diff=" + (s.Equals(diff) == false));
            Console.WriteLine("eq_case=" + (s.Equals("ABC") == false));
            Console.WriteLine("eq_prefix=" + (s.Equals("ab") == false));
            Console.WriteLine("eq_longer=" + (s.Equals("abcd") == false));
            Console.WriteLine("eq_empty_self=" + empty.Equals(empty));
            Console.WriteLine("eq_s_vs_empty=" + (s.Equals(empty) == false));
            Console.WriteLine("eq_null_value=" + (s.Equals((string)null) == false));
            Console.WriteLine("eq_self=" + s.Equals(s));

            // ===== 2. instance Equals(object) =====
            object boxed = "abc";
            Console.WriteLine("eq_obj_str=" + s.Equals(boxed));
            object boxedDiff = "abd";
            Console.WriteLine("eq_obj_diff=" + (s.Equals(boxedDiff) == false));
            object nonString = 5;
            Console.WriteLine("eq_obj_int=" + (s.Equals(nonString) == false));
            object nullObj = null;
            Console.WriteLine("eq_obj_null=" + (s.Equals(nullObj) == false));

            // ===== 3. static Equals(a,b) =====
            Console.WriteLine("eqst_same=" + string.Equals(s, t));
            Console.WriteLine("eqst_diff=" + (string.Equals(s, diff) == false));
            Console.WriteLine("eqst_null_null=" + string.Equals((string)null, (string)null));
            Console.WriteLine("eqst_null_x=" + (string.Equals((string)null, s) == false));
            Console.WriteLine("eqst_x_null=" + (string.Equals(s, (string)null) == false));
            Console.WriteLine("eqst_case=" + (string.Equals(s, "ABC") == false));

            // ===== 4. op_Equality / op_Inequality =====
            Console.WriteLine("op_eq_same=" + (s == t));
            Console.WriteLine("op_eq_diff=" + (s == diff ? false : true));
            Console.WriteLine("op_ne_same=" + (s != t ? false : true));
            Console.WriteLine("op_ne_diff=" + (s != diff));
            Console.WriteLine("op_eq_null_left=" + (null == s ? false : true));
            Console.WriteLine("op_eq_null_right=" + (s == null ? false : true));
            Console.WriteLine("op_eq_both_null=" + ((string)null == (string)null));
            Console.WriteLine("op_ne_null_left=" + (null != s));
            Console.WriteLine("op_ne_both_null=" + ((string)null != (string)null ? false : true));

            // ===== 5. Equals(string, StringComparison) -- all six modes =====
            Console.WriteLine("sc_ord_upper=" + (s.Equals("ABC", StringComparison.Ordinal) == false));
            Console.WriteLine("sc_ord_igncase=" + s.Equals("ABC", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("sc_cur_upper=" + (s.Equals("ABC", StringComparison.CurrentCulture) == false));
            Console.WriteLine("sc_cur_igncase=" + s.Equals("ABC", StringComparison.CurrentCultureIgnoreCase));
            Console.WriteLine("sc_inv_upper=" + (s.Equals("ABC", StringComparison.InvariantCulture) == false));
            Console.WriteLine("sc_inv_igncase=" + s.Equals("ABC", StringComparison.InvariantCultureIgnoreCase));
            Console.WriteLine("sc_ord_same=" + s.Equals(t, StringComparison.Ordinal));
            Console.WriteLine("sc_igncase_same=" + s.Equals(t, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("sc_empty_same=" + empty.Equals(empty, StringComparison.Ordinal));
            Console.WriteLine("sc_null_value=" + (s.Equals((string)null, StringComparison.Ordinal) == false));
            Console.WriteLine("sc_diff_len=" + (s.Equals("abcd", StringComparison.OrdinalIgnoreCase) == false));

            // ===== 6. round-trip: equality after transforms / pooling =====
            Console.WriteLine("rt_lower=" + "ABC".ToLower().Equals("abc"));
            Console.WriteLine("rt_upper=" + "abc".ToUpper().Equals("ABC"));
            Console.WriteLine("rt_pool_ref=" + ReferenceEquals("abc", "abc"));
            string concat = "a" + "bc";
            Console.WriteLine("rt_concat_eq=" + (concat == "abc"));
            Console.WriteLine("rt_var_eq_pool=" + (s == "abc"));
            Console.WriteLine("rt_eqchain=" + (s.Equals(t) && t.Equals(s)));
        }
    }
}
