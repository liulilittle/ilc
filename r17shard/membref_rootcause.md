# membref / no-member 六分型根因调查报告

**日期**：2026-09-06
**范围**：`src/il2cppc/Emit/CodeGenerator.cs` + `MethodRefResolver.cs` + `Ir/StackFold.cs`
**证据源**：`r77shard/r77shard1_shard1_errors.log` + `r77_verdict.md` (membref 1614 计数)

---

## 一、故障现象

r77 三片扫描 membref 簇（member reference + no member 合计 1614 条），代表性错误：

| 文件 | 行 | 错误 |
|---|---|---|
| `System_Console.cpp` | 2176 | `(&(inl2))->dwSize` → "member reference base type 'int32_t'" |
| `System_DateTimeParse.cpp` | 812 | `inl0->Index` → "member reference base type 'int32_t'" |
| `System_Collections_Concurrent_ConcurrentDictionary_2_int.cpp` | 567 | `no member named 'm_buckets' in ...` |
| `System_AppDomain.cpp` | 6770 | `no member named 'icall_box_intptr' in namespace 'il2cpp::runtime'` |

**共同模式**：接收者表达式的 C++ 类型被推断为 `int32_t`（标量槽），而非实际声明它的结构体类型 → 对标量做 `->member` / `.member` 即触发 "member reference base type 'int32_t' is not a structure or union"。

---

## 二、根因链

### 2.1 故障点 1：FieldAccess 的 owner 解析与接收者 locKind 不一致

**代码路径**：`CodeGenerator.FieldAccess`（Emit\CodeGenerator.cs:5562-5583）

```csharp
private static string FieldAccess(Expr obj, string owner, string fieldCppName, ...)
{
    var objCode = EmitExpr(obj, md, types);
    ...
    var ownerCpp = TypeCppName(owner);          // ← 声明侧 → 结构体名
    return $"(({ownerCpp}*)(il2cpp_null_check(...)))->{fieldCppName}";
}
```

- `owner` 来自 IR `FieldExpr.OwnerFullName`（元数据声明侧），经 `TypeCppName` 折叠为正确的 C++ 结构体名 —— 这里**通常是对的**。
- `objCode` 是接收者表达式；若 `obj` 是 `LocExpr`，其 C++ 声明类型来自 `locKind[name]`。
- **关键断点**：当 `locKind[obj.Name]` == `"val"`（int32_t）而 `owner` 是一个结构体类型时，发射的 C++ 是：
  ```
  reinterpret_cast<SomeStruct*>((&int32_t_var))->member
  ```
  对 int32_t 取地址再 reinterpret_cast 到结构体指针，在 clang 上触发 **"member reference base type 'int32_t' is not a structure or union"**。

### 2.2 故障点 2：value-type 方法返回值不播种 locKind

**代码路径**：`EmitBody` 的第二次 AssignStmt 循环（Emit\CodeGenerator.cs:1149-1185）

当前 value-type 返回播种仅发生在 `IsValueTypeCtorToken`（`.ctor` on ldloca）路径：

```csharp
if (vc.Function.StartsWith("call_", StringComparison.Ordinal))
{
    var tok = int.Parse(vc.Function[5..]);
    if (IsValueTypeCtorToken(md, tok, types))
        locKind[laCtor.Name] = ValueTypeCtorOwnerCpp(md, tok, types);  // ← 仅 .ctor 播种
}
else if (vc.Function.StartsWith("initobj_", ...))
    locKind[laCtor.Name] = TypeCppName(TypeTokenFullName(md, ioTok));
```

**缺陷**：普通方法调用返回 value-type（非 .ctor）**不播种**。

