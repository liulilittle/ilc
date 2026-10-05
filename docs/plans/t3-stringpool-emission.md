# T3 字符串池静态表——CodeGenerator 接线施工单（数据文件半已交付并验证，接线半被并行代理阻断）

日期：2026-08-05（T3 任务第二次执行）。设计：docs/architecture/string-pooling-design.md §2.3。
前置：T1/T2（0d28414/8ae3c04）+ **T3 数据文件半（已交付 + 本次验证）**：
`include/internal/string-pool.h` 由 Program.cs 经 `StringPoolPass.WritePoolHeader` 自动发射。

> **本单只剩 CodeGenerator.cs 三处接线**（ldstr 发射 + 填充调用 + generated.h include）+
> Program.cs 消费点。**动工前查 mtime**（项目铁律 + 任务指令）。本次执行中 CodeGenerator.cs
> 被 BCL-virtual 反虚拟化代理独占（mtime 6:16:16 → 8:46:42 持续变、git M 未提交、活跃编辑），
> 故本卡只做 Program/Opt 层验证 + 本施工单。下面给出**已逐行核实的完整接线配方**。

## 0. 现状（2026-08-05 09:00 实测证据）

- 数据文件半已交付且端到端验证：
  - `dotnet build src/il2cppc` → 0 错 0 警；`dotnet test --filter StringPoolTests` → 23/23 PASS。
  - 探针转译（tests/cases/intern，13 个 ldstr 位点 → 4 distinct）实测：
    `string pool: 4 distinct literal(s) from 13 site(s) across 6 method(s) (0 constant concat(s) folded, 9 duplicate site(s) deduped)`
    + `string pool table: g_strPool[4] -> include/internal/string-pool.h`。
  - 发射的 string-pool.h 自含（只依赖 il2cpp_runtime.h），clang-17 `-std=c++17 -Wall -Wextra
    -fsyntax-only` 独立编译 EXIT=0（仅 .h 扩展名 driver 提示，非代码问题）：
    ```cpp
    #pragma once
    #include "il2cpp_runtime.h"
    namespace il2cpp {
    inline Il2CppObject* g_strPool[4];
    inline void il2cpp_string_pool_fill() {
        g_strPool[0] = il2cpp_string_intern_literal("abc"); // 行序 = 池 id 序
        g_strPool[1] = il2cpp_string_intern_literal("xyz");
        g_strPool[2] = il2cpp_string_intern_literal("");
        g_strPool[3] = il2cpp_string_intern_literal("x");
    }
    } // namespace il2cpp
    ```
- 接线半（本次新确认）：生成的方法体仍为每 ldstr 位点一次 `(Il2CppObject*)il2cpp::il2cpp_string_intern_literal("...")`
  （探针 gen 输出 13 处调用）——ldstr 降为索引 load 未落地。

## 1. 冲突史（为何仍未动 CodeGenerator.cs——2026-08-05 实测）

- 08:27 检查：CodeGenerator.cs mtime 6:16:16（闲置 2h+）→ 按任务指令可编辑。
- 08:27-08:46 只读勘察期间：CodeGenerator.cs mtime 变 8:46:42（git diff：53+/4-——
  EmitVirtualCall BCL 基类反虚拟化、OwnerIsTranslated、MethodCppName→UserMethodCppName、
  MethodContext 扩展——**BCL-virtual 域，非字符串域**），且无新 commit（HEAD 仍 b59c6d9）。
- 08:58:59 CodeGenerator.cs 再次落盘（8:46:42 后继续编辑）；9:07:44 MethodMap.Console.cs
  ——同代理持续在途，本卡全程未获安全编辑窗口。
- 判定：git M + mtime 持续变 + 未提交 = 并行代理在途 → 任务规则"冲突则只做 Program/Opt 层+方案文档"。
- **后续轮次动工前必须重查**：mtime 稳定 ≥ 数小时 + 无新 diff + 无活跃 pwsh 才可编辑。

## 2. 接线点 0：Generate 权威池构建（D1 单写者——整个接线的地基）

**为什么**：`g_strPool[id]` 索引与表行必须来自**同一个池**。Generate 独立建池而
Program 另跑 ScanAssembly 写表 = 双路径（D3）——Normalize.FoldStmts 会删除死分支中的
字面量（Generate 侧池少行），两池发散后索引/表错位 = 运行期取错字符串。修复：**Generate
在自身方法循环内建池（fold→collect），发射即查同一池；Program 消费 Generate 的返回值**。

