using System;

// -----------------------------------------------------------------------------
// fmtchain.cs -- numeric ToString(format) chain: N / X / E / G / P formats
// on int32 / int64 / double, aligned to the net48 reference run.
//
// Each probe prints the FORMATTED VALUE (diagnostic line) followed by a BOOL
// comparison against a fixed known literal (culture-proof assertion line).
// The chosen formats (N/X/E/G) render identically in zh-CN and invariant
// cultures; P1 uses the zh-CN "n%" layout (no space, see formatext oracle).
//
// Subset exercised = the format_ext domain wired in MethodMap.Format.cs:
//   int32/int64: D / N / X / C / E / G
//   double:      G / F / N / E / C / P (+ custom '0'/'.')
// DELIBERATELY EXCLUDED (unwired / out-of-subset, transpiler would reject):
//   decimal.ToString(format)   -- no MethodMap entry for System.Decimal
//   int/Int64 "P"              -- formatext.cpp throws FormatException
// -----------------------------------------------------------------------------
class Program
{
    static void Main()
    {
        // ---- int32: N chain (N0/N2/N3) ----
        int p = 1234567;
        Console.WriteLine(p.ToString("N0"));
        Console.WriteLine(p.ToString("N0") == "1,234,567");
        Console.WriteLine(p.ToString("N2"));
        Console.WriteLine(p.ToString("N2") == "1,234,567.00");
        Console.WriteLine(p.ToString("N3"));
        Console.WriteLine(p.ToString("N3") == "1,234,567.000");
        Console.WriteLine((-p).ToString("N2"));
        Console.WriteLine((-p).ToString("N2") == "-1,234,567.00");
        Console.WriteLine((0).ToString("N2"));
        Console.WriteLine((0).ToString("N2") == "0.00");
        Console.WriteLine((-1).ToString("N2"));
        Console.WriteLine((-1).ToString("N2") == "-1.00");

        // ---- int32: X chain (X/X2/X4/X8) ----
        Console.WriteLine((255).ToString("X"));
        Console.WriteLine((255).ToString("X") == "FF");
        Console.WriteLine((255).ToString("X2"));
        Console.WriteLine((255).ToString("X2") == "FF");
        Console.WriteLine((255).ToString("X4"));
        Console.WriteLine((255).ToString("X4") == "00FF");
        Console.WriteLine((255).ToString("X8"));
        Console.WriteLine((255).ToString("X8") == "000000FF");
        Console.WriteLine((-1).ToString("X"));
        Console.WriteLine((-1).ToString("X") == "FFFFFFFF");
        Console.WriteLine((0).ToString("X2"));
        Console.WriteLine((0).ToString("X2") == "00");
        Console.WriteLine((0).ToString("X8"));
        Console.WriteLine((0).ToString("X8") == "00000000");
        Console.WriteLine(int.MaxValue.ToString("X"));
        Console.WriteLine(int.MaxValue.ToString("X") == "7FFFFFFF");
        Console.WriteLine(int.MaxValue.ToString("X8"));
        Console.WriteLine(int.MaxValue.ToString("X8") == "7FFFFFFF");

        // ---- int32: E chain (E2/E4) ----
        Console.WriteLine(p.ToString("E2"));
        Console.WriteLine(p.ToString("E2") == "1.23E+006");
        Console.WriteLine(p.ToString("E4"));
        Console.WriteLine(p.ToString("E4") == "1.2346E+006");
        Console.WriteLine((255).ToString("E2"));
        Console.WriteLine((255).ToString("E2") == "2.55E+002");
        Console.WriteLine((0).ToString("E2"));
        Console.WriteLine((0).ToString("E2") == "0.00E+000");

        // ---- int32: G chain (G/G15) ----
        Console.WriteLine(p.ToString("G"));
        Console.WriteLine(p.ToString("G") == "1234567");
        Console.WriteLine(p.ToString("G15"));
        Console.WriteLine(p.ToString("G15") == "1234567");
        Console.WriteLine((0).ToString("G"));
        Console.WriteLine((-1).ToString("G"));
        Console.WriteLine(int.MaxValue.ToString("N0"));
        Console.WriteLine(int.MaxValue.ToString("N0") == "2,147,483,647");

        // ---- int64: boundary probes ----
        long lp = 123456789012345L;
        Console.WriteLine(lp.ToString("N0"));
        Console.WriteLine(lp.ToString("N0") == "123,456,789,012,345");
        Console.WriteLine((-1L).ToString("X"));
        Console.WriteLine((-1L).ToString("X") == "FFFFFFFFFFFFFFFF");
        Console.WriteLine(long.MaxValue.ToString("X8"));
        Console.WriteLine(long.MaxValue.ToString("X8") == "7FFFFFFFFFFFFFFF");
        Console.WriteLine(lp.ToString("E2"));
        Console.WriteLine(lp.ToString("E2") == "1.23E+014");

        // ---- double: N chain ----
        double dp = 1234567.89;
        Console.WriteLine(dp.ToString("N0"));
        Console.WriteLine(dp.ToString("N0") == "1,234,568");
        Console.WriteLine(dp.ToString("N2"));
        Console.WriteLine(dp.ToString("N2") == "1,234,567.89");
        Console.WriteLine(dp.ToString("N3"));
        Console.WriteLine(dp.ToString("N3") == "1,234,567.890");
        Console.WriteLine((-dp).ToString("N2"));
        Console.WriteLine((-dp).ToString("N2") == "-1,234,567.89");
        Console.WriteLine((0.0).ToString("N2"));
        Console.WriteLine((0.0).ToString("N2") == "0.00");
        Console.WriteLine((-1.0).ToString("N2"));
        Console.WriteLine((-0.00123).ToString("N2"));
        Console.WriteLine((-0.00123).ToString("N2") == "0.00");

        // ---- double: E chain ----
        Console.WriteLine(dp.ToString("E2"));
        Console.WriteLine(dp.ToString("E2") == "1.23E+006");
        Console.WriteLine(dp.ToString("E4"));
        Console.WriteLine(dp.ToString("E4") == "1.2346E+006");
        Console.WriteLine((0.00123).ToString("E2"));
        Console.WriteLine((0.00123).ToString("E2") == "1.23E-003");
        Console.WriteLine((0.00123).ToString("E4"));
        Console.WriteLine((0.00123).ToString("E4") == "1.2300E-003");
        Console.WriteLine((-0.00123).ToString("E2"));
        Console.WriteLine((-0.00123).ToString("E2") == "-1.23E-003");
        Console.WriteLine((0.0).ToString("E4"));
        Console.WriteLine((0.0).ToString("E4") == "0.0000E+000");

        // ---- double: G chain (G/G15) ----
        Console.WriteLine(dp.ToString("G"));
        Console.WriteLine(dp.ToString("G") == "1234567.89");
        Console.WriteLine(dp.ToString("G15"));
        Console.WriteLine(dp.ToString("G15") == "1234567.89");
        Console.WriteLine((0.00123).ToString("G"));
        Console.WriteLine((0.00123).ToString("G") == "0.00123");
        Console.WriteLine((0.0).ToString("G"));
        Console.WriteLine((-0.0).ToString("G"));

        // ---- double: P1 percent chain (zh-CN "n%" layout) ----
        Console.WriteLine(dp.ToString("P1"));
        Console.WriteLine(dp.ToString("P1") == "123,456,789.0%");
        Console.WriteLine((0.00123).ToString("P1"));
        Console.WriteLine((0.00123).ToString("P1") == "0.1%");
        Console.WriteLine((-0.5).ToString("P1"));
        Console.WriteLine((-0.5).ToString("P1") == "-50.0%");
        Console.WriteLine((0.0).ToString("P1"));
        Console.WriteLine((0.0).ToString("P1") == "0.0%");
    }
}
