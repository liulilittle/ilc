# r77 System_TupleExtensions.cpp 302 nmf 错误模式分析

源: `E:\il2cpp\r77shard\r77shard{1,2,3}_shard{1,2,3}_errors.log`
分析时间: 2026-09-06
错误行数: 302 (r77 shard 中 nmf 总量 4160 的 7.3%)

## 一、错误分布

### 1.1 按函数分组

| 模式 | 次数 | 占比 | 根因 |
|------|------|------|------|
| get_Item1~7 链式调用 | 210 | 69.5% | 返回 int32_t → 传入期望 Il2CppObject* |
| get_Rest 链式调用 | 56 | 18.5% | 返回 int32_t → 传入期望 Il2CppObject* |
| CreateLong/CreateLongRef 参数 | 35 | 11.6% | 参数类型不匹配 |
| ValueTuple_8_int 构造 | 1 | 0.3% | 同上 |
| **合计** | **302** | | |

### 1.2 按行号区间

| 行区间 | 错误数 | 对应方法 |
|--------|--------|----------|
| 292 | 4 | Deconstruct_to_9 |
| 322-324 | 8 | Deconstruct_to_10 |
| 354-358 | 12 | Deconstruct_to_11 |
| 388-394 | 16 | Deconstruct_to_12 |
| 424-432 | 20 | Deconstruct_to_13 |
| 462-472 | 24 | Deconstruct_to_14 |
| 502-514 | 28 | Deconstruct_to_15 |
| 545-559 | 56 | Deconstruct_to_16 (含 get_Rest 链) |
| 650-657 | 28 | Deconstruct_to_17 (含 get_Rest 链) |
| 1087-1285 | 21 | ToValueTuple/ToTuple (CreateLong 参数) |
| 2062-2062 | 14 | ToTuple (CreateLongRef 参数) |
| 2299 | 1 | CreateLong (ValueTuple_8_int 构造) |

## 二、根因分析

### 2.1 错误签名声明 (generated.h)

```cpp
// Tuple`1-8 的 get_Item1~7 和 get_Rest — 声明为 int32_t 返回
int32_t System_Tuple_1_int_get_Item1_31f02c806a2b3833(Il2CppObject* arg0);
int32_t System_Tuple_2_int_get_Item1_0dd84af6f1518716(Il2CppObject* arg0);
int32_t System_Tuple_2_int_get_Item2_f47f4bf6e324ae1b(Il2CppObject* arg0);
int32_t System_Tuple_3_int_get_Item1_ad326461fe06fc85(Il2CppObject* arg0);
// ... 共 63 个 (Tuple`1-8 × Item1-7 + Tuple`8 × Rest)
int32_t System_Tuple_8_int_get_Rest_b3ab99604a10375a(Il2CppObject* arg0);
```

### 2.2 期望签名 (调用侧需要)

```cpp
// 链式调用中, get_Rest 返回值被直接传给 get_Item1~7
t0 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(arg0);   // 实际返回 int32_t
t1 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(t0);     // t0 是 int32_t, 但期望 Il2CppObject* → nmf!
```

### 2.3 错误链

```
System.Tuple`8<int>.get_Rest() → 声明返回 int32_t
  ↓
返回值被赋值给 int32_t 局部变量 t0
  ↓
下一层调用 System.Tuple`8<int>.get_Rest(t0) 期望 Il2CppObject* 参数
  ↓
