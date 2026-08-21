namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Uri -- System.Uri mappings
// (src/runtime/include/internal/versionuri.h -- icall_uri_* entry points).
// Owners: System.Uri
//
// RUNTIME OBJECT CLOSURE (M6 G6): System.Uri is now a managed object -- the
// runtime defines the System_Uri_class descriptor (Il2CppUriObject: header +
// scheme/host/path string-pointer slots, refFieldBitmap bits 1..3 --
// versionuri.h) and the object icalls below own the slot storage. Generated
// newobj allocates through il2cpp_object_new(&System_Uri_class) -- the
// BclAllocExpr fallback in CodeGenerator.cs emits exactly this mangled global
// symbol for System.Uri, so NO BclAllocExpr entry is required for the object
// to allocate (the "System.Uri" BclAllocExpr entry is a consistency
// convenience only -- and must NOT be added before the runtime descriptor
// exists, or il2cpp_object_new(&System_Uri_class) fails at link time).
//
//   .ctor(string)            -> icall_uri_ctor(Il2CppObject*, Il2CppObject*)
//                               (parse + store 3 string slots; malformed ->
//                               ArgumentException)
//   get_Scheme               -> icall_uri_get_scheme(Il2CppObject*) -> string
//   get_Host                 -> icall_uri_get_host   (Il2CppObject*) -> string
//   get_AbsolutePath         -> icall_uri_get_path   (Il2CppObject*) -> string
//                               (the simplified domain keeps the path verbatim
//                               from the first '/' on -- see versionuri.h)
//   ToString()               -> icall_uri_to_string(Il2CppObject*) -> string
//                               (rebuilds scheme://host/path verbatim)
//
// Members without an object icall (GetHashCode, the Uri(string, UriKind)
// ctor form, ...) stay LOUD -- wiring manual convention,
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
                // three parts as managed strings in the object slots.
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_uri_ctor", 1, false, false, true);
                throw new NotSupportedException($"Uri ctor with {count} param(s) not wired (only the single-string form has a runtime parse primitive)");
            case "get_Scheme":
                return I("il2cpp::runtime::icall_uri_get_scheme", 0, true, true, true);
            case "get_Host":
                return I("il2cpp::runtime::icall_uri_get_host", 0, true, true, true);
            case "get_AbsolutePath":
                return I("il2cpp::runtime::icall_uri_get_path", 0, true, true, true);
            case "ToString":
                if (count == 0) return I("il2cpp::runtime::icall_uri_to_string", 0, true, true, true);
                return null;
            default:
                throw new NotSupportedException(
                    "System.Uri member not wired: the object icalls cover " +
                    ".ctor(string)/get_Scheme/get_Host/get_AbsolutePath/ToString only " +
                    "(see versionuri.h)");
        }
    }
}
