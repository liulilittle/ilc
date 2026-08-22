using System;

// E2E math domain: System.Math + System.MathF over the wired icall surface
// (MethodMap.Math.cs + MethodMap.MathExtra.cs + MethodMap.MathF.cs).
// Oracle: local .NET 9 runtime (tmp/net910/net9) -- MathF / Math.Log2 /
// Math.Clamp are Core-only members absent from the net48 baseline.
//
// EXCLUSIONS (unwired surface, same NOTE convention as tmp/icallcase):
//   * Math.Clamp(long/double) -- only icall_math_clamp_i (int32) exists;
//     TryMapMathExtra routes ANY 3-arg Clamp to the int32 icall, so only
//     Clamp(int,int,int) is asserted here.
//   * Math.Sign(double) -- TryMapMath routes non-int32 Sign to sign_l (the
//     int64 slot); a double arg would truncate. Only Sign(int)/Sign(long)
//     asserted.
//   * MathF.Clamp -- no MethodMap.MathF case (would hit the generated-name
//     path and fail the C++ build).
//   * MathF.DivRem -- no such BCL member.
//   * NaN Min/Max operand ordering -- net48 contract (NaN loses) vs .NET
//     Core 3+ IEEE-754-2019 propagation is a documented divergence, not an
//     e2e target; no NaN operands fed to Min/Max.
static class Program
{
    static int failures = 0;

    static void Check(string name, bool ok)
    {
        if (!ok) { failures++; Console.WriteLine($"FAIL {name}"); }
        else Console.WriteLine($"PASS {name}");
    }

    // Relative tolerance for transcendental results (libm-bound icalls):
    // both sides bind to the platform CRT on this machine, but exact-bit
    // equality is not part of the libm contract for these values.
    static bool Near(double a, double b)
    {
        double scale = Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
        return Math.Abs(a - b) <= 1e-12 * scale;
    }

    static bool NearF(float a, float b)
    {
        float scale = MathF.Max(1.0f, MathF.Max(MathF.Abs(a), MathF.Abs(b)));
        return MathF.Abs(a - b) <= 1e-6f * scale;
    }

