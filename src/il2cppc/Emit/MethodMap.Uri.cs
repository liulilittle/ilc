namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Uri -- System.Uri mappings
// (src/runtime/include/internal/versionuri.h -- icall_uri_* entry points).
// Owners: System.Uri
//
// RUNTIME OBJECT CLOSURE (M6 G6): System.Uri is now a managed object -- the
// runtime defines the System_Uri_class descriptor (Il2CppUriObject: header +
// scheme/host/path/opaque/original string-pointer slots, refFieldBitmap bits
// 1..5, plus the isAbsolute flag -- versionuri.h) and the object icalls below
// own the slot storage. Generated newobj allocates through
// il2cpp_object_new(&System_Uri_class) -- the BclAllocExpr fallback in
// CodeGenerator.cs emits exactly this mangled global symbol for System.Uri,
// so NO BclAllocExpr entry is required for the object to allocate (the
// "System.Uri" BclAllocExpr entry is a consistency convenience only -- and
// must NOT be added before the runtime descriptor exists, or
// il2cpp_object_new(&System_Uri_class) fails at link time).
//
//   .ctor(string)            -> icall_uri_ctor(Il2CppObject*, Il2CppObject*)
//                               (parse + store slots + original; malformed ->
//                               ArgumentException)
//   .ctor(string, UriKind)   -> icall_uri_ctor_2(Il2CppObject*, Il2CppObject*,
//                               int32) (P1: Relative stores verbatim,
//                               Absolute parses, RelativeOrAbsolute falls back)
//   get_Scheme               -> icall_uri_get_scheme(Il2CppObject*) -> string
//   get_Host                 -> icall_uri_get_host   (Il2CppObject*) -> string
//   get_AbsolutePath         -> icall_uri_get_path   (Il2CppObject*) -> string
//                               (P4b: hierarchical absolute paths return the
//                               C3/C6-normalized path -- dot-folded, empty->"/",
//                               escapes preserved; opaque/relative stay verbatim)
//   get_OriginalString       -> icall_uri_get_originalstring (P1: verbatim)
//   get_AbsoluteUri          -> icall_uri_get_absoluteuri (P1: verbatim
//                               rebuild; escape-preserving normalization in P4)
//   get_IsAbsoluteUri        -> icall_uri_get_is_absolute_uri (P1: flag)
//   Equals(object)           -> icall_uri_equals (P1: value equality; also
//                               reached via Object::Equals on a Uri receiver
//                               -- see MethodMap.Object.cs)
//   GetHashCode()              -> icall_uri_get_hash_code (P3: deterministic
//                               ordinal contract hash -- accepted deviation
//                               from the net48 NLS integer; also reached via
//                               Object::GetHashCode on a Uri receiver)
//   ToString()               -> icall_uri_to_string(Il2CppObject*) -> string
//                               (normalized rebuild P3; relative storage
//                               returns the original text)
//
// Members without an object icall stay LOUD -- wiring manual convention,
// MethodRefResolver.cs: "known but unwireable members MUST throw".
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapUri(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // Uri(string): parse "scheme://host[/path]" and store the
                // parts as managed strings in the object slots.
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_uri_ctor", 1, false, false, true);
                // URI-FIX P1 (B3): Uri(string, UriKind) -- the kind is an
                // enum int32 carrier ("i", 0x08 or 0x11 -- the FileStream
                // precedent, MethodMap.StreamIO.cs:216-218); the receiver
                // rides as arg[0] (""). Relative stores verbatim,
                // Absolute parses, RelativeOrAbsolute falls back.
                if (count == 2 && ps[0] == 0x0E && ps[1] is 0x08 or 0x11)
                    return I("il2cpp::runtime::icall_uri_ctor_2", 2, false, false, true, new[] { "", "", "i" });
                // URI-BASE: Uri(Uri baseUri, string relativeUri) -- net48
                // base+relative resolution (System_net_System_URI.cs:452). The
                // base is a CLASS (0x12) Uri VALUE (an Il2CppObject*), the
                // relative rides as a managed string. The runtime combines the
                // base scheme/host with the base directory + relative text
                // (an absolute relative is stored verbatim).
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x0E)
                    return I("il2cpp::runtime::icall_uri_ctor_base_s", 2, false, false, true,
                        new[] { "", "", "" });
                // URI-BASE: Uri(Uri baseUri, Uri relativeUri) -- net48
                // (System_net_System_URI.cs:504). Both operands are Uri CLASS
                // values (0x12); the runtime reads the relative Uri's stored
                // absolute form through the object slots.
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_uri_ctor_base_u", 2, false, false, true,
                        new[] { "", "", "" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Uri ctor arity {count} -- net48 has only Uri(string), " +
                    "Uri(string, UriKind), Uri(Uri, string) and Uri(Uri, Uri) " +
                    "[Reference Source System_net_System_URI.cs:395,436,452,504]");
            case "get_Scheme":
                return I("il2cpp::runtime::icall_uri_get_scheme", 0, true, true, true);
            case "get_Host":
                return I("il2cpp::runtime::icall_uri_get_host", 0, true, true, true);
            case "get_AbsolutePath":
                return I("il2cpp::runtime::icall_uri_get_path", 0, true, true, true);
            // URI-FIX P1 (B2): the verbatim original text (OriginalString).
            case "get_OriginalString":
                return I("il2cpp::runtime::icall_uri_get_originalstring", 0, true, true, true);
            // URI-FIX P1 (B2): the absolute form (AbsoluteUri -- P1 rebuilds
            // verbatim; the escape-preserving normalization lands in P4).
            case "get_AbsoluteUri":
                return I("il2cpp::runtime::icall_uri_get_absoluteuri", 0, true, true, true);
            // URI-FIX P1 (B3): absolute vs relative-verbatim discriminator.
            case "get_IsAbsoluteUri":
                return I("il2cpp::runtime::icall_uri_get_is_absolute_uri", 0, true, false, true);
            // URI-FIX P1 (B2): value equality over the absolute form (see
            // also the Object::Equals Uri-receiver arm in MethodMap.Object.cs
            // -- Roslyn emits EqualsM as Object::Equals, evidence gen
            // Cases_Prog.cpp:379).
            case "Equals":
                if (count == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_uri_equals", 1, true, false, true);
                return null;
            // URI-FIX P3 (accepted deviation): deterministic ordinal
            // contract hash over the normalized absolute text (relative:
            // OriginalString) -- pairs with the ordinal Equals; never the
            // net48 NLS integer (see versionuri.h + URI-FIX-PLAN Addendum).
            case "GetHashCode":
                if (count == 0) return I("il2cpp::runtime::icall_uri_get_hash_code", 0, true, false, true);
                return null;
            case "ToString":
                if (count == 0) return I("il2cpp::runtime::icall_uri_to_string", 0, true, true, true);
                return null;
            default:
                throw new NotSupportedException(
                    "LOUD whitelist (no runtime descriptor/model): System.Uri member not wired -- the versionuri.h object icalls " +
                    "cover .ctor(string)/.ctor(string,UriKind)/get_Scheme/get_Host/get_AbsolutePath/get_OriginalString/" +
                    "get_AbsoluteUri/get_IsAbsoluteUri/Equals/GetHashCode/ToString only " +
                    "[Reference Source System_net_System_URI.cs:395-504]");
        }
    }
}
