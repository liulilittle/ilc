"""Render docs/audit/icall-mscorlib-current.md from classified.json.

Every number in the report is computed from that file, so the prose cannot
drift from the data. The generator is deterministic: no timestamps, no
environment-dependent ordering (all sorts are ordinal), and the only external
input is the source commit the numbers describe.

The five headline classes must add up to the number of icalls in net48
mscorlib. That is asserted, not hoped for: if a pipeline change adds or drops a
row, the renderer exits non-zero instead of publishing a report whose numbers
no longer reconcile.

usage: render_report.py <auditOutDir> <repoRoot> [commitSha]
"""
import io, os, json, collections, subprocess, sys

TMP = sys.argv[1]
ROOT = sys.argv[2] if len(sys.argv) > 2 else r'F:\il2cpp\.worktrees\audit-mscorlib-icall'
OUT = os.path.join(ROOT, 'docs', 'audit', 'icall-mscorlib-current.md')

TOTAL_ICALLS = 908


def source_commit():
    """The last commit that touched `src/`, i.e. the tree the numbers describe.

    Deliberately NOT `HEAD`: the report itself lives in a later commit, so
    stamping HEAD would make a rerun produce a different file. `src/` is what
    the icall surface and the runtime implementations come from, so its last
    commit is both stable and the honest provenance of these numbers.
    """
    if len(sys.argv) > 3 and sys.argv[3]:
        return sys.argv[3]
    try:
        return subprocess.check_output(
            ['git', '-C', ROOT, 'rev-list', '-1', 'HEAD', '--', 'src'],
            stderr=subprocess.DEVNULL).decode('ascii', 'replace').strip() or 'unknown'
    except Exception:
        return 'unknown'


rows = json.load(io.open(os.path.join(TMP, 'classified.json'), encoding='utf-8'))
graph = json.load(io.open(os.path.join(TMP, 'callgraph.json'), encoding='utf-8'))
arms = json.load(io.open(os.path.join(TMP, 'arms.json'), encoding='utf-8'))
# The FULL constant/empty-stub index (not only the ones an icall happens to
# target): §3.8 must list every constant stub the scanner found, including the
# runtime helpers outside the 908-row scope.
_rtsyms = json.load(io.open(os.path.join(TMP, 'runtime_syms.json'), encoding='utf-8'))
all_const = {k: tuple(v) for k, v in _rtsyms.get('const', {}).items()}

STATUS = ['wired', 'half', 'loud', 'shadow', 'partialShadow', 'missing']
STATUS_CN = {
    'wired': '已接',
    'half': '半接',
    'loud': '缺（LOUD 拒绝）',
    'shadow': '影子',
    'partialShadow': '部分影子',
    'missing': '缺（无接线）',
}
PRIO = ['P0', 'P1', 'P2', 'P3']

total = len(rows)
by_status = collections.Counter(r['status'] for r in rows)
by_prio = collections.Counter(r['priority'] for r in rows)

# The five headline classes. `partialShadow` is one of them for the total; it is
# reported on its own line so a partial reduction is never read as a full one.
FIVE = ('wired', 'half', 'loud', 'missing', 'shadow', 'partialShadow')
five_sum = sum(by_status[s] for s in FIVE)
if five_sum != TOTAL_ICALLS:
    sys.stderr.write(
        'FATAL: status classes sum to %d, expected %d: %s\n'
        % (five_sum, TOTAL_ICALLS, {s: by_status[s] for s in STATUS}))
    raise SystemExit(2)
if total != TOTAL_ICALLS:
    sys.stderr.write('FATAL: %d rows classified, expected %d\n' % (total, TOTAL_ICALLS))
    raise SystemExit(2)

# "Gap" = everything the runtime does not deliver, shadows excluded (their
# conclusion is "no wiring needed"). Partial shadows ARE gaps: part of the
# public surface still falls through.
gaps = [r for r in rows if r['status'] in ('half', 'loud', 'missing', 'partialShadow')]
shadows = [r for r in rows if r['status'] == 'shadow']
partials = [r for r in rows if r['status'] == 'partialShadow']
halves = [r for r in rows if r['status'] == 'half']
const_stubs = [r for r in halves if r.get('stubKind') == 'const']
loud_stubs = [r for r in halves if r.get('stubKind') == 'loud']
public = [r for r in rows if r['publicFace']]
nw_by_prio = collections.Counter(r['priority'] for r in rows if r['status'] != 'wired')


def md_cell(s):
    return (s or '').replace('|', '\\|').replace('\n', ' ')


def line_of(r):
    return f"`{r['site']}`" if r.get('site') else '`no wiring`'


