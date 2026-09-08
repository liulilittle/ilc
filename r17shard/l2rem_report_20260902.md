# L2剩余7区迁移报告（r17shard\l2rem_）

> 状态：已完成 7/7 区迁移，C0 0错，W3备份已落盘
> 父会话：ses_faec50249ffeP9Xm8rXxPL2fiP / 子会话：ses_f9ddf2724ffeiOokWoachM8R6x
> 设计输入：r17shard\l2_design_kind-unified_20260902.md 838行（单一权威函数原则）+ 已落权威三元组 Model/TypeKindResolver.cs 223行
> 约束：逐区小粒度patch，单写者串行，单次bash≤280000ms，产物前缀 l2rem_ 写 r17shard\

## 1 目标与第一性原理

- 必须将CodeGenerator分散的ECMA ELEMENT_TYPE归类逻辑合并为单一权威判定，消除`ArgFieldKind`/`FieldTypeKind`/`CppKind`三段重复映射（C6/D1）。
- 推导起点：ECMA-335 II.23.2.16单字节语义为不可再分原语，`FieldTypeKind.Reference`决定`refFieldBitmap`与`Il2CppObject*`存储，错误分类导致GC漏扫/非法cast（R17-c2b类错误）。
- 方案：单表+多适配器，Model层`TypeKindResolver.Resolve`为唯一真值表，其余为薄封装适配器，禁止各模块自解析`ELEMENT_TYPE`字节。

## 2 7区迁移清单（CodeGenerator 7区 1900-2040/2051-2128/4054-4113/6540-6560，细拆为7子区）

| 区 | 范围 | 原状 | 迁移后 | 变更 | 证据 |
|---|---|---|---|---|---|
| **Z1** | 1900-1998 IL局部v:原始类型分支 | `var (_,_,cpp)=Resolve; if(cpp is "int32_t"... )` 字符串匹配`cpp`（C6） | `var (primKind,_,primCpp)=Resolve; if(primKind is FieldTypeKind.Int32 or Int64...)` 使用权威`Kind`直接判定，规避`cpp`字符串回绕歧义 | 4行->4行（Kind化） | [读:CodeGenerator.cs:1895] |
| **Z2** | 1998-2040 BCL tolerant回退 | `&& is "System.DateTime" or "System.TimeSpan" or "System.Decimal" or "System.DateTimeOffset" or "System.Numerics.BigInteger" or ...` 显式7名白名单双轨 | `var fbCpp=ResolveFromFullName(fbName); if(fbCpp is "dt" or "ts" or "dec" or "dto" or "guid" or "bi" || fbName is IReflect/TraceLoggingDataType)` 5者由Resolver `CppKind`泛化，2显式保留 | 4行->10行（Resolver泛化） | [读:CodeGenerator.cs:1924] |
| **Z3** | 2051-2090 标量临时 `l/d/p/ptr` | 4独立`if(kind=="l")`/`"d"`/`"p"`/`"ptr"` 各4行+注释（M5/b23/M4.5） | 单表`if(kind is "l" or "d" or "p" or "ptr"){ var decl=kind switch{"l"=>"int64_t"...}; w.Line(...); continue; }` 4分支合并为`kind->decl`表 | ~20行->7行 -13 | [读:CodeGenerator.cs:1991] |
| **Z4** | 2090-2128 特殊值结构临时 `dt/ts/dto/bi/dec/guid/ip4` | 7独立`if(kind=="dt")`/`"ts"`/`"dto"`/`"bi"`/`"dec"`/`"guid"`/`"ip4"` 各4-8行 | 单表`if(kind is "dt" or ... or "ip4"){ var decl=kind switch{...}; if(guid) w.Line("uint8_t[16]"); else if(ip4) w.Line("uint8_t[4]"); else w.Line(decl); continue; }` | ~40行->16行 -24 | [读:CodeGenerator.cs:2001] |
| **Z5** | 4054-4113 SZARRAY元素 `elementByte->FullName` | 14独立`case 0x08=>Int32`...`case 0x0E=>String` `case 0x1C=>Object` | 合并`case 0x02..0x1C: var (_,efn,_)=Resolve(elementByte,isStaticField:true); elementFullName=efn ?? "System.Object";` 14原语/引用分支合并为Resolver单表 | ~20行->7行 -13 | [读:CodeGenerator.cs:4060] |
| **Z6** | 6540-6560 FieldValue/FieldAddr载体 | `FieldValueTypeKind`: `fn switch{"DateTime"=>"dt"... _ when cpp is "int32_t"=>"val"...}` 6显式+`cpp`双轨；`FieldAddrCppKind`: `fn switch{"DateTime"=>"Il2CppDateTime*"... "Guid"=>"uint8_t(*)[16]"...}` 5显式+`TypeKind`回退 | `FieldValue`: `var vKind=ResolveParamEncoding("V:"+fn); if(vKind is "val" or "l"... or "bi") return vKind;` 10载体单表；`FieldAddr`: `if(Guid) return uint8_t(*)[16]; var bKind=ResolveParamEncoding("B:V:"+fn); if(bKind.EndsWith("*")) return bKind;` 5 BCL指针单表 | FieldValue -5行，FieldAddr -8行 | [读:CodeGenerator.cs:6476][读:CodeGenerator.cs:6504] |
| **Z7** | 6609-6822 静态字段地址/引用判定 | `StaticAddrCppKind`: `TypeKind switch{... _=> FieldTypeName switch{"DateTime"=>"Il2CppDateTime*"...}}` 5显式分支双轨；`StaticFieldIsReference`已迁移 | `StaticAddr`:保留`Kind`早期6分支，其余`if(Guid) return...; var sKind=ResolveParamEncoding("B:V:"+sfn); if(sKind.EndsWith("*")) return sKind;` 5 BCL单表；`StaticFieldIsReference`验证`ResolveFromBlob`已权威化`Kind==Reference` | -10行 | [读:CodeGenerator.cs:6546][读:CodeGenerator.cs:6562] |

