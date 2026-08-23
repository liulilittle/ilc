namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Hashtable -- System.Collections.Hashtable icall mappings
// (il2cpp_hashtable.h).
// Owners: System.Collections.Hashtable
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapHashtable(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // M7R3 (hashtable2/hashtablecase): the 0-arg ctor's runtime
                // signature is icall_hashtable_ctor(Il2CppObject* self, int32_t
                // capacity) -- the receiver (args[0], the newobj temp) must be
                // FIRST, the injected capacity 0 AFTER it. AppendZeroArg (NOT
                // PrependZeroArg, which would emit `(0, t0)` -- the 0 in the
                // self slot and the object in the capacity slot, a C++ type
                // error).
                if (count == 0)
                    return I("il2cpp::runtime::icall_hashtable_ctor", 0, false, false, true, null, null, appendZero: true);
                return I("il2cpp::runtime::icall_hashtable_ctor", 1, false, false, true, new[] { "", "i" });
            case "Add": return I("il2cpp::runtime::icall_hashtable_add", 2, false, false, true);
            case "set_Item": return I("il2cpp::runtime::icall_hashtable_set", 2, false, false, true);
            case "get_Item": return I("il2cpp::runtime::icall_hashtable_get", 1, true, true, true);
            case "ContainsKey": return I("il2cpp::runtime::icall_hashtable_contains_key", 1, true, false, true);
            // ContainsValue(value): the O(n) linear scan (hashtable.cpp).
            case "ContainsValue": return I("il2cpp::runtime::icall_hashtable_contains_value", 1, true, false, true);
            case "Remove": return I("il2cpp::runtime::icall_hashtable_remove", 1, false, false, true);
            case "get_Count": return I("il2cpp::runtime::icall_hashtable_get_count", 0, true, false, true);
            case "Clear": return I("il2cpp::runtime::icall_hashtable_clear", 0, false, false, true);
            // M7 hash2 (2026-08-08): Hashtable.Keys / Hashtable.Values -- the
            // runtime icall_hashtable_get_keys / icall_hashtable_get_values
            // (il2cpp_hashtable.h:163/167) return SNAPSHOT arrays (the
            // table's stable enumeration order; Keys[i] and Values[i] always
            // describe the same entry -- net48 alignment invariant). Results
            // are reference arrays.
            case "get_Keys": return I("il2cpp::runtime::icall_hashtable_get_keys", 0, true, true, true);
            case "get_Values": return I("il2cpp::runtime::icall_hashtable_get_values", 0, true, true, true);
            default: return null;
        }
    }
}
