# B类icall 6臂查表映射（r17shard, 2026-09-02）

## 1 ScalarTable 单表（D1 只读，18项）

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
| System.IntPtr | intptr_t | icall_box_intptr | icall_unbox_intptr | false |
| System.UIntPtr | uintptr_t | icall_box_uintptr | icall_unbox_uintptr | false |

> 读:E:\il2cpp\src\il2cppc\Emit\ScalarTable.cs:13。`enumval/enumaddr` 复用 `System.Int64` 的 `int64_t` 载体（`System.Enum` 底层 `int64_t`），与 `Il2CppDateTime/TimeSpan/Decimal` 的 `CppType` 共用表项。

## 2 icall 6臂查表前后对照（B类6臂）

| CastCode | 原 `switch` 发射（硬编码） | 新 `ScalarTable.TryGet` 分支 | 字段/逻辑 |
|---|---|---|---|
| deref:dt | `(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` 其中 `desc=Table["System.DateTime"]` | CppType |
| deref:ts | `(*reinterpret_cast<il2cpp::runtime::Il2CppTimeSpan*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` 其中 `desc=Table["System.TimeSpan"]` | CppType |
| deref:dec | `(*reinterpret_cast<il2cpp::runtime::Il2CppDecimal*>({code}))` | `(*reinterpret_cast<{desc.CppType}*>({code}))` 其中 `desc=Table["System.Decimal"]` | CppType |
| dec | `LocAddr?code:&(code)` | 同 shape-aware，新增 `TryGet("System.Decimal")` 门控 | NeedsAddressOf=true 校验 |
| enumval | `static_cast<int64_t>(*code/code)` | `static_cast<{desc.CppType}>(*code/code)` 其中 `desc=Table["System.Int64"]` | CppType=int64_t |
| enumaddr | `reinterpret_cast<int64_t*>(code/&code)` | `reinterpret_cast<{desc.CppType}*>(code/&code)` 其中 `desc=Table["System.Int64"]` | CppType=int64_t |

> 映射表（`cast -> System.*`）位于 `读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7885`：
> `deref:dt=>DateTime, deref:ts=>TimeSpan, deref:dec=>Decimal, dec=>Decimal, enumval=>Int64, enumaddr=>Int64`，经 `ScalarTable.TryGet` 单点派生 `CppType`，删除6处硬编码 `Il2Cpp*`/`int64_t` 字面。

## 3 保留臂（非6臂，不在ScalarTable）

| CastCode | 发射 | 说明 |
|---|---|---|
| deref:dto | `(*reinterpret_cast<il2cpp::runtime::Il2CppDateTimeOffset*>({code}))` | 保留硬编码，待表增 `DateTimeOffset` 后查表 |
| deref:bi | `(*reinterpret_cast<il2cpp::runtime::Il2CppBigInteger*>({code}))` | 保留硬编码，待表增 `BigInteger` 后查表 |

## 4 验证方法

- `Select-String "\"deref:dt\" =>.*Il2CppDateTime` 在 `switch` 中 0命中（读:CodeGenerator.cs:8033 已删），仅映射表 `=> "System.DateTime"` 命中。
- `Select-String "ScalarTable.TryGet\(scalarType2"` 1命中（读:CodeGenerator.cs:7893）。
- `Select-String "scalarDesc2\.CppType"` 4命中（deref通用1 + enumval1 + enumaddr1 + deref分支1，读:CodeGenerator.cs:7897/7905/7912）。
- `dotnet build -o temp` 0错（执行:dotnet build:输出 0 个错 7 个警告）。
