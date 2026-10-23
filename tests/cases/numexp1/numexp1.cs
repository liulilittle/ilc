//------------------------------------------------------------------------------
// numexp1.cs -- custom numeric format strings with the scientific 'E'/'e'
// marker: mantissa placeholders, exponent sign/digit-count semantics, zero,
// negatives, grouping, '%' scaling and the literal-E fallbacks.
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases { public static class Numexp1 {
    static void P(string tag, double v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    static void I(string tag, int v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    static void L(string tag, long v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    static void M(string tag, decimal v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    static void S(string tag, float v, string fmt) {
        string r;
        try { r = v.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { r = "EXF"; }
        catch (Exception e) { r = "EXO:" + e.GetType().Name; }
        Console.WriteLine(tag + "=[" + r + "]");
    }

    public static void Main() {
        // The reported defect.
        P("a1", 0.000123, "0.00E+00");
        P("a2", 0.000123, "0.00e+00");
        P("a3", 0.000123, "0.###E+0");
        P("a4", 0.000123, "E");

        // Exponent sign semantics.
        P("b1", 1234.5, "0.00E+0");
        P("b2", 1234.5, "0.00E-0");
        P("b3", 1234.5, "0.00E0");
        P("b4", 1234.5, "0.00E+00");
        P("b5", 1234.5, "0.00E-00");
        P("b6", 1234.5, "0.00E00");
        P("b7", 1234.5, "0.00E+000");
        P("b8", 0.000123, "0.00E-0");
        P("b9", 0.000123, "0.00E0");

        // Case of the marker.
        P("c1", 1234.5, "0.00e+0");
        P("c2", 1234.5, "0.00e-00");
        P("c3", 1234.5, "0E0");
        P("c4", 1234.5, "0e0");

        // Mantissa placeholder geometry.
        P("d1", 1234.5, "0E+0");
        P("d2", 1234.5, "00E+0");
        P("d3", 1234.5, "0000.0E+0");
        P("d4", 1234.5, "##0.0E+0");
        P("d5", 1234.5, "#0.0E+0");
        P("d6", 1234.5, "#.0E+0");
        P("d7", 1234.5, ".0E+0");
        P("d8", 1234.5, "0.E+0");
        P("d9", 1234.5, "0.0#E+0");
        P("d10", 1234.5, "0.#E+0");

        // Zero, negatives, rounding carry.
        P("e1", 0.0, "0.00E+00");
        P("e2", 0.0, "0E+0");
        P("e3", 0.0, "##0.0E+0");
        P("e4", -0.0, "0.00E+00");
        P("e5", -1234.5, "0.00E+00");
        P("e6", -0.000123, "0.00E+00");
        P("e7", -0.5, "0.00E+0");
        P("e8", 9.999, "0.0E+0");
        P("e9", 9.999, "0.00E+00");
        P("e10", 0.09999, "0.0E+0");
        P("e11", 9.5, "0E+0");

        // Extremes.
        P("f1", 1e-320, "0.00E+00");
        P("f2", 1.7976931348623157e308, "0.00E+00");
        P("f3", 1.7976931348623157e308, "0.000000E+000");
        P("f4", 1e300, "0.00E+00");
        P("f5", 1e-300, "0.00E+00");

        // Grouping / scaling.
        P("g1", 1234.5, "#,##0.0E+0");
        P("g2", 1234567.0, "#,##0.0E+0");
        P("g3", 1234567.0, "#,##0.00E+00");
        P("g4", 0.000123, "#,##0.0E+0");
        P("g5", 1234.5, "0,0E+0");
        P("g6", 1234.5, "0,0.0E+0");
        P("g7", 1234.5, "0.00E+00%");
        P("g8", 1234.5, "0.00E+00\u2030");

        // Literal E/e (not an exponent marker) and literals around it.
        P("h1", 1234.5, "E+0x");
        P("h2", 1234.5, "0.00\\E+00");
        P("h3", 1234.5, "0.00'E+00'");
        P("h4", 1234.5, "'x'0.00E+0");
        P("h5", 1234.5, "0.00E+0x");
        P("h6", 1234.5, "0.00E+0''");
        P("h7", 1234.5, "E");

        // Two markers: only the first becomes the exponent.
        P("i1", 1234.5, "0.00E+0E+0");
        P("i2", 1234.5, "0.00E+0E+00");

        // Other numeric types.
        I("j1", 12345, "0.00E+00");
        I("j2", 12345, "0.00e+00");
        I("j3", 12345, "0E+0");
        I("j4", 0, "0.00E+00");
        I("j5", -12345, "0.00E+00");
        L("j6", 1234567890123456789L, "0.00E+00");
        M("j7", 1.5m, "0.00E+00");
        M("j8", 0.000123m, "0.00E+00");
        M("j9", 0m, "0.00E+00");
        M("j10", 1.5m, "0.###E+0");
        S("j11", 1.5f, "0.00E+00");
    }
}}
