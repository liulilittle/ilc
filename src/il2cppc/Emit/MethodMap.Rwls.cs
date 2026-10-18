namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Rwls -- System.Threading.ReaderWriterLockSlim icall mappings
// (il2cpp_rwls.h, implementation rwls.cpp).
// Owners: System.Threading.ReaderWriterLockSlim.
//
// OBJECT MODEL: ReaderWriterLockSlim is a managed object (Il2CppRwlsObject --
// header + ONE native state pointer) allocated through the GLOBAL class
// descriptor System_Threading_ReaderWriterLockSlim_class -- the transpiler's
// newobj fallback emits
// il2cpp_object_new(&System_Threading_ReaderWriterLockSlim_class) (the
// SemaphoreSlim contract, MethodMap.SyncLite.cs), so NO BclAllocExpr entry is
// required. The receiver passes through the "" cast; int32 args use "i".
//
// Wired surface (nb5 RWLS-LOCK; nb8 added the LockRecursionPolicy.
// SupportsRecursion recursion semantics in rwls.cpp):
// ctor()/ctor(LockRecursionPolicy) / Enter+Exit{Read,Write,UpgradeableRead}Lock
// / TryEnter{Read,Write,UpgradeableRead}Lock(int) / IsReadLockHeld /
// IsWriteLockHeld / IsUpgradeableReadLockHeld / CurrentReadCount /
// Waiting{Read,Write,Upgrade}Count / RecursionPolicy.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapRwls(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // ReaderWriterLockSlim() -- policy NoRecursion.
                if (count == 0)
                    return I("il2cpp::runtime::icall_rwls_ctor_default", 0, false, false, true);
                // ReaderWriterLockSlim(LockRecursionPolicy): the enum is a
                // VALUETYPE (0x11 + coded index) in the real Roslyn signature;
                // the unit-synthesized i4 (0x08) form is also accepted.
                if (count == 1 && ps[0] is 0x08 or 0x11)
                    return I("il2cpp::runtime::icall_rwls_ctor_policy", 1, false, false, true,
                        new[] { "", "i" });
                return null;
            case "EnterReadLock":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_enter_read", 0, false, false, true);
                return null;
            case "ExitReadLock":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_exit_read", 0, false, false, true);
                return null;
            case "EnterWriteLock":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_enter_write", 0, false, false, true);
                return null;
            case "ExitWriteLock":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_exit_write", 0, false, false, true);
                return null;
            case "EnterUpgradeableReadLock":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_enter_upgradeable", 0, false, false, true);
                return null;
            case "ExitUpgradeableReadLock":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_exit_upgradeable", 0, false, false, true);
                return null;
            case "TryEnterReadLock":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_rwls_try_enter_read", 1, true, false, true,
                        new[] { "", "i" });
                return null;
            case "TryEnterWriteLock":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_rwls_try_enter_write", 1, true, false, true,
                        new[] { "", "i" });
                return null;
            case "TryEnterUpgradeableReadLock":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_rwls_try_enter_upgradeable", 1, true, false, true,
                        new[] { "", "i" });
                return null;
            case "get_IsReadLockHeld":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_is_read_lock_held", 0, true, false, true);
                return null;
            case "get_IsWriteLockHeld":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_is_write_lock_held", 0, true, false, true);
                return null;
            case "get_IsUpgradeableReadLockHeld":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_is_upgradeable_read_lock_held", 0, true, false, true);
                return null;
            case "get_CurrentReadCount":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_current_read_count", 0, true, false, true);
                return null;
            case "get_WaitingReadCount":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_waiting_read_count", 0, true, false, true);
                return null;
            case "get_WaitingWriteCount":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_waiting_write_count", 0, true, false, true);
                return null;
            case "get_WaitingUpgradeCount":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_waiting_upgrade_count", 0, true, false, true);
                return null;
            case "get_RecursionPolicy":
                if (count == 0) return I("il2cpp::runtime::icall_rwls_get_recursion_policy", 0, true, false, true);
                return null;
            default:
                return null;
        }
    }
}
