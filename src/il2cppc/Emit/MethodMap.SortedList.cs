namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.SortedList -- System.Collections.SortedList icall mappings
// (sortedlist.h).
// Owners: System.Collections.SortedList
//
// DISPOSITION (dictsl-b23, 2026-08-11): the full sorted-array icall surface
// is WIRED for the non-generic SortedList domain (dictcase SortedList section
// green). Key/value are Il2CppObject* slots: the IL `box` opcode boxes value
// keys/values before the call (box wiring), so key/value args carry "" (the
// icalls cast internally); int32 index/capacity args carry "i"; indexer GET /
// GetByIndex / GetKey / GetValueAt / Keys / Values results stay
// Il2CppObject* -- the CALLER's castclass/unbox turns them back into values.
//
// SEMANTICS BASELINE (net48, see sortedlist.h):
//   * Add: duplicate key -> ArgumentException (list unchanged), null key ->
//     ArgumentNullException.
//   * get_Item (indexer GET by key, il2cpp_sortedlist_get_by_key): null for
//     both "missing" and "stored null" -- indistinguishable, like .NET.
//   * set_Item (indexer SET by key, il2cpp_sortedlist_set_by_key): replaces an
//     existing key's value or inserts at the sorted position -- never throws
//     for a missing key (unlike Add).
//   * ContainsKey / IndexOfKey / Remove: null key -> ArgumentNullException;
//     Remove of an absent key is a no-op.
//   * GetByIndex / GetKey / GetValueAt / RemoveAt: index outside [0, Count) ->
//     ArgumentOutOfRangeException.
//   * Keys / Values: SNAPSHOT arrays (not live views), index-aligned, in the
//     list's ascending enumeration order.
//   * Clear: drop every pair; Count -> 0, capacity unchanged.
//
// .ctor: the runtime create icall is SELF-MUTATING (void create(self,
// capacity)) -- the newobj temp stays the first argument and the capacity is
// the injected trailing 0 for the parameterless ctor (AppendZeroArg, the
// Hashtable precedent) or the int32 capacity arg for the 1-arg ctor.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapSortedList(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> create(self, 0) (the injected 0 lands
                // AFTER the receiver -- AppendZeroArg); capacity ctor ->
                // create(self, capacity). No other SortedList ctor is wired.
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 0, false, false, true, null, null, appendZero: true);
                if (count == 1)
                    return I("il2cpp::runtime::il2cpp_sortedlist_create", 1, false, false, true, new[] { "", "i" });
                throw new NotSupportedException($"SortedList ctor with {count} arg(s) not wired (runtime has only the parameterless / capacity create icalls)");
            case "Add": return I("il2cpp::runtime::il2cpp_sortedlist_add", 2, false, false, true, new[] { "", "", "" });
            case "set_Item": return I("il2cpp::runtime::il2cpp_sortedlist_set_by_key", 2, false, false, true, new[] { "", "", "" });
            case "get_Item": return I("il2cpp::runtime::il2cpp_sortedlist_get_by_key", 1, true, true, true, new[] { "", "" });
            case "GetByIndex": return I("il2cpp::runtime::il2cpp_sortedlist_get_by_index", 1, true, true, true, new[] { "", "i" });
            case "GetKey": return I("il2cpp::runtime::il2cpp_sortedlist_get_key_at", 1, true, true, true, new[] { "", "i" });
            case "GetValueAt": return I("il2cpp::runtime::il2cpp_sortedlist_get_value_at", 1, true, true, true, new[] { "", "i" });
            case "IndexOfKey": return I("il2cpp::runtime::il2cpp_sortedlist_index_of_key", 1, true, false, true, new[] { "", "" });
            case "ContainsKey": return I("il2cpp::runtime::il2cpp_sortedlist_contains_key", 1, true, false, true, new[] { "", "" });
            case "Remove": return I("il2cpp::runtime::il2cpp_sortedlist_remove_by_key", 1, false, false, true, new[] { "", "" });
            case "RemoveAt": return I("il2cpp::runtime::il2cpp_sortedlist_remove_at", 1, false, false, true, new[] { "", "i" });
            case "get_Count": return I("il2cpp::runtime::il2cpp_sortedlist_get_count", 0, true, false, true, new[] { "" });
            case "get_Capacity": return I("il2cpp::runtime::il2cpp_sortedlist_get_capacity", 0, true, false, true, new[] { "" });
            // D-SL-3 (sldeep): the Keys / Values snapshots are ILCollection-typed
            // at the C# surface. ReturnTypeName makes the fold track the result
            // as a distinct pseudo-type so ICollection.Count / IEnumerable
            // enumeration on the snapshot can dispatch by receiver type (see
            // MethodMap.Collections.cs -- the Console.OutStream precedent).
            case "get_Keys": return I("il2cpp::runtime::il2cpp_sortedlist_get_keys", 0, true, true, true, new[] { "" },
                returnTypeName: "System.Collections.SortedList.KeyList");
            case "get_Values": return I("il2cpp::runtime::il2cpp_sortedlist_get_values", 0, true, true, true, new[] { "" },
                returnTypeName: "System.Collections.SortedList.ValueList");
            case "Clear": return I("il2cpp::runtime::il2cpp_sortedlist_clear", 0, false, false, true, new[] { "" });
            default: return null;
        }
    }
}
