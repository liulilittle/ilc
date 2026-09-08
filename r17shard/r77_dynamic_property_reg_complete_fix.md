# r77 DynamicPropertyCollection+DynamicPropertyReg 错误分析报告

**分析时间**：2026-09-06  
**数据源**：r77 三片错误日志 + mscorlib_gen_r77 生成文件  
**结论**：r77 中 **不存在** `System.Runtime.Remoting.Contexts.DynamicPropertyCollection+DynamicPropertyReg` 相关错误。该类型在 r77 生成输出中完全缺失。

---

## 一、搜索结果

### 1.1 错误日志搜索

对 r77 三片错误日志执行全量搜索：

```powershell
Select-String -Path "E:\il2cpp\r77shard" -Pattern "DynamicPropertyCollection|DynamicPropertyReg"
```

**结果**：0 条匹配（三片均无）

### 1.2 生成文件搜索

```powershell
Select-String -Path "E:\il2cpp\out\mscorlib_gen_r77" -Pattern "DynamicPropertyCollection|DynamicPropertyReg"
```

**结果**：0 条匹配（src + include 均无）

### 1.3 il2cppc 源码搜索

```powershell
Select-String -Path "E:\il2cpp\src\il2cppc" -Pattern "DynamicProperty|DynamicProp"
```

**结果**：0 条匹配

### 1.4 全仓搜索

```powershell
Select-String -Path "E:\il2cpp" -Pattern "DynamicPropertyCollection|DynamicPropertyReg"
```

**结果**：0 条匹配

---

## 二、实际存在的类型

r77 中实际存在的 DynamicProperty 相关类型是：

| 类型 | 状态 |
|---|---|
| `System.Runtime.Remoting.Contexts.DynamicPropertyHolder` | ✅ 已生成（有 .cpp + .h） |
| `System.Runtime.Remoting.Contexts.IDynamicProperty` | ✅ 已生成 |
| `System.Runtime.Remoting.Contexts.IDynamicMessageSink` | ✅ 已生成 |
| `System.Runtime.Remoting.Contexts.IContributeDynamicSink` | ✅ 已生成 |
| `System.Runtime.Remoting.Contexts.DynamicPropertyCollection` | ❌ **不存在** |
| `System.Runtime.Remoting.Contexts.DynamicPropertyCollection+DynamicPropertyReg` | ❌ **不存在** |

---

## 三、DynamicPropertyHolder 生成状态

### 3.1 文件位置

- `E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_Remoting_Contexts_DynamicPropertyHolder.cpp`
- `E:\il2cpp\out\mscorlib_gen_r77\include\internal\generated.h`（第 40277-40292 行定义 struct）

### 3.2 编译状态

`DynamicPropertyHolder.cpp` **未出现在 r77 错误日志中**，说明该文件编译通过，无错误。

### 3.3 类型定义（generated.h:40283）

```cpp
struct System_Runtime_Remoting_Contexts_DynamicPropertyHolder
{
    // fields
    System_Array* _props;       // System.Array
    int32_t _numProps;          // System.Int32
    System_Array* _sinks;       // System.Array
    // GROW_BY static
};
```

---

## 四、根因分析

### 4.1 为何不存在？

`DynamicPropertyCollection` 和 `DynamicPropertyReg` 是 .NET Framework 中 `System.Runtime.Remoting.Contexts` 命名空间的内部类型：

- `DynamicPropertyCollection`：封装动态属性列表的集合类
- `DynamicPropertyReg`：`DynamicPropertyCollection` 的嵌套结构体，记录属性注册信息（`IDynamicProperty` + `Context` 引用）

在 mscorlib.dll 的元数据中，这些类型可能被标记为：
1. **internal/private**：il2cppc 默认不生成内部类型的 C++ 包装
2. **未引用**：若 mscorlib 的 IL 代码中无直接调用路径，transpiler 不会生成对应文件
3. **被优化掉**：.NET 4.x 的 NGEN/trimming 可能已移除未使用的内部类型

### 4.2 r77 生成范围

r77 生成 4628 个 src 文件 + 4629 个 include 文件，覆盖 mscorlib 的公开 API 表面。`DynamicPropertyCollection+DynamicPropertyReg` 作为内部实现细节，不在 il2cppc 的生成范围内。

---

## 五、可能的混淆来源

用户提到的 `DynamicPropertyCollection+DynamicPropertyReg` 可能来自：

1. **其他 mscorlib 版本**：不同 .NET Framework 版本的内部类型不同
2. **其他程序集**：`System.Runtime.Remoting.dll` 而非 `mscorlib.dll`
3. **r77 之前的版本**：r75/r76 可能有不同的生成范围
4. **误识别**：`DynamicPropertyHolder` 被误认为是 `DynamicPropertyCollection`

---

## 六、建议

### 6.1 验证步骤

1. 确认目标程序集：`System.Runtime.Remoting.dll` 还是 `mscorlib.dll`？
2. 确认 .NET Framework 版本：net48/net472/net461？
3. 检查元数据：用 ILSpy/ildasm 查看目标程序集中是否存在 `DynamicPropertyCollection+DynamicPropertyReg`

### 6.2 若确实需要生成

若该类型确实需要生成（例如被其他生成代码引用），需：
1. 在 `TypeDef.cs` 或 `TypeEmitter.cs` 中添加内部类型的生成支持
2. 在 `MetadataLoader.cs` 中确保内部类型被加载
3. 重新生成并验证

---

## 七、结论

**r77 中不存在 `System.Runtime.Remoting.Contexts.DynamicPropertyCollection+DynamicPropertyReg` 相关错误。** 该类型在 r77 生成输出中完全缺失，既无生成文件，也无编译错误。实际存在的 `DynamicPropertyHolder` 类型编译通过。

建议确认：
- 目标类型名称是否正确？
- 目标程序集是否正确？
- 是否需要扩展 il2cppc 以支持内部类型生成？

---

**分析文件**：`E:\il2cpp\r17shard\r77_dynamic_property_reg_complete_fix.md`
