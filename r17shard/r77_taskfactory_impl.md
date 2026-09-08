# R77 TaskFactory Implementation Fix

## Root Cause Analysis

The 167 TaskFactory errors are caused by **two bugs** in the transpiler's type name emission:

1. **`_int` suffix incorrectly appended to non-generic nested types** (84+ errors)
2. **Static field owner type resolution for `FromAsyncTrimPromise`** (12 errors)

---

## Bug 1: `_int` Suffix on Non-Generic Nested Types

### The Problem

In `TypeEmitter.cs:410-417`:
```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    return t.GenericArity > 0 ? baseName + "_int" : baseName;
}
```

This appends `_int` to ANY type with `GenericArity > 0`. But nested closure types within a generic type (e.g., `___c_object_array` inside `TaskFactory_1<object[]>`) are **not** generic definitions themselves - they're closed types. They should NOT have `_int` appended.

### Evidence

| Missing (Buggy) | Suggested (Correct) |
|-----------------|---------------------|
| `TaskFactory_1___c_object_array_int_sf___9` | `TaskFactory_1___c_object_array_sf___9` |
| `TaskFactory_1___c_object_int_sf___9` | `TaskFactory_1___c_object_sf___9` |
| `TaskFactory_1___c_string_int_sf___9` | `TaskFactory_1___c_string_sf___9` |
| `TaskFactory_1___c_System_Threading_Tasks_VoidTaskResult_int_sf___9` | `TaskFactory_1___c_System_Threading_Tasks_VoidTaskResult_sf___9` |

### The Fix

In `TypeEmitter.cs`, the `TypeCppName` function needs to distinguish between:
- A generic DEFINITION (e.g., `TaskFactory<T>`) → append `_int`
- A closed type NESTED in a generic (e.g., `TaskFactory<object[]>.___c_object_array`) → do NOT append `_int`

```csharp
// In TypeEmitter.cs, replace TypeCppName:
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    
    // M3 generics (Task 7): a generic definition emits ONE instantiation
    // with T -> int32_t. The instantiation name appends "_int" (the
    // value-type specialization marker; iron rule 2g -- no C++ templates).
    // 
    // FIX r77: Only append "_int" for the OUTERMOST generic definition,
    // not for non-generic nested types within a generic type.
    // A nested closure type like TaskFactory_1<object[]>.___c_object_array
    // should NOT have "_int" appended.
    if (t.GenericArity > 0 && !IsNestedInGeneric(t))
    {
        return baseName + "_int";
    }
    
    // For nested types within a generic, use the base name without "_int"
    // because the generic parameter is already encoded in the nested type name
    return baseName;
}

/// <summary>
/// Determines if a type is nested inside a generic type definition.
/// </summary>
private static bool IsNestedInGeneric(TypeDef t)
{
    return t.IsNested && t.DeclaringTypeName.Contains('`');
}
```

### Alternative Fix (More Targeted)

Instead of modifying `TypeCppName`, fix the specific reference sites:

```csharp
// In CodeGenerator.cs, when emitting static field references on nested types:
// Before (broken):
var storageName = FieldStorageName($"{ownerFullName}.{fieldName}");

// After (fixed):
// Strip the incorrect "_int" suffix from nested type names
var cleanOwner = StripNestedGenericSuffix(ownerFullName);
var storageName = FieldStorageName($"{cleanOwner}.{fieldName}");

private static string StripNestedGenericSuffix(string fullName)
{
    // Pattern: TaskFactory_1___c_<Type>_int_sf_<Field>
    // Remove the "_int" before "_sf_" for nested closure types
    return Regex.Replace(fullName, @"(_sf_)_int_", "$1_");
}
```

---

## Bug 2: Static Field Owner Resolution

### The Problem

`s_completeFromAsyncResult` is defined on `TaskFactory_1<FromAsyncTrimPromise<T>>`, but the transpiler looks for it on `TaskFactory_1<T>`.

### Evidence

```
Missing: System_Threading_Tasks_TaskFactory_1_int_sf_s_completeFromAsyncResult
Suggested: System_Threading_Tasks_TaskFactory_1_FromAsyncTrimPromise_1_int_sf_s_completeFromAsyncResult
```

### The Fix

```csharp
// In CodeGenerator.cs, add static field owner resolution:
private static string ResolveActualStaticFieldOwner(string ownerFullName, string fieldName)
{
    // TaskFactory`1.s_completeFromAsyncResult is defined on 
    // TaskFactory`1+FromAsyncTrimPromise`1<T>, not TaskFactory`1<T>
    if (ownerFullName.Contains("TaskFactory`1") 
        && fieldName == "s_completeFromAsyncResult")
    {
        return ownerFullName.Replace("TaskFactory`1", "TaskFactory`1+FromAsyncTrimPromise`1");
    }
    return ownerFullName;
}
```

---

## Bug 3: FromAsyncImpl Closure Type Parameters (4 errors)

### The Problem

`FromAsyncImpl` closures use the wrong type parameter. The closure is defined on `TaskFactory<VoidTaskResult>` but the transpiler uses the actual type parameter (e.g., `EventRegistrationToken`).

### Evidence

```
Missing: TaskFactory_1___c__DisplayClass32_0_EventRegistrationToken__FromAsyncImpl_b__0
Suggested: TaskFactory_1___c__DisplayClass32_0_VoidTaskResult__FromAsyncImpl_b__0
```

### The Fix

```csharp
// In MethodMap.Task.cs or CodeGenerator.cs:
// For FromAsyncImpl closures, use VoidTaskResult as the type parameter
private static string GetClosureTypeParam(string methodName, string outerTypeParam)
{
    if (method.Contains("FromAsyncImpl"))
    {
        return "System.Threading.Tasks.VoidTaskResult";
    }
    return outerTypeParam;
}
```

---

## Implementation Priority

| Fix | Impact | File | Complexity |
|-----|--------|------|------------|
| Bug 1 (remove `_int`) | 84+ errors | `TypeEmitter.cs` | Low |
| Bug 2 (field owner) | 12 errors | `CodeGenerator.cs` | Low |
| Bug 3 (closure param) | 4 errors | `CodeGenerator.cs` | Low |

---

## Complete Fix for TypeEmitter.cs

```csharp
public static string TypeCppName(TypeDef t)
{
    var baseName = Sanitize(t.FullName);
    
    // M3 generics: a generic definition emits ONE instantiation
    // with T -> int32_t. The instantiation name appends "_int".
    //
    // CRITICAL FIX r77: Do NOT append "_int" for non-generic nested 
    // types within a generic type definition. Only append it for 
    // the outermost generic definition itself.
    if (t.GenericArity > 0 && !t.IsNested)
    {
        return baseName + "_int";
    }
    
    return baseName;
}
```

This fix ensures:
- `TaskFactory<T>` → `System_Threading_Tasks_TaskFactory_1_int` (generic definition)
- `TaskFactory<object[]>.___c_object_array` → `System_Threading_Tasks_TaskFactory_1___c_object_array` (nested type, no `_int`)
- `TaskFactory<int>` → `System_Threading_Tasks_TaskFactory_1_int` (still works because outermost)

The key change: `!t.IsNested` ensures `_int` is only appended to top-level generic definitions, not to their nested closure types.
