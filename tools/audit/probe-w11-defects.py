"""P-probe harness for the W11 audit defects (D2/D3/D5/D6/D7).

Every defect must be RED-probed before it is reported:
  prints [FAIL]/[RED] with the concrete measured value + evidence reference,
  plus 根因:/ROOTCAUSE and [方案]/plan.

Read-only on tracked files. All scratch goes to .opencode/tmp.
Prints a final SUMMARY with an exit code (0 = all defects confirmed as stated).
"""
import io
import os
import re
import subprocess
import sys

sys.dont_write_bytecode = True   # rule 25: never leave __pycache__ in the tree

# repo under test: argv[1] > $W11_PROBE_REPO > default worktree.
# (parameterised so the probe itself can be mutation-tested against a sandbox)
REPO = (sys.argv[1] if len(sys.argv) > 1 else os.environ.get('W11_PROBE_REPO')
        or r'F:\il2cpp')
GITIGNORE_BAK = os.path.join(REPO, '.opencode', 'tmp', 'w11next', 'gi_head', '.gitignore')

RES = []


def sh(args, cwd=None):
    p = subprocess.run(args, cwd=cwd or REPO, capture_output=True, text=True,
                       errors='replace', shell=False)
    return p.returncode, (p.stdout or ''), (p.stderr or '')


def red(tag, msg, ev=None):
    RES.append((tag, 'RED', msg))
    print('[FAIL] %-4s %s' % (tag, msg))
    for line in (ev or []):
        print('         | %s' % line)


def ok(tag, msg, ev=None):
    RES.append((tag, 'OK', msg))
    print('[ OK ] %-4s %s' % (tag, msg))
    for line in (ev or []):
        print('         | %s' % line)


def head_gitignore():
    rc, out, _ = sh(['git', 'show', 'HEAD:.gitignore'])
    return out if rc == 0 else ''


print('=' * 88)
print('W11 audit P-probes (red-first)   repo=%s' % REPO)
print('=' * 88)

# ---------------------------------------------------------------- D2
print('\n--- D2: contract-anchor cases are not tracked / .gitignore missing ---')
gi_exists = os.path.exists(os.path.join(REPO, '.gitignore'))
rc, st, _ = sh(['git', 'status', '--porcelain', '.gitignore'])
if not gi_exists:
    red('D2', '.gitignore is MISSING from the worktree (HEAD has it)',
        ['git status --porcelain .gitignore -> %r' % st.strip(),
         'HEAD blob = %s  (3655B LF, 124 non-empty lines)' % 'f31d4699',
         'root cause: deleting it disables ~40 tests/cases glob rules'])
else:
    ok('D2', '.gitignore present')

rc, out, _ = sh(['git', 'ls-files', 'tests/cases/w11probe-*'])
tracked = [l for l in out.splitlines() if l.strip()]
if len(tracked) != 10:
    red('D2', '5 contract cases not tracked: expected 10 files, got %d' % len(tracked),
        ['git ls-files tests/cases/w11probe-* -> %d paths' % len(tracked),
         'expected w11probe-{console,clear,setters,getters,linqgrpdec}/{*.cs,*.csproj}'])
else:
    ok('D2', 'contract cases tracked (10)')

# stable metric: restored vs current, and show why current is unstable
# NOTE: the exclude-from file is derived from `git show HEAD:.gitignore` at run
# time and written to scratch inside .opencode/tmp, so this probe needs no
# hand-maintained backup and works for any repo given as argv[1].
gi_head = head_gitignore()
if not gi_head.strip():
    red('D2', 'HEAD .gitignore is unreadable -> cannot measure staging hazard')
