using System;
using System.Collections.Generic;

namespace Cases
{
    public sealed class FCmp : IComparer<float>
    {
        public int Compare(float a, float b) { return a.CompareTo(b); }
    }

    public static class Probe5
    {
        static string J(float[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; i++) s += (i == 0 ? "" : ",") + a[i].ToString();
            return s;
        }

        public static void Main()
        {
            try
            {
                float[] f = { 3f, 1f, 2f };
                Console.WriteLine("[raw]=" + J(f));
                Array.Sort(f);
                Console.WriteLine("[sort]=" + J(f));
                Console.WriteLine("[join]=" + string.Join(",", f));
                double[] d = { 3, 1, 2 };
                Array.Sort(d);
                Console.WriteLine("[djoin]=" + string.Join(",", d));

                float[] g = { 3f, 1f, 2f };
                Array.Sort(g, new FCmp());
                Console.WriteLine("[fcmp]=" + J(g));
                float[] h = { 5f, 4f, 3f, 2f, 1f };
                Array.Sort(h, 1, 3, new FCmp());
                Console.WriteLine("[fcmp_range]=" + J(h));
                float[] k = { 3f, 1f, 2f };
                Array.Sort(k, Comparer<float>.Create((a, b) => a.CompareTo(b)));
                Console.WriteLine("[fcreated]=" + J(k));

                string[] rs = { "c", "a", "b" };
                Array.Reverse((Array)rs);
                Console.WriteLine("[rev_str]=" + string.Join(",", rs));
                long[] rl = { 1, 2, 3 };
                Array.Reverse((Array)rl, 0, 3);
                Console.WriteLine("[rev_range_long]=" + string.Join(",", rl));
            }
            catch (Exception e) { Console.WriteLine("EX=" + e.GetType().Name); }
        }
    }
}
