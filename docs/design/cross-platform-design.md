# 跨平台抽象设计 — runtime 多平台移植（Windows/Linux/Android NDK/iOS/macOS/OpenBSD）

- 日期：2026-08-03
- 状态：设计文档（只读任务：不修改代码、不 commit）
- 铁律来源：用户铁律——必须实现 IOS/MACOS/LINUX/WINDOWS/ANDROID/OPENBSD 多平台 .NET 接口；先落地 NDK/WINDOWS/LINUX
- 证据基线：docs/audit/wsl-probe.md（WSL GCC 7.5 探路）、docs/audit/msvc-probe.md（MSVC 19.39 探路）+ 本次对 `src/runtime/src/*.cpp`（81 个源文件）的逐文件静态核查

## 0. 结论摘要

1. **81 个 runtime 源文件中 17 个（21%）含 Windows 平台足迹，且 17/17 已有 `#if defined(_WIN32)` / `#else` 双分支**——两个探路报告之间的时段内，唯一的"整文件纯 Windows"文件 encoding.cpp 已被并行修复（`#include <windows.h>` 已守卫 + 手写 UTF-8↔UTF-16 编解码器）。**当前没有"无守卫 Windows 代码"这一级别的硬阻塞。**
2. **剩余硬错误 1 处**：numberformat.cpp:339 `std::atoi` 缺 `#include <cstdlib>`（libstdc++ 7.5 编译错误；MSVC 靠传递包含掩盖）——一行修复。
3. **警告级缺口 4 处（runtime）+ 2 处（tests）**：boxingext2.cpp:304 strict-aliasing、streamio.cpp:273 未用变量、time.cpp:350 format-truncation、streamio.cpp:284/289 C4996 `_wfopen`、test_exceptionext.cpp:62/63 C4996 `strncpy`。
4. **链接级缺口 1 处**：CMakeLists 未 `find_package(Threads)`——monitor.cpp POSIX 分支直接使用 `pthread_mutex_*`，Linux 可执行文件链接时需 `-pthread`（两个探路报告只编译了 .a，从未链接 exe，故未暴露）。
5. **功能语义缺口 2 处（POSIX 分支为占位实现）**：pathproc.cpp `il2cpp_path_get_full_path` POSIX 返回 nullptr（未实现）；`il2cpp_path_get_temp_path` 硬编码 `"/tmp"`（忽略 TMPDIR）。thread.cpp:90-97 非 Linux POSIX 的线程 ID 是 `std::hash<std::thread::id>` 占位（非真实 OS tid）。
6. **转译器发射零平台假设**：entry.cpp/host.cpp 无任何平台调用；`IL2CPP_EXPORT`/`IL2CPP_C_API` 宏与生成 CMakeLists 已双分支。P/Invoke 库名解析（`mylib.dll` vs `libmylib.so`）是唯一接线层待定项。

## 1. 平台缺口全清单（逐文件）

### 1.1 文件级清单（Win 符号 → 现状 → POSIX 对应 → 剩余缺口）

