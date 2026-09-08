# R77 TaskAwaiter Fix Snippets

## Problem Analysis

Based on `r77_taskawaiter_deep_analysis.md`, there are three main TaskAwaiter error categories:

| Pattern | Count | Root Cause |
|---------|-------|------------|
| TaskAwaiter_1_int → Il2CppObject* | 226 | Method returns Il2CppObject* but value is TaskAwaiter struct |
| TaskAwaiter_1_int → int32_t | 73 | TaskAwaiter local assigned to int32_t; missing GetResult() |
| ValueTuple field access on TaskAwaiter | 100+ | locKind confuses TaskAwaiter with ValueTuple |

---

## Fix 1: TaskAwaiter_1_int → Il2CppObject* (226 errors)

### Root Cause
The transpiler emits `TaskAwaiter_1_int` as the C++ type for a local/return, but the target expects `Il2CppObject*`. This happens when the IL stack has a TaskAwaiter but the managed signature expects `object` (e.g., async state machine `Unsafe.As` patterns).

### Example Error
```
error: no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *'
```

### Generated Code Pattern (Broken)
```cpp
System_Runtime_CompilerServices_TaskAwaiter_1_int t2;
// ... some code produces int32_t ...
t2 = il2cpp::il2cpp_unbox_int32(arg1);  // ERROR: int32_t → TaskAwaiter_1_int
return t2;  // ERROR: TaskAwaiter_1_int → Il2CppObject* (return type)
```

### Fix Strategy
At the **return site** (RetStmt emission), when:
- Method return type is `Il2CppObject*`
- Actual value is `TaskAwaiter_1_int` (or similar)
- Insert `reinterpret_cast<Il2CppObject*>` OR box via runtime

### Code Snippet for CodeGenerator.cs

```csharp
// In EmitStmt, RetStmt case (already modified for cannot-init Top1):
case RetStmt r:
    if (r.Value == null)
    {
        w.LineAligned("return;",
            $"IL {At(r.SourceOpName, r.SourceOffset)} -- exit method with no value (void)");
    }
    else
    {
        var retExpr = EmitExpr(r.Value, md, types);
        
        // Fix 1a: cannot-init Top1 - int → Il2CppObject* return
        if (ReturnTypeIsIl2CppObjectPtr(_curBodyInfo!.ReturnType) && ExprIsIntVal(r.Value))
            retExpr = $"reinterpret_cast<Il2CppObject*>({retExpr})";
        
        // Fix 1b: TaskAwaiter_1_T → Il2CppObject* return
        // When returning a TaskAwaiter where object is expected, reinterpret_cast
        if (ReturnTypeIsIl2CppObjectPtr(_curBodyInfo!.ReturnType) && ExprIsTaskAwaiter(r.Value))
            retExpr = $"reinterpret_cast<Il2CppObject*>({retExpr})";
        
        w.LineAligned($"return {retExpr};",
            $"IL {At(r.SourceOpName, r.SourceOffset)} -- exit method returning the computed value");
    }
    break;
```

### New Helper Method
```csharp
// Check if expression is a TaskAwaiter type
private static bool ExprIsTaskAwaiter(Expr e)
{
    if (e is LocExpr le)
    {
        return _curLocKinds.TryGetValue(le.Name, out var k) 
            && k.StartsWith("System_Runtime_CompilerServices_TaskAwaiter", StringComparison.Ordinal);
    }
    if (e is CallExpr ce)
    {
        return ce.ResultKind?.StartsWith("System_Runtime_CompilerServices_TaskAwaiter", StringComparison.Ordinal) == true
            || (ce.Function?.Contains("TaskAwaiter", StringComparison.Ordinal) == true);
    }
    return false;
}
```

---

## Fix 2: TaskAwaiter_1_int → int32_t (73 errors)

### Root Cause
The transpiler declares a local as `TaskAwaiter_1_int` but assigns an `int32_t` to it (or vice versa). The IL `unbox.any` or `Unsafe.As` pattern produces an int32 that gets stored into a TaskAwaiter local.

