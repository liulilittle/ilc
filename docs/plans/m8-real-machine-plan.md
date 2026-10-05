# M8 实机验证清单（探通/S1/无实机项已执行 → 剩余实机依赖整理）

- 日期：2026-08-10（更新：执行状态标注）
- 分支：m1；HEAD=8838aba（本任务只读调研 + 仅更新本计划文件：不 commit、不改码、不写 .opencode/）
- 输入基线：
  - docs/audit/cross-platform-m8.md（Apple x86_64 / OpenBSD x86_64 全 121 文件 × 2 目标 `-fsyntax-only` PASS，0 错误；当时仅 enumext.cpp 2 条 sign-compare 警告——**该警告已由 d1cfbfd 修复**）
  - docs/audit/cross-platform-audit.md（G1-G14 缺口清单；平台层 31 API 已落地但采用度不足——**G1/G2/G3/G4/G5 等核心修复已由 68172df/d1cfbfd 落地，见 §5**）
  - docs/design/cross-platform-design.md（§5.4 真机验证依赖 CI 容器/模拟器；§6 批 1-4 验收门 = 编译零错零警 + 域单测绿 + 回归绿）
  - scripts/ci.ps1 / run-tests.ps1 / tests/runtime CMakeLists.txt（现有验证基础设施盘点）
- 性质：**验证清单 + 阶段化计划**。§0-§2 已按 2026-08-10 实测状态标注 [x]/[ ]；剩余未执行项全部为**外部资源依赖项**（实机/runner/VM/adb 许可环境），见 §7 清单。
- 执行状态摘要（本轮核验，证据见 §6）：
  1. ✅ 探通 121×2（Apple/OpenBSD 语法级全 PASS）
  2. ✅ WSL 运行时全量 135/135（runtime 测试 exe 全数 PASS）
  3. ✅ NDK S1（arm64-v8a 121/121 编译 + 135/135 链接 + G2 getrandom PASS）
  4. ✅ WSL mylib.so dlopen 路径 PASS（G1 接线确认）
  5. ✅ 交叉告警清零（enumext sign-compare + io.cpp -Wreturn-type 修复，4 平台 0 警告）

---

## 0. 现状盘点（实机验证缺什么）

| 能力 | 现状 | 缺口 |
|---|---|---|
| Apple x86_64 语法编译 | ✅ m8 探通：121/121 PASS（合成 sysroot + `-mmacosx-version-min=10.12`） | 无真实 SDK 的完整编译；无 arm64；链接与运行时全缺 |
| OpenBSD x86_64 语法编译 | ✅ m8 探通：121/121 PASS（合成 sysroot） | 同上 |
| **交叉告警清零** | ✅ **4 平台（MinGW/MSVC/WSL/NDK）0 警告**：d1cfbfd 修 enumext.cpp:244,256 sign-compare + io.cpp:783 -Wreturn-type（il2cpp_throw_io_exception 后补 `std::abort()` 兜底，io.cpp:784-786）；814c5af "4-platform zero-warning" | 无（告警门在源码层已达标；实机编译时复验即可） |
| **链接** | ✅ NDK S1 已链（arm64 121 编译 + 135/135 exe + DT_NEEDED 检查）；❌ Apple/OpenBSD **从未链接**（合成 sysroot 只有头文件、无库） | Apple/OpenBSD 需真实 sysroot（osxcross / 实机工具链） |
| **运行时** | ✅ WSL（Linux x86_64）已全量运行；❌ macOS/OpenBSD/Android **从未运行** | 全部 POSIX 平台需实机/VM |
| **测试** | ✅ WSL 全量 135/135（m8s2 全量 run）；✅ NDK 链接 135/135；Windows 侧已有；❌ macOS/OpenBSD/Android 未跑 | 三平台需实机/VM + adb 许可环境 |
| WSL 运行时 | ✅ 135/135（d1cfbfd；含 monitor/mathf/console/socket 平台自适应、framestack thread_local 9fa9ba5、decimal UB 修复） | 无（T2/T3 已有） |
| NDK | ✅ **S1 完成**：arm64-v8a 121/121 compile + 135/135 link + G2 getrandom PASS（d7a5dec，report m8s1-b23）；`build/runtime_b26_com4_ndk|com5_ndk|plp_ndk` 产物在 | **S2/S3 需实机 + adb**（禁 adb 铁律：HANDOFF.md:61 / project-state-20260810.md:150 → 外部资源，见 §7-C）；`scripts/run-ndk.ps1` 仍未落地（S1 用 %TEMP% 探针脚本执行，脚本化属实施批次） |
| CI 资源 | ❌ 无 .github/workflows（无任何 CI yml；`Test-Path .github\workflows` = False） | macOS runner / OpenBSD VM / Android 设备均需外部资源（§4） |

