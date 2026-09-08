# G2a3 m_dateTime语义桥边界完善设计（A族 27x 写回桥+ldflda左值收敛）

> 状态：已编码待发布 ｜ 备份：`E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902-214500` ｜ C0：0错 ｜ 产物前缀：`g2a3_` 落 `r17shard\` ｜ 定点：`System.DateTimeOffset.m_dateTime 27x`（r18/r19） ｜ 约束：只改 `CodeGenerator.cs` FieldAccess 4型映射表（~6673区） ｜ 父会话：`ses_faec50249ffeP9Xm8rXxPL2fiP` ｜ 子会话：`ses_f9dc49a3cffe21fzp5lHbfh1zt`

---

## 1 目标与约束

- **目标**：必须在已落 `m_dateTime→utcTicks` 别名读路径基础上，完善写回桥（`stfld m_dateTime ← Il2CppDateTime` 的 `utcTicks` 回写）及 `ldflda` 左值收敛的边界条件，消除剩余 `no member named 'm_dateTime' in 'Il2CppDateTimeOffset'` 27处编译失败，并保持 `m_offsetMinutes→offsetMinutes` 10处已映射一致性。
- **边界**：禁止扩大改动范围，禁止修改 `StackFold`/`TypeEmitter`/`il2cpp_dtoffset.h`/`il2cpp_time.h`；只得在 `CodeGenerator.cs:FieldAccess` 4型映射表内完成语义桥；禁止新增icall或新字段；显式 `&({base})` 括号消歧点/箭头优先级。
- **证据要求**：凡涉文件读取、命令执行、外部事实之论断，必须附 `读:路径:行号` 或 `执行:命令:输出`；无法验证时必须标注“未验证”并说明验证方法。

---

## 2 域模型与第一性原理推导

### 2.1 推导起点（不可再分事实）

- **BCL事实**：`System.DateTimeOffset` 为值类型，私有字段 `DateTime m_dateTime` + `Int16 m_offsetMinutes`（ReferenceSource，`ValidateDate` 返回 `new DateTime(utcTicks, Unspecified)`）[读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:8]。
- **Runtime事实**：`Il2CppDateTimeOffset { int64 utcTicks; int32 offsetMinutes; }`，`utcTicks` 位于 offset 0，`m_dateTime.ticks` 亦位于 offset 0，二者同为 8B UTC刻度（`il2cpp_time.h` 定义 `Il2CppDateTime{int64 ticks}` 联合 `ticks/_ticks/dateData`）[读:E:\il2cpp\src\runtime\include\internal\il2cpp_dtoffset.h:42][读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:38]。
- **约束**：`m_dateTime` 存储 UTC（`ValidateDate: dateTime.Ticks - offset.Ticks`），`ClockDateTime = _dateTime + Offset`，`UtcDateTime = _dateTime`（BCL注释 “it does not matter which is stored, helpers abstract”）[外部事实：dotnet/runtime DateTimeOffset.cs，已联网验证]。
- **系统本质**：值类型按值传递（`Il2CppDateTimeOffset` 16B 两寄存器），`stfld` 对 `m_dateTime` 的写入必须落于 `utcTicks` 别名存储，且外层需按值写回（`ldloca`/`stobj`/`cpobj` 语义）；`ldflda` 对 `m_dateTime` 取址必须产生 `Il2CppDateTime*` 左值而非 `int64*`，且 `&` 与 `->`/`.` 的优先级必须显式括号消歧。

### 2.2 已落与待完善

- **已落**：`m_offsetMinutes→offsetMinutes` 重命名已落（1行映射）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6604]；`m_dateTime→utcTicks` 读别名已落（`g2a2_design.md` 方案B）代码在 [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6607]，覆盖 `ldfld`/`stfld`/`ldflda` 同源 `*reinterpret<Il2CppDateTime*>(&base)`。
- **待完善**：`stfld` 写回桥的边界（`Il2CppDateTime` struct assign vs `ticks` 窄化、按值DTO的 `inl` 拷贝写回、1:1配对禁止双求值）及 `ldflda` 左值收敛的边界（`&*` 折叠、`&(&ptr)` 禁止、`int64*` 窄化禁止、`&({base})` 括号消歧 `.`/`->` 优先级、复用 `isValueTypeObject` 7分支）必须显式完善，不得引入第二路径分叉（D2禁止双路分叉）。

### 2.3 方案分叉与裁决

| 方案 | 路径 | 语义保真 | 读写/取址 | 代价 | 结论 |
|------|------|----------|-----------|------|------|
| A | icall语义路径 `icall_dtoffset_get_utc_datetime(dto)` 读 / `icall_dtoffset_ctor` 写 | 只读可保真 | 读为prvalue，`stfld`/`ldflda` 无法落左值，需额外写回icall | 需新增写路径，破坏按值写回 | **禁止** |
| B | 别名 `*reinterpret_cast<Il2CppDateTime*>(&dto.utcTicks)` + `&({base})` 括号 | 等价：同offset 0、8B ticks、Kind=Unspecified（`il2cpp_time.h` 联合） | 读/写/取址均为lvalue，`&m_dateTime` 收敛为 `&utcTicks` 重解释，显式括号消歧 | 最小改动、复用既有 `utcTicks` 发射路径，1:1配对 | **必须采用** |

**裁决**：必须采用 B 为唯一主路径，A 禁止落地；写回与取址均复用同一别名表达式，不得分叉；G2a3 在 B 基础上显式 `&({base})` 括号并文档化两边界。

---

## 3 4型映射表设计（仅此表可改）

- **位置**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6600` `FieldAccess` 方法入口，已有 `m_offsetMinutes→offsetMinutes` 1行映射[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6604]。
- **4型**：本映射表覆盖 BCL→Runtime 字段名漂移的 4类（`m_*` 去前缀 / 类型拓宽 `short→int32` / 值类型别名 / 无直接字段语义桥），当前仅 `System.DateTimeOffset` 触发 2项；本次完善第2项的写回与取址边界。
- **桥接不变量**：`m_dateTime` 任意访址（`ldfld`/`stfld`/`ldflda` + `reinterpret_cast<Il2CppDateTime*>` + `isValueTypeObject` 全分支）必须收敛为 `Il2CppDateTime` lvalue，不得产生 `int64←→Il2CppDateTime` 类型窄化错误；每次 `ldfld`/`stfld`/`ldflda` 必须与 `EmitExpr` 产生的 `objCode` 1:1配对，禁止 `objCode` 重复求值导致 `il2cpp_null_check` 双求值；`&({base})` 括号必须显式消歧 `.`/`->` 优先级。

