// numfmtdeep.cs -- DEEP numeric formatting coverage (e2e batch C20): the
// grouping / percent / custom surfaces AROUND the b57650b memchr fix
// ("numberformat GroupDigits strchr->memchr bounded scan, WSL ASAN
// negative-size bug"). The net48 oracle runs the SAME source; the ILC side
// must produce byte-identical strings.
//
// Focus:
//   * Standard "N" on int32/int64 -- every precision 0..4, boundary values
//     (int.Min/Max, long.Min/Max), negatives, group-boundary magnitudes
//     (999/1000/1,000,000), all via FormatNumberInt (numberformat.cpp:313).
//   * Standard "N" on double -- FormatGroupedFinite (formatext.cpp:331): the
//     buffer fed to the group scan is NOT NUL-terminated, so the N0 / P0 rows
//     below have NO '.' anywhere in the owned bytes (the exact condition the
//     b57650b memchr fix addresses). Rows with '.' at different positions
//     exercise the bounded dot-scan + verbatim tail copy. Rounding rows cross
//     a group boundary (999999.999 -> "1,000,000.00").
//   * Standard "P" on double (value*100) and int (digits+"00" scale).
//   * Custom patterns: grouping+percent, grouping on negatives, scaling
//     commas, grouping + fraction on double, zero-pad runs.
//
// Every expected value is spelled in a trailing comment (the live net48
// oracle is the diff authority).
using System;

namespace Cases
{
    public static class Numfmtdeep
    {
        private static void P(string label, string v) { Console.WriteLine(label + "=" + v); }

        public static void Main()
        {
            // ================= int32: standard N =================
            P("i_n0_0", (0).ToString("N0"));              // 0
            P("i_n2_0", (0).ToString("N2"));              // 0.00
            P("i_n0_7", (1234567).ToString("N0"));        // 1,234,567
            P("i_n4_7", (1234567).ToString("N4"));        // 1,234,567.0000
            P("i_n1_7", (1234567).ToString("N1"));        // 1,234,567.0
            P("i_n0_neg", (-1234567).ToString("N0"));     // -1,234,567
            P("i_n0_max", (2147483647).ToString("N0"));   // 2,147,483,647
            P("i_n0_min", (-2147483648).ToString("N0"));  // -2,147,483,648
            P("i_n0_999", (999).ToString("N0"));          // 999
            P("i_n0_1000", (1000).ToString("N0"));        // 1,000
            P("i_n1_6", (1000000).ToString("N1"));        // 1,000,000.0
            P("i_n0_999m", (999999999).ToString("N0"));   // 999,999,999

            // ================= int64: standard N =================
            P("l_n0_max", (9223372036854775807L).ToString("N0"));   // 9,223,372,036,854,775,807
            P("l_n0_min", (-9223372036854775808L).ToString("N0"));  // -9,223,372,036,854,775,808
            P("l_n3", (1234567890123L).ToString("N3"));             // 1,234,567,890,123.000

            // ================= double: standard N (FormatGroupedFinite) =================
            // N0/P0 rows have NO '.' in the owned bytes -- the unbounded
            // strchr scan condition the b57650b memchr fix addresses.
            P("d_n0_1e15", (1000000000000000.0).ToString("N0"));     // 1,000,000,000,000,000
            P("d_n0_neg1e15", (-1000000000000000.0).ToString("N0")); // -1,000,000,000,000,000
            P("d_n0_9", (123456789.0).ToString("N0"));               // 123,456,789
            P("d_n0_neg9", (-987654321.0).ToString("N0"));           // -987,654,321
            P("d_n0_zero", (0).ToString("N0"));                      // 0
            // '.' present at different positions (safe rows, dot-scan + tail copy).
            P("d_n2", (1234567.89).ToString("N2"));                  // 1,234,567.89
            P("d_n3", (123456789.125).ToString("N3"));               // 123,456,789.125
            P("d_n2_neg", (-1234567.89).ToString("N2"));             // -1,234,567.89
            P("d_n6_long", (123456.789012).ToString("N6"));          // 123,456.789012
            // Rounding crossing a group boundary.
            P("d_n2_round", (999999.999).ToString("N2"));            // 1,000,000.00
            P("d_n0_round", (999999.999).ToString("N0"));            // 1,000,000
            P("d_n4_tiny", (0.123456).ToString("N4"));               // 0.1235
            P("d_n2_001", (0.001).ToString("N2"));                   // 0.00
            P("d_n2_zero", (0).ToString("N2"));                      // 0.00

            // ================= double: standard P (value*100) =================
            P("d_p0", (0.5).ToString("P0"));                         // 50%
            P("d_p1", (1.5).ToString("P1"));                         // 150.0%
            P("d_p2", (0.1234).ToString("P2"));                      // 12.34%
            P("d_p2_neg", (-0.1234).ToString("P2"));                 // -12.34%
            P("d_p0_big", (123456789.0).ToString("P0"));             // 12,345,678,900%
            P("d_p4_tiny", (-0.0001234).ToString("P4"));             // -0.0123%
            P("d_p2_zero", (0).ToString("P2"));                      // 0.00%

            // ================= int: standard P (digits + "00") =================
            P("i_p0", (12345).ToString("P0"));                       // 1,234,500%
            P("i_p2", (123).ToString("P2"));                         // 12,300.00%
            P("i_p0_neg", (-12345).ToString("P0"));                  // -1,234,500%
            P("i_p0_zero", (0).ToString("P0"));                      // 0%
            P("i_p0_max", (2147483647).ToString("P0"));              // 214,748,364,700%

            // ================= custom patterns =================
            P("c_grp_pct", (12345).ToString("#,##0%"));              // 1,234,500%
            P("c_grp_neg", (-12345).ToString("#,##0"));              // -12,345
            P("c_grp_frac", (1234567.89).ToString("#,##0.00"));      // 1,234,567.89
            P("c_scale_grp", (123456789).ToString("#,##0,,"));       // 123
            P("c_grp_pct_dbl", (0.1234).ToString("#,##0.00%"));      // 12.34%
            P("c_grp_dbl_neg", (-123456.789).ToString("#,##0.00"));  // -123,456.79
            P("c_grp_dbl1", (1234567890.5).ToString("#,##0.0"));     // 1,234,567,890.5
            P("c_pad", (255).ToString("000000"));                    // 000255
            P("c_grp_dbl0", (0).ToString("#,##0.00"));               // 0.00
        }
    }
}