---

## 1. 验证矩阵总览（平台 × 阶段 × 手段）

| 平台 | S1 编译+链接（可交叉） | S2 运行时（需实机/VM） | S3 全测试 |
|---|---|---|---|
| **macOS 实机**（x86_64 + arm64） | [ ] 真实 AppleClang 全量编译 + 静态库/exe 链接（V1/V3-V6；V2 前置已修） | [ ] 冒烟 + 平台特有点 R1-R8 | [ ] tests/runtime 135 + e2e + C API T1-T3 |
| **OpenBSD 实机/VM**（x86_64） | [ ] 真实 sysroot 编译 + 链接（V1/V3/V4；V2 前置已修） | [ ] 冒烟 + 平台特有点 R1-R6 | [ ] 同上 T1-T3 |
| **Android 实机**（arm64-v8a） | ✅ **S1 全完成**（V1-V4：121/121 compile + 135/135 link + G2 PASS + e2e 生成物路径可用性，见 §2.3） | [ ] adb 推送运行冒烟 R1-R5（**禁 adb 铁律 → 外部资源**） | [ ] tests/runtime 全量（慢，可选子集）T1-T3 |
| **NDK 链接**（无实机） | ✅ 纯交叉完成（135/135 链接 + llvm-readelf DT_NEEDED 检查） | —（无运行时验证） | — |
| **WSL 运行时**（已有 Linux x86_64） | ✅ 4 平台 0 警告（含 WSL GCC 7.5） | ✅ R1-R3 全完成（mylib.so PASS、TMPDIR、线程 ID） | ✅ T1 135/135；T2/T3 已有 |
| Windows（基线对照） | ✅ 已有 msvc/mingw | ✅ 已有 | ✅ 已有（仅回归，不新增） |

---

## 2. 平台分项清单（验证项逐条）

> 标注规则：[x] = 已执行（本轮核验）；[ ] = 未执行（剩余项，依赖外部资源，验收标准见 §7）。

### 2.1 macOS 实机（x86_64 + arm64，二架构同单）——全部剩余，依赖 macOS runner/实机

**S1 编译+链接**（需 Xcode CLT / osxcross）
- [ ] V1 真实 SDK 全量编译：`clang++ -std=c++17 -Wall -Wextra -fexceptions -mmacosx-version-min=10.12` 编译 `src/runtime/src/*.cpp` 全 121 文件（替代 m8 合成 sysroot，验证真实头文件/宏环境）
- [x] V2 前置修复**已落地**：enumext.cpp:244,256 sign-compare 已修（d1cfbfd，`value > UINT8_MAX/UINT16_MAX` → 显式 255U/65535U）——实机编译时零警复验即可
- [ ] V3 链接 il2cpp_runtime 静态库（cmake，复用 tests/runtime/CMakeLists.txt 的 add_subdirectory 路径；macOS 下 `CMAKE_DL_LIBS` 为空、dl 在 libc 内，无需 -ldl）
- [ ] V4 链接 tests/runtime 全部 test_*.cpp exe（**135** 个）——验证 thread_local（pinvoke.cpp:59,159 / threadpool.cpp:90）在真实 clang 链接成功
- [ ] V5 arm64-apple-darwin 目标重复 V1-V4（m8 只覆盖 x86_64；Apple Silicon 是主要部署形态）
- [ ] V6 链接一个真实 e2e 生成物（tests/cases 任一生成 gen/ → cmake build → exe），验证"生成代码 + runtime"整链

