# Audit: EH 稳定性核查表 — exceptions / exceptionchain2 / cctor（2026-08-05 19:1x，快照验证）

## 任务与范围
- 目标：核查 EH 修复（f18 多 catch 链式分派：`OpenChainCatch`/`ContinueChainCatch`/`CloseChainCatch`，
  CodeGenerator.cs:754-1110）落地后 EH 域稳定性。
- exdeep **37/37 已证**（matrix4 13:41 复验 + 修复记录），本次不重复。
- 流程：重建 il2cppc.dll → 转译（dll 直跑）→ 构建 → 运行 → diff。
- 只读 + 文档；未改任何源码。

## 用例澄清（ehchain）
- 仓库中**不存在 `ehchain`**（无目录 / 无文件 / 无 doc / 无脚本引用，全仓检索 0 命中）。
- 用户确认"ehchain 是我记错了"。第三例采用 EH 修复记录同批用例 **`cctor`**（修复记录原文
  "exceptionchain2 17/17、exceptions/cctor PASS"，EH 域，cctor 异常语义），并在本表标注替代。

## 环境快照与干扰记录
- 验证期间**并行代理会话持续写源**（timezone/crypto/string-pool/Expr 批次）：
  - `crypto_obj.h:99` mid-edit（`Il2CppMd5Context` 未限定 `runtime::` 命名空间，crypto_obj.cpp 18:23 起）
    → **runtime 从源码构建被阻塞**（3 用例 build_mingw 全部在 crypto_obj.cpp.obj 失败，Error 1）。
  - 转译器源码**间歇可编译**：18:41 绿 → 18:51 红（CS1501 EmitMain 5vs4 参，t3 string-pool 重构
    只改调用侧 CodeGenerator.cs:448）→ 19:08 绿。
- **采用 matrix4 既有方法**：共享预构建 `build/runtime_e2e_shared/libil2cpp_runtime.a`
  （13:22:40）+ wrapper CMakeLists（GLOB gen/src/*.cpp 排除 host.cpp，链接共享库）。
  EH runtime 域源自 08-04/08-03 未变（exception.cpp 08-04 17:20、exceptionext.cpp 08-03、
  il2cpp_exception_ext.h 08-03）→ 共享库 EH 语义与当前一致。
- 快照标记：转译器 dll **19:11:47（427,008 B，dotnet build 0 错误 0 警告）**；共享库 13:22:40。
  文档写作时（20:38）源仍在写（Expr.cs 20:36、CodeGenerator.cs 20:17、decimal_format.cpp 20:34），
  快照之后的工作树变化**未**纳入本验证。

## 核查表（3/3 PASS）

| # | 用例 | 转译(il2cppc.dll) | 构建(wrapper) | 运行 | diff vs net48 参考 | expected.txt byte-check | 结论 |
|---|------|-------------------|---------------|------|--------------------|--------------------------|------|
| 1 | exceptions | OK exit=0 | OK exit=0 | exit=0，11 行 | **PASS** 11/11 字节相等 | 无 expected.txt（oracle=net48） | **PASS** |
| 2 | exceptionchain2 | OK exit=0 | OK exit=0 | exit=0，17 行 | **PASS** 17/17 字节相等 | **PASS**（17×True 逐字节） | **PASS** |
| 3 | cctor（替代 ehchain） | OK exit=0 | OK exit=0 | exit=0，4 行 | **PASS** 4/4 字节相等 | 无 expected.txt（oracle=net48） | **PASS** |

## 证据（本任务内捕获）
- [BASH] `dotnet build src/il2cppc -c Release` → 0 错误 0 警告；dll 19:11:47 / 427,008 B。
- [BASH] `dotnet src/il2cppc.dll <refExe> <gen>` ×3 → 全部 exit=0（transpile OK）。
- [BASH] wrapper `cmake -G "MinGW Makefiles"` + `cmake --build` ×3 → 全部 exit=0（共享库链接，避开
  crypto_obj.cpp 源码构建阻塞）。
- [BASH] `il2cpp_gen_exe.exe` ×3 → 全部 exit=0；行数 = exceptions 11 / exceptionchain2 17 / cctor 4。
- [BASH] net48 参考运行（`out/<case>.exe`）exit=0：exceptions 11 行、exceptionchain2 17×True、cctor
  10/30/42/5。
- [BASH] diff（LF-join 字节比较）：3/3 相等；exceptionchain2 expected.txt 逐字节 PASS。
- [READ] CodeGenerator.cs:754-1110 EH 链式分派符号在快照与当前工作树均存在（11 命中）。

## 结论
- **exceptions / exceptionchain2 / cctor 3/3 PASS**。EH 修复（f18 链式分派）稳定：与修复记录
  （exceptions/cctor PASS、exceptionchain2 17/17、exdeep 37/37）一致，EH 域 4 例全绿。
- **注意（快照语义）**：验证基于 19:11:47 转译器 + 13:22:40 共享 runtime 库，期间并行代理持续改源；
  若需对完整当前源码状态复验，须待并行代理收敛（mtime ≥30min 无写）后以 run-tests.ps1 官方流程重跑。
