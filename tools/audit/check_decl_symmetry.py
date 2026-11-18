"""W11 P0-D: header/definition symmetry check for the runtime icall surface.

WHY THIS EXISTS
---------------
`il2cpp_linq_grouping_first_dec` / `_last_dec` were DEFINED in linq_map.cpp (via
the IL2CPP_LINQ_GRP_DEC_OPS macro) but never DECLARED in linq_map.h. The
transpiler emitted `il2cpp::il2cpp_linq_grouping_first_dec(...)` and the C++
build failed with "is not a member of 'il2cpp'". Nothing in the repo compared
the two sets, so the gap survived every audit: the earlier scans only looked at
`il2cpp::runtime::`-prefixed literals, and this symbol lives in `il2cpp::`.

WHAT IT CHECKS
--------------
For every (header, source) pair given on the command line, collect the
`il2cpp_*` / `icall_*` function names that the SOURCE defines and the ones the
HEADER declares, then assert `defined - declared == {}`.

Macro-generated definitions are handled by expanding the object-like macros
that build a name from `##` (e.g. `il2cpp_linq_##NAME##_fam_i4`): the script
collects the macro's NAME argument values from its invocation sites and
substitutes them, so a macro family is compared by its REAL symbol names rather
than being skipped. Literal names spelled out INSIDE a macro body are collected
too (that is the P0-D shape: `IL2CPP_LINQ_GRP_DEC_OPS` names
`il2cpp_linq_grouping_first_dec` in full, indented, so a line-start scan misses
it).

REAL GAP vs INTERNAL
--------------------
`defined - declared` alone is NOT the verdict: the runtime deliberately keeps
TU-local helpers undeclared (e.g. `il2cpp_linq_order_by_key_dec_*`,
`il2cpp_throw_invalid_operation_exception`). A symbol is a REAL GAP only when
the transpiler can actually emit a call to it, so the check subtracts the
emission surface:

    REAL GAP = (defined - declared) ∩ emitted_by_transpiler()

`emitted_by_transpiler()` scans `ROOT/src/il2cppc/Emit/*.cs`.

*** ROOT MUST BE A COMPLETE REPO CHECKOUT (it must contain src/il2cppc/Emit) ***
If `src/il2cppc/Emit` is missing (e.g. a scratch copy that only mirrors
`src/runtime`), the emission surface is EMPTY, every gap is classified
"internal-only", and the script reports `REAL GAPS: 0` -- a FALSE GREEN.
This was observed in practice: a scratch tree without Emit reported 0 gaps for
a header that genuinely lacked the two P0-D declarations. The script now prints
an explicit warning when the emission surface is empty, but the caller must
still point ROOT at the real repository.

ABI DIMENSION (W11 P0-11-R3-D3)
-------------------------------
The set test above compares BARE IDENTIFIERS. It is blind to the namespace a
declaration sits in, so a declaration that moved to the wrong namespace still
satisfies `defined - declared == {}` while every emitted call fails to link
(the call mangles to `...::ns::fn` and no TU defines that symbol). The check is
therefore paired with a COMPILER-BASED ABI test: for every (header, symbol)
pair where the tree also defines the symbol, a probe TU includes the header and
`using`s the symbol from the namespace the DEFINITION was compiled into. g++
then resolves the declaration; the address-of in the same TU forces the mangled
name out, and it is compared VERBATIM with the `T` name from `nm` on the
definition object. No regex ever guesses a namespace.
The `ABI:` line always prints the coverage (checked/match/mismatch/skipped,
plus overload pairs that can only be `using`-checked and the reason for every
skip), so a hole in coverage is never silent. A missing toolchain is
INCONCLUSIVE (exit 2), never a quiet pass; `--no-abi` is the explicit opt-out.

usage: check_decl_symmetry.py <repoRoot> [--pair header:source ...] [--no-abi]
exit:  0 = no REAL GAP and no ABI mismatch, 1 = at least one of either,
       2 = emission surface empty or ABI probe inconclusive (ROOT is not a
       complete checkout, or no C++ toolchain was found)
"""
import concurrent.futures
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = sys.argv[1] if len(sys.argv) > 1 else r"F:\il2cpp"
HDR = os.path.join(ROOT, "src", "runtime", "include", "internal")
SRC = os.path.join(ROOT, "src", "runtime", "src")

# Default pairs: the two files this defect class was found in, plus the whole
# runtime tree as a single pair (every header vs every source).
DEFAULT_PAIRS = [
    ("linq_map.h", "linq_map.cpp"),
    ("linq_family.h", "linq_family.cpp"),
]

SYM = re.compile(r"\b((?:icall|il2cpp)_\w+)\s*\(")
# A macro body that builds a symbol name: il2cpp_linq_##NAME##_fam_i4
MACRO_NAME = re.compile(r"#define\s+(\w+)\s*\(([^)]*)\)")
# A token-paste name: an identifier run containing at least one `##`, ending at
# `(`. It must NOT require an `il2cpp_`/`icall_` prefix. The header-side
# declaration macros paste a SUFFIX onto a parameter (`NAME##_rs_i4`, linq.h
# :498-506), so a prefix-anchored pattern skipped all 16
# `il2cpp_linq_aggregate_seed_cell_*_rs_*` declarations and reported them as
# gaps. C10: the shapes below were confirmed against `g++ -E -P` output, not
# against source text (a macro-built name does not exist literally in source).
TOKEN_PASTE = re.compile(r"([A-Za-z_]\w*(?:\s*##\s*[A-Za-z_]\w*)+)\s*\(")

# Macro invocations whose paste parts could not be resolved to a real argument.
# A non-empty set means the checker cannot decide -> PROBE-BROKEN (exit 2),
# never a fabricated symbol (the `..._f_KEYFAM` phantom: KEYFAM is a macro
# PARAMETER name, not a symbol).
UNRESOLVED = set()


def read(path):
    with io.open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    text = re.sub(r"//[^\n]*", " ", text)
    return text


def _parse_params(params):
    return [p.strip() for p in params.split(",") if p.strip()]


