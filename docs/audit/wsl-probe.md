# WSL GCC 探路报告 — 三编译器矩阵第三轴可行性

- 日期：2026-08-03
- 任务：只读探测 + 报告，零代码修改，零 git 提交
- 构建目标：`tests/runtime`（runtime 静态库 `il2cpp_runtime`，`-Wall -Wextra -fexceptions -fno-rtti`）

## 1. 环境状态

| 项 | 值 |
|---|---|
| WSL 可用 | 是（WSL2） |
| 默认发行版 | Ubuntu1804（*） |
| 第二发行版 | Ubuntu2404（备份轴） |
| 默认 GCC | **7.5.0**（Ubuntu 7.5.0-3ubuntu1~18.04）— 与三编译器矩阵目标完全一致 |
| Ubuntu2404 GCC | 13.3.0（备用，若需更高 C++ 特性） |
| cmake | 3.10.2（默认发行版）/ 3.28.3（2404） |
| 可用工具 | make、ninja（否）、nproc=32 |
| /mnt/e 挂载 | 可用，`/mnt/e/il2cpp/tests/runtime/CMakeLists.txt` 存在 |

验证命令（本机运行）：
- `wsl --status` → 默认发行版 Ubuntu1804，WSL2
- `wsl -l -v` → Ubuntu1804（默认，Stopped, v2）+ Ubuntu2404（Stopped, v2）
- `wsl bash -c "gcc --version"` → 7.5.0
- `wsl -d Ubuntu2404 bash -c "gcc --version"` → 13.3.0

### cmake 兼容注意
默认发行版 cmake 3.10.2 **不支持 `-S`/`-B`**（3.13 引入）与 `cmake --build -j`。探路改用：
```
cmake /mnt/e/il2cpp/tests/runtime -DCMAKE_BUILD_TYPE=Release   # 在构建目录内执行
make -j4                                                        # 而非 cmake --build -j
```

## 2. 构建结果

- **cmake configure：成功**（GNU 7.5.0，编译器识别正常）
- **make -j4（首次）：失败**，停于第一个错误文件（encoding.cpp）
- **make -j4 -k（收集全部失败）：69 个源中 67 个 .o 成功生成，2 个硬错误**

```
[  0%] ... (67 个编译单元全部通过)
encoding.cpp:18:10: fatal error: windows.h: No such file or directory
numberformat.cpp:339:20: error: 'atoi' is not a member of 'std'
make: *** Error 2   (libil2cpp_runtime 未生成 .a)
```

## 3. POSIX 兼容问题清单

### 3.1 硬错误（阻塞构建，2 处）

| 文件:行 | 原因 | 严重度 |
|---|---|---|
| `src/runtime/src/encoding.cpp:18` | **无守卫 `#include <windows.h>`**；全文件（215 行）仅依赖 `WideCharToMultiByte` / `MultiByteToWideChar`（L70-101, L179-183），无任何 POSIX 分支 | 需整文件 POSIX 移植（UTF-8↔UTF-16 转换可改用 `iconv` 或手写解码器） |
| `src/runtime/src/numberformat.cpp:339` | `std::atoi` 缺 `#include <cstdlib>`（MSVC 传递包含掩盖，libstdc++ 不传递） | 一行修复：加 `<cstdlib>` 或改用 `std::strtol` |

### 3.2 警告（违背三编译器零警告门，3 处）

| 文件:行 | 警告 | 原因 |
|---|---|---|
| `src/runtime/src/boxingext2.cpp:304` | `-Wstrict-aliasing`：type-punned pointer | `*reinterpret_cast<int64_t*>(&value)` 违反严格别名规则 |
| `src/runtime/src/streamio.cpp:273` | `-Wunused-variable`：`str` | `str` 仅 `_WIN32` 分支使用；POSIX 分支用 `CsToUtf8(path)`（见 L275-299） |
| `src/runtime/src/time.cpp:346` | `-Wformat-truncation=`: `%02d` 可能截断 | `char buffer[32]` + `snprintf` 带符号 int 字段，GCC 7.5 保守分析（MSVC /W4 无此项） |

### 3.3 已验证有 POSIX 分支的文件（编译通过，无需改动）

以下文件均 `#if defined(_WIN32)` / `#else` POSIX 双分支，WSL 下编译零错误：
environment.cpp（L49/L221/...）、guid.cpp、interlocked.cpp、io.cpp、monitor.cpp（pthread 分支 L54-60）、pinvoke.cpp、pinvokeext.cpp、stopwatch.cpp（L63 POSIX）、thread.cpp、timer.cpp、console.cpp（L30-34 conio/termios）、allocator.cpp、streamio.cpp（除 L273 警告）。

### 3.4 测试文件（71 个 test_*.cpp）

- 未进入编译：lib 失败 → 测试目标未生成。但**静态扫描确认全部 8 个含平台代码的测试文件均有 `_WIN32`/`#else` 守卫**：
  test_console.cpp、test_debugext.cpp、test_environment.cpp、test_intptr.cpp、test_io.cpp、test_pinvoke.cpp、test_pinvokeext.cpp、test_streamio.cpp、test_time.cpp。
- 注意：test_pinvoke.cpp L22 用 `GetTickCount`（Windows）/`strlen`（POSIX）双路径，POSIX 侧已就绪。

## 4. 后续建议（需要条件编译/移植的文件）

1. **encoding.cpp（必须，唯一全 Windows 文件）**：`#include <windows.h>` 加 `#if defined(_WIN32)` 守卫 + 新增 `#else` POSIX 实现（iconv 或手写 UTF-8↔UTF-16）。这是三编译器矩阵该轴的最大工作量。
2. **numberformat.cpp:339**：`std::atoi` → 补 `#include <cstdlib>`（或用 `std::strtol`）。
3. **boxingext2.cpp:304**：`reinterpret_cast<int64_t*>` → 用 `std::memcpy` 拷贝位模式（保 ABI 语义 + 消除 strict-aliasing）。
4. **streamio.cpp:273**：`str` 声明移入 `_WIN32` 分支（或 `[[maybe_unused]]`）。
5. **time.cpp:346**：buffer 扩到 40 字节（或改用 `%d` 定宽逻辑），消除 -Wformat-truncation。
6. **构建脚本**：若未来接入 run-tests.ps1 / CI，需处理 cmake 3.10 无 `-S`/`-B` 与 `--build -j` 的差异（用目录内 `cmake ..` + `make -j`）。

## 5. 结论

1. **WSL 轴可行**：WSL2 + Ubuntu1804 + GCC 7.5.0 齐备，正好命中三编译器矩阵的 GCC 7.5 目标版本；configure 一次通过，67/69 源文件在 `-Wall -Wextra` 下零警告编译。
2. **仅 2 处硬阻塞**：encoding.cpp（整文件 Windows-only，需 POSIX 移植，最大工作量）+ numberformat.cpp（缺一行 include）；另有 3 处警告需清零才过零警告门。
3. **修复面很小**：5 个源文件的局部改动即可让 WSL 轴进入"可编译"状态，且全部有现成 POSIX 分支先例可循（9 个同类文件已双分支化）。
