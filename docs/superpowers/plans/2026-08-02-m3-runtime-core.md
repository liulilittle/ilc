# M3 内存管理 + 异常体系 + 核心类型系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 M2 对象模型/标记清除 GC/虚方法/接口/装箱之上，实现分代 GC + 内存管理子系统 + 托管异常体系 + string/array 核心类型 + 泛型实例化 + 委托，使包含异常、字符串、数组、泛型、委托的 C# 程序经转译后三编译器运行与 .NET Framework 4.8 参考逐字节一致。

**Architecture:** 运行时 GC 从单代标记-清除升级为 Gen0/1/2 + LOH 分代（写屏障 + 卡表 + 压缩回收）；异常体系以 C++ 原生异常为载体（DotNetException 持有托管异常对象，catch 继承匹配 + finally RAII 守卫）；string/array 用专用对象布局（头 + 长度 + 数据）；泛型按铁律 2g 编译期实例化（引用类型共享 + 值类型特化 + T=Il2CppClass* 隐藏参数）；委托 = 对象（方法指针 + 目标 + 组合链）。

**Tech Stack:** C# 8 / .NET 8（转译器）；C++17（运行时，GCC 7.5 兼容基线）；CMake ≥3.10；三编译器（MSVC/MinGW/WSL GCC7.5）

**设计文档:** `docs/superpowers/specs/2026-08-02-il2cpp-design.md`（§5.2 GC / §5.3 异常 / §5.4 帧记录 / §5.5 委托）
**风格规范:** `docs/superpowers/specs/2026-08-02-generated-code-style.md`（全英语超详细注释/列对齐/确定性）
**用户铁律:** `docs/REQUIREMENTS.md`（2e 性能 / 2f IL 语义 / 2g 泛型禁 C++ template / 2g-2 反射能力 / 2h 全英语）

## Global Constraints

（抄自 spec + M1/M2 经验，M3 阶段生效）
- 零 JIT；零第三方依赖；C++17 仅 + GCC 7.5 兼容基线；CMake 3.10 基线
- 引用语义 = 裸 GC 指针（追踪式），严禁 shared_ptr/引用计数/手写 free
- **禁全局构造分配内存（约束 #12）**：静态字段常量/零初始化；cctor 显式；runtime_init 显式分配
- **GC 元数据不得依赖 C++ 堆**（用户铁律，M2 Task 4 确立）：固定静态表 + fail-loud
- **泛型禁止 C++ template**（铁律 2g）：编译期实例化，引用类型共享代码 + 值类型独立特化，T=Il2CppClass* 隐藏参数
- 生成代码：全英语超详细注释 + 列对齐 + 4 空格缩进 + 确定性
- 三编译器验证：每任务三编译器编译零错误；e2e 用例双跑逐字节一致
- 任何代码变更后编译零错误（C0）；TDD 红绿循环；CI 一键全绿
- **M2 交接项（本里程碑必须处理）**：① string 常量目前是 const char*（ldstr → 裸 C 字符串），M3 改为托管 string 对象；② 异常体系落地后 il2cpp_null_check/unbox 类型错误的 abort 改为抛托管异常；③ 帧根协议（M2 Task 10 的 add/remove root range）升级为三合一帧记录；④ castclass 失败（M2 简化为 null）改为抛 InvalidCastException

---

### Task 1: string 核心类型（对象布局 + 字面量池 + 运行时 API）

**Files:**
- Create: `src/runtime/include/internal/il2cpp_string.h`
- Create: `src/runtime/src/string.cpp`
- Modify: `src/runtime/CMakeLists.txt`
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（ldstr → string 对象创建调用）
- Modify: `src/il2cppc/Il/MethodRefResolver.cs`（WriteLine(string) → 新 icall）
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（temp 类型推断：StringConstExpr → Il2CppString*）
- Test: `tests/runtime/test_string.cpp` + e2e `tests/cases/string.cs`

