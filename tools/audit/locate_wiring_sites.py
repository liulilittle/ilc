"""Locate the exact MethodMap source line behind each wiring decision.

Why not a stack trace: MethodRefResolver.BclResolve catches the domain
dispatcher's NotSupportedException and RE-THROWS a fresh one
(MethodRefResolver.cs:1411-1415), which discards the original stack. The audit
therefore anchors each `loud` row on the exact source line that produced the
message text, and each `resolved` row on the exact source line that emitted the
C++ symbol. Both are direct textual evidence, verifiable with a grep.

Output: sites.json
  { "loud":   { "<owner>\x1f<name>\x1f<sig>": [file, line] },
    "symbol": { "il2cpp::runtime::icall_x": [file, line], ... } }
"""
import io, os, json, re, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else r'F:\il2cpp\.worktrees\audit-mscorlib-icall'
TMP = sys.argv[2] if len(sys.argv) > 2 else r'C:\Users\liulilte\AppData\Local\Temp\opencode\icall-audit'
EMIT = os.path.join(ROOT, 'src', 'il2cppc', 'Emit')


def read(p):
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        return f.read().split('\n')


sources = {}
for fn in sorted(os.listdir(EMIT)):
    if fn.endswith('.cs'):
        sources[fn] = read(os.path.join(EMIT, fn))

# ---- symbol -> (file, line) : the line that names the C++ target ----
#
# Most arms pass the target as one string literal. A few build the name by
# concatenation (ThreadPool's "icall_thread_pool_get_" + kind + "_threads_out"),
# which no plain literal scan can see. Those are recorded as a SYNTHETIC entry
# keyed by the literal prefix, and `lookup_symbol` below falls back to the
# longest matching prefix so every concatenated family still gets a site.
symbol_sites = {}
symbol_prefixes = []

for fn, lines in sources.items():
    for i, line in enumerate(lines, 1):
        for sym in re.findall(r'"((?:il2cpp|icall)(?:::|_)[\w:]*)"', line):
            symbol_sites.setdefault(sym, (fn, i))
        # fold markers (::identity:: / ::refeq:: / ...) are inlined by the
        # emitter but still need a citable line: record the ResolvedMethod.X
        # Marker reference with the same lookup key.
        for mk in re.findall(r'ResolvedMethod\.(\w+Marker)', line):
            # IdentityMarker in the source == the literal "::identity::"
            # the probe reports, so normalize camel-case -> lower case.
            key = '::' + mk[:-6].lower() + '::'
            symbol_sites.setdefault(key, (fn, i))

        # concatenation arm: one or more literals joined with `+` on the line
        if line.count('"') >= 2 and '+' in line:
            parts = re.findall(r'"([^"]*)"', line)
            if parts and parts[0].startswith(('il2cpp', 'icall')) and parts[0].count('_') >= 1:
                symbol_prefixes.append((parts[0], (fn, i)))


def lookup_symbol(sym):
    """Site of a resolved C++ target, including concatenated-name families."""
    hit = symbol_sites.get(sym)
    if hit:
        return hit
    bare = sym.rsplit('::', 1)[-1]
    best = None
    for prefix, site in symbol_prefixes:
        p = prefix.rsplit('::', 1)[-1]
        if sym.startswith(prefix) or bare.startswith(p):
            if best is None or len(prefix) > len(best[0]):
                best = (prefix, site)
    return best[1] if best else None

ARM_DATA = json.load(io.open(os.path.join(TMP, 'arms.json'), encoding='utf-8'))


def owner_files(owner, kind):
    """The MethodMap files the resolver routes `owner` into.

    For an icall row the owner comes straight from metadata, so the owner's
    own case arm in BclResolveCore names the TryMap* functions. For a
    publicapi row the same lookup applies (a public method of the same owner).
    """
    fns = ARM_DATA['owner_funcs'].get(owner, ())
    files = []
    for fn in fns:
        info = ARM_DATA['funcs'].get(fn)
        if info and info['file'] not in files:
            files.append(info['file'])
    if not files:
        # families handled BEFORE the owner switch (MethodRefResolver.cs:1494)
        if owner in ('System.ArgumentException', 'System.ArgumentNullException',
                     'System.ArgumentOutOfRangeException', 'System.SystemException'):
            files = ['MethodMap.Exception.cs']
    return files