# A macro parameter name in this codebase is an ALL-CAPS identifier (NAME, FAM,
# SRC, SFX, KEYFAM, ACC, ELEM, TYPE, READ, INVOKE, ...). A literal paste segment
# is lowercase-ish (`il2cpp_linq_`, `_fam_`, `_rs_i4`, `_dtoffset`). The
# distinction is what lets an unresolvable paste be REPORTED instead of being
# silently turned into a fake symbol.
_PARAM_SHAPE = re.compile(r"^[A-Z][A-Z0-9_]*$")


def _looks_like_param(seg):
    return bool(_PARAM_SHAPE.match(seg))


def _is_known_param_name(seg, text):
    """Is `seg` a parameter name of SOME macro defined in `text`?

    This is the discriminator between the two all-caps shapes:
      * `FAM` / `KEYFAM` / `SRC` -- a parameter name of a macro in this file.
        Seeing it in a paste that does NOT bind it means the paste is
        unresolvable (the `..._where_FAM` phantom class).
      * `I4` / `I8` / `D` / `F` / `S3_RT_T_` -- a LITERAL suffix that merely
        happens to be all-caps (`SelVt##FAM##I4Fn`, `S3_RT_T_##RT`). It is not
        a parameter of any macro, so it is literal text and must be kept.
    """
    for m in MACRO_NAME.finditer(text):
        if seg in _parse_params(m.group(2)):
            return True
    return False


def _split_args(raw):
    """Split a macro argument list on top-level commas.

    Template arguments (`CmpKeyValue<int32_t>`) contain no commas here, but
    nested `<>`/`()`/`[]` are tracked anyway so a future argument like
    `std::pair<int,int>` does not split in the middle.
    """
    parts = []
    depth = 0
    cur = []
    for ch in raw:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
    parts.append("".join(cur).strip())
    return parts


def _expand_macros(text, out):
    """Add the symbols a macro body instantiates.

    Two shapes must both be handled, and missing either one makes the check
    blind to the very defect it exists for:
      * LITERAL names inside a macro body -- `IL2CPP_LINQ_GRP_DEC_OPS` spells
        `il2cpp_linq_grouping_first_dec` out in full (indented, so the plain
        line scan skips it). This is exactly the P0-D defect.
      * `##`-pasted names -- `il2cpp_linq_##NAME##_fam_i4` instantiated by
        `IL2CPP_LINQ_DECL_ELEM_ENTRIES(element_at)`.

    PASTE RESOLUTION (C10). A pasted run is split on `##` and EVERY segment is
    resolved independently:
      * a segment that names a macro PARAMETER is replaced by that parameter's
        argument AT THIS INVOCATION SITE;
      * a segment that is a LITERAL (lowercase-ish text such as `il2cpp_linq_`,
        `_fam_`, `_rs_i4`) is kept as-is;
      * a segment that is an ALL-CAPS identifier which is a parameter name of
        SOME macro in this file but is NOT bound here is UNRESOLVABLE: it is
        recorded in UNRESOLVED and the whole paste is SKIPPED. Emitting it
        would fabricate a symbol that exists in no translation unit.

    NESTED INVOCATIONS are expanded RECURSIVELY: a macro body may invoke
    another paste macro with its own parameter names as arguments
    (`IL2CPP_DECL_LST_AGG_RS(ACC, i4, SEEDTYPE)` inside
    `IL2CPP_DECL_LST_AGG_RS_ROW`). The outer arguments are substituted into the
    inner invocation's argument list and the inner paste is re-expanded, which
    is what the preprocessor does. A paste that still contains an unbound
    parameter name after recursion is UNRESOLVED, never a symbol.

    The previous implementation substituted only the FIRST parameter, so
    `il2cpp_linq_order_by_key_f_##KEYFAM` (KEYFAM is the 3rd parameter of
    `WIDE_LIST_ORDER_BY_ENTRY`) kept the literal text `KEYFAM` and fabricated
    the symbol `il2cpp_linq_order_by_key_f_KEYFAM`, which exists in NO
    translation unit (`g++ -E -P` on linq_list.cpp: 0 occurrences; the real
    names `..._f_i4` / `..._f_d` occur 3x each).
    """
    macros = {}
    for m in MACRO_NAME.finditer(text):
        macros[m.group(1)] = (_parse_params(m.group(2)), _macro_body(text, m.end()))

    def expand(name, amap, depth=0):
        """Symbols produced by macro `name` with parameters bound by `amap`."""
        if depth > 8 or name not in macros:
            return set()
        params, body = macros[name]
        got = set()
        # (a) literal names in the body
        for sm in SYM.finditer(body):
            got.add(sm.group(1))
        # (b) nested invocations of OTHER paste macros
        for im in re.finditer(r"\b(\w+)\s*\(([^;()]*)\)", body):
            inner = im.group(1)
            if inner == name or inner not in macros:
                continue
            iparams, ibody = macros[inner]
            if "##" not in ibody:
                continue
            iargs = _split_args(im.group(2))
            if len(iargs) != len(iparams):
                continue
            iamap = {}
            for p, a in zip(iparams, iargs):
                iamap[p] = amap.get(a, a)
            got |= expand(inner, iamap, depth + 1)
        # (c) pasted names in THIS body
        for pm in TOKEN_PASTE.finditer(body):
            segs = [s.strip() for s in pm.group(1).split("##")]
            resolved = []
            ok = True
            for seg in segs:
                if seg in amap:
                    val = amap[seg]
                    if not val:
                        UNRESOLVED.add("%s: empty argument for parameter %s"
                                       % (name, seg))
                        ok = False
                        break
                    resolved.append(val)
                elif _looks_like_param(seg) and _is_known_param_name(seg, text):
                    UNRESOLVED.add("%s: segment %s is not a parameter"
                                   % (name, seg))
                    ok = False
                    break
                else:
                    resolved.append(seg)
            if not ok:
                continue
            sym = "".join(resolved)
            if not re.match(r"^(?:icall|il2cpp)_\w+$", sym):
                continue
            if any(_is_known_param_name(tok, text) for tok in sym.split("_")):
                UNRESOLVED.add("%s: unbound parameter name in %s" % (name, sym))
                continue
            line_start = body.rfind("\n", 0, pm.start()) + 1
            if re.search(r"\bstatic\b", body[line_start:pm.start()]):
                continue
            got.add(sym)
        return got

    for name, (params, body) in macros.items():
        # Only a macro that PASTES can build a symbol name. A plain macro
        # (`STREAM_FSEEK(f,o,w)`, `IL2CPP_ALLOC_POOL_LOCK()`) never does, and
        # its invocation argument count is irrelevant here -- reporting it as
        # UNRESOLVED would be a false PROBE-BROKEN.
        if "##" not in body:
            continue
        for args in _macro_invocations(text, name):
            if len(args) != len(params):
                UNRESOLVED.add("%s: %d arg(s) for %d param(s)"
                               % (name, len(args), len(params)))
                continue
            # A NESTED invocation (one that appears inside another macro's body)
            # is expanded by the recursive `expand()` above, with the OUTER
            # arguments substituted. Expanding it here as well would bind its
            # parameters to the outer macro's PARAMETER NAMES
            # (`IL2CPP_DECL_LST_AGG_RS(ACC, i4, SEEDTYPE)` -> ACC='ACC'),
            # fabricating `..._aggregate_seed_ACC_i4_rs_i4`. Skip it here.
            if any(a in params or _is_known_param_name(a, text) for a in args):
                continue
            out |= expand(name, dict(zip(params, args)))


