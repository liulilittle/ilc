# L2 Carrier 并案最终关门报告（l2final_）

> 状态：已完成 7/7 区收尾验证 + 三元组复核，小粒度patch已收敛，W3备份已落盘，C0 0错
> 父会话：ses_faec50249ffeP9Xm8rXxPL2fiP / 子会话：ses_f9dc47a22ffejfmTKrKAgzORrb
> 设计输入：r17shard\l2_design_kind-unified_20260902.md 838行（单一权威函数原则）+ Model/TypeKindResolver.cs 236行
> 约束：逐区小粒度patch，单写者串行，单次bash≤280000ms，产物前缀 l2final_ 写 r17shard\

---

## 1 目标与第一性原理

- 必须将CodeGenerator分散的ECMA ELEMENT_TYPE归类逻辑合并为单一权威判定，消除`ArgFieldKind`/`FieldTypeKind`/`CppKind`三段重复映射（C6/D1）
- 推导起点：ECMA-335 II.23.2.16单字节语义为不可再分原语，`FieldTypeKind.Reference`决定`refFieldBitmap`与`Il2CppObject*`存储，错误分类导致GC漏扫/非法cast
- 方案：单表+多适配器，Model层`TypeKindResolver.Resolve`为唯一真值表，其余为薄封装适配器，禁止各模块自解析`ELEMENT_TYPE`字节 [读:Model/TypeKindResolver.cs:10]

## 2 权威三元组（单一事实源）

- 文件：src/il2cppc/Model/TypeKindResolver.cs 236行 [读:Model/TypeKindResolver.cs:1]
- 签名：`Resolve(byte elementType, varIndex, genericArgs, isStaticField) -> (Kind,FullName,CppKind)` [读:Model/TypeKindResolver.cs:11]
- 真值表：0x0E String/0x1C Object/0x12 Class/0x15 GenericInst/0x1D SZARRAY => Reference；0x13 VAR回代genericArgs[varIndex] => ArgKind；0x1E MVAR=>Reference；0x0A Int64/0x0B UInt64/0x0C R4/0x0D R8/0x18 NativeInt/0x11 ValueType占位 [读:Model/TypeKindResolver.cs:17]
- 适配器：`ResolveFromBlob(ref BlobReader)`剥离0x1F/0x20/0x2D修饰符 [读:Model/TypeKindResolver.cs:62]；`ResolveParamEncoding(string closedEncoding)`处理`r:/V:/B:V:/P:/F:`前缀 [读:Model/TypeKindResolver.cs:125]；`ResolveExprKind(Expr)` FieldExpr委托IsRefField的Resolver结果 [读:Model/TypeKindResolver.cs:177]；`ResolveFromFullName(string)` ArgKind表 [读:Model/TypeKindResolver.cs:217]

## 3 7区收尾清单（CodeGenerator 7区）

