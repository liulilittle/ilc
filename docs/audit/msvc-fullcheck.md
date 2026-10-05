# MSVC 全量构建验证报告 — metadata.cpp 修复后（三编译器矩阵第三臂）

> 只读 + 运行 + 写文档，仓库零改动。全新构建目录 `E:\il2cpp\build\runtime_msvc2\`（非复用）。
> 完整构建日志：`build\runtime_msvc2\build_log_fullcheck.txt`；测试逐进程输出：`build\runtime_msvc2\Release\*.run.txt`；退出码表：`build\runtime_msvc2\test_exitcodes.txt`。
> 日期：2026-08-04。

## 1. 工具链与构建环境

| 项目 | 值 | 来源 |
|---|---|---|
| VS 实例 | VS2022 Enterprise（`C:\Program Files\Microsoft Visual Studio\2022\Enterprise`） | [BASH:vswhere -latest] |
| cl.exe | MSVC 19.39.33523.0（toolset 14.39.33519） | [BASH:cmake configure] |
| Windows SDK | 10.0.22621.0 | [BASH:cmake configure] |
| cmake | 3.22.1 | [BASH:cmake --version] |
| 生成器 | `Visual Studio 17 2022` + `-A x64`，配置 `Release` | [BASH:cmake -S tests/runtime -B build/runtime_msvc2] |
| 编译选项 | runtime 库与测试：`/W4 /EHsc /bigobj /utf-8`（CMakeLists.txt MSVC 分支）+ Release 默认 `/O2 /MD` | [READ:src/runtime/CMakeLists.txt:17]、[READ:tests/runtime/CMakeLists.txt:35] |

## 2. 构建结果

| 项 | 结果 |
|---|---|
| runtime 静态库 `il2cpp_runtime.lib`（含 **metadata.cpp 修复**） | **构建成功** → `build/runtime_msvc2/runtime/Release/il2cpp_runtime.lib`（0 告警） |
| 测试可执行文件 | **87/88 构建成功**（`build/runtime_msvc2/Release/*.exe`） |
| 编译失败 | **1/88**：`test_string_utf8`（3 处错误，单文件同根因，见 §4.1） |
| 告警 | **4 处 C4996**（`strncpy`，仅测试文件；runtime 库 0 告警）——见 §4.2 |

对比基线 `docs/audit/msvc-probe.md`（上一轮 68/70，失败 2 文件同根因 `<limits>`）：本轮 `test_numberformat`/`test_formatext` 已含 `#include <limits>`（[READ:test_numberformat.cpp:35]、[READ:test_formatext.cpp:39]），不再失败；本轮新失败仅 `test_string_utf8`。

## 3. 测试结果（Release 全量运行，87/87 个构建成功的 exe 全部执行）

**86 PASS / 1 FAIL**（`test_math`）。

| 类别 | 数量 | 明细 |
|---|---|---|
| PASS（exit=0） | 86 | 全部 `test_*.exe`（含 metadata 修复相关的 string/typeinfo/hashtable 等） |
| FAIL（exit=1） | 1 | `test_math`：2 条 signed-zero 断言失败，见 §4.3 |
| 未构建 | 1 | `test_string_utf8`（编译错误，见 §4.1） |

PASS 列表（86，按字母序）：allocator, array_extra, array, arraybounds, arrayext, arrayext2, arraysort, attribute, bench_allocator, bench_inline, binaryio, bitconverter, boxedtostring, boxingext, boxingext2, buffer, concat, console, conv, convert, convertbase64, cultureinfo, debugext, delegate_cb, delegateext, dictsi, encoding, enumext, environment, exception, exceptionext, format, formatext, frame_deep, gc, gcapi, gcext, gcstress, guid, hashtable, interlocked, intptr, io, linq_map, linq_sort, linq, listi4, marshal, marshal2, marshalext, math_extra, mathext, monitor, numberformat, object, parse, pathproc, pinvoke, pinvokeext, random, regex_api, regex_match, sortedlist, stackqueue, stopwatch, streamio, string, stringbuilder, stringext, stringext2, stringext3, stringhash, stringintern, stringrw, thread, threadext, threadpool, time, timeext, timeext2, timer, typecheck, typeext, typeinfo, valuesemantics, versionuri。

FAIL 明细（`test_math.run.txt`）：

```
FAIL: max_d(-0.0,0.0) == -0.0 (first operand wins the tie)
FAIL: min_d(0.0,-0.0) == +0.0 (first operand wins the tie)
2 math assertion(s) failed
```

## 4. 问题清单

### 4.1 编译错误 — `test_string_utf8.cpp`（MSVC 特有，GNU 扩展不兼容）

| 文件:行号 | 错误 | 原因 |
|---|---|---|
| `tests/runtime/test_string_utf8.cpp:83` | C2010 `'字符': 宏名称中的字符` | `#define CHECK(what, bytes...)` 用 **GNU 具名变参宏** `bytes...`；MSVC 只认 C99 匿名 `...`（[READ:test_string_utf8.cpp:83]） |
| `tests/runtime/test_string_utf8.cpp:123` | C2466 `不能分配常量大小为 0 的数组` | `const char16_t expected[] = {};` 空初始化器（GNU 扩展，零长数组） |
| `tests/runtime/test_string_utf8.cpp:125` | C2466 同上 | `CHECK("empty input ...")` 展开为 `static const unsigned char b[] = { };` 空初始化器 |

**归因：MSVC 特有**。[BASH:g++ -fsyntax-only test_string_utf8.cpp → EXIT=0]（MinGW clang 17 接受 GNU 具名变参宏 + 空初始化器）。文件作者为并行代理（`tests/runtime/test_string_utf8.cpp` 首建 2026-08-03 19:32），仅对 MinGW 验证过。修复方向（本报告不修）：改 C99 匿名变参 `#define CHECK(what, ...)`，空数组用例改用 `unsigned char b[1]` + 显式长度参数。

### 4.2 告警 C4996（4 处，`strncpy`，全在测试文件）

零告警门破坏点（/W4 级别，CMakeLists 对 MSVC 未加 /WX，故不阻断构建但违门）：

| 文件:行号 | 告警 | 调用点 |
|---|---|---|
| `tests/runtime/test_arraybounds.cpp:68,69` | C4996 `strncpy`→`strncpy_s` | 异常类型/消息拷贝（[READ:test_arraybounds.cpp:68-69]） |
| `tests/runtime/test_regex_api.cpp:110,111` | C4996 `strncpy`→`strncpy_s` | 同模式（[READ:test_regex_api.cpp:110-111]） |

对比上轮基线：`streamio.cpp:284,289`（_wfopen）与 `test_exceptionext.cpp:62,63`（strncpy）已消除——4 处 C4996 全部为**并行代理新文件**引入。修复方向（不修）：两文件 4 处改 `strncpy_s`，或测试侧加缓冲长度实参（`.run.txt` 模式沿用既有 `sizeof(info.type)-1` 上限）。

### 4.3 运行时失败 — `test_math` 2 条 signed-zero 断言（**MSVC 特有，代码生成差异**）

- 断言（[READ:test_math.cpp:245-248]）：net48 契约下 signed-zero 平局**第一操作数胜出**——`Max(-0.0, 0.0) = -0.0`、`Min(0.0, -0.0) = +0.0`。
- runtime 实现（[READ:math.cpp:201-209]）：`icall_math_max_d(a,b){ return (a >= b) ? a : b; }`——C++ 语义正确，GCC/clang 保持三目 + 分支，第一操作数胜出。
- **根因**：MSVC Release `/O2` 把 `(a >= b) ? a : b` 直接折叠为 SSE `maxsd`/`minsd` 指令（[BASH:dumpbin /disasm math.obj → `F2 0F 5F C1 maxsd xmm0,xmm1` / `F2 0F 5D C1 minsd xmm0,xmm1`]）。Intel MAXSD/MINSD 语义：**两者相等（含 signed-zero 平局）时返回第二操作数**——与 net48 第一操作数胜出相反，故两断言反向失败。
- **实证**：
  - 链接真实 `il2cpp_runtime.lib` 的最小探针（/O2 与 /Od 均同结果，因函数体在预编译 lib 内）→ `max_d(-0.0,0.0)` signbit=0（应为 1）、`min_d(0.0,-0.0)` signbit=1（应为 0）[BASH:probe_signedzero2.exe]。
  - 同函数 MinGW clang 17 `/O2` → signbit=1/0 正确 [BASH:probe_gcc.exe]。
- **归因：MSVC 特有（代码生成），非 runtime 逻辑 bug，非在途**。runtime 源码正确，MinGW/WSL 全绿。修复方向（不修）：`math.cpp` 中避免 MSVC 折叠——如把返回语句改为先 `if (a >= b) return a;` 再 `return b;`（可阻止 maxsd 合成），或比较前先 `if (std::signbit(a) != std::signbit(b)) return signbit 为负者`，或该函数加 `#pragma optimize("", off)`；最小改动待领域代理决策。

## 5. 与三编译器矩阵基线对照

| 工具链 | runtime 库 | 测试编译 | 测试运行 | 告警 |
|---|---|---|---|---|
| MSVC（本轮） | ✅ 0 告警 | 87/88（1 编译失败） | 86/87 PASS（1 运行失败） | 4×C4996（测试侧） |
| MinGW clang 17（基线） | ✅ | ✅ | ✅（regexcase 等 e2e 逐字节一致） | 0（-Wall -Wextra） |
| WSL GCC 7.5（基线） | ✅ | ✅ | ✅ | 0 |

MSVC 独有问题合计：**1 编译失败**（test_string_utf8，GNU 扩展）+ **1 运行失败**（test_math，maxsd/minsd signed-zero 代码生成）+ **4 处 C4996**。均为编译器差异，非共享代码逻辑缺陷。

## 6. 结论（三行）

1. **metadata.cpp 修复验证通过**：`il2cpp_ensure_builtin_system_classes` 编译链接成功，runtime 库 0 告警；86/87 测试 PASS，无回归于既有域。
2. **MSVC 特有 2 项新失败**：`test_string_utf8.cpp:83/123/125` GNU 具名变参宏 `bytes...` 与空数组初始化器（MinGW 可编译）；`test_math.cpp:245/248` signed-zero 平局——MSVC /O2 把三目折叠为 SSE `maxsd/minsd`（第二操作数胜出），违反 net48 第一操作数胜出契约。
3. **零告警门缺口 4 处 C4996**（strncpy，test_arraybounds.cpp:68/69、test_regex_api.cpp:110/111），全为并行代理新测试文件；runtime 库本体 0 告警。三问题均需领域代理小改修复，非架构级缺陷。