**S2 运行时**（平台特有点，全部可脚本化断言）
- [ ] R1 test_object.exe + test_gc.exe 运行 EXIT 0（冒烟基线，等价 ci.ps1 第 3.5 步）
- [ ] R2 **线程 ID（G5 已收敛至 plat_thread_current_id，thread.cpp:9,24,90 / monitor.cpp:49,191,195）**：Apple 走 `pthread_threadid_np` 且非 0/负/哈希（运行 test_thread.exe / test_monitor.exe / test_pathproc.exe 观察线程 ID 域）
- [ ] R3 **pinvoke dlopen（G1 已接线 + G3 .dylib 候选已加，68172df）**：DllImport("mylib.dll") 经 `plat_dl_open` 命中 `libmylib.dylib`——运行 tests/cases/pinvoke
- [ ] R4 rng/arc4random_buf 分支：test_rng.exe / test_guid.exe PASS（随机源功能）
- [ ] R5 socket 回环：test_socket.exe / test_ipaddr.exe（BSD socket + SO_NOSIGPIPE 分支）
- [ ] R6 console termios：test_console.exe（如 CI 无 TTY，标记"人工/伪终端验证"，失败可豁免并记录）
- [ ] R7 timezone/localtime_r：test_time.exe / test_timezone.exe（真实时区库）
- [ ] R8 environment get_special_folder（G4 已修 DirSep，68172df）：POSIX 分支断言 `~/Desktop` 形态

**S3 全测试**
- [ ] T1 tests/runtime 全 135 个 exe 逐一运行 PASS（需 scripts/run-tests-posix.ps1 形态 runner，m8s2 已验证过临时脚本形态）
- [ ] T2 e2e 全量（POSIX 侧 diff 基线策略未定，见 §5 风险 R2）
- [ ] T3 run-capi.ps1 等价物（C API 冒烟）

### 2.2 OpenBSD 实机/VM（x86_64，建议 7.5+）——全部剩余，依赖 OpenBSD VM/实机

**S1**
- [ ] V1 真实 sysroot（/usr/include + libc.a）全量编译 121 文件 `clang++ -std=c++17 -Wall -Wextra`（或系统 GCC）
- [x] V2 前置修复**已落地**（同 Apple：enumext.cpp d1cfbfd，m8 已证两目标告警一致）——零警复验
- [ ] V3 链接静态库 + tests/runtime 135 exe（OpenBSD libc 内含 pthread/dl，无需 -pthread/-ldl——design §5.4 已文档化）
- [ ] V4 链接一个 e2e 生成物（整链验证）

**S2**
- [ ] R1 test_object + test_gc EXIT 0
- [ ] R2 **线程 ID（G5 已收敛）**：三处走 `getthrid`（pthread_np.h）且非 0/负
- [ ] R3 **pinvoke dlopen（G1 已接线）**：`libmylib.so` 命中（OpenBSD 无 .dylib 问题，验证 G1 映射主路径）
- [ ] R4 rng/getentropy 分支：test_rng / test_guid PASS
- [ ] R5 socket 回环（MSG_NOSIGNAL/SO_NOSIGPIPE 特征测试分支——OpenBSD 二者皆无，验证特征测试兜底路径）
- [ ] R6 timezone / console termios

**S3**
- [ ] T1 全 135 exe PASS；T2 e2e 全量（同上 diff 基线问题）；T3 C API

### 2.3 Android 实机（arm64-v8a）+ NDK 链接

