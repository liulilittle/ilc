//------------------------------------------------------------------------------
// decimalhash3.cs -- (double)decimal BIT-PATTERN probe (e2e).
//
// The decimal hash is defined in terms of the IEEE-754 bits of (double)d, so a
// wrong decimal->double conversion silently corrupts the hash too. This probe
// pins the conversion itself.
//
// DEFECT THIS PINS: the old runtime computed M / std::pow(10, scale), which is
// off by 1 ulp for 3.26% of random decimals (65,136 of 2,000,000 in the net48
// probe) and wrong at scales >= 23 even for simple values. net48 computes
// M / T10[scale] with T10 the CORRECTLY ROUNDED powers of ten.
//
// The bits are printed as 16 hex digits (BitConverter.DoubleToInt64Bits), which
// is exact and therefore diff-safe; one bool per value asserts the net48 bits.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class DecimalHash3 {
    private static string Bits(decimal d) {
        return BitConverter.DoubleToInt64Bits((double)d).ToString("X16", CultureInfo.InvariantCulture);
    }
    private static void P(string id, decimal d) {
        Console.WriteLine(id + "=" + Bits(d));
    }
    public static void Main() {
        // ---- 1. integers / exact ----
        P("b00", 0m);
        P("b01", 1m);
        P("b02", -1m);
        P("b03", 100m);
        P("b04", 1234m);
        P("b05", 1.5m);
        P("b06", -1.5m);
        P("b07", 0.5m);
        P("b08", 0.125m);
        P("b09", 9007199254740992m);
        P("b10", 9007199254740993m);   // NOT representable: rounds to the same double
        P("b11", 18446744073709551616m);

        // ---- 2. inexact at small scales (the old form was already right here) ----
        P("b12", 0.1m);
        P("b13", 1.1m);
        P("b14", 1234.5678m);
        P("b15", -1234.5678m);
        P("b16", 123.456m);

        // ---- 3. scale 10..28 ----
        P("b17", 0.0000000001m);
        P("b18", 0.0000000000000000000000000001m);
        P("b19", 0.0000000000000000000000000002m);
        P("b20", 1.0000000000000000000000000001m);

        // ---- 4. scales where pow(10,s) stops being exact (23..27) ----
        P("b21", 1.234567890123456789012345m);
        P("b22", 1.2345678901234567890123456m);
        P("b23", 9.8765432109876543210987654m);

        // ---- 5. 96-bit mantissas / max / min ----
        P("b24", decimal.MaxValue);
        P("b25", decimal.MinValue);
        P("b26", 123456789012345678901234567m);
        P("b27", 1234567890123456789012345678m);
        P("b28", 12345678901234567890123456789m);
        P("b29", 10000000000000000000000000000m);
        P("b30", 3.1415926535897932384626433832m);
        P("b31", 2.7182818284590452353602874713m);

        // ---- 6. negative zero keeps the sign bit ----
        P("b32", -0.0m);
        P("b33", decimal.Negate(0m));

        // ---- 7. bool assertions against the net48 oracle bits ----
        Console.WriteLine("k1=" + (Bits(1.5m) == "3FF8000000000000"));
        Console.WriteLine("k2=" + (Bits(0.1m) == "3FB999999999999A"));
        Console.WriteLine("k3=" + (Bits(1234.5678m) == "40934A456D5CFAAD"));
        Console.WriteLine("k4=" + (Bits(decimal.MaxValue) == "45F0000000000000"));
        Console.WriteLine("k5=" + (Bits(1m) == "3FF0000000000000"));
        Console.WriteLine("k6=" + (Bits(-1.5m) == "BFF8000000000000"));
        Console.WriteLine("k7=" + (Bits(0.0000000000000000000000000001m) == "3A1FB0F6BE50601A"));
        Console.WriteLine("k8=" + (Bits(-0.0m) == "8000000000000000"));
        Console.WriteLine("k9=" + (Bits(9007199254740992m) == Bits(9007199254740993m)));
        // 1.2345678901234567890123456m: net48 rounds DOWN to ...59FB, the old
        // form rounded UP to ...59FC (the 1-ulp divergence at scale 25).
        Console.WriteLine("k10=" + (Bits(1.2345678901234567890123456m) == "3FF3C0CA428C59FB"));
        Console.WriteLine("k11=" + (Bits(0.0000000001m) == "3DDB7CDFD9D7BDBB"));
        Console.WriteLine("k12=" + (Bits(3.1415926535897932384626433832m) == "400921FB54442D18"));
        // Convert.ToDouble must agree with the cast (both go through the same icall)
        Console.WriteLine("k13=" + (Bits(Convert.ToDecimal(0.1)) == Bits(0.1m)));
    } } }