# ---- loud message -> (file, line) : the throwing line ----
# The probe records the whole message. The most specific part is the sentence
# after "LOUD whitelist (...)": it is a long literal unique to one line.
loud_sites = {}

probe = []
with io.open(os.path.join(TMP, 'probe.tsv'), encoding='utf-8-sig') as f:
    for line in f:
        p = line.rstrip('\n').split('\t')
        if len(p) == 8:
            probe.append(p)

def norm(s):
    return re.sub(r'\s+', ' ', s).strip()

for owner, name, sig, kind, status, sym, site, err in probe:
    if status != 'loud' or kind != 'icall':
        continue
    msg = err or ''
    # drop the "<owner>::<name>: " prefix added by BclResolve
    head = owner + '::' + name + ': '
    if msg.startswith(head):
        msg = msg[len(head):]
    body = norm(msg)
    # The message may open with an INTERPOLATED token (the method name, the
    # owner) that the source line does not contain -- $"{name} is part of
    # System.AppDomain ...". Drop that leading token so the literal tail can
    # match the source text.
    for lead in (name, owner):
        if body.startswith(lead + ' '):
            body = body[len(lead) + 1:]
            break
    if not body:
        continue

    # Restrict the search to the files the OWNER actually dispatches into --
    # MethodRefResolver's owner switch routes every icall of one owner to a
    # fixed set of TryMap* functions, and those live in specific MethodMap
    # files. Without this restriction a generic message fragment can match an
    # unrelated domain's line (e.g. System.CurrentSystemTimeZone picking up a
    # System.WeakReference throw).
    candidate_files = owner_files(owner, kind)
    if not candidate_files:
        candidate_files = [f for f in sources if f.startswith(('MethodMap', 'MethodRefResolver'))]

    # A throw is frequently a multi-line expression: the message continues onto
    # later lines while the `throw new NotSupportedException(` sits on the first
    # one. Join the file into overlapping 4-line windows so a fragment that
    # spans lines still lands on the throw statement, and require the window to
    # actually contain a throw -- otherwise a fragment that happens to echo a
    # doc comment ("System.RuntimeFieldHandle::GetValue not wired ..." appears
    # verbatim in the header comment) would point at the comment instead.
    hit = None
    for cut in (80, 70, 55, 40, 28, 18):
        frag = body[:cut].replace('"', '')
        if len(frag) < 18:
            break
        for fn in candidate_files:
            lines = sources[fn]
            for i in range(len(lines)):
                window = norm(' '.join(lines[i:i + 4]))
                if frag not in window:
                    continue
                if 'throw new NotSupportedException' not in window and 'LOUD whitelist' not in window:
                    continue
                # walk back to the line that opens the throw
                start = i
                for back in range(i, min(i + 4, len(lines))):
                    if 'throw new NotSupportedException' in lines[back]:
                        start = back
                        break
                hit = (fn, start + 1)
                break
            if hit:
                break
        if hit:
            break

    loud_sites[owner + '\x1f' + name + '\x1f' + sig] = list(hit) if hit else None

# Expand concatenated families: every symbol the probe RESOLVED that has no
# literal site but matches a prefix gets that prefix's line.
for owner, name, sig, kind, status, sym, site, err in probe:
    if status != 'resolved' or not sym:
        continue
    if sym in symbol_sites:
        continue
    hit = lookup_symbol(sym)
    if hit:
        symbol_sites[sym] = hit

json.dump({'symbol': {k: list(v) for k, v in symbol_sites.items()},
           'loud': loud_sites},
          io.open(os.path.join(TMP, 'sites.json'), 'w', encoding='utf-8'), indent=1)

resolved_n = sum(1 for p in probe if p[4] == 'resolved' and p[3] == 'icall')
loud_n = sum(1 for p in probe if p[4] == 'loud' and p[3] == 'icall')
hit_n = sum(1 for v in loud_sites.values() if v)
print('probe rows', len(probe), 'resolved', resolved_n, 'loud', loud_n)
print('symbol sites', len(symbol_sites))
print('loud sites located', hit_n, '/', loud_n)
for p in probe:
    if p[4] == 'loud' and p[3] == 'icall':
        k = p[0] + '\x1f' + p[1] + '\x1f' + p[2]
        if not loud_sites.get(k):
            print('  MISS', p[0] + '::' + p[1])
