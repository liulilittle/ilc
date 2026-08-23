namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Object -- System.Object + the object-model members of the
// metadata surface (System.IConvertible::GetTypeCode,
// System.Reflection.MemberInfo::get_Name on a GetType() result).
// Owners: System.Object / System.IConvertible / System.Reflection.MemberInfo
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
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
                // M5 wiring: Object.GetHashCode virtual dispatch (value_semantics).
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_object_get_hash_code", 0, true, false, true);
                // icallwide: Object::ToString/GetHashCode on a TimeSpan
                // receiver (Roslyn emits callvirt Object::* for the overrides)
                // -- route to the ticks formatters/hashes (time.cpp).
                if (name == "ToString" && receiverType == "System.TimeSpan")
                    return I("il2cpp::runtime::icall_timespan_to_string", 0, true, true, true, new[] { "deref:ts" });
                if (name == "GetHashCode" && receiverType == "System.TimeSpan")
                    return I("il2cpp::runtime::icall_timespan_get_hash_code", 0, true, false, true, new[] { "deref:ts" });
                // icallwide: Version receiver (Roslyn emits callvirt Object::*)
                if (name == "GetHashCode" && receiverType == "System.Version")
                    return I("il2cpp::runtime::icall_version_get_hash_code", 0, true, false, true);
                if (name == "ToString" && receiverType == "System.Text.StringBuilder")
                    return I("il2cpp::runtime::icall_stringbuilder_to_string", 0, true, true, true);
                // icallwide: Object::ToString on a TimeSpan receiver (Roslyn
                // emits callvirt Object::ToString for the override) -- route
                // to the constant-"c" formatter (time.cpp).
                if (name == "ToString" && receiverType == "System.TimeSpan")
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
                // M6 G6 wiring: Object::ToString on a StringWriter receiver (Roslyn
                // emits callvirt Object::ToString for StringWriter.ToString) --
                // route to the StringWriter object icall (stringrw.cpp): a COPY
                // of the growable buffer (see MethodMap.StringIO.cs).
                if (name == "ToString" && receiverType == "System.IO.StringWriter")
                    return I("il2cpp::runtime::icall_stringwriter_to_string", 0, true, true, true);
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
                return null;
            default: return null;
        }
    }
}
