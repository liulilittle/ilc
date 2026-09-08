# G2a3 m_dateTime语义桥差异（仅改 FieldAccess 4型映射表，边界完善）

> 约束：只得修改 `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 的 `FieldAccess` 4型映射表；禁止触 `TypeEmitter`/`StackFold`/`il2cpp_dtoffset.h`；W3已备份 `E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-214500`；C0 0错已验证；产物前缀 `g2a3_` 落 `r17shard\`；父会话 `ses_faec50249ffeP9Xm8rXxPL2fiP` ｜ 子会话 `ses_f9dc49a3cffe21fzp5lHbfh1zt`

---

## 1 文件:行号

- **文件**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
- **行号**：`6600`（方法 `FieldAccess` 入口）+ `6603..6605` 既有映射句 + `6606..6614` G2a3 语义桥边界完善
- **证据**：旧代码见 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6600]；新代码同文件同方法；备份见 [执行:Copy-Item -Destination ".opencode/backups/CodeGenerator.cs.20260902-214500":True]。

---

## 2 旧代码（G2a2）

```csharp
// 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6606
// G2a2 m_dateTime语义桥(27x)：BCL m_dateTime(UTC DateTime,8B)↔runtime utcTicks(int64,offset0)按值别名；读写均走*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)；写回桥 stfld m_dateTime←Il2CppDateTime 的ticks/direct struct assign；ldflda 左值路径 &m_dateTime↔&utcTicks reinterpret（1:1配对，无双求值）
if (owner == "System.DateTimeOffset" && fieldCppName == "m_dateTime")
{
    var baseAccess = FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject);
    return $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>(&{baseAccess}))";
}
if (isValueTypeObject)
{
    // ... M3 Task 11 值类型接收者分支（按址/按值/dot/arrow多态，此处省略，见 6612..6728）
```

- **缺陷（边界待完善）**：`&{baseAccess}` 无显式括号，依赖 `&` 与 `->`/`.` 优先级隐式消歧（`&a->b` 解析为 `&(a->b)` 虽正确但未显式化，G2a3 显式 `&({base})`）；注释未分拆 `stfld` 写回桥的窄化/按值写回/双求值三边界与 `ldflda` 的 `&*` 折叠/双重取址/int64*窄化/7分支复用四边界，审计可追溯性不足。

---

## 3 新代码（G2a3 边界完善）

```csharp
        var objCode = EmitExpr(obj, md, types);
        // R18-A族字段映射(37行中可改名部分10行)：BCL DTO字段 m_offsetMinutes 在 runtime 模型 Il2CppDateTimeOffset 中为 offsetMinutes（无 m_ 前缀，类型 short→int32 兼容）；m_dateTime 无直接对应（DateTime值→int64 utcTicks 需语义桥，暂保留原名走后续 icall 路径）。
        if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
            fieldCppName = "offsetMinutes";
        // G2a3 m_dateTime语义桥(27x)边界完善：BCL m_dateTime(UTC DateTime,8B)↔runtime utcTicks(int64,offset0)按值别名；读写均走*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)；
        // 边界1 stfld写回桥：faTarget=*reinterpret(&utcTicks)为Il2CppDateTime lvalue，faValue为Il2CppDateTime(ValidateDate/unbox)时struct assign即ticks回写(union ticks/dateData同址8B原子)，禁止窄化为int32，禁止双求值(1:1配对)；按值DTO时若obj为inl拷贝需外层stobj/cpobj写回，已由StackFold ldloca路径保证；
        // 边界2 ldflda左值收敛：&FieldAccess(m_dateTime) → &*reinterpret(&utcTicks)折叠为reinterpret(&utcTicks)得Il2CppDateTime*，禁止&(&ptr)双重取址(由FieldAddrExpr单层& guard)，禁止int64*窄化，必须复用utcTicks的isValueTypeObject 7分支判别保证* / -> / .收敛；显式&({base})括号消歧点/箭头优先级，1:1配对无双求值
        if (owner == "System.DateTimeOffset" && fieldCppName == "m_dateTime")
        {
            var baseAccess = FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject);
            return $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>(&({baseAccess})))";
        }
        if (isValueTypeObject)
        {
            // ... 保持原 M3/R12/bcldeep 全分支不变（&、->、dot、ptr、FieldAddrExpr、reinterpret_cast判别）
```

- **要点**：
  1. 必须复用 `FieldAccess(obj, "utcTicks", ...)` 既有发射路径获取 `dto.utcTicks` 访问串（`arg0->utcTicks` / `loc0.utcTicks` / `(&loc0)->utcTicks` / `ldelema->utcTicks` 等），复用 `isValueTypeObject` 7分支判别，保证 1:1配对（每次 `ldloca`/`ldelema`/`FieldAddr` 恰好一次 `->`/`*`/`&({base})`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6609]。
  2. 必须包 `*reinterpret_cast<Il2CppDateTime*>(&({baseAccess}))` 产出 `Il2CppDateTime` lvalue，使三处调用点均收敛：
     - `ldfld`（`FieldExpr`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3758]：读得 `DateTime(utcTicks)`；
     - `stfld`（`FieldAccessStmt: faTarget = faValue`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:2978]：写得 `*reinterpret<DateTime*>(&({dto.utcTicks}))=value` 即 `dto.utcTicks = value.ticks`（写回桥，8B原子，union同址，禁止窄化）；
     - `ldflda`（`&FieldAccess`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3763]：取址得 `&(*reinterpret<DateTime*>(&({dto.utcTicks})))` 折叠为 `DateTime*(&({dto.utcTicks}))`（左值路径，禁止 `&(&ptr)`，禁止 `int64*`）。
  3. **G2a3 显式化**：`&({base})` 括号消歧 `.`/`->` 优先级（`&a->b` 隐式 `&(a->b)`，显式 `&({a->b})` 可审计）；注释分拆两边界各3子项，满足 W5 证据可追溯。
  4. 禁止新增字段、禁止新增icall、禁止改 `TypeEmitter` 布局（`il2cpp_dtoffset.h:42` 已定）；禁止在 `isValueTypeObject` 分支外重复求值 `objCode`。

---

## 4 备选路径（文档化，不落地）

- **icall语义路径（只读）**：`il2cpp::runtime::icall_dtoffset_get_utc_datetime(*reinterpret<Il2CppDateTimeOffset*>(...))` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:70]。读可保真，但 `stfld` 左值非法、`ldflda` 需写回 `utcTicks + offset` 换算，复杂度高于别名，禁止作为主路径。
- **重构为 utcTicks+offset 换算**：若按 BCL `ClockDateTime`（local）存储则需 `utcTicks + offsetMinutes*TicksPerMinute` 合成；但 BCL `ValidateDate` 已证明 `m_dateTime` 为 UTC（`utcTicks = dateTime.Ticks - offset.Ticks` [读:E:\il2cpp\src\runtime\src\dtoffset.cpp:53]），故直接别名即语义等价，无需加 offset。
- **直接 ticks 赋值**：`dto.utcTicks = value.ticks` 与 `*reinterpret<DateTime*>(&dto.utcTicks)=value` 等价（`Il2CppDateTime` 8B联合），但后者保持 `FieldAccess` 统一 lvalue 语义（`ldfld`/`stfld`/`ldflda` 同源），禁止分叉；若未来需 `ticks` 单字段赋值，必须经 `FieldAccess` 外的 `stfld` 特判，当前保持 struct assign。

---

## 5 预计消除与计数证据

- **消除**：`System_DateTimeOffset.cpp` 中 `m_dateTime` 编译错误 27行（r18定点 27x）必须全部消除；`m_offsetMinutes` 10x 已由既有 1行映射消除（`offsetMinutes`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6604]。
- **分类**：`stfld m_dateTime` 4（`ValidateDate` 3 + `unbox` 1）、`ldfld→icall_datetime_get_year/month/day/hour/minute` 15（`get_year/month/day` 各3 + `hour/minute` 各3）、`op_Addition/SpecifyKind/ldobj→inl` 8 =27[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552:271/297/364/409/412/631/663]。
- **G2a3 不变量**：`m_dateTime` 任意访址必须收敛为 `Il2CppDateTime` lvalue，不得产生 `int64*` 窄化；每次访址与 `EmitExpr` 的 `objCode` 1:1配对，禁止双求值。
- **未改动**：`System.DateTime`/`TimeSpan`/`Guid`/`Decimal`/`BigInteger` 值类型分支保持原样，不得漂移。

---

## 6 回归风险（DTO按值传递/写回）

- **按值传递**：DTO 16B 值类型跨方法/跨栈槽按值拷贝，对 `m_dateTime` 的写入若发生在 `inlN = arg0` 拷贝上，仅改拷贝，需外层 `stloc`/`stfld` 写回原位；禁止将 `argN` 值误判为指针（已依赖 `_curLocKinds` 指针判定）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6612]。
- **写回**：`faTarget = faValue` 对别名 `*reinterpret<DateTime*>(&({dto.utcTicks}))=value` 等价于 `dto.utcTicks = value.ticks`，8B 原子写入，`value` 为 `Il2CppDateTime` 时 `ticks` 联合体 `union{ticks,_ticks,dateData}` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:40] 必须完整拷贝，禁止窄化为 `int32`。
- **取址**：`&m_dateTime` 桥接后为 `&*reinterpret<DateTime*>(&({dto.utcTicks}))` 折叠为 `DateTime*(&({dto.utcTicks}))`，与 `il2cpp_time.h:45` 的 `Il2CppDateTime* operator->` 容忍值类型 `->ticks` 访问一致，不得引入 `&(&ptr)` 双重取址（已由 `FieldAddrExpr` 单层 `&` guard）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3763]。
- **括号消歧**：G2a3 新增 `&({base})` 保证 `&` 作用于 `base` 整体，消歧 `&` 与 `->`/`.` 优先级；`&a->b` 隐式 `&(a->b)` 虽正确但显式化后审计可验证。
- **并发/状态**：每次 `ldfld`/`stfld`/`ldflda` 必须与 `EmitExpr` 产生的 `objCode` 1:1配对，禁止 `objCode` 重复求值导致 `il2cpp_null_check` 双求值（引用路径已在 6789 行单次求值）。

---

## 7 完整diff（精简）

```diff
 // R18-A族字段映射 ... 暂保留原名走后续 icall 路径。
 if (owner == "System.DateTimeOffset" && fieldCppName == "m_offsetMinutes")
     fieldCppName = "offsetMinutes";
-// G2a2 m_dateTime语义桥(27x)：BCL m_dateTime(UTC DateTime,8B)↔runtime utcTicks(int64,offset0)按值别名；读写均走*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)；写回桥 stfld m_dateTime←Il2CppDateTime 的ticks/direct struct assign；ldflda 左值路径 &m_dateTime↔&utcTicks reinterpret（1:1配对，无双求值）
-if (owner == "System.DateTimeOffset" && fieldCppName == "m_dateTime")
-{
-    var baseAccess = FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject);
-    return $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>(&{baseAccess}))";
-}
+// G2a3 m_dateTime语义桥(27x)边界完善：BCL m_dateTime(UTC DateTime,8B)↔runtime utcTicks(int64,offset0)按值别名；读写均走*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)；
+// 边界1 stfld写回桥：faTarget=*reinterpret(&utcTicks)为Il2CppDateTime lvalue，faValue为Il2CppDateTime(ValidateDate/unbox)时struct assign即ticks回写(union ticks/dateData同址8B原子)，禁止窄化为int32，禁止双求值(1:1配对)；按值DTO时若obj为inl拷贝需外层stobj/cpobj写回，已由StackFold ldloca路径保证；
+// 边界2 ldflda左值收敛：&FieldAccess(m_dateTime) → &*reinterpret(&utcTicks)折叠为reinterpret(&utcTicks)得Il2CppDateTime*，禁止&(&ptr)双重取址(由FieldAddrExpr单层& guard)，禁止int64*窄化，必须复用utcTicks的isValueTypeObject 7分支判别保证* / -> / .收敛；显式&({base})括号消歧点/箭头优先级，1:1配对无双求值
+if (owner == "System.DateTimeOffset" && fieldCppName == "m_dateTime")
+{
+    var baseAccess = FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject);
+    return $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>(&({baseAccess})))";
+}
 if (isValueTypeObject)
```

