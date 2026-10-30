import io, json, re, os, sys

TMP = sys.argv[1]
ROOT = sys.argv[2]
EMIT = os.path.join(ROOT, 'src', 'il2cppcpp')
EMIT = os.path.join(ROOT, 'src', 'il2cppc', 'Emit')
DOC = os.path.join(ROOT, 'docs', 'audit', 'icall-mscorlib-current.md')

rows = json.load(io.open(os.path.join(TMP, 'classified.json'), encoding='utf-8'))
text = io.open(DOC, encoding='utf-8').read()

src_cache = {}


def src(fn):
    if fn not in src_cache:
        src_cache[fn] = io.open(os.path.join(EMIT, fn), encoding='utf-8-sig').read().split('\n')
    return src_cache[fn]


fail = []
checked = 0

for r in rows:
    site = r.get('site')
    if not site:
        continue
    m = re.match(r'(MethodMap\.[\w\.]+\.cs):(\d+)$', site)
    if not m and '.cpp:' in site:
        # a half row cites the RUNTIME throw stub, which lives in src/runtime
        fn2, n2 = site.rsplit(':', 1)
        path2 = os.path.join(ROOT, 'src', 'runtime', 'src', os.path.basename(fn2))
        if not os.path.exists(path2):
            fail.append((r['owner'], r['name'], site, 'stub file missing'))
            continue
        ls = io.open(path2, encoding='utf-8-sig').read().split('\n')
        idx2 = int(n2) - 1
        if not (0 <= idx2 < len(ls)):
            fail.append((r['owner'], r['name'], site, 'stub line out of range'))
        checked += 1
        continue
    if not m:
        fail.append((r['owner'], r['name'], site, 'unparseable'))
        continue
    fn, n = m.group(1), int(m.group(2))
    checked += 1

    lines = src(fn)
    if not (1 <= n <= len(lines)):
        fail.append((r['owner'], r['name'], site, 'out of range'))
        continue
    line = lines[n - 1]

    if r['status'] == 'wired':
        name = r['name']
        sym = (r['symbolBare'] or '').rsplit('::', 1)[-1]
        # the evidence line must either select this member by name, or emit
        # its C++ target (the multi-name `case a: case b:` shape shares one line)
        ok = (f'"{name}"' in line) or (sym and sym in line)
        if not ok:
            fail.append((r['owner'], r['name'], site, 'line=' + line.strip()[:70]))
    elif r['status'] in ('loud', 'half'):
        ok = ('throw new NotSupportedException' in line
              or 'LOUD whitelist' in line
              or line.strip().startswith('$"')
              or line.strip().startswith('"'))
        if not ok:
            fail.append((r['owner'], r['name'], site, 'line=' + line.strip()[:70]))

print('rows with site:', checked)
print('failures:', len(fail))
for f in fail[:25]:
    print('  ', f)

# also confirm every doc evidence cell exists in the doc text
missing_cells = 0
for r in rows:
    if r.get('site') and ('`' + r['site'] + '`') not in text:
        missing_cells += 1
print('doc cells not rendered:', missing_cells)
