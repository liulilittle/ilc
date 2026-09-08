# r77 Dictionary_2 值集合/键集合枚举器 Il2CppArray* 转型错误

**生成时间**: 2026-09-06  
**数据源**: E:\il2cpp\out\r77shard\shard1/2/3_errors.log  
**关联分析**: E:\il2cpp\r17shard\r77_array_init_analysis.md  
**总错误数**: 128 行（Dictionary_2_ValueCollection 和 Dictionary_2_KeyCollection 中的 Il2CppArray* ← Il2CppObject*）

---

## 一、问题定位

### 1.1 错误模式

所有 128 个错误都发生在以下两种方法中：

| 方法 | 签名 | 错误次数 |
|------|------|----------|
| `System.Collections.ICollection.CopyTo` | `void(Il2CppObject* arg0, Il2CppObject* arg1, int32_t arg2)` | ~64 |
| `System.Collections.IEnumerable.GetEnumerator` | `Il2CppObject*(Il2CppObject* arg0)` 返回枚举器后调用 MoveNext/Current | ~64 |

### 1.2 根因

生成的 C++ 方法签名使用 `Il2CppObject*` 作为参数类型（所有引用类型的统一表示），但方法体内部调用的 icall 期望 `Il2CppArray*`：

```cpp
// 生成的方法签名（参数 arg1 类型为 Il2CppObject*）
IL2CPP_NOINLINE void System_Collections_Generic_Dictionary_2_ValueCollection_int_int_System_Collections_ICollection_CopyTo_(
    Il2CppObject* arg0, 
    Il2CppObject* arg1,  // ← 运行时是一个 SZArray，但类型是 Il2CppObject*
    int32_t arg2)
{
    // 方法体中调用 icall 时期望 Il2CppArray*
    if(il2cpp::runtime::icall_array_get_rank(arg1) == 1LL) ...  // ← 错误！
    // icall_array_get_rank 签名: int32_t il2cpp_array_get_rank(Il2CppArray*)
}
```

---

## 二、具体出错位置

### 2.1 System.Collections.ICollection.CopyTo 方法

**文件模式**: `System_Collections_Generic_Dictionary_2_ValueCollection_*.cpp`  
**行号**: 502, 506

**生成的代码** (以 `ValueCollection_int_int.cpp` 为例):
```cpp
IL2CPP_NOINLINE void System_Collections_Generic_Dictionary_2_ValueCollection_int_int_System_Collections_ICollection_CopyTo_3abf2956a7d6b52b(
    Il2CppObject* arg0, Il2CppObject* arg1, int32_t arg2)
{
    // ... 边界检查 ...
    
    // 行 502: 错误位置
    if((arg2 > il2cpp::il2cpp_array_get_length(arg1)) == 0LL) goto L_60;
    //                ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
    //                arg1 是 Il2CppObject*，但函数期望 Il2CppArray*
    
    // 行 506: 错误位置  
    t3 = il2cpp::il2cpp_array_get_length(arg1);
    //    ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
    //    同样的问题
}
```

**出错的 icall 调用链**:
- `il2cpp::runtime::icall_array_get_rank(Il2CppArray*)` — 期望 `Il2CppArray*`
- `il2cpp::runtime::icall_array_get_lower_bound(Il2CppArray*, int32_t)` — 期望 `Il2CppArray*`
- `il2cpp::il2cpp_array_get_length(Il2CppArray*)` — 期望 `Il2CppArray*`
- `il2cpp::runtime::icall_array_copy(Il2CppArray*, ...)` — 期望 `Il2CppArray*`

### 2.2 System.Collections.IEnumerable.GetEnumerator + 枚举器

**文件模式**: `System_Collections_Generic_Dictionary_2_ValueCollection_*.cpp`  
**行号**: 167 (GetEnumerator), 枚举器内部调用点

**GetEnumerator 返回枚举器**:
```cpp
IL2CPP_NOINLINE Il2CppObject* System_Collections_Generic_Dictionary_2_ValueCollection_int_int_System_Collections_Generic_IEnumerable_TValue__GetEnumerator_e1668aed241497d0(Il2CppObject* arg0)
{
    // ... 构造枚举器 ...
    // 返回的是 ValueCollection_Enumerator_int_int（结构体），需要装箱为 Il2CppObject*
    // 行 167: 错误 - 结构体不能直接转为 Il2CppObject*（缺少 boxing）
}
```

