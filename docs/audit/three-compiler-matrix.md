# 三编译器矩阵汇总（收尾文档）— MinGW / MSVC / WSL GCC 7.5

- 日期：2026-08-04
- 任务：只读汇总 + 复核，不修代码，不 commit
- 范围：三编译器矩阵（MinGW clang17 / MSVC VS2022 19.39 / WSL GCC 7.5）全量验证收尾 + NDK 探路 + 平台抽象层现状 + 各轴残留问题清单
- 证据基线：docs/audit/msvc-probe.md、msvc-fullcheck.md、wsl-probe.md、wsl-fullcheck.md、ndk-probe.md、docs/design/cross-platform-design.md + 各修复批在 .opencode/ 的记录

## 1. 三编译器状态总表

| 轴 | 工具链 | runtime 库构建 | 测试构建 | 测试运行 | 警告 | 零警告门 |
|---|---|---|---|---|---|---|
| **MinGW** | clang 17（`-Wall -Wextra -fexceptions -fno-rtti`） | ✅ 0 错 0 警 | ✅ 全量 | ✅ e2e 逐字节一致 + 修复批 4 目标 exit 0 | **0** | ✅ 通过 |
| **MSVC** | VS2022 Enterprise 17.9.7 / cl 19.39.33523.0 / WinSDK 10.0.22621（`/W4 /EHsc /bigobj /utf-8`） | ✅ 0 错 0 警 | ✅ 88/88 | ✅ 86/87（fullcheck）+ 修复批 4 目标重验全绿 | **1 处 C4996**（平台层文件，见 §4.1） | ⚠ 1 残留 |
| **WSL** | GCC 7.5.0（Ubuntu1804，glibc 2.27）+ make + cmake 3.10.2（`-Wall -Wextra`） | ✅ 0 错（91 targets） | ✅ 全量 | ✅ 定向验证（test_pathproc 3 FAIL 为文档化 POSIX 缺口） | **2**（均在他域测试文件 test_gcstress.cpp） | ⚠ 2 残留（他域） |

### 1.1 MinGW（clang 17）— ✅ 绿

- 构建：runtime 库 + 全量 test_*.exe 零错误零警告（`-Wall -Wextra`）。
- 测试：跨域 e2e 大量用例逐字节一致（regexcase、arrays、stringext2、interlocked 28/28、base64 探针 20 行等，见 .opencode/memory.md）；MSVC 修复批同批 MinGW 验证 4 目标 0 警告 + 4 exe exit 0；test_pathproc 全绿。
- 结论：三轴中基线轴，始终零警告门通过，无残留。

### 1.2 MSVC（VS2022 19.39）— ✅ 绿（含 1 个平台层文件残留告警）

- 探路（msvc-probe.md）：runtime 库构建成功；68/70 测试编译；2 文件失败同根因（缺 `<limits>`）；4 处 C4996。
- 全量检查（msvc-fullcheck.md，metadata.cpp 修复后）：runtime 库 0 告警；**87/88 测试构建成功**（1 失败 = test_string_utf8 GNU 具名变参宏 + 空数组初始化器）；**86/87 运行 PASS**（1 失败 = test_math signed-zero 平局，MSVC /O2 把三目折叠为 SSE `maxsd/minsd` 第二操作数胜出，`dumpbin` 实证 `F2 0F 5F C1`）。
- 修复批（runtime_msvcfix_msvc，2026-08-04）：3 类 4 文件全修（test_string_utf8 宏/空数组、test_math 值级断言、strncpy×4 → `_MSC_VER` 条件 strncpy_s）→ **4 目标构建成功 + 4 exe exit 0，测试 TU C4996=0**（[BASH:Select-String build_targets.log → C4996 仅 platform_win.cpp:273]）。
- 测试数汇总：88 个目标全可构建可运行（4 修复目标重验绿 + 其余 84 fullcheck 绿；修复仅动测试文件，不影响其余目标行为）。
- 零警告门：测试侧已达标；**runtime 侧唯一残留 = `platform_win.cpp:273` `_wfopen` C4996**（见 §4.1）。

