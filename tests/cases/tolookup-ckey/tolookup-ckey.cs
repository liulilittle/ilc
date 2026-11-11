using System;
using System.Collections.Generic;
using System.Linq;

// Section 111: Enumerable.ToLookup with a char TKey.
// Char (0x05) rides the int32 keyInt slot (slot 0) with dictgen kind "1"
// (int32 hash/equals).
// net48 oracle:
//   new[]{'a','b','b','c','c','c'}.ToLookup(k => k, v => (int)v)
//   Distinct keys (first-appearance order): 'a','b','c' => Count=3
//   lookup['a'] -> [1]; lookup['b'] -> [2,2]; lookup['c'] -> [3,3,3]
//   lookup.Contains('b')=true, lookup.Contains('z')=false
//   lookup['z'] -> empty sequence
//   foreach preserves source order within each group
namespace Cases {
    public static class TolookupCharKey {
        public static void Main() {
            var src = new[] { 'a', 'b', 'b', 'c', 'c', 'c' };
            var lk = src.ToLookup(k => k, v => (int)v);
            Console.Write("count=" + lk.Count);
            Console.Write(" cb=" + lk.Contains('b'));
            Console.Write(" cz=" + lk.Contains('z'));
            Console.Write(" ga=" + string.Join(",", lk['a']));
            Console.Write(" gb=" + string.Join(",", lk['b']));
            Console.Write(" gc=" + string.Join(",", lk['c']));
            Console.Write(" gz=" + string.Join(",", lk['z']));
            Console.WriteLine();
        }
    }
}
