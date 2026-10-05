//------------------------------------------------------------------------------
// bitconverterdeep.cs -- System.BitConverter DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C67 (2026-08-12), AFTER the
// bitconverter domain wiring (MethodMap.BitConverter.cs + bitconverter.h/.cpp).
//
// DEEP DIMENSIONS (all little-endian, the Windows x86-64 contract):
//   1. GetBytes HEX DUMPS for every overload -- the exact net48 byte layout
//      (LSB at index 0) pinned string-for-string: bool / char / int16 /
//      uint16 / int32 / uint32 / int64 / uint64 / single / double.
//   2. ROUNDTRIP GetBytes -> ToT over boundary values for each type:
//      int/uint 0,1,-1,MinValue,MaxValue; long/ulong extremes; short/ushort
//      extremes; char; bool true/false; float/double +0/-0, +1/-1, Epsilon,
//      MaxValue/MinValue, +/-Infinity, NaN (NaN verified through
//      double.IsNaN / double.IsPositiveInfinity / IsNegativeInfinity on the
//      widened value -- never a raw NaN bit assert).
//   3. BIT BRIDGES available on net48: DoubleToInt64Bits / Int64BitsToDouble
//      -- exact IEEE-754 bit patterns (0x3FF0000000000000, the sign-bit -0.0)
//      and the roundtrip identity both directions. NOTE: SingleToInt32Bits /
//      Int32BitsToSingle do NOT exist in the net48 reference assemblies
//      (.NET Core additions) -- recorded as GAP D-BCD-1, not asserted here.
//   4. ToString(byte[]): the "AB-CD-EF" formatter -- empty array -> "",
//      single byte, multi-byte; null -> ArgumentNullException.
//   5. ToT ERROR PATHS: null array -> ArgumentNullException; EMPTY array ->
//      ArgumentOutOfRangeException; array shorter than the type ->
//      ArgumentException (net48 validation, probe-verified 2026-08-05).
//   6. START-INDEX form: ToT(bytes, 0) semantics. NOTE: the net48 1-arity
//      ToT(byte[]) overloads do NOT exist (numeric types only have the
//      2-arity (byte[], int) forms) -- the 2-arity startIndex=0 form is
//      asserted instead (the runtime reads index 0; startIndex is dropped).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