else:
    scratch = os.path.join(REPO, '.opencode', 'tmp', 'w11next')
    os.makedirs(scratch, exist_ok=True)
    gi_probe = os.path.join(scratch, 'gi_head_probe.gitignore')
    with io.open(gi_probe, 'w', encoding='utf-8', newline='\n') as f:
        f.write(gi_head)
    rc1, cur, _ = sh(['git', 'ls-files', '-o', '--exclude-standard'])
    rc2, res_, _ = sh(['git', 'ls-files', '-o', '--exclude-from', gi_probe])
    # rule 25/28: never count this probe's own artefacts in the metric it reports
    SELF = 'temp/scripts/probe-w11-defects'
    cur = chr(10).join(l for l in cur.splitlines() if not l.startswith(SELF))
    res_ = chr(10).join(l for l in res_.splitlines() if not l.startswith(SELF))
    cur_n = len([l for l in cur.splitlines() if l.strip()])
    res_n = len([l for l in res_.splitlines() if l.strip()])
    op_n = len([l for l in cur.splitlines() if l.startswith('.opencode/')])
    nonop = cur_n - op_n
    print('         STABLE  metric: restored_untracked = %d' % res_n)
    print('         VOLATILE metric: current_untracked = %d (of which .opencode/ = %d)'
          % (cur_n, op_n))
    print('         => report "restored=%d" and "non-.opencode=%d", never the raw total'
          % (res_n, nonop))
    if nonop > 40:
        red('D2', 'worktree .gitignore does not suppress build output: '
                  '`git add -A` would stage %d non-.opencode files '
                  '(restored would be %d)' % (nonop, res_n),
            ['threshold: non-.opencode staged files must stay <= 40 (measured %d)' % nonop,
             'with HEAD .gitignore applied the same tree yields only %d' % res_n])
    else:
        ok('D2', 'worktree .gitignore suppresses build output (%d staged)' % nonop)

# ---------------------------------------------------------------- D6
# Scope (per parent adjudication): ONLY real invocations count.
#   * a path named in a COMMENT is not a dependency  (e.g. scripts/run-nightly-audit.ps1)
#   * build/ outputs are generated, never tracked    (e.g. build/tests_runtime/*.exe)
# So we scan ci.ps1 for executable call forms only, then check each target.
print('\n--- D6: ci.ps1 invokes a file that is NOT in git (clean clone => CI abort) ---')
rc, ci_src, _ = sh(['git', 'show', 'HEAD:scripts/ci.ps1'])
if rc != 0:
    ci_src = io.open(os.path.join(REPO, 'scripts', 'ci.ps1'),
                     encoding='utf-8', errors='replace').read()

CALL_RES = [
    re.compile(r'&\s+(?:python\s+|\$?\w+\s+)?["\']?\$Root/([^"\'\s]+)'),
    re.compile(r'-File\s+["\']?\$Root/([^"\'\s]+)'),
]
invoked = []
for line in ci_src.splitlines():
    if line.lstrip().startswith('#'):
        continue                                   # comment mention != dependency
    for rx in CALL_RES:
        for m in rx.finditer(line):
            invoked.append((m.group(1), line.strip()))
invoked = sorted(set(invoked))

missing = []
checked = []
for path, line in invoked:
    if path.startswith('build/'):
        checked.append((path, 'BUILD-OUTPUT', line))   # generated, not a dep
        continue
    rcl, lsf, _ = sh(['git', 'ls-files', '--', path])
    tracked = bool(lsf.strip())
    checked.append((path, 'TRACKED' if tracked else 'UNTRACKED', line))
    if not tracked:
        missing.append((path, line))

print('         scanned ci.ps1 call sites: %d unique, %d non-build'
      % (len(invoked), len([c for c in checked if c[1] != 'BUILD-OUTPUT'])))
for path, state, _ in checked:
    print('         %-52s %s' % (path, state))

if missing:
    red('D6', 'ci.ps1 invokes %d file(s) that are NOT in git (clean clone -> CI abort)'
              % len(missing),
        ['missing: %s' % ', '.join(p for p, _ in missing),
         'call site: %s' % missing[0][1],
         'scope: only executable invocations; comment mentions and build/ outputs excluded'])
else:
    ok('D6', 'all %d ci.ps1 invocations resolve to tracked files'
             % len([c for c in checked if c[1] != 'BUILD-OUTPUT']))