| 文件 | Win 符号（:行号） | 守卫 | POSIX 对应 | 剩余缺口 |
|---|---|---|---|---|
| encoding.cpp | `WideCharToMultiByte`(:241,:244) `MultiByteToWideChar`(:258,:260) `#include <windows.h>`(:23) | 有(:22) | 手写 UTF-8↔UTF-16 编解码器(:62+)——无 iconv 依赖（Android Bionic 无 iconv，文件头 :7-8 已文档化） | **无**（探路后已修复） |
| environment.cpp | `GetEnvironmentVariableA`(:257,:263) `SetEnvironmentVariableA`(:222) `GetCurrentDirectoryA`(:303,:309) `SetCurrentDirectoryA`(:332) `GetTickCount`(:343) `GetSystemInfo`(:357) `GetUserNameA`(:377) `GetModuleHandleA`+`GetProcAddress`+`RtlGetVersion`(:440-447) | 有(:49) | `getenv`(:212,:270) `setenv/unsetenv`(:226) `getcwd/chdir/gethostname`+`uname`(:54-57,:463) | **无**（全双分支；Win 足迹最大的文件，27 处 API） |
| guid.cpp | `LoadLibraryA("ole32.dll")`+`GetProcAddress`(:42-47) `CoCreateGuid`(:153) | 有(:22) | RFC 4122 v4 自实现(:55-82)：SplitMix64-LCG 种子=steady_clock+地址 | 随机源弱（LCG 非 CSPRNG）；Android/iOS/OpenBSD 可升级 `arc4random`（见 §4） |
| interlocked.cpp | `InterlockedCompareExchange`(:71) `InterlockedCompareExchange64`(:121) `InterlockedCompareExchangePointer`(:144) | 有(:24) | GCC 内建/原子实现（12 处 #else） | 无 |
| monitor.cpp | `CRITICAL_SECTION`(:81,:95) `InitializeCriticalSection`(:102,:117) `DeleteCriticalSection`(:103,:126) `EnterCriticalSection`(:135,:162) `LeaveCriticalSection`(:144,:180) `TryEnterCriticalSection`(:171) `GetCurrentThreadId`(:192) | 有(:53) | `pthread_mutex_t`(:83,:108) + 全部 pthread 包装(:119-182) | **链接缺口**：pthread 符号需 `-pthread`（§3.3） |
| threadpool.cpp | `CRITICAL_SECTION`(:67) `CONDITION_VARIABLE`(:68) `SleepConditionVariableCS`(:107) `GetSystemInfo`(:85) `WaitForSingleObject`(:163) `InitializeCriticalSection`(:187) `CreateThread`(:197) `EnterCriticalSection`(:104,:157,:227) `LeaveCriticalSection`(:112,:117,:159,:229) `DeleteCriticalSection`(:166) | 有(:40) | `std::mutex`/`std::condition_variable`/`std::thread`(:71-73,:88) | 同 monitor 的 `-pthread` 链接缺口 |
| timer.cpp | `CreateThread`(:211) `WaitForSingleObject`(:127,:131,:249) `HANDLE`(:54-55) | 有(:32) | `std::thread`/`std::mutex`/`std::condition_variable`(:57-60) | 同 `-pthread` 链接缺口 |
| thread.cpp | `::Sleep`(:61) `::Sleep(0)`(:73) `SwitchToThread`(:108) `GetCurrentThreadId`(:89) | 有(:32) | `sleep_for`(:63) `sched_yield`(:77,:110) `syscall(SYS_gettid)`(:91，仅 `__linux__`) | **非 Linux POSIX 线程 ID 是哈希占位**(:93-96)；Apple 需 `pthread_threadid_np`，OpenBSD 需 `getthrid`（§4-6） |
| stopwatch.cpp | `QueryPerformanceFrequency`(:45) `QueryPerformanceCounter`(:59) | 有(:25) | `std::chrono::steady_clock`(:63+) | 无 |
| streamio.cpp | `_wfopen`(:284,:289) | 有(:275) | `std::fopen`(:294,:297) | `str`(:273) POSIX 未用→-Wunused-variable；`_wfopen` C4996（MSVC /W4） |
| io.cpp | `_mkdir/_rmdir/_stat/_S_IFDIR/_S_IFREG`（宏 :58-59） | 有(:53) | `mkdir(0755)/rmdir/stat/S_ISDIR`(:63-64,:88) | 无 |
| pathproc.cpp | `GetTempPathW`(:86,:92) `GetFullPathNameW`(:111,:118) `GetCurrentProcessId`(:242) | 有(:33) | `"/tmp"` 硬编码(:99)；`nullptr`(:127)；POSIX getpid 分支 | **功能缺口**：`GetFullPathName` 未实现（.NET Path.GetFullPath 语义缺失）；TempPath 忽略 TMPDIR 环境变量 |
| pinvoke.cpp | `LoadLibraryA`(:85) `GetProcAddress`(:124) | 有(:26) | `dlopen(RTLD_LAZY\|RTLD_LOCAL)`(:87) `dlsym`(:126) | **接线缺口**：库名按 DllImport 属性原样传递——POSIX 侧 `"mylib.dll"` 无法解析为 `libmylib.so`（见 §7.2） |
| pinvokeext.cpp | `SetLastError`(:183) | 有(:24) | errno 槽 | 无 |
| marshalext2.cpp | `GetLastError`(:129) `SetLastError`(:138) | 有(:40) | errno(:130,:139) | 无 |
| console.cpp | `_getch`(:142) | 有(:30) | termios（非阻塞轮询） | 无 |
| time.cpp | `_localtime64_s`(:238) `_gmtime64_s`(:238) | 有(:232) | `localtime_r/gmtime_r`(:244) | `buffer[32]`(:350) `%02d` → GCC 7.5 -Wformat-truncation 警告 |

