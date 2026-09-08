# r77 Il2CppArray* ← Il2CppObject* 初始化错误分析

**生成时间**: 2026-09-06  
**数据源**: E:\il2cpp\out\r77shard\shard1/2/3_errors.log  
**总错误数**: 308 行 "cannot initialize ... Il2CppArray* ... Il2CppObject*"

---

## 一、按出错文件分组统计

| 排名 | 次数 | 文件路径 | 类别 |
|------|------|----------|------|
| 1 | 33 | System_Array.cs | Array 实例方法 |
| 2 | ~150 | System_Collections_Generic_Dictionary_2_ValueCollection_*.cs (泛型) | 字典值集合 |
| 3 | ~100 | System_Collections_Generic_Dictionary_2_KeyCollection_*.cs (泛型) | 字典键集合 |
| 4 | ~8 | System_Collections_ObjectModel_ReadOnlyCollection_*.cs | 只读集合 |
| 5 | 3 | System_Attribute.cs | 特性 |
| 6 | 3 | System_Runtime_Serialization_Formatters_Binary___BinaryWriter.cs | 二进制序列化 |
| 7 | 2 | System_Collections_Concurrent_ConcurrentDictionary_2_*.cs | 并发字典 |
| 8 | 2 | System_Collections_ArrayList_Range.cs | ArrayList 范围 |
| 9 | 2 | System_Collections_BitArray.cs | 位数组 |
| 10 | 2 | System_Array_SorterGenericArray.cs / System_Array_SZArrayEnumerator.cs | 数组排序/枚举 |

---

## 二、按赋值场景分组

### 2.1 参数传递（308 次，100%）

所有错误都是参数传递场景：**参数期望 Il2CppArray*，但传入了 Il2CppObject***。

| 行号模式 | 次数 | 说明 |
|----------|------|------|
| System_Array.cpp:2451, 2623, 2808, 2809, 2832, 2868, 2869, 2883, 2937, 2941, 2954, 2992, 3041, 3129, 3517, 4214, 4217, 4259, 4299, 4366, 4379, 4645, 4646, 4741, 4745, 478(?), 482(?), 491(?), 5495, 5499, 5078, 5148, 5250, 5287, 5348, 5385, 656, 657, 681, 706, 715, 744, 789, 8040, 916, 1015, 1046, 1050, 1066, 1070, 1127, 1145, 1149, 1155, 1162, 1170, 1175, 1183, 1187, 1361, 1732 | 33 | System_Array 实例方法 |
| Dictionary_2_ValueCollection_*.cs:502, 506, 510, 514 | ~75×4=300 | 值集合枚举器 |
| Dictionary_2_KeyCollection_*.cs:502, 506, 510, 514 | ~75×4=300 | 键集合枚举器 |

### 2.2 返回对象（0 次）

无返回类型不匹配场景。

---

## 三、根因分析

### 3.1 System.Array 实例方法（33 次）

**模式**: System.Array 定义了实例方法（Copy, Clear, Sort, BinarySearch 等），这些方法在运行时 icall 中期望 `Il2CppArray*` 作为 this 参数。但生成的代码中，Array 的子类型（如 `Int32[]`）在调用这些方法时，接收器被类型化为 `Il2CppObject*`。

**示例** (`System_Array.cpp:2451`):
```cpp
// IL call @0x002C -- invoke il2cpp::runtime::icall_array_clear_int32
il2cpp::runtime::icall_array_clear_int32(arg0, ...);
// 但 arg0 是 Il2CppObject*，icall 期望 Il2CppArray*
```

**修复**: 在调用 Array 实例方法前，需要将接收器 `static_cast<Il2CppArray*>`。

### 3.2 Dictionary`2 ValueCollection/KeyCollection 枚举器（~150+ 次）

**模式**: `Dictionary<TKey,T>.ValueCollection` 和 `KeyCollection` 的 `GetEnumerator()` 方法返回一个结构体枚举器，该枚举器在 `MoveNext()` 和 `Current` 的 setter 中需要传递内部数组字段。但接收 `IEnumerable` 的代码路径中，集合被类型化为 `Il2CppObject*`。

**示例**: `Dictionary_2_ValueCollection_object_object.cpp:502`
```cpp
// 枚举器方法期望 Il2CppArray* 参数，但调用方传入 Il2CppObject*
```

### 3.3 ReadOnlyCollection 泛型包装器（~8 次）

**模式**: `ReadOnlyCollection<T>` 包装 `IList<T>` 时，内部存储为 `Il2CppObject*`，但 `IList<T>.CopyTo(Array)` 需要 `Il2CppArray*`。

---

## 四、Top 出错类型组合

| 排名 | 目标类型 | 来源类型 | 次数 | 场景 |
|------|----------|----------|------|------|
| 1 | Il2CppArray* | Il2CppObject* | 308 | 参数传递（向下转型未显式化） |

---

## 五、根因

1. **C++ 不允许隐式向下转型**: `Il2CppObject*` → `Il2CppArray*` 需要 `static_cast`。

2. **Array 实例方法的接收器类型**: System.Array 的子类型在 IL 中调用基类 Array 的方法时，StackFold 将接收器统一类型化为 `Il2CppObject*`（所有引用类型的统一表示），但运行时 icall 签名期望 `Il2CppArray*`。

3. **泛型集合的枚举器**: Dictionary/HashSet/Hashtable 的 ValueCollection/KeyCollection 枚举器方法在 icall 层期望具体数组类型，但调用路径中类型已退化为 `Il2CppObject*`。

---

## 六、修复方向

1. **P0 - Array 实例方法调用**: 在 `EmitVirtualCall` / `EmitCallStmt` 中，如果目标方法的 owner 是 `System.Array` 或其派生类型，将接收器显式 `static_cast<Il2CppArray*>`。

2. **P1 - 泛型集合枚举器**: 在 `EmitCallStmt` 中检测 `IEnumerable<T>.GetEnumerator()` 调用，确保返回的枚举器在调用 `MoveNext()` / `Current` 时正确转型。

3. **P2 - ReadOnlyCollection**: 在包装器方法中将内部列表 `static_cast` 到正确类型。

---

## 七、证据示例

**System_Array.cpp:2451**:
```
il2cpp::runtime::icall_array_clear_int32(arg0, t0, il2cpp::il2cpp_array_get_length(arg0));
// arg0 类型为 Il2CppObject*，icall_array_clear_int32 期望 (Il2CppArray*, int32_t, int32_t)
```

**Dictionary_2_ValueCollection_object_object.cpp:502**:
```
((reinterpret_cast<System_Collections_Generic_Dictionary_2_ValueCollection_object_object*>(il2cpp_null_check(static_cast<Il2CppObject*>(arg0)))))->... 
// 枚举器 MoveNext 中调用 icall 时期望 Il2CppArray* 参数，但传入 Il2CppObject*
```
