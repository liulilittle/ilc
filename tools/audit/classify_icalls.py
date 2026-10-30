"""Final classification for the net48 mscorlib InternalCall audit.

Inputs (all produced by the committed audit tools, joined on the exact
(owner, name, signature) triple):
  metadata.tsv       -- tools/audit/MetadataIcallCounter       (channel 2)
  reflection.tsv     -- tools/audit/enumerate-icall-reflection (channel 1)
  probe.tsv          -- tools/audit/IcallWiringProbe           (real BclResolve)
  sites.json         -- tools/audit/locate_wiring_sites.py     (source lines)
  callgraph.json     -- tools/audit/IcallCallGraph             (IL reachability)
  runtime_syms.json  -- tools/audit/parse_runtime_syms.py      (runtime symbols)

Statuses (mutually exclusive, first match wins):
  wired   -- BclResolve returned a C++ target (or a fold marker) that the
             runtime defines / the emitter inlines
  half    -- BclResolve returned a target the runtime does NOT define
  loud    -- BclResolve threw NotSupportedException with a source line
  shadow  -- the icall has no arm, but every public wrapper that transitively
             calls it from user-visible code IS wired: user code reaches the
             behaviour through the wrapper and never emits this icall
  missing -- none of the above

Priority P0..P3 is assigned to every non-wired row; see priority().
"""
import io, os, json, collections, sys

TMP = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\liulilte\AppData\Local\Temp\opencode\icall-audit'

graph  = json.load(io.open(os.path.join(TMP, 'callgraph.json'), encoding='utf-8'))
rtsyms = json.load(io.open(os.path.join(TMP, 'runtime_syms.json'), encoding='utf-8'))
sites  = json.load(io.open(os.path.join(TMP, 'sites.json'), encoding='utf-8'))
decl, defs = rtsyms['decl'], rtsyms['defs']
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

assert len(meta) == 908, len(meta)
assert len(icalls) == 908, len(icalls)
assert not (set(meta) ^ set(icalls))

gidx = collections.defaultdict(list)
for g in graph['icalls']:
    gidx[(g['owner'], g['name'])].append(g)

rev = collections.defaultdict(set)
for a, b in graph['edges']:
    rev[b].add(a)

public_faces = set()
for g in graph['icalls']:
    if g['publicFace']:
        public_faces.add(g['owner'] + '::' + g['name'])
    if g.get('publicRoot'):
        public_faces.add(g['publicRoot'])


def bare(sym):
    return sym.rsplit('::', 1)[-1] if sym else None


def is_marker(sym):
    return bool(sym) and sym.startswith('::') and sym.endswith('::')


LOUD_STUBS = dict(rtsyms.get('loud', {}))


def sym_ok(sym):
    """A resolved target that the runtime can actually deliver.

    Three cases count as delivered:
      * a fold marker -- the emitter inlines the operation;
      * a symbol with a REAL definition (not a `*_loud` throw stub);
      * a symbol defined `inline` in a runtime header (compiled into callers).
    A resolved symbol whose runtime body throws NotSupportedException is NOT
    delivered: the transpiler emitted a call that always throws. See
    `is_stub` for the separate accounting of those.
    """
    if is_marker(sym):
        return True
    b = bare(sym)
    return bool(b) and b in defs and b not in LOUD_STUBS


def is_stub(sym):
    """A resolved target whose runtime implementation is a LOUD throw stub."""
    if is_marker(sym) or not sym:
        return False
    b = bare(sym)
    return b in defs and b in LOUD_STUBS


def site_of_symbol(sym):
    if not sym or is_marker(sym):
        return None
    return symbol_site.get(sym)


def stub_site(sym):
    """Where the `*_loud` throw stub is defined (the runtime's own evidence that
    the feature is rejected, not delivered)."""
    if not is_stub(sym):
        return None
    return LOUD_STUBS.get(bare(sym))


def row_ok(row):
    return row is not None and row['status'] == 'resolved' and sym_ok(row['symbol'])


# (owner::name) -> [(sig, row)] over BOTH kinds: a wrapper may itself be an
# icall (e.g. Monitor.Enter) or an ordinary public method.
by_name = collections.defaultdict(list)
for k, row in list(icalls.items()) + list(apis.items()):
    o, n, s = k.split(SEP)
    by_name[o + '::' + n].append((s, row))


ARM_DATA = json.load(io.open(os.path.join(TMP, 'arms.json'), encoding='utf-8'))


def owner_funcs_for(owner):
    return ARM_DATA['owner_funcs'].get(owner, ())


def func_info_for(fname):
    return ARM_DATA['funcs'].get(fname)


