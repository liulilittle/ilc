// probe-grp-tolist.cs -- ToList over ONE GroupBy bucket.
//
// Baseline (main 9e7d49bf): the bucket switch had no ToList arm, so
// `bucket.ToList()` fell through to the generic ToList arm and bound
// il2cpp_linq_to_list -- which casts the GROUPING OBJECT to Il2CppArray* and
// dies with "Object is not an array." (probe-s103j2: oracle [j2] 3,3 vs
// runtime empty).
//
// Each shape is its own method: a single abort zeroes the whole probe. The
// elements are printed through get_Item (never string.Join over the List --
// the transpiler binds the ARRAY overload of string.Join, a different shape).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpToList
    {
        // ===== 1. int32 bucket (GroupBy, int key) =====
        private static void IntBucket()
        {
            int[] xs = { 3, 1, 3 };
            var g = xs.GroupBy(x => x).First();
            List<int> l = g.ToList();
            Console.WriteLine("[i4] " + l.Count + ":" + l[0] + ":" + l[1]);
        }

        // ===== 2. float bucket (GroupBy, int key) =====
        // The List<float> DECLARATION is deliberate: the transpiler tracks the
        // ToList result by the C# variable's type, so a bare List`1 name would
        // mask a wrong element domain. The runtime wrapper must materialize
        // the listobj domain (List`1_float), never the listi4 domain.
        private static void FloatBucket()
        {
            float[] xs = { 3f, 1f, 3f };
            var g = xs.GroupBy(x => (int)x).First();
            List<float> l = g.ToList();
            Console.WriteLine("[f] " + l.Count + ":" + l[0] + ":" + l[1]);
        }

        // ===== 3. string bucket (GroupBy, int key, string element) =====
        private static void RefBucket()
        {
            string[] xs = { "a", "b", "a" };
            var g = xs.GroupBy(x => x.Length).First();
            List<string> l = g.ToList();
            Console.WriteLine("[ref] " + l.Count + ":" + l[0] + ":" + l[1] + ":" + l[2]);
        }

        // ===== 4. int32 bucket read through a REFERENCE-typed slot =====
        // `object o = l[0]` makes the transpiler box the get_Item result, so a
        // wrong list domain (the listi4 domain for an int32 bucket) would be
        // observable: the boxed read yields the box payload, the raw read
        // yields the pointer's low bits.
        private static void IntBucketBoxed()
        {
            int[] xs = { 3, 1, 3 };
            var g = xs.GroupBy(x => x).First();
            List<int> l = g.ToList();
            object o0 = l[0];
            object o1 = l[1];
            Console.WriteLine("[i4box] " + l.Count + ":" + (int)o0 + ":" + (int)o1);
        }

        // ===== 5. float bucket read through a REFERENCE-typed slot =====
        private static void FloatBucketBoxed()
        {
            float[] xs = { 3f, 1f, 3f };
            var g = xs.GroupBy(x => (int)x).First();
            List<float> l = g.ToList();
            object o0 = l[0];
            object o1 = l[1];
            Console.WriteLine("[fbox] " + l.Count + ":" + (float)o0 + ":" + (float)o1);
        }

        public static void Main()
        {
            IntBucket();
            FloatBucket();
            RefBucket();
            IntBucketBoxed();
            FloatBucketBoxed();
        }
    }
}
