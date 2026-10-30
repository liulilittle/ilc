"""Final classification for the net48 mscorlib InternalCall audit.

Inputs (all produced by the committed audit tools, joined on the exact
(owner, name, signature) triple):
  metadata.tsv       -- tools/audit/MetadataIcallCounter       (channel 2)
  reflection.tsv     -- tools/audit/enumerate-icall-reflection (channel 1)
  probe.tsv          -- tools/audit/IcallWiringProbe           (real BclResolve)
  sites.json         -- tools/audit/locate_wiring_sites.py     (source lines)
  callgraph.json     -- tools/audit/IcallCallGraph             (IL reachability)
  runtime_syms.json  -- tools/audit/parse_runtime_syms.py      (runtime symbols)

The classification key is (owner, name, SIGNATURE). Overloads of one name can
differ in reachability AND in wiring, so collapsing them to (owner, name) mixes
a resolved overload's evidence into an unresolved sibling's row.

Statuses (mutually exclusive, first match wins):
  wired    -- BclResolve returned a C++ target (or a fold marker) that the
              runtime really implements / the emitter inlines
  half     -- BclResolve returned a target whose runtime body is a placeholder:
              a `*_loud` throw stub, or a constant/empty body (`return 0;`,
              `return nullptr;`, `{ }`) that cannot observe its arguments
  loud     -- BclResolve threw NotSupportedException with a source line
  shadow   -- the icall has no arm, but a PUBLIC ENTRY that user code calls is
              wired, and that entry's mscorlib body (which is never transpiled)
              is the only thing that would have reached this icall. `hops` is
              the number of IL edges from that entry: hops == 1 may be written
              as "directly delivered by X", hops >= 2 may not.
  partialShadow
           -- a shadow whose delivered public entry has an UNMAPPED sibling
              overload of the same name, so user code using that overload still
              falls to a phantom symbol. Counted separately from `shadow`.
  missing  -- none of the above

Priority P0..P3 is assigned to every non-wired row; see priority().
"""
import io, os, json, collections, sys

TMP = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\liulilte\AppData\Local\Temp\opencode\icall-audit'

TOTAL_ICALLS = 908

graph  = json.load(io.open(os.path.join(TMP, 'callgraph.json'), encoding='utf-8'))
rtsyms = json.load(io.open(os.path.join(TMP, 'runtime_syms.json'), encoding='utf-8'))
sites  = json.load(io.open(os.path.join(TMP, 'sites.json'), encoding='utf-8'))
decl, defs = rtsyms['decl'], rtsyms['defs']
LOUD_STUBS = dict(rtsyms.get('loud', {}))
CONST_STUBS = dict(rtsyms.get('const', {}))
symbol_site = sites['symbol']
loud_site = sites['loud']

SEP = '\x1f'


def key(owner, name, sig):
    return owner + SEP + name + SEP + sig


meta = {}
with io.open(os.path.join(TMP, 'metadata.tsv'), encoding='utf-8-sig') as f:
    for line in f:
        p = line.rstrip('\n').split('\t')
        if len(p) == 5:
            meta[key(p[0], p[1], p[2])] = dict(owner=p[0], name=p[1], sig=p[2], vis=p[3], kind=p[4])

icalls = {}          # key -> probe row      (kind == icall)
apis = {}            # key -> probe row      (kind == publicapi)
with io.open(os.path.join(TMP, 'probe.tsv'), encoding='utf-8-sig') as f:
    for line in f:
        p = line.rstrip('\n').split('\t')
        if len(p) != 8:
            continue
        row = dict(kind=p[3], status=p[4], symbol=p[5] or None,
                   site=p[6] or None, error=p[7] or None)
        (icalls if p[3] == 'icall' else apis)[key(p[0], p[1], p[2])] = row

# The whole report is anchored on this count. A pipeline change that adds or
# drops a row must fail loudly here rather than silently shift every number.
assert len(meta) == TOTAL_ICALLS, len(meta)
assert len(icalls) == TOTAL_ICALLS, len(icalls)
assert not (set(meta) ^ set(icalls)), 'metadata/probe key sets differ'

