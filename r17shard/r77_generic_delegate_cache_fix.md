# r77 GenericDelegateCache 38个错误修复方案

**日期**: 2026-09-06
**分析范围**: r77shard 全部3个错误日志文件 + 生成产物源码
**错误总数**: 38 (全部与 `System.Threading.Tasks.GenericDelegateCache`2` 及其嵌套类型 `<>`c` 相关)

---

## 1. 错误枚举

通过 `Select-String -Path E:\il2cpp\r77shard\*_errors.log -Pattern "GenericDelegateCache"` 提取38条错误，按根因归类为3组：

| 组 | 错误模式 | 数量 | 来源文件 |
|----|----------|------|----------|
| A | `use of undeclared identifier 'System_Threading_Tasks_GenericDelegateCache_2_int_class'` | 1 | `___c_int.cpp:104` (newobj) |
| B | `use of undeclared identifier 'System_Threading_Tasks_GenericDelegateCache_2___c_int_int_sf___9'` | 5 | `___c_int.cpp:106` + `_int.cpp:88-91` (stsfld) |
| C | `use of undeclared identifier 'System_Threading_Tasks_GenericDelegateCache_2___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken_int___cctor_b__4_[0-3]_...'` | 4 | `entry.cpp:329-332` (delegate_register) |
| 其余为 note/重复行 | GCC "did you mean" 提示 | 28 | — |

**核心错误行**:
- `___c_int.cpp:104`: `il2cpp_object_new(&System_Threading_Tasks_GenericDelegateCache_2_int_class)` — 符号未声明
- `___c_int.cpp:106`: `System_Threading_Tasks_GenericDelegateCache_2___c_int_int_sf___9 = t0` — 双重下划线拼写错误（`___c_int_int_sf_` 应为 `___c_int_sf_`）
- `_int.cpp:88-91`: 同样的双重下划线拼写错误（4个静态字段赋值）
- `entry.cpp:329-332`: `reinterpret_cast<void*>(&___cctor_b__4_0_f591782821837f92)` — 方法符号未声明

---

## 2. 根因分析

### 2.1 类型结构

`System.Threading.Tasks.GenericDelegateCache`2<T>` 是 BCL 中用于缓存 `ContinueWith` 委托的泛型类型，其结构为：

- **外层类型**: `GenericDelegateCache`2<int>` → C++ 名 `System_Threading_Tasks_GenericDelegateCache_2_int`
  - 包含静态字段 `<>9`（缓存的 `<>`c 实例引用）
  - 包含静态字段 `CWAnyFuncDelegate`, `CWAnyActionDelegate`, `CWAllFuncDelegate`, `CWAllActionDelegate`（委托实例）
  - **无实例字段**、**无 .ctor 方法**
- **嵌套类型**: `<>`c`（编译器生成的缓存类）→ C++ 名 `System_Threading_Tasks_GenericDelegateCache_2___c_int`
  - 包含 `<>`9`（静态自身引用）
  - 包含 `<.cctor>b__4_0` 到 `<.cctor>b__4_3` 四个委托目标方法

### 2.2 根因 A：外层类型缺少 struct 定义和 class  descriptor

检查 `generated.h` 发现只有 forward declaration 和 static field storage：

```cpp
struct System_Threading_Tasks_GenericDelegateCache_2_int;  // 仅前向声明
extern bool System_Threading_Tasks_GenericDelegateCache_2_int_cctor_done;
void ensure_cctor_System_Threading_Tasks_GenericDelegateCache_2_int(void);
extern Il2CppObject* System_Threading_Tasks_GenericDelegateCache_2_int_sf_CWAnyFuncDelegate;
// ... (4 个静态委托字段)
```

**缺失**:
- `struct System_Threading_Tasks_GenericDelegateCache_2_int { ... };` — struct 定义
- `extern Il2CppClass System_Threading_Tasks_GenericDelegateCache_2_int_class;` — 类描述符声明
- 在 `_int.cpp` 中缺少 `Il2CppClass System_Threading_Tasks_GenericDelegateCache_2_int_class = { ... };` 定义

**代码位置**: `CodeGenerator.cs:557-631` (EmitStructDefinition)

```csharp
var hasBody = t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor");
// ...
if (!hasBody) return;  // <-- 外层类型在此返回，不发射 struct 和 descriptor
```

外层类型无实例字段、无 `.ctor`，所以 `hasBody = false`，被跳过。但它的静态字段是 GC 引用（delegate/object），需要 `il2cpp_object_new` 分配对象。

### 2.3 根因 B：静态字段名双重下划线拼写错误

观察生成代码中的命名不一致：

- **定义处** (`___c_int.cpp:53`):
  ```cpp
  Il2CppObject* System_Threading_Tasks_GenericDelegateCache_2___c_int_sf___9 = nullptr;
  ```
- **使用处** (`___c_int.cpp:106`):
  ```cpp
  System_Threading_Tasks_GenericDelegateCache_2___c_int_int_sf___9 = t0;  // 多了一个 "_int"
  ```
