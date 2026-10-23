using System;
using System.Globalization;

// pdtx1 -- DateTime.ParseExact / TryParseExact OVERLOAD MATRIX (theme A1).
// Every probe prints a bool or a culture-independent value: no
// culture-dependent text output. InvariantCulture throughout.
//
// This case pins the 4-arg (styles) overloads and the string[] format-array
// overloads that the transpiler did not wire before (TimeExt2 only mapped
// arity 2/3; DateTimeExtra LOUD-rejected TryParseExact).
namespace Cases
{
    public static class PDtx1
    {
        static string Px(string s, string f, DateTimeStyles st)
        {
            try
            {
                DateTime d = DateTime.ParseExact(s, f, CultureInfo.InvariantCulture, st);
                return d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        static string Pxa(string s, string[] f, DateTimeStyles st)
        {
            try
            {
                DateTime d = DateTime.ParseExact(s, f, CultureInfo.InvariantCulture, st);
                return d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        static string Tx(string s, string f, DateTimeStyles st)
        {
            try
            {
                DateTime d;
                bool ok = DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, st, out d);
                return ok + "|" + d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        static string Txa(string s, string[] f, DateTimeStyles st)
        {
            try
            {
                DateTime d;
                bool ok = DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, st, out d);
                return ok + "|" + d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception e) { return "<" + e.GetType().Name + ">"; }
        }

        public static void Main()
        {
            Console.WriteLine("px4a=" + Px("2024-06-15", "yyyy-MM-dd", DateTimeStyles.None));
            Console.WriteLine("px4b=" + Px("2024-06-15 13:45:30", "yyyy-MM-dd HH:mm:ss", DateTimeStyles.None));
            Console.WriteLine("px4c=" + Px("24", "yy", DateTimeStyles.None));
            Console.WriteLine("px4d=" + Px("49", "yy", DateTimeStyles.None));
            Console.WriteLine("px4e=" + Px("50", "yy", DateTimeStyles.None));
            Console.WriteLine("px4f=" + Px("2024-06-15", "yyyy-MM-dd", DateTimeStyles.NoCurrentDateDefault));
            Console.WriteLine("px4g=" + Px("2024-06-15", "yyyy-MM-dd", DateTimeStyles.RoundtripKind));

            Console.WriteLine("pxa1=" + Pxa("2024-06-15", new string[] { "yyyyMMdd", "yyyy-MM-dd" }, DateTimeStyles.None));
            Console.WriteLine("pxa2=" + Pxa("20240615", new string[] { "yyyyMMdd", "yyyy-MM-dd" }, DateTimeStyles.None));
            Console.WriteLine("pxa3=" + Pxa("2024/06/15", new string[] { "yyyyMMdd", "yyyy/MM/dd" }, DateTimeStyles.None));
            Console.WriteLine("pxa4=" + Pxa("bad", new string[] { "yyyyMMdd", "yyyy-MM-dd" }, DateTimeStyles.None));
            Console.WriteLine("pxa5=" + Pxa("2024-06-15", new string[0], DateTimeStyles.None));
            Console.WriteLine("pxa6=" + Pxa("2024-06-15", (string[])null, DateTimeStyles.None));

            Console.WriteLine("tx4a=" + Tx("2024-06-15", "yyyy-MM-dd", DateTimeStyles.None));
            Console.WriteLine("tx4b=" + Tx("bad", "yyyy-MM-dd", DateTimeStyles.None));
            Console.WriteLine("tx4c=" + Tx(null, "yyyy-MM-dd", DateTimeStyles.None));
            Console.WriteLine("tx4d=" + Tx("2024", null, DateTimeStyles.None));
            Console.WriteLine("tx4e=" + Tx("2024", "", DateTimeStyles.None));
            Console.WriteLine("tx4f=" + Tx("2024-06-15", "yyyy-MM-dd", DateTimeStyles.AllowWhiteSpaces));
            Console.WriteLine("tx4g=" + Tx(" 2024-06-15 ", "yyyy-MM-dd", DateTimeStyles.AllowWhiteSpaces));

            Console.WriteLine("txa1=" + Txa("20240615", new string[] { "yyyyMMdd", "yyyy-MM-dd" }, DateTimeStyles.None));
            Console.WriteLine("txa2=" + Txa("bad", new string[] { "yyyyMMdd" }, DateTimeStyles.None));
            Console.WriteLine("txa3=" + Txa("2024-06-15", new string[] { null }, DateTimeStyles.None));
            Console.WriteLine("txa4=" + Txa("2024-06-15", new string[0], DateTimeStyles.None));
            Console.WriteLine("txa5=" + Txa("2024-06-15", (string[])null, DateTimeStyles.None));
        }
    }
}
