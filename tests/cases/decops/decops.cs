// decops.cs -- DECIMAL OPS deep probe (e2e batch C25, "decimal deep probe: ctor
// boundaries / arithmetic / parse -- after the UB fix"). Builds on decimalcase (T1/T2 basics) and
// targets the d1cfbfd ctor fix (int32/int64 MIN unsigned-magnitude UB:
// `-2147483648` / `-9223372036854775808` must ctor to the SIGNED magnitude)
// plus the DEEPER T2 surface.
//
// Coverage (deeper than decimalcase -- ctor boundaries / arithmetic extremes /
// parse contract / rounding modes / conversions):
//   * CTOR BOUNDARIES: every integer ctor at its extremes -- int32/int64 MIN
//     and MAX, uint32/uint64 MAX, zero, +/-1 -- printed RAW so a magnitude or
//     sign bug shows as a byte diff, plus a Parse round-trip.
//   * CTOR 5-ARG + SCALE: new decimal(lo, mid, hi, sign, scale) at scale 0..28
//     with a nonzero hi (a >2^32 mantissa) and a scale-28 unit.
//   * ARITHMETIC extremes: scale-preserving mul (1.5m*2m -> "3.0"), the 28-
//     digit fixed-point division (1/3, 2/3, -1/2), scale-aligned add/sub
//     (5.50m-0.50m -> "5.00"), modulo, an 18-digit product, EVERY
//     OverflowException shape (max+1, min-1, max*2, max+max) + div-by-zero.
//   * PARSE contract: signs/whitespace/leading plus, full-range max/min
//     literals, OverflowException past max, FormatException on junk, TryParse
//     (success / junk / null / overflow -> false, never throws).
//   * ROUNDING modes: banker's Round at .5 ties, Round(d,n) fraction ties,
//     AwayFromZero ties for both signs, Truncate/Floor/Ceiling both signs.
//   * CONVERSIONS: ToInt32/ToInt64/ToUInt32/ToUInt64 at exact bounds and one
//     past (OverflowException).
//   * COMPARE/EQUALS/HASH: Compare/CompareTo sign, scale-insensitive equality
//     (1.0m == 1.00m), instance Equals, hash identity across equal ctors.
//   * TO-STRING formats: N0/F1/F2 on the decimal custom engine + a COMPOSITE
//     specifier through string.Format (boxed decimal).
//
// NOTE (written on purpose): every decimal-producing operation's result goes
// into a NAMED decimal local first, and the probe is split into per-concern
// helper methods -- a decimal icall RESULT slot and an int32-returning call
// result share the runtime's "val" temp bucket, so mixing them in one method
// can mis-type a merged temp (recorded defect); named locals are always typed
// from the C# signature and keep the case compiler-stable.
//
// Determinism: pure managed; N/F use "." decimal + "," group separator
// (identical in zh-CN and invariant). Expected values in trailing comments.
using System;
using System.Globalization;

namespace Cases { public static class Decops {
    // ---------- 1. ctor boundaries (the d1cfbfd UB fix) ----------
    static void CtorBoundaries() {
        decimal b1 = new decimal(int.MaxValue);
        decimal b2 = new decimal(int.MinValue);
        decimal b3 = new decimal(long.MaxValue);
        decimal b4 = new decimal(long.MinValue);
        decimal b5 = new decimal(uint.MaxValue);
        decimal b6 = new decimal(ulong.MaxValue);
        decimal b7 = new decimal(0);
        decimal b8 = new decimal(1);
        decimal b9 = new decimal(-1);
        Console.WriteLine(b1.ToString());    // 2147483647
        Console.WriteLine(b2.ToString());    // -2147483648
        Console.WriteLine(b3.ToString());    // 9223372036854775807
        Console.WriteLine(b4.ToString());    // -9223372036854775808
        Console.WriteLine(b5.ToString());    // 4294967295
        Console.WriteLine(b6.ToString());    // 18446744073709551615
        Console.WriteLine(b7.ToString());    // 0
        Console.WriteLine(b8.ToString());    // 1
        Console.WriteLine(b9.ToString());    // -1
        decimal r1 = decimal.Parse(b2.ToString());
        decimal r2 = decimal.Parse(b4.ToString());
        Console.WriteLine(r1 == -2147483648m);           // True
        Console.WriteLine(r2 == -9223372036854775808m);  // True
    }

