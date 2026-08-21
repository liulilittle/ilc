# Android NDK 探路报告 — 批2 交叉编译可行性

- 日期：2026-08-04
- 任务：只读探测 + 报告，零代码修改，零 git 提交，未运行 adb
- 目标：验证本机 Android NDK 能否交叉编译 `src/runtime` 的纯 POSIX 文件（arm64-v8a）

## 1. 环境状态

### NDK（D:\android\sdk\ndk，3 个版本已安装）

| 版本 | 目录 | 备注 |
|---|---|---|
| **26.1.10909125** | `D:\android\sdk\ndk\26.1.10909125` | 最新，选用本次探测 |
| 21.1.6352462 | `D:\android\sdk\ndk\21.1.6352462` | 备用 |
| 20.1.5948944 | `D:\android\sdk\ndk\20.1.5948944` | 备用 |

- 环境变量 `ANDROID_NDK_HOME` / `ANDROID_HOME` / `ANDROID_SDK_ROOT` / `NDK_HOME`：**全部为空** → 工具链需显式路径引用（或后续设置）。
- SDK 邻件齐备：`D:\android\sdk\platforms`（android-21/22/23/24/30/33/34/35）、`D:\android\sdk\cmake\3.22.1`、`D:\android\sdk\build-tools`、`D:\android\sdk\platform-tools`（**未使用 adb**）。
- 主机 cmake 亦存在：`C:\Program Files\CMake\bin\cmake.exe`（系统版，版本未验；SDK 版 3.22.1 已验证可用）。

### 工具链路径（NDK 26.1.10909125）

| 项 | 路径 | 存在 |
|---|---|---|
| CMake toolchain | `D:\android\sdk\ndk\26.1.10909125\build\cmake\android.toolchain.cmake` | ✔ |
| ndk-build | `D:\android\sdk\ndk\26.1.10909125\ndk-build.cmd` | ✔ |
| clang 驱动 | `...\toolchains\llvm\prebuilt\windows-x86_64\bin\clang++.exe` | ✔ |
| aarch64 clang 包装 | `...\bin\aarch64-linux-android21-clang++.cmd`（`.cmd` 批处理，非 `.exe`） | ✔ |
| aarch64 全系 | `aarch64-linux-android{21..34}-clang++.cmd`；x86_64/i686/armv7a 同构 | ✔ |
| sysroot | `...\sysroot\usr\include`（unistd.h / sys/utsname.h / sched.h / sys/syscall.h / dlfcn.h / termios.h 全在） | ✔ |

## 2. 交叉编译结果（两条路径全部打通）

### 2.1 直接 clang 单文件编译（clang++.exe --target=aarch64-linux-android21）

```
clang++.exe --target=aarch64-linux-android21 -std=c++17 -O2 \
  -I E:\il2cpp\src\runtime\include -I E:\il2cpp\src\runtime\include\internal \
  -c src/runtime/src/<file>.cpp -o <file>.o
```

| 文件 | 结果 |
|---|---|
| stopwatch.cpp | ✔ EXIT=0（纯 `<chrono>` + `#if defined(_WIN32)`/`#else` POSIX 分支） |
| interlocked.cpp | ✔ EXIT=0（POSIX `__atomic` 内建路径） |
| monitor.cpp | ✔ EXIT=0（pthread/syscall POSIX 分支） |

产物核实：`llvm-readelf -h` → **ELF64 / little endian / Machine: AArch64**。

### 2.2 端到端链接（main.cpp + stopwatch.o + interlocked.o）

- **LINK EXIT=0**，产出 `ndkprobe`：ELF64 AArch64 **DYN（PIE）**，interp=`/system/bin/linker64`，NEEDED=`libc++.so / libm.so / libdl.so / libc.so`，GNU_STACK RW（无 NX 需求）。
- 动态符号：`printf@LIBC`、`__cxa_atexit@LIBC` 均解析（无缺失符号）。

