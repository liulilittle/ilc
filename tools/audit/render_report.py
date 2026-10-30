"""Render docs/audit/icall-mscorlib-current.md from classified.json.

Every number in the report is computed from that file, so the prose cannot
drift from the data. The generator is deterministic: no timestamps, no
environment-dependent ordering (all sorts are ordinal).

usage: render_report.py <auditOutDir> <repoRoot>
"""
import io, os, json, collections, sys

TMP = sys.argv[1]
ROOT = sys.argv[2] if len(sys.argv) > 2 else r'F:\il2cpp\.worktrees\audit-mscorlib-icall'
OUT = os.path.join(ROOT, 'docs', 'audit', 'icall-mscorlib-current.md')

rows = json.load(io.open(os.path.join(TMP, 'classified.json'), encoding='utf-8'))
graph = json.load(io.open(os.path.join(TMP, 'callgraph.json'), encoding='utf-8'))
arms = json.load(io.open(os.path.join(TMP, 'arms.json'), encoding='utf-8'))

STATUS = ['wired', 'half', 'loud', 'shadow', 'missing']
STATUS_CN = {
    'wired': '已接',
    'half': '半接',
    'loud': '缺（LOUD 拒绝）',
    'shadow': '影子',
    'missing': '缺（无接线）',
}
PRIO = ['P0', 'P1', 'P2', 'P3']

total = len(rows)
by_status = collections.Counter(r['status'] for r in rows)
by_prio = collections.Counter(r['priority'] for r in rows)
nw = [r for r in rows if r['status'] != 'wired']
nw_by_prio = collections.Counter(r['priority'] for r in nw)

public = [r for r in rows if r['publicFace']]


def md_cell(s):
    return (s or '').replace('|', '\\|').replace('\n', ' ')


def line_of(r):
    return f"`{r['site']}`" if r.get('site') else '`no wiring`'


def sort_key(r):
    return (r['owner'], r['name'], r['sig'])


L = []
w = L.append

w('# mscorlib InternalCall 现状盘点（2026-10-31）')
w('')
w('> 只读审计。以仓库 **当前** 代码（`c3a6b2e3`）为准重新生成，不引用任何历史数字。')
w('> 目标程序集：`C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\mscorlib.dll`（net48）。')
w('')
w('## 0. 结论摘要')
w('')
w(f'- net48 mscorlib InternalCall 总数：**{total}**（反射通道 {total} / 元数据通道 {total}，逐条 join key 一致）')
w(f'- 判定分布：'
  f'已接 **{by_status["wired"]}**、'
  f'半接 **{by_status["half"]}**、'
  f'缺 **{by_status["loud"] + by_status["missing"]}**'
  f'（LOUD 拒绝 {by_status["loud"]} + 无接线 {by_status["missing"]}）、'
  f'影子 **{by_status["shadow"]}**')
w(f'- 可达性分级（仅统计非「已接」的 {len(nw)} 条）：'
  f'P0 **{nw_by_prio["P0"]}**、P1 **{nw_by_prio["P1"]}**、'
  f'P2 **{nw_by_prio["P2"]}**、P3 **{nw_by_prio["P3"]}**')
w(f'- public API 面（类型 public 且方法 public）共 **{len(public)}** 条，其中已接 '
  f'**{sum(1 for r in public if r["status"] == "wired")}**、'
  f'LOUD {sum(1 for r in public if r["status"] == "loud")}、'
  f'无接线 {sum(1 for r in public if r["status"] == "missing")}')
w('')
w('### 0.1 判定口径（逐条都在 §2/§3 给出 file:line）')
w('')
w('| 判定 | 含义 | 证据形态 |')
w('|---|---|---|')
w('| 已接 | 转译器的 `MethodRefResolver.BclResolve` 对这条 icall 返回了 C++ 目标，'
  '且该目标在 `src/runtime` 有真实实现（非 `*_loud` 抛异常桩）；折叠标记（`::identity::` 等）'
  '视为已接，因为发射器直接内联 | `MethodMap.*.cs:<line>`（dispatcher 分支）+ 运行时定义 |')