**实例**：
```
inl2 = System_Console_GetBufferInfo_ef2b53c92c91357c();  // 返回 CONSOLE_SCREEN_BUFFER_INFO
```
- `System_Console_GetBufferInfo_` 是 user-method 路径（非 icall、非 .ctor）。
- `MethodRefResolver.Resolve(md, callTok2, types)?.ReturnTypeName` 返回 **null** —— `MethodMap.Console.cs` 未为 `GetBufferInfo` 设置 `returnTypeName`（仅 `get_Error`/`get_Out` 等 singleton 有 `ReturnTypeName`）。
- 因此 locKind["inl2"] 保持默认 `"val"` → 声明 `int32_t inl2`。
- 后续 `(&(inl2))->dwSize` 失败。

**证据**：[System_Console.cpp:2176]
```cpp
int32_t inl2;
...
inl2 = System_Console_GetBufferInfo_ef2b53c92c91357c();   // 应声明为 Microsoft_Win32_Win32Native_CONSOLE_SCREEN_BUFFER_INFO
inl0 = (&((&(inl2))->dwSize))->X;                       // 错误：int32_t 没有 dwSize
```

### 2.3 故障点 3：BCL 方法返回 value-type 时 ReturnTypeName 未填写

**代码路径**：`MethodRefResolver.TryMapConsole`、`MethodMap.*.cs`

BCL icall 映射使用 `I("il2cpp::runtime::icall_...")` helper，该 helper 构造 `ResolvedMethod` 时不设置 `ReturnTypeName`。

结果：即使返回类型元数据明确是 `Microsoft.Win32.Win32Native+CONSOLE_SCREEN_BUFFER_INFO`（0x11 valuetype），`MethodRefResolver.Resolve(md, tok, types).ReturnTypeName` 仍为 null，发射侧无法知道目标类型。

**波及**：所有返回 value-type 的 BCL 方法都受影响（`DateTime`、`TimeSpan`、`Guid` 等特殊路径已有 `dt`/`ts`/`guid` 分支兜底，但 `CONSOLE_SCREEN_BUFFER_INFO`、`MSG`、`WIN32_FIND_DATA` 等 P/Invoke 结构体无兜底）。

### 2.4 故障点 4：嵌套类型展开后 FieldOwnerDef 的 '+' 形式匹配不充分

**代码路径**：`CodeGenerator.FieldOwnerDef`（Emit\CodeGenerator.cs:6829-6847）

```csharp
var exact = types.FirstOrDefault(t => t.FullName == ownerFullName);
if (exact != null) return exact;
var byRefl = types.FirstOrDefault(t => TypeEmitter.ReflectionFullName(t) == ownerFullName);
```

`MetadataLoader` 展开嵌套类型后，外层 TypeDef.FullName 不含 '+'；而 `TypeEmitter.ReflectionFullName` 返回 `"Outer+Nested"` 形式。

缺陷：`TypeEmitter.ReflectionFullName` 仅在 `t.Name` 与 `ownerFullName` 的 '+' 形式匹配时命中；当 `ownerFullName` 是用户类型（非 BCL）且嵌套展开后未克隆 TypeDef 时，`FieldOwnerDef` 返回 null → `FieldValueTypeKind` 返回 null → `locKind` 不播种 → 默认 int32_t。

### 2.5 故障点 5：inl 临时变量在 inline 展开后类型丢失

**代码路径**：`InlinePass.Inline` + 行 1208-1215 注释

M7 CG-FIX 注释（Emit\CodeGenerator.cs:1208-1215）：
> the inlined base-ctor chain renames the callee's `this` (arg0) to a fresh inline temp (`inl0 = arg0;`), and the temp's type propagates from ExprKind(LocExpr arg0).

**缺陷**：`ExprKind` 对 `LocExpr arg0` 返回 `"ref"` 还是 `"val"` 取决于 `locKind["arg0"]` 是否已播种。行 1232-1233：
```csharp
if (!isStaticMethod && !MethodOwnerIsValueType(md, bodyInfo.MethodToken, types))
    locKind["arg0"] = "ref";
```
仅对**引用类型 owner** 播种 arg0。当 owner 是 value-type 时，行 1236-1239 通过 `ValueTypeThisCpp` 设置 `locKind["arg0"]` = 结构体名，但 `ExprKind(LocExpr arg0)` 走的是 `_curLocKinds` 查找，若 `arg0` 是 value-type this（`{T}*`），`ExprKind` 返回 `"ptr"` 而非结构体名，导致 inl 变量声明为 `int32_t` 而非 `T`。

