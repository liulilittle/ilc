//------------------------------------------------------------------------------
// decimalfloat1.cs -- (float)decimal BIT-PATTERN probe (e2e).
//
// Separate from decimalhash3 on purpose (one case, one subject): decimal->float
// may in principle use its own oleaut conversion (VarR4FromDec) instead of
// narrowing the double, so it is pinned independently.
//
// PROBE RESULT ON net48 (tests/cases/dhashprobe/dhashfit5.cs, 500,000 random
// decimals): (float)d is bit-identical to (float)((double)d) in EVERY sample,
// and also identical to the correctly rounded exact value. So the runtime needs
// no separate VarR4FromDec path; this case freezes that finding so a future
// "optimization" of to_float cannot silently drift.
//
// Bits are printed as 8 hex digits (BitConverter.GetBytes + ToInt32), exact and
// diff-safe. Scales 0..28, exact and inexact values, negatives, max/min.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class DecimalFloat1 {
    private static string Bits(decimal d) {
        return BitConverter.ToInt32(BitConverter.GetBytes((float)d), 0)
            .ToString("X8", CultureInfo.InvariantCulture);
    }
    private static void P(string id, decimal d) {
        Console.WriteLine(id + "=" + Bits(d));
    }
    public static void Main() {
        // ---- 1. integers / exact ----
        P("f00", 0m);
        P("f01", 1m);
        P("f02", -1m);
        P("f03", 100m);
        P("f04", 1234m);
        P("f05", 1.5m);
        P("f06", -1.5m);
        P("f07", 0.5m);
        P("f08", 0.125m);
        P("f09", 16777216m);   // 2^24: exactly representable as float
        P("f10", 16777217m);   // 2^24+1: rounds back to 2^24
        P("f11", 16777215m);

        // ---- 2. inexact ----
        P("f12", 0.1m);
        P("f13", 1.1m);
        P("f14", 1234.5678m);
        P("f15", -1234.5678m);
        P("f16", 123.456m);

        // ---- 3. scale 10..28 ----
        P("f17", 0.0000000001m);
        P("f18", 0.0000000000000000000000000001m);
        P("f19", 0.0000000000000000000000000002m);
        P("f20", 0.0000000000000000000000000005m);
        P("f21", 1.0000000000000000000000000001m);

        // ---- 4. 96-bit mantissas / max / min ----
        P("f22", decimal.MaxValue);
        P("f23", decimal.MinValue);
        P("f24", 123456789012345678901234567m);
        P("f25", 1234567890123456789012345678m);
        P("f26", 12345678901234567890123456789m);
        P("f27", 10000000000000000000000000000m);
        P("f28", 3.1415926535897932384626433832m);
        P("f29", 2.7182818284590452353602874713m);

        // ---- 5. negative zero ----
        P("f30", -0.0m);
        P("f31", decimal.Negate(0m));

        // ---- 6. bool assertions against the net48 oracle bits ----
        Console.WriteLine("g1=" + (Bits(1.5m) == "3FC00000"));
        Console.WriteLine("g2=" + (Bits(0.1m) == "3DCCCCCD"));
        Console.WriteLine("g3=" + (Bits(1234.5678m) == "449A522B"));
        Console.WriteLine("g4=" + (Bits(decimal.MaxValue) == "6F800000"));
        Console.WriteLine("g5=" + (Bits(decimal.MinValue) == "EF800000"));
        Console.WriteLine("g6=" + (Bits(16777216m) == "4B800000"));
        Console.WriteLine("g7=" + (Bits(16777217m) == "4B800000"));
        Console.WriteLine("g8=" + (Bits(16777215m) == "4B7FFFFF"));
        Console.WriteLine("g9=" + (Bits(0.0000000000000000000000000001m) == "10FD87B6"));
        Console.WriteLine("g10=" + (Bits(-0.0m) == "80000000"));
        Console.WriteLine("g11=" + (Bits(3.1415926535897932384626433832m) == "40490FDB"));
        // the float cast must be the narrowing of the double cast (no separate path)
        Console.WriteLine("g12=" + (Bits(1.2345678901234567890123456789m) == "3F9E0652"));
        // Convert.ToSingle goes through the same conversion
        Console.WriteLine("g13=" + (BitConverter.ToInt32(BitConverter.GetBytes(Convert.ToSingle(0.1m)), 0) ==
                                    BitConverter.ToInt32(BitConverter.GetBytes(0.1f), 0)));
    } } }