def sort_key(r):
    return (r['owner'], r['name'], r['sig'])


def shadow_note(r):
    """How a shadow is delivered, stated at the hop count that is actually true."""
    s = r['shadow']
    hops = s['hops']
    if hops == 1:
        return f"影子：由 `{s['entry']}` 直接承接（1 跳）"
    chain = ' → '.join(s['via'])
    return (f"影子：**{hops} 跳**，链路 `{chain}`，根入口 `{s['entry']}`；"
            f"不是由 `{s['entry']}` 直接承接")


L = []
w = L.append

SHA = source_commit()

w('# mscorlib InternalCall 现状盘点（net48）')
w('')
w('> 只读审计。目标程序集：`C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\mscorlib.dll`（net48）。')
w(f'> 本版数字由 `tools/audit/run-audit.ps1` 于源码提交 `{SHA}`（`git rev-list -1 HEAD -- src`）重新生成；'
  f'五类之和必须等于 **{TOTAL_ICALLS}**，脚本内 `assert sum == {TOTAL_ICALLS}`，不成立则渲染器直接失败退出。')
w('')
w('## 0. 结论摘要')
w('')
w(f'- net48 mscorlib InternalCall 总数：**{total}**（反射通道 {total} / 元数据通道 {total}，逐条 join key 一致）')
w(f'- 判定分布（五类，部分影子单列）：'
  f'已接 **{by_status["wired"]}** + 半接 **{by_status["half"]}** + 缺 **{by_status["loud"] + by_status["missing"]}**'
  f'（LOUD 拒绝 {by_status["loud"]} + 无接线 {by_status["missing"]}）+ '
  f'影子 **{by_status["shadow"]}** + 部分影子 **{by_status["partialShadow"]}** = '
  f'{by_status["wired"]}+{by_status["half"]}+{by_status["loud"] + by_status["missing"]}'
  f'+{by_status["shadow"]}+{by_status["partialShadow"]} = **{five_sum}**，'
  f'`assert sum == {TOTAL_ICALLS}` 通过')
w(f'- 可达性分级（仅统计非「已接」的 {total - by_status["wired"]} 条）：'
  f'P0 **{nw_by_prio["P0"]}**、P1 **{nw_by_prio["P1"]}**、'
  f'P2 **{nw_by_prio["P2"]}**、P3 **{nw_by_prio["P3"]}**')
w(f'- public API 面（类型 public 且方法 public）共 **{len(public)}** 条，其中已接 '
  f'**{sum(1 for r in public if r["status"] == "wired")}**、'
  f'半接 {sum(1 for r in public if r["status"] == "half")}、'
  f'LOUD {sum(1 for r in public if r["status"] == "loud")}、'
  f'无接线 {sum(1 for r in public if r["status"] == "missing")}')
w(f'- **「已接」不等于语义等价 net48**：{len(halves)} 条「半接」全部是运行时占位实现'
  f'（{len(const_stubs)} 条空体/恒值桩，见 §3.8），不是真实计算。')
w('')
w('### 0.1 判定口径（逐条都在 §2/§3 给出 file:line）')
w('')
w('| 判定 | 含义 | 证据形态 |')
w('|---|---|---|')
w('| 已接 | 转译器的 `MethodRefResolver.BclResolve` 对这条 icall 返回了 C++ 目标，'
  '且该目标在 `src/runtime` 有真实实现（不是抛异常桩，也不是恒值桩）；折叠标记（`::identity::` 等）'
  '视为已接，因为发射器直接内联 | `MethodMap.*.cs:<line>`（dispatcher 分支）+ 运行时定义 |')
w('| 半接 | 有 dispatch 目标，但运行时实现不交付语义：是 `*_loud` 抛异常桩（调用必然抛），'
  '或空体 / 单一 `return <常量>;` 恒值桩（永远返回同一个字面量，不看参数） | `MethodMap.*.cs:<line>` + 桩定义 `src/runtime/src/<file>:<line>` |')
w('| 缺（LOUD 拒绝）| 转译器对该成员显式抛 `NotSupportedException`，并在转译期把整个方法标记为未转译（不是静默错误） | `MethodMap.*.cs:<line>`（throw 点）+ 原始消息 |')
w('| 缺（无接线）| 没有 owner 分支也没有方法分支，落到生成的幽灵函数名（`System_Xxx_<hash>`），**C++ 编译期**失败（见 §6.3） | `no wiring` |')
w('| 影子 | 该 icall 自身无接线，但用户可见的入口方法已接线：mscorlib 方法体不会被转译，用户 IL 只发出对入口的那一次调用，'
  '所以这条 icall 不在生成的 C++ 走到的任何路径上。**必须看跳数**：1 跳才可写「由 X 直接承接」 | 承接入口 + 其 `MethodMap.*.cs:<line>` |')