# ---------------------------------------------------------------------------
# Reachability over the SIGNATURE-EXACT call graph.
#
# Entries are the methods user code can call: public methods on public types
# (`publicFaces`), plus the icalls that are themselves public face (a user call
# lands on the icall directly). Everything is keyed by (owner, name, sig) so an
# overload's distance is never borrowed from a sibling.
# ---------------------------------------------------------------------------
faces = {(o, n, s) for o, n, s in graph['publicFaces']}
public_icalls = {(g['owner'], g['name'], g['sig']) for g in graph['icalls'] if g['publicFace']}
entries = faces | public_icalls

fwd = collections.defaultdict(set)
for a in graph['edges']:
    fwd[(a[0], a[1], a[2])].add((a[3], a[4], a[5]))

# The BFS must be DETERMINISTIC: two entries can reach a node at equal distance,
# and whichever is dequeued first becomes the reported root. Iterating the raw
# sets would make the report depend on Python's string-hash randomisation, so
# every frontier is sorted (ordinal on the (owner, name, sig) tuple).
dist = {}
root = {}
chain = {}
q = collections.deque(sorted(entries))
for e in sorted(entries):
    dist[e] = 0
    root[e] = e
    chain[e] = ()
while q:
    cur = q.popleft()
    for c in sorted(fwd.get(cur, ())):
        if c in dist:
            continue
        dist[c] = dist[cur] + 1
        root[c] = root[cur]
        chain[c] = (cur,) + chain[cur]
        q.append(c)


def bare(sym):
    return sym.rsplit('::', 1)[-1] if sym else None


def is_marker(sym):
    return bool(sym) and sym.startswith('::') and sym.endswith('::')


def delivered(sym):
    """A resolved target the runtime can actually execute.

    Three cases count as delivered:
      * a fold marker -- the emitter inlines the operation;
      * a symbol with a REAL definition (not a `*_loud` throw stub);
      * a symbol defined `inline` in a runtime header (compiled into callers).
    A target whose body throws NotSupportedException, or whose whole body is
    `return <constant>;` / empty, is NOT delivered: it either always throws or
    answers with a compiled-in constant without looking at its arguments.
    """
    if is_marker(sym):
        return True
    b = bare(sym)
    return bool(b) and b in defs and b not in LOUD_STUBS and b not in CONST_STUBS


def stub_kind(sym):
    """'loud' / 'const' when the resolved target is a placeholder, else None."""
    if is_marker(sym) or not sym:
        return None
    b = bare(sym)
    if b in defs and b in LOUD_STUBS:
        return 'loud'
    if b in defs and b in CONST_STUBS:
        return 'const'
    return None


def site_of_symbol(sym):
    if not sym or is_marker(sym):
        return None
    return symbol_site.get(sym)


def stub_site(sym):
    kind = stub_kind(sym)
    if not kind:
        return None
    return (LOUD_STUBS if kind == 'loud' else CONST_STUBS).get(bare(sym))


def entry_delivered(k):
    """Whether the row at key `k` is wired (resolved AND really delivered).

    `k` is a (owner, name, sig) tuple; the probe tables are keyed by the
    SEP-joined form, so join them the same way the rest of the file does.
    """
    row = icalls.get(key(*k)) or apis.get(key(*k))
    if row is None or row['status'] != 'resolved':
        return False
    return delivered(row['symbol'])


# (owner, name) -> probe rows, over BOTH kinds. Used for the sibling-overload
# test: a shadow is only PARTIAL when a same-named public overload is unmapped.
by_name = collections.defaultdict(list)
for k, row in list(icalls.items()) + list(apis.items()):
    o, n, s = k.split(SEP)
    by_name[o + '::' + n].append((s, row))

# The PUBLIC surface of one name: the publicapi rows plus the icalls that are
# themselves public face. `Interlocked.CompareExchange` is the case that forces
# both -- its numeric overloads are InternalCall methods (kind `icall`) while its
# generic `ref T` overload is a managed method (kind `publicapi`), so looking at
# one kind alone would conclude the whole entry is unmapped (or all mapped).
public_group = collections.defaultdict(list)
for k, row in apis.items():
    o, n, s = k.split(SEP)
    public_group[o + '::' + n].append((s, row))
for k in public_icalls:
    o, n, s = k
    public_group[o + '::' + n].append((s, icalls[key(o, n, s)]))


def group_row_delivered(row):
    return row['status'] == 'resolved' and delivered(row['symbol'])


