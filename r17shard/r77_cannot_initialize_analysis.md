# r77 cannot initialize 错误分析

**生成时间**: 2026-09-06
**数据源**: E:\il2cpp\out\r77shard\shard1/2/3_errors.log
**总错误数**: 2093 行 "cannot initialize"

---

## 一、按类型组合分组（Top 15）

| 排名 | 次数 | 类型组合 | 说明 |
|------|------|----------|------|
| 1 | 598 | `a parameter \| Il2CppObject * \| int32_t` | 参数期望 Il2CppObject* 但传入 int32_t |
| 2 | 308 | `a parameter \| Il2CppArray * \| Il2CppObject *` | 参数期望 Il2CppArray* 但传入 Il2CppObject*（基类→派生类缺少显式转换） |
| 3 | 67 | `a parameter \| Il2CppObject * \| Il2CppObject **` | 二级指针未解引用 |
| 4 | 62 | `a parameter \| System_RuntimeType_ListBuilder_1_int * \| Il2CppObject **` | 泛型类型指针不匹配 |
| 5 | 61 | `return object \| Il2CppObject * \| int32_t` | 返回对象期望 Il2CppObject* 但返回 int32_t |
| 6 | 58 | `a parameter \| Il2CppObject * \| int32_t *` | 值类型指针未装箱 |
| 7 | 56 | `a parameter \| Il2CppObject * \| intptr_t` | 整数→指针需要 reinterpret_cast |
| 8 | 29 | `a parameter \| Il2CppClass * \| int64_t` | 整数误作类指针 |
| 9 | 18 | `a parameter \| Il2CppString * \| Il2CppObject *` | 字符串派生类缺少显式向下转换 |
| 10 | 16 | `a parameter \| System_Nullable_1_... \| Il2CppObject **` | Nullable 泛型指针不匹配 |
| 11 | 14 | `a parameter \| Il2CppObject * \| long long` | 同 #7（intptr_t） |
| 12 | 13 | `a parameter \| Il2CppObject * \| int64_t` | 64位整数误作对象指针 |
| 13 | 11 | `a parameter \| const Il2CppMethodInfo * \| int64_t` | 整数误作方法信息指针 |
| 14 | 10 | `a parameter \| System_Nullable_1_... \| System_Nullable_1_int *` | Nullable 内部类型不匹配 |
| 15 | 9 | `a parameter \| Il2CppObject ** \| int32_t *` | 指针宽度不匹配 |

---

## 二、按出错文件分组（Top 15）

| 排名 | 次数 | 文件路径 |
|------|------|----------|
| 1 | 56 | System_RuntimeType.cpp |
| 2 | 40 | System_Array.cpp |
| 3 | 33 | System_Diagnostics_Tracing_ManifestBuilder.cpp |
| 4 | 31 | System_RuntimeType_RuntimeTypeCache_MemberInfoCache_1_int.cpp |
| 5 | 28 | System_Text_UnicodeEncoding.cpp |
| 6 | 20 | System_Diagnostics_Tracing_EventSource.cpp |
| 7 | 20 | System_Text_UTF32Encoding.cpp |
| 8 | 16 | System_Text_ASCIIEncoding.cpp |
| 9 | 16 | System_Text_UTF8Encoding.cpp |
| 10 | 15 | System_Security_Cryptography_X509Certificates_PfxAsn.cpp |
| 11 | 14 | System_Security_Cryptography_X509Certificates_PasswordBasedEncryption.cpp |
| 12 | 14 | System_Collections_Concurrent_ConcurrentDictionary_2_string_object.cpp |
| 13 | 14 | System_Collections_Concurrent_ConcurrentDictionary_2_object_System_Reflection... |
| 14 | 14 | System_Collections_Concurrent_ConcurrentDictionary_2_object_object.cpp |
| 15 | 14 | System_Collections_Concurrent_ConcurrentDictionary_2_object_object_array.cpp |

---

## 三、按初始化类型分类统计

### 3.1 参数初始化错误（1973 次，94.3%）

