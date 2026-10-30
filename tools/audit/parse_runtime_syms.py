"""Index the runtime icall surface: symbol -> declaration site, definition site,
and whether the definition is a placeholder rather than a real implementation.

Two placeholder shapes are indexed:

  loud  -- the body deliberately rejects at runtime: it calls
           `il2cpp_throw_not_supported_exception` as its only exit, or the
           symbol carries the `*_loud` naming convention.
  const -- the body delivers no computation at all: it is empty (a no-op), or
           it is a single `return <constant>;` statement (`return 0;`,
           `return nullptr;`, `return false;`, ...). Such a body is a fixed
           answer that cannot observe its arguments, so it is a placeholder for
           the real net48 query even when the fixed value happens to be the
           correct answer on the host.

Both shapes are reported as `half` by the classifier: the transpiler emits a
call, but the runtime does not perform the operation. Treating either as
"wired" would let the audit claim a delivered feature.

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


# A single-statement body that returns a LITERAL cannot observe its arguments:
# the answer is a compiled-in constant, not the result of the net48 operation.
CONST_RETURN = re.compile(
    r'^return\s+(-?\d+|true|false|nullptr|NULL|0[xX][0-9a-fA-F]+|-?\d+\.\d*f?)\s*$')


def _body_statements(body):
    """Split a comment-stripped body into statements, dropping the braces.

    Semicolons inside a nested block are kept in their own statement, so a
    one-liner guard (`if (x) return 0;` then more code) never collapses into a
    single "constant return".
    """
    inner = body.strip()
    if inner.startswith('{') and inner.endswith('}'):
        inner = inner[1:-1]
    return [s.strip() for s in inner.split(';') if s.strip()]


def is_constant_stub(sym, body):
    """Whether the definition is empty, or answers with a literal constant.

    Recognised shapes (after comment stripping):
      * an empty body -- `{ }` or `{ }` with only `(void)arg;` noise;
      * exactly one statement, `return <literal>;`.

    Deliberately NOT recognised: `return some_global;`, delegations, and any
    body with more than one statement. Those do observe state and are real
    implementations even when short.
    """
    if re.search(r'_loud$', sym):
        return False                       # already a loud stub, different class
    stmts = _body_statements(body)
    stmts = [s for s in stmts if not re.match(r'^\(void\)', s)]
    if not stmts:
        return True
    if len(stmts) == 1 and CONST_RETURN.match(stmts[0]):
        return True
    return False


decl = {}
for fn in sorted(os.listdir(HDR)):
    if not fn.endswith('.h'):
        continue
    for i, line in enumerate(read(os.path.join(HDR, fn)), 1):
        for sym in SYM_ANY.findall(line):
            decl.setdefault(sym, (fn, i))

def _statement_line(body_lines, first_line_no, pattern, decl_line_no):
    """1-based source line of the first body line matching `pattern`.

    Citing the STATEMENT (the throw or the `return <constant>;`) instead of the
    function's first line makes the evidence checkable: the cited line shows the
    behaviour that disqualifies the implementation. An EMPTY body has no
    statement at all, so it falls back to the declaration line -- that is the
    only citable evidence for a no-op.
    """
    for off, ln in enumerate(body_lines):
        if re.search(pattern, ln):
            return first_line_no + off
    return decl_line_no


# ---- definitions (sources) ----
defs = {}
loud = {}
const = {}
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
            loud[sym] = (fn, _statement_line(body, j + 1, r'il2cpp_throw_not_supported_exception', i + 1))
        elif is_constant_stub(sym, text):
            const[sym] = (fn, _statement_line(body, j + 1, r'\breturn\b', i + 1))
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
print('constant/empty stubs', len(const))
print('declared-only', len(set(decl) - set(defs)))

json.dump({'decl': {k: list(v) for k, v in decl.items()},
           'defs': {k: list(v) for k, v in defs.items()},
           'loud': {k: list(v) for k, v in loud.items()},
           'const': {k: list(v) for k, v in const.items()},
           'inline': {k: list(v) for k, v in inline_defs.items()}},
          io.open(OUT, 'w', encoding='utf-8'), indent=1)
print('wrote', OUT)
