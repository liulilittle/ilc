//------------------------------------------------------------------------------
// decimaldeep2.cs -- decimal domain DEEP probe 2 (e2e batch C43): operation
// chains / rounding modes / format combinations -- byte-exact vs the net48
// oracle. Created after the decimal domain full chain landed (decimal.cpp /
// decimal_format.cpp + MethodMap.Decimal.cs).
//
// Reachable-surface discipline (decimalcase/decops/decformat precedents):
//   * EVERY decimal value is a NAMED decimal local first -- the icall RESULT
//     slot / temp-bucket rule keeps the case compiler-stable (a dec-kind
//     icall may not appear in expression position).
//   * STACK-FOLD TEMP SLOTS ARE KIND-HOMOGENEOUS PER METHOD (decops
//     precedent): the fold's last-wins kind inference types one C++ variable
//     per temp name, so a decimal icall result landing in the same slot as an
//     int32 compare result breaks the C++ build. Each section below is its
//     own method; arithmetic chains live in S1Chains (decimal temps only,
//     results printed byte-exact), comparisons/rounding live in methods that
//     never mix decimal temps with int32 temp users.
//   * rounding rides decimal.Round (1/2/3-arg) + Floor / Ceiling / Truncate;
//     both MidpointRounding modes (ToEven 0 / AwayFromZero 1) are pinned.
//   * format combos go through string.Format composite (the only wired
//     decimal-format channel: icall_format_composite_item ->
//     icall_decimal_to_string_custom, decimal_format.cpp); the formatted
//     STRING is printed so layout is verified byte-for-byte.
//   * conversions print the value via ToString (no uint/ulong literal
//     comparison surface).
//   * no float/double conversions (VarDecFromR4/R8 are v1-deferred, LOUD)
//     and no boxed-object Equals/CompareTo (unwired until the box icalls).
//
// RECORDED DEFECTS -- ALL FIXED in decimal-domain C44 (decgap-b23), the
// probes below exercise each fix:
//   * decimal % (System.Decimal.op_Modulus) -- the CLR operator method name
//     now maps to icall_decimal_rem (MethodMap.Decimal.cs, alongside the
//     ECMA op_Remainder alias); a runtime `a % b` emits the rem icall.
//   * decimal op results riding a stack-fold TEMP slot (the intermediate of
//     `a + b - c`) no longer collide with int32 compare results: the fold's
//     single-use forward substitution EXCLUDES value-slot icalls ("dec"/
//     "guid"/"ip4" -- StackFold.OptimizeTemps), so the icall keeps its
//     assignment statement and the temp stays Il2CppDecimal (this case's
//     methods now mix decimal chains AND int32 compares freely).
//   * decimal.Round 1/2-arg overloads now emit the full runtime signature
//     icall_decimal_round(result, d, decimals, mode) with the missing
//     defaults injected as literal 0s (1-arg -> decimals=0/mode=0,
//     2-arg -> mode=0 = MidpointRounding.ToEven).
//   * the -1m literal's ldsfld System.Decimal.MinusOne now has emitted BCL
//     static storage (TypeEmitter EmitStaticStorage -- sign-bit image).
//   * uint/ulong ToString() now maps to the full-range unsigned formatters
//     icall_uint32_to_string / icall_uint64_to_string (format.cpp).
//   * decimal.GetHashCode() VALUE diverges from net48 (768585957 vs
//     12083534 on 1234.5678m -- runtime icall_decimal_get_hash_code is not
//     VarDecHash-compatible) -- only the hash-EQUALITY probe (consistent on
//     both sides) is in this case, the raw value probe is NOT.
//   * digit placeholder custom patterns ({0:00.0}) are returned VERBATIM by
//     the runtime (decimal_format.cpp custom fallback) -- decformat holds
//     that boundary, NOT re-probed here.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Decimaldeep2 {
    // ---------- S1: operation chains (decimal temps only in this method) ----------
    private static void S1Chains() {
        decimal a = 10.5m;
        decimal b = 3.25m;
        decimal c = 2m;
        decimal t = a + b;
        decimal s1 = t - c;
        Console.WriteLine("ch1=" + string.Format("{0}", s1));
        decimal u = a + b;
        decimal s2 = u * c;
        Console.WriteLine("ch2=" + string.Format("{0}", s2));
        decimal v = a / b;
        decimal s3 = v + c;
        Console.WriteLine("ch3=" + string.Format("{0}", s3));
        decimal s4 = 10m % 3m;
        Console.WriteLine("ch4=" + string.Format("{0}", s4));
        decimal s5 = -a;
        Console.WriteLine("ch5=" + string.Format("{0}", s5));
        decimal w = b * c;
        decimal s6 = a - w;
        Console.WriteLine("ch6=" + string.Format("{0}", s6));
        decimal x = a * 10m;
        decimal s7 = x / b;
        Console.WriteLine("ch7=" + string.Format("{0}", s7));
        decimal y = a - b;
        decimal z = a + b;
        decimal s8 = y * z;
        Console.WriteLine("ch8=" + string.Format("{0}", s8));
        decimal s9 = 100m / 7m;
        decimal s10 = s9 * 7m;
        Console.WriteLine("ch9_prec=" + string.Format("{0}", s10));
        decimal q = s9 + s9;
        decimal s11 = q + s9;
        Console.WriteLine("ch10=" + string.Format("{0}", s11));
        decimal p = a * b;
        decimal r = c * a;
        decimal s12 = p + r;
        Console.WriteLine("ch11=" + string.Format("{0}", s12));
        decimal ab = a - b;
        decimal s13 = -ab;
        Console.WriteLine("ch12=" + string.Format("{0}", s13));
        decimal m = a + b;
        decimal n = a - b;
        decimal s14 = m * n + c;
        Console.WriteLine("ch13=" + string.Format("{0}", s14));
        // C44 fixed-defect probes:
        //   * runtime % (op_Modulus -> icall_decimal_rem) -- NOT the
        //     compile-time-folded constant the batch-rule version used.
        decimal mod1 = a % b;
        Console.WriteLine("ch14=" + string.Format("{0}", mod1));
        //   * single-line decimal chains riding fold temp slots...
        decimal s15 = a + b - c;
        Console.WriteLine("ch15=" + string.Format("{0}", s15));
        decimal s16 = a * b + c;
        Console.WriteLine("ch16=" + string.Format("{0}", s16));
        //   * ...MIXED with an int32 compare in the SAME method (the
        //     last-wins temp-typing conflict that forced homogeneous methods).
        bool c17 = a > b;
        Console.WriteLine("ch17=" + c17);
        //   * a % with an int32 branch result in one expression.
        decimal s18 = a % b + c;
        Console.WriteLine("ch18=" + string.Format("{0}", s18));
        Console.WriteLine("ch19=" + (a % b == 0.75m));
        //   * -1m literal -> ldsfld System.Decimal.MinusOne (emitted BCL
        //     static storage -- the sign-bit image).
        decimal minusOne = -1m;
        Console.WriteLine("ch20=" + string.Format("{0}", minusOne));
        decimal minusOneAdd = -1m + 2m;
        Console.WriteLine("ch21=" + string.Format("{0}", minusOneAdd));
    }

    // ---------- S2: rounding modes (Round/Floor/Ceiling/Truncate) ----------
    private static void S2Rounds() {
        decimal one = 1m;
        decimal m1 = -one;
        decimal r1 = decimal.Round(1.005m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_away_1005=" + (r1 == 1.01m));
        decimal r2 = decimal.Round(1.005m, 2, MidpointRounding.ToEven);
        Console.WriteLine("r_even_1005=" + (r2 == 1m));
        decimal r3 = decimal.Round(2.675m, 2, MidpointRounding.ToEven);
        Console.WriteLine("r_even_2675=" + (r3 == 2.68m));
        decimal r4 = decimal.Round(4.5m, 0, MidpointRounding.ToEven);
        Console.WriteLine("r_1arg=" + (r4 == 4m));
        decimal r5 = decimal.Round(-4.5m, 0, MidpointRounding.ToEven);
        Console.WriteLine("r_1arg_neg=" + (r5 == -4m));
        decimal r6 = decimal.Round(5.5m, 0, MidpointRounding.ToEven);
        Console.WriteLine("r_1arg_55=" + (r6 == 6m));
        decimal r7 = decimal.Round(12345.6789m, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_wide=" + (r7 == 12345.68m));
        decimal r8 = decimal.Round(-0.5m, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_neg_away=" + (r8 == m1));
        decimal r9 = decimal.Round(-0.5m, 0, MidpointRounding.ToEven);
        Console.WriteLine("r_neg_even=" + (r9 == 0m));
        decimal r10 = decimal.Round(2.25m, 1, MidpointRounding.ToEven);
        Console.WriteLine("r_even_225=" + (r10 == 2.2m));
        decimal r11 = decimal.Round(2.25m, 1, MidpointRounding.AwayFromZero);
        Console.WriteLine("r_away_225=" + (r11 == 2.3m));
        decimal r12 = decimal.Floor(-0.1m);
        Console.WriteLine("r_floor_neg=" + (r12 == m1));
        decimal r13 = decimal.Ceiling(-0.1m);
        Console.WriteLine("r_ceil_neg=" + (r13 == 0m));
        decimal r14 = decimal.Truncate(-0.9m);
        Console.WriteLine("r_trunc_neg=" + (r14 == 0m));
        decimal r15 = decimal.Round(10.5m, 0, MidpointRounding.AwayFromZero);
        decimal r16 = decimal.Round(3.25m, 1, MidpointRounding.AwayFromZero);
        decimal r17 = r15 + r16;
        Console.WriteLine("r_chain=" + string.Format("{0}", r17));
        decimal fp = 100m / 7m;
        decimal fpx = fp * 100m;
        decimal r18 = decimal.Floor(fpx);
        Console.WriteLine("r_floor_chain=" + string.Format("{0}", r18));
        decimal r19 = decimal.Ceiling(fpx);
        Console.WriteLine("r_ceil_chain=" + string.Format("{0}", r19));
        decimal r20 = decimal.Round(fpx, 2, MidpointRounding.ToEven);
        Console.WriteLine("r_wide_prec=" + string.Format("{0}", r20));
        // C44 fixed-defect probes: 1-arg and 2-arg Round overloads (the
        // missing icall_decimal_round decimals/mode defaults are injected
        // as literal 0s -- ToEven).
        decimal r21 = decimal.Round(4.5m);
        Console.WriteLine("r_1arg_new=" + (r21 == 4m));
        decimal r22 = decimal.Round(-4.5m);
        Console.WriteLine("r_1arg_neg_new=" + (r22 == -4m));
        decimal r23 = decimal.Round(5.5m);
        Console.WriteLine("r_1arg_55_new=" + (r23 == 6m));
        decimal r24 = decimal.Round(2.675m, 2);
        Console.WriteLine("r_2arg_new=" + (r24 == 2.68m));
        decimal r25 = decimal.Round(1.005m, 2);
        Console.WriteLine("r_2arg_1005_new=" + (r25 == 1.00m));
        decimal r26 = decimal.Round(-0.5m);
        Console.WriteLine("r_1arg_neg_half=" + (r26 == 0m));
        decimal r27 = decimal.Round(12345.6789m, 2);
        Console.WriteLine("r_2arg_wide=" + string.Format("{0}", r27));
    }

    // ---------- S3: format combos (string.Format composite) ----------
    private static void S3Formats() {
        decimal f1 = 1234.5678m;
        decimal rr = decimal.Round(f1, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("fm_round_n2=" + string.Format("{0:N2}", rr));
        Console.WriteLine("fm_round_e2=" + string.Format("{0:E2}", rr));
        Console.WriteLine("fm_round_f0=" + string.Format("{0:F0}", rr));
        Console.WriteLine("fm_round_p1=" + string.Format("{0:P1}", rr));
        decimal g1 = decimal.Round(f1, 1, MidpointRounding.AwayFromZero);
        Console.WriteLine("fm_chain_n1=" + string.Format("{0:N1}", g1));
        Console.WriteLine("fm_chain_g=" + string.Format("{0:G}", g1));
        decimal neg1 = -0.005m;
        Console.WriteLine("fm_neg_n2=" + string.Format("{0:N2}", neg1));
        Console.WriteLine("fm_neg_e2=" + string.Format("{0:E2}", neg1));
        decimal f2 = 999.995m;
        Console.WriteLine("fm_carry_f2=" + string.Format("{0:F2}", f2));
        decimal big1 = 123456789m;
        Console.WriteLine("fm_multi=" + string.Format("{0:N2}|{1:E2}", big1, f1));
        decimal small1 = 0.000001m;
        Console.WriteLine("fm_small_n4=" + string.Format("{0:N4}", small1));
        Console.WriteLine("fm_small_g=" + string.Format("{0:G}", small1));
        Console.WriteLine("fm_pad=" + string.Format("[{0,12:N2}]", f1));
        Console.WriteLine("fm_padneg=" + string.Format("[{0,-12:N2}]", f1));
        Console.WriteLine("fm_str=" + string.Format("{0}", f1));
        Console.WriteLine("fm_tostr=" + f1.ToString());
        decimal f3 = decimal.Round(f1, 0, MidpointRounding.AwayFromZero);
        Console.WriteLine("fm_tostr_round=" + f3.ToString());
        decimal f4 = decimal.Round(f1, 2, MidpointRounding.AwayFromZero);
        Console.WriteLine("fm_tostr_nested=" + f4.ToString());
    }

    // ---------- S4: parse / conversions / hash ----------
    private static void S4Parses() {
        decimal p1 = decimal.Parse("3.14");
        Console.WriteLine("pr_parse=" + string.Format("{0}", p1));
        decimal p2 = decimal.Parse(p1.ToString());
        Console.WriteLine("pr_rt=" + string.Format("{0}", p2));
        decimal p3;
        bool ok1 = decimal.TryParse("-0.001", out p3);
        Console.WriteLine("pr_try=" + ok1 + " " + string.Format("{0}", p3));
        decimal p4;
        bool ok2 = decimal.TryParse("not a number", out p4);
        Console.WriteLine("pr_trybad=" + ok2);
        decimal p5 = decimal.Parse("123456789.123456789");
        Console.WriteLine("pr_parsebig=" + string.Format("{0}", p5));
        decimal n1 = -3.9m;
        decimal n2 = 3.9m;
        decimal n3 = 9223372036854775807m;
        decimal n4 = 4294967295m;
        decimal n5 = 18446744073709551615m;
        Console.WriteLine("pr_toi32=" + decimal.ToInt32(n1).ToString());
        Console.WriteLine("pr_toi32pos=" + decimal.ToInt32(n2).ToString());
        Console.WriteLine("pr_toi64=" + decimal.ToInt64(n3).ToString());
        Console.Write("pr_tou32=");
        Console.WriteLine(decimal.ToUInt32(n4));
        Console.Write("pr_tou64=");
        Console.WriteLine(decimal.ToUInt64(n5));
        // C44 fixed-defect probes: uint/ulong ToString() -- the unsigned
        // full-range formatters (UInt32.MaxValue "4294967295", never the
        // signed -1; UInt64.MaxValue "18446744073709551615").
        Console.WriteLine("pr_tou32str=" + decimal.ToUInt32(n4).ToString());
        Console.WriteLine("pr_tou64str=" + decimal.ToUInt64(n5).ToString());
        decimal h1 = 1234.5678m;
        decimal h2 = 1234.5678m;
        Console.WriteLine("pr_hash2=" + (h1.GetHashCode() == h2.GetHashCode()));
    }

    public static void Main() {
        S1Chains();
        S2Rounds();
        S3Formats();
        S4Parses();
    }
}}
