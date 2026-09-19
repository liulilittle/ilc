namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.List -- System.Collections.Generic.List`1 (int32 AOT expansion)
// icall mappings (il2cpp_list_i4_*, see src/runtime/include/internal/listi4.h).
// Owners: System.Collections.Generic.List`1
//
// AOT generic expansion convention: only the List<int32> instantiation has a
// runtime icall domain. List<T> calls arrive as MemberRefs (NOT MethodSpecs --
// genArgs is always null), and the T-bearing parameters appear in the
// signature as ELEMENT_TYPE_VAR (0x13), NOT as the instantiated type byte
// (probe of genlist.exe: Add sig=20 01 01 13 00, get_Item sig=20 01 13 00 08
// with parent TypeSpec[15 12 ... 08]). The instantiation itself lives only in
// the TypeSpec parent, which TryMapList does not see -- so the int32-ness is
// enforced by:
//   1. the folded receiver instance name ("System.Collections.Generic.List`1_int",
//      when the fold tracked one); any other name -> LOUD,
//   2. the parameter COUNT (decoded reliably from the signature header),
//   3. the T-bearing parameter shape 0x13 (VAR) where one exists.
// A non-VAR T shape (a signature that cannot be the List`1 definition
// signature) -> LOUD. (Note: get_Item's RETURN type is T (0x13) -- its VAR
// index leaks into ps as a 0x00 artifact because RawSignature's ret handling
// does not consume a VAR-return index; get_Item therefore guards on count
// only. A List<string> instance selects the reference domain through the
// TypeSpec parent -- see the objReceiver classification below.)
//
// Instance methods: the receiver rides in a managed reference slot ("" cast);
// value parameters cast with "i". Method names are unique within List`1.
//
// Wired surface (D-LISTQ-1..5: 2026-08-12): .ctor(parameterless + capacity),
// get_Count / Clear / Add / get_Item / set_Item / RemoveAt / Insert / Remove /
// IndexOf / Contains / ToArray. Capacity ctor, set_Item, Insert, Remove and
// ToArray map to their listi4.cpp icalls (set_Item/Insert/Remove/ToArray were
// implemented in the runtime but unreachable -- no TryMapList arm; the
// capacity ctor runtime icall was added in the same batch).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapList(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        // The fold-tracked receiver instance name (when present) must be the int32 AOT
        // expansion name List`1_int. instName (the TypeSpec instantiation, when the
        // call's parent carries one) is authoritative -- it disambiguates the
        // FIRST pass too (a hint-less List<Exception> call must not silently
        // take the int32 arm); otherwise the tracked receiver name decides.
        // A null tag keeps the historical int32 mapping.
        var instTag = instName ?? receiverType;
        var i4Receiver = instTag is null or "System.Collections.Generic.List`1_int";
        // Reference-element instantiations ride the listobj reference domain
        // (listobj.h). The TypeSpec decoder names the element by its metadata
        // form: CLASS (0x12) -> "_class", STRING (0x0E) -> "_string",
        // OBJECT (0x1C) -> "_object", SZARRAY (0x1D) -> "_array". All four
        // are managed references sharing the Il2CppObject* slot ABI, so the
        // domain accepts every one of them (each pinned by a
        // Resolve_List_*_MapsToObj* test). Any other non-int instantiation
        // (a value-type element -- "_long"/"_valuetype"/nested) stays LOUD:
        // no runtime domain exists, and a silent fallback would emit wrong
        // element semantics.
        var objReceiver = instTag is "System.Collections.Generic.List`1_class"
            or "System.Collections.Generic.List`1_string"
            or "System.Collections.Generic.List`1_object"
            or "System.Collections.Generic.List`1_array";
        // G4: the System.Int64 instantiation rides the same listobj storage
        // through the *_long icall variants (box on entry / unbox on exit), so
        // value-touching members dispatch there while T-free members reuse the
        // object-domain entry points.
        var longReceiver = instTag is "System.Collections.Generic.List`1_long";
        // G4: the System.Double instantiation rides the same listobj storage
        // through the *_double icall variants (icall_box_double on entry /
        // icall_unbox_double on exit; the double carrier is ResultKind "d").
        var doubleReceiver = instTag is "System.Collections.Generic.List`1_double";
        // W5 (T2): the small-value instantiations ride the same listobj
        // storage through per-kind box/unbox forwarders (listobj.h), exactly
        // like the G4 long/double precedent. float crosses as the double
        // carrier ("d"); the int32-slot kinds cross as "i".
        var floatReceiver = instTag is "System.Collections.Generic.List`1_float";
        var byteLike = instTag is "System.Collections.Generic.List`1_bool"
            or "System.Collections.Generic.List`1_char"
            or "System.Collections.Generic.List`1_sbyte"
            or "System.Collections.Generic.List`1_byte"
            or "System.Collections.Generic.List`1_short"
            or "System.Collections.Generic.List`1_ushort"
            or "System.Collections.Generic.List`1_uint";
        // G4: a NESTED GenericInst element (e.g. List<List<int>>) is a managed
        // reference -- GenericInstantiationName encodes it as
        // "System.Collections.Generic.List`1_System.Collections.Generic.List`1_int"
        // (the fully nested suffix). It rides the listobj reference domain
        // exactly like the simple reference elements; the element type name
        // (the suffix after the first '_') is tracked on the get_Item result
        // so downstream member calls on the inner list resolve.
        var nestedReceiver = instTag != null
            && instTag.StartsWith("System.Collections.Generic.List`1_System.Collections.Generic.List", StringComparison.Ordinal);
        if (nestedReceiver) objReceiver = true;
        // A5/B1/B6 (myIList/baudit2/ilistException): user-class
        // instantiations ("List`1_System.Exception" /
        // "List`1_Cases.Baudit2.P") ride the SHARED listobj reference
        // domain -- every reference element shares the Il2CppObject* slot
        // ABI (the DottedRefArg kind rule, Queue dotted-tag precedent).
        // BCL struct full names stay LOUD (no per-type box domain).
        var dottedElem = DottedRefArg(instTag);
        if (dottedElem != null) objReceiver = true;
        ResolvedMethod Loud() => throw new NotSupportedException(
            $"List`1 member '{name}' is not the List<int32> AOT expansion (receiver '{receiverType ?? "<untracked>"}', ps [{BitConverter.ToString(ps)}]) -- only il2cpp_list_i4_* is wired");

        switch (name)
        {
            case ".ctor":
                // Parameterless ctor -> create (the runtime
                // create icall takes NO self and RETURNS the new object --
                // Queue/Stack/ArrayList convention; the newobj temp is
                // dropped). The reference instantiation creates through the
                // listobj domain.
                if (count == 0)
                    return objReceiver || longReceiver || doubleReceiver || floatReceiver || byteLike
                        ? I("il2cpp::il2cpp_list_obj_create", 0, true, true, true, new[] { "drop" })
                        : i4Receiver
                            ? I("il2cpp::il2cpp_list_i4_create", 0, true, true, true, new[] { "drop" })
                            : Loud();
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08 && i4Receiver)
                    return I("il2cpp::il2cpp_list_i4_create_with_capacity", 1, true, true, true, new[] { "drop", "i" });
                throw new NotSupportedException($"List`1 ctor with {count} arg(s) not wired (the runtime has only the parameterless il2cpp_list_i4_create and the capacity ctor)");
            case "get_Count":
                // ilistException: the reference instantiation reads the
                // element-count through the listobj domain (same value
                // contract -- Count is T-free).
                if (count != 0) return Loud();
                return objReceiver || longReceiver || doubleReceiver || floatReceiver || byteLike
                    ? I("il2cpp::il2cpp_list_obj_get_count", 0, true, false, true)
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_get_count", 0, true, false, true)
                        : Loud();
            case "Clear":
                // D-LISTOBJ-1: void Clear() is T-free, so the reference
                // instantiation simply routes to the listobj domain (the
                // element slots are cleared through write barriers there).
                if (count != 0) return Loud();
                return objReceiver || longReceiver || doubleReceiver || floatReceiver || byteLike
                    ? I("il2cpp::il2cpp_list_obj_clear", 0, false, false, true)
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_clear", 0, false, false, true)
                        : Loud();
            case "Add":
                // void Add(T): the single T parameter arrives as VAR (0x13).
                // The reference instantiation adds through the listobj domain
                // (the element rides as a managed reference, "" passthrough).
                if (count == 1 && ps.Length == 1 && ps[0] == 0x13)
                {
                    if (doubleReceiver)
                        return I("il2cpp::il2cpp_list_obj_add_double", 1, false, false, true, new[] { "", "d" });
                    if (longReceiver)
                        return I("il2cpp::il2cpp_list_obj_add_long", 1, false, false, true, new[] { "", "l" });
                    var valKindAdd = ListValKind(instTag);
                    if (valKindAdd != null)
                        return I($"il2cpp::il2cpp_list_obj_add_{valKindAdd}", 1, false, false, true,
                            new[] { "", valKindAdd == "float" ? "d" : "i" });
                    return objReceiver
                        ? I("il2cpp::il2cpp_list_obj_add", 1, false, false, true, new[] { "", "" })
                        : i4Receiver
                            ? I("il2cpp::il2cpp_list_i4_add", 1, false, false, true, new[] { "", "i" })
                            : Loud();
                }
                return Loud();
            case "get_Item":
                // T get_Item(int32): the return T (VAR) index leaks into ps
                // (RawSignature does not consume a VAR-return index), so the
                // shape check is count-only. The reference instantiation
                // returns the element as a managed reference (rr = true).
                if (count != 1) return Loud();
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_list_obj_get_item_double", 1, true, false, true, new[] { "", "i" }, resultKind: "d");
                if (longReceiver)
                    return I("il2cpp::il2cpp_list_obj_get_item_long", 1, true, false, true, new[] { "", "i" }, resultKind: "l");
                var valKindGet = ListValKind(instTag);
                if (valKindGet != null)
                    return valKindGet == "float"
                        ? I($"il2cpp::il2cpp_list_obj_get_item_{valKindGet}", 1, true, false, true, new[] { "", "i" }, resultKind: "d")
                        : I($"il2cpp::il2cpp_list_obj_get_item_{valKindGet}", 1, true, false, true, new[] { "", "i" });
                return (objReceiver)
                    ? I("il2cpp::il2cpp_list_obj_get_item", 1, true, true, true, new[] { "", "i" },
                        returnTypeName: nestedReceiver ? instTag!["System.Collections.Generic.List`1_".Length..] : dottedElem)
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_get_item", 1, true, false, true, new[] { "", "i" })
                        : Loud();
            case "set_Item":
                // D-LISTQ-1: void set_Item(int32 index, T value) -- the index
                // is T-free int32 (0x08) but the value is T and arrives as VAR
                // (0x13), like Add's value (the header's VAR convention); the
                // shape is 08-13 + the receiver gate. The reference
                // instantiation stores the managed reference through listobj.
                if (count != 2 || ps.Length != 2 || ps[0] != 0x08 || ps[1] != 0x13) return Loud();
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_list_obj_set_item_double", 2, false, false, true, new[] { "", "i", "d" });
                if (longReceiver)
                    return I("il2cpp::il2cpp_list_obj_set_item_long", 2, false, false, true, new[] { "", "i", "l" });
                var valKindSet = ListValKind(instTag);
                if (valKindSet != null)
                    return I($"il2cpp::il2cpp_list_obj_set_item_{valKindSet}", 2, false, false, true,
                        new[] { "", "i", valKindSet == "float" ? "d" : "i" });
                return objReceiver
                    ? I("il2cpp::il2cpp_list_obj_set_item", 2, false, false, true, new[] { "", "i", "" })
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_set_item", 2, false, false, true, new[] { "", "i", "i" })
                        : Loud();
            case "Insert":
                // D-LISTQ-2: void Insert(int32 index, T value) -- index is
                // int32 (0x08), value is T (0x13), exactly like set_Item.
                // D-LISTOBJ-2: the reference instantiation inserts a managed
                // reference value ("" passthrough), index stays int32.
                if (count != 2 || ps.Length != 2 || ps[0] != 0x08 || ps[1] != 0x13) return Loud();
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_list_obj_insert_double", 2, false, false, true, new[] { "", "i", "d" });
                if (longReceiver)
                    return I("il2cpp::il2cpp_list_obj_insert_long", 2, false, false, true, new[] { "", "i", "l" });
                var valKindInsert = ListValKind(instTag);
                if (valKindInsert != null)
                    return I($"il2cpp::il2cpp_list_obj_insert_{valKindInsert}", 2, false, false, true,
                        new[] { "", "i", valKindInsert == "float" ? "d" : "i" });
                return objReceiver
                    ? I("il2cpp::il2cpp_list_obj_insert", 2, false, false, true, new[] { "", "i", "" })
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_insert", 2, false, false, true, new[] { "", "i", "i" })
                        : Loud();
            case "Remove":
                // D-LISTQ-3 / D-LISTOBJ-3: bool Remove(T value) -- the value
                // parameter arrives as VAR (0x13), like Add/IndexOf/Contains.
                // bool is a value return, so returnsRef stays false on both
                // domains; only the value cast differs ("i" vs "").
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_list_obj_remove_double", 1, true, false, true, new[] { "", "d" });
                if (longReceiver)
                    return I("il2cpp::il2cpp_list_obj_remove_long", 1, true, false, true, new[] { "", "l" });
                var valKindRemove = ListValKind(instTag);
                if (valKindRemove != null)
                    return I($"il2cpp::il2cpp_list_obj_remove_{valKindRemove}", 1, true, false, true,
                        new[] { "", valKindRemove == "float" ? "d" : "i" });
                return objReceiver
                    ? I("il2cpp::il2cpp_list_obj_remove", 1, true, false, true, new[] { "", "" })
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_remove", 1, true, false, true, new[] { "", "i" })
                        : Loud();
            case "ToArray":
                // D-LISTQ-4: T[] ToArray() -- count-only guard (the SZARRAY
                // return's element byte is consumed by RawSignature). The
                // result is tracked as "System.Int32[]" so a downstream
                // Length/indexer/foreach on the array resolves (linq ToArray
                // precedent).
                // D-LISTOBJ-4: the reference instantiation materialises an
                // object array, tracked as "System.Object[]" so a downstream
                // Length/indexer/foreach resolves.
                if (count != 0) return Loud();
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_list_obj_to_array_double", 0, true, true, true, returnTypeName: "System.Double[]");
                if (longReceiver)
                    return I("il2cpp::il2cpp_list_obj_to_array_long", 0, true, true, true, returnTypeName: "System.Int64[]");
                var valKindToArray = ListValKind(instTag);
                if (valKindToArray != null)
                    return I($"il2cpp::il2cpp_list_obj_to_array_{valKindToArray}", 0, true, true, true,
                        returnTypeName: ListValToArrayType(valKindToArray));
                return objReceiver
                    ? I("il2cpp::il2cpp_list_obj_to_array", 0, true, true, true, returnTypeName: "System.Object[]")
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_to_array", 0, true, true, true, returnTypeName: "System.Int32[]")
                        : Loud();
            case "RemoveAt":
                // D-LISTOBJ-5: void RemoveAt(int32) -- the index parameter is
                // plain int32 on both domains.
                if (count != 1 || ps.Length != 1 || ps[0] != 0x08) return Loud();
                return objReceiver || longReceiver || doubleReceiver || floatReceiver || byteLike
                    ? I("il2cpp::il2cpp_list_obj_remove_at", 1, false, false, true, new[] { "", "i" })
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_remove_at", 1, false, false, true, new[] { "", "i" })
                        : Loud();
            case "IndexOf":
                // D-LISTOBJ-6: int32 IndexOf(T) -- the T parameter arrives as
                // VAR (0x13). int32 is a value return on both domains.
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_list_obj_index_of_double", 1, true, false, true, new[] { "", "d" });
                if (longReceiver)
                    return I("il2cpp::il2cpp_list_obj_index_of_long", 1, true, false, true, new[] { "", "l" });
                var valKindIndexOf = ListValKind(instTag);
                if (valKindIndexOf != null)
                    return I($"il2cpp::il2cpp_list_obj_index_of_{valKindIndexOf}", 1, true, false, true,
                        new[] { "", valKindIndexOf == "float" ? "d" : "i" });
                return objReceiver
                    ? I("il2cpp::il2cpp_list_obj_index_of", 1, true, false, true, new[] { "", "" })
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_index_of", 1, true, false, true, new[] { "", "i" })
                        : Loud();
            case "Contains":
                // D-LISTOBJ-7: bool Contains(T) -- same VAR shape as IndexOf.
                if (count != 1 || ps.Length != 1 || ps[0] != 0x13) return Loud();
                if (doubleReceiver)
                    return I("il2cpp::il2cpp_list_obj_contains_double", 1, true, false, true, new[] { "", "d" });
                if (longReceiver)
                    return I("il2cpp::il2cpp_list_obj_contains_long", 1, true, false, true, new[] { "", "l" });
                var valKindContains = ListValKind(instTag);
                if (valKindContains != null)
                    return I($"il2cpp::il2cpp_list_obj_contains_{valKindContains}", 1, true, false, true,
                        new[] { "", valKindContains == "float" ? "d" : "i" });
                return objReceiver
                    ? I("il2cpp::il2cpp_list_obj_contains", 1, true, false, true, new[] { "", "" })
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_contains", 1, true, false, true, new[] { "", "i" })
                        : Loud();
            case "GetEnumerator":
                // GetEnumerator() -> snapshot enumerator (Il2CppArrayEnumerator).
                // ReturnTypeName tracks it so the enumerator member dispatch in
                // MethodMap.Collections.cs resolves MoveNext/get_Current/Dispose.
                // D-LISTOBJ-8: the reference instantiation snapshots into the
                // same Il2CppArrayEnumerator object, but its get_Current reads
                // a REFERENCE element (the i4 enumerator reads int32), so the
                // tracked pseudo-type carries the element form
                // (ListEnumeratorTrackedType) and the dispatch picks the
                // listobj enumerator icalls.
                if (count != 0) return Loud();
                return objReceiver || longReceiver || doubleReceiver || floatReceiver || byteLike
                    ? I("il2cpp::il2cpp_list_obj_get_enumerator", 0, true, true, true,
                        returnTypeName: ListEnumeratorTrackedType(instTag))
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_get_enumerator", 0, true, true, true,
                            returnTypeName: "System.Collections.Generic.List`1.Enumerator")
                        : Loud();
            case "Sort":
                // D-LISTOBJ-9: void Sort() -- ascending in-place sort
                // [0, count). NOTE: the listobj runtime domain sorts by
                // IComparable; see listobj.cpp (the int domain uses the
                // numeric listi4 sort).
                if (count != 0) return Loud();
                return objReceiver || longReceiver || doubleReceiver || floatReceiver || byteLike
                    ? I("il2cpp::il2cpp_list_obj_sort", 0, false, false, true)
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_sort", 0, false, false, true)
                        : Loud();
            case "CopyTo":
                // D-LISTOBJ-10: void CopyTo(T[] array, int32 arrayIndex) --
                // the array is a reference ("" passthrough), the index is
                // int32 ("i") on both domains.
                if (count != 2) return Loud();
                return objReceiver
                    ? I("il2cpp::il2cpp_list_obj_copy_to", 2, false, false, true, new[] { "", "", "i" })
                    : i4Receiver
                        ? I("il2cpp::il2cpp_list_i4_copy_to", 2, false, false, true, new[] { "", "", "i" })
                        : Loud();
            default: return null;
        }
    }

    // The listobj element tracking: the element metadata suffix
    // ("class"/"string"/"object"/"array") -> the managed type name downstream
    // member resolution can dispatch on. "_class" keeps the historical
    // System.Exception hint (the ilistException consumer).
    private static string? ListObjElementTypeName(string? suffix) => suffix switch
    {
        "class" => "System.Exception",
        "string" => "System.String",
        "object" => "System.Object",
        "array" => "System.Object[]",
        _ => null,
    };

    // W5: the per-kind runtime suffix for a small-value instantiation
    // ("float"/"bool"/...), or null when the tag is not a W5 kind. The suffix
    // selects the il2cpp_list_obj_*_<kind> forwarder family (listobj.h).
    private static string? ListValKind(string? instTag) => instTag switch
    {
        "System.Collections.Generic.List`1_float" => "float",
        "System.Collections.Generic.List`1_bool" => "bool",
        "System.Collections.Generic.List`1_char" => "char",
        "System.Collections.Generic.List`1_sbyte" => "sbyte",
        "System.Collections.Generic.List`1_byte" => "byte",
        "System.Collections.Generic.List`1_short" => "short",
        "System.Collections.Generic.List`1_ushort" => "ushort",
        "System.Collections.Generic.List`1_uint" => "uint",
        _ => null,
    };

    // W5: the ToArray tracking name for a value kind (the materialised typed
    // array element, downstream Length/indexer resolution).
    private static string? ListValToArrayType(string valKind) => valKind switch
    {
        "float" => "System.Single[]",
        "bool" => "System.Boolean[]",
        "char" => "System.Char[]",
        "sbyte" => "System.SByte[]",
        "byte" => "System.Byte[]",
        "short" => "System.Int16[]",
        "ushort" => "System.UInt16[]",
        "uint" => "System.UInt32[]",
        _ => null,
    };

    // Enumerator receiver pseudo-type for a reference-element instantiation:
    // the element form rides the suffix so the enumerator-member dispatch
    // (MethodMap.Collections.cs TryMapListEnumerator) selects the listobj
    // enumerator icalls (get_Current returns a managed reference). The int32
    // expansion keeps the historical
    // "System.Collections.Generic.List`1.Enumerator".
    private static string? ListEnumeratorTrackedType(string? instTag) => instTag switch
    {
        "System.Collections.Generic.List`1_class" => "System.Collections.Generic.List`1.Enumerator_class",
        "System.Collections.Generic.List`1_string" => "System.Collections.Generic.List`1.Enumerator_string",
        "System.Collections.Generic.List`1_object" => "System.Collections.Generic.List`1.Enumerator_object",
        "System.Collections.Generic.List`1_array" => "System.Collections.Generic.List`1.Enumerator_array",
        "System.Collections.Generic.List`1_long" => "System.Collections.Generic.List`1.Enumerator_long",
        "System.Collections.Generic.List`1_double" => "System.Collections.Generic.List`1.Enumerator_double",
        "System.Collections.Generic.List`1_float" => "System.Collections.Generic.List`1.Enumerator_float",
        "System.Collections.Generic.List`1_bool" => "System.Collections.Generic.List`1.Enumerator_bool",
        "System.Collections.Generic.List`1_char" => "System.Collections.Generic.List`1.Enumerator_char",
        "System.Collections.Generic.List`1_sbyte" => "System.Collections.Generic.List`1.Enumerator_sbyte",
        "System.Collections.Generic.List`1_byte" => "System.Collections.Generic.List`1.Enumerator_byte",
        "System.Collections.Generic.List`1_short" => "System.Collections.Generic.List`1.Enumerator_short",
        "System.Collections.Generic.List`1_ushort" => "System.Collections.Generic.List`1.Enumerator_ushort",
        "System.Collections.Generic.List`1_uint" => "System.Collections.Generic.List`1.Enumerator_uint",
        _ => null,
    };

    //--------------------------------------------------------------------------
    // TryMapListInterface -- ilistException: the GENERIC list interface owners
    // with a tracked List receiver. IList`1.get_Item is the indexer the C#
    // compiler emits for `ilist[i]` on an interface-typed local (the concrete
    // List`1.get_Item arm never sees it -- the declaring owner differs);
    // ICollection`1.get_Count is the Count property getter on the same path.
    // The receiver instantiation selects the carrier: List`1_int -> the i4
    // value arms, the reference instantiations (listobj domain -- the same
    // "_class"/"_string"/"_object"/"_array" naming as TryMapList's
    // objReceiver) -> the listobj reference arms. The tracked element type
    // follows the suffix: "_string" -> System.String, "_object" ->
    // System.Object, "_array" -> System.Object[]; "_class" keeps the
    // historical System.Exception hint (the ilistException consumer). A null
    // (first-pass) or foreign receiver DEFERS (null -- the TextReader/sldeep
    // precedent), so a genuinely unknown shape stays loud at final resolution
    // instead of mis-mapping.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapListInterface(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (receiverType == null) return null;
        var isI4 = receiverType == "System.Collections.Generic.List`1_int";
        var objElemType = receiverType switch
        {
            "System.Collections.Generic.List`1_class" => "System.Exception",
            "System.Collections.Generic.List`1_string" => "System.String",
            "System.Collections.Generic.List`1_object" => "System.Object",
            "System.Collections.Generic.List`1_array" => "System.Object[]",
            _ => null,
        };
        // B6 (ilistException): a dotted user-class receiver
        // ("List`1_System.Exception") tracks its exact element type --
        // downstream member calls (e.g. Exception.get_Message) resolve on
        // it. BCL struct full names stay untracked (LOUD downstream).
        objElemType ??= DottedRefArg(receiverType);
        var isObj = objElemType != null;
        if (!isI4 && !isObj) return null;
        if (owner == "System.Collections.Generic.IList`1" && name == "get_Item" && count == 1)
            return isObj
                ? I("il2cpp::il2cpp_list_obj_get_item", 1, true, true, true, new[] { "", "i" }, returnTypeName: objElemType)
                : I("il2cpp::il2cpp_list_i4_get_item", 1, true, false, true, new[] { "", "i" });
        if (owner == "System.Collections.Generic.ICollection`1" && name == "get_Count" && count == 0)
            return isObj
                ? I("il2cpp::il2cpp_list_obj_get_count", 0, true, false, true)
                : I("il2cpp::il2cpp_list_i4_get_count", 0, true, false, true);
        return null;
    }
}