### 1.2 非守卫类缺口（与探路报告比对，当前状态）

| 位置 | 问题 | 严重度 | 来源 |
|---|---|---|---|
| numberformat.cpp:339 | `std::atoi` 缺 `#include <cstdlib>`（include 区 :27-30 只有 cmath/cstdint/cstdio/cstring） | **硬错误（libstdc++）** | wsl-probe §3.1；本次复查仍存在 |
| formatext.cpp:379 | `std::atoi`——已含 `<cstdlib>`(:30) | 无（对照项，已合规） | 本次复查 |
| boxingext2.cpp:304 | `*reinterpret_cast<int64_t*>(&value)` 违反严格别名 | 警告（-Wstrict-aliasing，GCC） | wsl-probe §3.2；本次复查仍存在 |
| streamio.cpp:273 | `str` 仅 _WIN32 分支使用 | 警告（-Wunused-variable，GCC） | wsl-probe §3.2；本次复查仍存在 |
| time.cpp:350 | `snprintf %02d` 进 `char[32]` | 警告（-Wformat-truncation，GCC 7.5 保守分析） | wsl-probe §3.2；本次复查仍存在 |
| streamio.cpp:284,289 | `_wfopen` → C4996（/W4 零告警门破坏） | 警告（MSVC） | msvc-probe §3.2；本次复查仍存在 |
| test_exceptionext.cpp:62,63 | `strncpy` → C4996 | 警告（MSVC，tests 域） | msvc-probe §3.2 |
| test_numberformat.cpp:164,166,169,171 / test_formatext.cpp:275,277 | 缺 `#include <limits>` → C2039/C2065/C2062 | **硬错误（MSVC，tests 域）** | msvc-probe §3.1 |

### 1.3 统计

