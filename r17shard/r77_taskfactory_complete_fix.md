# R77 TaskFactory Complete Fix Analysis

## Executive Summary

- **Total unique missing types:** 96
- **Total errors:** 167
- **Root Cause:** Generic type instantiation mismatch in C++ name emission
- **Key Pattern:** Transpiler emits `_int` suffix on generic type parameters that should not have it

---

## Error Breakdown by Category

### Category 1: Static Field Owner Mismatch (12 errors)

| Missing Type | Suggested Type |
|-------------|----------------|
| `TaskFactory_1_int_sf_s_completeFromAsyncResult` | `TaskFactory_1_FromAsyncTrimPromise_1_int_sf_s_completeFromAsyncResult` |

**Root Cause:** `s_completeFromAsyncResult` is a static field defined on `TaskFactory_1<FromAsyncTrimPromise<T>>`, but the transpiler looks for it on `TaskFactory_1<T>`.

**Example Error:**
```
error: use of undeclared identifier 'System_Threading_Tasks_TaskFactory_1_int_sf_s_completeFromAsyncResult'; 
did you mean 'System_Threading_Tasks_TaskFactory_1_FromAsyncTrimPromise_1_int_sf_s_completeFromAsyncResult'?
```

**Fix Location:** `CodeGenerator.cs` - `EmitExpr` for `StaticFieldExpr`
**Fix:** Before emitting static field access, resolve the actual closed type where the field is defined.

```csharp
// In CodeGenerator.cs, EmitExpr for StaticFieldExpr:
case StaticFieldExpr sf:
{
    var fieldName = sf.FieldName;
    var ownerFullName = sf.OwnerFullName;
    
    // FIX: Resolve actual owner type for generic static fields
    var actualOwner = ResolveActualStaticFieldOwner(ownerFullName, fieldName);
    var storageName = FieldStorageName($"{actualOwner}.{fieldName}");
    
    return storageName;
}

/// <summary>
/// Resolves the actual owner type for a static field when the field is defined
/// on a different closed generic type than expected.
/// Example: TaskFactory`1.s_completeFromAsyncResult is defined on 
/// TaskFactory`1+FromAsyncTrimPromise`1&lt;T&gt;, not TaskFactory`1&lt;T&gt;.
/// </summary>
private static string ResolveActualStaticFieldOwner(string ownerFullName, string fieldName)
{
    if (ownerFullName.StartsWith("System.Threading.Tasks.TaskFactory`1") 
        && fieldName == "s_completeFromAsyncResult")
    {
        // The static field lives on the nested FromAsyncTrimPromise`1 class
        return "System.Threading.Tasks.TaskFactory`1+FromAsyncTrimPromise`1";
    }
    return ownerFullName;
}
```

---

### Category 2: Generic Parameter Naming Mismatch (84+ errors)

**This is the dominant pattern - accounts for most of the 167 errors.**

**Missing Pattern:** `TaskFactory_1___c_<Type>_int_sf___9` and similar
**Suggested Pattern:** `TaskFactory_1___c_<Type>_sf___9` (no `_int`)

| Count | Missing Pattern | Suggested Pattern |
|-------|-----------------|-------------------|
| 5 each (6 types) | `___c_<T>_int_sf___9` | `___c_<T>_sf___9` |
| 3 each (18 types) | `___c_<T>_int_sf___9__NN_N` | `___c_<T>_sf___9__NN_N` |

**Affected Types:**
- `TaskFactory_1___c_object_array_int` → `TaskFactory_1___c_object_array`
- `TaskFactory_1___c_object_int` → `TaskFactory_1___c_object`
- `TaskFactory_1___c_string_int` → `TaskFactory_1___c_string`
- `TaskFactory_1___c_System_Threading_Tasks_Task_array_int` → `TaskFactory_1___c_System_Threading_Tasks_Task_array`
- `TaskFactory_1___c_System_Threading_Tasks_VoidTaskResult_int` → `TaskFactory_1___c_System_Threading_Tasks_VoidTaskResult`
- `TaskFactory_1_int_ctor_*` → `TaskFactory_1_*` (constructor closures)

**Root Cause:** The `TypeCppName` function in `TypeEmitter.cs` appends `_int` as a suffix for value type generic parameters. However, the actual emitted C++ types use a different naming convention where the `_int` suffix is NOT appended.

**Example Error:**
```
error: use of undeclared identifier 'System_Threading_Tasks_TaskFactory_1___c_object_array_int_sf___9'; 
did you mean 'System_Threading_Tasks_TaskFactory_1___c_object_array_sf___9'?
```

**Fix Location:** `TypeEmitter.cs` - `TypeCppName` function

**Fix:** The `_int` suffix should not be appended to the generic type name when forming the C++ type name. The suffix is only needed for the default instantiation marker, not for the generic parameter itself.

```csharp
// In TypeEmitter.cs, TypeCppName function:
public static string TypeCppName(TypeDef t)
{
    // ... existing generic type handling ...
    
    if (t.GenericParameters.Count > 0 || t.Name.Contains('`'))
    {
        var baseName = Sanitize(t.Name);
        var sb = new StringBuilder(baseName);
        
        foreach (var gp in t.GenericParameters)
        {
            var argName = GenericParamCppName(gp);
            
            // FIX: Don't append "_int" to the generic parameter name
            // The "_int" suffix was incorrectly being added for value type params
            // but the actual emitted types don't include it in their names
            sb.Append($"_");
            sb.Append(argName);
        }
        
        return sb.ToString();
    }
    
    return Sanitize(t.Name);
}

