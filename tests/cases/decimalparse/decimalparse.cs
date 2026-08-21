//------------------------------------------------------------------------------
// decimalparse.cs -- System.Decimal PARSE deep probe (e2e batch C72): the full
// throwing-Parse format matrix, the TryParse consistency matrix, and the
// rounding/floor/ceiling/truncate/conversion surface applied to PARSED values.
// Byte-exact vs the net48 oracle (expected.txt).
//
// Builds on decimalcase/decops/decimaldeep2 (which probe basic Parse and the
// arithmetic surface) with a PARSE-CENTRIC deep matrix over the wired decimal
// surface (MethodMap.Decimal.cs -> icall_decimal_parse / _try_parse /
// _round / _truncate / _floor / _ceiling / _to_int*).
//
// FORMAT GRAMMAR pinned by net48 (probe-verified, 2026-08-12):
//   * optional leading +/-; optional single '.'; ASCII whitespace trimmed
//     around the text; ',' tolerated BETWEEN digits (NumberStyles.Number
//     AllowThousands) -- "1,234" -> 1234, "12,34" -> 1234, "1,2,3" -> 123,
//     "1,000.5" -> 1000.5; a LEADING comma (",1") and double '.' ("1.2.3")
//     are FormatException; "." -> ".5" parses 0.5 and "5." parses 5.
//   * OverflowException: scale > 28, significant digits > 29 (the max decimal
//     literal "79228162514264337593543950335" and its negative are the exact
//     boundary; "79228162514264337593543950335.1" = 30 significant digits
//     throws), mantissa > 2^96-1.
//   * FormatException: junk, empty, whitespace-only, "1e3"/"1E3" (no
//     exponent), "+ 42"/"- 42" (sign separated from digits), "abc".
//   * Parse(null) -> ArgumentNullException; leading zeros collapse
//     ("0000..001" -> 1).
//   * Scale is PRESERVED: Parse("1.50").ToString() -> "1.50",
//     Parse("-0.00").ToString() -> "-0.00" (negative zero), so the value
//     round-trips through ToString byte-for-byte.
//
// DELIBERATELY NOT PROBED (documented net48/runtime divergences -- see
// GAPS.md; a probe would fail the byte diff):
//   * trailing sign ("42-"/"42+") -- net48 NumberStyles.Number accepts the
//     trailing sign, the runtime ParseCore only accepts a leading sign.
//   * trailing comma ("1,") and double comma ("1,,2") -- net48 tolerates them,
//     the runtime requires a digit after every comma.
//   * 29+ fractional digits ("0.00000000000000000000000000001") -- net48
//     ROUNDS to 28 decimal places, the runtime throws OverflowException.
//   * TryParse failure out-slot: net48 zeroes it, the runtime leaves it
//     untouched (verify-then-mutate, documented deviation) -- only the success
//     flag and the SUCCESS value are asserted.
//
// EVERY line prints a labeled bool; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

class Program
{
    // throwing Parse classified: "v:<value>" / "fmt" / "ovf" / "null".
    static string ParseLabel(string s)
    {
        try { decimal v = decimal.Parse(s); return "v:" + string.Format("{0}", v); }
        catch (FormatException) { return "fmt"; }
        catch (OverflowException) { return "ovf"; }
        catch (ArgumentNullException) { return "null"; }
    }

    // TryParse: "v:<value>" on success, "fail" on failure (accepts null).
    static string TryLabel(string s)
    {
        decimal v;
        return decimal.TryParse(s, out v) ? ("v:" + string.Format("{0}", v)) : "fail";
    }

