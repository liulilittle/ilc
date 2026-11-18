# 审计 A · D3 ABI 维度「异方法独立复核」终版报告（W11⑤ 复验）

审计对象：`tools/audit/check_decl_symmetry.py`
审计角色：审计 A（与审计 B 并行独立审计 → 交换质证 → 双向复核）
审计时段：2026-11-17 15:36 → 17:28
**被审文件在审计期间被改写 10 次**（见 §0），故所有结论均绑定 sha16。
终判：**FAIL**（v10 已修好单签名与重载集，但仍有 4 项可执行漏报，其中 2 项为「改坏仍绿」）

---

## 0. 版本漂移表（每次结论都有 sha16 归属）

| # | sha16 | 字节 | mtime | 关键变化 / 行为 |
|---|-------|------|-------|------|
| v1 | 19FBE9A5B40A832A | 53589 | 15:28:50 | 我首次读到的版本；`match=5660 skipped=0` |
| v2 | 8BF5DB2088D58A7C | 55541 | 15:54:30 | 新增 `source_fail` |
| v3 | 5414FC9818020DBD | 56610 | 16:12:29 | `source_fail` → exit 2 |
| v4 | 4A47A31A62A1A728 | 60663 | 16:52:06 | overload pin 落地 |
| v5 | CF94515037181A4A | 62226 | 16:52:44 | overload 部分匹配判定（过度报告 40 条） |
| v6 | F7FD74C5BBDD4DBC | 63346 | 16:59:33 | overload 全 skipped（漏报回归） |
| v7 | 04C1CC0C11D36A2A | 63853 | 17:03:17 | `inline_only` 进入 ABI 行 |
| v8 | FEE12E6B38BC38C8 | 64890 | 17:06:34 | overload 重写（至少一条签名命中即 match） |
| v9 | FEE12E6B38BC38C8→ | 64890 | — | 同上（快照） |
| v10 | F75EF77B5A19FE0E | 68428 | 17:12:24 | 逐签名 pin + drop 不可命名参数；`skipped=0` |

**主代理验收证据 `52635 B / C030512BA3426DC4` 本机不存在**：
```
[BASH] Get-ChildItem -Recurse -File F:\il2cpp | ? Length -eq 52635   → 0 个文件
[BASH] 当前 SHA256_16 = F75EF77B5A19FE0E, LEN = 68428
```
⇒ 主代理 [A]/[B]/[D]/[F] 的计数无法归因到磁盘上任何版本（W5：视为未验证）。

---

## 1. 全量实跑（任务 1）

### v1（19FBE9A5，早期，2 次）
```
EXIT=0  57.4s :: ABI: checked=5660 match=5660 mismatch=0 skipped=0 (pairs=5660 definitions=9690 overload_pairs=40 probe_fail=0)
EXIT=0  72.4s :: 同上（逐字等）
```
### v10（F75EF77B，最终，不可变快照，3 次）
```
EXIT=0  55.4s :: ABI: checked=5660 match=5660 mismatch=0 skipped=0 (pairs=5660 definitions=9690 overload_pairs=90 inline_only=0 probe_fail=1)
EXIT=0  70.2s :: 同上
EXIT=0  65.9s :: 同上
```
**说明**：`checked=5660` 稳定；但 `overload_pairs` 在版本间 40→90，`probe_fail` v10 恒为 1
（`regex.h` 的 `il2cpp_regex_match` 内部签名），而 **EXIT 仍是 0** —— 见 §3 D1。

### 不稳定复现（干净树 EXIT=1，v2）
```
RUN3 EXIT=1 59.5s :: ABI: checked=5660 match=5659 mismatch=1 ...（树在运行前后 git status 均为空）
```
同树、同命令，10 次中 1 次红。根因含执行侧实时改写（`dictgen.h 15:59:41→16:00:49` 往返，B 亦观察）+ 并行编译偶发。**这是"接受标准不可靠"的直接证据**。

---

## 2. 变异自证（任务 2，全部字节级 Copy-Item 恢复，未用 git 回退）

