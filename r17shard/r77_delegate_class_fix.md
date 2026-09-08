# R77 Ambiguous `System_Delegate_class` 修复方案

## 错误根因

```
E:\il2cpp\out\mscorlib_gen_r77\src\entry.cpp:567:36: error: reference to 'System_Delegate_class' is ambiguous
    E:/.../generated.h:110212:20: candidate: 'System_Delegate_class'          ← 全局命名空间
    E:/.../il2cpp_delegate.h:37:20: candidate: 'il2cpp::System_Delegate_class' ← namespace il2cpp
```

**同一符号存在两份声明**：

| 文件 | 命名空间 | 行号 |
|------|----------|------|
| `generated.h` | 全局 | 6251, 110212 |
| `il2cpp_delegate.h` | `namespace il2cpp` | 37 |
| `dictsi_obj.h` | `namespace il2cpp` | 97, 98, 132 |

`generated.h` 第 121 行有 `using namespace il2cpp;`，导致 `il2cpp::System_Delegate_class` 被引入全局命名空间，与 `generated.h` 自身声明的全局 `System_Delegate_class` 产生歧义。

---

## 受影响范围

**41 个 ambiguous 错误**，涉及：
- `System_Delegate_class` — 31 次（所有 `castclass`/`isinst`/`newarr` 到 Delegate 的代码）
- `System_Collections_Generic_Dictionary_2_string_int_class` — 2 次
- `System_Collections_Generic_Dictionary_2_Enumerator_string_int_class` — 4 次
- `System_Collections_Generic_KeyValuePair_2_string_int_class` — 4 次

---

## 修复方案

### 方案 A（推荐）：在生成器中将 extern 声明包裹到 `namespace il2cpp`

**修改文件**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`

**位置**: `EmitStructDefinition` 方法中 `extern Il2CppClass` 声明处

当前代码（约 line 572/628）：
```cpp
generatedHeader.Line($"extern Il2CppClass {TypeEmitter.TypeCppName(t)}_class;");
```

修改为：
```cpp
generatedHeader.Line($"namespace il2cpp {{ extern Il2CppClass {TypeEmitter.TypeCppName(t)}_class; }}");
```

或者更优雅地，在 `generated.h` 的**最顶部**添加一次 `namespace il2cpp {`，在**最底部**添加 `}`：

```cpp
// generated.h 第 ~90 行（在所有 #include 之后）
namespace il2cpp {

// ... 所有现有的 extern 声明 ...

// generated.h 末尾的最后一个 }
} // namespace il2cpp

// 保持 using namespace 以兼容现有代码
using namespace il2cpp;
```

**效果**: `generated.h` 中的 `extern Il2CppClass X_class;` 与 `il2cpp_delegate.h`/`dictsi_obj.h` 中的声明处于同一命名空间 (`namespace il2cpp`)，引用同一符号，歧义消除。

**风险**:
- `struct` 定义（如 `Il2CppDateTime`）也在 `generated.h` 中生成，它们同样需要进入 `namespace il2cpp`
- 所有 `struct` 前向声明也需要同步包裹
- 运行时头文件中的类型引用不受影响（它们已经通过 `using namespace il2cpp;` 可见）

---

### 方案 B（最小改动）：从运行时头文件中移除 `namespace il2cpp` 包装

**修改文件**: `il2cpp_delegate.h`, `dictsi_obj.h`, `dictsi.h`

将：
```cpp
namespace il2cpp {
  extern Il2CppClass System_Delegate_class;
}
```

改为：
```cpp
extern Il2CppClass System_Delegate_class;
```

**风险**: 运行时头文件中有大量符号依赖 `namespace il2cpp`（如 `Il2CppObject`, `Il2CppClass`），移出会导致更大量的编译错误。**不推荐**。

---

### 方案 C（快速 workaround）：在 generated.h 中用宏别名

```cpp
// generated.h 末尾，在 using namespace 之前
#define System_Delegate_class il2cpp::System_Delegate_class
```

**风险**: 宏替换会影响所有同名文本，可能误伤。**不推荐**。

---

## 推荐实施方案

**方案 A** 是最彻底且一致的修复。具体步骤：

1. 在 `CodeGenerator.cs` 中找到所有 `generatedHeader` 写入 `extern Il2CppClass` 的位置（约 4 处）
2. 在 `generatedHeader` 初始写入 `#pragma once` 之后、`#include` 块之前添加：
   ```cpp
   namespace il2cpp {
   ```
3. 在 `GenerateCore` 方法的最后（`generatedHeader` 内容写入文件之前），在末尾添加：
   ```cpp
   } // namespace il2cpp
   ```
4. 保留现有的 `using namespace il2cpp;` 以确保所有生成的 `.cpp` 文件（它们引用的是 `il2cpp::` 下的运行时类型）仍然可以无修饰访问这些符号

**验证**：
- 重新生成 mscorlib 后，检查 `generated.h` 中 `System_Delegate_class` 是否在 `namespace il2cpp { ... }` 内
- 编译不再报 ambiguous
- 所有 `.cpp` 文件仍能通过 `using namespace il2cpp;` 访问该符号

---

## 关联修复

同一问题也影响 `dictsi_obj.h` 中的 `System_Collections_Generic_Dictionary_2_*_class` 系列符号。方案 A 的 namespace 包裹同时消除所有这些歧义，无需逐个处理运行时头文件。