/// <summary>
/// Gets the C++ name for a generic parameter without the erroneous "_int" suffix.
/// </summary>
private static string GenericParamCppName(GenericParameter gp)
{
    // Resolve the parameter to its actual type name
    // For value types, the name should NOT have "_int" appended
    var typeRef = ResolveGenericParameterType(gp);
    return Sanitize(typeRef);
}
```

---

### Category 3: Closure Type Instantiation (60+ errors)

**Pattern:** Display class closures with wrong generic instantiation

| Count | Pattern |
|-------|---------|
| 3 each | `___c__DisplayClassNN_0_<T>_int_sf___9__MM_M` |

**Root Cause:** Same as Category 2 - the `_int` suffix is being appended to the closure type name, but the actual emitted closure type doesn't have it.

**Fix:** Same fix as Category 2.

---

### Category 4: Entry.cpp Undeclared Closures (4 errors)

**Pattern:** `TaskFactory_1___c__DisplayClass32_0_<Type>__FromAsyncImpl_b__N`

**Root Cause:** The transpiler is using the wrong type parameter for the closure class. It uses the actual type parameter (e.g., `EventRegistrationToken`) but the closure is defined on `VoidTaskResult`.

**Example:**
```
error: use of undeclared identifier 'System_Threading_Tasks_TaskFactory_1___c__DisplayClass32_0_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken__FromAsyncImpl_b__0_...'; 
did you mean 'System_Threading_Tasks_TaskFactory_1___c__DisplayClass32_0_System_Threading_Tasks_VoidTaskResult__FromAsyncImpl_b__0_...'?
```

**Root Cause:** The `FromAsyncImpl` method uses `Task<VoidTaskResult>` internally regardless of the outer type parameter. The closure class is defined on `TaskFactory<VoidTaskResult>`, not `TaskFactory<T>`.

**Fix Location:** `MethodMap.Task.cs` or `MethodEmitter.cs`

**Fix:** When mapping `FromAsyncImpl` methods, use `VoidTaskResult` as the type parameter for the closure class.

```csharp
// In MethodMap.Task.cs or relevant mapping:
// For TaskFactory.FromAsyncImpl closures, the closure type is always
// TaskFactory_1___c__DisplayClassNN_0_VoidTaskResult__FromAsyncImpl_b__M
// regardless of the outer type parameter.
```

---

## Implementation Plan

### Step 1: Fix Generic Parameter Naming (Addresses 84+ errors)

**File:** `src/il2cppc/Emit/TypeEmitter.cs`
**Function:** `TypeCppName`

The fix is to stop appending `_int` to generic parameter names. The `_int` suffix is a default instantiation marker that should only appear at the end of the full type name, not within generic parameter lists.

### Step 2: Fix Static Field Owner Resolution (Addresses 12 errors)

**File:** `src/il2cppc/Emit/CodeGenerator.cs`
**Function:** `EmitExpr` for `StaticFieldExpr`

Add a check to resolve `s_completeFromAsyncResult` to the `FromAsyncTrimPromise` type.

### Step 3: Fix FromAsyncImpl Closure Mapping (Addresses 4 errors)

**File:** `src/il2cppc/Emit/MethodMap.Task.cs`

Ensure closure types for `FromAsyncImpl` use `VoidTaskResult` as the type parameter.

---

## Type Parameter Analysis

### Missing Type Parameters (Top 10)

| Parameter | Count |
|-----------|-------|
| int | 12 |
| FromAsyncTrimPromise`1 | 11 |
| object | 5 |
| object[] | 5 |
| string | 5 |
| Task[] | 5 |
| VoidTaskResult | 5 |

The `FromAsyncTrimPromise` type is critical - it's the actual container for `s_completeFromAsyncResult` and related fields.

---

## Root Cause Summary

The 167 TaskFactory errors are caused by **two fundamental issues**:

1. **Generic parameter naming bug** (84+ errors): The `TypeCppName` function appends `_int` to value type generic parameters, but the actual emitted C++ types don't have this suffix. This causes a cascade of mismatches whenever a TaskFactory type is referenced.

2. **Static field owner resolution** (12 errors): The transpiler assumes static fields are defined on `TaskFactory<T>`, but `s_completeFromAsyncResult` is actually defined on `TaskFactory<FromAsyncTrimPromise<T>>`.

3. **Closure type mapping** (4 errors): `FromAsyncImpl` closures are defined on `TaskFactory<VoidTaskResult>` regardless of the outer type parameter.

## Files to Modify

| File | Change | Errors Fixed |
|------|--------|--------------|
| `src/il2cppc/Emit/TypeEmitter.cs` | Remove `_int` suffix from generic params | 84+ |
| `src/il2cppc/Emit/CodeGenerator.cs` | Fix static field owner resolution | 12 |
| `src/il2cppc/Emit/MethodMap.Task.cs` | Fix FromAsyncImpl closure mapping | 4 |
