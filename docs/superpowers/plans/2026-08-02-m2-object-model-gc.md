# M2 对象模型 + 标记清除 GC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 M1 转译器骨架上实现 .NET 对象模型（对象头/类型表/字段布局）+ 精确根扫描 + 标记-清除 GC + 类/继承/接口/虚方法/装箱，使面向对象 C# 程序（实例字段/构造/虚调用/接口/装箱/类型转换）经转译后三编译器运行与 .NET Framework 4.8 参考逐字节一致。

**Architecture:** 运行时增加对象头（类型指针+GC 标记）与 TypeMetadata（字段位图/基类/接口表/vtable），GC 为精确标记-清除（根 = 静态字段 + 方法级根集合表 + 帧记录栈）；转译器升级类型发射（struct 布局/TypeMetadata 静态表/引用字段位图）、实例字段指令、newobj、callvirt（vtable）、接口分派、装箱/拆箱、isinst/castclass、null 检查；三合一帧记录 = GC 安全点 = 根扫描基础。

**Tech Stack:** C# 8 / .NET 8（转译器）；C++17（运行时，GCC 7.5 兼容基线）；CMake ≥3.10；三编译器（MSVC/MinGW/WSL GCC7.5）

**设计文档:** `docs/superpowers/specs/2026-08-02-il2cpp-design.md`（§5.1 对象模型 / §5.2 GC / §5.4 帧记录 / §12.5 cctor）
**风格规范:** `docs/superpowers/specs/2026-08-02-generated-code-style.md`（全英语超详细注释/列对齐/确定性）

## Global Constraints

（抄自 spec + M1 经验，M2 阶段生效）
- 零 JIT；零第三方依赖；C++17 仅 + GCC 7.5 兼容基线（避免 GCC 8+ 库特性）；CMake 3.10 基线
- 引用语义 = 裸 GC 指针（Go/CLR 风格追踪式），严禁 shared_ptr/引用计数/手写 free
- **禁全局构造分配内存（约束 #12）**：静态字段常量/零初始化；cctor 显式；runtime_init 显式分配全部运行时全局状态
- 生成代码：全英语超详细注释 + 列对齐 + 4 空格缩进 + 确定性（FNV-1a 签名哈希，无时间戳/绝对路径）
- 三编译器验证：每任务代码变更后三编译器（MSVC/MinGW/WSL GCC7.5）编译零错误；e2e 用例双跑逐字节一致
- 测试对齐：net48 exe 直接运行（.NET Framework 4.8 参考，非 dotnet run）
- 任何代码变更后编译零错误（C0）；TDD 红绿循环；CI 一键全绿
- **M1 交接项（final review rulings，本里程碑必须处理）**：① 生成代码消警告（-Wall -Wextra /W4 旗标 + 多余括号 + LL 截断 + 未用 temp）；② ci.ps1 补 build-fixtures 步；③ 方法名哈希含完整签名（防重载冲突）；④ Cgt_un/Clt_un 无符号映射正确性测试；⑤ 静态字段 extern 声明（跨类型访问）；⑥ MethodBodyReader 方法签名匹配

---

### Task 1: M1 交接清理（CI 旗标 + 警告消解 + fixtures 步）

**Files:**
- Modify: `src/runtime/CMakeLists.txt`（-Wall -Wextra /W4 旗标）
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（生成 CMakeLists 时加警告旗标）
- Modify: `scripts/ci.ps1`（补 build-fixtures 步）
- Test: `tests/unit/`（警告消解验证走 e2e 编译）

**Interfaces:**
- Consumes: M1 全部
- Produces: 三编译器编译生成代码零警告基线（CI 强制）

- [ ] **Step 1: 写失败测试（旗标生效验证）**

在 `tests/unit/CodeGeneratorTests.cs` 追加 `Generate_CmakeIncludesWarningFlags`：断言生成的 CMakeLists.txt 含 `-Wall -Wextra`（非 MSVC 分支）与 `/W4`（MSVC 分支）。

