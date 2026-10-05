//------------------------------------------------------------------------------
// hashsetdeep.cs -- HashSet DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C73; extended in hset-b23 (D-HSET-1).
//
// ** System.Collections.Generic.HashSet`1<string> IS NOW WIRED ** (hset-b23:
// runtime hashset.h -- a set-of-unique-members GC-object wrapper over the
// dictsi domain; MethodMap.HashSet.cs maps the members to icall_hashset_string_*).
//
// The case tests the FULL set surface through two representations:
//
//   Section A -- SET MEMBERSHIP via Dictionary<string,int> as a set (the
//     dictsi domain, MethodMap.Dictionary.cs): a HashSet is a set of unique
//     members with Add (no-dup), Contains, Remove, Count, Clear. The dict
//     indexer set (insert-or-replace) gives Add's idempotence: re-inserting
//     an existing member keeps the count. Keys are content-equal (two
//     distinct string instances with equal content are the SAME member --
//     the ordinal content-equality contract), case-distinct keys are
//     DIFFERENT members.
//
//   Section B -- SET ALGEBRA via System.Linq (i4 family, MethodMap.Linq.cs):
//     Distinct  = the set-dedup contract (Add never duplicates; first-
//                 appearance order kept)
//     Except    = set difference (members of source not in second)
//     Concat    = multiset union (duplicates preserved)
//     plus chains Distinct->Except, Except->Concat, Concat->Distinct.
//
//   Section C (D-HSET-1, hset-b23) -- the DIRECT HashSet<string> surface:
//     Add/Contains/Remove/Count/Clear asserted against a REAL HashSet<string>
//     (Add returns false for a duplicate, never throws; ordinal content
//     equality; case/prefix distinctness; Clear + reuse). Net48 is the byte
//     reference for every line.
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

