"""Verify every `file:line` the audit report cites.

The report is generated from `classified.json`, and each row carries the single
source line that justifies its verdict. This script re-opens that line and
checks it actually says what the row claims, so a stale line number (a file that
shifted under an edit) is a hard failure rather than a plausible-looking report.

Rules per status:
  wired         -- line selects the member by name, or emits its C++ target
  loud          -- line opens a NotSupportedException throw / LOUD whitelist
  half          -- line is the runtime placeholder (throw stub or const body)
  shadow        -- line selects the ENTRY method (the call the user emits)
  partialShadow -- same as shadow

usage: verify_report.py <auditOutDir> <repoRoot>

exit code: 0 = every cited line checks out, 1 = at least one does not.
"""
import io, json, re, os, sys

TMP = sys.argv[1]
ROOT = sys.argv[2]
EMIT = os.path.join(ROOT, 'src', 'il2cppc', 'Emit')
DOC = os.path.join(ROOT, 'docs', 'audit', 'icall-mscorlib-current.md')

rows = json.load(io.open(os.path.join(TMP, 'classified.json'), encoding='utf-8'))
text = io.open(DOC, encoding='utf-8').read()

src_cache = {}


def src(fn):
    if fn not in src_cache:
        src_cache[fn] = io.open(os.path.join(EMIT, fn), encoding='utf-8-sig').read().split('\n')
    return src_cache[fn]


def runtime_src(fn):
    key = 'rt:' + fn
    if key not in src_cache:
        src_cache[key] = io.open(os.path.join(ROOT, 'src', 'runtime', 'src', fn),
                                 encoding='utf-8-sig', errors='replace').read().split('\n')
    return src_cache[key]


fail = []
checked = 0

for r in rows:
    site = r.get('site')
    if not site:
        continue

    m = re.match(r'(MethodMap\.[\w\.]+\.cs|CodeGenerator\.cs|MethodRefResolver\.cs|StackFold\.cs):(\d+)$', site)
    if not m and '.cpp:' in site:
        # A half row cites the RUNTIME placeholder body: the reason the target
        # cannot deliver. Keeping the check on the runtime tree proves the stub
        # is where the row says it is.
        fn2, n2 = site.rsplit(':', 1)
        path2 = os.path.join(ROOT, 'src', 'runtime', 'src', os.path.basename(fn2))
        if not os.path.exists(path2):
            fail.append((r['owner'], r['name'], site, 'stub file missing'))
            continue
        ls = runtime_src(os.path.basename(fn2))
        idx2 = int(n2) - 1
        if not (0 <= idx2 < len(ls)):
            fail.append((r['owner'], r['name'], site, 'stub line out of range'))
            checked += 1
            continue
        checked += 1
        line = ls[idx2]
        if r['status'] == 'half':
            # Must be the statement that disqualifies the target: the LOUD throw,
            # the constant/no-op return, or (for an EMPTY body, which has no
            # statement) the function's own signature line.
            sym = r['symbolBare'] or ''
            body = ('il2cpp_throw_not_supported_exception' in line
                    or re.search(r'\breturn\b', line) is not None
                    or (sym and sym in line))
            if not body:
                fail.append((r['owner'], r['name'], site, 'not a stub body: ' + line.strip()[:70]))
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
    elif r['status'] in ('shadow', 'partialShadow'):
        # The cited line is the ENTRY's dispatcher arm -- the call the user's IL
        # actually emits. It must select the entry by name, or emit the entry's
        # C++ target when the arm is symbol-only (`case` bodies that name no
        # member), which is still the line that delivers it.
        entry = (r.get('shadow') or {}).get('entry') or ''
        ename = entry.split('::', 1)[-1]
        ok = (f'"{ename}"' in line)
        if not ok:
            fail.append((r['owner'], r['name'], site, 'entry=' + ename + ' line=' + line.strip()[:60]))

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

if fail or missing_cells:
    sys.exit(1)