def has_unmapped_public_overload(owner, name):
    """Whether the public entry (owner, name) has an overload that is NOT delivered.

    The test runs on the ENTRY's name group, not the icall's, because that is
    the surface user code calls. `Interlocked.CompareExchange(!!0&, !!0, !!0)`
    is the shape a `ref T` user writes, and it is unmapped: the numeric/double
    overloads are delivered, the generic one falls to a phantom symbol, so the
    reduction through that entry is only PARTIAL.
    """
    return any(not group_row_delivered(row) for _, row in public_group.get(owner + '::' + name, ()))


ARM_DATA = json.load(io.open(os.path.join(TMP, 'arms.json'), encoding='utf-8'))


def owner_funcs_for(owner):
    return ARM_DATA['owner_funcs'].get(owner, ())


def func_info_for(fname):
    return ARM_DATA['funcs'].get(fname)


def arm_site(owner, name):
    """The dispatcher line that selects `name` inside `owner`'s TryMap*."""
    for fname in owner_funcs_for(owner):
        info = func_info_for(fname)
        if not info or name not in info['names']:
            continue
        return (info['file'], info['names'][name].get('line', info['line']))
    return None


rev = collections.defaultdict(set)
for a in graph['edges']:
    rev[(a[3], a[4], a[5])].add((a[0], a[1], a[2]))

_nearest_cache = {}


def nearest_entries(k):
    """Every entry that reaches `k` on a SHORTEST path.

    One icall usually has several user-visible entries (four `Double.ToString`
    overloads, or `TypedReference.ToObject` plus `Array.GetValue`). The verdict
    must account for ALL of them at the winning distance: if one is delivered
    and another is not, user code taking the second still fails, and calling the
    row a plain shadow would hide that.
    """
    if k in _nearest_cache:
        return _nearest_cache[k]
    d = dist.get(k)
    if d is None:
        _nearest_cache[k] = ()
        return ()
    if d == 0:
        _nearest_cache[k] = (k,)
        return (k,)
    out = set()
    for p in rev.get(k, ()):
        if dist.get(p) == d - 1:
            out.update(nearest_entries(p))
    res = tuple(sorted(out))
    _nearest_cache[k] = res
    return res


def entry_clean(e):
    """A delivered entry with no undelivered sibling overload."""
    return entry_delivered(e) and not has_unmapped_public_overload(e[0], e[1])


def shadow_verdict(k):
    """'shadow' / 'partialShadow' / None for an icall with no arm of its own.

    * shadow        -- at the winning distance every entry is clean: user code
                       can only emit calls that resolve to a runtime target, so
                       this icall's body is never reached.
    * partialShadow -- the reduction is real but incomplete. Some entry at the
                       winning distance is delivered, yet at least one entry
                       there is not delivered, or has an unmapped sibling
                       overload. User code taking that shape still falls
                       through, so the row must not be counted as fully covered.
    * None          -- no entry delivers anything; it is a real gap.
    """
    ents = nearest_entries(k)
    if not ents:
        return None
    clean = [e for e in ents if entry_clean(e)]
    delivered_any = [e for e in ents if entry_delivered(e)]
    if not delivered_any:
        return None
    if clean and all(e in clean for e in ents):
        return 'shadow'
    return 'partialShadow'


def shadow_entry(k):
    """The user-visible entry that stands in for this icall, plus its hop count.

    A shadow exists because only the USER assembly's IL is transpiled: a
    mscorlib body that would have called this icall never runs, so the user's
    single emitted call is the call to its own callee. If that callee maps to a
    runtime target, the icall behind it is not on any path the generated C++
    walks.

    `hops` is the IL distance from the entry, so the report may say "directly
    delivered by X" only when it is literally one edge away. hops >= 2 is an
    indirect chain and must be described as such: Thread::StartInternal sits
    behind public Start() -> internal Start(StackCrawlMark&) -> StartInternal,
    two edges, not one.
    """
    ents = nearest_entries(k)
    if not ents:
        return None
    # Prefer a fully-delivered entry; a `partialShadow` has none, so cite the
    # delivered one that still exists -- that is the call the user emits and the
    # reason the row is a reduction at all.
    clean = [e for e in ents if entry_clean(e)]
    delivered_any = [e for e in ents if entry_delivered(e)]
    cand = clean or delivered_any
    if not cand:
        return None
    e = cand[0]
    return dict(entry=e[0] + '::' + e[1], entrySig=e[2], hops=dist[k],
                via=[c[0] + '::' + c[1] for c in chain.get(k, ())])


