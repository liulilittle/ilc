# 泛型支持现状探明（generics-probe，2026-08-03，只读探路）

> 范围：新建 tests/cases/genlist/（泛型 List<int> + 泛型方法 Id<T> 最小 e2e 用例），
> 用现版 Release 转译器（src/il2cppc/bin/Release/net8.0/il2cppc.exe）转译 net48 exe，
> 记录精确失败点。不改仓库任何已有文件，不 git commit。
> 相关审计：recursion-audit.md（递归/尾调用）；generated-code-audit.md（发射形态）。

## 1. 用例与验证步骤

tests/cases/genlist/（本探路任务新建）：
- `genlist.cs`：`new List<int>()` + Add×3 + get_Count×2 + get_Item×2 + 泛型方法
  `static T Id<T>(T x) => x;` 以 int/string 两实例调用；输出 6 行（True×4 / 42 / ok）
- `genlist.csproj`：net48 / LangVersion 7.3 / AssemblyName=genlist（同 generics 用例模板）
- `expected.txt`：net48 参考运行逐行（True / True / True / True / 42 / ok），LF 无 BOM
- 手工复刻 run-tests.ps1 5 步：build → 参考运行 → 转译 → （未到 cmake 步，转译即失败）→ diff

参考运行（.NET Framework 4.8）验证 [BASH: genlist.exe → EXIT=0，6 行输出]。

## 2. 现状结论：**泛型 = 部分支持，BCL 泛型与泛型方法均 loud 拒绝**

| 探测面 | 结果 | 失败阶段 | 失败方式 |
|---|---|---|---|
| 用户定义泛型类型实例化 `Box<int>` | **支持**（既有 generics 用例：发射 `Box_1_int.h/.cpp`，转译通过） | — | — |
| BCL 泛型 `List<int>`（无泛型方法） | **拒绝** | 代码生成（StackFold fold） | loud `NotSupportedException`，exit 2，无错误 C++ 产出 |
| 泛型方法 `Id<T>`（仅声明即） | **拒绝** | 元数据加载（LoadAssembly） | loud `NotSupportedException`，exit 2 |

关键区分：
- **泛型类型实例化**（type instantiation，`List<int>` 出现在 IL 的 TypeSpec 父类）：
  用户类型路径已通（TypeEmitter.cs:51 `GenericArity > 0 → baseName + "_int"`；
  MethodRefResolver.cs:231-235 MemberRef.TypeSpec 父类 → GenericInstantiationName）。
  但该路径**只服务用户类型**——BCL 泛型的 MemberRef 走 BclResolve 表，List 域是空壳（见 §3-B）。
- **泛型方法实例化**（method instantiation，`Id<int>` 的 MethodSpec token）：两条独立阻塞，
  第一条在元数据加载阶段把整个程序挡死（见 §3-A），第二条在解析路径上（见 §3-D，代码读证）。

## 3. 失败点清单（精确 file:line:message，即泛型支持施工单）

### A. 泛型方法 → 元数据加载即崩（首先命中）

```
il2cppc error: signature parameter type byte 0x00 not supported in M2
   at Il2Cpp.Transpiler.Emit.MethodSignatureCoder.EncodeType   MethodSignatureCoder.cs:88
   at Il2Cpp.Transpiler.Emit.MethodSignatureCoder.Decode       MethodSignatureCoder.cs:34
   at Il2Cpp.Transpiler.MetadataLoader.LoadAssembly            MetadataLoader.cs:115
   at Il2Cpp.Transpiler.Program.RunMain                        Program.cs:19
EXIT=2
```

- **触发**：`Id<T>(T)` 的 MethodDef 签名（ildasm 实证 `static !!T Id<T>(!!T x)`）。
  签名字节：header 0x10(static|generic) / genParamCount 0x01 / paramCount 0x01 /
  返回 `0x1E`(ELEMENT_TYPE_MVAR) + 索引字节 `0x00` / 参数 `0x1E` + 索引字节 `0x00`。
- **根因**：`Decode`（MethodSignatureCoder.cs:29-31）仅对返回类型的
  0x11/0x12/0x40 消费后续 TypeDefOrRef，对 VAR/MVAR 返回类型既不映射也不消费其索引字节
  → 返回类型的索引字节 `0x00` 落入参数循环的 `EncodeType`（:88 default 分支）→ throw。
- **潜伏同源缺陷**：`EncodeType` 的 `0x13 => "i"`（:87）注释声称消费 VAR 索引字节，
  实际未读——任何"VAR 参数后还有参数"的签名都会错位。Box<T> 方法存活纯属侥幸
  （Set(T) 的 VAR 恰好是末参数，Get() 返回 VAR 且无参数）。
- **修复方向**：`0x13`/`0x1E` 后读走索引字节（对齐即可，定义级泛型参数编码为 "i"）；
  返回类型位置同样处理。此为**加载级阻塞**，修复前任何含泛型方法的程序集都无法加载。

### B. BCL 泛型 List`1 → 代码生成 loud 拒绝（无泛型方法时）

```
il2cppc error: callvirt on non-instance method (token 167772173)
   at Il2Cpp.Transpiler.Ir.StackFold.<Fold>g__EmitCall|0_7   StackFold.cs:906
   at Il2Cpp.Transpiler.Ir.StackFold.Fold                    StackFold.cs:354
   at Il2Cpp.Transpiler.Emit.CodeGenerator.Generate          CodeGenerator.cs:225
EXIT=2
```