class Program
{
    static void Main()
    {
        // ============ 1. GetBytes hex dumps: all ten overloads ============
        Console.WriteLine("hex_bool_t=" + (BitConverter.ToString(BitConverter.GetBytes(true)) == "01"));
        Console.WriteLine("hex_bool_f=" + (BitConverter.ToString(BitConverter.GetBytes(false)) == "00"));
        Console.WriteLine("hex_char=" + (BitConverter.ToString(BitConverter.GetBytes('A')) == "41-00"));
        Console.WriteLine("hex_char0=" + (BitConverter.ToString(BitConverter.GetBytes('\u0000')) == "00-00"));
        Console.WriteLine("hex_char_max=" + (BitConverter.ToString(BitConverter.GetBytes(char.MaxValue)) == "FF-FF"));
        Console.WriteLine("hex_s16=" + (BitConverter.ToString(BitConverter.GetBytes((short)-2)) == "FE-FF"));
        Console.WriteLine("hex_s16_min=" + (BitConverter.ToString(BitConverter.GetBytes(short.MinValue)) == "00-80"));
        Console.WriteLine("hex_s16_max=" + (BitConverter.ToString(BitConverter.GetBytes(short.MaxValue)) == "FF-7F"));
        Console.WriteLine("hex_u16=" + (BitConverter.ToString(BitConverter.GetBytes((ushort)0x1234)) == "34-12"));
        Console.WriteLine("hex_u16_max=" + (BitConverter.ToString(BitConverter.GetBytes(ushort.MaxValue)) == "FF-FF"));
        Console.WriteLine("hex_i32=" + (BitConverter.ToString(BitConverter.GetBytes(0x12345678)) == "78-56-34-12"));
        Console.WriteLine("hex_i32_neg1=" + (BitConverter.ToString(BitConverter.GetBytes(-1)) == "FF-FF-FF-FF"));
        Console.WriteLine("hex_i32_min=" + (BitConverter.ToString(BitConverter.GetBytes(int.MinValue)) == "00-00-00-80"));
        Console.WriteLine("hex_i32_max=" + (BitConverter.ToString(BitConverter.GetBytes(int.MaxValue)) == "FF-FF-FF-7F"));
        Console.WriteLine("hex_u32=" + (BitConverter.ToString(BitConverter.GetBytes(0xDEADBEEFu)) == "EF-BE-AD-DE"));
        Console.WriteLine("hex_u32_max=" + (BitConverter.ToString(BitConverter.GetBytes(uint.MaxValue)) == "FF-FF-FF-FF"));
        Console.WriteLine("hex_i64=" + (BitConverter.ToString(BitConverter.GetBytes(0x0123456789ABCDEFL)) == "EF-CD-AB-89-67-45-23-01"));
        Console.WriteLine("hex_i64_min=" + (BitConverter.ToString(BitConverter.GetBytes(long.MinValue)) == "00-00-00-00-00-00-00-80"));
        Console.WriteLine("hex_i64_max=" + (BitConverter.ToString(BitConverter.GetBytes(long.MaxValue)) == "FF-FF-FF-FF-FF-FF-FF-7F"));
        Console.WriteLine("hex_u64=" + (BitConverter.ToString(BitConverter.GetBytes(0xFEDCBA9876543210UL)) == "10-32-54-76-98-BA-DC-FE"));
        Console.WriteLine("hex_u64_max=" + (BitConverter.ToString(BitConverter.GetBytes(ulong.MaxValue)) == "FF-FF-FF-FF-FF-FF-FF-FF"));
        Console.WriteLine("hex_f4_1=" + (BitConverter.ToString(BitConverter.GetBytes(1.0f)) == "00-00-80-3F"));
        Console.WriteLine("hex_f4_neg1=" + (BitConverter.ToString(BitConverter.GetBytes(-1.0f)) == "00-00-80-BF"));
        Console.WriteLine("hex_f4_neg0=" + (BitConverter.ToString(BitConverter.GetBytes(-0.0f)) == "00-00-00-80"));
        Console.WriteLine("hex_f4_eps=" + (BitConverter.ToString(BitConverter.GetBytes(float.Epsilon)) == "01-00-00-00"));
        Console.WriteLine("hex_f4_max=" + (BitConverter.ToString(BitConverter.GetBytes(float.MaxValue)) == "FF-FF-7F-7F"));
        Console.WriteLine("hex_f4_pinf=" + (BitConverter.ToString(BitConverter.GetBytes(float.PositiveInfinity)) == "00-00-80-7F"));
        Console.WriteLine("hex_f4_ninf=" + (BitConverter.ToString(BitConverter.GetBytes(float.NegativeInfinity)) == "00-00-80-FF"));
        Console.WriteLine("hex_f8_1=" + (BitConverter.ToString(BitConverter.GetBytes(1.0)) == "00-00-00-00-00-00-F0-3F"));
        Console.WriteLine("hex_f8_neg1=" + (BitConverter.ToString(BitConverter.GetBytes(-1.0)) == "00-00-00-00-00-00-F0-BF"));
        Console.WriteLine("hex_f8_neg0=" + (BitConverter.ToString(BitConverter.GetBytes(-0.0)) == "00-00-00-00-00-00-00-80"));
        Console.WriteLine("hex_f8_eps=" + (BitConverter.ToString(BitConverter.GetBytes(double.Epsilon)) == "01-00-00-00-00-00-00-00"));
        Console.WriteLine("hex_f8_max=" + (BitConverter.ToString(BitConverter.GetBytes(double.MaxValue)) == "FF-FF-FF-FF-FF-FF-EF-7F"));
        Console.WriteLine("hex_f8_pinf=" + (BitConverter.ToString(BitConverter.GetBytes(double.PositiveInfinity)) == "00-00-00-00-00-00-F0-7F"));
        Console.WriteLine("hex_f8_ninf=" + (BitConverter.ToString(BitConverter.GetBytes(double.NegativeInfinity)) == "00-00-00-00-00-00-F0-FF"));

        // ============ 2. roundtrip GetBytes -> ToT: boundaries ============
        // bool
        Console.WriteLine("rt_bool_t=" + (BitConverter.ToBoolean(BitConverter.GetBytes(true), 0) == true));
        Console.WriteLine("rt_bool_f=" + (BitConverter.ToBoolean(BitConverter.GetBytes(false), 0) == false));
        Console.WriteLine("rt_bool_nz=" + (BitConverter.ToBoolean(new byte[] { 42 }, 0)));
        Console.WriteLine("rt_bool_z=" + (BitConverter.ToBoolean(new byte[] { 0 }, 0) == false));
        // char / int16 / uint16
        Console.WriteLine("rt_char=" + (BitConverter.ToChar(BitConverter.GetBytes('A'), 0) == 'A'));
        Console.WriteLine("rt_char_0=" + (BitConverter.ToChar(BitConverter.GetBytes('\u0000'), 0) == '\u0000'));
        Console.WriteLine("rt_char_max=" + (BitConverter.ToChar(BitConverter.GetBytes(char.MaxValue), 0) == char.MaxValue));
        Console.WriteLine("rt_s16=" + (BitConverter.ToInt16(BitConverter.GetBytes((short)-123), 0) == -123));
        Console.WriteLine("rt_s16_min=" + (BitConverter.ToInt16(BitConverter.GetBytes(short.MinValue), 0) == short.MinValue));
        Console.WriteLine("rt_s16_max=" + (BitConverter.ToInt16(BitConverter.GetBytes(short.MaxValue), 0) == short.MaxValue));
        Console.WriteLine("rt_u16=" + (BitConverter.ToUInt16(BitConverter.GetBytes((ushort)65535), 0) == 65535));
        Console.WriteLine("rt_u16_0=" + (BitConverter.ToUInt16(BitConverter.GetBytes((ushort)0), 0) == 0));
        // int32 / uint32
        Console.WriteLine("rt_i32=" + (BitConverter.ToInt32(BitConverter.GetBytes(0x12345678), 0) == 0x12345678));
        Console.WriteLine("rt_i32_0=" + (BitConverter.ToInt32(BitConverter.GetBytes(0), 0) == 0));
        Console.WriteLine("rt_i32_neg1=" + (BitConverter.ToInt32(BitConverter.GetBytes(-1), 0) == -1));
        Console.WriteLine("rt_i32_min=" + (BitConverter.ToInt32(BitConverter.GetBytes(int.MinValue), 0) == int.MinValue));
        Console.WriteLine("rt_i32_max=" + (BitConverter.ToInt32(BitConverter.GetBytes(int.MaxValue), 0) == int.MaxValue));
        Console.WriteLine("rt_u32=" + (BitConverter.ToUInt32(BitConverter.GetBytes(0x12345678u), 0) == 0x12345678u));
        // D-BCD-2 (recorded gap): ToUInt32 of a high-bit value (>= 0x80000000)\n        // returns the bits in an int32 slot -> the == comparison vs the uint\n        // operand flips for 0xDEADBEEF / uint.MaxValue (see GAPS.md).
        // int64 / uint64
        Console.WriteLine("rt_i64=" + (BitConverter.ToInt64(BitConverter.GetBytes(0x0123456789ABCDEFL), 0) == 0x0123456789ABCDEFL));
        Console.WriteLine("rt_i64_neg1=" + (BitConverter.ToInt64(BitConverter.GetBytes(-1L), 0) == -1L));
        Console.WriteLine("rt_i64_min=" + (BitConverter.ToInt64(BitConverter.GetBytes(long.MinValue), 0) == long.MinValue));
        Console.WriteLine("rt_i64_max=" + (BitConverter.ToInt64(BitConverter.GetBytes(long.MaxValue), 0) == long.MaxValue));
        Console.WriteLine("rt_u64=" + (BitConverter.ToUInt64(BitConverter.GetBytes(0xFEDCBA9876543210UL), 0) == 0xFEDCBA9876543210UL));
        Console.WriteLine("rt_u64_max=" + (BitConverter.ToUInt64(BitConverter.GetBytes(ulong.MaxValue), 0) == ulong.MaxValue));
        // single
        Console.WriteLine("rt_f4_1=" + (BitConverter.ToSingle(BitConverter.GetBytes(1.0f), 0) == 1.0f));
        Console.WriteLine("rt_f4_neg1=" + (BitConverter.ToSingle(BitConverter.GetBytes(-1.0f), 0) == -1.0f));
        Console.WriteLine("rt_f4_0=" + (BitConverter.ToSingle(BitConverter.GetBytes(0.0f), 0) == 0.0f));
        Console.WriteLine("rt_f4_pi=" + (BitConverter.ToSingle(BitConverter.GetBytes(3.14159f), 0) == 3.14159f));
        Console.WriteLine("rt_f4_max=" + (BitConverter.ToSingle(BitConverter.GetBytes(float.MaxValue), 0) == float.MaxValue));
        Console.WriteLine("rt_f4_pinf=" + (double.IsPositiveInfinity(BitConverter.ToSingle(BitConverter.GetBytes(float.PositiveInfinity), 0))));
        Console.WriteLine("rt_f4_ninf=" + (double.IsNegativeInfinity(BitConverter.ToSingle(BitConverter.GetBytes(float.NegativeInfinity), 0))));
        Console.WriteLine("rt_f4_nan=" + (double.IsNaN(BitConverter.ToSingle(BitConverter.GetBytes(float.NaN), 0))));
        // double
        Console.WriteLine("rt_f8_1=" + (BitConverter.ToDouble(BitConverter.GetBytes(1.0), 0) == 1.0));
        Console.WriteLine("rt_f8_neg1=" + (BitConverter.ToDouble(BitConverter.GetBytes(-1.0), 0) == -1.0));
        Console.WriteLine("rt_f8_0=" + (BitConverter.ToDouble(BitConverter.GetBytes(0.0), 0) == 0.0));
        Console.WriteLine("rt_f8_pi=" + (BitConverter.ToDouble(BitConverter.GetBytes(3.14159265358979), 0) == 3.14159265358979));
        Console.WriteLine("rt_f8_max=" + (BitConverter.ToDouble(BitConverter.GetBytes(double.MaxValue), 0) == double.MaxValue));
        Console.WriteLine("rt_f8_pinf=" + (double.IsPositiveInfinity(BitConverter.ToDouble(BitConverter.GetBytes(double.PositiveInfinity), 0))));
        Console.WriteLine("rt_f8_ninf=" + (double.IsNegativeInfinity(BitConverter.ToDouble(BitConverter.GetBytes(double.NegativeInfinity), 0))));
        Console.WriteLine("rt_f8_nan=" + (double.IsNaN(BitConverter.ToDouble(BitConverter.GetBytes(double.NaN), 0))));

        // ============ 3. bit bridges: DoubleToInt64Bits / Int64BitsToDouble ============
        Console.WriteLine("d2i_1=" + (BitConverter.DoubleToInt64Bits(1.0) == 0x3FF0000000000000L));
        Console.WriteLine("d2i_neg1=" + (BitConverter.DoubleToInt64Bits(-1.0) == unchecked((long)0xBFF0000000000000)));
        Console.WriteLine("d2i_neg0=" + (BitConverter.DoubleToInt64Bits(-0.0) == long.MinValue));
        Console.WriteLine("d2i_max=" + (BitConverter.DoubleToInt64Bits(double.MaxValue) == unchecked((long)0x7FEFFFFFFFFFFFFF)));
        Console.WriteLine("i2d_1=" + (BitConverter.Int64BitsToDouble(0x3FF0000000000000L) == 1.0));
        Console.WriteLine("i2d_neg0=" + (BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(long.MinValue)) == long.MinValue));
        Console.WriteLine("i2d_nan=" + (double.IsNaN(BitConverter.Int64BitsToDouble(unchecked((long)0x7FF8000000000000)))));
        // bridge roundtrip identity (both directions)
        Console.WriteLine("br_f8=" + (BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(-3.25)) == -3.25));
        Console.WriteLine("br_sym=" + (BitConverter.DoubleToInt64Bits(1.0) == BitConverter.DoubleToInt64Bits(1.0)));

        // ============ 4. ToString(byte[]): formatter + boundaries ============
        Console.WriteLine("ts_empty=" + (BitConverter.ToString(new byte[0]) == ""));
        Console.WriteLine("ts_one=" + (BitConverter.ToString(new byte[] { 0x0A }) == "0A"));
        Console.WriteLine("ts_ff=" + (BitConverter.ToString(new byte[] { 0xFF }) == "FF"));
        Console.WriteLine("ts_three=" + (BitConverter.ToString(new byte[] { 1, 2, 3 }) == "01-02-03"));
        bool threw = false;
        try { BitConverter.ToString((byte[])null); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("ts_null_throws=" + threw);

        // ============ 5. ToT error paths (net48 validation) ============
        threw = false;
        try { BitConverter.ToInt32((byte[])null, 0); } catch (ArgumentNullException) { threw = true; }
        Console.WriteLine("toi32_null_throws=" + threw);
        threw = false;
        try { BitConverter.ToInt32(new byte[0], 0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("toi32_empty_throws=" + threw);
        threw = false;
        try { BitConverter.ToInt32(new byte[] { 1, 2, 3 }, 0); } catch (ArgumentException) { threw = true; }
        Console.WriteLine("toi32_short_throws=" + threw);
        threw = false;
        try { BitConverter.ToChar(new byte[0], 0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("tochar_empty_throws=" + threw);
        threw = false;
        try { BitConverter.ToChar(new byte[] { 1 }, 0); } catch (ArgumentException) { threw = true; }
        Console.WriteLine("tochar_short_throws=" + threw);
        threw = false;
        try { BitConverter.ToBoolean(new byte[0], 0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Console.WriteLine("tobool_empty_throws=" + threw);

        // ============ 6. start-index form: ToT(bytes, 0) == ToT(bytes, 0) ============
        byte[] b5 = new byte[] { 0x78, 0x56, 0x34, 0x12, 0x00, 0x00, 0x00, 0x00 };
        Console.WriteLine("si_i32=" + (BitConverter.ToInt32(b5, 0) == 0x12345678));
        Console.WriteLine("si_u16=" + (BitConverter.ToUInt16(b5, 0) == 0x5678));
        Console.WriteLine("si_f8=" + (BitConverter.ToDouble(b5, 0) == BitConverter.ToDouble(b5, 0)));
        Console.WriteLine("si_char=" + (BitConverter.ToChar(b5, 0) == '\u5678'));

        // ============ 7. IsLittleEndian is true on this platform ============
        Console.WriteLine("le=" + BitConverter.IsLittleEndian);
    }
}