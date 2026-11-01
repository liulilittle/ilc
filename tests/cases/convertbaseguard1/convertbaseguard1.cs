//------------------------------------------------------------------------------
// convertbaseguard1.cs -- Convert.To*STRING, fromBase) RANGE GUARD contract
// (net48 oracle, byte-for-byte).
//
// Regression target: the runtime comment in il2cpp_parse.h / parse.cpp claimed
// "invalid fromBase returns 0 instead of throwing". MEASURED net48: EVERY
// Convert.To*(string, fromBase) overload throws ArgumentException for a base
// outside the legal set. The legal set is {2, 8, 10, 16} -- NOT "2..36":
// bases 1, 3, 17 and 36 all throw, and so do 0, 4..7, 9, 11..15, 18..35, 37.
//
// Asserted contract:
//   * Convert.ToInt32/ToInt64/ToSByte/ToByte/ToInt16/ToUInt16/ToUInt32/
//     ToUInt64("10", b): value for b in {2,8,10,16}; ArgumentException for
//     EVERY b outside that set (the full 0..37 sweep here).
//   * Convert.ToString(value, toBase) shares the same guard (ArgumentException
//     for any base outside {2,8,10,16}).
//   * the radix check runs BEFORE the null check: null text with an illegal
//     base still throws ArgumentException; null with a legal base returns 0.
//   * inside a legal base, the digit/sign/prefix rules still apply
//     (FormatException for a non-digit, ArgumentException for '-' outside
//     base 10, FormatException for whitespace).
//
// EVERY line prints "label=value" with the CAUGHT TYPE NAME, so a silent
// "returns 0" can never be mistaken for the exception.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ConvertBaseGuard1
    {
        static string F(Func<string> f)
        {
            try { return "v:" + f(); }
            catch (Exception e) { return "T:" + e.GetType().Name; }
        }

        static void Base(string label, string text, int b)
        {
            Console.WriteLine(label + ".i32=" + F(() => Convert.ToInt32(text, b).ToString()));
            Console.WriteLine(label + ".i64=" + F(() => Convert.ToInt64(text, b).ToString()));
            Console.WriteLine(label + ".i16=" + F(() => Convert.ToInt16(text, b).ToString()));
            Console.WriteLine(label + ".u16=" + F(() => Convert.ToUInt16(text, b).ToString()));
            Console.WriteLine(label + ".i8=" + F(() => Convert.ToSByte(text, b).ToString()));
            Console.WriteLine(label + ".u8=" + F(() => Convert.ToByte(text, b).ToString()));
            Console.WriteLine(label + ".u32=" + F(() => Convert.ToUInt32(text, b).ToString()));
            Console.WriteLine(label + ".u64=" + F(() => Convert.ToUInt64(text, b).ToString()));
        }

        public static void Main()
        {
            // ---- 1. the FULL base sweep 0..37 on "10" (the contract core) ----
            for (int b = 0; b <= 37; b++)
            {
                Console.WriteLine("b" + b + ".i32=" + F(() => Convert.ToInt32("10", b).ToString()));
            }

            // ---- 2. the illegal bases across EVERY To*(string, base) width ----
            Base("illegal.0", "10", 0);
            Base("illegal.1", "10", 1);
            Base("illegal.3", "10", 3);
            Base("illegal.4", "10", 4);
            Base("illegal.5", "10", 5);
            Base("illegal.6", "10", 6);
            Base("illegal.7", "10", 7);
            Base("illegal.9", "10", 9);
            Base("illegal.11", "10", 11);
            Base("illegal.12", "10", 12);
            Base("illegal.15", "10", 15);
            Base("illegal.17", "10", 17);
            Base("illegal.18", "10", 18);
            Base("illegal.20", "10", 20);
            Base("illegal.24", "10", 24);
            Base("illegal.32", "10", 32);
            Base("illegal.35", "10", 35);
            Base("illegal.36", "10", 36);
            Base("illegal.37", "10", 37);
            Base("illegal.-1", "10", -1);
            Base("illegal.100", "10", 100);

            // ---- 3. the four LEGAL bases still parse ----
            Base("legal.2", "10", 2);
            Base("legal.8", "10", 8);
            Base("legal.10", "10", 10);
            Base("legal.16", "10", 16);

            // ---- 4. radix checked BEFORE the null text ----
            Console.WriteLine("null.b3.i32=" + F(() => Convert.ToInt32((string)null, 3).ToString()));
            Console.WriteLine("null.b1.i32=" + F(() => Convert.ToInt32((string)null, 1).ToString()));
            Console.WriteLine("null.b17.i32=" + F(() => Convert.ToInt32((string)null, 17).ToString()));
            Console.WriteLine("null.b36.i32=" + F(() => Convert.ToInt32((string)null, 36).ToString()));
            Console.WriteLine("null.b37.i32=" + F(() => Convert.ToInt32((string)null, 37).ToString()));
            Console.WriteLine("null.b0.i64=" + F(() => Convert.ToInt64((string)null, 0).ToString()));
            Console.WriteLine("null.b3.u8=" + F(() => Convert.ToByte((string)null, 3).ToString()));
            Console.WriteLine("null.b2.i32=" + F(() => Convert.ToInt32((string)null, 2).ToString()));
            Console.WriteLine("null.b8.i32=" + F(() => Convert.ToInt32((string)null, 8).ToString()));
            Console.WriteLine("null.b10.i32=" + F(() => Convert.ToInt32((string)null, 10).ToString()));
            Console.WriteLine("null.b16.i32=" + F(() => Convert.ToInt32((string)null, 16).ToString()));

            // ---- 5. Convert.ToString(value, toBase) shares the guard ----
            for (int b = 0; b <= 37; b++)
            {
                Console.WriteLine("ts" + b + "=" + F(() => Convert.ToString(42, b)));
            }
            Console.WriteLine("ts.byte.3=" + F(() => Convert.ToString((byte)42, 3)));
            Console.WriteLine("ts.short.3=" + F(() => Convert.ToString((short)42, 3)));
            Console.WriteLine("ts.long.3=" + F(() => Convert.ToString(42L, 3)));
            Console.WriteLine("ts.byte.16=" + F(() => Convert.ToString((byte)42, 16)));
            Console.WriteLine("ts.short.16=" + F(() => Convert.ToString((short)42, 16)));
            Console.WriteLine("ts.long.16=" + F(() => Convert.ToString(42L, 16)));

            // ---- 6. inside a legal base the other rules are unchanged ----
            Console.WriteLine("ok.hex=" + F(() => Convert.ToInt32("ff", 16).ToString()));
            Console.WriteLine("ok.hex.upper=" + F(() => Convert.ToInt32("FF", 16).ToString()));
            Console.WriteLine("ok.hex.0x=" + F(() => Convert.ToInt32("0x10", 16).ToString()));
            Console.WriteLine("ok.oct=" + F(() => Convert.ToInt32("377", 8).ToString()));
            Console.WriteLine("ok.bin=" + F(() => Convert.ToInt32("101010", 2).ToString()));
            Console.WriteLine("ok.bin.plus=" + F(() => Convert.ToInt32("+10", 2).ToString()));
            Console.WriteLine("ok.dec.min=" + F(() => Convert.ToInt32("-2147483648", 10).ToString()));
            Console.WriteLine("ok.hex.wrap=" + F(() => Convert.ToInt32("ffffffff", 16).ToString()));
            Console.WriteLine("ok.hex.min=" + F(() => Convert.ToInt32("80000000", 16).ToString()));
            Console.WriteLine("bad.digit16=" + F(() => Convert.ToInt32("1G", 16).ToString()));
            Console.WriteLine("bad.digit2=" + F(() => Convert.ToInt32("102", 2).ToString()));
            Console.WriteLine("bad.digit8=" + F(() => Convert.ToInt32("8", 8).ToString()));
            Console.WriteLine("bad.neg2=" + F(() => Convert.ToInt32("-10", 2).ToString()));
            Console.WriteLine("bad.neg8=" + F(() => Convert.ToInt32("-10", 8).ToString()));
            Console.WriteLine("bad.neg16=" + F(() => Convert.ToInt32("-10", 16).ToString()));
            Console.WriteLine("bad.ws10=" + F(() => Convert.ToInt32(" 10", 10).ToString()));
            Console.WriteLine("bad.ws16=" + F(() => Convert.ToInt32(" 10", 16).ToString()));
            Console.WriteLine("bad.empty16=" + F(() => Convert.ToInt32("", 16).ToString()));
            Console.WriteLine("bad.0x2=" + F(() => Convert.ToInt32("0x10", 2).ToString()));
            Console.WriteLine("bad.0x10=" + F(() => Convert.ToInt32("0x10", 10).ToString()));
            Console.WriteLine("bad.ovf16=" + F(() => Convert.ToInt32("100000000", 16).ToString()));
            Console.WriteLine("bad.ovf10=" + F(() => Convert.ToInt32("2147483648", 10).ToString()));
        }
    }
}