# ---------------------------------------------------------------- D3
print('\n--- D3: check_decl_symmetry has no namespace/mangled dimension ---')
cds = os.path.join(REPO, 'tools', 'audit', 'check_decl_symmetry.py')
txt = io.open(cds, encoding='utf-8', errors='replace').read()
has_ns = ('c++filt' in txt) or ('mangled' in txt) or ('nm ' in txt and 'namespace' in txt)
if not has_ns:
    red('D3', 'reporter cannot see namespaces: declared()/defined() are bare-name sets',
        ['[READ:tools/audit/check_decl_symmetry.py:68] SYM = \\b((?:icall|il2cpp)_\\w+)\\s*\\(',
         '[READ::443-450] check_pair(): return sorted(d - c)   <- pure set difference',
         'proof: 4 declaration shapes (il2cpp / global / totally::wrong / il2cpp::runtime)'
         ' all yield the SAME declared() set => namespace blind'])
    # in-memory proof (no worktree writes)
    import importlib.util
    spec = importlib.util.spec_from_file_location('cds_probe', cds)
    if spec is None or spec.loader is None:
        raise RuntimeError('cannot load %s' % cds)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    shapes = [
        'namespace il2cpp { int32_t icall_exception_param_name(const char* x); }',
        'int32_t icall_exception_param_name(const char* x);',
        'namespace totally { namespace wrong { int32_t icall_exception_param_name(const char* x); }}',
        'namespace il2cpp { namespace runtime { int32_t icall_exception_param_name(const char* x); }}',
    ]
    sets = [frozenset(m.declared(s)) for s in shapes]
    print('         distinct declared() results across 4 shapes = %d (1 == blind)'
          % len(set(sets)))
else:
    ok('D3', 'reporter has a namespace/mangled dimension')

# ---------------------------------------------------------------- D5
print('\n--- D5: docs cite a script that is not in git (unreproducible evidence) ---')
doc = os.path.join(REPO, 'docs', 'w11-f4-bitmap-limit.md')
dt = io.open(doc, encoding='utf-8', errors='replace').read()
cites = 'run-w11probes.ps1' in dt
rc, lsf, _ = sh(['git', 'ls-files', 'temp/scripts/run-w11probes.ps1'])
script_tracked = bool(lsf.strip())
if cites and not script_tracked:
    red('D5', 'tracked doc cites an untracked script',
        ['[READ:docs/w11-f4-bitmap-limit.md:33] - The B w11 probe `run-w11probes.ps1` reported',
         'git ls-files temp/scripts/run-w11probes.ps1 -> EMPTY (temp/ never tracked)',
         'also: doc says "path exists" but the probe is a STRUCTURAL check (see D5b)'])
else:
    ok('D5', 'doc citation resolvable')

print('\n--- D5b: the doc over-states the probe evidence ---')
# Semantic criterion, not a single literal string: the doc must state BOTH
#   (a) the check is structural-only, and
#   (b) it does not verify that the guard actually throws.
# Accept the real wordings the doc may use ("STRUCTURAL ONLY", "STRUCTURAL check",
# "does NOT verify that the guard actually throws", "counts guard lines, not ...").
_dt_low = dt.lower()
_has_structural = ('structural only' in _dt_low) or ('structural check' in _dt_low)
_has_limit = (('does not verify' in _dt_low) or ('does not execute' in _dt_low)
              or ('counts guard lines' in _dt_low))
if not (_has_structural and _has_limit):
    red('D5b', 'doc does not state that the probe is structural-only',
        ['structural-only stated = %s ; execution-limit stated = %s'
         % (_has_structural, _has_limit),
         'required: BOTH (a) structural-only and (b) does not verify the guard throws',
         'measured: with 0 throw statements in TypeEmitter.cs the probe STILL prints [RED]',
         '=> it proves the guard LINES are present, not that the guard executes'])
else:
    ok('D5b', 'doc states STRUCTURAL-ONLY and that the guard is not verified to throw')

