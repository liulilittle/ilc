//------------------------------------------------------------------------------
// p-nb8-rwlsrec.cs -- nb8 RWLS SupportsRecursion difftest probe (net48 oracle ==
// transpiled runtime byte-exact).
//
// Pins the LockRecursionPolicy.SupportsRecursion recursion semantics that nb5
// left as a documented LOUD subset:
//   * RecursionPolicy observable == SupportsRecursion.
//   * recursive EnterReadLock: enters, CurrentReadCount stays 1 (unique reader),
//     each ExitReadLock pops one depth; one extra Exit -> SynchronizationLockException.
//   * recursive EnterWriteLock: enters, symmetric exits, extra Exit throws.
//   * read -> write is a cross-type violation: LockRecursionException even in
//     SupportsRecursion (the upgradeable read lock is the valid path).
//   * recursive EnterUpgradeableReadLock.
//   * upgradeable read -> write works; recursive write from the upgrade.
//
// Single-threaded and scheduling-free (deterministic boolean / integer gates).
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class PNb8Rwlsrec
    {
        static string B(bool b) { return b ? "True" : "False"; }

        static string Try(Action a)
        {
            try { a(); return "ok"; }
            catch (LockRecursionException) { return "LockRecursionException"; }
            catch (SynchronizationLockException) { return "SynchronizationLockException"; }
            catch (Exception e) { return e.GetType().Name; }
        }

        public static void Main()
        {
            var r0 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            Console.WriteLine("[sup.policy]=" + (r0.RecursionPolicy == LockRecursionPolicy.SupportsRecursion ? "SupportsRecursion" : "NoRecursion"));
            Console.WriteLine("[sup.init]=" + B(r0.IsReadLockHeld) + "|" + B(r0.IsWriteLockHeld) + "|" + B(r0.IsUpgradeableReadLockHeld) + "|" + r0.CurrentReadCount);

            // --- recursive read ------------------------------------------------
            var r1 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            string e1 = Try(() => { r1.EnterReadLock(); r1.EnterReadLock(); });
            Console.WriteLine("[sup.nestedread]=" + e1 + "|" + B(r1.IsReadLockHeld) + "|" + r1.CurrentReadCount);
            r1.ExitReadLock();
            Console.WriteLine("[sup.nestedread.d1]=" + B(r1.IsReadLockHeld) + "|" + r1.CurrentReadCount);
            r1.ExitReadLock();
            Console.WriteLine("[sup.nestedread.d2]=" + B(r1.IsReadLockHeld) + "|" + r1.CurrentReadCount);
            Console.WriteLine("[sup.nestedread.extra]=" + Try(() => r1.ExitReadLock()));

            // --- recursive write -----------------------------------------------
            var r2 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            string e2 = Try(() => { r2.EnterWriteLock(); r2.EnterWriteLock(); });
            Console.WriteLine("[sup.nestedwrite]=" + e2 + "|" + B(r2.IsWriteLockHeld));
            r2.ExitWriteLock();
            Console.WriteLine("[sup.nestedwrite.d1]=" + B(r2.IsWriteLockHeld));
            r2.ExitWriteLock();
            Console.WriteLine("[sup.nestedwrite.d2]=" + B(r2.IsWriteLockHeld));
            Console.WriteLine("[sup.nestedwrite.extra]=" + Try(() => r2.ExitWriteLock()));

            // --- read -> write (cross-type) ------------------------------------
            var r3 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            r3.EnterReadLock();
            Console.WriteLine("[sup.read.write]=" + Try(() => r3.EnterWriteLock()));
            Console.WriteLine("[sup.read.write.try]=" + Try(() => r3.TryEnterWriteLock(0)));
            Console.WriteLine("[sup.read.write.state]=" + B(r3.IsReadLockHeld) + "|" + B(r3.IsWriteLockHeld) + "|" + r3.CurrentReadCount);
            r3.ExitReadLock();

            // --- recursive upgradeable read ------------------------------------
            var r4 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            string e4 = Try(() => { r4.EnterUpgradeableReadLock(); r4.EnterUpgradeableReadLock(); });
            Console.WriteLine("[sup.nestedupg]=" + e4 + "|" + B(r4.IsUpgradeableReadLockHeld) + "|" + r4.CurrentReadCount);
            r4.ExitUpgradeableReadLock();
            Console.WriteLine("[sup.nestedupg.d1]=" + B(r4.IsUpgradeableReadLockHeld));
            r4.ExitUpgradeableReadLock();
            Console.WriteLine("[sup.nestedupg.d2]=" + B(r4.IsUpgradeableReadLockHeld));
            Console.WriteLine("[sup.nestedupg.extra]=" + Try(() => r4.ExitUpgradeableReadLock()));

            // --- upgradeable read -> write (same thread upgrade) ---------------
            var r5 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            r5.EnterUpgradeableReadLock();
            r5.EnterWriteLock();
            Console.WriteLine("[sup.upg.write]=" + B(r5.IsWriteLockHeld) + "|" + B(r5.IsReadLockHeld) + "|" + B(r5.IsUpgradeableReadLockHeld) + "|" + r5.CurrentReadCount);
            r5.ExitWriteLock();
            r5.ExitUpgradeableReadLock();
            Console.WriteLine("[sup.upg.write.after]=" + B(r5.IsWriteLockHeld) + "|" + B(r5.IsUpgradeableReadLockHeld));

            // --- upgradeable read -> read (recursive read under upgrade) --------
            var r6 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            r6.EnterUpgradeableReadLock();
            string e6 = Try(() => r6.EnterReadLock());
            Console.WriteLine("[sup.upg.read]=" + e6 + "|" + B(r6.IsReadLockHeld) + "|" + B(r6.IsUpgradeableReadLockHeld) + "|" + r6.CurrentReadCount);
            if (e6 == "ok") r6.ExitReadLock();
            r6.ExitUpgradeableReadLock();

            // --- write -> read (recursive read under write) --------------------
            var r7 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            r7.EnterWriteLock();
            string e7 = Try(() => r7.EnterReadLock());
            Console.WriteLine("[sup.write.read]=" + e7 + "|" + B(r7.IsWriteLockHeld) + "|" + B(r7.IsReadLockHeld) + "|" + r7.CurrentReadCount);
            if (e7 == "ok") r7.ExitReadLock();
            r7.ExitWriteLock();
            Console.WriteLine("[sup.write.read.after]=" + B(r7.IsWriteLockHeld) + "|" + B(r7.IsReadLockHeld));

            // --- write -> upgradeable read (allowed under SupportsRecursion) --
            var r9 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            r9.EnterWriteLock();
            string e9 = Try(() => r9.EnterUpgradeableReadLock());
            Console.WriteLine("[sup.write.upg]=" + e9 + "|" + B(r9.IsWriteLockHeld) + "|" + B(r9.IsUpgradeableReadLockHeld) + "|" + r9.CurrentReadCount);
            if (e9 == "ok") r9.ExitUpgradeableReadLock();
            Console.WriteLine("[sup.write.upg.after1]=" + B(r9.IsWriteLockHeld) + "|" + B(r9.IsUpgradeableReadLockHeld));
            r9.ExitWriteLock();
            Console.WriteLine("[sup.write.upg.after2]=" + B(r9.IsWriteLockHeld) + "|" + B(r9.IsUpgradeableReadLockHeld));

            // --- TryEnter success paths under SupportsRecursion ---------------
            var r8 = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            bool t1 = r8.TryEnterReadLock(0);
            bool t2 = r8.TryEnterReadLock(0);
            Console.WriteLine("[sup.tryread]=" + B(t1) + "|" + B(t2) + "|" + r8.CurrentReadCount);
            r8.ExitReadLock();
            r8.ExitReadLock();

            Console.WriteLine("[done]=1");
        }
    }
}
