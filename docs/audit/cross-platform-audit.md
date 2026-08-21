# Runtime 跨平台完备性审计（只读）

- 日期：2026-08-06
- 分支：m1
- 范围：`src/runtime/`（src/*.cpp 全部 + include/internal/*.h 平台相关 + CMakeLists.txt）
- 方式：逐文件读取 + 全量平台宏扫描 + 对照 Boost 模式（websearch 核实）与本地 coreclr PAL
- 基线文档：docs/design/cross-platform-design.md（2026-08-03，平台层设计 + 批次计划）

## 0. 结论摘要

1. **平台层（platform.h 31 API）已完整落地**：`platform_win.cpp`（_WIN32 全实现）+ `platform_posix.cpp`（__linux__/__ANDROID__/__APPLE__/__OpenBSD__/通用 POSIX 兜底），8 域全覆盖，UTF-8 边界契约、异常中性、.dll→.so 名字映射均已实现。**平台层本身零硬缺口。**
2. **核心问题 = 采用度不足**：25 个业务文件仍各自 `#if defined(_WIN32)` 双分支，**只有 threadobj.cpp 一个文件接入了平台层**（thread 域）。设计文档 §2.3 的"业务文件逐域迁移"批次未执行——8 个域中 7 个仍在业务文件内自持平台分支（功能正确，但抽象目标未达成，且产生 3 处重复的线程 ID 实现、2 处重复的路径/临时目录实现）。
3. **P1 缺口 2 处**：
   - pinvoke.cpp 裸 `dlopen`，DllImport("xxx.dll") 在 POSIX 上无法解析（设计文档 §7.2 标注的"唯一接线待定项"**仍然未解决**——`plat_dl_open` 的名字映射是死代码）；
   - rng.cpp `#if defined(__linux__)` 在 Android 上也成立（Bionic 定义 __linux__），但 `getrandom`/`<sys/random.h>` 仅 __ANDROID_API__ >= 28 可见，设计基线 android-24 存在**编译失败风险**。
4. **P2 缺口 4 处**：plat_dl_open 缺 Apple .dylib 候选；environment.cpp get_special_folder 在 POSIX 用 `\` 分隔符；线程 ID 三处重复实现（Apple/OpenBSD 落到哈希/截断占位）；io.cpp Windows 非 ASCII 路径经 ANSI 码页（绕开 plat_file_open 的宽字符转换）。
5. **P3 若干**：Android tmpdir 落 `/tmp`（不存在）；os_version 两个表面不一致；GetSystemInfo >64 核分组限制；guid LCG 随机源（设计文档已知）；POSIX CommandLine/Process 为文档化 stub。
6. 全部 27 个含平台分支的文件**均有 _WIN32 守卫**，无"无守卫 Windows 代码"；`numberformat.cpp` 缺 `<cstdlib>` 的硬错误已修复（现已有，见证据索引）。

## 1. 现状盘点

### 1.1 平台分支使用统计（27 个文件：25 cpp + 2 头）

| 文件 | 分支点 | Win 侧符号 | POSIX 侧 |
|---|---|---|---|
| platform_win.cpp | 4 | 31 API 全实现 | （空 TU） |
| platform_posix.cpp | 9 | （空 TU） | linux/android/apple/openbsd 内联 #if |
| environment.cpp | 27 | Get/SetEnvironmentVariableA、GetCurrentDirectoryA、GetTickCount、GetSystemInfo、GetUserNameA、GetComputerNameA、RtlGetVersion、GetCommandLineW | getenv/setenv/unsetenv、getcwd/chdir、gethostname、uname、steady_clock |
| pathproc.cpp | 27 | GetTempPathW、GetFullPathNameW、CreateProcessW 全家 | TMPDIR//tmp、realpath+词法归一、getpid/syscall |
| interlocked.cpp | 25 | Interlocked\* x13 | GCC/Clang __atomic 内建 |
| monitor.cpp | 24 | CRITICAL_SECTION、GetCurrentThreadId | pthread_mutex、SYS_gettid、哈希兜底 |
| threadpool.cpp | 18 | CS+CV+CreateThread+GetSystemInfo | std::mutex/cv/thread |
| socket.cpp | 25 | winsock2（WSAStartup 惰性、#pragma comment lib） | BSD socket + MSG_NOSIGNAL/SO_NOSIGPIPE 特征测试 |
| timer.cpp | 12 | CreateEventW/CreateThread/WaitForSingleObject | std::thread/mutex/cv |
| thread.cpp | 12 | Sleep/SwitchToThread/GetCurrentThreadId | sleep_for/sched_yield/SYS_gettid/哈希兜底 |
| io.cpp | 8 | _mkdir/_rmdir/_stat/_S_IFDIR/_S_IFREG | mkdir(0755)/rmdir/stat/S_ISDIR |
| rng.cpp | 8 | BCryptGenRandom→RtlGenRandom 运行时解析 | getrandom(Linux)→/dev/urandom |
| streamio.cpp | 8 | _wfopen/_fseeki64/_ftelli64 | fopen/fseeko/ftello |
| console.cpp | 7 | _setmode、_getch | termios 原始模式 |
| encoding.cpp | 7 | WideCharToMultiByte/MultiByteToWideChar | 手写 UTF-8↔UTF-16 编解码器（无 iconv） |
| ipaddr.cpp | 6 | winsock2 | getaddrinfo |
| marshalext2.cpp | 5 | Get/SetLastError | errno |
| pinvoke.cpp | 8 | LoadLibraryA/GetProcAddress | **裸 dlopen/dlsym（无名字映射）** |
| pinvokeext.cpp | 3 | SetLastError | errno |
| guid.cpp | 3 | CoCreateGuid 运行时解析 | RFC4122 v4 LCG |
| decimal.cpp | 3 | _BitScanReverse（_MSC_VER） | __builtin_clz |
| stopwatch.cpp | 3 | QueryPerformanceCounter/Frequency | steady_clock（1e9） |
| time.cpp | 2 | _localtime64_s/_gmtime64_s | localtime_r/gmtime_r |
| timezone.cpp | 7 | GetTimeZoneInformation | localtime_r/gmtime_r |
| allocator.cpp | 1 | —（纯 stdlib） | — |
| il2cpp_runtime.h / il2cpp_api.h | 各 2 | __declspec(dllexport/noinline) | visibility("default")/noinline |

### 1.2 平台宏覆盖矩阵

| 宏 | 出现的文件 | 说明 |
|---|---|---|
| `_WIN32` | 全部 27 | 主分割，27/27 双分支完整 |
| `__linux__` | thread/monitor/pathproc/rng/platform_posix | 线程 ID、getrandom、syscall |
| `__ANDROID__` | 仅 platform_posix.cpp（冗余于 __linux__） | 无 Bionic 特有分支（iconv 已规避、dlopen 可用） |
| `__APPLE__` | 仅 platform_posix.cpp | pthread_threadid_np、clock_gettime 基线 10.12+ |
| `__OpenBSD__` | 仅 platform_posix.cpp | getthrid(2) |
| `_MSC_VER` | platform_win/streamio/decimal/environment | _wfopen_s、intrin、CRT 安全变体 |
| 特征测试 | socket.cpp（MSG_NOSIGNAL/SO_NOSIGPIPE）、encoding.cpp（无 iconv 自实现） | 少量，方向正确 |

Android 覆盖率说明：Bionic 同时定义 `__linux__`，故 thread/monitor/pathproc 的 SYS_gettid 分支在 Android 可用（编译与运行均正确）；**唯一例外是 rng.cpp 的 getrandom（见缺口 G2）**。

### 1.3 platform.h 31 API 状态（8 域）

| 域 | API 数 | win 实现 | posix 实现 | 平台差异点 | 状态 |
|---|---|---|---|---|---|
| ①线程 | 5 | GetCurrentThreadId/Sleep/SwitchToThread/CreateThread/WaitForSingleObject | syscall(SYS_gettid)/pthread_threadid_np/getthrid/哈希兜底 + std::thread | Apple/OpenBSD 已专门处理 | **完整** |
| ②同步 | 10 | CS/ConditionVariable | pthread_mutex/pthread_cond | 无 | **完整** |
| ③时钟 | 2 | QPC/QPF | clock_gettime(CLOCK_MONOTONIC) | macOS 10.12+ 基线已文档化 | **完整** |
| ④文件 | 3 | _wfopen 宽转换/GetFullPathNameW/GetTempPathW | fopen/realpath/TMPDIR→/tmp | realpath 要求存在（已文档化） | **完整**（Android tmpdir 见 G7） |
| ⑤动态库 | 2 | LoadLibraryA/GetProcAddress | dlopen/dlsym + .dll→.so 映射 | **Apple .dylib 缺失（G3）**；pinvoke 未接入（G1） | 部分 |
| ⑥环境变量 | 2 | Get/SetEnvironmentVariableA | getenv/setenv/unsetenv（空串删除对齐） | 无 | **完整** |
| ⑦目录 | 4 | _stat/_mkdir/_rmdir | stat/mkdir(0755)/rmdir | 无 | **完整** |
| ⑧进程 | 3 | GetCurrentProcessId/GetSystemInfo/RtlGetVersion | getpid/hardware_concurrency/uname | 无 | **完整** |

## 2. Boost 模式对照

### 2.1 Boost 的做法（websearch 核实 boost/config + Boost.Predef）

1. **归一化检测宏**：Boost.Predef 把编译器预定义宏归一为 `BOOST_OS_*` 版本号宏——`BOOST_OS_LINUX` ← `__linux__`、`BOOST_OS_MACOS`/`BOOST_OS_IOS` ← `__APPLE__`、`BOOST_PLAT_ANDROID` ← `__ANDROID__`、`BOOST_OS_BSD_OPEN` 等 ← `__OpenBSD__/__FreeBSD__/__NetBSD__/__DragonFly__`、`BOOST_OS_WINDOWS` ← `_WIN32`（含 `_WIN64` 等变体）。**业务代码只测试 BOOST_OS_\*，从不直接碰编译器预定义宏。**
2. **单点平台选择**：`boost/config/detail/select_platform_config.hpp` 用一串 `#elif defined(__FreeBSD__) || defined(__NetBSD__) || defined(__OpenBSD__) ...` 选 `boost/config/platform/{linux,bsd,macos,win32}.hpp` 之一——平台族检测集中在一个头。
3. **能力宏而非平台宏**：平台配置头定义的是**能力宏**（`BOOST_HAS_UNISTD_H`、`BOOST_HAS_PTHREADS`、`BOOST_HAS_SCHED_YIELD`、`BOOST_HAS_GETTIMEOFDAY`、`BOOST_HAS_SIGACTION`……），实现代码按**能力**分支（"有这个函数才用它"），而不是按平台名分支。新平台接入 = 新增一个 platform 配置头 + 定义能力宏，业务代码零改动。

### 2.2 CoreCLR PAL 的做法（本地 reference/coreclr/src/coreclr/pal）

- **统一 PAL API 面**（PAL_\* 函数）向 runtime 上层提供，内部按域一个 TU（file/file.cpp、misc/environ.cpp、thread/thread.cpp……），域内 `#if defined(__APPLE__)`/`#ifdef TARGET_WASM`/`HAVE_*` 内联分支。
- **每 OS quirk 就地处理**：如 file.cpp:588-599 OpenBSD `O_TRUNC` 需 O_RDWR 否则 EINVAL 的特例；HAVE_* 是 configure 生成的特性宏（autoconf 风格）。
- 与本项目 `platform_win.cpp`/`platform_posix.cpp` + 内联 #if 的形状**高度一致**。

### 2.3 本项目对照结论

| 维度 | Boost | CoreCLR PAL | 本项目 | 差距 |
|---|---|---|---|---|
| 平台检测 | BOOST_OS_\* 归一化宏 | TARGET_\* 宏 | 原始预定义宏散落 25 文件 | 缺归一化头（建议 `runtime_platform.h` 或 platform.h 顶部集中 `#if defined(__APPLE__)||...` 定义 IL2CPP_OS_*） |
| 能力宏 | BOOST_HAS_\* | HAVE_\*（configure 生成） | 仅 2 处特征测试（MSG_NOSIGNAL/SO_NOSIGPIPE） | 缺（如"clock_gettime 是否可用""getrandom 是否可用"应能力化） |
| 平台层 API | — | PAL_\* 统一面 | plat_\* 31 API ✓ | **采用度：仅 threadobj.cpp 接入，7/8 域未迁移** |
| 实现文件 | per-platform config 头 | 每域一个 TU 内联 #if | platform_win/posix 双 TU ✓ | 无 |

**结论**：平台层形状（= PAL 模式）是对的；缺的是 (a) 检测宏归一化层（Boost 第一步）、(b) 能力宏、(c) 设计文档承诺的业务文件逐域迁移（把 25 个文件的散落 #if 收敛到 plat_\*）。

## 3. 缺口清单（按域 × 平台）

### 3.1 动态库域

| # | 缺口 | 位置 | 影响平台 | 严重度 |
|---|---|---|---|---|
| G1 | pinvoke.cpp `GetLibraryHandle` 裸 `dlopen(dll)`，DllImport 原名（"mylib.dll"）直接传给 dlopen——POSIX 上必然失败；`plat_dl_open` 的 .dll→lib\<base\>.so 映射是死代码（设计文档 §7.2 的"唯一接线待定项"仍开着） | src/runtime/src/pinvoke.cpp:75-98 | LINUX/MACOS/ANDROID/OPENBSD | **P1** |
| G3 | `plat_dl_open` 映射只试 `lib\<base\>.so`/`\<base\>.so`，缺 Apple 的 `.dylib` 候选 | src/runtime/src/platform_posix.cpp:281-305 | MACOS | P2 |

### 3.2 随机域

| # | 缺口 | 位置 | 影响平台 | 严重度 |
|---|---|---|---|---|
| G2 | `#if defined(__linux__)` 同时命中 Android（Bionic 定义 __linux__）；`<sys/random.h>` 的 getrandom 声明带 `__INTRODUCED_IN(28)`，设计基线 android-24 下**编译期不可见** → NDK 构建失败风险 | src/runtime/src/rng.cpp:36-38, 176-199 | ANDROID（NDK < API 28） | **P1/P2** |
| G10 | guid.cpp POSIX 随机源为 SplitMix64 LCG（非 CSPRNG）；设计文档已计划 `__ANDROID__`→arc4random_buf、`__OpenBSD__`→getentropy，未实施 | src/runtime/src/guid.cpp:62-84 | 全部 POSIX | P3（已知） |

### 3.3 线程域

| # | 缺口 | 位置 | 影响平台 | 严重度 |
|---|---|---|---|---|
| G5 | 线程 ID 三处重复实现，均只认 `__linux__` + 兜底：thread.cpp:90-97（哈希）、monitor.cpp:193-198（哈希）、pathproc.cpp:672-681（pthread_self 截断，Apple/OpenBSD 上可能为 0/负）——Apple 应用 pthread_threadid_np、OpenBSD 应用 getthrid（平台层已正确实现，应调用 `plat_thread_current_id`） | thread.cpp / monitor.cpp / pathproc.cpp | MACOS/OPENBSD（语义）；Android 因 __linux__ 可用但隐式 | P2 |

### 3.4 路径/文件域

| # | 缺口 | 位置 | 影响平台 | 严重度 |
|---|---|---|---|---|
| G6 | io.cpp 文件打开/删除用 `std::ifstream/ofstream/remove`（窄字符路径）：MSVC 下走 ANSI 码页，**非 ASCII 路径（UTF-8）损坏**；`plat_file_open`（_wfopen 宽转换）已存在但未使用 | src/runtime/src/io.cpp:379,390,410,426,445,476（icall_file_\* 全部） | WINDOWS（MSVC/MinGW） | P2 |
| G7 | `plat_path_tmpdir` POSIX 兜底 `/tmp`：Android 无 /tmp（应 /data/local/tmp 或应用 cache 目录） | src/runtime/src/platform_posix.cpp:263-276 | ANDROID | P3 |
| G14 | `icall_file_write_all_lines` 无条件写 CRLF（net48 Windows 语义）；.NET Core Unix 用 `\n`——跨平台语义选择需文档化/参数化 | src/runtime/src/io.cpp:464 | 全部 POSIX | P3（语义） |

### 3.5 环境/进程域

| # | 缺口 | 位置 | 影响平台 | 严重度 |
|---|---|---|---|---|
| G4 | `get_special_folder` 的 folder 0/5 用 `home + "\\Desktop"`/`"\\Documents"`——**反斜杠在全部平台生效**，POSIX 产出 `~/Desktop` 形态的错误路径（仅 folder 28 有 POSIX 分支） | src/runtime/src/environment.cpp:474-492 | LINUX/MACOS/ANDROID/OPENBSD | P2 |
| G8 | `icall_environment_get_os_version` POSIX 返回 "Unix \<release\>"（缺 sysname），与平台层 `plat_os_version`（"\<sysname\> \<release\>"）不一致 | src/runtime/src/environment.cpp:462-471 | 全部 POSIX | P3 |
| G9 | Windows `plat_cpu_count`/threadpool `worker_count` 用 GetSystemInfo：>64 逻辑核时受处理器组限制，建议 GetActiveProcessorCount | platform_win.cpp:426-431 / threadpool.cpp:84-99 | WINDOWS | P3 |
| G11 | `icall_environment_get_command_line` POSIX 为 "" stub；Process 域（start/wait/read_stdout_line）POSIX 全返回失败——均文档化"后续任务"，非分支缺失 | environment.cpp:522-543 / pathproc.cpp:421-657 | 全部 POSIX | P3（文档化 stub） |

### 3.6 编码/GC/其他域

| # | 缺口 | 位置 | 影响平台 | 严重度 |
|---|---|---|---|---|
| G12 | encoding.cpp "unicode" 页按 UTF-16LE 直拷——假设小端；当前全部目标（x86/ARM64）为 LE，需文档化为契约 | encoding.cpp:365-375 | 全部（未来大端） | P3（假设） |
| G13 | stopwatch.cpp `System_Diagnostics_Stopwatch_sf_Frequency` 在全局作用域（:59）与 namespace il2cpp（:79）**各定义一次**（两个符号）——非平台问题，DRY 违规 | stopwatch.cpp:59,79 | 全部 | P3（代码卫生） |
| — | GC 域（gc.cpp/gcapi.cpp/gcext.cpp）零平台调用（malloc 段式 mark-sweep） | — | 全部 | 无缺口 |
| — | http.cpp 15 处"OS 调用"全部是 socket 域委托（il2cpp_socket_\*），无裸系统调用 | http.cpp | 全部 | 无缺口 |
| — | 线程 ID 之外：timer/threadpool 的 -pthread 链接缺口已修复（CMakeLists find_package(Threads)）；numberformat `<cstdlib>` 已补 | CMakeLists.txt:6-8 | LINUX | 已修复 |

## 4. 修复建议（按优先级）

**P1（功能破坏，先修）**
1. `pinvoke.cpp` `GetLibraryHandle` 改为调用 `plat_dl_open`（拿到 .dll→so 映射），`dlsym` 保持或换 `plat_dl_sym`——同时消除设计文档 §7.2 的长期待定项。验证：POSIX 上 DllImport("mylib.dll") 命中 libmylib.so。
2. `rng.cpp`：`#if defined(__linux__)` 改 `#if defined(__linux__) && !defined(__ANDROID__)`（Android 走 /dev/urandom 兜底，天然可用），或按 `__ANDROID_API__ >= 28` 守卫。验证：NDK android-24 交叉编译。

**P2（特定平台语义错误）**
3. `platform_posix.cpp` `plat_dl_open` 增加 Apple 候选 `lib\<base\>.dylib`/`\<base\>.dylib`。
4. `environment.cpp` get_special_folder folder 0/5 加 POSIX 分支（`/Desktop`、`/Documents`）。
5. thread.cpp/monitor.cpp/pathproc.cpp 的线程 ID 改为调用 `plat_thread_current_id`（平台层已有 Apple/OpenBSD 正确处理）——同时消掉三份重复实现（DRY）。
6. io.cpp 文件操作迁移 `plat_file_open`（Windows 宽路径正确性）；或至少在文件头文档化 ANSI 码页限制。

**P3（加固/已知项）**
7. `plat_path_tmpdir` 加 `__ANDROID__` 分支（/data/local/tmp）。
8. environment.cpp os_version POSIX 对齐平台层格式。
9. Windows cpu_count 换 GetActiveProcessorCount。
10. guid.cpp 按设计文档批次升级 arc4random_buf/getentropy。
11. （结构层，跨批次）在 platform.h 顶部加 OS 归一化宏（IL2CPP_OS_WINDOWS/LINUX/ANDROID/APPLE/OPENBSD——对照 Boost BOOST_OS_\*），业务文件逐域迁移 plat_\*（设计文档 §2.3 原计划）。

**后续审计建议**：tests/ 847 个测试文件中有 209 个含平台分支（本次任务范围外），建议单独一轮 tests 域跨平台审计。

## 5. 证据索引

- [READ] src/runtime/include/internal/platform.h（31 API 契约）
- [READ] src/runtime/src/platform_posix.cpp / platform_win.cpp（双实现，逐行）
- [READ] src/runtime/src/：io、environment、pathproc、time、timeext、timezone、timer、stopwatch、thread、threadpool、console、rng、guid、interlocked、monitor、encoding、socket、ipaddr、streamio（前 120 行+关键区）、pinvoke、pinvokeext、marshalext2、allocator、gc（扫描）、gcext、http（扫描）、random（扫描）、decimal（_MSC_VER 区）
- [READ] src/runtime/CMakeLists.txt（Threads/CMAKE_DL_LIBS/ws2_32 已双分支）
- [READ] docs/design/cross-platform-design.md（§2.3 迁移批次未执行、§7.2 待定项仍开着）
- [BASH] Select-String 全量统计：27 文件含平台分支（25 cpp + 2 头）；plat_\* 调用者仅 threadobj.cpp；线程 ID 实现 4 处（平台层 1 + 业务 3）；tests 209/847 含分支
- [READ] reference/coreclr/src/coreclr/pal/：file/file.cpp:588-599（OpenBSD O_TRUNC quirk）、misc/environ.cpp:919（HAVE__NSGETENVIRON）、pal/inc/pal.h:3802（TARGET 宏来源）
- [WEB] boost.org Boost.Predef 文档（BOOST_OS_\* ← __APPLE__/__linux__ 等）、boost/config/detail/select_platform_config.hpp（#elif defined(__FreeBSD__)||…||defined(__OpenBSD__) 选 platform 配置头）、boost/config/platform/macos.hpp（BOOST_HAS_UNISTD_H/BOOST_HAS_SCHED_YIELD 等能力宏）、boost/predef/platform/android.h（BOOST_PLAT_ANDROID ← __ANDROID__）