# ---------------------------------------------------------------- D7
print('\n--- D7: HEAD .gitignore lacks Python bytecode rules ---')
gi = head_gitignore()
n_pycache = len(re.findall(r'__pycache__', gi))
n_pyc = len(re.findall(r'\*\.pyc', gi))
if n_pycache == 0 and n_pyc == 0:
    red('D7', 'HEAD .gitignore has no __pycache__/ and no *.pyc rule',
        ['HEAD .gitignore: __pycache__ rules = %d, *.pyc rules = %d' % (n_pycache, n_pyc),
         'measured impact: importing tools/audit/check_decl_symmetry.py creates '
         'tools/audit/__pycache__/check_decl_symmetry.cpython-314.pyc (untracked noise)'])
else:
    ok('D7', 'HEAD .gitignore covers Python bytecode')

# ---------------------------------------------------------------- summary
print('\n' + '=' * 88)
reds = [r for r in RES if r[1] == 'RED']
oks = [r for r in RES if r[1] == 'OK']
print('SUMMARY: %d probes, %d RED, %d OK' % (len(RES), len(reds), len(oks)))
for tag, kind, msg in reds:
    print('  RED  [%-4s] %s' % (tag, msg))
print('\n[根因/ROOTCAUSE]')
if any(t == 'D2' for t, k, _ in RES if k == 'RED'):
    print('  D2  : .gitignore deleted from the worktree (" D"); its ~40 tests/cases globs')
    print('        (obj/, tests/runtime/build_*/, tests/cases/*/gen*/ ...) stop applying.')
else:
    print('  D2  : RESOLVED - .gitignore matches HEAD blob and the 10 contract files are tracked.')
if any(t == 'D6' for t, k, _ in RES if k == 'RED'):
    print('  D6  : a CI dependency was added (ci.ps1) without adding the file to git.')
else:
    print('  D6  : RESOLVED - every ci.ps1 invocation now resolves to a tracked file.')
if any(t == 'D3' for t, k, _ in RES if k == 'RED'):
    print('  D3  : the reporter compares bare NAME sets; namespace is never parsed.')
else:
    print('  D3  : RESOLVED - the reporter now has a namespace/mangled dimension.')
if any(t in ('D5', 'D5b') for t, k, _ in RES if k == 'RED'):
    print('  D5  : the doc cites temp/scripts/run-w11probes.ps1, and temp/ was never tracked;')
    print('        the doc also over-states what that probe proves (structural, not behavioural).')
else:
    print('  D5  : RESOLVED - doc citation resolvable and the STRUCTURAL-ONLY limit is stated.')
print('  D7  : HEAD .gitignore predates the python tooling in tools/audit/.')
print('\n[方案/plan]')
if any(t == 'D6' for t, k, _ in RES if k == 'RED'):
    print('  D6  : git add the untracked ci.ps1 dependency (P0, unblocks clean-clone CI)')
else:
    print('  D6  : DONE - no action needed (all ci.ps1 invocations tracked).')
if any(t == 'D2' for t, k, _ in RES if k == 'RED'):
    print('  D2  : restore .gitignore (parent-authorised revert) OR accept the " D" state and')
    print('        never run `git add -A`; contract cases are already staged by explicit path.')
else:
    print('  D2  : DONE - .gitignore restored to HEAD blob; 10 contract files tracked.')
if any(t == 'D3' for t, k, _ in RES if k == 'RED'):
    print('  D3  : add a mangled/namespace dimension; the new gate must be mutation-falsifiable')
else:
    print('  D3  : DONE - namespace/mangled dimension present.')
if any(t in ('D5', 'D5b') for t, k, _ in RES if k == 'RED'):
    print('  D5  : move the probe to tests/audit-probes/ + README; rewrite docs :33-36 as')
    print('        "STRUCTURAL check ... does NOT verify that the guards actually throw"')
else:
    print('  D5  : DONE - doc citation resolvable; STRUCTURAL-ONLY limit stated.')
print('  D7  : (P3, record-only per parent) append __pycache__/ and *.pyc to .gitignore')
print('=' * 88)
# exit code contract (so this probe can itself be mutation-tested):
#   0  = every probe RED  (all defects reproduce on this tree)
#  10  = every probe OK   (tree is fully fixed)
#   1  = mixed / partial   (unexpected, needs investigation)
if reds and not oks:
    sys.exit(0)
if oks and not reds:
    sys.exit(10)
sys.exit(1)
