# M8 全量交叉编译矩阵 — Apple x86_64 / OpenBSD x86_64（只读验证）

- 日期：2026-08-09
- 分支：m1；HEAD=6b8bb02（验证未改动）
- 范围：`src/runtime/src/*.cpp` 全量 **121 文件** × 2 目标 = **242 次语法编译**
- 工具链：clang 17.0.6（ming-w64）`-fsyntax-only -std=c++17` + 合成 libc sysroot（`%TEMP%\opencode\m8ff-sysroot\`，因前次探通的 m8p-stubs 已被清空而重建）
- 方式：`clang++ -target <triple> --sysroot=<sysroot> -I src/runtime/include/internal`，仅验证不改码、不 commit

## 0. 结论摘要

**121/121 文件在 x86_64-apple-darwin（macOS 10.12 基线）与 x86_64-unknown-openbsd 两目标上 `-fsyntax-only` 全部 PASS，0 编译错误。** 全量矩阵见 §2。仅 1 文件（enumext.cpp）在 `-Wall -Wextra` 下报 2 条 sign-compare 警告（两目标一致，性质见 §3）。

## 1. 方法

1. 重建合成 libc sysroot（`usr/include`）——覆盖 C 标准头 + POSIX 头 + Apple/OpenBSD 特有 API（`pthread_threadid_np`/`getthrid`/`getentropy`/`arc4random_buf`/`xlocale.h`/`sys/endian.h`/`pthread_np.h` 等）；MinGW 自带 libc++（c++/v1）整体复制入 sysroot（OpenBSD 目标的 clang 驱动会在 sysroot 内找 libc++）。
2. **Apple 目标需 `-mmacosx-version-min=10.12`**：clang 驱动对 `x86_64-apple-darwin` 默认映射到 macOS 10.4，而 macOS 10.4 不支持 TLS（`thread_local`），`pinvoke.cpp:59,159` 与 `threadpool.cpp:90` 的真实 `thread_local` 用法会报 "thread-local storage is not supported"。10.12 与跨平台设计文档 §3 的 clock_gettime 基线（"macOS 10.12+/iOS 10+"）一致。
3. 分批执行（每批 timeout ≤540000ms），逐文件记录错误数；全量复核一次（121×2）确认 0 错误。

## 2. 全量矩阵（121 文件 × 2 目标）

| 文件 | Apple x86_64 | OpenBSD x86_64 |
|---|---|---|
| aes.cpp | PASS | PASS |
| allocator.cpp | PASS | PASS |
| api.cpp | PASS | PASS |
| array.cpp | PASS | PASS |
| arrayext.cpp | PASS | PASS |
| arrayext2.cpp | PASS | PASS |
| arrayextra.cpp | PASS | PASS |
| arraylist.cpp | PASS | PASS |
| arraysort.cpp | PASS | PASS |
| attribute.cpp | PASS | PASS |
| binaryio.cpp | PASS | PASS |
| bitconverter.cpp | PASS | PASS |
| boxing.cpp | PASS | PASS |
| boxingext.cpp | PASS | PASS |
| boxingext2.cpp | PASS | PASS |
| buffer.cpp | PASS | PASS |
| com.cpp | PASS | PASS |
| concat.cpp | PASS | PASS |
| console.cpp | PASS | PASS |
| conv.cpp | PASS | PASS |
| convert.cpp | PASS | PASS |
| convertbase64.cpp | PASS | PASS |
| crypto_obj.cpp | PASS | PASS |
| cultureinfo.cpp | PASS | PASS |
| debugext.cpp | PASS | PASS |
| debuggerext.cpp | PASS | PASS |
| decimal_format.cpp | PASS | PASS |
| decimal.cpp | PASS | PASS |
| delegate.cpp | PASS | PASS |
| delegateext.cpp | PASS | PASS |
| dependhandle.cpp | PASS | PASS |
| dictsi.cpp | PASS | PASS |
| encoding.cpp | PASS | PASS |
| enumext.cpp | PASS* | PASS* |
| environment.cpp | PASS | PASS |
| exception.cpp | PASS | PASS |
| exceptionext.cpp | PASS | PASS |
| format.cpp | PASS | PASS |
| formatext.cpp | PASS | PASS |
| framestack.cpp | PASS | PASS |
| gc.cpp | PASS | PASS |
| gcapi.cpp | PASS | PASS |
| gcext.cpp | PASS | PASS |
| gchandle.cpp | PASS | PASS |
| guid.cpp | PASS | PASS |
| hashtable.cpp | PASS | PASS |
| hmac.cpp | PASS | PASS |
| http.cpp | PASS | PASS |
| icallb.cpp | PASS | PASS |
| interlocked.cpp | PASS | PASS |
| intptr.cpp | PASS | PASS |
| invoker.cpp | PASS | PASS |
| io.cpp | PASS | PASS |
| ipaddr.cpp | PASS | PASS |
| linq_map.cpp | PASS | PASS |
| linq_sort.cpp | PASS | PASS |
| linq.cpp | PASS | PASS |
| listi4.cpp | PASS | PASS |
| marshalext.cpp | PASS | PASS |
| marshalext2.cpp | PASS | PASS |
| marshalext3.cpp | PASS | PASS |
| marshalext4.cpp | PASS | PASS |
| marshalext5.cpp | PASS | PASS |
| math.cpp | PASS | PASS |
| mathext.cpp | PASS | PASS |
| mathf.cpp | PASS | PASS |
| md5.cpp | PASS | PASS |
| metadata.cpp | PASS | PASS |
| monitor.cpp | PASS | PASS |
| numberformat.cpp | PASS | PASS |
| object.cpp | PASS | PASS |
| parse.cpp | PASS | PASS |
| pathproc.cpp | PASS | PASS |
| pinvoke.cpp | PASS | PASS |
| pinvokecb.cpp | PASS | PASS |
| pinvokeext.cpp | PASS | PASS |
| pinvokestruct.cpp | PASS | PASS |
| platform_posix.cpp | PASS | PASS |
| platform_win.cpp | PASS | PASS |
| random.cpp | PASS | PASS |
| reflect_base.cpp | PASS | PASS |
| reflecthandle.cpp | PASS | PASS |
| regex_api.cpp | PASS | PASS |
| regex_match.cpp | PASS | PASS |
| regex_parse.cpp | PASS | PASS |
| resources.cpp | PASS | PASS |
| rng.cpp | PASS | PASS |
| runtime.cpp | PASS | PASS |
| safehandle.cpp | PASS | PASS |
| sha1.cpp | PASS | PASS |
| sha256.cpp | PASS | PASS |
| sha512.cpp | PASS | PASS |
| socket.cpp | PASS | PASS |
| sortedlist.cpp | PASS | PASS |
| stackqueue.cpp | PASS | PASS |
| stopwatch.cpp | PASS | PASS |
| streamio.cpp | PASS | PASS |
| string.cpp | PASS | PASS |
| stringbuilder.cpp | PASS | PASS |
| stringext.cpp | PASS | PASS |
| stringext2.cpp | PASS | PASS |
| stringext3.cpp | PASS | PASS |
| stringintern.cpp | PASS | PASS |
| stringrw.cpp | PASS | PASS |
| thread.cpp | PASS | PASS |
| threadext.cpp | PASS | PASS |
| threadobj.cpp | PASS | PASS |
| threadpool.cpp | PASS | PASS |
| time.cpp | PASS | PASS |
| timeext.cpp | PASS | PASS |
| timeext2.cpp | PASS | PASS |
| timer.cpp | PASS | PASS |
| timezone.cpp | PASS | PASS |
| typecheck.cpp | PASS | PASS |
| typeext.cpp | PASS | PASS |
| typeinfo.cpp | PASS | PASS |
| valuesemantics.cpp | PASS | PASS |
| versionuri.cpp | PASS | PASS |
| xml_doc.cpp | PASS | PASS |
| xml_parse.cpp | PASS | PASS |
| xml_query.cpp | PASS | PASS |

**合计：121/121 PASS（Apple）/ 121/121 PASS（OpenBSD）；0 文件 FAIL。** `PASS*` = 编译通过，但有 `-Wall -Wextra` 警告（见 §3）。

## 3. 警告清单（`-Wall -Wextra`，两目标一致）

| 文件:行 | 警告 | 性质 |
|---|---|---|
| enumext.cpp:244 | `comparison of integers of different signs: 'int32_t' (aka 'int') and 'unsigned int' [-Wsign-compare]` | `value > UINT8_MAX` 中 `UINT8_MAX` 在跨目标 clang 资源目录 stdint.h 下展开为 `unsigned`（原生 MinGW 下为 `int`，故原生构建零警告）——值 255 可安全落入 int，**无行为影响** |
| enumext.cpp:256 | 同上（`value > UINT16_MAX`） | 同上 |

说明：其余 10 文件的 `-Winvalid-noreturn`/`-Wreturn-type` 警告在给 sysroot 的 `abort()/exit()/_Exit()` 补上 `__attribute__((noreturn))` 后全部消失——属探针 stub 不完整，非业务代码问题。

## 4. 平台分支验证要点（本次覆盖的关键路径）

- `platform_posix.cpp`：`__APPLE__`→`pthread_threadid_np`、`__OpenBSD__`→`getthrid`（pthread_np.h）、`__linux__`→`syscall(SYS_gettid)` 全分支语法通过
- `rng.cpp`：`arc4random_buf`（Apple/BSD 族）与 `sys/random.h getrandom`（Linux）分支通过
- `socket.cpp`/`ipaddr.cpp`：BSD socket + `SO_NOSIGPIPE`（Apple）分支通过
- `threadpool.cpp:90`/`pinvoke.cpp:59,159`：`thread_local` 需 macOS ≥10.7（本项目基线 10.12，OK）
- `console.cpp`：termios + `dup2` 分支通过
- `encoding.cpp`：无 iconv 自实现 UTF-8↔UTF-16（Apple wchar_t 32 位安全）通过
- `platform_win.cpp`：在 POSIX 目标下为空 TU（`#if !defined(_WIN32)` 守卫），同样 PASS