- **token 167772173 = 0xA00000D** = MemberRef 表 rid 13 = `List<int>::get_Count`
  （rid 1-10 为程序集特性 ctor，11=List`.ctor，12=Add，13=get_Count——按 ildasm 首次使用序推导）。
- **根因链**（三段）：
  1. `MethodMap.List.cs:16-20` `TryMapList` 是**空壳骨架**，无条件 `return null`（静默）。
     分发行 MethodRefResolver.cs:361 已存在（`case "System.Collections.Generic.List`1"`）。
  2. null → `BclResolve` 落回默认 MemberRef 解析 MethodRefResolver.cs:263-267：
     `isInstanceMr = ... && !name.StartsWith("get_") && ...` —— **get_* 一律判为 static**。
  3. Roslyn 对泛型引用类型实例的成员一律发射 `callvirt`（ildasm 实证
     `callvirt instance ...List`1<int32>::Add/get_Count/get_Item`）→
     StackFold.cs:905-906 `resolved is { IsInstance: false }` 检查命中 → loud throw。
- **Add 为何先过**：`Add` 不匹配 get_* 前缀 → 判 instance → 通过 905 检查，
  折叠为 `VirtualCallExpr`（StackFold.cs:982，fold 阶段无错）→ 若修好 get_Count，
  Add/ctor 的下一个闸门是发射期 CodeGenerator.cs:1230
  `callvirt owner {System.Collections.Generic.List`1} not in translated types`。
  即：**List 域不接线，链上每个成员都会在某个阶段 loud 拒绝，不会产出错误 C++**。

### C. List 域接线时的后续缺口（代码读证）

- `newobj List<int>()` 与 `Add` 若走到默认解析：生成 `List_1_int_ctor_<hash>` /
  `List_1_int_Add_<hash>` 等**不存在的 C++ 符号**（MemberRef.TypeSpec 父类 →
  GenericInstantiationName 前缀，MethodRefResolver.cs:226-235）→ 若绕过 B 的检查，
  会在 C++ 编译/链接期暴露未定义符号。接线 TryMapList 即可消解。
- 运行时侧：无 List<T> 类描述符/容器（runtime 有 il2cpp_array 可作后备存储）。

### D. 泛型方法调用点 → 解析路径无用户类型分支（代码读证，被 A 挡住未实证）

- `Id<int>(42)` 在 IL 中为 MethodSpec token（ildasm 实证 `call !!0 Cases.GenList::Id<int32>`）。
- MethodRefResolver.cs:202-220：MethodSpec → 解码 GenericInst 参数 → `BclResolve(...)`；
  owner `Cases.GenList` 不在 BCL 表 → null → :219 throw
  `generic instantiation of Cases.GenList.Id has no BCL mapping in M5`。
- **结论**：即使修好 A（MVAR 解码），用户泛型方法调用仍会被 :219 拒绝——
  MethodSpec 路径只咨询 BCL 映射表，无"按具体类型展开用户泛型方法"的分支。

## 4. 后续建议（泛型实例化方案）

**现状语义**：转译器本质是"按具体类型展开"（用户泛型类型 Box<T> → Box_1_int 已如此工作）。
BCL 泛型应沿用同一哲学，两个方向：

### 方案 A（推荐起步）：转译器按具体类型展开 + 每实例化一个 runtime 容器 icall 集
- List<int> 的 ctor/Add/get_Count/get_Item/get_Item 各映射一个
  `il2cpp_list_int_*` 类 icall（仿 MethodMap.Hashtable.cs / MethodMap.Array.cs 模式，
  TryMapList 骨架已在 MethodMap.List.cs 就位，分发行 MethodRefResolver.cs:361 已存在）。
- 后备存储复用现有 il2cpp_array（int*）；List<T> 对象 = 小型 Il2CppObject 派生
  （capacity/count/items 三字段 + 类描述符）。T 类型数 = 需要接线的 icall 集数——
  **先只接 int（本用例），再按需扩 string/其他**。KISS：不做共享代码生成。
- 与现有 hashtablecase 的 Hashtable icall 模式同构，风险最低。

### 方案 B（远期）：runtime 类型化容器（类型擦除/void* + 运行时类型标签）
- 单一 List 实现服务所有 T，但要求 runtime 对象模型引入类型化元素存储与装箱语义；
  与现有"每实例化一文件名后缀"的展开哲学冲突，且值类型元素（int）会引入装箱压力。
- 仅在方案 A 出现"实例化爆炸"（几十个 T）时才值得考虑；当前 e2e 规模不需要。

### 前置修补（方案 A/B 共同依赖，工作量大头）
1. MethodSignatureCoder VAR/MVAR 索引字节消费（§3-A）——否则**任何**泛型方法程序集无法加载。
2. MethodRefResolver MethodSpec 用户类型分支（§3-D）——展开用户泛型方法调用
   （签名用具体实例化类型重解析，如 Id<int> → 参数/返回按 int32 折叠）。
3. TryMapList 接线（§3-B/C）——List 域第一段真实映射 + 3 个 icall。

## 5. 证据附录

- ildasm 摘录（genlist.exe，.NET 4.7.2 工具）：
  `.method private hidebysig static !!T Id<T>(!!T x) cil managed`
  `IL_0007: callvirt instance void class [mscorlib]System.Collections.Generic.List`1<int32>::Add(!0)`
  `IL_0014: callvirt instance int32 ...List`1<int32>::get_Count()`
  `IL_0055: call !!0 Cases.GenList::Id<int32>(!!0)`（MethodSpec 形态）
- 既有 generics 用例（用户泛型 Box<int>）：转译成功，gen/src 下发射 `Box_1_int.cpp`，
  include/internal/Box_1_int.h —— 泛型类型展开路径实证。
- 本探路的两轮转译输出（exit 2）见 §3-A/§3-B。
