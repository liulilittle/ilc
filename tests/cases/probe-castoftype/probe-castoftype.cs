using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeCastOfType
    {
        static string J(int[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(CultureInfo.InvariantCulture); } return s; }
        static string J(long[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(CultureInfo.InvariantCulture); } return s; }
        static string J(double[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString("R", CultureInfo.InvariantCulture); } return s; }
        static string J(bool[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i] ? "T" : "F"; } return s; }
        static string J(char[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(); } return s; }
        static string J(decimal[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString(CultureInfo.InvariantCulture); } return s; }
        static string J(string[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i] == null ? "<n>" : a[i]; } return s; }
        static string J(DateTime[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString("yyyyMMdd", CultureInfo.InvariantCulture); } return s; }
        static string J(Guid[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i].ToString("N"); } return s; }
        static string JO(object[] a) { string s = ""; for (int i = 0; i < a.Length; ++i) { if (i > 0) s += ","; s += a[i] == null ? "<n>" : a[i].ToString(); } return s; }

        public static void Main()
        {
            object[] objs = new object[]
            {
                1, "a", 2L, 4.5, true, 'x', 9m,
                new DateTime(2020, 1, 2), new Guid("11111111-2222-3333-4444-555555555555"),
                null, "b", 2,
            };

            Console.WriteLine("[of.i4]=" + J(objs.OfType<int>().ToArray()));
            Console.WriteLine("[of.str]=" + J(objs.OfType<string>().ToArray()));
            Console.WriteLine("[of.obj]=" + JO(objs.OfType<object>().ToArray()));
            Console.WriteLine("[of.l]=" + J(objs.OfType<long>().ToArray()));
            Console.WriteLine("[of.d]=" + J(objs.OfType<double>().ToArray()));
            Console.WriteLine("[of.bool]=" + J(objs.OfType<bool>().ToArray()));
            Console.WriteLine("[of.char]=" + J(objs.OfType<char>().ToArray()));
            Console.WriteLine("[of.dec]=" + J(objs.OfType<decimal>().ToArray()));
            Console.WriteLine("[of.dt]=" + J(objs.OfType<DateTime>().ToArray()));
            Console.WriteLine("[of.guid]=" + J(objs.OfType<Guid>().ToArray()));
            Console.WriteLine("[of.count]=" + objs.OfType<int>().Count().ToString(CultureInfo.InvariantCulture));

            object[] ints = new object[] { 3, 1, 2 };
            Console.WriteLine("[cast.i4]=" + J(ints.Cast<int>().ToArray()));
            object[] strs = new object[] { "a", null, "b" };
            Console.WriteLine("[cast.str]=" + J(strs.Cast<string>().ToArray()));
            object[] mix = new object[] { 1, "a", null };
            Console.WriteLine("[cast.obj]=" + JO(mix.Cast<object>().ToArray()));

            try { new object[] { 1, "x" }.Cast<int>().ToArray(); Console.WriteLine("[cast.bad]=(none)"); }
            catch (Exception ex) { Console.WriteLine("[cast.bad]=" + ex.GetType().Name); }
            try { new object[] { 1, null }.Cast<int>().ToArray(); Console.WriteLine("[cast.null]=(none)"); }
            catch (Exception ex) { Console.WriteLine("[cast.null]=" + ex.GetType().Name); }
            try { new object[] { "a" }.Cast<string>().ToArray(); Console.WriteLine("[cast.ok]=(ok)"); }
            catch (Exception ex) { Console.WriteLine("[cast.ok]=" + ex.GetType().Name); }
        }
    }
}