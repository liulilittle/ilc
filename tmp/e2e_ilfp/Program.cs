using System;
using System.Threading;

// e2e_ilfp -- Interlocked float/double/generic E2E probe (full pipeline:
// net9-refs compile -> managed oracle run -> il2cppc transpile -> MinGW
// native build -> byte-exact output diff).
//
// Value semantics exercised:
//   Exchange(ref float / ref double) -> returns the OLD value, stores the
//       NEW value; -0.0 sign-bit patterns checked on BOTH sides (returned
//       old value and stored new value) via 1/v sign detection.
//   CompareExchange(ref double)     -> returns the OLD value; writes only
//       when the location BITWISE-equals the comparand: +0.0 vs -0.0 are
//       numerically equal but bitwise distinct, so a -0.0 comparand MUST
//       MISS on a +0.0 location and HIT on a -0.0 location.
//   Exchange<long> (generic form)   -> returns the OLD value, stores the
//       NEW value (explicit type argument forces the generic IL call).
//
// Output contract: one "PASS <check>" line per check, then "ALL-PASS"
// (or "FAIL <check>" lines and "ALL-FAIL failures=N"). ASCII-only,
// deterministic -- safe for byte-exact diff between oracle and native.
namespace E2eIlFp
{
    static class Program
    {
        static int failures;

        static void Check(bool ok, string name)
        {
            if (ok)
            {
                Console.WriteLine("PASS " + name);
            }
            else
            {
                failures++;
                Console.WriteLine("FAIL " + name);
            }
        }

        // -0.0 detection without BitConverter: v == 0 and 1/v is -Infinity.
        static bool IsNegZero(float v) => v == 0f && 1f / v < 0f;
        static bool IsNegZero(double v) => v == 0.0 && 1.0 / v < 0.0;

        static void Main()
        {
            // --- Exchange(ref float): old-value semantics ---
            float f1 = 1.5f;
            float o1 = Interlocked.Exchange(ref f1, 2.5f);
            Check(o1 == 1.5f && f1 == 2.5f, "F1 float-exchange-basic");

            // OLD side carries the exact -0.0 pattern back to the caller.
            float f2 = -0.0f;
            float o2 = Interlocked.Exchange(ref f2, 1.0f);
            Check(IsNegZero(o2) && f2 == 1.0f, "F2 float-exchange-old-negzero");

            // NEW side lands in the location as exact -0.0.
            float f3 = 3.5f;
            float o3 = Interlocked.Exchange(ref f3, -0.0f);
            Check(o3 == 3.5f && IsNegZero(f3), "F3 float-exchange-new-negzero");

            // --- Exchange(ref double): old-value semantics ---
            double d1 = 2.25;
            double od1 = Interlocked.Exchange(ref d1, 4.5);
            Check(od1 == 2.25 && d1 == 4.5, "D1 double-exchange-basic");

            double d2 = -0.0;
            double od2 = Interlocked.Exchange(ref d2, 7.5);
            Check(IsNegZero(od2) && d2 == 7.5, "D2 double-exchange-old-negzero");

            double d3 = 1.5;
            double od3 = Interlocked.Exchange(ref d3, -0.0);
            Check(od3 == 1.5 && IsNegZero(d3), "D3 double-exchange-new-negzero");

            // --- CompareExchange(ref double): hit / miss ---
            double c1 = 5.5;
            double oc1 = Interlocked.CompareExchange(ref c1, 6.5, 5.5);
            Check(oc1 == 5.5 && c1 == 6.5, "C1 ce-hit");

            double c2 = c1;
            double oc2 = Interlocked.CompareExchange(ref c2, 9.5, 100.0);
            Check(oc2 == 6.5 && c2 == 6.5, "C2 ce-miss");

            // +0.0 location vs -0.0 comparand: numerically equal, bitwise
            // distinct -> MUST miss (store rejected, current returned).
            double c3 = 0.0;
            double oc3 = Interlocked.CompareExchange(ref c3, 1.0, -0.0);
            Check(oc3 == 0.0 && !IsNegZero(oc3) && c3 == 0.0 && !IsNegZero(c3),
                "C3 ce-zero-sign-miss");

            // -0.0 location vs -0.0 comparand: bitwise equal -> hit.
            double c4 = -0.0;
            double oc4 = Interlocked.CompareExchange(ref c4, 1.0, -0.0);
            Check(IsNegZero(oc4) && c4 == 1.0, "C4 ce-negzero-hit");

            // --- generic Exchange<long> ---
            long g1 = 1234567890123L;
            long og1 = Interlocked.Exchange<long>(ref g1, 987654321098L);
            Check(og1 == 1234567890123L && g1 == 987654321098L, "G1 generic-exchange-long");

            long g2 = -42L;
            long og2 = Interlocked.Exchange<long>(ref g2, 7L);
            Check(og2 == -42L && g2 == 7L, "G2 generic-exchange-long-negative");

            if (failures == 0)
            {
                Console.WriteLine("ALL-PASS");
            }
            else
            {
                Console.WriteLine("ALL-FAIL failures=" + failures);
            }
        }
    }
}
