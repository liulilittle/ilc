namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Stack -- System.Collections.Stack icall mappings (stackqueue.h).
// Owners: System.Collections.Stack
//
// NAMESPACE: the stackqueue.h/.cpp icalls are declared in PLAIN namespace
// il2cpp (unlike most BCL icall families under il2cpp::runtime) -- the
// CppNames below must stay "il2cpp::il2cpp_stack_*" or the generated code
// will fail to link.
//
// Elements are Il2CppObject* slots: the IL `box` opcode already boxes value
// arguments before Push (box wiring), and Pop/Peek results stay
// Il2CppObject* -- the CALLER's castclass/unbox turns them back into values.
// No cast codes are therefore needed on the argument/result slots.
//
// .ctor: the runtime create icall takes NO self and RETURNS the new object
// (unlike the Hashtable self-mutating ctor). The newobj temp is passed as
// the call's first argument by the fold, so it must be DROPPED ("drop") to
// keep the emitted call arity correct. Capacity ctors have no runtime
// icall -> LOUD.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStack(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::il2cpp_stack_create", 0, true, true, true, new[] { "drop" });
                throw new NotSupportedException($"Stack ctor with {count} arg(s) not wired (the runtime has only the parameterless create icall)");
            case "Push": return I("il2cpp::il2cpp_stack_push", 1, false, false, true);
            case "Pop": return I("il2cpp::il2cpp_stack_pop", 0, true, true, true);
            case "Peek": return I("il2cpp::il2cpp_stack_peek", 0, true, true, true);
            case "get_Count": return I("il2cpp::il2cpp_stack_get_count", 0, true, false, true);
            case "Clear": return I("il2cpp::il2cpp_stack_clear", 0, false, false, true);
            // M7 stkq (2026-08-08): Stack.ToArray -- the runtime
            // il2cpp_stack_to_array (stackqueue.h:116, stkq audit GAP-1)
            // returns an EXACT-LENGTH object[] of the stack's elements in
            // TOP-OF-STACK order (result[i] = slots[count-1-i]); empty stack
            // -> length-0 array. The result is a reference array.
            case "ToArray": return I("il2cpp::il2cpp_stack_to_array", 0, true, true, true);
            // C42 sqenum-b23: GetEnumerator -> the snapshot enumerator object
            // (ReturnTypeName tracks it so the IEnumerator member dispatch in
            // MethodMap.Collections.cs resolves MoveNext/get_Current/Reset).
            case "GetEnumerator":
                return I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.Collections.Stack.Enumerator");
            default: return null;
        }
    }
}
