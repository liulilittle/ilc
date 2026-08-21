# invoker / decimal / TimeZone 域落地核查（2026-08-05 23:1x）

> 验证任务（30 分钟）：glob 核查 invoker.h/invoker.cpp/decimal.h/decimal.cpp/timezone.h
> 存在性 → cmake 构建对应测试 → 运行。产出本核查记录。
> 方法：glob 目标文件 + mtime/进程活性 + cmake 构建尝试 + 独立 clang 全量重建回退 + 测试运行。

## 结论速览

| 域 | 实现文件 | 测试文件 | 构建 | 运行 | 状态 |
|---|---|---|---|---|---|
| invoker | invoker.h + invoker.cpp | test_invoker.cpp | OK | **PASS (exit=0)** | **已落地** |
| decimal | decimal.h + decimal_format.h/cpp | test_decimal_format.cpp | OK | **PASS (exit=0)** | **已落地**（命名偏差见下） |
| TimeZone | timezone.h + timezone.cpp | test_timezone.cpp | OK | **PASS (exit=0)** | **已落地** |

三域全部落地且独立构建+运行全绿。**cmake 路径仍被并行代理 crypto_obj.cpp 阻塞**（见 §5），
本核查走既有回退路径（排除 crypto_obj 的全量 clang 独立重建）。

## 1. 目标文件存在性（glob 证据）

| # | 目标 | 期望路径 | 结果 |
|---|---|---|---|
| 1 | invoker.h | src/runtime/include/internal/invoker.h | **存在**（5046 B，15:56:41） |
| 2 | invoker.cpp | src/runtime/src/invoker.cpp | **存在**（11387 B，17:40:49） |
| 3 | decimal.h | src/runtime/include/internal/decimal.h | **存在**（6053 B，16:32:18） |
| 4 | decimal.cpp | src/runtime/src/decimal.cpp | **缺失**——实现以 **decimal_format.h/cpp** 落地（16049 B，20:34:18） |
| 5 | timezone.h | src/runtime/include/internal/timezone.h | **存在**（3036 B，17:02:11） |
| 6 | timezone.cpp | src/runtime/src/timezone.cpp | **存在**（6068 B，17:21:27） |

- decimal 域**命名偏差**：任务期望 `decimal.cpp`，实际为 `decimal_format.cpp`。与 T2 会话
  「契约先行」策略一致（decimal_format.h 自含 Il2CppDecimal 定义），且 git 未跟踪（代理未提交）。
- 测试文件齐备：test_invoker.cpp（12.5 KB，16:42:58）、test_timezone.cpp（11 KB，17:40:02）、
  test_decimal_format.cpp（15 KB，21:18:17）。
- 活性：核查时仅 1 个 dotnet 进程（23:07 起，VBCSCompiler 类）；无 cmake/make/clang 构建进程。
  三域源码 mtime 距核查（23:1x）均 >30 分钟，判定代理已收工。

## 2. cmake 构建尝试（任务主路径）

- 目标构建目录 = `build/tests_runtime`（MinGW Makefiles，`C:/Program Files/ming-w64/bin/g++.exe`，
  实际为 clang 17.0.6，libc++；configure 16:24:33）。
- `cmake --build build/tests_runtime --target test_invoker test_timezone test_decimal_format -- -j8`
  → **失败**：编译 `crypto_obj.cpp` 时 libc++ 头文件报 `no template named 'integral_constant'`
  （is_function/is_base_of/is_class/is_convertible/is_copy_constructible 等 20 个错误）。
  `runtime/CMakeFiles/il2cpp_runtime.dir/src/crypto_obj.cpp.obj` Error 1。
- 该文件此前 17:44 的 `il2cpp::Il2CppSha512Ctx` 命名空间错已被代理修掉（20:29:48 后再 grep
  为裸 `Il2CppSha512Ctx`），但**换成了另一类 libc++ 头文件污染**（memory 已录「libc++ ldiv」同族）。

## 3. 回退路径：排除 crypto_obj 的全量独立重建（既有惯例）

- 编译 `src/runtime/src/*.cpp` 104 源（排除 crypto_obj.cpp）→ **0 编译错误，0 警告**
  （clang++ -std=c++17 -fno-rtti -fexceptions -Wall -Wextra，include 头 = PUBLIC 契约）。
- `llvm-ar rcs libil2cpp_runtime.a` → exit 0。
- 链接 3 测试（同一 include/编译契约）→ test_invoker / test_timezone / test_decimal_format 全 OK。

## 4. 测试运行结果（fresh 全量库）

| 测试 | 输出 | exit |
|---|---|---|
| test_invoker | `all runtime invoker tests passed` | 0 |
| test_timezone | `test_timezone: all checks passed` | 0 |
| test_decimal_format | `all decimal format tests passed` | 0 |

全部 exit=0。产物：`%TEMP%\opencode\domainaudit\{libil2cpp_runtime.a,test_invoker.exe,
test_timezone.exe,test_decimal_format.exe,compile.log,link.log}`。

## 5. 阻塞项（记录，不修他人文件）

- **crypto_obj.cpp（并行代理）仍使 cmake 全量构建失败**：libc++ 头文件污染（integral_constant
  系列）。memory 已录 17:44 命名空间错 +「libc++ ldiv」同族，现为第三变体。修复属 crypto 域
  代理；三域验证不受影响（独立重建已排除该文件）。
- 三域文件全部 **git 未跟踪**（`??` 状态）——代理批次未提交，待批次统一提交后重跑官方验证。

## 6. 建议后续（非本任务范围）

- crypto_obj.cpp 修复后：重跑 `cmake --build build/tests_runtime --target test_invoker test_timezone
  test_decimal_format`（CONFIGURE_DEPENDS 自动纳入新源/新测试），建立官方路径全绿基线。
- decimal.cpp 命名：确认任务卡是否接受 decimal_format.cpp 命名，或补 decimal.cpp 转发。
