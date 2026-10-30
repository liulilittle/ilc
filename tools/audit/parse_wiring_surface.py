"""Parse the ILC BCL wiring surface (MethodRefResolver + MethodMap.*.cs).

Produces arms.json:
  owner_case_line : owner full name -> 1-based line of its `case "<owner>":`
  owner_funcs     : owner -> [TryMap* functions reachable from that case arm]
  funcs           : TryMap* -> {file, line, names: {armName: {symbols, throws}},
                                prefixes: {armName: StartsWith|EndsWith|Contains}}

Stacked `case` labels (e.g. `case "System.Object":` / `case "System.IConvertible":`
sharing one body) are grouped: a label run ends at the first case whose next
significant line is not another label, and the whole run maps to that body.
"""
import io, os, json, collections, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else r'F:\il2cpp\.worktrees\audit-mscorlib-icall'
EMIT = os.path.join(ROOT, 'src', 'il2cppc', 'Emit')


def read(p):
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        return f.read().split('\n')


files = {}
for fn in sorted(os.listdir(EMIT)):
    if fn.startswith('MethodMap') or fn == 'MethodRefResolver.cs':
        files[fn] = read(os.path.join(EMIT, fn))

resolver = files['MethodRefResolver.cs']
start = next(i for i, l in enumerate(resolver)
             if 'private static ResolvedMethod? BclResolveCore(' in l)
end = next(i for i in range(start + 1, len(resolver))
           if resolver[i].startswith('    private static ')
           and '(' in resolver[i])

case_re = __import__('re').compile(r'^            case "(?P<name>[^"]+)":')
null_re = __import__('re').compile(r'^            case null:')
label_re = __import__('re').compile(r'^            (case |//)')

labels = []          # (line_no, owner|'<null>')
for i in range(start, end):
    m = case_re.match(resolver[i])
    if m:
        labels.append((i + 1, m.group('name')))
    elif null_re.match(resolver[i]):
        labels.append((i + 1, '<null>'))

# A label run falls through to the first region that actually contains a
# statement: `case "System.Object":` / `case "System.IConvertible":` share the
# body that starts after the intervening comment block, so grouping on
# adjacent line numbers alone would attribute an empty body to the first label.
stmt_re = __import__('re').compile(r'\breturn\b|\bthrow\b|^\s*\{', __import__('re').M)

groups = []          # (owners, body_start_line, body_end_line)
gi = 0
while gi < len(labels):
    owners = [labels[gi][1]]
    body_start = labels[gi][0]
    j = gi
    while True:
        seg_end = labels[j + 1][0] - 1 if j + 1 < len(labels) else end
        seg = '\n'.join(resolver[body_start - 1:seg_end])
        if stmt_re.search(seg) or j + 1 >= len(labels):
            break
        j += 1
        owners.append(labels[j][1])
    owners = [o for _, o in labels[gi:j + 1]]
    body_end = labels[j + 1][0] - 1 if j + 1 < len(labels) else end
    groups.append((owners, body_start, body_end, labels[j][0]))
    gi = j + 1

owner_case_line = {}
owner_funcs = collections.defaultdict(list)
for owners, bstart, bend, last_label in groups:
    body = '\n'.join(resolver[bstart - 1:bend])
    fns = []
    for fn in __import__('re').findall(r'\b(TryMap\w+)\s*\(', body):
        if fn not in fns:
            fns.append(fn)
    for o in owners:
        owner_case_line[o] = bstart          # first label line of the run
        owner_funcs[o] = fns

# ---------- arms inside each TryMap* ----------
pat_func = __import__('re').compile(r'^(\s{4,8})private static [\w\?<>,\.\[\]\s]*?\b(TryMap\w+)\s*\(')
arm_re = __import__('re').compile(r'^(\s*)case\s+"([^"]+)"\s*:')
# Some domains select the member inside a compound guard
# (`if (owner == "System.ValueType" && name == "GetHashCodeOfPtr")`), so the
# name comparison is looked for anywhere on an `if` line, not only at its start.
iff_re = __import__('re').compile(r'^(\s*)if\s*\(.*?\bname\s*==\s*"([^"]+)"')
prefix_re = __import__('re').compile(r'name\.(StartsWith|EndsWith|Contains)\("([^"]+)"')

funcs = {}
for fn, lines in files.items():
    i = 0
    while i < len(lines):
        m = pat_func.match(lines[i])
        if not m:
            i += 1
            continue
        fname = m.group(2)
        depth, started, j = 0, False, i
        while j < len(lines):
            depth += lines[j].count('{') - lines[j].count('}')
            if '{' in lines[j]:
                started = True
            if started and depth <= 0:
                break
            j += 1
        body = lines[i:j + 1]

        bounds = []
        for k, bl in enumerate(body):
            am = arm_re.match(bl)
            if am:
                bounds.append((k, am.group(2)))
                continue
            im = iff_re.match(bl)
            if im:
                bounds.append((k, im.group(2)))

        arms = {}
        for idx, (k, nm) in enumerate(bounds):
            stop = bounds[idx + 1][0] if idx + 1 < len(bounds) else len(body)
            text = '\n'.join(body[k:stop])
            symbols = sorted(set(__import__('re').findall(r'((?:icall|il2cpp)_\w+)', text)))
            throws = 'throw new NotSupportedException' in text
            line_no = i + k + 1          # 1-based source line of the case label
            if nm in arms:
                # A multi-label arm (`case "A": case "B":`) shares one body; the
                # FIRST label's line is the one a `file:line` citation should use,
                # so it is kept and only the symbol/throw unions accumulate.
                arms[nm]['symbols'] = sorted(set(arms[nm]['symbols']) | set(symbols))
                arms[nm]['throws'] = arms[nm]['throws'] or throws
            else:
                arms[nm] = dict(symbols=symbols, throws=throws, line=line_no)

        prefixes = {}
        for k in range(len(body)):
            pm = prefix_re.search(body[k])
            if pm:
                prefixes[pm.group(2)] = pm.group(1)

        funcs[fname] = dict(file=fn, line=i + 1, names=arms, prefixes=prefixes)
        i = j + 1

missing = sorted({f for fs in owner_funcs.values() for f in fs if f not in funcs})
print('owner cases', len(owner_case_line))
print('groups', len(groups))
print('funcs', len(funcs))
print('owner funcs not parsed', missing)

OUT = sys.argv[2] if len(sys.argv) > 2 else \
    r'C:\Users\liulilte\AppData\Local\Temp\opencode\icall-audit\arms.json'
json.dump({'owner_funcs': owner_funcs, 'owner_case_line': owner_case_line, 'funcs': funcs},
          io.open(OUT, 'w', encoding='utf-8'), indent=1)
print('wrote arms.json')