    static int Main()
    {
        // ---- Math.Sqrt ----
        Check("math.sqrt.exact", Math.Sqrt(144.0) == 12.0 && Math.Sqrt(0.0) == 0.0 && Math.Sqrt(1.0) == 1.0);
        Check("math.sqrt.irrational", Near(Math.Sqrt(2.0), 1.4142135623730951));

        // ---- Math.Pow ----
        Check("math.pow.integral", Math.Pow(2.0, 10.0) == 1024.0 && Math.Pow(4.0, 3.0) == 64.0 && Math.Pow(10.0, 0.0) == 1.0);
        Check("math.pow.fractional", Near(Math.Pow(2.0, 0.5), Math.Sqrt(2.0)));

        // ---- Math.Abs: int / long / double ----
        Check("math.abs.i", Math.Abs(-9) == 9 && Math.Abs(7) == 7 && Math.Abs(0) == 0);
        Check("math.abs.l", Math.Abs(-999999999999L) == 999999999999L && Math.Abs(42L) == 42L);
        Check("math.abs.d", Math.Abs(-7.5) == 7.5 && Math.Abs(3.25) == 3.25);

        // ---- Math.Min / Math.Max: int / double ----
        Check("math.minmax.i", Math.Min(3, 5) == 3 && Math.Max(3, 5) == 5
            && Math.Min(-7, -2) == -7 && Math.Max(-7, -2) == -2);
        Check("math.minmax.d", Math.Min(2.5, 3.5) == 2.5 && Math.Max(2.5, 3.5) == 3.5
            && Math.Min(-1.5, -2.5) == -2.5 && Math.Max(-1.5, -2.5) == -1.5);

        // ---- Math.Floor / Math.Ceiling ----
        Check("math.floorceil", Math.Floor(2.7) == 2.0 && Math.Floor(-2.3) == -3.0
            && Math.Ceiling(2.1) == 3.0 && Math.Ceiling(-2.9) == -2.0
            && Math.Floor(3.0) == 3.0 && Math.Ceiling(4.0) == 4.0);

        // ---- Math.Round: default MidpointRounding.ToEven (banker's) ----
        Check("math.round.banker", Math.Round(0.5) == 0.0 && Math.Round(1.5) == 2.0
            && Math.Round(2.5) == 2.0 && Math.Round(3.5) == 4.0 && Math.Round(-1.5) == -2.0);
        Check("math.round.plain", Math.Round(2.4) == 2.0 && Math.Round(2.6) == 3.0
            && Math.Round(-2.4) == -2.0 && Math.Round(-2.6) == -3.0);

        // ---- Math.Truncate ----
        Check("math.truncate", Math.Truncate(2.9) == 2.0 && Math.Truncate(-2.9) == -2.0
            && Math.Truncate(0.75) == 0.0);

        // ---- Math.Log / Log10 / Log2 ----
        Check("math.log.natural", Math.Log(1.0) == 0.0 && Math.Log(Math.E) == 1.0);
        Check("math.log.base", Near(Math.Log(8.0, 2.0), 3.0) && Near(Math.Log(100.0, 10.0), 2.0));
        Check("math.log10", Math.Log10(100.0) == 2.0 && Math.Log10(1000.0) == 3.0 && Math.Log10(1.0) == 0.0);
        Check("math.log2", Math.Log2(8.0) == 3.0 && Math.Log2(1024.0) == 10.0 && Math.Log2(1.0) == 0.0);

        // ---- Math.Sign: int / long ----
        Check("math.sign.i", Math.Sign(-5) == -1 && Math.Sign(0) == 0 && Math.Sign(7) == 1);
        Check("math.sign.l", Math.Sign(-9L) == -1 && Math.Sign(0L) == 0 && Math.Sign(42L) == 1);

        // ---- Math.IEEERemainder: x - y*round-to-nearest-even(x/y) ----
        Check("math.ieeeremainder", Math.IEEERemainder(7.0, 4.0) == -1.0
            && Math.IEEERemainder(9.0, 4.0) == 1.0 && Math.IEEERemainder(-7.0, 4.0) == 1.0
            && Math.IEEERemainder(8.0, 4.0) == 0.0);

        // ---- Math.DivRem: quotient returned, remainder via out ----
        int drI;
        long drL;
        Check("math.divrem.i", Math.DivRem(17, 5, out drI) == 3 && drI == 2
            && Math.DivRem(-17, 5, out drI) == -3 && drI == -2);
        Check("math.divrem.l", Math.DivRem(100L, 7L, out drL) == 14 && drL == 2
            && Math.DivRem(-100L, 7L, out drL) == -14 && drL == -2);

        // ---- Math.Clamp: int32 form only (see header exclusions) ----
        Check("math.clamp.i", Math.Clamp(15, 0, 10) == 10 && Math.Clamp(-3, 0, 10) == 0
            && Math.Clamp(5, 0, 10) == 5);

        // ---- MathF.Sqrt ----
        Check("mathf.sqrt.exact", MathF.Sqrt(16.0f) == 4.0f && MathF.Sqrt(0.0f) == 0.0f);
        Check("mathf.sqrt.irrational", NearF(MathF.Sqrt(2.0f), 1.4142135f));

        // ---- MathF.Pow ----
        Check("mathf.pow", MathF.Pow(2.0f, 5.0f) == 32.0f && MathF.Pow(3.0f, 3.0f) == 27.0f
            && NearF(MathF.Pow(2.0f, 0.5f), 1.4142135f));

        // ---- MathF.Abs ----
        Check("mathf.abs", MathF.Abs(-2.5f) == 2.5f && MathF.Abs(2.5f) == 2.5f);

        // ---- MathF.Min / MathF.Max ----
        Check("mathf.minmax", MathF.Min(1.5f, 2.5f) == 1.5f && MathF.Max(1.5f, 2.5f) == 2.5f
            && MathF.Min(-3.5f, -1.5f) == -3.5f && MathF.Max(-3.5f, -1.5f) == -1.5f);

        // ---- MathF.Floor / MathF.Ceiling ----
        Check("mathf.floorceil", MathF.Floor(1.9f) == 1.0f && MathF.Floor(-1.1f) == -2.0f
            && MathF.Ceiling(1.1f) == 2.0f && MathF.Ceiling(-1.9f) == -1.0f);

        // ---- MathF.Round: banker's (ToEven) on floats ----
        Check("mathf.round.banker", MathF.Round(0.5f) == 0.0f && MathF.Round(1.5f) == 2.0f
            && MathF.Round(2.5f) == 2.0f && MathF.Round(3.5f) == 4.0f);
        Check("mathf.round.plain", MathF.Round(2.4f) == 2.0f && MathF.Round(2.6f) == 3.0f);

        // ---- MathF.Truncate ----
        Check("mathf.truncate", MathF.Truncate(1.9f) == 1.0f && MathF.Truncate(-1.9f) == -1.0f);

        // ---- MathF.Log / Log2 / Log10 ----
        Check("mathf.log", MathF.Log(1.0f) == 0.0f && NearF(MathF.Log(MathF.E), 1.0f));
        Check("mathf.log2", MathF.Log2(8.0f) == 3.0f && MathF.Log2(1024.0f) == 10.0f);
        Check("mathf.log10", MathF.Log10(100.0f) == 2.0f && MathF.Log10(1000.0f) == 3.0f);

        // ---- MathF.Sign ----
        Check("mathf.sign", MathF.Sign(-3.5f) == -1 && MathF.Sign(0.0f) == 0 && MathF.Sign(3.5f) == 1);

        // ---- MathF.IEEERemainder ----
        Check("mathf.ieeeremainder", MathF.IEEERemainder(7.0f, 4.0f) == -1.0f
            && MathF.IEEERemainder(9.0f, 4.0f) == 1.0f);

        Console.WriteLine(failures == 0 ? "ALL-PASS" : string.Concat("FAILURES=", failures.ToString()));
        return failures == 0 ? 0 : 1;
    }
}
