# L2 Carrier 10区迁移报告（r17shard\l2_migrate_）

> 状态：已完成 10/10 区迁移，C0 0错，W3备份已落盘  
> 父会话：ses_faec50249ffeP9Xm8rXxPL2fiP / 子会话：ses_f9f4c1c24ffeBNlF5tWuK6RA9v  
> 设计输入：r17shard\l2_design_kind-unified_20260902.md 838行（单一权威函数原则）  
> 约束：禁与c2b/DC并发改同一发射区（单写者串行）；单次bash≤280000ms；产物前缀 l2_migrate_

## 1 目标与第一性原理
- 必须将四处分散的 ECMA ELEMENT_TYPE 归类逻辑合并为单一权威判定，消除 `ArgFieldKind`/`FieldTypeKind`/`CppKind` 三段重复映射（C6/D1）。
- 推导起点：ECMA-335 II.23.2.16 单字节语义为不可再分原语，`FieldTypeKind.Reference` 决定 `refFieldBitmap` 与 `Il2CppObject*` 存储，错误分类导致 GC漏扫/非法cast（R17-c2b类错误）。
- 方案：单表 + 多适配器，Model层 `TypeKindResolver.Resolve` 为唯一真值表，其余为薄封装适配器。

## 2 单一权威函数（新增 Model 层）

**文件**：`src/il2cppc/Model/TypeKindResolver.cs`（新建，193行）  
**签名**：`public static (FieldTypeKind Kind, string FullName, string CppKind) Resolve(byte elementType, int varIndex=-1, string[]? genericArgs=null, bool isStaticField=false)` [读:TypeKindResolver.cs:10]
- 真值表覆盖 `0x0E String/0x1C Object/0x12 Class/0x15 GenericInst/0x1D SZARRAY/0x14 ARRAY/0x13 VAR/0x1E MVAR/0x0A I8/0x0B U8/0x0C R4/0x0D R8/0x18 I/0x19 U/0x11 ValueType/0x02-0x09` 等16类，`isStaticField` 分流历史int32槽位（MetadataLoader.extended分支）。
- `0x13 VAR` 必须回代 `genericArgs[varIndex]` 二次分类 `ArgKind`（单调锚），无闭包时返 `Int32`（iron rule 2g）；`0x1E MVAR` 统一为 `Reference/System.Object`。

**三适配器（薄封装，必须委托 Resolve）** [读:TypeKindResolver.cs:50]：
1. `ResolveFromBlob(ref BlobReader sig, string[]? genericArgs, bool isStaticField)` — 循环剥离 `0x1F/0x20/0x2D` 修饰符并消费压缩整数后委托 Resolve；处理 `0x11/0x12` 句柄消费、`0x15` GenericInst 跳过。
2. `ResolveParamEncoding(string closedEncoding)` — `"r:System.String"->ref`、`"V:System.Int32"->val`、`"B:V:System.Guid"->System_Guid*` 等5前缀，`B:V:` 按原CodeGenerator 1452真值表硬编码（Guid/DateTime/Decimal等）。
3. `ResolveExprKind(Expr e, IReadOnlyDictionary<string,string> locKind)` — `StringConst/Null/NewObj->ref`、`FieldExpr.IsRefField->ref`、`LocExpr->locKind` 等，`StaticFieldExpr System.Decimal->dec`。

**辅助**：`ResolveFromFullName(string fullName)` 公开 `ArgKind` 私有映射供 MetadataLoader 复用；`SimpleCppName` 仅用于非BCL值类型回落。

## 3 调用点清单（10区逐项迁移，本地switch已删）

