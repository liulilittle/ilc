namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Queue -- System.Collections.Queue icall mappings (stackqueue.h).
// Owners: System.Collections.Queue
//
// NAMESPACE: the stackqueue.h/.cpp icalls are declared in PLAIN namespace
// il2cpp (unlike most BCL icall families under il2cpp::runtime) -- the
// CppNames below must stay "il2cpp::il2cpp_queue_*" or the generated code
// will fail to link.
//
// Elements are Il2CppObject* slots: the IL `box` opcode already boxes value
// arguments before Enqueue (box wiring), and Dequeue/Peek results stay
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
    private static ResolvedMethod? TryMapQueue(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::il2cpp_queue_create", 0, true, true, true, new[] { "drop" });
                throw new NotSupportedException($"Queue ctor with {count} arg(s) not wired (the runtime has only the parameterless create icall)");
            case "Enqueue": return I("il2cpp::il2cpp_queue_enqueue", 1, false, false, true);
            case "Dequeue": return I("il2cpp::il2cpp_queue_dequeue", 0, true, true, true);
            case "Peek": return I("il2cpp::il2cpp_queue_peek", 0, true, true, true);
            case "get_Count": return I("il2cpp::il2cpp_queue_get_count", 0, true, false, true);
            case "Clear": return I("il2cpp::il2cpp_queue_clear", 0, false, false, true);
            // M7 stkq (2026-08-08): Queue.ToArray -- the runtime
            // il2cpp_queue_to_array (stackqueue.h:149, stkq audit GAP-1)
            // returns an EXACT-LENGTH object[] in HEAD-FIRST ring-aware order
            // (result[i] = slots[RingIndex(head,i,len)]); empty queue ->
            // length-0 array. The result is a reference array.
            case "ToArray": return I("il2cpp::il2cpp_queue_to_array", 0, true, true, true);
            // C42 sqenum-b23: GetEnumerator -> the snapshot enumerator object
            // (ReturnTypeName tracks it so the IEnumerator member dispatch in
            // MethodMap.Collections.cs resolves MoveNext/get_Current/Reset).
            case "GetEnumerator":
                return I("il2cpp::il2cpp_queue_get_enumerator", 0, true, true, true, new[] { "" },
                    returnTypeName: "System.Collections.Queue.Enumerator");
            default: return null;
        }
    }
}
