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
# W11 P0-5 R1: the rendered document is an INPUT here (every cited `file:line`
# must appear in it). `--doc <path>` lets CI verify a scratch render without
# touching the tracked document; the default stays the tracked path so
# run-audit.ps1 is unchanged.
DOC = os.path.join(ROOT, 'docs', 'audit', 'icall-mscorlib-current.md')
if '--doc' in sys.argv:
    _i = sys.argv.index('--doc')
    if _i + 1 >= len(sys.argv) or not sys.argv[_i + 1]:
        sys.stderr.write('FATAL: --doc requires a path\n')
        raise SystemExit(3)
    DOC = sys.argv[_i + 1]
if not os.path.exists(DOC):
    sys.stderr.write('FATAL: document not found: %s\n' % DOC)
    raise SystemExit(3)

rows = json.load(io.open(os.path.join(TMP, 'classified.json'), encoding='utf-8'))
text = io.open(DOC, encoding='utf-8').read()
# W11 P0-B: the runtime symbol index. A `wired` row claims the transpiler emits
# a C++ target that the runtime actually defines; the old check only proved the
# EVIDENCE LINE mentioned the name, so a fabricated symbol passed. The index is
# the authority for "this symbol exists in src/runtime".
_rtsyms = json.load(io.open(os.path.join(TMP, 'runtime_syms.json'), encoding='utf-8'))
RUNTIME_SYMS = set(_rtsyms.get('decl', {})) | set(_rtsyms.get('defs', {})) | set(_rtsyms.get('inline', {}))

# Symbols that are legitimately NOT in the runtime index because they are not
# runtime functions at all. Every entry is an EXACT shape -- substring matching
# would let a fabricated name through (proven by the B3-whitelist-abuse probe:
# `icall_fcap_fake_symbol` / `il2cpp_nullable_fake_symbol` must be rejected).
#   * `::marker::` folds (::refeq:: / ::identity:: / ::null:: / ::com_*:: /
#     ::nested_combine::) -- the emitter inlines the operation, no call is emitted;
#   * `::lit::<literal>` folds (::lit::NaN / ::lit::mscorlib / ...) -- a literal
#     the emitter substitutes, not a call;
#   * `System.Delegate_Invoke` -- the delegate-invoke fold marker
#     (MethodRefResolver.cs:2004); the emitter's DelegateInvokeExpr owns the call;
#   * emitter-inlined Nullable`1 markers (il2cpp_nullable_ctor / _has_value /
#     _get_value / _get_value_or_default / _get_value_or_default_arg) -- the
#     emitter synthesises the body (CodeGenerator.cs:11024 IsNullableMemberMarker
#     matches the `il2cpp::runtime::il2cpp_nullable_` prefix);
#   * `il2cpp_stringbuilder_capacity` -- the `fcap` field-capture collapse
#     (CodeGenerator.cs:13088), emitted as a field read, never a call;
#   * macro-generated families (il2cpp_linq_*_fam_*) -- a preprocessor expansion,
#     so the literal name never appears in the source;
#   * generatedHeader inline definitions (il2cpp_double_bits / _from_bits /
#     il2cpp_localloc) -- emitted into the generated header by CodeGenerator.
# MEASURED (audit A/B/main all reproduced): none of these 9 names is in
# runtime_syms.json nor in src/runtime sources, so without this whitelist a
# LEGITIMATE marker row fails (false failure).
MARKER_RE = re.compile(r'^::[\w:]+::$')
LIT_RE = re.compile(r'^::lit::')
GENERATED_INLINE = frozenset(('il2cpp_double_bits', 'il2cpp_double_from_bits', 'il2cpp_localloc'))
EMITTER_MARKERS = frozenset((
    'il2cpp_nullable_ctor',
    'il2cpp_nullable_has_value',
    'il2cpp_nullable_get_value',
    'il2cpp_nullable_get_value_or_default',
    'il2cpp_nullable_get_value_or_default_arg',
    'il2cpp_stringbuilder_capacity',
    'System.Delegate_Invoke',
))
# The macro family in src/runtime/src/linq_family.cpp expands
# `il2cpp_linq_##NAME##_fam_<SUFFIX>`; the SUFFIX set is closed (the element
# kinds the macros instantiate), so pin it instead of accepting any tail -- a
# bare `\w+` tail would whitelist a fabricated `il2cpp_linq_x_fam_bogus`.
LINQ_FAM_RE = re.compile(r'^il2cpp_linq_\w+_fam_(i4|i8|d|ref|dec|dt|dtoffset|guid|ts)$')


def symbol_is_whitelisted(sb, raw=''):
    # `symbolBare` is the last `::` segment, so a fold marker like `::refeq::`
    # reduces to the empty string -- check the RAW symbol for the marker shape.
    if raw and (MARKER_RE.match(raw) or LIT_RE.match(raw)):
        return True
    if not sb:
        return False
    if MARKER_RE.match(sb) or LIT_RE.match(sb):
        return True
    if sb in GENERATED_INLINE or sb in EMITTER_MARKERS:
        return True
    if LINQ_FAM_RE.match(sb):
        return True
    return False


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
no_wiring = 0

