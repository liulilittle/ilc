# GAPS.md — p-refl2-1types (refl2 item 1)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: Type.GetMethod(string, Type[]) / GetProperty(string, Type[])

- **Phenomenon**: the two-parameter arm in MethodMap.Type.cs matched ANY
  `ps[0]==0x0E` pair and dispatched to the no-longer-exclusive BindingFlags
  icall -- a Type[] (0x1D SZARRAY) argument rode the "i" int32 cast, so the
  emitted C++ did `static_cast<int32_t>(Il2CppArray*)` (invalid) and the cmake
  build failed (COMPILE-BROKEN).
- **Fix**:
  - MethodMap.Type.cs: split the 2-param arms on `ps[1]` -- 0x11 BindingFlags
    (the int32-riding enum) vs 0x1D Type[] (SZARRAY); the Type[] arms map to
    new icalls `il2cpp_typeinfo_get_method_types` / `il2cpp_typeinfo_get_property_types`.
  - runtime typeinfo.cpp: exact-signature matching -- the Type[]-declared arity
    + per-param TYPE IDENTITY (each Type[] element's full-name string vs the
    reflected member's encoded signature decoded to the canonical full name).
    Method params resolve through the transpiler "RET(PARAMS)" signature;
    property indexer params resolve through the getter's encoded signature
    (net48 property indexer authority). Default net48 binding
    Public|Instance|Static applies to both.
- **RED**: cmake build failed (invalid static_cast on the Type[] flag-arm).
- **GREEN**: all 14 records byte-exact.

## Known simplification (W7)

- The int32-family slot encoding conflates System.Int32/UInt32 (MethodSignature
  Coder "i") -- an int-vs-uint overload distinction is not expressible in the
  typeinfo signature, so the exact match treats them as one type.
- Type[] exact input strings are compared to the DECODED signature names; a
  net48 coercion (e.g. int->long under the default binder) is NOT reproduced
  (the probe's `G(string,int)` record relies on net48 NOT coercing in the
  tested direction).