- CodeGenerator.cs:14 签名：`public static void Generate(...)` → `public static GenerateResult Generate(...)`
  （`public sealed record GenerateResult(StringPool Pool, int Folded, int Methods);`，放 Emit 命名空间）。
  既有调用方忽略返回值，源码兼容。
- 需要 `using Il2Cpp.Transpiler.Opt;`（CodeGenerator.cs:1-7 using 区）。
- **锁**：xUnit 并行跑测试类，Generate 被多测试并发调用；若发射路径用静态池字段
  （`private static StringPool? _pool`）必须 `private static readonly object _poolLock`
  包住 Generate 主体（改名 `GenerateCore` 内层避免 400 行缩进改动），否则测试交错 = 错 id。
  运行时 CLI 单线程无竞态。
- 方法循环内（CodeGenerator.cs:301 `stmts = Normalize.FoldStmts(...)` 与 :302
  `EmitBody(...)` 之间）插入：
  ```csharp
  var (pooled, foldedN) = StringPoolPass.FoldConcatConstants(stmts);
  stmts = pooled;                       // 折叠产物参与发射（设计 §2.2 顺序约束）
  foldedCount += foldedN;
  StringPoolPass.Collect(_pool, pooled); // 与 ScanAssembly 同形——同 pipeline 同 id
  ```
  方法计数在循环内同样累加（与 ScanAssembly 的过滤条件一致：interface/delegate/PInvoke/
  abstract/折叠 accessor 均跳过——Generate 循环已天然跳过）。

## 3. 接线点 1：ldstr 发射 → 单次索引 load（CodeGenerator.cs:1134-1143）

```csharp
case StringConstExpr s:
    // T3 string pool (design §2.3): ldstr -> single load from the static
    // table g_strPool[id] (built by Generate's in-loop fold->collect pass).
    // Table entries ARE intern-pool instances (fill uses intern_literal), so
    // .NET interned identity semantics are preserved and no cast is needed
    // (g_strPool is Il2CppObject* -- the IR's uniform reference shape).
    return $"il2cpp::g_strPool[{_pool!.IdOf(s.Value)}]";
```
- `IdOf` 幂等：同内容必回同 id（StringPoolPass.cs:56-65）；发射时所有内容已被 Collect 注册，
  不会新增行；即便新增，表也由同一池写（仍一致）。
- 边界：StringConstExpr 出现于非常量上下文（MarshalUtf8Expr 参数 CodeGenerator.cs:1148 等）——
  `g_strPool[id]` 与旧形态同型（Il2CppObject*），无需 cast。
- **勿动** CodeGenerator.cs:1292（ExceptionEntryExpr）——运行时计算字符串，非字面量，保持 intern_literal。
- 转义一致性已核实：CodeGenerator.EscapeC（:2444）与 StringPoolPass.EscapeCppLiteral（:209）
  逐字节一致（同 5 替换同序）。理想收敛为单实现——需动 CodeGenerator，留待接线轮。

## 4. 接线点 2：generated.h 条件 include（类型循环后、generatedHeader 落盘前，约 :356）

```csharp
// T3 string pool (design §2.3): the static table data file -- emitted by
// Program.cs only when the pool is non-empty; include conditionally.
if (pool.LiteralCount > 0)
    generatedHeader.Line("#include \"string-pool.h\"");
```
- **必须条件化**：空池不写文件（WritePoolHeader 空池 no-op）→ 无条件 include 会让所有
  无字符串程序的 generated.h 引用不存在的头文件，全量 e2e 崩塌。
- 空池程序（calc/refarr 等）生成输出**零变化** = 零回归路径。

## 5. 接线点 3：entry.cpp 急切填充（EmitMain）

- EmitMain 签名（:2512）加 `StringPool pool` 参数（唯一调用点 :370 一并改）。
- `il2cpp::runtime::runtime_init();`（:2525）**之后立即**加：
  ```csharp
  // T3 string pool (design §2.3): eager fill BEFORE any generated user code
  // can run -- the Main-type .cctor (ensure_cctor_* below) may ldstr; the
  // table must be filled or ldstr reads uninitialized slots. Entries are the
  // intern-pool instances (the intern pool is a GC root), so the table needs
  // no root of its own.
  if (pool.LiteralCount > 0)
      w.Line("il2cpp::il2cpp_string_pool_fill();");
  ```
