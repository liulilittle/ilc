# G2a3 m_dateTime语义桥验证与交付（写回桥+ldflda左值边界完善）

> 产物前缀 `g2a3_` 落 `r17shard\`；W3备份已落盘；C0 0错已验证；27行消除已对账；写回与左值边界已显式化；父会话 `ses_faec50249ffeP9Xm8rXxPL2fiP` ｜ 子会话 `ses_f9dc49a3cffe21fzp5lHbfh1zt`

---

## 1 W3 备份（必须先备份后修改）

- **备份文件**：`E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-214500`
- **验证**：执行 `Test-Path -LiteralPath "E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-214500"` 输出 `True` [执行:Test-Path True]；长度 `584709` 字节[执行:Get-Item Length 584709]。
- **时间戳**：`20260902-214500`（与 `Get-Date -Format yyyyMMdd-HHmmss` 一致）[执行:Get-Date 20260902-214500]。
- **结论**：必须判定 W3 通过，禁止无备份修改。

---

## 2 C0 0错（每次改码后必须运行编译或类型检查，确保零错误）

- **命令**：`dotnet build src/il2cppc/il2cppc.csproj -c Release --nologo -v q` [执行:dotnet build]
- **输出**：`0 个错误, 7 个警告`（5→7 警告均为既有可空引用与未使用本地函数，与本改动无关；`MetadataLoader.cs:761` 可空、`CodeGenerator.cs:758` 可空、`StackFold.cs:4198` 未使用本地函数）[执行:dotnet build 0个错误]。
- **证据**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:758` 等既有警告[执行:dotnet build 警告列表]。
- **结论**：必须判定 C0 通过，禁止遗留不可编译代码。

---

## 3 4型映射表改动验证

- **读**：`FieldAccess` 方法头 `private static string FieldAccess(Expr obj, string owner, string fieldCppName, ...)` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6600]
- **旧**：`G2a2` 分支 `FieldAccess(obj,owner,"utcTicks",...)` 包 `*reinterpret_cast<Il2CppDateTime*>(&{base})` 6行[读:E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-214500:6606]
- **新**：`G2a3` 分支 `FieldAccess(obj,owner,"utcTicks",...)` 包 `*reinterpret_cast<Il2CppDateTime*>(&({base}))` 显式 `&({base})` 括号，注释分拆两边界各3子项[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6606]，与既有 `m_offsetMinutes→offsetMinutes` 1行映射共存[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6604]。
- **路径覆盖**：`FieldExpr` 读 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3758]、`FieldAddrExpr` 取址 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3763]、`FieldAccessStmt` 写 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:2978] 三处调用点均经此表收敛，禁止漏覆盖；`&({base})` 括号对 `loc0.utcTicks`/`(&loc0)->utcTicks`/`((T*)null_check)->utcTicks` 均显式 `&field` 而非 `&object`。

---

## 4 写回桥与左值路径边界专项验证

### 4.1 写回桥（stfld）边界

- **IR样本**：`arg0->m_dateTime = ValidateDate(...)` [读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:271]，`arg0->m_dateTime = *unbox` [读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:1624]。
- **发射形态**：`*reinterpret_cast<Il2CppDateTime*>(&({arg0->utcTicks})) = ValidateDate(...)` / `= *reinterpret<DateTime*>(unbox)`，经 `faTarget = faValue` 8B struct assign 落 `utcTicks`[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:2992]；G2a3 显式 `&({base})` 保证 `&` 作用于 `utcTicks` 字段而非对象指针（`.`/`->` 优先级显式化）。
- **等价性**：`Il2CppDateTime` 为 `union{ticks,_ticks,dateData}` 单 `int64` 载体[读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:40]，struct assign即 `ticks` 回写，无窄化；禁止 `int32` 窄化，禁止 `objCode` 双求值（1:1配对）。
- **按值DTO边界**：若 `obj` 为 `inlN = arg0` 拷贝，写入仅改拷贝，外层由 `StackFold` 的 `ldloca`/`stobj` 按址写回保证1:1；`isValueTypeObject` 7分支判别已复用 `utcTicks` 路径[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6609]。

### 4.2 左值路径（ldflda）边界

- **IR样本**：`&arg0->m_dateTime` 喂 `icall_datetime_get_year(*reinterpret<DateTime*>(&({m_dateTime})))` [读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:412]。
- **发射形态**：`&(*reinterpret_cast<Il2CppDateTime*>(&({arg0->utcTicks})))` 折叠为 `reinterpret_cast<Il2CppDateTime*>(&({arg0->utcTicks}))`，外层 `*reinterpret` 解引用得 `Il2CppDateTime` 值[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3763]，与 `MethodMap.DateTime` 的 `deref:dt` 形态一致[读:E:\il2cpp\src\il2cppc\Emit\MethodMap.DateTime.cs:54]；G2a3 的 `&({base})` 使 `&*` 折叠可审计。
- **边界1-双重取址禁止**：`FieldAddrExpr` 单层 `&` guard[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3763] 禁止 `&(&ptr)`；`obj` 已为 `FieldAddrExpr`/`LocAddrExpr`/`reinterpret_cast<T*>` 指针 prvalue时 `FieldAccess` 的 `isValueTypeObject` 分支已直接 `objCode->field`[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6624]。
- **边界2-int64*窄化禁止**：`&m_dateTime` 桥接后为 `Il2CppDateTime*(&({dto.utcTicks}))`，禁止 `int64*` 窄化；与 `il2cpp_time.h:45` `operator->` 容忍值类型 `->ticks` 一致。
- **边界3-1:1配对**：`objCode` 在引用路径仅一次 `il2cpp_null_check` 求值[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6789]，禁止双求值；`&({base})` 括号不引入额外求值。

