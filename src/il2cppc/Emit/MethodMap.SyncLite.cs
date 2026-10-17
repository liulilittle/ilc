namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.SyncLite -- System.Threading lightweight synchronization icall
// mappings (il2cpp_synclite.h, implementation sync_lite.cpp).
// Owners: System.Threading.SemaphoreSlim / CountdownEvent / SpinLock /
// Barrier.
//
// OBJECT MODEL: SemaphoreSlim / CountdownEvent / Barrier are managed objects
// (Il2CppSyncObject -- header + ONE native state pointer) allocated through
// the GLOBAL class descriptors (System_Threading_SemaphoreSlim_class /
// CountdownEvent_class / Barrier_class) -- the transpiler's newobj fallback
// emits il2cpp_object_new(&System_Threading_*_class) (TypeCppName sanitizes
// the full names, no explicit BclAllocExpr entry -- the System.Version
// contract). The receiver passes through the "" cast; int32 args use "i".
//
// SpinLock is a VALUETYPE: the CodeGenerator declares the `SpinLock` local as
// an Il2CppObject* REF CARRIER ("v:System.Threading.SpinLock" special case,
// the AsyncTaskMethodBuilder model). The .ctor icall writes the allocated
// SpinLock carrier object INTO the slot (the receiver is the local's ldloca
// ADDRESS -- passed through ""), and Enter/Exit/IsHeld deref:r the slot to
// the carrier object (the dict-enumerator address-receiver precedent). The
// `ref bool lockTaken` enters by address ("", the Monitor 2-arg flag
// precedent); the runtime writes *lockTaken.
//
// Wired surface (nb4 SYNC-LITE, deterministic protocol gates -- no
// wall-clock): SemaphoreSlim(int[,int]) ctor / Wait / Wait(int) /
// Release()/Release(int) / CurrentCount; CountdownEvent(int) ctor / Signal /
// CurrentCount / IsSet; SpinLock(bool) ctor / Enter(ref bool) / Exit /
// IsHeld; Barrier(int) ctor / SignalAndWait / CurrentPhaseNumber /
// ParticipantCount.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapSyncLite(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // SemaphoreSlim(int initialCount) -- maximumCount = int.MaxValue
                // (2147483647, injected as a literal -- the runtime icall takes
                // (self, initialCount, maximumCount)).
                if (owner == "System.Threading.SemaphoreSlim" && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_synclite_semslim_ctor", 1, false, false, true,
                        new[] { "", "i" }, appendLiterals: new[] { "2147483647" });
                // SemaphoreSlim(int initialCount, int maximumCount).
                if (owner == "System.Threading.SemaphoreSlim" && ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_synclite_semslim_ctor", 2, false, false, true,
                        new[] { "", "i", "i" });
                // CountdownEvent(int initialCount).
                if (owner == "System.Threading.CountdownEvent" && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_synclite_countdownevent_ctor", 1, false, false, true,
                        new[] { "", "i" });
                // Barrier(int participantCount).
                if (owner == "System.Threading.Barrier" && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_synclite_barrier_ctor", 1, false, false, true,
                        new[] { "", "i" });
                // SpinLock(bool enableThreadOwnerTracking) -- the VALUETYPE
                // ctor: the receiver is the local slot's ADDRESS (Il2CppObject**).
                // The emitter passes it through "" (the receiver is a LocAddrExpr --
                // non-runtime value-type ctor addressing); the runtime writes the
                // allocated carrier into *slot.
                if (owner == "System.Threading.SpinLock" && ps.Length == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_synclite_spinlock_ctor", 1, false, false, true,
                        new[] { "", "i" });
                return null;
            case "get_CurrentCount":
                if (owner == "System.Threading.SemaphoreSlim" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_semslim_get_current_count", 0, true, false, true);
                if (owner == "System.Threading.CountdownEvent" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_countdownevent_get_current_count", 0, true, false, true);
                return null;
            case "Wait":
                // void SemaphoreSlim.Wait() (infinite).
                if (owner == "System.Threading.SemaphoreSlim" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_semslim_wait", 0, false, false, true);
                // bool SemaphoreSlim.Wait(int) -- 0 = non-blocking probe.
                if (owner == "System.Threading.SemaphoreSlim" && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_synclite_semslim_wait_ms", 1, true, false, true);
                return null;
            case "Release":
                // int SemaphoreSlim.Release().
                if (owner == "System.Threading.SemaphoreSlim" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_semslim_release", 0, true, false, true);
                // int SemaphoreSlim.Release(int releaseCount).
                if (owner == "System.Threading.SemaphoreSlim" && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_synclite_semslim_release_n", 1, true, false, true);
                return null;
            case "Signal":
                // bool CountdownEvent.Signal().
                if (owner == "System.Threading.CountdownEvent" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_countdownevent_signal", 0, true, false, true);
                return null;
            case "get_IsSet":
                if (owner == "System.Threading.CountdownEvent" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_countdownevent_get_is_set", 0, true, false, true);
                return null;
            case "Enter":
                // SpinLock.Enter(ref bool lockTaken): the flag's address passes
                // through "" (the Monitor 2-arg flag precedent).
                if (owner == "System.Threading.SpinLock" && ps.Length == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_synclite_spinlock_enter", 1, false, false, true,
                        new[] { "deref:r", "" });
                return null;
            case "Exit":
                if (owner == "System.Threading.SpinLock" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_spinlock_exit", 0, false, false, true,
                        new[] { "deref:r" });
                return null;
            case "get_IsHeld":
                if (owner == "System.Threading.SpinLock" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_spinlock_is_held", 0, true, false, true,
                        new[] { "deref:r" });
                return null;
            case "SignalAndWait":
                if (owner == "System.Threading.Barrier" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_barrier_signal_and_wait", 0, true, false, true);
                return null;
            case "get_CurrentPhaseNumber":
                if (owner == "System.Threading.Barrier" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_barrier_get_current_phase_number", 0, true, false, true);
                return null;
            case "get_ParticipantCount":
                if (owner == "System.Threading.Barrier" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_synclite_barrier_get_participant_count", 0, true, false, true);
                return null;
            default: return null;
        }
    }
}