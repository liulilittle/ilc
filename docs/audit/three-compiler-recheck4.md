# 三编译器矩阵 recheck4 — crypto_obj 修复 + 新域 4 目标双轴复验（2026-08-06）

- 任务：只报告（不修代码、不 commit）。MSVC + WSL 双轴构建/运行 7 目标 =
  test_decimal / test_invoker / test_timezone / test_reflect_base（新域）+
  test_crypto_obj / test_aes / test_hmac（crypto_obj 修复后对象层）。
- 基线：batch-13（2659d8c）crypto_obj 对象层落地 + matrix5（92/131，crypto 全族 PASS）；
  recheck3 时 in-flight 编辑导致 MSVC 构建失败，本次以稳定快照为准。

## 1. 状态表

| 轴 | 工具链 | 构建 | 警告数 | 运行 | 结论 |
|---|---|---|---|---|---|
| **MSVC** | VS2022 Enterprise 19.39.33523.0 / cmake 3.22.1（`-A x64`，`/W4 /EHsc /bigobj /utf-8`） | ✅ 7/7 目标，0 错误 | **8**（1 runtime + 7 test，全 C4996） | ✅ **7/7 exit=0** | **PASS** |
| **WSL** | Ubuntu1804 GCC 7.5.0 / cmake 3.10.2 / make 4.1（`-j32 -k`） | ❌ 共享库 1 处编译错误 | 0 | —（共享库构建阻断） | **BUILD FAIL** |

## 2. MSVC 明细（✅ 全绿）

- 配置：`cmake -S tests/runtime -B build/recheck4_msvc -G "Visual Studio 17 2022" -A x64` → OK。
- 构建：7 目标 + runtime 库 → **0 error**；产物 7 exe 全生成。
- 运行：7/7 exit=0，输出全 PASS：
  `all decimal arithmetic tests passed` / `all runtime invoker tests passed` /
  `test_timezone: all checks passed` / `all runtime reflect_base tests passed` /
  `all runtime crypto_obj tests passed` / `all runtime aes tests passed` /
  `all runtime hmac tests passed`（附 hmac_sha256(case1)=b0344c61…）。
- 警告 **8 处**（全部 C4996 弃用告警）：
  - runtime 库 ×1：`src/runtime/src/format.cpp:130` `strcpy`
  - 测试 ×7：`test_aes.cpp:59,240,242`（3）、`test_crypto_obj.cpp:80,428,430`（3）、
    `test_hmac.cpp:42`（1）——`sprintf`
  - test_decimal / test_invoker / test_timezone / test_reflect_base：**0 警告**。

## 3. WSL 明细（❌ 构建 FAIL，根因定位）

- 错误（唯一）：
  ```
  src/runtime/src/io.cpp:68:34: error: '::rmdir' has not been declared
      #define IL2CPP_IO_RMDIR(p) ::rmdir(p)
  src/runtime/src/io.cpp:631:12: note: in expansion of macro 'IL2CPP_IO_RMDIR'
      return IL2CPP_IO_RMDIR(p.c_str()) == 0 ? 0 : 1;   // icall_directory_delete
  ```
- 根因：io.cpp 非 Windows 分支（`#else`，io.cpp:64-69）只 include `<sys/stat.h>` /
  `<sys/types.h>`，**缺 `<unistd.h>`**（POSIX 声明 `rmdir` 的头）。GCC 7.5 编译失败。
  MSVC / MinGW 走 `_WIN32` 分支（io.cpp:57-63，`::_rmdir` + `<direct.h>`）不受影响。
- 性质：**batch-13（2659d8c）潜伏的 WSL 轴回归**。此前从未暴露——recheck2_wsl 为部分构建
  （runtime 库仅 21 个 .o，停在字母 'c' 段 convertbase64，io.cpp 未编译）；recheck3 仅 MSVC。
- 7 个 test TU 用 GCC 7.5 单独 `-fsyntax-only`（同 flags：`-Wall -Wextra -fexceptions
  -fno-rtti -std=gnu++1z`）验证：**全 0 错误 0 警告** → io.cpp 补 include 后预期可直接
  构建链接运行。
- 运行：不可达（共享库构建阻断），WSL 侧运行未验证。

## 4. 备注

- test_crypto_obj 源文件：工作区原无 `test_crypto_obj.cpp`，仅 `test_crypto_obj.cpp.inflight`
  （26472B，内容完整含 main，CMake GLOB `test_*.cpp` 不匹配 `.inflight`）。本复验将其复制为
  `test_crypto_obj.cpp` 后构建运行（MSVC 7/7 含 crypto_obj PASS）。该 .cpp 现为 untracked，
  若与在途 crypto_obj 代理冲突可删除（.inflight 仍为权威源）。

## 5. 三行结论

1. **MSVC 全绿**：7/7 目标构建 0 错误、7/7 运行 exit=0；仅 8 处 C4996 残留（1 runtime
   format.cpp:130 strcpy + 7 test sprintf），新域 4 目标 0 警告。
2. **WSL 构建 FAIL**：共享库 io.cpp:68/631 `::rmdir` 未声明（非 Windows 分支缺 `<unistd.h>`，
   batch-13 潜伏回归，recheck2 部分构建从未编译 io.cpp）；7 个 test TU 本身 WSL 全 0 错 0 警。
3. **修复路径单行**：io.cpp `#else` 分支补 `#include <unistd.h>` 即可解 WSL 全阻塞
   （报告期遵循"只报告"约束未改动代码）。

## 证据

- [BASH:cmake -S tests/runtime -B build/recheck4_msvc -G "Visual Studio 17 2022" -A x64] → configure OK，MSVC 19.39.33523.0
- [BASH:cmake --build recheck4_msvc --config Release --parallel 8 --target 7 目标] → 0 error；log 警告 8 处 unique
- [BASH:7 exe 运行] → 全 exit=0 + PASS 输出行
- [BASH:wsl make -j32 -k 7 目标] → error lines=7（unique 1 = io.cpp:68），warning=0
- [BASH:wsl g++ -fsyntax-only 7 TU] → 全 exit=0 errors=0 warnings=0
- [READ:io.cpp:57-69] 非 Windows 分支缺 <unistd.h>；[READ:io.cpp:631] IL2CPP_IO_RMDIR 使用点
- [BASH:git log -S IL2CPP_IO_RMDIR] → io.cpp 改动在 2659d8c（batch-13）
- [BASH:recheck2_wsl runtime .o count=21]（停在 'c' 段）→ io.cpp 从未在 WSL 编译