def _resolve_nested(text, outer_name, outer_amap, partial_sym):
    """Resolve a paste that a NESTED macro invocation left half-substituted.

    Shape (linq_list.h:870-883):
        #define IL2CPP_DECL_LST_AGG_RS_ROW(ACC, SEEDTYPE)      \\
            IL2CPP_DECL_LST_AGG_RS(ACC, i4, SEEDTYPE)           \\
            ...
        IL2CPP_DECL_LST_AGG_RS_ROW(i4, int32_t)

    Expanding the OUTER macro yields the partial name
    `il2cpp_linq_list_aggregate_seed_ACC_i4_rs_i4` (ACC is the outer parameter,
    not yet bound). The inner macro's own invocation supplies the remaining
    argument (`i4` for ELEM). Substituting the outer arguments into the inner
    invocation's argument list and re-running the paste yields the real name.

    Returns the set of resolved symbols, or None when the shape is not
    recognised (the caller then reports UNRESOLVED rather than inventing a
    name).
    """
    # Which parameter names are still unbound in the partial symbol?
    unbound = [tok for tok in partial_sym.split("_")
               if tok in outer_amap or _is_known_param_name(tok, text)]
    if not unbound:
        return None
    # Find an inner macro whose body pastes a name that could produce this
    # shape, and whose invocation (inside the outer body) supplies the rest.
    for m in MACRO_NAME.finditer(text):
        inner = m.group(1)
        if inner == outer_name:
            continue
        inner_params = _parse_params(m.group(2))
        inner_body = _macro_body(text, m.end())
        if "##" not in inner_body:
            continue
        # The inner invocation must appear inside the OUTER macro's body.
        outer_m = None
        for om in MACRO_NAME.finditer(text):
            if om.group(1) == outer_name:
                outer_m = om
                break
        if outer_m is None:
            continue
        outer_body = _macro_body(text, outer_m.end())
        call = re.search(r"\b" + re.escape(inner) + r"\s*\(([^;()]*)\)", outer_body)
        if not call:
            continue
        inner_args = _split_args(call.group(1))
        if len(inner_args) != len(inner_params):
            continue
        # Bind the inner parameters: an argument that is an OUTER parameter name
        # takes the outer argument; anything else is literal.
        inner_amap = {}
        for p, a in zip(inner_params, inner_args):
            inner_amap[p] = outer_amap.get(a, a)
        # Re-run the paste with the fully bound map.
        got = set()
        for pm in TOKEN_PASTE.finditer(inner_body):
            segs = [s.strip() for s in pm.group(1).split("##")]
            resolved = []
            ok = True
            for seg in segs:
                if seg in inner_amap:
                    v = inner_amap[seg]
                    if not v:
                        ok = False
                        break
                    resolved.append(v)
                else:
                    resolved.append(seg)
            if not ok:
                continue
            s = "".join(resolved)
            if not re.match(r"^(?:icall|il2cpp)_\w+$", s):
                continue
            if any(_is_known_param_name(tok, text) for tok in s.split("_")):
                continue
            got.add(s)
        if got:
            return got
    return None


def declared(header_text):
    """Symbols the header declares (prototypes + inline definitions + macros)."""
    text = strip_comments(header_text)
    out = set()
    for line in text.split("\n"):
        for m in SYM.finditer(line):
            out.add(m.group(1))
    _expand_macros(text, out)
    return out


def _macro_invocations(src_text, macro_name):
    """The FULL argument list at every invocation site.

    The trailing `;` is OPTIONAL: a declaration macro is invoked as
    `IL2CPP_LINQ_DECL_ELEM_ENTRIES(element_at)` with no semicolon, while a
    definition macro is `IL2CPP_LINQ_GRP_DEC_OPS(a, b)`. Requiring `;` silently
    dropped every header-side family (observed: 20 phantom gaps).

    ALL arguments are returned, not just the first: a paste may reference any
    parameter (`WIDE_LIST_ORDER_BY_ENTRY` pastes its 3rd, KEYFAM). Returning
    only the first argument is what fabricated `..._f_KEYFAM`.

    THE `#define` LINE IS NOT AN INVOCATION. `#define M(A, B)` matches the
    invocation regex with args `['A', 'B']`, so the parameter NAMES were fed
    back as if they were arguments -- `il2cpp_linq_where_##FAM` then expanded
    to the literal `il2cpp_linq_where_FAM` (91 such phantoms). A definition
    line is skipped explicitly.
    """
    vals = []
    for m in re.finditer(r"\b" + re.escape(macro_name) + r"\s*\(([^;()]*)\)", src_text):
        line_start = src_text.rfind("\n", 0, m.start()) + 1
        prefix = src_text[line_start:m.start()]
        if re.match(r"\s*#\s*define\b", prefix):
            continue
        vals.append(_split_args(m.group(1)))
    return vals


def _macro_body(text, start):
    """The macro body: from `start` to the last line that ends with a backslash.

    A fixed-size window is WRONG here: it bleeds into the NEXT macro and
    substitutes that macro's parameter names, producing phantom symbols
    (observed: `il2cpp_linq_aggregate_seed_FAM_dec`). The body ends at the first
    line that does not end with a continuation backslash.
    """
    out = []
    for line in text[start:].split("\n"):
        out.append(line)
        if not line.rstrip().endswith("\\"):
            break
    return "\n".join(out)