### 2.1 单签名命名空间变异 → **PASS**
符号 `il2cpp_dict_gen_kind_is_ref`（`dictgen.h:128`，非主代理所用符号），在 v10 上复验：
```
PRE : SHA16=6E5A91DD66B8F058 gitblob=6a9c44207f41c3a31f2425fd023c95d85893f3af LEN=21404
MUT : SHA16=1395CC84B0FF5A01（namespace wrong_ws { ... }）
结果: ABI: checked=5660 match=5659 mismatch=1 skipped=0 ... EXIT=1
      ABI MISMATCH: dictgen.h: il2cpp_dict_gen_kind_is_ref -- resolves in NONE of the 1 namespace(s)
        expected il2cpp::il2cpp_dict_gen_kind_is_ref / produced _ZN6il2cpp27il2cpp_dict_gen_kind_is_refEi
POST: SHA256=6E5A91DD...C239E9C1B LEN=21404 gitblob=6a9c4420...f3af（PRE 全等）git status = 空
```
✅ 红→绿→字节级一致。

### 2.2 重载集签名漂移 → **v10 PASS / 中间版 FAIL**（与 B 结论一致，我独立复现）
真实仓 `arraysort.h:158`：
```
变异 void il2cpp_array_reverse_int32(Il2CppArray*, int32_t, int32_t)
   → (Il2CppArray*, int64_t, int64_t)
v6/v7（F7FD74C5/04C1CC0C）: ABI mismatch=0 skipped=163/probe_fail=2 → EXIT=0   ← 漏报
v10（F75EF77B）           : ABI mismatch=1 skipped=0                 → EXIT=1   ← 已修
恢复: SHA16=175545B1AA9D0B41 gitblob=1db3a03f... git status=空
```
**链接器实证（真断链，非理论）**：
```
调用方未定义: ..._ZN6il2cpp26il2cpp_array_reverse_int32EPNS_11Il2CppArrayExx
库实际导出  : ..._ZN6il2cpp26il2cpp_array_reverse_int32EPNS_11Il2CppArrayEii
LINK_RC=1 ld.exe: undefined reference to `...reverse_int32(Il2CppArray*, long long, long long)'
```

### 2.3 重载集整体进错命名空间 → **v4~v9 FAIL / v10 PASS**
fixture `fix6`（`namespace wrong_ns { int il2cpp_ovl(int); int il2cpp_ovl(long); }`，定义在 `il2cpp::`）：
```
v4..v9: ABI mismatch=0 skipped=1 probe_fail=1 → EXIT=0   ← 漏报
v10   : ABI mismatch=1 skipped=0              → EXIT=1   ← 已修
```

### 2.4 重载签名漂移（regex.h，第 0 号签名）→ v10 PASS
```
变异 il2cpp_regex_match(Il2CppString*, Il2CppString*, int32_t=0)
   → il2cpp_regex_match(Il2CppString*, const char16_t*, int32_t=0)
结果 EXIT=1: ABI MISMATCH: regex.h: il2cpp_regex_match -- no definition signature ... declared here
恢复 blob a55c768e... 全等, git status=空
```

---

## 3. 实现质量审查（任务 3）

| 项 | 结论 | 证据 |
|---|---|---|
| (a) `while attempt:` 每轮至少移除 1 个？（死循环？） | **成立** | `bad = {sym for sym,_ in failures}` 非空才进入裁剪；`if not failures: break` |
| (b) `checked` 每 (header,symbol) 只累加一次？ | **成立** | `count_once()` 以 `counted` set 门控 |
| (c) `pending` 兜底判 mismatch 前提严格？ | **v10 起成立，v4~v9 不成立** | v9 及以前「不可 pin」走 `skipped`+`pending.remove`，永不计 mismatch（§2.2/2.3 漏报机制）；v10 改为 mismatch |
| (d) `probe_fail`/`skipped` 每条都有 ABI-NOTE？ | **成立** | v10 实测 `skipped=0`、`probe_fail=1`，NOTE 恰 1 条（"regex.h signature 1 cannot be probed"） |
| (e) 无工具链 → INCONCLUSIVE + exit 2？ | **成立** | `ABI-INCONCLUSIVE: g++/nm/c++filt not found on PATH` → EXIT=2（v10 实测） |

### 仍存在的缺陷（v10 未修）

**D1（严重·改坏仍绿）`probe_fail` 不进退出码。**
v10 干净树就恒为 `probe_fail=1`（`regex.h:il2cpp_regex_match` 的内部签名无法从该头 pin），
却 `EXIT=0`。任何「新增一个不可探针符号」的回归都不会被 CI 拦住。
```
[BASH] v10 三次全量均: probe_fail=1 ... EXIT=0
[READ:tools/audit/check_decl_symmetry.py:1397-1416] 只有 source_fail / checked==0 / unavailable 触发 2；probe_fail 不在判决内
```

**D2（严重·改坏仍绿）`extern "C"` 声明进错命名空间 → 漏报。**
真实仓 `il2cpp_api.h:42` 的 `IL2CPP_C_API void il2cpp_gc_collect(void);` 被移入
`namespace il2cpp { namespace wrong_capi { ... } }` 后：
```
[BASH] ABI: checked=5660 match=5660 mismatch=0 skipped=0 ... EXIT=0      ← 漏报
[BASH] 真实调用方编译失败（证明是真破坏）:
       error: 'il2cpp_gc_collect' was not declared in this scope; did you mean 'il2cpp::wrong_capi::il2cpp_gc_collect'?
       CALLER_COMPILE_RC=1
