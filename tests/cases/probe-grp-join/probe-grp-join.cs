// probe-grp-join.cs -- string.Join over ONE GroupBy bucket (IEnumerable<T>).
//
// Baseline (main ed80f028): the Join arm bound icall_string_join (the ARRAY
// overload), which casts the GROUPING OBJECT to Il2CppArray* and answers an
// empty join (probe-s100r4: oracle [r4] 3,3 vs runtime empty).
//
// Each shape is its own method: a single abort zeroes the whole probe.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpJoin
    {
        // ===== 1. int32 bucket (GroupBy, int key) =====
        private static void IntBucket()
        {
            int[] xs = { 3, 1, 3 };
            var g = xs.GroupBy(x => x).First();
            Console.WriteLine("[i4] " + string.Join(",", g));
        }

        // ===== 2. float bucket (GroupBy, int key) =====
        private static void FloatBucket()
        {
            float[] xs = { 3f, 1f, 3f };
            var g = xs.GroupBy(x => (int)x).First();
            Console.WriteLine("[f] " + string.Join(",", g));
        }

        // ===== 3. string bucket (GroupBy, int key, string element) =====
        private static void RefBucket()
        {
            string[] xs = { "a", "b", "a" };
            var g = xs.GroupBy(x => x.Length).First();
            Console.WriteLine("[ref] " + string.Join(",", g));
        }

        // ===== 4. multi-char separator =====
        private static void MultiSep()
        {
            int[] xs = { 3, 1, 3 };
            var g = xs.GroupBy(x => x).First();
            Console.WriteLine("[sep] " + string.Join("--", g));
        }

        public static void Main()
        {
            IntBucket();
            FloatBucket();
            RefBucket();
            MultiSep();
        }
    }
}
