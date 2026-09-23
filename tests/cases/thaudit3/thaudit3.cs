// thaudit3.cs -- G6 thread-audit probe #3: Semaphore(0, 2).Release(int.MaxValue).
// net48 rejects an over-maximum release count (ArgumentOutOfRangeException);
// the runtime check (waithandle.cpp: previous + releaseCount > maxCount) is a
// signed int32 addition, so overflow can slip through and corrupt the count
// (negative -> later waits never observe count > 0).
//
// WaitOne(0) is the required NON-BLOCKING probe after the release -- a
// blocking WaitOne() could deadlock forever on a corrupted count and is
// never used here. Typed catches distinguish the exception types without
// reading exception members (the M-model catch-store shape).
using System;
using System.Threading;

namespace Cases { public static class ThreadAudit3 {
    public static void Main() {
        Semaphore sem = new Semaphore(0, 2);
        bool aore = false;
        bool sfe = false;
        bool other = false;
        try {
            int prev = sem.Release(int.MaxValue);
            Console.WriteLine("release_prev=" + prev);
        } catch (ArgumentOutOfRangeException) { aore = true; }
        catch (SemaphoreFullException) { sfe = true; }
        catch (Exception) { other = true; }
        Console.WriteLine("aore=" + aore);
        Console.WriteLine("sfe=" + sfe);
        Console.WriteLine("other=" + other);
        bool wait0 = sem.WaitOne(0);
        Console.WriteLine("wait0=" + wait0);
    }
}}