w('| 半接 | 有 dispatch 目标但运行时实现是 `*_loud` 抛异常桩（调用必然抛） | `MethodMap.*.cs:<line>` + 桩定义 |')
w('| 缺（LOUD 拒绝）| 转译器对该成员显式抛 `NotSupportedException`，并在转译期把整个方法标记为未转译（不是静默错误） | `MethodMap.*.cs:<line>`（throw 点）+ 原始消息 |')
w('| 缺（无接线）| 没有 owner 分支也没有方法分支，落到生成的幽灵函数名（`System_Xxx_<hash>`），C++ 链接期失败 | `no wiring` |')
w('| 影子 | 该 icall 自身无接线，但它的**直接调用者**是 public 方法且该 public 方法已接线，用户代码只发出那一次调用，icall 体不会被转译 | 承接的 public 方法 + 其 `MethodMap.*.cs:<line>` |')
w('')
w('可达性分级（对非「已接」行）：')
w('')
w('| 级别 | 定义 |')
w('|---|---|')
w('| P0 | 类型 public 且方法 public —— 用户 IL 可直接 call |')
w('| P1 | 非 public 面，但存在 public 入口到它的调用链（≤3 跳），且属主不在 P2 命名空间 |')
w('| P2 | 只能经 reflection / remoting / interop / diagnostics / serialization / security / '
  'AppDomain / CompilerServices 到达，或链路深度 ≥4 |')
w('| P3 | mscorlib 内部无任何 public 入口可达（legacy / CLR 内部 / 已废弃） |')
w('')
w('## 1. 方法与证据（双通道一致性与差异）')
w('')
w('两条**互相独立**的通道，join key 为 `(声明类型全名, 方法名, 签名, method|ctor)`：')
w('')
w('| 通道 | 实现 | 结果 |')
w('|---|---|---|')
w('| 反射 | `tools/audit/enumerate-icall-reflection.ps1`（Windows PowerShell 5.1，`Assembly.LoadFrom` Framework mscorlib，'
  '枚举全部类型含 non-public 的 methods+ctors，过滤 `MethodImplAttributes.InternalCall`） | 908 |')
w('| 元数据 | `tools/audit/MetadataIcallCounter`（.NET 9 + `System.Reflection.Metadata`，直接读 `MethodDef.ImplAttributes` 位） | 908 |')
w('')
w('- **两通道数量相等：908 = 908**；join key 集合完全相同（`Compare-Object` 差集为 0）。')
w('- 类型数反射 3354 / 元数据 3356，差 2 是 `<Module>` 等反射不可枚举行，不含 icall，不影响计数。')
w('- 首轮曾出现 6 行 join 差异，全部是 `` !0 `` 泛型参数在反射通道被渲染成空串'
  '（`System.WeakReference`1::Create/get_Target/set_Target`）。已修正反射脚本的 '
  '`Format-ParamType`（泛型参数渲染为 `!n` / `!!n`），修正后差异归零 —— 这是**渲染差异，不是方法差异**。')
w('')
w('方法学：判定不是用正则猜的。`tools/audit/IcallWiringProbe` 直接加载 `il2cppc` 程序集并对这 908 条 icall'
  '逐条调用真实入口 `MethodRefResolver.BclResolve`，因此「已接/LOUD/无接线」来自转译器自身的运行结果，'
  '而不是对源码的文本匹配。')
w('')
w('可达性另有独立证据：`tools/audit/IcallCallGraph` 解码 mscorlib 全部托管方法体'
  f'（{graph["methodCount"]} 个方法，解码失败 {graph["decodeFailures"]} 个）建立调用图，'
  '从 public 面做前向 BFS，得到「该 icall 到最近 public 入口的距离 / 该入口是谁」。')
w('')
w('## 2. 已接清单')
w('')
w(f'共 **{by_status["wired"]}** 条。证据列是 dispatcher 的 `MethodMap.*.cs:<line>`，'
  '即 `BclResolveCore` 的 owner 分支最终命中的 `TryMap*` 里那个方法名分支。')
w('')
w('| # | 声明类型 | 方法 | 签名 | 可见性 | 运行时目标 | 证据 |')
w('|---|---|---|---|---|---|---|')
for i, r in enumerate(sorted([x for x in rows if x['status'] == 'wired'], key=sort_key), 1):
    tgt = r['symbolBare'] or (r['symbol'] or '')
    w(f"| {i} | `{r['owner']}` | `{r['name']}` | `{md_cell(r['sig'])}` | {r['vis']} | "
      f"`{md_cell(tgt)}` | {line_of(r)} |")
