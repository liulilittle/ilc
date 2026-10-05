# WSL GCC 7.5 全量构建验证 — 三编译器矩阵批1

- 日期：2026-08-04
- 任务：只读 + 运行 + 写文档；不修代码；不 commit
- 环境：WSL2 Ubuntu1804（默认发行版），GCC **7.5.0**（Ubuntu 7.5.0-3ubuntu1~18.04），cmake 3.10.2
- 范围：批1 跨平台（encoding POSIX 移植 + 警告清零）在 GCC 7.5 轴的全量验证

## 1. 环境确认

| 项 | 值 |
|---|---|
| WSL | 可用（WSL2） |
| 默认发行版 | Ubuntu1804（GCC 7.5.0） |
| 备用发行版 | Ubuntu2404（GCC 13.3.0） |
| cmake | 3.10.2（**不支持 `-S/-B`**，用目录内 `cmake /mnt/e/il2cpp/tests/runtime` + `make -j4`） |
| 源文件 | 87 个 `src/*.cpp` |

验证命令（本机运行）：
- `wsl -l -v` → Ubuntu1804（默认,Stopped,v2）+ Ubuntu2404（Stopped,v2）
- `wsl -d Ubuntu1804 bash -c "gcc --version"` → 7.5.0

## 2. 构建状态

```
cmake configure: 成功（GCC 7.5.0 识别正常；find_package(Threads) → pthread 找到）
make -k -j4:     失败（Error 2）—— 86/87 编译单元成功，1 个硬错误文件
libil2cpp_runtime.a: 未生成（pathproc.cpp 阻塞）
test_*.exe:      未生成（lib 未产出，链接阶段未到达）
```

### 错误清单（文件:行号）

| 文件:行 | 错误 | 归因 |
|---|---|---|
| `src/runtime/src/pathproc.cpp:151` | `'IsSegmentTerminator' was not declared in this scope` | **POSIX 缺口（8a9a3a9 遗留，批1 未碰）** |
| `src/runtime/src/pathproc.cpp:264` | `'::exit' has not been declared` | 同（缺 `#include <cstdlib>`） |

### 归因

`IsSegmentTerminator(char16_t)` 定义于 `pathproc.cpp:69-72`，**位于 `#if defined(_WIN32)` 块内**（L43 `#if` / L74 `#endif`），但共享函数 `il2cpp_path_change_extension`（L131，无平台守卫）在 L151 无条件调用它 → **POSIX 编译必然失败**。

`::exit(code)`（L264）位于 POSIX 分支，当前 include 列表（L27-36：cstdint/cstring/vector + pthread.h/unistd.h）无 `<cstdlib>` → `::exit` 未声明（MSVC 传递包含掩盖，libstdc++ 不传递——与 wsl-probe 记录的 numberformat.cpp:339 `std::atoi` 同根因）。

**git 佐证**：`git status --short src/runtime/src/pathproc.cpp` 为空 + `git log` 最后改动为 `8a9a3a9`（08-03）→ 批1 未触及此文件，该编译错误为 8a9a3a9 引入的遗留 POSIX 缺口。

**影响**：这是当前 WSL 轴**唯一**硬阻塞；修复后批1 产物（encoding/numberformat/console/formatext 等）预计全绿（86 个编译单元已在 -Wall -Wextra 下零错误零警告通过）。

## 3. 测试表

未生成（构建失败 → lib 缺失 → 无 exe 可跑）。测试结果待 pathproc.cpp 修复后补测。

## 4. 警告数（-Wall -Wextra 质量门）

| 统计 | 值 |
|---|---|
| 87 编译单元中警告文件 | **1 个**（pathproc.cpp） |
| 警告位置 | `pathproc.cpp:266` — `warning: 'noreturn' function does return` |
| 其余 86 个文件 | **零警告**（含批1 全部目标文件） |

警告说明：pathproc.cpp:259-266 `il2cpp_process_exit` 声明为 `[[noreturn]]`（头文件），但 POSIX 分支因 `::exit` 未声明导致函数体"落空返回" → GCC 报 noreturn 违反。此为 `::exit` 错误的下游警告，`<cstdlib>` 修复后自动消失。

**零警告门：86/87 满足，唯一超门文件 = pathproc.cpp（阻塞修复项）**。

## 5. pthread 链接缺口确认（施工单）

### 现状（静态确认 + configure 佐证）