def wrapper_of(node):
    """The public wrapper that delivers `node` to user code, if any.

    A shadow exists only when the icall's DIRECT caller is itself a
    user-callable public method that the resolver maps. That is the precise
    condition because:

      * ILC never transpiles mscorlib method bodies -- only the USER assembly's
        IL is emitted. A net48 internal helper that "would have called" the
        icall at JIT time therefore never runs here, so a transitive chain
        through BCL internals does NOT deliver the behaviour.
      * The user's own IL emits exactly ONE call: the call to its direct
        callee. If that callee is a public BCL method with a MethodMap arm, the
        icall behind it is never reached and never needs its own arm.

    So the test is depth-1: an edge (public facade) -> node where the facade is
    a public method on a public type and resolves to a runtime target.
    """
    for caller in sorted(rev.get(node, ())):
        if caller not in public_faces:
            continue
        rows = by_name.get(caller, ())
        for sig, row in rows:
            if not row_ok(row):
                continue
            sym = row['symbol']
            # The citable evidence for a shadow is the WRAPPER's own dispatcher
            # arm -- that is the line the transpiler consults for the call the
            # user actually emits. A marker has no C++ symbol at all, and a
            # symbol-site grep can land in any file that emits the same target.
            site = None
            owner2, _, n2 = caller.partition('::')
            for fname in owner_funcs_for(owner2):
                info = func_info_for(fname)
                if not info or n2 not in info['names']:
                    continue
                site = (info['file'], info['names'][n2].get('line', info['line']))
                break
            if site is None:
                site = site_of_symbol(sym)
            return dict(method=caller, signature=sig, symbol=sym, site=site)
    return None


P2_NAMESPACES = (
    'System.Reflection', 'System.Runtime.Remoting', 'System.Runtime.InteropServices',
    'System.Diagnostics', 'System.Runtime.Serialization', 'System.Security',
    'System.AppDomain', 'System.Mda', 'Microsoft.Win32', 'Microsoft.Runtime.Hosting',
    'System.Runtime.CompilerServices',
)

out = []
for k, r in meta.items():
    t = icalls[k]
    gs = gidx.get((r['owner'], r['name']), [])
    dist = min((g['publicDistance'] for g in gs if g['publicDistance'] >= 0), default=-1)
    root = next((g['publicRoot'] for g in gs if g.get('publicRoot')), None)
    node = r['owner'] + '::' + r['name']

    if t['status'] == 'resolved' and sym_ok(t['symbol']):
        status = 'wired'
    elif t['status'] == 'resolved':
        status = 'half'
    elif t['status'] == 'loud':
        status = 'loud'
    else:
        status = None

    wrappers = []
    if status is None:
        w = wrapper_of(node)
        if w is not None:
            wrappers = [w]
        status = 'shadow' if w is not None else 'missing'

    public_face = r['vis'].startswith('public/public/')

    if status == 'wired':
        prio = '-'
    elif public_face:
        prio = 'P0'
    elif dist < 0:
        prio = 'P3'
    elif dist >= 4:
        prio = 'P2'
    elif r['owner'].startswith(P2_NAMESPACES):
        prio = 'P2'
    else:
        prio = 'P1'

    site = t['site']
    if status == 'loud' and loud_site.get(k):
        site = loud_site[k][0] + ':' + str(loud_site[k][1])
    elif status == 'wired':
        s = site_of_symbol(t['symbol'])
        if s:
            site = s[0] + ':' + str(s[1])

    if status == 'shadow' and wrappers and wrappers[0].get('site'):
        ws = wrappers[0]['site']
        site = ws[0] + ':' + str(ws[1])

    stub = stub_site(t['symbol'])
    if stub:
        site = stub[0] + ':' + str(stub[1])

    out.append(dict(r, key=node, status=status, priority=prio,
                    symbol=t['symbol'], symbolBare=bare(t['symbol']),
                    stub=(stub[0] + ':' + str(stub[1])) if stub else None,
                    error=t['error'], site=site,
                    publicFace=public_face, publicDistance=dist, publicRoot=root,
                    wrappers=wrappers,
                    callers=[c['owner'] + '::' + c['name'] for g in gs for c in g['callers']]))

# ---------------------------------------------------------------------------
# Final evidence pass: prefer the DISPATCHER line as the `file:line` of a wired
# row. A raw symbol grep can point at any file that emits the same C++ target
# (System.Type::op_Inequality is emitted from MethodMap.Type, .Reflect,
# .Resources and .GC); the authoritative answer is the TryMap* function the
# OWNER actually routes to, so record that line and use it as the site.
# ---------------------------------------------------------------------------
arms = json.load(io.open(os.path.join(TMP, 'arms.json'), encoding='utf-8'))
owner_funcs = arms['owner_funcs']
funcs = arms['funcs']

for r in out:
    # A `half` row's evidence is the runtime THROW STUB (the reason the call
    # cannot succeed), not the dispatch arm that selects it.
    if r['status'] != 'wired':
        continue
    for fname in owner_funcs.get(r['owner'], ()):
        info = funcs.get(fname)
        if not info or r['name'] not in info['names']:
            continue
        arm = info['names'][r['name']]
        r['dispatchFile'] = info['file']
        r['dispatchFuncLine'] = info['line']
        r['dispatchArm'] = r['name']
        # the CASE line, not the function header: it is the line that actually
        # selects this member.
        r['dispatchSite'] = info['file'] + ':' + str(arm.get('line', info['line']))
        r['site'] = r['dispatchSite']
        break

json.dump(out, io.open(os.path.join(TMP, 'classified.json'), 'w', encoding='utf-8'), indent=1)

print('total', len(out))
print('status', dict(collections.Counter(r['status'] for r in out)))
print('priority', dict(collections.Counter(r['priority'] for r in out)))
print('nonWired x priority')
for s in ('loud', 'missing', 'shadow', 'half'):
    print(' ', s, dict(collections.Counter(r['priority'] for r in out if r['status'] == s)))
print('publicFace by status', dict(collections.Counter(r['status'] for r in out if r['publicFace'])))
print('no-site rows', sum(1 for r in out if not r['site'] and r['status'] != 'missing'))