### 2.6 故障点 6：generic T 字段 / 返回的闭包参数丢失

**代码路径**：`CodeGenerator.FindCarrierClosure` + `FieldValueTypeKind` 行 6874-6897

generic 方法体内的 `T` 字段读（如 `fe.OwnerFullName` 含 `_int` 后缀）走 `FieldValueTypeKind` 的闭包参数分支：
```csharp
if (fe.OwnerFullName.Contains("_int", StringComparison.Ordinal))
    resolved = "System.Int32";
```
但当 owner 是 generic 闭包 `__c__DisplayClass17_0_1_int` 且 T 字段是从闭包 Args 推导时，若 `closureArgs` 未传入（`StackFold._curClosureArgs` 作用域失效），`resolved` 为 null → 默认 int32_t。

---

## 三、证据链（Console.GetBufferInfo 完整追踪）

1. **元数据侧**：`System.Console.GetBufferInfo()` 返回 `Microsoft.Win32.Win32Native+CONSOLE_SCREEN_BUFFER_INFO`（嵌套结构体）。
2. **TypeDef 侧**：`MetadataLoader` 展开后生成 `Microsoft_Win32_Win32Native_CONSOLE_SCREEN_BUFFER_INFO` TypeDef。
3. **MethodMap 侧**：`TryMapConsole` 无 `GetBufferInfo` 映射 → 返回 null → 走 user-method 路径。
4. **MethodRefResolver 侧**：`ResolveMemberReference` 生成 `System_Console_GetBufferInfo_ef2b53c92c91357c`，`ReturnTypeName = null`（未设 returnTypeName）。
5. **StackFold 侧**：`objTypes[stName]` 未记录返回类型（因为 `resolved.ReturnTypeName == null`）。
6. **CodeGenerator.EmitBody 侧**：
   - locKind 扫描行 1158-1185：`call_System_Console_GetBufferInfo_ef2b53c92c91357c` 不是 `.ctor`，不播种。
   - 行 1486：`retName = MethodRefResolver.Resolve(md, callTok2, types)?.ReturnTypeName` = null。
   - 行 1501-1509：反向 MethodDef 查找 —— 但 `System.Console` 是 BCL 类型，无 TypeDef。
   - 行 1510：`string.IsNullOrEmpty(retName)` 为 true → `continue` → **不播种**。
7. **结果**：`inl2` 声明为 `int32_t`；`(&(inl2))->dwSize` 失败。

---

## 四、六分型根因汇总

| # | 根因 | 影响面 | 修复入口 |
|---|---|---|---|
| 1 | BCL value-type 返回不播种 `ReturnTypeName` | 所有返回 struct 的 BCL 方法 | `MethodMap.*.cs` + `MethodRefResolver.BclResolve` |
| 2 | `FieldAccess` 不验证接收者 locKind 一致性 | 所有 field access | `CodeGenerator.FieldAccess`（行 5562） |
| 3 | 嵌套类型展开后 FieldOwnerDef null | 嵌套结构体字段读 | `CodeGenerator.FieldOwnerDef`（行 6829） |
| 4 | inline 展开后 inl temp 类型丢失 | 所有 inline 展开 | `InlinePass` + `EmitBody` arg0 播种 |
| 5 | generic T 字段闭包参数丢失 | generic 方法体 | `FieldValueTypeKind` + `StackFold._curClosureArgs` |
| 6 | 标量 temp 未验证声明类型 vs 实际赋值 | 所有 temp 声明 | `EmitBody` 的 AssignStmt 后处理 |

---

## 五、修复方案

