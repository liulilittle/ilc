# 审计 B：D3 ABI 维度异方法独立复核报告

日期：2026-11-17 16:20
被审对象：`tools/audit/check_decl_symmetry.py`
角色：审计 B（异方法独立复核），只审计，不改实现、不改 `src/runtime/**` 最终状态。

---

## 0. 首要点：版本标识与主代理报告不一致（必须先解决）

| 时刻 | 长度 | sha256_16 | 来源 |
|---|---|---|---|
| 15:28:50 | 53589 | `19FBE9A5B40A832A` | 我开始审计时实测 [BASH: Get-FileHash + Length] |
| 16:12:29 | 56610 | `5414FC9818020DBD` | 审计中实测（**我未改动该文件**） |
| — | 52635 | `C030512BA3426DC4` | 主代理任务提示声称 |
| — | 52635 | — | 全盘搜索 `Length -eq 52635`：**0 个文件** [BASH: Get-ChildItem -Recurse Where Length -eq 52635] |

结论：**主代理报告中的 `C030512BA3426DC4` / 52635B 在本工作树中不存在**。该文件在审计期间被他人修改（53589→56610，行号从 782 漂移到 803、新增 `source_fail` 字段、`ABI-NOTE` 文案变更）。下文全部结论均针对**实测存在的 `5414FC9818020DBD` / 56610B**，并用不落盘副本 `repo2` 冻结复现。

### 0.1 同期还发现：他人正在共享工作树里实时改 `src/runtime`

用后台 watcher 逐 150ms 采样，实测：
```
CHANGED src\runtime\include\internal\dictgen.h at 15:59:41.798: 6E5A91DD66B8|02:20:46.660 -> 63EB6D0386AA|15:59:41.631
CHANGED src\runtime\include\internal\dictgen.h at 16:00:49.039: 63EB6D0386AA|15:59:41.631 -> 6E5A91DD66B8|02:20:46.660
```
这是我跑 run2 出现**单条假阳性**（`dictgen.h: il2cpp_dict_gen_kind_is_ref` mismatch）以及 run6 出现 `ABI-NOTE: source compile failed: dictgen.cpp`（definitions 9690→9598）的根因：**兄弟代理在共享树里做变异**。此后我全部改用只读副本 `C:\...\Temp\opencode\auditB\repo2` 进行受控实验，避免竞态污染。此点也证明：**在共享工作树上跑这个 60~90s 的检查器，结果不可作为裁决依据**。

---

## 1. 规则16 变异矩阵（每类必须红）—— 全部 PASS

统一方法：`Copy-Item` 备份 → 精确字符串替换（先断言目标唯一）→ 运行 → `Copy-Item` 写回 → sha16 复核。

| # | 变异 | 变异前 sha16 | 变异后 sha16 | 检出 | EXIT | 恢复 |
|---|---|---|---|---|---|---|
| a1 | `dictgen.h` 的 `box_double`+`unbox_double` 声明包进 `namespace wrong_ws {}` | `6E5A91DD66B8F058` | `01536452C7A247F8` | `mismatch=2` | 1 | 恢复=`6E5A91DD66B8F058` **EQUAL=True** |
| a2 | `il2cpp_string.h` 的 `il2cpp_string_is_ascii_bulk` 包进 `wrong_ws` | `B31AEEA76BC87A79` | `58C90CE36BD3EA03` | `mismatch=1` | 1 | 恢复=`B31AEEA76BC87A79` **EQUAL=True** |
| b | `box_double(double)` → `box_double(int64_t)`（签名漂移） | `6E5A91DD66B8F058` | `F989614F64589C80` | `mismatch=1` | 1 | **EQUAL=True** |
| c | 删 `box_double`+`unbox_double` 两条声明 | `6E5A91DD66B8F058` | `C73FDA0F2FBDB0F6` | `LATENT=2`（ABI mismatch=0） | 1 | **EQUAL=True** |
| d | combo：`box_double` 错 namespace + 删 `unbox_double` | `6E5A91DD66B8F058` | `416F2F7EDAF99CA2` | `mismatch=1` **且** `LATENT=1` | 1 | **EQUAL=True** |

原始检出（a1）：
```
ABI: checked=5660 match=5658 mismatch=2 skipped=0 (pairs=5660 definitions=9690 overload_pairs=40 probe_fail=0)
   ABI MISMATCH: dictgen.h: il2cpp_dict_gen_box_double -- the declaration resolves in NONE of the 1 namespace(s) that define this name
       expected (definition): il2cpp::il2cpp_dict_gen_box_double
       produced (by header):  _ZN6il2cpp26il2cpp_dict_gen_box_doubleEd
   ABI MISMATCH: dictgen.h: il2cpp_dict_gen_unbox_double -- ...
       expected (definition): il2cpp::il2cpp_dict_gen_unbox_double
       produced (by header):  _ZN6il2cpp28il2cpp_dict_gen_unbox_doubleEPKNS_12Il2CppObjectE
```
（a2）`expected: il2cpp::runtime::il2cpp_string_is_ascii_bulk` vs `produced: _ZN6il2cpp7runtime27il2cpp_string_is_ascii_bulkEPKNS_12Il2CppStringE` —— 正是 D3 设计要抓的形态，检出正确。

