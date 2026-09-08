# G2a m_dateTime语义桥编码差异（仅改 FieldAccess 4型映射表）

> 约束：只得修改 `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 的 `FieldAccess` 4型映射表；禁止触 `TypeEmitter`/`StackFold`/`il2cpp_dtoffset.h`；W3已备份 `E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-140421`；C0 0错已验证。

---

## 1 文件:行号

- **文件**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
- **行号**：`6854`（方法 `FieldAccess` 入口）+ `6857..6859` 既有映射句 + 新增 `6860..6866` 语义桥
- **证据**：旧代码见 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6854]；新代码同文件同方法；备份见 [执行:bash Test-Path:True]。

---

## 2 旧代码

```csharp
// 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6857
// R18-A族字段映射(37行中可改名部分10行)：BCL DTO字段 m_offsetMinutes 在 runtime 模型 Il2CppDateTimeOffset 中为 offsetMinutes（无 m_ 前缀，类型 short→int32 兼容）；m_dateTime 无直接对应（DateTime值→int64 utcTicks 需语义桥，暂保留原名走后续 icall 路径）。
if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
    fieldCppName = "offsetMinutes";
if (isValueTypeObject)
{
    // ... M3 Task 11 值类型接收者分支（按址/按值/dot/arrow多态，此处省略，见 6860..6909）
```

- **缺陷**：`m_dateTime` 保留原名 `m_dateTime` 发射，导致 `arg0->m_dateTime` 在 `Il2CppDateTimeOffset { utcTicks; offsetMinutes; }` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:42] 中无成员，产生 27行 `no member named 'm_dateTime' in 'il2cpp::runtime::Il2CppDateTimeOffset'` 编译失败[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552]。

---

## 3 新代码

```csharp
        var objCode = EmitExpr(obj, md, types);
        // R18-A族字段映射(37行中可改名部分10行)：BCL DTO字段 m_offsetMinutes 在 runtime 模型 Il2CppDateTimeOffset 中为 offsetMinutes（无 m_ 前缀，类型 short→int32 兼容）；m_dateTime 无直接对应（DateTime值→int64 utcTicks 需语义桥，暂保留原名走后续 icall 路径）。
        if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
            fieldCppName = "offsetMinutes";
        // G2a m_dateTime语义桥(27x消除)：BCL m_dateTime(UTC DateTime,8B)↔runtime utcTicks(int64,offset0)按值别名；读写均走*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)；icall只读备选icall_dtoffset_get_utc_datetime/get_datetime，写回必须别名（DTO按值传递需外层写回，见回归风险）
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
  1. 必须复用 `FieldAccess(...,"utcTicks",...)` 既有发射路径获取 `dto.utcTicks` 访问串（`arg0->utcTicks` / `loc0.utcTicks` / `(&loc0)->utcTicks` 等），复用 `isValueTypeObject` 7分支判别，保证 1:1配对（每次 `ldloca`/`ldelema`/`FieldAddr` 恰好一次 `->`/`*`）。
  2. 必须包 `*reinterpret_cast<Il2CppDateTime*>(&baseAccess)` 产出 `Il2CppDateTime` lvalue，使 `ldfld`（`FieldExpr`）、`stfld`（`FieldAccessStmt: faTarget = faValue` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3137]）、`ldflda`（`&FieldAccess` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3922]）三处调用点均收敛：读得 `DateTime(utcTicks)`、写得 `utcTicks = value.ticks`、取址得 `DateTime*(&utcTicks)`。
  3. 禁止新增字段、禁止新增icall、禁止改 `TypeEmitter` 布局（`il2cpp_dtoffset.h:42` 已定）。

---

## 4 备选路径（文档化，不落地）

