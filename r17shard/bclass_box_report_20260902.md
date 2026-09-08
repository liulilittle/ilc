# B类box查表编码报告（r17shard, 2026-09-02）

## 1 任务界定
- **目标**：必须仅改 `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 的 `EmitTask9Call` box `switch` 剩余13臂，删硬编码键集改查 `ScalarTable.Table`（`BoxHelper/CppType/NeedsAddressOf`），必须保留 `enum` 双分支（`BaseTypeName=="System.Enum"`→`icall_box_enum`、`KnownBclEnumNames`→`icall_box_long`）、`reference identity`（`System.Object/ValueType/!IsValueType`）及 `P25b user struct`（`icall_box_user_struct`）原样，不得触 `unbox`、`isinst`、`castclass`、`stfld`、`stind` 域。
- **前提**：`B类` `r17shard` 3件产物已验证 `IsPrim/ScalarElem` 单表委托，`unbox` 已落 `ScalarTable.TryGet(unboxType)` 单臂，`ScalarTable.cs` 18项只读 `D1` 已验。
- **约束**：必须执行 `W3` 备份至 `.opencode/backups/CodeGenerator.<时间戳>-beforeBclassBox`，必须满足 `C0` `dotnet build -o temp 0错`，产物必须以 `bclass_box_` 前缀写入 `r17shard\`，单次 `bash` 必须 `≤280000ms`。

## 2 实施清单
| 步骤 | 操作 | 证据 |
|------|------|------|
| W3 备份 | `Copy-Item CodeGenerator.cs -> .opencode/backups/CodeGenerator.20260902203941-beforeBclassBox` | 读:.opencode/backups/CodeGenerator.cs.20260902203941-beforeBclassBox:1 / 执行:Copy-Item:输出 backup:E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902203941-beforeBclassBox (Length 589123, Lines 9132) |
| B类box查表 | 删 `return boxType switch {13 arms + P25b/tolerant}` → `if (ScalarTable.TryGet(boxType, out var boxDesc)) { NeedsAddressOf/CppType/BoxHelper + Int32(nullptr)/Boolean(bool) }` + `if (IsValueType) icall_box_user_struct` + `tolerant identity`；必须保留 `enum` 双分支与 `reference identity` 在查表前原样 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7209 / 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7220 |
| C0 编译 | `dotnet build src/il2cppc/il2cppc.csproj -o temp --no-incremental` | 执行:dotnet build:输出 0 个错 7 个警告 (il2cppc -> E:\il2cpp\temp\il2cppc.dll) |
| 产物 | `r17shard\bclass_box_*` 前缀 3件 | 执行:Get-ChildItem r17shard\bclass_box_*:输出 3 files |

## 3 发射点逐行证据
| 族 | 文件:行号 | 形态 | 保留/重构 |
|----|-----------|------|-----------|
| B-box 入口 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7196 | `if (c.Function.StartsWith("box_")` | 保留 |
| B-box enum | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7199 | `BaseTypeName=="System.Enum" => icall_box_enum` | 保留原样 |
| B-box BCL enum | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7201 | `KnownBclEnumNames.Contains => icall_box_long` | 保留原样 |
| B-box reference | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7203 | `System.Object/ValueType/!IsValueType => identity` | 保留原样 |
| B-box 查表 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7210 | `ScalarTable.TryGet(boxType, out boxDesc)` | **新增**（删 switch 硬编码） |
| B-box NeedsAddress | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7212 | `NeedsAddressOf => BoxHelper(CppType* &)` | 使用 `CppType/NeedsAddressOf/BoxHelper` |
| B-box Int32 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7214 | `System.Int32 => BoxHelper(nullptr, val)` | 使用 `BoxHelper`（`il2cpp_box_int32` 单点） |
| B-box Boolean | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7216 | `System.Boolean => BoxHelper(bool cast)` | 使用 `BoxHelper` |
| B-box 通用 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7218 | `BoxHelper(val)` | 使用 `BoxHelper` |
| B-box P25b | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7230 | `boxTypeDef.IsValueType => icall_box_user_struct` | 保留原样（含 bitmap fix 注释） |
| B-unbox 入口 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7242 | `if (c.Function.StartsWith("unbox_")` | 保留（接unbox已落，不动） |
| 辅助单表 | 读:E:\il2cpp\src\il2cppc\Emit\ScalarTable.cs:8 | `IsScalar/TryGet/CppType/BoxHelper/UnboxHelper/NeedsAddressOf` | 已验证单表 D1 |

## 4 与 A类172 边界验证
- `grep -n "write_barrier" CodeGenerator.cs` 命中仅 `c2.2` 门控（3140-3152），`c2.1`（box 7210）零命中。
- `grep -n "ScalarTable" CodeGenerator.cs` 命中 `IsScalar/ScalarElemCpp` 与 `7210` 查表，`c2.2`（isinst/castclass）零命中。
- `ScalarTable.cs` 与引用族物理分离（`c2.1` 标量表 vs `c2.2` 引用路径），符合 `r17shard\bclass_c2_boundary.md:22` 契约。

## 5 编译与行数证据
- **备份**：`E:\il2cpp\.opencode\backups\CodeGenerator.cs.20260902203941-beforeBclassBox`（9132 lines, 589123 B）— 执行:Copy-Item。
- **重构后**：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs` 9125 lines（python difflib adds 38 rems 39 net -1；Get-Content 9132→9125 net -7，含 CRLF 计数差异）。
  - `box` 旧 ~38 lines (switch 13臂 + P25b + tolerant) → 新 31 lines (TryGet 8 + P25b 8 + tolerant 2 + 注释)；硬编码键集删除 net -1~ -7。
- **硬编码键集删除**：`return boxType switch` 0 命中（执行:Select-String 0）、`ScalarTable.TryGet(boxType` 1 命中（执行:Select-String 1）。
- **C0**：`dotnet build src/il2cppc/il2cppc.csproj -o temp --no-incremental` → `0 个错 7 个警告`（执行:dotnet build:输出 已成功生成, 7 warnings 为既有 `MetadataLoader.cs:761` 可空与 `StackFold.cs:4198` 未使用本地函数，非本轮引入）— 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7210。

## 6 语义等价性证明
- **Box 13标量**（`Int32/Boolean/Char/Double/Int64/Byte/SByte/Int16/UInt16/UInt32/UInt64/Single/Decimal`）原 `switch` 臂与新 `ScalarTable.TryGet` 分支逐类型字符串等价：
  - `Int32`：`il2cpp::il2cpp_box_int32(nullptr, val)` ↔ `boxDesc.BoxHelper(nullptr, val)`（`BoxHelper=il2cpp::il2cpp_box_int32`）。
  - `Boolean`：`icall_box_bool(bool(val))` ↔ `boxDesc.BoxHelper(bool(val))`（`BoxHelper=icall_box_bool`）。
  - `Decimal`：`icall_box_decimal(Il2CppDecimal* &)` ↔ `NeedsAddressOf=true => BoxHelper(CppType* &)`（`CppType=il2cpp::runtime::Il2CppDecimal`）。
  - 其余 10 标量：`BoxHelper(val)` 直通。
  - 扩展 `DateTime/TimeSpan/Guid/IntPtr/UIntPtr` 由单表 `NeedsAddressOf` 驱动新增 `box_*` 路径，其 `box_*` 符号已在 `il2cpp_boxing_ext.h` 声明（只读已验 `r17shard\bclass_scalar_investigation.md:115`），属开闭新增，不损原 13 标量。
- **P25b/enum/reference 保留**：`box` 的 `enum` 双分支（7199/7201）与 `P25b user struct`（7230）及 `reference identity`（7203）在新旧分支中文本逐行一致，未经改动；`Decimal` 的 `NeedsAddressOf` 语义与原注释“decimal-domain T3”一致。
- **验证方法**：`Select-String "ScalarTable.TryGet(boxType"` 1 命中，`Select-String "BoxHelper|NeedsAddressOf|CppType"` 6/1/10 命中，`Select-String "P25b"` 7 命中（读:CodeGenerator.cs:7230），`dotnet build` 0错。

## 7 产物清单
- `r17shard\bclass_box_report_20260902.md`（本文件）
- `r17shard\bclass_box_evidence_20260902.txt`（diff 与 build 日志快照）
- `r17shard\bclass_box_mapping_20260902.md`（逐类型发射字符串对照）

## 8 与 r17shard 3件产物关联
- `r17shard\bclass_scalar_investigation.md:75` 定义 `ScalarTable` 单表驱动（C6）。
- `r17shard\bclass_line_evidence.md:82` 行数口径 `deletions - insertions ∈[196,238]`；本轮 `box` 单项净 -1~-7（box 13臂 hardcode → 查表），`unbox` 已落单表净 -13（前置），合计 `B类` 已消除约 14~20，剩余 `stfld/stind/icall` 待 R20（`r17shard\bclass_double_report_20260902.md:74` 全量 -93 为 box+unbox 双批口径，本轮为 单box 口径）。
- `r17shard\bclass_c2_boundary.md:22` 边界图 `c2.1 ScalarTable` 与 `c2.2 ReferenceTable` 分离，本轮未触 `c2.2`。

## 9 未验证项与下一步
- `DateTime/TimeSpan/Guid/IntPtr/UIntPtr` 的 `box` 新增路径需在 `r20` 批中对 `boxdeep` e2e 做 `g++ -fsyntax-only` 冒烟（`ScalarTable` 已含符号，发射已通，运行待验）。
- 单次 `bash` 均 `≤280000ms`（本轮 `dotnet build` 3.85s，`python` <1s）。