**Interfaces:**
- Consumes: M2 对象模型（Il2CppObject/Il2CppClass/il2cpp_gc_alloc）
- Produces:
  - `struct Il2CppString { Il2CppObject obj; int32_t length; char16_t chars[]; }`（专用布局：头 + 长度 + UTF-16 数据）
  - `Il2CppString* il2cpp_string_new(const char16_t* chars, int32_t length)`（GC 分配 + 拷贝）
  - `Il2CppString* il2cpp_string_new_from_utf8(const char* utf8)`（icall 边界用，M3 icall 字符串参数）
  - `Il2CppString* il2cpp_string_new_from_cs(const char* ascii)`（生成代码 ldstr 用：ASCII 字面量 → string 对象）
  - 编译期字符串池（静态字面量 → 惰性创建 + 缓存；M2 简化：每次 ldstr 新建——M3 内池化缓存）

- [ ] **Step 1: 写失败测试（运行时 C++ 侧）**

`tests/runtime/test_string.cpp`：
- `il2cpp_string_new_from_cs("hi")` → 非空、length==2、chars 内容 "hi"
- length==0 的空串（"" → length 0，非空指针）
- 多字节 ASCII（含大写/数字/符号）
- 新字符串与源缓冲区独立（修改源不影响字符串内容）
- string 对象经 GC 分配（il2cpp_gc_collect 后仍存活——根保持）

- [ ] **Step 2: RED**

Run: `cmake -S tests/runtime -B build/tests_runtime -G "MinGW Makefiles" && cmake --build build/tests_runtime`
Expected: FAIL（Il2CppString 不存在）

- [ ] **Step 3: 实现**

`il2cpp_string.h`:
```cpp
#pragma once
#include "il2cpp_object.h"
#include <cstdint>

namespace il2cpp {

// Managed string layout: object header + length + UTF-16 payload.
// The length counts char16_t units (not bytes); M3 stores UTF-16 like .NET
// (surrogate pairs occupy two units; surrogate handling arrives with M3
// string operations). The char16_t array is variable-length -- the GC
// allocates (header + length*2) bytes for a string of 'length' units.
struct Il2CppString
{
    Il2CppObject obj;      // object header (klass pointer)
    int32_t      length;   // number of char16_t units (not bytes)
    char16_t     chars[1]; // payload: 'length' units follow inline
};

} // namespace il2cpp
```

`string.cpp`:
```cpp
#include "il2cpp_string.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include <cstring>

namespace il2cpp {

// The System.String runtime descriptor (M2: minimal -- name/instanceSize/
// no fields). instanceSize covers the header + length slot; the GC allocates
// (header + length*2) bytes for the payload via il2cpp_gc_alloc_size.
// NOTE: M2's il2cpp_gc_alloc takes (klass) and allocates klass->instanceSize.
// String needs a SIZE-EXTENDED allocation: il2cpp_gc_alloc_size(klass, total)
// is added in this task (the collector's block table records the true size,
// so sweep/scanning stay exact).
Il2CppClass g_stringClass = {
    "System.String", sizeof(Il2CppString), 0u, 0, nullptr, nullptr, 0, nullptr, 0
};
// registered lazily (same pattern as the Int32 box class)

Il2CppString* il2cpp_string_new(const char16_t* chars, int32_t length) { ... }
Il2CppString* il2cpp_string_new_from_utf8(const char* utf8) { ... }
Il2CppString* il2cpp_string_new_from_cs(const char* ascii) { ... }

} // namespace il2cpp
```

- [ ] **Step 4: 运行时加 size 扩展分配**

`il2cpp_gc.h`/`gc.cpp` 加 `Il2CppObject* il2cpp_gc_alloc_size(Il2CppClass* klass, size_t totalSize)`（totalSize >= instanceSize，块登记真实尺寸；分配路径与 il2cpp_gc_alloc 共用，仅尺寸参数不同）。string 用 `sizeof(Il2CppString) + (length-1)*sizeof(char16_t)`。

