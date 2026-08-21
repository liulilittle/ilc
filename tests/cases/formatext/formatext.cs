using System;

// -----------------------------------------------------------------------------
// formatext.cs -- ToString(format) reference program. net48 is the ORACLE:
// this program runs on the real .NET Framework 4.8 and its output pins the
// exact semantics that the runtime icalls
// (icall_format_int32_custom / icall_format_int64_custom /
// icall_format_double_custom in src/runtime/src/formatext.cpp) must match --
// see tests/runtime/test_formatext.cpp, whose expected values come from this
// capture.
//
// The program exercises ONLY the implemented subset (see il2cpp_format_ext.h):
//   int32/int64: D, D5, X, X4, N2, C, E2, G, null format, "" format, D10,
//                lowercase specifier, N0, C1
//   double:      F, F2, N2, E2, E0, E, G, "0.00"/"00.000"/"0" (custom
//                '0'/'.'), P2, C
// plus rounding probes (half away from zero), -0.0/NaN/Infinity probes and
// the unknown-format exception path. Every comment below is the verified
// net48 output observed on the verification host (zh-CN default culture).
//
// Out-of-subset divergence probe REMOVED from the oracle: "D100" does NOT
// throw on net48 (custom fallback "D112345") but this runtime's subset
// rejects it with FormatException (documented in il2cpp_format_ext.h) -- the
// two sides can never print a byte-identical line, so the executable probe
// is gone; the divergence stays documented in il2cpp_format_ext.h.
// -----------------------------------------------------------------------------
class Program
{
    static void Main()
    {
        int p = 12345;
        int n = -12345;
        // int32 standard formats
        Console.WriteLine(p.ToString("D"));            // 12345
        Console.WriteLine(p.ToString("D5"));           // 12345
        Console.WriteLine(n.ToString("D"));            // -12345
        Console.WriteLine(n.ToString("D5"));           // -12345
        Console.WriteLine(p.ToString("X"));            // 3039
        Console.WriteLine(p.ToString("X4"));           // 3039
        Console.WriteLine(n.ToString("X"));            // FFFFCFC7
        Console.WriteLine(n.ToString("X4"));           // FFFFCFC7
        Console.WriteLine(p.ToString("N2"));           // 12,345.00
        Console.WriteLine(n.ToString("N2"));           // -12,345.00
        // C format: assert the STRING, not the symbol bytes -- net48's
        // console best-fits U+00A5 to GBK ￥ while this runtime writes UTF-8
        // U+00A5, so a byte-exact oracle can never match (same artifact as
        // the ∞ specials below).
        Console.WriteLine(p.ToString("C") == "\u00A512,345.00");    // True
        Console.WriteLine(n.ToString("C") == "\u00A5-12,345.00");   // True
        Console.WriteLine((int)p.ToString("C")[0]);    // 165 (U+00A5)
        Console.WriteLine(p.ToString("E2"));           // 1.23E+004
        Console.WriteLine(n.ToString("E2"));           // -1.23E+004
        Console.WriteLine(p.ToString("G"));            // 12345
        Console.WriteLine(n.ToString("G"));            // -12345
        // null / empty format behave like "G" (.NET contract)
        Console.WriteLine(p.ToString((string)null));   // 12345
        Console.WriteLine(p.ToString(""));             // 12345
        // multi-digit precision / lowercase / zero fraction
        Console.WriteLine(p.ToString("D10"));          // 0000012345
        Console.WriteLine(p.ToString("d5"));           // 12345
        Console.WriteLine(p.ToString("N0"));           // 12,345
        Console.WriteLine(p.ToString("C1") == "\u00A512,345.0");    // True

        // int64 standard formats
        long lp = 123456789012345L;
        long ln = -123456789012345L;
        Console.WriteLine(lp.ToString("D"));           // 123456789012345
        Console.WriteLine(lp.ToString("D5"));          // 123456789012345
        Console.WriteLine(ln.ToString("D"));           // -123456789012345
        Console.WriteLine(lp.ToString("X"));           // 7048860DDF79
        Console.WriteLine(lp.ToString("X4"));          // 7048860DDF79
        Console.WriteLine(ln.ToString("X"));           // FFFF8FB779F22087
        Console.WriteLine(lp.ToString("N2"));          // 123,456,789,012,345.00
        Console.WriteLine(ln.ToString("N2"));          // -123,456,789,012,345.00
        Console.WriteLine(lp.ToString("C") == "\u00A5123,456,789,012,345.00"); // True
        Console.WriteLine(lp.ToString("E2"));          // 1.23E+014
        Console.WriteLine(lp.ToString("G"));           // 123456789012345
        Console.WriteLine(long.MaxValue.ToString("E2")); // 9.22E+018

        // double standard + custom formats
        double dp = 1234.5678;
        double dn = -1234.5678;
        double ds = 0.001234;
        Console.WriteLine(dp.ToString("F"));           // 1234.57
        Console.WriteLine(dp.ToString("F2"));          // 1234.57
        Console.WriteLine(dn.ToString("F2"));          // -1234.57
        Console.WriteLine(ds.ToString("F2"));          // 0.00
        Console.WriteLine(dp.ToString("N2"));          // 1,234.57
        Console.WriteLine(dp.ToString("E2"));          // 1.23E+003
        Console.WriteLine(ds.ToString("E2"));          // 1.23E-003
        Console.WriteLine(dp.ToString("E0"));          // 1E+003
        Console.WriteLine(dp.ToString("E"));           // 1.234568E+003
        Console.WriteLine(dp.ToString("G"));           // 1234.5678
        Console.WriteLine(dp.ToString("0.00"));        // 1234.57
        Console.WriteLine(dn.ToString("0.00"));        // -1234.57
        Console.WriteLine((1.234).ToString("00.000")); // 01.234
        Console.WriteLine((0.5).ToString("0"));        // 1
        Console.WriteLine(dp.ToString("P2"));          // 123,456.78%
        Console.WriteLine(dn.ToString("P2"));          // -123,456.78%
        Console.WriteLine(ds.ToString("P2"));          // 0.12%
        Console.WriteLine(dp.ToString("C") == "\u00A51,234.57");    // True

        // rounding probes (formatting rounds half away from zero)
        Console.WriteLine((2.5).ToString("F0"));       // 3
        Console.WriteLine((-2.5).ToString("F0"));      // -3
        Console.WriteLine((1.25).ToString("F1"));      // 1.3
        Console.WriteLine((0.005).ToString("F2"));     // 0.01
        Console.WriteLine((9.99).ToString("F1"));      // 10.0
        Console.WriteLine((9999.99).ToString("E2"));   // 1.00E+004
        Console.WriteLine((1e100).ToString("E2"));     // 1.00E+100
        double nz = -0.0; // negative zero probes (runtime path)
        Console.WriteLine(nz.ToString("F2"));          // 0.00
        Console.WriteLine(nz.ToString("E2"));          // 0.00E+000

        // specials: NaN stays ASCII; the infinities render as U+221E '∞' on
        // BOTH sides (zh-CN culture), but net48's console encodes it GBK
        // A1DE while this runtime writes UTF-8 E2 88 9E -- a byte-exact
        // oracle diff can never match, so assert the VALUE as a bool instead
        // (R5 pattern, mathext precedent: semantics agree on both sides).
        Console.WriteLine(double.NaN.ToString("F2"));                      // NaN
        Console.WriteLine(double.NaN.ToString("P2"));                      // NaN%
        Console.WriteLine(double.PositiveInfinity.ToString("F2") == "\u221E"); // True
        Console.WriteLine(double.NegativeInfinity.ToString("F2") == "-\u221E"); // True

        // integer edge probes
        Console.WriteLine((42).ToString("D5"));        // 00042
        Console.WriteLine((-42).ToString("D5"));       // -00042
        Console.WriteLine((0).ToString("X4"));         // 0000
        Console.WriteLine((-1L).ToString("X"));        // FFFFFFFFFFFFFFFF
        Console.WriteLine((-123).ToString("X4"));      // FFFFFF85

        // exception paths
        try
        {
            Console.WriteLine(p.ToString("Q"));
        }
        catch (FormatException)
        {
            Console.WriteLine("FormatException");
        }
        // out-of-subset divergence probe REMOVED: net48 falls back to custom
        // formatting ("D112345") while this runtime's subset rejects D100
        // with FormatException -- the lines can never match, see header.
    }
}
