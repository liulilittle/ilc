//------------------------------------------------------------------------------
// strdeep3.cs -- STRING DEEP 3 e2e reference case (net48 oracle, byte-for-byte).
// Created in e2e batch C40, AFTER the string-pooling iron rule is pinned at
// HEAD: literals are interned (the StringLiteral table -> stringintern.cpp),
// the empty string is pooled exactly once, runtime-built strings are FRESH,
// and String.Intern returns the pool entry (intern C20 / internchain /
// internops all byte-exact). THIS case interleaves all FIVE string domains --
// INTERN / COMPARISON / CONCAT / REPLACE / SPLIT -- into cross-domain chains
// so a break in one domain (a pooled instance reused where a fresh one is
// required, a comparison mode fold, a replace identity path, a split
// separator shape) flips exactly one probe:
//
//   1. POOLING vs VALUE: ReferenceEquals vs == on literal / fresh / interned
//      strings; intern of a runtime concat returns the LITERAL instance
//      (its content's literal is already pooled -- the input itself is NOT
//      returned); concat of two empties returns the POOLED empty (net48
//      Concat null/empty short-circuit, csc-probe-pinned); intern of a
//      fresh-built MISS makes the input the pool entry.
//   2. COMPARISON family: == != Equals(object) Equals(StringComparison)
//      CompareTo Compare(string,string) CompareOrdinal StartsWith EndsWith
//      Contains -- null-aware and mode-aware (OrdinalIgnoreCase folds ASCII).
//   3. CONCAT family: +, 2/3/4-arg fixed forms, string[] array form,
//      object[] array form, numeric convenience (string + number), null
//      counts as empty.
//   4. REPLACE family: char/char + string/string, identity-on-no-match,
//      delete-via-empty newValue, chained replace.
//   5. SPLIT family: char[], string[] separators, StringSplitOptions
//      (None retains / RemoveEmptyEntries drops), null-array whitespace
//      overload, split->join / split->replace->concat round-trips.
//   6. CROSS-DOMAIN chains: intern -> split; replace on a pooled instance
//      does NOT break pool identity; the five-domain
//      replace->split->join->upper->intern chain.
//
// Every line prints "label=True/False". net48 is the reference baseline.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class StrDeep3
    {
        static string Pick() { return "pd-key"; }
        static string PickOther() { return "pd-other"; }

        // reference equality via the (object) cast (disables string == value).
        static int RefEq(string a, string b) { return (object)a == (object)b ? 1 : 0; }

        public static void Main()
        {
            // ===== 1. pooling vs value =====
            Console.WriteLine("pool_lit_same=" + ReferenceEquals("pd-key", "pd-key"));
            Console.WriteLine("pool_lit_cross=" + ReferenceEquals("pd-key", Pick()));
            Console.WriteLine("pool_lit_diff=" + (ReferenceEquals("pd-key", PickOther()) == false));
            string fresh = string.Copy("pd-key");
            Console.WriteLine("pool_fresh_notlit=" + (RefEq(fresh, "pd-key") == 0));
            string built = string.Concat("pd-", "key");
            Console.WriteLine("pool_concat_fresh=" + (RefEq(built, "pd-key") == 0));
            string pooled = string.Intern(built);
            Console.WriteLine("pool_intern_lit=" + (RefEq(pooled, "pd-key") == 1));
            Console.WriteLine("pool_intern_fresh=" + (RefEq(pooled, built) == 0));
            Console.WriteLine("pool_empty=" + ReferenceEquals(string.Intern(""), ""));
            Console.WriteLine("pool_empty_fresh=" + (RefEq(string.Concat("", ""), "") == 1));
            Console.WriteLine("pool_val_equal=" + (built == "pd-key"));

            // ===== 2. comparison family =====
            Console.WriteLine("cmp_eq=" + ("abc" == "abc"));
            Console.WriteLine("cmp_ne=" + ("abc" != "abd"));
            Console.WriteLine("cmp_equals_obj=" + "abc".Equals((object)"abc"));
            Console.WriteLine("cmp_equals_ign=" + "abc".Equals("ABC", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("cmp_equals_ign_false=" + ("abc".Equals("ABD", StringComparison.OrdinalIgnoreCase) == false));
            Console.WriteLine("cmp_compareto_lt=" + ("abc".CompareTo("abd") < 0));
            Console.WriteLine("cmp_compareto_eq=" + ("abc".CompareTo("abc") == 0));
            Console.WriteLine("cmp_compare_lt=" + (string.Compare("abc", "abd") < 0));
            Console.WriteLine("cmp_compareord_lt=" + (string.CompareOrdinal("abc", "abd") < 0));
            Console.WriteLine("cmp_compareord_gt=" + (string.CompareOrdinal("abd", "abc") > 0));
            Console.WriteLine("cmp_compareord_case=" + (string.CompareOrdinal("abc", "ABC") != 0));
            Console.WriteLine("cmp_sw=" + "abcdef".StartsWith("abc"));
            Console.WriteLine("cmp_ew=" + "abcdef".EndsWith("def"));
            Console.WriteLine("cmp_contains=" + "abcdef".Contains("cd"));
            Console.WriteLine("cmp_sw_false=" + ("abcdef".StartsWith("bcd") == false));
            Console.WriteLine("cmp_chain=" + ("abcdef".StartsWith("ab") && "abcdef".EndsWith("ef") && "abcdef".Contains("cde")));

            // ===== 3. concat family =====
            string a = "ab", b = "cd", c = "ef", d = "gh";
            Console.WriteLine("cat_plus=" + (a + b == "abcd"));
            Console.WriteLine("cat_2=" + (string.Concat(a, b) == "abcd"));
            Console.WriteLine("cat_3=" + (string.Concat(a, b, c) == "abcdef"));
            Console.WriteLine("cat_4=" + (string.Concat(a, b, c, d) == "abcdefgh"));
            Console.WriteLine("cat_arr=" + (string.Concat(new string[] { "x", "y", "z" }) == "xyz"));
            Console.WriteLine("cat_null=" + (string.Concat("x", (string)null, "y") == "xy"));
            Console.WriteLine("cat_obj=" + (string.Concat(new object[] { 1, "+", 2 }) == "1+2"));
            Console.WriteLine("cat_int=" + (string.Concat("n=", 42) == "n=42"));
            Console.WriteLine("cat_chain=" + ("x" + 1 + "y" == "x1y"));

            // ===== 4. replace family =====
            Console.WriteLine("rep_c=" + ("aXbXc".Replace('X', '1') == "a1b1c"));
            string rq = "abc";
            Console.WriteLine("rep_nomatch_id=" + ReferenceEquals(rq, rq.Replace('z', 'y')));
            Console.WriteLine("rep_s=" + ("aXbXc".Replace("Xb", "!") == "a!Xc"));
            Console.WriteLine("rep_del=" + ("abXcd".Replace("X", "") == "abcd"));
            Console.WriteLine("rep_chain=" + ("aXbYc".Replace('X', '1').Replace('Y', '2') == "a1b2c"));
            Console.WriteLine("rep_word=" + ("Hello World".Replace("World", "IL2CPP") == "Hello IL2CPP"));
            Console.WriteLine("rep_all=" + ("aaaa".Replace('a', 'b') == "bbbb"));
            Console.WriteLine("rep_chain_len=" + ("aXbYc".Replace('X', '1').Replace('Y', '2').Length == 5));

            // ===== 5. split family =====
            string[] sp1 = "a,b,c".Split(',');
            Console.WriteLine("spl_1=" + (sp1.Length == 3 && sp1[0] == "a" && sp1[2] == "c"));
            string[] sp2 = "a-b_c".Split(new char[] { '-', '_' });
            Console.WriteLine("spl_multi=" + (sp2.Length == 3));
            string[] sp3 = "a,,b".Split(',');
            Console.WriteLine("spl_retain=" + (sp3.Length == 3 && sp3[1] == ""));
            string[] sp4 = "a,,b".Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine("spl_rem=" + (sp4.Length == 2));
            string[] sp5 = "abXYcdXYef".Split(new string[] { "XY" }, StringSplitOptions.None);
            Console.WriteLine("spl_sarr=" + (sp5.Length == 3 && sp5[0] == "ab" && sp5[2] == "ef"));
            string[] sp6 = "a b c".Split((char[])null);
            Console.WriteLine("spl_ws=" + (sp6.Length == 3 && sp6[0] == "a" && sp6[2] == "c"));
            string[] sp7 = " a  b ".Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine("spl_ws_rem=" + (sp7.Length == 2 && sp7[0] == "a" && sp7[1] == "b"));

            // ===== 6. cross-domain chains =====
            string p0 = "aa|bb|cc".Split('|')[0];
            string p1 = "aa|bb|cc".Split('|')[1].Replace('b', 'z');
            Console.WriteLine("x_p1=" + (p1 == "zz"));
            Console.WriteLine("x_concat=" + (string.Concat(p0, p1, "cc") == "aazzcc"));
            Console.WriteLine("x_join=" + (string.Join(",", "a,b,c".Split(',')) == "a,b,c"));
            string pbuilt = string.Concat("k1;", "k2");
            string ppool = string.Intern(pbuilt);
            Console.WriteLine("x_intern_miss=" + (RefEq(ppool, pbuilt) == 1));
            string[] pparts = ppool.Split(';');
            Console.WriteLine("x_parts=" + (pparts.Length == 2 && pparts[0] == "k1" && pparts[1] == "k2"));
            string plit = string.Intern(string.Concat("pd-", "key"));
            Console.WriteLine("x_intern_hit=" + (RefEq(plit, "pd-key") == 1));
            string pid = string.Intern("identity");
            Console.WriteLine("x_pool_rep=" + (pid.Replace("ent", "!!") == "id!!ity"));
            Console.WriteLine("x_pool_still=" + (RefEq(pid, "identity") == 1));
            string s = "The quick brown fox";
            string rep = s.Replace("fox", "cat");
            string[] words = rep.Split(' ');
            string back = string.Join("-", words);
            Console.WriteLine("x_words=" + (words.Length == 4));
            Console.WriteLine("x_back=" + (back == "The-quick-brown-cat"));
            Console.WriteLine("x_up=" + (back.ToUpper() == "THE-QUICK-BROWN-CAT"));
            Console.WriteLine("x_intern_lit=" + (RefEq(string.Intern(s), s) == 1));
            Console.WriteLine("x_fresh_after=" + (RefEq(string.Intern(string.Concat("The ", "quick")), s) == 0));
        }
    }
}
