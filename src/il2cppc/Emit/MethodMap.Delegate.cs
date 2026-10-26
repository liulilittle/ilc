namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Delegate -- System.Delegate icall mappings (il2cpp_delegate.h).
// Owners: System.Delegate
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDelegate(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Combine":
            case "op_Addition":
                // returnTypeName: the result is a delegate of the SAME type as
                // the operands -- tracked so receiver hints propagate through
                // combine chains (ab.Equals(ab3) must see ab3 as a delegate).
                // Static calls carry no receiver: the LAST-arg hint
                // (arg0Type) is the delegate type (IL is type-safe).
                //
                // BUG-2 (batch 177b): the runtime combine check lives INSIDE
                // icall_delegate_combine -- every delegate carries its
                // delegate-type ID from creation (il2cpp_delegate_new's
                // typeId arg, FNV-1a of the full type name; 0 = untracked), so
                // the runtime compares the OPERAND OBJECTS directly and raises
                // ArgumentException on differing non-zero ids (net48:
                // Func<int,int> + Action<int> throws -- probe-verified). No
                // call-site literals needed; the type identity rides with the
                // delegate through every flow (event backing fields, cached
                // lambda static fields, joins, method args).
                return I("il2cpp::runtime::icall_delegate_combine", 2, true, true, false, returnTypeName: arg0Type);
            case "Remove":
            case "op_Subtraction":
                return I("il2cpp::runtime::icall_delegate_remove", 2, true, true, false, returnTypeName: arg0Type);
            case "op_Equality":
            case "op_Inequality":
                // static bool op_Equality/op_Inequality(Delegate, Delegate): C#
                // == / != on ANY delegate type emits the base System.Delegate
                // operator (csc never emits the derived type's operator). Both
                // fold to the null-safe structural equals -- netcore
                // Delegate.cs op_Equality contract: a==b -> true, one-null ->
                // false, else pairwise invocation-list compare. The icall is
                // the SAME for both; the fold negates it for op_Inequality
                // (the "== 0" shape) -- negation is NOT a runtime concern.
                if (ps.Length != 2) return null;
                return I("il2cpp::runtime::icall_delegate_equals", 2, true, false, false);
            case "GetInvocationList": return I("il2cpp::runtime::icall_delegate_get_invocation_list", 0, true, true, true);
            case "get_Target": return I("il2cpp::runtime::icall_delegate_get_target", 0, true, true, true);
            case "Equals":
                // instance Delegate.Equals(object): receiver + argument.
                return ps.Length == 1 ? I("il2cpp::runtime::icall_delegate_equals", 1, true, false, true) : null;
            case "RemoveAll":
                // Delegate.RemoveAll(Delegate, Delegate): net48 removes EVERY
                // occurrence of the value's invocation list (the runtime's
                // Remove removes only the LAST). Same delegate-type result
                // tracking as Remove (the LAST-arg hint carries the type).
                return I("il2cpp::runtime::icall_delegate_remove_all", 2, true, true, false, returnTypeName: arg0Type);
            // M7 delegate2 (2026-08-08): Delegate.get_Method -- the runtime
            // delegate (il2cpp_delegate.h) stores only a raw methodPtr, and
            // the name registry (delegateext.cpp icall_delegate_get_method_name,
            // added by the deleg2 runtime audit) flattens the multicast chain
            // to the LAST leaf and looks up its SHORT method name -- returning
            // a managed System.String (NOT a MethodInfo). `f.Method` therefore
            // yields the name string directly, and the follow-up `.Name`
            // (callvirt MemberInfo::get_Name on a System.String receiver)
            // folds to the IdentityMarker (MethodMap.Object.cs / CultureInfo.cs
            // precedent) -- the string IS the name, no object model needed.
            // The ReturnTypeName hint propagates the "System.String" receiver
            // type so StackFold's receiver-aware re-resolve can select that
            // fold.
            case "get_Method":
                return I("il2cpp::runtime::icall_delegate_get_method_name", 0, true, true, true, returnTypeName: "System.String");
            // P19 exprtree: Delegate.DynamicInvoke(params object[] args) -- the
            // compiled expression lambda (tracked as "System.Delegate" by the
            // Compile mapping) evaluates its body and returns the BOXED int32
            // result (ReturnsRef=true -- the result is a managed reference).
            // The params array (ps.Length == 1) is dropped (the runtime
            // evaluates the parameterless lambda).
            case "DynamicInvoke":
                if (ps.Length == 1)
                    return I("il2cpp::runtime::icall_expr_dynamic_invoke", 1, true, true, true, new[] { "", "drop" });
                return null;
            // RF-N7 (NEXT2-DECISION section ② RF-N7): Delegate.CreateDelegate
            // -- explicit LOUD. Binding a delegate to a runtime MethodInfo has
            // no AOT support (the member VALUE is a name string; there is no
            // runtime method binding table), so the net48 (Type, MethodInfo)
            // overload maps to a runtime stub that ALWAYS throws
            // NotSupportedException (il2cpp_createdelegate_loud) -- never a
            // silent spelling generated-member name. STATIC (Delegate.*) --
            // no receiver slot; both IL args ride as ref slots.
            case "CreateDelegate":
                if (ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::il2cpp_createdelegate_loud", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: "System.Delegate");
                throw new NotSupportedException(
                    $"Delegate.CreateDelegate with {ps.Length} parameter(s) not wired in RF-N7 -- only the net48 (Type, MethodInfo) overload maps to the LOUD stub il2cpp_createdelegate_loud");
            // P6: Delegate internal helpers -- the BCL calls these for delegate
            // infrastructure but the ILC runtime reimplements delegates natively.
            // Stubs return sensible defaults.
            case "InternalAllocLike": return I("il2cpp::runtime::icall_delegate_internal_alloc_like", 1, true, true, false);
            case "GetMulticastInvoke": return I("il2cpp::runtime::icall_delegate_get_multicast_invoke", 0, true, false, false);
            case "GetInvokeMethod": return I("il2cpp::runtime::icall_delegate_get_invoke_method", 0, true, false, false);
            case "FindMethodHandle": return I("il2cpp::runtime::icall_delegate_find_method_handle", 0, true, true, false);
            case "InternalEqualMethodHandles": return I("il2cpp::runtime::icall_delegate_internal_equal_method_handles", 2, true, false, false);
            case "AdjustTarget": return I("il2cpp::runtime::icall_delegate_adjust_target", 2, true, true, false);
            case "GetCallStub": return I("il2cpp::runtime::icall_delegate_get_call_stub", 0, true, false, false);
            // P6: Delegate internal methods (stubs).
            case "InternalEqualTypes": return I("il2cpp::runtime::icall_delegate_internal_equal_types", 2, true, false, false);
            case "CompareUnmanagedFunctionPtrs": return I("il2cpp::runtime::icall_delegate_compare_unmanaged_function_ptrs", 2, true, false, false);
            default: return null;
        }
    }
}