恢复: blob f11bbb8b23dc24ca6f83e5f0f7d2abf7740013b6 全等, git status=空
```
根因：`_abi_split_qualified()` 对 `extern "C"` 定义返回 `params=None` 的裸名；
`is_overload_set` 判定 `il2cpp_gc_collect` 的 2 个定义（`api.cpp:114` C 链接 + `gc.cpp:1841` C++）
**跨命名空间**故不是 overload set；探针用 `decltype(&il2cpp::il2cpp_gc_collect)` 命中 `gc.cpp` 的
C++ 定义即算 match，**`il2cpp_api.h` 那一条 C 声明的根作用域漂移完全不看**。
真实仓现在两个定义同时存在（`api.cpp:114 void il2cpp_gc_collect(void)` 与 `gc.cpp:1841 void il2cpp_gc_collect()`），
属**潜在双链接风险**，检查器对此没有任何输出。

**D3（中·证据质量）签名漂移时 `produced` 打印 nm 节行。**
```
produced (by header): .bss, .data, .pdata, .rdata$.refptr._ZN6il2cpp13il2cpp_pdriftEi
```
`_ABI_NAMES()` 无条件取每行第一列；nm posix 的节行（`.bss`/`.data`/`.pdata`）被当符号。
（B 独立观察到同一现象。v10 的部分路径已过滤，但 `_abi_pdrift` 类 fixture 仍复现。）

**D4（低·死统计）`inline_only`**：v1–v6 计算但不打印；v7 起才进入 ABI 行，实测恒 0。

**D5（中·覆盖语义）伞头遮蔽使「跨头同名声明」无法逐头判定。**
每个探针 TU 先 include `il2cpp_runtime.h`（`has_umbrella`），于是「A 头正确 + B 头漂移」的
真实缺陷被 A 头的正确声明掩盖：
```
[BASH] fixture fixMask3 (good.h 正确 + bad.h 漂移, umbrella 同时 include 两者)
       ABI: checked=2 match=2 mismatch=0 → EXIT=0     ← 两个头都判 match
[BASH] 同 fixture 去掉 umbrella, 只 include bad.h → 立即 'not a member of il2cpp'（确定性红）
```
真实仓受影响面（umbrella 闭包内被多头声明的符号）我实测统计：
```
declared in 2+ headers (declaration-only 形态): 10 个
  其中至少一个在 umbrella 闭包内: 1 个 → il2cpp_gc_collect (il2cpp_api.h, il2cpp_gc.h)
