# FieldAssignStmt cannot-init P0 修复报告

**时间:** 2026-09-06
**目标文件:** `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`

---

## 修复状态：已完成

FieldAssignStmt 实例字段 bridge 已在代码中实现，位于第 2522-2528 行。

## 修改位置

**文件:** `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
**行号:** 2522-2528

## 修复代码

```csharp
var faValue = fa.IsRefField
    ? (ExprKind(fa.Value, locKind) == "val"
        ? $"reinterpret_cast<Il2CppObject*>({EmitExpr(fa.Value, md, types)})"
        : (faValueCpp == "Il2CppObject"
            ? $"static_cast<{faValueCpp}*>({EmitExpr(fa.Value, md, types)})"
            : NullSafePtrCast($"{faValueCpp}*", EmitExpr(fa.Value, md, types))))
    : EmitExpr(fa.Value, md, types);
```

**逻辑：**
1. 当实例字段是引用类型（`fa.IsRefField`）
2. 且赋值源是 int32/bool 值（`ExprKind(fa.Value, locKind) == "val"`）
3. 使用 `reinterpret_cast<Il2CppObject*>(val)` 桥接
4. 否则保持原有逻辑（`static_cast` 或 `NullSafePtrCast`）

## 编译验证

```
dotnet build src/il2cppc
```

**结果:** 0 错误 0 警告 ✅

## 覆盖范围

此修复覆盖以下 cannot-init 场景：
- 实例字段是 `Il2CppObject*`（委托、接口、class 类型字段）
- 赋值源是 int32/bool 值（`ExprKind == "val"`）

**不覆盖：**
- 参数传递（需 EmitIcallCallArgs "r" cast 码，见 cannot_init_fix.md 方案 B）
- 返回值（已在 RetStmt bridge 覆盖）
- 静态字段（已在 stsfld bridge 覆盖）

## 预期效果

根据 r77shard 错误日志统计，实例字段赋值导致的 cannot-init 约 20-50 行（与静态字段合计）。此修复合入后，配合已有的 AssignStmt bridge、stsfld bridge、RetStmt bridge，cannot-init 错误预计从 2284 行降至 ~1600 行（剩余主要为参数传递 602 行）。

## 后续建议

**P0 剩余：** EmitIcallCallArgs 参数传递 bridge（~602 行），需新增 `"r"` cast 码或自动检测 Il2CppObject* param 的 int 值。