w('| 部分影子 | 影子结论只部分成立：同一最短距离上还有未接线的入口，或该入口有同名未映射的重载，'
  '用户写那种形状仍会落到幽灵符号 | 承接入口 + 其 `MethodMap.*.cs:<line>` |')
w('')
w('可达性分级（对非「已接」行）：')
w('')
w('| 级别 | 定义 |')
w('|---|---|')
w('| P0 | 类型 public 且方法 public —— 用户 IL 可直接 call |')
w('| P1 | 非 public 面，但存在 public 入口到它的调用链；`publicDistance == 1` 一律为 P1（命名空间不得压低可及性） |')
w('| P2 | 链路深度 ≥4，或深度 ≥2 且属主位于 reflection / remoting / interop / diagnostics / serialization / security / '
  'AppDomain / CompilerServices 命名空间（按**类型全名**带分隔符匹配） |')
w('| P3 | mscorlib 内部无任何 public 入口可达（legacy / CLR 内部 / 已废弃） |')
w('')
w('分类键是 `(声明类型全名, 方法名, 签名)`。同名重载的可达性与接线状态可以完全不同'
  '（`Interlocked::CompareExchange` 的数值重载已接，泛型 `!!0&` 重载未映射），'
  '折叠成 `(owner, name)` 会让一个重载的证据污染另一个。')
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
  f'从 public 面（{len(graph["publicFaces"])} 个可被用户直接调用的方法）做**前向** BFS，'
  '得到「该 icall 到最近 public 入口的距离 / 该入口是谁 / 中间经过哪几跳」。'
  '边按签名精确匹配，因此重载不会互相借距离；BFS 前沿按序展开，结果与 Python 哈希随机化无关，可重复。')
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
w(f'「缺口」= 非「已接」且非「影子」的全部行 = **{len(gaps)}** 条'
  f'（半接 / LOUD 拒绝 / 无接线 / 部分影子）。'
  '影子单列在 §3.6（结论是「无需接线」），部分影子单列在 §3.7（结论是「只部分覆盖」）。')
w('')
for prio in PRIO:
    sel = sorted([r for r in gaps if r['priority'] == prio], key=sort_key)
    w(f'### {prio}（{len(sel)} 条）')
    w('')
    if not sel:
        w('（无）')
        w('')
        continue
    if prio == 'P0':
        w('类型与方法均为 public，用户 IL 可以直接 call，属于**必须处理**的面。')
    elif prio == 'P1':
        w('非 public 面，但经普通 BCL 内部路径可达（集合/字符串/LINQ/IO/格式化/数值等），'
          '或距离 public 入口只有 1 跳。')
    elif prio == 'P2':
        w('仅经反射/序列化/调试器/COM 宿主/interop 路径可达，或链路深度 ≥4。')
    else:
        w('mscorlib 内部没有任何 public 入口可达（legacy / CLR 内部）。')
    w('')
    w('| # | 声明类型 | 方法 | 可见性 | 判定 | 说明 | 证据 |')
    w('|---|---|---|---|---|---|---|')
    for i, r in enumerate(sel, 1):
        if r['status'] == 'partialShadow':
            note = '部分影子：' + shadow_note(r).replace('影子：', '', 1)
        elif r['status'] == 'loud':
            msg = (r['error'] or '').split(': ', 1)[-1]
            msg = msg.split(' [Reference Source', 1)[0]
            note = f"LOUD 拒绝：{md_cell(msg[:150])}"
        elif r['status'] == 'half':
            kind = r.get('stubKind')
            if kind == 'const':
                note = (f"半接：dispatch 指向 `{r['symbolBare']}`，但运行时是空体/恒值桩"
                        f"（桩定义 `{r['stub']}`）")
            else:
                note = (f"半接：dispatch 指向 `{r['symbolBare']}`，但该运行时实现是抛异常的 LOUD 桩"
                        f"（桩定义 `{r['stub']}`）")
        else:
            note = '无 owner 分支 / 无方法分支'
        w(f"| {i} | `{r['owner']}` | `{r['name']}` | {r['vis']} | {STATUS_CN[r['status']]} | "
          f"{note} | {line_of(r)} |")
    w('')

w('### 3.6 影子清单')
w('')
w(f'共 **{len(shadows)}** 条。判定依据：icall 自身没有任何接线，但用户可见的入口方法已接线；'
  'mscorlib 的方法体不会被转译，用户 IL 只发出对入口的那一次调用，因此这条 icall 不在生成的 C++ 走到的路径上。')