def m2_shadow_of(k):
    """Whether a `loud`/`half` row is really a shadow (the M2 reduction).

    A row whose OWN arm rejects (loud) or selects a placeholder (half) is still
    a shadow when user code never reaches that arm: the public entry it sits
    behind is delivered, so the user's emitted call resolves to the entry's own
    runtime target and the rejecting arm is dead code.

    Deliberately STRICTER than `shadow_of`, because this reclassifies a row that
    already has an arm:

      * the row is not itself public face;
      * the entry is exactly ONE edge away -- the icall's direct caller IS the
        public method. A deeper chain means the immediate caller is an internal
        helper, and "user code only calls X" would need the whole chain argued;
      * EVERY depth-1 public caller resolves to a real runtime implementation
        (non-stub), so no user path lands on the rejecting arm.

    The caller's own overload SET is not part of this test: the question M2 asks
    is whether THIS icall is dead code, and it is dead for every delivered
    caller. A partially-mapped caller name is reported wherever it actually
    costs something -- as `partialShadow` on the icall that overload reaches.
    """
    o, n, s = k
    if k in entries:
        return None
    d1 = {c for c in rev.get(k, ()) if c in faces}
    if not d1:
        return None
    if not all(entry_delivered(c) for c in d1):
        return None
    e = sorted(d1)[0]
    return dict(entry=e[0] + '::' + e[1], entrySig=e[2], hops=1,
                via=[c[0] + '::' + c[1] for c in chain.get(k, ())])


# ---------------------------------------------------------------------------
# Priority rules.
# ---------------------------------------------------------------------------

P2_NAMESPACES = (
    'System.Reflection', 'System.Runtime.Remoting', 'System.Runtime.InteropServices',
    'System.Diagnostics', 'System.Runtime.Serialization', 'System.Security',
    'System.AppDomain', 'System.Mda', 'Microsoft.Win32', 'Microsoft.Runtime.Hosting',
    'System.Runtime.CompilerServices',
)


def in_p2_namespace(owner):
    """Namespace membership tested against a TYPE FULL NAME with a separator.

    A bare `startswith('System.AppDomain')` also swallows `System.AppDomainSetup`
    -- a different type. Requiring the separator (or an exact match) keeps the
    test at namespace granularity.
    """
    for ns in P2_NAMESPACES:
        if owner == ns or owner.startswith(ns + '.') or owner.startswith(ns + '+'):
            return True
    return False


def priority(status, public_face, d, owner):
    """P0..P3 for one non-wired row.

    `publicDistance == 1` is ALWAYS at most P1: the icall is one IL edge from a
    method user code calls, so it is reached by ordinary BCL paths whatever its
    namespace says. Letting the namespace demote it was the bug that pushed
    AppDomain::GetAssemblies -> nGetAssemblies and 70 other rows to P2 even
    though their facade arm is resolved.
    """
    if status == 'wired':
        return '-'
    if public_face:
        return 'P0'
    if d < 0:
        return 'P3'
    if d == 1:
        return 'P1'
    if d >= 4:
        return 'P2'
    if in_p2_namespace(owner):
        return 'P2'
    return 'P1'


