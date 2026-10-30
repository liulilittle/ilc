// audit-fix3-d2d -- D2 spot checks the fix's own probe does NOT cover:
//   * DefaultIfEmpty(source, defaultValue) over NARROW element families
//     (sbyte/byte/short/ushort/char/bool/uint) for ARRAY and LIST sources --
//     the real-cell-width write (1/2-byte cells) is the risk.
//   * A few non-diagonal (acc, elem, result) triples of the 216-entry matrix.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class AuditD2d
    {
        public static void Main()
        {
            sbyte[] sa = new sbyte[0];
            Console.WriteLine("[die_arr_i1]=" + sa.DefaultIfEmpty((sbyte)-7).First().ToString(CultureInfo.InvariantCulture));
            byte[] ba = new byte[0];
            Console.WriteLine("[die_arr_u1]=" + ba.DefaultIfEmpty((byte)200).First().ToString(CultureInfo.InvariantCulture));
            short[] s2 = new short[0];
            Console.WriteLine("[die_arr_i2]=" + s2.DefaultIfEmpty((short)-30000).First().ToString(CultureInfo.InvariantCulture));
            ushort[] u2 = new ushort[0];
            Console.WriteLine("[die_arr_u2]=" + u2.DefaultIfEmpty((ushort)60000).First().ToString(CultureInfo.InvariantCulture));
            char[] ca = new char[0];
            Console.WriteLine("[die_arr_c]=" + (int)ca.DefaultIfEmpty('Z').First());
            bool[] boa = new bool[0];
            Console.WriteLine("[die_arr_b]=" + boa.DefaultIfEmpty(true).First());
            uint[] ua = new uint[0];
            Console.WriteLine("[die_arr_u4]=" + ua.DefaultIfEmpty(4000000000u).First().ToString(CultureInfo.InvariantCulture));

            List<sbyte> lsb = new List<sbyte>();
            Console.WriteLine("[die_list_i1]=" + lsb.DefaultIfEmpty((sbyte)-7).First().ToString(CultureInfo.InvariantCulture));
            List<short> ls2 = new List<short>();
            Console.WriteLine("[die_list_i2]=" + ls2.DefaultIfEmpty((short)-30000).First().ToString(CultureInfo.InvariantCulture));
            List<char> lc = new List<char>();
            Console.WriteLine("[die_list_c]=" + (int)lc.DefaultIfEmpty('Z').First());
            List<uint> lu = new List<uint>();
            Console.WriteLine("[die_list_u4]=" + lu.DefaultIfEmpty(4000000000u).First().ToString(CultureInfo.InvariantCulture));

            List<int> li = new List<int> { 1, 2, 3 };
            List<long> ll = new List<long> { 5L, 6L };
            List<double> ld = new List<double> { 0.5, 1.5 };
            List<string> ls = new List<string> { "a", "bb" };

            // non-diagonal triples
            Console.WriteLine("[rs_i4_f_i8]=" + li.Aggregate(0f, (a, x) => a + x, a => (long)a * 3L));
            Console.WriteLine("[rs_i8_ref_ref]=" + ll.Aggregate("", (a, x) => a + x.ToString(CultureInfo.InvariantCulture),
                a => "{" + a + "}"));
            Console.WriteLine("[rs_d_i4_i8]=" + ld.Aggregate(0, (a, x) => a + (int)(x * 2), a => (long)a * 1000000L));
            Console.WriteLine("[rs_ref_d_f]=" + ls.Aggregate(0.0, (a, x) => a + x.Length, a => (float)a / 4f)
                .ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("[rs_f_i8_d]=" + ll.Aggregate(0f, (a, x) => a + x, a => (double)a * 0.5)
                .ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