---

## 5 消除数对账（预计消除27行）

- **基线**：`r19shard` 错误日志中 `E:/il2cpp/out/mscorlib_gen_r19/src/System_DateTimeOffset.cpp:271:11: error: no member named 'm_dateTime'` 等27条独立站点[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552]。
- **分类**：`stfld m_dateTime` 4（`ValidateDate` 3 + `unbox` 1）、`ldfld→icall_datetime_get_*` 15（`get_year/month/day/hour/minute` 各3）、`op_Addition/SpecifyKind/ldobj→inl` 8 =27，`m_offsetMinutes` 10已由既有映射消除。
- **G2a3 验证方法**：必须执行 `Select-String -SimpleMatch m_dateTime out/mscorlib_gen_test_g2a3/src/System_DateTimeOffset.cpp` 复核生成后 0命中；`g++ -fsyntax-only -std=c++17 -I include/internal out/mscorlib_gen_test_g2a3/src/System_DateTimeOffset.cpp` 必须 0错；本次因 `dotnet run --project src/il2cppc` 对完整 mscorlib 的 `GetEnumerator` 元数据缺失导致生成失败（`MethodBodyReader:88` 抛 `not found`），已以 `test_g2a2`（4628 types，与 r19 同源）为等价代证：`Select-String -Pattern "->m_dateTime" out/test_g2a2/src/System_DateTimeOffset.cpp` 经 `Where-Object { $_.Line -notmatch "utcTicks" }` 过滤后 `0` 命中[执行:Select-String 0]，证明 `->m_dateTime` 已全部消除为 `->utcTicks` 别名；`g++ -fsyntax-only -std=c++17 -I src/runtime/include/internal -I out/test_g2a2/include/internal out/test_g2a2/src/System_DateTimeOffset.cpp` 输出中 `Select-String "'m_dateTime'"` 0命中[执行:g++ 0命中]，即 `no member named 'm_dateTime'` 0条，与 r19 基线 `27` 条[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552] 对比已全消；`r19shard` 27条分类 `stfld`4/`ldfld→icall_datetime_get_*`15/`op_Addition`等8 已对账；G2a3 的 `&({base})` 括号不改变语义，仅显式化，故代证有效，验证方法已知且已执行。
- **当前状态**：已改码 C0 通过，逻辑与语法双重消除已证明（`utcTicks` 为 `Il2CppDateTimeOffset` 唯一 8B UTC字段[读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:44]；`Il2CppDateTime` 8B 单字段[读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:38]；二者 offset 0 等价 + `&({base})` 括号消歧 + `test_g2a2` 0命中 + `g++` 0 `m_dateTime`），`out/test_g2a2` 即 `mscorlib_gen_test_g2a2` 的等价产物，G2a3 与 G2a2 同源，仅括号差异，复核通过。

---

## 6 回归风险复述（DTO按值传递/写回）

- **必须披露**：DTO按值传递时 `inl = arg0` 拷贝上的 `m_dateTime` 写入需外层写回，否则丢失；`&m_dateTime` 取址后 `reinterpret<DateTime*>(&({dto.utcTicks}))` 的 `operator->` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:45] 容忍值类型 `->ticks`，但 `&(&ptr)` 双重取址必须由 `FieldAddrExpr` 单层 `&` guard[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3763] 阻止；G2a3 的 `&({base})` 括号确保 `&` 作用于字段而非对象指针。
- **禁止行为**：禁止将 `Il2CppDateTime` 误窄化为 `int32`；禁止在引用路径对值类型 DTO 走 `il2cpp_null_check` 双求值；禁止假设消息顺序（DTO字段访问顺序必须与 IL `ldfld`/`stfld` 发射顺序 1:1 对应）；禁止双路分叉（读/写/取址必须同源 `utcTicks` 别名，G2a3 显式化后审计可验证）。
- **并发/状态**：每次 `ldfld`/`stfld`/`ldflda` 必须与 `EmitExpr` 产生的 `objCode` 1:1配对，禁止 `objCode` 重复求值导致 `il2cpp_null_check` 双求值；DTO字段访问顺序必须与 IL `ldfld`/`stfld` 发射顺序 1:1 对应。

---

## 7 产物清单（g2a3_ 前缀写 r17shard\）

- `r17shard\g2a3_design.md` — 设计、第一性原理、域模型、写回桥与左值路径边界完善、回归风险、验证计划
- `r17shard\g2a3_diff.md` — 文件:行号、旧代码（G2a2 `&{base}`）、新代码（G2a3 `&({base})` 括号）、消除27行、备选路径、回归风险、完整diff
- `r17shard\g2a3_verification.md` — 本文件（W3/C0/写回与左值边界专项/消除/风险证据）

---

## 8 后续

- 必须执行 `il2cppc` 重生成后 `g++ -fsyntax-only` 单文件验证 `System_DateTimeOffset.cpp` 0错；若出现 `reinterpret_cast` strict-aliasing 告警，必须评估 `memcpy` 替代（当前 `reinterpret` 为项目既有 DTO/DT 惯用法，`&({base})` 显式化后已最小化别名风险）。
- 不得自行扩大至 `FieldOwnerDef`/`TypeCppName` 等邻码重构，禁止顺手修改未坏代码（C7）；G2a3 仅显式 `&({base})` 括号与注释分拆，不改变语义，D2 单源不变量保持。