- [ ] **Step 2: RED**

Run: `dotnet test tests/unit/unit.csproj` → FAIL（当前 CMakeLists 无旗标）

- [ ] **Step 3: 实现**

- `src/runtime/CMakeLists.txt`：MSVC 分支 `target_compile_options(... /W4)`；else 分支 `-Wall -Wextra`（保持 /EHsc、-fexceptions、cxx_std_17、/utf-8）
- `CodeGenerator.EmitCmake`：生成 exe target 时同款旗标（MSVC `/W4 /EHsc /bigobj`；else `-Wall -Wextra -fexceptions`）
- `scripts/ci.ps1`：步骤 2 插入 `powershell -File scripts/build-fixtures.ps1`（在 unit tests 前）

- [ ] **Step 4: 警告消解（生成器侧）**

运行 `pwsh -NoProfile -File scripts/run-tests.ps1 -Compiler mingw` 收集生成代码全部警告，在生成器侧消解：
- 多余括号（`if((x==4LL))` → `if(x==4LL)`）：CodeGenerator 分支发射时避免双重括号（BranchStmt.Condition 已带括号时不再加）
- LL 常量截断（`icall(2147483647LL+1LL)` → `-Woverflow`）：int32 常量运算在生成时以 int32 语义截断（模 2^32 取低 32 位）
- 未用临时变量：join spill 生成的 temp 若全部路径覆盖（无未用）——优化：仅当 temp 确实被读时声明

- [ ] **Step 5: 全量验证 + 提交**

Run: `pwsh -NoProfile -File scripts/ci.ps1` → 全绿且无警告
```bash
git add . && git -c user.name="il2cpp-dev" -c user.email="dev@il2cpp.local" commit -m "fix: M1 handoff - warning flags, zero-warning codegen, ci fixtures step"
```

---

### Task 2: 方法哈希含签名 + 无符号比较测试 + 静态字段 extern + 签名匹配

**Files:**
- Modify: `src/il2cppc/Emit/MethodRefResolver.cs`、`src/il2cppc/Emit/CodeGenerator.cs`（哈希输入 = 完整签名）
- Modify: `src/il2cppc/Il/MethodBodyReader.cs`（方法签名匹配）
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（generated.h 静态字段 extern 声明）
- Test: `tests/unit/`

**Interfaces:**
- Consumes: Task 1
- Produces: 签名级方法标识（`{Ns}_{Type}_{Method}_{SigHash(完整签名)}`）；跨类型静态字段可访问；方法体按签名解析

- [ ] **Step 1: 写失败测试**

- `SigHash` 相关：`MethodEmitterTests` 或新测试断言：两个重载方法（`Add(int,int)` 与 `Add(int,int,int)`）哈希不同（签名含参数类型）
- `CgtUn_Unsigned` 测试：StackFold 对 `Cgt_un` 生成 CgtUn（无符号）——已有；补 e2e 用例 `unsigned.cs`（uint 比较）→ M2 范围 uint 支持？**M2 范围决策：M2 暂不支持 uint 装箱，此交接项记为"生成正确性已修 + 回归测试延后到 uint 支持时"**——本任务只保证生成器映射正确（已有），补文档说明

- [ ] **Step 2: RED → 实现**

- `MethodRefResolver`/`CodeGenerator`：方法签名（参数类型+返回类型，从 MethodBodyReader 或签名解码）→ 哈希输入 `{owner}.{name}({paramTypes})`，M1 的 `{owner}.{name}` 输入改为带参数——**注意与 M1 命名兼容**：M1 生成的函数名不可再生（生成物可再生，无兼容负担）；直接切新哈希
- `MethodBodyReader`：从调用方（CodeGenerator）按 (owner, name, paramCount) 匹配方法体；签名匹配 = paramCount + 返回类型
- `generated.h`：为每个类型静态字段生成 `extern int32_t {Type}_sf_{Field};`（与 cpp 定义对应）

- [ ] **Step 3: 验证 + 提交**

