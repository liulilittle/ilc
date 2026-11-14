# 当前 HEAD 治理状态（锚：26442b31）

生成日期：2026-11-13
锚点：[BASH] `git log -1 --format='%H|%ci'` = `26442b31a5c1abfbe7774b22170ee851a8e2c707|2026-11-13 01:44:58 +0800`
定案：[READ:.opencode/plan.md:16-23] 主代理书面定案（轮43）

本文件是**当前 HEAD 的治理状态快照**，不冒充运行时绿证，也不把历史快照
（mtime 2026-10-05 的 project-state-2026*.md / roadmap-status-v*.md）当现状。
工作树为多代理共享未提交态；本轮 W11③ 执行 AI **未 commit / 未 push**
（提交授权与 W11⑤⑥ 双审盖章排序解耦，待盖章后主代理另行授权）。

## 本轮范围（W11③ 执行 AI）

按主代理书面定案实现 **F1 + F2 + F4 + 治理状态文档** 四项：

| 项 | 状态 | 内容 | 证据 |
| --- | --- | --- | --- |
| F1 | 已实现 + TDD 红→绿 | F1-a 空集 INCONCLUSIVE(exit2) 止血；F1-b 固定哨兵活跃 LOUD 契约集（scripts/loud-contracts.txt + tests/cases/zz-loud-sentinel）；接 ci.ps1 门禁链（build transpiler 后/e2e 前/固定 -Compiler mingw/throw 消费） | `.opencode/tmp/w11exec/f1-results-RED.txt`（RED）、`f1-results.txt`（GREEN 8/8）、`f1-mutation-evidence.txt`（J3 6/6 RED + RESTORE GREEN）、真实链 gate→difftest→sentinel EXIT=0 |
| F2 | 已实现 + TDD 红→绿 | F2-α 诚实去标：difftest.ps1 / diff-smoke.ps1 的 ValidateSet 收敛为 mingw/clang；docs/diffsweep-exclude-contract.md 登记去标 + 历史 `.msvc.*` 处置清单 | `f2-results-RED.txt`（RED）、`f2-results.txt`（GREEN 8/8）、`f2-mutation-evidence.txt`（J3 4/4 RED + RESTORE GREEN） |
| F4 | 已实现 + TDD 红→绿 | F4-a 三处 TypeEmitter.cs warn+skip → NotSupportedException；F4-c 探针/文档登记（docs/w11-f4-bitmap-limit.md） | `f4-results-RED.txt`（RED 4 fail）、`f4-results.txt`+`f4-green.trx`（GREEN 6/6）、`f4-mutation-evidence.txt`（J3 3/3 RED + RESTORE GREEN）、影响面 151/151 |
| 治理状态文档 | 本文件 | 以 26442b31 为锚的当前 HEAD 状态 | 本文件 |

## 明确不在本轮范围（scope 缺口登记，另案）

- **M5.5 功能绿证 / T301-T310 逐卡落地**：当前 HEAD 为**部分实现**——T304 统一 Invoker
  （`src/runtime/include/internal/invoker.h` / `src/runtime/src/invoker.cpp` /
  `tests/runtime/test_invoker.cpp`）与 T306 模式 B trimming
  （`src/il2cppc/Program.cs:189/:254` `FilterReflectionManifest` +
  `src/il2cppc/Emit/CodeGenerator.cs:14752-14765` 真消费）**已实质实现**；
  其余卡（T301/T302/T303/T305/T307/T308/T309/T310/T609）未落地。
  ⇒ 禁把 IMPLEMENTED 当 gap 重复实现，也禁据二者存在判 M5.5 已过（无 T609 分级规范 /
  T310 矩阵 / L0-L2 分级双跑绿证）。两套 L 编号（T609 level vs
  reflection-status-v2.md:38/:49 落地小节号）须隔离。本轮不实现。
- **M8 S2 实机**：外部实机依赖，未验证，另案。
- **F2-β 真双通道**：真 MSVC 端到端双通道绿证维持**未验证**；本轮 F2-α 不触发
  vcvars/VS generator 门禁（J7）。ci.ps1 主 MSVC 走 run-tests.ps1 独立真通道，未削弱。
