# r77 cannot-init Top1 根因分析报告

## 症状描述

**错误模式**: C++ 编译报错 `cannot initialize a parameter of type System_Int32* with int32_t*` (以及类似 `System_Int64*` vs `int64_t*` 等)。

**Top1 触发场景**: callvirt（虚方法调用）的 receiver 是 `LocAddrExpr`（即 `&loc0`，其中 `loc0` 是 `int32_t` 类型的局部变量）。

**r77 实测证据**:
```
out\mscorlib_gen_r77\src\System_Convert.cpp:6173:    t1 = reinterpret_cast<Il2CppObject*>(System_Int32_ToString_92d509c041ae4121((&arg0), t0));
out\mscorlib_gen_r77\src\System_RuntimeType_ConstructorInfoComparer.cpp:120:    return System_Int32_CompareTo_49124a32406e0a90((&loc0), t1);
```

`System_Int32_ToString_92d509c041ae4121` 的函数签名是：
```cpp
IL2CPP_NOINLINE Il2CppObject* System_Int32_ToString_92d509c041ae4121(System_Int32* arg0, Il2CppObject* arg1)
```

`arg0` 形参类型是 `System_Int32*`（生成的结构体指针），但实参 `(&arg0)` / `(&loc0)` 的类型是 `int32_t*`（因为 `arg0`/`loc0` 是 `int32_t` 类型的 C++ 局部变量）。这两种指针类型无继承关系，C++ 拒绝隐式转换。

---

## 根因定位

### 核心文件
`src/il2cppc/Emit/CodeGenerator.cs`

### 根因函数
`EmitVirtualCall` (line 3786-3909)

### 关键行

#### 路径 1: 非虚拟调用路径 (non-virtual callvirt, line 3898-3900)
```csharp
// line 3898-3900
var targetMethod = owner.Methods.FirstOrDefault(m => VTablePlanner.SigKey(m) == sig)
    ?? throw new NotSupportedException($"callvirt non-virtual target {owner.FullName}::{sig} not found");
var fnName = MethodCppName(owner, targetMethod);
var callArgs2 = string.Join(", ", new[] { receiverCode }.Concat(args));
return $"{fnName}({callArgs2})";
```
此处 `receiverCode` 直接拼接到调用参数中，无任何类型转换。当 `receiver` 是 `LocAddrExpr` 时，`receiverCode` = `(&loc0)` (int32_t*)，而目标函数期望 `System_Int32*`。

#### 路径 2: vtable 分发路径 (line 3907)
```csharp
// line 3907
var callArgs = string.Join(", ", new[] { receiverCode }.Concat(args));
return $"{fnPtr}({callArgs})";
```
同上，`receiverCode` 直接拼接。

#### 路径 3: devirt S1/S3 路径 (line 3954, 3973)
```csharp
// line 3954 (interface dispatch)
var ifaceArgs = string.Join(", ", new[] { receiverChecked }.Concat(args));
return $"{ifaceFn}({ifaceArgs})";

// line 3973 (class dispatch)
var callArgs = string.Join(", ", new[] { receiverChecked }.Concat(args));
return $"{fnName}({callArgs})";
```
此处使用了 `receiverChecked`（已包装 `il2cpp_null_check`），但 `receiverChecked` 本身也有问题（见下）。

#### 路径 4: receiverChecked 本身 (line 3855)
```csharp
// line 3855
var receiverChecked = $"il2cpp_null_check(static_cast<Il2CppObject*>({receiverCode}))";
```
`static_cast<Il2CppObject*>((&loc0))` 失败：`int32_t*` → `Il2CppObject*` 不是 static_cast 允许的转换（两者无继承关系，也不是 void*）。

---

## 遗漏检测点

`IsIntThis` (line 4758，不存在 — 实际是在 r71 添加在 `EmitCallStmt` 类似路径中的) 和当前 `EmitVirtualCall` 中对 `LocAddrExpr` 的处理：

当前代码只处理了 `LocExpr`（值）和 `CallExpr`（调用结果）的 int-kind receiver，未处理 `LocAddrExpr`（地址-of-int-loc）。

```csharp
// 当前 IsIntThis（在 EmitCallStmt 中的模式）
bool IsIntThis(Expr e) => e is Ir.LocExpr leT && ... ;
```

不覆盖 `LocAddrExpr`。

---

## 修复方案

需要在 `EmitVirtualCall` 中新增对 `LocAddrExpr` 的检测和 reinterpret_cast 桥接。

### 修复点 1: 非虚拟路径 (line 3898-3900)

在拼接 `callArgs2` 之前，检测 receiver 是否为 `LocAddrExpr` 且指向 int-kind loc，若是则 reinterpret_cast 到 value-type 的 C++ 结构体类型：

```csharp
// 插入位置：line 3899 之前
var thisArgCode = receiverCode;
if (receiver is LocAddrExpr laR)
{
    var laRKind = _curLocKinds.TryGetValue(laR.Name, out var kR) ? kR : null;
    var laRCpp = _curDeclaredCppType.TryGetValue(laR.Name, out var cppR) ? cppR : null;
    if ((laRKind == "val" || laRKind == "i" || (laRKind?.Contains("int32") ?? false))
        && (laRCpp?.Contains("int32_t") == true || laRCpp?.Contains("int64_t") == true))
    {
        thisArgCode = $"reinterpret_cast<{TypeEmitter.TypeCppName(owner)}*>({receiverCode})";
    }
}
var callArgs2 = string.Join(", ", new[] { thisArgCode }.Concat(args));
```

