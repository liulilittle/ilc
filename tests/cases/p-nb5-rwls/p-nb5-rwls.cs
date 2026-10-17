//------------------------------------------------------------------------------
// p-nb5-rwls.cs -- nb5 RWLS-LOCK difftest probe (net48 oracle == transpiled
// runtime byte-exact).
//
// Pins ReaderWriterLockSlim NoRecursion minimum state machine, single-threaded
// and scheduling-free:
//   * default policy = NoRecursion; initial held flags false.
//   * EnterReadLock/ExitReadLock -> IsReadLockHeld / CurrentReadCount.
//   * EnterWriteLock/ExitWriteLock -> IsWriteLockHeld.
//   * nested Enter in NoRecursion -> LockRecursionException.
//   * holding read then write (even TryEnterWriteLock(0)) -> LockRecursionException.
//   * upgradeable read: IsUpgradeableReadLockHeld true, CurrentReadCount 0.
//   * upgrade -> write: IsWriteLockHeld true, IsReadLockHeld false.
//   * Exit without entering -> SynchronizationLockException.
//   * TryEnterReadLock(0)/TryEnterWriteLock(0) success path.
// (SupportsRecursion is a documented LOUD subset: the runtime rejects the
// policy at construction with NotSupportedException, so it is NOT probed here
// -- net48 would diverge.)
//------------------------------------------------------------------------------
using System;
using System.Threading;

namespace Cases
{
    public static class PNb5Rwls
    {
        static string B(bool b) { return b ? "True" : "False"; }

        public static void Main()
        {
            var rw = new ReaderWriterLockSlim();
            Console.WriteLine("[rw.policy]=" + (rw.RecursionPolicy == LockRecursionPolicy.NoRecursion ? "NoRecursion" : "SupportsRecursion"));
            Console.WriteLine("[rw.init]=" + B(rw.IsReadLockHeld) + "|" + B(rw.IsWriteLockHeld) + "|" + B(rw.IsUpgradeableReadLockHeld));
            rw.EnterReadLock();
            Console.WriteLine("[rw.read]=" + B(rw.IsReadLockHeld) + "|" + rw.CurrentReadCount + "|" + rw.WaitingWriteCount + "|" + B(rw.IsWriteLockHeld));
            rw.ExitReadLock();
            Console.WriteLine("[rw.after]=" + B(rw.IsReadLockHeld) + "|" + rw.CurrentReadCount);

            var rw2 = new ReaderWriterLockSlim();
            rw2.EnterWriteLock();
            Console.WriteLine("[rw2.write]=" + B(rw2.IsWriteLockHeld) + "|" + rw2.CurrentReadCount);
            rw2.ExitWriteLock();
            Console.WriteLine("[rw2.after]=" + B(rw2.IsWriteLockHeld));

            var rw3 = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            rw3.EnterReadLock();
            string e3 = "none";
            try { rw3.EnterReadLock(); }
            catch (LockRecursionException) { e3 = "LockRecursionException"; }
            catch (SynchronizationLockException) { e3 = "SynchronizationLockException"; }
            Console.WriteLine("[rw3.nestedread]=" + e3);
            rw3.ExitReadLock();

            var rw4 = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            rw4.EnterWriteLock();
            string e4 = "none";
            try { rw4.EnterWriteLock(); }
            catch (LockRecursionException) { e4 = "LockRecursionException"; }
            catch (SynchronizationLockException) { e4 = "SynchronizationLockException"; }
            Console.WriteLine("[rw4.nestedwrite]=" + e4);
            rw4.ExitWriteLock();

            var rw6 = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            rw6.EnterReadLock();
            string e6 = "none";
            try { rw6.TryEnterWriteLock(0); }
            catch (LockRecursionException) { e6 = "LockRecursionException"; }
            catch (SynchronizationLockException) { e6 = "SynchronizationLockException"; }
            Console.WriteLine("[rw6.r2w.try.exc]=" + e6);
            rw6.ExitReadLock();

            var rw7 = new ReaderWriterLockSlim();
            rw7.EnterUpgradeableReadLock();
            Console.WriteLine("[rw7.upg]=" + B(rw7.IsUpgradeableReadLockHeld) + "|" + rw7.CurrentReadCount + "|" + rw7.WaitingUpgradeCount);
            rw7.ExitUpgradeableReadLock();

            var rw8 = new ReaderWriterLockSlim();
            Console.WriteLine("[rw8.wait]=" + rw8.WaitingReadCount + "|" + rw8.WaitingWriteCount + "|" + rw8.WaitingUpgradeCount);

            var rw9a = new ReaderWriterLockSlim();
            string e9a = "none";
            try { rw9a.ExitReadLock(); }
            catch (LockRecursionException) { e9a = "LockRecursionException"; }
            catch (SynchronizationLockException) { e9a = "SynchronizationLockException"; }
            Console.WriteLine("[rw9.exitread.noenter]=" + e9a);
            var rw9b = new ReaderWriterLockSlim();
            string e9b = "none";
            try { rw9b.ExitWriteLock(); }
            catch (LockRecursionException) { e9b = "LockRecursionException"; }
            catch (SynchronizationLockException) { e9b = "SynchronizationLockException"; }
            Console.WriteLine("[rw9.exitwrite.noenter]=" + e9b);

            var rwA = new ReaderWriterLockSlim();
            bool tr = rwA.TryEnterReadLock(0);
            Console.WriteLine("[rwA.tryread]=" + B(tr) + "|" + rwA.CurrentReadCount);
            rwA.ExitReadLock();
            var rwAw = new ReaderWriterLockSlim();
            bool tw = rwAw.TryEnterWriteLock(0);
            Console.WriteLine("[rwA.trywrite]=" + B(tw) + "|" + B(rwAw.IsWriteLockHeld));
            rwAw.ExitWriteLock();

            var rwB = new ReaderWriterLockSlim();
            rwB.EnterUpgradeableReadLock();
            rwB.EnterWriteLock();
            Console.WriteLine("[rwB.upg2write]=" + B(rwB.IsWriteLockHeld) + "|" + B(rwB.IsReadLockHeld) + "|" + B(rwB.IsUpgradeableReadLockHeld) + "|" + rwB.CurrentReadCount);
            rwB.ExitWriteLock();
            rwB.ExitUpgradeableReadLock();
            Console.WriteLine("[rwB.after]=" + B(rwB.IsWriteLockHeld) + "|" + B(rwB.IsUpgradeableReadLockHeld));

            Console.WriteLine("[done]=1");
        }
    }
}
