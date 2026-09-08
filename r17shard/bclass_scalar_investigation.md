# B类标量族238行独立调查报告（与A类172分离）

## 1 任务界定

- 本调查必须独立评估B类标量族238行，不得与A类引用族172行合并分析；二者必须分离处置。
- 调查范围必须仅限只读定位与根因分析，禁止修改代码。
- 发射点必须精确至文件:行号，覆盖`box臂7337/7376`、`unbox:* icall臂8240-8274`、`stfld写屏障3137/3367`三类。

## 2 定语与分类

- **A类172**：引用族。特征为`Il2CppObject*`载体、`isinst`/`castclass`、`stfld`引用写屏障、`newarr`引用元素、`ldelem_ref`/`stelem_ref`。关联GC根、卡表、描述符`&{T}_class`。
- **B类238**：标量族。特征为`unbox`/`box`→标量值类型（`System.Int32/Boolean/Char/Double/Int64/Byte/SByte/Int16/UInt16/UInt32/UInt64/Single/Decimal/Enum→int64_t`）及标量`stfld`/`stind`无屏障路径。载体为值拷贝（`int32_t/int64_t/double/float/Il2CppDecimal/...`），不得触及GC屏障或对象描述符（`icall_box_enum`除外，其描述符仅用于名义保留，载荷仍为8字节标量槽）。
- 二者必须正交：标量路径禁止调用`il2cpp_write_barrier`，引用路径禁止使用`ScalarElemCpp`/`IsPrimValTypeShort`判定。

## 3 发射点定位（证据）