---

## 三、修复方案

### 方案 A：在枚举器方法入口处插入 static_cast（推荐）

在 `EmitCallStmt` 中检测到 `System.Collections.ICollection.CopyTo` 调用时，将 `arg1` 参数类型从 `Il2CppObject*` 改为 `Il2CppArray*`，或在方法体开头插入转型代码。

**修改位置**: `CodeGenerator.cs:770-780` (ParamTypesCpp 生成逻辑)

```csharp
// 在 ParamTypesCpp 中检测 System.Collections.ICollection.CopyTo
if (m.ParamTypes == "r:System.Array,i" && 
    m.Name == "System.Collections.ICollection.CopyTo")
{
    // 将第一个参数类型从 Il2CppObject* 改为 Il2CppArray*
    types.Add("Il2CppArray*");  // arg1 是 Array
    types.Add("int32_t");       // arg2 是 index
    return types.ToArray();
}
```

### 方案 B：在方法体开头插入转型（更通用）

在 `EmitBody` 中检测到 `Il2CppObject*` 参数需要作为 `Il2CppArray*` 使用时，在方法体开头插入：

```cpp
Il2CppArray* __arg1_array = static_cast<Il2CppArray*>(arg1);
```

然后将方法体中的 `arg1` 替换为 `__arg1_array`（仅 icall 调用点）。

### 方案 C：在虚拟调用接收器上插入 cast（针对 CopyTo）

在 `EmitVirtualCall` 中，如果目标方法的 owner 是 `System.Array` 或签名包含 `Il2CppArray*` 参数，将接收器/参数 `static_cast<Il2CppArray*>`。

---

## 四、修复实施建议

### 4.1 最小改动方案（方案 A）

**文件**: `CodeGenerator.cs`  
**函数**: `ParamTypesCpp(MethodDef m, MethodBodyInfo info)`  
**位置**: 行 7190-7231

```csharp
private static string[] ParamTypesCpp(MethodDef m, MethodBodyInfo info)
{
    var types = new List<string>();
    if (!m.IsStatic)
    {
        types.Add(m.Owner.IsValueType ? $"{TypeEmitter.TypeCppName(m.Owner)}*" : "Il2CppObject*");
    }
    
    // ===== 新增修复开始 =====
    // System.Collections.ICollection.CopyTo 签名是 (System.Array array, int index)
    // 但生成的方法参数是 (Il2CppObject* arg0, Il2CppObject* arg1, int32_t arg2)
    // arg1 实际是 Array，icall 期望 Il2CppArray*
    if (m.Name == "System.Collections.ICollection.CopyTo" && 
        m.ParamTypes == "r:System.Array,i")
    {
        types.Add("Il2CppArray*");  // arg1: array 参数
        types.Add("int32_t");       // arg2: index 参数
        return types.ToArray();
    }
    // ===== 新增修复结束 =====
    
    if (m.ParamTypes.Length > 0)
        foreach (var p in m.ParamTypes.Split(','))
            types.Add(p switch
            {
                // ... 原有逻辑 ...
            });
    return types.ToArray();
}
```

### 4.2 头文件同步修改

还需要修改 `generated.h` 中的方法声明（`ParamListTyped` 调用处），确保声明与定义一致。

---

## 五、受影响文件清单

| 文件模式 | 实例数 | 错误数/文件 | 总错误数 |
|----------|--------|------------|----------|
| `Dictionary_2_ValueCollection_TKey_TValue.cs` | ~32 | 2 | ~64 |
| `Dictionary_2_KeyCollection_TKey_TValue.cs` | ~32 | 2 | ~64 |
| **合计** | **~64** | - | **~128** |

---

## 六、验证方案

1. 应用修改后重新生成 mscorlib
2. 检查 `System_Collections_Generic_Dictionary_2_ValueCollection_int_int.cpp:502` 等行是否消失
3. 确认 `icall_array_get_length` 等调用的参数类型正确
4. 验证 Dictionary 的 CopyTo/GetEnumerator 功能正常

---

## 七、风险评估

- **低风险**: 修改仅影响参数类型声明，不改变 ABI 或语义
- **回归风险**: 如果其他代码路径传入非 Array 对象到 CopyTo，可能在运行时失败（但 .NET 语义保证 Array 参数）
- **测试覆盖**: 需要测试 Dictionary.CopyTo 和 Dictionary.GetEnumerator 的基本功能
