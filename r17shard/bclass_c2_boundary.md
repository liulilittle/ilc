# c2事业群边界划分（B类标量族238 vs A类引用族172）

## 1 c2事业群定义

- **c2** = `src/il2cppc/Emit` 编译后端发射事业群，权威Writer为`CodeGenerator.Generate`（D1单写者，持`_poolLock`）。
- 子域：
  - `c2.1 标量域`：B类238行，职责`box/unbox→标量`、`stfld/stind`标量直赋、`ScalarElemCpp`宽度映射。
  - `c2.2 引用/GC域`：A类172行，职责`isinst/castclass`、`stfld`引用写屏障、`newarr`引用元素、`ldelem_ref/stelem_ref`。
  - `c2.3 值类型布局域`：`Decimal(16B)/Guid(16B)/DateTime(8B)/BigInteger`等结构体定义与`TypeEmitter.EmitClassStruct`。
  - `c2.4 泛型闭包域`：`MetadataLoader.CloneClosedType`与`MethodRefResolver.GenericInstantiationName`。

## 2 边界图

```
c1 (MetadataLoader/StackFold/Ir) ── CallExpr{box_/unbox_/isinst_/castclass_/stfld/fa} ──> c2
                                                                                 │
                                          ┌────────────────────────────────────┼────────────────────────────────────┐
                                          │                                    │                                    │
                                        c2.1 标量                         c2.2 引用/GC                        c2.3 布局
                                    (B 238)                            (A 172)                         (Decimal/Guid)
                                          │                                    │                                    │
                                    ScalarTable                          ReferenceTable                    TypeEmitter
                                    box_int32/bool/...                 write_barrier/is_instance         struct {int32}
                                          │                                    │                                    │
                                          └─────────────────┬──────────────────┘                                    │
                                                            │ FieldAccess仲裁器 (fa.IsRefField)                      │
                                                            ▼                                                      │
                                                      C++生成物 (generated.h/.cpp) ──> runtime (boxing*.cpp/gc.cpp/array.cpp)
```

## 3 契约表

| 维度 | c2.1 标量（B）禁止 | c2.1 标量（B）必须 | c2.2 引用（A）禁止 | c2.2 引用（A）必须 |
|---|---|---|---|---|
| 符号 | 禁止调用`il2cpp_write_barrier`、`il2cpp_is_instance` | 必须调用`icall_box_*`/`icall_unbox_*`（`il2cpp_boxing_ext.h`） | 禁止调用`ScalarElemCpp`/`IsPrimValTypeShort` | 必须调用`il2cpp_write_barrier(reinterpret_cast<void**>(...), static_cast<Il2CppObject*>...)` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3150 |
| 类型 | 禁止依赖`&{T}_class`（user struct除外，`icall_box_enum`名义除外） | 必须依赖`ScalarDescriptor.CppType`（`int32_t/int64_t/double/float/Il2CppDecimal`） | 禁止依赖`int32_t`等标量C++类型作为对象载体 | 必须依赖`Il2CppObject*`/`Il2CppArray*`/`void**` |
| 内存 | 禁止经`il2cpp_array_new(..., isRef=true)` | 必须经标量`stind`宽度分发（`i1/u1/i2/u2/i4/u4/i8/r4/r8`）读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3369 | 禁止经标量`stind`宽度写引用槽（`ref`除外） | 必须经`stelem_ref`+`write_barrier_array_range`（`gc.cpp:1306`） |
| 元数据 | 禁止在c2解析`MetadataToken`二次判类型（应由c1附`ScalarKind`） | 必须查`ScalarTable`单表 | 禁止查`ScalarTable` | 必须查`ArrayElementType`/`IsCustomDelegateType` |
| 并发 | 无状态，只读表，并发读安全 | 表静态只读，D1 | 需持`_poolLock`写`_delegateMethodTokens` | 单写者 |

## 4 关键仲裁器