def defined(source_text):
    """Symbols the source defines, with ##-macros expanded by invocation args.

    `static` definitions are EXCLUDED: a static function is TU-local by
    construction, so "not declared in a header" is its intended state, not a
    gap. (W11 P0-E made 9 dictgen.cpp helpers static for exactly this reason.)
    """
    text = strip_comments(source_text)
    out = set()
    # 1. plain definitions: a line that starts a function whose name matches
    for line in text.split("\n"):
        if line[:1] in (" ", "\t", "#", "/"):
            continue
        # A `static` definition is TU-local. The attribute forms must be
        # recognised too: `[[maybe_unused]] static` (added by W11 P0-11-R3 to
        # silence -Wunused-function on the 11 TU-local string helpers) does NOT
        # start with `static`, so a `^static` test alone let all 11 reappear as
        # LATENT -- the checker reported 11 gaps for symbols it had just been
        # told to ignore.
        if re.match(r"^(?:\[\[[^\]]*\]\]\s*)*static\b", line):
            continue
        for m in SYM.finditer(line):
            out.add(m.group(1))
    # 2. macro-generated definitions
    _expand_macros(text, out)
    return out


def check_pair(header, source):
    hp = os.path.join(HDR, header)
    sp = os.path.join(SRC, source)
    if not os.path.exists(hp) or not os.path.exists(sp):
        return None
    d = defined(read(sp))
    c = declared(read(hp))
    return sorted(d - c), len(d), len(c)


def all_sources():
    return sorted(fn for fn in os.listdir(SRC) if fn.endswith(".cpp"))


def all_headers():
    """Every header under include/ (recursive: include/*.h + include/internal/*.h)."""
    inc = os.path.join(ROOT, "src", "runtime", "include")
    out = []
    for dp, _dn, fns in os.walk(inc):
        for fn in fns:
            if fn.endswith(".h"):
                out.append(os.path.join(dp, fn))
    return sorted(out)


def global_sets():
    """The GLOBAL model: every definition in the tree vs every declaration.

    A declaration may live in ANY header -- the runtime is one library, and a
    symbol declared in a sibling header is still visible to every TU that
    includes it. Comparing per-pair (header X vs source X) produces false
    positives for symbols declared in a shared header (observed:
    `icall_binary_read_bool`).
    """
    d = set()
    for fn in all_sources():
        d |= defined(read(os.path.join(SRC, fn)))
    c = set()
    for path in all_headers():
        c |= declared(read(path))
    return d, c


def emitted_by_transpiler():
    """Every `il2cpp_*` / `icall_*` name the transpiler can emit.

    This is the discriminator that makes the check ACTIONABLE: a symbol that is
    defined-but-not-declared is only a real gap when the transpiler can actually
    emit a call to it. A TU-local helper (or a family member no code path
    selects) is not a gap -- declaring it would be noise.

    TEST FILES ARE EXCLUDED (`*Tests*.cs`, namespace `Il2Cpp.Tests.*`). A test
    file does not emit anything: it ASSERTS what the mapper should produce. If a
    test names a symbol the mapper never emits, that is a stale test, not a
    missing declaration. Measured: `MethodMapTests.ThreadMissing.cs` asserts
    `icall_threadobj_join_timespan` / `icall_thread_sleep_timespan`, while
    MethodMap.Thread.cs:177 says "Join(TimeSpan) stays unwired" -- counting the
    test file as emission surface produced 2 phantom REAL GAPs.

    Returns an EMPTY set when ROOT has no `src/il2cppc/Emit`; the caller treats
    that as INCONCLUSIVE (exit 2), never as "no gaps" -- an empty emission
    surface would classify every real gap as internal-only (false green).
    """
    emit = os.path.join(ROOT, "src", "il2cppc", "Emit")
    out = set()
    if not os.path.isdir(emit):
        return out
    for fn in os.listdir(emit):
        if not fn.endswith(".cs"):
            continue
        if "Tests" in fn:
            continue
        text = read(os.path.join(emit, fn))
        if "namespace Il2Cpp.Tests" in text:
            continue
        for m in re.finditer(r"\b((?:icall|il2cpp)_\w+)", text):
            out.add(m.group(1))
        # CONCATENATED NAMES. The mapper does not always spell the symbol out:
        # `I(cppBase + resSuffix, ...)` (MethodMap.Linq.cs:5219) builds
        # `il2cpp_linq_aggregate_seed_cell_i4_rs_i4` from a literal base plus a
        # suffix chosen by a switch. A literal-only scan cannot see those, so a
        # genuinely emitted symbol was classified internal-only and the
        # mutation gate stayed GREEN (measured: deleting the
        # `NB9_DECL_AGG_CELL_RS(il2cpp_linq_aggregate_seed_cell_i4, int32_t)`
        # declaration moved 4 symbols to internal-only but exit stayed 0).
        #
        # Recover them by pairing every `il2cpp_*`/`icall_*` literal in the file
        # with every `"_suffix"` literal in the SAME file. This over-approximates
        # (a base may be paired with a suffix it never takes), which is the SAFE
        # direction: an over-approximated emission surface turns a real gap into
        # a REAL GAP (loud), never the reverse.
        bases = set(re.findall(r"\b((?:icall|il2cpp)_\w+)", text))
        suffixes = set(re.findall(r'"(_\w+)"', text))
        for b in bases:
            for suf in suffixes:
                out.add(b + suf)
    return out