- `tests/runtime/CMakeLists.txt:21` — `find_package(Threads REQUIRED)` 已存在（configure 日志：`Found Threads: TRUE`，`pthread_create` 在 `libpthread` 中探到）
- `tests/runtime/CMakeLists.txt:32` — `target_link_libraries(${TEST_NAME} PRIVATE il2cpp_runtime)` 只链 runtime + `:36` `${CMAKE_DL_LIBS}`
- `src/runtime/CMakeLists.txt:13` — `target_link_libraries(il2cpp_runtime PUBLIC ${CMAKE_DL_LIBS})` 只 PUBLIC dl
- **两处均未把 `Threads::Threads` 链给任何目标** → `find_package(Threads)` 的结果从未被使用

### 为什么会链接失败

`src/runtime/src/monitor.cpp` POSIX 分支直接调用 `pthread_mutex_*`（wsl-probe 记录 L54-60），`threadpool.cpp`/`timer.cpp` 用 `std::thread/std::mutex/std::condition_variable`（跨平台设计文档 §3.3 记录）。Linux 下这些符号在 `libpthread`，GCC **不会**自动链接（glibc ≥2.34 起 pthread 并入 libc，但 Ubuntu 18.04/glibc 2.27 **仍需 `-pthread`**）。→ 当 lib 修复后进入 exe 链接阶段，将出现 `undefined reference to pthread_mutex_* / pthread_create / pthread_cond_*`。

**本次验证未能实测该链接错误**——构建在 lib 编译阶段（pathproc.cpp）即失败，链接阶段未到达。该缺口由代码静态扫描 + configure `Found Threads` 但未使用证实。

### CMakeLists 修复施工单

```cmake
# src/runtime/CMakeLists.txt — il2cpp_runtime 目标
if(NOT MSVC)
    target_link_libraries(il2cpp_runtime PUBLIC ${CMAKE_DL_LIBS} Threads::Threads)
endif()

# tests/runtime/CMakeLists.txt — 每个 test 目标
target_link_libraries(${TEST_NAME} PRIVATE il2cpp_runtime Threads::Threads)
```

说明：
1. `Threads::Threads` 是 `find_package(Threads REQUIRED)`（tests/runtime/CMakeLists.txt:21）产出的 IMPORTED target；GCC 下解析为 `-pthread`（含 `-D_REENTRANT`），MinGW 下解析为空 → **三编译器零破坏**。
2. 放 PUBLIC 到 `il2cpp_runtime`（src/runtime）可传播给生成代码与全部 test；或仅在 tests 侧逐个链接。**推荐 PUBLIC**（一处修复，全部消费方覆盖）。
3. 同时修复 pathproc.cpp（非 CMake，见 §2）后即可打通 WSL 轴：`cmake /mnt/e/il2cpp/tests/runtime` + `make -j4`。

## 6. 结论

1. **WSL GCC 7.5 轴当前被 pathproc.cpp 遗留 POSIX 缺口阻塞**（2 err + 1 下游 warning），批1 产物自身 86/87 编译单元 -Wall -Wextra 零警告零错误。
2. **pthread 链接缺口确认**：`find_package(Threads)` 已加但 `Threads::Threads` 从未链接；pathproc 修复后 exe 链接阶段将实测暴露 `undefined reference to pthread_*`——需按 §5 施工单接线。
3. 测试表为空（lib 未产出），待 pathproc.cpp + CMake pthread 修复后补全量 test_*.exe 运行验证。

## 附：验证证据

- [BASH] `wsl -l -v` → Ubuntu1804（默认,Stopped,v2）+ Ubuntu2404（Stopped,v2）
- [BASH] `wsl -d Ubuntu1804 bash -c "gcc --version | head -1"` → 7.5.0
- [BASH] cmake configure → `Found Threads: TRUE`、`Build files written to /tmp/il2cpp_wsl2`
- [BASH] `make -k -j4` → Error 2；full.log 87 编译单元，2 err + 1 warning 全在 pathproc.cpp；86/87 通过
- [BASH] `ls src/*.cpp | wc -l` → 87；`.o` → 86；`libil2cpp_runtime.a` 不存在；`test_*.exe` → 0
- [READ] `src/runtime/src/pathproc.cpp:43-74`（IsSegmentTerminator 在 _WIN32 块内）、`:131,151`（共享函数调用）、`:27-36`（无 cstdlib）、`:264`
- [READ] `tests/runtime/CMakeLists.txt:21,32,36`；[READ] `src/runtime/CMakeLists.txt:13`
- [BASH] `git status --short src/runtime/src/pathproc.cpp` 为空 + `git log` → 8a9a3a9（批1 未碰）