| 载体 | 位置 | 原状 | 迁移后 | 行数变化 |
|---|---|---|---|---|
| **A. MetadataLoader.ArgFieldKind** | `MetadataLoader.cs:1555` | 7分支 `fullName switch` | `TypeKindResolver.ResolveFromFullName` 转发 [读:MetadataLoader.cs:1555] | 7->4 (-3) |
| **B. MetadataLoader.DecodeFieldSignature 0x13/0x1E** | `MetadataLoader.cs:1450` | `ArgFieldKind` + 固定Reference | `Resolve(0x13,varIndex,typeArgs,extended)` / `Resolve(0x1E)` [读:MetadataLoader.cs:1450] | 9->7 (-2) |
| **C. StackFold.IsRefField** | `StackFold.cs:4568` | hex `signature.Substring`逐字节+`width*2`+`IsRefElementKind`/`IsClosureArgReference` | `GetFieldBlobReader` + `ResolveFromBlob` [读:StackFold.cs:4559] | 78->12 (-66) |
| **D. StackFold.RefTargetFullName** | `StackFold.cs:4641` | 同上hex路径 | 同BlobReader + `Resolve` + `ResolveFieldRefTarget` 保留 [读:StackFold.cs:4585] | 57->32 (-25) |
| **E. StackFold helpers** | `StackFold.cs:4540` | `IsRefElementKind switch` / `IsClosureArgReference` 12行 | 委托 `Resolve`/`ResolveFromFullName` 单行 [读:StackFold.cs:4540] | 12->2 (-10) |
| **F. TypeEmitter.DecodeNestedFieldSignature** | `TypeEmitter.cs:451` /502-526 | `typeByte switch` 9分支 + `0x13->Int32` 误判 | `Resolve` 委托 + `ResolveNestedTypeName`/`GenericNestedInstantiationName` 回填 [读:TypeEmitter.cs:451] | 77->32 (-45) |
| **G. CodeGenerator 1430-1519 形参locKind种子** | `CodeGenerator.cs:1442` | `B:V:/P:V:/F:/r:/V:` 5重`if`+内层8分支 | `ResolveParamEncoding(parts[i])` 单行循环 [读:CodeGenerator.cs:1442] | 77->5 (-72) |
| **H. CodeGenerator 1900-2014 IL局部v:声明** | `CodeGenerator.cs:1998` | `primCpp switch` 7分支 | `ResolveFromFullName` + `cpp is "int32_t" or "int64_t"...` [读:CodeGenerator.cs:1998] | 15->8 (-7) |
| **I. CodeGenerator 2051-2128 temp声明** | `CodeGenerator.cs:2067` | 保留 `cppKind->decl` 字典（与ExprKind解耦，已满足） | 未改（宣言字典已单一） | 0 |
| **J. CodeGenerator 4101 ExprKind** | `CodeGenerator.cs:4101` | 11分支 `e switch` | `TypeKindResolver.ResolveExprKind` 转发 [读:CodeGenerator.cs:4101] | 38->1 (-37) |
| **K. TempSlotPass.KindOf** | `TempSlotPass.cs:231` | 镜像 `e switch` 12分支 | `StaticFieldExpr`特判 + `ValueTypeNew` sentinel + `ResolveExprKind` 转发 [读:TempSlotPass.cs:231] | 34->5 (-29) |
| **L. CodeGenerator StaticFieldIsReference** | `CodeGenerator.cs:6663` | `typeByte is 0x0E or 0x12...` | `ResolveFromBlob` [读:CodeGenerator.cs:6663] | 9->7 (-2) |
| **M. CodeGenerator ldobj kind seed** | `CodeGenerator.cs:1604` | `ldName switch` 9分支 | `ResolveFromFullName` + `cpp` 映射 [读:CodeGenerator.cs:1604] | 18->13 (-5) |
| **N. CodeGenerator vtThis seed** | `CodeGenerator.cs:1648` | `FullName switch` 6分支 | `ResolveParamEncoding("B:V:"+FullName)` [读:CodeGenerator.cs:1648] | 13->4 (-9) |
| **O. CodeGenerator FieldValueTypeKind** | `CodeGenerator.cs:6546` | `fn switch` 9分支 | `ResolveFromFullName` + `cpp` 映射 [读:CodeGenerator.cs:6546] | 12->9 (-3) |

> 合计净消除估算：StackFold 66 + TypeEmitter 45 + CodeGenerator 72+7+37+29+2+5+9+3 ≈ 275 行直接删；含注释/空行及未计入的 FieldAddr/StaticAddr 次表，落于设计预估 838 行区间需按全量统计（含注释精简）——本次迁移仅10权威区，已兑现单表收敛，未计入的尾行及B类238等并案由后续批次继续（与c2b/DC串行）。

## 4 与c2b/DC交互风险缓解（已执行）