# ---------------------------------------------------------------------------
# ABI dimension (W11 P0-11-R3-D3): the SET test above is namespace-blind.
#
# MEASURED MOTIVATION: W11 P0-11-R3 added `il2cpp_string_is_ascii_bulk` to
# `il2cpp_string.h` inside `namespace il2cpp::runtime`, while the definition
# lives in `il2cpp::`. The set test reported REAL=0 LATENT=0 -- the name WAS in
# a header -- yet a caller's object file referenced
# `_ZN6il2cpp7runtime27il2cpp_string_is_ascii_bulkEPKNS_12Il2CppStringE` and the
# link failed because the only `T` name was `_ZN6il2cpp27...`. Four such
# declarations existed at once. A name-in-some-header test cannot see this.
#
# WHY NOT A REGEX: the namespace is not recoverable from the header text by
# looking for `namespace X {` blocks (preprocessor conditionals, macros that
# open/close namespaces, and sibling/global-scope declarations all break it),
# and matching the mangled name with `\b` does not even work: `E` is a word
# character, so `\b` never matches between the final `E` and end-of-string.
# The verdict must come from the COMPILER.
#
# HOW IT WORKS (per header, batched -- see abi_check()):
#   1. Compile every `src/runtime/src/*.cpp` once (-O0 -c) and collect the
#      defined function names (`T`/`W`) plus their mangled spelling.
#   2. DEMANGLE those names once (c++filt) to get the real qualified name of
#      every definition, and index it by bare identifier. The qualified name is
#      what a caller must name; it is the oracle, never a guess.
#   3. For each header, emit ONE probe TU:
#          #include "<header>"
#          namespace __abiN { using <qualified>; }   // demand the TYPE of the
#          extern "C" void __probe() {                 // definition here
#            auto* p = &<qualified>; }                 // and its ADDRESS
#      `using` alone rejects a moved declaration ("no member named ..."), the
#      address-of forces the mangled name into the object's undefined list.
#   4. The reportable verdict is per (header, symbol): the mangled name the
#      DECLARATION produced (undefined symbol in the probe object, i.e. what
#      the linker would ask for) must equal the mangled name the DEFINITION
#      produced (`T` name from step 1). A mismatch is a real link failure.
#
# COVERAGE IS PRINTED, NEVER SILENT: every skip reason is counted and the
# reason text for non-trivial ones is echoed, so `skipped=N` can be audited.
#
# FAILURE DIRECTIONS:
#   * overloaded name (2+ definitions share the bare identifier): a `using`
#     of the single definition pins the overload set to exactly that function,
#     so the produced mangled name IS the definition's; compared directly.
#   * the symbol is defined somewhere the compiler could not see (a macro
#     defined name, an inline in a header no probe reaches): SKIPPED, counted.
#   * toolchain missing: INCONCLUSIVE (exit 2), never a green.
# ---------------------------------------------------------------------------

ABI_CXX_STD = "c++17"
# Mirrors src/runtime/CMakeLists.txt: the runtime is built -fno-rtti
# -fexceptions. Rules that change the mangled name must match the real build.
ABI_EXTRA_FLAGS = ["-fno-rtti", "-fexceptions", "-O0"]
_ABI_SYMBOL = re.compile(r"^(?:icall|il2cpp)_\w+$")
_ABI_ABI_TAG = re.compile(r"\[abi:[^\]]*\]")


def _ABI_NAMES(nm_posix_output):
    """First column of every `nm --format=posix` line (the mangled name)."""
    return {line.split()[0] for line in nm_posix_output.splitlines() if line.strip()}


def _abi_find_toolchain():
    """Locate g++/nm/c++filt. Returns None when the toolchain is unusable."""
    names = ("g++", "nm", "c++filt")
    found = {}
    for tool in names:
        path = shutil.which(tool)
        if not path:
            return None
        found[tool] = path
    return found


def _abi_run(cmd, cwd=None):
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True,
                          errors="replace")


def _abi_demangle_all(filt, names):
    """Demangle a list of mangled names in ONE c++filt call (order kept)."""
    if not names:
        return []
    proc = subprocess.run([filt], input="\n".join(names), capture_output=True,
                          text=True, errors="replace")
    out = proc.stdout.splitlines()
    if len(out) != len(names):
        return []
    return out


def _abi_split_qualified(demangled, mangled):
    """(bare name, qualified name, parameter list) from a demangled signature.

    Trailing `[abi:cxx11]` is removed first: it is a tag GCC appends to the
    demangled form and must not leak into a source-level probe.

    THREE SHAPES, all from the COMPILER (`c++filt` on the `T` symbol):
      * C++ function      -> `ns::fn(int*, char const*)`, params `(int*, char const*)`
      * `extern "C"`      -> the BARE name `fn` (C linkage mangles no parameter
        types, so c++filt has nothing to expand); params are unknown -> None
      * data / anonymous  -> rejected (a header cannot declare those here)

    The parameter list is quoted VERBATIM from the definition, so an overload
    set can be disambiguated with `static_cast<R (*)(PARAMS)>(&ns::fn)` and a
    `decltype(ns::fn(__dv<PARAMS>()))` return-type probe. c++filt already emits
    the declaration-order spelling GCC accepts as a function type.

    Returns None for entries that must not be probed.
    """
    text = _ABI_ABI_TAG.sub("", demangled)
    if "(" in text:
        qualified = text.split("(", 1)[0].strip()
        params = text[text.index("("):].strip()
        # A trailing qualifier list (` const`, ` noexcept`) sits AFTER the
        # parameter list; it is not part of the parameter type list.
        params = params[:params.index(")") + 1]
    else:
        qualified = text.strip()  # extern "C": demangler returns the bare name
        params = None
    if not qualified or "(anonymous namespace)" in qualified:
        return None
    if "{" in qualified or "}" in qualified:
        return None
    bare = qualified.rsplit("::", 1)[-1]
    if not _ABI_SYMBOL.match(bare):
        return None
    if "::" not in qualified and mangled != bare:
        # A C++ symbol that demangles without a scope without being a C name
        # (e.g. a local static) -- not something a header can declare.
        return None
    return bare, qualified, params


def _abi_compile_source_tree(gxx, inc, inc_internal, tmp, jobs):
    """Compile every runtime source once.

    Returns (strong, weak, failed):
      * strong = {mangled: source} for `T` entries -- an OUT-OF-LINE definition
        with external linkage. This is the only oracle for "the library really
        defines this symbol": `T` is what the static archive carries.
      * weak = {mangled: source} for `W` entries -- a COMDAT/weak definition,
        which for an `inline` function means the compiler materialised a local
        copy inside this TU. Such a symbol may have no out-of-line definition at
        all (it lives in the header), so it must NOT be treated as a
        header-vs-source pair: comparing it would compare the header against
        itself and always "match" (a tautology, not evidence).
      * failed = sources that did not compile.
    """
    src_dir = os.path.join(ROOT, "src", "runtime", "src")
    sources = sorted(fn for fn in os.listdir(src_dir) if fn.endswith(".cpp"))

    def one(fn):
        obj = os.path.join(tmp, "src_" + fn + ".o")
        cmd = [gxx, "-std=" + ABI_CXX_STD] + ABI_EXTRA_FLAGS + [
            "-c", "-I", inc, "-I", inc_internal, "-o", obj,
            os.path.join(src_dir, fn)]
        proc = _abi_run(cmd)
        return fn, obj, proc.returncode

    workers = max(1, min(jobs, len(sources)))
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        results = list(pool.map(one, sources))
    failed = [fn for fn, _o, rc in results if rc != 0]
    strong = {}
    weak = {}
    for fn, obj, rc in results:
        if rc != 0 or not os.path.exists(obj):
            continue
        nm = _abi_run([_TOOLS["nm"], "--defined-only", "--format=posix", obj])
        for line in nm.stdout.splitlines():
            parts = line.split()
            if len(parts) < 2:
                continue
            if parts[1] == "T":
                strong[parts[0]] = fn
            elif parts[1] == "W":
                weak[parts[0]] = fn
    return strong, weak, failed