放宽到"任意出现"形态: 14 个（含 il2cpp_array_new / il2cpp_gc_add_pinned / il2cpp_null_check / il2cpp_string_new / il2cpp_frame_push ...）
```
即 D5 在真实仓当前影响 1~14 个符号的逐头判定能力（取决于计数口径），不会造成当前全量假绿，
但**新增这类跨头声明时会把漂移吞掉**。修法：per-header 探针只 include 该头本身，
umbrella 仅用于「该头单独不能自洽」的兜底并单独计数。

---

## 4. 历史缺陷对照：`il2cpp_gc.h` 43 phantom（任务 4）
```
[BASH] python tools/audit/check_decl_symmetry.py F:\il2cpp --pair il2cpp_gc.h:gc.cpp
il2cpp_gc.h vs gc.cpp: defined=44 declared=44
OK: defined - declared == {}
REAL GAPS: 0  internal-only: 0       EXIT_PAIR=0
```
✅ 44 符号全 match（与 B 一致）。

**异方法独立核验**（不依赖该脚本）：编译 `gc.cpp` → 167 个 `T`；用 44 条声明逐条
`decltype(&il2cpp::N)` 生成探针 TU，比对 undefined 集合：
```
PROBE_SYMS=44  DEFINED_IN_GC_O=167  MISSING=0
```
另做 10 头 552 条独立抽样（hashset.h 135 / dictgen.h 92 / arraysort.h 147 / il2cpp_array.h 20 /
il2cpp_char.h 16 / il2cpp_string.h 18 / il2cpp_exception.h 21 / cultureinfo.h 26 / threadobj.h 34 / gc.h 43）：
```
TOTAL syms=552 match=552 mismatch=0 skipped=0
```

---

## 5. 失效方向（任务 5）
```
--no-abi              → ABI: DISABLED by --no-abi ...  EXIT=0        ✅ 显式退出
移除 g++/nm/c++filt   → ABI-INCONCLUSIVE: ... not found  EXIT=2      ✅
源码语法错误          → ABI-NOTE: source compile failed ... + INCONCLUSIVE  EXIT=2  ✅
0 pair 被验证         → ABI-INCONCLUSIVE: 0 pairs ...  EXIT=2        ✅
probe_fail=1（仅探针不可用） → 无 INCONCLUSIVE，EXIT=0               ❌ D1
```

---

## 6. 结论：FAIL

| # | 项 | 判定 |
|---|---|---|
| 1 | 全量可重复 | 计数稳定；**1/10 假红**；版本间 overload_pairs 40→90 → **FAIL** |
| 2 | 单签名命名空间变异 | **PASS** |
| 2' | 重载集签名漂移 | v10 **PASS**（v6/v7 为 FAIL，链接器实证） |
| 2'' | 重载集整体错命名空间 | v10 **PASS**（v4–v9 FAIL） |
| 2''' | `extern "C"` 声明错命名空间 | **FAIL**（EXIT=0，真实调用方编译失败） |
| 3 | 实现质量 | 剪枝/计数/NOTE **PASS**；`probe_fail` 不进判决 **FAIL**；伞头遮蔽 **FAIL**；垃圾证据 **FAIL** |
| 4 | il2cpp_gc.h | **PASS**（+ 异方法编译器复核） |
| 5 | 失效方向 exit 2 | **PASS**，但 probe_fail 例外 |

### 阻断提交的必修项
1. **D1** `probe_fail > 0` 必须 exit 2（或计入 mismatch 判决）。v10 干净树恒有 1 条，
   加一个不可探针符号不会有任何信号。
2. **D2** `extern "C"` 根作用域声明漂移必须检。当前对 `il2cpp_gc_collect` 这类
   「C 链接声明 + C++ 定义并存」的符号零覆盖；建议对 `params is None` 的候选额外生成
   `extern "C"` 根作用域地址探针（`&::NAME`），并显式报告「同名双链接」。
3. **D5** per-header 探针不得默认带 umbrella（或带 umbrella 时单独降级为「仅成员性」），
   否则跨头同名声明的漂移会被掩盖。
4. **D3** `_ABI_NAMES()` 需过滤 `^\.` 节行（nm posix 格式）。
5. 验收证据必须给出与磁盘一致的 sha16+LEN+mtime（当前 52635B/C030512BA3426DC4 不可复核）。

## 7. 反思（先于结论，覆盖全程）
- 我最初准备凭「单签名命名空间变异红 + 全量 5660 绿」判 PASS。是 B 的重载维度质证让我
  发现自己的**变异矩阵只覆盖了 `decltype(&N::name)` 一条分支**。教训：变异矩阵必须按代码分支枚举。
- 我的第一版异方法抽样脚本有缺陷（只看 undefined，漏掉 inline 物化的本地 `T`），产生 3 条假
  mismatch；我在下结论前用对照组修正了它。教训：**异方法工具本身必须先被验证**。
- 我早期把「5660/5660/0 与 5497/163」当成偶发不稳定，实为**文件被连续改写 10 次**。
  教训：审计第一步与每次结论前都必须复核 sha16+LEN+mtime。
- 我在报告中对 v4 的「过度报告」与 v6/v7 的「漏报」都如实记录，没有只挑有利一侧；
  v10 的三项红证我重跑并给出 EXIT=1，确认修复真实存在（而非声称）。

---

## 8. 追加（与 B 磋商后、v10 深挖）

### 8.1 v10 已修的（B 的 D2/D3 在 v10 上不再成立）
- B-D2「`using` 假绿」：v10 改为逐签名 SFINAE pin，`# using compiled: it IS there` 注释已消失
  （[READ:cds_v10.py:1191-1225]）。