- **F4-b 大改位图 ABI**：无天然触发实证、双方审计不推荐，不取；逐类型 slot 精确计数 +
  GC 存活探针标 **未验证**（验证方法见 docs/w11-f4-bitmap-limit.md）。
- **G14**（net48 定义下 CRLF 是否算 gap）：留另案定案。
- 历史 `.msvc.*` 6 份（`.opencode/tmp/dr-exec-msvc/loud-linq-decorderbygrp.msvc.*`，
  mtime 2026-11-12 20:31，`compiler=msvc ok=True` 实为 MinGW 产物）：登记为
  「作废重标 mingw / 标 deprecated」另案处置，本轮不删除/不改写（避动共享工作树
  历史证据）；`smoke-summary.tsv` 当前树不存在，无 tsv 行需迁移。

## 验收判据 J1-J8 状态

- J1 TDD 先红：F1/F2/F4 三项均保留 RED 证据 + GREEN 证据，红→绿链完整，未删除失败断言。
- J2 C0：`dotnet build src/il2cppc/il2cppc.csproj -c Debug --nologo -v q` 0 警 0 错；
  5+ 个相关 .ps1 `ParseFile=0`。`dotnet test tests/unit/unit.csproj` 侧有 **15 个 HEAD
  预存 analyzer 警告**（CS8602×1 / xUnit1026×3 / xUnit1012×10 / xUnit2013×1），
  改动前后一致、非本次引入（如实表述，禁谎报 0 警、禁称 restore 噪声）。
- J3 第16条变异：F1 6/6、F2 4/4、F4 3/3 全 RED，RESTORE 全 GREEN；均为**真实实现
  隔离副本** + `Copy-Item` 恢复（非 git restore、非 stub、禁留存变异体）。
- J4 影响面回归（禁全量）：F1→loud-gate/sweep-exclude/difftest:174；F2→difftest 三
  Compiler + diff-smoke/smoke-summary.tsv；F4→Layout/MarkEmbedded/emit 单测 151/151 +
  gc.cpp:705/:844 一致性。**不含 run-tests 的 160 build_msvc**。
- J5 绿证 mtime 均 > HEAD ci=2026-11-13 01:44:58（各证据文件 mtime 见报告）；
  `regression-final.txt`（2026-11-12）不算绿证。
- J6 精确 `git add <定案文件>`：须排除 ` D .gitignore`（tracked 删除，第29条禁
  checkout/reset/restore 覆工作树）；**本轮不授权 commit/push**。
- J7 工具链门禁：F2-α 不触发真 MSVC 端到端，端到端双通道绿证维持未验证。
- J8 参数/env 动态配置必保：`difftest -Compiler/-ReportDir/-TimeoutMs/-AppArgs`、
  `IL2CPP_DIFFTEST_LOCK_MS`、`IL2CPP_MSVC_GENERATOR`（build-app.ps1:193）、
  TMP/TEMP/TMPDIR/NUGET_PACKAGES 均保留。

## 三层区分（防误判）

定义层 = [READ:docs/project-state-20260810.md:14]「对齐 net48、M1-M8」；
历史快照层 = mtime 2026-10-05 的状态文档（快照 HEAD `8d865d2` 不在当前 git 对象库，
`git cat-file -t` exit=128）；当前 HEAD 层 = 26442b31 的 tracked 源码 + 可重跑一手产物。
任何「运行时绿」证据 mtime 必须晚于 `2026-11-13 01:44:58 +0800`。

## W5 诚实限定

- F4 是**静态一致性风险**（32-bit 位图 + gc.cpp 双 slot<32 + 三处 warn+skip 曾静默
  漏标），非已实证 GC 误回收；TRIGGER=UNVERIFIED。
- F1 哨兵真实链绿证证明 gate→真实 difftest→真实哨兵可检出契约丢失；oracle exit
  `0xE0434352` = .NET 未处理异常（双侧真抛），非 `0xC0000135` DLL 缺失环境红。
- 本轮所有绿证为**当前工作树源码 + 本机一手产物**，未 commit/push、未经双审盖章；
  未取得 W11⑤⑥ 双审一致盖章前不声称「任务完成/通过」。