### Example Error
```
error: no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'int32_t' (aka 'int')
```

### Generated Code Pattern (Broken)
```cpp
System_Runtime_CompilerServices_TaskAwaiter_1_int t2;
// ...
t2 = il2cpp::il2cpp_unbox_int32(arg1);  // ERROR: int32_t → TaskAwaiter_1_int
// or
int32_t loc0 = t2;  // ERROR: TaskAwaiter_1_int → int32_t
```

### Fix Strategy
At **AssignStmt emission**, when:
- Target is `TaskAwaiter_1_T` but source is `int32_t` → wrap with `TaskAwaiter_1_T{source}`
- Target is `int32_t` but source is `TaskAwaiter_1_T` → call `.GetResult()` or static_cast

### Code Snippet for CodeGenerator.cs

```csharp
// In EmitStmt, AssignStmt case:
case AssignStmt a:
{
    var target = a.Target;
    var value = a.Value;
    var assignExpr = EmitExpr(value, md, types);
    
    // Fix 2a: int32_t → TaskAwaiter_1_int assignment
    if (IsTaskAwaiterLoc(target) && ExprIsIntVal(value))
    {
        var cppType = _curLocKinds.GetValueOrDefault(target, "System_Runtime_CompilerServices_TaskAwaiter_1_int");
        assignExpr = $"{cppType}{{{assignExpr}}}";
    }
    // Fix 2b: TaskAwaiter_1_int → int32_t assignment (missing GetResult())
    else if (IsIntLoc(target) && ExprIsTaskAwaiter(value))
    {
        assignExpr = $"{assignExpr}.GetResult()";
    }
    
    w.LineAligned($"{EmitTarget(target)} = {assignExpr};",
        $"IL {At(a.SourceOpName, a.SourceOffset)} -- store evaluated value into {target}");
    break;
}
```

### Helper Methods
```csharp
// Check if a local name is a TaskAwaiter type
private static bool IsTaskAwaiterLoc(string name)
{
    return _curLocKinds.TryGetValue(name, out var k) 
        != null && k.StartsWith("System_Runtime_CompilerServices_TaskAwaiter", StringComparison.Ordinal);
}

// Check if a local name is an int32 type
private static bool IsIntLoc(string name)
{
    if (!_curLocKinds.TryGetValue(name, out var k)) return false;
    return k == "val" || k == "i" || k == "int32_t" || k.Contains("int32", StringComparison.Ordinal);
}
```

---

## Fix 3: ValueTuple Field Access on TaskAwaiter (100+ errors)

### Root Cause
The IR locKind analysis incorrectly classifies `TaskAwaiter_1_int` locals as `ValueTuple_1_int`, causing `Item1` field access to be emitted.

### Example Error
```
error: no member named 'Item1' in 'System_Runtime_CompilerServices_TaskAwaiter_1_int'
```

### Generated Code Pattern (Broken)
```cpp
System_Runtime_CompilerServices_TaskAwaiter_1_int t2;
// ...
int32_t x = t2.Item1;  // ERROR: TaskAwaiter has no Item1
```

### Fix Strategy
Fix the **locKind analysis** in `EmitBody` to correctly identify TaskAwaiter types. The locKind should be set to the TaskAwaiter type name, not "val" (which causes ValueTuple field access).

### Code Snippet for CodeGenerator.cs (EmitBody section)

