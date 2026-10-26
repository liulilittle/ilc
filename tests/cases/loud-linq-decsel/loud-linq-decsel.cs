//------------------------------------------------------------------------------
// loud-linq-decsel.cs -- e2e reference case for the DECIMAL-source Select with a
// VALUE result (net48 Select<decimal,TResult> and the (element,index) form).
//
// The element crosses as a 16-byte decimal cell BY VALUE; the TResult picks
// the kernel (linq_map.h):
//   Select<decimal,int>    -> il2cpp_linq_select_dec_i4
//   Select<decimal,long>   -> il2cpp_linq_select_dec_i8
//   Select<decimal,double> -> il2cpp_linq_select_dec_d
//   Select<decimal,float>  -> il2cpp_linq_select_dec_f
//   (element,index) forms  -> il2cpp_linq_select_idx_dec_i4/_i8/_d/_f/_dec
// Before this change the non-decimal results LOUD-rejected ("the decimal-
// element source surface covers a System.Decimal result only").
//
// Every line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDecSel
    {
        private static string D(decimal v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static string Join(int[] a) { return string.Join(",", a); }
        private static string JoinL(long[] a) { return string.Join(",", a); }
        private static string JoinD(double[] a) { return string.Join(",", a); }

        private static string JoinDec(decimal[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += D(a[i]);
            }
            return s;
        }

        public static void Main()
        {
            decimal[] d = { 1.5m, 2.5m, 3.5m };

            // ===== non-index overloads =====
            Console.WriteLine("[i4]=" + Join(d.Select(x => (int)x).ToArray()));
            Console.WriteLine("[i8]=" + JoinL(d.Select(x => (long)x).ToArray()));
            Console.WriteLine("[d]=" + JoinD(d.Select(x => (double)x).ToArray()));

            // ===== (element, index) overloads =====
            Console.WriteLine("[idx-i4]=" + Join(d.Select((x, i) => (int)x + i).ToArray()));
            Console.WriteLine("[idx-i8]=" + JoinL(d.Select((x, i) => (long)x + i).ToArray()));
            Console.WriteLine("[idx-d]=" + JoinD(d.Select((x, i) => (double)x + i).ToArray()));
            Console.WriteLine("[idx-dec]=" + JoinDec(d.Select((x, i) => x).ToArray()));

            // ===== a decimal result still rides the decimal kernel (regression) =====
            Console.WriteLine("[dec]=" + JoinDec(d.Select(x => x * 2m).ToArray()));

            // ===== an empty source =====
            decimal[] empty = new decimal[0];
            Console.WriteLine("[empty]=" + (empty.Select(x => (int)x).Count() == 0));

            Console.WriteLine("done");
        }
    }
}
