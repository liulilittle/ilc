// e2e_dec.cs -- E2E decimal probe (exclusive dir tmp/e2e_dec).
//
// Pipeline: built against net9 reference assemblies (Microsoft.NETCore.App.Ref
// 9.0.6), oracle-run on the private dotnet 9.0.19 runtime (tmp/net910/net9),
// transpiled by src/il2cppc to C++, compiled by MinGW cmake, output diffed
// byte-for-byte against the oracle.
//
// KNOWN EMITTER DEFECT WORKAROUND: this program uses ONLY decimal.Parse --
// NO decimal literals anywhere (the emitter mis-compiles decimal literal
// constants). Every decimal value enters through Parse("...").
//
// Checks:
//   T1  Parse("0.1") + Parse("0.2") == Parse("0.3")   exact base-10 addition
//   T2  Parse("2")   * Parse("3")   == Parse("6")     integer-valued multiply
//   T3  ordering via static decimal.Compare across a sign/magnitude chain,
//       plus scale-insensitive equality ("1.10" vs "1.100")
//   T4  ToString roundtrip of Parse("123.45") -> "123.45"
//
// Output contract: one "label=PASS|FAIL" line per check, evidence lines with
// raw values, then exactly "ALL-PASS" (exit 0) or "ALL-FAIL count=N" (exit 1).
using System;

public static class Program
{
    private static int failures;

    private static void Check(string label, bool ok)
    {
        Console.WriteLine(label + "=" + (ok ? "PASS" : "FAIL"));
        if (!ok)
        {
            failures++;
        }
    }

    public static int Main()
    {
        // ---- T1: 0.1 + 0.2 == 0.3 (exact decimal; binary FP would fail this)
        decimal a = decimal.Parse("0.1");
        decimal b = decimal.Parse("0.2");
        decimal c = decimal.Parse("0.3");
        decimal sum = a + b;
        Check("t1_add_01_02_03", sum == c);
        Console.WriteLine("t1_sum=" + sum.ToString());

        // ---- T2: 2 * 3 == 6
        decimal d = decimal.Parse("2");
        decimal e = decimal.Parse("3");
        decimal f = decimal.Parse("6");
        decimal prod = d * e;
        Check("t2_mul_2_3_6", prod == f);
        Console.WriteLine("t2_prod=" + prod.ToString());

        // ---- T3: ordering chain -2.5 < 0 < 0.0001 < 1 < 123.45, and
        //          -999.99 < -2.5 < 123.45; scale-insensitive equality.
        decimal n = decimal.Parse("-2.5");
        decimal z = decimal.Parse("0");
        decimal tiny = decimal.Parse("0.0001");
        decimal one = decimal.Parse("1");
        decimal mid = decimal.Parse("123.45");
        decimal negbig = decimal.Parse("-999.99");
        bool ord =
            decimal.Compare(n, z) < 0 &&
            decimal.Compare(z, tiny) < 0 &&
            decimal.Compare(tiny, one) < 0 &&
            decimal.Compare(one, mid) < 0 &&
            decimal.Compare(negbig, n) < 0 &&
            decimal.Compare(mid, negbig) > 0 &&
            decimal.Compare(decimal.Parse("1.10"), decimal.Parse("1.100")) == 0 &&
            decimal.Compare(z, z) == 0;
        Check("t3_compare_ordering", ord);
        Console.WriteLine("t3_cmp_neg_zero=" + decimal.Compare(n, z));
        Console.WriteLine("t3_cmp_zero_tiny=" + decimal.Compare(z, tiny));
        Console.WriteLine("t3_cmp_one_mid=" + decimal.Compare(one, mid));
        Console.WriteLine("t3_cmp_scale_eq=" + decimal.Compare(decimal.Parse("1.10"), decimal.Parse("1.100")));

        // ---- T4: ToString roundtrip of 123.45
        string s = "123.45";
        string rt = decimal.Parse(s).ToString();
        Check("t4_roundtrip_123_45", rt == s);
        Console.WriteLine("t4_str=" + rt);

        Console.WriteLine(failures == 0 ? "ALL-PASS" : "ALL-FAIL count=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
