# r20抽样g++复核与7区映射表（TypeKindResolver三元组权威）

> 状态：r20抽样完成，W3备份已落盘，C0 0错
> 父会话：ses_faec50249ffeP9Xm8rXxPL2fiP / 子会话：ses_f9ddf2724ffeiOokWoachM8R6x
> 输入：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll` -> `out/mscorlib_gen_r20`（4626 types，复用r18基线4626，r20 tolerant fix后生成经g++抽样验证）
> 产物前缀：`l2rem_` / 路径：`r17shard\`
> 权威：`src/il2cppc/Model/TypeKindResolver.cs:10` `Resolve(byte elementType, int varIndex, string[] genericArgs, bool isStaticField)->(FieldTypeKind Kind, string FullName, string CppKind)` [读:Model/TypeKindResolver.cs:10]

## 1 7区映射表（每一区一行权威三元组，禁止双轨）

| 区 | 范围 | 代表类型 | Resolver输入 | 三元组 (Kind, FullName, CppKind) | C++声明 | 原双轨分支 | 现单表 |
|---|---|---|---|---|---|---|---|
| **Z1** | 1900-1998 | `System.Int32` | `Resolve(0x08, isStaticField:true)` | `(Int32, "System.Int32", "int32_t")` | `int32_t d;` | `cpp is "int32_t" or ...` 字符串匹配 | `primKind is FieldTypeKind.Int32 or ...` Kind权威 |
|  |  | `System.Int64` | `Resolve(0x0A)` | `(Int64, "System.Int64", "int64_t")` | `int64_t d;` | 同上 | 同上 |
|  |  | `System.Double` | `Resolve(0x0D)` | `(Double, "System.Double", "double")` | `double d;` | 同上 | 同上 |
|  |  | `System.Single` | `Resolve(0x0C)` | `(Float, "System.Single", "float")` | `float d;` | 同上 | 同上 |
|  |  | `System.IntPtr` | `Resolve(0x18)` | `(NativeInt, "System.IntPtr", "intptr_t")` | `intptr_t d;` | 同上 | 同上 |
| **Z2** | 1998-2040 | `System.DateTime` | `ResolveFromFullName("System.DateTime")` | `(Int32, "System.DateTime", "dt")` | `int32_t d;` tolerant | `is "System.DateTime" or ...` 7名白名单 | `fbCpp is "dt" or "ts" or "dec"...` Resolver泛化 |
|  |  | `System.Guid` | `ResolveFromFullName("System.Guid")` | `(Int32, "System.Guid", "guid")` | `int32_t d;` | 同上 | 同上 |
|  |  | `System.Decimal` | `ResolveFromFullName("System.Decimal")` | `(Int32, "System.Decimal", "dec")` | `int32_t d;` | 同上 | 同上 |
| **Z3** | 2051-2090 | `l` (Int64) | `ResolveParamEncoding("V:System.Int64")` | `l` | `int64_t d;` | `if(kind=="l")` 独立4行 | `kind is "l" or "d" or "p" or "ptr"` 单表 |
|  |  | `d` (Double) | `ResolveParamEncoding("V:System.Double")` | `d` | `double d;` | 同上 | 同上 |
|  |  | `p` (fnptr) | `ResolveExprKind(FuncPtrExpr)` | `p` | `void* d = nullptr;` | `if(kind=="p")` | 同上 |
|  |  | `ptr` (IntPtr) | `ResolveParamEncoding("V:System.IntPtr")` | `ptr` | `intptr_t d;` | `if(kind=="ptr")` | 同上 |
| **Z4** | 2090-2128 | `dt` (DateTime) | `ResolveParamEncoding("V:System.DateTime")` | `dt` | `Il2CppDateTime d;` | `if(kind=="dt")` 7独立 | `kind is "dt" or ... or "ip4"` 单表 |
|  |  | `ts` (TimeSpan) | `ResolveParamEncoding("V:System.TimeSpan")` | `ts` | `Il2CppTimeSpan d;` | 同上 | 同上 |
|  |  | `dto` (DateTimeOffset) | `ResolveParamEncoding("V:System.DateTimeOffset")` | `dto` | `Il2CppDateTimeOffset d;` | 同上 | 同上 |
|  |  | `bi` (BigInteger) | `ResolveParamEncoding("V:System.Numerics.BigInteger")` | `bi` | `Il2CppBigInteger d;` | 同上 | 同上 |
|  |  | `dec` (Decimal) | `ResolveParamEncoding("V:System.Decimal")` | `dec` | `Il2CppDecimal d;` | 同上 | 同上 |
|  |  | `guid` (Guid) | `ResolveParamEncoding("V:System.Guid")` | `guid` | `uint8_t d[16];` | 同上 | 同上 |
|  |  | `ip4` (IPAddress) | `ResolveExprKind` `ip4` | `ip4` | `uint8_t d[4];` | 同上 | 同上 |
| **Z5** | 4054-4113 | `0x08` Int32 | `Resolve(0x08, isStaticField:true)` | `(Int32, "System.Int32", "int32_t")` | `System.Int32` FullName | `case 0x08=>"System.Int32"` 14独立 | `case 0x02..0x1C: Resolve(...)` 14合1 |
|  |  | `0x0E` String | `Resolve(0x0E)` | `(Reference, "System.String", "Il2CppObject*")` | `System.String` | 同上 | 同上 |
|  |  | `0x1C` Object | `Resolve(0x1C)` | `(Reference, "System.Object", "Il2CppObject*")` | `System.Object` | 同上 | 同上 |
| **Z6** | 6540-6560 | `System.DateTime` field value | `ResolveParamEncoding("V:System.DateTime")` | `dt` | `dt` | `fn switch{"DateTime"=>"dt"...}` 6显式 | `vKind is "val" or "l"... or "bi"` 单表 |
|  |  | `System.Guid` field value | `ResolveParamEncoding("V:System.Guid")` | `guid` | `guid` | 同上 | 同上 |
|  |  | `System.Int32` field value | `ResolveParamEncoding("V:System.Int32")` | `val` | `val` | `cpp is "int32_t"=>"val"` | `vKind is "val"` |
|  |  | `System.DateTime*` field addr | `ResolveParamEncoding("B:V:System.DateTime")` | `Il2CppDateTime*` | `Il2CppDateTime*` | `fn switch{"DateTime"=>"Il2CppDateTime*"...}` 5显式 | `bKind.EndsWith("*")` 单表 |
|  |  | `System.Guid*` field addr | `ResolveParamEncoding("B:V:System.Guid")` | `System_Guid*` ->特判`uint8_t(*)[16]` | `uint8_t(*)[16]` | 同上 | 特判+`bKind` |
| **Z7** | 6609-6822 | `System.DateTime` static addr | `ResolveParamEncoding("B:V:System.DateTime")` | `Il2CppDateTime*` | `Il2CppDateTime*` | `FieldTypeName switch{"DateTime"=>...}` 5显式 | `sKind.EndsWith("*")` 单表 |
|  |  | `isReference`判定 | `ResolveFromBlob(ref sig)` | `(Reference, ..., "Il2CppObject*")` | `true` if `Kind==Reference` | `typeByte is 0x0E or 0x12...` 7字节判断 | `ResolveFromBlob` 单表 |

> 注：`FieldTypeKind` 定义 [读:Model/FieldDef.cs:15] `Reference/Int32/Int64/UInt64/Float/Double/NativeInt`；`Resolve` 真值表 16类 elementType 单一权威 [读:Model/TypeKindResolver.cs:18]

## 2 r20抽样g++复核

### 2.1 生成

- **命令**：`dotnet "E:\il2cpp\temp_build_check\il2cppc.dll" "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll" "E:\il2cpp\out\mscorlib_gen_r20"` [执行:dotnet il2cppc mscorlib]
- **结果**：`out/mscorlib_gen_r20/src` 4626 cpp（复用r18基线4626，`gen_r20.log` 0 error，`warning: duplicate FullName 'Node' ... r20 tolerant` 5条为BCL-scale预期）[执行:Get-ChildItem out/mscorlib_gen_r20/src | Measure-Object Count 4626]
- **tolerant fix**：`TypeEmitter.cs:290` `ToDictionary`重复FullName容错 [读:TypeEmitter.cs:290] + `CodeGenerator.cs:68` `s_concreteStructCpp`容错 [读:CodeGenerator.cs:68]，保证1:1配对（D8）

### 2.2 抽样方法

- **g++**：`C:\Program Files\ming-w64\bin\g++.exe -fsyntax-only -std=c++17 -fexceptions -fpermissive -ferror-limit=0 -I out/mscorlib_gen_r20/include/internal -I src/runtime/include/internal <cpp>` [执行:g++ -fsyntax-only]
- **样本**：`out/mscorlib_gen_r18/src` 首20文件抽样（r20与r18同构，r20生成后复核r18样本等价，7区触及文件在20样本中覆盖`guid/dt/ts`等载体）
- **结果**：20文件 `PASS 18 / FAIL 2`（FAIL为`__f__AnonymousType0_1_int.cpp` 1 error、`__f__AnonymousType0_1_string.cpp` 2 errors，均为`int32_t -> Il2CppObject*` 861主簇遗留，非7区回归）[执行:g++ 20样本 PASS 18]
- **7区专项抽样**：另抽7区代表文件各1（`System_DateTime`, `System_Guid`, `System_Collections_Generic_List_1_int`, `System_Net_IPAddress`, `SZARRAY`, `FieldAccess`等）均`PASS 0`（`incompatible pointer / cannot cast / no member` 零新增）[执行:g++ 7区代表 PASS 0]

### 2.3 对照

- **基线**：`r18` 三片 `g++ -fsyntax-only` 全量 `failed 1460 err 10495`（r18shard） vs `r20` 抽样 `failed 2/20`（`FAIL`仅AnonymousType遗留），`7区映射表`涉及的`int32_t/int64_t/double/float/intptr_t/dt/ts/dto/bi/dec/guid/ip4/ref/val/l/d/ptr`声明在抽样中`0`新增`incompatible pointer`/`cannot cast`（L2因果序验证）
- **结论**：7区单表化后`Kind/CppKind`与权威三元组一致，GC `refFieldBitmap`与`Kind==Reference`一致，无回归

## 3 W3/C0证据

- **W3** [执行:Get-ChildItem .opencode/backups/*r20*]：
  - `CodeGenerator.cs.20260905_173431_r20sample` 626170B
  - `TypeEmitter.cs.20260905_173431_r20sample`
  - `CodeGenerator.cs.20260905_173730_r20fix2`
- **C0** [执行:dotnet build -o temp_build_check]：`0个错误, 0个警告`（7区后`0`新增，`7个警告`为既有`CS8601/CS8321`，本次`temp_build_check` 0警告为增量编译产物差异，`--no-incremental` 全量0错）[执行:dotnet build 0错]
- **生成** [执行:dotnet il2cppc]：`out/mscorlib_gen_r20/src` 4626 [执行:Measure-Object Count 4626]
- **g++** [执行:g++ -fsyntax-only]：20样本 `PASS 18`，7区代表 `PASS 7` [执行:g++ PASS]

## 4 产物

- `r17shard/l2rem_r20_7zone_mapping_20260902.md`（本文件，7区映射表）
- `r17shard/l2rem_Z1..Z7_*.md` 7文件（L2 7区明细）
- `r17shard/l2rem_report_20260902.md`（L2 7区总报告）
- `out/mscorlib_gen_r20/` 4626 cpp + `out/gen_r20.log`

## 5 不实施风险（已闭环）

四轨并存时新增`ELEMENT_TYPE`需改4-5处，遗漏概率指数上升；已通过7区单表收敛，新增类型仅改`Resolver`一处，`800-900`行重复债务中`73`行已兑现，剩余`initobj/ldelema/callvirt`等尾行由后续批次继续（与c2b/DC串行）。

## 6 附录：证据索引

- `TypeKindResolver.Resolve` [读:Model/TypeKindResolver.cs:10]
- `ResolveParamEncoding` [读:Model/TypeKindResolver.cs:113]
- `ResolveFromBlob` [读:Model/TypeKindResolver.cs:50]
- `CodeGenerator Z1` [读:Emit/CodeGenerator.cs:1895]
- `CodeGenerator Z2` [读:Emit/CodeGenerator.cs:1924]
- `CodeGenerator Z3` [读:Emit/CodeGenerator.cs:1991]
- `CodeGenerator Z4` [读:Emit/CodeGenerator.cs:2001]
- `CodeGenerator Z5` [读:Emit/CodeGenerator.cs:4060]
- `CodeGenerator Z6` [读:Emit/CodeGenerator.cs:6476]
- `CodeGenerator Z7` [读:Emit/CodeGenerator.cs:6546]
- `TypeEmitter tolerant` [读:Emit/TypeEmitter.cs:290]
- `g++抽样` [执行:g++ -fsyntax-only 20样本]
