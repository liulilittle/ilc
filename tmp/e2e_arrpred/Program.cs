//------------------------------------------------------------------------------
// e2e_arrpred -- System.Array predicate + mutation surface, net9 refs pipeline
// (csc vs net9 runtime refs -> il2cppc transpiler -> MinGW -> own runtime).
//
// Groups:
//   A. Find<int> / FindAll<int> / Exists<int> / TrueForAll<int> with lambdas
//   B. Sort (1-arg + Comparison<int> lambda), BinarySearch, IndexOf,
//      Reverse, Clear, Copy (5-arg), Resize
//   C. Array.Empty<string>().Length == 0
//   D. GetLength / GetLowerBound / GetUpperBound
//
// Every check prints "PASS <name>" or "FAIL <name>"; the program ends with
// ALL-PASS (exit 0) or ALL-FAIL (exit 1).
//------------------------------------------------------------------------------
using System;

namespace E2eArrPred
{
    public static class Program
    {
        private static int failures;

        private static void Check(string name, bool ok)
        {
            if (ok) Console.WriteLine("PASS " + name);
            else { Console.WriteLine("FAIL " + name); failures++; }
        }

        public static int Main()
        {
            // ============ A. predicate family with lambdas ============
            int[] a = { 10, 20, 30, 20, 40 };
            int[] odd = { 1, 3, 5 };
            int[] empty = new int[0];

            int f = Array.Find(a, x => x == 20);
            Check("find_hit", f == 20);

            int f2 = Array.Find(a, x => x > 100);
            Check("find_miss_default0", f2 == 0);

            int f3 = Array.Find(empty, x => x == 1);
            Check("find_empty_default0", f3 == 0);

            int[] fa = Array.FindAll(a, x => x > 15);
            Check("findall_len", fa.Length == 4);
            Check("findall_vals",
                fa[0] == 20 && fa[1] == 30 && fa[2] == 20 && fa[3] == 40);

            int[] fa2 = Array.FindAll(odd, x => x > 100);
            Check("findall_none_empty", fa2.Length == 0);

            Check("exists_true", Array.Exists(a, x => x == 30));
            Check("exists_false", !Array.Exists(odd, x => x % 2 == 0));
            Check("exists_empty", !Array.Exists(empty, x => true));

            Check("trueforall_true", Array.TrueForAll(odd, x => x % 2 == 1));
            Check("trueforall_false", !Array.TrueForAll(a, x => x > 15));
            Check("trueforall_empty_vacuous", Array.TrueForAll(empty, x => false));

            // ============ B. Sort / BinarySearch / IndexOf / Reverse /
            //                 Clear / Copy / Resize ============
            int[] s = { 40, 10, 30, 20 };
            Array.Sort(s);
            Check("sort_1arg", s[0] == 10 && s[1] == 20 && s[2] == 30 && s[3] == 40);

            int[] s2 = { 4, 2, 3, 1 };
            Array.Sort(s2, (x, y) => y - x);   // descending via Comparison lambda
            Check("sort_comparison_lambda",
                s2[0] == 4 && s2[1] == 3 && s2[2] == 2 && s2[3] == 1);

            int idx = Array.BinarySearch(s, 30);          // sorted ascending
            Check("binarysearch_hit", idx == 2);

            int idx2 = Array.BinarySearch(s, 25);
            Check("binarysearch_miss_negative", idx2 < 0);

            int io = Array.IndexOf(a, 20);                // a = {10,20,30,20,40}
            Check("indexof_first", io == 1);

            int io2 = Array.IndexOf(a, 99);
            Check("indexof_miss", io2 == -1);

            int[] r = { 1, 2, 3, 4, 5 };
            Array.Reverse(r);
            Check("reverse_full", r[0] == 5 && r[2] == 3 && r[4] == 1);

            int[] r2 = { 1, 2, 3, 4, 5 };
            Array.Reverse(r2, 1, 3);                      // reverse [2,3,4]
            Check("reverse_range",
                r2[0] == 1 && r2[1] == 4 && r2[2] == 3 && r2[3] == 2 && r2[4] == 5);

            int[] c = { 9, 8, 7, 6 };
            Array.Clear(c, 1, 2);
            Check("clear_range", c[0] == 9 && c[1] == 0 && c[2] == 0 && c[3] == 6);

            int[] src = { 10, 20, 30, 40, 50 };
            int[] dst = new int[5];
            Array.Copy(src, 1, dst, 0, 4);                // 5-arg form only
            Check("copy_window",
                dst[0] == 20 && dst[1] == 30 && dst[2] == 40 && dst[3] == 50 && dst[4] == 0);

            int[] rs = new int[3];
            Array.Resize(ref rs, 5);
            Check("resize_grow", rs.Length == 5);

            int[] rs2 = { 1, 2, 3, 4, 5 };
            Array.Resize(ref rs2, 2);
            Check("resize_shrink", rs2.Length == 2 && rs2[0] == 1 && rs2[1] == 2);

            // ============ C. Array.Empty<string>() ============
            string[] es = Array.Empty<string>();
            Check("empty_string_len0", es != null && es.Length == 0);

            // ============ D. GetLength / GetLowerBound / GetUpperBound ====
            int[] g = { 7, 8, 9 };
            Check("getlength", g.GetLength(0) == 3);
            Check("getlowerbound", g.GetLowerBound(0) == 0);
            Check("getupperbound", g.GetUpperBound(0) == 2);
            Check("empty_getlength", empty.GetLength(0) == 0);
            Check("empty_getupperbound", empty.GetUpperBound(0) == -1);

            if (failures == 0)
            {
                Console.WriteLine("ALL-PASS");
                return 0;
            }
            Console.WriteLine("ALL-FAIL failures=" + failures);
            return 1;
        }
    }
}