g++ 报错: no matching function for call to '...' (int32_t vs Il2CppObject*)
```

### 2.4 实际 .NET 语义

`System.Tuple<T1..T8>.Rest` 返回的是 **`System.Tuple`** (引用类型), 不是 `int32`。

- `System.Tuple` 是 `System.Object` 的子类 → 引用类型
- `System.Tuple<T1..T7>` 的 `Rest` 属性返回剩余的 `System.Tuple`
- 正确 C++ 签名应为: `Il2CppObject* get_Rest(Il2CppObject* arg0)`
- 正确 C++ 签名应为: `Il2CppObject* get_Item<N>(Il2CppObject* arg0)` (当 T 本身是引用类型时)

## 三、证据链

### 3.1 generated.h 中的错误声明

```cpp
int32_t System_Tuple_8_int_get_Rest_b3ab99604a10375a(Il2CppObject* arg0);
```

### 3.2 调用侧实际使用

```cpp
// System_TupleExtensions.cpp:559
t0 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(arg0);   // arg0=Il2CppObject* ✓
...
t1 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(t0);     // t0=int32_t ✗, 期望 Il2CppObject*
```

### 3.3 与 Tuple 声明对比

```cpp
// generated.h: Tuple 的 Create (工厂方法) — 正确
System_ValueTuple System_ValueTuple_Create_8aa8898e19ab9553(int32_t arg0);
System_ValueTuple System_ValueTuple_Create_f0298bdc7a17d422(int32_t arg0, int32_t arg1);

// Tuple 的 CreateLong — 参数声明正确
Il2CppObject* System_TupleExtensions_CreateLong_65dc87761b412fd1(int32_t arg0, ..., int32_t arg7);
// 但调用侧传入的是 int32_t 返回值 → 期望 Il2CppObject* 参数 → nmf
```

## 四、修复方案

### 方案 A: 修正 Tuple_Item/Rest 返回类型 (推荐)

在 `TypeEmitter.EmitMethodSignature` 或 `MethodRefResolver` 中，将 `System.Tuple<T1..T8>` 的 `get_Item1~7` 和 `get_Rest` 的返回类型从 `int32_t` 改为 `Il2CppObject*`。

```csharp
// TypeEmitter.cs / MethodRefResolver.cs 中的 Tuple 特殊处理
if (ownerFullName.StartsWith("System.Tuple`") && 
    (name.StartsWith("get_Item") || name == "get_Rest"))
{
    retCpp = "Il2CppObject*";  // Tuple.Item/Rest 返回引用类型
}
```

**预期消除**: 266 行 (Pattern A1+A2)

### 方案 B: 为 CreateLong/CreateLongRef 添加重载

在 `generated.h` 中添加 `int32_t` 参数重载:

```cpp
// 现有
Il2CppObject* System_TupleExtensions_CreateLong_65dc87761b412fd1(int32_t arg0, ..., int32_t arg7);
// 新增 (用于接收 int32_t 链式调用结果)
Il2CppObject* System_TupleExtensions_CreateLong_int_...(int32_t arg0, ..., int32_t arg7);
```

**预期消除**: 35 行 (Pattern B)

### 方案 C: 插入 reinterpret_cast (临时)

在 Tuple 方法调用侧插入显式转换:

```cpp
t1 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(
    reinterpret_cast<Il2CppObject*>(t0));
```

**预期消除**: 266 行, 但代码侵入性大。

## 五、推荐方案

**方案 A + B 结合**:

1. 在 `TypeEmitter.EmitMethodSignature` (或 `MethodRefResolver`) 中:
   - 检测 `System.Tuple<T1..T8>` + `get_Item1~7` / `get_Rest` → `retCpp = "Il2CppObject*"`
   - 同时把 `get_Item` 的参数也从 `int32_t` 改为 `Il2CppObject*`

2. 在 `generated.h` 中手动修正 Tuple 方法签名 (快速验证):
   - 63 个 `get_Item1~7` 和 `get_Rest` 返回类型从 `int32_t` 改为 `Il2CppObject*`

**预期总消除**: 302 行 (100%)

## 六、影响范围

- 仅影响 `System.Tuple<T1..T8>` 的 `get_Item1~7` 和 `get_Rest` 方法
- 不影响 `System.ValueTuple<T1..T8>` (值类型, 返回 int32_t 正确)
- 不影响 `System.Tuple` 的 `get_Item` 当 T 是 int32 时 (值类型字段, 返回 int32_t 正确)
- 仅当 T 是引用类型时, `get_Item` 才应返回 `Il2CppObject*`

## 七、验证方法

1. 修改后重新生成 mscorlib_gen_r77
2. 编译 System_TupleExtensions.cpp
3. 验证 302 个 nmf 错误全部消除
4. 同时检查 Tuple`1-8 的其他调用点不受影响
