"""Index the runtime icall surface: symbol -> declaration site, definition site,
and whether the definition is a LOUD throw stub rather than a real
implementation.

A definition whose body calls `il2cpp_throw_not_supported_exception` (or the
`*_loud` naming convention) deliberately rejects at runtime. Treating such a
symbol as "wired" would let the audit report a delivered feature where the
runtime in fact throws, so the distinction is kept explicitly.

usage: parse_runtime_syms.py <repoRoot> <out.json>
"""
import io, os, json, re, sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else r'F:\il2cpp\.worktrees\audit-mscorlib-icall'
OUT = sys.argv[2] if len(sys.argv) > 2 else r'C:\Users\liulilte\AppData\Local\Temp\opencode\icall-audit\runtime_syms.json'

HDR = os.path.join(ROOT, 'src', 'runtime', 'include', 'internal')
SRC = os.path.join(ROOT, 'src', 'runtime', 'src')

SYM = re.compile(r'\b((?:icall|il2cpp)_\w+)\s*\(')
SYM_ANY = re.compile(r'\b((?:icall|il2cpp)_\w+)\s*\(')


def read(p):
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        return f.read().split('\n')


# ---- declarations (headers) ----

def strip_comments(text):
    text = re.sub(r'/\*.*?\*/', ' ', text, flags=re.S)
    text = re.sub(r'//[^\n]*', ' ', text)
    return text


def is_pure_loud_stub(sym, body):
    """Whether the runtime definition does nothing but reject.

    A body that merely CONTAINS a NotSupported throw in one arm (e.g. an
    unsupported VARIANT kind) still delivers the supported cases, so it must not
    be counted as a stub. The stub test is: the name carries the `_loud`
    convention, or the body's only exit is the NotSupported throw.
    """
    if re.search(r'_loud$', sym):
        return True
    if 'il2cpp_throw_not_supported_exception' not in body:
        return False

    # Any other runtime call, or any early-return ARGUMENT/NULL check that then
    # continues, means real work happens before the rejection.
    other_calls = re.findall(r'\b(il2cpp_throw_\w+|il2cpp_\w+|icall_\w+)\s*\(', body)
    other_calls = [c for c in other_calls if c != 'il2cpp_throw_not_supported_exception']
    if other_calls:
        return False

    # A body with only the throw, an optional `(void)` cast and a `return
    # nullptr/0` tail is a pure stub.
    meaningful = [ln.strip() for ln in body.split('\n')]
    meaningful = [ln for ln in meaningful
                  if ln and ln not in ('{', '}')
                  and not ln.startswith('(void)')
                  and not re.match(r'^(return\s+(nullptr|0|false|0\.0f?|NULL)\s*;|return\s*;)$', ln)]
    return len(meaningful) <= 2


decl = {}
for fn in sorted(os.listdir(HDR)):
    if not fn.endswith('.h'):
        continue
    for i, line in enumerate(read(os.path.join(HDR, fn)), 1):
        for sym in SYM_ANY.findall(line):
            decl.setdefault(sym, (fn, i))

# ---- definitions (sources) ----
defs = {}
loud = {}
inline_defs = {}

for fn in sorted(os.listdir(SRC)):
    if not fn.endswith('.cpp'):
        continue
    lines = read(os.path.join(SRC, fn))
    i = 0
    while i < len(lines):
        line = lines[i]
        if not line or line[0] in ' \t/#':
            i += 1
            continue
        m = re.match(r'^[\w\s\*&:<>,\.]*?\b((?:icall|il2cpp)_\w+)\s*\(', line)
        if not m:
            i += 1
            continue

        sym = m.group(1)
        # join the signature until the opening brace (or a ';' prototype)
        j = i
        joined = line
        while j < len(lines) - 1 and not re.search(r'[;{]', joined):
            j += 1
            joined += lines[j].strip()

        if joined.rstrip().endswith(';'):
            i = j + 1
            continue

        # body: brace-balanced scan
        depth = 0
        started = False
        k = j
        body = []
        while k < len(lines):
            depth += lines[k].count('{') - lines[k].count('}')
            if '{' in lines[k]:
                started = True
            body.append(lines[k])
            if started and depth <= 0:
                break
            k += 1

        defs.setdefault(sym, (fn, i + 1))

        text = strip_comments('\n'.join(body))
        if is_pure_loud_stub(sym, text):
            loud[sym] = (fn, i + 1)
        i = k + 1

# inline definitions in headers count as implementations
for fn in sorted(os.listdir(HDR)):
    if not fn.endswith('.h'):
        continue
    for i, line in enumerate(read(os.path.join(HDR, fn)), 1):
        if 'inline' not in line:
            continue
        m = SYM.search(line)
        if m:
            defs.setdefault(m.group(1), (fn, i))
            inline_defs[m.group(1)] = (fn, i)

print('declared symbols', len(decl))
print('defined symbols', len(defs))
print('inline definitions', len(inline_defs))
print('loud stubs', len(loud))
print('declared-only', len(set(decl) - set(defs)))

json.dump({'decl': {k: list(v) for k, v in decl.items()},
           'defs': {k: list(v) for k, v in defs.items()},
           'loud': {k: list(v) for k, v in loud.items()},
           'inline': {k: list(v) for k, v in inline_defs.items()}},
          io.open(OUT, 'w', encoding='utf-8'), indent=1)
print('wrote', OUT)
