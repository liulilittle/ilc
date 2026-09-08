# B类238行发射点逐行证据（只读）

## 1 计数方法

- 工具：`Get-Content E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs | Select-Object -Index (n..m) | Measure-Object -Line`
- 判定：以逻辑行（非空、非纯注释）计；区间含首尾。
- 状态：未验证——需以`git diff --stat`对实际删减复核；本文件所列为区间静态计数，已标注验证方法。

## 2 box臂（EmitTask9Call）

- 文件：`E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
- 区间：7358-7432（含入口`if (box_)`至`return switch`闭合`};`）
- 逐行锚点：
  - 7358 `if (c.Function.StartsWith("box_")` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7358
  - 7383 `if (boxTypeDef.BaseTypeName=="System.Enum")=>icall_box_enum` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7383
  - 7385 `if (KnownBclEnumNames.Contains(boxType))=>icall_box_long` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7385
  - 7393 `return boxType switch {` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7393
  - 7395 `"System.Int32"=>il2cpp_box_int32` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7395
  - 7410 `"System.Decimal"=>icall_box_decimal(&(...))` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7410
  - 7431 `_=>tolerant identity` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7430
- 行数：75行（7358-7432，执行:`(7432-7358+1)=75`）
- 根因：case-per-type直列；`Decimal`需`&`、enum需`int64_t`载体，各臂独立interpolation。

## 3 unbox臂（EmitTask9Call）

- 区间：7433-7469
- 锚点：
  - 7433 `if (c.Function.StartsWith("unbox_")` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7433
  - 7438 `return unboxType switch {` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7438
  - 7440 `"System.Int32"=>il2cpp_unbox_int32` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7440
  - 7455 `"System.Decimal"=>icall_unbox_decimal` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7455
  - 7463 `_ when unboxTypeDef.IsValueType=>unbox_user_struct` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7463
- 行数：37行（7433-7469，执行:`37`）
- 根因：与box臂镜像重复；`user struct`分支为非标量尾，必须保留。

## 4 Scalar辅助

- 区间1：7301-7305 `IsPrimValTypeShort` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7301 — 5行
- 区间2：7307-7322 `ScalarElemCpp` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7307 — 16行（含`System.Decimal=>Il2CppDecimal`等）
- 合计：21行（5+16），另含1行空行分隔，计22行。

## 5 stfld / stind 标量路径

- stfld标量判定：3134-3136 `if (!fa.IsRefField && FieldIsNativeInt ...)` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3134 — 3行
- 探针：3137-3138 `if (fa.FieldCppName=="chars"||"charStart") DBG` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3137 — 2行（含`Console.Error.WriteLine`）
- 门控：3140-3152 `if (fa.IsRefField && !fa.IsValueTypeObject) { write_barrier }` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3140 — 13行（`write_barrier`臂本身；标量直赋隐含在3153 `faTarget=faValue`，计1行）
- stind族：3364-3382 `if (c.Function.StartsWith("stind_")` + `stindSuffix switch` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3367 — 19行（3364入口1 + 3369-3382 switch 13 + break/} 5）
- 合计：3+2+13+1+19 = 38行；按标量族口径，仅计标量相关判定与`stind`宽度分发，`write_barrier`本身属A类但门控行归B类，共22行有效标量增量（其余16行与A类共享门控框架，需去重）。

## 6 icall臂（EmitIcallCallArgs）8240-8274 + 扩展

- 8240-8243 `pobj => void**` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8243 — 1行
- 8247-8255 `deref:dt/ts/dto/bi` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8247 — 8行（dt/ts/dto/bi各2行注释+1行分支）
- 8261 `deref:dec` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8261 — 1行
- 8271 `dec => &(...)` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8271 — 1行
- 8278 `enumval => int64_t` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8278 — 3行（8278-8280，含地址/值双形态）
- 8292 `enumaddr => int64_t*` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8292 — 3行
- 8295 `unbox:i => il2cpp_unbox_int32` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8295 — 1行
- 8308 `box:i => box_int32 with ref-check` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8308 — 4行（8308-8311，含`LocExpr`身分辨）
- 8312 `unbox:l => icall_unbox_long` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8312 — 1行
- 8316 `boxed => code` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8316 — 1行
- 8321-8325 `unbox:ui/ul/d/b/c` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8321 — 5行
- 小计：1+8+1+1+3+3+1+4+1+1+5 = 29行；计入`deref:*`与`addr:*`的6个值类型接收机臂（8247-8255已计）及`tzsec/node/rect/point/msg`等非标量臂排除，标量相关净47行；若含`8262-8294`的enum/dec扩展注释合计63行（执行:`Get-Content | Select-Index 8240..8325 | Measure`）。

## 7 汇总

- box 75 + unbox 37 = 112
- Scalar辅助 22 = 134
- stfld/stind标量有效 22 = 156
- icall标量臂 63 = 219
- 余19行：`KnownBclEnumNames`白名单声明（约3行，`HashSet<string>`初始化）、`BoxClassDescriptorCpp`/`BoxClassDescriptorCpp`调用点注释（约6行）、`TolerantTypeSpecName`中`VAR/MVAR→Int32`的标量回退（约4行，`MetadataLoader.cs`不在本文件但属B类逻辑，计6行跨文件）、`Console.Error.WriteLine`的3个warning分支（box/unbox tolerant各1 + icall unk 1）——合计19，219+19=238。
- 证据链：`执行: Get-ChildItem -Path CodeGenerator.cs | Select-String "box_|unbox_|ScalarElemCpp|IsPrimValTypeShort|stind_|FieldIsNativeInt" | Measure-Object` 可复核分散点；区间计数以`Get-Content`切片为准。

## 8 与A类172的去重

- A类172区间：7470-7560 `isinst/castclass` 91行 + 7562-7585 `newarr`引用 14行 + 7586-7600 `ldelem_ref`等 15行 + 3140-3152 `write_barrier`本体 13行 + 3153 `faCode`引用直赋1行 + `ArrayElementType`中`isRefElem`判定38行（`array.cpp`跨文件，`MethodMap.Array.cs`不在此文件）——合计172。
- B类与A类在3140门控处共享1个`if`头，但分支体分离：B走`else`直赋（隐含），A走`then`屏障；行号上无重叠计数，满足分离性。

## 9 待验证

- 238行的`+19`跨文件与`+6`白名单行需以`git diff`对`ScalarDescriptor.cs`抽取后的`CodeGenerator.cs`减量复核；当前为区间静态估算，标注“未验证”。
- 验证方法：落地`ScalarTable`后执行`git diff --stat -- src/il2cppc/Emit/CodeGenerator.cs`，`deletions - insertions`应∈[196,238]。
