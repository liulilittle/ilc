using System.Reflection.Metadata;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Type -- System.Type icall mappings (il2cpp_typeext.h).
// Owners: System.Type
//
// Simplified Type model (il2cpp_typeext.h): a Type VALUE is the full type
// NAME as a managed string -- the same shape icall_object_get_type produces
// for GetType().ToString(). The typeext primitives CONSUME runtime
// descriptors (Il2CppClass* -- the address of the static {Type}_class
// descriptor the transpiler emits per translated type), the transpiler
// passes the descriptor where the IL holds a RuntimeTypeHandle/typeof
// value. GetTypeFromHandle converts a descriptor to the name string; the
// result is recorded as "System.Type" (ReturnTypeName -> the fold's
// objTypes) so later get_Name/GetTypeCode/Equals/IsAssignableFrom calls on
// it resolve -- the GetType()-linkage the fold already uses.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapType(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // Type.GetTypeFromHandle(RuntimeTypeHandle) -- static. The handle
            // IS the runtime descriptor address; the icall returns the type
            // NAME string (managed ref). ReturnTypeName records the result as
            // "System.Type" (same tracked shape as icall_object_get_type), so
            // .Name/.GetTypeCode/.Equals/.IsAssignableFrom on the result
            // resolve through this domain (the fold records it in objTypes).
            case "GetTypeFromHandle":
                return I("il2cpp::runtime::il2cpp_type_get_type_from_handle", 1, true, true, false,
                    new[] { "" }, returnTypeName: "System.Type");
            // Type.Name -- TWO paths (the Type-value model split):
            //   * RUNTIME receivers (a GetType() result -- the FULL-name
            //     string the runtime icall produces from klass->name, e.g.
            //     "System.Int32"): the runtime truncation primitive
            //     (il2cpp_type_get_short_name: "System.Int32" -> "Int32" --
            //     net48 Type.Name semantics). The old identity was wrong
            //     here: the runtime value is the FULL name and .NET Name is
            //     the SHORT name (the boxing2/boxdeep GetType mismatch).
            //   * FOLDED receivers (typeof(X) -- the full-name string the
            //     ldtoken fold produces): StackFold intercepts get_Name
            //     BEFORE this mapping and truncates to the SHORT name
            //     (net48: typeof(int).Name == "Int32").
            case "get_Name":
                return I("il2cpp::runtime::il2cpp_type_get_short_name", 0, true, true, true, returnTypeName: "System.String");
            // refl2 (item 5): Type.get_Namespace -- the FULL-name string
            // truncated at the LAST '.' (net48 Namespace of the outermost
            // declaring type; a global-namespace type -> null). The runtime
            // name-string wrapper (il2cpp_typeinfo_get_namespace) does the
            // truncation -- the pre-refl2 surface had no get_Namespace arm
            // (the receiver fell to the generated-member phantom).
            case "get_Namespace":
                return I("il2cpp::il2cpp_typeinfo_get_namespace", 0, true, true, true, returnTypeName: "System.String");
            // Type.GetTypeCode(Type) -- STATIC overload: int32 TypeCode from
            // the descriptor's name (net48 values, identical table to
            // icall_object_get_type_code). TWO paths:
            //   * FOLDED argument (typeof(X)): StackFold intercepts and
            //     folds the full-name string to the typecode CONSTANT
            //     (no call emitted -- the runtime primitive takes a
            //     DESCRIPTOR, not the name string).
            //   * RUNTIME argument: the descriptor-ABI icall is emitted
            //     (the name-string receiver does not compile -- the Type
            //     domain's documented runtime-path limitation).
            case "GetTypeCode":
                if (ps.Length == 1)
                    return I("il2cpp::runtime::il2cpp_type_get_typecode", 1, true, false, false, new[] { "" });
                // INSTANCE GetTypeCode() (IConvertible impl): the receiver is
                // the name-string Type value -- the runtime has no
                // string->TypeCode path (the static primitive takes a
                // descriptor); unwireable, must be LOUD.
                throw new NotSupportedException("Type.GetTypeCode() (instance) not wired in M5 -- the simplified Type model has no name-string to TypeCode path (static Type.GetTypeCode(Type) is wired)");
            // Type.IsAssignableFrom(Type c) -- instance: target = this,
            // source = c (runtime conformance model: identity, array prefix,
            // baseClass chain, interface scan). refl2: the receiver AND the
            // argument are name-string Type VALUES; the runtime wrapper
            // (il2cpp_typeinfo_is_assignable_from) resolves both names to
            // descriptors and delegates -- the pre-refl2 mapping passed the
            // receiver name-string straight to il2cpp_type_is_assignable_from
            // (Il2CppClass*) and failed to compile.
            case "IsAssignableFrom":
                return I("il2cpp::il2cpp_typeinfo_is_assignable_from", 1, true, false, true,
                    new[] { "", "" });
            // Type.Equals(object o) -- instance type identity (icall_type_equals:
            // descriptor identity fast path, else name equality). Wired only
            // for a TRACKED Type argument -- a non-Type argument cannot be
            // passed to the descriptor-taking icall (no .NET "false for
            // non-Type" support in the runtime).
            case "Equals":
                if (ps.Length == 1 && arg0Type == "System.Type")
                    return I("il2cpp::runtime::icall_type_equals", 1, true, false, true, new[] { "", "" });
                throw new NotSupportedException($"Type.Equals(object) with {(arg0Type == null ? "untracked" : $"non-Type argument {arg0Type}")} not wired in M5 -- only a tracked System.Type argument maps to icall_type_equals");
            // Type.GetProperty/GetMethod/GetField(string name) -- M5.5
            // typeinfo queries (see typeinfo.h managed-string ABI). The
            // receiver is the Type VALUE (the full-name string, same shape
            // as GetTypeFromHandle produces); the runtime resolves the type
            // through the typeinfo registry, scans the member table (own
            // table first, baseType chain second) and returns the MEMBER-NAME
            // string on a hit -- the simplified member model (the member
            // VALUE is its name string, like the Type model) -- or null
            // when the type is not reflected / the member is absent (.NET
            // "not found -> null" contract, no throw). ReturnTypeName
            // "System.String" keeps WriteLine(object)/Concat linkage working
            // on the result. Only the (string) overload is wired; the
            // BindingFlags overloads have no runtime support in M5.5.
            case "GetProperty":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_property", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                // GAP-1 (P0): Type.GetProperty(string, BindingFlags) -- the
                // runtime flags-aware query (Public/NonPublic/Instance/Static
                // filter). The BindingFlags enum rides the validation-stage
                // int32 value; the transpiler passes it through ("i").
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_property_flags", 2, true, true, true,
                        new[] { "", "", "i" }, returnTypeName: "System.String");
                // refl2 (R2-G-1): Type.GetProperty(string, Type[]) -- the
                // exact-signature indexer query. The Type[] argument is an
                // SZARRAY (0x1D) -- a REFERENCE array (rides as Il2CppArray*,
                // the "a" cast -- the GetConstructor (Type[]) precedent), NOT
                // the int32-riding BindingFlags enum (ps[1]==0x11 arm above).
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                    return I("il2cpp::il2cpp_typeinfo_get_property_types", 2, true, true, true,
                        new[] { "", "", "a" }, returnTypeName: "System.String");
                // refl5 (item 2): Type.GetProperty(string, Type returnType) --
                // the net48 arity-2 return-type disambiguator (there is NO
                // (string, Type[], Type[]) 3-param form -- refl4 note; the
                // 3-param overload is (string, Type, Type[])). The Type
                // argument is a CLASS (0x12) already folded to a Type VALUE
                // string; the runtime matches by property-type equality.
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x12)
                    return I("il2cpp::il2cpp_typeinfo_get_property_return_type", 2, true, true, true,
                        new[] { "", "", "" }, returnTypeName: "System.String");
                // refl5 (item 2): Type.GetProperty(string, Type returnType,
                // Type[] types) -- name + return type + indexer params.
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x12 && ps[2] == 0x1D)
                    return I("il2cpp::il2cpp_typeinfo_get_property_types_ret", 3, true, true, true,
                        new[] { "", "", "", "a" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetProperty with {ps.Length} parameter(s) not wired in M5.5/refl5 -- the (string) / (string, BindingFlags) / (string, Type[]) / (string, Type) / (string, Type, Type[]) overloads map to il2cpp_typeinfo_get_property(_flags/_types/_return_type/_types_ret)");
            case "GetMethod":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_method", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                // GAP-1 (P0) / refl2: the two 2-parameter overloads are SPLIT
                // on ps[1] -- BindingFlags (0x11, the int32-riding enum) vs
                // Type[] (0x1D, SZARRAY). The pre-refl2 code matched BOTH with
                // the flags arm, so a Type[] argument was static_cast<int32_t>
                // (the reflected array -> int C++ error).
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_method_flags", 2, true, true, true,
                        new[] { "", "", "i" }, returnTypeName: "System.String");
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x1D)
                    return I("il2cpp::il2cpp_typeinfo_get_method_types", 2, true, true, true,
                        new[] { "", "", "a" }, returnTypeName: "System.String");
                // refl5 (item 2): Type.GetMethod(string, Type[],
                // ParameterModifier[]) -- reuse the exact-signature matcher
                // (parameter modifiers are ignored by net48's match, W7; the
                // modifier ARGUMENT is dropped from the emitted icall call).
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x1D && ps[2] == 0x1D)
                    return I("il2cpp::il2cpp_typeinfo_get_method_types", 3, true, true, true,
                        new[] { "", "", "a", "drop" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetMethod with {ps.Length} parameter(s) not wired in M5.5/refl5 -- the (string) / (string, BindingFlags) / (string, Type[]) / (string, Type[], ParameterModifier[]) overloads map to il2cpp_typeinfo_get_method(_flags/_types)");
            case "GetField":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_field", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                if (ps.Length == 2 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_field_flags", 2, true, true, true,
                        new[] { "", "", "i" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetField with {ps.Length} parameter(s) not wired in M5.5 -- the (string) / (string, BindingFlags) overloads map to il2cpp_typeinfo_get_field(_flags)");
            // R2 (B2): member ENUMERATION -- GetMethods()/GetFields()/
            // GetProperties()/GetConstructors()/GetInterfaces(). Each maps to
            // a runtime icall that materialises an Il2CppArray* of member-NAME
            // strings (the simplified member model -- a member VALUE is its
            // name string, typeinfo.h). ReturnTypeName "...[]" keeps the
            // Array-domain folds (.Length / element read / foreach) resolved.
            // Only the 0-param overloads are wired; the BindingFlags overloads
            // have no runtime support and fail LOUD.
            case "GetMethods":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_methods", 0, true, true, true,
                        returnTypeName: "System.String[]");
                // refl5 (item 1): GetMethods(BindingFlags) -- the arity-1
                // filtered enumeration (net48 no-FlattenHierarchy semantics:
                // inherited static/private base members excluded, own table +
                // base chain public/protected/internal instance members).
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_methods_flags", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetMethods with {ps.Length} parameter(s) not wired in R2/refl5 -- the () overload maps to il2cpp_typeinfo_get_methods; the (BindingFlags) overload to il2cpp_typeinfo_get_methods_flags");
            case "GetFields":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_fields", 0, true, true, true,
                        returnTypeName: "System.String[]");
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_fields_flags", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetFields with {ps.Length} parameter(s) not wired in R2/refl5 -- the () overload maps to il2cpp_typeinfo_get_fields; the (BindingFlags) overload to il2cpp_typeinfo_get_fields_flags");
            case "GetProperties":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_properties", 0, true, true, true,
                        returnTypeName: "System.String[]");
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_properties_flags", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetProperties with {ps.Length} parameter(s) not wired in R2/refl5 -- the () overload maps to il2cpp_typeinfo_get_properties; the (BindingFlags) overload to il2cpp_typeinfo_get_properties_flags");
            case "GetConstructors":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_constructors", 0, true, true, true,
                        returnTypeName: "System.String[]");
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_constructors_flags", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetConstructors with {ps.Length} parameter(s) not wired in R2/refl5 -- the () overload maps to il2cpp_typeinfo_get_constructors; the (BindingFlags) overload to il2cpp_typeinfo_get_constructors_flags");
            // GAP-5 (P1): Type.GetConstructor(Type[]) -- the single-ctor query
            // by arity. The receiver is the Type VALUE (full-name string);
            // the runtime matches a PUBLIC ctor of the types array length and
            // returns the DISAMBIGUATED ctor member value
            // "<FullName>::.ctor" (the carrier ConstructorInfo.Invoke
            // resolves) or null. ReturnTypeName "System.Reflection.
            // ConstructorInfo" keeps the downstream Invoke receiver tracked.
            case "GetConstructor":
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::il2cpp_typeinfo_get_constructor", 1, true, true, true,
                        new[] { "", "a" }, returnTypeName: "System.Reflection.ConstructorInfo");
                if (ps.Length == 1 && ps[0] == 0x0E)
                    throw new NotSupportedException("Type.GetConstructor(string) does not exist in net48 (the ctor name overload is .NET Core-only)");
                throw new NotSupportedException($"Type.GetConstructor with {ps.Length} parameter(s) not wired in GAP-5 -- only the (Type[]) overload maps to il2cpp_typeinfo_get_constructor");
            case "GetInterfaces":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_interfaces", 0, true, true, true,
                        returnTypeName: "System.Type[]");
                throw new NotSupportedException($"Type.GetInterfaces with {ps.Length} parameter(s) not wired in R2 -- only the () overload maps to il2cpp_typeinfo_get_interfaces");
            // refl2 (item 6): Type.GetInterface(string[, bool]) -- the single-
            // interface query by FULL or SIMPLE name across the type's own +
            // base chain. The receiver is the Type VALUE (name string); the
            // runtime wrapper matches the name against the interface full/simple
            // name (net48 full-name compare is always case-sensitive, the
            // simple-name compare follows ignoreCase) and returns the interface
            // FULL-NAME string (a Type VALUE) or null on a miss.
            case "GetInterface":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_interface", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.Type", appendZero: true);
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x02)
                    return I("il2cpp::il2cpp_typeinfo_get_interface", 2, true, true, true,
                        new[] { "", "", "i" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetInterface with {ps.Length} parameter(s) not wired in refl2 -- only the (string) / (string, bool) overloads map to il2cpp_typeinfo_get_interface");
            // RF-N5 (NEXT2-DECISION section ② RF-N5): GetNestedTypes() -- the
            // typeinfo's nested sub-table (own nested types only, metadata
            // declaration order; net48 never inherits nested types). Each
            // element is the nested type's REFLECTION full name ("Cases.Outer+
            // Inner" -- the '+' form), i.e. a Type VALUE (full-name string)
            // like GetInterfaces/GetGenericArguments -- ReturnTypeName
            // "System.Type[]" keeps the Array-domain folds (get_FullName on
            // the elements) resolved.
            case "GetNestedTypes":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_nested_types", 0, true, true, true,
                        returnTypeName: "System.Type[]");
                throw new NotSupportedException($"Type.GetNestedTypes with {ps.Length} parameter(s) not wired in RF-N5 -- only the () overload maps to il2cpp_typeinfo_get_nested_types");
            // RF-N5 (NEXT2-DECISION section ② RF-N5): GetMembers() -- the
            // five-enumeration union (the runtime concats the public methods
            // WITH their property/event accessors -- net48 returns accessors
            // as method members -- then the public ctors, then properties,
            // then events, then fields; own + base chain). Elements are
            // member-NAME strings, the same carrier as GetMethods/GetFields,
            // so ReturnTypeName "System.String[]" keeps the Array-domain
            // folds (.Length / foreach / element read) resolved.
            case "GetMembers":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_members", 0, true, true, true,
                        returnTypeName: "System.String[]");
                // refl5 (item 1): GetMembers(BindingFlags) -- the five-slice
                // union under the binding filter.
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_members_flags", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetMembers with {ps.Length} parameter(s) not wired in RF-N5/refl5 -- the () overload maps to il2cpp_typeinfo_get_members; the (BindingFlags) overload to il2cpp_typeinfo_get_members_flags");
            // refl6a (item B, probe p-refl6-gmember): Type.GetMember(string) /
            // GetMember(string, BindingFlags). ONE runtime filter icall
            // (il2cpp_typeinfo_get_members_flags_name) over the refl5
            // members_flags union + an exact SIMPLE-name strcmp (net48
            // GetMember matches by simple member name). net48 DEFAULT binding
            // for the 1-arg overload is Public|Instance|Static (28 = 16+4+8)
            // -- the transpiler appends the binding constant; the 2-arg
            // overload rides the BindingFlags int32 slot ("i"). Exception
            // contract lives in the runtime (null memberName ->
            // ArgumentNullException; empty memberName -> EMPTY array, no
            // throw). Elements are member-NAME strings (the same carrier as
            // GetMembers), so the probe's element .Name/.MemberType/
            // .DeclaringType resolve via the refl4/5 member arms.
            case "GetMember":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_members_flags_name", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String[]",
                        appendLiterals: new[] { "28" });
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_members_flags_name", 2, true, true, true,
                        new[] { "", "", "i" }, returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetMember with {ps.Length} parameter(s) not wired in refl6a -- only the (string) / (string, BindingFlags) overloads map to il2cpp_typeinfo_get_members_flags_name");
            // RF-N7 (NEXT2-DECISION section 2 RF-N7): Type.MakeGenericType
            // -- explicit LOUD. The runtime has no generic-argument fabrication
            // (no fabrication, the genericArgNames Plan B stays deferred) and no SRE-like
            // type loading, so the (Type[]) overload maps to a runtime stub that
            // ALWAYS throws NotSupportedException (il2cpp_makegenerictype_loud)
            // -- never a silent spelling generated-member name. Receiver =
            // Type VALUE (full-name string); the typeArgs array rides as a ref.
            case "MakeGenericType":
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::il2cpp_makegenerictype_loud", 1, true, true, true,
                        new[] { "", "a" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.MakeGenericType with {ps.Length} parameter(s) not wired in RF-N7 -- only the (Type[]) overload maps to the LOUD stub il2cpp_makegenerictype_loud");
            // C3-1 (COM-C3) / RF-N1: GetEvents() -- the event-table member
            // enumeration (own + base chain, ALL events -- Il2CppEventInfo has
            // no flags field, the documented simplification). The events
            // sub-table is the shared piece with the COM-C3 event metadata
            // pipeline (event name + add/remove accessors + the folded event
            // IID). ReturnTypeName "System.String[]" keeps the Array-domain
            // folds (.Length / element read / foreach) resolved.
            case "GetEvents":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_events", 0, true, true, true,
                        returnTypeName: "System.String[]");
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_typeinfo_get_events_flags", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetEvents with {ps.Length} parameter(s) not wired in C3-1/refl5 -- the () overload maps to il2cpp_typeinfo_get_events; the (BindingFlags) overload to il2cpp_typeinfo_get_events_flags");
            // C3-1 (COM-C3): Type.GetEvent(string) -- the single-event member
            // query (own + base chain), the M5.5 get_property/get_method/get_field
            // shape: the receiver is the Type VALUE (full-name string), the
            // runtime returns the EVENT-NAME string on a hit or null (the
            // simplified member model -- a member VALUE is its name string).
            case "GetEvent":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_event", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetEvent with {ps.Length} parameter(s) not wired in C3-1 -- only the (string) overload maps to il2cpp_typeinfo_get_event");
            // Type.op_Equality(Type, Type) -- the C# == operator (csc emits
            // `call`, never callvirt; the resolver's default op_* heuristic
            // classifies it INSTANCE and the call path would throw "pops this
            // on empty stack" -- delechain's op_Equality precedent). The
            // simplified Type model (il2cpp_typeext.h) makes a Type VALUE the
            // full-name string, so equality is STRING content equality -- the
            // same icall_string_equals_static the String domain uses. net48
            // reference semantics reduce to name equality (one RuntimeType
            // instance per type): typeof(int) == typeof(int) -> "System.Int32"
            // vs "System.Int32" -> True, typeof(int) == typeof(string) ->
            // "System.Int32" vs "System.String" -> False.
            case "op_Equality":
                if (ps.Length == 2)
                    return I("il2cpp::runtime::icall_string_equals_static", 2, true, false, false);
                throw new NotSupportedException($"Type.op_Equality with {ps.Length} arg(s) not wired in M5 -- only the 2-arg (Type, Type) overload maps to string equality");
            // P6: Type.GetTypeFromHandleUnsafe (stub: returns nullptr).
            case "GetTypeFromHandleUnsafe": return I("il2cpp::runtime::icall_type_get_type_from_handle_unsafe", 1, true, true, false, new[] { "i" });
            // P6: Type.op_Inequality (stub: returns 0).
            case "op_Inequality": return I("il2cpp::runtime::icall_type_op_inequality", 2, true, false, false);
            // Type.GetElementType() -- instance: the element type of an array type
            // (e.g., int[] -> int). The receiver is the Type value (name string);
            // the refl2 name-string wrapper strips the array '['-prefix and
            // returns the element's full name as the Type VALUE (or null for a
            // non-array -- net48 typeof(int).GetElementType() -> null).
            case "GetElementType":
                return I("il2cpp::il2cpp_typeinfo_get_element_type", 0, true, true, true, returnTypeName: "System.Type");
            // Type.FullName -- instance: the full ECMA name of the type (e.g.,
            // "System.Int32"). Contrast with get_Name (short name). The receiver
            // is the Type value (name string); the runtime returns the full name.
            case "get_FullName":
                // raudit3 (2026-09-13): the simplified Type VALUE is its
                // FULL-name string (typeof folds and il2cpp_type_get_type both
                // carry the canonical name), so FullName is the receiver
                // itself. The descriptor-taking il2cpp_type_get_full_name
                // (Il2CppClass*) cannot consume the string-model receiver --
                // raudit3 cmake: "cannot convert 'Il2CppObject*' to
                // 'Il2CppClass*'". Identity via the shared marker (the
                // CultureInfo.get_Name precedent, MethodMap.CultureInfo.cs).
                return new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true);
            // Type.IsInstanceOfType(object o) -- instance: whether the OBJECT
            // argument o is an instance of this type (the .NET isinst
            // conformance test). refl2: the receiver is the name-string Type
            // VALUE; the runtime wrapper resolves the name to a descriptor and
            // delegates to the typecheck conformance test (obj first, klass
            // second -- same signature as the descriptor primitive).
            case "IsInstanceOfType":
                return I("il2cpp::il2cpp_typeinfo_is_instance", 1, true, false, true,
                    new[] { "", "" }, argOrder: new[] { 1, 0 });
            // Type.IsSubclassOf(Type c) -- instance: whether this type derives
            // from c. refl2: BOTH the receiver and the argument are name-string
            // Type VALUES -- the wrapper resolves the names and delegates to the
            // subclass primitive (NOT-of-ITSELF semantics). The pre-refl2
            // mapping required a tracked System.Type IL argument (the typeof-
            // in-local shapes were untracked -> LOUD) AND passed the string to
            // the descriptor primitive (fail-to-compile); both are gone.
            case "IsSubclassOf":
                return I("il2cpp::il2cpp_typeinfo_is_subclass_of", 1, true, false, true,
                    new[] { "", "" });
            // A-class predicates (bool Type properties backed by existing
            // descriptor fields -- name / baseClass chain / instanceSize).
            // Each is a 0-arg instance getter returning int32 1/0. refl2:
            // the RECEIVER is the name-string Type VALUE (the simplified Type
            // model), so each maps to the refl2 NAME-STRING WRAPPER in
            // typeinfo.cpp (il2cpp_typeinfo_is_* / has_element) -- NOT the
            // descriptor primitives (il2cpp_type_is_* take Il2CppClass*, which
            // would not compile with a string receiver).
            case "get_IsArray":
                return I("il2cpp::il2cpp_typeinfo_is_array", 0, true, false, true);
            case "get_IsEnum":
                return I("il2cpp::il2cpp_typeinfo_is_enum", 0, true, false, true);
            // refl6a (item A, probe p-refl6-enum): the System.Type enum surface
            // -- GetEnumNames / GetEnumValues / GetEnumName / GetEnumUnderlying-
            // Type. All four are INSTANCE methods on the Type VALUE (the
            // full-name string -- the receiver flows to the icall's typeName
            // slot). Each reuses an EXISTING enumext name-string query icall
            // (documented enumext.h contract) through a zero-new-ABI typeinfo
            // wrapper that enforces net48's non-enum-receiver ArgumentException
            // BEFORE delegating (probe-pinned: typeof(int).GetEnum* throws
            // ArgumentException; the shared enum API keeps its unknown-name
            // fallback). GetEnumName(object value) additionally passes the
            // boxed value slot through unchanged.
            case "GetEnumNames":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_enum_names", 0, true, true, true,
                        returnTypeName: "System.String[]");
                throw new NotSupportedException($"Type.GetEnumNames with {ps.Length} parameter(s) not wired in refl6a -- only the () overload maps to il2cpp_typeinfo_get_enum_names");
            case "GetEnumValues":
                // ReturnTypeName "System.Array" keeps the returned value
                // array in the Array domain (.Length / GetValue(int) -- the
                // probe's `(int)(object)GetValue(i)` unbox shape). The
                // runtime array is int32[] (int-backed) / boxed-enum object[]
                // (wide), both Il2CppArray*.
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_enum_values", 0, true, true, true,
                        returnTypeName: "System.Array");
                throw new NotSupportedException($"Type.GetEnumValues with {ps.Length} parameter(s) not wired in refl6a -- only the () overload maps to il2cpp_typeinfo_get_enum_values");
            case "GetEnumName":
                // string GetEnumName(object value) -- the boxed value rides
                // as the second managed-ref slot (the enumext name-string
                // ABI).
                if (ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::il2cpp_typeinfo_get_enum_name", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetEnumName with {ps.Length} parameter(s) not wired in refl6a -- only the (object value) overload maps to il2cpp_typeinfo_get_enum_name");
            case "GetEnumUnderlyingType":
                // Type GetEnumUnderlyingType() -- returns a Type VALUE
                // (full-name string, e.g. "System.Int32" / "System.Int64").
                // ReturnTypeName "System.Type" keeps the Type domain (the
                // probe prints .FullName -- the identity fold).
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_enum_underlying_type", 0, true, true, true,
                        returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetEnumUnderlyingType with {ps.Length} parameter(s) not wired in refl6a -- only the () overload maps to il2cpp_typeinfo_get_enum_underlying_type");
            case "get_IsValueType":
                return I("il2cpp::il2cpp_typeinfo_is_value_type", 0, true, false, true);
            case "get_IsInterface":
                return I("il2cpp::il2cpp_typeinfo_is_interface", 0, true, false, true);
            case "get_IsClass":
                return I("il2cpp::il2cpp_typeinfo_is_class", 0, true, false, true);
            case "get_IsPrimitive":
                return I("il2cpp::il2cpp_typeinfo_is_primitive", 0, true, false, true);
            case "get_HasElementType":
                return I("il2cpp::il2cpp_typeinfo_has_element_type", 0, true, false, true);
            case "get_BaseType":
                return I("il2cpp::il2cpp_typeinfo_get_base_type", 0, true, true, true, returnTypeName: "System.Type");
            // RF-N4 -- generic-type identification (NEXT2-DECISION section 2
            // RF-N4, ruling 1: the three-way split lands on the transpiler side):
            //   * get_IsGenericType -- the RUNTIME predicate, judged by the
            //     typeinfo's emitted genericArity (il2cpp_typeinfo_is_generic_type).
            //     Both the generic DEFINITION and its closed CLONES register
            //     under the SAME name key (ReflectionFullName), and the folded
            //     name of a closed instantiation IS the definition's name, so
            //     the arity check answers True for typeof(Box<>) and
            //     typeof(Box<int>) alike -- the runtime cannot (and need not)
            //     tell them apart (net48 IsGenericType is True for both).
            //   * get_IsGenericTypeDefinition -- NOT wired here: StackFold
            //     intercepts the folded typeof chain (definition -> 1, closed
            //     instance -> 0). A RUNTIME receiver (obj.GetType()) reaches
            //     this mapping -- the registry cannot distinguish a definition
            //     from its clones (same name key), so it must fail LOUD below.
            case "get_IsGenericType":
                return I("il2cpp::il2cpp_typeinfo_is_generic_type", 0, true, false, true);
            case "get_IsGenericTypeDefinition":
                throw new NotSupportedException(
                    "Type.get_IsGenericTypeDefinition on a runtime (GetType-derived) Type is not decidable: the typeinfo registry cannot distinguish a generic definition from its closed clones (both register the definition's name -- RF-N4), so StackFold must fold this on a typeof chain and this runtime path cannot be wired");
            //   * GetGenericArguments() -- the THREE-WAY runtime split
            //     (il2cpp_typeinfo_get_generic_arguments): non-generic ->
            //     EMPTY array; genericArity > 0 -> LOUD (no fabrication -- the
            //     genericArgNames Plan B is deferred). ReturnTypeName
            //     "System.Type[]" keeps the Array-domain folds (.Length /
            //     element read) resolved.
            //   * GetGenericTypeDefinition() -- returns the DEFINITION's own
            //     name (identity -- the simplified Type model); a non-generic
            //     receiver throws InvalidOperationException (net48).
            case "GetGenericArguments":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_generic_arguments", 0, true, true, true,
                        returnTypeName: "System.Type[]");
                throw new NotSupportedException($"Type.GetGenericArguments with {ps.Length} parameter(s) not wired in RF-N4 -- only the () overload maps to il2cpp_typeinfo_get_generic_arguments");
            case "GetGenericTypeDefinition":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_typeinfo_get_generic_type_definition", 0, true, true, true,
                        returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetGenericTypeDefinition with {ps.Length} parameter(s) not wired in RF-N4 -- only the () overload maps to il2cpp_typeinfo_get_generic_type_definition");
            // C1 (A1): Type-COM creation. Type.GetTypeFromProgID(string) /
            // Type.GetTypeFromCLSID(Guid) produce the COM Type VALUE string
            // "COMCLSID:<32hex>" (il2cpp_com.h -- the simplified Type model's
            // sentinel for a COM type). ReturnTypeName "System.Type" keeps the
            // Type-domain linkage (get_Name / IsComObject / op_Equality) on
            // the result.
            case "GetTypeFromProgID":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_com_type_from_progid", 1, true, true, false,
                        new[] { "s" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetTypeFromProgID with {ps.Length} parameter(s) not wired in C1 -- only the (string) overload maps to il2cpp_com_type_from_progid");
            case "GetTypeFromCLSID":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::il2cpp_com_type_from_clsid", 1, true, true, false,
                        new[] { "g" }, returnTypeName: "System.Type");
                throw new NotSupportedException($"Type.GetTypeFromCLSID with {ps.Length} parameter(s) not wired in C1 -- only the (Guid) overload maps to il2cpp_com_type_from_clsid");
            // C2 (A2): Type.InvokeMember(string, BindingFlags, Binder, object,
            // object[]) on a COM Type -- the ONE-CALL late-bound IDispatch
            // invocation. ParamCount IS THE IL ARITY (5, the fold's stack pop
            // count); the argOrder maps the icall slots to the IR arg list
            // [receiver, name, invokeAttr, binder, target, args]: slot0 (rcw)
            // = index 4 (target), slot1 (name) = index 1, slot2 (args) = index
            // 5; the receiver and the invokeAttr/binder slots are not
            // referenced (dropped). property-put is LOUD by design (only
            // DISPATCH_METHOD + DISPATCH_PROPERTYGET). ReturnTypeName
            // "System.Object" keeps the null/IsComObject/Concat linkage.
            case "InvokeMember":
                if (ps.Length == 5 && ps[0] == 0x0E && ps[1] == 0x11 && ps[4] == 0x1D)
                    return I("il2cpp::runtime::icall_com_invoke_member", 5, true, true, true,
                        new[] { "", "s", "a" },
                        returnTypeName: "System.Object", argOrder: new[] { 4, 1, 5 });
                throw new NotSupportedException($"Type.InvokeMember with {ps.Length} parameter(s) not wired in C2 -- only the (string, BindingFlags, Binder, object, object[]) overload maps to icall_com_invoke_member");
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // REFL4 (item 3) -- the System.Type assembly surface (needs the input
    // assembly's simple name, read from the AssemblyDef header):
    //   * get_AssemblyQualifiedName -- the net48 AQN string
    //     "<FullName>, <asm>, Version=1.0.0.0, Culture=neutral,
    //      PublicKeyToken=null". The receiver is the Type VALUE full-name
    //     string; the runtime icall formats it + the transpiler-baked
    //     simple-name literal (appendLiterals -- the CustomAttributeExtensions
    //     intern-literal precedent). ReturnTypeName "System.String".
    //   * get_Assembly -- the assembly SIMPLE-NAME string as the Assembly
    //     VALUE (the simplified Assembly model: like the Type VALUE is the
    //     full name, an Assembly VALUE is its simple name). Folds to the
    //     literal marker -- the value depends only on the single input
    //     assembly (net48: the same assembly object for every type), so the
    //     property is receiver-independent. This replaces the old NullMarker
    //     fold (MethodMap.Resource.cs) -- `asm == null` must be False (C7); a
    //     folded non-null simple-name string answers False.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapTypeAssembly(MetadataReader md, string owner, string name, byte[] ps, int count,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.Type") return null;
        string asmName;
        try
        {
            asmName = md.GetString(md.GetAssemblyDefinition().Name);
        }
        catch
        {
            asmName = "";
        }
        if (!string.IsNullOrEmpty(asmName))
        {
            switch (name)
            {
                case "get_AssemblyQualifiedName":
                    if (count == 0)
                    {
                        var asmLit = $"il2cpp::il2cpp_string_intern_literal(\"{asmName}\")";
                        return I("il2cpp::il2cpp_typeinfo_get_assembly_qualified_name", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String",
                            appendLiterals: new[] { asmLit });
                    }
                    throw new NotSupportedException(
                        $"Type::get_AssemblyQualifiedName with {count} parameter(s) not wired in REFL4 -- the () property maps to il2cpp_typeinfo_get_assembly_qualified_name with the AssemblyDef simple-name literal");
                case "get_Assembly":
                    if (count == 0)
                        return new ResolvedMethod(ResolvedMethod.LiteralMarker + asmName, 0, false, true, true,
                            Array.Empty<bool>(), true, ReturnTypeName: "System.Reflection.Assembly");
                    throw new NotSupportedException(
                        $"Type::get_Assembly with {count} parameter(s) not wired in REFL4 -- the () property folds to the assembly simple-name literal");
            }
        }
        return null;
    }
}