def abi_check(headers, both, jobs=16):
    """Compiler-based declaration/definition ABI agreement, per header.

    Returns (stats, mismatches, unavailable_reason). `stats` holds the coverage
    counters printed on the single `ABI:` line; `mismatches` is a list of
    (header, symbol, why, definition_names, probe_evidence) tuples.

    ORACLE: the compiler, not a regex. A declaration is only "in namespace N"
    if `N::name` reaches it, so the probe emits one statement per symbol and
    compiles it:

        typedef decltype(&N::name) F; F f = &N::name;    // N = the namespace of
                                                         // the compiled DEFINITION

    * compiles  -> the object records the mangled name the DECLARATION
                   produced; it is compared VERBATIM with the definition's `T`
                   name from `nm`.
    * rejects   -> the declaration is not in N (or its signature differs).
                   That is the defect this dimension exists for, so it is a
                   MISMATCH, not a skip.
    * other err -> a probe limitation; SKIPPED with the compiler's own text.

    Overloaded names (`N::name` with 2+ signatures) cannot be address-taken
    without a target type, so they are checked for NAMESPACE MEMBERSHIP only
    (`using N::name;` must compile) and counted separately as
    `overload_pairs` -- an honest, printed coverage split.
    """
    stats = {"checked": 0, "match": 0, "mismatch": 0, "skipped": 0,
             "definitions": 0, "pairs": 0, "ambiguous": 0, "probe_fail": 0,
             "inline_only": 0, "notes": []}
    mismatches = []
    tools = _abi_find_toolchain()
    if tools is None:
        return stats, mismatches, "g++/nm/c++filt not found on PATH"
    _TOOLS.update(tools)
    inc = os.path.join(ROOT, "src", "runtime", "include")
    inc_internal = os.path.join(inc, "internal")
    probe_flags = [tools["g++"], "-std=" + ABI_CXX_STD] + ABI_EXTRA_FLAGS + [
        "-I", inc, "-I", inc_internal, "-c"]
    umbrella = "il2cpp_runtime.h"
    has_umbrella = os.path.exists(os.path.join(inc_internal, umbrella))
    tmp = tempfile.mkdtemp(prefix="decl_sym_abi_")
    try:
        strong, weak, failed = _abi_compile_source_tree(
            tools["g++"], inc, inc_internal, tmp, jobs)
        if failed:
            stats["notes"].append("source compile failed for %d file(s): %s"
                                  % (len(failed), ", ".join(failed[:5])))
        stats["definitions"] = len(strong)

        by_name = {}
        mangled = list(strong)
        demangled = _abi_demangle_all(tools["c++filt"], mangled)
        if len(demangled) != len(mangled):
            return stats, mismatches, "c++filt demangle count mismatch"
        for mang, dem in zip(mangled, demangled):
            split = _abi_split_qualified(dem, mang)
            if split is None:
                continue
            bare, qualified, params = split
            by_name.setdefault(bare, []).append((qualified, mang, params))

        # OUT-OF-LINE ONLY. `both` is a NAMESPACE-BLIND identifier set, so it
        # also holds names whose only definition is an `inline` body in a
        # header (`il2cpp_time.h:72/79`). Those have no `T` entry anywhere, so
        # they are absent from `by_name` and never probed -- correct by design:
        # every including TU type-checks an inline body, and comparing it with
        # itself would be a tautology. The count is printed, not hidden.
        weak_dem = _abi_demangle_all(tools["c++filt"], list(weak))
        weak_bare = set()
        for dem in weak_dem:
            split = _abi_split_qualified(dem, "")
            if split is not None:
                weak_bare.add(split[0])
        stats["inline_only"] = len(both & weak_bare - set(by_name))

        per_header = []
        for header in headers:
            path = os.path.join(inc, "internal", header)
            if not os.path.exists(path):
                path = os.path.join(inc, header)
            if not os.path.exists(path):
                continue
            syms = sorted(declared(read(path)) & both)
            trials = [s for s in syms if s in by_name]
            if trials:
                per_header.append((header, os.path.basename(path), trials))
        stats["pairs"] = sum(len(t) for _h, _b, t in per_header)

        def is_overload_set(sym):
            """ONE namespace, 2+ signatures -> `decltype(&N::name)` is ambiguous.

            A name defined in SEVERAL namespaces is NOT an overload set: each
            candidate still yields one unambiguous `N::name`, so it stays
            address-verifiable (and a namespace mismatch is caught).
            """
            quals = {q for q, _m, _p in by_name[sym]}
            return len(quals) == 1 and len(by_name[sym]) > 1

        def compile_probe(base, syms, variant, tag):
            """One TU for `syms`, each through namespace candidate `variant`.

            Layout is (includes..., one statement per symbol), and the line
            index map is built for THAT layout so `file:LINE:` errors are
            attributed to the declaration that caused them.
            """
            cpp = os.path.join(tmp, "p_%s_%s.cpp" % (base, tag))
            obj = os.path.join(tmp, "p_%s_%s.o" % (base, tag))
            out_lines, line_of = [], {}
            if has_umbrella and base != umbrella:
                out_lines.append('#include "%s"' % umbrella)
            out_lines.append('#include "%s"' % base)
            for pos, sym in enumerate(syms):
                variants = by_name[sym]
                if variant >= len(variants):
                    continue
                qualified = variants[variant][0]
                line_of[len(out_lines) + 1] = (sym, "overload")
                if is_overload_set(sym):
                    out_lines.append("namespace __u%d { using %s; }"
                                     % (pos, qualified))
                else:
                    # A statement, so it must live in a function body: one
                    # `__p<N>` per symbol keeps the file:LINE: attribution exact.
                    out_lines.append(
                        "static void __p%d(void) { typedef decltype(&%s) F;"
                        " F f = &%s; (void)f; }"
                        % (pos, qualified, qualified))
            with io.open(cpp, "w", encoding="utf-8") as fh:
                fh.write("\n".join(out_lines) + "\n")
            proc = _abi_run(probe_flags + ["-o", obj, cpp])
            if proc.returncode != 0 or not os.path.exists(obj):
                failures, seen = [], set()
                for line in proc.stderr.splitlines():
                    if "error:" not in line:
                        continue
                    m = re.search(r":(\d+):(?:\d+)?:?\s*(?:fatal\s+)?error:",
                                  line)
                    if not m:
                        continue
                    entry = line_of.get(int(m.group(1)))
                    if entry is None or entry[0] in seen:
                        continue
                    seen.add(entry[0])
                    failures.append((entry[0],
                                     line.split("error:", 1)[-1].strip()[:180]))
                return None, failures
            produced = _ABI_NAMES(_abi_run(
                [tools["nm"], "--undefined-only", "--format=posix", obj]).stdout)
            produced |= _ABI_NAMES(_abi_run(
                [tools["nm"], "--defined-only", "--format=posix", obj]).stdout)
            return produced, []

        _NOT_MEMBER = ("is not a member of", "no member named",
                       "has not been declared", "was not declared")

        def classify(err):
            text = (err or "").lower()
            return "namespace" if any(m in text for m in _NOT_MEMBER) else "other"

        for header, base, syms in per_header:
            pending = list(syms)
            n_variants = max(len(by_name[s]) for s in syms)
            for variant in range(n_variants):
                if not pending:
                    break
                attempt = [s for s in pending if len(by_name[s]) > variant]
                if not attempt:
                    continue
                produced, failures = compile_probe(
                    base, attempt, variant, "v%d" % variant)
                if produced is not None:
                    for sym in attempt:
                        variants = by_name[sym]
                        stats["checked"] += 1
                        if is_overload_set(sym):
                            stats["ambiguous"] += 1
                            stats["match"] += 1  # `using` compiled: it IS there
                        elif variants[variant][1] in produced:
                            stats["match"] += 1
                        elif any(m in produced for _q, m, _p in variants):
                            stats["match"] += 1  # right name, different overload
                        else:
                            stats["mismatch"] += 1
                            mismatches.append((
                                base, sym,
                                "header declaration is not the compiled "
                                "definition (mangles to %s)"
                                % (sorted(produced) or ["<nothing>"])[0],
                                [v[0] for v in variants],
                                sorted(produced)[:4]))
                    pending = [s for s in pending if s not in attempt]
                    continue
                # This candidate namespace rejects some declarations. A
                # "not a member" rejection is NOT conclusive yet (another
                # candidate may satisfy it), so keep it pending; any other
                # error is a probe limitation and is skipped with its text.
                for sym, err in failures:
                    if classify(err) == "other":
                        stats["checked"] += 1
                        stats["skipped"] += 1
                        stats["notes"].append(
                            "%s: %s could not be probed (%s)" % (base, sym, err))
                        pending.remove(sym)
                if not failures:
                    for sym in attempt:
                        stats["checked"] += 1
                        stats["skipped"] += 1
                        stats["notes"].append(
                            "%s: %s could not be probed (unattributed compile "
                            "failure)" % (base, sym))
                    pending = [s for s in pending if s not in attempt]
            # A symbol left pending here was never probed ALONE: the batched TU
            # also carried a symbol whose candidate namespace did not fit, and one
            # compile error fails the whole TU. `il2cpp_gc_collect` is defined BOTH
            # as `il2cpp::il2cpp_gc_collect` (gc.cpp) and with C linkage
            # `il2cpp_gc_collect` (api.cpp); variant 0 picked the C-linkage form,
            # the TU died on it, and `il2cpp_gc_alloc` -- which had only that one
            # variant -- was then reported as a mismatch although it is correct.
            #
            # So retry each still-pending symbol on its OWN, one TU per candidate
            # namespace: a collateral failure can no longer condemn it.
            for sym in pending:
                settled = False
                for variant in range(len(by_name[sym])):
                    produced, failures = compile_probe(
                        base, [sym], variant, "s%d_%d" % (len(sym), variant))
                    if produced is not None:
                        stats["checked"] += 1
                        if by_name[sym][variant][1] in produced:
                            stats["match"] += 1
                        elif any(m in produced for _q, m, _p in by_name[sym]):
                            stats["match"] += 1
                        else:
                            stats["mismatch"] += 1
                            mismatches.append((
                                base, sym,
                                "header declaration is not the compiled "
                                "definition (mangles to %s)"
                                % (sorted(produced) or ["<nothing>"])[0],
                                [v[0] for v in by_name[sym]],
                                sorted(produced)[:4]))
                        settled = True
                        break
                    if failures and classify(failures[0][1]) != "namespace":
                        stats["checked"] += 1
                        stats["skipped"] += 1
                        stats["notes"].append(
                            "%s: %s could not be probed (%s)"
                            % (base, sym, failures[0][1]))
                        settled = True
                        break
                if not settled:
                    stats["checked"] += 1
                    stats["mismatch"] += 1
                    mismatches.append((
                        base, sym,
                        "the declaration resolves in NONE of the %d namespace(s) "
                        "that define this name" % len(by_name[sym]),
                        [v[0] for v in by_name[sym]],
                        [v[1] for v in by_name[sym]]))
        return stats, mismatches, None
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