- 源文件总数：**81**（src/runtime/src/*.cpp）
- 含 Win 平台足迹文件：**17**（21.0%）——全部已双分支守卫
- 含 Windows 头文件：14 个文件 `#include <windows.h>`（全部在 `#if defined(_WIN32)` 内）
- 残留缺口：**1 硬错误 + 4 runtime 警告 + 2 tests 硬错误 + 2 tests 警告 + 1 链接缺口 + 2 功能占位 + 1 线程 ID 占位**
- 两个探路报告的历史缺口中已修复：encoding.cpp（整文件 Windows-only → 双分支 + 手写编解码器）

## 2. 抽象策略：轻量平台层

### 2.1 文件布局（新建，条件编译 `_WIN32 / __linux__ / __ANDROID__ / __APPLE__ / __OpenBSD__`）

```
src/runtime/include/internal/platform.h     — 纯接口头：8 域 API 声明 + 类型（零平台头）
src/runtime/src/platform_win.cpp            — _WIN32 实现（封装现有 Win32 调用）
src/runtime/src/platform_posix.cpp          — 其余平台共享实现（__linux__/__ANDROID__/__APPLE__/__OpenBSD__，
                                              平台差异点内联 #if）
```

**策略原则**：不是"把所有 17 个文件重写"，而是**收敛抽象点**——把 8 类系统调用的分散 `#if` 提取到单一平台层，业务文件只调 `il2cpp_platform_*` 接口。收敛后业务文件内 `#if defined(_WIN32)` 数量应降至接近 0；平台层两个 .cpp 保留全部条件编译。C10 约束：每个文件迁移一个函数一个函数做，每步可独立编译验证。

### 2.2 八域接口设计（对照现状调用点）

| 域 | 现状调用点（文件） | platform.h 接口（提议） | Win 实现 | POSIX 实现 |
|---|---|---|---|---|
| ①线程 | thread.cpp（Sleep/yield/tid）、timer.cpp（CreateThread）、threadpool.cpp（CreateThread） | `thread_id_t plat_thread_current_id()`；`void plat_thread_sleep(ms)`；`void plat_thread_yield()`；`plat_thread_t plat_thread_spawn(fn,arg)`；`void plat_thread_join(plat_thread_t)` | `GetCurrentThreadId`/`Sleep`/`SwitchToThread`/`CreateThread`/`WaitForSingleObject` | `syscall(SYS_gettid)`（linux）/`pthread_threadid_np`（apple）/`getthrid`（openbsd）；`sleep_for`/`sched_yield`/`std::thread` |
| ②同步 | monitor.cpp（CRITICAL_SECTION×8）、threadpool.cpp（CS+CV）、timer.cpp（mutex+cv） | `plat_mutex_t`（init/lock/trylock/unlock/destroy）；`plat_cond_t`（wait/signal/broadcast） | 现有 CS 包装直迁 | `pthread_mutex_t`/`pthread_cond_t` |
| ③高精度时钟 | stopwatch.cpp（QPC/QPF） | `int64_t plat_clock_monotonic_ns()`；`int64_t plat_clock_frequency()` | `QueryPerformanceCounter/Frequency` | `clock_gettime(CLOCK_MONOTONIC)`（macOS 10.12+；低版本 `mach_absolute_time`） |
| ④文件 | streamio.cpp（_wfopen）、pathproc.cpp（GetTempPathW/GetFullPathNameW） | `plat_file_open(utf8path, mode)→FILE*`；`bool plat_path_full(utf8, out)`；`const char* plat_path_tmpdir()` | `_wfopen`/`GetFullPathNameW`/`GetTempPathW` | `fopen`/`realpath`（实现 GetFullPathName 语义）/`getenv("TMPDIR")` 或 `/tmp` |
| ⑤动态库 | pinvoke.cpp（LoadLibraryA/GetProcAddress） | `void* plat_dl_open(name)`；`void* plat_dl_sym(h,name)` | `LoadLibraryA`/`GetProcAddress` | `dlopen`/`dlsym`；**库名映射 `*.dll→*.so` 放平台层做**（见 §7.2） |
| ⑥环境变量 | environment.cpp（Get/SetEnvironmentVariableA） | `std::string plat_env_get(name)`；`bool plat_env_set(name,value,overwrite)` | `GetEnvironmentVariableA`/`SetEnvironmentVariableA`（NULL→删除） | `getenv`/`setenv`/`unsetenv`（保持"空串删除"语义对齐） |
| ⑦目录 | io.cpp（_mkdir/_rmdir/_stat）、pathproc.cpp（TempPath） | `bool plat_dir_exists(p)`；`bool plat_dir_mkdir(p)`；`bool plat_dir_rmdir(p)`；`bool plat_path_is_dir(p)` | `_stat/_mkdir/_rmdir` | `stat/mkdir(0755)/rmdir` |
| ⑧进程 | pathproc.cpp（GetCurrentProcessId）、environment.cpp（GetSystemInfo/uname） | `int32_t plat_process_id()`；`uint32_t plat_cpu_count()`；`std::string plat_os_version()` | `GetCurrentProcessId`/`GetSystemInfo`/`RtlGetVersion` | `getpid`/`std::thread::hardware_concurrency`/`uname` |

### 2.3 迁移顺序（每域独立，保持绿）

每域 = 1 提交单元：`platform.h` 声明 → `platform_win.cpp` + `platform_posix.cpp` 实现 → 业务文件替换为平台调用 → 三编译器（MinGW/MSVC/WSL GCC7.5）零警告编译验证。**同步域（②）最先**——monitor/threadpool/timer 共享 `-pthread` 链接缺口，先修 CMake 再迁移。

## 3. NDK/Android 注意点（Bionic libc）

1. **iconv 缺失**：Bionic 无 iconv——encoding.cpp 已自实现 UTF-8↔UTF-16 编解码器（:62+，文件头明确记录），**零改动**。禁止未来为编码引入 iconv 依赖。
2. **pthread 可用**：Bionic 提供完整 pthread API；NDK 下 pthread 在 libc 内，**无需 `-pthread` 编译旗标**（与桌面 Linux 不同）。monitor.cpp POSIX 分支直接可用。
3. **dladdr 部分功能**：Android 的 dladdr 实现不完整（只能解析运行时 dlopen 的库，静态库符号不可解析）。**本项目 pinvoke 只用 dlopen/dlsym，不用 dladdr**——现状无碍，平台层文档注明禁用。
4. **gettid**：`syscall(SYS_gettid)` 在 Android 可用（thread.cpp:91 的 `__linux__` 分支覆盖 Android）——**无需改动**。
5. **GUID 随机源**：Bionic 提供 `arc4random_buf`（API 21+）——guid.cpp 的 RFC 4122 fallback 可在 `__ANDROID__` 分支升级为 arc4random（当前 LCG 种子熵不足理论风险）。迁移批 2 顺带完成。
6. **时区/版本**：`uname` 可用；`GetUserNameA` 对应 `getenv("USER")` 分支已就绪。
7. **CMake/NDK**：用 NDK 独立工具链（`-DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-24`）编译 il2cpp_runtime 静态库；libc++ 提供 std::thread/std::mutex（threadpool/timer POSIX 分支依赖）。

## 4. iOS/macOS 注意点（`__APPLE__`）

1. **clock_gettime**：macOS 10.12+/iOS 10+ 可用 `clock_gettime(CLOCK_MONOTONIC)`；低版本需 `mach_absolute_time`（平台层可加版本守卫，或直接声明最低支持版本）。
2. **无 CoCreateGuid**：`__APPLE__` 走 RFC 4122 fallback；`arc4random_buf` 在 Apple 全平台可用（libsystem_c），优于当前 LCG——guid.cpp 迁移时升级。
3. **线程 ID**：无 `SYS_gettid`（thread.cpp:90 只认 `__linux__`）——Apple 用 `pthread_threadid_np(NULL, &tid)`，替换当前哈希占位（:93-96）。
4. **pthread/dlopen**：完整可用；`dlopen` 需要额外 flag 注意（`RTLD_FIRST` 等按需）。
5. **无 /proc、无 RtlGetVersion**：OS 版本走 `uname`（已就绪，environment.cpp:463）。
6. **wchar_t**：Apple 平台 `wchar_t` 是 32 位——encoding.cpp 的 `ToWide` reinterpret_cast 技巧（:60-61）在 `__APPLE__` 下**不成立**（该函数已限制在 `_WIN32` 分支内，POSIX 用 char16_t 手写编解码器——安全）。平台层禁止在 POSIX 引入 wchar_t 路径。

## 5. OpenBSD 注意点

1. **POSIX 全齐**：pthread、dlopen/dlsym、clock_gettime、uname、realpath 均可用——monitor/stopwatch/pinvoke/environment 的 POSIX 分支**零改动**。
2. **无 glibc 扩展**：无 `SYS_gettid`（OpenBSD 用 `getthrid(2)`）、无 `setenv` 差异（有 setenv ✓）、`getentropy(2)` 可用作 GUID 随机源（优于 arc4random 也可，arc4random 本身是 OpenBSD 发明）。
3. **iconv**：libc 内置（但项目自实现，不依赖）。
4. **CMake**：无需 `-pthread`（libc 内含 pthread）；`dl` 在 libc 内（CMAKE_DL_LIBS 自动为空，现状 CMakeLists:13 已兼容）。
5. **验证手段**：无本机 OpenBSD——用交叉编译（clang `--target=x86_64-unknown-openbsd`）+ 语法级验证，或 CI 容器。

## 6. 实施批次

### 批 1 — Windows/Linux（先行，0 新平台）
| 文件域 | 内容 | 验证 |
|---|---|---|
| numberformat.cpp | 补 `#include <cstdlib>`（:30） | WSL GCC 7.5 编译（消除唯一硬错误） |
| boxingext2.cpp:304 / streamio.cpp:273 / time.cpp:350 | 严格别名→memcpy；`str` 移入 _WIN32 分支；buffer 扩容 | 三编译器 -Wall/-Wextra//W4 零警告 |
| src/runtime/CMakeLists.txt | 非 MSVC 分支 `find_package(Threads)` + `Threads::Threads` | WSL 链接 tests/runtime 全 exe 成功 |
| tests/runtime/test_numberformat.cpp + test_formatext.cpp | 补 `#include <limits>` | MSVC 70/70 测试全编译 |
| streamio.cpp:284,289 + test_exceptionext.cpp:62,63 | `_wfopen`→`_wfopen_s`；`strncpy`→`strncpy_s` | MSVC /W4 零告警 |
| pathproc.cpp POSIX | GetFullPathName→realpath、TempPath→TMPDIR（功能补齐，平台层先行样板） | tests/runtime test_pathproc 双平台绿 |

### 批 2 — Android NDK（铁律"先落地"第二轴）
| 文件域 | 内容 | 验证 |
|---|---|---|
| platform.h + platform_win.cpp + platform_posix.cpp | 8 域接口全量落地（§2.2）；业务文件逐域迁移（同步域优先） | 三编译器零警告 + NDK arm64-v8a 交叉编译零错误 |
| guid.cpp | `__ANDROID__` 分支 arc4random_buf | NDK 构建 + 单测 |
| CMakeLists | `if(ANDROID)` 分支（无需 -pthread；CMAKE_DL_LIBS 空） | NDK cmake configure + make |
| 新验证链路 | scripts/run-ndk.ps1：NDK standalone → 交叉编译 il2cpp_runtime + tests → 模拟器推送运行（可选） | 至少构建级绿 |

### 批 3 — Apple（iOS/macOS）
| 文件域 | 内容 | 验证 |
|---|---|---|
| platform_posix.cpp `__APPLE__` 分支 | thread id→pthread_threadid_np；GUID→arc4random_buf；clock 守卫 | clang `--target=x86_64-apple-macos` 编译级验证（或 osxcross） |
| thread.cpp/guid.cpp | 同上层改造 | 同上 |
| 说明 | iOS 无主线程 fork 限制等运行时语义由宿主承担，runtime 库本身仅需编译绿 | — |

### 批 4 — OpenBSD
| 文件域 | 内容 | 验证 |
|---|---|---|
| platform_posix.cpp `__OpenBSD__` 分支 | thread id→getthrid(2)；GUID→getentropy(2) | clang `--target=x86_64-unknown-openbsd` 编译级验证 |
| 全量回归 | 批 1-4 后四平台编译矩阵回归 | 每批验证脚本化（scripts/ci-platform.ps1） |

每批验收门（C0/C3）：平台内编译零错误零警告（对齐三编译器零告警门）+ 域单测绿 + 前一批回归绿。批 1 是唯一有本机硬件的批次，批 2-4 以"编译级绿 + 交叉工具链"为验收下限。

## 7. 接线影响（转译器生成代码平台假设检查）

### 7.1 entry.cpp / host.cpp 发射（已核查，零平台假设）

| 发射物 | 内容 | 平台假设 |
|---|---|---|
| entry.cpp（CodeGenerator.cs:2218-2291） | `il2cpp_runtime_init` + `il2cpp_entry_main`：runtime_init + export 注册 + class 注册 + ensure_cctor + Main 调用——纯 C++，仅调 runtime API | **无**（不 include 任何系统头） |
| host.cpp（:2293-2315） | `int main()` + try/catch DotNetException | **无**（无 WinMain/wmain、无 CRT 特判；MSVC/MinGW/GCC 通用 main 签名） |
| IL2CPP_EXPORT / IL2CPP_C_API 宏（il2cpp_runtime.h:22-28、il2cpp_api.h:27-29） | `_WIN32`→`__declspec(dllexport)`，else→`visibility("default")` | **已双分支**，零改动 |
| 生成 CMakeLists（CodeGenerator.cs:2318-2372） | MSVC `/W4` else `-Wall -Wextra` + `-fexceptions`；`add_subdirectory` runtime | **已双分支**；`-fno-rtti` 在 runtime CMakeLists:20 已给 |
| pinvoke 模块接线（:2326-2342） | 按 DllImport 模块名 `add_subdirectory(<mod>)` 进构建 | **唯一待定项**：mylib 是 STATIC（tests/cases/pinvoke/mylib/CMakeLists.txt:3），而运行时用 `LoadLibraryA(dll)`/`dlopen(dll)`（pinvoke.cpp:85,:87）按 DllImport 名解析——POSIX 侧 `.dll` 名无法命中 `libmylib.so`。**平台层 `plat_dl_open` 内做名字归一（dll→so 后缀映射），生成代码零改动** |

### 7.2 结论

转译器发射层（C# 侧）**不需要任何平台分支**——全部平台差异收敛在 runtime 平台层（`plat_dl_open` 库名归一 + 既有双分支宏）。生成代码含 `#include "generated.h"` → 只依赖 internal 头（il2cpp_runtime.h 等），不触平台 API。因此批 1 完成后，同一 gen/ 输出可直接在 Windows/MinGW、Windows/MSVC、WSL GCC 三编译器构建（run-tests.ps1 三编译器矩阵的既设目标），批 2-4 无需转译器改动。

## 8. 风险与待决

1. **-pthread 缺失是当前"编译过但链接挂"的潜在状态**——批 1 第一优先（tests/runtime 的 exe 链接是验证点）。
2. **POSIX 功能占位（Path.GetFullPath=nullptr、TempPath=/tmp 硬编码）是 .NET 语义缺口**——批 1 平台层落地时顺带补齐，需 net48 参考测试对齐（Path.GetFullPath 的大小写/尾斜杠归一语义）。
3. **guid LCG 随机源**在全部非 Win 平台是已知弱点，批 2/3/4 按平台升级（arc4random/getentropy）。
4. **Apple/OpenBSD 无本机硬件**，验收只能到编译级；建议后续 CI 引入对应容器/模拟器。
5. **wchar_t 32 位陷阱**（Apple）——平台层接口一律 UTF-8 字节，禁止 wchar_t 外泄（encoding.cpp ToWide 已正确限在 `_WIN32`）。

## 9. 证据索引（本次核查）

- [READ] src/runtime/src/encoding.cpp:22-24（windows.h 已守卫）、:62+（手写编解码器）
- [READ] src/runtime/src/numberformat.cpp:27-30（include 区缺 <cstdlib>）、:339（std::atoi）
- [READ] src/runtime/src/streamio.cpp:273-299（_wfopen/fopen 双分支 + str 未用）
- [READ] src/runtime/src/boxingext2.cpp:300-305（reinterpret_cast strict-aliasing）
- [READ] src/runtime/src/time.cpp:232-244（_localtime64_s/localtime_r）、:346-353（buffer[32]）
- [READ] src/runtime/src/monitor.cpp:83-195（pthread 双分支）
- [READ] src/runtime/src/threadpool.cpp:40-119（std::mutex/std::thread 双分支）
- [READ] src/runtime/src/thread.cpp:60-97（Sleep/sleep_for + SYS_gettid + 哈希占位）
- [READ] src/runtime/src/pathproc.cpp:90-128（TempPath 硬编码 /tmp、GetFullPath=nullptr）
- [READ] src/runtime/src/guid.cpp:42-82（CoCreateGuid 动态解析 + RFC4122 LCG）
- [READ] src/runtime/src/stopwatch.cpp:25-69（QPC/steady_clock）
- [READ] src/runtime/src/pinvoke.cpp:84-87（LoadLibraryA/dlopen）
- [READ] src/runtime/src/io.cpp:53-65（_mkdir/mkdir 宏双分支）
- [READ] src/il2cppc/Emit/CodeGenerator.cs:2218-2316（entry.cpp/host.cpp 发射）、:2318-2372（生成 CMakeLists）
- [READ] src/runtime/include/internal/il2cpp_runtime.h:22-28（IL2CPP_EXPORT 双分支）
- [READ] src/runtime/CMakeLists.txt:11-21（CMAKE_DL_LIBS 无 Threads）
- [READ] docs/audit/wsl-probe.md、docs/audit/msvc-probe.md
- [BASH] Select-String 全量统计：81 源文件 / 17 文件含 Win 足迹 / 全部有 _WIN32 守卫
