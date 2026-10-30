//------------------------------------------------------------------------------
// probe-tolistnumeric -- List<T>.ToList() over a LIST receiver for the NUMERIC
// element families: long / double / float / sbyte / byte / short / ushort /
// uint / bool / char.
//
// The listobj storage BOXES every value element, and the element snapshot is a
// typed value array at the family's real cell width (listobj.h
// il2cpp_list_obj_to_array_<kind>). ToList has to re-box every snapshot cell
// through the SAME family add; the reference loop read the first 8 bytes of the
// cell as a GC pointer, so a numeric ToList returned a List<T> whose slots were
// wild pointers (a crash on the next get_Item / foreach, or a silent wrong
// value when the cell happened to look like an address).
//
// Each family is pinned over: a non-empty source (Count / indexer / foreach /
// a second ToList chain), an EMPTY source, and the family's boundary values
// (long.MaxValue / long.MinValue, the double/float NaN / -0.0 / +-Infinity /
// MaxValue / MinValue / Epsilon bit patterns, and the narrow integral limits).
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cases
{
    public static class ProbeToListNumeric
    {
        private static void W(string id, string v)
        {
            Console.WriteLine("[" + id + "]=" + v);
        }

        private static string L(long v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string U(uint v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string D(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        private static string Dbits(double v) { return L(BitConverter.DoubleToInt64Bits(v)); }
        private static string Fbits(float v)
        {
            return I(BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
        }
        private static string B(bool v) { return v ? "T" : "F"; }

        //--------------------------------------------------------------------------
        // List<long>
        //--------------------------------------------------------------------------
        private static void LongOps()
        {
            List<long> l = new List<long>();
            l.Add(long.MaxValue);
            l.Add(long.MinValue);
            l.Add(long.MaxValue - 1);
            l.Add(-1L);
            l.Add(0L);
            l.Add(1234567890123L);

            List<long> copy = l.ToList();
            W("l_count", I(copy.Count));
            W("l_0", L(copy[0]));
            W("l_1", L(copy[1]));
            W("l_2", L(copy[2]));
            W("l_3", L(copy[3]));
            W("l_4", L(copy[4]));
            W("l_5", L(copy[5]));
            long xor = 0;
            foreach (long v in copy) { xor ^= v; }
            W("l_foreach_xor", L(xor));

            List<long> chain = copy.ToList();
            W("l_chain_count", I(chain.Count));
            W("l_chain_2", L(chain[2]));
            W("l_chain_last", L(chain[chain.Count - 1]));

            copy.Add(7L);
            W("l_src_unchanged", I(l.Count));
            W("l_copy_after_add", I(copy.Count) + "/" + L(copy[6]));

            List<long> empty = new List<long>();
            List<long> emptyCopy = empty.ToList();
            W("l_empty_count", I(emptyCopy.Count));
            W("l_empty_any", B(emptyCopy.Any()));
            W("l_empty_chain", I(emptyCopy.ToList().Count));
        }

        //--------------------------------------------------------------------------
        // List<double>
        //--------------------------------------------------------------------------
        private static void DoubleOps()
        {
            List<double> l = new List<double>();
            l.Add(0.0);
            l.Add(-0.0);
            l.Add(1.5);
            l.Add(double.NaN);
            l.Add(double.PositiveInfinity);
            l.Add(double.NegativeInfinity);
            l.Add(double.MaxValue);
            l.Add(double.MinValue);
            l.Add(double.Epsilon);

            List<double> copy = l.ToList();
            W("d_count", I(copy.Count));
            for (int i = 0; i < copy.Count; i++)
            {
                W("d_bits_" + I(i), Dbits(copy[i]));
            }
            W("d_0_str", D(copy[0]));
            W("d_1_str", D(copy[1]));
            W("d_2_str", D(copy[2]));
            int nan = 0, posInf = 0, negInf = 0, negZero = 0;
            foreach (double v in copy)
            {
                if (double.IsNaN(v)) nan++;
                else if (double.IsPositiveInfinity(v)) posInf++;
                else if (double.IsNegativeInfinity(v)) negInf++;
                else if (v == 0.0 && 1.0 / v < 0.0) negZero++;
            }
            W("d_foreach_nan", I(nan));
            W("d_foreach_posinf", I(posInf));
            W("d_foreach_neginf", I(negInf));
            W("d_foreach_negzero", I(negZero));

            List<double> chain = copy.ToList();
            W("d_chain_count", I(chain.Count));
            W("d_chain_3", Dbits(chain[3]));
            W("d_chain_8", Dbits(chain[8]));

            List<double> empty = new List<double>();
            W("d_empty_count", I(empty.ToList().Count));
            W("d_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<float>
        //--------------------------------------------------------------------------
        private static void FloatOps()
        {
            List<float> l = new List<float>();
            l.Add(0.0f);
            l.Add(-0.0f);
            l.Add(1.5f);
            l.Add(float.NaN);
            l.Add(float.PositiveInfinity);
            l.Add(float.NegativeInfinity);
            l.Add(float.MaxValue);
            l.Add(float.MinValue);
            l.Add(float.Epsilon);

            List<float> copy = l.ToList();
            W("f_count", I(copy.Count));
            for (int i = 0; i < copy.Count; i++)
            {
                W("f_bits_" + I(i), Fbits(copy[i]));
            }
            W("f_2_str", F(copy[2]));
            int nan = 0, posInf = 0, negInf = 0, negZero = 0;
            foreach (float v in copy)
            {
                if (double.IsNaN((double)v)) nan++;
                else if (double.IsPositiveInfinity((double)v)) posInf++;
                else if (double.IsNegativeInfinity((double)v)) negInf++;
                else if (v == 0.0f && 1.0f / v < 0.0f) negZero++;
            }
            W("f_foreach_nan", I(nan));
            W("f_foreach_posinf", I(posInf));
            W("f_foreach_neginf", I(negInf));
            W("f_foreach_negzero", I(negZero));

            List<float> chain = copy.ToList();
            W("f_chain_count", I(chain.Count));
            W("f_chain_3", Fbits(chain[3]));
            W("f_chain_8", Fbits(chain[8]));

            List<float> empty = new List<float>();
            W("f_empty_count", I(empty.ToList().Count));
            W("f_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<sbyte>
        //--------------------------------------------------------------------------
        private static void SByteOps()
        {
            List<sbyte> l = new List<sbyte>();
            l.Add(sbyte.MinValue);
            l.Add(sbyte.MaxValue);
            l.Add((sbyte)-1);
            l.Add((sbyte)0);
            l.Add((sbyte)100);

            List<sbyte> copy = l.ToList();
            W("sb_count", I(copy.Count));
            W("sb_0", I(copy[0]));
            W("sb_1", I(copy[1]));
            W("sb_2", I(copy[2]));
            W("sb_4", I(copy[4]));
            int sum = 0;
            foreach (sbyte v in copy) { sum += v; }
            W("sb_foreach_sum", I(sum));

            List<sbyte> chain = copy.ToList();
            W("sb_chain_count", I(chain.Count));
            W("sb_chain_0", I(chain[0]));

            List<sbyte> empty = new List<sbyte>();
            W("sb_empty_count", I(empty.ToList().Count));
            W("sb_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<byte>
        //--------------------------------------------------------------------------
        private static void ByteOps()
        {
            List<byte> l = new List<byte>();
            l.Add(0);
            l.Add(255);
            l.Add(1);
            l.Add(128);
            l.Add(127);

            List<byte> copy = l.ToList();
            W("by_count", I(copy.Count));
            W("by_0", I(copy[0]));
            W("by_1", I(copy[1]));
            W("by_2", I(copy[2]));
            W("by_3", I(copy[3]));
            W("by_4", I(copy[4]));
            int sum = 0;
            foreach (byte v in copy) { sum += v; }
            W("by_foreach_sum", I(sum));

            List<byte> chain = copy.ToList();
            W("by_chain_count", I(chain.Count));
            W("by_chain_1", I(chain[1]));

            List<byte> empty = new List<byte>();
            W("by_empty_count", I(empty.ToList().Count));
            W("by_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<short>
        //--------------------------------------------------------------------------
        private static void ShortOps()
        {
            List<short> l = new List<short>();
            l.Add(short.MinValue);
            l.Add(short.MaxValue);
            l.Add((short)-1);
            l.Add((short)0);
            l.Add((short)12345);

            List<short> copy = l.ToList();
            W("sh_count", I(copy.Count));
            W("sh_0", I(copy[0]));
            W("sh_1", I(copy[1]));
            W("sh_2", I(copy[2]));
            W("sh_4", I(copy[4]));
            int sum = 0;
            foreach (short v in copy) { sum += v; }
            W("sh_foreach_sum", I(sum));

            List<short> chain = copy.ToList();
            W("sh_chain_count", I(chain.Count));
            W("sh_chain_1", I(chain[1]));

            List<short> empty = new List<short>();
            W("sh_empty_count", I(empty.ToList().Count));
            W("sh_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<ushort>
        //--------------------------------------------------------------------------
        private static void UShortOps()
        {
            List<ushort> l = new List<ushort>();
            l.Add(0);
            l.Add(ushort.MaxValue);
            l.Add(32768);
            l.Add(1);

            List<ushort> copy = l.ToList();
            W("us_count", I(copy.Count));
            W("us_0", I(copy[0]));
            W("us_1", I(copy[1]));
            W("us_2", I(copy[2]));
            W("us_3", I(copy[3]));
            int sum = 0;
            foreach (ushort v in copy) { sum += v; }
            W("us_foreach_sum", I(sum));

            List<ushort> chain = copy.ToList();
            W("us_chain_count", I(chain.Count));
            W("us_chain_1", I(chain[1]));

            List<ushort> empty = new List<ushort>();
            W("us_empty_count", I(empty.ToList().Count));
            W("us_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<uint>
        //--------------------------------------------------------------------------
        private static void UIntOps()
        {
            List<uint> l = new List<uint>();
            l.Add(0u);
            l.Add(uint.MaxValue);
            l.Add(2147483648u);
            l.Add(1u);

            List<uint> copy = l.ToList();
            W("ui_count", I(copy.Count));
            W("ui_0", U(copy[0]));
            W("ui_1", U(copy[1]));
            W("ui_2", U(copy[2]));
            W("ui_3", U(copy[3]));
            long sum = 0;
            foreach (uint v in copy) { sum += v; }
            W("ui_foreach_sum", L(sum));

            List<uint> chain = copy.ToList();
            W("ui_chain_count", I(chain.Count));
            W("ui_chain_1", U(chain[1]));

            List<uint> empty = new List<uint>();
            W("ui_empty_count", I(empty.ToList().Count));
            W("ui_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<bool>
        //--------------------------------------------------------------------------
        private static void BoolOps()
        {
            List<bool> l = new List<bool>();
            l.Add(true);
            l.Add(false);
            l.Add(true);
            l.Add(false);

            List<bool> copy = l.ToList();
            W("bo_count", I(copy.Count));
            W("bo_0", B(copy[0]));
            W("bo_1", B(copy[1]));
            W("bo_2", B(copy[2]));
            W("bo_3", B(copy[3]));
            int t = 0;
            foreach (bool v in copy) { if (v) t++; }
            W("bo_foreach_true", I(t));

            List<bool> chain = copy.ToList();
            W("bo_chain_count", I(chain.Count));
            W("bo_chain_1", B(chain[1]));

            List<bool> empty = new List<bool>();
            W("bo_empty_count", I(empty.ToList().Count));
            W("bo_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // List<char>
        //--------------------------------------------------------------------------
        private static void CharOps()
        {
            List<char> l = new List<char>();
            l.Add('\0');
            l.Add('A');
            l.Add('\u00FF');
            l.Add('\uFFFF');
            l.Add('z');

            List<char> copy = l.ToList();
            W("ch_count", I(copy.Count));
            W("ch_0", I((int)copy[0]));
            W("ch_1", I((int)copy[1]));
            W("ch_2", I((int)copy[2]));
            W("ch_3", I((int)copy[3]));
            W("ch_4", I((int)copy[4]));
            int sum = 0;
            foreach (char v in copy) { sum += (int)v; }
            W("ch_foreach_sum", I(sum));

            List<char> chain = copy.ToList();
            W("ch_chain_count", I(chain.Count));
            W("ch_chain_3", I((int)chain[3]));

            List<char> empty = new List<char>();
            W("ch_empty_count", I(empty.ToList().Count));
            W("ch_empty_any", B(empty.ToList().Any()));
        }

        //--------------------------------------------------------------------------
        // INLINE consumption: the ToList() result is used WITHOUT an intermediate
        // typed local. The receiver hint here is the call's own tracked result
        // type, so a bare "List`1" pseudo-type (the int32 domain) would route
        // get_Count / get_Item / foreach to the listi4 entries and read a 4-byte
        // slice of an 8-byte int64 / double cell or a 1/2-byte narrow cell.
        //--------------------------------------------------------------------------
        private static void DirectChainOps()
        {
            List<long> ll = new List<long>();
            ll.Add(long.MaxValue);
            ll.Add(-1L);
            ll.Add(1234567890123L);
            W("dl_count", I(ll.ToList().Count));
            W("dl_1", L(ll.ToList()[1]));
            W("dl_2", L(ll.ToList()[2]));
            long lx = 0;
            foreach (long v in ll.ToList()) { lx ^= v; }
            W("dl_foreach_xor", L(lx));
            W("dl_any", B(ll.ToList().Any()));
            W("dl_first", L(ll.ToList().First()));

            List<double> ld = new List<double>();
            ld.Add(1.5);
            ld.Add(double.NaN);
            ld.Add(-0.0);
            W("dd_count", I(ld.ToList().Count));
            W("dd_0", Dbits(ld.ToList()[0]));
            W("dd_1", Dbits(ld.ToList()[1]));
            W("dd_2", Dbits(ld.ToList()[2]));
            double dx = 0.0;
            foreach (double v in ld.ToList()) { if (!double.IsNaN(v)) dx += v; }
            W("dd_foreach_sum", D(dx));

            List<float> lf = new List<float>();
            lf.Add(2.5f);
            lf.Add(float.NegativeInfinity);
            W("df_count", I(lf.ToList().Count));
            W("df_0", Fbits(lf.ToList()[0]));
            W("df_1", Fbits(lf.ToList()[1]));
            float fx = 0.0f;
            foreach (float v in lf.ToList()) { if (!double.IsNegativeInfinity((double)v)) fx += v; }
            W("df_foreach_sum", F(fx));

            List<byte> lb = new List<byte>();
            lb.Add(255);
            lb.Add(7);
            W("db_count", I(lb.ToList().Count));
            W("db_0", I(lb.ToList()[0]));
            W("db_1", I(lb.ToList()[1]));
            int bx = 0;
            foreach (byte v in lb.ToList()) { bx += v; }
            W("db_foreach_sum", I(bx));

            List<sbyte> lsb = new List<sbyte>();
            lsb.Add(sbyte.MinValue);
            lsb.Add((sbyte)127);
            W("dsb_count", I(lsb.ToList().Count));
            W("dsb_0", I(lsb.ToList()[0]));
            W("dsb_1", I(lsb.ToList()[1]));
            int sx = 0;
            foreach (sbyte v in lsb.ToList()) { sx += v; }
            W("dsb_foreach_sum", I(sx));

            List<short> lsh = new List<short>();
            lsh.Add(short.MinValue);
            lsh.Add(short.MaxValue);
            W("dsh_count", I(lsh.ToList().Count));
            W("dsh_0", I(lsh.ToList()[0]));
            W("dsh_1", I(lsh.ToList()[1]));
            int hx = 0;
            foreach (short v in lsh.ToList()) { hx += v; }
            W("dsh_foreach_sum", I(hx));

            List<ushort> lus = new List<ushort>();
            lus.Add(65535);
            lus.Add(3);
            W("dus_count", I(lus.ToList().Count));
            W("dus_0", I(lus.ToList()[0]));
            W("dus_1", I(lus.ToList()[1]));
            int ux = 0;
            foreach (ushort v in lus.ToList()) { ux += v; }
            W("dus_foreach_sum", I(ux));

            List<uint> lui = new List<uint>();
            lui.Add(uint.MaxValue);
            lui.Add(2147483648u);
            W("dui_count", I(lui.ToList().Count));
            W("dui_0", U(lui.ToList()[0]));
            W("dui_1", U(lui.ToList()[1]));
            long ix = 0;
            foreach (uint v in lui.ToList()) { ix += v; }
            W("dui_foreach_sum", L(ix));

            List<bool> lbool = new List<bool>();
            lbool.Add(true);
            lbool.Add(false);
            W("dbo_count", I(lbool.ToList().Count));
            W("dbo_0", B(lbool.ToList()[0]));
            W("dbo_1", B(lbool.ToList()[1]));
            int tx = 0;
            foreach (bool v in lbool.ToList()) { if (v) tx++; }
            W("dbo_foreach_true", I(tx));

            List<char> lc = new List<char>();
            lc.Add('\uFFFF');
            lc.Add('A');
            W("dch_count", I(lc.ToList().Count));
            W("dch_0", I((int)lc.ToList()[0]));
            W("dch_1", I((int)lc.ToList()[1]));
            int cx = 0;
            foreach (char v in lc.ToList()) { cx += (int)v; }
            W("dch_foreach_sum", I(cx));

            List<char> emptyC = new List<char>();
            W("dch_empty_count", I(emptyC.ToList().Count));
            W("dch_empty_any", B(emptyC.ToList().Any()));
        }

        public static void Main()
        {
            LongOps();
            DoubleOps();
            FloatOps();
            SByteOps();
            ByteOps();
            ShortOps();
            UShortOps();
            UIntOps();
            BoolOps();
            CharOps();
            DirectChainOps();
        }
    }
}
