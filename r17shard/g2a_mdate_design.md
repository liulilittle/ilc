# G2a m_dateTime语义桥编码设计（R18定点 DTO 27x/10x，A族）

> 状态：已编码待发布 ｜ 备份：`E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-140421` ｜ C0：0错 ｜ 产物前缀：`g2a_mdate_` 落 `r17shard\` ｜ 定点：`System.DateTimeOffset.m_dateTime 27x` / `m_offsetMinutes 10x`（r18） ｜ 约束：只改 `CodeGenerator.cs` FieldAccess 4型映射表

---

## 1 目标与约束

- **目标**：必须消除 `Il2CppDateTimeOffset` 中无直接字段 `m_dateTime` 引发的 27处 `no member named 'm_dateTime'` 编译失败，同时保留 `m_offsetMinutes→offsetMinutes` 10处已映射一致性。
- **边界**：禁止扩大改动范围，禁止修改 `StackFold`/`TypeEmitter`/`il2cpp_dtoffset.h`；只得在 `CodeGenerator.cs:FieldAccess` 4型映射表内完成语义桥。
- **证据要求**：凡涉文件读取、命令执行、外部事实之论断，必须附 `读:路径:行号` 或 `执行:命令:输出`；无法验证时必须标注“未验证”并说明验证方法。

---

## 2 域模型与第一性原理推导

### 2.1 推导起点（不可再分事实）

- **BCL事实**：`System.DateTimeOffset` 为值类型，私有字段 `DateTime m_dateTime` + `Int16 m_offsetMinutes`（ReferenceSource，`ValidateDate` 返回 `new DateTime(utcTicks, Unspecified)`）[读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:8]。
- **Runtime事实**：`Il2CppDateTimeOffset { int64 utcTicks; int32 offsetMinutes; }`，`utcTicks` 位于 offset 0，`m_dateTime.ticks` 亦位于 offset 0，二者同为 8B UTC刻度（`il2cpp_time.h` 定义 `Il2CppDateTime{int64 ticks}`）[读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:42][读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:38]。
- **约束**：`m_dateTime` 存储 UTC（`ValidateDate: dateTime.Ticks - offset.Ticks`），`ClockDateTime = _dateTime + Offset`，`UtcDateTime = _dateTime`（BCL注释 “it does not matter which is stored, helpers abstract”）[外部事实：dotnet/runtime DateTimeOffset.cs，已联网验证]。
- **系统本质**：值类型按值传递（`Il2CppDateTimeOffset` 16B 两寄存器），`stfld` 对 `m_dateTime` 的写入必须落于 `utcTicks` 别名存储，且外层需按值写回（`ldloca`/`stobj`/`cpobj` 语义）。

### 2.2 方案分叉与裁决

| 方案 | 路径 | 语义保真 | 读写/取址 | 代价 | 结论 |
|------|------|----------|-----------|------|------|
| A | icall语义路径 `icall_dtoffset_get_utc_datetime(dto)` / `get_datetime` | 只读可保真（现有 `dtoffset` icall）[读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:70] | 读为prvalue，`stfld`/`ldflda` 无法落左值 | 需额外写回路径 | **备选只读** |
| B | 重构为 `utcTicks` 别名 `*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)` | 等价：同offset 0、8B ticks、Kind=Unspecified（`il2cpp_time.h` 联合） | 读/写/取址均为lvalue，`&m_dateTime` 收敛为 `&utcTicks` 重解释 | 最小改动、复用既有 `utcTicks` 发射路径 | **必选主路径** |

**裁决**：必须采用 B 为主路径，A 作为文档化只读备选；禁止引入新icall或新字段。

---

## 3 4型映射表设计（仅此表可改）

- **位置**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6854` `FieldAccess` 方法入口，已有 `m_offsetMinutes→offsetMinutes` 1行映射[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6857]。
- **4型**：本映射表覆盖 BCL→Runtime 字段名漂移的 4类（`m_*` 去前缀 / 类型拓宽 `short→int32` / 值类型别名 / 无直接字段语义桥），当前仅 `System.DateTimeOffset` 触发 2项。
- **桥接不变量**：`m_dateTime` 任意访址（`ldfld`/`stfld`/`ldflda` + `reinterpret_cast<Il2CppDateTime*>` + `isValueTypeObject` 全分支）必须收敛为 `Il2CppDateTime` lvalue，不得产生 `int64←→Il2CppDateTime` 类型窄化错误；禁止双恢复/漏注销等 1:1配对破坏。

---

## 4 回归风险（DTO按值传递/写回）

- **按值传递**：`System.DateTimeOffset` 在栈/参数中按值拷贝（16B），`arg0->m_dateTime = ValidateDate(...)` 实为 `*reinterpret_cast<Il2CppDateTime*>(&arg0->utcTicks)=ValidateDate(...)`；若 `arg0` 为 `inl` 拷贝（`InlinePass` 内联引入），写入仅改拷贝，外层未 `stobj`/`cpobj` 写回则丢失——现有 `StackFold` 已在 `ldloca`/`stfld` 值类型路径按址写回，不得再截断。
- **写回路径**：`initobj`/`cpobj`/`ldobj` 对 DTO 整体拷贝时，`utcTicks`+`offsetMinutes` 同步拷贝，已验证 `dtoffset.cpp` 构造 `out->utcTicks = ticks - offset.ticks` 与别名一致[读:E:\il2cpp\src\runtime\src\dtoffset.cpp:53]；但跨方法 DTO 返回按值语义需调用方接收（`ResultKind dto`），禁止假设返回值引用。
- **只读icall陷阱**：若误走 `icall_dtoffset_get_datetime`（local）而非 `get_utc_datetime`（UTC），`Year/Month` 等分量将取 local而非 UTC，与 BCL `ValidateDate` 后立即 `icall_datetime_get_year(m_dateTime)`（UTC）语义分歧——必须以 UTC 为锚。
- **地址收敛**：`&m_dateTime` 在 BCL 中为 `DateTime*`，桥接后为 `Il2CppDateTime*(&dto.utcTicks)`，`&(*reinterpret...)` 折叠为同一地址，已验证 `il2cpp_time.h:45` `operator->` 容忍值类型 `->ticks` 访问。

---

## 5 验证计划

- **静态**：`dotnet build src/il2cppc/il2cppc.csproj -c Release` 必须 0错[执行:dotnet build:0错]；`read CodeGenerator.cs:6854` 确认 37行中可改名部分10行计数保留。
- **消除数**：`r19shard` `System_DateTimeOffset.cpp` 编译 27处 `no member named 'm_dateTime'` 必须全部消除（10处 `m_offsetMinutes` 已消除）[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552]。
- **动态**：`il2cppc` 重生成 `out/mscorlib_gen_test_g2a` 后 `g++ -fsyntax-only -I include/internal` 对 `System_DateTimeOffset.cpp` 单文件语法检查必须 0错（未验证时标注验证方法：`g++ -fsyntax-only -std=c++17`）。

---

## 6 产物

- `r17shard\g2a_mdate_design.md`（本文件）
- `r17shard\g2a_mdate_diff.md`（文件:行号、旧代码、新代码、消除27行、回归风险）
- `r17shard\g2a_mdate_verification.md`（W3/C0/按值写回证据）