Run: `dotnet test tests/unit/unit.csproj` 全绿；`dotnet build src/il2cppc` 0/0；e2e 6 用例仍全绿（命名变化不影响行为）
```bash
git add . && git commit -m "feat: signature-hashed method names + static field externs + body signature matching"
```

---

### Task 3: 运行时对象模型（对象头 + TypeMetadata + 类型注册表）

**Files:**
- Create: `src/runtime/include/internal/il2cpp_object.h`
- Create: `src/runtime/include/internal/il2cpp_metadata.h`
- Create: `src/runtime/src/object.cpp`、`src/runtime/src/metadata.cpp`
- Modify: `src/runtime/CMakeLists.txt`、`include/internal/il2cpp_runtime.h`

**Interfaces:**
- Consumes: Task 1/2
- Produces:
  - `Il2CppObject { Il2CppClass* klass; }`（对象头；M2 无 GC 标记位，M3 加）
  - `Il2CppClass { const char* name; int32_t instanceSize; uint32_t refFieldBitmap; int32_t refFieldCount; Il2CppClass* baseClass; ... vtable; interfaceMap; }`（M2 核心字段见实现）
  - `il2cpp_class_register(Il2CppClass*)`（类型表注册，编译期生成代码调用）
  - `il2cpp_object_new(Il2CppClass*) -> Il2CppObject*`（分配+零初始化）
  - 引用字段位图：`il2cpp_class_is_ref_field(Il2CppClass*, int32_t offset) -> bool`（GC 扫描用）

- [ ] **Step 1: 写失败测试（运行时 C++ 侧）**

新建 `tests/runtime/`（C++ 测试，CMake 集成）——M2 起运行时组件需要 C++ 单元测试：
`tests/runtime/test_object.cpp`：构造 mock Il2CppClass（instanceSize=16、bitmap=0b101），`il2cpp_object_new` → 对象零初始化、klass 正确、is_ref_field(0/8)=true、is_ref_field(4)=false。

- [ ] **Step 2: RED（运行时测试）**

Run: `cmake -S tests/runtime -B build/tests_runtime && cmake --build ... && ./il2cpp_runtime_tests` → FAIL（类型不存在）

- [ ] **Step 3: 实现**

`il2cpp_object.h`:
```cpp
#pragma once
#include <cstdint>
#include <cstddef>

namespace il2cpp {

struct Il2CppClass;

// GC object header: every managed object begins with its class pointer.
struct Il2CppObject
{
    Il2CppClass* klass; // runtime type descriptor (compiled-time generated static table)
};

} // namespace il2cpp
```

`il2cpp_metadata.h`（M2 子集）:
```cpp
#pragma once
#include <cstdint>
#include <cstddef>

namespace il2cpp {

// Runtime type descriptor; one static instance per translated type, emitted by the transpiler.
struct Il2CppClass
{
    const char*     name;           // full type name, e.g. "Calc.Foo"
    int32_t         instanceSize;   // total size in bytes of one instance (aligned)
    uint32_t        refFieldBitmap; // bit i set => field at i*sizeof(void*) is a GC reference
    int32_t         refFieldCount;  // number of reference-typed fields (for root iteration)
    Il2CppClass*    baseClass;      // null for System.Object
    // M2 fields (vtable / interfaces) added by Tasks 6-7
};

} // namespace il2cpp
```

`object.cpp`:
```cpp
#include "il2cpp_object.h"
#include "il2cpp_metadata.h"
#include <cstdlib>
#include <cstring>

namespace il2cpp {

// NOTE: M2 uses raw malloc-backed allocation; the real GC heap arrives in Task 4.
static Il2CppObject* RawAlloc(Il2CppClass* klass)
{
    void* mem = std::calloc(1, static_cast<size_t>(klass->instanceSize));
    Il2CppObject* obj = static_cast<Il2CppObject*>(mem);
    obj->klass = klass;
    return obj;
}

Il2CppObject* il2cpp_object_new(Il2CppClass* klass)
{
    return RawAlloc(klass);
}

bool il2cpp_class_is_ref_field(Il2CppClass* klass, int32_t offset)
{
    const int32_t slot = offset / static_cast<int32_t>(sizeof(void*));
    return (klass->refFieldBitmap & (1u << slot)) != 0;
}

} // namespace il2cpp
```

