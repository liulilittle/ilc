//------------------------------------------------------------------------------
// probe-listselnum -- List<T>.Select over the NUMERIC element families.
//
// The List<T> select kernel (src/runtime/src/linq_list.cpp
// il2cpp_linq_list_select) read an int64 / float / double element as
// "static_cast<int32_t>(ReadI8/ReadD(...))" and then called the selector leaf
// through the int32 ABI, so every projection of a List<long> / List<double> /
// List<float> received a TRUNCATED element (a silent wrong value, no
// exception). This probe pins every (source family x result family) cell that
// the runtime claims to support, with elements outside the int32 range and with
// the IEEE754 specials, so the net48 oracle and the runtime must agree
// byte-for-byte.
//
// Sections:
//   A  List<int>    control row (i4/i8/f/d/ref + index forms)
//   B  List<long>   identity / int / double / float / string, index forms,
//                   empty, single, ToList/foreach chains
//   C  List<float>  identity / double / int / long / string
//   D  List<double> identity / int / long / float / string
//   E  List<ulong>  identity / double / string
//   F  narrow families (sbyte/byte/short/ushort/uint/bool/char) controls
//   G  empty and single-element sources
//
// Every record is one line "[id]=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeListSelNum
    {
        private static void W(string id, string v)
        {
            Console.WriteLine("[" + id + "]=" + v);
        }

        private static string L(long v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string U(ulong v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string D(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static string Dbits(double v) { return L(BitConverter.DoubleToInt64Bits(v)); }
        private static string Fbits(float v)
        {
            return I(BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
        }
        private static string B(bool v) { return v ? "T" : "F"; }

        private static string JoinL(long[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(L(a[i])); }
            return sb.ToString();
        }

        private static string JoinI(int[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(I(a[i])); }
            return sb.ToString();
        }

        private static string JoinD(double[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(Dbits(a[i])); }
            return sb.ToString();
        }

        private static string JoinF(float[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(Fbits(a[i])); }
            return sb.ToString();
        }

        private static string JoinS(string[] a)
        {
            return string.Join(",", a);
        }

        private static string JoinSb(sbyte[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(I(a[i])); }
            return sb.ToString();
        }

        private static string JoinBy(byte[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(I(a[i])); }
            return sb.ToString();
        }

        private static string JoinSh(short[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(I(a[i])); }
            return sb.ToString();
        }

        private static string JoinUs(ushort[] a)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i != 0) sb.Append(','); sb.Append(I(a[i])); }
            return sb.ToString();
        }

        //--------------------------------------------------------------------------
        // A: List<int> control row
        //--------------------------------------------------------------------------
        private static void I4Control()
        {
            List<int> l = new List<int>();
            l.Add(7); l.Add(-3); l.Add(2147483647); l.Add(int.MinValue);

            W("A_i4", JoinI(l.Select(x => x * 2).ToArray()));
            W("A_i8", JoinL(l.Select(x => (long)x + 1L).ToArray()));
            W("A_f", JoinF(l.Select(x => (float)x + 0.5f).ToArray()));
            W("A_d", JoinD(l.Select(x => (double)x / 2.0).ToArray()));
            W("A_ref", JoinS(l.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
            W("A_idx_i4", JoinI(l.Select((x, i) => x + i).ToArray()));
            W("A_idx_i8", JoinL(l.Select((x, i) => (long)x * 1000L + i).ToArray()));
            W("A_idx_ref", JoinS(l.Select((x, i) => x.ToString(CultureInfo.InvariantCulture) + ":" + i.ToString(CultureInfo.InvariantCulture)).ToArray()));
        }

        //--------------------------------------------------------------------------
        // B: List<long>
        //--------------------------------------------------------------------------
        private static void LongOps()
        {
            List<long> l = new List<long>();
            l.Add(long.MaxValue);
            l.Add(long.MinValue);
            l.Add(1L << 40);
            l.Add(1234567890123L);
            l.Add(-1L);
            l.Add(0L);

            W("B_ident", JoinL(l.Select(x => x).ToArray()));
            W("B_ident_idx", JoinL(l.Select((x, i) => x).ToArray()));
            W("B_i4", JoinI(l.Select(x => (int)x).ToArray()));
            W("B_i8_shift", JoinL(l.Select(x => x >> 4).ToArray()));
            W("B_d", JoinD(l.Select(x => (double)x).ToArray()));
            W("B_f", JoinF(l.Select(x => (float)x).ToArray()));
            W("B_ref", JoinS(l.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
            W("B_idx_ref", JoinS(l.Select((x, i) => x.ToString(CultureInfo.InvariantCulture) + "@" + L(i)).ToArray()));
            W("B_idx_d", JoinD(l.Select((x, i) => (double)x + (double)i).ToArray()));
            W("B_idx_i4", JoinI(l.Select((x, i) => (int)(x % 1000) + i).ToArray()));

            // Chained consumption: ToList / index / foreach.
            List<long> copy = l.Select(x => x).ToList();
            W("B_chain_count", I(copy.Count));
            W("B_chain_0", L(copy[0]));
            W("B_chain_1", L(copy[1]));
            W("B_chain_5", L(copy[5]));
            long xor = 0;
            foreach (long v in copy) { xor ^= v; }
            W("B_chain_foreach_xor", L(xor));

            List<string> strs = l.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
            W("B_str_count", I(strs.Count));
            W("B_str_0", strs[0]);
            W("B_str_1", strs[1]);
            W("B_str_3", strs[3]);

            long total = 0;
            foreach (long v in l.Select(x => x / 2L)) { total += v; }
            W("B_foreach_half_sum", L(total));
        }

        //--------------------------------------------------------------------------
        // C: List<float>
        //--------------------------------------------------------------------------
        private static void FloatOps()
        {
            List<float> l = new List<float>();
            l.Add(1.5f);
            l.Add(-0.0f);
            l.Add(3.7f);
            l.Add(float.NaN);
            l.Add(float.PositiveInfinity);
            l.Add(float.NegativeInfinity);
            l.Add(1e18f);

            W("C_ident", JoinF(l.Select(x => x).ToArray()));
            W("C_ident_idx", JoinF(l.Select((x, i) => x).ToArray()));
            W("C_d", JoinD(l.Select(x => (double)x).ToArray()));
            W("C_d_scaled", JoinD(l.Select(x => (double)x * 1e18).ToArray()));
            W("C_i4", JoinI(l.Select(x => (int)x).ToArray()));
            W("C_i8", JoinL(l.Select(x => (long)x).ToArray()));
            W("C_ref", JoinS(l.Select(x => x.ToString("R", CultureInfo.InvariantCulture)).ToArray()));
            W("C_idx_ref", JoinS(l.Select((x, i) => x.ToString("R", CultureInfo.InvariantCulture) + "/" + I(i)).ToArray()));
        }

        //--------------------------------------------------------------------------
        // D: List<double>
        //--------------------------------------------------------------------------
        private static void DoubleOps()
        {
            List<double> l = new List<double>();
            l.Add(3.7);
            l.Add(-2.5);
            l.Add(1.5e18);
            l.Add(-0.0);
            l.Add(double.NaN);
            l.Add(double.PositiveInfinity);
            l.Add(double.NegativeInfinity);

            W("D_ident", JoinD(l.Select(x => x).ToArray()));
            W("D_ident_idx", JoinD(l.Select((x, i) => x).ToArray()));
            W("D_i4", JoinI(l.Select(x => (int)(x % 1000.0)).ToArray()));
            W("D_i8", JoinL(l.Select(x => (long)(x % 1000000.0)).ToArray()));
            W("D_f", JoinF(l.Select(x => (float)x).ToArray()));
            W("D_ref", JoinS(l.Select(x => x.ToString("R", CultureInfo.InvariantCulture)).ToArray()));
            W("D_idx_ref", JoinS(l.Select((x, i) => x.ToString("R", CultureInfo.InvariantCulture) + "|" + I(i)).ToArray()));
            W("D_idx_d", JoinD(l.Select((x, i) => x + (double)i).ToArray()));

            List<double> whole = new List<double>();
            whole.Add(1e18);
            whole.Add(9007199254740993.0);
            W("D_big_ident", JoinD(whole.Select(x => x).ToArray()));
            W("D_big_long", JoinL(whole.Select(x => (long)x).ToArray()));
        }

        //--------------------------------------------------------------------------
        // E: List<string> (REFERENCE source -- element arrives as a pointer)
        //--------------------------------------------------------------------------
        private static void RefOps()
        {
            List<string> l = new List<string>();
            l.Add("a");
            l.Add("bbb");
            l.Add("cc");
            l.Add("");

            W("E_i4", JoinI(l.Select(x => x.Length).ToArray()));
            W("E_i8", JoinL(l.Select(x => (long)x.Length * 1000000000L).ToArray()));
            W("E_ref", JoinS(l.Select(x => x + "!").ToArray()));
            W("E_idx_i4", JoinI(l.Select((x, i) => x.Length + i).ToArray()));
        }

        //--------------------------------------------------------------------------
        // F: narrow families
        //--------------------------------------------------------------------------
        private static void NarrowOps()
        {
            List<byte> b = new List<byte>();
            b.Add(0); b.Add(255); b.Add(128);
            W("F_byte_i4", JoinBy(b.Select(x => x).ToArray()));
            W("F_byte_i8", JoinL(b.Select(x => (long)x * 2L).ToArray()));

            List<sbyte> sb = new List<sbyte>();
            sb.Add(sbyte.MinValue); sb.Add(-1); sb.Add(127);
            W("F_sbyte_i4", JoinSb(sb.Select(x => x).ToArray()));
            W("F_sbyte_i8", JoinL(sb.Select(x => (long)x).ToArray()));

            List<short> sh = new List<short>();
            sh.Add(short.MinValue); sh.Add(short.MaxValue);
            W("F_short_i4", JoinSh(sh.Select(x => x).ToArray()));
            W("F_short_d", JoinD(sh.Select(x => (double)x).ToArray()));

            List<ushort> us = new List<ushort>();
            us.Add(65535); us.Add(3);
            W("F_ushort_i4", JoinUs(us.Select(x => x).ToArray()));

            List<uint> ui = new List<uint>();
            ui.Add(uint.MaxValue); ui.Add(2147483648u);
            W("F_uint_i4", JoinI(ui.Select(x => (int)x).ToArray()));
            W("F_uint_ref", JoinS(ui.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));

            List<bool> bo = new List<bool>();
            bo.Add(true); bo.Add(false);
            W("F_bool_i4", JoinI(bo.Select(x => x ? 1 : 0).ToArray()));

            List<char> ch = new List<char>();
            ch.Add('\uFFFF'); ch.Add('A');
            W("F_char_i4", JoinI(ch.Select(x => (int)x).ToArray()));
        }

        //--------------------------------------------------------------------------
        // G: empty / single
        //--------------------------------------------------------------------------
        private static void EdgeOps()
        {
            List<long> e = new List<long>();
            W("G_i8_empty_ident", I(e.Select(x => x).ToArray().Length));
            W("G_i8_empty_ref", I(e.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray().Length));

            List<long> one = new List<long>();
            one.Add(1L << 40);
            W("G_i8_one_ident", JoinL(one.Select(x => x).ToArray()));
            W("G_i8_one_ref", JoinS(one.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));

            List<double> ed = new List<double>();
            W("G_d_empty_ident", I(ed.Select(x => x).ToArray().Length));

            List<double> oned = new List<double>();
            oned.Add(3.7);
            W("G_d_one_ident", JoinD(oned.Select(x => x).ToArray()));
            W("G_d_one_ref", JoinS(oned.Select(x => x.ToString("R", CultureInfo.InvariantCulture)).ToArray()));

            List<float> ef = new List<float>();
            W("G_f_empty_ident", I(ef.Select(x => x).ToArray().Length));
        }

        //--------------------------------------------------------------------------
        // H: F2 -- a REFERENCE source with every VALUE result family
        //--------------------------------------------------------------------------
        private static void RefValueResult()
        {
            List<string> l = new List<string>();
            l.Add("a");
            l.Add("bbbb");
            l.Add("cc");

            int[] i4 = l.Select(x => x.Length).ToArray();
            W("H_i4", JoinI(i4));
            long[] i8 = l.Select(x => (long)x.Length * 4294967296L).ToArray();
            W("H_i8", JoinL(i8));
            float[] f = l.Select(x => (float)x.Length / 4.0f).ToArray();
            W("H_f", JoinF(f));
            double[] d = l.Select(x => (double)x.Length / 4.0).ToArray();
            W("H_d", JoinD(d));
            string[] r = l.Select(x => x + "#").ToArray();
            W("H_ref", JoinS(r));

            int[] i4i = l.Select((x, i) => x.Length * 10 + i).ToArray();
            W("H_idx_i4", JoinI(i4i));
            long[] i8i = l.Select((x, i) => (long)x.Length * 4294967296L + i).ToArray();
            W("H_idx_i8", JoinL(i8i));
            float[] fi = l.Select((x, i) => (float)x.Length / 4.0f).ToArray();
            W("H_idx_f", JoinF(fi));
            double[] di = l.Select((x, i) => (double)x.Length / 4.0).ToArray();
            W("H_idx_d", JoinD(di));

            long sum = 0;
            foreach (long v in l.Select(x => (long)x.Length)) { sum += v; }
            W("H_foreach_sum", L(sum));
        }

        //--------------------------------------------------------------------------
        // I: F3 -- a TUPLE result over a WIDE source (the element must keep its
        // own width)
        //--------------------------------------------------------------------------
        private static void TupleOverWide()
        {
            // The supported consumption shape of a tuple Select result is the
            // enumerator (the boxes are unboxed by the tuple enumerator
            // contract); indexing the materialized array would read the box
            // POINTER as a struct (a separate, pre-existing consumer gap).
            List<int> li = new List<int>();
            li.Add(3); li.Add(9);
            foreach (var t in li.Select(x => (x, x * 2)))
                W("I_i4", I(t.Item1) + "/" + I(t.Item2));

            // Only the <= 8-byte (register-carried) tuple shapes are wired, so
            // each wide source projects a PAIR OF INT32 fields whose values still
            // prove the element reached the selector at its real width.
            List<long> ll = new List<long>();
            ll.Add(1L << 40);
            ll.Add(4294967301L);
            foreach (var t in ll.Select(x => ((int)(x >> 32), (int)(x & 0xFFFFFFFFL))))
                W("I_i8", I(t.Item1) + "/" + I(t.Item2));

            List<double> ld = new List<double>();
            ld.Add(3.7);
            ld.Add(1.5e18);
            foreach (var t in ld.Select(x => ((int)x, (int)(x % 100.0))))
                W("I_d", I(t.Item1) + "/" + I(t.Item2));

            List<float> lf = new List<float>();
            lf.Add(1.5f);
            lf.Add(3.7f);
            foreach (var t in lf.Select(x => ((int)x, (int)(x * 10.0f))))
                W("I_f", I(t.Item1) + "/" + I(t.Item2));

            List<string> ls = new List<string>();
            ls.Add("a"); ls.Add("bbbb");
            foreach (var t in ls.Select(x => (x.Length, x.Length * 2)))
                W("I_ref", I(t.Item1) + "/" + I(t.Item2));
        }

        //--------------------------------------------------------------------------
        // J: F4 -- GroupBy over a WIDE / REFERENCE List source (the key selector
        // must receive the REAL element)
        //--------------------------------------------------------------------------
        private static void ListGroupBySource()
        {
            List<long> ll = new List<long>();
            ll.Add(1L << 40);
            ll.Add((1L << 40) + 1L);
            ll.Add(1L << 41);
            foreach (var g in ll.GroupBy(x => x % 2L))
            {
                W("J_i8_key", L(g.Key));
                foreach (long e in g) { W("J_i8_elem", L(e)); }
            }
            foreach (var g in ll.GroupBy(x => x.ToString(CultureInfo.InvariantCulture)))
                W("J_i8_strkey", g.Key + ":" + I(g.Count()));

            List<string> ls = new List<string>();
            ls.Add("aa"); ls.Add("bb"); ls.Add("cc");
            foreach (var g in ls.GroupBy(x => x.Length))
            {
                W("J_ref_key", I(g.Key));
                foreach (string e in g) { W("J_ref_elem", e); }
            }

            List<double> ld = new List<double>();
            ld.Add(1.5); ld.Add(2.5); ld.Add(1.5);
            foreach (var g in ld.GroupBy(x => x > 2.0 ? 1 : 0))
                W("J_d_key", I(g.Key) + ":" + I(g.Count()));
        }

        public static void Main()
        {
            I4Control();
            LongOps();
            FloatOps();
            DoubleOps();
            RefOps();
            NarrowOps();
            EdgeOps();
            RefValueResult();
            TupleOverWide();
            ListGroupBySource();
        }
    }
}