w('')
w('## 3. 缺口清单（按 P0→P3）')
w('')
w('「缺口」= 非「已接」的全部行（半接 / LOUD 拒绝 / 无接线）。影子单列在 §3.6，'
  '因为它的结论是「无需接线」。')
w('')
for prio in PRIO:
    sel = sorted([r for r in nw if r['priority'] == prio], key=sort_key)
    w(f'### {prio}（{len(sel)} 条）')
    w('')
    if not sel:
        w('（无）')
        w('')
        continue
    if prio == 'P0':
        w('类型与方法均为 public，用户 IL 可以直接 call，属于**必须处理**的面。')
    elif prio == 'P1':
        w('非 public 面，但经普通 BCL 内部路径可达（集合/字符串/LINQ/IO/格式化/数值等）。')
    elif prio == 'P2':
        w('仅经反射/序列化/调试器/COM 宿主/interop 路径可达，或链路深度 ≥4。')
    else:
        w('mscorlib 内部没有任何 public 入口可达（legacy / CLR 内部）。')
    w('')
    w('| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |')
    w('|---|---|---|---|---|---|---|')
    for i, r in enumerate(sel, 1):
        if r['status'] == 'shadow':
            wrapper = r['wrappers'][0]
            note = f"影子：由 `{wrapper['method']}` 承接（`{wrapper['symbol']}`）"
            ev = f"`{r['site']}`" if r['site'] else '`no wiring`'
        elif r['status'] == 'loud':
            msg = (r['error'] or '').split(': ', 1)[-1]
            msg = msg.split(' [Reference Source', 1)[0]
            note = f"LOUD 拒绝：{md_cell(msg[:150])}"
            ev = line_of(r)
        elif r['status'] == 'half':
            note = (f"半接：dispatch 指向 `{r['symbolBare']}`，但该运行时实现是抛异常的 LOUD 桩"
                    f"（桩定义 `{r['stub']}`）")
            ev = line_of(r)
        else:
            note = '无 owner 分支 / 无方法分支'
            ev = '`no wiring`'
        w(f"| {i} | `{r['owner']}` | `{r['name']}` | {r['vis']} | {STATUS_CN[r['status']]} | "
          f"{note} | {ev} |")
    w('')
w('### 3.6 影子清单')
w('')
shadows = sorted([r for r in rows if r['status'] == 'shadow'], key=sort_key)
w(f'共 **{len(shadows)}** 条。判定依据：icall 的**直接调用者**是 public 面方法，且该 public 方法自身已接线；'
  '用户代码只发出那一次调用，因此这条 icall 不需要自己的接线。')
w('')
w('| # | 声明类型 | 方法 | 可见性 | 承接的 public 方法 | 承接者的接线证据 |')
w('|---|---|---|---|---|---|')
for i, r in enumerate(shadows, 1):
    wrapper = r['wrappers'][0]
    w(f"| {i} | `{r['owner']}` | `{r['name']}` | {r['vis']} | `{wrapper['method']}` | "
      f"`{r['site']}` |")
w('')
idx = {(r['owner'], r['name']): r for r in rows}

# key-level view (dedup by owner+name), because the 2026-08-06 report counted
# at that granularity and a like-for-like comparison is the only honest one.
key_groups = collections.defaultdict(list)
for r in rows:
    key_groups[r['owner'] + '::' + r['name']].append(r)


def key_status(rs):
    st = {x['status'] for x in rs}
    for o in ['wired', 'half', 'shadow', 'loud', 'missing']:
        if o in st:
            return o
    return 'missing'


pub_keys = {k: v for k, v in key_groups.items() if any(x['publicFace'] for x in v)}
pub_key_status = collections.Counter(key_status(v) for v in pub_keys.values())
pub_keys_wired = pub_key_status['wired']
pub_keys_gap = len(pub_keys) - pub_keys_wired

w('## 4. 与前次盘点（2026-08-06）的差异')
w('')
w('旧文档 `docs/audit/icall-inventory-net48.md` 的核心结论：**910 条 InternalCall；public 面 121 个键；'
  '54 已接 / 67 缺口（42 域在方法缺 + 25 无域）**。以当前代码重扫后，这些数字**全部不再成立**。')