- [ ] **Step 5: 转译器 ldstr 接入**

- StackFold：ldstr 的 StringConstExpr 保持（IR 不变）
- CodeGenerator：StringConstExpr 发射 `il2cpp::il2cpp_string_new_from_cs("...")`；temp 类型推断 StringConstExpr → 引用（Il2CppString*，与 Il2CppObject* 兼容）
- MethodRefResolver：WriteLine(string) → 新 icall `il2cpp::runtime::icall_console_write_line_s(Il2CppString*)`（打印 UTF-16 → ASCII 转换；M3 icall 边界统一）
- `il2cpp_runtime.h` 声明

- [ ] **Step 6: e2e string.cs**

```csharp
using System;
namespace Cases { public static class Str {
    public static void Main() {
        Console.WriteLine("hello");        // hello
        Console.WriteLine("Hello, World!"); // Hello, World!
        Console.WriteLine("123");          // 123
        Console.WriteLine("");             // (empty line)
    }
}}
```
双跑一致。

- [ ] **Step 7: 三编译器验证 + CI + 提交**

```bash
git add . && git -c user.name="il2cpp-dev" -c user.email="dev@il2cpp.local" commit -m "feat: M3 managed string (Il2CppString layout + literal creation + WriteLine icall)"
```

---

### Task 2: 分代 GC 架构（Gen0/1/2 + LOH 段管理 + 晋升）

**Files:**
- Modify: `src/runtime/src/gc.cpp`、`src/runtime/include/internal/il2cpp_gc.h`
- Modify: `tests/runtime/test_gc.cpp`

**Interfaces:**
- Consumes: Task 1（il2cpp_gc_alloc_size）
- Produces:
  - `il2cpp_gc_alloc(klass)` → Gen0 bump（M2 的段链表保留为 Gen0）
  - `il2cpp_gc_collect()` → 全代回收（M2 语义保持：从根全标记）
  - 代元数据：`g_gen0/g_gen1/g_gen2`（段链表）+ LOH 段（≥85000 字节对象）
  - 晋升：幸存对象从 Gen0 复制/移动到 Gen1，Gen1 幸存 → Gen2；M3 首版**非压缩**晋升（复制到新段），压缩在 Task 4
  - LOH：≥85000 字节分配走独立段（不晋升不移动，标记-清除）

- [ ] **Step 1: 写失败测试**

`tests/runtime/test_gc.cpp` 追加：
- 分配 < 85000 字节对象 → Gen0 段；≥ 85000 → LOH 段（通过内部查询 helper 或行为验证：大量小对象回收复用 vs LOH 大对象存活）
- 晋升行为：Gen0 分配 → collect → 对象在 Gen1（内部计数验证或行为验证：二次 collect 后仍存活）
- M2 既有测试全部保持通过（API 不变）

- [ ] **Step 2: RED → 实现**

gc.cpp 重构：
```cpp
// 段结构扩展：generation 标记（0/1/2/LOH）+ 段池复用
struct Segment {
    Segment* next;
    int32_t  generation;   // 0=Gen0, 1=Gen1, 2=Gen2, 3=LOH
    size_t   used;
    size_t   capacity;
    char     data[];       // FLEM 用 offsetof 模式（M2 已用）
};
// 分配路径：
//   size < kLohThreshold(85000) → Gen0 bump（当前段满 → 新 Gen0 段）
//   size >= kLohThreshold        → LOH 段 bump（独立，不参与晋升）
// Collect：
//   Mark（根 = 静态根 + 根区间 + 帧根——M2 协议不变）
//   Sweep Gen0：幸存 → 复制到 Gen1 段（晋升）；未标记 → 空闲链
//   Sweep Gen1：幸存 → 复制到 Gen2；未标记 → 空闲链
//   Sweep Gen2 + LOH：标记-清除（free-list 复用，不移动——Task 4 压缩）
//   LOH 对象不晋升（存活即留在 LOH 段）
```

