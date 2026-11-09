using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeCastVal
    {
        static string J(int[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(CultureInfo.InvariantCulture); } return s; }
        static string J(long[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(CultureInfo.InvariantCulture); } return s; }
        static string J(double[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString("R", CultureInfo.InvariantCulture); } return s; }
        static string JO(object[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i] == null ? "<n>" : a[i].ToString(); } return s; }

        public static void Main()
        {
            int[] ints = new int[] { 3, 1, 2 };
            long[] longs = new long[] { 20L, 10L };
            double[] dbls = new double[] { 2.5, 1.5 };

            // A MIXED-FAMILY value source: the int32 cells are {1, 2L, 3} -- an
            // OfType<long> must keep ONLY the long cell (the family filter is
            // observable), while Cast<long> follows unbox.any and throws
            // InvalidCastException on the first int32 cell.
            object[] mixed = new object[] { 1, 2L, 3 };
            Console.WriteLine("[of.mix.l]=" + J(mixed.OfType<long>().ToArray()));
            try { new object[] { 1, 2L }.Cast<long>().ToArray(); Console.WriteLine("[cast.mix]=(none)"); }
            catch (Exception ex) { Console.WriteLine("[cast.mix]=" + ex.GetType().Name); }

            // A NARROW value source: byte[] cells box as System.Byte, so
            // OfType<int> must DROP every element (the box family differs) while
            // Cast<int> follows unbox.any and throws InvalidCastException. This
            // makes the OfType family filter observable on a value source.
            byte[] bytes = new byte[] { 1, 2 };
            Console.WriteLine("[of.byte.i4]=" + J(bytes.OfType<int>().ToArray()));
            try { bytes.Cast<int>().ToArray(); Console.WriteLine("[cast.byte]=(none)"); }
            catch (Exception ex) { Console.WriteLine("[cast.byte]=" + ex.GetType().Name); }
            Console.WriteLine("[of.byte.b]=" + bytes.OfType<byte>().Count().ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("[of.i4]=" + J(ints.OfType<int>().ToArray()));
            Console.WriteLine("[of.l]=" + J(longs.OfType<long>().ToArray()));
            Console.WriteLine("[of.d]=" + J(dbls.OfType<double>().ToArray()));
            Console.WriteLine("[of.obj]=" + JO(ints.OfType<object>().ToArray()));
            Console.WriteLine("[cast.i4]=" + J(ints.Cast<int>().ToArray()));
            Console.WriteLine("[cast.l]=" + J(longs.Cast<long>().ToArray()));
            Console.WriteLine("[cast.d]=" + J(dbls.Cast<double>().ToArray()));
            Console.WriteLine("[cast.obj]=" + JO(ints.Cast<object>().ToArray()));
            Console.WriteLine("[of.count]=" + ints.OfType<int>().Count().ToString(CultureInfo.InvariantCulture));
        }
    }
}