**S1（NDK 交叉，✅ 已完成——2026-08-10 实测）**
- [x] V1 NDK r26.1 工具链 `-DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-24` cmake 配置 + 编译 121 文件（**121/121 PASS，0 错误**）+ il2cpp_runtime 静态库（15,971,284 字节；m8s1-b23-report）
- [x] V2 **G2 编译风险验证 PASS**：rng.cpp:42-44 已有 `#if defined(__linux__) && !defined(__ANDROID__)` 守卫 → Android 走 /dev/urandom 兜底；pin-before-fix 实验证实 android-24 下 `getrandom` 报 `use of undeclared identifier`（Bionic `__INTRODUCED_IN(28)`），守卫正确排除（m8s1-b23-report §3）
- [x] V3 链接 tests/runtime **135/135 exe**（392/392 目标，EXIT=0，0 errors；llvm-readelf：ELF64/AArch64/DYN-PIE；DT_NEEDED=libdl/libm/libc，libc++ 动态依赖，libc++_shared.so 需随包——缺依赖项=空）
- [x] V4 e2e 生成物链接路径可用性：NDK S1 全量 CMake 链接（含 add_subdirectory 路径）通过，生成 CMakeLists 路径在 NDK 工具链下可用（m8s1-b23-report；链接集含全部测试域）

**S2（需实机或模拟器——【禁 adb 铁律】→ 外部资源，见 §7-C）**
- [ ] R1 adb push tests/runtime exe 至 /data/local/tmp 运行 test_object + test_gc EXIT 0
- [ ] R2 **tmpdir（G7）**：`plat_path_tmpdir` **未加 `__ANDROID__` 分支**（本轮核验：pathproc.cpp 无 `__ANDROID__`/`data/local/tmp`）→ 预期失败，记录为已知缺口，不阻塞
- [ ] R3 线程 ID SYS_gettid（Bionic 定义 __linux__，理论可用——运行验证）
- [ ] R4 socket（Android 网络权限：测试用例需 INTERNET 权限或在受限环境豁免）
- [ ] R5 pinvoke：`libmylib.so` 随 apk/jniLibs 或直接 dlopen 路径命中（验证 G1 在 Android 的 .so 映射）

**S3**
- [ ] T1 tests/runtime 全量（实机较慢，可按域分批）；T2 e2e 子集（冒烟优先，全量可选）；T3 C API

### 2.4 WSL 运行时（Linux x86_64，✅ 全部完成）

- [x] V1（S1）`-Wall -Wextra` 零警告复核：**4 平台（MinGW/MSVC/WSL/NDK）0 警告**（d1cfbfd + 814c5af）
- [x] R1（S2）**pinvoke .so（G1）PASS**：`tests/cases/pinvoke/mylib/mylib.cpp` `g++ -shared -fPIC` 构建 `libmylib.so`（nm -D 确认 add_ints/concat_len 导出）→ `il2cpp::il2cpp_pinvoke_resolve("mylib.dll", "add_ints")` 命中 → `G1-PASS: add_ints(20,22)=42`，EXIT 0（m8s2-b23-report §1；映射代码 platform_posix.cpp:342-363，test_platform.cpp:387-402 9b 覆盖测试）
  - 注：e2e pinvoke 用例 CMakeLists 走**静态链接**（gen/CMakeLists.txt:11 target_link_libraries mylib），dlopen 路径已由 runtime API 直接验证；run-tests.ps1:41-47 仍只构建 mylib.dll，**.so 分支未合入脚本**（探针已验证，脚本化属实施批次）
- [x] R2（S2）TMPDIR 语义：`il2cpp_path_get_temp_path` 读 TMPDIR 分支——覆盖于 135/135 全量 run（test_pathproc PASS）
- [x] R3（S2）线程 ID SYS_gettid 分支——覆盖于 135/135 全量 run（test_thread/test_monitor PASS）
- [x] T1（S3）**tests/runtime 全量 135/135**（d1cfbfd；含 82d0770 cstdlib、9fa9ba5 framestack thread_local、d7a5dec 平台自适应；m8s2-b23-report §2：132→135 路径 = 2 测试文件补 `<cstdlib>` + decimal INT32/INT64_MIN UB 修复）
- [x] T2（S3）e2e 全量已有（ci 第 6 步）；T3 C API 已有

### 2.5 Windows 基线（不新增，仅回归）

- [ ] 每次平台批次合入后跑 ci.ps1 全量（msvc/mingw/wsl 三编译器）确认无回归——对齐 design §6"前一批回归绿"验收门（当前基线：runtime 135/135 四平台 0 警）

---

## 3. 阶段化（2026-08-10 实测更新）

