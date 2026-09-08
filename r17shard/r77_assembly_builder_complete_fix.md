# r77 System.Reflection.Emit.AssemblyBuilder cannot-cast Error Analysis

**Date**: 2026-09-06
**File**: `E:\il2cpp\out\mscorlib_gen_r77\src\System_Reflection_Emit_AssemblyBuilder.cpp`
**Total errors**: 19+ (including cascading)
**Root cause**: Value-type/enum fields being assigned to int32_t locals instead of their struct types, and int32_t enumerator results used where Il2CppObject* is expected.

---

## Error Summary

### Error Pattern 1: `il2cpp_list_i4_enumerator_get_current` returns int32_t assigned to Il2CppObject*

**Locations**: Lines 483, 837

**Generated code**:
```cpp
// Line 483 (GetModuleBuilder_eb0b525559c30004)
int32_t loc3;
loc3 = il2cpp::il2cpp_list_i4_enumerator_get_current(
    (*reinterpret_cast<Il2CppObject**>((&loc2))));  // OK: returns int32_t

// Line 837 (SetCustomAttribute related - in a different method)
int32_t loc9;
loc9 = il2cpp::il2cpp_list_i4_enumerator_get_current(
    (*reinterpret_cast<Il2CppObject**>((&loc8))));  // OK: returns int32_t

// Line 838 - ERROR:
System_Reflection_Emit_AssemblyBuilder_SetCustomAttribute_603a254b2e8bfb8c(arg0, loc9);
// SetCustomAttribute expects Il2CppObject* but receives int32_t loc9
```

**Root cause**: The `il2cpp_list_i4_enumerator_get_current` returns `int32_t` (the list item type), but `SetCustomAttribute` expects `Il2CppObject*`. This is because the enumerated list contains `System.Runtime.InteropServices.CustomAttributeBuilder` objects, not `int32_t`.

The type mismatch happens because:
1. The IL stack type tracking recorded the list as `System.Collections.Generic.List<int>` instead of `System.Collections.Generic.List<object>`
2. Or: the method signature mismatch -- the `get_current` icall for list objects returns Il2CppObject*, but the generated code routes through the int32 list variant

**The real root cause**: At line 837, `loc9` is declared as `int32_t` but it should be `Il2CppObject*`. The C# source code enumerates a `List<CustomAttributeBuilder>` but the transpiler inferred the wrong list type from metadata.

**Fix**: Change the local declaration from `int32_t loc9` to `Il2CppObject* loc9` at line 648.

---

### Error Pattern 2: `m_con` field on CustomAttributeBuilder is int32_t instead of Il2CppObject*

**Locations**: Lines 485, 740, 746, 750, 751, 752, 755, 763, 766, 771, 777, 781, 783

**Generated code**:
```cpp
// Line 485
(reinterpret_cast<System_Reflection_Emit_ModuleBuilder*>(
    il2cpp_null_check(static_cast<Il2CppObject*>(loc3))))->m_internalModuleBuilder

// Line 740
(reinterpret_cast<System_Reflection_Emit_CustomAttributeBuilder*>(
    il2cpp_null_check(static_cast<Il2CppObject*>(loc5))))->m_con
```

**Root cause**: The `m_con` field on `CustomAttributeBuilder` is declared as `int32_t` in the generated struct, but it should be `Il2CppObject*` (it holds a `System.Type` reference). The struct layout in `generated.h` has the wrong type for this field.

**Fix**: Update the struct definition in `generated.h` to declare `m_con` as `Il2CppObject*` instead of `int32_t`.

---

### Error Pattern 3: `m_blob` field on CustomAttributeBuilder is int32_t instead of Il2CppArray*

**Locations**: Lines 766, 781

**Generated code**:
```cpp
// Line 766
il2cpp::il2cpp_array_get_length(reinterpret_cast<Il2CppArray*>(
    reinterpret_cast<Il2CppObject*>(((reinterpret_cast<System_Reflection_Emit_CustomAttributeBuilder*>(
    il2cpp_null_check(static_cast<Il2CppObject*>(loc5)))))->m_blob)));
```

**Root cause**: The `m_blob` field is declared as `int32_t` in the generated struct, but it should be `Il2CppArray*` (or `Il2CppObject*`).

**Fix**: Update the struct definition in `generated.h` to declare `m_blob` as `Il2CppArray*`.

---

### Error Pattern 4: `m_constructorArgs` field on CustomAttributeBuilder is int32_t instead of Il2CppArray*

**Locations**: Lines 750, 751, 752, 755

**Root cause**: Same as Pattern 3 -- `m_constructorArgs` should be `Il2CppArray*`.

---

### Error Pattern 5: `SetModuleName` and `PrepareForSavingManifestToDisk` P/Invoke signature mismatch

**Locations**: Lines 1243, 3883, 4510

