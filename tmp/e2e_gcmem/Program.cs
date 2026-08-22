using System;

namespace E2eGcMemProbe
{
    public static class Program
    {
        public static int Main()
        {
            int failures = 0;

            long total = GC.GetTotalMemory(false);
            if (total > 0)
                Console.WriteLine("PASS GetTotalMemory(false)=" + total);
            else { Console.WriteLine("FAIL GetTotalMemory(false)=" + total); failures++; }

            try { GC.Collect(); Console.WriteLine("PASS Collect() completed"); }
            catch (Exception ex) { Console.WriteLine("FAIL Collect(): " + ex.Message); failures++; }

            int maxGen = GC.MaxGeneration;
            if (maxGen >= 0) Console.WriteLine("PASS MaxGeneration=" + maxGen);
            else { Console.WriteLine("FAIL MaxGeneration=" + maxGen); failures++; }

            object obj = new object();
            int gen = GC.GetGeneration(obj);
            if (gen >= 0) Console.WriteLine("PASS GetGeneration(new object())=" + gen);
            else { Console.WriteLine("FAIL GetGeneration(new object())=" + gen); failures++; }

            try
            {
                const int bigLen = 16 * 1024 * 1024;
                int[] big = new int[bigLen];
                big[0] = 12345;
                big[bigLen - 1] = 54321;
                if (big != null && big.Length == bigLen && big[0] == 12345 && big[bigLen - 1] == 54321)
                    Console.WriteLine("PASS large array alloc len=" + bigLen);
                else { Console.WriteLine("FAIL large array alloc integrity"); failures++; }
                big = null;
                GC.Collect();
                Console.WriteLine("PASS Collect() after large alloc");
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL large alloc/collect: " + ex.Message);
                failures++;
            }

            GC.KeepAlive(obj);

            if (failures == 0) { Console.WriteLine("ALL-PASS"); return 0; }
            Console.WriteLine("ALL-FAIL failures=" + failures);
            return 1;
        }
    }
}