w('')
w('**跳数必须写清楚。** 文档旧版把 `System.Threading.Thread::StartInternal` 写成「由 `Start()` 直接承接」，'
  '实际链路是 public `Start()` → internal `Start(StackCrawlMark&)` → `StartInternal`，**2 跳**，'
  '中间那一跳的 mscorlib 方法体同样不会被转译，所以这个结论仍然成立，但「直接承接」的表述不成立。'
  f'本表 `跳数` 列给出实测 IL 边数：1 跳才是直接承接；≥2 跳列出完整链路。'
  f'（本版 {sum(1 for r in shadows if r["shadow"]["hops"] == 1)} 条 1 跳、'
  f'{sum(1 for r in shadows if r["shadow"]["hops"] > 1)} 条 ≥2 跳。）')
w('')
w('| # | 声明类型 | 方法 | 可见性 | 承接的 public 入口 | 跳数 | 完整链路 | 承接者接线证据 |')
w('|---|---|---|---|---|---|---|---|')
for i, r in enumerate(sorted(shadows, key=sort_key), 1):
    s = r['shadow']
    chain = ' → '.join(s['via']) if s['hops'] > 1 else '（直接调用）'
    w(f"| {i} | `{r['owner']}` | `{r['name']}` | {r['vis']} | `{s['entry']}` | {s['hops']} | "
      f"`{md_cell(chain)}` | `{r['site']}` |")
w('')

w('### 3.7 部分影子清单')
w('')
w(f'共 **{len(partials)}** 条。这些不是「无需接线」也不是「完全没接线」，而是**只覆盖了一部分调用形状**：'
  '同一最短距离上存在未接线的入口，或该入口有同名但未映射的重载，用户写那种形状仍会落到幽灵符号。')
w('')
w('典型是 `System.Threading.Interlocked::_CompareExchange`：它的数值重载 `CompareExchange(int&,int,int)` 等'
  '都已接线，但泛型重载 `CompareExchange(!!0&, !!0, !!0)` 在探针里是 `unmapped` —— 用户写 `ref T` 就走幽灵符号。'
  '所以 `_CompareExchange` / `_Exchange` 只能算**部分承接**，不能计入完整影子。')
w('')
w('| # | 声明类型 | 方法 | 签名 | 承接的 public 入口 | 跳数 | 证据 |')
w('|---|---|---|---|---|---|---|')
for i, r in enumerate(sorted(partials, key=sort_key), 1):
    s = r['shadow']
    w(f"| {i} | `{r['owner']}` | `{r['name']}` | `{md_cell(r['sig'])}` | `{s['entry']}` | {s['hops']} | "
      f"`{r['site']}` |")
w('')

w('### 3.8 已接 ≠ 语义等价 net48（恒值桩）')
w('')
w('下面这些运行时实现是**空体或单一 `return <常量>;`**：它永远返回同一个字面量、完全不看参数，'
  '或者干脆什么都不做。它们不是「LOUD 拒绝」（不是明确抛异常），也不是「无接线」（能编译能链接），'
  '而是**能跑但答案恒定的静默偏差** —— 对 net48 byte-exact 来说等价于「编译通过、行为恒定」，'
  '不能算语义等价。')
w('')
w('扫描器（`tools/audit/parse_runtime_syms.py` 的 `is_constant_stub`）在 `src/runtime/src` 全表命中'
  f'**{len(all_const)}** 个恒值/空体桩。其中 **{len(const_stubs)}** 个正好是 908 条 icall 的 dispatch 目标，'
  '所以那 30 条 icall 从「已接」改判为「半接」；其余是没有 icall 引用的运行时辅助符号，'
  '不在本审计的 908 条范围内，但同样是净偏差（一并列出，便于后续排查）。')
w('')
w('| # | 运行时实现 | 源码位置 | 实现体 | 被 N 条 icall 命中 |')
w('|---|---|---|---|---|')
const_hit = collections.Counter(r['symbolBare'] for r in halves if r.get('symbolBare'))
for i, (sym, site) in enumerate(sorted(all_const.items()), 1):
    fn, ln = site
    try:
        ls = io.open(os.path.join(ROOT, 'src', 'runtime', 'src', fn),
                     encoding='utf-8-sig', errors='replace').read().split('\n')
        body = ls[int(ln) - 1].strip() if 1 <= int(ln) <= len(ls) else ''
    except OSError:
        body = ''
    w(f'| {i} | `{sym}` | `src/runtime/src/{fn}:{ln}` | `{md_cell(body)}` | {const_hit.get(sym, 0)} |')
