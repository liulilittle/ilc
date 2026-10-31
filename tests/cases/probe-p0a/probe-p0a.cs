//------------------------------------------------------------------------------
// probe-p0a.cs -- mscorlib P0 batch A: RuntimeHelpers JIT/stack/CER hints,
// Interlocked.SpeculationBarrier, Thread critical-region / affinity /
// COM-eager-cleanup. net48 oracle == transpiled runtime, byte exact.
//
// Measured net48 behavior (csc 4.0.30319 x64, see NOTES.md):
//   * EnsureSufficientExecutionStack: normal depth no-throw; deep recursion
//     throws System.InsufficientExecutionStackException (never a raw SOE).
//   * ProbeForSufficientStack: normal depth no-throw; deep recursion is a raw
//     StackOverflowException (a stack-touch hint, not a managed check) -- the
//     probe therefore only exercises the normal-depth contract.
//   * ExecuteCodeWithGuaranteedCleanup: cleanup runs on BOTH paths; the bool
//     argument is false on normal completion and true when the try delegate
//     threw; the original exception then keeps propagating. Null code /
//     backoutCode -> ArgumentNullException.
//   * PrepareDelegate / PrepareContractedDelegate: no-throw for null and for
//     a valid delegate (pure JIT-prepare hints).
//   * InitializeArray: the folded `new T[] { ... }` forms copy the RVA blob;
//     a DEFAULT RuntimeFieldHandle -> ArgumentNullException.
//   * SpeculationBarrier / Thread.Begin-EndCriticalRegion /
//     Begin-EndThreadAffinity / DisableComObjectEagerCleanup: no-throw.
//
// Determinism: no depth numbers, no addresses, no timings; every record is a
// fixed bool/int/string derived from fixed inputs.
//------------------------------------------------------------------------------
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Cases
{
    public static class ProbeP0A
    {
        private static int _cleanupNormal;
        private static int _cleanupEx;
        private static bool _cleanupNormalFlag;
        private static bool _cleanupExFlag;
        private static int _published;
        private static int _payload;
        private static bool _readerSawPayload;

        private static void TryOk(object userData)
        {
            Console.WriteLine("[cer.try.ud]=" + (userData == null ? "null" : userData.ToString()));
        }

        private static void CleanupNormal(object userData, bool exceptionThrown)
        {
            _cleanupNormal++;
            _cleanupNormalFlag = exceptionThrown;
            Console.WriteLine("[cer.cleanup.normal.ex]=" + (exceptionThrown ? "True" : "False"));
        }

        private static void CleanupThrow(object userData, bool exceptionThrown)
        {
            _cleanupEx++;
            _cleanupExFlag = exceptionThrown;
            Console.WriteLine("[cer.cleanup.ex.ex]=" + (exceptionThrown ? "True" : "False"));
        }

        private static void TryThrow(object userData)
        {
            throw new InvalidOperationException("boom");
        }

        private static bool DeepEnsure()
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
                return DeepEnsure();
            }
            catch (InsufficientExecutionStackException)
            {
                return true;
            }
        }

        private static void SpecWriter()
        {
            _payload = 12345;
            Interlocked.SpeculationBarrier();
            Volatile.Write(ref _published, 1);
        }

        private static void SpecReader()
        {
            for (int i = 0; i < 100000000 && Volatile.Read(ref _published) == 0; i++)
            {
            }
            _readerSawPayload = _payload == 12345;
        }

        private static void ThreadCalls()
        {
            Thread.BeginCriticalRegion();
            Thread.EndCriticalRegion();
            Thread.BeginThreadAffinity();
            Thread.EndThreadAffinity();
            Thread.CurrentThread.DisableComObjectEagerCleanup();
        }

        public static void Main()
        {
            // 1. EnsureSufficientExecutionStack: normal depth.
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
                Console.WriteLine("[ens.normal]=no-throw");
            }
            catch (Exception e)
            {
                Console.WriteLine("[ens.normal]=" + e.GetType().Name);
            }

            // 2. EnsureSufficientExecutionStack: deep recursion -> the managed
            //    insufficient-stack exception (not a process-killing SOE).
            Console.WriteLine("[ens.deep]=" + (DeepEnsure() ? "InsufficientExecutionStackException" : "none"));

            // 3. ProbeForSufficientStack: normal depth.
            try
            {
                RuntimeHelpers.ProbeForSufficientStack();
                Console.WriteLine("[pfs.normal]=no-throw");
            }
            catch (Exception e)
            {
                Console.WriteLine("[pfs.normal]=" + e.GetType().Name);
            }

            // 4. ExecuteCodeWithGuaranteedCleanup: normal path (cleanup runs,
            //    bool false).
            RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup(TryOk, CleanupNormal, "ud1");
            Console.WriteLine("[cer.normal.count]=" + _cleanupNormal);
            Console.WriteLine("[cer.normal.flag]=" + (_cleanupNormalFlag ? "True" : "False"));

            // 5. ... exceptional path (cleanup runs with true, exception
            //    continues propagating).
            try
            {
                RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup(TryThrow, CleanupThrow, "ud2");
                Console.WriteLine("[cer.ex.propagated]=false");
            }
            catch (Exception e)
            {
                Console.WriteLine("[cer.ex.propagated]=" + e.GetType().Name);
                Console.WriteLine("[cer.ex.count]=" + _cleanupEx);
                Console.WriteLine("[cer.ex.flag]=" + (_cleanupExFlag ? "True" : "False"));
            }

            // 6. null delegate arguments.
            try
            {
                RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup(null, CleanupNormal, null);
            }
            catch (Exception e)
            {
                Console.WriteLine("[cer.null.try]=" + e.GetType().Name);
            }
            try
            {
                RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup(TryOk, null, null);
            }
            catch (Exception e)
            {
                Console.WriteLine("[cer.null.cleanup]=" + e.GetType().Name);
            }

            // 7. PrepareDelegate / PrepareContractedDelegate: no-throw.
            try
            {
                RuntimeHelpers.PrepareDelegate(new Action<object>(TryOk));
                RuntimeHelpers.PrepareDelegate(null);
                RuntimeHelpers.PrepareContractedDelegate(new Action<object>(TryOk));
                RuntimeHelpers.PrepareContractedDelegate(null);
                Console.WriteLine("[prep]=no-throw");
            }
            catch (Exception e)
            {
                Console.WriteLine("[prep]=" + e.GetType().Name);
            }

            // 8. InitializeArray: the compiler-generated (folded) initializers.
            byte[] bytes = new byte[] { 1, 2, 3, 4 };
            int[] ints = new int[] { 10, 20, 30, 40 };
            Console.WriteLine("[init.bytes]=" + bytes[0] + bytes[1] + bytes[2] + bytes[3]);
            Console.WriteLine("[init.ints]=" + ints[0] + "," + ints[1] + "," + ints[2] + "," + ints[3]);

            // 9. InitializeArray: the un-folded call shape (default handle).
            try
            {
                RuntimeHelpers.InitializeArray(new byte[4], default(RuntimeFieldHandle));
                Console.WriteLine("[init.default]=no-throw");
            }
            catch (Exception e)
            {
                Console.WriteLine("[init.default]=" + e.GetType().Name);
            }

            // 10. SpeculationBarrier publish/subscribe.
            Thread writer = new Thread(SpecWriter);
            Thread reader = new Thread(SpecReader);
            writer.Start();
            reader.Start();
            writer.Join();
            reader.Join();
            Console.WriteLine("[spec.pubsub]=" + (_readerSawPayload ? "True" : "False"));

            // 11. Thread critical-region / affinity / COM cleanup on the main
            //     thread.
            try
            {
                ThreadCalls();
                Console.WriteLine("[thread.main]=no-throw");
            }
            catch (Exception e)
            {
                Console.WriteLine("[thread.main]=" + e.GetType().Name);
            }

            // 12. ... and on a worker thread.
            Thread worker = new Thread(() =>
            {
                try
                {
                    ThreadCalls();
                    Console.WriteLine("[thread.worker]=no-throw");
                }
                catch (Exception e)
                {
                    Console.WriteLine("[thread.worker]=" + e.GetType().Name);
                }
            });
            worker.Start();
            worker.Join();
        }
    }
}