_TOOLS = {}


def main():
    emitted = emitted_by_transpiler()
    if not emitted:
        print("=" * 70)
        print("INCONCLUSIVE: emission surface is EMPTY -- %s does not exist."
              % os.path.join(ROOT, "src", "il2cppc", "Emit"))
        print("  ROOT must be a COMPLETE repo checkout: without the transpiler")
        print("  sources every real gap is misclassified as internal-only")
        print("  (false green). Point ROOT at the real repository.")
        return 2

    # --pair mode: the original per-pair view (kept for targeted diagnosis).
    if "--pair" in sys.argv:
        pairs = []
        for i, a in enumerate(sys.argv):
            if a == "--pair" and i + 1 < len(sys.argv):
                h, s = sys.argv[i + 1].split(":")
                pairs.append((h, s))
        real_gap = 0
        internal = 0
        for header, source in pairs:
            res = check_pair(header, source)
            if res is None:
                print("SKIP (missing file): %s / %s" % (header, source))
                continue
            missing, nd, nc = res
            print("=" * 70)
            print("%s vs %s: defined=%d declared=%d" % (header, source, nd, nc))
            if not missing:
                print("OK: defined - declared == {}")
                continue
            gaps = [s for s in missing if s in emitted]
            others = [s for s in missing if s not in emitted]
            real_gap += len(gaps)
            internal += len(others)
            if gaps:
                print("REAL GAP -- defined, not declared, AND emitted by the transpiler (%d):" % len(gaps))
                for s in gaps:
                    print("   ", s)
            if others:
                print("internal (defined, not declared, NOT emitted by the transpiler) (%d):" % len(others))
                for s in others:
                    print("   ", s)
        print("=" * 70)
        print("REAL GAPS:", real_gap, " internal-only:", internal)
        return 1 if real_gap else 0

    # GLOBAL model (default): every definition in the tree vs every declaration
    # in every header. This is the only model that covers the whole runtime --
    # the per-pair model covered 2 of 177 sources (1.1%).
    d, c = global_sets()
    missing = sorted(d - c)
    gaps = [s for s in missing if s in emitted]
    others = [s for s in missing if s not in emitted]
    print("=" * 70)
    print("GLOBAL: sources=%d headers=%d defined=%d declared=%d"
          % (len(all_sources()), len(all_headers()), len(d), len(c)))
    if gaps:
        print("REAL GAP -- defined, not declared in ANY header, AND emitted by the transpiler (%d):"
              % len(gaps))
        for s in gaps:
            print("   ", s)
    else:
        print("REAL GAP: none")
    if others:
        print("LATENT (defined, not declared, NOT emitted by the transpiler) (%d):" % len(others))
        for s in others:
            print("   ", s)
    print("=" * 70)
    # SPLIT COLUMNS (W11 P0-11-R3): REAL and LATENT are different verdicts and
    # must never be summed into one number. REAL = a symbol the transpiler can
    # emit but no header declares (a build break waiting to happen). LATENT = a
    # definition nothing declares and nothing emits (dead surface or a TU-local
    # helper that should be `static`). Reporting only a total let a LATENT
    # backlog hide behind "REAL GAPS: 0".
    print("REAL   (emitted, undeclared):", len(gaps))
    print("LATENT (defined, undeclared, not emitted):", len(others))
    # UNRESOLVED is ALWAYS printed, including when it is empty: a reader must be
    # able to SEE that the macro expander resolved every paste, rather than
    # inferring it from the absence of a warning.
    if UNRESOLVED:
        print("UNRESOLVED:", len(UNRESOLVED))
        for u in sorted(UNRESOLVED):
            print("   ", u)
        print("PROBE-BROKEN: %d macro paste(s) could not be resolved -- the"
              " symbol set is INCOMPLETE, so the counts above are NOT a verdict."
              % len(UNRESOLVED))
        return 2
    print("UNRESOLVED: none")

    # ABI DIMENSION (W11 P0-11-R3-D3). This is what stops the set test from
    # being a false green: a declaration moved into the wrong namespace keeps
    # `defined - declared == {}` while every call site fails to link. The
    # coverage counters are printed unconditionally so a hole in the check is
    # visible; `--no-abi` is the explicit opt-out and prints why.
    abi_mismatches = []
    if "--no-abi" in sys.argv:
        print("ABI: DISABLED by --no-abi (namespace/ABI agreement NOT verified)")
    else:
        headers_rel = [os.path.relpath(p, os.path.join(
            ROOT, "src", "runtime", "include")) for p in all_headers()]
        astats, abi_mismatches, unavailable = abi_check(headers_rel, d & c)
        print("ABI: checked=%d match=%d mismatch=%d skipped=%d "
              "(pairs=%d definitions=%d overload_pairs=%d probe_fail=%d)"
              % (astats["checked"], astats["match"], astats["mismatch"],
                 astats["skipped"], astats["pairs"], astats["definitions"],
                 astats["ambiguous"], astats["probe_fail"]))
        for note in astats["notes"]:
            print("   ABI-NOTE:", note)
        if unavailable:
            print("ABI-INCONCLUSIVE: %s" % unavailable)
        for base, sym, why, want, saw in abi_mismatches:
            print("   ABI MISMATCH: %s: %s -- %s" % (base, sym, why))
            print("       expected (definition):", ", ".join(want))
            print("       produced (by header): ", ", ".join(saw))
        if unavailable:
            # Same rule as an empty emission surface: a check that did not run
            # is INCONCLUSIVE, never a green.
            return 2
        # A check that verified nothing is also INCONCLUSIVE -- silence is the
        # failure mode this dimension exists to remove.
        if astats["checked"] == 0:
            print("ABI-INCONCLUSIVE: 0 (header, symbol) pairs were verified")
            return 2

    # BOTH columns are a failure now. REAL is a build break waiting to happen;
    # LATENT is a definition nothing declares and nothing emits -- either a dead
    # surface or a TU-local helper that should be `static`. W11 P0-11-R3 drove
    # both to 0 (13 declarations added, 12 helpers made static), so ANY new
    # LATENT is a regression of that state. Leaving LATENT non-fatal made the
    # mutation gate stay GREEN when a declaration was removed (measured: deleting
    # the dictgen box_double pair moved LATENT 0->2 but exit stayed 0), which is
    # exactly the "改坏仍绿" failure Art.16 forbids. ABI mismatches join them: a
    # declaration in the wrong namespace is a build break the set test cannot
    # express (measured: 4 `il2cpp_string_is_ascii_*` declarations sat in
    # `il2cpp::runtime` while the definitions were in `il2cpp::` and the set test
    # reported REAL=0 LATENT=0).
    return 1 if (gaps or others or abi_mismatches) else 0


if __name__ == "__main__":
    sys.exit(main())
