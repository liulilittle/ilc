using System;
using System.Collections.Generic;

//------------------------------------------------------------------------------
// zz-b4-cmp5 -- nb9 E1: primitive CompareTo(T) / CompareTo(object) for every
// numeric/char/bool owner (net48 sign verdict; NaN sorts first; a mismatched
// object argument throws ArgumentException). Written with static helpers so
// the receivers are real parameters (no closure-field carriers).
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ZZB4Cmp5
    {
        static int CmpI8(long a, long b) { return a.CompareTo(b); }
        static int CmpU64(ulong a, ulong b) { return a.CompareTo(b); }
        static int CmpU32(uint a, uint b) { return a.CompareTo(b); }
        static int CmpF8(double a, double b) { return a.CompareTo(b); }
        static int CmpF4(float a, float b) { return a.CompareTo(b); }
        static int CmpU1(byte a, byte b) { return a.CompareTo(b); }
        static int CmpI1(sbyte a, sbyte b) { return a.CompareTo(b); }
        static int CmpI2(short a, short b) { return a.CompareTo(b); }
        static int CmpU2(ushort a, ushort b) { return a.CompareTo(b); }
        static int CmpCh(char a, char b) { return a.CompareTo(b); }
        static int CmpBo(bool a, bool b) { return a.CompareTo(b); }
        static int CmpObjI8(long a, object b) { return a.CompareTo(b); }
        static int CmpObjF8(double a, object b) { return a.CompareTo(b); }
        static int CmpObjF4(float a, object b) { return a.CompareTo(b); }
        static int CmpObjU32(uint a, object b) { return a.CompareTo(b); }
        static int CmpObjU1(byte a, object b) { return a.CompareTo(b); }
        static int CmpObjCh(char a, object b) { return a.CompareTo(b); }

        static string P(Func<int> f)
        {
            try { return f().ToString(); }
            catch (Exception e) { return e.GetType().Name; }
        }

        public static void Main()
        {
            Console.WriteLine("[i8]=" + CmpI8(5, 3) + "," + CmpI8(5, 7) + "," + CmpI8(5, 5));
            Console.WriteLine("[u64]=" + CmpU64(5, 3) + "," + CmpU64(5, 7));
            Console.WriteLine("[u32]=" + CmpU32(5, 3) + "," + CmpU32(5, 0xFFFFFFFF));
            Console.WriteLine("[f8]=" + CmpF8(2.5, 1.5) + "," + CmpF8(2.5, 3.5));
            Console.WriteLine("[nan1]=" + CmpF8(double.NaN, 1.0));
            Console.WriteLine("[nan2]=" + CmpF8(1.0, double.NaN));
            Console.WriteLine("[nan3]=" + CmpF8(double.NaN, double.NaN));
            Console.WriteLine("[f4]=" + CmpF4(2.5f, 1.5f) + "," + CmpF4(2.5f, 3.5f));
            Console.WriteLine("[u1]=" + CmpU1(5, 3));
            Console.WriteLine("[i1]=" + CmpI1(-5, -7));
            Console.WriteLine("[i2]=" + CmpI2(-5, -7));
            Console.WriteLine("[u2]=" + CmpU2(5, 3));
            Console.WriteLine("[ch]=" + CmpCh('b', 'a'));
            Console.WriteLine("[bo]=" + CmpBo(true, false));

            Console.WriteLine("[i8_obj]=" + CmpObjI8(5, 7L));
            Console.WriteLine("[f8_obj]=" + CmpObjF8(2.5, 1.5));
            Console.WriteLine("[f4_obj]=" + CmpObjF4(2.5f, 3.5f));
            Console.WriteLine("[u32_obj]=" + CmpObjU32(5, 3U));
            Console.WriteLine("[u1_obj]=" + CmpObjU1(5, (byte)3));
            Console.WriteLine("[ch_obj]=" + CmpObjCh('b', 'a'));
            Console.WriteLine("[bad_obj]=" + P(() => CmpObjI8(5, "x")));

            long[] arr = { 1, 3, 2 };
            Array.Sort(arr, Comparer<long>.Create((x, y) => y.CompareTo(x)));
            Console.WriteLine("[created_cmp_sort]=" + arr[0] + "," + arr[1] + "," + arr[2]);
        }
    }
}
