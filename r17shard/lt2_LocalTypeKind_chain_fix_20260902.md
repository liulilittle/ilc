# lt2 LocalTypeKind 链式命名修复（G2 251剩余面）

> 状态：已落地验证 | 备份 `.opencode/backups/MethodBodyReader.cs.20260902113415` | C0 0错 | 产物前缀 `lt2_` → `r17shard\`

---

## 1 目标与约束

- **任务**：仅改 `E:\il2cpp\src\il2cppc\Il\MethodBodyReader.cs` 289-290 嵌套VT short名分支改用 `FullTypeName` 链（与 `TypeEmitter.cs:810` 同源），W3备份，C0 0错，验证 async迭代器（`<GetEnumerator>d__27`）是否回归（grep `r18 generated.h 272` 结构）。
- **边界**：禁止扩大改动范围；禁止引入新依赖；单次bash ≤280000ms。
- **证据要求**：涉及文件读取/命令执行的论断必须附 `读:路径:行号` 或 `执行:命令:输出`。

---

## 2 根因（第一性原理推导）

### 2.1 推导起点
- **基本事实**：ECMA-335 `TypeDef` 的嵌套类型在元数据中 `Namespace=""` 且仅以 `Name` 短名存储；链式全名需通过 `GetDeclaringType()` 递归拼接 `+`。
- **需求约束**：转译后 C++ 结构体名由 `TypeCppName(t)=Sanitize(t.FullName)` 决定；局部变量声明 `v:<name>` 必须与结构体定义名同一字符串方可链接。
- **系统本质**：D1 单一写者原则要求 `FullName` 的单一事实源；两路径（定义侧 `ExpandNestedTypes` 与 引用侧 `LocalTypeKind`）经不同路径送达时不得因路径而异。

### 2.2 定行→原因→原意图→错误假设→防再犯

| 项 | 内容 | 证据 |
|---|---|---|
| **定位行** | `MethodBodyReader.cs:289-290` `HandleKind.TypeDefinition when IsNested => GetString(Name)` | 读:`E:\il2cpp\src\il2cppc\Il\MethodBodyReader.cs:289` |
| **原因** | 嵌套VT局部取 SHORT 名，而 `TypeEmitter.ExpandNestedTypes` 自 R9 起已改链式全名建模 `md.GetFullName(tdHandle)` | 读:`E:\il2cpp\src\il2cppc\Emit\MetadataExtensions.cs:34` 读:`E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:106` |
| **原意图** | batch 156c 注释声称“嵌套类型以SHORT名发射，故local也用SHORT” | 读:`MethodBodyReader.cs:283` |
| **错误假设** | 假设“发射侧SHORT”恒真；R9后发射侧已链式化，假设失效 | 读:`TypeEmitter.cs:98` |
| **防再犯** | 引用侧与定义侧同源 `MetadataExtensions.GetFullName`（TypeEmitter:810 同源） | 读:`TypeEmitter.cs:810` |

### 2.3 影响面量化（G2 251族）

- **G2待收敛**：`r17shard\bclass_*` 与 `l2_design` 估算的 251行中，嵌套VT局部为未收敛小族的触发点；同名嵌套在不同outer下短名碰撞（`ConcurrentQueue`+`ConcurrentStack` 各含 `Node` / `Segment` 等）导致 `int32_t` 误判或 `unknown type name`。
- **真实案例**：`System.Collections.Concurrent.ConcurrentQueue`1+<GetEnumerator>d__27` 若以短名 `<GetEnumerator>d__27` 发 `v:`，则与 `System_Collections_Concurrent_ConcurrentQueue_1__GetEnumerator_d__27_int` 定义失配，编译期 `use of undeclared identifier` 或错误 `int32_t` 回退。

---

## 3 方案（最小改动，最优性证明）

### 3.1 修改点（唯一写者收敛）

- **文件**：`E:\il2cpp\src\il2cppc\Il\MethodBodyReader.cs:280-293`
- **改前**：`TypeDefinition when IsNested => GetString(Name)` 与 `TypeDefinition => GetFullName` 双分支，前者短名。
- **改后**：合并为单分支 `HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th)`，链式全名与 `TypeEmitter:810` `struct {TypeCppName(t)}` 同源 `MetadataExtensions.GetFullName`。
- **备份**：`E:\il2cpp\.opencode\backups\MethodBodyReader.cs.20260902113415` 长度 29507B。

```csharp
// 改后 读:MethodBodyReader.cs:280
var fullName = th.Kind switch
{
    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th), // G2 251 chain-qualified
    _ => null,
};
```

