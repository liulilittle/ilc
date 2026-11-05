//------------------------------------------------------------------------------
// probe-widelines.cs -- wide-key OrderBy combination survey (research probe):
// iteration 2 (GREEN, byte-exact): float-source with a double key (and float
// key controls). Later iterations measured in .opencode/logs: int-source with a
// decimal key and float-source with a string key are LOUD-rejected.
//------------------------------------------------------------------------------
using System;
using System.Linq;

namespace Cases
{
    public static class ProbeWideLines
    {
        private static double KeyD(float x) { return x * 1.0; }
        private static float KeyF(float x) { return x; }

        private static string J(float[] a)
        {
            var s = "";
            for (var i = 0; i < a.Length; i++)
            {
                if (i > 0) s += ",";
                s += a[i];
            }
            return s;
        }

        public static void Main()
        {
            var src = new[] { 1.5f, 2.5f, 0.5f };
            Console.WriteLine("[f2d.static.order]=" + J(src.OrderBy(KeyD).ToArray()));
            Console.WriteLine("[f2d.static.desc]=" + J(src.OrderByDescending(KeyD).ToArray()));
            Console.WriteLine("[f2f.static.order]=" + J(src.OrderBy(KeyF).ToArray()));
            Console.WriteLine("[f2f.static.desc]=" + J(src.OrderByDescending(KeyF).ToArray()));

            var src2 = new[] { 1.5f, 2.5f, 0.5f };
            Console.WriteLine("[f2d.lambda.order]=" + J(src2.OrderBy(x => x * 1.0).ToArray()));
            var src3 = new[] { 1.5f, 2.5f, 0.5f };
            Console.WriteLine("[f2f.lambda.order]=" + J(src3.OrderBy(x => x).ToArray()));
        }
    }
}