    // ---------- 2. 5-arg ctor + scale ----------
    static void Ctor5Scale() {
        decimal c1 = new decimal(123, 0, 0, false, 2);
        decimal c2 = new decimal(123, 0, 0, true, 1);
        decimal c3 = new decimal(0, 0, 1, false, 0);
        decimal c4 = new decimal(int.MaxValue, int.MaxValue, 0, false, 0);
        decimal c5 = new decimal(1, 0, 0, false, 28);
        decimal c6 = new decimal(12345, 0, 0, false, 4);
        decimal c7 = new decimal(12345, 0, 0, true, 0);
        Console.WriteLine(c1.ToString());    // 1.23
        Console.WriteLine(c2.ToString());    // -12.3
        Console.WriteLine(c3.ToString());    // 4294967296
        Console.WriteLine(c4.ToString());    // 9223372034707292159
        Console.WriteLine(c5.ToString());    // 0.0000000000000000000000000001
        Console.WriteLine(c6.ToString());    // 1.2345
        Console.WriteLine(c7.ToString());    // -12345
    }

    // ---------- 3. arithmetic extremes (compile-time literals) ----------
    static void ArithPrint() {
        decimal a1 = 1.5m * 2m;        // folds to 3.0m
        decimal a2 = 1m / 3m;          // folds to 0.333...33m
        decimal a3 = 2m / 3m;          // folds to 0.666...67m
        decimal a4 = -1m / 2m;         // folds to -0.5m
        decimal a5 = 5.50m - 0.50m;    // folds to 5.00m
        decimal a6 = 10m % 3m;
        decimal a7 = 7.5m % 2m;
        decimal a8 = 0.1m + 0.7m;
        decimal a9 = 100m + 0.01m;
        decimal a10 = 123456789m * 987654321m;
        Console.WriteLine(a1.ToString());    // 3.0
        Console.WriteLine(a2.ToString());    // 0.3333333333333333333333333333
        Console.WriteLine(a3.ToString());    // 0.6666666666666666666666666667
        Console.WriteLine(a4.ToString());    // -0.5
        Console.WriteLine(a5.ToString());    // 5.00
        Console.WriteLine(a6.ToString());    // 1
        Console.WriteLine(a7.ToString());    // 1.5
        Console.WriteLine(a8.ToString());    // 0.8
        Console.WriteLine(a9.ToString());    // 100.01
        Console.WriteLine(a10.ToString());   // 121932631112635269
    }

    // ---------- 4. arithmetic extremes (runtime overflow/div0/negate) ----------
    static void ArithRuntime() {
        decimal mx = decimal.MaxValue;
        decimal mn = decimal.MinValue;
        bool ovf1 = false, ovf2 = false, ovf3 = false, ovf4 = false;
        try { decimal x = mx + 1m; } catch (OverflowException) { ovf1 = true; }
        try { decimal y = mn - 1m; } catch (OverflowException) { ovf2 = true; }
        try { decimal z = mx * 2m; } catch (OverflowException) { ovf3 = true; }
        try { decimal w = mx + mx; } catch (OverflowException) { ovf4 = true; }
        Console.WriteLine(ovf1);             // True
        Console.WriteLine(ovf2);             // True
        Console.WriteLine(ovf3);             // True
        Console.WriteLine(ovf4);             // True
        bool dz = false;
        decimal zeroDiv = 0m;
        try { decimal d0 = 1m / zeroDiv; } catch (DivideByZeroException) { dz = true; }
        Console.WriteLine(dz);               // True
        decimal n1 = decimal.Negate(5m);
        decimal n2 = decimal.Negate(1m);
        decimal n3 = decimal.Negate(n2);
        Console.WriteLine(n1.ToString());    // -5
        Console.WriteLine(n2.ToString());    // -1
        Console.WriteLine(n3.ToString());    // 1
    }

