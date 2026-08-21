namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Guid -- System.Guid icall mappings (src/runtime/include/internal/guid.h).
// Owners: System.Guid
//
// VALUE-TYPE MODEL: a Guid is a 16-byte value; the runtime icalls take RAW
// POINTERS to those 16 bytes (const uint8_t* -- the array parameter decays
// to a pointer) and new_guid/parse WRITE through the pointer. The receiver
// (an ldloca ADDRESS of the value slot) and Guid arguments carry the "g"
// cast code -- the emitter renders it as `(uint8_t*)(...)` (the slot is
// `uint8_t loc[16]`, so the pointer-to-array address does NOT decay). The
// parse input string gets the "s" cast (Il2CppString* -- the IR carries it
// as Il2CppObject*).
//
// WHY "g" AND NOT "" (interlockedcase regression, 2026-08-05): "" is the
// RAW-PASSTHROUGH code -- Interlocked's int32_t*/int64_t* location params,
// value-type ctor Point* receivers etc. pass their addresses through
// UNCAST. Commit b59c6d9 blanket-cast every ""-cast LocAddrExpr arg to
// (uint8_t*) because Guid's ldloca addresses need it, and broke every other
// address-taking domain (interlockedcase/valuetypes both regressed in the
// matrix4 cmake_build stage). The emitter no longer blanket-casts: the
// Guid byte-slot cast is carried explicitly by this "g" code.
//
// SLOT-TYPE GAP (documented, out of scope): the generated-code type for a
// Guid value slot does not exist yet -- no TypeCppName entry and no temp
// kind (DateTime/TimeSpan got the "dt"/"ts" kinds the same way). NewGuid's
// result carries the "guid" kind marker so that future slot wiring can
// recognize it; until the slot type lands, calls through these mappings
// fail loudly at C++ compilation instead of silently truncating.
//
// Known-unwireable members throw NotSupportedException (LOUD):
//   * ToString(format)       -- guid.h implements the "D" form only; a
//     format argument would be silently ignored.
//   * Equals(object)/CompareTo(object) -- a boxed Guid has no runtime box
//     class (the emitter's box table rejects it), so the boxed value
//     cannot reach the icall.
//   * .ctor(byte[])          -- guid.h has no from-bytes icall (surface:
//     new_guid / parse / to_string / equals / compare only).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapGuid(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // static Guid NewGuid(): the icall writes the 16 fresh bytes
            // into the result slot; "guid" is the value-type result marker.
            case "NewGuid":
                return I("il2cpp::il2cpp_guid_new_guid", 0, true, false, false, null, "guid");
            // instance string ToString(): the receiver address is the
            // icall's const uint8_t[16] input ("g" = byte-pointer cast).
            case "ToString":
                if (ps.Length == 0)
                    return I("il2cpp::il2cpp_guid_to_string", 0, true, true, true, new[] { "g" });
                throw new NotSupportedException("ToString(format) not wired: guid.h implements the \"D\" form only and a format argument would be silently ignored");
            // static Guid Parse(string): the runtime STRICT parse wrapper
            // (il2cpp_guid_parse_strict) throws ArgumentNullException on a
            // null string and FormatException on any other invalid input --
            // net48 Parse semantics recovered on top of the TryParse-shaped
            // core. The result carries the "guid" value-type marker, exactly
            // like NewGuid.
            case "Parse":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_guid_parse_strict", 1, true, false, false, new[] { "s" }, "guid");
                return null;
            // static bool TryParse(string, out Guid): the out param is the
            // BYREF (0x10) ldloca address of the result slot -- exactly the
            // icall's output buffer (uint8_t bytes[16]). ps[1] carries the
            // BYREF ELEMENT byte (0x11 VALUETYPE -- ReadParamByte surfaces
            // the element, not the 0x10 marker).
            case "TryParse":
                return ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11
                    ? I("il2cpp::il2cpp_guid_parse", 2, true, false, false, new[] { "s", "g" })
                    : null;
            // static bool op_Equality(Guid, Guid): the C# == operator. Both
            // Guid operands carry the "g" cast like the Equals receiver
            // (each is the address expression of a 16-byte value slot --
            // exactly the icall's const uint8_t* pair).
            case "op_Equality":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::il2cpp_guid_equals", 2, true, false, false, new[] { "g", "g" });
                return null;
            // static bool op_Inequality(Guid, Guid): no inequality icall on
            // the guid.h surface, so the mapping emits the equality icall
            // under a C++ logical-NOT -- "!f(a, b)" parses as "!(f(a, b))"
            // (call binds tighter than unary !), exactly the net48 !=
            // contract over the byte-wise equals. The "!" prefix is safe for
            // the same reason as String.op_Inequality: the fold's
            // il2cpp:: classification gates only inspect CALLVIRT receivers,
            // and op_* are always static calls.
            case "op_Inequality":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("!il2cpp::il2cpp_guid_equals", 2, true, false, false, new[] { "g", "g" });
                return null;
            case "Equals":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_guid_equals", 1, true, false, true, new[] { "g", "g" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("Equals(object) not wired: a boxed Guid has no runtime box class, so the boxed value cannot reach the icall");
                return null;
            case "CompareTo":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::il2cpp_guid_compare", 1, true, false, true, new[] { "g", "g" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("CompareTo(object) not wired: a boxed Guid has no runtime box class, so the boxed value cannot reach the icall");
                return null;
            case ".ctor":
                if (ps.Length == 1 && ps[0] == 0x1D)
                    throw new NotSupportedException(".ctor(byte[]) not wired: guid.h has no from-bytes icall (the surface is new_guid/parse/to_string/equals/compare only)");
                return null;
            default: return null;
        }
    }
}