- [ ] **Step 3: 验证 + 提交**

---

### Task 3: 写屏障 + 卡表（老→新引用跟踪）

**Files:**
- Modify: `src/runtime/src/gc.cpp`、`src/runtime/src/object.cpp`
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（引用字段写屏障）
- Test: `tests/runtime/test_gc.cpp` + e2e

**Interfaces:**
- Consumes: Task 2
- Produces:
  - `void il2cpp_write_barrier(Il2CppObject** slot, Il2CppObject* value)`（引用字段写入前调用）
  - 卡表：每 4KB 段一个卡位（byte），老对象（Gen1/2/LOH）写引用字段时标记其卡片
  - Gen0 收集只扫 Gen0 段 + 卡表标记的老段卡片（增量收集的根）

- [ ] **Step 1: 写失败测试**

- 行为验证：老对象（晋升到 Gen1/2 后）写新 Gen0 引用 → 卡表标记 → 只收 Gen0 时新对象仍可达（卡表扫描保住）
- 新→新写入不标记卡表
- 卡表重置在收集后

- [ ] **Step 2: RED → 实现**

```cpp
// 卡表：g_cardTable[]，索引 = 地址 >> 12（4KB 粒度），0/1 标记
// 写屏障（生成代码引用字段 stfld 时调用）：
void il2cpp_write_barrier(Il2CppObject** slot, Il2CppObject* value) {
    // M2 语义：记录 slot 所在对象是否老代（klass->generation >= 1）
    // 老代写入 → 标记卡片。M3 简化：总是标记（保守正确，性能优化后续）
}
```
- CodeGenerator：FieldAssignStmt 引用字段写入 → `il2cpp::il2cpp_write_barrier(&obj->field, value)`（M2 是裸写）

- [ ] **Step 3: 验证 + 提交**

---

### Task 4: Gen0/1 压缩回收 + 引用更新

**Files:**
- Modify: `src/runtime/src/gc.cpp`
- Test: `tests/runtime/test_gc.cpp`

**Interfaces:**
- Consumes: Task 2/3
- Produces: Gen0/1 收集时对象移动 + 全图引用更新（对象图/卡表/根集合）；Gen2/LOH 不移动（标记-清除 + 空闲合并）

- [ ] **Step 1: 写失败测试**

- 压缩后对象地址变化但数据完好（移动正确）
- 引用更新：静态根/帧根/字段引用在新地址正确指向
- 压缩后分配继续工作

- [ ] **Step 2: RED → 实现**

```cpp
// Gen0/1 压缩：幸存对象顺序复制到新段（Gen1/2 目标段），记录旧地址→新地址映射
// （forwarding map：侧表，收集期间有效）；复制后全图引用更新——
//   ① 根集合（静态根/根区间/帧根）指向的旧地址 → 新地址
//   ② 对象图（每个存活对象的引用字段）旧地址 → 新地址
//   ③ 卡表引用
// 压缩是 M3 高风险区：地址更新遗漏 = 悬挂指针崩溃。TDD 覆盖全部引用路径。
```

- [ ] **Step 3: 验证 + 提交**

---

### Task 5: 数组类型（多维 + 边界检查 + 类型化访问）

**Files:**
- Create: `src/runtime/include/internal/il2cpp_array.h`、`src/runtime/src/array.cpp`
- Modify: `src/il2cppc/`（newarr/ldelem/stelem/ldlen 折叠 + 发射）
- Modify: `src/il2cppc/Il/IlDecoder.cs`（数组 opcode 已在解码器，确认）
- Test: `tests/runtime/test_array.cpp` + e2e `tests/cases/arrays.cs`

