namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.WeakReference -- System.WeakReference object icalls
// (src/runtime/include/internal/il2cpp_gc_ext.h -- il2cpp::runtime::icall_weakref_*).
// Owners: System.WeakReference
//
// RUNTIME OBJECT CLOSURE (W-1 weakrefcase work order): System.WeakReference is
// a managed object -- the runtime defines the System_WeakReference_class
// descriptor (Il2CppWeakRefObject: header + int32 HANDLE into the gcext
// weak-target handle table, il2cpp_gc_ext.h) and the object icalls own the
// handle. Allocation goes through
// il2cpp_object_new(&System_WeakReference_class) -- the BclAllocExpr fallback
// in CodeGenerator.cs emits exactly this mangled global symbol, so NO
// BclAllocExpr entry is required for the object to allocate.
//
//   .ctor(object target)      -> icall_weakref_ctor(Il2CppObject*, Il2CppObject*)
//   get_IsAlive()             -> icall_weakref_get_is_alive(Il2CppObject*) -> bool
//   get_Target()              -> icall_weakref_get_target(Il2CppObject*) -> object
//   set_Target(object value)  -> icall_weakref_set_target(Il2CppObject*, Il2CppObject*)
//
// NOT wired (LOUD): .ctor(object, bool trackResurrection) and
// get_TrackResurrection -- the handle-table model does not track resurrection
// (a documented net48 simplification).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapWeakReference(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_weakref_ctor", 1, false, false, true);
                if (count == 2 && ps[0] == 0x1C && ps[1] == 0x02)
                    throw new NotSupportedException(
                        "WeakReference(object, bool trackResurrection) not wired: the handle-table object model does not track resurrection");
                throw new NotSupportedException($"WeakReference ctor with {count} param(s) not wired");
            case "get_IsAlive":
                return I("il2cpp::runtime::icall_weakref_get_is_alive", 0, true, false, true);
            case "get_Target":
                // ReturnsValue=true: the icall returns the target (Il2CppObject*)
                // and IL consumes it (e.g. `wr.Target == o`); rv=false would fold
                // the call into a statement + DiscardedValueExpr (StackFold
                // rv=false path) and the emitter rejects that shape loudly.
                return I("il2cpp::runtime::icall_weakref_get_target", 0, true, true, true);
            case "set_Target":
                return I("il2cpp::runtime::icall_weakref_set_target", 1, false, false, true);
            // netcore21/31 audit closure: Finalize() -- finalization is driven
            // by the GC's finalizer machinery (icall_gc_finalize_trigger);
            // the explicit Finalize icall surface is a validated no-op
            // (stubs.cpp icall_weak_reference_finalize).
            case "Finalize":
                return I("il2cpp::runtime::icall_weak_reference_finalize", 0, false, false, true);
            default:
                throw new NotSupportedException(
                    "System.WeakReference member not wired: the object icalls cover .ctor(object)/IsAlive/Target " +
                    "(see il2cpp_gc_ext.h)");
        }
    }
}