---

## 4 写回桥与左值路径边界完善

### 4.1 写回桥（stfld）边界

- **IR**：`FieldAccessStmt { Object, Owner=System.DateTimeOffset, Field=m_dateTime, Value=Il2CppDateTime }` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:2978]
- **发射**：`faTarget = FieldAccess(obj, "System.DateTimeOffset", "m_dateTime", ...)` 产出 `(*reinterpret_cast<Il2CppDateTime*>(&({base}))`，其中 `base = FieldAccess(obj,"utcTicks",...)` 为 `arg0->utcTicks` / `loc0.utcTicks` / `(&loc0)->utcTicks` 等[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6609]，`faValue` 为 `Il2CppDateTime`（`ValidateDate` 返回或 `unbox_user_struct` 载荷）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:2992]，最终 `*reinterpret<DateTime*>(&({dto.utcTicks})) = value` 等价于 `dto.utcTicks = value.ticks`（8B 原子拷贝，`Il2CppDateTime` 联合 `ticks/_ticks/dateData` 同址）[读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:40]。
- **边界1-窄化禁止**：`Il2CppDateTime` 为 8B 联合，struct assign即 `ticks` 回写，禁止窄化为 `int32`；`value` 为 `Il2CppDateTime` 时必须完整拷贝，不得取 `value.ticks` 单字段再赋值（保持 `dateData` 联合语义）。
- **边界2-按值写回**：DTO为16B值类型，若 `obj` 为 `inlN = arg0` 拷贝，写入仅改拷贝，外层由 `StackFold` 的 `ldloca`/`stobj` 按址写回保证1:1；禁止在 `isValueTypeObject` 分支中将 `argN` 值误判为指针（依赖 `_curLocKinds` 指针判定）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6612]；`stfld` 的 `faTarget = faValue` 必须与 `FieldAccess` 的 `&({base})` 1:1配对，禁止 `objCode` 重复求值导致 `il2cpp_null_check` 双求值。
- **边界3-括号消歧**：`&({base})` 显式括号保证 `&` 作用于 `base` 整体（`a->b` / `a.b` 先于 `&`），避免 `&a->b` 解析歧义；对 `loc0.utcTicks` / `(&loc0)->utcTicks` / `((T*)null_check)->utcTicks` 均收敛为 `&field` 而非 `&object`。

### 4.2 左值路径（ldflda）边界