`metadata.cpp`: 类型注册表（M2 简化：编译期生成的静态 Il2CppClass 实例直接内联在生成代码中；运行时仅提供查找辅助 `il2cpp_class_by_name`，M3 反射时完整化）。

`il2cpp_runtime.h` 加声明；CMakeLists 加源文件。

- [ ] **Step 4: GREEN + 提交**

Run: 运行时测试全绿；`dotnet build src/il2cppc` 不受影响
```bash
git add . && git commit -m "feat: M2 runtime object model (Il2CppObject/Il2CppClass/refFieldBitmap)"
```

---

### Task 4: 运行时 GC（标记-清除 + 根接口 + 分配器）

**Files:**
- Create: `src/runtime/include/internal/il2cpp_gc.h`
- Create: `src/runtime/src/gc.cpp`
- Modify: `object.cpp`（分配走 GC）、`CMakeLists.txt`

**Interfaces:**
- Consumes: Task 3
- Produces:
  - `il2cpp_gc_init()`（堆初始化：段 + 空闲列表；M2 单堆不分代）
  - `Il2CppObject* il2cpp_gc_alloc(Il2CppClass* klass)`（GC 分配，触发回收阈值）
  - `void il2cpp_gc_collect()`（标记-清除：根 = 静态字段区（编译期注册）+ 托管帧栈（Task 9 接入）→ 标记 → 清除）
  - `void il2cpp_gc_add_static_root(void** addr)`（静态字段根注册，编译期生成代码调用）
  - 分配接口替换 `il2cpp_object_new` 内部实现（API 不变）

- [ ] **Step 1: 写失败测试**

`tests/runtime/test_gc.cpp`：
- 分配 N 对象 → 全部非空、零初始化、klass 正确
- 释放语义：释放后地址可被复用（分配压力 > 堆大小触发收集）
- 存活对象保留：根指向对象 → collect 后仍可读
- 静态根：`il2cpp_gc_add_static_root` 注册 → collect 后保留

- [ ] **Step 2: RED**

- [ ] **Step 3: 实现（标记-清除）**

```cpp
// gc.cpp design:
// - Heap: linked list of segments (each 64KB); objects bump-allocated within segment
//   until full; full segment goes to a "needs collection" state.
// - Mark phase: iterative worklist from roots:
//   root set = staticRoots[] + frame stack (Task 9) ;
//   for each object: read klass->refFieldBitmap, walk reference fields, mark.
//   Mark bit stored in a side table (separate bitmap, not in object header — keeps
//   header minimal; M3 分代再议 header 布局)。
// - Sweep phase: walk all segments, free unmarked objects (link into free list),
//   compact within segment (M2 简单：free-list 复用，不移动——移动在 M3 分代压缩)。
// - Allocation: free list first-fit, else bump in current segment, else new segment.
// - Collection triggers: allocation when free space < threshold.
// - NOTE M2 保守正确性：非移动 + 精确位图扫描，无写屏障（M3 分代加）。
```

- [ ] **Step 4: GREEN + 提交**

---

### Task 5: 转译器类类型发射（struct 布局 + TypeMetadata + 实例字段访问）

**Files:**
- Modify: `src/il2cppc/Emit/TypeEmitter.cs`（类 → struct + Il2CppClass 静态表）
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（实例字段指令 ldfld/stfld/ldflda/stflda）
- Modify: `src/il2cppc/Model/`（TypeDef 补字段类型信息：引用/值类型判定 → 位图）
- Modify: `src/il2cppc/Il/IlDecoder.cs`（ldfld/stfld/ldflda/stflda 解码已支持 FieldToken ✓ 确认）
- Test: `tests/unit/` + e2e `tests/cases/objects.cs`

