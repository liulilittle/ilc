using System;

namespace E2eEnvProbe
{
    public static class Program
    {
        public static int Main()
        {
            int failures = 0;

            // 1. ProcessorCount > 0
            int cores = Environment.ProcessorCount;
            if (cores > 0) Console.WriteLine("PASS ProcessorCount=" + cores);
            else { Console.WriteLine("FAIL ProcessorCount=" + cores); failures++; }

            // 2. CurrentManagedThreadId > 0
            int tid = Environment.CurrentManagedThreadId;
            if (tid > 0) Console.WriteLine("PASS CurrentManagedThreadId=" + tid);
            else { Console.WriteLine("FAIL CurrentManagedThreadId=" + tid); failures++; }

            // 3. TickCount64 >= TickCount (read TickCount first, then TickCount64:
            //    both are ms since boot, monotonic non-decreasing, so the later
            //    64-bit read must be >= the earlier 32-bit read)
            int tc = Environment.TickCount;
            long tc64 = Environment.TickCount64;
            if (tc64 >= tc) Console.WriteLine("PASS TickCount64>=TickCount (" + tc64 + " >= " + tc + ")");
            else { Console.WriteLine("FAIL TickCount64>=TickCount (" + tc64 + " < " + tc + ")"); failures++; }

            // 4. NewLine nonempty
            string nl = Environment.NewLine;
            if (!string.IsNullOrEmpty(nl)) Console.WriteLine("PASS NewLine.Length=" + nl.Length);
            else { Console.WriteLine("FAIL NewLine empty"); failures++; }

            // 5. ExitCode set/get == 42, then reset to 0
            Environment.ExitCode = 42;
            int got = Environment.ExitCode;
            if (got == 42) Console.WriteLine("PASS ExitCode set/get=42");
            else { Console.WriteLine("FAIL ExitCode set 42 got " + got); failures++; }
            Environment.ExitCode = 0;
            int got2 = Environment.ExitCode;
            if (got2 == 0) Console.WriteLine("PASS ExitCode reset/get=0");
            else { Console.WriteLine("FAIL ExitCode reset 0 got " + got2); failures++; }

            if (failures == 0)
            {
                Console.WriteLine("ALL-PASS");
                return 0;
            }
            Console.WriteLine("ALL-FAIL failures=" + failures);
            return 1;
        }
    }
}