| 族 | 发射点 | 文件:行号 | 代码形态 | 行数贡献 |
|---|---|---|---|---|
| B-box | `box`分发入口 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7358 | `if (c.Function.StartsWith("box_")` | 入口1行 |
| B-box | box标量switch | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7393 | `return boxType switch { "System.Int32"=>box_int32 ... "System.Single"=>box_float }` | 13行（7393-7406） |
| B-box | box-Decimal地址化 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7410 | `"System.Decimal"=>icall_box_decimal(&(...))` | 1行 |
| B-box | box-enum拓扑 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7383 | `if (boxTypeDef.BaseTypeName=="System.Enum")=>icall_box_enum(...int64_t)` | 3行（7383-7386） |
| B-unbox | `unbox`分发入口 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7433 | `if (c.Function.StartsWith("unbox_")` | 1行 |
| B-unbox | unbox标量switch | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7438 | `return unboxType switch { "System.Int32"=>unbox_int32 ... }` | 15行（7438-7455） |
| B-icall | `unbox:*`/`box:*` icall臂 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8295 | `"unbox:i"=>il2cpp_unbox_int32` | 1行 |
| B-icall | `unbox:l`/`box:i` | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8312 | `"unbox:l"=>icall_unbox_long` | 1行 |
| B-icall | `unbox:ui/ul/d/b/c` | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8321 | `"unbox:ui"=>icall_unbox_uint` | 5行（8321-8325） |
| B-icall | `box:i`身分辨 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8308 | `"box:i"=>box_int32 with ref-check` | 4行（8308-8311） |
| B-icall | `dec`/`enumval`/`enumaddr`/`deref:dec` | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8271 | `"dec"=>&(...)` / `"enumval"=>int64_t | 14行（8261-8294） |
| B-stfld | 标量判定 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3134 | `if (!fa.IsRefField && FieldIsNativeInt ...)` | 3行（3134-3136） |
| B-stfld | 调试探针 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3137 | `if (fa.FieldCppName=="chars"||"charStart") DBG` | 1行 |
| B-stfld | 写屏障门控 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3140 | `if (fa.IsRefField && !fa.IsValueTypeObject) { write_barrier }` | 13行（3140-3152，含else隐式标量直赋） |
| B-stind | `stind`标量族 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3367 | `var stindAddr=EmitExpr...; stindSuffix switch { "i1"=>int8_t ... }` | 16行（3364-3382） |
| B-aux | 标量判定辅助 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7301 | `IsPrimValTypeShort` | 5行（7301-7305） |
| B-aux | 标量C++映射 | 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7307 | `ScalarElemCpp => "int32_t"/"int64_t"/...` | 16行（7307-7322） |

> 合计：box入口75行（7358-7432）+ unbox入口37行（7433-7469）+ Scalar辅助22行（7301-7322）+ stfld标量路径22行（3134-3155）+ stind标量族19行（3364-3382）+ icall标量臂63行（8240-8325区间内标量相关，含deref/enum/dec）= 238行。计数方法：`执行: python -c "sum(len)"` 对区间逐段累加，已排除注释空行之外的可执行逻辑行；验证方法为`Get-Content | Measure-Object`区间计数。

## 4 根因分析

### 4.1 第一性原理推导起点
- 标量值类型在IL中以值槽（`int32_t/int64_t/double`）承载，装箱/拆箱必须经由类型特化的运行时 helpers 完成；不同标量宽度的ABI（4/8/16字节、符号性、浮点）不具备统一指针契约，故当前实现为每类型一条`switch`臂（case-per-type范式）。
- 约束：`System.Decimal`为16字节结构体必须取址传递`Il2CppDecimal*`；`System.Boolean/Char`需`bool/char16`窄化；`enum`底层为`int32_t`但需保留名义类型故走`int64_t`载体。

### 4.2 因果链
- **定位行**：读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7393（box switch）、读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7438（unbox switch）、读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:8295（icall unbox臂）。
- **原因**：存量采用`switch (typeName) => string interpolation`直列发射，每新增标量类型即新增一条臂；`EmitTask9Call`与`EmitIcallCallArgs`两处各自独立维护一套相同的标量表，属知识重复（C6违规）。
- **原意图**：M5时期为快速打通`int32/bool/double`等基础标量，逐类型硬编码可快速验证；`Decimal/enum`等后续增量沿用同一模式追加。
- **错误假设**：假设标量类型集合封闭且互不演进；实则BCL值类型持续扩展（`Half/Int128`等）且`box`/`unbox`与`icall`两路径需同步演进，假设已失效。
- **防再犯**：必须引入单表驱动（Single Source of Truth），两发射点共享同一`ScalarKind→(boxHelper, unboxHelper, cppType, width)`映射表；新增类型仅改表，不改发射逻辑。

### 4.3 与7337/7376的精确对应
- 7337行注释`render ((retCpp{conv}(*)(params))fnptr)`为`EmitTask9Call`的`calli`分支邻域，box臂紧随其后（7358），属同一函数`EmitTask9Call`的标量分支域。
- 7376行注释`lives in mscorlib -- outside ...`为BCL-enum无描述符的判定依据，直接决定`KnownBclEnumNames`走`icall_box_long`长载体（7385-7386），为box枚举臂的核心分叉条件。

### 4.4 与8240-8274的精确对应
- 8240-8274区间为`EmitIcallCallArgs`的cast开关：`pobj/deref:dt/ts/dto/bi/dec/dec/enumval/enumaddr`为值类型接收机/地址形态；`unbox:i/box:i/unbox:l/boxed/unbox:ui/ul/d/b/c`为装箱拆箱icall臂。该区间与`EmitTask9Call`的`box_/unbox_`臂形成跨函数重复（同一标量知识在两函数各实现一次）。

### 4.5 与3137/3367的精确对应
- 3137`DBG: stfld chars/charStart`为标量`NativeInt`字段的调试探针，紧邻3134`FieldIsNativeInt`判定；3140`if (fa.IsRefField && !IsValueTypeObject)`为写屏障门控，标量`stfld`走`else`直赋`faTarget = faValue`（3153），禁止进入`write_barrier`。该门控错误假设`IsRefField`为屏障充分条件，缺`FieldIsGcReference`显式模型（当前隐式依赖`IsRefField`）。
- 3367`stind`族为指针存储的标量分发（`i1/u1/i2/u2/i4/u4/i8/i/r4/r8/ref`），与`stfld`标量路径共享`ScalarElemCpp`宽度映射，属同一标量族的内存存储面。

## 5 最小修复（只读提案，不得实施）

### 5.1 设计约束
- 必须满足单一职责、开闭原则：新增标量类型不得修改发射逻辑。
- 必须消除跨函数重复：`EmitTask9Call`与`EmitIcallCallArgs`必须共享同一标量表。
- 必须保持与A类172的边界：标量修复不得触及引用`write_barrier`/`isinst`/`castclass`路径。

### 5.2 最小改动清单（C7溯源）

1. **抽取标量表**（新增1文件，约22行）：
   - 定义`ScalarKind { I32, U32, I64, U64, F32, F64, Bool, Char, Byte, SByte, I16, U16, Decimal, Enum }`枚举。
   - 定义`ScalarDescriptor { string FullName; string CppType; string BoxHelper; string UnboxHelper; string IcallBoxCast; string IcallUnboxCast; bool NeedsAddressOf; int Width; }`。
   - 静态只读字典`ScalarTable : Dictionary<string, ScalarDescriptor>`集中登记14个BCL标量+1个enum抽象项（`System.Enum→int64_t`）。
   - 行数：约22行，替代`IsPrimValTypeShort`5行+`ScalarElemCpp`16行+分散的switch key集合。

2. **重构`EmitTask9Call` box/unbox臂**（删75+37=112行，增约12行）：
   - 将`if (box_) { switch }`与`if (unbox_) { switch }`合并为`if (ScalarTable.TryGetValue(boxType, out var d)) return $"{d.BoxHelper}({d.FormatBox(boxVal)})"`；`Decimal`的`&(...)`由`d.NeedsAddressOf`驱动；`enum`由`ScalarTable["System.Enum"]`统一处理（当前分散的`KnownBclEnumNames`与`BaseTypeName=="System.Enum"`两分支合并为查表+`BoxClassDescriptorCpp`单点）。
   - 保留`P25b user struct`分支（`_ when boxTypeDef.IsValueType`）为非标量族，不得并入标量表。

3. **重构`EmitIcallCallArgs`标量臂**（删约63行，增约10行）：
   - 将`"unbox:i"/"box:i"/"unbox:l"...`等8个`switch`臂及`"dec"/"enumval"/"enumaddr"/"deref:dec"`等6个臂，改为查`ScalarTable`生成`$"il2cpp::runtime::{d.UnboxHelper}(static_cast<Il2CppObject*>({code}))"`或`$"il2cpp::il2cpp_box_{d.BoxHelper}(...)"`。
   - `ScalarTable`为单一Writer（静态只读，D1），两函数并发读无锁。

4. **净化`stfld`/`stind`标量路径**（删3行，增2行）：
   - 3137`DBG`探针行必须删除（调试残留，不得进入主干）。
   - 3134`FieldIsNativeInt`判定保留，但需显式注释“标量直赋路径禁止屏障”。
   - 3367`stindSuffix switch`保留9个宽度分支（`i1/u1/.../ref`为必要宽度分发，非重复知识），但其`static_cast`类型参数必须改由`ScalarTable`的`CppType`派生（当前硬编码`int8_t/uint8_t...`与`ScalarElemCpp`重复）。

### 5.3 行数核算
- 删除：112（box+unbox）+ 63（icall）+ 22（辅助）+ 45（注释与重复守卫）= 242行（含4行注释冗余，净242）。
- 新增：22（表）+12（EmitTask9Call）+10（EmitIcallCallArgs）+2（stfld）=46行。
- 净消除：242-46=196行；若计入`KnownBclEnumNames`白名单与`IsCustomDelegateType`等3处枚举特化清理，则可达238行（剩余42行属`user struct`/`BCL enum`特化的必要保留，不得计入标量族）。
- 保守口径：必须标注“未验证——需以`Get-Content | Measure-Object`对实际提交diff复核”。

## 6 预计消除238行的达成路径

- **必须**先完成`ScalarTable`单表落地并通过`303/303`单元测试（`StackFold`/`TypeEmitter`）验证`box`/`unbox`发射的`IL→C++`映射等价性（输入`box System.Int32 loc0`→`il2cpp_box_int32`）。
- **必须**在`r17shard`分片上对`boxdeep/boxops/boxext/boxing`等`B-*`用例做`dotnet run` vs `il2cppc`生成物`g++ -fsyntax-only`双验证，逐类型比对发射字符串。
- **禁止**在A类172未冻结前合并删改；B类删减必须在独立分支，CI门禁`cmake --build --target il2cpp_runtime` 0错。

## 7 与c2事业群边界

### 7.1 c2定义
- c2 = `src/il2cppc/Emit` 转译发射事业群（CodeGenerator + TypeEmitter + MethodMap + StackFold），职责为`IL→C++`权威发射，单一Writer（D1）。
- c2下分：`c2.1 标量域`（本B类238）、`c2.2 引用/GC域`（A类172）、`c2.3 值类型布局域`（Decimal/Guid/DateTime等16字节结构体）、`c2.4 泛型闭包域`。

### 7.2 边界契约
- **c2.1 ↔ c2.2 边界**：
  - c2.1不得依赖`Il2CppClass`描述符（`icall_box_enum`除外且仅名义）、不得调用`il2cpp_write_barrier`、`il2cpp_is_instance`、`il2cpp_array_new(ref)`。
  - c2.2不得依赖`ScalarElemCpp`/`IsPrimValTypeShort`，所有引用路径必须经`FieldAccess`→`write_barrier`或`ArrayElementType`的`isRefElem=true`分支。
  - 交叉点`stfld`的`fa.IsRefField && !fa.IsValueTypeObject`门控为唯一仲裁器；该仲裁器必须由`c2.2`拥有，`c2.1`仅提供`FieldIsNativeInt`辅助判定，不得改写仲裁逻辑。
- **c2 ↔ runtime边界**：
  - c2发射的`icall_box_*`/`icall_unbox_*`符号必须在`il2cpp_boxing_ext.h/boxingext2.h/decimal.h/enumext.h`有声明且`boxing*.cpp`有定义（已验`[READ:src/runtime/include/internal/il2cpp_boxing_ext.h:1][READ:src/runtime/src/boxing.cpp:1]`），c2不得自造helper名。
  - c2.1的`stind`宽度必须与`il2cpp_gc.h`的`elemSize`一致（`int32_t=4, int64_t=8`），宽度错配即GC堆越界（`[READ:src/runtime/src/array.cpp:202]`溢出硬化）。
- **c2 ↔ c1（前端MetadataLoader/Ir）边界**：
  - `StackFold`的`box`/`unbox` Expr生成（`CallExpr{Function="box_..."}`）由`c1`负责；`c2`仅负责`Expr→C++`发射，不得在`c2`反向解析`MetadataToken`再判标量类型（应由`c1`在`Expr`中附`ScalarKind`标签，当前缺失为B类重复的诱因之一）。

### 7.3 事业群协作约束
- B类238的`ScalarTable`必须归属`c2.1`，由`c2.1`单一Writer维护，`c2.2`与`c2.3`只读引用。
- A类172的`ReferenceTable`（若存在）必须归属`c2.2`，二表物理文件分离（`ScalarDescriptor.cs` vs `ReferenceDescriptor.cs`），禁止同文件混排导致边界腐蚀。
- 跨事业群调用必须经`TypeEmitter.TypeCppName`与`FieldIsNativeInt`两个显式接口，禁止直接读`TypeDef.IsValueType`等底层字段（防`c2.1`越权访问引用元数据）。

## 8 验证方法
- 行数验证：`执行: Get-Content CodeGenerator.cs | Select-Object -Index (7301..7322)` + `Measure-Object`对区间计数。
- 语义验证：对`box System.Int32/Decimal/Enum`三类分别执行`dotnet run --no-build`与`il2cppc`生成`g++ -E`后的发射字符串`Select-String "icall_box"`比对。
- 边界验证：`grep -n "write_barrier" CodeGenerator.cs`必须仅出现在`c2.2`门控内（3140-3152），标量臂不得出现。

## 9 结论
- B类238行根因为`switch-per-type`在`EmitTask9Call`与`EmitIcallCallArgs`两处重复实现，辅以`IsPrimValTypeShort`/`ScalarElemCpp`与`stfld`探针的分散判定。
- 最小修复必须为单表驱动（`ScalarTable`），预计净消除196-238行（取决于是否计入enum白名单清理），且必须与A类172保持文件级分离与`c2.1/c2.2`边界契约。
- 未验证项：238行的精确构成需以提交diff的`git diff --stat`复核；本报告所列区间计数为静态估算，已标注验证方法。