**Interfaces:**
- Consumes: Task 1（string 的 alloc_size 模式）
- Produces:
  - `struct Il2CppArray { Il2CppObject obj; int32_t length; int32_t elemSize; Il2CppClass* elemClass; char data[]; }`（一维 M3 首版；多维 M3.5）
  - `Il2CppArray* il2cpp_array_new(Il2CppClass* elemClass, int32_t length)`（分配 + 零初始化）
  - `int32_t il2cpp_array_get_length(Il2CppArray* arr)` / `void* il2cpp_array_get_elem_ptr(Il2CppArray* arr, int32_t index)`（边界检查 helper）
  - 生成代码：`newarr` → 分配；`ldelem` → 边界检查 + 读取；`stelem` → 边界检查 + 写屏障；`ldlen` → length
  - 数组越界 → IndexOutOfRangeException（M3 异常对象，Task 6 前先 abort——Task 6 落地后改抛）

- [ ] **Step 1: 写失败测试**

- newarr 非空、length 正确、零初始化
- ldelem/stelem 值往返
- 边界：越界读/写 → IOORE 语义（M3 abort，Task 6 后抛异常）
- 长度 0 数组合法

- [ ] **Step 2: e2e arrays.cs**

```csharp
using System;
namespace Cases { public static class Arrays {
    public static void Main() {
        int[] a = new int[5];
        for (int i = 0; i < 5; i++) a[i] = i * i;
        Console.WriteLine(a[0] + a[4]);       // 16
        Console.WriteLine(a.Length);          // 5
        int[] b = { 3, 1, 4, 1, 5 };
        Console.WriteLine(b[2] + b[4]);       // 9
        int[] c = new int[0];
        Console.WriteLine(c.Length);          // 0
    }
}}
```

- [ ] **Step 3: 验证 + 提交**

---

### Task 6: 异常体系（DotNetException + try/catch/finally + 继承匹配）

**Files:**
- Create: `src/runtime/include/internal/il2cpp_exception.h`、`src/runtime/src/exception.cpp`
- Modify: `src/il2cppc/Il/IlDecoder.cs`（try/catch/finally/leave 解码——IL 异常处理表）
- Modify: `src/il2cppc/Ir/StackFold.cs`（异常处理块 → IR：try 边界/catch 过滤/finally）
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（try/catch/finally → C++ try/catch + DotNetException 匹配）
- Test: e2e `tests/cases/exceptions.cs`

**Interfaces:**
- Consumes: Task 1（string 用于异常消息）
- Produces:
  - `class DotNetException : std::exception`（携带 `Il2CppObject* exception` GC 引用 + 消息）
  - `void il2cpp_throw(Il2CppObject* ex)`（抛 DotNetException——异常对象是 GC 对象，须保证 throw 期间根保持）
  - `Il2CppObject* il2cpp_get_current_exception()`（catch 块读取）
  - 生成代码映射：
    - `try { ... } catch (Exception e) { ... }` → `try { } catch (il2cpp::DotNetException& e) { if (il2cpp::il2cpp_is_instance(e.exception, &{Ex}_class)) { ... } else throw; }`
    - `finally` → C++ RAII 守卫（析构执行 finally 体；finally 抛异常替代原异常——.NET 规则）
  - `il2cpp_null_check`/`unbox 类型错误`/数组越界 → 改抛对应托管异常（NRE/ICE/IOORE）
  - 顶层防护：main 包 try/catch → 未捕获异常打印消息 + 非零退出

- [ ] **Step 1: 写失败测试（运行时 C++）**

`tests/runtime/test_exception.cpp`：
- il2cpp_throw → catch(DotNetException&) 捕获，异常对象正确
- 继承匹配 helper：is_instance 判定异常类型
- 嵌套 try/catch 内层捕获外层不捕获

- [ ] **Step 2: e2e exceptions.cs**