| 阶段 | 内容 | 前置条件 | 可执行环境 | 验收门 |
|---|---|---|---|---|
| **S1 编译+链接** | 各平台全量编译 121 文件零错零警 + 静态库 + tests/runtime exe + 1 个 e2e 生成物链接 | enumext/io 告警已清零（d1cfbfd）；NDK G2 已验证 PASS | ✅ NDK：**已完成**（121/121 + 135/135 + G2 PASS）；❌ Apple/OpenBSD 需真实 sysroot（osxcross 或实机工具链）——**若无 osxcross/实机，Apple/OpenBSD 链接级延迟到 S2 上实机做** | NDK：**已达标**；每平台：编译 121/121 零错误；`-Wall -Wextra` 零警告；链接产物齐全（静态库 + 135 exe + 1 e2e exe） |
| **S2 运行时** | 冒烟（test_object/test_gc）+ 平台特有点（§2 每平台 R1-R8） | S1 绿（NDK 已绿） | ✅ WSL：**已完成**；❌ macOS（x86_64/arm64）、OpenBSD VM（vmm/qemu 7.x）、Android 实机（**禁 adb 铁律 → 需外部许可环境**） | WSL：**已达标**；其余：冒烟 EXIT 0；特有点断言 PASS；已知缺口（G7 Android tmpdir 未修）按"预期失败+记录"处理，不阻塞 |
| **S3 全测试** | tests/runtime 全 135 exe + e2e 全量 + C API | S2 绿 | ✅ WSL：**已完成**（135/135 + e2e + C API）；❌ 其余三平台同上 | WSL：**已达标**；其余：135/135 PASS；e2e 全量 PASS（需先解决 POSIX 侧 diff 基线，见 §5 风险 R2）；C API PASS |

执行状态：**无实机/无 CI 的验证项已全部完成**（探通 121×2、WSL 全量、NDK S1、告警清零）。剩余项全部依赖外部资源（§4/§7）。

---

## 4. 依赖（实机/CI 资源需求——2026-08-10 更新）

| 资源 | 用途 | 形态 | 成本/备注 | 状态 |
|---|---|---|---|---|
| GitHub Actions 或等效 runner | macOS 实机化 | `macos-13`（x86_64）/`macos-14`（arm64）runner | macOS runner 托管免费（公开仓库） | 未获取（外部资源） |
| OpenBSD 实机/VM | OpenBSD 全阶段 | qemu/vmm 跑 OpenBSD 7.x amd64；或第三方 BSD runner（无官方 GH Actions OpenBSD runner） | 低配 VM（2c/4G）足够；S3 e2e 全量建议 4c | 未获取（外部资源） |
| Android 实机/模拟器 + **adb 许可环境** | Android S2/S3 | arm64-v8a 实机（或 x86_64 模拟器冒烟）；**当前环境禁 adb（HANDOFF.md:61 / project-state-20260810.md:150 铁律）** | 模拟器 arm64 镜像慢；实机 adb 需外部环境或明确解禁 | 未获取（外部资源 + 规则约束） |
| osxcross（可选） | Apple S1 链接级（无实机时） | macOS SDK + osxcross clang → 可链接 libSystem | 许可证：SDK 需从 Xcode 提取（许可约束）；**建议直接上实机 runner，osxcross 仅作降级路径** | 未获取（外部资源） |
| NDK r26+（已有方案） | Android S1 | 独立工具链 / cmake toolchain 文件；`ANDROID_PLATFORM=android-24` 是 G2 验证基线 | 免费；**S1 已完成**（D:\android\sdk\ndk\26.1.10909125） | ✅ 已有 |
| WSL Ubuntu1804（已有） | WSL 全阶段 | 现有 `wsl -d Ubuntu1804`（GCC 7.5） | 零新增；**全阶段已完成** | ✅ 已有 |
| net48 参考运行 | e2e diff 基线 | 仅 Windows 可跑（现有 run-tests.ps1 第 2 步） | POSIX 平台 e2e 的"参考输出"需跨平台方案（见风险 R2） | 已有（Windows 侧） |
| 新增脚本（实施批次做） | runner | scripts/run-tests-posix.ps1（全 135 exe runner——**已创建 2026-08-19**，Windows 侧实测 15/15 PASS + 错误路径验证）、scripts/run-ndk.ps1（design 批 2 已规划，**仍不存在**——NDK S1 用 %TEMP% 探针脚本执行）、run-tests.ps1 补 WSL mylib.so 构建分支（探针已验证路径） | 3 个脚本，均为实施批次范围，非实机阻塞项 | run-tests-posix.ps1 已创建；其余未创建（实施批次） |

