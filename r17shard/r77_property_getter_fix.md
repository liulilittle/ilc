# r77 PropertyInfo+PropertyGetter 错误分析报告

**分析时间**：2026-09-06  
**数据源**：r77 三片错误日志 + mscorlib_gen_r77 生成文件 + il2cppc 源码  
**结论**：r77 中 **不存在** `System.Reflection.PropertyInfo+PropertyGetter` 相关错误。该嵌套类型在 r77 生成输出中完全缺失。

---

## 一、搜索结果

### 1.1 错误日志搜索

```powershell
Select-String -Path "E:\il2cpp\r77shard" -Pattern "PropertyGetter|PropertyInfo"
```

**结果**：0 条匹配（三片错误日志均无）

### 1.2 生成文件搜索

```powershell
Select-String -Path "E:\il2cpp\out\mscorlib_gen_r77" -Pattern "PropertyGetter"
```

**结果**：0 条匹配（src + include 均无）

### 1.3 全仓源码搜索

```powershell
Select-String -Path "E:\il2cpp" -Pattern "PropertyInfo\+PropertyGetter|PropertyInfo_PropertyGetter"
```

**结果**：0 条匹配

### 1.4 il2cppc 源码搜索

```powershell
Select-String -Path "E:\il2cpp\src\il2cppc" -Pattern "PropertyGetter"
```

**结果**：0 条匹配

---

## 二、r77 生成范围内的 PropertyInfo 相关类型

| 类型 | 状态 |
|---|---|
| `System.Reflection.PropertyInfo` | ✅ 已生成（64KB .cpp + struct + vtable + descriptor） |
| `System.Reflection.RuntimePropertyInfo` | ✅ 已生成（136KB .cpp） |
| `System.Runtime.InteropServices._PropertyInfo` | ✅ 已生成 |
| `System.Reflection.Emit.PropertyBuilder` | ✅ 已生成 |
| `System.Reflection.Emit.PropertyToken` | ✅ 已生成 |
| `System.Reflection.PropertyAttributes` | ✅ 已生成 |
| `System.Reflection.PropertyInfo+PropertyGetter` | ❌ **不存在** |

---

## 三、根因分析

### 3.1 为何不存在？

`PropertyInfo+PropertyGetter` 是 .NET Framework mscorlib 中的 **内部嵌套类型**（`private`/`internal`），用于缓存属性 getter 的委托引用。在 mscorlib.dll 的元数据中：

```csharp
// mscorlib internal type
private class PropertyGetter {
    public PropertyInfo m_property;
    public Delegate m_delegate;
    // ...
}
```

### 3.2 MetadataLoader 的嵌套类型过滤

`MetadataLoader.cs:36` 存在过滤规则：

```csharp
if (td.IsNested && !md.GetString(td.Name).StartsWith("<>")) continue;
```

这意味着：
- **只保留** 以 `<>` 开头的编译器生成的嵌套类型（lambda 缓存类、async 状态机等）
- **跳过** 所有其他内部嵌套类型（如 `PropertyInfo+PropertyGetter`）

### 3.3 TypeEmitter.ExpandNestedTypes 的影响

`TypeEmitter.cs:90-110` 的 `ExpandNestedTypes` 方法负责将嵌套类型展开为独立顶层条目，但它的碰撞守卫会跳过已存在的类型：

```csharp
if (types.Concat(added).Any(t => t.FullName == fullName))
{
    continue; // skip duplicate nested type
}
```

### 3.4 缺失触发条件

`PropertyGetter` 未被生成的完整链路：

1. **MetadataLoader** 遇到 `PropertyInfo+PropertyGetter`（`IsNested=true`, `Name="PropertyGetter"`）
2. 名称不以 `<>` 开头 → **跳过**
3. `ExpandNestedTypes` 可能尝试加载，但若类型已以短名存在 → **跳过**
4. 生成代码中引用该类型时 → **找不到定义** → 编译错误

### 3.5 r77 实际错误模式

r77 的错误主要是：
- `cannot initialize a parameter of type 'int32_t *' with ... 'intptr_t *'`（StrongNameHelpers）
- `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'`（AggregateException）
- `use of undeclared identifier`（嵌套 CG 类型、ValueTuple 相关）

**没有 `PropertyGetter` 相关错误**。

---

## 四、修复方案（若未来需要生成）

### 4.1 最小改动：将 PropertyGetter 加入允许列表

在 `MetadataLoader.cs:36` 修改过滤条件：

```csharp
// 当前：
if (td.IsNested && !md.GetString(td.Name).StartsWith("<>")) continue;

// 修改为：
if (td.IsNested && !md.GetString(td.Name).StartsWith("<>") 
    && !IsKnownNestedType(md, td)) continue;

static bool IsKnownNestedType(MetadataReader md, TypeDefinition td)
{
    var name = md.GetString(td.Name);
    // 添加 PropertyGetter 等内部嵌套类型白名单
    return name is "PropertyGetter" or "PropertySetter" /* ... */;
}
```

### 4.2 更优方案：ExpandNestedTypes 保留引用链

若 `PropertyGetter` 被 IL 代码直接引用（callvirt/newobj/ldtoken），则：
1. 在 `ExpandNestedTypes` 中解析其字段（`m_property` 指向 `PropertyInfo`，`m_delegate` 指向 `Delegate`）
2. 生成独立 struct `System_Reflection_PropertyGetter`
3. 生成对应的 descriptor 和方法

### 4.3 风险评估

| 影响 | 说明 |
|---|---|
| 触发条件 | 仅当 IL 代码直接引用 `PropertyInfo+PropertyGetter` 时 |
| 命名冲突 | `PropertyGetter` 是短名，需确保不与外层类型冲突 |
| 依赖链 | 需要 `PropertyInfo` 和 `Delegate` 已定义 |
| 回归风险 | 低。此前不生成，未来生成只会新增定义 |

---

## 五、建议

### 5.1 验证步骤

1. 确认 mscorlib.dll 中是否存在 `PropertyInfo+PropertyGetter`：
   ```powershell
   ildasm /text mscorlib.dll | findstr "PropertyGetter"
   ```
2. 检查 IL 代码中是否有对该类型的引用（ldtoken/newobj/callvirt）
3. 若有引用，确认是 internal 路径还是公开 API 反射

### 5.2 若确认需要

按 4.1 方案实现，修改 `MetadataLoader.cs:36` 的过滤条件，添加白名单。

---

## 六、结论

**r77 中不存在 `System.Reflection.PropertyInfo+PropertyGetter` 相关错误。** 该类型在 r77 生成输出中完全缺失（既无生成文件，也无编译错误）。

根本原因是 `MetadataLoader` 的嵌套类型过滤规则（只保留 `<>` 开头的编译器生成类型），`PropertyGetter` 作为内部嵌套类型被跳过。

---

**分析文件**：`E:\il2cpp\r17shard\r77_property_getter_fix.md`
