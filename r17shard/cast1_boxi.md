# cast-1 box:i解码器根因调查

**日期**: 2026-09-06  
**调查文件**: `E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs`  
**错误簇**: `cannot-cast` / `cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'`  
**r77基线**: shard1=870, shard2=175, shard3=279 (合计1324/片)

---

## 一、证据链

### 1.1 错误模式（r77shard1_shard1_errors.log）
```
System_AggregateException.cpp:610: cannot cast from type 'int32_t' to pointer type 'Il2CppObject *'
System_Collections_Concurrent_ConcurrentDictionary_2_DictionaryEnumerator_int_...cpp:154: same
```

### 1.2 生成代码实例（System_Collections_Concurrent_ConcurrentDictionary_2_DictionaryEnumerator_int_...cpp:149-156）
```cpp
loc0 = il2cpp::il2cpp_ienum_get_current_i4(...);                       // → int32_t
t0 = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_kvp_string_int_get_key(*(&loc0))); // → Il2CppObject*
t1 = il2cpp::il2cpp_box_int32(nullptr, t0);                            // → Il2CppObject* (正确装箱)
loc0 = il2cpp::il2cpp_ienum_get_current_i4(...);                       // → int32_t
t2 = il2cpp::il2cpp_kvp_string_int_get_value(*(&loc0));                // → int32_t ???
t0 = static_cast<Il2CppObject*>(t2);                                   // ✗ 错误: int32_t → Il2CppObject*
```

**IL注释标注为 `// IL box @0x002B`**，但生成的是 `static_cast<Il2CppObject*>` 而非 `il2cpp_box_int32` 调用。

### 1.3 CodeGenerator.cs 关键行

| 行号 | 代码 | 用途 |
|------|------|------|
| 6551 | `"i" => $"static_cast<int32_t>({code})"` | cast=="i" 基础转换 |
| 6706 | `"unbox:i" => $"il2cpp::il2cpp_unbox_int32(static_cast<Il2CppObject*>({code}))"` | 显式拆箱 |
| 5896 | `"System.Int32" => $"il2cpp::il2cpp_box_int32(nullptr, {boxVal})"` | 显式装箱 (EmitExpr box_分支) |
| 1138 | `objTypes[boxTemp.Name] = TypeFullNameFromToken(...)` | StackFold BOX 追踪 objTypes |

---

## 二、根因

### 2.1 直接根因

**CodeGenerator.cs:6551 `cast=="i"` 分支缺少 box:i 解码逻辑。**

当IL执行 `box` 指令（将int32装箱为Il2CppObject*），但上游方法返回值已被识别为int32_t时：
- StackFold的 `objTypes` 追踪标记该temp为 `System.Int32` 类型
- EmitExpr的 `box_{token}` 分支（CodeGenerator.cs:5865-5927）正确生成 `il2cpp::il2cpp_box_int32(nullptr, val)`
- **但当该box的结果参与后续icall参数时，若该参数cast="i"，CodeGenerator.cs:6551 执行 `static_cast<int32_t>(Il2CppObject*)`**
- 反向场景（int32→object参数）时，`static_cast<Il2CppObject*>(int32_t)` 产生无效指针解释

### 2.2 深层机制

**objTypes追踪与cast解码不匹配**：

1. StackFold.cs:1138 正确设置 `objTypes[boxTemp.Name] = "System.Int32"`（或对应token类型）
2. 但当该box的结果到达 EmitIcallCallArgs 的cast路径时，判断逻辑未查询objTypes来确定"该Il2CppObject*是否实际是装箱的int32"
3. 结果：`cast=="i"` 对已经是 `Il2CppObject*` 的装箱值做 `static_cast<int32_t>` → 在C++端变成 `reinterpret_cast` 语义（整数↔指针宽度不匹配）
4. 反向：`cast=="s"/"a"/"boxed"` 对已经是 `int32_t` 的未装箱值做 `static_cast<Il2CppObject*>` → 把整数值当地址

### 2.3 触发条件

该错误仅当以下条件同时满足时触发：
- IL存在 `box` 指令（值类型 → 引用类型转换）
- 装箱结果作为参数传递给cast为 `"i"` / `"s"` / `"a"` / `"boxed"` 的icall
- 折叠后temp的objTypes已正确记录，但cast路径未使用它做判断

---

## 三、修复方案

### 3.1 核心修改：CodeGenerator.cs:6548-6553 增加 box:i 解码

```csharp
// 修改前（第6551行）:
"i" => $"static_cast<int32_t>({code})",

// 修改后:
"i" => code.Contains("Il2CppObject*", StringComparison.Ordinal) || code.Contains("g_strPool", StringComparison.Ordinal)
    ? $"il2cpp::il2cpp_unbox_int32(static_cast<Il2CppObject*>({code}))"  // box:i 解码
    : $"static_cast<int32_t>({code})",
```

### 3.2 对称修复：cast为引用类型时加 i→box 编码

对 `"s"`, `"a"`, `"boxed"` 等引用型cast，当code为int32_t（来自objTypes追踪的int类型）时：
```csharp
"s" => code.HasInt32Origin(objTypes, args[srcIdx]) 
    ? $"il2cpp::il2cpp_box_int32(nullptr, static_cast<int32_t>({code}))" 
    : NullSafePtrCast("Il2CppString*", code),
```

### 3.3 验证方案

1. `dotnet build src/il2cppc` → 0错0警告
2. 抽查 r77生成文件 `System_Collections_Concurrent_ConcurrentDictionary_2_DictionaryEnumerator_int_*.cpp` 确认 cannot-cast 从175/279下降
3. 全量r78扫描对比r77基线

---

## 四、风险评估

- **影响范围**: 仅修改cast=="i"的分支逻辑，对非box路径无影响
- **回归风险**: 现有的 `static_cast<int32_t>(int表达)` 路径保留，仅在code文本含 `Il2CppObject*` 时走新分支
- **性能**: `il2cpp_unbox_int32` 是运行时helper（读box payload），语义正确；原 `static_cast` 只是UB指针解释

---

## 五、结论

**根因已定位**：CodeGenerator.cs:6551 的 `cast=="i"` 分支缺少对 box:i（装箱int→Il2CppObject*）的解码，把已装箱对象当整数强转。修复需在cast=="i"分支增加box:i识别（通过objTypes或code文本启发），路由到 `il2cpp_unbox_int32` 而非 `static_cast<int32_t>`。