---

## 5. 风险与前置修复（2026-08-10 更新：G1-G5 修复已落地）

| 缺口 | 验证位置 | 前置要求 | 状态（本轮核验） | 若未修 |
|---|---|---|---|---|
| G1 pinvoke 裸 dlopen（P1） | macOS R3 / OpenBSD R3 / Android R5 / WSL R1 | 接线 `plat_dl_open`（含名字映射） | ✅ **已修**（68172df；platform_posix.cpp:342-363 .dll→.so/.dylib 映射；WSL R1 PASS 实证 m8s2-b23） | 已消除 |
| G2 rng getrandom（P1，Android 编译） | NDK V2 | `__ANDROID__` 守卫 | ✅ **已修 + 验证 PASS**（68172df 守卫 + d7a5dec/m8s1-b23 G2 pin-before-fix 实证：android-24 下 getrandom 不可见，/dev/urandom 兜底正确） | 已消除 |
| G3 plat_dl_open 缺 .dylib（P2） | macOS R3 | 加 .dylib 候选 | ✅ **已修**（68172df "dl_open Apple dylib candidates"） | 已消除 |
| G5 线程 ID 三处重复（P2） | macOS R2 / OpenBSD R2 | 迁移 plat_thread_current_id | ✅ **已修**（68172df "thread ID convergence to plat_thread_current_id"；thread.cpp:9,24,90 / monitor.cpp:49,191,195 现行引用） | 已消除 |
| G4 反斜杠路径（P2） | macOS R8 / 各 POSIX | POSIX 分支（DirSep） | ✅ **已修**（68172df "get_special_folder DirSep"）——实机复验 R8 即可 | 已消除 |
| G7 Android tmpdir（P3） | Android R2 | `__ANDROID__` 分支 | ❌ **未修**（pathproc.cpp 无 `__ANDROID__`/`data/local/tmp`，本轮核验） | 预期失败 + 记录，不阻塞 |
| G8 os_version（P3）/ G11 stub（P3）/ G14 CRLF（P3）/ G10 LCG（P3） | 各 POSIX S2/S3 | —（已知项） | 未核验（需实机） | 记录实测值，不阻塞 |

**风险（2026-08-10 更新）**
- R1 **Apple/OpenBSD 无实机、无 osxcross：S1 链接级与 S2/S3 全部阻塞** → 依赖 §4 资源到位；**这是探通之后唯一的硬依赖**（探通本身已完成 121×2）
- R2 e2e 参考基线：net48 参考运行只在 Windows；POSIX 平台的 e2e 预期输出需改用"快照比对"（首次在实机生成 golden 文件）或"生成物自持断言"——S3 前必须先定基线策略（本计划不解决，属实施批次决策）
- R3 告警门：✅ **已消除**（enumext.cpp + io.cpp 修复，d1cfbfd；4 平台 0 警告实证）——实机编译仅需零警复验
- R4 模拟器 vs 实机：Android x86_64 模拟器可加速 S2 冒烟，但 arm64 ABI 行为差异需实机终验（arm64-v8a 是设计基线）；**且 adb 铁律约束下模拟器同样需外部环境**
- R5 **禁 adb 铁律**：当前环境（Windows 开发机）禁止使用 adb（HANDOFF.md:61；project-state-20260810.md:150）→ Android S2/S3 全部为**外部资源依赖项**，除非规则被明确解禁；同时为已获取资源的唯一前置约束

---

## 6. 证据索引（2026-08-10 本轮核验）

