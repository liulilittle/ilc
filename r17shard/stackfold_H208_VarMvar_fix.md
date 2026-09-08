# StackFold H报告② 208行 int2ptr+ptr2int修复 — 0x13/0x1E闭合实例臂

## 任务
修复 `StackFold.IsRefField` 对 ELEMENT_TYPE_VAR(0x13)/MVAR(0x1E) 的错误分类。旧路径按M3单例化恒判为值类型(int64的int32槽)，导致闭合实例中 `T=string/object` 等引用实参的字段仍走 `int32_t` 本地槽，发射 `int2ptr+ptr2int` 转换在208行附近触发 `reinterpret_cast` 类型不匹配。

## 修改文件（仅此一文件）
- 读: E:\il2cpp\src\il2cppc\Ir\StackFold.cs:4539
- 读: E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:502
- 执行: Copy-Item StackFold.cs -> E:\il2cpp\.opencode\backups\StackFold.cs.20260902-113402 [W3备份]
- 改: StackFold.cs 新增共享 helper `IsRefElementKind` + `IsClosureArgReference`（与 TypeEmitter 502-526 共表：0x0E/0x12/0x1C/0x1D/0x14/0x15 => Reference，其余值类型），在 `IsRefField` 中对 0x13/0x1E 解码压缩索引查 `_curClosureArgs` 判ref，`RefTargetFullName` 同步修饰符剥离+0x13/0x1E返回闭合全名

## 变更明细（对应任务行号 4559-4572 / 4577-4591）

### 1) IsRefElementKind — 共享 TypeEmitter 502-526
```csharp
static bool IsRefElementKind(int typeByte) => typeByte switch { 0x0E or 0x12 or 0x1C or 0x1D or 0x14 or 0x15 => true, _ => false, };
```
与 TypeEmitter.DecodeNestedFieldSignature 502-526:
- 0x0E String => Reference, 0x1C Object => Reference, 0x12 CLASS => Reference, 0x1D/0x14 SZARRAY/ARRAY => Reference, 0x15 GenericInst => Reference, 0x13 VAR => Int32(动态后判), 0x11 VALUETYPE => Int32, 默认 => Reference — 完全对齐，仅 VAR/MVAR 由闭合实参覆盖。

### 2) IsClosureArgReference — 闭合实参值/引用判定
复用 `IsRefTypeName` 值类型集 + 2797行闭合逻辑及Sanitized短名 `int/long/double/string/object/array`：
- `System.Int32/Int64/Boolean/Byte/SByte/Int16/UInt16/UInt32/UInt64/Single/Double/Char/IntPtr/UIntPtr/Guid/Decimal/DateTime/TimeSpan/DateTimeOffset` 及短名 `int/long/double` 等 => value
- `System.String/System.Object` 及短名 `string/object/array` => reference

### 3) IsRefField 0x13/0x1E 臂（原 4559-4572 区间内）
```csharp
if (typeByte is 0x13 or 0x1E) {
  // VAR索引 = Following compressed int (1/2/4字节，ECMA II.24)
  var idxPos=pos+2; firstIdxByte...
  if (_curClosureArgs!=null && varIdx in range) return IsClosureArgReference(_curClosureArgs[varIdx]);
  return false; // 无闭合则M3 int32
}
```
- 修饰符已在 pos 循环中剥离（0x1F/0x20/0x2D + coded index），`pos` 精确定位真实 element
- 压缩整数宽度按 `cb<0x80?1:(cb&0xC0)==0x80?2:(cb&E0)==0xC0?4:1`，与 `ReadCompressedInteger` 一致

### 4) RefTargetFullName 同步（原 4577-4591 区间内）
- 同样剥离修饰符（原实现缺失，直接读 2,2）
- 0x12 仍走 `ResolveFieldRefTarget`，0x1D/0x14 => `System.Array`，0x15 => `System.Object`
- 0x13/0x1E：解码索引查 `_curClosureArgs`，若 `IsClosureArgReference` 为真则返回闭合全名（`System.String` 等），否则 `""`（值字段无target）

## 证据
- 执行: dotnet build src/il2cppc/il2cppc.csproj -c Release — 0 个警告 0 个错误 [BASH:dotnet build 1.07s]
- 读: StackFold.cs:4595-4620 IsRefElementKind/IsClosureArgReference 已插入
- 读: StackFold.cs:4628-4655 IsRefField VAR臂
- 读: StackFold.cs:4680-4720 RefTargetFullName 修饰符+VAR同步

## 验证方法（待执行/已部分）
- C0: dotnet build 零错已验证
- 208行复现：需构造 `class Box<T>{ T v; }` 闭合 `Box<string>` 对 `ldfld T v` / `stfld T v` 折叠，断言 `IsRefField==true` 且 `RefTarget=="System.String"` 时发射 `Il2CppObject*` 而非 `int32_t`，原路径产生 `int2ptr`+`ptr2int`
- 边界：`Box<int>` => IsRef false + target "" ; `Box<object>` => true + "System.Object" ; 无闭合原型 `Box<T>` => false ; MVAR 0x1E 同理
- 与 TypeEmitter 共表验证：对比 `DecodeNestedFieldSignature` 0x0E/0x12/0x1C/0x1D/0x14/0x15 分支完全一致

## 产物
- r17shard/stackfold_H208_VarMvar_fix.md（本文件）