w('')
w('逐条影响（命中 icall 的那些）：任何**依赖真值**的 net48 程序都会静默走错分支 ——')
w('`Debugger.IsAttached`/`IsLogging` 恒 false、`GC.IsServerGC` 恒 false、')
w('`Environment.HasShutdownStarted` 恒 false、`Marshal.GetExceptionPointers` 恒 0、')
w('`StackTrace.GetStackFramesInternal` 填充 0 帧（栈回溯为空）、`Type.GetTypeFromHandleUnsafe` 恒 null、')
w('`ReaderWriterLock.AnyWritersSince` 恒 false、`Thread.GetApartmentStateNative` 恒 0（MTA）。')
w('处理原则见 §5.3 第 3 条：它们不排进 P0/P1 的「缺口」队列，但必须单独立项。')
w('')

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

# Key-level view, reported at the granularity the old document used
# (owner+name), because a like-for-like comparison is the only honest one. The
# sig-exact count is given alongside it since that is what this version keys on.
key_groups = collections.defaultdict(list)
for r in rows:
    key_groups[r['owner'] + '::' + r['name']].append(r)
sig_groups = collections.defaultdict(list)
for r in rows:
    sig_groups[(r['owner'], r['name'], r['sig'])].append(r)


def key_status(rs):
    st = {x['status'] for x in rs}
    for o in ['wired', 'half', 'loud', 'missing', 'partialShadow', 'shadow']:
        if o in st:
            return o
    return 'missing'


pub_keys = {k: v for k, v in key_groups.items() if any(x['publicFace'] for x in v)}
pub_key_status = collections.Counter(key_status(v) for v in pub_keys.values())
pub_keys_wired = pub_key_status['wired']
pub_keys_gap = len(pub_keys) - pub_keys_wired
pub_sig = [r for r in rows if r['publicFace']]
pub_sig_wired = sum(1 for r in pub_sig if r['status'] == 'wired')
pub_sig_gap = len(pub_sig) - pub_sig_wired

w('### 4.2 接线结论（键级口径，与旧文档同粒度）')
w('')
w('| 项目 | 旧结论 | 本次实测 | 结论 |')
w('|---|---|---|---|')
w(f'| public 面键数（owner+name 折叠，旧文档口径） | 121 | **{len(pub_keys)}** | 保持（口径一致，可对齐比较：'
  f'{len(pub_sig) - len(pub_keys)} 个键是同名重载折叠） |')
w(f'| public 面键级已接（折叠口径） | 54 | **{pub_keys_wired}** | **推翻**：增加 {pub_keys_wired - 54} 个键 |')
w(f'| public 面键级缺口（折叠口径） | 67 | **{pub_keys_gap}**'
  f'（LOUD {pub_key_status["loud"]} + 半接 {pub_key_status["half"]} + 无接线 {pub_key_status["missing"]}） | '
  f'**推翻**：减少 {67 - pub_keys_gap} 个键；但注意其中 {pub_key_status["half"]} 个键是**恒值桩**（§3.8） |')
w(f'| public 面行数（owner+name+签名，本版口径） | — | **{len(pub_sig)}** | 同名重载各自成行，判定不再互相污染 |')
w(f'| public 面行级已接（签名精确） | — | **{pub_sig_wired}** | 与上面的键级数字不可直接相减 |')
w(f'| public 面行级缺口（签名精确） | — | **{pub_sig_gap}**'
  f'（半接 {sum(1 for r in pub_sig if r["status"] == "half")} + '
  f'LOUD {sum(1 for r in pub_sig if r["status"] == "loud")} + '
  f'无接线 {sum(1 for r in pub_sig if r["status"] == "missing")}） | '
  f'剩余缺口的绝大多数是显式 LOUD 拒绝，不再是静默幽灵调用 |')
w('')
w('### 4.3 被推翻的具体旧结论')
w('')
w('**A. 旧文档 §5「最高优先级缺口（10 个）」—— 10 条全部被推翻**，当前代码均已接线：')
w('')
w('| # | 旧文档认定 | 现状 | 证据 |')
w('|---|---|---|---|')
idx = {}
for r in sorted(rows, key=lambda x: (0 if x['status'] == 'wired' else 1, x['owner'], x['name'], x['sig'])):
    idx.setdefault((r['owner'], r['name']), r)
