//------------------------------------------------------------------------------
// parsefloat.cs -- FLOATING-POINT PARSE/TryParse DEEP e2e reference case
// (net48 oracle, byte-for-byte). Created in e2e batch C84, after the parse
// domain closure (MethodMap.ValueType.cs -> il2cpp_parse.h TryParse family +
// il2cpp_format.h throwing Parse family; the int Parse/TryParse lattice is
// parseint C78).
//
// This case drives the DOUBLE parse surface (float values live in double
// slots on the ILC side) across the full text-format lattice:
//
//   1. Parse(string) FULL MATRIX (classified by caught type -- v/fmt/ovf):
//      signed forms / ASCII whitespace padding (space/tab/CR/LF/FF) / leading
//      zeros / '.' placement (leading, trailing, none) / e/E exponents (both
//      signs, no-sign, multi-digit) / full 15-sig-digit values / near-max
//      magnitude / NaN / empty / whitespace-only / junk / doubled sign /
//      trailing junk / hex-looking text / comma-grouped text / out-of-range
//      magnitude (OverflowException) / underflow-to-zero.
//   2. TryParse(string, out double) IDENTICAL MATRIX with the
//      Parse/TryParse CONSISTENCY asserted line-by-line (they must agree on
//      what is valid; net48 writes the value on success only -- the
//      failure-path out-slot divergence D-NP-2 is recorded, NOT asserted).
//   3. The zh-CN infinity words U+221E / -U+221E (whitespace padded) parse
//      to +/-Infinity -- verified through double.IsPositiveInfinity /
//      IsNegativeInfinity so the U+221E symbol is NEVER printed to the
//      console (the ILC console byte-encodes it differently, fmtround
//      precedent). The ASCII spellings "Infinity"/"-Infinity" must FAIL
//      (net48 reads the culture words; zh-CN uses the symbol).
//   4. FLOAT dimension: float.ToString()/WriteLine for EXACTLY-REPRESENTABLE
//      float values whose decimal text is identical under float G7 and the
//      ILC G15 double formatter. Non-representable floats (0.1f -> net48
//      "0.1" vs ILC "0.100000001490116") and big-magnitude floats whose G7
//      and G15 texts differ are EXCLUDED (D-PF-1 in GAPS).
//
// KNOWN GAPS (NOT exercised -- acceptance surface, see GAPS.md):
//   * System.Single.Parse/TryParse (float.Parse(float.TryParse) -- NOT
//     wired: falls to the generated-name path (D-PAR-5 style). The case
//     uses the double surface, which is the ILC single-equivalent.
//   * double.Parse/TryParse 3/4-arg overloads (NumberStyles/IFormatProvider)
//     -- NOT wired.
//   * ASCII "Infinity" accepts/negation forms, hex floats, abbreviated
//     "inf"/"nan", "+Infinity"/"-NaN" -- all FAIL on net48 zh-CN too, but
//     only the culture words are probed (the runtime pins the zh-CN
//     contract).
//   * "1,000" (comma-grouped) is EXCLUDED: net48 double.Parse(string)
//     defaults to NumberStyles.Float | AllowThousands (parses "1,000" ->
//     1000) while the runtime's double grammar has no group separators
//     (D-PF-2, recorded in GAPS).
//
// EVERY line prints "label=..."; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class ParseFloat
    {
        // double.Parse: "v:<value>" / "fmt" / "ovf". null is NOT passed to
        // Parse (net48 throws ArgumentNullException there, the runtime throws
        // FormatException -- D-PAR-4 style divergence, recorded in GAPS).
        static string ParseLabel(string s)
        {
            try { return "v:" + double.Parse(s); }
            catch (FormatException) { return "fmt"; }
            catch (OverflowException) { return "ovf"; }
        }

        // double.TryParse: "v:<value>" on success, "fail" on failure
        // (accepts null -- no-throw contract on both sides).
        static string TryLabel(string s)
        {
            double v;
            return double.TryParse(s, out v) ? ("v:" + v) : "fail";
        }

        // Parse/TryParse consistency on the same text.
        static string Pair(string s)
        {
            return "p=" + ParseLabel(s) + " t=" + TryLabel(s);
        }

        // Infinity/NaN classification through the wired Double predicates --
        // never prints the U+221E symbol.
        static string SpecLabel(string s)
        {
            double d;
            if (!double.TryParse(s, out d)) return "fail";
            if (double.IsPositiveInfinity(d)) return "posinf";
            if (double.IsNegativeInfinity(d)) return "neginf";
            if (double.IsNaN(d)) return "nan";
            return "v:" + d;
        }

        public static void Main()
        {
            // ================= 1. Parse full matrix =================
            string[] inputs = new string[]
            {
                "0", "1", "-1", "42", "+42", "-42",
                "  42.5  ", "\t1.5\t", "42.5\n", "42.5\r", "42.5\f",
                "0001.500", "42.", ".5", "+.5", "-.5",
                "0.1", "3.14159", "123456789.123456789",
                "1e3", "1E-05", "1e20", "-1.5e2", "1e+5", "1e-5", "1.5E-7",
                "1.7976931348623157e308", "1e308", "1e-308", "1e-320",
                "NaN",
                "", " ", "abc", "1e", "1.2.3", "Infinity", "-Infinity",
                "++1", "--1", "1x", "0x10", "+", "-", "5-1",
                "1e400", "-1e400", "1e309", "1e-400", "NaN1", "1.5e", "e5"
            };
            for (int i = 0; i < inputs.Length; i++)
            {
                Console.WriteLine("r" + i + " " + inputs[i] + " " + ParseLabel(inputs[i]));
            }

            // ================= 2. TryParse identical matrix + consistency =================
            for (int i = 0; i < inputs.Length; i++)
            {
                Console.WriteLine("c" + i + " " + inputs[i] + " " + Pair(inputs[i]));
            }

            // null text: TryParse-only (no-throw on both sides).
            Console.WriteLine("cnull t=" + TryLabel(null));

            // ================= 3. culture infinity words (zh-CN U+221E) =================
            Console.WriteLine("sp_pos=" + SpecLabel("\u221E"));
            Console.WriteLine("sp_neg=" + SpecLabel("-\u221E"));
            Console.WriteLine("sp_pad=" + SpecLabel("  \u221E  "));
            Console.WriteLine("sp_nan=" + SpecLabel("NaN"));
            Console.WriteLine("sp_ascii_pos=" + SpecLabel("Infinity"));
            Console.WriteLine("sp_ascii_neg=" + SpecLabel("-Infinity"));
            Console.WriteLine("sp_plus_sym=" + SpecLabel("+\u221E"));

            // ================= 4. float formatting dimension =================
            // ONLY exactly-representable floats: net48 formats float with
            // G7 (7 sig digits) while the ILC routes float through the G15
            // double formatter, so a non-representable float like 0.1f would
            // print "0.1" on net48 but "0.100000001490116" on ILC (D-PF-1 in
            // GAPS). Exactly-representable values agree on both.
            float f1 = 0.5f;
            Console.WriteLine("f_fmt_half=" + f1);        // 0.5
            float f2 = 3.5f;
            Console.WriteLine("f_fmt_35=" + f2);          // 3.5
            float f3 = -2.25f;
            Console.WriteLine("f_fmt_neg=" + f3);         // -2.25
            float f4 = 100.0f;
            Console.WriteLine("f_fmt_100=" + f4);         // 100
            float f5 = 0.125f;
            Console.WriteLine("f_fmt_eighth=" + f5);      // 0.125
            float f6 = 0.25f;
            Console.WriteLine("f_fmt_quarter=" + f6);     // 0.25
            float f7 = 1.5f;
            Console.WriteLine("f_fmt_15=" + f7);          // 1.5
            float f8 = -0.75f;
            Console.WriteLine("f_fmt_negq=" + f8);        // -0.75
            float f9 = 2.0f;
            Console.WriteLine("f_fmt_two=" + f9);         // 2
            float fa = 10.0f;
            Console.WriteLine("f_fmt_ten=" + fa);         // 10
        }
    }
}