### 2.3 CMake 交叉编译（android.toolchain.cmake）

```
cmake -S <tmp> -B <tmp>/build \
  -DCMAKE_TOOLCHAIN_FILE=D:\android\sdk\ndk\26.1.10909125\build\cmake\android.toolchain.cmake \
  -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-21 -DCMAKE_BUILD_TYPE=Release
```

- **CONFIGURE EXIT=0**（3.22.1 SDK cmake，ninja generator）
- **BUILD EXIT=0**：[4/4] 全过，产出 `build\ndkprobe`（362,280 B，ELF64 AArch64 DYN）
- CMakeLists 直接引用仓库文件 `E:/il2cpp/src/runtime/src/stopwatch.cpp` 与 `interlocked.cpp`，零仓库改动。

### 2.4 记录的错误（非 Bionic 缺口）

1. **monitor.cpp 链接缺符号**（`il2cpp_throw_argument_null_exception` / `il2cpp_throw_invalid_operation_exception` / `il2cpp_throw_new`）——monitor.cpp 跨文件依赖 exception.cpp/exceptionext.cpp 的异常函数，**非平台问题**，链接需纳入整个 runtime 库（`src/runtime/CMakeLists.txt` GLOB 已含）。编译本身无错。

## 3. Android 特有缺口（Bionic libc）

| 项 | 证据 | 影响 |
|---|---|---|
| **iconv 缺失** | `libc.so`（API 21）`llvm-nm -D` 中 `iconv_open/iconv_close` **零符号**；`iconv.h` 头文件在 sysroot 但无实现符号 | `encoding.cpp:7-8` 已注明"hand-written UTF-8↔UTF-16, no iconv dependency (Android's Bionic libc ships no iconv)"——**现状实现恰好规避此缺口，无 action** |
| gai_strerror / strsignal | `libc.so` 已导出 | 无缺口 |
| POSIX 头 | unistd.h / sys/utsname.h / sched.h / sys/syscall.h / dlfcn.h / termios.h 全部存在 | 无缺口 |
| Windows-only 头 | conio.h（console.cpp:31）/ direct.h（io.cpp:56）/ objbase.h（guid.cpp:23）均在 `#if defined(_WIN32)` 守卫内 | 交叉编译不触发 |

## 4. 结论

1. **NDK 轴可用**：NDK 26.1 完整安装；直接 clang 与 CMake toolchain 两条交叉编译路径在 `-std=c++17` 下对 `src/runtime` 纯 POSIX 文件全部零错误。
2. **文件级兼容好**：interlocked / stopwatch / monitor 等已具 `#if defined(_WIN32)`/`#else` 双分支，Bionic 下无编译错。
3. **无 Bionic 大坑**：本项目唯一潜在的 iconv 缺口已被 encoding.cpp 手写编解码规避（该文件恰好是 WSL 轴唯一全 Windows 硬阻塞，Android 轴反而无此问题）。
4. **集成路径**：`src/runtime/CMakeLists.txt`（GLOB + STATIC，`-fexceptions -fno-rtti`）可直接作为 `add_subdirectory` 纳入 Android 构建；需注意 monitor 等文件的异常函数跨文件依赖（整库编译即解）。

## 5. 批2 任务卡建议

1. **设置环境变量**（一次性）：`ANDROID_NDK_HOME=D:\android\sdk\ndk\26.1.10909125`（当前为空，工具链引用靠显式路径）。
2. **集成 CMake**：新建 `android/`（或 `build-android/`）CMake 项目，`add_subdirectory(E:/il2cpp/src/runtime)` 整库编译；toolchain 文件 + `-DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-21`。
3. **全量编译验证**：首次全库 86 个 .cpp 编译，收集错误清单（预期面：Bionic 与 glibc 差异、`std::` 传递包含差异，同 WSL 轴模式）。
4. **禁止 adb**：本任务仅编译（产生 .so/.a），部署验证是设备环节（域外）。
