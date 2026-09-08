# G2a2 m_dateTime语义桥差异（仅改 FieldAccess 4型映射表，补写回桥+ldflda）

> 约束：只得修改 `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 的 `FieldAccess` 4型映射表；禁止触 `TypeEmitter`/`StackFold`/`il2cpp_dtoffset.h`；W3已备份 `E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-204033`；C0 0错已验证；产物前缀 `g2a2_` 落 `r17shard\`；父会话 `ses_faec50249ffeP9Xm8rXxPL2fiP`

---

## 1 文件:行号

- **文件**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
- **行号**：`6673`（方法 `FieldAccess` 入口）+ `6676..6678` 既有映射句 + 新增 `6680..6685` 语义桥补全
- **证据**：旧代码见 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6673]；新代码同文件同方法；备份见 [执行:Copy-Item -Destination ".opencode/backups/CodeGenerator.cs.20260902-204033":True]。

---

## 2 旧代码

```csharp
// 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6676
// R18-A族字段映射(37行中可改名部分10行)：BCL DTO字段 m_offsetMinutes 在 runtime 模型 Il2CppDateTimeOffset 中为 offsetMinutes（无 m_ 前缀，类型 short→int32 兼容）；m_dateTime 无直接对应（DateTime值→int64 utcTicks 需语义桥，暂保留原名走后续 icall 路径）。
if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
    fieldCppName = "offsetMinutes";