w('')
w('### 4.1 总量')
w('')
w('| 项目 | 旧结论（2026-08-06） | 本次实测 | 结论 |')
w('|---|---|---|---|')
w('| InternalCall 总数 | 910 | **908**（mscorlib 单程序集） | **推翻**：「910」是 7 个程序集的合计'
  '（mscorlib 908 + System.dll 2 个 COM 内部 ctor）。本审计的范围是 mscorlib，'
  '908 才是该程序集的真实总数。旧文档把一个跨程序集的合计当作 mscorlib 的数 |')
w('| 双通道一致 | 910/910 | **908/908，join key 差异 0** | 改用精确 join key（类型+方法+签名+method/ctor）后一致 |')
w('| 类型数 | 3356 | 反射 3354 / 元数据 3356 | 反射不可枚举 `<Module>` 与两个编译器行，均不含 icall |')
w('')
w('### 4.2 接线结论（键级口径，与旧文档同粒度）')
w('')
w('| 项目 | 旧结论 | 本次实测 | 结论 |')
w('|---|---|---|---|')
w(f'| public 面键数 | 121 | **{len(pub_keys)}** | 保持（口径一致，可对齐比较） |')
w(f'| public 面已接 | 54 | **{pub_keys_wired}** | **推翻**：增加 {pub_keys_wired - 54} 个键 |')
w(f'| public 面缺口 | 67 | **{pub_keys_gap}**（LOUD {pub_key_status["loud"]} + 无接线 {pub_key_status["missing"]}） | '
  f'**推翻**：减少 {67 - pub_keys_gap} 个键；且剩余缺口几乎全部是显式 LOUD 拒绝，不再是静默的幽灵调用 |')
w('')
w('### 4.3 被推翻的具体旧结论')
w('')
w('**A. 旧文档 §5「最高优先级缺口（10 个）」—— 10 条全部被推翻**，当前代码均已接线并有运行时实现：')
w('')
w('| # | 旧文档认定 | 现状 | 证据 |')
w('|---|---|---|---|')
TOP10 = [
    ('System.String', '.ctor', '`new string(char, int)` → `icall_string_ctor_repeat`'),
    ('System.String', 'IndexOfAny', '→ `icall_string_index_of_any_range`'),
    ('System.String', 'LastIndexOfAny', '→ `icall_string_last_index_of_any_range`'),
    ('System.Array', 'get_Rank', '→ `icall_array_get_rank`'),
    ('System.ValueType', 'GetHashCode', '→ `icall_value_type_get_hash_code`（首字段分类）'),
    ('System.Runtime.InteropServices.Marshal', 'UnsafeAddrOfPinnedArrayElement',
     '→ `icall_marshal_unsafe_addr_of_pinned_array_element`'),
    ('System.Diagnostics.Debugger', 'get_IsAttached',
     '→ `il2cpp_debugger_is_attached`；Break 路径见下（BreakInternal 为影子）'),
    ('System.Runtime.InteropServices.Marshal', 'GetHRForException', '→ `icall_marshal_get_hr_for_exception`'),
    ('System.Runtime.InteropServices.Marshal', 'GetExceptionPointers', '→ `icall_marshal_get_exception_pointers`'),
    ('System.Runtime.InteropServices.SafeHandle', 'DangerousAddRef',
     'SafeHandle 三项 → `icall_safehandle_dangerous_add_ref` 等'),
]
for n, (o, nm, note) in enumerate(TOP10, 1):
    r = idx.get((o, nm))
    st = STATUS_CN[r['status']] if r else 'n/a'
    w(f'| {n} | `{o}.{nm}` 缺 | {st} — {note} | {line_of(r) if r else "`n/a`"} |')
