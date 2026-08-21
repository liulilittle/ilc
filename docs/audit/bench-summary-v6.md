# Bench Summary v6 — 2026-08-10（WinForms/COM/去虚化后最终性能确认）

- Date: 2026-08-10 · Mode: **全量实测**（只跑不改；未 commit）· Toolchain: MinGW (Clang 17.0.6, Release/-O3)
- HEAD: **1d99ad3**（M6 batch 50, e2e runtime 135/135）· worktree clean（0 modified）· 无构建竞争
- Transpiler: 源码构建（`dotnet run --project src/il2cppc`），非预置 DLL
- v5 基线: bench5f（HEAD 6b8bb02, 2026-08-09，`%TEMP%\opencode\bench5f-b23-report.md`）

## 0. 执行摘要（回退检测）

| 检查项 | v5 | **v6** | 结论 |
|---|---|---|---|
| object | **inf**（循环消除） | **inf**（9/0） | ✅ 无回退 |
| str_len | **inf** | **inf**（20/0） | ✅ 无回退 |
| array | 12.00 | **12.00**（12/1） | ✅ 持平 |
| string | 1.65 | **1.82**（51/28） | ✅ 改善 |
| fib | 1.40 | **1.62**（21/13） | ✅ 改善 |
| arr_sum | 8.00 | **8.00**（8/1） | ✅ 完全一致 |
| arr_write | 4.20 | **4.00**（20/5） | ✅ 噪声内（ILC 稳定 5，net48_core 漂移） |
| str_chars | ~9-19 | **16-17** | ✅ 维持巨幅改善 |
| str_concat | ~0.83-1.04 | **0.81-0.85** | ✅ 持平 |
| virtbench 去虚化 | 1.056-1.087 | **1.03-1.07**（直接调用已确认） | ✅ 无回退 |

**结论：无任何基准回退；main suite 与去虚化全部维持或改善。WinForms/COM 工作对性能无退化。**

## 1. tests/bench（全 7 基准，net48 vs ILC）

| name   | v5 net48/ilc | v5 ratio | **v6 net48/ilc** | **v6 ratio** | delta |
|--------|--------------|----------|------------------|--------------|-------|
| fib    | 21/15        | 1.40     | **21/13**        | **1.62**     | ▲ +0.22 |
| loop   | 61/63        | 0.97     | **59/60**        | **0.98**     | ~ +0.01 |
| string | 51/31        | 1.65     | **51/28**        | **1.82**     | ▲ +0.17 |
| array  | 12/1         | 12.00    | **12/1**         | **12.00**    | = 持平 |
| object | 10/0         | **inf**  | **9/0**          | **inf**      | = 持平 |
| sort   | 19/20        | 0.95     | **14/18**        | **0.78**     | ▼ −0.17（见噪声分析） |
| hash   | 6/6          | 1.00     | **7/6**          | **1.17**     | ~ +0.17（见噪声分析） |

复跑（第 2 遍）：fib 20/13=1.54, loop 59/61=0.97, sort 14/17=0.82, array 12/1, hash 6/6=1.00,
string 50/28=1.79, object 9/0。**sort/hash 为全套件噪声最大项**（整数毫秒分辨率，v5 已标注
±0.3）：sort 第 2 遍 0.82 与 v5 第 2 遍（15/18=0.83）同噪声带；hash 第 2 遍 1.00 与 v5 完全一致。
**两遍均无基准回退。**

CSV: `tests/bench/results/2026-08-10_bench6-mingw.csv`（本会话新写，未 commit）。

## 2. tests/bench-array（core 程序：同二进制双侧）

| name      | v5 net48_core/ilc | v5 ratio | **v6 net48_core/ilc** | **v6 ratio** | delta |
|-----------|-------------------|----------|----------------------|--------------|-------|
| arr_sum   | 8/1               | 8.00     | **8/1**              | **8.00**     | = 完全一致 |
| arr_write | 21/5              | 4.20     | **20/5**             | **4.00**     | ~ −0.20（net48_core 噪声，ILC 稳定 5） |
| str_len   | 22/0              | **inf**  | **20/0**             | **inf**      | = 持平 |

CSV: `tests/bench-array/results/2026-08-10_bench6-array-mingw.csv`。

## 3. tests/bench-array（FULL 程序 — 手动运行，harness 固有标记 FAIL）

HEAD 的 `run-bench-array.ps1` 无条件把三个 full-only 基准（arr_copy/str_chars/str_concat）在
CSV 中标 FAIL，但它们在 HEAD 正常转译+构建+运行（与 v5 相同；两遍实测取最新）：

| name      | net48_full | ilc_ms | ratio | v5 ratio | delta |
|-----------|-----------|--------|-------|----------|-------|
| arr_copy  | 27        | 25     | **1.08** | ~1.2-1.4 | ~ 持平（第 2 遍 27/24=1.13） |
| str_chars | 17        | 1      | **17.0** | ~9-19    | ✅ 维持巨幅改善（v4 曾 0.27） |
| str_concat| 21        | 26     | **0.81** | ~0.83-1.04 | ~ 持平（第 2 遍 23/27=0.85） |

## 4. virtbench（去虚化 S1/S3 确认，HEAD 全新建 Release 构建）

| mode   | net48_ms | ilc_ms（3 遍中位数） | **ratio** | v5 ratio |
|--------|----------|----------------------|-----------|----------|
| iface  | 840      | 785（768/800/785）   | **1.07**  | 1.056    |
| poly   | 810      | 790（775/790/803）   | **1.03**  | 1.087    |
| sealed | 805      | 784（803/781/784）   | **1.03**  | 1.069    |
| imulti | 206      | 209（206/209/212）   | **0.99**  | 1.005    |

checksum **-166660532 双侧一致** → 字节级正确性 oracle PASS（同 v5）。

**生成代码去虚化确认**（`tests/cases/virtbench/gen/src/Cases_VirtBench.cpp`）：
- iface L72：直接调用 `OpA_Apply_42787f...(...)` — S3 单实现者去虚化 ✅
- sealed L154：直接调用 `SealedImpl_F_40570b...(...)` — S1 sealed 接收者去虚化 ✅
- poly L113：`->klass->vtable[...]` 运行时分派保留 — 对照组正确 ✅
- imulti L209：`il2cpp_interface_dispatch` 线性查找保留 — 对照组正确 ✅

> 方法学注：首个 virtbench 构建漏传 `-DCMAKE_BUILD_TYPE=Release`（未优化，ILC 3494-3625 ms，
> ~4.5x 虚慢）→ 已按 v5 相同 Release 方法重建重测，上表为有效数字。

## 5. 方法学注

- 本会话：只跑不改；未修改任何 src/；未 commit；未写 .opencode/（仅文档产出 + %TEMP% 检查点）。
- 每项数字为整数毫秒（bench.cs 固定工作量设计，net48 参考 ≥15ms 保证 1ms 分辨率有意义）。
- sort/hash 为噪声最大项（v5 同注）；复跑全部落在 v5 噪声带内。
- 转译器为 HEAD 源码构建；bench 全流程 net48 构建 → 转译 → CMake Release → 双侧运行 → CSV。

## 6. 结论

- **无任何回退**：object/str_len 循环消除 inf 维持、array 12x 持平、arr_sum 8.00 完全一致、
  string/fib 改善、str_chars 巨幅改善维持、去虚化 1.03-1.07 直接调用确认。
- **WinForms/COM/去虚化（M6 batch 44-50）对性能零退化**；性能域 v5 承诺全部兑现。