if (isValueTypeObject)
{
    // ... M3 Task 11 值类型接收者分支（按址/按值/dot/arrow多态，此处省略，见 6687..6728）
```

- **缺陷**：`m_dateTime` 保留原名 `m_dateTime` 发射，导致 `arg0->m_dateTime` 在 `Il2CppDateTimeOffset { utcTicks; offsetMinutes; }` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:42] 中无成员，产生 27行 `no member named 'm_dateTime' in 'il2cpp::runtime::Il2CppDateTimeOffset'` 编译失败[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552]，覆盖 `stfld` 4 + `ldfld→icall_datetime_get_*` 15 + `op_Addition/SpecifyKind/ldobj` 8 =27。

---

## 3 新代码

```csharp
        var objCode = EmitExpr(obj, md, types);
        // R18-A族字段映射(37行中可改名部分10行)：BCL DTO字段 m_offsetMinutes 在 runtime 模型 Il2CppDateTimeOffset 中为 offsetMinutes（无 m_ 前缀，类型 short→int32 兼容）；m_dateTime 无直接对应（DateTime值→int64 utcTicks 需语义桥，暂保留原名走后续 icall 路径）。
        if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
            fieldCppName = "offsetMinutes";
        // G2a2 m_dateTime语义桥(27x)：BCL m_dateTime(UTC DateTime,8B)↔runtime utcTicks(int64,offset0)按值别名；读写均走*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)；写回桥 stfld m_dateTime←Il2CppDateTime 的ticks/direct struct assign；ldflda 左值路径 &m_dateTime↔&utcTicks reinterpret（1:1配对，无双求值）
        if (owner == "System.DateTimeOffset" && fieldCppName == "m_dateTime")
        {
            var baseAccess = FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject);
            return $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>(&{baseAccess}))";
        }
        if (isValueTypeObject)
        {
            // ... 保持原 M3/R12/bcldeep 全分支不变（&、->、dot、ptr、FieldAddrExpr、reinterpret_cast判别）
```

- **要点**：
  1. 必须复用 `FieldAccess(obj, "utcTicks", ...)` 既有发射路径获取 `dto.utcTicks` 访问串（`arg0->utcTicks` / `loc0.utcTicks` / `(&loc0)->utcTicks` 等），复用 `isValueTypeObject` 7分支判别，保证 1:1配对（每次 `ldloca`/`ldelema`/`FieldAddr` 恰好一次 `->`/`*`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6689]。
  2. 必须包 `*reinterpret_cast<Il2CppDateTime*>(&baseAccess)` 产出 `Il2CppDateTime` lvalue，使三处调用点均收敛：
     - `ldfld`（`FieldExpr`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3806]：读得 `DateTime(utcTicks)`；
     - `stfld`（`FieldAccessStmt: faTarget = faValue`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3028]：写得 `*reinterpret<DateTime*>(&dto.utcTicks)=value` 即 `dto.utcTicks = value.ticks`（写回桥）；
     - `ldflda`（`&FieldAccess`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3813]：取址得 `&(*reinterpret<DateTime*>(&dto.utcTicks))` 折叠为 `DateTime*(&dto.utcTicks)`（左值路径）。
  3. 禁止新增字段、禁止新增icall、禁止改 `TypeEmitter` 布局（`il2cpp_dtoffset.h:42` 已定）；禁止在 `isValueTypeObject` 分支外重复求值 `objCode`。

---

## 4 备选路径（文档化，不落地）

- **icall语义路径（只读）**：`il2cpp::runtime::icall_dtoffset_get_utc_datetime(*reinterpret<Il2CppDateTimeOffset*>(...))` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:70]。读可保真，但 `stfld` 左值非法、`ldflda` 需写回 `utcTicks + offset` 换算，复杂度高于别名，禁止作为主路径。
- **重构为 utcTicks+offset 换算**：若按 BCL `ClockDateTime`（local）存储则需 `utcTicks + offsetMinutes*TicksPerMinute` 合成；但 BCL `ValidateDate` 已证明 `m_dateTime` 为 UTC（`utcTicks = dateTime.Ticks - offset.Ticks` [读:E:\il2cpp\src\runtime\src\dtoffset.cpp:53]），故直接别名即语义等价，无需加 offset。

---

## 5 预计消除与计数证据

- **消除**：`System_DateTimeOffset.cpp` 中 `m_dateTime` 编译错误 27行（r18定点 27x）必须全部消除；`m_offsetMinutes` 10x 已由既有 1行映射消除（`offsetMinutes`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6677]。
- **分类**：`stfld m_dateTime` 4（`ValidateDate` 返回赋值）、`ldfld→icall_datetime_get_year/month/day/hour/minute` 15、`op_Addition/SpecifyKind/ldobj/unbox` 8 =27[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552:271/297/364/409/412/631/663]。
- **未改动**：`System.DateTime`/`TimeSpan`/`Guid`/`Decimal`/`BigInteger` 值类型分支保持原样，不得漂移。

---

## 6 回归风险（DTO按值传递/写回）

- **按值传递**：DTO 16B 值类型跨方法/跨栈槽按值拷贝，对 `m_dateTime` 的写入若发生在 `inlN = arg0` 拷贝上，仅改拷贝，需外层 `stloc`/`stfld` 写回原位；禁止将 `argN` 值误判为指针（已依赖 `_curLocKinds` 指针判定）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6724]。
- **写回**：`faTarget = faValue` 对别名 `*reinterpret<DateTime*>(&dto.utcTicks)=value` 等价于 `dto.utcTicks = value.ticks`，8B 原子写入，`value` 为 `Il2CppDateTime` 时 `ticks` 联合体 `union{ticks,_ticks,dateData}` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:40] 必须完整拷贝，禁止窄化为 `int32`。
- **取址**：`&m_dateTime` 桥接后为 `&*reinterpret<DateTime*>(&dto.utcTicks)` 折叠为 `DateTime*(&dto.utcTicks)`，与 `il2cpp_time.h:45` 的 `Il2CppDateTime* operator->` 容忍值类型 `->ticks` 访问一致，不得引入 `&(&ptr)` 双重取址（已由 `FieldAddrExpr` 单层 `&` guard）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3813]。
- **并发/状态**：每次 `ldfld`/`stfld`/`ldflda` 必须与 `EmitExpr` 产生的 `objCode` 1:1配对，禁止 `objCode` 重复求值导致 `il2cpp_null_check` 双求值（引用路径已在 6789 行单次求值）。

---

## 7 完整diff（精简）

```diff
 // R18-A族字段映射 ... 暂保留原名走后续 icall 路径。
 if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
     fieldCppName = "offsetMinutes";
+// G2a2 m_dateTime语义桥(27x)：BCL m_dateTime(UTC DateTime,8B)↔runtime utcTicks(int64,offset0)按值别名；读写均走*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)；写回桥 stfld m_dateTime←Il2CppDateTime 的ticks/direct struct assign；ldflda 左值路径 &m_dateTime↔&utcTicks reinterpret（1:1配对，无双求值）
+if (owner == "System.DateTimeOffset" && fieldCppName == "m_dateTime")
+{
+    var baseAccess = FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject);
+    return $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>(&{baseAccess}))";
+}
 if (isValueTypeObject)
```