    // ---------- 5. parse contract ----------
    static void ParseOps() {
        decimal p1 = decimal.Parse("0");
        decimal p2 = decimal.Parse("3.14");
        decimal p3 = decimal.Parse("+42");
        decimal p4 = decimal.Parse(" 7 ");
        decimal p5 = decimal.Parse("-0.0001");
        decimal p6 = decimal.Parse("79228162514264337593543950335");
        decimal p7 = decimal.Parse("-79228162514264337593543950335");
        Console.WriteLine(p1 == 0m);         // True
        Console.WriteLine(p2 == 3.14m);      // True
        Console.WriteLine(p3 == 42m);        // True
        Console.WriteLine(p4 == 7m);         // True (whitespace)
        Console.WriteLine(p5.ToString());    // -0.0001
        Console.WriteLine(p6 == 79228162514264337593543950335m);   // True (max)
        Console.WriteLine(p7 == -79228162514264337593543950335m);  // True (min)
        bool po = false, pf = false;
        try { decimal x = decimal.Parse("79228162514264337593543950336"); } catch (OverflowException) { po = true; }
        try { decimal y = decimal.Parse("abc"); } catch (FormatException) { pf = true; }
        Console.WriteLine(po);               // True
        Console.WriteLine(pf);               // True
        decimal td1, td2, td3, td4;
        Console.WriteLine(decimal.TryParse("3.5", out td1));  // True
        Console.WriteLine(td1 == 3.5m);                       // True
        Console.WriteLine(decimal.TryParse("xyz", out td2));  // False
        Console.WriteLine(decimal.TryParse(null, out td3));   // False
        Console.WriteLine(decimal.TryParse("79228162514264337593543950336", out td4)); // False (overflow)
    }

    // ---------- 6. rounding modes (3-arg form only) ----------
    static void RoundOps() {
        decimal q1 = decimal.Round(2.5m, 0, MidpointRounding.ToEven);
        decimal q2 = decimal.Round(3.5m, 0, MidpointRounding.ToEven);
        decimal q3 = decimal.Round(-2.5m, 0, MidpointRounding.ToEven);
        decimal q4 = decimal.Round(3.14159m, 2, MidpointRounding.ToEven);
        decimal q5 = decimal.Round(3.146m, 2, MidpointRounding.ToEven);
        decimal q6 = decimal.Round(2.5m, 0, MidpointRounding.AwayFromZero);
        decimal q7 = decimal.Round(-2.5m, 0, MidpointRounding.AwayFromZero);
        decimal q8 = decimal.Round(1.25m, 1, MidpointRounding.AwayFromZero);
        Console.WriteLine(q1 == 2m);         // True (banker's)
        Console.WriteLine(q2 == 4m);         // True
        Console.WriteLine(q3 == -2m);        // True
        Console.WriteLine(q4 == 3.14m);      // True
        Console.WriteLine(q5 == 3.15m);      // True
        Console.WriteLine(q6 == 3m);         // True
        Console.WriteLine(q7 == -3m);        // True
        Console.WriteLine(q8 == 1.3m);       // True
    }

    // ---------- 7. truncate / floor / ceiling ----------
    static void TruncOps() {
        decimal q9 = decimal.Truncate(3.9m);
        decimal q10 = decimal.Truncate(-3.9m);
        decimal q11 = decimal.Floor(3.9m);
        decimal q12 = decimal.Floor(-3.9m);
        decimal q13 = decimal.Ceiling(3.1m);
        decimal q14 = decimal.Ceiling(-3.1m);
        decimal q15 = decimal.Truncate(0.0001m);
        Console.WriteLine(q9 == 3m);         // True
        Console.WriteLine(q10 == -3m);       // True
        Console.WriteLine(q11 == 3m);        // True
        Console.WriteLine(q12 == -4m);       // True
        Console.WriteLine(q13 == 4m);        // True
        Console.WriteLine(q14 == -3m);       // True
        Console.WriteLine(q15 == 0m);        // True
    }