## 5. 证据索引

- [BASH] `clang++ -target x86_64-apple-darwin --sysroot=%TEMP%\opencode\m8ff-sysroot -mmacosx-version-min=10.12 -fsyntax-only -std=c++17 -I src/runtime/include/internal <file>` → 121 文件 × 全部 EXIT 0
- [BASH] `clang++ -target x86_64-unknown-openbsd --sysroot=<同上> -fsyntax-only -std=c++17 -I ... <file>` → 121 文件 × 全部 EXIT 0
- [BASH] 全量复核日志 `%TEMP%\opencode\m8ff-fullmatrix.txt`（121 行 `PASS apple=0 openbsd=0`）
- [BASH] `-Wall -Wextra` 全量扫描 → 仅 enumext.cpp 2 警告（两目标一致）
- [BASH] `git rev-parse HEAD` → 6b8bb02（本任务未 commit、未改码）
- [READ] docs/design/cross-platform-design.md §3/§5（macOS 10.12 基线、OpenBSD 验证手段）

## 6. 后续建议（超出本只读任务范围，未执行）

- 修复 enumext.cpp:244,256 的 sign-compare（`uint32_t`/显式 `255U` 比较）以达成跨目标 `-Wall -Wextra` 零警告
- Apple arm64（`arm64-apple-darwin`）与 Android（NDK arm64-v8a）目标可复用同一 sysroot 补充验证（前次探通已含 arm64，本次任务范围仅为 x86_64 两目标）
- 真机验证仍按设计文档 §5.4 依赖 CI 容器/模拟器（本环境无 Apple/OpenBSD 实机）
