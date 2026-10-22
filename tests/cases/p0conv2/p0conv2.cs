using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// p0conv2 -- P0 defect closure case for three LOUD gaps:
//   D1  Convert.ToDecimal(<every source>)        (was: unconditional LOUD)
//   D2  Convert.ToInt32/ToInt64/ToDouble(decimal) (was: LOUD; net48 ROUNDS
//       half-to-even for the integer targets -- NOT the truncating cast)
//   D3  Enumerable.OrderBy over a long[] source with an int32/int64 key
//       selector (was: LOUD -- the key-selector surface was int32-source only)
//
// Callbacks are INSTANCE-BOUND delegates (the transpiler has no lambdas).
namespace Cases
{
    public sealed class Conv2Keys
    {
        public long Self(long v) { return v; }
        public int Low(long v) { return (int)(v % 10); }
        public int IntKey(int v) { return v; }
    }

    public static class P0Conv2
    {
        static string Dec(decimal d)
        {
            return d.ToString(CultureInfo.InvariantCulture);
        }

        static string Bits(decimal d)
        {
            // Scale witness without decimal.GetBits (unwired): net48 preserves
            // trailing zeros, so ToString() exposes the parsed scale.
            return d.ToString(CultureInfo.InvariantCulture);
        }

        static string JoinL(long[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString(CultureInfo.InvariantCulture);
            }
            return s;
        }

        static string JoinI(int[] a)
        {
            string s = "";
            for (int i = 0; i < a.Length; ++i)
            {
                if (i > 0) s += ",";
                s += a[i].ToString(CultureInfo.InvariantCulture);
            }
            return s;
        }