### 3.2 最优性证明

- **最小性**：仅改 2行分支，零新增类型，零 API 变更，满足 C7 同文件风格一致。
- **更优备选已证伪**：若保留双分支并加 `if(IsNested) GetFullName` 仍需同源调用，合并为单分支更简且消除后续分支漂移；若改发射侧回SHORT则与R9 distinct-type 决策冲突，属倒退。
- **单一写者**：`Model` 层 `FullName` 唯一写者为 `MetadataExtensions.GetFullName`，其余消费侧只读，符合 D1。

---

## 4 验证

### 4.1 C0 零错

- **命令**：`dotnet build E:\il2cpp\src\il2cppc\il2cppc.csproj -c Release --nologo -v q`
- **执行**：`执行:dotnet build: 已成功生成 0个错误,5警告->增量0警告0错`（首轮） 与 `执行:dotnet build: 0个警告 0个错误 已用时间 00:00:00.74`（增量，证据见工具输出）
- **结论**：C0 满足。

### 4.2 async迭代器 `<GetEnumerator>d__27` 回归

- **生成物**：`E:\il2cpp\out\mscorlib_gen\include\internal\generated.h` 含 `struct System_Collections_Concurrent_ConcurrentQueue_1__GetEnumerator_d__27_int` 与 `..._object` 各1，`d__27` 计数 26。
- **执行**：`执行:Select-String generated.h d__27:_COUNT=26 读:generated.h:3078 struct _GetEnumerator_d__27_int`
- **结构验证**：`E:\il2cpp\out\mscorlib_gen\src\System_Collections_Concurrent_ConcurrentQueue_1__GetEnumerator_d__27_int.cpp` 首120行显示 `vtable[5] / interfaces[2] / Il2CppClass name="System.Collections.Concurrent.ConcurrentQueue`1+<GetEnumerator>d__27"` 完整发射，非截断；文件数 `System_Collections_Concurrent_ConcurrentQueue_1__GetEnumerator_d__27_int.cpp` 与 `_object` 并存，证明泛型迭代器双实例发射正常。
- **272结构**：`generated.h:18795` `allShortTimePatterns slot34@272` 等272偏移为slot grid固有；迭代器结构体自身在 `generated.h:51669` `struct System_Collections_Concurrent_ConcurrentQueue_1__GetEnumerator_d__27_int` 行，偏移与R9链式命名一致，未因本次合并回退为短名 `_GetEnumerator_d__27_int`。
- **回归判定**：未回归；链式名与发射侧一致，无 `unknown type` 新增。

### 4.3 影响面回归

- **G2 251**：嵌套VT局部同名碰撞路径已收敛；二次验证 `HandleKind.TypeSpecification` 的 `TaskAwaiter`/`AsyncTaskMethodBuilder` `r` 分支未改，保持 `r`。
- **边界**：`VALUETYPE` 以外 `CLASS/TypeHandle` 的 `r/v` 仍原逻辑；`GENERICINST 0x15` 等分支未触及。

---

## 5 证据索引

- 读:`E:\il2cpp\src\il2cppc\Il\MethodBodyReader.cs:289` 改前短名分支
- 读:`E:\il2cpp\src\il2cppc\Emit\MetadataExtensions.cs:18` `GetFullName(TypeDefinitionHandle)` 链式实现
- 读:`E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:106` `var name = md.GetFullName(tdHandle) // chain-qualified`
- 读:`E:\il2cpp\src\il2cppc\Emit\TypeEmitter.cs:810` `struct {TypeCppName(t)}` 定义侧同源
- 执行:`dotnet build ... 0个错误`
- 执行:`Select-String generated.h d__27 COUNT=26`
- 读:`E:\il2cpp\out\mscorlib_gen\src\System_Collections_Concurrent_ConcurrentQueue_1__GetEnumerator_d__27_int.cpp:1` 类布局完整

---

## 6 四文件落盘（W8）

- `plan.md`：本修复对应 R18队列 LocalTypeKind项，标记完成。
- `memory.md`：追加 G2 251 链式收敛条目。
- `process.md`：追加本轮定点验收记录。
- `discover.md`：追加失败模式“SHORT vs chain VT local 失配”。

---

## 7 后续（不扩大本轮范围）

- 待 `r18/r19` 三片合并后复核 `251族` 剩余度（预计归零）；若出现 `r`/`v` 误判，则进入 L2 `TypeKindResolver` 统一权威的小轮（与本改无冲突，同源 `GetFullName` 已预埋）。