- B-D3「nm 节行当符号」：v10 `_ABI_NAMES()` 已过滤 `name.startswith(".") or name.endswith(":")`
  且要求第二列是链接字母（[READ:cds_v10.py, def _ABI_NAMES]）。但**仍有残留**：
  `fix3`（返回类型漂移）实测 `produced (by header): _ZL4__p0v, _ZN6il2cpp13il2cpp_pdriftEi`
  ——节行已过滤，但**探针自身的 static 函数 `__p0` 被当成了「header 产出的符号」**。

### 8.2 【新增·最严重】v10 重载「部分签名漂移」→ 漏报（链接器实证）
v10 的判定是「至少一条定义签名被声明即 match」（[READ:cds_v10.py:1199-1219]）。
于是**一个 3 签名的重载集，漂移 1 条、另 2 条正确 → match=1 / EXIT=0**。

fixture（`namespace il2cpp { int il2cpp_q(int); int il2cpp_q(int,int); int il2cpp_q(int,int,double); }`
定义侧第 3 条为 `(int,int,int)`），检查器：
```
[BASH] ABI: checked=1 match=1 mismatch=0 skipped=0 ... overload_pairs=3    EXIT=0
```
链接器证明这是真断链：
```
[BASH] caller 调 il2cpp_q(1,2,3L)（按头文件声明解析为 (int,int,double)）
       未定义: _ZN6il2cpp8il2cpp_qEiid
       库导出: _ZN6il2cpp8il2cpp_qEi  /  Eii  /  Eiii
       LINK_RC=1  undefined reference to `il2cpp::il2cpp_q(int, int, double)'
```
**真实仓复现**（`arraysort.h:150` 的 `il2cpp_array_sort_object` 4 参版 `int32_t,int32_t` → `int64_t,int64_t`，
该头同时在第 132 行声明 2 参版且两者定义都在 `arraysort.cpp:730/813`）：
```
[BASH] MUT(仅第 150 行) SHA16=37C9415F6C7C0A73
       ABI: checked=5660 match=5660 mismatch=0 skipped=0 ... probe_fail=1    EXIT=0   ← 漏报