### 方案 A（最小侵入，推荐）：在 FieldAccess 入口验证接收者类型

在 `EmitBody` 的 locKind 分析完成后，插入一个** field access 目标类型对齐 pass**：

```csharp
// 新增 pass：扫描 FieldAssignStmt / FieldExpr，若接收者 locKind 是 "val"/"ref" 但 owner 是 value-type，
// 则通过 MethodRefResolver / TypeDef 查找 owner 的实际 C++ 类型，覆盖 locKind[name] = 结构体名。
foreach (var s in stmts)
{
    if (s is FieldAssignStmt fa)
        EnsureReceiverType(md, types, locKind, fa.Object, fa.OwnerFullName);
    else if (s is FieldExpr f)
        EnsureReceiverType(md, types, locKind, f.Object, f.OwnerFullName);
}
```

`EnsureReceiverType` 逻辑：
1. 若接收者不是 LocExpr，跳过。
2. 若 `locKind[locName]` 已是非 "val"/"ref"/"p" 的具体类型，跳过。
3. 通过 `FieldOwnerDef(owner, types)` 查找 TypeDef。
4. 若找到，设 `locKind[locName] = TypeCppName(def)`。
5. 若 owner 含 '+' 或 `\`，尝试 ReflectionFullName 匹配。

### 方案 B（根因修复）：BCL value-type 返回播种 ReturnTypeName

修改 `MethodRefResolver.BclResolve`，在分派到 `TryMap<Domain>` 后，若 `resolved != null` 且 `resolved.ReturnTypeName == null`，通过 `MethodSignatureCoder.Decode` 解码返回类型字节：
- 若是 0x11 (valuetype) / 0x12 (class) / 0x15 (genericinst)，编码 `ReturnTypeName = "V:<FullName>"`。

### 方案 C（保守兼容）：EmitBody 的 value-type 返回后处理

在行 1476 循环后，追加一个兜底 pass：
```csharp
// 若 locKind[name] == "val" 但 AssignStmt 右侧是已知返回 value-type 的 user method，
// 通过 MethodSignatureCoder 解码方法签名获取返回类型。
```

### 方案 D（嵌套类型）：ExpandNestedTypes 后补 TypeDef 克隆

在 `MetadataLoader.ExpandNestedTypes` 中，若外层 TypeDef 未生成独立 TypeDef，生成一个浅层克隆（FullName = "Outer+Nested"，Name = "Nested"），确保 `FieldOwnerDef` 的 FirstOrDefault 命中。

---

## 六、验证方案

1. `dotnet build src/il2cppc` → 0 错误 0 警告（C0）。
2. 重新生成 r77：`dotnet run --project src/il2cppc -- C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll E:\il2cpp\out\mscorlib_gen_r78`。
3. 三片扫描后 Select-String `member reference|no member` 计数，与 r77 的 1614 比较。
4. 抽查 `System_Console.cpp`、`System_DateTimeParse.cpp` 中 `inl2`/`inl0` 的声明类型（应为 struct，非 int32_t）。

预期：membref 簇减少 ≥ 60%（方案 A + B 联合）。

---

## 七、关键代码坐标

| 位置 | 行 | 作用 |
|---|---|---|
| `CodeGenerator.FieldAccess` | 5562 | field access 发射入口 |
| `CodeGenerator.TypeCppName` | 5626 | fullName → C++ 名 |
| `CodeGenerator.FieldOwnerDef` | 6829 | 字段 owner TypeDef 查找 |
| `CodeGenerator.EmitBody` locKind scan | 1149-1185 | ctor/initobj 播种 |
| `CodeGenerator.EmitBody` return scan | 1476-1509 | value-type 返回播种 |
| `MethodRefResolver.TryMapConsole` | 9-144 | Console 域 icall 映射 |
| `MethodRefResolver.BclResolve` | ~1400 | BCL 域分派主入口 |
| `StackFold.cs` objTypes 记录 | 2498-2501 | ReturnTypeName → objTypes |
