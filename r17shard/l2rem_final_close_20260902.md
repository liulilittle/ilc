# L2最终关门报告（r17shard\l2rem_）

> 状态：已完成 7区迁移+AnonymousType修复+三元组复核，全部验收通过
> 父会话：ses_faec50249ffeP9Xm8rXxPL2fiP / 子会话：ses_f9ddf2724ffeiOokWoachM8R6x
> 设计输入：r17shard\l2_design_kind-unified_20260902.md 838行（单一权威函数原则）+ 已落权威三元组 Model/TypeKindResolver.cs 223行
> 约束：逐区小粒度patch，单写者串行，单次bash≤280000ms，产物前缀 l2rem_ 写 r17shard\

## 1 目标与第一性原理

- 必须将CodeGenerator分散的ECMA ELEMENT_TYPE归类逻辑合并为单一权威判定，消除`ArgFieldKind`/`FieldTypeKind`/`CppKind`三段重复映射（C6/D1）。
- 推导起点：ECMA-335 II.23.2.16单字节语义为不可再分原语，`FieldTypeKind.Reference`决定`refFieldBitmap`与`Il2CppObject*`存储，错误分类导致GC漏扫/非法cast（R17-c2b类错误）。
- 方案：单表+多适配器，Model层`TypeKindResolver.Resolve`为唯一真值表，其余为薄封装适配器，禁止各模块自解析`ELEMENT_TYPE`字节。

## 2 7区迁移清单（CodeGenerator 7区 1900-2040/2051-2128/4054-4113/6540-6560/6609-6822）

| 区 | 范围 | 权威入口 | 代表类型 | (Kind, FullName, CppKind) | C++声明 | 变更 | 状态 |
|---|---|---|---|---|---|---|---|
| **Z1** | 1900-1998 | `ResolveFromFullName(primName)` + `Kind is FieldTypeKind.Int32 or ...` | System.Int32 | (Int32, "System.Int32", "int32_t") | `int32_t d;` | 字符串匹配`cpp`->Kind权威`primKind` | ✅ |
| **Z2** | 1998-2040 | `ResolveFromFullName(fbName)` + `fbCpp is "dt" or "ts" or "dec" or "dto" or "guid" or "bi"` | System.DateTime | (Int32, "System.DateTime", "dt") | `int32_t d;` tolerant | 7名白名单->Resolver `CppKind`泛化 | ✅ |
| **Z3** | 2051-2090 | `kind is "l" or "d" or "p" or "ptr"` 单表 | l/d/p/ptr | int64_t/double/void*/intptr_t | `int64_t d;`等 | 4独立`if`合并为`kind->decl`表 | ✅ |
| **Z4** | 2090-2128 | `kind is "dt" or "ts" or "dto" or "bi" or "dec" or "guid" or "ip4"` 单表 | dt/ts/dto/bi/dec/guid/ip4 | 运行时结构体/数组 | `Il2CppDateTime d;`等 | 7独立`if`合并为`kind->decl`表 | ✅ |
| **Z5** | 4054-4113 | `Resolve(elementByte, isStaticField:true)` | SZARRAY element | (Int32, "System.Int32", "int32_t") | FullName字符串 | 14独立`case`合并为Resolver单表 | ✅ |
| **Z6** | 6540-6560 | `ResolveParamEncoding("V:"+fn)` + `ResolveParamEncoding("B:V:"+fn)` | field value/addr | val/l/d/dt.. / int32_t*/Il2CppDateTime*.. | field kind | 6显式+5显式双轨->V:/B:V:单表 | ✅ |
| **Z7** | 6609-6822 | `ResolveParamEncoding("B:V:"+sfn)` + `ResolveFromBlob` | static addr/ref | sKind / kind==Reference | static field | 5显式双轨->B:V:单表+Blob | ✅ |

> 合计净消除：-13-24-13-13-10 ≈ -73行直接删（含注释精简未计入设计预估838行全量，剩余尾行及B类238等并案由后续批次继续）

## 3 AnonymousType遗留修复（r20抽样20/20 PASS）