**Generated code**:
```cpp
// Line 4510 (generated.h)
extern "C" void PrepareForSavingManifestToDisk(int32_t, int32_t);
extern "C" void SetModuleName(int32_t, int32_t);

// Line 3883 (usage)
PrepareForSavingManifestToDisk(t0, loc12);  // t0 is Il2CppObject*, loc12 is int32_t
```

**Root cause**: The P/Invoke declarations use `int32_t` for the parameters, but the actual native functions expect `Il2CppObject*` (the RuntimeAssembly/RuntimeModule handles).

**Fix**: Update the P/Invoke declarations to use `Il2CppObject*` parameters.

---

### Error Pattern 6: `MethodToken` value type assigned to int32_t

**Locations**: Lines 3372, 3373, 3557, 3559

**Generated code**:
```cpp
// Line 3372
System_Reflection_Emit_MethodToken loc2;
loc2 = System_Reflection_Emit_ModuleBuilder_GetMethodToken_17ed056ac197ffc5(...);

// Line 3373
int32_t t0;
t0 = loc2;  // ERROR: cannot assign MethodToken to int32_t
```

**Root cause**: `MethodToken` is a value type (struct) but the code tries to assign it to an `int32_t` local.

**Fix**: Either:
1. Change the local type to `System_Reflection_Emit_MethodToken`
2. Or add a conversion: `t0 = static_cast<int32_t>(loc2.token);` (if MethodToken has a token field)

---

### Error Pattern 7: `StringHandleOnStack` value type assigned to int32_t

**Locations**: Lines 3900, 4534

**Generated code**:
```cpp
// Line 4534 (generated.h)
extern "C" void CreateVersionInfoResource(..., System_Runtime_CompilerServices_StringHandleOnStack);

// Line 3900
System_Runtime_CompilerServices_StringHandleOnStack t2;
t2 = System_Runtime_CompilerServices_JitHelpers_GetStringHandleOnStack_25b0c16f214bff26((&loc11));
```

**Root cause**: The `StringHandleOnStack` parameter is passed by reference in the native declaration, but the generated code passes it by value.

**Fix**: Pass `(&t2)` instead of `t2` to the native function.

---

## Fix Priority

| Priority | Pattern | Count | Fix Location |
|----------|---------|-------|--------------|
| 1 | `m_con` field type wrong | 12 | `generated.h` struct definition |
| 2 | `m_blob` field type wrong | 2 | `generated.h` struct definition |
| 3 | `m_constructorArgs` field type wrong | 4 | `generated.h` struct definition |
| 4 | `loc9` type wrong (enumerator) | 1 | `AssemblyBuilder.cpp:648` |
| 5 | `MethodToken` assignment | 4 | `AssemblyBuilder.cpp:3373,3557` |
| 6 | P/Invoke signature mismatch | 2 | `generated.h:4510,4534` |
| 7 | `StringHandleOnStack` pass-by-value | 1 | `AssemblyBuilder.cpp:3900` |

---

## Recommended Fix Strategy

### Option A: Fix the struct definitions in generated.h (Recommended)

The root cause is that the struct layout generator in `TypeEmitter.EmitClassStruct` is assigning wrong types to fields. The fields `m_con`, `m_blob`, `m_constructorArgs` on `CustomAttributeBuilder` should be `Il2CppObject*`/`Il2CppArray*` but are being emitted as `int32_t`.

**Fix location**: `E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs` - the `EmitClassStruct` method's field type resolution logic.

### Option B: Fix the local variable types in the generated code

If the struct definitions are correct, then the issue is in `CodeGenerator.EmitBody` where local variable types are inferred from the IL stack tracking. The `loc9` at line 648 should be `Il2CppObject*` not `int32_t`.

**Fix location**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` - the `EmitBody` method's local type inference.

### Option C: Add runtime-level field offset fixups

If the struct layout cannot be changed (e.g., due to metadata constraints), add runtime-level accessor methods that return the correct pointer type.

**Fix location**: `E:\il2cpp\src\runtime\include\internal\reflect_emit.h` (or similar).

---

## Appendix: Full Error List

```
Line 485: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 486: incompatible integer to pointer conversion assigning to 'Il2CppObject *' from 'int32_t'
Line 740: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' (x2)
Line 746: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' (x2)
Line 750: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 751: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 752: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 755: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 763: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' (x2)
Line 766: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 768: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 771: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' (x2)
Line 777: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *' (x2)
Line 781: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 783: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
Line 838: no matching function for call to 'SetCustomAttribute'
Line 1243: no matching function for call to 'SetModuleName'
Line 3372: assigning to 'int32_t' from incompatible type 'MethodToken'
Line 3373: no viable overloaded '='
Line 3557: assigning to 'int32_t' from incompatible type 'MethodToken'
Line 3559: member reference base type 'int32_t' is not a structure or union
Line 3883: no matching function for call to 'PrepareForSavingManifestToDisk'
Line 3900: assigning to 'int32_t' from incompatible type 'StringHandleOnStack'
```
