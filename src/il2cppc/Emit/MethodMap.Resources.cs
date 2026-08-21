namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Resources -- RES-3 (iron rule 18): embedded manifest resources.
// Owners: System.Reflection.Assembly (manifest queries) + System.Resources
// .ResourceManager (minimal .resources string lookup). il2cpp_resources.h.
//
// Data flow (RES-3): MetadataLoader.LoadManifestResources extracts the raw
// payloads; Program.RenderResourcePoolHeader emits include/internal/
// resource-pool.h (the T3 g_strPool table + il2cpp_resource_pool_fill ->
// il2cpp_resources_register). The three icalls below query THAT single-writer
// registry (D1). All results are managed references (byte[]/string[]/string);
// a missing resource returns null, never throws.
//
// RECEIVER CONVENTION -- Assembly queries are INSTANCE members whose
// receiver (the Assembly object) the single-assembly runtime ignores: the
// registry IS the manifest. The receiver slot is DROPPED (the "drop" cast on
// args[0], EmitIcallCallArgs) -- GetManifestResourceStream(name) emits
// il2cpp_assembly_get_manifest_resource_stream(name), GetManifestResourceNames()
// emits the zero-arg call.
//
// RESOURCEMANAGER FORWARD ABI (handle-gap mode, the MethodMap.Xml.cs
// precedent): GetString is instance on a ResourceManager receiver, but the
// runtime icall needs the resolved "<BaseName>.resources" byte[] blob
// (il2cpp_resources.h:85 -- il2cpp_resource_manager_get_string(resourcesBlob,
// name)). The receiver passes through UNCAST as the blob slot -- the
// ResourceManager ctor wiring (the emitter-side work that resolves the base
// name + ".resources" to the blob and stores it INTO the receiver local, like
// XmlDocument.LoadXml's handle-gap store) is the prerequisite. Until that
// ctor mapping lands, a ResourceManager receiver is only reachable through
// newobj + the unwired .ctor, so no generated program can hit this with a
// non-blob receiver.
//
// INCLUDE CHAIN NOTE (CG-FIX): il2cpp_resources.h is NOT yet in
// CodeGenerator's generated.h include list -- the icall declarations need
// that one include line before any resource call site compiles (same gap as
// the resource-pool.h include the res3 report documented). Out of this
// file's domain.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapResources(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Reflection.Assembly":
                switch (name)
                {
                    // byte[] GetManifestResourceStream(string): the raw payload of
                    // the named manifest resource (unknown name -> null). Instance:
                    // the Assembly receiver is DROPPED (single-assembly registry);
                    // the name passes through uncast (the runtime param is
                    // Il2CppObject*, already a string reference).
                    case "GetManifestResourceStream":
                        if (ps.Length != 1 || ps[0] != 0x0E) return null;
                        return I("il2cpp::il2cpp_assembly_get_manifest_resource_stream", 1, true, true, true,
                            new[] { "drop", "" });
                    // string[] GetManifestResourceNames(): the registered names in
                    // table order (empty pool -> zero-length array). Instance:
                    // receiver dropped, zero-arg call.
                    case "GetManifestResourceNames":
                        if (ps.Length != 0) return null;
                        return I("il2cpp::il2cpp_assembly_get_manifest_resource_names", 0, true, true, true,
                            new[] { "drop" });
                    // Any other System.Reflection.Assembly member has no runtime
                    // primitive on the il2cpp_resources.h surface (GetType/
                    // GetExecutingAssembly/... ) -- fall through to the
                    // generated-name path (LOUD-gated by the assembly's wiring).
                    default:
                        return null;
                }
            case "System.Resources.ResourceManager":
                switch (name)
                {
                    // string GetString(string): the minimal .resources parser
                    // (Magic 0xBEEFCACE + name/value tables). Instance: the
                    // receiver IS the resourcesBlob slot (handle-gap forward ABI,
                    // see the file header -- the ctor wiring stores the resolved
                    // blob into the receiver local); the key passes through uncast.
                    //
                    // RM-CULT (culture fallback): ILC pins the INVARIANT culture
                    // (numberformat.h/MethodMap.NumberFormat.cs contract), so the
                    // culture-aware net48 path collapses to the invariant blob:
                    // GetString resolves "<BaseName>.resources" and the default
                    // fallback IS the satellite lookup. The full fallback chain
                    // (specific -> parent... -> invariant, net48 semantics,
                    // satellite = culture-suffixed manifest resources in the
                    // single-writer registry) is implemented as the runtime
                    // primitive il2cpp_resources_find_string_fallback(baseName,
                    // cultureName, key, ...) -- see il2cpp_resources.h RM-CULT.
                    // It is the wiring point for a future GetString(string,
                    // CultureInfo) / culture-aware ctor path; until then every
                    // generated call is the invariant path below.
                    case "GetString":
                        if (ps.Length != 1 || ps[0] != 0x0E) return null;
                        return I("il2cpp::il2cpp_resource_manager_get_string", 1, true, true, true,
                            new[] { "", "" });
                    default:
                        return null;
                }
            default:
                return null;
        }
    }
}
