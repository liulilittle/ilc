# Audit: 深递归 e2e 复验 — recurse / exdeep（2026-08-04）

任务：嵌套类型发射 + ExpandNestedTypes 接线后 recurse 应能转译（运行时可能另有缺口——记录精确）。
范围：只读用例（tests/cases/recurse/、tests/cases/exdeep/ 未改），只写本文件；不 commit。

## 0. 环境先验（转译器新鲜度）

| 项 | 值 | 证据 |
|---|---|---|
| il2cppc.dll mtime | 08/04/2026 01:21:53 | [BASH] Get-Item src/il2cppc/bin/Release/net8.0/il2cppc.dll |
| 最新源文件 | MethodBodyReader.cs 01:20:51 | [BASH] Get-ChildItem src/il2cppc -Recurse -Include *.cs,*.csproj 排序 |
| 先验判定 | OK：dll 新于全部源 | [BASH] 比较输出 |
| transpiler build | 0 警告 0 错误 | [BASH] dotnet build src/il2cppc/il2cppc.csproj -c Release |
| 用例 fixture | recurse.csproj / exdeep.csproj rebuild exit=0 | [BASH] dotnet build tests/cases/{recurse,exdeep} -c Release |

## 1. 状态总表

| 用例 | 转译 | C++ 构建 (mingw) | 运行/对比 | 结论 |
|---|---|---|---|---|
| recurse | ✅ exit=0（仅 1 type） | ❌ 6 编译错（Node 未定义） | 未达（编译阻断） | **ExpandNestedTypes 接线未生效**（见 §2） |
| exdeep | ❌ exit=2（f14b callvirt） | 未达 | 未达 | **f14b 仍卡**（见 §3） |

## 2. recurse 详情

### 2.1 转译结果（exit=0）
- 输出：`generated 1 type(s) -> tests/cases/recurse/gen`（应 2 个：Cases.Recurse + Node）
- string pool 诊断行正常（7 literals / 6 methods）
- 证据：[BASH] dotnet il2cppc.dll recurse.exe gen → "generated 1 type(s)"

### 2.2 生成代码：引用侧在、定义侧无（Node 缺口）
gen/src/Cases_Recurse.cpp 引用侧（[READ:grep gen Node]）：
- :65 `t0 = il2cpp_object_new(&Node_class);` — Node_class 未定义
- :66 `Node__ctor_b3740a9592788e34(t0);` — ctor 未定义
- :68 `((Node*)il2cpp_null_check((Il2CppObject*)(loc0)))->Id = arg0;` — Node 类型未定义
- :69 `(Cases_Recurse_DeepAlloc_...(..) + ((Node*)il2cpp_null_check((Il2CppObject*)(loc0)))->Id)`

定义侧（Cases_Recurse.h / Cases_Recurse.cpp）无任何 Node 结构/描述符/ctor 声明。

### 2.3 C++ 构建失败（mingw，cmake --build exit=2）
gen/src/Cases_Recurse.cpp:
- `:65:29 error: use of undeclared identifier 'Node_class'`
- `:66:5  error: use of undeclared identifier 'Node__ctor_b3740a9592788e34'`
- `:68:7  error: use of undeclared identifier 'Node'`
- `:68:12 error: expected expression`
- `:69:78 error: use of undeclared identifier 'Node'`
- `:69:83 error: expected expression`
（6 errors；il2cpp_gen.dir build.make:76 中止）

### 2.4 根因：接线未生效（"Node_class 未定义"仍在 → 任务预案的判定条件命中）
- `ExpandNestedTypes` 定义存在：[READ:src/il2cppc/Emit/TypeEmitter.cs:90]
- 调用点**不存在**于生产管线：全仓 grep 仅命中定义 + 单测（tests/unit/TypeEmitterNestedTests.cs:148）
- Program.cs 主流程：[READ:Program.cs:25-26] `MetadataLoader.LoadAssembly` → `CodeGenerator.Generate`，中间无 ExpandNestedTypes 调用
- MetadataLoader 显式跳过嵌套：[READ:MetadataLoader.cs:32] `if (td.IsNested) continue;`
- 单测隔离通过（7/7）：[BASH] dotnet test --filter TypeEmitterNestedTests → 已通过 7
  → pass 本身正确，只是从未被生产管线调用

## 3. exdeep 详情

### 3.1 转译失败（exit=2，首错即停）
```
il2cppc error: callvirt owner System.Object not in translated types
   at CodeGenerator.VirtualTarget(...) CodeGenerator.cs:1332
   at EmitVirtualCall(...) CodeGenerator.cs:1228
   at EmitExpr(...) CodeGenerator.cs:1089
   at EmitStmt(...) CodeGenerator.cs:752
   at EmitBody(...) CodeGenerator.cs:670
   at Generate(...) CodeGenerator.cs:241
   at Program.RunMain(...) Program.cs:26
```

