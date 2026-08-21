namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Resource -- resmgmt/resops wiring (b23): the ResourceManager
// construction path + the culture-aware GetString overload + the supporting
// System.Type.get_Assembly / System.Globalization.CultureInfo ctor folds.
// Runtime surface: il2cpp_resources.h (registry + parser + RM-CULT) +
// il2cpp_resource_manager_ctor_resolve (resourcemanager.cpp).
//
// HANDLE-GAP MODE (the XmlDocument.LoadXml precedent): ResourceManager is NOT
// an allocated object. The RM ctor resolves the INVARIANT
// "<BaseName>.resources" blob and stores it INTO the receiver local
// (ResultIsReceiver -- the runtime ctor icall RETURNS the byte[] blob). Every
// GetString(receiver, key) then passes the receiver as its resourcesBlob
// slot (the existing 1-arg mapping in MethodMap.Resources.cs).
//
//   .ctor(string baseName, Assembly assembly)
//       -> il2cpp_resource_manager_ctor_resolve(baseName, assembly)
//          (ResultIsReceiver: the blob lands in the receiver local; the
//          assembly arg is DROPPED -- the single-assembly registry IS the
//          manifest)
//   GetString(string, CultureInfo)
//       -> il2cpp_resource_manager_get_string(receiver, name)
//          (2-arg overload: the culture arg is DROPPED -- ILC pins the
//          invariant culture, and the case's no-satellite model falls back to
//          the invariant blob the ctor resolved; net48 byte-identical)
//   System.Type::get_Assembly
//       -> folds to null (the RM ctor's assembly argument is unused; typeof(X)
//          is only ever consumed there)
//   System.Globalization.CultureInfo::.ctor(string)
//       -> il2cpp_object_ctor_nop (the CultureInfo object is only ever the
//          DROPPED GetString culture argument -- never dereferenced)
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapResourceManager(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Type":
                // typeof(X).Assembly: fold to null. The ResourceManager ctor
                // drops the assembly argument (the registry IS the manifest),
                // so the get_Assembly result is never consumed. IsInstance=true:
                // the member is a callvirt (property getter on a Type receiver).
                if (name == "get_Assembly" && count == 0)
                    return new ResolvedMethod(ResolvedMethod.NullMarker, 0, false, true, true,
                        Array.Empty<bool>(), true, ReturnTypeName: "System.Reflection.Assembly");
                return null;
            case "System.Resources.ResourceManager":
                switch (name)
                {
                    // new ResourceManager(baseName, assembly): resolve the
                    // invariant blob (ResultIsReceiver -- the blob replaces the
                    // receiver local; the assembly arg passes through to the
                    // runtime which ignores it).
                    case ".ctor":
                        if (count == 2 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_ctor_resolve", 2, true, true, true,
                                new[] { "", "", "drop" }, resultIsReceiver: true,
                                returnTypeName: "System.Resources.ResourceManager");
                        return null;
                    // GetString(string, CultureInfo): invariant-pinned model --
                    // the culture is dropped, the lookup runs on the invariant
                    // blob (the receiver, resolved by the ctor).
                    case "GetString":
                        if (count == 2 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_get_string", 2, true, true, true,
                                new[] { "", "", "drop" });
                        return null;
                    default:
                        return null;
                }
            case "System.Globalization.CultureInfo":
                // new CultureInfo(name): no-op ctor (the CultureInfo object is
                // only ever the dropped GetString culture argument).
                if (name == ".ctor" && count == 1 && ps[0] == 0x0E)
                    return new ResolvedMethod("il2cpp::runtime::il2cpp_object_ctor_nop", 1, false, false, false,
                        new[] { false, false }, true, ParamTypeNames: new[] { "drop", "drop" });
                return null;
            default:
                return null;
        }
    }
}