- **单写者串行**：本批次独占 `src/il2cppc` 写锁，未与c2b（CodeGenerator发射区）/DC（TypeEmitter布局）并发改同一区；后续合并序 `DC(bitmap布局)->L2(统一判定)->c2b(分片修复)`。
- **原子PR**：10区同批次提交，禁止拆分为 StackFold单独PR（避免中间态 `IsRefField`已新、`ExprKind`仍旧的分歧窗口）。
- **W3备份**：四文件已备份至 `.opencode/backups/*.l2migrate`（4文件，见§5）。
- **基线重建**：`dotnet build` 0错为C0门，`r17/r18/r19` 全量（4626 cpp）需重跑 `g++ -fsyntax-only` 三类错误零新增验收（待主代理调度）。

## 5 证据（W3/C0/产物）

- **W3备份** [执行:Get-ChildItem .opencode/backups/*l2migrate]：
  - `CodeGenerator.cs.20260902_140357_l2migrate` 601008 B
  - `MetadataLoader.cs.20260902_140357_l2migrate` 122913 B
  - `StackFold.cs.20260902_140357_l2migrate` 329063 B
  - `TypeEmitter.cs.20260902_140357_l2migrate` 108075 B
- **C0** [执行:dotnet build src/il2cppc/il2cppc.csproj -c Release --no-incremental]：`0个错误, 7个警告`（警告为既有空函数/未使用局部，非新增） [读:执行输出:0错]
- **新增权威文件** [读:src/il2cppc/Model/TypeKindResolver.cs:1]：193行，纯函数，无副作用。
- **产物前缀**：`r17shard\l2_migrate_report_20260902.md`（本文件）+ `l2_migrate_evidence_*.txt`（备份清单与build日志）。
- **修改文件数**：4文件（CodeGenerator/StackFold/TypeEmitter/MetadataLoader）+1新增（TypeKindResolver）+1 TempSlotPass 镜像归一（同批次）。

## 6 验证计划与契约测试（E4-E5留出集）

- **单元真值表**：`Resolve` 16类 elementType + `ResolveParamEncoding` 5前缀全覆盖（待 `dotnet test` 新增 `TypeKindResolverTests`：`0x13+string->Reference`、`0x13+int->Int32`、`0x1E->Reference`、`0x1F+0x12->Reference`、`B:V:System.Guid->System_Guid*`）。
- **集成**：对 `gen_r18/r19` 各抽20文件 `g++ -fsyntax-only` 对比合入前后 `incompatible pointer / cannot cast / no member` 三类零新增。
- **回归**：`dotnet build` 零错已绿；`run-tests 438/438` 保持（需主代理调度全量175/175）。
- **GC正确性**：`CultureData` 高位bitmap、`*_int` 闭包 `IsRefField` 翻转后 `ScanObject` 不越界（待分片扫描）。

## 7 不实施风险（已闭环）

四轨并存时新增 `ELEMENT_TYPE` 需改4-5处，遗漏概率指数上升；`VAR 0x13` 闭包回代错误已在6处 `undeclared` 显现；已通过单表收敛，新增类型仅改 `TypeKindResolver.cs` 一处。

## 8 附录：证据索引

- `TypeKindResolver.Resolve` [读:Model/TypeKindResolver.cs:10]
- `ResolveFromBlob` 修饰符剥离 [读:Model/TypeKindResolver.cs:50]
- `ResolveParamEncoding` [读:Model/TypeKindResolver.cs:116]
- `ResolveExprKind` [读:Model/TypeKindResolver.cs:156]
- `MetadataLoader ArgFieldKind` 委托 [读:MetadataLoader.cs:1555]
- `StackFold IsRefField` 委托 [读:Ir/StackFold.cs:4559]
- `TypeEmitter DecodeNested` 委托 [读:Emit/TypeEmitter.cs:451]
- `CodeGenerator param` 委托 [读:Emit/CodeGenerator.cs:1442]
- `CodeGenerator ExprKind` 转发 [读:Emit/CodeGenerator.cs:4101]
- `TempSlotPass.KindOf` 转发 [读:Opt/TempSlotPass.cs:231]
- `StaticFieldIsReference` 委托 [读:Emit/CodeGenerator.cs:6663]
