# r77 System.DateTime 相关错误模式分析

## 总览

| 指标 | 数量 |
|------|------|
| 总 DateTime 相关错误 | 2316 |
| Shard 1 | 1252 |
| Shard 2 | 631 |
| Shard 3 | 433 |

## 错误分类

| 错误类型 | 数量 | 占比 |
|----------|------|------|
| 其他（log/注释） | 2020 | 87.2% |
| no viable overloaded '=' | 99 | 4.3% |
| no viable conversion from | 97 | 4.2% |
| no matching function for call | 53 | 2.3% |
| cannot cast | 47 | 2.0% |

> 注：2020 条"其他"为 log 行中的注释/中间代码（如 `DateTime__ctor` 注释）。**实际编译错误约 296 条**。

## 实际错误分析

### 转换失败模式

| 来源类型 -> 目标类型 | 次数 | 根因 |
|---|---|---|
| Il2CppTimeSpan -> Il2CppDateTime | 17 | 缺少转换构造函数 |
| TaskAwaiter_1_int -> Il2CppObject* | 6 | awaiter 未解包 |
| Il2CppDateTime -> Il2CppObject* | 6 | 缺少 boxing |
| Il2CppDateTimeOffset -> Il2CppObject* | 6 | 缺少 boxing |
| TaskAwaiter_1_int -> Il2CppDateTime | 2 | awaiter 未解包 |
| System_TimeSpan -> Il2CppDateTime | 2 | 缺少转换构造函数 |
| System_TimeSpan -> Il2CppTimeSpan | 2 | 缺少转换构造函数 |
| TaskAwaiter_1_int -> Il2CppDateTimeOffset | 2 | awaiter 未解包 |

### 模式 1: Il2CppTimeSpan -> Il2CppDateTime 转换失败（17 次）

**错误示例**：
```
no viable conversion from 'il2cpp::runtime::Il2CppTimeSpan' to 'Il2CppDateTime'
candidate: Il2CppDateTime(int64_t value)
```

**根因**：
- `Il2CppDateTime` 有 `int64_t` 构造函数（接收 ticks）
- `Il2CppTimeSpan` 无法隐式转换为 `int64_t`，不能调用该构造函数
- 两者虽然都是 8 字节值类型，但语义不同，没有定义转换运算符

**修复方案**：
- 显式提取 Ticks：`Il2CppDateTime(timeSpan.Ticks)`
- 或者定义转换运算符

---

### 模式 2: Il2CppDateTime -> Il2CppObject* 转换（6 次）

**错误示例**：
```
no viable conversion from 'il2cpp::runtime::Il2CppDateTime' to 'Il2CppObject *'
```

**根因**：
- `Il2CppDateTime` 是值类型（8 字节结构体）
- 不能直接转换为 `Il2CppObject*`（指针）
- 需要先进行 boxing 操作

**修复方案**：
```cpp
// 错误：直接转换
static_cast<Il2CppObject*>(dateTime);

// 正确：显式 boxing
il2cpp::runtime::icall_box_dateTime(&dateTime);
```

---

### 模式 3: TaskAwaiter 转换失败（约 10 次）

**根因**：
- `TaskAwaiter<int>` 是值类型包装器
- 未正确解包为 `int` 就传入期望 `Il2CppDateTime` 的位置
- async 状态机生成时返回类型处理错误

---

### 模式 4: DateTime 构造函数调用失败（约 53 次 no matching function）

**涉及函数**：
- `System_DateTime__ctor_d38f32de3c92faec`
- `System_DateTime__ctor_6271d95ab2743115`
- `System_DateTime__ctor_fea11466c256ebc3`
- `System_DateTimeOffset__ctor_0ad01bd3acde4e49`
- `System_DateTimeOffset__ctor_de337fece9806671`

**根因**：
- 构造函数期望特定参数类型（如 `int64_t`）
- 传入了不兼容的类型（如 `int32_t`、`Il2CppTimeSpan`）
- 缺少隐式转换路径

---

### 模式 5: 赋值过载错误（99 次）

**根因**：
- `Il2CppDateTime` 之间赋值需要 `operator=`
- 值类型到指针的赋值
- 不同大小结构体之间的赋值

---

## 根本原因总结

| 根因 | 错误数 | 占比 |
|------|--------|------|
| Il2CppTimeSpan -> Il2CppDateTime 转换缺失 | ~21 | 7.1% |
| DateTime/DateTimeOffset 缺少 boxing | ~12 | 4.1% |
| TaskAwaiter 未解包 | ~10 | 3.4% |
| DateTime 构造函数参数类型不匹配 | ~53 | 17.9% |
| 赋值操作符缺失 | 99 | 33.4% |
| 其他/类型不匹配 | ~101 | 34.1% |

## 受影响文件

错误集中在以下文件：
1. `System_DateTime__ctor_*.cpp` - 构造函数调用失败
2. `System_DateTimeOffset__ctor_*.cpp` - 构造函数调用失败
3. `System_Globalization_Calendar_*.cpp` - Calendar 操作
4. `System_Collections_Generic_EqualityComparer_1_System_DateTime.cpp` - 比较器
5. `System_Runtime_CompilerServices_AsyncTaskMethodBuilder_*.cpp` - async 状态机
6. `System_Runtime_InteropServices_*.cpp` - P/Invoke 回调

## 修复方案汇总

### 1. Il2CppTimeSpan -> Il2CppDateTime 转换
```cpp
// 方案 A：显式转换（推荐）
Il2CppDateTime dateTime(timeSpan.ticks);

// 方案 B：在 Il2CppTimeSpan 中添加转换运算符
struct Il2CppSpan {
    operator Il2CppDateTime() const { return Il2CppDateTime(ticks); }
};
```

### 2. DateTime Boxing
```cpp
// 错误：直接转指针
static_cast<Il2CppObject*>(dateTime);

// 正确：显式 boxing
Il2CppDateTime* boxed = il2cpp_gc_alloc_size(sizeof(Il2CppDateTime));
*boxed = dateTime;
return static_cast<Il2CppObject*>(boxed);

// 或使用 icall
return il2cpp::runtime::icall_box_dateTime(&dateTime);
```

### 3. TaskAwaiter 解包
```cpp
// 错误：传入 TaskAwaiter
void Process(TaskAwaiter<int> awaiter);

// 正确：先解包
int result = awaiter.GetResult();
void Process(result);
```

### 4. 构造函数参数匹配
```cpp
// 错误：传入 int32_t
DateTime__ctor((&dt), int32_value);

// 正确：显式转型
DateTime__ctor((&dt), static_cast<int64_t>(int32_value));
```

### 5. 赋值操作符
- 为 `Il2CppDateTime` 定义默认 `operator=`
- 或者确保值类型之间通过拷贝构造而非赋值操作