（c）删除走 LATENT 且 `mismatch=0`，ABI 维度不遮蔽 set 维度；（d）两类同时报告，**互不遮蔽成立**。

---

## 2. 假阳性回归：43 个 `il2cpp_gc.h` phantom mismatch 已消失 —— PASS

```
python check_decl_symmetry.py <ROOT> --pair il2cpp_gc.h:gc.cpp     (ROOT 在 --pair 之前)
il2cpp_gc.h vs gc.cpp: defined=44 declared=44
OK: defined - declared == {}
REAL GAPS: 0  internal-only: 0        EXIT=0
```
另用插桩副本（不改被审文件）输出**逐头**统计，全 176 头无一 mismatch，`il2cpp_gc.h` 为 `checked=44 match=44 mismatch=0`：
```
   PH il2cpp_gc.h                checked= 44 match= 44 mismatch= 0 skipped= 0
   PH dictgen.h                  checked= 92 match= 92 mismatch= 0 skipped= 0
   PH il2cpp_string.h            checked= 18 match= 18 mismatch= 0 skipped= 0
   ...（176 行，全部 mismatch=0，合计 checked=5660）
```
剪枝-重试（prune-and-retry）现在是**逐轮重跑直到该 variant 能编译**，不再把"未归因的接受项"当成拒绝，43 条 phantom 确实消失。

---

## 3. 两者互不遮蔽 —— PASS

见 §1 (d)。`ABI: checked=5659 match=5658 mismatch=1` 与 `LATENT (defined, undeclared, not emitted): 1` 同时出现，exit 1。

---

## 4. 稳定性（连跑 2 次，ABI 计数全等）—— PASS

冻结副本 `repo2` 上 4 次全量：
```
RUN1 EXIT=0 78.2s  ABI: checked=5660 match=5660 mismatch=0 skipped=0 (pairs=5660 definitions=9690 overload_pairs=40 probe_fail=0)
RUN2 EXIT=0 81.1s  同上（逐字节相同）
RUN3 EXIT=0 57.0s  同上
RUN4 EXIT=0 67.6s  同上
```
计数全等，耗时 57.0–81.1s（32 逻辑核，`jobs=16`），远低于 600s 上限。O(N²) 剪枝已消除（每 variant 的编译轮数 = 被归因拒绝数 + 1）。

---

## 5. 无效输入 —— PASS

| 场景 | 输出 | EXIT |
|---|---|---|
| `--no-abi` | `ABI: DISABLED by --no-abi (namespace/ABI agreement NOT verified)` + 其余维度照跑 | 0（显式声明，非静默） |
| PATH 移除 mingw（`g++/nm/c++filt` 全无） | `ABI: checked=0 ...` + `ABI-INCONCLUSIVE: g++/nm/c++filt not found on PATH` | **2** |
| 源码 TU 编译失败（我注入语法错误） | `ABI: ... probe_fail=1` + `ABI-NOTE: source compile failed for dictgen.cpp -- its definitions are MISSING from the ABI oracle (coverage hole)` + `ABI-INCONCLUSIVE: 1 source file(s) did not compile, so 5568 pair(s) could not be verified` | **2** |

---

## 6. 发现的缺陷（PASS 项之外，必须修）

### D1【严重·假阴性】重载集只做"名字在不在该 namespace"，完全不校验签名
`abi_check` 对 overload set 生成 `namespace __uN { using N::name; }`，编译通过即 `stats["match"] += 1  # using compiled: it IS there`（当前文件 1018 行附近）。**没有任何 mangle 比对**。