        static string TryI32(decimal d)
        {
            try { return Convert.ToInt32(d).ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TryI64(decimal d)
        {
            try { return Convert.ToInt64(d).ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TryU32(decimal d)
        {
            try { return Convert.ToUInt32(d).ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TryU64(decimal d)
        {
            try { return Convert.ToUInt64(d).ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TryDbl(decimal d)
        {
            try { return Convert.ToDouble(d).ToString("R", CultureInfo.InvariantCulture); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TrySgl(decimal d, float expected)
        {
            try { return (Convert.ToSingle(d) == expected).ToString(); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TryDecD(double v)
        {
            try { return Dec(Convert.ToDecimal(v)); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TryDecF(float v)
        {
            try { return Dec(Convert.ToDecimal(v)); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        static string TryDecS(string v)
        {
            try { return Dec(Convert.ToDecimal(v)); }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        public static void Main()
        {
            Conv2Keys k = new Conv2Keys();
            Func<long, long> longSelf = k.Self;
            Func<long, int> longLow = k.Low;
            Func<int, int> intKey = k.IntKey;

            // ================= D2: decimal -> int32/int64 (ToEven rounding) ===
            Console.WriteLine("[i32-12.5]=" + TryI32(12.5m));
            Console.WriteLine("[i32-2.5]=" + TryI32(2.5m));
            Console.WriteLine("[i32-3.5]=" + TryI32(3.5m));
            Console.WriteLine("[i32-12.9]=" + TryI32(12.9m));
            Console.WriteLine("[i32-neg12.5]=" + TryI32(-12.5m));
            Console.WriteLine("[i32-neg2.5]=" + TryI32(-2.5m));
            Console.WriteLine("[i32-neg3.5]=" + TryI32(-3.5m));
            Console.WriteLine("[i32-0.5]=" + TryI32(0.5m));
            Console.WriteLine("[i32-1.5]=" + TryI32(1.5m));
            Console.WriteLine("[i32-4.5]=" + TryI32(4.5m));
            Console.WriteLine("[i32-5.5]=" + TryI32(5.5m));
            Console.WriteLine("[i32-neg0.9]=" + TryI32(-0.9m));
            Console.WriteLine("[i32-1.999]=" + TryI32(1.999m));
            Console.WriteLine("[i32-max-i32]=" + TryI32(2147483647m));
            Console.WriteLine("[i32-over]=" + TryI32(2147483648m));
            Console.WriteLine("[i32-roundover]=" + TryI32(2147483647.6m));
            Console.WriteLine("[i32-decmin]=" + TryI32(decimal.MinValue));
            Console.WriteLine("[i32-decmax]=" + TryI32(decimal.MaxValue));

            Console.WriteLine("[i64-12.5]=" + TryI64(12.5m));
            Console.WriteLine("[i64-3.5]=" + TryI64(3.5m));
            Console.WriteLine("[i64-2.5]=" + TryI64(2.5m));
            Console.WriteLine("[i64-neg3.5]=" + TryI64(-3.5m));
            Console.WriteLine("[i64-max]=" + TryI64(9223372036854775807m));
            Console.WriteLine("[i64-over]=" + TryI64(9223372036854775808m));

            Console.WriteLine("[u32-12.5]=" + TryU32(12.5m));
            Console.WriteLine("[u32-3.5]=" + TryU32(3.5m));
            Console.WriteLine("[u32-neg0.5]=" + TryU32(-0.5m));
            Console.WriteLine("[u32-over]=" + TryU32(4294967296m));
            Console.WriteLine("[u64-12.5]=" + TryU64(12.5m));
            Console.WriteLine("[u64-3.5]=" + TryU64(3.5m));
            Console.WriteLine("[u64-neg0.5]=" + TryU64(-0.5m));

            // ================= D2: decimal -> double/single (exact) ===========
            Console.WriteLine("[dbl-12.5]=" + TryDbl(12.5m));
            Console.WriteLine("[dbl-0.1]=" + TryDbl(0.1m));
            Console.WriteLine("[dbl-third]=" + TryDbl(1m / 3m));
            Console.WriteLine("[dbl-max]=" + TryDbl(decimal.MaxValue));
            Console.WriteLine("[dbl-min]=" + TryDbl(decimal.MinValue));
            Console.WriteLine("[dbl-long]=" + TryDbl(1.2345678901234567890123456789m));
            Console.WriteLine("[dbl-tiny]=" + TryDbl(0.0000000000000000000000000001m));
            Console.WriteLine("[dbl-big]=" + TryDbl(79228162514264337593543950335m));
            Console.WriteLine("[dbl-2^53]=" + TryDbl(9007199254740993m));
            Console.WriteLine("[dbl-hi96]=" + TryDbl(7922816251426433759354395033.5m));
            Console.WriteLine("[sgl-12.5]=" + TrySgl(12.5m, 12.5f));
            Console.WriteLine("[sgl-0.1]=" + TrySgl(0.1m, 0.1f));
            Console.WriteLine("[sgl-neg0.1]=" + TrySgl(-0.1m, -0.1f));

            // ================= D1: Convert.ToDecimal(int-family) ==============
            Console.WriteLine("[dec-int]=42:" + Dec(Convert.ToDecimal(42)));
            Console.WriteLine("[dec-long]=7:" + Dec(Convert.ToDecimal(7L)));
            Console.WriteLine("[dec-byte]=7:" + Dec(Convert.ToDecimal((byte)7)));
            Console.WriteLine("[dec-sbyte]=-7:" + Dec(Convert.ToDecimal((sbyte)-7)));
            Console.WriteLine("[dec-short]=-7:" + Dec(Convert.ToDecimal((short)-7)));
            Console.WriteLine("[dec-ushort]=7:" + Dec(Convert.ToDecimal((ushort)7)));
            Console.WriteLine("[dec-uint]=4294967295:" + Dec(Convert.ToDecimal(4294967295u)));
            Console.WriteLine("[dec-ulong]=18446744073709551615:"
                + Dec(Convert.ToDecimal(18446744073709551615UL)));
            Console.WriteLine("[dec-bool-t]=1:" + Dec(Convert.ToDecimal(true)));
            Console.WriteLine("[dec-bool-f]=0:" + Dec(Convert.ToDecimal(false)));
            Console.WriteLine("[dec-int-min]=" + Dec(Convert.ToDecimal(int.MinValue)));

            // ================= D1: Convert.ToDecimal(double/float) ============
            Console.WriteLine("[dec-d3.5]=" + TryDecD(3.5));
            Console.WriteLine("[dec-d0.1]=" + TryDecD(0.1));
            Console.WriteLine("[dec-d-neg2.5]=" + TryDecD(-2.5));
            Console.WriteLine("[dec-d1e30]=" + TryDecD(1e30));
            Console.WriteLine("[dec-dnan]=" + TryDecD(double.NaN));
            Console.WriteLine("[dec-dinf]=" + TryDecD(double.PositiveInfinity));
            Console.WriteLine("[dec-dbits3.5]=" + Bits(Convert.ToDecimal(3.5)));
            Console.WriteLine("[dec-f3.5]=" + TryDecF(3.5f));
            Console.WriteLine("[dec-f0.1]=" + TryDecF(0.1f));
            Console.WriteLine("[dec-f1e30]=" + TryDecF(1e30f));

            // ================= D1: Convert.ToDecimal(string) ==================
            Console.WriteLine("[dec-s1.25]=" + TryDecS("1.25"));
            Console.WriteLine("[dec-snull]=" + TryDecS(null));
            Console.WriteLine("[dec-sempty]=" + TryDecS(""));
            Console.WriteLine("[dec-sws]=" + TryDecS(" 1.25 "));
            Console.WriteLine("[dec-sthousands]=" + TryDecS("1,234.5"));
            Console.WriteLine("[dec-sexp]=" + TryDecS("1e3"));
            Console.WriteLine("[dec-sjunk]=" + TryDecS("abc"));

            // ================= D3: OrderBy over a long[] source ===============
            long[] nums = { 3L, 1L, 2L };
            Console.WriteLine("[orderby-long]=" + JoinL(nums.OrderBy(longSelf).ToArray()));
            Console.WriteLine("[orderby-long-src]=" + JoinL(nums));
            Console.WriteLine("[orderbydesc-long]="
                + JoinL(nums.OrderByDescending(longSelf).ToArray()));

            long[] wide = { 5000000000L, -3L, 4294967296L, 7L };
            Console.WriteLine("[orderby-long-wide]=" + JoinL(wide.OrderBy(longSelf).ToArray()));

            long[] st = { 21L, 12L, 33L, 11L, 22L };
            Console.WriteLine("[orderby-long-intkey]=" + JoinL(st.OrderBy(longLow).ToArray()));

            Console.WriteLine("[orderby-long-empty]="
                + new long[0].OrderBy(longSelf).ToArray().Length);
            Console.WriteLine("[orderby-long-one]="
                + JoinL(new long[] { 9L }.OrderBy(longSelf).ToArray()));

            // int32 source still works (regression guard).
            int[] ints = { 3, 1, 2 };
            Console.WriteLine("[orderby-int]=" + JoinI(ints.OrderBy(intKey).ToArray()));
        }
    }
}