| 目标类型 | 来源类型 | 次数 |
|----------|----------|------|
| Il2CppObject* | int32_t | 598 |
| Il2CppArray* | Il2CppObject* | 308 |
| Il2CppObject* | Il2CppObject** | 67 |
| System_RuntimeType_ListBuilder_1_int* | Il2CppObject** | 62 |
| Il2CppObject* | int32_t* | 58 |
| Il2CppObject* | intptr_t | 56 |
| Il2CppClass* | int64_t | 29 |
| Il2CppString* | Il2CppObject* | 18 |
| Il2CppObject* | int64_t | 13 |

**根因**: 值类型（int32_t）未装箱直接传递给期望引用类型（Il2CppObject*）的参数；派生类指针未显式转换为基类。

### 3.2 返回对象初始化错误（68 次，3.2%）

| 期望返回类型 | 实际返回类型 | 次数 |
|-------------|-------------|------|
| Il2CppObject* | int32_t | 61 |
| Il2CppObject* | long long | 4 |
| double | Il2CppObject* | 1 |
| Il2CppObject* | WindowsIdentity* | 1 |
| Il2CppObject* | CompressedStack* | 1 |

**根因**: 方法签名期望返回 Il2CppObject* 但实际返回了 int32_t（缺少装箱或 static_cast）。

---

## 四、Top 10 出错的初始化类型组合

| 排名 | 组合 | 次数 | 修复方向 |
|------|------|------|----------|
| 1 | 参数 Il2CppObject* ← int32_t | 598 | 需要 box 或 static_cast<Il2CppObject*> |
| 2 | 参数 Il2CppArray* ← Il2CppObject* | 308 | 需要 static_cast<Il2CppArray*> |
| 3 | 参数 Il2CppObject* ← Il2CppObject** | 67 | 需要解引用 * |
| 4 | 参数 ListBuilder_1_int* ← Il2CppObject** | 62 | 需要 reinterpret_cast |
| 5 | 返回 Il2CppObject* ← int32_t | 61 | 需要 reinterpret_cast<Il2CppObject*> |
| 6 | 参数 Il2CppObject* ← int32_t* | 58 | 需要 reinterpret_cast |
| 7 | 参数 Il2CppObject* ← intptr_t | 56 | 需要 reinterpret_cast |
| 8 | 参数 Il2CppClass* ← int64_t | 29 | 需要 reinterpret_cast |
| 9 | 参数 Il2CppString* ← Il2CppObject* | 18 | 需要 static_cast |
| 10 | 参数 Nullable<T>* ← Il2CppObject** | 16 | 需要 reinterpret_cast |

---

## 五、根因分析

1. **值类型未装箱**: int32_t 直接传给 Il2CppObject* 参数（598次）。这是 M1/M2 模型的遗留问题——早期假设 int 可以直接作为对象传递，但 C++ 编译需要显式 box。

2. **派生类未显式转换**: Il2CppObject* 传给 Il2CppArray* / Il2CppString*（308+18次）。C++ 不允许隐式向下转型，需要 static_cast。

3. **二级指针未解引用**: Il2CppObject** 传给 Il2CppObject*（67次）。ldloca 产生的地址被错误地直接传递。

4. **整数类型误作指针**: intptr_t/int64_t 传给 Il2CppClass* 或 Il2CppObject*（56+29次）。需要 reinterpret_cast。

5. **返回类型不匹配**: int32_t 返回给 Il2CppObject*（61次）。缺少 reinterpret_cast 或 box。

---

## 六、修复优先级

1. **P0**: int32_t → Il2CppObject* (598次) — 需要 box_int32 或 reinterpret_cast
2. **P0**: Il2CppObject* → Il2CppArray* (308次) — 需要 static_cast
3. **P1**: 返回类型不匹配 (68次) — 需要 reinterpret_cast<Il2CppObject*>
4. **P1**: 二级指针问题 (129次) — 需要解引用或 reinterpret_cast
5. **P2**: intptr_t/int64_t → 指针 (85次) — 需要 reinterpret_cast