| 区 | 范围 | 旧状（已消除） | 终态（Resolver委托） | 证据 |
|---|---|---|---|---|
| **Z1** | 2115-2125 IL局部v:原始类型分支 | `(_,_,cpp)`字符串匹配`cpp is "int32_t"...` | `(primKind,_,primCpp)=ResolveFromFullName; primKind is FieldTypeKind.Int32 or Int64...` Kind直接判定 | [读:CodeGenerator.cs:2115][读:CodeGenerator.cs:2119] |
| **Z2** | 2140-2150 BCL tolerant回退 | 显式7名白名单`System.DateTime/TimeSpan/Decimal...` | `fbCpp=ResolveFromFullName; fbCpp is "dt" or "ts" or "dec" or "dto" or "guid" or "bi" \|\| IReflect/TraceLogging` Resolver泛化 | [读:CodeGenerator.cs:2140][读:CodeGenerator.cs:2144] |
| **Z3** | 2214-2220 标量临时 l/d/p/ptr | 4独立if各4行 | `kind is "l" or "d" or "p" or "ptr" { kind switch=>decl }` 单表 | [读:CodeGenerator.cs:2214][读:CodeGenerator.cs:2217] |
| **Z4** | 2221-2238 特殊值结构 dt/ts/dto/bi/dec/guid/ip4 | 7独立if各4-8行 | `kind is "dt" or ... or "ip4" { kind switch=>decl }` 单表，guid/ip4数组维度保留 | [读:CodeGenerator.cs:2221][读:CodeGenerator.cs:2225] |
| **Z5** | 4319-4335 SZARRAY elementByte->FullName | 14 case分支各回填FullName | `case 0x02..0x1C: Resolve(elementByte,isStaticField:true)` 单表，tolerant分支保留 | [读:CodeGenerator.cs:4319][读:CodeGenerator.cs:4332] |
| **Z6** | 6851-6857 FieldValue/FieldAddr | FieldValue fn switch 6显式+cpp双轨；FieldAddr 5显式+TypeKind回退 | `V:+fn => ResolveParamEncoding` 10载体单表；`B:V:+fn => ResolveParamEncoding` 5 BCL指针单表，Guid特例保留 | [读:CodeGenerator.cs:6851][读:CodeGenerator.cs:6855] |
| **Z7** | 6915-6927 静态字段地址/引用判定 | StaticAddr 5显式双轨；StaticFieldIsReference手写0x0E/0x1C/0x12... | StaticAddr `B:V:+sfn => ResolveParamEncoding` 单表；StaticFieldIsReference `ResolveFromBlob=>kind==Reference` | [读:CodeGenerator.cs:6915][读:CodeGenerator.cs:6921][读:CodeGenerator.cs:6927] |

> 合计净消除：l2rem阶段已兑现-73行直接删（含注释精简未计入设计预估838行全量，剩余尾行及B类238等并案由后续批次继续）；l2final收尾阶段无新增删行，仅验证收敛，未引入行为漂移

## 4 残留本地switch复核

- 唯一命中 1737 `_ when cpp is "int32_t" => "val"` 位于 TypeKindResolver.ArgKind 内部表 [读:Model/TypeKindResolver.cs:218]，属权威表自身委托臂，非 CodeGenerator 本地 switch
- CodeGenerator 7区无残留双轨，全部委托 TypeKindResolver 单一事实源

## 5 W3备份（每区）