红证（两组独立变异，均 **EXIT=0**）：
```
MUT_1of2_OVERLOADS  arraysort.h:  il2cpp_array_reverse_int32(arr,int32_t,int32_t) -> (...,int64_t,int64_t)
  before=175545B1AA9D0B41 after=A950E7ECF5DF6573
  EXIT=0 80.0s   ABI: checked=5660 match=5660 mismatch=0 ... overload_pairs=40     ← 未检出

MUT_1of2_OVERLOADS  arrayextra.h: il2cpp_array_reverse_int32(arr) -> (arr,int32_t)
  before=FEA3696315AE392D after=85F5126F42EA1184
  EXIT=0 63.3s   ABI: checked=5660 match=5660 mismatch=0 ...                        ← 未检出
```
这不是"无害变异"，是真断链（链接器实证）：
```
调用方（用被改的声明）目标文件未定义符号：
  _ZN6il2cpp26il2cpp_array_reverse_int32EPNS_11Il2CppArrayExx   U
库（未改实现）实际导出：
  _ZN6il2cpp26il2cpp_array_reverse_int32EPNS_11Il2CppArrayEii   T
ld.exe: undefined reference to `il2cpp::il2cpp_array_reverse_int32(il2cpp::Il2CppArray*, long long, long long)'
```
覆盖面：实测 **35 个重载名（`overload_pairs=40` 个 (header,symbol) 对）** 全部处于"零 ABI 校验"状态，占 checked=5660 的 0.7%。**这与该维度存在的理由（签名/ABI 漂移）完全正撞**。
建议：对 overload set 用 `_abi_split_qualified` 已解析出的 `params`（当前是死数据，见 D4）生成 `static_cast<RET (*)(PARAMS)>(&N::name)` 探针逐个钉住每个签名。文件注释 673 行已宣称用 `static_cast`，实现未落地。

### D2【假绿·判据】`match` 可在零证据下自增
`if is_overload_set(sym): stats["ambiguous"] += 1; stats["match"] += 1` —— 该 `match` 只证明"该名字在 namespace 里存在"，**不证明签名一致**。这与文件头"A declaration is only 'in namespace N' if `N::name` reaches it … compared VERBATIM"的承诺矛盾，且是 D1 的计数面具（`mismatch=0` 看起来有 40 项被验证过，实际只是成员存在性）。建议把 overload 分支单独计为 `overload_membership_only`，`checked` 与 `match` 只统计真正做过 mangle 比对的对，让覆盖率诚实。

### D3【报告质量·误导】签名漂移的 mismatch 证据打印垃圾
```
ABI MISMATCH: dictgen.h: il2cpp_dict_gen_box_double -- header declaration is not the compiled definition (mangles to .bss)
    produced (by header):  .bss, .data, .pdata, .rdata
```
原因：`_ABI_NAMES()` 取 `line.split()[0]`，而 `nm --format=posix` 的**节行**（`.bss b 0`、`.pdata p 0`）也被当成符号名收进 `produced`；`compile_probe` 又把 undefined ∪ defined 合并，`sorted(produced)[0]` 恰好是 `.bss`。判定（mismatch）本身正确，但证据行毫无价值——审计的人无法据此判断"到底 mangle 成了什么"。建议 `_ABI_NAMES` 过滤掉以 `.` 开头的行与 `len(parts)<3` 的节行，并优先展示 `_Z` 开头的名字。

### D4【死代码/未验证承诺】
- `stats["inline_only"]` 算出后**从不打印**（实测值 0）。
- `_abi_split_qualified` 返回的 `params` 只被存进 `by_name` 三元组，除 `is_overload_set` 的解包外**从未使用**（673 行注释承诺的 `static_cast` + `decltype(__dv<PARAMS>())` 探针不存在）。

### D5【环境】共享工作树并发变异 + 检查器被改（见 §0）
两台审计/执行代理在同一未提交工作树上并行改 `src/runtime` 与被审脚本，导致同一 sha 的"全绿/假阳性"交替出现。**建议所有 P0 门禁类检查在读源码前先做一次 tree-hash 快照，运行后复核，不一致即判 INCONCLUSIVE。**

---

## 7. 结论

- **任务 1-5：全部 PASS**（变异矩阵 5 类全红且恢复 sha 一致；43 条 phantom 消失；互不遮蔽；4 次计数全等；`--no-abi`/无 g++/源编译失败三条无效输入均按设计失败）。
- **但整体不盖章 PASS**：D1 是**可执行的假阴性红证**（重载签名漂移改坏仍绿，且能实证断链），D2 是其计数面具，属于"改坏仍绿"类，按规则16 必须重写该分支后才能通过。
- D3/D4 是报告与死代码缺陷，不改变判定但降低可审计性。
- D5 是流程风险：主代理声称的 `C030512BA3426DC4`/52635B 不存在，实际复核版本为 `5414FC9818020DBD`/56610B；请在盖章前确认要验收的到底是哪一版，并在隔离树上重跑。

证据文件（原始输出）：
- `C:\Users\liulilte\AppData\Local\Temp\opencode\auditB\r2_base1..4.out`（稳定性）
- `r2_ns.out` / `r2_sig.out` / `r2_del.out` / `r2_combo.out` / `r2_nsstr.out`
- `r2_ovlsig.out`（D1 假阴性）、`r2_noabi.out`、`r2_nogpp.out`
- `x_srcfail.out`（源编译失败 → exit 2）
- `ph1.out`（逐头覆盖统计）
