//------------------------------------------------------------------------------
// customfmt.cs -- end-to-end reference case for the CUSTOM numeric format
// engine (net48 oracle, formatext.cpp / numberformat.cpp): '0' / '#' /
// '.' / ',' grouping / scaling commas / '%' / U+2030 per-mille / "D100"
// standard-looking-custom fallback / quoted literals / '\' escapes.
//
// WIRING STATUS (2026-08-09, HEAD 8921ff8): WIRED. The custom engine
// (ParseCustomPattern + EmitCustomFormat + ShiftScaleInt64 + the double
// path) was oracle-verified in %TEMP%\opencode\cstmprobe (probe1..9 +
// oracle1..9.txt) and committed with the "custom format engine (0/#/%/‰,
// runtime 135/135)" batch. MethodMap.Format.cs routes ToString(format)
// through icall_format_int32/int64/double_custom. The format string is a
// compile-time literal passed through the g_strPool.
//
// PROBE STYLE: every probe prints "label=" + the raw formatted value for
// the ASCII formats and a bool equality assertion against a fixed net48
// literal (hand-verified against the cstmprobe oracle). The per-mille (U+2030)
// probes assert ONLY the bool equality (the U+2030 symbol is a multi-byte
// UTF-8 sequence that the M3 transcoder can drop in the oracle file -- the
// equality comparison happens in managed memory and is byte-exact regardless
// of console encoding).
//
// DELIBERATELY EXCLUDED (documented engine gaps -- FormatException):
//   scientific patterns ("0.00E+00" / "0E0" / "0.##e-0") and ';' section
//   separators -- both throw FormatException (out of the committed subset).
//   They get their own probe section parked below (see the engine header).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Customfmt
    {
        private static int s_checks;
        private static bool Check(bool condition)
        {
            ++s_checks;
            return condition;
        }

        public static void Main()
        {
            // ================= int32 =================
            Console.WriteLine("i0=" + (0).ToString("0"));
            Console.WriteLine("i0ok=" + Check((0).ToString("0") == "0"));
            Console.WriteLine("ihash=" + (0).ToString("#"));
            Console.WriteLine("ihashok=" + Check((0).ToString("#") == ""));
            Console.WriteLine("i12345=" + (12345).ToString("#"));
            Console.WriteLine("i12345ok=" + Check((12345).ToString("#") == "12345"));
            Console.WriteLine("i12345x4=" + (12345).ToString("####"));
            Console.WriteLine("i12345x4ok=" + Check((12345).ToString("####") == "12345"));
            Console.WriteLine("i12pad=" + (12).ToString("0000"));
            Console.WriteLine("i12padok=" + Check((12).ToString("0000") == "0012"));
            Console.WriteLine("i0pad=" + (0).ToString("0"));
            Console.WriteLine("i0padok=" + Check((0).ToString("0") == "0"));

            // ---- ',' grouping ----
            Console.WriteLine("ig7=" + (1234567).ToString("#,##0"));
            Console.WriteLine("ig7ok=" + Check((1234567).ToString("#,##0") == "1,234,567"));
            Console.WriteLine("ig4=" + (1234).ToString("#,##0.00"));
            Console.WriteLine("ig4ok=" + Check((1234).ToString("#,##0.00") == "1,234.00"));
            Console.WriteLine("ig4z=" + (1234).ToString("0,0"));
            Console.WriteLine("ig4zok=" + Check((1234).ToString("0,0") == "1,234"));
            Console.WriteLine("ig5=" + (12345).ToString("0,0"));
            Console.WriteLine("ig5ok=" + Check((12345).ToString("0,0") == "12,345"));
            Console.WriteLine("igsep=" + (12345).ToString("000-00"));
            Console.WriteLine("igsepok=" + Check((12345).ToString("000-00") == "123-45"));
            Console.WriteLine("ig0=" + (0).ToString("#,##0"));
            Console.WriteLine("ig0ok=" + Check((0).ToString("#,##0") == "0"));
            Console.WriteLine("igneg=" + (-12345).ToString("#,##0"));
            Console.WriteLine("ignegok=" + Check((-12345).ToString("#,##0") == "-12,345"));
            Console.WriteLine("ignegpad=" + (-12345).ToString("0000"));
            Console.WriteLine("ignegpadok=" + Check((-12345).ToString("0000") == "-12345"));

            // ---- "D100" standard-looking custom fallback (net48: custom) ----
            Console.WriteLine("iD100=" + (12345).ToString("D100"));
            Console.WriteLine("iD100ok=" + Check((12345).ToString("D100") == "D112345"));

            // ---- '%' percent (x100) ----
            Console.WriteLine("ipct=" + (12345).ToString("0%"));
            Console.WriteLine("ipctok=" + Check((12345).ToString("0%") == "1234500%"));
            Console.WriteLine("ipct2=" + (12345).ToString("0.00%"));
            Console.WriteLine("ipct2ok=" + Check((12345).ToString("0.00%") == "1234500.00%"));

            // ---- ',' scaling commas (/1000 per trailing comma) ----
            Console.WriteLine("iscal=" + (123456789).ToString("0,,"));
            Console.WriteLine("iscalok=" + Check((123456789).ToString("0,,") == "123"));
            Console.WriteLine("iscal2=" + (100000000).ToString("0,,"));
            Console.WriteLine("iscal2ok=" + Check((100000000).ToString("0,,") == "100"));
            Console.WriteLine("iscal3=" + (123456789).ToString("#,##0,,"));
            Console.WriteLine("iscal3ok=" + Check((123456789).ToString("#,##0,,") == "123"));

            // ---- '\' escape + quoted literals ----
            Console.WriteLine("iesc=" + (5).ToString("0\\x"));
            Console.WriteLine("iescok=" + Check((5).ToString("0\\x") == "5x"));
            Console.WriteLine("iquote=" + (123).ToString("##' units'"));
            Console.WriteLine("iquoteok=" + Check((123).ToString("##' units'") == "123 units"));

            // ---- int with fraction placeholders ----
            Console.WriteLine("ifrac=" + (255).ToString("0000.00"));
            Console.WriteLine("ifracok=" + Check((255).ToString("0000.00") == "0255.00"));

            // ================= int64 =================
            Console.WriteLine("lmax=" + (9223372036854775807L).ToString("#,##0"));
            Console.WriteLine("lmaxok=" + Check((9223372036854775807L).ToString("#,##0") == "9,223,372,036,854,775,807"));
            Console.WriteLine("lmin=" + (-9223372036854775808L).ToString("#,##0"));
            Console.WriteLine("lminok=" + Check((-9223372036854775808L).ToString("#,##0") == "-9,223,372,036,854,775,808"));
            Console.WriteLine("lpct=" + (9223372036854775807L).ToString("0%"));
            Console.WriteLine("lpctok=" + Check((9223372036854775807L).ToString("0%") == "922337203685477580700%"));

            // ================= double =================
            // ---- '#' optional placeholders (leading/trailing suppressed) ----
            Console.WriteLine("d05=" + (0.5).ToString("#.##"));
            Console.WriteLine("d05ok=" + Check((0.5).ToString("#.##") == ".5"));
            Console.WriteLine("d0=" + (0).ToString("#.##"));
            Console.WriteLine("d0ok=" + Check((0).ToString("#.##") == ""));
            Console.WriteLine("d2675=" + (2.675).ToString("#.##"));
            Console.WriteLine("d2675ok=" + Check((2.675).ToString("#.##") == "2.68"));
            Console.WriteLine("d05b=" + (0.5).ToString("0.##"));
            Console.WriteLine("d05bok=" + Check((0.5).ToString("0.##") == "0.5"));

            // ---- ',' grouping on double ----
            Console.WriteLine("dgrp=" + (1234567.89).ToString("#,##0.00"));
            Console.WriteLine("dgrpok=" + Check((1234567.89).ToString("#,##0.00") == "1,234,567.89"));
            Console.WriteLine("dgrp2=" + (1234.5).ToString("#,##0.00"));
            Console.WriteLine("dgrp2ok=" + Check((1234.5).ToString("#,##0.00") == "1,234.50"));
            Console.WriteLine("dgrp0=" + (0).ToString("#,##0.00"));
            Console.WriteLine("dgrp0ok=" + Check((0).ToString("#,##0.00") == "0.00"));

            // ---- rounding (half away from zero) ----
            Console.WriteLine("dround=" + (123.456).ToString("0.#"));
            Console.WriteLine("droundok=" + Check((123.456).ToString("0.#") == "123.5"));
            Console.WriteLine("dround2=" + (0.25).ToString("0.#"));
            Console.WriteLine("dround2ok=" + Check((0.25).ToString("0.#") == "0.3"));
            Console.WriteLine("dround3=" + (0.25).ToString("0.0#"));
            Console.WriteLine("dround3ok=" + Check((0.25).ToString("0.0#") == "0.25"));
            Console.WriteLine("dround4=" + (0).ToString("0.00#"));
            Console.WriteLine("dround4ok=" + Check((0).ToString("0.00#") == "0.00"));
            Console.WriteLine("dfixed3=" + (12345.6789).ToString("0.000"));
            Console.WriteLine("dfixed3ok=" + Check((12345.6789).ToString("0.000") == "12345.679"));
            Console.WriteLine("dfixed1=" + (1).ToString("0.000"));
            Console.WriteLine("dfixed1ok=" + Check((1).ToString("0.000") == "1.000"));
            Console.WriteLine("dtiny=" + (0.0001).ToString("0.0000"));
            Console.WriteLine("dtinyok=" + Check((0.0001).ToString("0.0000") == "0.0001"));
            Console.WriteLine("dneg=" + (-0.4).ToString("0000.00"));
            Console.WriteLine("dnegok=" + Check((-0.4).ToString("0000.00") == "-0000.40"));

            // ---- '%' on double (x100) ----
            Console.WriteLine("dpct=" + (0.1234).ToString("0.00%"));
            Console.WriteLine("dpctok=" + Check((0.1234).ToString("0.00%") == "12.34%"));
            Console.WriteLine("dpctneg=" + (-0.1234).ToString("0.00%"));
            Console.WriteLine("dpctnegok=" + Check((-0.1234).ToString("0.00%") == "-12.34%"));
            Console.WriteLine("dpct0=" + (0).ToString("0.00%"));
            Console.WriteLine("dpct0ok=" + Check((0).ToString("0.00%") == "0.00%"));

            // ---- U+2030 per-mille (x1000) -- bool-only (multibyte symbol) ----
            Console.WriteLine("dpermille=" + Check((0.1234).ToString("0.00\u2030") == "123.40\u2030"));
            Console.WriteLine("dpermille0=" + Check((0).ToString("0.00\u2030") == "0.00\u2030"));
            Console.WriteLine("dpermille1=" + Check((1.5).ToString("0.00\u2030") == "1500.00\u2030"));
        }
    }
}