- **IR**：`FieldAddrExpr { Object, Owner=System.DateTimeOffset, Field=m_dateTime }` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3758]
- **发射**：`&FieldAccess(...)` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3763] 产出 `&(*reinterpret_cast<Il2CppDateTime*>(&({base})))` 折叠为 `reinterpret_cast<Il2CppDateTime*>(&({base}))`，与 `il2cpp_time.h:45` `operator->` 容忍值类型 `->ticks` 访问一致[读:E:\il2cpp\src\runtime\include\internal\il2cpp_time.h:45]，不得引入 `&(&ptr)` 双重取址（已由 `FieldAddrExpr` 单层 `&` guard）。
- **边界1-折叠收敛**：`&*` 折叠得 `Il2CppDateTime*(&dto.utcTicks)`，与 `int64*` 窄化禁止；消费侧 `icall_datetime_get_year(*reinterpret<Il2CppDateTime*>(&({dto.utcTicks})))` 等 `deref:dt` 形态直接解引用该左值，无需额外 `icall_dtoffset_get_utc_datetime`[读:E:\il2cpp\src\il2cppc\Emit\MethodMap.DateTime.cs:54]。
- **边界2-双重取址禁止**：若 `obj` 已为 `FieldAddrExpr`/`LocAddrExpr`/`reinterpret_cast<T*>` 指针 prvalue，`FieldAccess` 的 `isValueTypeObject` 分支已直接 `objCode->field`（`FieldAddrExpr` 65行/ `reinterpret` 68行），禁止再 `&(&ptr)`。
- **边界3-7分支复用**：必须复用 `FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject)` 既有发射路径获取 `dto.utcTicks` 访问串（`arg0->utcTicks` / `loc0.utcTicks` / `(&loc0)->utcTicks` / `ldelema->utcTicks` 等），复用 `isValueTypeObject` 7分支判别，保证 1:1配对（每次 `ldloca`/`ldelema`/`FieldAddr` 恰好一次 `->`/`*`/`&({base})`）。

### 4.3 复用与1:1配对

- 必须复用 `FieldAccess(obj, owner, "utcTicks", md, types, isValueTypeObject)` 既有发射路径，保证 1:1配对（每次 `ldloca`/`ldelema`/`FieldAddr` 恰好一次 `->`/`*`）。
- 禁止双恢复/漏注销等配对破坏；`objCode` 在引用路径仅求值一次（`reinterpret_cast<Il2CppObject*>(il2cpp_null_check(...))`）[读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6789]。
- `&({base})` 括号为本次 G2a3 新增边界完善点，G2a2 的 `&{base}` 在无括号时依赖优先级，G2a3显式化后与 `FieldAddrExpr` 的 `&` 共同保证无歧义。

---

## 5 回归风险（DTO按值传递/写回）

- **按值传递**：`arg0->m_dateTime = ValidateDate(...)` 实为 `*reinterpret<Il2CppDateTime*>(&({arg0->utcTicks}))=ValidateDate(...)`；若 `arg0` 为 `inl` 拷贝，外层未 `stobj`/`cpobj` 写回则丢失——现有 `StackFold` 已在 `ldloca`/`stfld` 值类型路径按址写回，不得再截断。
- **写回路径**：`initobj`/`cpobj`/`ldobj` 对 DTO 整体拷贝时 `utcTicks`+`offsetMinutes` 同步拷贝，已验证 `dtoffset.cpp` 构造 `out->utcTicks = ticks - offset.ticks` 与别名一致[读:E:\il2cpp\src\runtime\src\dtoffset.cpp:53]；跨方法 DTO 返回按值语义需调用方接收（`ResultKind dto`）。
- **只读icall陷阱**：若误走 `icall_dtoffset_get_datetime`（local）而非 UTC 别名，`Year/Month` 等分量将取 local而非 UTC，与 BCL `ValidateDate` 后立即 `icall_datetime_get_year(m_dateTime)`（UTC）语义分歧——必须以 UTC 为锚。
- **地址收敛**：`&m_dateTime` 桥接后为 `Il2CppDateTime*(&({dto.utcTicks}))`，`&(*reinterpret...)` 折叠为同一地址，不得产生 `int64*` 窄化；G2a3 的 `&({base})` 括号确保 `&` 作用于字段而非对象指针。
- **并发/状态**：每次 `ldfld`/`stfld`/`ldflda` 必须与 `EmitExpr` 产生的 `objCode` 1:1配对，禁止 `objCode` 重复求值导致 `il2cpp_null_check` 双求值；DTO字段访问顺序必须与 IL `ldfld`/`stfld` 发射顺序 1:1 对应，禁止双路分叉。

---

## 6 验证计划

- **静态**：`dotnet build src/il2cppc/il2cppc.csproj -c Release` 必须 0错[执行:dotnet build:0错]；`read CodeGenerator.cs:6600` 确认新增 `&({base})` 括号与既有 `m_offsetMinutes` 映射共存。
- **消除数**：`r19shard` `System_DateTimeOffset.cpp` 编译 27处 `no member named 'm_dateTime'` 必须全部消除（4×`stfld` + 15×`ldfld→icall_datetime_get_*` + 8×`op_Addition/SpecifyKind/ldobj`）[读:E:\il2cpp\r19shard\r19shard补发_shard2_errors.log:12552]，`m_offsetMinutes` 10x 保持消除。
- **动态**：`il2cppc` 重生成 `out/mscorlib_gen_test_g2a3` 后 `Select-String m_dateTime` 必须 0命中，`g++ -fsyntax-only -std=c++17 -I include/internal` 对 `System_DateTimeOffset.cpp` 单文件语法检查必须 0错（未验证时标注验证方法：`g++ -fsyntax-only ...`，本次因生成超时标注“未验证—需重生成后复核”，逻辑正确性已由别名等价+括号消歧证明）。

