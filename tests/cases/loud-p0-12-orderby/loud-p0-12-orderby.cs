using System;
using System.Collections.Generic;
using System.Linq;

// B5-R2: OrderBy / OrderByDescending / ThenBy / ThenByDescending over
// REFERENCE-element sources (string[] and List<string>), plus the int32
// ThenByDescending path that was never wired.
//
// The runtime has key-SELECTOR entries for the int32 family
// (il2cpp_linq_order_by_key_i4 / _desc / then_by_key_i4 / then_by_key_desc_i4)
// and, for reference sources, only the older Comparison<T>-style
// il2cpp_linq_order_by_ref / il2cpp_linq_order_by_string. Enumerable.OrderBy
// passes a 1-arg Func<TSource,TKey>, so the reference path needs its own
// key-selector entries -- passing a key selector to the 2-arg comparator
// entry would call a 1-arg function with 2 args (UB).
//
// Callbacks are INSTANCE-BOUND delegates (the transpiler has no lambdas).
namespace Cases
{
    public sealed class OrderKeys
    {
        public int Key(string s) { return s.Length; }
        public string Self(string s) { return s; }
        public int IntKey(int v) { return v; }
        public int IntKey2(int v) { return v % 2; }
    }

    public static class LoudP012OrderBy
    {
        static string Join(string[] a)
        {
            return string.Join(",", a);
        }

        static string JoinInt(int[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        public static void Main()
        {
            OrderKeys k = new OrderKeys();
            Func<string, int> key = k.Key;
            Func<string, string> self = k.Self;
            Func<int, int> intKey = k.IntKey;
            Func<int, int> intKey2 = k.IntKey2;

            // ===== OrderBy over a reference source =====
            string[] words = { "dddd", "a", "ccc", "bb" };
            Console.WriteLine("[orderby-str]=" + Join(words.OrderBy(key).ToArray()));
            Console.WriteLine("[orderby-str-src-unchanged]=" + Join(words));

            // A key selector that returns the element itself (string key).
            // NOTE: single-case inputs only -- net48 Comparer<string>.Default is
            // CULTURE-sensitive (it sorts "a" before "A"); this runtime's string
            // ordering is ordinal (the documented ASCII-domain deviation, see
            // .opencode/tmp/b5-gaps.md).
            string[] mixed = { "d", "b", "a", "c" };
            Console.WriteLine("[orderby-str-self]=" + Join(mixed.OrderBy(self).ToArray()));

            Console.WriteLine("[orderby-str-stable]=" +
                Join(new string[] { "bb", "aa", "cc", "dd" }.OrderBy(key).ToArray()));
            Console.WriteLine("[orderby-str-empty]=" + new string[0].OrderBy(key).ToArray().Length);
            Console.WriteLine("[orderby-str-one]=" + Join(new string[] { "x" }.OrderBy(key).ToArray()));

            // ===== OrderByDescending over a reference source =====
            Console.WriteLine("[orderbydesc-str]=" + Join(words.OrderByDescending(key).ToArray()));

            // ===== OrderBy over a List<string> =====
            List<string> list = new List<string> { "dddd", "a", "ccc", "bb" };
            Console.WriteLine("[orderby-list]=" + Join(list.OrderBy(key).ToArray()));
            Console.WriteLine("[orderbydesc-list]=" + Join(list.OrderByDescending(key).ToArray()));
            Console.WriteLine("[orderby-list-src-unchanged]=" + Join(list.ToArray()));

            // ===== ThenBy / ThenByDescending over int32 (the unwired DESC path) =====
            int[] nums = { 3, 1, 2, 3, 1, 5 };
            Console.WriteLine("[thenby-int]=" + JoinInt(nums.OrderBy(intKey).ThenBy(intKey2).ToArray()));
            Console.WriteLine("[thenbydesc-int]=" + JoinInt(nums.OrderBy(intKey).ThenByDescending(intKey2).ToArray()));
            int[] nums2 = { 1, 3, 5, 5, 1, 3 };
            Console.WriteLine("[thenbydesc-int2]=" + JoinInt(nums2.OrderBy(intKey).ThenByDescending(intKey2).ToArray()));
        }
    }
}