# W11 P0-B (1): a row with NO site is only acceptable when it is a `missing`
# row that the report renders as the explicit `no wiring` cell. The old code
# `continue`d on every site-less row, so a corpus where ALL 908 rows lost their
# evidence still printed "failures: 0" and exited 0 -- a vacuous green.
for r in rows:
    site = r.get('site')
    if not site:
        if r['status'] == 'missing':
            no_wiring += 1
            continue
        fail.append((r['owner'], r['name'], '<no site>',
                     'status=' + r['status'] + ' has no evidence site (only missing rows may be `no wiring`)'))
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
        # W11 P0-B (2): a `wired` row claims the runtime DELIVERS the target, so
        # the target symbol must exist in src/runtime (declared, defined, or an
        # inline header definition). The old check only proved the evidence line
        # mentioned the name, so a fabricated C++ symbol passed 230/231 times.
        sb = r['symbolBare'] or ''
        if not symbol_is_whitelisted(sb, r.get('symbol') or '') and sb not in RUNTIME_SYMS:
            fail.append((r['owner'], r['name'], site,
                         'symbol not in src/runtime: ' + (sb or '<empty>')))
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
print('rows with no wiring:', no_wiring)

# W11 P0-B (1b): coverage. Every row must be ACCOUNTED FOR -- either it carried
# a site that was checked, or it is an explicit `no wiring` missing row. A row
# that silently fell out of the loop (the old `continue`) is a hole in the
# evidence chain, so the count must reconcile exactly.
if checked + no_wiring != len(rows):
    fail.append(('<coverage>', '', '', 'checked %d + no-wiring %d != %d rows'
                 % (checked, no_wiring, len(rows))))

# W11 P0-B (3): uniqueness. The report is keyed by the join key
# (owner, name, sig, kind); a duplicate means one icall's verdict was silently
# overwritten by another's, so the 908-row total can be satisfied while rows are
# missing. The old renderer only asserted the total, so 25 duplicated rows
# passed. (`key` alone is owner::name and legitimately repeats for overloads.)
join_keys = [(r['owner'], r['name'], r['sig'], r.get('kind')) for r in rows]
dups = len(join_keys) - len(set(join_keys))
print('duplicate join keys:', dups)
if dups:
    seen = set()
    for k in join_keys:
        if k in seen:
            fail.append((k[0], k[1], '', 'duplicate join key (sig=%s kind=%s)' % (k[2], k[3])))
            break
        seen.add(k)

# W11 P0-B (4): the signature must be REAL. Uniqueness alone only proves the
# rows differ from each other -- a fabricated signature (e.g. a 5-arg
# String..ctor that net48 does not have) is unique and passed. metadata.tsv is
# the ECMA-335 channel's own enumeration of the 908 icalls, and
# classify_icalls.py:80 already asserts metadata/probe key sets are equal, so
# re-using it here closes the loop: every classified row must exist in the
# metadata channel with the SAME (owner, name, sig, kind).
meta_keys = set()
_meta_path = os.path.join(TMP, 'metadata.tsv')
if os.path.exists(_meta_path):
    for _line in io.open(_meta_path, encoding='utf-8-sig', errors='replace'):
        _f = _line.rstrip('\n').split('\t')
        if len(_f) >= 5:
            meta_keys.add((_f[0], _f[1], _f[2], _f[4]))
print('metadata keys:', len(meta_keys))
if not meta_keys:
    fail.append(('<metadata>', '', '', 'metadata.tsv missing or empty -- cannot cross-check signatures'))
else:
    not_in_meta = [k for k in join_keys if k not in meta_keys]
    print('rows absent from metadata.tsv:', len(not_in_meta))
    for k in not_in_meta[:10]:
        fail.append((k[0], k[1], '', 'signature absent from metadata.tsv (sig=%s kind=%s)' % (k[2], k[3])))

# W11 P0-B (5): the publicapi channel. probe.tsv carries TWO channels -- the 908
# `icall` rows (what classified.json covers) and ~12k `publicapi` rows that the
# report never looked at. A publicapi row marked `resolved` claims the
# transpiler emits a working target, so its symbol must exist in src/runtime
# exactly like a wired icall. The 21 System.Console device members are the live
# proof this was unchecked: the OLD corpus marks them `resolved` while their
# `icall_console_*` symbols exist nowhere (the C++ build fails).
_publicapi_checked = 0
_publicapi_fail = 0
_probe_path = os.path.join(TMP, 'probe.tsv')
if os.path.exists(_probe_path):
    for _line in io.open(_probe_path, encoding='utf-8-sig', errors='replace'):
        _f = _line.rstrip('\n').split('\t')
        if len(_f) < 6 or _f[3] != 'publicapi' or _f[4] != 'resolved':
            continue
        _publicapi_checked += 1
        _sym = _f[5]
        _bare = _sym.rsplit('::', 1)[-1] if _sym else ''
        if symbol_is_whitelisted(_bare, _sym) or _bare in RUNTIME_SYMS:
            continue
        _publicapi_fail += 1
        if _publicapi_fail <= 10:
            fail.append((_f[0], _f[1], 'probe.tsv:publicapi',
                         'resolved but symbol not in src/runtime: ' + (_sym or '<empty>')))
print('publicapi resolved rows checked:', _publicapi_checked)
print('publicapi resolved failures:', _publicapi_fail)
if _publicapi_checked == 0:
    fail.append(('<publicapi>', '', '', 'probe.tsv missing or has no publicapi/resolved rows -- channel not verified'))

# also confirm every doc evidence cell exists in the doc text
missing_cells = 0
for r in rows:
    if r.get('site') and ('`' + r['site'] + '`') not in text:
        missing_cells += 1
print('doc cells not rendered:', missing_cells)

print('failures:', len(fail))
for f in fail[:25]:
    print('  ', f)

if fail or missing_cells:
    sys.exit(1)
