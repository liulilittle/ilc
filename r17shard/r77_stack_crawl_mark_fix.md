# r77 StackCrawlMark* ← int32_t* 错误分析与修复方案

**日期**: 2026-09-06  
**数据源**: `E:\il2cpp\r77shard\*.csv` (23个错误)  
**目标**: 分析 StackCrawlMark 类型识别错误并提出修复方案

---

## 一、错误统计

### 1.1 总体分布

| 错误类别 | 数量 | 典型位置 |
|---------|------|----------|
| `ContinuationResultTaskFromResultTask_2<T>` | 8 | line 142 |
| `ContinuationTaskFromResultTask_1<T>` | 8 | line 143 |
| `Task_1<T>` | 5 | lines 539/545 |
| `ContinuationTaskFromTask` | 1 | line 143 |
| `SecurityRuntime` | 1 | line 292 |
| **合计** | **23** | — |

### 1.2 错误消息

```
error: incompatible pointer types assigning to 'System_Threading_StackCrawlMark *' 
       from 'int32_t *' (aka 'int *')
```

---

## 二、根因分析

### 2.1 StackCrawlMark 类型特征

`System.Threading.StackCrawlMark` 是 mscorlib 中的值类型 (struct), 内部封装一个 `int32_t` 字段:

```csharp
// mscorlib 定义
public struct StackCrawlMark {
    int value;  // 唯一字段
}
```

### 2.2 错误传播链

```
IL 局部变量签名: 0x11 VALUETYPE + TypeDef(System.Threading.StackCrawlMark)
    ↓
MethodBodyReader.LocalTypeKind → "v:System.Threading.StackCrawlMark"  ✓ 正确
    ↓
CodeGenerator.EmitBody locKind 传播 → locKind[locName] = "v:System.Threading.StackCrawlMark"  ✓ 正确
    ↓
声明: System_Threading_StackCrawlMark locN;  ✓ 正确
    ↓
ldloca locN → 应生成 (&locN) 类型为 System_Threading_StackCrawlMark *
    ↓
❌ 但 TypeKindResolver.Resolve 把 VALUETYPE 默认映射为 int32_t
    ↓
源表达式被分类为 int32_t *
    ↓
赋值: System_Threading_StackCrawlMark * = int32_t *  ✗ 编译失败
```

### 2.3 根本原因

**TypeKindResolver.Resolve** (TypeKindResolver.cs:31) 对 VALUETYPE 的默认处理:

```csharp
case 0x11: return (FieldTypeKind.Int32, "", "int32_t");
```

`System.Threading.StackCrawlMark` 是 VALUETYPE, 但 TypeKindResolver 没有它的特殊映射, 默认返回 `int32_t`。

当 `StackCrawlMark` 作为字段类型或 ldloca 操作数时:
- 目标端: 局部变量声明正确 → `System_Threading_StackCrawlMark`
- 源端: TypeKindResolver 解析为 `int32_t` → 指针为 `int32_t *`

### 2.4 为何泛型实例化受影响最大

`ContinuationResultTaskFromResultTask`2<T>` 等 async state machine 包含 `StackCrawlMark` 字段。当 T 为不同实例化时:
- 机器生成的 `SetResult`/`SetContinuationFor` 方法访问此字段
- 每次访问都触发 `TypeKindResolver.Resolve` 对 VALUETYPE 的默认处理
- 导致 16+ 个泛型实例化全部失败

---

## 三、修复方案

### 3.1 方案 A: TypeKindResolver 添加特殊映射 (推荐)

**位置**: `TypeKindResolver.cs:31`

```csharp
// 修改前:
case 0x11: return (FieldTypeKind.Int32, "", "int32_t");

// 修改后:
case 0x11:
    // StackCrawlMark 是封装 int32 的 struct, 需要完整 C++ 类型名
    // 不能映射为 int32_t, 否则指针类型不匹配
    return (FieldTypeKind.Int32, "", "int32_t");  // 保留默认, 但在 ResolveFromFullName 中处理
```

**更优方案** — 在 `ResolveFromFullName` 中添加:

```csharp
public static (FieldTypeKind Kind, string FullName, string CppKind) ResolveFromFullName(string fullName) => fullName switch
{
    // ... 现有映射 ...
    "System.Threading.StackCrawlMark" => (FieldTypeKind.ValueType, fullName, "System_Threading_StackCrawlMark"),
    _ => ArgKind(fullName),
};
```

### 3.2 方案 B: 在 CodeGenerator.EmitBody 的 locKind 传播中添加特殊处理

**位置**: `CodeGenerator.cs:1902-1908` (IL 局部签名权威分支)

```csharp
// 添加: 当 IL 签名是 v:System.Threading.StackCrawlMark 时
// 确保 locKind 使用完整 struct 名而非降级为 int32_t
for (var i = 0; i < bodyInfo.LocalKinds.Length; i++)
{
    if (bodyInfo.LocalKinds[i] is "v:System.Threading.StackCrawlMark")
    {
        locKind[bodyInfo.LocNames[i]] = "System_Threading_StackCrawlMark";
    }
}
```

### 3.3 方案 C: 在 TypeCppName 中添加 StackCrawlMark 映射

**位置**: `CodeGenerator.cs:5626-5649`

```csharp
private static string TypeCppName(string fullName) => fullName switch
{
    // ... 现有 BCL 映射 ...
    "System.Threading.StackCrawlMark" => "System_Threading_StackCrawlMark",
    _ => TypeEmitter.Sanitize(fullName),
};
```

---

## 四、推荐修复组合

**最小改动 + 最大覆盖**: 方案 A (ResolveFromFullName) + 方案 C (TypeCppName)

| 修改位置 | 改动 | 覆盖范围 |
|----------|------|----------|
| `TypeKindResolver.ResolveFromFullName` | +1 行 | 所有通过 FullName 解析的场景 |
| `CodeGenerator.TypeCppName` | +1 行 | 所有通过 TypeCppName 映射的场景 |

---

## 五、预期效果

| 指标 | 当前 | 修复后 |
|------|------|--------|
| StackCrawlMark 指针错误 | 23 | 0 |
| 受影响源文件 | ~15 | 0 |
| 泛型实例化编译 | 全部失败 | 全部通过 |

---

## 六、验证清单

1. `dotnet build src/il2cppc` → 0错0警告
2. 重新生成 r77 → StackCrawlMark 错误归零
3. 抽查 `ContinuationResultTaskFromResultTask_2_int_int.cpp:142` 确认:
   - 目标: `System_Threading_StackCrawlMark *`
   - 源: `System_Threading_StackCrawlMark *` (不再为 `int32_t *`)
4. 回归: 不影响其他 VALUETYPE 的 int32_t 映射

---

**调查人**: 子代理 (ses_f8d427dafffegHpvvixhOajmiS)  
**状态**: 待实施修复