恢复: SHA16=175545B1AA9D0B41 blob=1db3a03f... git status=空
```

**为什么 v10 这么设计**：注释写明「Demanding ALL would be a false POSITIVE, because an overload set
is legitimately split across headers (`arraysort.h` 3-arg / `arrayextra.h` 1-arg)」[READ:cds_v10.py:1199-1204]。
这个理由是**成立的一半**：跨头拆分确实不能要求「全头都声明全签名」；
但正确判据应是**按头分别比较「本头声明了哪些签名」vs「本 TU 能链接到哪些签名」**，
而不是「本头至少命中一条就整体通过」。当前实现对「同一头内 2+ 签名的部分漂移」完全失明。

### 8.3 其余未修项（v10 仍成立）
- **D1'** `probe_fail=1`（`regex.h:il2cpp_regex_match` 内部签名不可 pin）在干净树上恒存在，EXIT=0。
- **D2'** `extern "C"` 根作用域声明漂移（`il2cpp_api.h:42 il2cpp_gc_collect`）→ EXIT=0 漏报，
  真实调用方编译失败（`CALLER_COMPILE_RC=1`），blob 已恢复全等。
- **D5** 伞头遮蔽：umbrella 同时 include good.h+bad.h 时，bad.h 漂移被掩盖（`fixMask3` EXIT=0）；
  只 include bad.h 则确定性红。真实仓严格口径影响 1 个符号（`il2cpp_gc_collect`），宽口径 14 个。

### 8.4 终判维持 FAIL，必修项增补
在 §6 的 5 项之外，**新增最高优先级**：
0. **重载「部分签名漂移」必须红**（§8.2）。这是 v10 引入的新漏报，且是真实仓可复现
   （`arraysort.h:150`），链接器实证。修法：逐头把「本头声明签名集」与「定义签名集」
   做子集判定——本头声明的每一条都必须在定义集中；缺任一 → mismatch。跨头拆分由
   「每个头只对自己声明的那些签名负责」保证不会误报。


---

## 9. 追加 2（v11 17CFDD02 崩溃 + 执行侧修法方向反证）

### 9.1 【P0】当前磁盘版本崩溃
磁盘 `17CFDD02B8019925 / 71156B / 17:33:39`：
```
[BASH] python tools/audit/check_decl_symmetry.py F:\il2cpp
  line 1410 in main: astats, abi_mismatches, unavailable = abi_check(headers_rel, d & c)
  line 913 in abi_check: syms = sorted(declared(read(path)) & both)
  UnboundLocalError: cannot access local variable "declared"
  REALRUN_EXIT=1
[BASH] python -m py_compile -> 0 (scoping regression, not syntax)
```
= 该版本 ABI 判决从未执行；以其为对象的任何评分无效。文件变更序列：
v9 FEE12E6B -> v10 F75EF77B -> v11a 25969CAA(68560) -> v11b 17CFDD02(71156)。

### 9.2 【P0 方向性】执行侧「并集语义」漏报悬空声明（链接器实证）
fixture %TEMP%\opencode\fixDangle
```
头  : namespace il2cpp { int il2cpp_d(int); int il2cpp_d(long); int il2cpp_d(double); }
定义: il2cpp_d(int) / il2cpp_d(long)          <- (double) 只有声明，无定义
[BASH] v10 F75EF77B  -> ABI: checked=1 match=1 mismatch=0    EXIT=0   <- 漏报
[BASH] v11a 25969CAA -> 同上 EXIT=0
[BASH] 链接器: caller il2cpp::il2cpp_d(1.5) -> undefined _ZN6il2cpp8il2cpp_dEd
       ld.exe: undefined reference to il2cpp::il2cpp_d(double)
```
并集语义满足（定义侧 2 条都被声明）却判绿，但调用方必断链。
= ABI 一致性是双向集合关系，单向包含（无论哪个方向）都会漏报一类真实断链。

### 9.3 中间版统计丢账（25969CAA）
```
[BASH] fixPart2  -> ABI: checked=1 match=0 mismatch=0 skipped=0   EXIT=0
[BASH] fixDangle -> ABI: checked=1 match=0 mismatch=0 skipped=0   EXIT=0
```
checked=1 而 match+mismatch+skipped=0：overload verdict 被 defer 到 tree-wide pass，
而 sig_declared 当时只有写入无读取点（grep 仅 841/844 定义 + 1210 setdefault）。
= 静默丢符号。必须断言 match+mismatch+skipped == pairs。

### 9.4 我向执行侧提出的正确判据（双向 + 按头归责）
1. 定义->声明：每定义签名被某头声明（并集）。
2. 声明->定义：每个头声明的签名必须可链接到某定义（本地可判，不得 defer）。
3. tree-wide 仅作补充告警（发射面外未声明 -> note）。
4. 恒等式 match+mismatch+skipped == pairs 必须成立。
5. 探针自身符号（__p* / __abi_*）不得计入 produced（D3 残留，_ZL4__p0v 实测）。

### 9.5 结论（维持 FAIL）
- 当前磁盘版本崩溃，验收必须重新开始。
- 即便回到 v10（F75EF77B），仍存在 D6（重载部分签名漂移）、D7（悬空声明）、
  D1'（probe_fail 不进退出码）、D2'（extern "C" 漂移漏报）、D5（伞头遮蔽）5 项漏报，
  其中 D6/D7 均有链接器实证。
- 请勿在 v11 跑通并补齐双向判据前接受「PASS」。
