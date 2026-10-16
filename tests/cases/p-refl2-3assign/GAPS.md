# GAPS.md — p-refl2-3assign (refl2 item 3)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, LF-join diff).

## Fixed: IsAssignableFrom / IsInstanceOfType / IsSubclassOf name-string wrappers

- **Phenomenon**:
  - `IsAssignableFrom`/`IsInstanceOfType` passed the receiver NAME STRING (Type
    VALUE) to descriptor primitives (`il2cpp_type_is_assignable_from
    (Il2CppClass*)`, `icall_type_is_instance(Il2CppClass*)`) -> cmake build
    failed (COMPILE-BROKEN).
  - `IsSubclassOf` LOUD-rejected any call whose IL argument was not a tracked
    `typeof(...)` chain (an arg stored in a local first -> "untracked") AND
    would have hit the same descriptor-param compile error when wired.
- **Fix**:
  - runtime typeinfo.cpp: `il2cpp_typeinfo_is_assignable_from(targetName,
    sourceName)` / `il2cpp_typeinfo_is_instance(obj, typeName)` /
    `il2cpp_typeinfo_is_subclass_of(derivedName, baseName)` resolve each type
    NAME to a descriptor (typeinfo registry klass first, then
    `il2cpp_class_by_name` after `il2cpp_ensure_builtin_system_classes` for
    BCL/primitive/array types) and delegate to the existing conformance
    primitives.
  - MethodMap.Type.cs: rewire the three arms to the wrappers; `IsInstanceOfType`
    keeps the ArgOrder {1,0} swap (IL receiver-first vs the runtime
    obj-first); the `IsSubclassOf` untracked-arg LOUD gate is REMOVED (name
    strings resolve at runtime, no fold tracking needed).
  - net48 contracts preserved: IsAssignableFrom(null-source) -> False; 
    IsInstanceOfType(null) -> False; IsSubclassOf(null) -> ArgumentNullException.
- **RED**: runtime exit 1, all 19 records missing (IsSubclassOf untracked-arg
  LOUD -> Main skipped).
- **GREEN**: all 19 records byte-exact.

## Known simplification (W7)

- Types whose names resolve to neither registry (unreflected BCL/boxed types)
  yield a null descriptor -> the delegate primitive's defensive fallback
  (false, or the null-argument contracts where named).