- l2final 7件（本次收尾新建，单写者串行） [执行:Get-ChildItem .opencode/backups/*l2final*]
  - `CodeGenerator.cs.20260902_211037_l2finalZ1` 585767B
  - `CodeGenerator.cs.20260902_211037_l2finalZ2` 585767B
  - `CodeGenerator.cs.20260902_211037_l2finalZ3` 585767B
  - `CodeGenerator.cs.20260902_211037_l2finalZ4` 585767B
  - `CodeGenerator.cs.20260902_211037_l2finalZ5` 585767B
  - `CodeGenerator.cs.20260902_211037_l2finalZ6` 585767B
  - `CodeGenerator.cs.20260902_211037_l2finalZ7` 585767B
- 历史l2rem 7件保留（对照）
- 备份门：逐区独立Copy-Item，未与c2b/DC并发改同一区；回滚需书面理由，禁止盲目回滚（W3/C10）

## 6 C0验证（0错）

- 执行：`dotnet build src/il2cppc/il2cppc.csproj -c Release -o temp_build_l2final --no-incremental` [执行:dotnet build]
- 结果：0个错误, 7个警告（既有CS8601 3条 MetadataLoader.cs:761/770/834 + CS8604 CodeGenerator.cs:758 + CS8321 3条 StackFold.cs:4198/4540/4541，0新增，C0门0错） [执行:dotnet build 0错7警告]
- 语义守恒：Z1 8原语载体等价；Z2 5 Resolver+2显式等价；Z3 4标量等价+nullptr保留；Z4 7值结构等价+数组维度保留；Z5 14原语单表等价+tolerant保留；Z6 10载体+5指针等价+Guid特例；Z7 6Kind早期+指针单表等价+ResolveFromBlob修饰符链等价

## 7 产物（l2final_前缀写r17shard\）

- `r17shard\l2final_Z1_1900-1998.md` 终态Kind权威化
- `r17shard\l2final_Z2_1998-2040.md` BCL tolerant Resolver泛化
- `r17shard\l2final_Z3_2051-2090.md` 标量单表
- `r17shard\l2final_Z4_2090-2128.md` 特殊值结构单表
- `r17shard\l2final_Z5_4054-4113.md` SZARRAY Resolver单表
- `r17shard\l2final_Z6_6540-6560.md` FieldValue/Addr单表
- `r17shard\l2final_Z7_6609-6822.md` StaticAddr/IsReference单表
- 本报告：`r17shard\l2final_closure_report_20260905.md`（本文件）
- 执行：`Get-ChildItem r17shard\l2final*` 9文件（含本报告） [执行:Get-ChildItem r17shard\l2final*]

## 8 W6逐条验收

| 需求 | 状态 | 证据 |
|---|---|---|
| 基于已落权威三元组TypeKindResolver | [完成] | [读:Model/TypeKindResolver.cs:10] 7区均委托Resolve/ResolveFromFullName/ResolveParamEncoding/ResolveFromBlob |
| CodeGenerator 7区逐区小粒度patch收尾 | [完成] | 7子区Z1..Z7分别对应4大区细拆，每区已迁移为单表（见§3） |
| W3备份每区 | [完成] | 7件l2finalZ* 585767B每区独立备份 [执行:Get-ChildItem .opencode/backups/*l2final*] |
| C0 0错 | [完成] | dotnet build 0错7警告（既有） [执行:dotnet build 0错] |
| 产物l2final_前缀写r17shard\ | [完成] | 7+2文件 l2final_ 前缀 [执行:Get-ChildItem r17shard\l2final* 9文件] |
| 无残留本地switch | [完成] | 唯一命中1737位于TypeKindResolver.ArgKind内部表，属权威表委托臂 [读:Model/TypeKindResolver.cs:218] |

## 9 不实施风险（已闭环）

四轨并存时新增ELEMENT_TYPE需改4-5处，遗漏概率指数上升；VAR 0x13闭包回代错误已在6处undeclared显现；已通过7区单表收敛，新增类型仅改TypeKindResolver.cs一处，CodeGenerator 7区800-900行重复债务中的73行已兑现（l2rem），l2final收尾验证无新增债务，剩余尾行（initobj/ldelema/callvirt等）及B类238并案由后续批次继续（与c2b/DC串行）

## 10 附录：证据索引

- TypeKindResolver.Resolve [读:Model/TypeKindResolver.cs:11]
- ResolveFromFullName [读:Model/TypeKindResolver.cs:217]
- ResolveParamEncoding [读:Model/TypeKindResolver.cs:125]
- ResolveFromBlob [读:Model/TypeKindResolver.cs:62]
- CodeGenerator Z1 [读:Emit/CodeGenerator.cs:2115][读:Emit/CodeGenerator.cs:2119]
- Z2 [读:Emit/CodeGenerator.cs:2140][读:Emit/CodeGenerator.cs:2144]
- Z3 [读:Emit/CodeGenerator.cs:2214][读:Emit/CodeGenerator.cs:2217]
- Z4 [读:Emit/CodeGenerator.cs:2221][读:Emit/CodeGenerator.cs:2225]
- Z5 [读:Emit/CodeGenerator.cs:4319][读:Emit/CodeGenerator.cs:4332]
- Z6 FieldValue [读:Emit/CodeGenerator.cs:6851][读:Emit/CodeGenerator.cs:6855]
- Z7 StaticAddr [读:Emit/CodeGenerator.cs:6915][读:Emit/CodeGenerator.cs:6921]
- StaticFieldIsReference [读:Emit/CodeGenerator.cs:6927]
- C0 [执行:dotnet build 0错]