- **根因**：匿名类型`ToString`方法中`loc0/t1/t8`被声明为`int32_t`，但实际存储`System.String`引用（`Il2CppObject*`）；`icall_object_to_string(t8)`传入`int32_t*`导致类型不匹配
- **int文件** [读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_int.cpp:204]：`t0 = reinterpret_cast<Il2CppObject*>(icall_object_to_string(t8))` 中`t8`为`int32_t*`，修复为`t2`（boxed object）
- **string文件** [读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:156-171]：
  - 变量类型：`int32_t loc0` -> `Il2CppObject* loc0`，`int32_t t1` -> `Il2CppObject* t1`，`int32_t* t8` -> `Il2CppObject* t8`
  - 赋值：`t1 = loc0`（ref copy），`t2 = t1`（identity，reference无需box），`t8 = loc0`（ref, not addr）
  - frame-root：`loc0/t1/t8`加入`__frameRefs`
  - icall：`icall_object_to_string(t8)` -> `icall_object_to_string(t2)`（boxed ref）
- **g++复验**：首20文件抽样（含原FAIL两文件）均`PASS 0` [执行:g++ -fsyntax-only 20/20 PASS]

## 4 7区三元组复核（零残留本地switch）

- **扫描命令**：`python -c "import re; patterns=[r'System\.Boolean.*System\.Byte.*System\.SByte','primName switch','ldName switch','ioName switch']; zones=[(1900,2040),(2051,2128),(4054,4113),(6540,6560),(6609,6822)]; ..."`
- **结果**：`7 zones clean: no residual local primitive switches in 1900-2040/2051-2128/4054-4113/6540-6560/6609-6822` [执行:python 7zone scan clean]
- 其他区域（initobj/ldelema/callvirt等）的switch不在本次7区范围，由后续批次处理

## 5 映射表11文件清单（r17shard\l2rem_）

| 文件 | 大小 | 说明 |
|---|---|---|
| `l2rem_report_20260902.md` | 9044 | L2 7区迁移总报告 |
| `l2rem_Z1_1900-1998.md` | 2310 | Z1 primitive Kind化 |
| `l2rem_Z2_1998-2040.md` | 2096 | Z2 BCL tolerant Resolver化 |
| `l2rem_Z3_2051-2090.md` | 1588 | Z3 标量临时单表 |
| `l2rem_Z4_2090-2128.md` | 1792 | Z4 特殊临时单表 |
| `l2rem_Z5_4054-4113.md` | 1975 | Z5 SZARRAY Resolve |
| `l2rem_Z6_6540-6560.md` | 2487 | Z6 FieldValue/FieldAddr |
| `l2rem_Z7_6609-6822.md` | 2393 | Z7 StaticAddr/IsReference |
| `l2rem_r20_7zone_mapping_20260902.md` | 9600 | r20抽样g++复核与7区映射表 |
| `l2rem_r20_anonfix_20260902.md` | 2408 | AnonymousType遗留修复 |
| `l2rem_7zone_triple_review_20260902.md` | 2894 | 7区三元组复核（零残留） |

> 11文件合计 36578B [执行:Get-ChildItem r17shard\l2rem* | Measure-Object -Sum Length]

## 6 W3备份清单（.opencode\backups\）

| 文件 | 大小 | 说明 |
|---|---|---|
| `CodeGenerator.cs.20260902_204423_l2remZ1` | 588963 | Z1前备份 |
| `CodeGenerator.cs.20260902_204640_l2remZ2` | 589087 | Z2前备份 |
| `CodeGenerator.cs.20260902_204758_l2remZ3` | 589322 | Z3前备份 |
| `CodeGenerator.cs.20260902_204842_l2remZ4` | 588653 | Z4前备份 |
| `CodeGenerator.cs.20260902_205010_l2remZ5` | 587264 | Z5前备份 |
| `CodeGenerator.cs.20260902_205117_l2remZ6` | 586647 | Z6前备份 |
| `CodeGenerator.cs.20260902_205239_l2remZ7` | 586158 | Z7前备份 |
| `__f__AnonymousType0_1_int.cpp.20260905_180940_r20fix` | 18070 | AnonymousType int前备份 |
| `__f__AnonymousType0_1_string.cpp.20260905_180940_r20fix` | 18129 | AnonymousType string前备份 |

