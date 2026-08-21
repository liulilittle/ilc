namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Attribute -- System.Attribute icall mappings (attribute.h).
// Owners: System.Attribute
//
// Query surface (simplified AOT model -- attribute.h): the static
// GetCustomAttribute/GetCustomAttributes overloads map to the runtime
// registry lookup il2cpp::runtime::il2cpp_attribute_get_custom. There is no
// metadata table at runtime: the transpiler registers a compile-time-built
// array of attribute OBJECTS per annotated type (il2cpp_attribute_register)
// and this query resolves the registry entry for the queried type.
//
// Semantics (documented simplifications, mirroring attribute.h):
//   - GetCustomAttributes() returns ALL registered attributes of the type;
//     the attributeType / inherit overloads are NOT modelled -- the second
//     and third arguments are DROPPED (they cannot change the registry
//     answer).
//   - GetCustomAttribute (singular) shares the same array primitive; the
//     .NET first-or-AmbiguousMatch semantics are a future runtime
//     refinement (the transpiler-side contract is the same lookup).
//   - The first argument is the queried element -- in the simplified Type
//     model a type IS its full NAME as a managed string (see
//     il2cpp_typeext.h); the registry key is the descriptor name, which the
//     type-name string carries.
//
// Forward ABI (why this is resolution-level today; the emitted C++ is
// currently unreachable -- typeof/GetTypeFromHandle is not emitted yet):
//   1. Ldtoken (IlDecoder.cs) has no fold support and System.Type has no
//      GetTypeFromHandle mapping (MethodMap.Type.cs) -- the query argument
//      cannot be produced by the pipeline yet.
//   2. The runtime primitive takes (Il2CppClass*, int32_t* outCount): the
//      emitter has no arg cast that expands a tracked type to
//      &{Type}_class and no out-count scratch-slot injection
//      (PrependCountArg/PrependZeroArg inject literals only).
//   3. The result is a raw Il2CppObject** (not an Il2CppArray): Length /
//      element access through the Array domain needs a future wrap helper.
//
// Register side -- how the transpiler should generate il2cpp_attribute_
// register calls for annotated types (scheme; cctor mechanism study):
//   a. Metadata: MetadataLoader must read the CustomAttribute table of each
//      TypeDefinition (System.Reflection.Metadata exposes
//      td.GetCustomAttributes()) -- ctor token + blob of fixed/named args.
//      Not loaded today (fields/methods only).
//   b. Attribute instances: an attribute class is a user type deriving
//      System.Attribute, hence a translated type: construct with
//      il2cpp_object_new(&{Attr}_class) + ctor call + field stores for the
//      blob's constant args; the array is emitted as a static
//      Il2CppObject* array next to the type descriptor.
//   c. Registration call sites (the two existing static-init mechanisms):
//      - Types WITH .cctor: inside ensure_cctor_{T} (CodeGenerator.cs),
//        right after the done-flag is set and BEFORE the .cctor body call --
//        once-only, before any user code runs.
//      - Types WITHOUT .cctor: in il2cpp_runtime_init next to
//        il2cpp::il2cpp_class_register(&{T}_class) (CodeGenerator.cs entry
//        emission) -- startup, before user code; the registry key
//        "{T.FullName}" is the same literal the class descriptor carries
//        (TypeEmitter.cs).
//      Both satisfy the attribute.h threading contract (single-threaded
//      startup registration, read-only afterwards, no lock).
//   d. Capacity: kMaxAttributeTypes = 32 (attribute.cpp) -- the transpiler
//      must count annotated types at compile time and LOUD-reject beyond
//      32 (the runtime's register returns 0 when full; the compile-time
//      count is the right place to surface it).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapAttribute(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (name is "GetCustomAttribute" or "GetCustomAttributes")
        {
            // D-ATTR-1 (2026-08-11) forward-ABI fix: the queries now emit the
            // runtime's MANAGED-STRING ABI (attribute.h), NOT the old
            // (Il2CppClass*, int32_t*) raw-primitive shape. The simplified
            // Type model (il2cpp_typeext.h assumption 1) represents a Type
            // VALUE as its full name string, so the element argument rides as
            // the type-name string -- exactly what il2cpp_attribute_get_custom_
            // named / get_single_named take (the attribute registry is keyed by
            // type NAME string; the same AsciiFromManagedString + name-keyed
            // lookup pattern as typeinfo.cpp). attributeType / inherit are
            // DROPPED (arity 1..3: (element[, attributeType][, inherit])) --
            // the registry answers only by the element's type name (documented
            // simplification, unchanged). GetCustomAttributes returns an
            // Il2CppArray* (net48: an EMPTY array, never null, for a type
            // without attributes); GetCustomAttribute (singular) returns the
            // first matching object or null. Both results are ref-typed, so the
            // generic emission wraps them as Il2CppObject* refs and .Length /
            // null-compare on them fold through the standard array/ref paths.
            var cpp = name == "GetCustomAttributes"
                ? "il2cpp::runtime::il2cpp_attribute_get_custom_named"
                : "il2cpp::runtime::il2cpp_attribute_get_single_named";
            var casts = new string[ps.Length];
            for (var i = 0; i < ps.Length; i++) casts[i] = i == 0 ? "" : "drop";
            return I(cpp, ps.Length, true, true, false, casts,
                returnTypeName: name == "GetCustomAttributes" ? "System.Attribute[]" : "System.Attribute");
        }
        // Known instance surface: there is no managed Attribute object model
        // in the runtime (instances are registry objects owned by the
        // generated code) -- LOUD, per the BCL wiring convention.
        if (name is "get_TypeId" or "Match")
            throw new NotSupportedException("instance Attribute members are not wired (attribute instances are registry objects without a managed model)");
        return null;
    }
}