### 修复点 2: vtable 分发路径 (line 3907)

```csharp
// 在 line 3907 之前增加检测
bool IsIntThisAddr(Expr e) => e is Ir.LocAddrExpr laT 
    && _curLocKinds.TryGetValue(laT.Name, out var kT) 
    && (kT == "val" || kT == "i" || kT.Contains("int32")) 
    && _curDeclaredCppType.TryGetValue(laT.Name, out var cppT) 
    && (cppT.Contains("int32_t") || cppT.Contains("int64_t"));

var thisArg = IsIntThisAddr(receiver) 
    ? $"reinterpret_cast<Il2CppObject*>({receiverCode})" 
    : receiverCode;
var callArgs = string.Join(", ", new[] { thisArg }.Concat(args));
```

### 修复点 3: receiverChecked (line 3855)

`receiverChecked` 用于 devirt 和 interface dispatch 路径。当前使用 `static_cast<Il2CppObject*>` 对 int* 不合法。需要区分：
- 如果 receiver 是 `LocAddrExpr` 且 int-kind → 使用 `reinterpret_cast<Il2CppObject*>`
- 否则 → 保持 `static_cast<Il2CppObject*>`

```csharp
var receiverChecked = (receiver is LocAddrExpr laR && IsIntAddrLoc(laR))
    ? $"il2cpp_null_check(reinterpret_cast<Il2CppObject*>({receiverCode}))"
    : $"il2cpp_null_check(static_cast<Il2CppObject*>({receiverCode}))";
```

---

## 影响范围评估

### 受影响的生成文件（r77 实测）
- `System_RuntimeType_ConstructorInfoComparer.cpp:120` — `CompareTo(int)` 的 callvirt
- `System_Convert.cpp:6173, 6187` — `ToString(IFormatProvider)` 的 callvirt
- `System_Diagnostics_Tracing_EventProvider.cpp:917`
- `System_Diagnostics_Tracing_EventSource.cpp:4308`
- `System_IntPtr.cpp:320`
- `System_IO_TextWriter.cpp:671`
- `System_Runtime_Remoting_Identity.cpp:843`
- `System_Runtime_Versioning_VersioningHelper.cpp:198`
- `System_Security_Policy_CodeConnectAccess.cpp:299, 525`
- `System_Security_Util_URLString.cpp:1461`
- `System_Text_StringBuilder.cpp:2133, 2696`
- `System_TimeZoneInfo.cpp:4030, 4082, 4108`
- `System_TimeZoneInfo_StringSerializer.cpp:366, 380, 386, 402, 408, 414`

### 共现 pattern
`(&int_var)` 作为 callvirt 的 receiver，其中 `int_var` 是 `int32_t`/`int64_t` 类型的局部变量或参数，该 callvirt 的 target 是 value-type 方法（如 `System.Int32::ToString`）。

---

## 依赖字段

- `_curLocKinds` (Dictionary<string, string>) — 当前方法各 local 的 IR kind ("val"/"i"/"int32" 等)
- `_curDeclaredCppType` (Dictionary<string, string>) — 当前方法各 local 的 C++ 声明类型 ("int32_t"/"int64_t"/"Il2CppObject*" 等)
- `TypeEmitter.TypeCppName(TypeDef)` — 将 TypeDef 转为 C++ 结构体名 (如 `System_Int32`)
- `MethodCppName(TypeDef, MethodDef)` — 将 (owner, method) 转为 C++ 函数名

---

## 量化指标

- **r77 基线**: `Select-String "System_Int32_[A-Za-z0-9_]+\(\(&"` 匹配 **24 处**
- 全部位于实参位置，形参类型不匹配 (`System_Int32*` vs `int32_t*`)
- 涉及文件 13 个，方法均为 `System.Int32` / `System.Int64` 等原始类型的 `ToString(IFormatProvider)` / `CompareTo(int)` 等

## 验证方法

1. `dotnet build src/il2cppc` → 确认 0 错误 0 警告
2. 对 r78 生成文件抽检：`Select-String "System_Int32_[A-Za-z0-9_]+\(\(&"` 确认结果减少
3. 确认无新的 `cannot initialize a parameter of type` 错误

---

## 文件路径与行号索引

| 位置 | 行号 | 用途 |
|------|------|------|
| `CodeGenerator.cs` | 3786-3909 | `EmitVirtualCall` 函数体 |
| `CodeGenerator.cs` | 3855 | `receiverChecked` 生成（需修复） |
| `CodeGenerator.cs` | 3898-3900 | 非虚拟路径 callArgs2（需修复） |
| `CodeGenerator.cs` | 3907 | vtable 路径 callArgs（需修复） |
| `CodeGenerator.cs` | 3954 | devirt interface path ifaceArgs（需修复 receiverChecked） |
| `CodeGenerator.cs` | 3973 | devirt class path callArgs（需修复 receiverChecked） |
| `CodeGenerator.cs` | 5608 | `IsCppIntDecl` 辅助方法 |
| `CodeGenerator.cs` | 3091-3095 | `EmitExpr(LocExpr)` → `l.Name`; `EmitExpr(LocAddrExpr)` → `(&la.Name)` |
| `TypeEmitter.cs` | 417 | `TypeCppName(TypeDef)` — 获取 C++ 结构体名 |
| `MethodEmitter.cs` | 21-34 | `EmitMethod` — C++ 函数壳生成 |

---

## 状态

- [x] 根因定位完成
- [x] 修复方案完成
- [ ] 源码修改（按新策略只分析不修改）
- [ ] 验证