### 3.2 根因链（f14b `no.ToString()`，任务预案"若仍卡——报告"命中）
1. 失败点：[READ:CodeGenerator.cs:1332-1334] `VirtualTarget` 在 types 中找不到 owner `System.Object` 即抛。VirtualCallExpr 无条件走此路径：[READ:CodeGenerator.cs:1088-1089]。
2. 为什么没被 MethodRefResolver 接管：Object::ToString 的映射要求 `receiverType == "System.Object"`（[READ:MethodMap.Object.cs:62-66]，目标 icall `il2cpp::runtime::icall_object_to_string` 在运行时存在：[READ:valuesemantics.cpp:246]）。
3. receiverType 提示缺失：[READ:StackFold.cs:930-932] `recvType` 只从 `objTypes` 取；`objTypes` 只记录 newobj/box/GetType 结果（[READ:StackFold.cs:977-980]）。`object no = null;` 是普通局部变量（声明类型 System.Object），**不入 objTypes** → recvType=null → 二次 resolve 无提示（StackFold.cs:937-940 仅 recvType!=null 才重解析）→ 映射不命中 → VirtualCallExpr → VirtualTarget 抛。
4. 判定：f14（`ns.Length`）在 f14b 之前已通过（否则错误 owner 会是 System.String）→ 阻塞点精确落在 f14b。

### 3.3 net48 参考运行（f14b 期望语义）
`f14b: nre call` — null 接收者 ToString 必须抛 NullReferenceException（参考运行 37 行全对 expected.txt，[BASH] exdeep.exe）。

## 4. 缺口清单（后续任务施工单）

| # | 缺口 | 位置 | 修复方向（建议，待施工者定夺） |
|---|---|---|---|
| G1 [BLOCKER] | ExpandNestedTypes 接线缺失：嵌套类型从不发射 → recurse 生成代码 Node_class/Node 未定义 | Program.cs:25-26（无调用）；MetadataLoader.cs:32（跳过嵌套） | 在 LoadAssembly 返回前调用 `ExpandNestedTypes(md, types)`（md 在其作用域内，MetadataLoader.cs:27）——TypeEmitter.cs:80-81 的 caller contract 注明"LoadAssembly 之后、Generate 之前"。另可选：Program.cs 自行重开 PEReader（Generate 已示范：CodeGenerator.cs:23-24）。单测 TypeEmitterNestedTests 7/7 已覆盖 pass 行为 |
| G2 [BLOCKER] | callvirt owner System.Object not in translated types：f14b 卡死 exdeep 转译 | CodeGenerator.cs:1332-1334（抛点）；StackFold.cs:930-940（receiverType 提示缺失） | 把局部变量的**声明类型**（MethodBodyReader LocNames/LocalKinds 签名）注入 objTypes（或 callvirt 路径按本地声明类型兜底取 recvType），使 MethodMap.Object.cs:62-66 的 System.Object 分支命中 → 路由到 `il2cpp::runtime::icall_object_to_string`（已存在） |
| G3 | G2 修复后运行时语义未验：`icall_object_to_string` 对 null 接收者须抛 NullReferenceException（期望 `f14b: nre call`） | valuesemantics.cpp:246 起 | 施工后跑 exdeep 全 37 行 diff |
| G4 | G1 修复后 recurse 运行时未验：DeepAlloc 用 `GC.Collect()`（期望 mid-recursion 根不丢，sum=7998000）；Sum1ToN 10000 层、tail_sum 20000 层栈深；`Node` 实例字段写/读 cast | gen（重建后） | 施工后 cmake mingw + 运行 diff expected.txt 5 行；GC 根迭代若有缺口按行记录 |
| G5 | exdeep 嵌套用户类型（MyException/MyException2）在 G1 修复后须展开，异常链（throw_MyException2 → "MyException2,MyException2,MyException,…"）依赖 BaseType 解析（TypeEmitter.cs 已实现 DecodeNestedBaseType + 全列表解析） | TypeEmitter.cs:117-126+ | G1 落地后 exdeep 转译应越过 f14b 前全部 f1-f14 并到达 G2 阻塞点；届时 f1-f21 异常链可验 |

## 5. 验证证据汇总
- [BASH] dotnet build src/il2cppc -c Release → 0 警告 0 错误
- [BASH] dll 01:21:53 > 最新源 01:20:51（MethodBodyReader.cs）
- [BASH] dotnet il2cppc.dll recurse.exe → exit=0, "generated 1 type(s)"
- [BASH] cmake --build recurse/build_mingw → exit=2, 6 errors（Cases_Recurse.cpp:65/66/68/69）
- [BASH] dotnet il2cppc.dll exdeep.exe → exit=2, "callvirt owner System.Object not in translated types"
- [BASH] dotnet test --filter TypeEmitterNestedTests → 7/7 通过
- [BASH] recurse.exe (net48) → 5 行与 expected.txt 全同；exdeep.exe (net48) → 37 行与 expected.txt 全同
- [READ] Program.cs:25-26 / MetadataLoader.cs:32 / TypeEmitter.cs:90 / CodeGenerator.cs:1088-1089,1332-1334 / StackFold.cs:930-940,977-980 / MethodMap.Object.cs:62-66 / valuesemantics.cpp:246
