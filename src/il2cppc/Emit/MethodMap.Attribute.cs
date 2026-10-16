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
            // R4 (B3): the attributeType/inherit overloads are no longer
            // DROPPED -- the arity>=2 forms route to the net48-semantics
            // _ex_named primitives (attribute.cpp CollectAttributes: the
            // baseType-chain inheritance walk + attributeType assignability
            // filter + AttributeUsage rules). The element (arg0) rides as
            // the type-name string; attributeType (a Type, 0x12) rides as
            // its name string; inherit (a bool, 0x02) rides as int32.
            // Arity 1 keeps the no-filter _named primitives (byte-identical
            // behavior, no regression).
            // The _ex_named runtime signature is FIXED at (element,
            // attributeType, inherit) -- 3 slots. Arity-2 IL shapes fill the
            // missing slot via argOrder: (element, inherit) injects a nullptr
            // attributeType (slot literal -1), (element, attributeType)
            // injects inherit = 1 (trailing literal). The casts array is
            // SLOT-keyed (the emitter indexes ParamTypeNames by icall slot j,
            // CodeGenerator EmitIcallCallArgs "slot j reads casts2[j]").
            if (ps.Length >= 2)
            {
                cpp = name == "GetCustomAttributes"
                    ? "il2cpp::runtime::il2cpp_attribute_get_custom_ex_named"
                    : "il2cpp::runtime::il2cpp_attribute_get_single_ex_named";
            }
            int[]? argOrder = null;
            string[] casts;
            string[]? appendLiterals = null;
            if (ps.Length == 1)
            {
                casts = new[] { "" };
            }
            else if (ps.Length == 2 && ps[1] == 0x02)
            {
                // (element, inherit) -> (element, nullptr, inherit)
                argOrder = new[] { 0, -1, 1 };
                casts = new[] { "", "", "i" }; // slot 1 (nullptr) has no cast
            }
            else if (ps.Length == 2)
            {
                // (element, attributeType) -> (element, attributeType, 1)
                // net48 GetCustomAttributes(Type, Type) walks the chain
                // (inherit defaults to true); the trailing literal 1 is
                // appended after the IL argument list (appendLiterals).
                casts = new[] { "", "" };
                appendLiterals = new[] { "1" };
            }
            else
            {
                // (element, attributeType, inherit) -> identity
                casts = new[] { "", "", "i" };
            }
            return I(cpp, ps.Length, true, true, false, casts,
                returnTypeName: name == "GetCustomAttributes" ? "System.Attribute[]" : "System.Attribute",
                argOrder: argOrder, appendLiterals: appendLiterals);
        }
        // REFL3 (item 1): Attribute.IsDefined(...) -- the static existence
        // query. net48 member-element overloads:
        //   IsDefined(MemberInfo element, Type attributeType)        -- 2 args
        //   IsDefined(MemberInfo element, Type attributeType, bool)  -- 3 args
        // net48 default inherit for the 2-arg statform is TRUE (oracle-pinned:
        // the base-chain [Tag3A] on Attr3Derived answers True); the 3-arg form
        // carries the explicit bool. Both map to the managed-string existence
        // primitive il2cpp_attribute_is_defined_named(element, attributeType,
        // inherit) -- the exact _ex_named resolution + CollectAttributes walk.
        // In the simplified model the MemberInfo/Type element rides as its name
        // string (a Type VALUE full name for typeof(X) receivers), the same
        // carrier the get_custom_ex_named element slot consumes.
        if (name == "IsDefined" && ps.Length is 2 or 3 && ps[0] == 0x12)
        {
            if (ps.Length == 2 && ps[1] == 0x12)
            {
                // (element, attributeType) -> (element, attributeType, 1)
                return I("il2cpp::runtime::il2cpp_attribute_is_defined_named", 2, true, false, false,
                    new[] { "", "" }, appendLiterals: new[] { "1" });
            }
            if (ps.Length == 3 && ps[1] == 0x12 && ps[2] == 0x02)
            {
                // (element, attributeType, inherit) -> identity
                return I("il2cpp::runtime::il2cpp_attribute_is_defined_named", 3, true, false, false,
                    new[] { "", "", "i" });
            }
        }
        if (name == "IsDefined")
            throw new NotSupportedException(
                $"System.Attribute::IsDefined with {ps.Length} parameter(s) not wired in REFL3 -- the net48 member-element overloads IsDefined(MemberInfo, Type[, bool]) map to il2cpp_attribute_is_defined_named");
        // Known instance surface: there is no managed Attribute object model
        // in the runtime (instances are registry objects owned by the
        // generated code) -- LOUD, per the BCL wiring convention.
        if (name is "get_TypeId" or "Match")
            throw new NotSupportedException("instance Attribute members are not wired (attribute instances are registry objects without a managed model)");
        return null;
    }
}
