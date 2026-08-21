# e2e 排序域重验（e2e-sort-recheck）— 2026-08-06 10:29-10:47，30 分钟验证任务

> 范围：sortdeep / sortcase2 / arrcomp（排序域 stability 核查，long Sort 代理松手后）。

## 前置门槛：代理收工确认

| 检查项 | 证据 | 结论 |
|---|---|---|
| 排序域源文件 mtime ≥30min | MethodMap.Array.cs 8/5 10:20、MethodMap.ArraySort.cs 8/5 10:41、arraysort.cpp 8/5 10:52 | ✓（均 ≥20h，远超 30min） |
| 用例源文件 mtime ≥30min | sortdeep.cs 8/5 10:34、sortcase2.cs 8/6 0:23、arrcomp.cs 8/6 8:47 | ✓（arrcomp 最新 8:47，距启动 10:29 达 102min） |
| il2cppc.dll 已重建 | `src/il2cppc/bin/Release/net8.0/il2cppc.dll` mtime **2026/8/6 10:25:32** | ✓（晚于 CodeGenerator.cs 10:09 / TempSlotPass.cs 10:22） |
| 无活跃代理/构建 | 仅存 dotnet VBCSCompiler 守护进程（PID 698528，10:25:30 创建 = 构建残留）；无 cmake/g++/make 进程 | ✓ |

## 逐用例全链（转译→构建→运行→diff，MinGW）

| 用例 | 转译 gen mtime | 构建 exe mtime | run-tests 结果 | expected.txt 字节级 diff | 结论 |
|---|---|---|---|---|---|
| sortdeep | gen/src/Program.cpp 10:39:48 | build_mingw/il2cpp_gen_exe.exe 10:40:27 | **PASS 1/1**（EXITCODE=0） | LF-normalized SHA256 **A66730C7…D299** 一致 | **PASS** |
| sortcase2 | gen/src/Program.cpp 10:43:12 | build_mingw/il2cpp_gen_exe.exe 10:43:52 | **PASS 1/1**（EXITCODE=0） | BYTE-EXACT SHA256 **32F7DFF6…73B1** | **PASS** |
| arrcomp | gen/src/Cases_ArrComp.cpp 10:46:29 | build_mingw/il2cpp_gen_exe.exe 10:46:39 | **PASS 1/1**（EXITCODE=0） | BYTE-EXACT SHA256 **4EE81358…C25** | **PASS** |

## 证据链

- [BASH:pwsh run-tests.ps1 -Case sortdeep mingw] → `PASS: sortdeep` / `1/1 passed`
- [BASH:pwsh run-tests.ps1 -Case sortcase2 mingw] → `PASS: sortcase2` / `EXITCODE=0`
- [BASH:pwsh run-tests.ps1 -Case arrcomp mingw] → `PASS: arrcomp` / `EXITCODE=0`
- [BASH:每用例独立运行 build_mingw/il2cpp_gen_exe.exe + Get-FileHash 比对] →
  sortdeep LF-normalized 相等（expected.txt 为 CRLF 31 行，先规整 `\r\n→\n` 消除格式伪影）、
  sortcase2/arrcomp 逐字节相等（expected.txt 均为 LF）
- run-tests.ps1 内部 oracle = net48 live 输出（L52 `$expected = & net48 exe`）逐行比对（L88-90），
  与预期文件比对双轨一致。

## 结论

- **排序域 stability：3/3 PASS**（sortdeep / sortcase2 / arrcomp，转译 + MinGW 构建 + 运行 diff
  全链通过，字节级一致）。
- 与 matrix5 基线一致（sortcase2 PASS 见 e2e-full-matrix5.md:81；arrcomp 全链 PASS 见
  e2e-sprint-worklist.md §16，本次在 il2cppc.dll 10:25:32 快照下重验确认）。
- long Sort 代理松手后零回归；排序域（arraysort/arrayext direct-call 裸 `il2cpp::` 约定 +
  net48 oracle）稳定。

## 产物

- 验证日志：`%TEMP%\opencode\sortrecheck_sortdeep.log` / `sortrecheck_sortcase2.log` / `sortrecheck_arrcomp.log`
- 实际输出快照：`%TEMP%\opencode\sortrecheck_actual\*.actual.txt`
