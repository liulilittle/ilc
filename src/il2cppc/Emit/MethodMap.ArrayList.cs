namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ArrayList -- System.Collections.ArrayList icall mappings
// (arraylist.h/.cpp; runtime domain complete).
// Owners: System.Collections.ArrayList
//
// NAMESPACE: the arraylist.h/.cpp icalls are declared in PLAIN namespace
// il2cpp (like stackqueue.h, unlike most BCL icall families under
// il2cpp::runtime) -- the CppNames below must stay "il2cpp::il2cpp_arraylist_*"
// or the generated code will fail to link.
//
// Elements are Il2CppObject* slots: the IL `box` opcode boxes value
// arguments BEFORE Add/set_Item (box wiring) -- `arr.Add(5)` pushes a boxed
// int, so NO cast code is needed on the value slots. get_Item results stay
// Il2CppObject* -- the CALLER's castclass/unbox turns them back into values.
//
// .ctor: the runtime create icall takes NO self and RETURNS the new object
// (Queue/Stack convention). The newobj temp is passed as the call's first
// argument by the fold, so it must be DROPPED ("drop"). Capacity ctors have
// no runtime icall -> LOUD.
//
// Add: .NET Add(object) returns the int32 insertion index, but the runtime
// il2cpp_arraylist_add returns void -- the managed return is DROPPED
// (ReturnsValue false). Code that stores the Add result (int n = list.Add(x))
// fails loud at C++ compile ("void value not ignored") -- no silent wrong
// value is ever produced.
//
// Remove(value): .NET removes the FIRST element EQUAL to 'value' (static
// Object.Equals value semantics -- null matches null, boxed values by
// payload, strings by ordinal content) and shifts the followers left. The
// runtime il2cpp_arraylist_remove (arraylist.h:106) implements exactly this
// single-call contract (index_of + remove_at + null-shift in one icall) --
// the M7 collchain unblock (the composite RemoveAt(IndexOf(value)) is NOT
// expressible in one ResolvedMethod, but the runtime now owns it).
//
// NOT MAPPED (out of scope; return null -> generated-name path):
//   * ToArray (runtime il2cpp_arraylist_to_array exists -- one-liner when
//     a case needs it).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapArrayList(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::il2cpp_arraylist_create", 0, true, true, true, new[] { "drop" });
                // ArrayList(int capacity): an INSTANCE-style ctor -- the receiver
                // (the newobj-allocated list) is pre-sized in place and the int32
                // capacity rides the "i" cast (arraylist.h). ParamCount is the
                // IL argument count (1); the casts array carries the receiver
                // slot too (the List<T>(int) capacity-ctor precedent).
                if (count == 1 && ps.Length >= 1 && ps[0] == 0x08)
                    return I("il2cpp::il2cpp_arraylist_create_capacity", 1, false, false, true,
                        new[] { "", "i" });
                throw new NotSupportedException($"ArrayList ctor with {count} arg(s) not wired (the runtime exposes only the parameterless and int-capacity create icalls)");
            case "Add": return I("il2cpp::il2cpp_arraylist_add", 1, false, false, true);
            case "get_Item": return I("il2cpp::il2cpp_arraylist_get_item", 1, true, true, true, new[] { "", "i" });
            case "set_Item": return I("il2cpp::il2cpp_arraylist_set_item", 2, false, false, true, new[] { "", "i", "" });
            case "get_Count": return I("il2cpp::il2cpp_arraylist_get_count", 0, true, false, true);
            case "Clear": return I("il2cpp::il2cpp_arraylist_clear", 0, false, false, true);
            case "RemoveAt": return I("il2cpp::il2cpp_arraylist_remove_at", 1, false, false, true, new[] { "", "i" });
            case "Insert": return I("il2cpp::il2cpp_arraylist_insert", 2, false, false, true, new[] { "", "i", "" });
            case "IndexOf": return I("il2cpp::il2cpp_arraylist_index_of", 1, true, false, true);
            case "Contains": return I("il2cpp::il2cpp_arraylist_contains", 1, true, false, true);
            // M7 collchain (2026-08-09): the runtime value-matching Remove
            // icall exists (il2cpp_arraylist_remove, arraylist.h:106) -- the
            // receiver passes through "" (Il2CppObject*), the value arg is a
            // reference slot (boxed by the IL box opcode before the call).
            case "Remove": return I("il2cpp::il2cpp_arraylist_remove", 1, false, false, true);
            // ArrayList A-class (capacity read + three Is-false + SyncRoot=self).
            case "get_Capacity": return I("il2cpp::il2cpp_arraylist_get_capacity", 0, true, false, true);
            case "get_IsFixedSize": return I("il2cpp::il2cpp_arraylist_get_is_fixed_size", 0, true, false, true);
            case "get_IsReadOnly": return I("il2cpp::il2cpp_arraylist_get_is_read_only", 0, true, false, true);
            case "get_IsSynchronized": return I("il2cpp::il2cpp_arraylist_get_is_synchronized", 0, true, false, true);
            case "get_SyncRoot": return I("il2cpp::il2cpp_arraylist_get_sync_root", 0, true, true, true);
            // ArrayList B-class (new runtime icalls, arraylist.cpp).
            case "set_Capacity":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::il2cpp_arraylist_set_capacity", 1, false, false, true, new[] { "", "i" });
                return null;
            case "Clone":
                if (count == 0)
                    return I("il2cpp::il2cpp_arraylist_clone", 0, true, true, true);
                return null;
            case "CopyTo":
                if (count == 2)
                    return I("il2cpp::il2cpp_arraylist_copy_to", 2, false, false, true, new[] { "", "", "i" });
                return null;
            case "TrimToSize":
                if (count == 0)
                    return I("il2cpp::il2cpp_arraylist_trim_to_size", 0, false, false, true);
                return null;
            case "Reverse":
                if (count == 0)
                    return I("il2cpp::il2cpp_arraylist_reverse", 0, false, false, true);
                if (count == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::il2cpp_arraylist_reverse_range", 2, false, false, true, new[] { "", "i", "i" });
                return null;
            case "RemoveRange":
                if (count == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::il2cpp_arraylist_remove_range", 2, false, false, true, new[] { "", "i", "i" });
                return null;
            default: return null;
        }
    }
}