### 1.3 WSL（GCC 7.5）— ✅ 绿（含 2 个他域测试文件警告）

- 探路（wsl-probe.md）：67/69 编译成功；2 硬错误（encoding.cpp 无守卫 windows.h + numberformat.cpp:339 缺 `<cstdlib>`）；3 警告。
- 全量检查（wsl-fullcheck.md）：批1 后 86/87 编译单元零错误零警告；唯一硬阻塞 = pathproc.cpp 遗留 POSIX 缺口（`IsSegmentTerminator` 在 `_WIN32` 块内 + `::exit` 缺 `<cstdlib>`，8a9a3a9 引入）；确认 pthread 链接缺口（`find_package(Threads)` 已加但从未链接）。
- 修复批（pathproc.cpp 4 处修复 + Threads::Threads 接线，2026-08-04）：**全量 make -j4 0 error（91 targets built）**；警告收敛至 0（lib+pathproc），仅剩 **test_gcstress.cpp:383,425** 2 处 unused-but-set（他域测试文件）；test_pathproc 定向重验：3 FAIL 全为 GetFullPath POSIX 文档化缺口（`nullptr`，header 注明 best-effort；realpath 为未来平台层项），GetTempPath/ChangeExtension/HasExtension/Process ids/Start 全过；get_current_thread_id 修复（`syscall(SYS_gettid)`，pthread_t 64 位指针截断可为负实证）。
- 说明：WSL 轴验证到"构建全绿 + 警告收敛 + 定向测试"；全量 test exe 运行表未在修复后重跑（wsl-fullcheck 测试表为空因当时构建失败）。

## 2. NDK 探路（Android arm64-v8a）— 编译过 / 链接缺口

来源：docs/audit/ndk-probe.md（2026-08-04，只读探测，未跑 adb）。

| 项 | 结果 |
|---|---|
| 环境 | NDK 26.1.10909125（`D:\android\sdk\ndk`，另有 21.1/20.1 备用）；`ANDROID_NDK_HOME` 等环境变量全部为空（需显式路径） |
| 单文件编译（直接 clang `--target=aarch64-linux-android21`） | stopwatch / interlocked / monitor 全 EXIT=0；产物 `llvm-readelf` 确认 ELF64 AArch64 |
| 端到端链接（main + stopwatch.o + interlocked.o） | **LINK EXIT=0**，PIE DYN，interp=`/system/bin/linker64`，NEEDED=libc++.so/libm.so/libdl.so/libc.so，符号全解析 |
| CMake 交叉编译（android.toolchain.cmake，-DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-21） | CONFIGURE EXIT=0 + BUILD EXIT=0（4/4，ninja） |
| **链接缺口** | monitor.cpp 缺 `il2cpp_throw_*` 异常函数符号——跨文件依赖 exception.cpp/exceptionext.cpp，**非平台问题**；整库编译（src/runtime CMakeLists GLOB 已含）即解 |
| Bionic 特有 | **iconv 缺失**（libc.so API21 零符号）——encoding.cpp 手写 UTF-8↔UTF-16 编解码器恰好规避，零改动；gai_strerror/strsignal 已导出；POSIX 头全齐；Windows-only 头（conio/direct/objbase）全在 `_WIN32` 守卫内 |
| 集成路径 | `src/runtime/CMakeLists.txt`（GLOB + STATIC + `-fexceptions -fno-rtti`）可直接 `add_subdirectory` 纳入 Android 构建 |

结论：NDK 轴文件级兼容好、无 Bionic 大坑，编译链路打通；剩余为整库链接 + 设备部署（adb 环节域外）。

## 3. 平台抽象层现状

来源：docs/design/cross-platform-design.md §2 + 平台层落地批1（.opencode/memory.md）+ 本次复核。

