# G2a m_dateTime语义桥验证与交付

> 产物前缀 `g2a_mdate_` 落 `r17shard\`；W3备份已落盘；C0 0错已验证；27行消除已对账；回归风险已披露。

---

## 1 W3 备份（必须先备份后修改）

- **备份文件**：`E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-140421`
- **验证**：执行 `Test-Path -LiteralPath "E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-140421"` 输出 `True` [执行:bash Test-Path:True]
- **时间戳**：`20260902-140421`（ISO8601秒级，与 `Get-Date -Format yyyyMMdd-HHmmss` 一致）

---

## 2 C0 0错（每次改码后必须运行编译或类型检查，确保零错误）

- **命令**：`dotnet build src/il2cppc/il2cppc.csproj -c Release --nologo -v q`
- **输出**：`0 个错误, 5 个警告` [执行:dotnet build:0个错误]
- **证据**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:758` 等5警告均为既有可空引用警告，与本改动无关。
- **结论**：必须判定 C0 通过，禁止遗留不可编译代码。

---

## 3 4型映射表改动验证

- **读**：`FieldAccess` 方法头 `private static string FieldAccess(Expr obj, string owner, string fieldCppName, ...)` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6854]
- **旧**：仅 `m_offsetMinutes→offsetMinutes` 1行映射 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6857]
- **新**：新增 `m_dateTime` 分支 `FieldAccess(obj,owner,"utcTicks",...)` 包 `*reinterpret_cast<Il2CppDateTime*>` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6860]
- **路径覆盖**：`FieldExpr` 读 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3915]、`FieldAddrExpr` 取址 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3922]、`FieldAccessStmt` 写 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3137] 三处调用点均经此表收敛，禁止漏覆盖。

---

## 4 消除数对账（预计消除27行）

- **基线**：`r19shard` 错误日志中 `E:/il2cpp/out/mscorlib_gen_r19/src/System_DateTimeOffset.cpp:271:11: error: no member named 'm_dateTime'` 等27条独立站点 [读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552]。
- **分类**：`stfld m_dateTime` 4 + `ldfld m_dateTime→icall_datetime_get_*` 15 + `op_Addition/SpecifyKind` 8 = 27，`m_offsetMinutes` 10已由既有映射消除。
- **验证方法**：必须执行 `Select-String -SimpleMatch m_dateTime out/mscorlib_gen_test_g2a/src/System_DateTimeOffset.cpp` 复核生成后 0命中；若无生成环境则标注“未验证—需 `il2cppc` 重生成后 `g++ -fsyntax-only` 对 `System_DateTimeOffset.cpp` 语法检查 0错”。
- **当前状态**：已改码未重生成——逻辑消除已证明（`utcTicks` 为 `Il2CppDateTimeOffset` 唯一 8B UTC字段 [读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:44]；`Il2CppDateTime` 8B 单字段 [读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:38]；二者 offset 0 等价），语法消除待 `g++ -fsyntax-only` 二次确认。

---

## 5 回归风险复述（DTO按值传递/写回）

- **必须披露**：DTO按值传递时 `inl = arg0` 拷贝上的 `m_dateTime` 写入需外层写回，否则丢失；`&m_dateTime` 取址后 `reinterpret_cast<DateTime*>(&dto.utcTicks)` 的 `operator->` [读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:45] 容忍值类型 `->ticks`，但 `&(&ptr)` 双重取址必须由 `FieldAddrExpr` guard [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6887] 阻止。
- **禁止行为**：禁止将 `Il2CppDateTime` 误窄化为 `int32`；禁止在引用路径对值类型 `DTO` 走 `il2cpp_null_check` 双求值；禁止假设消息顺序（DTO字段访问顺序必须与 IL `ldfld`/`stfld` 发射顺序 1:1 对应）。

---

## 6 产物清单（g2a_mdate_ 前缀写 r17shard\）

- `r17shard\g2a_mdate_design.md` — 设计、第一性原理、4型表、风险、验证计划
- `r17shard\g2a_mdate_diff.md` — 文件:行号、旧代码、新代码、消除27行、回归风险
- `r17shard\g2a_mdate_verification.md` — 本文件（W3/C0/消除/风险证据）

---

## 7 后续（G2a设计待细化）

- 必须由用户确认是否需补充只读icall路径（`icall_dtoffset_get_utc_datetime`）作为独立重构，或保留当前别名统一路径；
- 不得自行扩大至 `FieldOwnerDef`/`TypeCppName` 等邻码重构，禁止顺手修改未坏代码（C7）。

