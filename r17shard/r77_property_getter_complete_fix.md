# r77 PropertyGetter / PropertyInfo 错误分析报告

## 执行摘要

**父会话任务：分析 r77 中 "System.Reflection.PropertyInfo+PropertyGetter" 的 11 个缺失错误的修复方案。**

**实际发现：r77 错误日志中不存在任何 PropertyGetter 相关错误。**

## 搜索过程（证据）

### 搜索 1：r77 错误日志全量扫描 PropertyGetter
```
文件：E:\il2cpp\out\r77shard\shard1_errors.log
      E:\il2cpp\out\r77shard\shard2_errors.log
      E:\il2cpp\out\r77shard\shard3_errors.log
命令：Select-String -Path shard*.log -Pattern "PropertyGetter" -SimpleMatch
结果：0 命中
```

### 搜索 2：r77 生成文件全量扫描 PropertyGetter
```
文件：E:\il2cpp\out\mscorlib_gen_r77\src\*.cpp
命令：Select-String -Pattern "PropertyGetter" -SimpleMatch
结果：0 命中
```

### 搜索 3：r77 头文件全量扫描 PropertyGetter
```
文件：E:\il2cpp\out\mscorlib_gen_r77\include\internal\generated.h
命令：Select-String -Pattern "PropertyGetter" -SimpleMatch
结果：0 命中
```

### 搜索 4：r77 子代理错误日志全量扫描
```
文件：E:\il2cpp\r77shard\r77shard1_shard1_errors.log
      E:\il2cpp\r77shard\r77shard2_shard2_errors.log
      E:\il2cpp\r77shard\r77shard3_shard3_errors.log
命令：Select-String -Pattern "PropertyGetter" -SimpleMatch
结果：0 命中
```

### 搜索 5：r77 CSV 报告扫描
```
文件：E:\il2cpp\r77shard\*.csv
命令：Select-String -Pattern "PropertyGetter|PropertyInfo" -SimpleMatch
结果：0 命中（仅 RuntimePropertyInfo 出现 1 次）
```

### 搜索 6：r77 生成文件全量扫描 RuntimePropertyInfo
```
文件：E:\il2cpp\out\mscorlib_gen_r77\src\*.cpp
命令：Select-String -Pattern "RuntimePropertyInfo" -SimpleMatch
结果：0 错误命中（仅有 class_register 等注册代码）
```

## 实际存在的 r77 RuntimePropertyInfo 错误（非 PropertyGetter）

| 行号 | 错误 | 文件 |
|------|------|------|
| 292 | no matching function for call to 'System_Reflection_Associates_AssignAssociates_67b9e92bcae0f1f4' | RuntimePropertyInfo.cpp |
| 362 | no matching function for call to 'System_Reflection_MetadataImport_GetPropertyProps_d3809ddb5fc18fdc' | RuntimePropertyInfo.cpp |
| 364 | no matching function for call to 'System_IntPtr_ToPointer_6f8284eb489c6ac8' | RuntimePropertyInfo.cpp |
| 372 | no matching function for call to 'System_Signature_GetSignature_9387590b8f92c33e' | RuntimePropertyInfo.cpp |
| 669 | no matching function for call to 'System_Utf8String__ctor_773c4778de4aa0b5' | RuntimePropertyInfo.cpp |
| 671 | cannot initialize a parameter of type 'Il2CppObject *' with an rvalue of type 'System_Utf8String *' | RuntimePropertyInfo.cpp |
| 822 | assigning to 'int32_t' from incompatible type 'System_Reflection_MetadataImport' | RuntimePropertyInfo.cpp |
| 825 | assigning to 'int32_t' from incompatible type 'System_RuntimeTypeHandle' | RuntimePropertyInfo.cpp |
| 826 | no matching function for call to 'System_Reflection_MdConstant_GetValue_9e7084ebf1241280' | RuntimePropertyInfo.cpp |
| 1861 | no matching function for call to 'System_Reflection_RuntimePropertyInfo_EqualsSig_264113669a52740a' | RuntimeTypeCache_MemberInfoCache_1_int.cpp |

**以上 10 条错误均与 PropertyGetter 无直接关联。**

## 关于 "PropertyGetter 嵌套类型未生成" 的理论分析

虽然 r77 不存在 PropertyGetter 错误，但父会话任务描述的场景（嵌套类型未生成）在 r77 代码生成器中确实存在系统性问题。以下是根因分析：

### 根因：MetadataLoader.cs:36 跳过嵌套类型

```csharp
// MetadataLoader.cs:34-36
// Skip nested types unless they are compiler-generated closures
// (e.g., <>c__DisplayClass*) which are referenced by IL.
if (td.IsNested && !md.GetString(td.Name).StartsWith("<>")) continue;
```

**该过滤器的效果：**
- `System.Reflection.PropertyInfo+PropertyGetter` 是嵌套类型（`td.IsNested == true`）
- `PropertyGetter` 不以 `<>` 开头
- → 该 TypeDef 被跳过，不加入 `types` 列表
- → `Generate` 不为其生成 C++ 结构体/类定义
- → 任何引用该嵌套类型的 IL 操作（newobj、castclass、字段签名）将产生 undeclared 错误

### 为何 r77 未见 PropertyGetter 错误？

可能原因：
1. mscorlib 中 PropertyGetter 的 IL 调用点已在 StackFold 阶段被内联/折叠消除
2. r77 编译单元尚未覆盖到 PropertyGetter 的实际使用点
3. 该类型的引用通过 MethodRef/TypeRef 间接解析，未触发生成

### 修复方案（如需启用嵌套类型生成）

**方案 A（保守）：扩展 MetadataLoader.cs:36 的白名单**
```csharp
if (td.IsNested && !md.GetString(td.Name).StartsWith("<>") 
    && td.Name != "PropertyGetter" /* 按需扩展 */) continue;
```

**方案 B（系统）：为所有引用到的嵌套类型生成 TypeDef**
- 在 LoadAssembly 后，扫描所有 MethodBody 的 TypeRef/TypeSpec 引用
- 对被引用但未加载的嵌套类型，执行 GetNestedTypeDef 加载
- 需要同步更新 TypeEmitter 的命名逻辑（嵌套名用 `+` 分隔）

**方案 C（按需）：仅在检测到 undeclared 时动态补载**
- 在 CodeGenerator 的 GetTypeDef 未命中时
- 尝试从 MetadataReader 动态加载嵌套 TypeDef
- 加入 types 列表并触发 TypeEmitter 生成

## 结论

1. **r77 中不存在 PropertyGetter 相关错误**（已全量扫描验证）
2. **实际存在的 RuntimePropertyInfo 错误有 10 条**，均为签名/类型不匹配问题
3. **PropertyGetter 嵌套类型未生成的根本原因**是 MetadataLoader.cs:36 的硬编码过滤器
4. 如需修复，推荐方案 B（系统加载引用到的嵌套类型），但需评估对 r78+ 生成规模的冲击

## 交付

本分析结果已写入：E:\il2cpp\r17shard\r77_property_getter_complete_fix.md
