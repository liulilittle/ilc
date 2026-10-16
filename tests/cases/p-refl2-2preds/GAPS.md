# GAPS.md — p-refl2-2preds (refl2 item 2)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: A-class type predicates on the name-string (Type VALUE) receiver

- **Phenomenon**: `get_IsArray/IsEnum/IsValueType/IsInterface/IsClass/
  IsPrimitive/HasElementType` + `GetElementType` mapped to the descriptor
  primitives (`il2cpp_type_is_array(Il2CppClass*)` etc.). The transpiler Type
  VALUE is the FULL-NAME string (the simplified Type model), so the emitted
  C++ passed a managed string where a descriptor was required -> cmake build
  failed (COMPILE-BROKEN). Additionally `typeof(int[])` folded to the bogus
  "TaskAwaiter`1" name (TypeSpecName SZARRAY fallback), so even a compile
  would have mis-answered the array predicates.
- **Fix**:
  - StackFold TypeNameForReflection: a SZARRAY typeof token (0x1D) folds to the
    ARRAY DESCRIPTOR name ("[System.Int32" -- the array.cpp identity), matching
    the runtime GetType()/class_by_name key; jagged recurse + multidim decode.
  - MethodMap.Type.cs: rewire the eight predicates to new name-string icalls
    `il2cpp_typeinfo_is_array/is_enum/is_value_type/is_interface/is_class/
    is_primitive/has_element_type/get_element_type`.
  - runtime typeinfo.cpp: name/typeinfo-decided predicates (no descriptor):
    arrays by the '['-prefix; primitives by the .NET primitive full-name set;
    enum by the typeinfo "System.Enum" base terminal; value-type by primitive +
    reflected isValueType/"System.Enum"; class = NOT value type / NOT interface;
    interface by the zero-instanceSize klass; element-type by '['-strip.
- **RED**: cmake build failed (name-string to Il2CppClass*).
- **GREEN**: all 29 records byte-exact. net48 pinned: enum IsValueType True.

## Known simplification (W7)

- The `GetElementType()` results are compared with `==` (Type.op_Equality ->
  string identity); `ReferenceEquals` on a runtime-produced element type vs a
  folded typeof is pointer-inequality in this model (fresh string vs pool) --
  net48 RuntimeType reference-equals there. The probe uses `==` (net48 True).