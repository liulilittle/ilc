namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Object -- System.Object + the object-model members of the
// metadata surface (System.IConvertible::GetTypeCode,
// System.Reflection.MemberInfo::get_Name on a GetType() result).
// Owners: System.Object / System.IConvertible / System.Reflection.MemberInfo
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    // P0-narrow (2026-10-22): the receivers whose IConvertible.ToString /
    // IFormattable.ToString implementations are the runtime's own primitive
    // formatters (i.e. the ones icall_object_to_string / the composite
    // formatter reproduce exactly). A USER type's implementation is its own
    // code and must never be routed here.
    private static bool IsPrimitiveToStringReceiver(string? t) => t is
        "System.Int32" or "System.Int64" or "System.UInt32" or "System.UInt64"
        or "System.Boolean" or "System.Char" or "System.Byte" or "System.SByte"
        or "System.Int16" or "System.UInt16" or "System.Double" or "System.Single"
        or "System.Decimal" or "System.String";

    // IFormattable is implemented by the numeric primitives + decimal ONLY
    // (bool/char/string do not implement it -- net48 rejects the cast).
    private static bool IsFormattableReceiver(string? t) => t is
        "System.Int32" or "System.Int64" or "System.UInt32" or "System.UInt64"
        or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
        or "System.Double" or "System.Single" or "System.Decimal";

    private static ResolvedMethod? TryMapObject(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Object":
                if (name == "GetType")
                    return I("il2cpp::runtime::icall_object_get_type", 0, true, true, true);
                if (name == "Equals" && ps.Length == 1)
                {
                    // .NET Delegate.Equals is STRUCTURAL (same target + method per
                    // leaf); Roslyn emits callvirt Object::Equals for delegate
                    // equality, so a TRACKED delegate receiver routes to the
                    // delegate-ext comparison (delegateext.cpp) instead of the
                    // boxed-value path below (reference equality would be wrong).
                    if (receiverType == "System.Delegate")
                        return I("il2cpp::runtime::icall_delegate_equals", 1, true, false, true);
                    // URI-FIX P1: Object::Equals on a tracked Uri receiver --
                    // value equality over the absolute form (versionuri.cpp
                    // icall_uri_equals), not reference identity (diffuri001
                    // uri.equals.same; evidence gen Cases_Prog.cpp:379 takes
                    // the boxed path today).
                    if (receiverType == "System.Uri")
                        return I("il2cpp::runtime::icall_uri_equals", 1, true, false, true);
                    // .NET object.Equals -- the runtime boxed-value equality dispatches
                    // on the box classes (bool/char/int/double/long/...) and falls back
                    // to reference equality, mirroring Object.Equals semantics.
                    return I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true);
                }
                // M5 wiring: STATIC object.Equals(a, b) -- the value_semantics domain
                // (reference identity first, strings by content, boxes by value).
                if (name == "Equals" && ps.Length == 2)
                    return I("il2cpp::runtime::icall_object_equals", 2, true, false, false);
                // M5 wiring: object.ReferenceEquals(a, b) -- the fold replaces the
                // call with a C++ pointer comparison (reference identity).
                if (name == "ReferenceEquals" && ps.Length == 2)
                    return new ResolvedMethod(ResolvedMethod.RefEqMarker, 2, false, true, false, new[] { false, false }, false);
                // icallwide: typed-receiver dispatches MUST precede the
                // Object::GetHashCode default arm below (the default arm
                // would swallow every callvirt with a tracked receiver type).
                // boxwire: the "deref:ts" cast requires an ADDRESS receiver
                // (the ldloca of a value-typed local). A tracked TimeSpan
                // receiver can ALSO be a BOX OBJECT (`((object)ts).ToString()`
                // -- StackFold records objTypes[boxTemp] = "System.TimeSpan"),
                // and dereferencing the box object as if it were the value
                // slot printed garbage (probe: cw_ts=162.20:16:33.7676000).
                // A boxed receiver keeps the klass dispatch: it falls through
                // to icall_object_to_string / icall_object_get_hash_code.
                if (name == "ToString" && receiverType == "System.TimeSpan" && receiverIsAddress)
                    return I("il2cpp::runtime::icall_timespan_to_string", 0, true, true, true, new[] { "deref:ts" });
                if (name == "GetHashCode" && receiverType == "System.TimeSpan" && receiverIsAddress)
                    return I("il2cpp::runtime::icall_timespan_get_hash_code", 0, true, false, true, new[] { "deref:ts" });
                if (name == "GetHashCode" && receiverType == "System.Version")
                    return I("il2cpp::runtime::icall_version_get_hash_code", 0, true, false, true);
                // boxwire (nullable GN-2 follow-up): Roslyn emits
                // `callvirt Object::GetHashCode` for the DateTime/TimeSpan/Guid
                // OVERRIDES (the TimeSpan arm above is the same shape). The
                // address-gated value dispatches keep a BOXED receiver on the
                // klass path (icall_object_get_hash_code). Without the Guid arm
                // the value ADDRESS was passed as an Il2CppObject* (probe:
                // `g.GetHashCode()` -> icall_object_get_hash_code((&loc2))).
                if (name == "GetHashCode" && receiverType == "System.Guid" && receiverIsAddress)
                    return I("il2cpp::il2cpp_guid_get_hash_code", 0, true, false, true, new[] { "g" });
                if (name == "GetHashCode" && receiverType == "System.DateTime" && receiverIsAddress)
                    return I("il2cpp::runtime::icall_datetime_get_hash_code", 0, true, false, true, new[] { "deref:dt" });
                // URI-FIX P3: Object::GetHashCode on a tracked Uri receiver
                // (Roslyn emits callvirt Object::GetHashCode for the
                // override, like Equals in P1b) -- route to the deterministic
                // ordinal contract hash (versionuri.cpp
                // icall_uri_get_hash_code), not the identity default arm.
                if (name == "GetHashCode" && receiverType == "System.Uri")
                    return I("il2cpp::runtime::icall_uri_get_hash_code", 0, true, false, true);
                // B4 (dtoprobe [hc]): Object::GetHashCode on a tracked
                // DateTimeOffset receiver (Roslyn emits callvirt
                // Object::GetHashCode for the override, the Uri precedent
                // above) -- net48 returns UtcDateTime.GetHashCode()
                // (referencesource datetimeoffset.cs), i.e. the ticks-fold
                // of the UTC instant (no offset component).
                if (name == "GetHashCode" && receiverType == "System.DateTimeOffset")
                    return I("il2cpp::runtime::icall_dtoffset_get_hash_code", 0, true, false, true, new[] { "deref:dto" });
                if (name == "ToString" && receiverType == "System.Text.StringBuilder")
                    return I("il2cpp::runtime::icall_stringbuilder_to_string", 0, true, true, true);
                if (name == "ToString" && receiverType == "System.Version")
                    return I("il2cpp::runtime::icall_version_to_string", 0, true, true, true);
                // M5 wiring: Object.GetHashCode virtual dispatch (value_semantics).
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_object_get_hash_code", 0, true, false, true);
                if (name == "ToString" && receiverType == "System.Text.StringBuilder")
                    return I("il2cpp::runtime::icall_stringbuilder_to_string", 0, true, true, true);
                // icallwide: Object::ToString on a TimeSpan receiver (Roslyn
                // emits callvirt Object::ToString for the override) -- route
                // to the constant-"c" formatter (time.cpp). boxwire: address
                // receivers only (see the GetHashCode/ToString pair above --
                // a BOXED TimeSpan keeps the klass dispatch).
                if (name == "ToString" && receiverType == "System.TimeSpan" && receiverIsAddress)
                    return I("il2cpp::runtime::icall_timespan_to_string", 0, true, true, true, new[] { "deref:ts" });
                // M6 G6 wiring: Object::ToString on a Version receiver (Roslyn
                // emits callvirt Object::ToString) -- route to the Version
                // object icall (versionuri.cpp): rebuilds from the stored
                // components (see MethodMap.Version.cs).
                if (name == "ToString" && receiverType == "System.Version")
                    return I("il2cpp::runtime::icall_version_to_string", 0, true, true, true);
                // M6 G6 wiring: Object::ToString on a Uri receiver (Roslyn
                // emits callvirt Object::ToString) -- route to the Uri object
                // icall (versionuri.cpp): rebuilds scheme://host/path from
                // the stored slots (see MethodMap.Uri.cs).
                if (name == "ToString" && receiverType == "System.Uri")
                    return I("il2cpp::runtime::icall_uri_to_string", 0, true, true, true);
                // adm1 D2 (review fix): Object::ToString on an AppDomain
                // receiver -- Roslyn emits callvirt Object::ToString for the
                // net48 AppDomain.ToString override, so the AppDomain-owner
                // LOUD arm (MethodMap.Legacy.cs ToString) is DEAD at real call
                // sites: the resolver here fell to the System_Object_ToString_
                // mangled fallback and the runtime printed the klass NAME
                // ("System.AppDomain") instead of the adm1 NotSupported LOUD
                // contract. Route the tracked AppDomain receiver to the
                // throwing icall so ad.ToString() genuinely throws.
                if (name == "ToString" && receiverType == "System.AppDomain")
                    return I("il2cpp::runtime::icall_appdomain_to_string", 0, true, true, true);
                // M6 G6 wiring: Object::ToString on a StringWriter receiver (Roslyn
                // emits callvirt Object::ToString for StringWriter.ToString) --
                // route to the StringWriter object icall (stringrw.cpp): a COPY
                // of the growable buffer (see MethodMap.StringIO.cs).
                if (name == "ToString" && receiverType == "System.IO.StringWriter")
                    return I("il2cpp::runtime::icall_stringwriter_to_string", 0, true, true, true);
                // nb5 OBJ-1: Object::ToString on a tracked Match receiver
                // (Roslyn emits callvirt Object::ToString for the Match.ToString
                // override) -- route to the Match object icall (regex_obj.cpp)
                // which returns the matched substring (net48 Match.ToString()).
                if (name == "ToString" && ps.Length == 0 && receiverType == "System.Text.RegularExpressions.Match")
                    return I("il2cpp::runtime::icall_regex_match_to_string", 0, true, true, true,
                        returnTypeName: "System.String");
                // M5 wiring: Object.ToString on a TRACKED boxed value -- the
                // value_semantics dispatch prints boxed ints decimally, boxed bools
                // "True"/"False", strings as identity, a plain new object() as its
                // type full name; anything untracked falls through to the vtable
                // path (loud failure downstream). Byte/SByte/Int16/UInt16 join the
                // numeric family the emitter can box (CodeGenerator box_ switch);
                // their value formatting lands in the runtime task (valuesemantics.cpp
                // klass-name fallback) -- the wiring maps the available icall now.
                if (name == "ToString" && ps.Length == 0 && receiverType is "System.Int32" or "System.Int64"
                    or "System.UInt64" or "System.Boolean" or "System.Double" or "System.Single"
                    or "System.Char" or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
                    or "System.String" or "System.Object"
                    // decimal-domain T3: a BOXED decimal receiver formats
                    // through icall_object_to_string (the valuesemantics.cpp
                    // klass dispatch; the System.Decimal arm lands with T2).
                    or "System.Decimal")
                    return I("il2cpp::runtime::icall_object_to_string", 0, true, true, true);
                // netcase (M7R2): IPAddress.Parse(s).ToString() -- the parse
                // result is an ip4 VALUE slot (uint8_t[4], ReturnTypeName
                // tracked from the Parse mapping), and the callvirt routes
                // here with receiverType "System.Net.IPAddress". Map to the
                // ipaddr.h ToString (the "g" byte-pointer cast survives both
                // the plain-array value and the ldloca address shape).
                if (name == "ToString" && ps.Length == 0 && receiverType == "System.Net.IPAddress")
                    return I("il2cpp::runtime::il2cpp_ipaddress_to_string", 0, true, true, true, new[] { "g" });
                // P13 dtoffset: DateTimeOffset.ToString() -- the receiver is an
                // ldloca ADDRESS of a 16-byte value slot; deref to the struct
                // VALUE (the icall takes it by value, the DateTime "deref:dt"
                // precedent). The MemberRef declares ToString on the Object
                // base, so the receiver hint routes it here.
                if (name == "ToString" && ps.Length == 0 && receiverType == "System.DateTimeOffset")
                    return I("il2cpp::runtime::icall_dtoffset_to_string", 0, true, true, true, new[] { "deref:dto" });
                // P17 biginteger: BigInteger.ToString() -- the receiver is an
                // ldloca ADDRESS of an 8-byte value slot; deref to the struct
                // VALUE (the icall takes it by value).
                if (name == "ToString" && ps.Length == 0 && receiverType == "System.Numerics.BigInteger")
                    return I("il2cpp::runtime::icall_biginteger_to_string", 0, true, true, true, new[] { "deref:bi" });
                // excapi1 (2026-10-24): Roslyn emits `callvirt Object::ToString`
                // for Exception.ToString() (the MemberRef declares on the Object
                // base), so the tracked exception receiver routes here. Map to
                // the runtime icall that renders "<TypeName>: <Message>" (the
                // composed message / the type-based default text).
                if (name == "ToString" && ps.Length == 0 && IsExceptionReceiverType(receiverType))
                    return I("il2cpp::runtime::icall_exception_to_string", 0, true, true, true,
                        returnTypeName: "System.String");
                // M7 gcext (2026-08-09): Object::Finalize -- the IMPLICIT base
                // call every C# destructor emits at the end of its Finalize
                // body (gcext's ~Tracked()). .NET's Object.Finalize is an
                // empty virtual no-op; the runtime has no object-finalize
                // icall but DOES ship the receiver-accepting no-op
                // il2cpp_object_ctor_nop (runtime.cpp:102, the Object::.ctor
                // mapping target). The base-call semantic is "do nothing with
                // the receiver" -- exactly what the ctor-nop does, so the
                // Finalize base call folds to it (the receiver passes through
                // the "" slot; the icall ignores it). No fabricated runtime
                // surface: the no-op icall is the documented .ctor target and
                // is semantically identical to an empty finalizer.
                if (name == "Finalize")
                    return I("il2cpp::runtime::il2cpp_object_ctor_nop", 0, false, false, true);
                // P6: Object.MemberwiseClone (stub).
                if (name == "MemberwiseClone")
                    // rv=true, ReturnsRef=TRUE (the result is a managed
                    // reference -- the reinterpret/ref-local path), inst=true
                    // (the receiver rides as arg0 even though the IL `call`
                    // pushes nothing: MemberwiseClone is this-method).
                    return I("il2cpp::runtime::icall_object_memberwise_clone", 0, true, true, true);
                return null; // Object::ToString on unknown receivers: loud vtable failure downstream
            case "System.IConvertible":
                if (name == "GetTypeCode")
                    return I("il2cpp::runtime::icall_object_get_type_code", 0, true, false, true);
                // P0-narrow (2026-10-22): IConvertible.ToString(IFormatProvider)
                // -- the explicit interface call `((IConvertible)sb).ToString(inv)`
                // (net48: the provider is IGNORED, the result is the plain
                // ToString() text; probed for every primitive: sbyte -1, byte
                // 255, short -5, ushort 65535, int -1, long -1234567890123,
                // double 1.5, decimal 1.5m, bool "True", char "A"). The
                // receiver is the BOXED object (the IL is `box T; castclass
                // IConvertible`), so the boxed-value dispatch
                // icall_object_to_string is the exact semantics -- it prints
                // the same text and throws NullReferenceException on null,
                // like net48. The provider rides the second slot as a "drop"
                // (the icall takes the receiver only).
                // Gated on a tracked PRIMITIVE receiver: a user type's
                // IConvertible implementation is its own code, not
                // Object.ToString, so an untracked/other receiver keeps the
                // pre-existing LOUD gap (never a silent wrong value).
                if (name == "ToString" && count == 1 && IsPrimitiveToStringReceiver(receiverType))
                    return I("il2cpp::runtime::icall_object_to_string", 1, true, true, true,
                        new[] { "", "drop" });
                // convic1: ((IConvertible)boxedInt32).ToInt32(provider). The
                // receiver is the boxed Int32 (receiverType tracks the box
                // source); unbox it at the call site and apply the net48
                // identity. The IFormatProvider argument is ignored ("drop").
                // Other receivers are NOT wired here: their net48 conversions
                // are checked/rounded and would need dedicated icalls.
                if (name == "ToInt32" && count == 1 && receiverType == "System.Int32")
                    return I("il2cpp::runtime::icall_iconvertible_to_int32", 1, true, false, true,
                        new[] { receiverIsAddress ? "deref:i" : "unbox:i", "drop" });
                return null;
            case "System.IFormattable":
                // P0-narrow (2026-10-22): IFormattable.ToString(string,
                // IFormatProvider) -- `((IFormattable)sb).ToString("X", inv)`.
                // The owner had NO dispatch case at all, so the call fell to
                // the phantom generated-name path and the whole METHOD was
                // dropped from the output (a runtime NotSupportedException
                // stub). net48 formats exactly like ToString(format, provider):
                // probed (sbyte)-1 "X" -> "FF", "X4" -> "00FF", (short)-1 "X"
                // -> "FFFB", (byte)255 "X" -> "FF", (ushort)65535 "X" ->
                // "FFFF", "D5" -> "-00001", int -1 "X" -> "FFFFFFFF",
                // ulong.MaxValue "X" -> 16 digits, 1.5f/double "F2" -> "1.50",
                // decimal "F2" -> "1.50", and "Q" -> FormatException.
                // icall_format_iface_tostring is the composite formatter with
                // alignment 0 -- the SAME boxed-klass dispatch (including the
                // narrow hex width and the decimal payload), so every row
                // above is reproduced by one arm.
                if (name == "ToString" && count == 2 && IsFormattableReceiver(receiverType))
                    return I("il2cpp::runtime::icall_format_iface_tostring", 2, true, true, true,
                        new[] { "", "", "drop" }, providerCulture: true);
                return null;
            case "System.Reflection.MemberInfo":
                // M5 wiring: MemberInfo::get_Name on a GetType() result (the
                // Roslyn MemberRef parent for `o.GetType().Name`). The
                // receiver is the FULL-name string the GetType icall
                // produces; .NET Type.Name is the SHORT name, so the
                // property maps to the runtime truncation primitive (not the
                // old identity -- the boxing2/boxdeep GetType mismatch).
                if (name == "get_Name" && receiverType == "System.Type")
                    return I("il2cpp::runtime::il2cpp_type_get_short_name", 0, true, true, true, returnTypeName: "System.String");
                // M7 delegate2 (2026-08-08): MemberInfo::get_Name on a
                // "System.String" receiver -- the NAME STRING that
                // Delegate.get_Method's icall_delegate_get_method_name
                // produced (f.Method.Name). The string IS the name, so the
                // property folds to the receiver (IdentityMarker), exactly
                // like CultureInfo.get_Name on a CurrentCulture string
                // (MethodMap.CultureInfo.cs:24-29). The ReturnTypeName hint
                // from the get_Method mapping propagates this receiver type.
                if (name == "get_Name" && receiverType == "System.String")
                    return new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true);
                // REFL4 (item 4, C1): ConstructorInfo::get_Name on the ctor
                // member VALUE "<FullName>::.ctor". The MemberInfo::get_Name
                // on a ConstructorInfo receiver (recvType "System.Reflection.
                // ConstructorInfo" -- the GetConstructor ReturnTypeName) must
                // decode to net48's ".ctor". The ctor VALUE is the
                // disambiguated "<FullName>::.ctor" (GAP-5 -- Invoke parses
                // it), so the fold CANNOT be the "."/identity decode: the
                // VALUE special-cases the ".ctor" key (decodes to ".ctor")
                // while the Invoke dependency on the disambiguated VALUE is
                // untouched (this mapping is only the Name PROPERTY arm).
                if (name == "get_Name" && receiverType == "System.Reflection.ConstructorInfo")
                    return I("il2cpp::il2cpp_ctorinfo_get_name", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.String");
                // M7 delegate2 (2026-08-08): MemberInfo::get_Name on a NON-Type
                // receiver (the MethodInfo that Delegate.get_Method would
                // return -- `f.Method.Name`). The runtime has only the Type
                // short-name icall (il2cpp_type_get_short_name); method/field/
                // property member-name icalls do not exist. The delegate2 case
                // is already blocked earlier at the get_Method LOUD
                // (MethodMap.Delegate.cs) -- this arm documents the same gap
                // but keeps returning null so the StackFold receiver-aware
                // re-resolve (recvType hint) can still select the Type path;
                // a direct MemberInfo::get_Name on a MethodInfo would fall to
                // the generated-name path (loud C++ undefined symbol).
                // RF-N6 (NEXT2-DECISION section ② RF-N6): member-level
                // GetCustomAttributes(bool inherit) -- the receiver is the
                // MEMBER-NAME string (the simplified member model, the same
                // carrier the GetMethod/GetField/GetProperty icalls return);
                // the runtime scans the typeinfo registry for the deepest
                // declaring type and walks the own + base chain collecting
                // the same-named member's registered attributes
                // (il2cpp_attribute_member_get_custom -- typeinfo.h RF-N6
                // contract). Only the net48 (bool) overload reaches the
                // icall; the (Type, bool) attributeType-filter form fails
                // LOUD (no fabricated filter -- no fabrication). ReturnTypeName
                // "System.Object[]" keeps the Array-domain folds (.Length /
                // foreach element -> o.GetType().Name) resolved.
                if (name == "GetCustomAttributes" && ps.Length == 1 && ps[0] == 0x02)
                    return I("il2cpp::il2cpp_attribute_member_get_custom", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.Object[]");
                if (name == "GetCustomAttributes")
                    throw new NotSupportedException(
                        $"System.Reflection.MemberInfo::GetCustomAttributes with {ps.Length} parameter(s) not wired in RF-N6 -- only the net48 (bool inherit) overload maps to il2cpp_attribute_member_get_custom (the (Type, bool) attributeType-filter form is not wired)");
                // REFL3 (item 1): MemberInfo.IsDefined(Type attributeType, bool
                // inherit) -- net48 bool. The receiver rides the simplified
                // member VALUE (for a Type receiver -- the probe shape -- the
                // full type-name string, the same carrier the _ex_named element
                // slot consumes). Maps to the managed-string existence query
                // il2cpp_attribute_is_defined_named(element, attributeType,
                // inherit) (attribute.cpp) -- the same CollectAttributes walk
                // the _ex_named queries run (base chain under inherit,
                // attributeType assignability, AttributeUsage rules).
                if (name == "IsDefined" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x02)
                    return I("il2cpp::runtime::il2cpp_attribute_is_defined_named", 2, true, false, true,
                        new[] { "", "", "i" });
                // REFL3 (item 3): MemberInfo.IsDefined(Type) -- the 1-arg
                // instance overload (net48 default inherit = TRUE, oracle-
                // pinned by p-refl3-3gaps). Same existence primitive with an
                // appended inherit literal (the probe's 1-arg vs 2-arg(true)
                // cross-check is identical).
                if (name == "IsDefined" && ps.Length == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::il2cpp_attribute_is_defined_named", 1, true, false, true,
                        new[] { "" }, appendLiterals: new[] { "1" });
                if (name == "IsDefined")
                    throw new NotSupportedException(
                        $"System.Reflection.MemberInfo::IsDefined with {ps.Length} parameter(s) not wired in REFL3 -- only the net48 (Type attributeType, bool inherit) overload maps to il2cpp_attribute_is_defined_named");
                // REFL4 (item 1): MemberInfo.get_MemberType -- the six-state
                // kind label. The receiver is the MEMBER-VALUE string (a
                // member NAME, the simplified member model, OR a Type VALUE
                // full-name string for the typeof(X).MemberType / nested-type
                // shapes -- the simplified Type model); the runtime classifies
                // by scanning the typeinfo member tables. Returns the net48
                // MemberTypes NUMERIC value (Method=8 Field=4 Property=16
                // Constructor=1 Event=2 TypeInfo=32 NestedType=128) as an
                // int32 -- the (int) cast probes print byte-exact. The
                // result is a value (MemberTypes underlying int32), so no
                // ReturnTypeName is tracked.
                if (name == "get_MemberType" && count == 0)
                    return I("il2cpp::il2cpp_memberinfo_get_member_type", 0, true, false, true,
                        new[] { "" });
                // REFL4 (item 1): MemberInfo.get_DeclaringType -- the type
                // declaring the member (or the original Type receiver).
                // Same member/Type VALUE string receiver; the runtime returns
                // the declaring type's FULL-NAME STRING (a Type VALUE --
                // ReturnTypeName "System.Type" keeps downstream
                // .Name/.FullName/.IsClass/op_Equality folds resolving on
                // the Type domain, e.g. `m.DeclaringType == typeof(X)`).
                if (name == "get_DeclaringType" && count == 0)
                    return I("il2cpp::il2cpp_memberinfo_get_declaring_type", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Type");
                // refl5 (item 3 / C2): MemberInfo.get_ReflectedType on a
                // MEMBERINFO-TYPED receiver (the MemberRef parent resolves to
                // the base owner here; the derived-owner MemberRefs route via
                // TryMapReflectionMember -- same icall). The type the member
                // was obtained from == the DEEPEST declaring type (W7: the
                // name-string member VALUE carries no query-type); a TYPE
                // receiver answers itself (net48 typeof(X).ReflectedType).
                if (name == "get_ReflectedType" && count == 0)
                    return I("il2cpp::il2cpp_memberinfo_get_reflected_type", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Type");
                return null;
            default: return null;
        }
    }
}
