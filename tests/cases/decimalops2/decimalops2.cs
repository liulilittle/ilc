//------------------------------------------------------------------------------
// decimalops2.cs -- decimal OPERATION deep probe 2 (e2e batch C54): multiply /
// divide chains, remainder (op_Modulus), and the comparison lattice --
// byte-exact vs the net48 oracle, dual-compiler (mingw + msvc).
//
// Goes DEEPER than decops (C25: ctor boundaries/parse/rounding/conversions)
// and decimaldeep2 (C43: op chains/rounding modes/format combos): this case
// is ENTIRELY arithmetic-operator focused on the decimal domain surface
// (MethodMap.Decimal.cs):
//
//   * S1 MUL/DIV CHAINS -- scale evolution across a chain (1.5m*0.2m ->
//     0.30m, then *1.5m -> 0.450m), the (a*b)/b and (a/b)*b round-trips
//     (the div-then-mul identity does NOT hold: 1m/3m*3m is 0.999...99),
//     the fixed-point 28-digit division lattice (1/3, 2/3, 1/7, 100/7),
//     and scale-alignment products (0.001m*0.001m -> 0.000001m).
//   * S2 REMAINDER -- runtime op_Modulus (icall_decimal_rem): sign of the
//     dividend (-10m%3m is -1), scale of the dividend (10.5m%3.25m is
//     0.75), the exact-divide/zero-dividend/divisor-greater cases, a
//     remainder chain, and DivideByZeroException.
//   * S3 COMPARISON lattice -- decimal.Compare/CompareTo/==/!=/</>/<=/>=:
//     scale-insensitive equality (1m==1.00m), sign ordering, Max/Min
//     extremes, symmetric Compare signs, and the operator superpositions.
//
// Discipline (decimaldeep2 C43/C44 precedents -- KEEP):
//   * EVERY decimal value is a NAMED decimal local first (the icall RESULT
//     slot / temp-bucket rule).
//   * RUNTIME operands only: a compile-time-folded constant expression
//     (`10m % 3m`) never reaches the icall, so every probe computes from
//     NAMED LOCALS (the C44 "NOT the compile-time-folded constant" rule).
//   * value output rides the wired composite channel string.Format("{0}", x)
//     (icall_format_composite_item -> icall_decimal_to_string).
//   * comparison results print as int32 (-1/0/1) or bool; decimals never
//     mix with an int32-returning icall in expression position (the S1
//     method keeps decimal temps only).
//
// Determinism: pure managed; N/F use "." decimal + "," group separator
// (identical in zh-CN and invariant). Expected values in trailing comments.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class DecimalOps2 {
    // ---------- S1: multiply / divide chains (decimal temps only) ----------
    private static void S1MulDiv() {
        decimal a = 1.5m;
        decimal b = 0.2m;
        decimal c = 3m;

        decimal m1 = a * b;                  // 0.30 (scale 2)
        Console.WriteLine("md1=" + string.Format("{0}", m1));
        decimal m2 = m1 * a;                 // 0.450 (scale 3)
        Console.WriteLine("md2=" + string.Format("{0}", m2));
        decimal m3 = a * c;                  // 4.5
        decimal m4 = m3 * b;                 // 0.90
        Console.WriteLine("md3=" + string.Format("{0}", m4));

        decimal rt1 = (a * b) / b;           // (1.5*0.2)/0.2 == 1.5 (exact round trip)
        Console.WriteLine("md4=" + string.Format("{0}", rt1));
        decimal p1 = 0.001m;
        decimal p2 = 0.001m;
        decimal p3 = p1 * p2;                // 0.000001 (scale 6)
        Console.WriteLine("md5=" + string.Format("{0}", p3));

        decimal q1 = 1m / 3m;                // 0.3333333333333333333333333333
        Console.WriteLine("md6=" + string.Format("{0}", q1));
        decimal q2 = q1 * 3m;                // 0.9999999999999999999999999999 (NOT 1)
        Console.WriteLine("md7=" + string.Format("{0}", q2));
        decimal q3 = 2m / 3m;                // 0.6666666666666666666666666667
        Console.WriteLine("md8=" + string.Format("{0}", q3));
        decimal q4 = 1m / 7m;                // 0.1428571428571428571428571429
        Console.WriteLine("md9=" + string.Format("{0}", q4));
        decimal q5 = q4 * 7m;                // 1.0000000000000000000000000003 (28-digit carry)
        Console.WriteLine("md10=" + string.Format("{0}", q5));

        decimal h1 = 100m / 7m;              // 14.285714285714285714285714286
        Console.WriteLine("md11=" + string.Format("{0}", h1));
        decimal h2 = h1 / 2m;                // 7.142857142857142857142857143
        Console.WriteLine("md12=" + string.Format("{0}", h2));
        decimal h3 = h2 * 2m;                // 14.285714285714285714285714286
        Console.WriteLine("md13=" + string.Format("{0}", h3));

        decimal big1 = 123456789m;
        decimal big2 = 987654321m;
        decimal big3 = big1 * big2;          // 121932631112635269
        Console.WriteLine("md14=" + string.Format("{0}", big3));
        decimal big4 = big3 / big1;          // 987654321 (exact round trip)
        Console.WriteLine("md15=" + string.Format("{0}", big4));

        decimal nc = -a;                     // -1.5
        decimal nc2 = nc * b;                // -0.30
        Console.WriteLine("md16=" + string.Format("{0}", nc2));
        decimal nc3 = nc / c;                // -0.5
        Console.WriteLine("md17=" + string.Format("{0}", nc3));
    }

    // ---------- S2: remainder (runtime op_Modulus -> icall_decimal_rem) ----------
    private static void S2Remainders() {
        decimal a = 10m;
        decimal b = 3m;
        decimal na = -10m;
        decimal nb = -3m;

        decimal r1 = a % b;                  // 1 (sign of dividend: positive)
        Console.WriteLine("rm1=" + string.Format("{0}", r1));
        decimal r2 = na % b;                 // -1
        Console.WriteLine("rm2=" + string.Format("{0}", r2));
        decimal r3 = a % nb;                 // 1 (sign still from dividend)
        Console.WriteLine("rm3=" + string.Format("{0}", r3));
        decimal r4 = na % nb;                // -1
        Console.WriteLine("rm4=" + string.Format("{0}", r4));

        decimal c1 = 10.5m;
        decimal c2 = 3.25m;
        decimal r5 = c1 % c2;                // 0.75 (scale of dividend)
        Console.WriteLine("rm5=" + string.Format("{0}", r5));
        decimal r6 = 7.5m % 2m;              // 1.5
        Console.WriteLine("rm6=" + string.Format("{0}", r6));
        decimal r7 = 0m % b;                 // 0
        Console.WriteLine("rm7=" + string.Format("{0}", r7));
        decimal r8 = 5m % 5m;                // 0 (exact divide)
        Console.WriteLine("rm8=" + string.Format("{0}", r8));
        decimal r9 = 3.7m % 0.5m;            // 0.2
        Console.WriteLine("rm9=" + string.Format("{0}", r9));
        decimal r10 = 1m % 0.0001m;          // 0 (divisor divides evenly)
        Console.WriteLine("rm10=" + string.Format("{0}", r10));
        decimal r11 = c1 % c1;               // 0.0 (scale preserved)
        Console.WriteLine("rm11=" + string.Format("{0}", r11));
        decimal r12 = c2 % a;                // 3.25 % 10 == 3.25 (divisor larger)
        Console.WriteLine("rm12=" + string.Format("{0}", r12));

        decimal r13 = r5 % c2;               // 0.75 % 3.25 == 0.75 (chain)
        Console.WriteLine("rm13=" + string.Format("{0}", r13));
        decimal r14 = r5 % 0.5m;             // 0.75 % 0.5 == 0.25
        Console.WriteLine("rm14=" + string.Format("{0}", r14));

        decimal r15 = a % b;
        decimal r16 = r15 % b;               // 1 % 3 == 1
        Console.WriteLine("rm15=" + string.Format("{0}", r16));

        bool dz = false;
        decimal zb = 0m;
        try { decimal x = a % zb; } catch (DivideByZeroException) { dz = true; }
        Console.WriteLine("rm_dz=" + dz);
    }

    // ---------- S3: comparison lattice ----------
    private static void S3Compares() {
        decimal z = 0m;
        decimal p = 1m;
        decimal p2 = 1.0m;
        decimal p3 = 1.00m;
        decimal half = 1.5m;
        decimal half2 = 1.50m;
        decimal q = 2m;
        decimal n1 = -1m;
        decimal big = decimal.MaxValue;
        decimal small = decimal.MinValue;

        Console.WriteLine("cm_lt=" + (p < q));
        Console.WriteLine("cm_gt=" + (q > p));
        Console.WriteLine("cm_le=" + (p <= p2));
        Console.WriteLine("cm_ge=" + (q >= p));
        Console.WriteLine("cm_eq=" + (p == p2));
        Console.WriteLine("cm_eq2=" + (p2 == p3));
        Console.WriteLine("cm_ne=" + (p != q));
        Console.WriteLine("cm_scaleEq=" + (half == half2));
        Console.WriteLine("cm_scaleLt=" + (half < p2));
        Console.WriteLine("cm_scaleLt2=" + (half2 < q));
        Console.WriteLine("cm_neg=" + (n1 < z));
        Console.WriteLine("cm_neg2=" + (n1 > small));
        Console.WriteLine("cm_max=" + (big > q));
        Console.WriteLine("cm_min=" + (small < n1));
        Console.WriteLine("cm_maxEq=" + (big == decimal.MaxValue));
        Console.WriteLine("cm_minNe=" + (small != big));

        Console.WriteLine("cm_cmp1=" + decimal.Compare(p, q));
        Console.WriteLine("cm_cmp2=" + decimal.Compare(q, p));
        Console.WriteLine("cm_cmp3=" + decimal.Compare(p, p2));
        Console.WriteLine("cm_cmp4=" + decimal.Compare(half, half2));
        Console.WriteLine("cm_cmp5=" + decimal.Compare(big, small));
        Console.WriteLine("cm_cmp6=" + decimal.Compare(small, big));
        Console.WriteLine("cm_cmp7=" + decimal.Compare(z, n1));
        Console.WriteLine("cm_ci1=" + p.CompareTo(q));
        Console.WriteLine("cm_ci2=" + q.CompareTo(p));
        Console.WriteLine("cm_ci3=" + p2.CompareTo(p3));
        Console.WriteLine("cm_ci4=" + n1.CompareTo(z));
        Console.WriteLine("cm_eq3=" + p.Equals(p3));
        Console.WriteLine("cm_eq4=" + n1.Equals(p));
        Console.WriteLine("cm_eq5=" + q.Equals(p));
    }

    public static void Main() {
        S1MulDiv();
        S2Remainders();
        S3Compares();
    }
}}