class Program
{
    static void Main()
    {
        // ===== Section A: set membership via Dictionary-as-set =====
        Dictionary<string, int> set = new Dictionary<string, int>();

        set["alpha"] = 1;
        set["beta"] = 2;
        Console.WriteLine("s_count2=" + (set.Count == 2));
        Console.WriteLine("s_contains_alpha=" + set.ContainsKey("alpha"));
        Console.WriteLine("s_contains_beta=" + set.ContainsKey("beta"));
        Console.WriteLine("s_contains_gamma=" + (set.ContainsKey("gamma") == false));

        set["alpha"] = 1;                            // re-insert: membership no-op
        Console.WriteLine("s_reinsert_count=" + (set.Count == 2));
        Console.WriteLine("s_reinsert_alpha=" + set.ContainsKey("alpha"));

        string keyA = "alpha";
        string keyB = "al" + "pha";                  // distinct instance, equal content
        Console.WriteLine("s_content_same=" + set.ContainsKey(keyB));
        Console.WriteLine("s_content_refdiff=" + (!ReferenceEquals(keyA, keyB)));

        Console.WriteLine("s_case_distinct=" + (set.ContainsKey("ALPHA") == false));
        Console.WriteLine("s_prefix_distinct=" + (set.ContainsKey("alphabet") == false));

        Console.WriteLine("s_value=" + (set["beta"] == 2));
        Console.WriteLine("s_value_alpha=" + (set["alpha"] == 1));

        Console.WriteLine("s_remove_hit=" + set.Remove("beta"));
        Console.WriteLine("s_remove_miss=" + (set.Remove("gamma") == false));
        Console.WriteLine("s_after_remove=" + (set.Count == 1));
        Console.WriteLine("s_removed_gone=" + (set.ContainsKey("beta") == false));

        set.Clear();
        Console.WriteLine("s_after_clear=" + (set.Count == 0));
        Console.WriteLine("s_cleared_gone=" + (set.ContainsKey("alpha") == false));
        set["new"] = 9;
        Console.WriteLine("s_reuse=" + set.ContainsKey("new"));
        Console.WriteLine("s_reuse_count=" + (set.Count == 1));

        // ===== Section B: set algebra via LINQ (i4) =====
        int[] src = { 3, 1, 2, 3, 1, 4, 2 };
        int[] second = { 2, 4, 9 };
        int[] empty = new int[0];

        // Distinct -- dedup keeping first appearance (the set-add no-dup contract)
        int[] d = src.Distinct().ToArray();
        Console.WriteLine("d_len=" + (d.Length == 4));
        Console.WriteLine("d_order=" + (d[0] == 3 && d[1] == 1 && d[2] == 2 && d[3] == 4));

        int[] alleq = { 7, 7, 7, 7 };
        Console.WriteLine("d_alleq=" + (alleq.Distinct().Count() == 1));
        int[] alldist = { 9, 8, 7 };
        Console.WriteLine("d_alldist=" + (alldist.Distinct().Count() == 3));
        int[] single = { 42 };
        Console.WriteLine("d_single=" + (single.Distinct().Count() == 1));
        Console.WriteLine("d_empty=" + (empty.Distinct().Count() == 0));

        // Except -- set difference, first-appearance order, deduplicated output
        int[] e = src.Except(second).ToArray();
        Console.WriteLine("e_len=" + (e.Length == 2));
        Console.WriteLine("e_members=" + (e[0] == 3 && e[1] == 1));
        Console.WriteLine("e_self=" + (src.Except(src).Count() == 0));

        int[] disjoint = { 8, 5 };
        Console.WriteLine("e_disjoint=" + (src.Except(disjoint).Count() == 4));
        Console.WriteLine("e_empty_src=" + (empty.Except(second).Count() == 0));
        Console.WriteLine("e_empty_second=" + (src.Except(empty).Count() == 4));

        // Concat -- multiset union, order and duplicates preserved
        int[] c = src.Concat(second).ToArray();
        Console.WriteLine("c_len=" + (c.Length == 10));
        Console.WriteLine("c_head=" + (c[0] == 3 && c[6] == 2 && c[9] == 9));
        Console.WriteLine("c_empty_l=" + (empty.Concat(second).Count() == 3));
        Console.WriteLine("c_empty_r=" + (src.Concat(empty).Count() == 7));

        // Chains
        Console.WriteLine("chain_de=" + (src.Distinct().Except(second).Count() == 2));
        Console.WriteLine("chain_ec=" + (src.Except(second).Concat(new int[] { 1, 3 }).Count() == 4));
        Console.WriteLine("chain_dc=" + (src.Concat(src).Distinct().Count() == 4));
        Console.WriteLine("chain_all=" + (src.Concat(second).Distinct().Except(new int[] { 9 }).Count() == 4));

        // ===== Section C (D-HSET-1, hset-b23): the DIRECT HashSet<string>
        // surface -- now wired (hashset.h set-of-unique-members wrapper over
        // the dictsi domain, MethodMap.HashSet.cs). The same membership
        // contract as Section A, asserted against a REAL HashSet<string>.
        HashSet<string> hs = new HashSet<string>();
        Console.WriteLine("hs_fresh_count=" + (hs.Count == 0));
        Console.WriteLine("hs_add1=" + hs.Add("alpha"));
        Console.WriteLine("hs_add2=" + (hs.Add("alpha") == false));
        Console.WriteLine("hs_count_nodup=" + (hs.Count == 1));
        Console.WriteLine("hs_contains_alpha=" + hs.Contains("alpha"));
        Console.WriteLine("hs_contains_beta=" + (hs.Contains("beta") == false));
        Console.WriteLine("hs_contains_gamma=" + (hs.Contains("gamma") == false));
        Console.WriteLine("hs_content_same=" + hs.Contains("al" + "pha"));
        Console.WriteLine("hs_case_distinct=" + (hs.Contains("ALPHA") == false));
        Console.WriteLine("hs_prefix_distinct=" + (hs.Contains("alphabet") == false));
        hs.Add("beta");
        Console.WriteLine("hs_count2b=" + (hs.Count == 2));
        Console.WriteLine("hs_remove_hit=" + hs.Remove("alpha"));
        Console.WriteLine("hs_remove_miss=" + (hs.Remove("gamma") == false));
        Console.WriteLine("hs_after_remove=" + (hs.Count == 1));
        Console.WriteLine("hs_removed_gone=" + (hs.Contains("alpha") == false));
        hs.Clear();
        Console.WriteLine("hs_after_clear=" + (hs.Count == 0));
        Console.WriteLine("hs_cleared_gone=" + (hs.Contains("beta") == false));
        hs.Add("new");
        Console.WriteLine("hs_reuse=" + hs.Contains("new"));
        Console.WriteLine("hs_reuse_count=" + (hs.Count == 1));
    }
}