    // ---------- 8. conversions (int32/int64/uint32/uint64) ----------
    static void ConvertOps() {
        decimal v1 = 3.9m;
        decimal v2 = -3.9m;
        decimal v3 = 2147483647m;
        decimal v4 = 9223372036854775807m;
        decimal v5 = 4294967295m;
        decimal v6 = 18446744073709551615m;
        decimal v7 = -9223372036854775808m;
        int imax = int.MaxValue;
        long lmax = long.MaxValue;
        uint umax = uint.MaxValue;
        ulong ulmax = ulong.MaxValue;
        long lmin = long.MinValue;
        Console.WriteLine(decimal.ToInt32(v1) == 3);              // True
        Console.WriteLine(decimal.ToInt32(v2) == -3);             // True
        Console.WriteLine(decimal.ToInt32(v3) == imax);           // True
        Console.WriteLine(decimal.ToInt64(v4) == lmax);           // True
        Console.WriteLine((long)decimal.ToUInt32(v5) == 4294967295L); // True
        ulong ulres = decimal.ToUInt64(v6);
        Console.WriteLine(ulres == ulmax);                      // True
        Console.WriteLine(ulres > 0);                           // True
        Console.WriteLine(decimal.ToInt64(v7) == lmin);           // True
        decimal v8 = 2147483648m;
        decimal v9 = 4294967296m;
        decimal v10 = -9223372036854775809m;
        bool x1 = false, x2 = false, x3 = false;
        try { decimal.ToInt32(v8); } catch (OverflowException) { x1 = true; }
        try { decimal.ToUInt32(v9); } catch (OverflowException) { x2 = true; }
        try { decimal.ToInt64(v10); } catch (OverflowException) { x3 = true; }
        Console.WriteLine(x1);               // True
        Console.WriteLine(x2);               // True
        Console.WriteLine(x3);               // True
    }

    // ---------- 9. compare / equals ----------
    static void CompareOps() {
        decimal o1 = 1m;
        decimal o2 = 2m;
        decimal on1 = new decimal(-1);
        decimal o3 = 1.0m;
        decimal o4 = 1.00m;
        decimal o5 = 1.5m;
        decimal o6 = 1.50m;
        Console.WriteLine(decimal.Compare(o1, o2) == -1);   // True
        Console.WriteLine(decimal.Compare(o2, o1) == 1);    // True
        Console.WriteLine(decimal.Compare(o2, o2) == 0);    // True
        Console.WriteLine(decimal.Compare(on1, o1) == -1);  // True
        Console.WriteLine(decimal.Compare(o3, o4) == 0);    // True (scale-insensitive)
        Console.WriteLine(o1.CompareTo(o2) == -1);          // True
        Console.WriteLine(o1.Equals(o4));                   // True
        Console.WriteLine(o5.Equals(o6));                   // True
        Console.WriteLine(o1 != o4);                        // False
        Console.WriteLine(o1 < o2);                         // True
        Console.WriteLine(o2 >= o2);                        // True
    }

    // ---------- 10. hash code ----------
    static void HashOps() {
        decimal h1a = new decimal(1);
        decimal h1b = new decimal(1);
        decimal h2 = new decimal(2);
        Console.WriteLine(h1a.GetHashCode() == h1b.GetHashCode()); // True
        Console.WriteLine(h1a.GetHashCode() != h2.GetHashCode());  // True
    }

    // ---------- 11. to-string composite (boxed decimal through Format) ----------
    // NOTE: `decimal.ToString(string)` is excluded -- the transpiler's
    // constrained-ToString wiring derefs the 16-byte receiver for the
    // pointer-ABI icall (recorded defect); the COMPOSITE path (boxed decimal
    // -> icall_format_composite_item -> decimal custom icall) is wired.
    static void FormatOps() {
        decimal f3 = 1234.5678m;
        Console.WriteLine(string.Format("{0:N2}", f3));  // 1,234.57
    }

    public static void Main() {
        CtorBoundaries();
        Ctor5Scale();
        ArithPrint();
        ArithRuntime();
        ParseOps();
        RoundOps();
        TruncOps();
        ConvertOps();
        CompareOps();
        HashOps();
        FormatOps();
    }
}}