- [READ] docs/audit/cross-platform-m8.md（121×2 语法 PASS 全表；enumext 2 告警——已修）
- [READ] docs/audit/ndk-probe.md（NDK r26.1 环境盘点；**未使用 adb**）
- [READ] docs/project-state-20260810.md（"四平台 0 警告…M8 平台：S1 完成（NDK-arm64）121/121 compile + 135/135 link + G2 PASS；探通 121/121×2；S2 大部分完成（WSL mylib.so PASS）；剩余 macOS/OpenBSD 实机验证项全部依赖外部资源"；:150 禁 adb）
- [READ] docs/HANDOFF.md:61（"NDK=…；**禁 adb**"铁律原文）
- [BASH] git log --all --oneline（d7a5dec "WSL 135/135 + NDK arm64 S1 (121/121 compile, 135/135 link, G2 getrandom PASS)"；0e11019 "M8 S2 (WSL mylib.so PASS"；d1cfbfd "enumext/io warning cleanup (WSL 135/135, 4-platform 0-warning)"；814c5af "4-platform zero-warning"；8921ff8 "M8 Apple/OpenBSD 121x2 full pass"；68172df "cross-platform P1/P2（pinvoke plat_dl_open .dll→.so/.dylib 映射、rng getrandom Android 守卫、dl_open Apple dylib 候选、get_special_folder DirSep、thread ID 收敛 plat_thread_current_id）"）
- [BASH] git show d1cfbfd --stat（6 文件：decimal.cpp/enumext.cpp/io.cpp/test_decimal.cpp/test_marshalext4.cpp/test_pinvoke_utf16.cpp）
- [BASH] git show d1cfbfd -- src/runtime/src/io.cpp（`std::abort()` 兜底补 -Wreturn-type）
- [READ] %TEMP%\opencode\m8s2-b23-report.md（WSL mylib.so G1 PASS；135 全量 run 132→135 修复路径；platform_posix.cpp:342-363 映射）
- [READ] %TEMP%\opencode\m8s1-b23-report.md（NDK S1：121/121 编译 0 错误 1 警告→已修；135/135 链接 DT_NEEDED；G2 pin-before-fix PASS；io.cpp:783 警告→d1cfbfd 修复）
- [BASH] Get-ChildItem tests\runtime -Filter test_*.cpp → **135**（原计划 129 已过时）
- [BASH] Test-Path .github\workflows → **False**（无 CI yml）
- [READ] src/runtime/src/io.cpp:784-786（abort 兜底现行）；src/runtime/src/pathproc.cpp（无 `__ANDROID__`——G7 未修）；src/runtime/src/thread.cpp:9,24,90 / monitor.cpp:49,191,195（plat_thread_current_id 收敛）
- [BASH] git rev-parse HEAD → 8838aba（本任务未 commit、未改码；仅更新本计划文件）

---

## 7. 剩余实机依赖清单（未执行项 × 验收标准 × 依赖资源）

> 全部剩余项均为**外部资源依赖**（实机/runner/VM/adb 许可环境）。执行顺序建议：macOS 与 OpenBSD 资源并行度最高；Android 受 adb 铁律约束，需外部许可环境或规则解禁。

### A. macOS runner 验证项（依赖：GitHub Actions `macos-13`(x86_64)/`macos-14`(arm64) runner 或自有 Mac + Xcode CLT；osxcross 为降级路径）

| 项 | 验收标准 | 依赖资源 |
|---|---|---|
| S1-V1 真实 SDK 全量编译 121 文件 | 121/121 PASS，0 错误；`-Wall -Wextra` 零警告（复验；告警源已修） | macOS x86_64 + Xcode CLT |
| S1-V3/V4 链接静态库 + 135 test exe | 静态库 + 135 exe 链接成功；thread_local 在真实 clang 链接通过 | 同上 + cmake |
| S1-V5 arm64 重复 V1-V4 | arm64 全量编译链接零错零警 | `macos-14`（arm64）runner |
| S1-V6 链接 1 个 e2e 生成物 | "生成代码 + runtime"整链 exe 可执行 | 同上 + tests/cases 任一生成物 |
| S2-R1 冒烟 test_object/test_gc | EXIT 0 | macOS runner |
| S2-R2 线程 ID pthread_threadid_np | test_thread/monitor/pathproc 线程 ID 非 0/负/哈希 | 同上 |
| S2-R3 pinvoke .dylib dlopen | DllImport("mylib.dll") 命中 libmylib.dylib（G1+G3 已接线） | 同上 |
| S2-R4~R8 rng/socket/console/timezone/environment | 各 test_*.exe 断言 PASS；R6 无 TTY 可豁免记录 | 同上 |
| S3-T1/T2/T3 全测试 | 135/135 PASS；e2e 全量（先定 diff 基线策略 R2）；C API PASS | macOS runner + 基线策略决策 |