| 项 | 状态 |
|---|---|
| `src/runtime/include/internal/platform.h` | ✅ 纯接口头（零平台头），**8 域 31 API**：[READ 复核] 线程 5（current_id/sleep/yield/spawn/join）+ 同步 10（mutex init/lock/trylock/unlock/destroy + cond init/wait/signal/broadcast/destroy）+ 时钟 2（monotonic_ns/frequency）+ 文件 3（file_open/path_full/path_tmpdir）+ 动态库 2（dl_open/dl_sym）+ 环境 2（env_get/env_set）+ 目录 4（dir_exists/mkdir/rmdir/path_is_dir）+ 进程 3（process_id/cpu_count/os_version） |
| `src/runtime/src/platform_win.cpp` | ✅ 464 行 `_WIN32` 实现（QPC/CS/CV/_wfopen/GetFullPathNameW/GetTempPathW/LoadLibraryA/GetEnvironmentVariableA/_stat/_mkdir/RtlGetVersion）；其他平台空 TU |
| `src/runtime/src/platform_posix.cpp` | ✅ 394 行，`__linux__`(SYS_gettid)/`__ANDROID__`/`__APPLE__`(pthread_threadid_np)/`__OpenBSD__`(getthrid) 四分支 + 其他 POSIX fallback（std::hash<thread::id>）；**.dll→.so 库名映射在 plat_dl_open 内**（设计文档 §7.2：转译器零平台分支） |
| `tests/runtime/test_platform.cpp` | ✅ 12 节覆盖 8 域；MinGW 全绿 EXIT=0 + WSL 3 连跑 RC=0（trylock 测试移到 owner-less 路径后双平台一致） |
| 契约 | platform.h 明示 mutex **NOT reentrant**（pthread 非递归 vs CRITICAL_SECTION 可重入的双平台差异实证） |
| 迁移顺序 | 设计文档 §2.3：同步域②最先（monitor/threadpool/timer 共享 -pthread 缺口），文件域④ GetFullPath/TempPath 功能缺口属平台层样板项 |

## 4. 各轴残留问题清单

### 4.1 MSVC

| 残留 | 位置 | 状态 | 复核结论 |
|---|---|---|---|
| C4996 `_wfopen` | `src/runtime/src/platform_win.cpp:273` | **仍存在**（本次复核 [READ:platform_win.cpp:273]） | msvc-fullcheck 的"runtime 库 0 告警"在其加入前写出，**已过期**；但当时记录的 streamio.cpp:284,289 C4996 已被修复批改为 `_wfopen_s`（[READ:streamio.cpp:285,297]）——**旧清单已清零，新残留来自后加入的平台层文件**。platform_win.cpp:267 注释"accepted by the existing streamio.cpp precedent"也已过期（先例已修）。待平台层代理完工前改 `_wfopen_s` |
| C4819 代码页噪音 | — | **无残留** | 全 audit 文档零记录（[BASH:grep C4819 → No files found]）；`/utf-8` 编译选项已在 CMakeLists 两处（[READ:tests/runtime/CMakeLists.txt:40]、[READ:src/runtime/CMakeLists.txt:17]）——MSVC 按代码页 936 解读 UTF-8 源文件的噪音从探路起就被消除，从未进过问题清单 |
| 附注 | CMakeLists.txt 中文注释为 GBK 编码（Select-String 输出乱码实证） | 文件编码不一致瑕疵，不触发编译告警（CMake 不编译），非编译器轴问题，记录不处理 |

### 4.2 WSL / POSIX

| 残留 | 位置 | 状态 |
|---|---|---|
| 警告×2 | `tests/runtime/test_gcstress.cpp:383,425` unused-but-set | 他域测试文件，非共享代码；WSL 全量构建唯一警告来源 |
| GetFullPath 3 FAIL | pathproc POSIX `nullptr`（header 文档化 best-effort） | **功能缺口**：.NET Path.GetFullPath 语义缺失；设计文档 L82 列 realpath 为平台层文件域④未来项 |
| Threads::Threads | `src/runtime/CMakeLists.txt:13` 仅 `PUBLIC ${CMAKE_DL_LIBS}` | wsl-fullcheck §5 施工单建议 PUBLIC 到 il2cpp_runtime，**当前只执行了 tests 侧**（[READ:tests/runtime/CMakeLists.txt:32] `PRIVATE il2cpp_runtime Threads::Threads`）；src/runtime 侧仍未链——生成代码（e2e gen/）若直接链 il2cpp_runtime 且用线程符号，在 glibc<2.34 平台需注意。记录待办 |

