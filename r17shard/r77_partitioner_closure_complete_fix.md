# r77 Partitioner`1 闭锁类错误修复方案

生成时间: 2026-09-06  
分析范围: mscorlib_gen_r77 全部 Partitioner 相关错误  
错误总数: ~71 (分属 16 类独立根因)

---

## 一、错误分类总览

| # | 根因 | 涉及文件 | 错误数 |
|---|------|----------|--------|
| 1 | 2-arity 泛型实例未克隆 | `OrderablePartitioner`1<System.Tuple`2<..>>`, `DynamicPartitionerForIndexRange_Abstract`2<..>` 等 | ~18 |
| 2 | `KeyValuePair`2<long,int>` 类/接口未克隆 | `IEnumerator`1<KeyValuePair`2<long,int>>`, `IEnumerator`1<KeyValuePair`2<long,int>>` | ~8 |
| 3 | `StaticIndexRangePartitioner`2<..>` 的 2-arity 泛型实例未生成 | `StaticIndexRangePartitionerForArray`1<int>`, `StaticIndexRangePartitionerForIList`1<int>` | ~12 |
| 4 | `DynamicPartitionEnumeratorForIndexRange_Abstract`2<..>` 2-arity 实例缺失 | 多文件 | ~8 |
| 5 | `DynamicPartitionEnumerator_Abstract`2<int,IEnumerator`1<int>>` 缺失 | `DynamicPartitionerForIEnumerable`1<int>` 等 | ~6 |
| 6 | `int32_t` vs `Il2CppObject*` 类型桥接 | 多处 (arg-type mismatch) | ~10 |
| 7 | `DisplayClass42_0_2<int,int>` vs `DisplayClass42_0_2<int>` 命名分叉 | `Parallel.__c__DisplayClass42_0_2` 的 Partitioner 方法 | ~9 |

---

## 二、根因详细分析

### 根因 1：2-arity 泛型实例未克隆

**症状（典型错误）**
```
error: use of undeclared identifier 'System_Collections_Concurrent_Partitioner_1_System_Tuple_2_int_int__ctor_91c461aebebdb8b7';
did you mean 'System_Collections_Concurrent_Partitioner_1_int__ctor_91c461aebebdb8b7'?
```

**生成代码引用位置**
- `System_Collections_Concurrent_OrderablePartitioner_1_System_Tuple_2_int_int.cpp:90`
- `System_Collections_Concurrent_OrderablePartitioner_1_System_Tuple_2_long_long.cpp:90`
- `System_Collections_Concurrent_Partitioner_StaticIndexRangePartitionerForArray_1_int.cpp:85`
- `System_Collections_Concurrent_Partitioner_StaticIndexRangePartitionerForIList_1_int.cpp:85`

**根因**
R4/R5 fixpoint（`ExpandOpenInstances` + `MetadataLoader.ExpandNestedTypes`）在首轮克隆时只注册了单实参闭包（如 `Partitioner`1<int>` / `Partitioner`1<long>`）。对于嵌套在闭包方法内的 2-arity 泛型引用（如 `OrderablePartitioner`1<System.Tuple<int,int>>`），克隆侧未推导出需求闭包，导致只有默认 `Partitioner`1_int` 被生成而 `Partitioner`1_System_Tuple_2_int_int` 从未克隆。

**核心矛盾**：`MetadataLoader` 目前仅按 1-泛型实参进行闭包推导，2-参数泛型实例需要工作清单迭代收敛才能发现。

### 根因 2：`KeyValuePair`2<long,int>` 类/接口未克隆

**症状**
```
error: use of undeclared identifier 'System_Collections_Generic_IEnumerator_1_System_Collections_Generic_KeyValuePair_2_long_int_class';
did you mean 'System_Collections_Generic_IEnumerator_1_System_Collections_Generic_KeyValuePair_2_int_object_class'?
```