```csharp
// In EmitBody, after locKind analysis loop, add TaskAwaiter type detection:
foreach (var stmt in stmts)
{
    if (stmt is AssignStmt a && a.Value is CallExpr ce)
    {
        // Detect TaskAwaiter constructor calls
        if (ce.Function?.Contains("TaskAwaiter", StringComparison.Ordinal) == true 
            || ce.ResultKind?.Contains("TaskAwaiter", StringComparison.Ordinal) == true)
        {
            var cppType = ce.ResultKind ?? "System_Runtime_CompilerServices_TaskAwaiter_1_int";
            if (!locKind.TryGetValue(a.Target, out var existing) || existing == "val" || existing == "ref")
            {
                locKind[a.Target] = cppType;
            }
        }
    }
}

// Also fix FieldExpr emission: when accessing a field on a TaskAwaiter,
// don't emit ItemN access - instead emit GetResult() or static_cast
case FieldExpr fe:
{
    var fieldObj = EmitExpr(fe.Object, md, types);
    var fieldCppName = fe.FieldCppName;
    
    // Fix 3: TaskAwaiter has no ItemN fields - use GetResult()
    if (ExprIsTaskAwaiter(fe.Object) && fieldCppName.StartsWith("Item", StringComparison.Ordinal))
    {
        w.LineAligned($"// FIX: TaskAwaiter has no {fieldCppName}, using GetResult()",
            $"IL {At(fe.SourceOpName, fe.SourceOffset)} -- field access on TaskAwaiter replaced with GetResult()");
        // Fall through to emit just the object; caller must wrap with .GetResult()
    }
    else
    {
        // Normal field access
        w.LineAligned($"((reinterpret_cast<{ownerCpp}*>(il2cpp_null_check(static_cast<Il2CppObject*>({fieldObj})))))->{fieldCppName};",
            $"IL {At(fe.SourceOpName, fe.SourceOffset)} -- read field {fe.OwnerFullName}.{fe.FieldCppName}");
    }
    break;
}
```

---

## Fix 4: TaskAwaiter in Call Arguments (9+ errors)

### Root Cause
`TaskAwaiter_1_int` passed as `Il2CppObject**` or `Il2CppObject*` parameter.

### Fix Strategy
At **CallExpr/CallStmt emission**, bridge TaskAwaiter args to the expected type:

```csharp
// In EmitExpr/CallExpr emission, before emitting args:
var argExprs = new List<string>();
for (var i = 0; i < c.Args.Count; i++)
{
    var arg = c.Args[i];
    var argExpr = EmitExpr(arg, md, types);
    
    // Fix 4: TaskAwaiter → Il2CppObject* arg
    if (ExprIsTaskAwaiter(arg) && i < resolvedVc.ParamTypeNames?.Length 
        && resolvedVc.ParamTypeNames[i] == "Il2CppObject*")
    {
        argExpr = $"reinterpret_cast<Il2CppObject*>({argExpr})";
    }
    // Fix 4b: TaskAwaiter → Il2CppObject** arg
    else if (ExprIsTaskAwaiter(arg) && i < resolvedVc.ParamTypeNames?.Length 
        && resolvedVc.ParamTypeNames[i] == "Il2CppObject**")
    {
        argExpr = $"reinterpret_cast<Il2CppObject**>(&{argExpr})";
    }
    
    argExprs.Add(argExpr);
}
```

---

## Implementation Priority

1. **Fix 1 (TaskAwaiter → Il2CppObject* return)** - Already partially implemented in cannot-init Top1 fix
2. **Fix 2 (TaskAwaiter → int32_t assign)** - Need to add to AssignStmt emission
3. **Fix 3 (ValueTuple field access)** - Need to fix locKind analysis in EmitBody
4. **Fix 4 (TaskAwaiter call args)** - Need to add arg bridging in CallExpr emission

## Insertion Points in CodeGenerator.cs

| Fix | Method | Line Range (approx) |
|-----|--------|---------------------|
| 1 | `EmitStmt` (RetStmt case) | 2528-2555 |
| 2 | `EmitStmt` (AssignStmt case) | ~3120-3150 |
| 3 | `EmitBody` (locKind analysis) | ~1850-1900 |
| 3 | `EmitExpr` (FieldExpr case) | ~6600-6650 |
| 4 | `EmitExpr` (CallExpr emission) | ~3850-3950 |