    static void Main()
    {
        // ============ 1. throwing Parse FULL FORMAT MATRIX ============
        Console.WriteLine("p_zero=" + (ParseLabel("0") == "v:0"));
        Console.WriteLine("p_pos0=" + (ParseLabel("+0") == "v:0"));
        Console.WriteLine("p_neg0=" + (decimal.Parse("-0") == 0m));
        Console.WriteLine("p_42=" + (ParseLabel("42") == "v:42"));
        Console.WriteLine("p_plus42=" + (ParseLabel("+42") == "v:42"));
        Console.WriteLine("p_minus42=" + (ParseLabel("-42") == "v:-42"));
        Console.WriteLine("p_ws=" + (ParseLabel("  42  ") == "v:42"));
        Console.WriteLine("p_ws_tab=" + (ParseLabel("\t42\r\n") == "v:42"));
        Console.WriteLine("p_ws_sign=" + (ParseLabel("  -42  ") == "v:-42"));
        Console.WriteLine("p_lead0=" + (ParseLabel("00042") == "v:42"));
        Console.WriteLine("p_lead0long=" + (ParseLabel("00000000000000000000000000000000000000000001") == "v:1"));
        Console.WriteLine("p_dec=" + (ParseLabel("3.14") == "v:3.14"));
        Console.WriteLine("p_decneg=" + (ParseLabel("-0.5") == "v:-0.5"));
        Console.WriteLine("p_leadpt=" + (ParseLabel(".5") == "v:0.5"));
        Console.WriteLine("p_trailpt=" + (ParseLabel("5.") == "v:5"));
        Console.WriteLine("p_grp=" + (ParseLabel("1,234") == "v:1234"));
        Console.WriteLine("p_grp2=" + (ParseLabel("12,34") == "v:1234"));
        Console.WriteLine("p_grp3=" + (ParseLabel("1,2,3") == "v:123"));
        Console.WriteLine("p_grpdec=" + (ParseLabel("1,000.5") == "v:1000.5"));
        Console.WriteLine("p_grpbig=" + (ParseLabel("1,234,567,890.12345") == "v:1234567890.12345"));
        Console.WriteLine("p_max=" + (ParseLabel("79228162514264337593543950335") == "v:79228162514264337593543950335"));
        Console.WriteLine("p_min=" + (ParseLabel("-79228162514264337593543950335") == "v:-79228162514264337593543950335"));
        Console.WriteLine("p_scale28=" + (ParseLabel("1.0000000000000000000000000000") == "v:1.0000000000000000000000000000"));
        Console.WriteLine("p_28sig=" + (ParseLabel("1.2345678901234567890123456789") == "v:1.2345678901234567890123456789"));

        // ---- throwing Parse ERROR matrix (classification only) ----
        Console.WriteLine("e_ovf1=" + (ParseLabel("79228162514264337593543950336") == "ovf"));
        Console.WriteLine("e_ovf2=" + (ParseLabel("-79228162514264337593543950336") == "ovf"));
        Console.WriteLine("e_ovf30sig=" + (ParseLabel("123456789012345678901234567890") == "ovf"));
        Console.WriteLine("e_fmt_abc=" + (ParseLabel("abc") == "fmt"));
        Console.WriteLine("e_fmt_empty=" + (ParseLabel("") == "fmt"));
        Console.WriteLine("e_fmt_ws=" + (ParseLabel("   ") == "fmt"));
        Console.WriteLine("e_fmt_2pt=" + (ParseLabel("1.2.3") == "fmt"));
        Console.WriteLine("e_fmt_leadcomma=" + (ParseLabel(",1") == "fmt"));
        Console.WriteLine("e_fmt_exp=" + (ParseLabel("1e3") == "fmt"));
        Console.WriteLine("e_fmt_expU=" + (ParseLabel("1E3") == "fmt"));
        Console.WriteLine("e_fmt_signws=" + (ParseLabel("+ 42") == "fmt"));
        Console.WriteLine("e_fmt_signws2=" + (ParseLabel("- 42") == "fmt"));
        Console.WriteLine("e_null=" + (ParseLabel(null) == "null"));

        // ============ 2. Parse -> ToString scale-preservation round-trip ============
        Console.WriteLine("s_150=" + (string.Format("{0}", decimal.Parse("1.50")) == "1.50"));
        Console.WriteLine("s_100=" + (string.Format("{0}", decimal.Parse("0.100")) == "0.100"));
        Console.WriteLine("s_pos0=" + (string.Format("{0}", decimal.Parse("0.00")) == "0.00"));
        Console.WriteLine("s_big=" + (string.Format("{0}", decimal.Parse("1234567890.12345")) == "1234567890.12345"));
        Console.WriteLine("s_pad=" + (string.Format("{0}", decimal.Parse("000.500")) == "0.500"));
        Console.WriteLine("s_grp=" + (string.Format("{0}", decimal.Parse("1,234.50")) == "1234.50"));
        Console.WriteLine("s_max=" + (string.Format("{0}", decimal.Parse("79228162514264337593543950335")) == "79228162514264337593543950335"));

        // ============ 3. TryParse CONSISTENCY MATRIX ============
        Console.WriteLine("t_ok=" + (TryLabel("3.14") == "v:3.14"));
        Console.WriteLine("t_okneg=" + (TryLabel("-0.5") == "v:-0.5"));
        Console.WriteLine("t_okplus=" + (TryLabel("+42") == "v:42"));
        Console.WriteLine("t_okws=" + (TryLabel("  7  ") == "v:7"));
        Console.WriteLine("t_okgrp=" + (TryLabel("1,234.5") == "v:1234.5"));
        Console.WriteLine("t_okpt=" + (TryLabel(".25") == "v:0.25"));
        Console.WriteLine("t_fail_junk=" + (TryLabel("xyz") == "fail"));
        Console.WriteLine("t_fail_empty=" + (TryLabel("") == "fail"));
        Console.WriteLine("t_fail_2pt=" + (TryLabel("1.2.3") == "fail"));
        Console.WriteLine("t_fail_leadcomma=" + (TryLabel(",1") == "fail"));
        Console.WriteLine("t_fail_exp=" + (TryLabel("1e3") == "fail"));
        Console.WriteLine("t_fail_ovf=" + (TryLabel("79228162514264337593543950336") == "fail"));
        Console.WriteLine("t_fail_ovfneg=" + (TryLabel("-79228162514264337593543950336") == "fail"));
        Console.WriteLine("t_fail_ws=" + (TryLabel("   ") == "fail"));
        Console.WriteLine("t_null=" + (TryLabel(null) == "fail"));
        Console.WriteLine("t_max=" + (TryLabel("79228162514264337593543950335") == "v:79228162514264337593543950335"));
        Console.WriteLine("t_scale=" + (TryLabel("1.50") == "v:1.50"));

        // ============ 4. rounding / floor / ceiling / truncate on PARSED values ============
        decimal r1 = decimal.Round(decimal.Parse("1.005"), 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_afz=" + (string.Format("{0}", r1) == "1.01"));
        decimal r2 = decimal.Round(decimal.Parse("1.005"), 2, MidpointRounding.ToEven);
        Console.WriteLine("r_toeven=" + (string.Format("{0}", r2) == "1.00"));
        decimal r3 = decimal.Round(decimal.Parse("2.675"), 2);
        Console.WriteLine("r_2675=" + (string.Format("{0}", r3) == "2.68"));
        decimal r4 = decimal.Round(decimal.Parse("4.5"));
        Console.WriteLine("r_45=" + (string.Format("{0}", r4) == "4"));
        decimal r5 = decimal.Round(decimal.Parse("-4.5"));
        Console.WriteLine("r_neg45=" + (string.Format("{0}", r5) == "-4"));
        decimal r6 = decimal.Round(decimal.Parse("5.5"));
        Console.WriteLine("r_55=" + (string.Format("{0}", r6) == "6"));
        decimal r7 = decimal.Round(decimal.Parse("-0.5"), 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_neg05afz=" + (string.Format("{0}", r7) == "-1"));
        decimal r8 = decimal.Round(decimal.Parse("10.5"), 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_105afz=" + (string.Format("{0}", r8) == "11"));
        decimal r9 = decimal.Round(decimal.Parse("2.25"), 1, MidpointRounding.ToEven);
        Console.WriteLine("r_225te=" + (string.Format("{0}", r9) == "2.2"));
        decimal r10 = decimal.Round(decimal.Parse("2.25"), 1, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_225afz=" + (string.Format("{0}", r10) == "2.3"));
        decimal r11 = decimal.Round(decimal.Parse("12345.6789"), 2);
        Console.WriteLine("r_big=" + (string.Format("{0}", r11) == "12345.68"));
        decimal f1 = decimal.Floor(decimal.Parse("-0.1"));
        Console.WriteLine("r_floor_neg=" + (string.Format("{0}", f1) == "-1"));
        decimal f2 = decimal.Ceiling(decimal.Parse("-0.1"));
        Console.WriteLine("r_ceil_neg=" + (f2 == 0m));
        decimal f3 = decimal.Truncate(decimal.Parse("-0.9"));
        Console.WriteLine("r_trunc_neg=" + (f3 == 0m));
        decimal f4 = decimal.Truncate(decimal.Parse("3.9"));
        Console.WriteLine("r_trunc_pos=" + (string.Format("{0}", f4) == "3"));

        // ============ 5. conversions on PARSED values (truncate + range) ============
        Console.WriteLine("c_i32=" + (decimal.ToInt32(decimal.Parse("3.7")) == 3));
        Console.WriteLine("c_i32neg=" + (decimal.ToInt32(decimal.Parse("-3.7")) == -3));
        Console.WriteLine("c_i64=" + (decimal.ToInt64(decimal.Parse("1234567890.99")) == 1234567890L));
        Console.WriteLine("c_i64max=" + (decimal.ToInt64(decimal.Parse("9223372036854775807")) == long.MaxValue));
        Console.WriteLine("c_roundtrip=" + (decimal.Parse(decimal.Parse("1234.5678").ToString()) == 1234.5678m));
    }
}
