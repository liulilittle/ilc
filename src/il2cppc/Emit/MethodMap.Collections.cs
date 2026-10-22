namespace Il2Cpp.Transpiler.Emit;

using Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// MethodMap.Collections -- System.Collections INTERFACE member dispatch
// (D-SL-3, sldeep). Owners: System.Collections.ICollection / IEnumerable /
// IEnumerator -- the interface surface of the non-generic collections.
//
// The ONLY concrete runtime objects reachable through these interfaces in the
// wired surface are the SortedList Keys / Values SNAPSHOT arrays (sortedlist.h
// -- get_Keys / get_Values return reference arrays whose C# compile-time type
// is ICollection). The resolver therefore dispatches on the RECEIVER's tracked
// pseudo-type (set by MethodMap.SortedList.cs get_Keys/get_Values
// ReturnTypeName, and by the get_enumerator result below):
//
//   receiver == "System.Collections.SortedList.KeyList" / ".ValueList"
//     ICollection::get_Count    -> il2cpp_sortedlist_snapshot_get_count
//     IEnumerable::GetEnumerator-> il2cpp_sortedlist_snapshot_get_enumerator
//   receiver == "System.Collections.SortedList.Enumerator"
//     IEnumerator::MoveNext     -> il2cpp_sortedlist_enumerator_move_next
//     IEnumerator::get_Current  -> il2cpp_sortedlist_enumerator_get_current
//     IEnumerator::Reset        -> il2cpp_sortedlist_enumerator_reset
//
// C42 sqenum-b23 (Stack/Queue foreach): the Stack / Queue containers also
// implement IEnumerable with snapshot enumerators (stackqueue.h -- the frozen
// items array + count/head at GetEnumerator time):
//
//   receiver == "System.Collections.Stack"
//     IEnumerable::GetEnumerator -> il2cpp_stack_get_enumerator
//   receiver == "System.Collections.Stack.Enumerator"
//     IEnumerator::MoveNext     -> il2cpp_stack_enumerator_move_next
//     IEnumerator::get_Current  -> il2cpp_stack_enumerator_get_current
//     IEnumerator::Reset        -> il2cpp_stack_enumerator_reset
//   (the Queue arms mirror with il2cpp_queue_* and the
//    "System.Collections.Queue.Enumerator" pseudo-type)
//
// All icalls are INSTANCE (receiver rides in arg[0] as a managed reference,
// "" passthrough -- the runtime casts internally). MoveNext returns int32
// (bool carrier); get_Current returns Il2CppObject* (the key/value element);
// get_Count returns int32 (the snapshot length == list count at snapshot
// time). The FIRST resolution pass has receiverType==null (the fold defers
// the receiver-aware re-resolve -- the TextReader/IDisposable precedent), so
// every arm requires a KNOWN tracked receiver pseudo-type; any other receiver
// falls through to the generated-name path (loud).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapCollectionInterface(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // P25 (listp25): the generic List<int>.Enumerator is a STRUCT, so the
        // C# compiler calls its members DIRECTLY on the concrete nested type
        // (owner "System.Collections.Generic.List`1+Enumerator", a nested-type
        // name -- the '+' is the ECMA nesting separator -- from a TypeSpec
        // parent), NOT through the IEnumerator interface. The owner itself IS
        // the dispatcher: no receiverType hint is needed. When the receiver
        // hint IS tracked ("System.Collections.Generic.List`1.Enumerator",
        // the GetEnumerator ReturnTypeName), the IEnumerator-owner subroutine
        // below also matches. Both routes converge on the same arms.
        if (owner == "System.Collections.Generic.List`1+Enumerator")
            return TryMapListEnumerator(owner, name, ps, count, genArgs, receiverType);
        // G4 B2 (b2b8): the generic Queue`1<int> / Stack`1<string> struct
        // enumerators -- members are called directly on the nested type (the
        // same owner-direct route as the List`1+Enumerator arm above).
        if (owner == "System.Collections.Generic.Queue`1+Enumerator")
            return TryMapQueueEnumerator(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
        if (owner == "System.Collections.Generic.Stack`1+Enumerator")
            return TryMapStackEnumerator(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);

        switch (owner)
        {
            case "System.Collections.ICollection":                // ICollection.get_Count on a Keys / Values snapshot: the
                // snapshot array length (see sortedlist.cpp).
                if (name == "get_Count" && count == 0
                    && receiverType is "System.Collections.SortedList.KeyList" or "System.Collections.SortedList.ValueList")
                    return I("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_count", 0, true, false, true, new[] { "" });
                return null;
            case "System.Collections.IEnumerable":
                // IEnumerable.GetEnumerator on a Keys / Values snapshot: wrap
                // the array in a snapshot enumerator (ReturnTypeName tracks the
                // enumerator for the IEnumerator member dispatch below).
                if (name == "GetEnumerator" && count == 0
                    && receiverType is "System.Collections.SortedList.KeyList" or "System.Collections.SortedList.ValueList")
                    return I("il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator", 0, true, true, true, new[] { "" },
                        returnTypeName: "System.Collections.SortedList.Enumerator");
                // C42 sqenum-b23: the Stack / Queue containers (foreach).
                if (name == "GetEnumerator" && count == 0)
                {
                    if (receiverType == "System.Collections.Stack")
                        return I("il2cpp::il2cpp_stack_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Collections.Stack.Enumerator");
                    if (receiverType == "System.Collections.Queue")
                        return I("il2cpp::il2cpp_queue_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Collections.Queue.Enumerator");
                    // xmlenum: XmlNodeList implements IEnumerable -- the
                    // static-type route (IEnumerable x = doc.SelectNodes(...);
                    // foreach) resolves here; the concrete XmlNodeList route
                    // lives in MethodMap.Xml.cs (TryMapXml GetEnumerator).
                    if (receiverType == "System.Xml.XmlNodeList")
                        return I("il2cpp::runtime::il2cpp_xml_nodelist_get_enumerator", 0, true, true, true, new[] { "" },
                            returnTypeName: "System.Xml.XmlNodeList.Enumerator");
                }
                return null;
            case "System.Collections.IEnumerator":
                // IEnumerator members on the snapshot enumerator object.
                if (receiverType == "System.Collections.SortedList.Enumerator")
                {
                    switch (name)
                    {
                        case "MoveNext":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_move_next", 0, true, false, true, new[] { "" });
                            return null;
                        case "get_Current":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_get_current", 0, true, true, true, new[] { "" });
                            return null;
                        case "Reset":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_reset", 0, false, false, true, new[] { "" });
                            return null;
                        default:
                            return null;
                    }
                }
                // C42 sqenum-b23: the Stack / Queue enumerator objects.
                if (receiverType == "System.Collections.Stack.Enumerator" || receiverType == "System.Collections.Queue.Enumerator")
                {
                    bool isStack = receiverType == "System.Collections.Stack.Enumerator";
                    switch (name)
                    {
                        case "MoveNext":
                            if (count == 0)
                                return I(isStack ? "il2cpp::il2cpp_stack_enumerator_move_next" : "il2cpp::il2cpp_queue_enumerator_move_next",
                                    0, true, false, true, new[] { "" });
                            return null;
                        case "get_Current":
                            if (count == 0)
                                return I(isStack ? "il2cpp::il2cpp_stack_enumerator_get_current" : "il2cpp::il2cpp_queue_enumerator_get_current",
                                    0, true, true, true, new[] { "" });
                            return null;
                        case "Reset":
                            if (count == 0)
                                return I(isStack ? "il2cpp::il2cpp_stack_enumerator_reset" : "il2cpp::il2cpp_queue_enumerator_reset",
                                    0, false, false, true, new[] { "" });
                            return null;
                        default:
                            return null;
                    }
                }
                // xmlenum: the XmlNodeList snapshot enumerator object
                // (System.Xml.XmlNodeListEnumerator semantics: document order;
                // get_Current before MoveNext / after the end yields null;
                // Reset rewinds).
                if (receiverType == "System.Xml.XmlNodeList.Enumerator")
                {
                    switch (name)
                    {
                        case "MoveNext":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_xml_nodelist_enumerator_move_next", 0, true, false, true, new[] { "" });
                            return null;
                        case "get_Current":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_xml_nodelist_enumerator_get_current", 0, true, true, true, new[] { "" });
                            return null;
                        case "Reset":
                            if (count == 0)
                                return I("il2cpp::runtime::il2cpp_xml_nodelist_enumerator_reset", 0, false, false, true, new[] { "" });
                            return null;
                        default:
                            return null;
                    }
                }
                // P25b: List<int> enumerator (Il2CppArrayEnumerator re-use).
                // The generated code passes (&loc) with deref:r, so the icall
                // receives Il2CppObject* (the enumerator). MoveNext returns
                // int32 (bool carrier); get_Current returns int32 (the element
                // value, NOT a reference -- the enumerator walks a value array).
                // listrefdeep: the reference expansions track the receiver as
                // "...List`1.Enumerator_<element>" (ListEnumeratorTrackedType)
                // -- TryMapListEnumerator picks the listobj arm from the suffix.
                if (receiverType == "System.Collections.Generic.List`1.Enumerator"
                    || (receiverType != null && receiverType.StartsWith("System.Collections.Generic.List`1.Enumerator_", StringComparison.Ordinal)))
                    return TryMapListEnumerator(owner, name, ps, count, genArgs, receiverType);
                return null;
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapListEnumerator -- P25 (listp25): System.Collections.Generic.
    // List`1+Enumerator (int32 AOT). The generic List<int>.Enumerator is a
    // STRUCT, so the C# compiler calls its members DIRECTLY on the concrete
    // nested type (owner "System.Collections.Generic.List`1+Enumerator" --
    // ECMA nested-name '+'), NOT via IEnumerator. Reaches this routine via:
    //   (a) the owner-direct route (BclResolveCore owner case), or
    //   (b) the IEnumerator-owner interface route with a tracked receiver
    //       hint "System.Collections.Generic.List`1.Enumerator".
    // The runtime re-uses Il2CppArrayEnumerator (sortedlist.h) as the list
    // enumerator object; the icalls take Il2CppObject*.
    // listrefdeep: a REFERENCE-element instantiation tracks the receiver as
    // "...List`1.Enumerator_<element>" (ListEnumeratorTrackedType); its
    // get_Current walks the reference array and returns Il2CppObject*
    // (il2cpp_list_obj_enumerator_*), so the dispatch follows the suffix.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapListEnumerator(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType)
    {
        const string objPrefix = "System.Collections.Generic.List`1.Enumerator_";
        var objElemSuffix = receiverType != null && receiverType.StartsWith(objPrefix, StringComparison.Ordinal)
            ? receiverType[objPrefix.Length..]
            : null;
        var objElemType = ListObjElementTypeName(objElemSuffix);
        var isObjEnum = objElemType != null;
        // G4: the Int64 expansion rides the same snapshot enumerator; only
        // get_Current differs (int64 unbox instead of a reference read).
        var longElem = objElemSuffix == "long";
        // G4: the Double expansion mirrors the Int64 one (double carrier,
        // ResultKind "d" on get_Current).
        var doubleElem = objElemSuffix == "double";
        // W5 (T2): the small-value expansions ride the same snapshot
        // enumerator; get_Current unboxes per kind (float through the double
        // carrier "d", the int32-slot kinds as plain int32).
        var valFloatElem = objElemSuffix == "float";
        var valIntElem = objElemSuffix is "bool" or "char" or "sbyte" or "byte" or "short" or "ushort" or "uint";
        // p0linq4 (D1): the BOXED value expansions (List<decimal> /
        // List<DateTime> / List<TimeSpan> / List<user struct>). The tracked
        // suffix carries the element: "System.Decimal" / "System.DateTime" /
        // "System.TimeSpan" or a dotted user-struct full name.
        var decElem = objElemSuffix == "System.Decimal";
        var dtElem = objElemSuffix == "System.DateTime";
        var tsElem = objElemSuffix == "System.TimeSpan";
        TypeDef? structElemDef = null;
        string? structElemCpp = null;
        if (objElemSuffix != null && objElemSuffix.Contains('.') && TypeEmitter.SessionTypes is { } knownEnumTypes)
        {
            structElemDef = knownEnumTypes.FirstOrDefault(t => t.MatchesFullName(objElemSuffix)
                && t.IsValueType && t.BaseTypeName != "System.Enum");
            if (structElemDef != null)
            {
                TypeEmitter.LayoutInstanceFields(structElemDef);
                structElemCpp = TypeEmitter.TypeCppName(structElemDef);
            }
        }
        var boxedValueElem = decElem || dtElem || tsElem || structElemDef != null;
        switch (name)
        {
            case "MoveNext":
                // bool MoveNext(): advances the snapshot cursor; returns the
                // bool carrier as int32.
                return count == 0
                    ? I(isObjEnum || longElem || doubleElem || valFloatElem || valIntElem || boxedValueElem ? "il2cpp::il2cpp_list_obj_enumerator_move_next" : "il2cpp::il2cpp_list_i4_enumerator_move_next",
                        0, true, false, true, new[] { "deref:r" })
                    : null;
            case "get_Current":
                // int32 get_Current() for the value expansion; the element
                // REFERENCE (Il2CppObject*) for the listobj expansion.
                if (count != 0) return null;
                if (doubleElem)
                    return I("il2cpp::il2cpp_list_obj_enumerator_get_current_double", 0, true, false, true, new[] { "deref:r" },
                        resultKind: "d");
                if (longElem)
                    return I("il2cpp::il2cpp_list_obj_enumerator_get_current_long", 0, true, false, true, new[] { "deref:r" },
                        resultKind: "l");
                if (valFloatElem)
                    return I("il2cpp::il2cpp_list_obj_enumerator_get_current_float", 0, true, false, true, new[] { "deref:r" },
                        resultKind: "d");
                // p0linq4 (D1): the boxed value families (see TryMapList's
                // get_Item arms -- same value-return shapes).
                if (decElem)
                    return I("il2cpp::il2cpp_list_obj_enumerator_get_current_dec", 0, true, false, true,
                        new[] { "deref:r" }, resultKind: "dec", returnTypeName: "System.Decimal");
                if (dtElem)
                    return I("il2cpp::il2cpp_list_obj_enumerator_get_current_dt", 0, true, false, true,
                        new[] { "deref:r" }, resultKind: "dt", returnTypeName: "System.DateTime");
                if (tsElem)
                    return I("il2cpp::il2cpp_list_obj_enumerator_get_current_ts", 0, true, false, true,
                        new[] { "deref:r" }, resultKind: "ts", returnTypeName: "System.TimeSpan");
                if (structElemCpp != null)
                    return I($"il2cpp::il2cpp_list_obj_enumerator_get_current_struct_t<{structElemCpp}>", 0, true, false, true,
                        new[] { "deref:r" }, resultKind: "us:" + structElemCpp,
                        returnTypeName: structElemDef!.FullName);
                if (valIntElem)
                    return I($"il2cpp::il2cpp_list_obj_enumerator_get_current_{objElemSuffix}", 0, true, false, true, new[] { "deref:r" });
                return isObjEnum
                    ? I("il2cpp::il2cpp_list_obj_enumerator_get_current", 0, true, true, true, new[] { "deref:r" },
                        returnTypeName: objElemType)
                    : I("il2cpp::il2cpp_list_i4_enumerator_get_current", 0, true, false, true, new[] { "deref:r" });
            case "Reset":
                return count == 0
                    ? I(isObjEnum || longElem || doubleElem || valFloatElem || valIntElem || boxedValueElem ? "il2cpp::il2cpp_list_obj_enumerator_reset" : "il2cpp::il2cpp_list_i4_enumerator_reset",
                        0, false, false, true, new[] { "deref:r" })
                    : null;
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // a5reds (strsort_ordinal): System.StringComparer static comparer getters.
    // get_Ordinal / get_OrdinalIgnoreCase return the runtime singleton objects
    // (arraysort.cpp; the klass names carry the semantics). The ReturnTypeName
    // pseudo-types are the dispatch keys for the generic Array.Sort<T> arm
    // (MethodMap.Array.cs Sort count==2), which routes a comparator argument
    // to il2cpp_array_sort_icomparer instead of the Comparison<T> delegate
    // sort. Compare(string,string) callvirt on the concrete type is NOT wired
    // (out of the two-red scope; the sort surface is the reachable path).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapStringComparer(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_Ordinal":
                if (count == 0)
                    return I("il2cpp::il2cpp_string_comparer_ordinal", 0, true, true, false,
                        returnTypeName: "System.StringComparer.Ordinal");
                return null;
            case "get_OrdinalIgnoreCase":
                if (count == 0)
                    return I("il2cpp::il2cpp_string_comparer_ordinal_ignore_case", 0, true, true, false,
                        returnTypeName: "System.StringComparer.OrdinalIgnoreCase");
                return null;
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapComparer -- D-ICMP-2 (icomparedeep): System.Collections.Comparer,
    // the non-generic DEFAULT comparer. The static get_Default / get_Default
    // Invariant map to the runtime singleton getter (arraysort.h
    // il2cpp_comparer_default); its result is tracked under the pseudo-type
    // "System.Collections.Comparer" (ReturnTypeName) so the INSTANCE
    // Compare(object, object) callvirt resolves on the receiver hint. Compare
    // dispatches on the runtime argument types (null-first, string ordinal,
    // numeric/char/bool boxes, user IComparable fallback -- net48 Comparer
    // semantics).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapComparer(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_Default":
            case "get_DefaultInvariant":
                if (count == 0)
                    return I("il2cpp::il2cpp_comparer_default", 0, true, true, false,
                        returnTypeName: "System.Collections.Comparer");
                return null;
            case "Compare":
                // instance int Compare(object, object): the receiver rides in
                // arg[0] ("" passthrough), the two compared objects follow.
                if (count == 2)
                    return I("il2cpp::il2cpp_comparer_compare", 2, true, false, true, new[] { "", "", "" });
                return null;
            default:
                return null;
        }
    }
}
