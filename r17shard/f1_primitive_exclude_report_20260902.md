# F1 primitive排除编码 — TypeEmitter.FindEmbeddedValueType 修复报告

> 子会话：`ses_f9fd9389fffeVApuvRZDcrylFp` / 父会话：`ses_faec50249ffeP9Xm8rXxPL2fiP`
> 约束：只改 `E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:719-726` `FindEmbeddedValueType`，按 `FieldValueTypeKind` 原语表排除，防 `System_Int32` 嵌入声明
> 前缀：`f1_*` / 路径：`r17shard\`

---

## 1 变更范围与证据

- **修改文件**：`E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:719-736` [读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:719]
- **变更前**：
  ```csharp
  public static TypeDef? FindEmbeddedValueType(FieldDef f, IReadOnlyList<TypeDef>? allTypes)
  {
      if (allTypes == null || string.IsNullOrEmpty(f.FieldTypeName)) return null;
      return allTypes.FirstOrDefault(x =>
          x.IsValueType && x.BaseTypeName != "System.Enum" &&
          (x.FullName == f.FieldTypeName || ReflectionFullName(x) == f.FieldTypeName
           || x.Name == f.FieldTypeName || x.FullName.EndsWith("+" + f.FieldTypeName, StringComparison.Ordinal)));
  }
  ```
- **变更后**：在空检查后插入原语排除守卫，命中则 `return null`：
  ```csharp
  if (f.FieldTypeName is "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char"
      or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
      or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double"
      or "System.IntPtr" or "System.UIntPtr") return null;
  ```
  [读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:722]
- **原语表依据**：`CodeGenerator.cs:FieldValueTypeKind` — `System.Boolean/Byte/SByte/Char/Int16/UInt16/Int32/UInt32 => val`，`Int64/UInt64 => l`，`Single/Double => d`，`IntPtr/UIntPtr => ptr` [读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:6711]
- **禁止扩大**：未改 `EmbeddedValueShape`、`LayoutInstanceFieldsCore`、`EmitClassStruct`、`CodeGenerator.FindEmbeddedValueType` 调用点；满足"只改 719-726"硬约束。

## 2 W3 备份

- 必须先备份至 `.opencode/backups/TypeEmitter.<时间戳>`；已执行 `Copy-Item TypeEmitter.cs -> TypeEmitter.20260902_113120` [执行:Copy-Item TypeEmitter.cs -> TypeEmitter.20260902_113120:True]
- 备份文件大小 107326 B，与改前一致；时间戳 `20260902_113120`。
- 回滚条件：仅当 `FindEmbeddedValueType` 原语排除导致 `ConcurrentQueue+Segment` 等真实值类型被误排除时方可回滚，且必须书面说明理由；当前排除集不含 `System_Guid`/`System.Decimal`/`System.DateTime` 等 BCL 16/8 字节结构体，故不触发。

## 3 C0 编译验证

- 命令：`dotnet build "E:\il2cpp\src\il2cppc\il2cppc.csproj" -o "E:\il2cpp\temp" --nologo` [执行:dotnet build -o temp:0错]
- 输出：`il2cppc -> E:\il2cpp\temp\il2cppc.dll`，`5 警告 0 错误`，`已成功生成` [执行:dotnet build:0个错]
- 警告仅为既有 `CS8604/CS8601/CS8321`（`CodeGenerator.cs:758` / `MetadataLoader.cs:761,770,834` / `StackFold.cs:4198`），与本次修改无关，未引入新警告。
- 产物：`E:\il2cpp\temp\il2cppc.dll` 已生成；C0 门禁通过。

## 4 根因与 208 行族

- **根因**：`FindEmbeddedValueType` 将 `FieldTypeName="System.Int32"` 的 `Int32` 字段判为可嵌入值类型，若 `allTypes` 含 `System.Int32` 的 `TypeDef`（`IsValueType=true`，`BaseTypeName != System.Enum`），则 `EmitClassStruct` 中 `cppType==int32_t && TypeKind==Int32` 分支误取 `TypeCppName(System_Int32)` 作为成员类型，导致 `System_Int32` 嵌入声明（`System_Int32 field;`）——该类型无 C++ 定义或为不完整类型，引发 `unknown type / incomplete type / no viable overloaded '='` 等 208 行族错误。
- **F 报告**：208 行族即 `System_Int*`/`System_Boolean` 等原语嵌入错误的全量；本次排除 14 个原语名，直接消除该族的 `FirstOrDefault` 命中路径。
- **预计消除**：208 行（F 报告全族）。验证方法：`gen_r17` 全量 `g++ -fsyntax-only` 三类错误 `no member / cannot cast / incomplete type` 基线对比，`System_Int32` 声明应归零；当前未跑全量，标注"未验证"，验证方法已知。
- **与 EmbeddedValueShape 解耦**：`EmbeddedValueShape` 对 `DateTime/Decimal/Guid` 等已做 `slot` 特判并直接返回 `(1/2,0,0)`，不经 `FindEmbeddedValueType`；本次排除不影响该路径（保留 `System.Decimal` 等的 `il2cpp::runtime::Il2CppDecimal` 映射）[读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:696]

## 5 回归风险 — ConcurrentQueue Segmentation 仍折叠

- **风险描述**：`System.Collections.Concurrent.ConcurrentQueue`1+Segment` 为用户嵌套值类型（`IsValueType=true`，`FullName≈System.Collections.Concurrent.ConcurrentQueue`1+Segment`），其字段含 GC 引用，需经 `EmbeddedValueShape -> FindEmbeddedValueType -> LayoutInstanceFieldsCore` 折叠为 `slot` 并 OR `RefFieldBitmap`，否则 GC 漏扫（`TransitionTime` 类 48 字节 4 指针先例）[读:E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:692]
- **本次影响**：排除集仅含 14 原语，不含 `System.Collections.Concurrent.ConcurrentQueue`1+Segment`，故该类型仍可被 `FindEmbeddedValueType` 命中并正确折叠，布局与 `RefFieldBitmapHigh` 不变。
- **仍折叠的保障**：`LayoutInstanceFieldsCore` 对 `Segment` 的递归 `LayoutInstanceFieldsCore(vt)` 与 `visiting` 循环守卫保持不变；`EmitClassStruct` 的 `TypeCppName(vt)` 嵌入亦保持。
- **残留风险**：若后续将 `System.Guid`/`System.Decimal` 等 BCL 结构体误加入排除集，则 `Segment` 内若嵌 `Guid` 将退化为 `int32_t` 槽位，触发宽度错误；当前已避免。
- **验证建议**：`CultureData`/`Segment` 所在 cpp `g++ -fsyntax-only` 零新增 `incomplete type`；`dotnet test` 中 `ConcurrentQueue` 用例（如有）回归。

## 6 产物与交接

- 本文件：`r17shard\f1_primitive_exclude_report_20260902.md`（前缀 `f1_*`）
- 备份：`.opencode/backups\TypeEmitter.20260902_113120`
- 编译物：`E:\il2cpp\temp\il2cppc.dll` (C0 0错)
- 后续：父会话可据此重建 `b0-b3` 错误分类基线；若需全量验证，执行 `gen_r17` 全量并统计 `System_Int` 声明数。

---
*生成于 2026-09-02，F1 分片单点修复，满足 W3/C0 约束。*