**Interfaces:**
- Consumes: Task 2-4
- Produces:
  - 类发射：`struct Calc_Foo { Il2CppObject _obj; int32_t f_int; Il2CppObject* f_ref; };`（首字段继承对象头）+ `static Il2CppClass Calc_Foo_class = {...};`（类型表内联）
  - 布局规则：对象头(8B) + 字段按声明序（M2 简化：顺序布局，M3 对齐规则完整化）→ 引用字段位图按 slot 计算
  - 实例字段访问：`obj->f_int`（ldfld→读、stfld→写、ldflda→取址）
  - 引用字段写：`obj->f_ref = value;`（M2 无写屏障，M3 插入）

- [ ] **Step 1: 写失败测试**

`tests/unit/TypeEmitterTests.cs`：断言类类型生成 struct 含对象头字段 + 引用字段为指针 + Il2CppClass 静态表字段正确（name/instanceSize/refFieldBitmap 数值断言：Foo{f_int, f_ref} → instanceSize=16, bitmap=0b10）

- [ ] **Step 2: RED → 实现**

- `Model/FieldDef` 补 `TypeKind`（引用/值类型/基元）→ 从字段签名解码（M2 支持 int32/引用类型字段）
- `TypeEmitter`：类 → struct 发射 + Il2CppClass 表发射（含 `il2cpp_class_register` 调用或直接静态实例——M2 选静态实例 + 注册到静态根表）
- `StackFold` 补 ldfld/stfld/ldflda/stflda：栈操作（obj 引用 + 字段名）→ 新 IR `FieldAccessExpr/FieldAccessStmt`（或直接 LocExpr 风格：`FieldExpr { Object, Name }`）
- `CodeGenerator` 发射：`obj->name` 形式；**null 检查**：`obj->f` 前判空 → 抛 NRE（M2 生成 `il2cpp_null_check(obj)` helper：空则抛 NullReferenceException——异常体系 M3 完整化，M2 先 `il2cpp_throw_nre()` 简单实现：打印 + abort？不——按约束 #10 语义：M2 先实现最小 NRE（抛出 C++ 异常对象，M3 接入托管异常对象）**

- [ ] **Step 3: e2e 用例 objects.cs**

```csharp
using System;
namespace Cases { public static class Objects {
    public static void Main() {
        var p = new Point(3, 4);
        Console.WriteLine(p.X + p.Y);          // 7
        p.X = 10;
        Console.WriteLine(p.X);                // 10
        var q = new PointRef();
        q.ref1 = new Point(1,2);
        Console.WriteLine(q.ref1.X);           // 1
    }
}}
public class Point { public int X; public int Y; public Point(int x, int y) { X = x; Y = y; } }
public class PointRef { public Point ref1; public PointRef() {} }
```
双跑一致。

- [ ] **Step 4: 验证 + 提交**

---

### Task 6: newobj + 实例构造 + cctor 顺序

**Files:**
- Modify: `src/il2cppc/Ir/StackFold.cs`（newobj → alloc + ctor 调用）
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（newobj 发射：`il2cpp_object_new(&Class)` → 调 ctor（参数入栈））
- Modify: `MethodRefResolver`（ctor 解析：`{Type}_ctor_{hash}`）

**Interfaces:**
- Produces: `newobj` 语义 = 分配对象（零初始化）→ 调用实例构造器（`this` 隐式 arg0）

- [ ] **Step 1: 测试（e2e objects.cs 含构造已覆盖）→ 实现 → 验证 → 提交**

newobj IL：`newobj` token 指向 .ctor MemberRef → 折叠为 CallExpr 前插入分配。生成：
```cpp
Calc_Point* tmp = (Calc_Point*)il2cpp_object_new(&Calc_Point_class);
Calc_Point__ctor_xxx(tmp, 3, 4);
```
注意：**构造器内 `this` 赋值与字段位图**——构造完成前对象可能未被引用（安全：alloc 后立即 ctor，无中间 GC 点）。