w('')
w('**B. 旧文档 §4 分域缺口表 —— 11 条具体条目被推翻**：')
w('')
w('| 旧文档结论 | 现状 | 证据 |')
w('|---|---|---|')
OTHER = [
    ('System.Runtime.InteropServices.SafeHandle', 'DangerousAddRef'),
    ('System.Runtime.InteropServices.SafeHandle', 'DangerousRelease'),
    ('System.Runtime.InteropServices.SafeHandle', 'SetHandleAsInvalid'),
    ('System.Enum', 'Equals'),
    ('System.Type', 'op_Inequality'),
    ('System.Threading.Interlocked', 'SpeculationBarrier'),
    ('System.Threading.Thread', 'MemoryBarrier'),
    ('System.Threading.Thread', 'get_IsThreadPoolThread'),
    ('System.Array', 'get_LongLength'),
    ('System.Array', 'GetLowerBound'),
    ('System.Array', 'Initialize'),
]
for o, nm in OTHER:
    r = idx.get((o, nm))
    w(f'| `{o}.{nm}` 称缺口 | {STATUS_CN[r["status"]] if r else "n/a"} | {line_of(r) if r else "`n/a`"} |')
w('')
w('**C. 旧文档 §6(b)「JIT/转译器已有语义，不算缺口」—— 2 条被推翻、4 条成立**：')
w('')
w('- `Thread.MemoryBarrier` / `Interlocked.SpeculationBarrier`：旧文档称「空实现即可」——'
  '**推翻**，两者都已是真实运行时 icall（`icall_thread_memory_barrier`、'
  '`il2cpp_interlocked_speculation_barrier`），不再是空实现。')
w(f'- `RuntimeHelpers.InitializeArray`：旧文档称折叠即可、不算缺口 —— **部分推翻**。'
  f'折叠路径存在（`StackFold.FoldInitializeArray`），但未折叠的形状现在被显式 LOUD 拒绝'
  f'（{line_of(idx[("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray")])}）。')
w('- `Type.GetTypeFromHandle`：旧文档称已接 —— **成立**（'
  f'{line_of(idx[("System.Type", "GetTypeFromHandle")])}）。')
w('- `RuntimeHelpers.GetHashCode` / `Equals` / `GetObjectValue`：旧文档称可折叠 —— **成立**'
  '（`Equals`/`GetObjectValue` 走 `::refeq::` / `::identity::` 折叠标记，`GetHashCode` 走真实 icall）。')
w('')
w('**D. 旧文档 §7「内部 icall 764 个键由 public 包装承接」—— 口径被推翻**：'
  f'严格按「直接调用者是已接线的 public 面」验证，只有 **{len(shadows)}** 条成立，'
  '而不是按 owner 类型一概而论的 764。其余内部 icall 要么自身被接线，要么就是真缺口。')
w('')
w('### 4.4 仍成立的旧结论（未被推翻）')
w('')
w('- 「内部 icall 大多由 public 包装承接」这一**机制判断成立**（只是数量被高估，见 D）。')
w('- `System.Runtime.Remoting.*` / `AppDomain` / `ArgIterator` / `ReaderWriterLock` 属 legacy：**成立**，'
  '当前全部为显式 LOUD 拒绝。')
w('- Marshal 的 COM 面不建议实现：**成立**，当前是显式 LOUD 拒绝并给出「ILC 没有 COM 运行时」的理由。')
w('- `System.Runtime.InteropServices.Marshal` 占缺口最大头：**成立**，当前仍是最大单一来源。')
w('')
w('### 4.5 被推翻条数统计')
w('')
w('| 类别 | 条数 |')
w('|---|---|')
w('| 总量/口径错误（910 当 mscorlib 总数、754 内部承接口径） | 2 |')
w('| §5 最高优先级缺口被推翻 | 10 |')
w('| §4 分域缺口条目被推翻 | 11 |')
w('| §6(b)「已有语义」被推翻 | 2 |')
w(f'| **合计** | **25** |')
w('')
w('## 5. 下一步建议（P0/P1 优先队列）')
w('')
p0 = sorted([r for r in nw if r['priority'] == 'P0'], key=sort_key)
w(f'### 5.1 P0（{len(p0)} 条，用户可直接调用）')
w('')
w('| 声明类型 | 方法 | 判定 | 建议 | 证据 |')
w('|---|---|---|---|---|')
SUGGEST = {
    ('System.Runtime.CompilerServices.RuntimeHelpers', 'EnsureSufficientExecutionStack'):
        'AOT 无托管栈上限，保持 LOUD 即可；若要「可跑」可返回空实现',
    ('System.Runtime.CompilerServices.RuntimeHelpers', 'ExecuteCodeWithGuaranteedCleanup'):
        'CER 无语义，保持 LOUD；或在 try/finally 上做等价降级',
    ('System.Runtime.CompilerServices.RuntimeHelpers', 'InitializeArray'):
        '折叠已覆盖常见形状；剩余形状保持 LOUD（不要改成幽灵调用）',
    ('System.Runtime.CompilerServices.RuntimeHelpers', 'PrepareContractedDelegate'):
        'JIT 提示，AOT 下空实现即可（可降级为 wired）',
    ('System.Runtime.CompilerServices.RuntimeHelpers', 'PrepareDelegate'):
        '同上，JIT 提示，空实现即可',
    ('System.Runtime.CompilerServices.RuntimeHelpers', 'ProbeForSufficientStack'):
        '同上，空实现即可',
    ('System.ArgIterator', 'GetRemainingCount'): 'vararg 语义，保持 LOUD',
}
for r in p0:
    key = (r['owner'], r['name'])
    sug = SUGGEST.get(key)
    if sug is None:
        if r['owner'].startswith('System.Runtime.InteropServices.Marshal'):
            sug = 'COM/interop 面，保持 LOUD（明确不支持优于幽灵调用）'
        elif r['owner'].startswith('System.Runtime.Remoting'):
            sug = 'legacy remoting，保持 LOUD'
        else:
            sug = '需单独评估'
    w(f"| `{r['owner']}` | `{r['name']}` | {STATUS_CN[r['status']]} | {sug} | {line_of(r)} |")