out = []
for k, r in meta.items():
    t = icalls[k]
    gs = [g for g in graph['icalls'] if g['owner'] == r['owner'] and g['name'] == r['name'] and g['sig'] == r['sig']]
    d = dist.get((r['owner'], r['name'], r['sig']), -1)
    rt = root.get((r['owner'], r['name'], r['sig']))
    public_face = r['vis'].startswith('public/public/')

    shadow = None
    if t['status'] == 'resolved' and delivered(t['symbol']):
        status = 'wired'
    elif t['status'] == 'resolved':
        status = 'half'
    elif t['status'] == 'loud':
        status = 'loud'
    else:
        status = None

    if status == 'wired':
        prio = '-'
    else:
        if status is None:
            # No arm at all: the row is a shadow only if user code reaches the
            # behaviour through a delivered entry instead.
            verdict = shadow_verdict((r['owner'], r['name'], r['sig']))
            if verdict is not None:
                status = verdict
                shadow = shadow_entry((r['owner'], r['name'], r['sig']))
            else:
                status = 'missing'
        else:
            # The row has its own arm that rejects (loud) or selects a
            # placeholder (half). Only the strict M2 test may call that arm dead
            # code; otherwise the arm's own verdict stands.
            shadow = m2_shadow_of((r['owner'], r['name'], r['sig']))
            if shadow is not None:
                status = 'shadow'

        prio = priority(status, public_face, d, r['owner'])

    site = t['site']
    if status in ('loud',) and loud_site.get(k):
        site = loud_site[k][0] + ':' + str(loud_site[k][1])
    elif status == 'wired':
        s = site_of_symbol(t['symbol'])
        if s:
            site = s[0] + ':' + str(s[1])

    if status in ('shadow', 'partialShadow') and shadow is not None:
        # The citable line is the ENTRY's own dispatcher arm: that is the call
        # the user's IL actually emits for the entry method. Entries delivered
        # by the emitter (fold markers, StackFold-inlined paths) have no MethodMap
        # arm, so fall back to the line that emits the entry's C++ target.
        eo, en = shadow['entry'].split('::', 1)
        s = arm_site(eo, en)
        if s:
            site = s[0] + ':' + str(s[1])
        else:
            entry_row = apis.get(key(eo, en, shadow['entrySig'])) or icalls.get(key(eo, en, shadow['entrySig']))
            site = site_of_symbol(entry_row['symbol']) if entry_row else None

    skind = stub_kind(t['symbol'])
    stub = stub_site(t['symbol'])
    if stub and status == 'half':
        # A `half` row's evidence is the runtime PLACEHOLDER body -- the reason
        # the target cannot answer. When M2 has reclassified the row as a shadow
        # the cited line is the entry's arm instead (the call the user emits),
        # so the stub line must not win there.
        site = stub[0] + ':' + str(stub[1])

    entry_arm = None
    if shadow is not None:
        eo, en = shadow['entry'].split('::', 1)
        s = arm_site(eo, en)
        if s:
            entry_arm = s[0] + ':' + str(s[1])

    out.append(dict(r, key=r['owner'] + '::' + r['name'], status=status, priority=prio,
                    symbol=t['symbol'], symbolBare=bare(t['symbol']),
                    stub=(stub[0] + ':' + str(stub[1])) if stub else None,
                    stubKind=skind,
                    error=t['error'], site=site,
                    publicFace=public_face, publicDistance=d,
                    publicRoot=(rt[0] + '::' + rt[1]) if rt else None,
                    shadow=shadow, entryArm=entry_arm,
                    callers=[c['owner'] + '::' + c['name'] for g in gs for c in g['callers']]))

# ---------------------------------------------------------------------------
# Final evidence pass: prefer the DISPATCHER line as the `file:line` of a wired
# row. A raw symbol grep can point at any file that emits the same C++ target;
# the authoritative answer is the TryMap* function the OWNER actually routes to.
# ---------------------------------------------------------------------------
for r in out:
    if r['status'] != 'wired':
        continue
    s = arm_site(r['owner'], r['name'])
    if s:
        r['dispatchFile'] = s[0]
        r['dispatchArm'] = r['name']
        r['dispatchSite'] = s[0] + ':' + str(s[1])
        r['site'] = r['dispatchSite']

assert len(out) == TOTAL_ICALLS, len(out)

STATUSES = ('wired', 'half', 'loud', 'shadow', 'partialShadow', 'missing')
by_status = collections.Counter(r['status'] for r in out)
for s in STATUSES:
    assert by_status[s] == by_status.get(s, 0)
assert sum(by_status.values()) == TOTAL_ICALLS, dict(by_status)

# The five headline classes. `partialShadow` is split OUT of `shadow` for
# visibility, so the five-class total and the six-status total must both be 908.
five = by_status['wired'] + by_status['half'] + by_status['loud'] + by_status['missing'] \
    + by_status['shadow'] + by_status['partialShadow']
assert five == TOTAL_ICALLS, five

json.dump(out, io.open(os.path.join(TMP, 'classified.json'), 'w', encoding='utf-8'), indent=1)

print('total', len(out))
print('status', {s: by_status[s] for s in STATUSES})
print('priority', dict(collections.Counter(r['priority'] for r in out)))
print('nonWired x priority')
for s in STATUSES[1:]:
    print(' ', s, dict(collections.Counter(r['priority'] for r in out if r['status'] == s)))
print('publicFace by status', dict(collections.Counter(r['status'] for r in out if r['publicFace'])))
print('shadow hops', dict(collections.Counter(r['shadow']['hops'] for r in out if r['shadow'])))
print('no-site rows', sum(1 for r in out if not r['site'] and r['status'] != 'missing'))
print('ASSERT sum==908: OK')
