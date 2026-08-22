using System;
using System.Threading;

namespace E2eThrProbe
{
    public static class Program
    {
        public static bool WorkerFlag;

        private static void Worker()
        {
            WorkerFlag = true;
        }

        public static int Main()
        {
            int failures = 0;

            // 1. Thread.Sleep(0) returns without error
            try
            {
                Thread.Sleep(0);
                Console.WriteLine("PASS Thread.Sleep(0)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL Thread.Sleep(0): " + ex.GetType().Name);
                failures++;
            }

            // 2. Thread.SpinWait(100) returns without error
            try
            {
                Thread.SpinWait(100);
                Console.WriteLine("PASS Thread.SpinWait(100)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL Thread.SpinWait(100): " + ex.GetType().Name);
                failures++;
            }

            // 3. CurrentThread.ManagedThreadId > 0
            int tid = Thread.CurrentThread.ManagedThreadId;
            if (tid > 0) Console.WriteLine("PASS ManagedThreadId=" + tid);
            else { Console.WriteLine("FAIL ManagedThreadId=" + tid); failures++; }

            // 4. Start a thread that sets a static flag, Join, assert flag set
            WorkerFlag = false;
            Thread t = new Thread(new ThreadStart(Worker));
            t.Start();
            t.Join();
            if (WorkerFlag) Console.WriteLine("PASS worker thread set flag after Join");
            else { Console.WriteLine("FAIL worker flag not set after Join"); failures++; }

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
