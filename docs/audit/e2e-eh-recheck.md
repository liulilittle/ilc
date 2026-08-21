# e2e EH 域复核 — exceptionchain2 / exdeep / exceptions / cctorchain（2026-08-06）

## 结论

**4/4 PASS**（全链路 转译→构建→运行→diff，MinGW x64，输出与 net48 oracle 逐字节一致）。

| # | 用例 | 转译 | 构建 | 运行 | diff | 结果 |
|---|------|------|------|------|------|------|
| 1 | exceptionchain2 | exit=0 | cmake 0 err | exit=0 | 17/17 逐字节 | **PASS** |
| 2 | exdeep | exit=0 | cmake 0 err | exit=0 | 37/37 逐字节 | **PASS** |
| 3 | exceptions | exit=0 | cmake 0 err | exit=0 | 11/11 逐字节 | **PASS** |
| 4 | cctorchain | exit=0 | cmake 0 err | exit=0 | 15/15 逐字节 | **PASS**（`boom_caught=2`） |

## 代理收工确认（前置门）

| 检查项 | 证据 | 判定 |
|--------|------|------|
| CodeGenerator.cs 空闲 | mtime 10:09:27（10:43 检查，≥30min） | ✓ |
| il2cppc.dll 重建 | mtime 10:09:41 > CodeGenerator.cs | ✓ |
| EH runtime 源稳定 | exception.cpp 8/4 17:20 / exceptionext.cpp 8/3 / il2cpp_exception_ext.h 8/3 | ✓ |
| 共享库 | build/runtime_e2e_shared/libil2cpp_runtime.a 8:52:12 | ✓ |
| 无活动构建 | 仅 VBCSCompiler（Roslyn 服务器 10:36:57）；无 g++/cc1plus/cmake 进程；runtime_fullcheck7 最后写 10:29:36 | ✓ |

## 关键断言逐条验证

### cctorchain boom_caught=2（wo4 §2.1 守卫形状）
[READ:tests/cases/cctorchain/expected.txt:13-14] `Boom.cctor` ×1 + `boom_caught=2`。
[BASH:gen exe 输出] 与 expected.txt **15/15 逐字节一致**，含 `Boom.cctor` 单行 + `boom_caught=2` + `boom_survived=True`。

[READ:gen/src/Cases_Boom.cpp] 守卫逐字符合 wo4 §2.1：
- `bool Cases_Boom_cctor_done = false;` + `static bool _cctor_running` + `static const char* _cctor_exc_type/_exc_msg`（4 变量）
- `if (done) return; if (running) return; if (exc_type) throw DotNetException(...);`
- `try { Body(); } catch (DotNetException& ex) { exc_type=..; exc_msg=..; running=false; throw; } catch (...) { running=false; throw; }`
- `running = false; done = true;`（**成功后置位**）
- 证明：访问 1 跑体+抛+缓存；访问 2 命中缓存分支直接重抛不重跑体 → `Boom.cctor` 只打一次且 `boom_caught=2`。

### exdeep 多 catch 子句链式分发（EH 修复）
[READ:gen/src/Cases_Exdeep.cpp:169-173]：
```cpp
catch (il2cpp::DotNetException& __il2cpp_ex) {
    if (il2cpp::il2cpp_exception_matches("System.ArgumentException", ...)) {
    } else if (il2cpp::il2cpp_exception_matches("System.InvalidOperationException", ...)) {
    } else {
        throw; // unmatched -> rethrow, no C++ same-type-catch fallthrough bug
    }
}
```
单 catch + `if/else if/else throw` 分派链（OpenChainCatch/ContinueChainCatch/CloseChainCatch，CodeGenerator.cs:1073-1103），回避 GCC 同类型 catch 重抛不续检的既有缺陷。f18 三子句区第 2/3 子句现均命中。

### exceptionchain2 / exceptions
exceptionchain2 17 行全链（嵌套 try/catch/finally + 多子句 + 类型匹配 + 重抛），exceptions 11 行，均逐字节 PASS。空首子句/重抛/外层区无回归。

## 复用证据链
- 全部 4 例：`dotnet build` ref → `dotnet il2cppc.dll` 转译 exit=0 → cmake MinGW 构建 0 err → `il2cpp_gen_exe.exe` 运行 exit=0 → 与 net48 oracle 逐字节 diff PASS。
- 用例源码自 8/3-8/5 未动（exceptionchain2.cs 8/5 3:27、exdeep.cs 8/3 15:45、cctorchain.cs 8/3 19:33）。

## 遗留
- exceptions 用例无 expected.txt（运行期以 net48 ref 直跑为 oracle，正常）。
- 他域在途（与 EH 无关）：io.cpp 10:16、stringbuilder.cpp 9:46、MethodMap.Type.cs 10:03、StackFold.cs 10:00（转译器批）。