```csharp
using System;
namespace Cases { public static class Exceptions {
    public static void Main() {
        int result = TryDiv(10, 0);
        Console.WriteLine(result);            // -1 (caught DivideByZero)
        try {
            throw new Exception("boom");
        } catch (Exception e) {
            Console.WriteLine("caught");     // caught
        }
        try {
            TryNested();
        } catch (Exception) {
            Console.WriteLine("outer");      // outer
        }
        int[] a = new int[2];
        try { Console.WriteLine(a[5]); }
        catch (Exception) { Console.WriteLine("oob"); }  // oob
    }
    static int TryDiv(int x, int y) {
        try { return x / y; }
        catch (Exception) { return -1; }
    }
    static void TryNested() {
        try { throw new Exception("inner"); }
        finally { Console.WriteLine("fin"); }  // fin
    }
}}
```
注意：M3 异常对象构造需要 Exception 类型——Task 1 的 string 可用；System.Exception 类 M3 首版用运行时内置（throw new Exception(string) → icall 或最小转译）。

- [ ] **Step 3: 验证 + 提交**

---

### Task 7: 泛型实例化（编译期展开 + 共享代码 + T 参数）

**Files:**
- Modify: `src/il2cppc/Analysis/`（泛型实例枚举——保留清单核心）
- Modify: `src/il2cppc/Emit/`（泛型类型发射：`List_int` 等独立 struct + 方法）
- Modify: `src/il2cppc/Model/`（GenericInstantiation 模型）
- Test: e2e `tests/cases/generics.cs`

**Interfaces:**
- Consumes: Task 5（数组）
- Produces:
  - 泛型实例枚举：静态可达 ∪ 动态保留清单中的全部 `T<Arg1,...>` 实例
  - 每实例独立 C++ 类型：`struct {Ns}_{T}_{Arg1}_{Arg2}`（sanitize 规则：`<`/`>`/`,` → `_`）
  - 引用类型实参共享：`List<string>`/`List<object>`/`List<任意类>` = 1 套代码（元素槽 = Il2CppObject*，隐藏 T 参数区分）——**铁律 2g**
  - 值类型实参特化：`List<int>` 独立代码（元素槽 = int32_t）
  - T 映射：`Il2CppClass* T` 隐藏参数（方法签名 `void List_Add(List_int* this, Il2CppClass* T, int32_t item)`）
  - 约束验证编译期完成（闭合世界证明）
  - **禁止 C++ template**（铁律 2g 硬约束）

- [ ] **Step 1: 写失败测试**

- 泛型类型声明（`class Box<T> { public T item; }`）→ 实例 `Box<int>` 生成独立 struct
- 泛型方法（`T Identity<T>(T v)`）→ 实例化
- 引用类型实参共享：`Box<string>` 与 `Box<object>` 同一代码

- [ ] **Step 2: e2e generics.cs**

```csharp
using System;
public class Box<T> {
    public T item;
    public T Get() { return item; }
    public void Set(T v) { item = v; }
}
namespace Cases { public static class Generics {
    static int IdentityInt(int v) => v;   // 直接调用（非泛型）
    static T Identity<T>(T v) => v;       // 泛型方法
    public static void Main() {
        Box<int> bi = new Box<int>();
        bi.Set(42);
        Console.WriteLine(bi.Get());      // 42
        Box<Box<int>> bb = new Box<Box<int>>();
        bb.Set(bi);
        Console.WriteLine(bb.Get().Get()); // 42
        Console.WriteLine(Identity(7));   // 7 (int 实例)
        Console.WriteLine(Identity(9));   // 9
    }
}}
```

- [ ] **Step 3: 验证 + 提交**

---

### Task 8: 委托（方法指针 + 目标 + Invoke + 组合）

**Files:**
- Create: `src/runtime/include/internal/il2cpp_delegate.h`、`src/runtime/src/delegate.cpp`
- Modify: `src/il2cppc/`（newdelegate/ldftn/callvirt Invoke 折叠 + 发射）
- Test: e2e `tests/cases/delegates.cs`