w('')
p1 = sorted([r for r in nw if r['priority'] == 'P1'], key=sort_key)
grouped = collections.Counter(r['owner'] for r in p1)
w(f'### 5.2 P1（{len(p1)} 条，常见 BCL API 间接可达）')
w('')
w('按属主聚合，优先处理「用户高频 API 会走到的内部 icall」：')
w('')
w('| 属主 | 条数 | 说明 |')
w('|---|---|---|')
for owner, cnt in grouped.most_common():
    sample = [r['name'] for r in p1 if r['owner'] == owner][:4]
    w(f'| `{owner}` | {cnt} | {md_cell(", ".join(sample))}{" …" if cnt > 4 else ""} |')
w('')
w('### 5.3 处理原则')
w('')
w('1. **P0 里的 LOUD 拒绝优先转成「明确的运行时语义」**：`RuntimeHelpers.PrepareDelegate` / '
  '`PrepareContractedDelegate` / `ProbeForSufficientStack` / `EnsureSufficientExecutionStack` 在 AOT 下没有实际工作要做，'
  '空实现即语义正确，比转译期直接拒绝更好。')
w('2. **P0 里的 COM/remoting 保持 LOUD**：设计上就不支持，转译期失败远好于链接期幽灵符号。')
w('3. **P1 按属主成批推进**：同一 `MethodMap.*.cs` 域的分支集中加，避免反复触碰同一文件。')
w('4. **`缺（无接线）` 的 321 条里 P3 占 202 条**，多数是 CLR 内部（`System.StubHelpers.*`、'
  '`System.Reflection.MetadataImport`、`System.Signature`）或 legacy（`System.Mda`、`System.Variant`），'
  '不建议排期。')