- **位置是硬约束**：必须早于 :2558-2562 的 `ensure_cctor_mainType()`（Main 的 .cctor 可能
  ldstr）；放类型注册循环（:2545）之前/之后均可（注册不发用户代码）。
- Main 类型不存在（库形态）→ EmitMain 提前 return → 无填充调用，由外部宿主负责
  （与现状一致，无需改动）。

## 6. Program.cs 消费点（接线落地后同步改，两处）

- :36 `CodeGenerator.Generate(...)` → `var result = CodeGenerator.Generate(...)`。
- :57-74 `WriteStringPoolReport(List<TypeDef>, string, string)` → `WriteStringPoolReport(GenerateResult, string)`：
  pool = result.Pool（不再 ScanAssembly），folded/methods = result 字段，其余不变
  （WritePoolHeader + 报告行格式保持）。
- ScanAssembly（StringPoolPass.cs:271-315）保留为独立 API（公共方法），其注释
  （:264-270 "CLI hook"）与文件头注释（:37-42）届时改为"Generate 为权威路径，本方法为
  独立诊断/参考 API"。

## 7. 验证清单（接线完成后——本卡只完成了 1/6 项）

1. [x] Program/Opt 层端到端：build 0 错 0 警 + StringPoolTests 23/23 + 探针报告/表文件/独立编译
2. [ ] `dotnet build src/il2cppc` 0 错 0 警 + `dotnet test` 全绿（StringPool 套件 + 既有回归）
3. [ ] 单测（tests/unit/StringPoolTests.cs 本域追加，FixturePath 同 CodeGeneratorTests 模式，
   fixture = capicalc（唯一含字面量 "boom" 的 fixture））：
   Generate(capicalc) → 返回 Pool 含 "boom"；`include/internal/string-pool.h` 存在且含
   `g_strPool[` 与 `intern_literal("boom")`；generated.h 含 `#include "string-pool.h"`；
   entry.cpp 含 `il2cpp_string_pool_fill();`；`src/Program.cpp` 含 `g_strPool[` 且
   **不含** `il2cpp_string_intern_literal("boom")`（每测试临时目录，finally 删除）
4. [ ] 探针：转译 intern（13 位点 → 4 distinct）→ gen 方法体 g_strPool[id] 单 load、
   intern_literal 调用从方法体消失、表 4 行 + entry 填充（本次已录基线：接线前 13 处调用）
5. [ ] e2e 只读双跑：tests/cases/stringcase/ + interncase2/（net48 参考逐字节一致）；
   注：折叠进发射后常量 concat 实例身份变化（C# 编译器源码级已折叠常量拼接，e2e fixture
   几乎不触发；若 stringcase 出现 diff 按 net48 oracle 复核）
6. [ ] 运行时测试 tests/runtime/test_stringpool.cpp（设计 T3 卡）：fill 后同内容引用相等、
   与 String.Intern 一致、动态 intern 不受影响——运行时零改动，该卡独立排期
7. [ ] 三编译器（MSVC/MinGW/WSL GCC 7.5）零警告

## 8. 阈值旋钮（设计 §3：长字符串不池化，默认 1KB——YAGNI 留档，勿提前实现）

见旧版 §4：加阈值会破坏 `g_strPool[id]` 直映射不变式（池 id ≠ 表槽需重编号），动之前先改设计。

## 9. 关键决策记录（接线轮必须遵守，防返工）

| 决策 | 理由 |
|---|---|
| 池只在 Generate 建（权威），Program 消费返回值 | D1 单写者；D3 双路径发散（Normalize 删字面量） |
| Generate 返回 GenerateResult，非静态属性 | xUnit 并行测试类交错读静态 = 幻影数据 |
| 发射内部静态池字段 + lock 包裹 Generate | 避免 30+ 处 EmitExpr 调用签名改造；锁只串行化测试 |
| include/fill 条件化（N>0） | 空池无文件；无条件 = 全量 e2e 崩塌 |
| fill 在 runtime_init 后、ensure_cctor 前 | Main .cctor 可能 ldstr；未填充读 = 野指针 |
| fold→collect 在 Normalize 后 EmitBody 前 | §2.2 顺序约束 + 发射即查同池 |
| EscapeC/ EscapeCppLiteral 双实现暂存 | 收敛需动 CodeGenerator（被独占）；两处已核实逐字节一致 |