- **唯一仲裁器**：`fa.IsRefField && !fa.IsValueTypeObject` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3140
  - 真分支 → c2.2（写屏障+`FieldAccess`对象槽`&->Next`的`Node**→void**` reinterpret，M7修复）。
  - 假分支 → c2.1（`faTarget = faValue`直赋，`faValue`由`FieldIsNativeInt`决定是否`reinterpret_cast<intptr_t>`）。
- 该仲裁器必须由c2.2拥有，c2.1仅提供`FieldIsNativeInt`辅助，不得改写仲裁条件；否则标量直赋误入屏障或引用写漏屏障（GC漏扫）。

## 5 跨群调用约束

- **c2.1→c2.3**：通过`ScalarDescriptor.NeedsAddressOf`决定`Decimal`取址`&(...)`（`Il2CppDecimal*`），不得直接操作`TypeEmitter.FindEmbeddedValueType`。
- **c2.1→runtime**：仅允许`il2cpp::runtime::icall_box_*`/`icall_unbox_*`与`il2cpp::il2cpp_box_int32/il2cpp_unbox_int32`（`boxing.cpp`基座）；`BigInteger/DateTime`等16/8字节值类型归c2.3，不得由c2.1发射。
- **c2.2→runtime**：仅允许`il2cpp::il2cpp_write_barrier`、`il2cpp::il2cpp_is_instance`、`il2cpp::il2cpp_array_new`（`isRef=true`）等GC/类型检查符号。
- **c1→c2**：`StackFold`产生的`CallExpr{Function="box_..."}`必须附`SignatureTypeCode`（当前仅`token`），c2不得再调`TypeTokenFullName`二次解析（重复知识根因）；修复后c1应附`ScalarKind`标签，c2查表即可。

## 6 与A类172的分离证据

- A类172的典型发射点：
  - `isinst_/castclass_` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7470（70行，7470-7540，含TypeSpec分支、delegate identity、box descriptor `class_by_name`）
  - `newarr`引用分支 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7562（`isRefElem ? ArrayElementClassCpp : nullptr`）
  - `stfld`引用屏障 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:3150（`write_barrier`）
  - `ldelem_ref` 读:E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs:7586（`ldelem_ref`）
- B类238的发射点与上述无交集（`grep "write_barrier" CodeGenerator.cs`仅命中3140-3152；`grep "ScalarElemCpp" CodeGenerator.cs`仅命中c2.1），二者在源码行号上物理分离（c2.1集中7301-7470与8240-8325，c2.2集中7470-7680与3139-3152），满足“独立调查”要求。

## 7 演进约束

- 新增标量类型（如`Half/Int128`）必须仅改`c2.1 ScalarTable`一行，禁止改`EmitTask9Call`与`EmitIcallCallArgs`逻辑（开闭原则）。
- 新增引用类型（如新GC引用字段）必须仅改`c2.2 ReferenceTable`，禁止触碰`c2.1`表。
- 跨域需求（如`Nullable<T>`的box-nullable）归`c2.4`泛型域，不得由`c2.1`或`c2.2`直接处理；当前`R11c`的`C族 Nullable-struct receiver box-nullable`即属此类，已在`process.md R11c`标注。

## 8 验证清单

- [ ] `grep -n "ScalarElemCpp\|IsPrimValTypeShort" CodeGenerator.cs` 命中行必须全部落在`c2.1`区间（7301-7470,8240-8325），`c2.2`区间（7470-7600,3139-3152）零命中。
- [ ] `grep -n "write_barrier\|is_instance" CodeGenerator.cs` 命中行必须全部落在`c2.2`区间，`c2.1`零命中。
- [ ] `ScalarTable`文件必须位于`src/il2cppc/Emit/ScalarDescriptor.cs`（新建），`ReferenceTable`（若有）必须位于`ReferenceDescriptor.cs`，二文件不得互引。
- 验证方法：`执行: Select-String -Pattern "ScalarElemCpp" -Path CodeGenerator.cs` 逐行核对区间。

