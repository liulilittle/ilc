using System;
using System.Globalization;

//------------------------------------------------------------------------------
// convertall-fam -- Array.ConvertAll<TInput,TOutput> over the remaining family
// combinations (int64 / double / reference sources and outputs).
//
// Before this batch only (int32,int32), (string,int32), (int32,int64) and
// (int32,string) were wired; every other (TInput,TOutput) pair was
// LOUD-rejected at transpile time. The general converter template covers the
// int32 / int64 / double / reference element families.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ConvertAllFam
    {
        static int ToD(double x) { return (int)x; }
        static double DToD(double x) { return x * 2; }
        static string DToStr(double x) { return x.ToString(CultureInfo.InvariantCulture); }
        static int I8ToI4(long x) { return (int)(x / 2); }
        static long I8ToI8(long x) { return x + 1; }
        static double I8ToD(long x) { return x / 2.0; }
        static string I8ToStr(long x) { return "L" + x; }
        static int StrToI8(string s) { return s.Length; }
        static double StrToD(string s) { return s.Length + 0.25; }
        static string StrToStr(string s) { return s + "!"; }

        public static void Main()
        {
            double[] ds = { 1.5, 2.5, 3.5 };
            Console.WriteLine("[d_i4] " + string.Join(",", Array.ConvertAll(ds, ToD)));
            Console.WriteLine("[d_d] " + string.Join(",", Array.ConvertAll(ds, DToD)));
            Console.WriteLine("[d_str] " + string.Join(",", Array.ConvertAll(ds, DToStr)));

            long[] ls = { 10, 20, 30 };
            Console.WriteLine("[i8_i4] " + string.Join(",", Array.ConvertAll(ls, I8ToI4)));
            Console.WriteLine("[i8_i8] " + string.Join(",", Array.ConvertAll(ls, I8ToI8)));
            Console.WriteLine("[i8_d] " + string.Join(",", Array.ConvertAll(ls, I8ToD)));
            Console.WriteLine("[i8_str] " + string.Join(",", Array.ConvertAll(ls, I8ToStr)));

            string[] ss = { "a", "bb", "ccc" };
            Console.WriteLine("[str_i8] " + string.Join(",", Array.ConvertAll(ss, StrToI8)));
            Console.WriteLine("[str_d] " + string.Join(",", Array.ConvertAll(ss, StrToD)));
            Console.WriteLine("[str_str] " + string.Join(",", Array.ConvertAll(ss, StrToStr)));
        }
    }
}
