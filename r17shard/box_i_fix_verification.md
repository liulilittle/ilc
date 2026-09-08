# box:i cast arm 修复验证报告

**验证日期**: 2026-09-06  
**目标文件**: E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs

---

## 验证结果

### 1. "box:i" 分支是否存在

**结论**: ❌ **不存在**

搜索 `box:i` 在 CodeGenerator.cs 中无匹配结果。当前 cast arm 仅包含 `unbox:*` 分支。

### 2. "il2cpp_box_int32" 引用情况

**位置**:
- L5805: 注释 `box_{token}(v) -> il2cpp::il2cpp_box_int32(nullptr, v)`
- L5887: `box_` token 的 System.Int32 分支（`EmitTask9Call` 函数内）

**结论**: `il2cpp_box_int32` 仅在 `box_` token 特殊处理函数中，不在 cast arm 中。

### 3. TypeKindResolver.ResolveExprKind 引用情况

**结论**: ❌ **不存在**

CodeGenerator.cs 中无 `TypeKindResolver` 或 `ResolveExprKind` 的引用。

### 4. unbox:i 分支位置

**位置**: L6697

```csharp
"unbox:i" => $"il2cpp::il2cpp_unbox_int32(static_cast<Il2CppObject*>({code}))",
```

**结论**: `unbox:i` 分支存在，但 `box:i` 分支缺失。

---

## 当前 cast arm 结构 (L6690-6720)

```csharp
"unbox:i"   => il2cpp_unbox_int32(static_cast<Il2CppObject*>(code)),
"unbox:l"   => icall_unbox_long(static_cast<Il2CppObject*>(code)),
"unbox:ui"  => icall_unbox_uint(static_cast<Il2CppObject*>(code)),
"unbox:ul"  => icall_unbox_ulong(static_cast<Il2CppObject*>(code)),
"unbox:d"   => icall_unbox_double(static_cast<Il2CppObject*>(code)),
"unbox:b"   => icall_unbox_bool(static_cast<Il2CppObject*>(code)),
"unbox:c"   => icall_unbox_char(static_cast<Il2CppObject*>(code)),
"unbox:dec" => icall_unbox_decimal(static_cast<Il2CppObject*>(code)),
// ❌ box:i 分支缺失
```

---

## 结论

**box:i cast arm 修复尚未应用到 CodeGenerator.cs。**

当前状态:
- `unbox:i` 分支存在 (L6697)
- `box:i` 分支缺失
- `TypeKindResolver.ResolveExprKind` 未在 CodeGenerator.cs 中使用

建议: 如需添加 `box:i` 分支，应在 unbox:i 之后添加:
```csharp
"box:i" => $"il2cpp::il2cpp_box_int32(nullptr, {code})",
```