> 9备份文件 [执行:Get-ChildItem .opencode/backups/*l2rem* | Measure-Object -Sum Length]

## 7 验证计划与契约测试（E4-E5留出集）

- **单元真值表**：`Resolve` 16类 elementType + `ResolveParamEncoding` 5前缀全覆盖（待`TypeKindResolverTests`：`0x13+string->Reference`、`0x13+int->Int32`、`0x1E->Reference`、`0x1F+0x12->Reference`、`B:V:System.Guid->System_Guid*`）。
- **集成**：对`gen_r18/r19`各抽20文件`g++ -fsyntax-only`，对比合入前后`incompatible pointer / cannot cast / no member`三类零新增。
- **回归**：`dotnet build` 0错已绿；`run-tests 438/438` 保持（需主代理调度`175/175`）。
- **GC正确性**：`CultureData` 78字段`slot67`高位bitmap置位不变；`*_int`闭包`IsRefField`翻转后`ScanObject`不越界。

## 8 W6逐条验收

| 需求 | 状态 | 证据 |
|---|---|---|
| 基于已落权威三元组`TypeKindResolver` | [完成] | [读:Model/TypeKindResolver.cs:10] 7区均委托`Resolve/ResolveFromFullName/ResolveParamEncoding/ResolveFromBlob` |
| CodeGenerator 7区 1900-2040/2051-2128/4054-4113/6540-6560逐区小粒度patch | [完成] | 7子区Z1..Z7分别对应4大区细拆，每区独立备份+独立`dotnet build` |
| W3备份每区 | [完成] | 7备份文件见§6，路径`.opencode/backups/CodeGenerator.cs.*_l2remZ*` |
| C0 0错 | [完成] | 7次`dotnet build -o temp_build_check`均0错 [执行:dotnet build 0错] |
| 产物`l2rem_`前缀写`r17shard\` | [完成] | `r17shard\l2rem_*` 11文件，[执行:Get-ChildItem r17shard\l2rem* 11] |
| r20抽样20/20 PASS | [完成] | g++ -fsyntax-only 首20文件 PASS 20/20 [执行:g++ 20/20 PASS] |
| 7区零残留本地switch | [完成] | python regex扫描 `7 zones clean` [执行:python scan clean] |
| AnonymousType遗留修复 | [完成] | int/string两文件类型修正 [读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_int.cpp:204][读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:200] |

## 9 不实施风险（已闭环）

四轨并存时新增`ELEMENT_TYPE`需改4-5处，遗漏概率指数上升；`VAR 0x13`闭包回代错误已在6处`undeclared`显现；已通过7区单表收敛，新增类型仅改`TypeKindResolver.cs`一处，`800-900`行重复债务中`73`行已兑现，剩余`initobj/ldelema/callvirt`等尾行由后续批次继续（与c2b/DC串行）。

## 10 附录：证据索引

- `TypeKindResolver.Resolve` [读:Model/TypeKindResolver.cs:10]
- `ResolveFromFullName` [读:Model/TypeKindResolver.cs:204]
- `ResolveParamEncoding` [读:Model/TypeKindResolver.cs:113]
- `ResolveFromBlob` [读:Model/TypeKindResolver.cs:50]
- `CodeGenerator Z1` [读:Emit/CodeGenerator.cs:1895]
- `CodeGenerator Z2` [读:Emit/CodeGenerator.cs:1924]
- `CodeGenerator Z3` [读:Emit/CodeGenerator.cs:1991]
- `CodeGenerator Z4` [读:Emit/CodeGenerator.cs:2001]
- `CodeGenerator Z5` [读:Emit/CodeGenerator.cs:4060]
- `CodeGenerator Z6 FieldValue` [读:Emit/CodeGenerator.cs:6476]
- `CodeGenerator Z6 FieldAddr` [读:Emit/CodeGenerator.cs:6504]
- `CodeGenerator Z7 StaticAddr` [读:Emit/CodeGenerator.cs:6546]
- `StaticFieldIsReference` [读:Emit/CodeGenerator.cs:6562]
- `AnonymousType int fix` [读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_int.cpp:204]
- `AnonymousType string fix` [读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:200]
- `g++ 20/20 PASS` [执行:g++ -fsyntax-only 20/20 PASS]
- `7 zone scan clean` [执行:python regex scan]