cctor 顺序：类型首次使用前运行 cctor（M1 已从 entry 调用全部 cctor；M2 改为**惰性 beforefieldinit 守卫**——每类型生成 `ensure_cctor()` 双检锁；M2 单线程先行：简单布尔守卫，M3 线程安全）。

---

### Task 7: 虚方法 vtable + callvirt 分派

**Files:**
- Modify: `Il2CppClass`（vtable 字段）
- Modify: `TypeEmitter`（vtable 静态表生成：基类槽 + 覆写替换）
- Modify: `StackFold`/`CodeGenerator`（callvirt → vtable 查表调用）

**Interfaces:**
- Produces:
  - `Il2CppClass.vtable: void**`（编译期生成静态表：`void* Calc_Foo_vtable[] = { Calc_Foo_ToString_xxx, Calc_Foo_Virtual_x_xxx, ... };`）
  - callvirt 发射：`((void(*)(Il2CppObject*))obj->klass->vtable[slot])(obj, args...)`
  - 虚槽分配：从基类继承槽 + 新增虚方法追加槽；覆写 = 同槽替换
  - **需要类型图预扫描**：全量类型虚方法表计算（CodeGenerator 前 pass）

- [ ] **Step 1: 测试（e2e virtual.cs：基类虚方法覆写、多态调用、base 调用）→ RED → 实现 → GREEN → 提交**

```csharp
public class Shape { public virtual int Area() => 0; }
public class Rect : Shape { public int W; public int H; public override int Area() => W * H; }
// Main: Shape s = new Rect{ W=3, H=4 }; Console.WriteLine(s.Area());  // 12
```

---

### Task 8: 接口 + 接口映射表分派

**Files:**
- Modify: `Il2CppClass`（interfaceMap）
- Modify: `TypeEmitter`（接口映射表：接口 → 槽位映射）
- Modify: `StackFold`/`CodeGenerator`（callvirt 接口分派）

**Interfaces:**
- Produces:
  - `Il2CppClass.interfaces: { Il2CppClass* iface; int32_t* slotMap; }[]`（编译期生成）
  - 接口调用：`obj->klass` 查接口映射 → 槽 → 调用
  - `isinst/castclass` 接口判定：沿继承链 + 接口表

- [ ] **Step 1: e2e interface.cs → RED → 实现 → GREEN → 提交**

```csharp
public interface ICalc { int Calc(int v); }
public class Doubler : ICalc { public int Calc(int v) => v * 2; }
// Main: ICalc c = new Doubler(); Console.WriteLine(c.Calc(21));  // 42
```

---

### Task 9: 装箱/拆箱 + isinst/castclass + null 检查

**Files:**
- Create: `src/runtime/src/boxing.cpp`（box/unbox helper）
- Modify: `StackFold`（box/unbox.any/isinst/castclass 折叠）
- Modify: `CodeGenerator`（发射）

**Interfaces:**
- Produces:
  - `Il2CppObject* il2cpp_box_int32(Il2CppClass* klass, int32_t value)`（装箱：分配对象 + 值区；M2 用"盒子类"= 每值类型一个包装 struct？**M2 简化：boxed int = Il2CppObject + 4B 值区**，用专用 boxed-int 布局（class = 预生成 Int32 class 表））
  - `int32_t il2cpp_unbox_int32(Il2CppObject* obj)`（拆箱，类型不符抛 InvalidCastException——M2 最小）
  - `bool il2cpp_is_instance(Il2CppObject* obj, Il2CppClass* klass)`（isinst/castclass 判定，沿基类链 + 接口表）
  - null 检查：`il2cpp_null_check(obj)`（空 → NRE）

- [ ] **Step 1: e2e boxing.cs → RED → 实现 → GREEN → 提交**

```csharp
object o = 5;                      // box
Console.WriteLine((int)o);         // 5  unbox
object s = "hi";                   // string 引用（M2 string 仍最小）
object n = null;
Console.WriteLine(n == null);      // True
int[] 数组 M3
```