### 4.3 平台层 / 移动端

| 残留 | 状态 |
|---|---|
| GUID 随机源 | 非 Win 平台 LCG（SplitMix64）非 CSPRNG——设计文档批 2/3/4 按平台升级 arc4random（`__ANDROID__`/`__APPLE__`）/getentropy（`__OpenBSD__`） |
| Apple（iOS/macOS） | **未验证**——无本机硬件；设计文档批 3：`__APPLE__` 分支（pthread_threadid_np/arc4random/clock 守卫）+ clang 交叉编译级验证；wchar_t 32 位陷阱已由平台层 UTF-8 接口规避 |
| OpenBSD | **未验证**——设计文档批 4：`__OpenBSD__` 分支（getthrid/getentropy）+ clang `--target=x86_64-unknown-openbsd` 编译级验证；POSIX 全齐零改动预期 |
| NDK 全量整库编译 | 探路只验 3 文件 + 最小链接；整库 86+ 文件编译与 Bionic 差异收集为批 2 任务卡 |

### 4.4 已清零项（历史问题 → 现状态）

- encoding.cpp 无守卫 `#include <windows.h>` → 双分支 + 手写编解码器（✅）
- numberformat.cpp:339 `std::atoi` 缺 `<cstdlib>` → 已修（✅）
- boxingext2.cpp:304 strict-aliasing、streamio.cpp:273 未用变量、time.cpp:346 format-truncation → 已清零（✅）
- test_numberformat/test_formatext 缺 `<limits>` → 已补（✅）
- streamio.cpp `_wfopen` C4996 ×2、test_exceptionext.cpp `strncpy` C4996 ×2 → 已修（✅）
- test_string_utf8 GNU 宏/空数组、test_math signed-zero、test_arraybounds/test_regex_api strncpy×4 → 已修（✅）
- pathproc.cpp POSIX 编译缺口（IsSegmentTerminator/::exit）→ 已修（✅）
- pthread 链接缺口 → tests 侧已链（⚠ src/runtime 侧见 §4.2）

## 5. 跨平台结论

1. **三轴全绿**：MinGW（clang 17）零警告门通过；MSVC（VS2022 19.39）88 目标全可构建可运行，测试侧 C4996=0，唯一残留为平台层文件 `platform_win.cpp:273` C4996（在途域）；WSL（GCC 7.5）全量构建 0 error，警告收敛至 0（lib），仅 2 处他域测试文件警告 + 3 处 GetFullPath 文档化功能缺口。
2. **平台抽象层就位**：platform.h 8 域 31 API + platform_win/platform_posix 双实现 + test_platform 12 节双平台全绿；转译器零平台分支（.dll→.so 映射收敛在 plat_dl_open）。
3. **移动端待办**：NDK arm64-v8a 交叉编译链路已探通（编译过/最小链接过，monitor 跨文件异常符号整库即解，iconv 缺口被手写编解码器规避）；Apple/OpenBSD 未验证（设计文档批 3/批 4，验收下限为交叉编译级）；GUID 随机源升级 + NDK 整库编译为批 2 内容。

## 附：本次复核证据

- [BASH:grep C4819 docs/ → No files found]
- [READ:platform_win.cpp:265-273]（`_wfopen` C4996 仍存在；:267 注释引用的 streamio 先例已过期）
- [READ:streamio.cpp:285,297]（已改 `_wfopen_s`）
- [BASH:Select-String build/runtime_msvcfix_msvc/build_targets.log → C4996 仅 platform_win.cpp(273,14) _wfopen]
- [READ:platform.h]（8 域 31 API 计数复核：5+10+2+3+2+2+4+3=31）
- [READ:tests/runtime/CMakeLists.txt:21,32,40,43]（Threads::Threads 已链 tests 侧；/utf-8 + /W4）
- [READ:src/runtime/CMakeLists.txt:13,17]（src/runtime 侧无 Threads；/utf-8 + /W4）
- [BASH:build/runtime_msvcfix_msvc/Release → test_arraybounds/test_math/test_regex_api/test_string_utf8 4 exe 产物在]
