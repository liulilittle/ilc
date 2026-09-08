# B类icall 8臂查表映射（r17shard, 2026-09-05, r20）

## 1 ScalarTable 单表（D1 只读，20项，扩展dto/bi）

| FullName | CppType | BoxHelper | UnboxHelper | NeedsAddressOf |
|---|---|---|---|---|
| System.Boolean | int32_t | icall_box_bool | icall_unbox_bool | false |
| System.Byte | int32_t | icall_box_byte | icall_unbox_byte | false |
| System.SByte | int32_t | icall_box_sbyte | icall_unbox_sbyte | false |
| System.Char | int32_t | icall_box_char | icall_unbox_char | false |
| System.Int16 | int32_t | icall_box_short | icall_unbox_short | false |
| System.UInt16 | int32_t | icall_box_ushort | icall_unbox_ushort | false |
| System.Int32 | int32_t | il2cpp_box_int32 | il2cpp_unbox_int32 | false |
| System.UInt32 | int32_t | icall_box_uint | icall_unbox_uint | false |
| System.Int64 | int64_t | icall_box_long | icall_unbox_long | false |
| System.UInt64 | int64_t | icall_box_ulong | icall_unbox_ulong | false |
| System.Single | float | icall_box_float | icall_unbox_float | false |
| System.Double | double | icall_box_double | icall_unbox_double | false |
| System.DateTime | il2cpp::runtime::Il2CppDateTime | icall_box_date_time | icall_unbox_date_time | true |
| System.TimeSpan | il2cpp::runtime::Il2CppTimeSpan | icall_box_time_span | icall_unbox_time_span | true |
| System.Decimal | il2cpp::runtime::Il2CppDecimal | icall_box_decimal | icall_unbox_decimal | true |
| System.Guid | System_Guid | icall_box_guid | icall_unbox_guid | true |
| System.DateTimeOffset | il2cpp::runtime::Il2CppDateTimeOffset | icall_box_date_time_offset | icall_unbox_date_time_offset | true |
| System.Numerics.BigInteger | il2cpp::runtime::Il2CppBigInteger | icall_box_big_integer | icall_unbox_big_integer | true |
| System.IntPtr | intptr_t | icall_box_intptr | icall_unbox_intptr | false |
| System.UIntPtr | uintptr_t | icall_box_uintptr | icall_unbox_uintptr | false |

> 读:src/il2cppc/Emit/ScalarTable.cs:13-33。新增 DateTimeOffset/BigInteger 两项 [读:ScalarTable.cs:31][读:ScalarTable.cs:32]。

## 2 icall 8臂查表前后对照（B类8臂，r20）

| CastCode | 原 switch 硬编码发射 | 新 ScalarTable.TryGet 分支 | 字段 |
|---|---|---|---|
| deref:dt | `(*reinterpret_cast<Il2CppDateTime*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` desc=Table["System.DateTime"] | CppType |
| deref:ts | `(*reinterpret_cast<Il2CppTimeSpan*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` desc=Table["System.TimeSpan"] | CppType |
| deref:dto | `(*reinterpret_cast<Il2CppDateTimeOffset*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` desc=Table["System.DateTimeOffset"] | CppType |
| deref:bi | `(*reinterpret_cast<Il2CppBigInteger*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` desc=Table["System.Numerics.BigInteger"] | CppType |
| deref:dec | `(*reinterpret_cast<Il2CppDecimal*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` desc=Table["System.Decimal"] | CppType |
| dec | `LocAddr?code:&(code)` | 同 shape-aware，经 TryGet("System.Decimal") 门控 | NeedsAddress |
| enumval | `static_cast<int64_t>(*code/code)` | `static_cast<{desc.CppType}>(*code/code)` desc=Table["System.Int64"] | CppType=int64_t |
| enumaddr | `reinterpret_cast<int64_t*>(code/&code)` | `reinterpret_cast<{desc.CppType}*>(code/&code)` desc=Table["System.Int64"] | CppType |

> 映射表 [读:CodeGenerator.cs:8366-8373]：8项 `deref:dt=>DateTime, deref:ts=>TimeSpan, deref:dto=>DateTimeOffset, deref:bi=>BigInteger, deref:dec=>Decimal, dec=>Decimal, enumval=>Int64, enumaddr=>Int64` 经 TryGet(scalarType2) 单点派生 CppType，删除 8处硬编码 `Il2Cpp*`/`int64_t` 字面。

## 3 全量零硬编码核查（switch残留）

- 执行:Select-String '"deref:dt" =>.*Il2CppDateTime' 0命中
- 执行:Select-String '"deref:ts" =>.*Il2CppTimeSpan' 0命中
- 执行:Select-String '"deref:dto" =>.*Il2CppDateTimeOffset' 0命中
- 执行:Select-String '"deref:bi" =>.*Il2CppBigInteger' 0命中
- 执行:Select-String '"deref:dec" =>.*Il2CppDecimal' 0命中
- 执行:Select-String '"dec" =>.*&\(code\)' 0命中（均经表分支）
- 执行:Select-String '"enumval" =>.*static_cast<int64_t>' 0命中（经CppType）
- 执行:Select-String '"enumaddr" =>.*reinterpret_cast<int64_t' 0命中（经CppType）
- 读:CodeGenerator.cs:8366 8臂路由存在，读:CodeGenerator.cs:8551 B类8臂已查表注释存在，switch中硬编码已清零。

## 4 r20分片验证（单文件g++冒烟）

- 执行:dotnet build src/il2cppc/il2cppc.csproj -o temp 0错0警告 [执行:dotnet build:输出 0个错 0个警告]
- 执行:g++ -std=c++17 -I src/runtime/include/internal -fsyntax-only r20_smoke2.cpp（含 decimal.h/il2cpp_time.h/il2cpp_dtoffset.h/il2cpp_biginteger.h 覆盖8臂CppType）[执行:g++ -fsyntax-only r20_smoke2.cpp:输出 EXIT:0]
- 读:ScalarTable.cs:31 DateTimeOffset，读:ScalarTable.cs:32 BigInteger，读:CodeGenerator.cs:8380 TryGet(scalarType2)，均单表派生。

## 5 验证方法

- 全量核查：Select-String 8项硬编码发射均0命中，仅映射表 `"deref:dt" => "System.DateTime"` 等8项命中（非C++硬编码）。
- r20分片：单文件g++冒烟覆盖8臂CppType，0错即证明 Il2CppDateTime/Span/Offset/BigInteger/Decimal/int64_t 均可编译，无缺符号。