**Interfaces:**
- Consumes: Task 6（异常）
- Produces:
  - `struct Il2CppDelegate { Il2CppObject obj; void* methodPtr; Il2CppObject* target; Il2CppDelegate* next; }`（组合链：next 非空 = multicast）
  - `Il2CppDelegate* il2cpp_delegate_new(void* methodPtr, Il2CppObject* target)`（单播）
  - `Il2CppDelegate* il2cpp_delegate_combine(Il2CppDelegate* a, Il2CppDelegate* b)` / `il2cpp_delegate_remove`
  - Invoke 发射：`((RetType(*)(...))d->methodPtr)(d->target, args...)`；multicast 链遍历调用
  - 静态方法委托：target=null，methodPtr=静态函数

- [ ] **Step 1: e2e delegates.cs**

```csharp
using System;
namespace Cases { public static class Delegates {
    static int Twice(int x) => x * 2;
    static int PlusTen(int x) => x + 10;
    public static void Main() {
        Func<int, int> f = Twice;
        Console.WriteLine(f(21));          // 42
        f += PlusTen;
        Console.WriteLine(f(5));           // 20 (10 + 10; multicast 遍历 2*5+10+5? no -- 返回最后值)
        f -= PlusTen;
        Console.WriteLine(f(3));           // 6
        Func<int, int> g = Twice;
        Console.WriteLine(g(4));           // 8
    }
}}
```
注意：M3 委托返回值语义 = 链上最后一个返回（.NET multicast 语义）；测试用简单链验证。

- [ ] **Step 2: 验证 + 提交**

---

### Task 9: 帧记录三合一（帧栈 + GC 安全点 + StackTrace 基础）

**Files:**
- Create: `src/runtime/src/framestack.cpp`、`src/runtime/include/internal/il2cpp_frame.h`
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（方法序言压帧）
- Modify: `src/runtime/src/gc.cpp`（帧栈扫描接入根）

**Interfaces:**
- Consumes: Task 10（M2 帧根协议的升级）
- Produces:
  - `struct Il2CppFrame { Il2CppFrame* prev; const char* methodName; void** refLocals; int32_t refCount; };`（帧记录：方法名 + 引用局部槽数组）
  - `void il2cpp_frame_push(Il2CppFrame* frame)` / `il2cpp_frame_pop(Il2CppFrame* frame)`（线程局部帧栈）
  - 方法序言：构造帧记录 + push；返回处 pop（M2 WrapFrameRoots 升级）
  - GC 扫描：帧栈 → 每帧 refLocals 数组 → 根（替代 M2 的 add/remove root range）
  - `il2cpp_frame_dump()`（调试：打印当前帧栈——StackTrace 基础）

- [ ] **Step 1: 写失败测试**

- push/pop 正确配对（计数断言）
- GC 收集时帧栈引用对象存活（gcstress 用例改用帧栈——M2 的 add/remove range 移除）
- 多帧嵌套（递归）根保持

- [ ] **Step 2: 验证 + 提交**

---

### Task 10: 异常与帧栈集成（StackTrace + 顶层防护）

**Files:**
- Modify: `src/runtime/src/exception.cpp`、`src/runtime/src/framestack.cpp`
- Test: e2e

**Interfaces:**
- Consumes: Task 6/9
- Produces:
  - 异常抛出时回溯帧栈 → 重建托管堆栈（`at {Type}.{Method}({sig})` 行格式——.NET 风格）
  - 顶层防护：main 包 catch-all → 打印完整堆栈 + 非零退出码（用户铁律：绝不静默）
  - 异常路径帧 pop（C++ RAII：帧 push 用 RAII 守卫析构 pop——异常展开时自动执行）

- [ ] **Step 1: 写失败测试**

- 抛出未捕获异常 → 顶层打印方法名 + 非零退出
- catch 内读取异常消息
- 嵌套调用链 StackTrace 行数正确

- [ ] **Step 2: 验证 + 提交**

---

### Task 11: 值类型（struct + 装箱语义 + Nullable 基础）

