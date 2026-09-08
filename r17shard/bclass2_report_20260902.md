# B类icall 6臂查表重构报告（bclass2, 2026-09-02）

## 1 任务界定
- **目标**：必须仅改 `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 的 `EmitIcallCallArgs` 标量臂（8240-8325 历史区间，现 7880-8040）中6个`dec/enum/deref`臂，删硬编码改查 `ScalarTable.Table`（`CppType`/`NeedsAddress`），保留 `deref:dto`/`deref:bi`/`pobj`/`unbox:*`/`box:i`等其余臂原样。
- **前提**：`B类` `ScalarTable`单表已落（`IsPrimValTypeShort`/`ScalarElemCpp` 已委托 `IsScalar/TryGet`，`box`/`unbox`双switch已改 `TryGet`），`ScalarTable.cs` 18项只读 `D1` 已验。
- **约束**：`W3` 备份至 `.opencode/backups/CodeGenerator.<时间戳>-beforeBclass2Icall`，`C0` `dotnet build -o temp 0错`，产物 `bclass2_` 前缀写 `r17shard\`，单次 `bash ≤280000ms`。

## 2 实施清单

| 步骤 | 操作 | 证据 |
|------|------|------|
| W3 备份 | `Copy-Item CodeGenerator.cs -> .opencode/backups/CodeGenerator.20260902211122-beforeBclass2Icall` | 读:.opencode/backups/CodeGenerator.20260902211122-beforeBclass2Icall:1 / 执行:Copy-Item:输出 backup:E:\il2cpp\.opencode\backups\CodeGenerator.20260902211122-beforeBclass2Icall (Length 585767, Lines 9058) |
| B类icall 6臂查表 | 新增 `if (cast is "deref:dt" or "deref:ts" or "deref:dec" or "dec" or "enumval" or "enumaddr") { scalarType2 switch + ScalarTable.TryGet(scalarType2, out scalarDesc2) + CppType/ shape-aware }` 并删 `deref:dt`/`deref:ts`/`deref:dec`/`dec`/`enumval`/`enumaddr` 6个 `switch` 硬编码臂，保留 `deref:dto`/`deref:bi` 原样 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7880 / 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8037 |
| C0 编译 | `dotnet build src/il2cppc/il2cppc.csproj -o temp --no-incremental` | 执行:dotnet build:输出 0 个错 7 个警告 (il2cppc -> E:\il2cpp\temp\il2cppc.dll) |
| 产物 | `r17shard\bclass2_*` 前缀 3件 | 执行:Get-ChildItem r17shard\bclass2_*:输出 3 files |

## 3 发射点逐行证据

| 族 | 文件:行号 | 形态 | 保留/重构 |
|----|-----------|------|-----------|
| B-icall 查表入口 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7880 | `if (cast is "deref:dt" or ... or "enumaddr")` | **新增**（6臂路由） |
| B-icall 类型映射 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7885 | `cast switch { "deref:dt"=>"System.DateTime" ... "enumaddr"=>"System.Int64" }` | **新增**（映射到 ScalarTable key） |
| B-icall TryGet | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7893 | `ScalarTable.TryGet(scalarType2, out scalarDesc2)` | **新增**（查表） |
| B-icall deref通用 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7897 | `cast.StartsWith("deref:") => (*reinterpret_cast<CppType*>({code}))` | 使用 `CppType`（DateTime/TimeSpan/Decimal） |
| B-icall dec地址化 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7902 | `cast=="dec" => LocAddr?code:&(code)` | 保留 shape-aware，经表存在性校验 |
| B-icall enumval | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7905 | `cast=="enumval" => static_cast<CppType>(*code/code)` | 使用 `CppType=int64_t`（Int64表项） |
| B-icall enumaddr | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7912 | `cast=="enumaddr" => reinterpret_cast<CppType*>(code/&code)` | 使用 `CppType=int64_t` |
| B-icall 保留dto | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8033 | `"deref:dto" => (*reinterpret_cast<Il2CppDateTimeOffset*>` | 保留原样（不在6臂内，ScalarTable无DTO） |
| B-icall 保留bi | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8036 | `"deref:bi" => (*reinterpret_cast<Il2CppBigInteger*>` | 保留原样（不在6臂内，ScalarTable无BI） |
| 辅助单表 | 读:E:\il2cpp\src\il2cppc\Emit\ScalarTable.cs:8 | `IsScalar/TryGet/CppType/BoxHelper/UnboxHelper/NeedsAddressOf` | 已验证单表 D1 |

## 4 与 A类172 边界验证

- `grep -n "write_barrier" CodeGenerator.cs` 命中仅 `c2.2` 门控（3140-3152），`c2.1`（icall 6臂 7880）零命中。
- `grep -n "ScalarTable" CodeGenerator.cs` 命中 `7068 IsScalar/ScalarElemCpp` 与 `7143/7181 box/unbox` 与 `7893 icall 6臂`，`c2.2`（7470-7600 isinst/castclass）零命中。
- `ScalarTable.cs` 与引用族物理分离（`c2.1` 标量表 vs `c2.2` 引用路径），符合 `r17shard\bclass_c2_boundary.md:22` 契约。

## 5 编译与行数证据

- **备份**：`E:\il2cpp\.opencode\backups\CodeGenerator.20260902211122-beforeBclass2Icall`（9058 lines, 585767 B）— 执行:Copy-Item。
- **重构后**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 约 9080 lines（`+35` 含表路由32行 + 注释3，`-` 6臂硬编码21行 + 注释8，净 `+6`；与全量 `git diff --stat` 中 `1948 insertions` 叠加于历史未提交改动，icall 6臂单项净 `+6` 已隔离验证）。
  - **硬编码键集删除**：`"deref:dt" => $"(*reinterpret_cast<Il2CppDateTime` 0命中、`"deref:ts" =>` 0、`"deref:dec" =>` 0、`"dec" =>` 硬编码 `&(` 0（均经表分支）、`"enumval" => static_cast<int64_t>` 0（经 `CppType`）、`"enumaddr" => reinterpret_cast<int64_t*` 0（经 `CppType`）— 读:CodeGenerator.cs:7880/8037，执行:Select-String 0。
  - **保留臂**：`"deref:dto"` 1、`"deref:bi"` 1 — 读:CodeGenerator.cs:8033。
- **C0**：`dotnet build src/il2cppc/il2cppc.csproj -o temp --no-incremental` → `0 个错 7 个警告`（执行:dotnet build:输出 已成功生成, 7 warnings 为既有 `MetadataLoader.cs:761` 可空与 `StackFold.cs:4198` 未使用本地函数，非本轮引入）— 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7880。

## 6 语义等价性证明

- **deref:dt/ts**：`(*reinterpret_cast<Il2CppDateTime/TimeSpan*>({code}))` ↔ `(*reinterpret_cast<{desc.CppType}*>({code}))`（`CppType` 为 `il2cpp::runtime::Il2CppDateTime/TimeSpan`，来自 `ScalarTable["System.DateTime/TimeSpan"].CppType`）。
- **deref:dec**：`(*reinterpret_cast<Il2CppDecimal*>({code}))` ↔ 同上 `CppType=il2cpp::runtime::Il2CppDecimal`。
- **dec**：`LocAddr?code:&(code)` 保持 shape-aware，新增 `ScalarTable.TryGet("System.Decimal")` 存在性门控（`NeedsAddressOf=true` 语义一致）。
- **enumval/enumaddr**：`static_cast<int64_t>(*code/code)` / `reinterpret_cast<int64_t*>(code/&code)` ↔ `static_cast<{desc.CppType}>(*code/code)` / `reinterpret_cast<{desc.CppType}*>(code/&code)`（`CppType=int64_t` 来自 `ScalarTable["System.Int64"]`，与 `System.Enum` 底层 `int64_t` 载体一致，已验 `ScalarTable.cs:23`）。
- **保留 dto/bi**：未改动，文本逐行一致。
- **验证方法**：`Select-String "ScalarTable.TryGet(scalarType2"` 1命中、`Select-String "CppType"` 在7880块 4命中、`Select-String "\"deref:dt\" =>"` 在switch中 0命中、`dotnet build` 0错。

## 7 产物清单

- `r17shard\bclass2_report_20260902.md`（本文件）
- `r17shard\bclass2_evidence_20260902.txt`（diff 与 build 日志快照）
- `r17shard\bclass2_mapping_20260902.md`（逐类型发射字符串对照）

## 8 与 r17shard 3件产物关联

- `r17shard\bclass_scalar_investigation.md:75` 定义 `ScalarTable` 单表驱动（C6）。
- `r17shard\bclass_line_evidence.md:63` 行数口径 `deletions - insertions ∈[196,238]`；本轮icall 6臂单项净 `+6`（6臂硬编码21行→查表32行，删硬编码但增路由），`box`/`unbox`双switch已净 `-93`（前置），合计B类已消除约 `111-` 后回增6，剩余 `stfld/stind` 待后续。
- `r17shard\bclass_c2_boundary.md:22` 边界图 `c2.1 ScalarTable` 与 `c2.2 ReferenceTable` 分离，本轮未触 `c2.2`。

## 9 未验证项与下一步

- `DateTime/TimeSpan/Decimal` 的 `deref` 新增表路径需在 `r20` 批中对 `boxdeep` e2e 做 `g++ -fsyntax-only` 冒烟（`ScalarTable` 已含符号，发射已通，运行待验）。
- `deref:dto`/`deref:bi` 需在 `ScalarTable` 增 `System.DateTimeOffset`/`System.Numerics.BigInteger` 表项后于下批改查表（当前保留硬编码，符合 `c2.1` 开闭增量）。
- 单次 `bash` 均 `≤280000ms`（本轮 `dotnet build` 2.72s，`git diff` <1s）。
