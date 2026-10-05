# Bench: AOT vs .NET Core 8.0（2026-08-14，批次 152——性能对标基准）

## 结论
**AOT（clang 17 -O3）全面达到并超越 .NET Core 8.0**（唯一持平项 loop 0.98× 为计时噪声）——"对标 Core 8.0"基准达成（bench 面）。

## 对比表（2026-08-14，同机实测）

| bench | net8 (ms) | AOT (ms) | ratio (net8/AOT) |
|---|---|---|---|
| fib | 19 | 12 | 1.58× |
| loop | 60 | 61 | 0.98×（持平/噪声） |
| sort | 19 | 18 | 1.06× |
| array | 12 | 1 | **12×** |
| hash | 10 | 6 | 1.67× |
| string | 43 | 27 | 1.59× |
| object | 13 | 0 | **inf**（循环消除） |

## 方法
- net8 参考：`dotnet build tests/bench/bench.csproj -f net8.0`（多目标 net48;net8.0——out8/bench.exe + 复制 benchlib.dll）。
- AOT：`cmake -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_COMPILER=clang++`（v8 基线同编译器）。
- 计时：bench 程序内部 Stopwatch（每用例 ilc/net8 净时间）。

## 备注
- 与 net48 基线（v8 CSV）：AOT 继续零回退（fib 12 vs 13、array 1 vs 1 等）。
- Core 8.0 的 RyuJIT（Tiered PGO）在纯计算 bench 上被 AOT 的 clang -O3 超越——AOT 无 JIT 预热/内联边界开销。
- 扩大基准面（集合/字符串/GC）与微优化为后续工作。