**Files:**
- Modify: `src/il2cppc/`（值类型发射：struct 非 GC 对象 + box/unbox）
- Modify: `src/runtime/src/boxing.cpp`（通用 box/unbox）
- Test: e2e `tests/cases/valuetypes.cs`

**Interfaces:**
- Consumes: Task 1/6
- Produces:
  - 值类型 = C++ struct（非 GC 对象，栈/字段内联存储）
  - 值类型字段/参数/局部：按值传递（C++ 值语义）
  - box：值 → GC 对象（对象头 + 值拷贝）；unbox：类型校验 + 值拷贝
  - `Nullable<T>` 基础（hasValue + value 槽）
  - 值类型数组：连续存储（非引用数组）

- [ ] **Step 1: e2e valuetypes.cs**

```csharp
using System;
public struct Point {
    public int X; public int Y;
    public Point(int x, int y) { X = x; Y = y; }
}
namespace Cases { public static class ValueTypes {
    public static void Main() {
        Point p = new Point(3, 4);
        Console.WriteLine(p.X + p.Y);      // 7
        p.X = 10;
        Console.WriteLine(p.X);            // 10
        Point q = p;                        // copy
        q.X = 100;
        Console.WriteLine(p.X + q.X);      // 110
        object o = p;                       // box
        Point r = (Point)o;                 // unbox
        Console.WriteLine(r.X + r.Y);      // 110+4? (10+4=14)
        int? maybe = 5;
        Console.WriteLine(maybe.HasValue ? maybe.Value : -1); // 5
        int? none = null;
        Console.WriteLine(none.HasValue ? 1 : 0); // 0
    }
}}
```

- [ ] **Step 2: 验证 + 提交**

---

### Task 12: CI 扩展 + 文档 + final review

**Files:**
- Modify: `scripts/ci.ps1`（新 runtime 测试 target 自动纳入）
- Modify: `README.md`（M3 状态）
- Modify: `docs/superpowers/specs/2026-08-02-il2cpp-design.md`（偏差修订）

- [ ] **Step 1: 全量验证** `pwsh -NoProfile -File scripts/ci.ps1` → 全绿

- [ ] **Step 2: README 更新（M3 [x]，范围：分代 GC/异常/string/array/泛型/委托/值类型；M4 待办）**

- [ ] **Step 3: 提交 + final whole-branch review**

---

## Self-Review 记录

**1. Spec 覆盖（M3 范围）**：5.2 分代 GC→Task 2/3/4；5.3 异常→Task 6/10；5.4 帧记录→Task 9/10；5.5 委托→Task 8；string/array 核心类型→Task 1/5；泛型（4.4 + 铁律 2g）→Task 7；值类型装箱→Task 11。反射/icall 平台层/Marshal = M4。CLDR/COM/DLR = M6.5+。
**2. 占位符扫描**：Task 2/3/4 的 gc.cpp 重构为设计概要（接口签名明确，实现细节 TDD 中展开）；Task 7 泛型实例枚举细节在任务内细化。接口签名全部给出。
**3. 类型一致性**：`Il2CppString{obj,length,chars}` / `Il2CppArray{obj,length,elemSize,elemClass,data}` / `Il2CppDelegate{obj,methodPtr,target,next}` / `Il2CppFrame{prev,methodName,refLocals,refCount}` 全链路一致；`il2cpp_gc_alloc_size(klass,totalSize)` 由 Task 1 引入 Task 2-5 复用；DotNetException 在 Task 6 定义 Task 10 扩展。
**4. 风险**：分代 GC 压缩（Task 4）是高危区——引用更新遗漏 = 悬挂指针；异常体系（Task 6）的 finally 语义与 C++ RAII 结合需精心设计；泛型（Task 7）的共享代码 + 特化双轨是最大架构风险（IL2CPP 同款复杂度）。M3 每任务都必须三编译器 + 双跑逐字节验证。
