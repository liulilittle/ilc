namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.GenericComparer -- the G6 default generic comparer singletons
// (genericcomparer_obj.h). Owners: System.Collections.Generic.EqualityComparer`1,
// System.Collections.Generic.Comparer`1
//
// The DEFAULT instances are runtime SINGLETONS (the System.Collections.
// Comparer precedent in MethodMap.Collections.cs TryMapComparer): the static
// get_Default maps to the singleton getter and its result is TRACKED under the
// instance pseudo-type so the INSTANCE member call resolves on the receiver
// hint. Only the int / double instantiations are wired (caudit1); any other
// instantiation returns null -> generated-name fallback (loud at link).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapGenericEqualityComparer(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        var instTag = instName ?? receiverType;
        // W6 (T2): the remaining EqualityComparer instantiations ride the
        // singleton domain (genericcomparer_obj.h): long/double compare by
        // value; string compares ordinal; object/class compare by reference.
        // Exact-kind match only (shared table below).
        var eqKind = ExactEqKind(instTag);
        // User reference-type instantiation (the M3 GenericInstantiationName
        // encodes CLASS args by full name, e.g. "EqualityComparer`1_Prog/Box",
        // never the "class" marker above): get_Default is a STATIC call with
        // no receiver, so the instance IS the default -- route it to the
        // class singleton (reference identity, the net48
        // ObjectEqualityComparer contract). This fallback runs ONLY for
        // get_Default: an untracked Equals receiver may hold a CUSTOM
        // comparer whose override must never be silently replaced by
        // reference identity -- it stays P26 loud (StackFold caudit2
        // Fold_UntrackedComparerReceiver_DoesNotFold). User VALUE types
        // never reach get_Default here in practice -- StackFold's caudit2
        // fold rewrites their Equals first.
        var defaultKind = eqKind
            ?? (instTag != null && instTag.StartsWith("System.Collections.Generic.EqualityComparer`1_",
                StringComparison.Ordinal) ? "class" : null);
        switch (name)
        {
            case "get_Default":
                if (count != 0 || defaultKind == null) return null;
                return I($"il2cpp::il2cpp_equality_comparer_default_{defaultKind}", 0, true, true, false,
                    returnTypeName: $"System.Collections.Generic.EqualityComparer`1_{defaultKind}");
            case "Equals":
                if (count != 2) return null;
                // Resolve ONLY on the tracked receiver kind (the
                // Default-normalized "..._class"/"..._string"/... tag): the
                // MemberRef parent instName carries the RAW instantiation
                // ("..._Prog/Box") which cannot name the kind, and an
                // untracked receiver may hold a CUSTOM comparer whose
                // override must never be silently replaced -- it stays P26
                // loud (StackFold caudit2 Fold_UntrackedComparerReceiver).
                var rkind = ExactEqKind(receiverType);
                if (rkind == null) return null;
                return rkind switch
                {
                    "long" => I("il2cpp::runtime::icall_equality_comparer_long_equals", 2, true, false, true, new[] { "", "l", "l" }),
                    "double" => I("il2cpp::runtime::icall_equality_comparer_double_equals", 2, true, false, true, new[] { "", "d", "d" }),
                    _ => I($"il2cpp::runtime::icall_equality_comparer_{rkind}_equals", 2, true, false, true,
                        rkind == "int" ? new[] { "", "i", "i" } : new[] { "", "", "" }),
                };
            default:
                return null;
        }
    }

    // Exact-kind table shared by the get_Default tag match and the Equals
    // tracked-receiver match (no guessing: unknown/user tags yield null).
    private static string? ExactEqKind(string? tag) => tag switch
    {
        "System.Collections.Generic.EqualityComparer`1_int" => "int",
        "System.Collections.Generic.EqualityComparer`1_string" => "string",
        "System.Collections.Generic.EqualityComparer`1_long" => "long",
        "System.Collections.Generic.EqualityComparer`1_double" => "double",
        "System.Collections.Generic.EqualityComparer`1_object" => "object",
        "System.Collections.Generic.EqualityComparer`1_class" => "class",
        _ => null,
    };

    private static ResolvedMethod? TryMapGenericComparer(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string? instName = null)
    {
        var instTag = instName ?? receiverType;
        // W6 (T2): the remaining Comparer instantiations ride the singleton
        // domain: int/long compare numerically; string compares ordinal;
        // object dispatches IComparable (il2cpp_icomparable_compare_to).
        var cmpKind = instTag switch
        {
            "System.Collections.Generic.Comparer`1_int" => "int",
            "System.Collections.Generic.Comparer`1_double" => "double",
            "System.Collections.Generic.Comparer`1_string" => "string",
            "System.Collections.Generic.Comparer`1_long" => "long",
            "System.Collections.Generic.Comparer`1_object" => "object",
            _ => null,
        };
        switch (name)
        {
            case "get_Default":
                if (count != 0 || cmpKind == null) return null;
                return I($"il2cpp::il2cpp_comparer_default_{cmpKind}", 0, true, true, false,
                    returnTypeName: $"System.Collections.Generic.Comparer`1_{cmpKind}");
            case "Create":
                // nb9 (M4/M18, P0-2): Comparer<T>.Create(Comparison<T>) -- a
                // heap comparer wrapping the caller's delegate. net48's
                // returned object's Compare dispatches to that delegate, so the
                // wrapper class (and therefore the tracked receiver type / the
                // Compare ABI) must follow T.
                if (count == 1 && ps.Length == 1 && ps[0] == 0x15)
                {
                    if (cmpKind == null) return null;
                    return cmpKind switch
                    {
                        "int" => I("il2cpp::runtime::il2cpp_comparer_create_int", 1, true, true, false,
                            new[] { "" }, returnTypeName: "System.Collections.Generic.Comparer`1_int"),
                        "long" => I("il2cpp::runtime::il2cpp_comparer_create_long", 1, true, true, false,
                            new[] { "" }, returnTypeName: "System.Collections.Generic.Comparer`1_long"),
                        "double" => I("il2cpp::runtime::il2cpp_comparer_create_double", 1, true, true, false,
                            new[] { "" }, returnTypeName: "System.Collections.Generic.Comparer`1_double"),
                        _ => I("il2cpp::runtime::il2cpp_comparer_create_ref", 1, true, true, false,
                            new[] { "" }, returnTypeName: "System.Collections.Generic.Comparer`1_string"),
                    };
                }
                return null;
            case "Compare":
                if (count != 2 || cmpKind == null) return null;
                return cmpKind switch
                {
                    "int" => I("il2cpp::runtime::icall_comparer_int_compare", 2, true, false, true, new[] { "", "i", "i" }),
                    "long" => I("il2cpp::runtime::icall_comparer_long_compare", 2, true, false, true, new[] { "", "l", "l" }),
                    "double" => I("il2cpp::runtime::icall_comparer_double_compare", 2, true, false, true, new[] { "", "d", "d" }),
                    _ => I($"il2cpp::runtime::icall_comparer_{cmpKind}_compare", 2, true, false, true, new[] { "", "", "" }),
                };
            default:
                return null;
        }
    }
}