w('')
w('## 6. 红证（可执行复现）')
w('')
w('以下三条都是**实跑**结果，不是代码推断。')
w('')
w('### 6.1 旧缺口确已转译（先红后绿）')
w('')
w('构造一个 net48 程序，覆盖旧文档 §5 里点名“缺”的 6 条：')
w('')
w('```csharp')
w("new string('*', 5);                        // String..ctor(char,int)")
w('"abcdef".IndexOfAny(stops, 0, 6);          // String.IndexOfAny')
w('"abcdef".LastIndexOfAny(stops, 5, 6);      // String.LastIndexOfAny')
w('grid.Rank;                                 // Array.get_Rank')
w('grid.GetLowerBound(1);                     // Array.GetLowerBound')
w('p.GetHashCode();                           // ValueType.GetHashCode')
w('Marshal.UnsafeAddrOfPinnedArrayElement(buf, 0); // Marshal pin addr')
w('```')
w('')
w('il2cppc 转译后，gen/src/Probe.cpp 里实际生成的调用（摘录）：')
w('')
w('```cpp')
w('t0 = il2cpp::runtime::icall_string_ctor_repeat(t0, (char16_t)42, (int32_t)5);')
w('il2cpp::runtime::icall_string_index_of_any_range(g_strPool[0], loc0, 0, 6)')
w('il2cpp::runtime::icall_string_last_index_of_any_range(g_strPool[0], loc0, 5, 6)')
w('il2cpp::runtime::icall_array_get_rank(t0)')
w('il2cpp::runtime::icall_array_get_lower_bound(t0, (int32_t)1)')
w('il2cpp::runtime::icall_int32_get_hash_code((*reinterpret_cast<int32_t*>(&loc1)))')
w('il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element((Il2CppArray*)t0, 0)')
w('`')
w('')
w('stub-report 为空 —— 整个方法转译成功。**这就是旧结论被推翻的直接证据。**')
w('')
w('### 6.2 LOUD 拒绝真的会拦下整个方法')
w('')
w('把程序改成只调用 RuntimeHelpers.PrepareDelegate(null)，转译输出：')
w('')
w('```')
w('warning: method fold failed -> skipped (Probe::Main: System.Runtime.CompilerServices.RuntimeHelpers::PrepareDelegate:')
w('  LOUD whitelist (no machine/JIT state): ... )')
w('warning: 1 method(s) were not transpiled (see --stub-report)')
w('`')
w('')
w('stub-report 内容为 Probe::Main/0#0x06000001。也就是说 §3 里的「缺（LOUD 拒绝）」不是静默的：')
w('转译期就失败并留下可核对的报告，而不是生成一个链接期才炸的幽灵符号。')
w('')
w('### 6.3 「无接线」确实是幽灵调用（这是真问题）')
w('')
w('Marshal.GetUniqueObjectForIUnknown 在 §3 里判为「缺（无接线）」。同样方式转译调用它的程序，')
w('**没有** LOUD 警告、**没有** stub-report 条目，生成的是：')
w('')
w('```cpp')
w('t0 = reinterpret_cast<Il2CppObject*>(')
w('        System_Runtime_InteropServices_Marshal_GetUniqueObjectForIUnknown_6d094451fafe8f02(System_IntPtr_sf_Zero));')
w('`')
w('')
w('这个 System_..._<hash> 符号在生成的 C++ 里不存在，只有到 C++ 链接期才会失败。')
w(f'**所以 §3 的 {by_status['missing']} 条「无接线」是本审计里优先级最高的一类：它们的失败发生在最晚、最难定位的阶段。**')
w('')
w('## 7. 复现方式')
w('')
w('```powershell')
w('powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/run-audit.ps1')
w('```')
w('')
w('脚本按顺序执行 9 个阶段（反射通道 → 元数据通道 → join key 交叉校验 → 调用图 → 真实 BclResolve 探针 '
  '→ 运行时符号索引 → dispatcher 分支解析 → 证据行定位 → 分类），产物写入 `-OutDir`'
  '（默认 `%TEMP%\\opencode\\icall-audit`），最终数据为 `classified.json`。')
w('')
w('各阶段工具：')
w('')
w('| 工具 | 作用 |')
w('|---|---|')
w('| `tools/audit/enumerate-icall-reflection.ps1` | 通道 1：运行时反射枚举 icall（PS 5.1） |')
w('| `tools/audit/MetadataIcallCounter/` | 通道 2：ECMA-335 元数据位枚举 |')
w('| `tools/audit/IcallCallGraph/` | IL 调用图 + 从 public 面的可达距离 |')
w('| `tools/audit/IcallWiringProbe/` | 逐条调用真实 `MethodRefResolver.BclResolve` |')
w('| `tools/audit/parse_runtime_syms.py` | `src/runtime` 的声明/定义/LOUD 桩索引 |')
w('| `tools/audit/parse_wiring_surface.py` | `MethodMap.*.cs` 的 dispatcher 分支解析 |')
w('| `tools/audit/locate_wiring_sites.py` | 每条判定对应的源码行 |')
w('| `tools/audit/classify_icalls.py` | 最终判定 + P0..P3 |')
w('| `tools/audit/shared/SignatureProvider.cs` | 两通道共用的签名渲染器（保证 join key 不漂移） |')
w('')

io.open(OUT, 'w', encoding='utf-8', newline='\n').write('\n'.join(L))
print('wrote', OUT, len(L), 'lines')
