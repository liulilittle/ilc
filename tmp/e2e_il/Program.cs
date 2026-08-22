// e2e_il -- System.Threading.Interlocked end-to-end (net9 refs -> il2cppc -> MinGW native)
//
// Value semantics under test (.NET contract):
//   Add / Increment / Decrement : return the NEW value stored at the location
//   Exchange                    : return the OLD value, store the new one
//   CompareExchange             : return the OLD value; store only when old == comparand
//                                 (hit: returns old + writes; miss: returns CURRENT, unchanged)
//
// Every step asserts BOTH the returned value AND the final location state.
// Output is fully deterministic: "PASS <name>" / "FAIL <name>" per check,
// then a final "ALL-PASS" or "ALL-FAIL failures=N".
using System;
using System.Threading;

namespace E2eIlProbe
{
    public static class Program
    {
        private static int _failures = 0;

        private static void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
            if (!ok) _failures++;
        }

        public static void Main()
        {
            // ---------- Add: int ----------
            int ai = 10;
            int r1 = Interlocked.Add(ref ai, 5);          // NEW sum
            Check("add_int_new_value", r1 == 15 && ai == 15);
            int r2 = Interlocked.Add(ref ai, -20);        // negative addend
            Check("add_int_negative", r2 == -5 && ai == -5);

            // ---------- Add: long ----------
            long al = 100000000000L;
            long lr1 = Interlocked.Add(ref al, 1);
            Check("add_long_new_value", lr1 == 100000000001L && al == 100000000001L);
            long lr2 = Interlocked.Add(ref al, -100000000001L);
            Check("add_long_negative", lr2 == 0L && al == 0L);

            // ---------- Exchange: int ----------
            int xi = 7;
            int xo = Interlocked.Exchange(ref xi, 99);
            Check("exchange_int_old_value", xo == 7 && xi == 99);

            // ---------- Exchange: long ----------
            long xl = 123456789012345L;
            long xlo = Interlocked.Exchange(ref xl, -987654321098L);
            Check("exchange_long_old_value", xlo == 123456789012345L && xl == -987654321098L);

            // ---------- Exchange: float ----------
            float xf = 1.5f;
            float xfo = Interlocked.Exchange(ref xf, 2.5f);
            Check("exchange_float_old_value", xfo == 1.5f && xf == 2.5f);

            // ---------- Exchange: double ----------
            double xd = 0.75;
            double xdo = Interlocked.Exchange(ref xd, 2.5);
            Check("exchange_double_old_value", xdo == 0.75 && xd == 2.5);

            // ---------- Exchange: object ----------
            object oslot = new object();
            object witnessA = oslot;
            object nextB = new object();
            object oold1 = Interlocked.Exchange(ref oslot, nextB);
            Check("exchange_object_old_value",
                ReferenceEquals(oold1, witnessA) && ReferenceEquals(oslot, nextB));
            object nextC = new object();
            object oold2 = Interlocked.Exchange(ref oslot, nextC);
            Check("exchange_object_twice",
                ReferenceEquals(oold2, nextB) && ReferenceEquals(oslot, nextC));

            // ---------- CompareExchange: int hit + miss ----------
            int ci = 42;
            int c1 = Interlocked.CompareExchange(ref ci, 100, 42);   // hit: writes
            Check("cas_int_hit", c1 == 42 && ci == 100);
            int c2 = Interlocked.CompareExchange(ref ci, 200, 42);   // miss: unchanged
            Check("cas_int_miss", c2 == 100 && ci == 100);

            // ---------- CompareExchange: long hit + miss ----------
            long cl = 5000000000L;
            long cl1 = Interlocked.CompareExchange(ref cl, 6000000000L, 5000000000L);
            Check("cas_long_hit", cl1 == 5000000000L && cl == 6000000000L);
            long cl2 = Interlocked.CompareExchange(ref cl, 7000000000L, 5000000000L);
            Check("cas_long_miss", cl2 == 6000000000L && cl == 6000000000L);

            // ---------- CompareExchange: double hit + miss ----------
            double cd = 0.5;
            double cd1 = Interlocked.CompareExchange(ref cd, 1.5, 0.5);
            Check("cas_double_hit", cd1 == 0.5 && cd == 1.5);
            double cd2 = Interlocked.CompareExchange(ref cd, 9.5, 0.5);
            Check("cas_double_miss", cd2 == 1.5 && cd == 1.5);

            // ---------- generic Exchange<int> ----------
            int gi = 11;
            int gio = Interlocked.Exchange<int>(ref gi, 22);
            Check("exchange_generic_int", gio == 11 && gi == 22);

            // ---------- Increment/Decrement chains: int ----------
            int ic = 0;
            int s1 = Interlocked.Increment(ref ic);
            int s2 = Interlocked.Increment(ref ic);
            int s3 = Interlocked.Increment(ref ic);
            Check("increment_chain_int", s1 == 1 && s2 == 2 && s3 == 3 && ic == 3);
            int t1 = Interlocked.Decrement(ref ic);
            int t2 = Interlocked.Decrement(ref ic);
            Check("decrement_chain_int", t1 == 2 && t2 == 1 && ic == 1);

            // ---------- Increment/Decrement chains: long ----------
            long lc = 0;
            long u1 = Interlocked.Increment(ref lc);
            long u2 = Interlocked.Increment(ref lc);
            long u3 = Interlocked.Increment(ref lc);
            Check("increment_chain_long", u1 == 1L && u2 == 2L && u3 == 3L && lc == 3L);
            long v1 = Interlocked.Decrement(ref lc);
            long v2 = Interlocked.Decrement(ref lc);
            Check("decrement_chain_long", v1 == 2L && v2 == 1L && lc == 1L);

            // ---------- chained loop: returns must be 6..15 then 14..5 ----------
            int lp = 5;
            int sum = 0;
            for (int k = 0; k < 10; k++) sum += Interlocked.Increment(ref lp);
            Check("increment_loop_chain", sum == 105 && lp == 15);
            for (int k = 0; k < 10; k++) sum += Interlocked.Decrement(ref lp);
            Check("decrement_loop_chain", sum == 200 && lp == 5);

            Console.WriteLine(_failures == 0 ? "ALL-PASS" : "ALL-FAIL failures=" + _failures);
        }
    }
}
