using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Nullable -- System.Nullable`1<T> member mappings (GN-2).
// Owners: System.Nullable`1
//
// The runtime models a Nullable<T> VALUE as an Il2CppObject* (null = no
// value, boxed T = a value -- the TypeEmitter M6 contract for Nullable
// fields); it has no generated C++ struct and no nullable icalls. The
// members therefore RETURN SYNTHETIC MARKERS that the CodeGenerator
// intercepts and inlines (the boxed-object semantics):
//   * .ctor              -> (*slot) = box T(value)
//   * get_HasValue       -> (obj != nullptr)
//   * get_Value          -> unbox T(require_value(obj))
//   * GetValueOrDefault  -> obj == nullptr ? default(T)/arg : unbox T(obj)
//   * ToString           -> obj == nullptr ? "" : icall_object_to_string(obj)
//   * Equals(object)     -> null-aware boxed equality
//   * GetHashCode        -> obj == nullptr ? 0 : icall_object_get_hash_code(obj)
// The markers are NOT runtime symbols -- they must never reach the C++ text
// as a call (the emitter special-cases them in the call emission paths;
// any leak would be an undefined-symbol link failure, so both the
// expression and statement emitters intercept them).
//
// `.ctor` reaches this table through a plain `call` (NOT newobj): csc emits
// `ldloca V; ldc.i4.5; call instance void Nullable`1<int32>::.ctor(!0)` for
// `int? nv = 5;`, so the newobj->box fold in StackFold.EmitNewObj never sees
// it. The receiver is the ldloca ADDRESS of the Il2CppObject* slot.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    // md-aware variant: the underlying T (the declaring TypeSpec's single
    // generic argument) decides the marker's ResultKind so the result temp is
    // declared with the right C++ slot (int64_t / double / Il2CppDecimal /
    // Il2CppDateTime / Il2CppGuid / the user struct). Called from
    // ResolveMemberReference BEFORE the generic BCL dispatch.
    internal static ResolvedMethod? TryMapNullableMember(MetadataReader md, EntityHandle parent, string name, int argCount)
    {
        if (parent.Kind != HandleKind.TypeSpecification) return null;
        var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
        if (sig.ReadByte() != 0x15) return null; // not GENERICINST
        sig.ReadByte(); // CLASS(0x12) / VALUETYPE(0x11) marker
        var def = sig.ReadTypeHandle();
        var defName = def.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)def),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)def),
            _ => null,
        };
        if (defName != "System.Nullable`1") return null;
        if (sig.ReadCompressedInteger() != 1) return null;
        var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        var t = b switch
        {
            0x02 => "System.Boolean",
            0x03 => "System.Char",
            0x04 => "System.SByte",
            0x05 => "System.Byte",
            0x06 => "System.Int16",
            0x07 => "System.UInt16",
            0x08 => "System.Int32",
            0x09 => "System.UInt32",
            0x0A => "System.Int64",
            0x0B => "System.UInt64",
            0x0C => "System.Single",
            0x0D => "System.Double",
            0x11 or 0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
            _ => null,
        };
        if (t == null) return null;
        // Value-typed results carry the slot kind the declaration site needs.
        // The "us:" prefix (not "dec"/"guid") keeps these results on the plain
        // assignment path: the bare "dec"/"guid" kinds mean "the icall writes
        // through the result slot", which the nullable helpers do NOT do (they
        // RETURN the value).
        var kind = t switch
        {
            "System.Int64" or "System.UInt64" => "l",
            "System.Double" or "System.Single" => "d",
            "System.Decimal" => "us:il2cpp::runtime::Il2CppDecimal",
            "System.DateTime" => "dt",
            "System.TimeSpan" => "ts",
            "System.Guid" => "us:il2cpp::Il2CppGuid",
            _ => null as string,
        };
        if (kind == null && !t.StartsWith("System.", StringComparison.Ordinal))
            kind = "us:" + TypeEmitter.Sanitize(t);
        // ResolvedMethod(CppName, ParamCount, IsConsoleWriteLine, ReturnsValue,
        // ReturnsRef, ParamIsRef, IsInstance, ...) -- every nullable member is
        // an INSTANCE member whose receiver is the slot ADDRESS.
        switch (name)
        {
            case ".ctor":
                return new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_ctor", 1, false, false, false,
                    new[] { false, false }, true, ParamTypeNames: new[] { "", "" });
            case "get_HasValue":
                return new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_has_value", 0, false, true, false,
                    new[] { false }, true);
            case "get_Value":
                return new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_get_value", 0, false, true, false,
                    new[] { false }, true, ResultKind: kind);
            case "GetValueOrDefault":
                return argCount == 0
                    ? new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_get_value_or_default", 0, false, true, false,
                        new[] { false }, true, ResultKind: kind)
                    : new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_get_value_or_default_arg", 1, false, true, false,
                        new[] { false, false }, true, ResultKind: kind);
            case "ToString":
                return new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_to_string", 0, false, true, true,
                    new[] { false }, true, ReturnTypeName: "System.String");
            case "Equals":
                return new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_equals", 1, false, true, false,
                    new[] { false, false }, true);
            case "GetHashCode":
                return new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_get_hash_code", 0, false, true, false,
                    new[] { false }, true);
            default:
                return null;
        }
    }

    private static ResolvedMethod? TryMapNullable(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // .ctor(T value): instance ctor, one explicit param, void return.
            // The emitter boxes the argument into the slot the receiver
            // addresses.
            case ".ctor":
                if (count == 1)
                    return I("il2cpp::runtime::il2cpp_nullable_ctor", 1, false, false, true);
                break;
            // get_HasValue(): instance getter, receiver-only (0 explicit params),
            // returns bool/int32. ParamCount=0 = the fold pops only the receiver
            // (the call-path instance branch pops this separately).
            case "get_HasValue":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_nullable_has_value", 0, true, false, true);
                break;
            // get_Value(): instance getter, receiver-only, returns the underlying T.
            // Throws InvalidOperationException on the no-value path (net48).
            case "get_Value":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_nullable_get_value", 0, true, false, true);
                break;
            // GetValueOrDefault() / GetValueOrDefault(T defaultValue).
            case "GetValueOrDefault":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_nullable_get_value_or_default", 0, true, false, true);
                if (count == 1)
                    return I("il2cpp::runtime::il2cpp_nullable_get_value_or_default_arg", 1, true, false, true);
                break;
            // Nullable<T>.ToString(): "" when no value, the value's text otherwise.
            case "ToString":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_nullable_to_string", 0, true, true, true);
                break;
            // Nullable<T>.Equals(object other).
            case "Equals":
                if (count == 1)
                    return I("il2cpp::runtime::il2cpp_nullable_equals", 1, true, false, true);
                break;
            // Nullable<T>.GetHashCode(): 0 when no value.
            case "GetHashCode":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_nullable_get_hash_code", 0, true, false, true);
                break;
            default:
                break;
        }
        return null;
    }
}
