# MSVC 探路报告 — 三编译器矩阵（MinGW/MSVC/WSL GCC7.5）可行性探测

> 只读探测，仓库零改动。构建产物在 `E:\il2cpp\build\runtime_msvc\`（全新 configure，非复用旧目录）。
> 完整构建日志：`E:\il2cpp\build\runtime_msvc\build_log.txt`。

## 1. 工具链版本

| 项目 | 值 | 来源 |
|---|---|---|
| 本机 MSVC 实例 | VS2022 Enterprise **17.9.7**（`C:\Program Files\Microsoft Visual Studio\2022\Enterprise`） | [BASH:vswhere -latest] |
| 另一实例 | VS2026 BuildTools **18.7.3**（`C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools`，isComplete=true） | [BASH:vswhere -all] |
| cl.exe | **MSVC 19.39.33523.0**（toolset `14.39.33519`，Hostx64/x64） | [BASH:cmake configure] |
| Windows SDK | 10.0.22621.0 | [BASH:cmake configure] |
| cmake | 3.22.1（来自 Android SDK，非独立安装） | [BASH:cmake --version] |
| 生成器 | `Visual Studio 17 2022` + `-A x64`（自动选中 VS2022 实例与 14.39 toolset） | [BASH:cmake configure] |

- `cl` 不在 PATH，但 vswhere 可定位实例，cmake VS 生成器自动选工具链，无需 vcvars64 手动设置。
- CMakeLists 已含 MSVC 分支：`/W4 /EHsc /bigobj /utf-8`（tests/runtime/CMakeLists.txt:35、src/runtime/CMakeLists.txt:17）——配置正确，是质量门的现成承载点。

## 2. 构建结果

| 项 | 结果 |
|---|---|
| runtime 静态库 `il2cpp_runtime.lib` | **构建成功** → `build/runtime_msvc/runtime/Release/il2cpp_runtime.lib` |
| 测试可执行文件 | **68/70 构建成功**（`build/runtime_msvc/Release/*.exe`） |
| 编译失败 | **2/70**：`test_numberformat`、`test_formatext`（同一根因，见 §3） |
| 运行时验证 | 抽样 6 个测试全过：test_string/conv/gc/exception/array/boxingext 均 `exit=0` |
| MSVC 告警 | **4 处 C4996**（/W4 级别，破坏零告警门） |
| 本次探测期间并行代理新增 | `test_linq{,_map,_sort}.cpp`(19:33-19:35)、`test_arraybounds.cpp`(19:31)、`test_string_utf8.cpp`(19:32)——构建在 19:35 前完成，未包含这 5 个新目标，**不在本报告失败清单内** |

结论：**MSVC 工具链可用，runtime 主体可编译可运行**；阻塞零告警门的是 2 类共 6 处问题。

## 3. MSVC 兼容问题清单（全部为 MSVC 独有）

### 3.1 编译错误 C2039/C2065/C2062 — 缺 `#include <limits>`

MinGW(clang 17)/GCC 经 `<cstring>` 等头文件**传递包含**引入 `<limits>`；MSVC 标准库不这样做，`std::numeric_limits` 未声明。

| 文件:行号 | 错误 | 原因 |
|---|---|---|
| `tests/runtime/test_numberformat.cpp:164,166,169,171` | C2039 `"numeric_limits"` 非 `std` 成员 / C2065 未定义标识符 / C2062 类型 unexpected | include 区仅 cmath/cstdint/cstdio/cstring（:31-34），缺 `<limits>` |
| `tests/runtime/test_formatext.cpp:275,277` | 同上（`std::numeric_limits<double>::infinity()`） | 缺 `<limits>` |

修复方向：两文件补 `#include <limits>`（单行，MinGW 不受影响）。

### 3.2 告警 C4996（/W4 零告警门破坏点）

| 文件:行号 | 告警 | MSVC 建议 |
|---|---|---|
| `src/runtime/src/streamio.cpp:284,289` | C4996 `_wfopen` 不安全，建议 `_wfopen_s` | 改 `_wfopen_s`，或 MSVC 编译选项加 `_CRT_SECURE_NO_WARNINGS` |
| `tests/runtime/test_exceptionext.cpp:62,63` | C4996 `strncpy` 不安全，建议 `strncpy_s` | 改 `strncpy_s`（需算缓冲/终止符语义），或同上宏 |

注意：`_CRT_SECURE_NO_WARNINGS` 是宏，若走条件编译需保证 `#define` 先于任何 CRT 头；**逐处替换 API 更符合现有"零告警门"精神**（CMakeLists 已对非 MSVC 用 `-Wall -Wextra`，对 MSVC 用 `/W4`，两类都吃告警即失败）。

## 4. 后续建议

1. **补 include（必修）**：`test_numberformat.cpp`、`test_formatext.cpp` 加 `#include <limits>`。→ 使 MSVC 测试集 70/70 全编译。
2. **消 C4996（必修，零告警门）**：`streamio.cpp` 的 `_wfopen`→`_wfopen_s`；`test_exceptionext.cpp` 的 `strncpy`→`strncpy_s`。优先逐处替换而非全局宏抑制。
3. **统一三编译器告警旗标（可做）**：MSVC 分支现有 `/W4` 已对齐 MinGW `-Wall -Wextra`；如需更严，MSVC 加 `/permissive-`（标准一致性）与 `/Zc:__cplusplus`（正确报告 C++17 宏，供生成代码 `#if __cplusplus` 判断使用）。
4. **工具链选择固定化（可做）**：本机有 VS2022+VS2026 双实例，cmake 默认选最新可用 toolset；CI 中建议在生成器后固定 `-T v143`（VS2022 toolset 14.39）保证可复现。
5. **并行代理在途提示**：`test_linq*`/`test_arraybounds`/`test_string_utf8` 是并行代理新产物（探测期间写入），首次纳入 MSVC 构建前需重跑 cmake configure（CMakeLists 用 `CONFIGURE_DEPENDS` GLOB，自动纳入）。

## 5. 结论（三行）

1. **MSVC 可用**：VS2022 Enterprise 17.9.7 + MSVC 19.39 + WinSDK 10.0.22621，cmake `Visual Studio 17 2022 -A x64` 免 vcvars 直接构建；runtime 库 + 68/70 测试编译成功、抽样运行全过。
2. **失败仅 2 文件同根因**：`test_numberformat.cpp:164/166/169/171` 与 `test_formatext.cpp:275/277` 缺 `#include <limits>`（MSVC 无传递包含，MinGW/GCC 有）——每文件补一行即可全绿。
3. **零告警门缺口 4 处 C4996**：`streamio.cpp:284/289`(_wfopen)、`test_exceptionext.cpp:62/63`(strncpy)——建议逐处替换为 `_wfopen_s`/`strncpy_s`，勿用全局宏抑制。