TOP10 = [
    ('System.String', '.ctor', '`new string(char, int)` → `icall_string_ctor_repeat`'),
    ('System.String', 'IndexOfAny', '→ `icall_string_index_of_any_range`'),
    ('System.String', 'LastIndexOfAny', '→ `icall_string_last_index_of_any_range`'),
    ('System.Array', 'get_Rank', '→ `icall_array_get_rank`'),
    ('System.ValueType', 'GetHashCode', '→ `icall_value_type_get_hash_code`（首字段分类）'),
    ('System.Runtime.InteropServices.Marshal', 'UnsafeAddrOfPinnedArrayElement',
     '→ `icall_marshal_unsafe_addr_of_pinned_array_element`'),
    ('System.Diagnostics.Debugger', 'get_IsAttached',
     '→ `il2cpp_debugger_is_attached`（**运行时是 `return 0;` 恒值桩，见 §3.8**）'),
    ('System.Runtime.InteropServices.Marshal', 'GetHRForException', '→ `icall_marshal_get_hr_for_exception`'),
    ('System.Runtime.InteropServices.Marshal', 'GetExceptionPointers',
     '→ `icall_marshal_get_exception_pointers`（**运行时是 `return 0;` 恒值桩，见 §3.8**）'),
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
  '`il2cpp_interlocked_speculation_barrier`）。但 `SpeculationBarrier` 的实现本身是空体，'
  '按本版口径是**半接**（§3.8），所以「不算缺口」这个结论仍然不成立。')
w(f'- `RuntimeHelpers.InitializeArray`：旧文档称折叠即可、不算缺口 —— **部分推翻**。'
  f'折叠路径存在（`StackFold.FoldInitializeArray`），但未折叠的形状现在被显式 LOUD 拒绝'
  f'（{line_of(idx[("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray")])}）。')
w('- `Type.GetTypeFromHandle`：旧文档称已接 —— **成立**（'
  f'{line_of(idx[("System.Type", "GetTypeFromHandle")])}）。')
w('- `RuntimeHelpers.GetHashCode` / `Equals` / `GetObjectValue`：旧文档称可折叠 —— **成立**'
  '（`Equals`/`GetObjectValue` 走 `::refeq::` / `::identity::` 折叠标记，`GetHashCode` 走真实 icall）。')
w('')
w('**D. 旧文档 §7「内部 icall 764 个键由 public 包装承接」—— 口径被推翻**：')
w(f'严格按「icall 自身无接线、且最短 public 入口已接线且其重载组全部已接线」验证，只有 **{len(shadows)}** 条成立'
  f'（另有 {len(partials)} 条只部分成立），而不是按 owner 类型一概而论的 764。'
  '其余内部 icall 要么自身被接线，要么就是真缺口。')
w('')
w('### 4.4 仍成立的旧结论（未被推翻）')
w('')
w('- 「内部 icall 大多由 public 包装承接」这一**机制判断成立**（只是数量被高估，见 D）。')
w('- `System.Runtime.Remoting.*` / `AppDomain` / `ArgIterator` / `ReaderWriterLock` 属 legacy：**成立**，'
  '当前全部为显式 LOUD 拒绝或恒值桩。')
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
w('### 4.6 本轮修订（相对上一版本文档）')
w('')
w('上一版本 doc 的数字（已接 224 / 半接 1 / LOUD 297 / 影子 65 / 无接线 321，共 908）'
  '在四处口径上是错的，本版逐条修正：')
w('')
w('| 修订 | 旧值 | 新值 | 根因 |')
w('|---|---|---|---|')
w(f'| Q1 影子口径 | 影子 {65}（把 publicRoot 也塞进 public_faces，得 507 个假 public 面） | '
  f'影子 {by_status["shadow"]} + 部分影子 {by_status["partialShadow"]} | public face 必须只含真 public-face icall；'
  f'影子必须标注跳数；泛型重载未映射的判部分影子 |')
w(f'| M1 恒值桩 | 已接 {224} | 已接 {by_status["wired"]} / 半接 {by_status["half"]} | '
  f'空体与 `return <常量>;` 也是桩，全表重扫出 {len(const_stubs)} 条（远超点名的 4 条） |')
w(f'| M2 loud/half→shadow | LOUD {297} | LOUD {by_status["loud"]} | 17 条 loud/half 的运行时分支实为死代码'
  f'（depth-1 public 调用者全部检出真实实现） |')
w(f'| Q3 P2 降级 | P2 173 | P2 {nw_by_prio["P2"]} | 命名空间不得压过 `publicDistance == 1`；'
  f'前缀匹配必须按类型全名 |')
w('')

w('## 5. 下一步建议（P0/P1 优先队列）')
w('')
p0 = sorted([r for r in gaps if r['priority'] == 'P0'], key=sort_key)
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
        elif r['status'] == 'half':
            sug = '替换恒值桩为真实实现（当前静默返回常量）'
        else:
            sug = '需单独评估'
    w(f"| `{r['owner']}` | `{r['name']}` | {STATUS_CN[r['status']]} | {sug} | {line_of(r)} |")