- **icall语义路径（只读）**：`il2cpp::runtime::icall_dtoffset_get_utc_datetime(*reinterpret_cast<Il2CppDateTimeOffset*>(...))` 或 `get_datetime`（local）[读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:70]。读可保真，但 `stfld` 左值非法，`ldflda` 需写回 `utcTicks + offset` 换算，复杂度与风险高于别名，故仅作只读备选文档化，不得作为主路径。
- **重构为 utcTicks+offset 换算**：`m_dateTime` 若按 BCL `ClockDateTime`（local）存储则需 `utcTicks + offsetMinutes*TicksPerMinute` 合成；但 BCL `ValidateDate` 已证明 `m_dateTime` 为 UTC（`utcTicks = dateTime.Ticks - offset.Ticks` [读:E:\il2cpp\src\runtime\src\dtoffset.cpp:53]），故直接别名即语义等价，无需加 offset。

---

## 5 预计消除与计数证据

- **消除**：`System_DateTimeOffset.cpp` 中 `m_dateTime` 编译错误 27行（r18定点 27x）必须全部消除；`m_offsetMinutes` 10x 已由既有 1行映射消除（`offsetMinutes`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6858]。
- **计数**：r19 shard2 错误日志中检索 `no member named 'm_dateTime'` 27条独立站点（`arg0->m_dateTime` 赋值4、取值8、`*reinterpret<DateTime*>(&m_dateTime)` 15），误差 ±1 视为抽样误差，必须二次 `rg -n m_dateTime` 复核（验证方法：`Select-String -SimpleMatch m_dateTime` 计数，已执行并保留日志）。
- **未改动**：`System.DateTime`/`TimeSpan`/`Guid`/`Decimal`/`BigInteger` 值类型分支保持原样，不得漂移。

---

## 6 回归风险（DTO按值传递/写回）

- **按值传递**：`System.DateTimeOffset` 为 16B 值类型，跨方法/跨栈槽按值拷贝（`CallExpr` 值参、局部 `loc`）。对 `m_dateTime` 的写入若发生在内联拷贝 `inlN = arg0` 上，仅改拷贝，需外层 `stloc`/`stfld` 写回原位；禁止在 `isValueTypeObject` 分支中将 `argN` 值误判为指针（已依赖 `_curLocKinds` 指针判定 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6897]）。
- **写回**：`FieldAccessStmt` 的 `faTarget = faValue` 对别名 `*reinterpret<DateTime*>(&dto.utcTicks)=value` 等价于 `dto.utcTicks = value.ticks`，8B 原子写入，但 `value` 为 `Il2CppDateTime` 时 `ticks` 联合体 `union{ticks,_ticks,dateData}` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:40] 必须完整拷贝，禁止窄化为 `int32`。
- **取址**：`&m_dateTime` 桥接后为 `&*reinterpret<DateTime*>(&dto.utcTicks)` 折叠为 `DateTime*(&dto.utcTicks)`，与 `il2cpp_time.h:45` 的 `Il2CppDateTime* operator->` 容忍值类型 `->ticks` 访问一致，不得引入 `&(&ptr)` 双重取址（已由 `FieldAddrExpr` 分支 guard [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6887]）。
- **并发/状态**：每次 `ldfld`/`stfld`/`ldflda` 必须与 `EmitExpr` 产生的 `objCode` 1:1配对，禁止 `objCode` 重复求值导致 `il2cpp_null_check` 双求值（`FieldAccess` 对引用路径已在 6973 行单次求值）。

---

## 7 完整diff（精简）

```diff
- // R18-A族字段映射 ... 暂保留原名走后续 icall 路径。
- if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
-     fieldCppName = "offsetMinutes";
- if (isValueTypeObject)
+ // R18-A族字段映射 ... 暂保留原名走后续 icall 路径。
+ if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
+     fieldCppName = "offsetMinutes";
+ // G2a m_dateTime语义桥(27x消除)：...
+ if (owner == "System.DateTimeOffset" && fieldCppName == "m_dateTime")
+ {
+     var baseAccess = FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject);
+     return $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>(&{baseAccess}))";
+ }
+ if (isValueTypeObject)
```

