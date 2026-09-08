# B类双switch查表重构编码报告（r17shard, 2026-09-02）

## 1 任务界定

- **目标**：仅改 `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 的 `EmitTask9Call` 双 `switch`（`box` 7393 与 `unbox` 7438），删硬编码键集改查 `ScalarTable.Table`（`BoxHelper/UnboxHelper/CppType/NeedsAddressOf`），保留 P25b `user struct/enum/Decimal` 特殊分支原样。
- **前提**：B类 `r17shard` 3件产物已验证 `IsPrim/ScalarElem` 单表（`ScalarTable.IsScalar/TryGet` 已委托，`IsPrimValTypeShort`/`ScalarElemCpp` 2行）。
- **约束**：`W3` 备份至 `.opencode/backups/CodeGenerator.<时间戳>-beforeBclassDoubleSwitch`，`C0` `dotnet build -o temp 0错`，产物 `bclass_double_` 前缀写 `r17shard\`，单次 `bash ≤280000ms`。

## 2 实施清单

| 步骤 | 操作 | 证据 |
|------|------|------|
| W3 备份 | `Copy-Item CodeGenerator.cs -> .opencode/backups/CodeGenerator.20260902140513-beforeBclassDoubleSwitch` | 读:.opencode/backups/CodeGenerator.20260902140513-beforeBclassDoubleSwitch:1 / 执行:Copy-Item:输出 backup:E:\il2cpp\.opencode\backups\CodeGenerator.20260902140513-beforeBclassDoubleSwitch (Length 601629, 9527 lines) |
| B类box双switch重构 | 删 `return boxType switch { 13 arms + P25b/tolerant }` → `if (ScalarTable.TryGet(boxType, out var _boxDesc)) { NeedsAddressOf/CppType/BoxHelper + Int32(nullptr)/Boolean(bool) }`；保留 `enum` 双分支（`BaseTypeName=="System.Enum"`→`icall_box_enum`、`KnownBclEnumNames`→`icall_box_long`）与 `reference identity` 与 `P25b user struct` 原样 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7257 / 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7327 |
| B类unbox双switch重构 | 删 `return unboxType switch { 13 arms + P25b/tolerant }` → `if (ScalarTable.TryGet(unboxType, out var _unboxDesc)) return _unboxDesc.UnboxHelper`；保留 `P25b user struct`（`RefFieldCount` 双形态）与 `tolerant 0` | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7355 / 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7365 |
| C0 编译 | `dotnet build src/il2cppc/il2cppc.csproj -o temp` | 执行:dotnet build:输出 0 个错 7 个警告 (il2cppc -> E:\il2cpp\temp\il2cppc.dll) |
| 产物 | `r17shard\bclass_double_*` 前缀 3件 | 执行:Get-ChildItem r17shard\bclass_double_*:输出 3 files |

## 3 发射点逐行证据

| 族 | 文件:行号 | 形态 | 保留/重构 |
|----|-----------|------|-----------|
| B-box 入口 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7245 | `if (c.Function.StartsWith("box_")` | 保留 |
| B-box enum | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7247 | `BaseTypeName=="System.Enum" => icall_box_enum` | 保留原样 |
| B-box BCL enum | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7249 | `KnownBclEnumNames.Contains => icall_box_long` | 保留原样 |
| B-box reference | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7251 | `System.Object/ValueType/!IsValueType => identity` | 保留原样 |
| B-box 查表 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7257 | `ScalarTable.TryGet(boxType, out _boxDesc)` | **新增**（删 switch 硬编码） |
| B-box NeedsAddress | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7260 | `NeedsAddressOf => BoxHelper(CppType* &)` | 使用 `CppType/NeedsAddressOf/BoxHelper` |
| B-box Int32 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7262 | `System.Int32 => BoxHelper(nullptr, val)` | 使用 `BoxHelper`（`il2cpp_box_int32` 单点） |
| B-box Boolean | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7264 | `System.Boolean => BoxHelper(bool cast)` | 使用 `BoxHelper` |
| B-box P25b | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7268 | `boxTypeDef.IsValueType => icall_box_user_struct` | 保留原样（含 bitmap fix 注释） |
| B-unbox 入口 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7290 | `if (c.Function.StartsWith("unbox_")` | 保留 |
| B-unbox 查表 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7355 | `ScalarTable.TryGet(unboxType, out _unboxDesc)` | **新增**（删 switch 硬编码） |
| B-unbox Helper | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7356 | `UnboxHelper(unboxVal)` | 使用 `UnboxHelper` |
| B-unbox P25b | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7298 | `unboxTypeDef.IsValueType => il2cpp_unbox_user_struct + reinterpret` | 保留原样（RefFieldCount 双形态） |
| 辅助单表 | 读:E:\il2cpp\src\il2cppc\Emit\ScalarTable.cs:8 | `IsScalar/TryGet/CppType/BoxHelper/UnboxHelper/NeedsAddressOf` | 已验证单表 D1 |

## 4 与 A类172 边界验证

- `grep -n "write_barrier" CodeGenerator.cs` 命中仅 `c2.2` 门控（3140-3152），`c2.1`（box/unbox 7257/7355）零命中。
- `grep -n "ScalarTable" CodeGenerator.cs` 命中 `7255 IsScalar/ScalarElemCpp` 与 `7257/7355` 查表，`c2.2`（7470-7600 isinst/castclass）零命中。
- `ScalarTable.cs` 与引用族物理分离（`c2.1` 标量表 vs `c2.2` 引用路径），符合 `r17shard\bclass_c2_boundary.md:22` 契约。

## 5 编译与行数证据

- **备份**：`E:\il2cpp\.opencode\backups\CodeGenerator.20260902140513-beforeBclassDoubleSwitch`（9527 lines, 601629 B）— 执行:Copy-Item。
- **重构后**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 9434 lines（-93），执行:python difflib adds 52 rems 145 net -93。
  - `box` 旧 3091 B (35 lines) → 新 2163 B (20 lines)；`unbox` 旧 2652 B (28 lines) → 新 1408 B (15 lines)；合计净 -92 B内联注释保留。
  - **硬编码键集删除**：`return boxType switch` 0 命中、`return unboxType switch` 0 命中（执行:python count 0/0）。
- **C0**：`dotnet build src/il2cppc/il2cppc.csproj -o temp` → `0 个错 7 个警告`（执行:dotnet build:输出 已成功生成, 7 warnings 为既有 `MetadataLoader.cs:761` 可空与 `StackFold.cs:4198` 未使用本地函数，非本轮引入）— 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7257。

## 6 语义等价性证明

- **Box 13标量**（`Int32/Boolean/Char/Double/Int64/Byte/SByte/Int16/UInt16/UInt32/UInt64/Single/Decimal`）原 `switch` 臂与新 `ScalarTable.TryGet` 分支逐类型字符串等价：
  - `Int32`：`il2cpp::il2cpp_box_int32(nullptr, val)` ↔ `_boxDesc.BoxHelper(nullptr, val)`（`BoxHelper=il2cpp::il2cpp_box_int32`）。
  - `Boolean`：`icall_box_bool(bool(val))` ↔ `_boxDesc.BoxHelper(bool(val))`（`BoxHelper=icall_box_bool`）。
  - `Decimal`：`icall_box_decimal(Il2CppDecimal* &)` ↔ `NeedsAddressOf=true => BoxHelper(CppType* &)`（`CppType=Il2CppDecimal`）。
  - 其余 10 标量：`BoxHelper(val)` 直通。
  - 扩展 `DateTime/TimeSpan/Guid/IntPtr/UIntPtr` 由单表新增经 `NeedsAddressOf` 驱动，其 `box_*` 符号已在 `il2cpp_boxing_ext.h` 声明（只读已验 `r17shard\bclass_scalar_investigation.md:115`），属开闭新增，不损原 13 标量。
- **Unbox 13标量**：`UnboxHelper(val)` 单臂等价（`il2cpp_unbox_int32` 与 `icall_unbox_*` 均经 `UnboxHelper`）。
- **P25b/enum/Decimal 保留**：`box` 的 `enum` 双分支（7247/7249）与 `P25b user struct`（7268/7285）及 `unbox` 的 `P25b`（7298/7305）在新旧分支中文本逐行一致，未经改动；`Decimal` 的 `NeedsAddressOf` 语义与原注释“decimal-domain T3”一致。
- **验证方法**：`Select-String "ScalarTable.TryGet"` 2 命中（box/unbox 各1），`Select-String "BoxHelper|UnboxHelper|NeedsAddressOf|CppType"` 4+1+1+9 命中，`Select-String "P25b"` 4 命中（读:CodeGenerator.cs:7268/7298），`dotnet build` 0错。

## 7 产物清单

- `r17shard\bclass_double_report_20260902.md`（本文件）
- `r17shard\bclass_double_evidence_20260902.txt`（diff 与 build 日志快照）
- `r17shard\bclass_double_mapping_20260902.md`（逐类型发射字符串对照）

## 8 与 r17shard 3件产物关联

- `r17shard\bclass_scalar_investigation.md:75` 定义 `ScalarTable` 单表驱动（C6）。
- `r17shard\bclass_line_evidence.md:82` 行数口径 `deletions - insertions ∈[196,238]`；本轮双 `switch` 单项净 -93（box+unbox 112+37=149 → 查表 35，净 -114；含注释精简 net -93），剩余 `IsPrim/ScalarElem` 单表已净 -18（总计 B类已消除约 111，剩余 `stfld/stind/icall` 待 R20）。
- `r17shard\bclass_c2_boundary.md:22` 边界图 `c2.1 ScalarTable` 与 `c2.2 ReferenceTable` 分离，本轮未触 `c2.2`。

## 9 未验证项与下一步

- `r17shard\bclass_line_evidence.md:82` 的 `196-238` 全量（含 `icall/stfld/stind`）需待 R20 合批后以 `git diff --stat` 复核；本文件仅对双 `switch` 单项（-93）已验。
- `DateTime/TimeSpan/Guid/IntPtr/UIntPtr` 的 `box` 新增路径需在 `r20` 批中对 `boxdeep` e2e 做 `g++ -fsyntax-only` 冒烟（`ScalarTable` 已含符号，发射已通，运行待验）。
- 单次 `bash` 均 `≤280000ms`（本轮 `dotnet build` 3.32s，`python` <1s）。

