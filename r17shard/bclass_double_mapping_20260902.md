# B类双switch逐类型发射字符串对照（2026-09-02）

## 1 ScalarTable 单表（D1 只读，18项）

| FullName | CppType | BoxHelper | UnboxHelper | NeedsAddressOf |
|----------|---------|-----------|-------------|----------------|
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
| System.DateTime | Il2CppDateTime | icall_box_date_time | icall_unbox_date_time | true |
| System.TimeSpan | Il2CppTimeSpan | icall_box_time_span | icall_unbox_time_span | true |
| System.Decimal | Il2CppDecimal | icall_box_decimal | icall_unbox_decimal | true |
| System.Guid | System_Guid | icall_box_guid | icall_unbox_guid | true |
| System.IntPtr | intptr_t | icall_box_intptr | icall_unbox_intptr | false |
| System.UIntPtr | uintptr_t | icall_box_uintptr | icall_unbox_uintptr | false |

> 读:E:\il2cpp\src\il2cppc\Emit\ScalarTable.cs:13。`IsPrimValTypeShort`/`ScalarElemCpp` 已委托 `IsScalar/TryGet`（`r17shard` 前置已验）。

## 2 Box 13标量对照（原 switch 臂 vs 新查表分支）

| Type | 原 `boxType switch` 发射 | 新 `ScalarTable.TryGet` 分支 | 字段 |
|------|--------------------------|------------------------------|------|
| System.Int32 | `il2cpp::il2cpp_box_int32(nullptr, val)` | `if NeedsAddressOf? else if Int32 => BoxHelper(nullptr,val)` | `BoxHelper=il2cpp_box_int32` |
| System.Boolean | `icall_box_bool(bool(val))` | `else if Boolean => BoxHelper(bool(val))` | `BoxHelper=icall_box_bool` |
| System.Char | `icall_box_char(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.Double | `icall_box_double(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.Int64 | `icall_box_long(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.Byte | `icall_box_byte(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.SByte | `icall_box_sbyte(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.Int16 | `icall_box_short(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.UInt16 | `icall_box_ushort(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.UInt32 | `icall_box_uint(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.UInt64 | `icall_box_ulong(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.Single | `icall_box_float(val)` | `BoxHelper(val)` | `BoxHelper` |
| System.Decimal | `icall_box_decimal(Il2CppDecimal* &(val))` | `if NeedsAddressOf => BoxHelper(CppType* &(val))` | `BoxHelper + CppType + NeedsAddressOf` |

> 保留：`enum` 双分支（`BaseTypeName=="System.Enum"`→`icall_box_enum`，`KnownBclEnumNames`→`icall_box_long`）与 `reference identity`（`System.Object/ValueType/!IsValueType`）与 `P25b user struct`（`icall_box_user_struct`）在查表前/后原样（读:CodeGenerator.cs:7247/7249/7251/7268）。

**扩展 5 标量**（DateTime/TimeSpan/Guid/IntPtr/UIntPtr）由 `NeedsAddressOf` 驱动新增 `box_*` 路径，其运行时符号已在 `il2cpp_boxing_ext.h` 声明（只读已验 `r17shard\bclass_scalar_investigation.md:115`），属开闭新增。

## 3 Unbox 13标量对照

| Type | 原 `unboxType switch` | 新分支 | 字段 |
|------|-----------------------|--------|------|
| System.Int32 | `il2cpp_unbox_int32(val)` | `UnboxHelper(val)` | `UnboxHelper=il2cpp_unbox_int32` |
| System.Boolean | `icall_unbox_bool(val)` | `UnboxHelper(val)` | `UnboxHelper` |
| System.Char | `icall_unbox_char(val)` | `UnboxHelper(val)` | `UnboxHelper` |
| System.Double | `icall_unbox_double(val)` | `UnboxHelper(val)` | |
| System.Int64 | `icall_unbox_long(val)` | `UnboxHelper(val)` | |
| System.Byte | `icall_unbox_byte(val)` | | |
| System.SByte | `icall_unbox_sbyte(val)` | | |
| System.Int16 | `icall_unbox_short(val)` | | |
| System.UInt16 | `icall_unbox_ushort(val)` | | |
| System.UInt32 | `icall_unbox_uint(val)` | | |
| System.UInt64 | `icall_unbox_ulong(val)` | | |
| System.Single | `icall_unbox_float(val)` | | |
| System.Decimal | `icall_unbox_decimal(val)` | | |

> 单臂 `UnboxHelper(val)` 覆盖 13 标量；`P25b`（`il2cpp_unbox_user_struct` + `RefFieldCount` 双形态）与 `tolerant 0` 保留（读:CodeGenerator.cs:7298/7305）。

## 4 P25b/enum/Decimal 保留证据

- `box` enum：`boxTypeDef.BaseTypeName=="System.Enum"`→`icall_box_enum(BoxClassDescriptorCpp, int64_t)`（读:7247）、`KnownBclEnumNames.Contains=>icall_box_long`（读:7249）— 未改。
- `box` P25b：`boxTypeDef.IsValueType => icall_box_user_struct(&T_class, &(val), sizeof(T))` 含 `P25b extended bitmap fix` 注释（读:7268）。
- `unbox` P25b：`unboxTypeDef.IsValueType => il2cpp_unbox_user_struct` + `RefFieldCount>0` 双形态（读:7298）。
- `Decimal` 的 `NeedsAddressOf` 分支与原注释 `decimal-domain T3 16-byte ADDRESS` 等价（读:7260）。

## 5 验证方法

- `Select-String "ScalarTable.TryGet"` 2 命中（box/unbox 各1）。
- `dotnet build -o temp 0错`（执行:dotnet build）。
- `bclass_double_evidence_20260902.txt:硬编码键集删除 0命中`。

