// E2E ilgen probe: generic-form Interlocked atomics
// Pipeline: net9 refs -> il2cppc transpile -> MinGW native
using System;
using System.Threading;

namespace E2eIlGenProbe
{
    public static class Program
    {
        private static int _failures;

        private static void Check(bool ok, string label)
        {
            if (ok) Console.WriteLine("PASS " + label);
            else { Console.WriteLine("FAIL " + label); _failures++; }
        }

        public static int Main()
        {
            // 1) Generic-form Exchange<int>: returns OLD, stores NEW
            int intVal = 41;
            int oldInt = Interlocked.Exchange<int>(ref intVal, 5);
            Check(oldInt == 41, "Exchange<int> old=" + oldInt);
            Check(intVal == 5, "Exchange<int> new=" + intVal);

            // 2) Generic-form CompareExchange<long>: hit returns OLD comparand + stores
            long longVal = 123456789012345L;
            long oldLongHit = Interlocked.CompareExchange<long>(ref longVal, 99L, 123456789012345L);
            Check(oldLongHit == 123456789012345L, "CompareExchange<long> hit old=" + oldLongHit);
            Check(longVal == 99L, "CompareExchange<long> hit new=" + longVal);

            // 2b) Generic-form CompareExchange<long>: miss returns current, stores nothing
            long oldLongMiss = Interlocked.CompareExchange<long>(ref longVal, 777L, 42L);
            Check(oldLongMiss == 99L, "CompareExchange<long> miss old=" + oldLongMiss);
            Check(longVal == 99L, "CompareExchange<long> miss unchanged=" + longVal);

            // 3) Generic-form Exchange<object>: raw reference swap
            object sentinelA = new object();
            object sentinelB = new object();
            object slot = sentinelA;
            object oldObj = Interlocked.Exchange<object>(ref slot, sentinelB);
            Check(ReferenceEquals(oldObj, sentinelA), "Exchange<object> old==sentinelA");
            Check(ReferenceEquals(slot, sentinelB), "Exchange<object> slot==sentinelB");
            Check(!ReferenceEquals(oldObj, sentinelB), "Exchange<object> old!=sentinelB");

            if (_failures == 0)
            {
                Console.WriteLine("ALL-PASS");
                return 0;
            }
            Console.WriteLine("ALL-FAIL failures=" + _failures);
            return 1;
        }
    }
}
