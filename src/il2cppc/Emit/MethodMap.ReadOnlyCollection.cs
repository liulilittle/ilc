namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ReadOnlyCollection -- System.Collections.ObjectModel.ReadOnlyCollection`1
// mappings.
//
// The runtime type is a thin VIEW over an array (readonlycollection.h): it
// stores the array and answers every member by delegating to it. The wrapper is
// element-agnostic, so the mapping only has to pick the TYPED accessor the call
// site needs:
//
//   get_Item / Contains / IndexOf  -- an int32 element uses the unboxed i4
//                                     accessor; every other element family
//                                     (reference, long, double, ...) uses the
//                                     boxed object accessor.
//   get_Count / CopyTo / GetEnumerator -- element-agnostic, one entry each.
//
// The generic argument byte (genArgs[0]) is what selects the family, exactly as
// Array.Empty<T> does.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapReadOnlyCollection(
        string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type,
        string? instName = null)
    {
        // The element family comes from the INSTANTIATION name (the TypeSpec
        // decoder names it "ReadOnlyCollection`1_int" / "_string" / ...), not
        // from genArgs: a member call on a closed instantiation carries no
        // MethodSpec generic arguments, so genArgs is null here. int32 is the
        // only family with an unboxed accessor; everything else goes through
        // the boxed carrier.
        var instTag = instName ?? receiverType;
        var isI4 = instTag is "System.Collections.ObjectModel.ReadOnlyCollection`1_int";

        switch (name)
        {
            case "get_Count":
                return I("il2cpp::il2cpp_readonlycollection_get_count", 0, true, false, true);
            case "get_Item":
                if (isI4)
                    return I("il2cpp::il2cpp_readonlycollection_get_item_i4", 1, true, false, true,
                        new[] { "", "i" });
                return I("il2cpp::il2cpp_readonlycollection_get_item_obj", 1, true, true, true,
                    new[] { "", "i" }, returnTypeName: "System.Object");
            case "Contains":
                if (isI4)
                    return I("il2cpp::il2cpp_readonlycollection_contains_i4", 1, true, false, true,
                        new[] { "", "i" });
                return I("il2cpp::il2cpp_readonlycollection_contains_obj", 1, true, false, true,
                    new[] { "", "" });
            case "IndexOf":
                if (isI4)
                    return I("il2cpp::il2cpp_readonlycollection_index_of_i4", 1, true, false, true,
                        new[] { "", "i" });
                return I("il2cpp::il2cpp_readonlycollection_index_of_obj", 1, true, false, true,
                    new[] { "", "" });
            case "CopyTo":
                return I("il2cpp::il2cpp_readonlycollection_copy_to", 2, false, false, true,
                    new[] { "", "", "i" });
            case "GetEnumerator":
                return I("il2cpp::il2cpp_readonlycollection_get_enumerator", 0, true, true, true,
                    returnTypeName: "System.Collections.IEnumerator");
            default:
                return null;
        }
    }
}