- **使用处** (`_int.cpp:88-91`):
  ```cpp
  ...System_Threading_Tasks_GenericDelegateCache_2___c_int_int_sf___9...  // 同样多一个 "_int"
  ```

这是一个静态字段名生成 bug：嵌套类型 `<>`c` 的静态字段 `<>`9` 在 stsfld 生成时，类型前缀错误地使用了嵌套类型的"外层+内层"拼接，但存储定义时只用了内层类型名。

**影响范围**: 5条错误（1处赋值 + 4处读取）。

### 2.4 根因 C：嵌套类型的委托目标方法未声明

`entry.cpp:329-332` 调用 `il2cpp_delegate_register_method` 注册 `___cctor_b__4_0` 等方法，这些方法定义在 `___c_int.cpp` 中，但:

- 嵌套类型 `<>`c` 的 **class descriptor 没有注册到 entry.cpp**（因为外层类型的 struct 未生成，嵌套类型的 newobj 也无法编译）
- entry.cpp 中调用 `il2cpp_class_register(&System_Threading_Tasks_GenericDelegateCache_2___c_int_class)` 但 `___c_int_class` 定义在 `___c_int.cpp` 中，该文件因上述错误无法编译，导致整个翻译单元失败

实际上，`___cctor_b__4_0_f591782821837f92` 等函数在 `___c_int.cpp:129` 有正确定义。问题在于：
1. `___c_int.cpp` 自身编译失败（根因 A 和 B 的连锁）
2. 因此这些符号在链接时不可达
3. entry.cpp 引用它们 → "undeclared identifier"

---

## 3. 修复方案

### 修复 1（高优先）：放宽 struct 和 descriptor 发射条件

**文件**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
**位置**: `EmitStructDefinition` 方法内 `hasBody` 计算处（约 line 575）

**当前逻辑**:
```csharp
var hasBody = t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor");
```

**修复**: 增加"具有 GC 引用类型的静态字段"作为触发条件：

```csharp
// 修复：包含引用类型静态字段的类型也需要 struct + descriptor
// 因为 il2cpp_object_new(&{T}_class) 需要 {T}_class 存在
var hasReferenceStaticField = t.StaticFields.Any(f =>
    f.IsStatic && !f.IsLiteral &&
    StaticFieldSemantics(f, md).Semantic == StaticFieldSemantic.Reference);
var hasBody = t.InstanceFields.Count > 0 ||
              t.Methods.Any(m => m.Name == ".ctor") ||
              hasReferenceStaticField;
