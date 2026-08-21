# Decimal 域核查记录（2026-08-05）

> 验证任务：核查 T1/T2/T3 三代理并行后 decimal 域现状（可能已建）。
> 方法：glob 目标文件 + grep 内容 + git 溯源 + 进程/mtime 活性检查。

## 状态：NOT BUILT（未建）——仅设计文档

目标产物 5 项全部缺失，T1/T2/T3 无任何实现落地。

## 逐项核查证据

| # | 目标 | 期望路径 | 结果 | 证据 |
|---|---|---|---|---|
| 1 | decimal.h | src/runtime/include/internal/decimal.h | **缺失** | [GLOB:src/runtime/include/internal/*.h 无 decimal.h；有 versionuri.h/guid.h 先例] |
| 2 | decimal.cpp / decimal_format | src/runtime/src/decimal.cpp / decimal_fmt.cpp | **缺失** | [GLOB:src/runtime/src/*.cpp 无任何 decimal* 文件] |
| 3 | MethodMap.Decimal.cs | src/il2cppc/Emit/MethodMap.Decimal.cs | **缺失** | [GLOB:src/il2cppc/Emit/MethodMap.*.cs 无 Decimal.cs；Guid.cs/DateTime.cs 先例在] |
| 4 | test_decimal.cpp | tests/runtime/test_decimal.cpp | **缺失** | [GLOB:tests/runtime/test_*.cpp 无 test_decimal.cpp] |
| 5 | cmake 测试接线 | CMakeLists.txt decimal 源/测试条目 | **缺失** | [GREP:CMakeLists.txt "decimal" 零匹配] |

## 佐证

- **e2e 卡例未解封**：fmtchain.cs:15-16 排除注释仍在
  `// decimal.ToString(format) -- no MethodMap entry for System.Decimal`。
- **git 无 decimal 提交**：`git log --all --grep=decimal -i` 零结果；decimal-domain.md 为
  未跟踪文件（04:18:44，旧设计文档，非本次施工产出）。
- **无在建活动**：无 cmake/g++/mingw32-make/dotnet 进程；src 最近写 12:50:01
  （matrix4 批次遗留，非 decimal 相关）。
- **decimal 域仅存在于任务卡规划**：docs/plans/taskcards-128.md:357-358（含
  il2cpp_decimal.h/decimal.cpp/test_decimal.cpp/MethodMap.Decimal.cs/decimalcase e2e）。

## 结论

T1/T2/T3 三代理未产出任何 decimal 代码。起点 = docs/design/decimal-domain.md（设计完备，
含 T1/T2/T3 任务卡拆分 §5）。实施前需：参照 regex/guid 域先例确认代理收工标准；
无 runtime 符号依赖时 T3 可并行准备测试占位。

---

## 追加：快照 2（15:15，IN PROGRESS——核查期间代理仍在写）

> 核查期间发现实现已启动（初始 NOT BUILT 结论已过时，仅 5 分钟窗口内变化）。

### 新出现的 decimal 工件（15:15 后）

| 工件 | 时间 | 说明 |
|---|---|---|
| tests/cases/decimalcase/decimalcase.cs | 15:15:18 | e2e oracle，30 断言全 bool（T1 字面量/四则/比较 + T2 ToString/Parse 已知值 + T3 decimal→int 转换 + 100m/7m 溢出探针） |
| tests/cases/decimalcase/decimalcase.csproj | 15:15:19 | net48 工程 |
| tests/cases/decimalcase/expected.txt | 15:15 前后 | 30 行 net48 oracle（29 True + 1 False = 100m/7m*7m） |
| tests/cases/decimalcase/obj/out/ | 15:25:48 | net48 编译产物（.NET 编译，非转译输出） |

### 仍缺失（核心实现 4 项，glob 实时复查确认）

- src/runtime/include/internal/decimal.h / decimal_format.h — **无**
- src/runtime/src/decimal.cpp / decimal_format.cpp — **无**
- src/il2cppc/Emit/MethodMap.Decimal.cs — **无**
- src/runtime/CMakeLists.txt decimal 源条目 / tests/runtime/test_decimal*.cpp — **无**
- decimalcase 转译产物（gen_* C++ / 运行输出）— **无**（out 下仅 net48 exe）

### 活跃性证据

- dotnet 1313608 = VBCSCompiler（Roslyn 编译守护，14:44:48 起），非 decimal 构建进程。
- 共享 .opencode/ 文件被并发会话覆盖：process.md 现为 decimal T2 会话日志
  （"契约先行"：T1 decimal.h 不存在 → decimal_format.h 自含 Il2CppDecimal 定义）；
  memory.md 现为尾调用优化验证会话内容。
- src 最近写 stringext.cpp 14:27:52（decimal 域外，他域代理）。

### 修正结论

**decimal 域状态 = 实施中（T3 e2e fixture 已落地；T1/T2 runtime icall 与 T3 接线未落）。**
T2 会话已确认「契约先行」策略（decimal_format.h 自含结构定义）。验证若需「完成态」结论，
须等三代理收工（mtime ≥30min + 无构建进程，参照 matrix4 轮询标准）后重跑本核查。