**根因**
`StaticIndexRangePartitioner` / `DynamicPartitionerForIList` 等方法返回 `KeyValuePair<long,TElement>`。但工作清单推导以 `int` 作为闭包默认实参，导致 `KeyValuePair`2<long,int>` 实例被当成了 `KeyValuePair`2<long,object>` 或完全漏掉，生成器只注册了 `KeyValuePair`2_int_object` 的 IEnumerator 类。

### 根因 3：2-arity 基类泛型实例缺失

**症状**
```
error: use of undeclared identifier 'System_Collections_Concurrent_Partitioner_StaticIndexRangePartitioner_2_int_int__ctor_f1c58f886ae60f11';
did you mean 'System_Collections_Concurrent_Partitioner_StaticIndexRangePartitioner_2_int__ctor_f1c58f886ae60f11'?
```

**根因**
`StaticIndexRangePartitioner`2<int,int>` 与 `StaticIndexRangePartitioner`2<int>` 是不同的闭包类型。当前 R4 派生算法未处理"泛型实参本身是泛型实例且部分闭合"的情况——派生工作清单只展开到第一层 VAR，遗漏了嵌套 VAR 的第二参数闭合。

### 根因 4：`int32_t` vs `Il2CppObject*` 类型桥接

**症状**
```
error: cannot initialize a parameter of type 'int32_t' (aka 'int') with an lvalue of type 'Il2CppObject *'
error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' (aka 'int')
```

**根因**
调用点侧传 `int` 但声明侧签名被推断为 `Il2CppObject*`（或反之）。是根因 1-3 的**派生效应**：因为缺少真实闭包类，生成器把局部/参数类型推断回退到默认 `int`/`object` 桥接。

### 根因 5：`DisplayClass42_0_2<int,int>` 命名分叉

**症状**
```
error: use of undeclared identifier 'System_Threading_Tasks_Parallel___c__DisplayClass42_0_2_int_int__PartitionerForEachWorker_b__0_cab179655028cbaa';
did you mean 'System_Threading_Tasks_Parallel___c__DisplayClass42_0_2_int__PartitionerForEachWorker_b__0_cab179655028cbaa'?
```

**根因**
`Parallel.PartitionerForEachWorker<TLocal>` 是一个带两个泛型参数的闭包方法，显示类 `<>c__DisplayClass42_0<T,TLocal>` 生成了 `_int` 单参数版本（只有一个闭包实参被替换），但方法体内引用了双参数版本。命名哈希冲突导致双参数版从未独立生成。

---

## 三、修复方案

### 方案 A：工作清单推导 2-arity 泛型闭包（根治根因 1、2、3）

**文件**: `src/il2cppc/MetadataLoader.cs`

**机制**
R5 fixpoint 当前只扫描 `MemberRef` 的 `TypeSpec` 父。需扩展 `ScanSignaturesForInsts` / `ExpandOpenInstances` 闭包派生：

1. **双层 VAR 派生**：当扫描到一个方法体包含 GenericInst 类型参数时，除了加入单参数实例（`<int>`），必须也加入所有嵌套 VAR 组合（如 `<Tuple<int,int>, int>`）。
2. **泛型实参组合展开**：对 `Partitioner<T>` 这类 1-arity 类型，其闭包方法内若引用了 `Partitioner<U,V>` 的 2-arity 实例，派生时按闭包实际泛型参数组合枚举。
3. **迭代收敛**：do-while 循环直到工作清单不再新增（当前 R4 只对 `Dictionary`2` 等派生 2-arity 组合，未扩展到所有 BCL 泛型类）。

**验证**
```
mscorlib_gen_r77 应新增：
  - Partitioner_1_System_Tuple_2_int_int
  - Partitioner_1_System_Tuple_2_long_long
  - OrderablePartitioner_1_System_Tuple_2_int_int
  - OrderablePartitioner_1_System_Tuple_2_long_long
  - StaticIndexRangePartitioner_2_int_int
  - DynamicPartitionerForIndexRange_Abstract_2_int_int
  - DynamicPartitionerForIndexRange_Abstract_2_int_System_Collections_Generic_IList_1_int
  - DynamicPartitionEnumerator_Abstract_2_int_System_Collections_Generic_IEnumerator_1_int
  - IEnumerator_1_System_Collections_Generic_KeyValuePair_2_long_int
  - 等 ~12 个新类型
```

### 方案 B：`<>c__DisplayClass` 多泛型参数命名分叉（根治根因 5）

**文件**: `src/il2cppc/MetadataLoader.cs` (ClosureName / ClosureShortKeys 派生)

**机制**
当前 `ClosureShortKeys` 只按 outer type 去重。需要把泛型参数数量纳入闭包短名计算：

- `<>c__DisplayClass42_0<T>` → `_int`
- `<>c__DisplayClass42_0<T,TLocal>` → `_int_int`（独立去重槽）

**代码位置**
`MetadataLoader.cs:218-300` 附近的闭包短名派生逻辑，需检查 `ClosureShortKeys` 键是否包含 `GenericArity` 字段。

### 方案 C：类型桥接（根治根因 4，依赖 A+B）

根因 4 是 A+B 的派生效应。一旦 A+B 落地，多数 `int32_t` vs `Il2CppObject*` 错误将自动消失（因为真实闭包类生成后，调用点类型推断会命中正确声明）。

若仍有残余桥接问题，在 `CodeGenerator.cs` 的 `EmitCall` 中增加 int-object reinterpret_cast 兜底（参照 `IsIntThis` 模式）。

---

## 四、实施顺序

1. **P0 - 方案 B**：`ClosureShortKeys` 增加 `GenericArity` 区分（小改，低风险，立即降 9 错）
2. **P1 - 方案 A**：工作清单 2-arity 泛型推导（大改，高收益，预计降 ~40 错）
3. **P2 - 方案 C**：残余桥接兜底（仅 A+B 后仍有残留时启用）

---

## 五、风险评估

- **方案 A** 是中高风险的 fixpoint 扩展。若工作清单收敛控制不好，可能克隆爆炸（参考 R7 的 b0 爆炸历史）。建议加深度上限（3层）和类型总数硬门。
- **方案 B** 风险极低，只影响命名哈希键。
- **方案 C** 是保守兜底，只在 A+B 后启用。

---

## 六、预期收益

实施 A+B+C 后，预计 mowershell_gen_r78 可消除全部 71 个 Partitioner 类错误，为 r78 整体 fail 数降至 ~1400 以下奠定基础。

---

*分析完成。等待用户确认方向后派发子代理实施。*
