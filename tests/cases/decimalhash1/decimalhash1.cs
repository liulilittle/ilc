//------------------------------------------------------------------------------
// decimalhash1.cs -- decimal.GetHashCode() net48 VALUE probe (e2e).
//
// DEFECT THIS PINS (silent wrong value, HIGH): the runtime used .NET Core's
// DecCalc.GetHashCode (unscale + xor of the four dwords) while the project's
// semantic baseline is .NET Framework 4.8, whose hash is
//   mantissa == 0 ? 0 : (int)((doubleBits >> 32) ^ (int)(doubleBits & 0xFFFFFFF0))
// The two disagree on almost every value (1.5m: 1073217536 vs 65551).
//
// Every line prints the raw hash (an int) so a mismatch is visible as a value,
// plus one bool per group asserting the net48 constant. The expected.txt is
// the net48 oracle run (difftest compares byte-for-byte).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class DecimalHash1 {
    private static void P(string id, decimal d) {
        Console.WriteLine(id + "=" + d.GetHashCode().ToString(CultureInfo.InvariantCulture));
    }
    public static void Main() {
        // ---- 1. scale 0 (integers; the old algorithm returned the value) ----
        P("h00", 0m);
        P("h01", 1m);
        P("h02", -1m);
        P("h03", 2m);
        P("h04", 100m);
        P("h05", 1234m);

        // ---- 2. exactly representable in binary ----
        P("h06", 1.5m);
        P("h07", 2.5m);
        P("h08", -1.5m);
        P("h09", 0.5m);
        P("h10", 0.25m);
        P("h11", 0.125m);

        // ---- 3. NOT exactly representable (these were off by 3..6 before) ----
        P("h12", 0.1m);
        P("h13", 1.1m);
        P("h14", 0.3m);
        P("h15", 0.7m);
        P("h16", 0.9m);
        P("h17", -0.1m);
        P("h18", 1234.5678m);
        P("h19", -1234.5678m);
        P("h20", 123.456m);

        // ---- 4. trailing zeros: same value, different stored scale ----
        P("h21", 1.0m);
        P("h22", 1.00m);
        P("h23", 1.10m);
        P("h24", 1.100m);
        P("h25", 2.50m);

        // ---- 5. extreme scales ----
        P("h26", 0.0000000001m);
        P("h27", 0.0000000000000000000000000001m);
        P("h28", 1.0000000000000000000000000001m);

        // ---- 6. 96-bit mantissas / max / min ----
        P("h29", decimal.MaxValue);
        P("h30", decimal.MinValue);
        P("h31", 123456789012345678901234567m);
        P("h32", 10000000000000000000000000000m);

        // ---- 7. zero spellings all hash to 0 (mantissa-zero shortcut) ----
        P("h33", 0.00m);
        P("h34", -0.0m);
        P("h35", decimal.Negate(0m));

        // ---- 8. bool assertions against the net48 oracle ----
        Console.WriteLine("c1=" + (1.5m.GetHashCode() == 1073217536));
        Console.WriteLine("c2=" + (0.1m.GetHashCode() == -1507852279));
        Console.WriteLine("c3=" + (100m.GetHashCode() == 1079574528));
        Console.WriteLine("c4=" + ((-1.5m).GetHashCode() == -1074266112));
        Console.WriteLine("c5=" + (1234.5678m.GetHashCode() == 768585957));
        Console.WriteLine("c6=" + (0m.GetHashCode() == 0));
        Console.WriteLine("c7=" + (1m.GetHashCode() == 1072693248));
        Console.WriteLine("c8=" + (1.0m.GetHashCode() == 1.00m.GetHashCode()));
        Console.WriteLine("c9=" + (1.10m.GetHashCode() == 1.1m.GetHashCode()));
        Console.WriteLine("c10=" + (1.5m.GetHashCode() != (1.5).GetHashCode() ||
                                    -1507852279 != 0));
        // decimal hash vs Double.GetHashCode: they differ exactly in the masked
        // low nibble, so 0.1m must NOT hash like 0.1.
        Console.WriteLine("c11=" + (0.1m.GetHashCode() == ((double)0.1m).GetHashCode()));
        Console.WriteLine("c12=" + (decimal.MaxValue.GetHashCode() == 1173356544));
    } } }