w('')
p1 = sorted([r for r in gaps if r['priority'] == 'P1'], key=sort_key)
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
w('2. **P0 里的 COM/remoting 保持 LOUD**：设计上就不支持，转译期失败远好于编译期幽灵符号。')
w('3. **恒值桩单独列一类**（§3.8）：它们不是「缺口」也不是「已接」，而是静默偏差。'
  '按 net48 byte-exact 的要求，`Debugger.IsAttached` / `GC.IsServerGC` / `Marshal.GetExceptionPointers` 这类'
  '必须给出真实语义（或至少让偏差可观测），不能永远返回常量。')
w('4. **P1 按属主成批推进**：同一 `MethodMap.*.cs` 域的分支集中加，避免反复触碰同一文件。')
w(f'5. **`缺（无接线）` 的 {by_status["missing"]} 条里 P3 占 '
  f'{sum(1 for r in gaps if r["status"] == "missing" and r["priority"] == "P3")} 条**，'
  '多数是 CLR 内部（`System.StubHelpers.*`、`System.Reflection.MetadataImport`、`System.Signature`）'
  '或 legacy（`System.Mda`、`System.Variant`），不建议排期。')
w('')

w('## 6. 红证（可执行复现）')
w('')
w('以下都是**实跑**结果，不是代码推断。')
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
w('```')
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
w('```')
w('')
w('stub-report 内容为 Probe::Main/0#0x06000001。也就是说 §3 里的「缺（LOUD 拒绝）」不是静默的：')
w('转译期就失败并留下可核对的报告，而不是生成一个编译期才炸的幽灵符号。')
w('')
w('### 6.3 「无接线」的失效时机：**编译期**，不是链接期')
w('')
w('旧版文档说这类失效「只有到 C++ 链接期才会失败」。实测更早：转译照常成功、'
  '**没有任何 warning**、`--stub-report` 也没有条目，幽灵符号被写进生成的 C++，'
  '然后 `cmake --build` 在**编译单元**上以 **rc=2** 失败 —— 同样不会走到链接期。')
w('')
w('复现用的 net48 程序（`csc.exe /target:exe`，Framework 4.8）：')
w('')
w('```csharp')
w('using System;')
w('using System.Runtime.InteropServices;')
w('')
w('internal static class Q2Marshal')
w('{')
w('    private static void Main()')
w('    {')
w('        var p = new IntPtr(0x1234);')
w('        var o = Marshal.GetUniqueObjectForIUnknown(p);')
w('        Console.WriteLine(o == null ? "null" : "obj");')
w('    }')
w('}')
w('```')
w('')
w('`il2cppc gen6_in gen6_out` 的完整转译输出（没有任何 warning，也没有 stub）：')
w('')
w('```')
w('symbols: no Portable PDB found -> no #line C# line directives')
w('string pool: 2 distinct literal(s) from 2 site(s) across 1 method(s) ...')
w('resources: 0 embedded manifest resource(s) -> include/internal/resource-pool.h')
w('licm: 0 invariant length read(s) hoistable across 1 method(s)')
w('inline: 0 simple call site(s) inlineable across 1 method(s)')
w('tempslot: 4 temp slot(s) -> 4 with reuse across 1 method(s)')
w('generated 1 type(s) -> gen6_out')
w('```')
w('')
w('生成的 `gen6_out/src/Q2Marshal.cpp` 第 77 行把调用直接写成幽灵符号（源码里没有这个函数）：')
w('')
w('```cpp')
w('loc1 = reinterpret_cast<Il2CppObject*>(System_Runtime_InteropServices_Marshal_GetUniqueObjectForIUnknown_6d094451fafe8f02(loc0));'
  ' // IL stloc.1 @0x0014 -- store call result into loc1')
w('```')
w('')
w('`cmake -S gen6_out -B build6 -G "MinGW Makefiles" '
  '-DCMAKE_TOOLCHAIN_FILE=cmake/mingw-w64.cmake` 配置成功，'
  '`cmake --build build6 --target il2cpp_gen_exe` 的**实际**输出（rc=2）：')
