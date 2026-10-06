namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Resource -- resmgmt/resops wiring (b23 + R2/R3): the ResourceManager
// construction path + the culture-aware GetString/GetObject/GetStream surface +
// the supporting System.Type.get_Assembly / System.Globalization.CultureInfo
// ctor folds. Runtime surface: il2cpp_resources.h (registry + parser +
// RM-CULT + typed values) + il2cpp_resource_manager_ctor_resolve /
// culture_from_name (resourcemanager.cpp).
//
// HANDLE-GAP MODE (the XmlDocument.LoadXml precedent): ResourceManager is NOT
// an allocated object. The RM ctor validates and stores the BASE NAME into the
// receiver local (ResultIsReceiver -- the runtime ctor icall RETURNS the base
// name string; R2: the culture-aware chain resolves "<baseName>.<culture>.
// resources" at query time, so the receiver must carry the name, not the
// pre-R2 invariant blob). Every GetString/GetObject/GetStream(receiver, key,
// [culture]) then passes the receiver as its baseName slot.
//
//   .ctor(string baseName, Assembly assembly)
//       -> il2cpp_resource_manager_ctor_resolve(baseName, assembly)
//          (ResultIsReceiver: the base name lands in the receiver local; the
//          assembly arg is PASSED THROUGH -- the runtime signature takes two
//          parameters and ignores the assembly internally, the single-
//          assembly registry IS the manifest)
//   GetString(string, CultureInfo)
//       -> il2cpp_resource_manager_get_string_culture(baseName, key, cultureName)
//          (R2: the net48 fallback chain [culture, parents..., invariant] over
//          the registry; the culture arg is the CultureInfo handle-gap name)
//   GetObject(string [, CultureInfo]) / GetStream(string [, CultureInfo])
//       -> il2cpp_resource_manager_get_object[_culture] /
//          il2cpp_resource_manager_get_stream[_culture]
//          (R3: typed values -- String(1)=string, ByteArray(32)=byte[],
//          Stream(33)=stream object)
//   new CultureInfo(string) -> il2cpp_resource_manager_culture_from_name
//       (R2 handle-gap: a CultureInfo IS its culture-name string -- receiver
//       local = the name; the invariant culture is the empty name. Same
//       convention as MethodMap.CultureInfo.cs get_InvariantCulture.)
//   System.Type::get_Assembly
//       -> folds to null (the RM ctor's assembly argument is unused; typeof(X)
//          is only ever consumed there)
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
                    // new ResourceManager(baseName, assembly): validate and
                    // store the BASE NAME (ResultIsReceiver -- the base name
                    // replaces the receiver local). The assembly argument is
                    // PASSED THROUGH.
                    case ".ctor":
                        if (count == 2 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_ctor_resolve", 2, true, true, true,
                                new[] { "", "", "" }, resultIsReceiver: true,
                                returnTypeName: "System.Resources.ResourceManager");
                        return null;
                    // GetString(string, CultureInfo): the culture-aware overload
                    // walks the net48 fallback chain (the culture arg is the
                    // CultureInfo handle-gap name string; the invariant culture
                    // is the empty name).
                    case "GetString":
                        if (count == 2 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_get_string_culture", 2, true, true, true,
                                new[] { "", "", "" });
                        return null;
                    // GetObject(string) / GetObject(string, CultureInfo): the
                    // typed object value (String/ByteArray/Stream by the entry's
                    // resgen type code).
                    case "GetObject":
                        if (count == 1 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_get_object", 1, true, true, true,
                                new[] { "", "" }, returnTypeName: "System.Object");
                        if (count == 2 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_get_object_culture", 2, true, true, true,
                                new[] { "", "", "" }, returnTypeName: "System.Object");
                        return null;
                    // GetStream(string) / GetStream(string, CultureInfo): a
                    // Stream(33) resource as a read-only MemoryStream.
                    case "GetStream":
                        if (count == 1 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_get_stream", 1, true, true, true,
                                new[] { "", "" }, returnTypeName: "System.IO.Stream");
                        if (count == 2 && ps[0] == 0x0E)
                            return I("il2cpp::il2cpp_resource_manager_get_stream_culture", 2, true, true, true,
                                new[] { "", "", "" }, returnTypeName: "System.IO.Stream");
                        return null;
                    default:
                        return null;
                }
            case "System.Globalization.CultureInfo":
                // new CultureInfo(name): handle-gap -- the CultureInfo object IS
                // its culture-name string (the receiver local stores the name);
                // the name is what the GetString/GetObject/GetStream culture chain
                // consumes. The invariant culture is the empty name ("").
                if (name == ".ctor" && count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_resource_manager_culture_from_name", 1, true, true, true,
                        new[] { "", "" }, resultIsReceiver: true,
                        returnTypeName: "System.Globalization.CultureInfo");
                return null;
            default:
                return null;
        }
    }
}