```

**影响**: 确保 `System_Threading_Tasks_GenericDelegateCache_2_int` 生成 struct 定义和 `extern Il2CppClass ..._class` 声明。

### 修复 2（高优先）：放宽 descriptor 发射条件

**文件**: `CodeGenerator.cs`
**位置**: 类描述符发射条件（约 line 731-739）

**当前逻辑**:
```csharp
if ((!t.IsValueType && !IsDelegateTypeDef(t) &&
     (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor")
      || t.VirtualMethods.Count > 0 || t.IsInterface))
    || (t.IsValueType && !IsDelegateTypeDef(t) &&
        (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"))))
```

**修复**: 增加静态引用字段条件：

```csharp
var hasRefStatic = t.StaticFields.Any(f =>
    f.IsStatic && !f.IsLiteral &&
    StaticFieldSemantics(f, md).Semantic == StaticFieldSemantic.Reference);
if ((!t.IsValueType && !IsDelegateTypeDef(t) &&
     (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor")
      || t.VirtualMethods.Count > 0 || t.IsInterface || hasRefStatic))
    || (t.IsValueType && !IsDelegateTypeDef(t) &&
        (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor")
         || hasRefStatic)))
```

**影响**: 确保 `System_Threading_Tasks_GenericDelegateCache_2_int_class` 在 `_int.cpp` 中定义，在 `generated.h` 中声明。

### 修复 3（中优先）：修复嵌套类型静态字段名双重下划线

**文件**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`
**位置**: `EmitExpr` 中 `StaticFieldExpr` / `StaticFieldAddrExpr` 处理（约 line 3060-3080），或 `FieldStorageName` 方法（line 5788）

**分析**: `FieldStorageName` 的逻辑是 `TypeCppName(Owner)_sf_Sanitize(field)`。但嵌套类型 `<>`c` 的字段 `<>`9` 的 owner 是全名 `GenericDelegateCache`2<int>+<>`c`，应该生成 `System_Threading_Tasks_GenericDelegateCache_2___c_int_sf___9`。但使用处却生成了 `System_Threading_Tasks_GenericDelegateCache_2___c_int_int_sf___9`。

这个额外的 `_int` 表明在某处把外层类型名也拼进去了。

**修复**: 检查 `StackFold.cs` 中 stsfld 生成 `AssignStmt.Target` 时 `FieldName(md, ins)` 的返回值。`FieldName` 返回 `owner + "." + name`，其中 owner 来自 `ResolveField` 的 owner 部分。需要验证对于嵌套类型的字段，owner 是否是完整的外层+内层名（`GenericDelegateCache`2<int>+<>`c`）。

如果是，那么 `FieldStorageName` 中 `TypeCppName("System.Threading.Tasks.GenericDelegateCache`2<int>+<>c")` 应当返回 `System_Threading_Tasks_GenericDelegateCache_2___c_int`（因为 `<>`c` 是一个独立的 TypeDef/TypeRef）。需要检查 `TypeCppName` 和 `Sanitize` 对嵌套类型的处理。

**快速修复**: 在 `FieldStorageName` 中，如果 `fullName[..idx]` 包含嵌套分隔符 `+`，应当解析为嵌套类型并调用 `TypeCppName`（它会处理嵌套）：

```csharp
private static string FieldStorageName(string fullName)
{
    var idx = fullName.LastIndexOf('.');
    if (idx < 0) return fullName;
    var ownerPart = fullName[..idx];
    var fieldPart = fullName[(idx + 1)..];
    // 修复：owner 可能包含嵌套分隔符（如 GenericDelegateCache`2<int>+<>c）
    // 需要通过 TypeCppName 解析，而不是直接用 Sanitize
    if (ownerPart.Contains('+'))
    {
        // 嵌套类型：通过 TypeDef/TypeRef 解析其 C++ 名称
        var ownerDef = types.FirstOrDefault(t => t.FullName == ownerPart);
        if (ownerDef != null)
            return $"{TypeEmitter.TypeCppName(ownerDef)}_sf_{Sanitize(fieldPart)}";
    }
    return $"{Sanitize(ownerPart)}_sf_{Sanitize(fieldPart)}";
}
```

但这需要 `FieldStorageName` 访问 `types` 集合，当前是 private static 方法无参数。需要添加 `types` 参数（类似于 `FieldAccess`）。

**影响**: 修复后 `___c_int_int_sf___9` 会变成 `___c_int_sf___9`，与定义一致。

### 修复 4（中优先）：确保嵌套类型的 descriptor 在 entry.cpp 注册

**文件**: `CodeGenerator.cs`
**位置**: `EmitTypeRegistrations`（约 line 7500-7520）

当前注册循环应该已经覆盖所有有 descriptor 的类型。但需确认嵌套类型 `<>`c` 不会被 `IsDelegateTypeDef` 或其他守卫过滤掉。

---

## 4. 验证方案

修复后需要验证：

1. **编译零错误**:
   ```bash
   dotnet build src/il2cppc
   ```

2. **生成产物检查**:
   ```bash
   # 验证外层类型有 struct 定义
   Select-String -Path E:\il2cpp\out\mscorlib_gen_r77\include\internal\generated.h -Pattern "struct System_Threading_Tasks_GenericDelegateCache_2_int "
   # 验证有 class descriptor 声明
   Select-String -Path E:\il2cpp\out\mscorlib_gen_r77\include\internal\generated.h -Pattern "extern Il2CppClass System_Threading_Tasks_GenericDelegateCache_2_int_class"
   # 验证 _int.cpp 中有 class 定义
   Select-String -Path E:\il2cpp\out\mscorlib_gen_r77\src\System_Threading_Tasks_GenericDelegateCache_2_int.cpp -Pattern "^Il2CppClass System_Threading_Tasks_GenericDelegateCache_2_int_class"
   # 验证静态字段名一致（无双重下划线）
   Select-String -Path E:\il2cpp\out\mscorlib_gen_r77\src\System_Threading_Tasks_GenericDelegateCache_2___c_int.cpp -Pattern "System_Threading_Tasks_GenericDelegateCache_2___c_int_sf___9"
   ```

3. **抽查 r77 生成文件确认 cannot-init 减少**:
   比较修复前后 r77 错误日志中 GenericDelegateCache 相关错误数（应为 0）。

---

## 5. 风险评估

- 修复 1 和 2 会扩大 struct/descriptor 发射范围，可能影响所有具有引用静态字段但无实例字段/ctor 的类型。这是正确的，因为这些类型确实需要运行时分配。
- 修复 3 改变了嵌套类型字段的命名逻辑，可能影响其他嵌套类型。需要验证 `TypeCppName` 对所有嵌套类型的返回值是否正确（例如 `List`1+Node` 应映射到 `System_Collections_Generic_List_1_Node` 而非 `System_Collections_Generic_List_1+Node`）。
- 修复 4 是防御性检查，不应引入风险。

---

## 6. 工作量估计

| 修复 | 文件 | 行数 | 复杂度 |
|------|------|------|--------|
| 修复 1 | CodeGenerator.cs | ~5 行 | 低 |
| 修复 2 | CodeGenerator.cs | ~5 行 | 低 |
| 修复 3 | CodeGenerator.cs | ~20 行（含方法签名修改） | 中 |
| 修复 4 | CodeGenerator.cs | ~0 行（验证守卫） | 低 |
| **合计** | | **~30 行** | **低-中** |