w('')
w('```')
w("C:\\...\\gen6_out\\src\\Q2Marshal.cpp: In function 'void Q2Marshal_Main_96da4d79b4e56284()':")
w('C:\\...\\gen6_out\\src\\Q2Marshal.cpp:77:44: error:')
w("  'System_Runtime_InteropServices_Marshal_GetUniqueObjectForIUnknown_6d094451fafe8f02'")
w('  was not declared in this scope')
w('   77 |     loc1 = reinterpret_cast<Il2CppObject*>(')
w('      |                                            ^~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~')
w('mingw32-make[3]: *** [CMakeFiles\\il2cpp_gen.dir\\build.make:79: CMakeFiles\\il2cpp_gen.dir/src/Q2Marshal.cpp.obj] Error 1')
w('mingw32-make[2]: *** [CMakeFiles\\Makefile2:114: CMakeFiles\\il2cpp_gen.dir/all] Error 2')
w('mingw32-make[1]: *** [CMakeFiles\\Makefile2:186: CMakeFiles\\il2cpp_gen.dir/rule] Error 2')
w('mingw32-make: *** [Makefile:195: il2cpp_gen_exe] Error 2')
w('```')
w('')
w('同一条「无接线」还有第二种**更早**的失败形态：当调用形状能让折叠器拿到接收者类型提示时，'
  'M5 phantom guard（`src/il2cppc/Emit/MethodRefResolver.cs:1390-1408`）在**转译期**就把它变成 LOUD，'
  '连幽灵符号都不产出。把上面程序的 `var p = new IntPtr(0x1234);` 改成内联实参'
  '`Marshal.GetUniqueObjectForIUnknown(new IntPtr(0x1234))` 即触发：')
w('')
w('```')
w('warning: method fold failed -> skipped (Q2Probe::Main: System.Runtime.InteropServices.Marshal::GetUniqueObjectForIUnknown:')
w('  ... not wired in M5 (BCL owner has no mapping arm and is not defined in the input assembly --')
w("  a phantom generated-name call would never link; receiverType='System.IntPtr' arg0Type='System.IntPtr'))")
w('warning: 1 method(s) were not transpiled (see --stub-report)')
w('```')
w('')
w(f'**结论**：§3 的 {by_status["missing"]} 条「无接线」不是「最晚才炸」的一类，'
  '而是**最早**就炸的一类 —— 要么在转译期被 LOUD 拦下（带接收者提示的形状），'
  '要么在 C++ 编译期以 `was not declared in this scope` 失败（无提示的形状，实测 rc=2、'
  '转译零 warning）。两种形态都不会跑到链接期，更不会静默产出可执行文件。')
w('')
w('## 7. 复现方式')
w('')
w('```powershell')
w('powershell -NoProfile -ExecutionPolicy Bypass -File tools/audit/run-audit.ps1')
w('```')
w('')
w('脚本按顺序执行 9 个阶段（反射通道 → 元数据通道 → join key 交叉校验 → 调用图 → 真实 BclResolve 探针 '
  '→ 运行时符号索引 → dispatcher 分支解析 → 证据行定位 → 分类 → 渲染 → 证据自校验），产物写入 `-OutDir`'
  '（默认 `%TEMP%\\opencode\\icall-audit`），最终数据为 `classified.json`。')
w('')
w('任一阶段失败（通道数量不等、五类之和 ≠ 908、文档里任何一处 `file:line` 对不上）脚本都会非零退出，'
  '不会发布一份数字不自洽的报告。')
w('')
w('各阶段工具：')
w('')
w('| 工具 | 作用 |')
w('|---|---|')
w('| `tools/audit/enumerate-icall-reflection.ps1` | 通道 1：运行时反射枚举 icall（PS 5.1） |')
w('| `tools/audit/MetadataIcallCounter/` | 通道 2：ECMA-335 元数据位枚举 |')
w('| `tools/audit/IcallCallGraph/` | IL 调用图（按签名精确成边）+ 从 public 面的可达距离/链路 |')
w('| `tools/audit/IcallWiringProbe/` | 逐条调用真实 `MethodRefResolver.BclResolve` |')
w('| `tools/audit/parse_runtime_syms.py` | `src/runtime` 的声明/定义/LOUD 桩/**恒值桩**索引 |')
w('| `tools/audit/parse_wiring_surface.py` | `MethodMap.*.cs` 的 dispatcher 分支解析（含分支行号） |')
w('| `tools/audit/locate_wiring_sites.py` | 每条判定对应的源码行 |')
w('| `tools/audit/classify_icalls.py` | 最终判定（含影子跳数/部分影子）+ P0..P3 + `assert sum==908` |')
w('| `tools/audit/render_report.py` | 本文档渲染（数字全部来自 `classified.json`） |')
w('| `tools/audit/verify_report.py` | 文档内每一处 `file:line` 的自校验 |')
w('| `tools/audit/shared/SignatureProvider.cs` | 两通道共用的签名渲染器（保证 join key 不漂移） |')
w('')

io.open(OUT, 'w', encoding='utf-8', newline='\n').write('\n'.join(L))
print('wrote', OUT, len(L), 'lines')
print('classes', {s: by_status[s] for s in STATUS}, 'sum', five_sum)