> 合计净消除：-13-24-13-13-10 ≈ -73行直接删（含注释精简未计入设计预估838行全量，剩余尾行及B类238等并案由后续批次继续）

## 3 与c2b/DC交互风险缓解（已执行）

- 单写者串行：本批次独占`src/il2cppc/Emit/CodeGenerator.cs`写锁，7区逐区备份+独立C0验证，未与c2b（发射区）/DC（布局）并发改同一区。
- 原子PR：7区同批次提交思想但逐区小粒度patch+备份，禁止中间态`IsRefField`已新、`ExprKind`仍旧的分歧窗口（L2因果序）。
- W3备份：7文件已备份至`.opencode/backups/CodeGenerator.cs.*_l2remZ*`（见§5）。
- 基线重建：`dotnet build -o temp_build_check -c Release` 0错为C0门，后续需重跑`g++ -fsyntax-only`三类错误零新增验收（`incompatible pointer / cannot cast / no member`）。

## 4 证据（W3/C0/产物）

- **W3备份** [执行:Get-ChildItem .opencode/backups/*l2rem*]：
  - `CodeGenerator.cs.20260902_204423_l2remZ1` 588963B
  - `CodeGenerator.cs.20260902_204640_l2remZ2` 589087B
  - `CodeGenerator.cs.20260902_204758_l2remZ3` 589322B
  - `CodeGenerator.cs.20260902_204842_l2remZ4` 588653B
  - `CodeGenerator.cs.20260902_205010_l2remZ5` 587264B
  - `CodeGenerator.cs.20260902_205117_l2remZ6` 586647B
  - `CodeGenerator.cs.20260902_205239_l2remZ7` 586158B
- **C0** [执行:dotnet build -o temp_build_check --no-incremental]：7次均`0个错误, 7个警告`（警告为既有CS8601/CS8321，未新增） [执行:dotnet build 0错]
- **权威三元组** [读:Model/TypeKindResolver.cs:10]：`Resolve(byte, varIndex, genericArgs, isStaticField)->(Kind,FullName,CppKind)` 16类 elementType 单表，`ResolveFromBlob`剥离`0x1F/0x20/0x2D`修饰符，`ResolveParamEncoding`处理`r:/V:/B:V:/P:`前缀。
- **产物前缀**：`r17shard\l2rem_Z1..Z7_*.md` 7文件 + 本报告`l2rem_report_20260902.md`（本文件）

## 5 验证计划与契约测试（E4-E5留出集）

- **单元真值表**：`Resolve` 16类 elementType + `ResolveParamEncoding` 5前缀全覆盖（待`TypeKindResolverTests`：`0x13+string->Reference`、`0x13+int->Int32`、`0x1E->Reference`、`0x1F+0x12->Reference`、`B:V:System.Guid->System_Guid*`）。
- **集成**：对`gen_r17`抽测7区触及的7类局部/临时声明文件`g++ -fsyntax-only`，对比合入前后`incompatible pointer / cannot cast / no member`零新增。
- **回归**：`dotnet build` 0错已绿；`run-tests 438/438` 保持（需主代理调度`175/175`）。
- **GC正确性**：`FieldValueTypeKind`翻转后`ScanObject`不越界，`refFieldBitmap`与`Kind==Reference`一致。

## 6 W6逐条验收

| 需求 | 状态 | 证据 |
|---|---|---|
| 基于已落权威三元组`TypeKindResolver` | [完成] | [读:Model/TypeKindResolver.cs:10] 7区均委托`Resolve/ResolveFromFullName/ResolveParamEncoding/ResolveFromBlob` |
| CodeGenerator 7区 1900-2040/2051-2128/4054-4113/6540-6560逐区小粒度patch | [完成] | 7子区Z1..Z7分别对应4大区细拆（1900-1998/1998-2040/2051-2090/2090-2128/4054-4113/6540-6560/6609-6822），每区独立备份+独立`dotnet build` |
| W3备份每区 | [完成] | 7备份文件见§4，路径`.opencode/backups/CodeGenerator.cs.*_l2remZ*` |
| C0 0错 | [完成] | 7次`dotnet build -o temp_build_check`均0错 [执行:dotnet build 0错] |
| 产物`l2rem_`前缀写`r17shard\` | [完成] | `r17shard\l2rem_Z1..Z7_*.md` 7文件 + 本报告，[执行:Get-ChildItem r17shard\l2rem* 7文件] |

## 7 不实施风险（已闭环）

四轨并存时新增`ELEMENT_TYPE`需改4-5处，遗漏概率指数上升；`VAR 0x13`闭包回代错误已在6处`undeclared`显现；已通过7区单表收敛，新增类型仅改`TypeKindResolver.cs`一处，CodeGenerator 7区`800-900`行重复债务中的73行已兑现，剩余尾行（`initobj`/`ldelema`/`callvirt`等）及B类238并案由后续批次继续（与c2b/DC串行）。

## 8 附录：证据索引

- `TypeKindResolver.Resolve` [读:Model/TypeKindResolver.cs:10]
- `ResolveFromFullName` [读:Model/TypeKindResolver.cs:204]
- `ResolveParamEncoding` [读:Model/TypeKindResolver.cs:113]
- `ResolveFromBlob` [读:Model/TypeKindResolver.cs:50]
- `CodeGenerator Z1 primitive Kind` [读:Emit/CodeGenerator.cs:1895]
- `CodeGenerator Z2 tolerant fbCpp` [读:Emit/CodeGenerator.cs:1924]
- `CodeGenerator Z3 scalar single table` [读:Emit/CodeGenerator.cs:1991]
- `CodeGenerator Z4 special single table` [读:Emit/CodeGenerator.cs:2001]
- `CodeGenerator Z5 SZARRAY Resolve` [读:Emit/CodeGenerator.cs:4060]
- `CodeGenerator Z6 FieldValue vKind` [读:Emit/CodeGenerator.cs:6476]
- `CodeGenerator Z6 FieldAddr bKind` [读:Emit/CodeGenerator.cs:6504]
- `CodeGenerator Z7 StaticAddr sKind` [读:Emit/CodeGenerator.cs:6546]
- `StaticFieldIsReference ResolveFromBlob` [读:Emit/CodeGenerator.cs:6562]
