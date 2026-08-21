# GAPS.md -- resmgmt unwired surface (e2e batch C15, 2026-08-09)

Case status: **FAIL at cmake_build** (net48 oracle 11-line PASS; ILC
transpile OK + resource-pool.h emitted; generated C++ fails to compile with
12 undeclared-identifier errors). This is a FORWARD GATE: it becomes green
when the ResourceManager construction path is wired. Every gap below was
VERIFIED empirically (fixtures/resx transpile + build probe + this case).

## D-RES-1 -- Type.get_Assembly not wired

- **Phenomenon**: `typeof(Resmgmt).Assembly` (the `new ResourceManager(
  baseName, assembly)` second argument) generates a call to
  `System_Type_get_Assembly_4d7b8078f2cb1c4e` which has no definition in
  the runtime / generated code -> "use of undeclared identifier".
- **Causal chain**: no MethodMap entry maps System.Type.get_Assembly to an
  icall; the call falls through to the generated-name fallback (an extern
  declaration with no definition). The single-assembly AOT model has no
  Assembly object, so a folded icall (return the manifest) would be the
  wiring shape.
- **Suspected root file**: MethodRefResolver.cs Type dispatch /
  MethodMap (System.Type / System.Reflection.Assembly).

## D-RES-2 -- System.Resources.ResourceManager class descriptor not emitted

- **Phenomenon**: the `new ResourceManager(...)` newobj emits
  `il2cpp_object_new(&System_Resources_ResourceManager_class)` but the
  class descriptor is undeclared -> compile error.
- **Causal chain**: the ResourceManager TypeDef is a BCL framework type (not
  in the assembly's own types); the type-descriptor emission pass
  (TypeEmitter) has no entry for it. Other BCL object classes (e.g.
  FileStream, StringReader) get class descriptors via their domain wiring;
  ResourceManager's was never registered.
- **Suspected root file**: TypeEmitter.cs / the runtime's BCL class
  descriptor registry.

## D-RES-3 -- ResourceManager .ctor not mapped (handle-gap forward ABI incomplete)

- **Phenomenon**: `System_Resources_ResourceManager__ctor_c18b51621c75182d`
  undeclared. MethodMap.Resources.cs documents this exact gap: "the
  ResourceManager ctor wiring (the emitter-side work that resolves the base
  name + '.resources' to the blob and stores it INTO the receiver local) is
  the prerequisite. Until that ctor mapping lands, a ResourceManager receiver
  is only reachable through newobj + the unwired .ctor".
- **Causal chain**: GetString's icall ABI
  (il2cpp_resource_manager_get_string(resourcesBlob, name)) needs the
  receiver local to BE the resolved blob; only the ctor can store it there.
  Without a ctor mapping the receiver is a bare ResourceManager newobj with
  no blob.
- **Suspected root file**: MethodMap.Resources.cs (ctor mapping) +
  CodeGenerator newobj/ctor path.

## D-RES-4 -- GetString(string, CultureInfo) 2-arg overload not mapped

- **Phenomenon**: every 2-arg `rm.GetString(name, culture)` call emits
  `System_Resources_ResourceManager_GetString_b88891751d346be4` undeclared.
  TryMapResources maps ONLY the 1-arg GetString (ps.Length==1 && ps[0]==0x0E);
  the 2-arg form falls through to the generated-name fallback.
- **Causal chain**: the RM-CULT runtime primitive
  (il2cpp_resources_find_string_fallback) exists but no transpiler entry
  wires the culture-aware overload to it. The culture argument is dropped in
  the invariant-pinned model.
- **Suspected root file**: MethodMap.Resources.cs GetString case (add the
  culture overload).

## D-RES-5 -- System.Globalization.CultureInfo unwired

- **Phenomenon**: `new CultureInfo("en-US")` emits
  `System_Globalization_CultureInfo_class` + `CultureInfo__ctor_*`
  undeclared.
- **Causal chain**: CultureInfo construction is not mapped; there is no
  culture object model in the runtime (the culture is pinned invariant).
- **Suspected root file**: MethodRefResolver CultureInfo dispatch.