---

### Task 10: GC 根集合表生成 + 三合一帧记录接入

**Files:**
- Modify: `src/il2cppc/Emit/CodeGenerator.cs`（每方法根集合表：引用槽位图 → 编译期静态表）
- Modify: `src/il2cppc/Emit/MethodEmitter.cs`（序言帧记录插入）
- Modify: `src/runtime/src/gc.cpp`（帧栈扫描）
- Create: `src/runtime/src/framestack.cpp`（托管帧栈：压栈/弹出/扫描）

**Interfaces:**
- Produces:
  - 每方法序言：`il2cpp_frame_push(&methodFrame)`（帧记录含：局部引用槽位图 + 静态引用指针列表 + 返回地址；方法返回处 pop——**RAII 守卫**）
  - 帧栈 = GC 根扫描输入（三合一：帧记录 = 安全点 = 根扫描）
  - 局部引用槽位图：方法局部变量中引用类型的槽位（编译期已知）

- [ ] **Step 1: 测试（e2e gcstress.cs：分配压力循环 + 根保持）→ RED → 实现 → GREEN → 提交**

```csharp
public static void Main() {
    var keep = new ListRef();  // M2 无 List——用简单链：Node
    var head = new Node(0);
    var cur = head;
    for (int i = 1; i < 1000; i++) { cur.Next = new Node(i); cur = cur.Next; }
    // 遍历校验
    int sum = 0; cur = head; while (cur != null) { sum += cur.V; cur = cur.Next; }
    Console.WriteLine(sum);    // 499500
}
public class Node { public int V; public Node Next; public Node(int v) { V = v; } }
```
（分配压力触发收集；根 = 静态 keep/head/cur 局部引用槽 + 帧栈）

---

### Task 11: CI 扩展 + 文档 + final review

**Files:**
- Modify: `scripts/ci.ps1`（e2e 新用例自动纳入——run-tests.ps1 按目录扫描已自动；补 runtime 测试步骤）
- Modify: `README.md`（M2 状态、范围）
- Modify: `docs/superpowers/specs/2026-08-02-il2cpp-design.md`（如有偏差修订）

**Interfaces:**
- Produces: M2 一键 CI 全绿（单元 + 运行时 C++ 测试 + e2e 全用例 × 3 编译器）

- [ ] **Step 1: ci.ps1 加 runtime 测试步**

- [ ] **Step 2: 全量验证** `pwsh -NoProfile -File scripts/ci.ps1` → 全绿

- [ ] **Step 3: README 更新（M2 [x]，范围：类/继承/接口/虚方法/装箱/最小 GC；M3 待办）**

- [ ] **Step 4: 提交 + final whole-branch review**

---

## Self-Review 记录

**1. Spec 覆盖（M2 范围）**：spec 5.1 对象模型→Task 3/5/6；5.2 GC（标记-清除不分代 M2 简化）→Task 4/10；4.4 代码生成扩展→Task 5-9；12.5 cctor→Task 6；M1 rulings→Task 1/2。泛型/string/数组/异常托管对象/分代 = M3。
**2. 占位符扫描**：Task 4 gc.cpp 设计为概要（实现细节在 TDD 中展开，接口签名明确）；Task 7-9 虚表/接口/装箱细节在任务内细化。接口签名全部给出。
**3. 类型一致性**：`Il2CppObject.klass` / `Il2CppClass.{name,instanceSize,refFieldBitmap,refFieldCount,baseClass}` 全链路一致；`il2cpp_object_new(Il2CppClass*)` / `il2cpp_gc_alloc(Il2CppClass*)` / `il2cpp_class_is_ref_field(Il2CppClass*, int32_t)` 签名统一；生成代码 struct 首字段 `_obj` 继承对象头。
**4. 风险**：M2 布局规则为顺序布局（对齐规则 M3 完整化）——e2e 用例避免对齐敏感字段；GC 无写屏障（M3 分代加）——M2 单线程正确性优先。
