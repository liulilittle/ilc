//------------------------------------------------------------------------------
// conv-arity.cs -- System.Convert arity/param-family e2e reference case
// (net48 oracle, byte-for-byte). Created in batch 9 to pin the statically
// analyzable LOUD residuals that were eliminated:
//   1) ToString(byte/short/int/long, int toBase) -- the radix render icalls.
//   2) ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64/ToInt64(string, int
//      fromBase) -- the radix parse icalls.
//   3) The 2-arg IFormatProvider forms of ToInt32/ToInt64/ToDouble/ToBoolean/
//      ToChar/ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64/ToSingle.
//   4) Convert.ToChar(decimal) / ToChar(DateTime) -- InvalidCastException.
// Every printed line is "label=True/False" or an exception-type catch probe.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ConvArityCase {
    public static void Main() {
        // ===== 1. ToString(value, int toBase) =====
        Console.WriteLine("TB2_255=" + (Convert.ToString((byte)255, 2) == "11111111"));
        Console.WriteLine("TB8_255=" + (Convert.ToString((byte)255, 8) == "377"));
        Console.WriteLine("TB10_255=" + (Convert.ToString((byte)255, 10) == "255"));
        Console.WriteLine("TB16_255=" + (Convert.ToString((byte)255, 16) == "ff"));
        Console.WriteLine("TB16_0=" + (Convert.ToString((byte)0, 16) == "0"));
        Console.WriteLine("TS2_neg1=" + (Convert.ToString((short)-1, 2) == "1111111111111111"));
        Console.WriteLine("TS8_neg1=" + (Convert.ToString((short)-1, 8) == "177777"));
        Console.WriteLine("TS10_neg1=" + (Convert.ToString((short)-1, 10) == "-1"));
        Console.WriteLine("TS16_neg1=" + (Convert.ToString((short)-1, 16) == "ffff"));
        Console.WriteLine("TS16_max=" + (Convert.ToString((short)32767, 16) == "7fff"));
        Console.WriteLine("TS10_max=" + (Convert.ToString((short)32767, 10) == "32767"));
        Console.WriteLine("TL2_neg1=" + (Convert.ToString((long)-1, 2) == "1111111111111111111111111111111111111111111111111111111111111111"));
        Console.WriteLine("TL8_neg1=" + (Convert.ToString((long)-1, 8) == "1777777777777777777777"));
        Console.WriteLine("TL10_neg1=" + (Convert.ToString((long)-1, 10) == "-1"));
        Console.WriteLine("TL16_neg1=" + (Convert.ToString((long)-1, 16) == "ffffffffffffffff"));
        Console.WriteLine("TL16_min=" + (Convert.ToString(long.MinValue, 16) == "8000000000000000"));
        Console.WriteLine("TL10_min=" + (Convert.ToString(long.MinValue, 10) == "-9223372036854775808"));
        Console.WriteLine("TI2_max=" + (Convert.ToString(int.MaxValue, 2) == "1111111111111111111111111111111"));
        Console.WriteLine("TI16_max=" + (Convert.ToString(int.MaxValue, 16) == "7fffffff"));
        // ===== 2. ToXxx(string, int fromBase) =====
        Console.WriteLine("FB2=" + (Convert.ToByte("11111111", 2) == 255));
        Console.WriteLine("FB10=" + (Convert.ToByte("255", 10) == 255));
        Console.WriteLine("FB16=" + (Convert.ToByte("ff", 16) == 255));
        Console.WriteLine("FSB2=" + (Convert.ToSByte("10000000", 2) == -128));
        Console.WriteLine("FSB10=" + (Convert.ToSByte("-128", 10) == -128));
        Console.WriteLine("FSB16=" + (Convert.ToSByte("ff", 16) == -1));
        Console.WriteLine("FI2_2=" + (Convert.ToInt16("1000000000000000", 2) == -32768));
        Console.WriteLine("FI2_10=" + (Convert.ToInt16("-32768", 10) == -32768));
        Console.WriteLine("FI2_16=" + (Convert.ToInt16("ffff", 16) == -1));
        Console.WriteLine("FU2_2=" + (Convert.ToUInt16("1111111111111111", 2) == 65535));
        Console.WriteLine("FU2_10=" + (Convert.ToUInt16("65535", 10) == 65535));
        Console.WriteLine("FU4_2=" + (Convert.ToUInt32("11111111111111111111111111111111", 2) == 4294967295u));
        Console.WriteLine("FU4_10=" + (Convert.ToUInt32("4294967295", 10) == 4294967295u));
        Console.WriteLine("FL_2=" + (Convert.ToInt64("1000000000000000000000000000000000000000000000000000000000000000", 2) == long.MinValue));
        Console.WriteLine("FL_10=" + (Convert.ToInt64("-9223372036854775808", 10) == long.MinValue));
        Console.WriteLine("FL_16=" + (Convert.ToInt64("ffffffffffffffff", 16) == -1));
        Console.WriteLine("FUL_2=" + (Convert.ToUInt64("1111111111111111111111111111111111111111111111111111111111111111", 2) == ulong.MaxValue));
        Console.WriteLine("FUL_10=" + (Convert.ToUInt64("18446744073709551615", 10) == ulong.MaxValue));
        // ===== 3. 2-arg IFormatProvider forms =====
        Console.WriteLine("PV_TI32=" + (Convert.ToInt32("42", null) == 42));
        Console.WriteLine("PV_TI64=" + (Convert.ToInt64("42", null) == 42));
        Console.WriteLine("PV_TD=" + (Convert.ToDouble("1.5", null) == 1.5));
        Console.WriteLine("PV_TB=" + Convert.ToBoolean("true", null));
        Console.WriteLine("PV_TC=" + (Convert.ToChar("A", null) == 'A'));
        Console.WriteLine("PV_TBy=" + (Convert.ToByte("42", null) == 42));
        Console.WriteLine("PV_TS=" + (Convert.ToSingle("1.5", null) == 1.5f));
        Console.WriteLine("PV_TU4=" + (Convert.ToUInt32("42", null) == 42u));
        Console.WriteLine("PV_TU8=" + (Convert.ToUInt64("42", null) == 42ul));
        Console.WriteLine("PV_TI2=" + (Convert.ToInt16("42", null) == 42));
        Console.WriteLine("PV_TU2=" + (Convert.ToUInt16("42", null) == 42));
        Console.WriteLine("PV_TSB=" + (Convert.ToSByte("42", null) == 42));
        // ===== 4. ToChar(decimal) / ToChar(DateTime) -- InvalidCastException =====
        try { Convert.ToChar(1.5m); Console.WriteLine("TCDec:ok"); } catch (Exception e) { Console.WriteLine("TCDec:" + e.GetType().Name); }
        try { Convert.ToChar(new DateTime(2020, 1, 1)); Console.WriteLine("TCDT:ok"); } catch (Exception e) { Console.WriteLine("TCDT:" + e.GetType().Name); }
    }
}}
