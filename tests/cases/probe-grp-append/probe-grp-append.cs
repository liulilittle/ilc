// probe-grp-append.cs -- Append / Prepend over a GroupBy bucket.
//
// Baseline (main e0e3e9f8): the bucket switch has no Append/Prepend arm, so
// either the call is LOUD (the floatBucket / valueBucket gates) or it falls
// through to the array kernels and casts the GROUPING OBJECT to Il2CppArray*
// (a loud element-family mismatch / a wrong result).
//
// Each shape is its own method: a single LOUD abort zeroes the whole probe.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeGrpAppend
    {
        private static string FB(float v)
        {
            return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ===== 1. Append over an int32 bucket =====
        private static void AppendInt()
        {
            var g = new[] { 3, 1, 3 }.GroupBy(x => x).First();
            Console.WriteLine("[appI4] " + string.Join(",", g.Append(9)));
        }

        // ===== 2. Prepend over an int32 bucket =====
        private static void PrependInt()
        {
            var g = new[] { 3, 1, 3 }.GroupBy(x => x).First();
            Console.WriteLine("[preI4] " + string.Join(",", g.Prepend(9)));
        }

        // ===== 3. Append over a float bucket =====
        private static void AppendFloat()
        {
            var g = new[] { 3f, 1f, 3f }.GroupBy(x => (int)x).First();
            Console.WriteLine("[appF] " + string.Join(",", g.Append(9f).Select(v => FB(v))));
        }

        // ===== 4. Prepend over a float bucket =====
        private static void PrependFloat()
        {
            var g = new[] { 3f, 1f, 3f }.GroupBy(x => (int)x).First();
            Console.WriteLine("[preF] " + string.Join(",", g.Prepend(9f).Select(v => FB(v))));
        }

        // ===== 5. Append over a string bucket =====
        private static void AppendString()
        {
            var g = new[] { "a", "b", "a" }.GroupBy(x => x).First();
            Console.WriteLine("[appS] " + string.Join(",", g.Append("z")));
        }

        public static void Main()
        {
            AppendInt();
            PrependInt();
            AppendFloat();
            PrependFloat();
            AppendString();
        }
    }
}