### B. OpenBSD VM 验证项（依赖：qemu/vmm 跑 OpenBSD 7.x amd64，2c/4G 最低、S3 建议 4c；或第三方 BSD runner）

| 项 | 验收标准 | 依赖资源 |
|---|---|---|
| S1-V1 真实 sysroot 编译 121 文件 | 121/121 PASS，零错零警（clang 或系统 GCC） | OpenBSD 7.5+ 环境 |
| S1-V3/V4 链接静态库 + 135 exe + 1 e2e 生成物 | 链接产物齐全（libc 内含 pthread/dl，无需 -pthread/-ldl） | 同上 |
| S2-R1 冒烟 | test_object/test_gc EXIT 0 | VM |
| S2-R2 线程 ID getthrid | 非 0/负 | 同上 |
| S2-R3 pinvoke libmylib.so | G1 映射主路径命中 | 同上 |
| S2-R4~R6 rng(getentropy)/socket(特征测试兜底)/timezone/termios | 断言 PASS | 同上 |
| S3-T1/T2/T3 全测试 | 135/135 + e2e（diff 基线同上）+ C API | VM 4c |

### C. Android 实机 adb 冒烟（依赖：arm64-v8a 实机 + **adb 许可环境**——当前环境禁 adb 铁律（HANDOFF.md:61），需外部环境或明确解禁；x86_64 模拟器可作冒烟降级但仍需 adb 许可）

| 项 | 验收标准 | 依赖资源 |
|---|---|---|
| S2-R1 adb push + 冒烟 | test_object/test_gc 于 /data/local/tmp EXIT 0 | 实机/模拟器 + adb 许可环境 |
| S2-R2 tmpdir（G7 未修） | 预期失败 + 记录为已知缺口（pathproc 无 `__ANDROID__` 分支）；若已补分支则断言 /data/local/tmp | 同上 |
| S2-R3 线程 ID SYS_gettid | test_thread 断言 PASS | 同上 |
| S2-R4 socket | INTERNET 权限用例 PASS 或受限环境豁免记录 | 同上 + 权限配置 |
| S2-R5 pinvoke libmylib.so | G1 在 Android 的 .so 映射命中 | 同上 |
| S3-T1/T2/T3 | 135 exe 子集/全量 PASS（按域分批）；e2e 冒烟子集；C API | 同上（实机较慢） |

### D. 非实机补齐项（实施批次，不阻塞实机依赖）

| 项 | 验收标准 | 依赖资源 |
|---|---|---|
| scripts/run-ndk.ps1 落地（design 批 2 规划，现仍不存在；S1 已用 %TEMP% 探针脚本执行验证） | 脚本可复现 NDK S1（121/121 compile + 135/135 link） | 无（NDK 已有） |
| scripts/run-tests-posix.ps1（全 135 exe runner，m8s2 已验证临时脚本形态） | 在任一 POSIX 环境一键跑全量 | 无（随 S2/S3 使用） |
| run-tests.ps1 补 WSL mylib.so 构建分支 | e2e pinvoke 在 WSL 走 dlopen .so 路径（探针已验证，未合入脚本） | 无（WSL 已有） |
| e2e POSIX diff 基线策略（快照比对/自持断言） | S3-T2 前置决策 | 决策（实施批次） |

### E. Windows 回归门（不新增）

| 项 | 验收标准 | 依赖资源 |
|---|---|---|
| ci.ps1 全量（msvc/mingw/wsl） | 每次平台批次合入后回归绿（当前基线：runtime 135/135 + 4 平台 0 警） | 无（已有） |
