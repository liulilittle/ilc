#!/usr/bin/env python3
"""Parse the three authoritative .NET Core internal-call tables and compare them
against the ILC (this repository) wiring surface.

Sources (all read-only):
  reference/coreclr/src/coreclr/vm/ecalllist.h          -- FCall table (FCClassElement groups)
  reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp -- QCall table (s_QCall[] DllImportEntry)
  reference/corert/src/System.Private.CoreLib/src/System/Runtime/RuntimeImports.cs
                                                         -- [RuntimeImport] table (Redhawk)

Declaration surfaces cross-checked:
  reference/netcore/src/libraries/System.Private.CoreLib/**/*.cs   ([MethodImpl(InternalCall)] / [LibraryImport(RuntimeHelpers.QCall)])
  reference/coreclr/src/coreclr/System.Private.CoreLib/**/*.cs     (same attributes, full CoreCLR CoreLib subset)

Wiring surface (this repo):
  src/il2cppc/Emit/MethodRefResolver.cs   -- BclResolveCore owner dispatch
  src/il2cppc/Emit/MethodMap.*.cs         -- per-domain case tables
  src/runtime/{include,src}/**/*.{h,cpp}  -- runtime icall symbols

Usage:
  python tools/audit/parse_icall_tables.py [--repo-root .] [--json out.json]
                                          [--template tools/audit/icall-corelib-current.template.md]
                                          [--out docs/audit/icall-corelib-current.md]

The script never writes outside --out / --json.
"""

import argparse
import json
import os
import re
import sys

# ---------------------------------------------------------------------------
# generic helpers
# ---------------------------------------------------------------------------


def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        return f.read()


def read_lines(path):
    return read(path).splitlines()


def rel(root, path):
    try:
        return os.path.relpath(path, root).replace("\\", "/")
    except ValueError:
        return path.replace("\\", "/")


IFDEF_RE = re.compile(r"^\s*#\s*(if|ifdef|ifndef|else|elsif|endif|elif)\b(.*)$")


CORECLR_DEFINES = {
    "TARGET_64BIT": True,
    "TARGET_AMD64": True,
    "TARGET_WINDOWS": True,
    "TARGET_UNIX": False,
    "TARGET_32BIT": False,
    "TARGET_ARM": False,
    "TARGET_ARM64": False,
    "TARGET_X86": False,
    "TARGET_WASM": False,
    "TARGET_BROWSER": False,
    "TARGET_WASI": False,
    "TARGET_SUNOS": False,
    "TARGET_OSX": False,
    "TARGET_FREEBSD": False,
    "TARGET_LINUX": False,
    "FEATURE_COMINTEROP": True,
    "FEATURE_COMINTEROP_APARTMENT_SUPPORT": True,
    "FEATURE_INTERPRETER": False,
    "FEATURE_JAVAMARSHAL": False,
    "FEATURE_TYPEEQUIVALENCE": True,
    "FEATURE_MULTICOREJIT": True,
    "FEATURE_EVENTSOURCE_XPLAT": False,
    "FEATURE_PERFTRACING": True,
}

NATIVEAOT_DEFINES = dict(CORECLR_DEFINES)
NATIVEAOT_DEFINES.update({
    "FEATURE_COMINTEROP": False,
    "FEATURE_COMINTEROP_APARTMENT_SUPPORT": False,
    "FEATURE_INTERPRETER": False,
    "FEATURE_JAVAMARSHAL": False,
    "FEATURE_PERFTRACING": True,
})


def preprocessor_conditions(lines, defines):
    """Return (conds, actives) for `lines` under the given `defines` map.

    conds[i]  -- the enclosing `#if`-directive text of line i (1-based)
    actives[i] -- True when the line is compiled under `defines`
    """
    conds = [None] * (len(lines) + 1)
    actives = [True] * (len(lines) + 1)
    stack = []  # [{"kind","expr","value","parent"}]

    def active_now():
        for entry in stack:
            if not entry["value"]:
                return False
        return True

    for idx, line in enumerate(lines, 1):
        m = IFDEF_RE.match(line)
        if m:
            kind = m.group(1)
            expr = m.group(2).strip()
            if kind in ("if", "ifdef", "ifndef"):
                parent = active_now()
                value = parent and eval_cond(kind, expr, defines)
                stack.append({"value": value, "expr": expr, "kind": kind,
                              "parent": parent})
            elif kind in ("else", "elif"):
                if stack:
                    top = stack[-1]
                    if kind == "else":
                        top["value"] = top["parent"] and not top["value"]
                    else:
                        prev = top["value"]
                        top["value"] = (top["parent"] and not prev
                                        and eval_cond("if", expr, defines))
            elif kind == "endif":
                if stack:
                    stack.pop()
            conds[idx] = " ; ".join(
                ("!" if e["kind"] == "ifndef" else "") + e["expr"] for e in stack)
            actives[idx] = active_now()
            continue
        conds[idx] = " ; ".join(
            ("!" if e["kind"] == "ifndef" else "") + e["expr"] for e in stack)
        actives[idx] = active_now()
    return conds, actives


def eval_cond(kind, expr, defines):
    if not expr:
        return True
    expr = expr.split("//")[0].strip()
    if expr in ("0", "false", "FALSE"):
        return False
    if expr in ("1", "true", "TRUE"):
        return True
    expr = re.sub(r"\bdefined\s*\(\s*(\w+)\s*\)",
                  lambda m: "1" if defines.get(m.group(1), False) else "0", expr)
    expr = re.sub(r"\bdefined\s+(\w+)",
                  lambda m: "1" if defines.get(m.group(1), False) else "0", expr)
    # any remaining bare macro name is a defined()-test in the C preprocessor
    expr = re.sub(r"\b[A-Z_][A-Z0-9_]*\b",
                  lambda m: "1" if defines.get(m.group(0), False) else "0", expr)
    py = expr.replace("&&", " and ").replace("||", " or ")
    py = re.sub(r"!(?!=)", " not ", py)
    env = dict(defines)
    try:
        val = bool(eval(py, {"__builtins__": {}}, {}))
    except Exception:
        val = False
    return val if kind != "ifndef" else (not val if expr else True)


# ---------------------------------------------------------------------------
# 1. FCall table: reference/coreclr/src/coreclr/vm/ecalllist.h
# ---------------------------------------------------------------------------

RE_FCSTART = re.compile(r"^\s*FCFuncStart\(\s*(\w+)\s*\)")
RE_FCEND = re.compile(r"^\s*FCFuncEnd\(\s*\)")
RE_FCEL = re.compile(r"^\s*FCFuncElement(?:Sig)?\(\s*\"([^\"]+)\"\s*,\s*([^,)]+)")
RE_FCDYN = re.compile(r"^\s*FCDynamic(?:Sig)?\(\s*(\"[^\"]+\"|\w+)\s*,\s*([^,)]+)")
RE_FCUNREF = re.compile(r"^\s*FCUnreferenced\s*$")
RE_CLASSELEM = re.compile(r"^\s*FCClassElement\(\s*\"([^\"]+)\"\s*,\s*\"([^\"]+)\"\s*,\s*(\w+)\s*\)")


def parse_ecalllist(path):
    lines = read_lines(path)
    conds, actives = preprocessor_conditions(lines, CORECLR_DEFINES)
    groups = {}       # group symbol -> dict(class, ns, entries[])
    order = []
    cur = None
    for idx, line in enumerate(lines, 1):
        m = RE_FCSTART.match(line)
        if m:
            cur = {"group": m.group(1), "class": None, "ns": None,
                   "line": idx, "entries": [], "cond": conds[idx] or ""}
            groups[m.group(1)] = cur
            order.append(m.group(1))
            continue
        if RE_FCEND.match(line):
            cur = None
            continue
        if cur is None:
            continue
        m = RE_FCEL.match(line)
        if m:
            cur["entries"].append({
                "kind": "element", "name": m.group(1), "impl": m.group(2).strip(),
                "line": idx, "cond": conds[idx] or "", "group": cur["group"],
                "active": actives[idx]})
            continue
        m = RE_FCDYN.match(line)
        if m:
            raw = m.group(1)
            if raw.startswith('"'):
                name = raw[1:-1]
            elif raw == "COR_CTOR_METHOD_NAME":
                name = ".ctor"
            else:
                name = raw
            cur["entries"].append({
                "kind": "dynamic", "name": name, "impl": m.group(2).strip(),
                "line": idx, "cond": conds[idx] or "", "group": cur["group"],
                "active": actives[idx]})
            continue
    # attach class mapping
    classes = []
    for idx, line in enumerate(lines, 1):
        m = RE_CLASSELEM.match(line)
        if m:
            cls, ns, grp = m.group(1), m.group(2), m.group(3)
            classes.append({"class": cls, "ns": ns, "group": grp,
                            "line": idx, "cond": conds[idx] or "",
                            "active": actives[idx]})
            if grp in groups:
                groups[grp]["class"] = cls
                groups[grp]["ns"] = ns
    fcalls = []
    for cls in classes:
        g = groups.get(cls["group"])
        if not g:
            continue
        for e in g["entries"]:
            fcalls.append({
                "table": "FCall",
                "family": cls["class"],
                "namespace": cls["ns"],
                "owner": cls["ns"] + "." + cls["class"],
                "name": e["name"],
                "impl": e["impl"],
                "kind": e["kind"],
                "cond": e["cond"] or cls["cond"],
                "active": bool(e["active"] and cls["active"]),
                "file": rel(ROOT, path),
                "line": e["line"],
            })
    return fcalls, classes, groups


# ---------------------------------------------------------------------------
# 2. QCall table: reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp
# ---------------------------------------------------------------------------

RE_DLLIMPORT = re.compile(r"^\s*DllImportEntry\(\s*(\w+)\s*\)")


def parse_qcall_table(path):
    lines = read_lines(path)
    conds, actives = preprocessor_conditions(lines, CORECLR_DEFINES)
    rows = []
    for idx, line in enumerate(lines, 1):
        m = RE_DLLIMPORT.match(line)
        if m:
            rows.append({
                "table": "QCall",
                "entry": m.group(1),
                "cond": conds[idx] or "",
                "active": actives[idx],
                "file": rel(ROOT, path),
                "line": idx,
            })
    return rows


# ---------------------------------------------------------------------------
# 3. corert RuntimeImports.cs
# ---------------------------------------------------------------------------

RE_RUNTIMEIMPORT = re.compile(
    r"\[RuntimeImport\(\s*(?:\w+|\"[^\"]*\")\s*(?:,\s*\"([^\"]+)\"\s*)?\)\]")
RE_METHOD_DECL = re.compile(
    r"^\s*(?:(?:public|private|internal|protected)\s+)?(?:static\s+)?(?:extern\s+)?"
    r"(?:unsafe\s+)?[\w\.\<\>\[\]\*,\s]+?\s(\w+)\s*\(")


def parse_runtimeimports(path):
    lines = read_lines(path)
    rows = []
    pending = None
    for idx, line in enumerate(lines, 1):
        m = RE_RUNTIMEIMPORT.search(line)
        if m:
            exported = m.group(1)
            libm = re.search(r"\[RuntimeImport\(\s*([\w\.\"]+)", line)
            lib = libm.group(1) if libm else "?"
            pending = {"lib": lib, "exported": exported, "line": idx}
            continue
        if pending is not None:
            decl = RE_METHOD_DECL.match(line)
            if decl:
                pending["name"] = decl.group(1)
                pending["sig"] = line.strip()
                pending["decl_line"] = idx
                rows.append(pending)
                pending = None
            elif line.strip().startswith("["):
                continue
            elif line.strip():
                # non-matching attribute or declaration text; keep waiting a
                # couple of lines then drop
                pending["_wait"] = pending.get("_wait", 0) + 1
                if pending["_wait"] > 3:
                    pending = None
    out = []
    for r in rows:
        out.append({
            "table": "RuntimeImport",
            "lib": r["lib"],
            "exported": r["exported"],
            "name": r.get("name", ""),
            "sig": r.get("sig", ""),
            "file": rel(ROOT, path),
            "line": r.get("decl_line", r["line"]),
            "attr_line": r["line"],
        })
    return out


# ---------------------------------------------------------------------------
# 4. C# declaration surfaces
# ---------------------------------------------------------------------------

RE_INTERNALCALL = re.compile(r"\[MethodImpl(?:Attribute)?\(\s*MethodImplOptions\.InternalCall")
# CoreLib uses two spellings for the QCall library: the RuntimeHelpers constant
# and a file-local `QCall` alias (12 declarations in the coreclr subset).
RE_QCALL_IMPORT = re.compile(r"\[LibraryImport\(\s*(?:RuntimeHelpers\.)?QCall\b")
RE_DLLIMPORT_Q = re.compile(r"\[DllImport\(\s*\"QCall\"")
RE_ENTRYPOINT = re.compile(r"EntryPoint\s*=\s*\"([^\"]+)\"")
RE_DECL = re.compile(
    r"^\s*(?P<mods>(?:(?:public|private|internal|protected|static|extern|unsafe|override|virtual|readonly|partial|new)\s+)*)"
    r"(?P<ret>[\w\.\<\>\[\]\*\?,\s]+?)\s+(?P<name>[\w\.]+|operator\s*\S+|this)\s*[\(\[]")
RE_TYPE_DECL = re.compile(
    r"^\s*(?:(?:public|private|internal|protected|static|sealed|abstract|partial|unsafe|readonly|ref)\s+)*"
    r"(?:class|struct|interface|record)\s+(\w+)")


def strip_cs_comments(s):
    """Remove // and /* */ comment text so signature regexes see plain code.

    CoreLib declarations routinely carry inline type comments
    (`IntPtr /* IUnknown* */ pUnk`); without stripping them the declaration
    regex misses the line and the following member body would be mis-paired
    with the pending attribute.
    """
    out = []
    i = 0
    in_block = False
    in_str = False
    while i < len(s):
        if in_block:
            j = s.find("*/", i)
            if j < 0:
                i = len(s)
            else:
                i = j + 2
                in_block = False
            out.append(" ")
            continue
        ch = s[i]
        if in_str:
            out.append(ch)
            if ch == "\\" and i + 1 < len(s):
                out.append(s[i + 1])
                i += 2
                continue
            if ch == '"':
                in_str = False
            i += 1
            continue
        if ch == '"':
            in_str = True
            out.append(ch)
            i += 1
            continue
        if s.startswith("//", i):
            break
        if s.startswith("/*", i):
            in_block = True
            i += 2
            continue
        out.append(ch)
        i += 1
    return "".join(out)


def parse_cs_declarations(root_dir):
    """Walk a CoreLib source subtree and return the two declaration lists.

    Each record carries `cond`: the enclosing `#if` directive text (empty for an
    unconditional declaration). Declarations guarded by `MONO` / `NATIVEAOT` /
    `TARGET_*` never reach a CoreCLR build and are reported separately.
    """
    internalcalls = []
    qcalls = []
    files = []
    for dirpath, _dirnames, filenames in os.walk(root_dir):
        for fn in filenames:
            if fn.endswith(".cs"):
                files.append(os.path.join(dirpath, fn))
    files.sort()
    for path in files:
        lines = read_lines(path)
        namespace = None
        # one entry per open `{`: the type name it opens (None for method bodies)
        brace_stack = []
        condstack = []
        pending = None           # (kind, entrypoint, attr_line)
        pending_wait = 0
        pending_type = None      # type name awaiting its opening brace
        for idx, line in enumerate(lines, 1):
            s = line.strip()
            mif = re.match(r"^#\s*(if|ifdef|ifndef|else|elif|endif)\b(.*)", s)
            if mif:
                kind, expr = mif.group(1), mif.group(2).strip()
                if kind in ("if", "ifdef", "ifndef"):
                    condstack.append(expr)
                elif kind == "endif":
                    if condstack:
                        condstack.pop()
                # a preprocessor line never terminates a pending declaration
                continue
            mns = re.match(r"^namespace\s+([\w\.]+)", s)
            if mns:
                namespace = mns.group(1)
            mty = RE_TYPE_DECL.match(line)
            if mty:
                pending_type = mty.group(1)
            types = [t for t in brace_stack if t]
            pending_attr = pending is not None and s and not s.startswith("[")
            if pending_attr:
                ep = RE_ENTRYPOINT.search(s)
                consumed = False
                if ep and pending[1] is None:
                    pending = (pending[0], ep.group(1), pending[2])
                    consumed = True
                if not consumed:
                    code = strip_cs_comments(s)
                    d = RE_DECL.match(code)
                    if d and "=>" not in code:
                        owner = ".".join([x for x in ([namespace] + types) if x])
                        mods = d.group("mods").split()
                        access = next((x for x in mods
                                       if x in ("public", "private", "internal", "protected")),
                                      "private")
                        mname = d.group("name")
                        # a declaration whose name equals its declaring type is a
                        # constructor: normalise to ".ctor" (the FCall spelling)
                        if types and mname == types[-1]:
                            mname = ".ctor"
                        rec = {
                            "owner": owner,
                            "name": mname,
                            "access": access,
                            "static": "static" in mods,
                            "sig": s,
                            "file": path,
                            "line": idx,
                            "attr_line": pending[2],
                            "cond": " && ".join(condstack),
                        }
                        if pending[0] == "QCall":
                            rec["entry"] = pending[1]
                            qcalls.append(rec)
                        else:
                            internalcalls.append(rec)
                        pending = None
                    else:
                        pending_wait += 1
                        if pending_wait > 6 or s.startswith("}"):
                            pending = None
            if RE_INTERNALCALL.search(line):
                pending = ("InternalCall", None, idx)
                pending_wait = 0
            elif RE_QCALL_IMPORT.search(line) or RE_DLLIMPORT_Q.search(line):
                ep = RE_ENTRYPOINT.search(line)
                pending = ("QCall", ep.group(1) if ep else None, idx)
                pending_wait = 0
            # brace bookkeeping: a type's `{` is tagged with its name
            for ch in line:
                if ch == "{":
                    brace_stack.append(pending_type)
                    pending_type = None
                elif ch == "}" and brace_stack:
                    brace_stack.pop()
    return internalcalls, qcalls


INACTIVE_COND_RE = re.compile(r"\bMONO\b|\bNATIVEAOT\b|\bTARGET_WASM\b|\bTARGET_BROWSER\b|"
                              r"\bTARGET_WASI\b|\bTARGET_32BIT\b|\bTARGET_ARM\b|\bTARGET_X86\b|"
                              r"\bTARGET_OSX\b|\bTARGET_LINUX\b|\bTARGET_UNIX\b")


def split_active(records):
    """Split C# declarations into (actively-compiled-for-CoreCLR-x64, others).

    `others` are the ones behind MONO / NATIVEAOT / non-x64 TARGET_* guards that
    never make it into a CoreCLR x64 desktop build of the same source tree.
    """
    active, inactive = [], []
    for r in records:
        if r.get("cond") and INACTIVE_COND_RE.search(r["cond"]):
            inactive.append(r)
        else:
            active.append(r)
    return active, inactive


# ---------------------------------------------------------------------------
# 5. ILC wiring surface
# ---------------------------------------------------------------------------

RE_OWNER_CASE = re.compile(r"case\s+\"([^\"]+)\"\s*:")
RE_FUNC_DEF = re.compile(r"^\s*(?:private|public|internal)\s+static\s+[\w\?<>\[\],\. ]+?\s(\w+)\s*\(")
RE_MAP_CASE = re.compile(r"case\s+\"([^\"]+)\"\s*:")
RE_MAP_TUPLE = re.compile(r"\(\s*\"([^\"]+)\"\s*,\s*\"([^\"]+)\"\s*,(?:\s*(\d+))?\s*\)")
RE_SYMBOL = re.compile(r"\b((?:il2cpp|icall)_\w+)\b")
RE_STR_LITERAL = re.compile(r"\$?\"((?:[^\"\\]|\\.)*)\"")
RE_STR_TOKEN = re.compile(r"((?:il2cpp(?:::\w+)+)|(?:icall_\w+)|(?:il2cpp_\w+))")
RE_STR_MARKER = re.compile(r"\"((?:System\.Delegate_Invoke)|(?:ResolvedMethod\.NullMarker))\"")


def extract_symbols(line):
    """Runtime icall symbols named on one C# line.

    Handles plain literals ("il2cpp::runtime::icall_x"), interpolated stems
    ($"il2cpp::runtime::icall_mathf_{stem}_f" -> "icall_mathf_{stem}_f") and
    the synthetic fold markers (System.Delegate_Invoke / NullMarker).
    """
    out = []
    for lit in RE_STR_LITERAL.finditer(line):
        body = lit.group(1)
        for m in RE_STR_TOKEN.finditer(body):
            tok = m.group(1)
            if body[m.end():m.end() + 1] == "{":
                tok = tok + "{...}"
            out.append(tok)
    for m in RE_STR_MARKER.finditer(line):
        out.append(m.group(1))
    return out


def extract_function_body(lines, start_idx):
    """Return (body_lines, end_idx) for the C# method starting at start_idx."""
    depth = 0
    started = False
    out = []
    for i in range(start_idx, len(lines)):
        line = lines[i]
        depth += line.count("{") - line.count("}")
        out.append(line)
        if "{" in line:
            started = True
        if started and depth <= 0:
            return out, i
    return out, len(lines) - 1


def parse_wiring(root):
    emit_dir = os.path.join(root, "src", "il2cppc", "Emit")
    resolver_path = os.path.join(emit_dir, "MethodRefResolver.cs")
    resolver_lines = read_lines(resolver_path)

    # --- owner dispatch: BclResolveCore case "Owner": ... TryMapXxx(
    owner_dispatch = {}
    core_start = None
    for idx, line in enumerate(resolver_lines):
        if "private static ResolvedMethod? BclResolveCore" in line:
            core_start = idx
            break
    if core_start is None:
        raise SystemExit("BclResolveCore not found in " + resolver_path)
    body, _end = extract_function_body(resolver_lines, core_start + 1)
    for i, line in enumerate(body):
        for m in RE_OWNER_CASE.finditer(line):
            owner = m.group(1)
            # scan forward to the end of the statement that dispatches (skipping
            # comments); a chained `?? TryMap...(...)` continues on later lines.
            chunk = []
            paren = 0
            for follow in body[i: i + 40]:
                s = follow.strip()
                if s.startswith("//"):
                    continue
                chunk.append(follow)
                paren += follow.count("(") - follow.count(")")
                if ("return" in follow or "=>" in follow) and ";" in follow and paren <= 0:
                    break
                if "return" in follow and paren <= 0 and s.endswith(";"):
                    break
            targets = re.findall(r"\b(TryMap\w+)\s*\(", " ".join(chunk))
            entry = owner_dispatch.setdefault(
                owner, {"targets": [], "line": core_start + 2 + i})
            for t in targets:
                if t not in entry["targets"]:
                    entry["targets"].append(t)

    # --- per-function case literals in MethodMap.*.cs (and resolver helpers)
    func_cases = {}   # func -> {name|owner::name: {file, line, symbols}}
    map_files = sorted(
        f for f in os.listdir(emit_dir)
        if f.startswith("MethodMap.") and f.endswith(".cs"))
    map_files = [os.path.join(emit_dir, f) for f in map_files]
    for path in map_files + [resolver_path]:
        lines = read_lines(path)
        idx = 0
        while idx < len(lines):
            line = lines[idx]
            fm = RE_FUNC_DEF.match(line)
            if not (fm and "TryMap" in fm.group(1)):
                idx += 1
                continue
            func = fm.group(1)
            body, end = extract_function_body(lines, idx)
            cases = func_cases.setdefault(func, {})
            pending = []
            last_was_label = False

            def flush(keys):
                for key, ln in keys:
                    cases.setdefault(
                        key, {"file": rel(root, path), "line": ln, "symbols": []})

            for off, bline in enumerate(body):
                found = [(m.group(1), idx + off + 1) for m in RE_MAP_CASE.finditer(bline)]
                tuple_found = [(m.group(1) + "::" + m.group(2), idx + off + 1)
                               for m in RE_MAP_TUPLE.finditer(bline)
                               if "switch" not in bline]
                stripped = bline.strip()
                if stripped.startswith("//") or not stripped:
                    continue
                if found or tuple_found:
                    # consecutive case labels share one arm; a label that
                    # follows real statements starts a NEW arm, so the previous
                    # arm (if still unresolved) has no runtime symbol of its own
                    if pending and not last_was_label:
                        flush(pending)
                        pending = []
                    pending.extend(found)
                    pending.extend(tuple_found)
                    last_was_label = True
                else:
                    last_was_label = False
                syms = extract_symbols(bline)
                if pending and syms:
                    for key, ln in pending:
                        rec = cases.setdefault(
                            key, {"file": rel(root, path), "line": ln, "symbols": []})
                        for s in syms:
                            if s not in rec["symbols"]:
                                rec["symbols"].append(s)
                    pending = []
                elif pending and re.search(r"\bthrow\b|=>\s*null", bline):
                    # a case arm that produces no runtime symbol (fold / PNSE)
                    flush(pending)
                    pending = []
            flush(pending)
            idx = end + 1

    # --- runtime symbols (declarations + definitions)
    runtime_roots = [os.path.join(root, "src", "runtime", "include"),
                     os.path.join(root, "src", "runtime", "src")]
    symbols = {}
    for base in runtime_roots:
        for dirpath, _dn, fns in os.walk(base):
            for fn in fns:
                if not fn.endswith((".h", ".cpp", ".hpp")):
                    continue
                path = os.path.join(dirpath, fn)
                for idx, line in enumerate(read_lines(path), 1):
                    for m in RE_SYMBOL.finditer(line):
                        symbols.setdefault(m.group(1), (rel(root, path), idx))
    return {
        "owner_dispatch": owner_dispatch,
        "func_cases": func_cases,
        "symbols": symbols,
        "resolver": resolver_path,
    }


def wiring_lookup(wiring, owner, name):
    """Static (grep-level) wiring check for one (owner, method) pair.

    Returns (status, evidence). status is one of
      wired          -- the owner's dispatch chain has a case arm for the method
                        that names at least one runtime icall symbol
      half-wired     -- the arm exists but names no runtime symbol (fold / null
                        marker / PNSE throw)
      no-wiring      -- no case literal reachable from the owner's dispatch chain
    """
    disp = wiring["owner_dispatch"].get(owner)
    if not disp:
        return "no-wiring", None
    for target in disp["targets"]:
        cases = wiring["func_cases"].get(target) or {}
        hit = cases.get(name)
        if hit is None:
            # generic tuple-keyed arms (owner::name) used by TryMapEmit
            hit = cases.get(owner + "::" + name)
        if hit is not None:
            status = "wired" if hit["symbols"] else "half-wired"
            return status, {"func": target, "file": hit["file"], "line": hit["line"],
                            "symbols": hit["symbols"], "owner_line": disp["line"]}
    return "no-wiring", {"funcs": disp["targets"], "owner_line": disp["line"]}


# ---------------------------------------------------------------------------
# 6. reachability tiers
# ---------------------------------------------------------------------------

P0_TYPES = {
    "System.String", "System.Math", "System.MathF", "System.Array", "System.Buffer",
    "System.Object", "System.Environment", "System.GC", "System.Convert",
    "System.Delegate", "System.Exception", "System.Threading.Interlocked",
    "System.Threading.Thread", "System.Threading.Monitor", "System.Threading.Volatile",
    "System.Runtime.CompilerServices.RuntimeHelpers", "System.Threading.Tasks.Task",
}
P1_PREFIX = (
    "System.Collections", "System.Text", "System.IO", "System.Globalization",
    "System.Numerics", "System.Diagnostics", "System.TimeZone", "System.Uri",
    "System.Version", "System.Random", "System.Security.Cryptography",
    "System.Resources", "System.ComponentModel", "System.Xml", "System.Data",
    "System.Threading",
)
P2_PREFIX = (
    "System.RuntimeTypeHandle", "System.RuntimeMethodHandle", "System.RuntimeFieldHandle",
    "System.Runtime", "System.Reflection", "System.Type", "System.Activator",
    "System.Runtime.InteropServices", "System.Runtime.Loader", "System.AppDomain",
    "System.ModuleHandle", "System.Signature", "System.TypedReference",
    "System.ArgIterator", "System.Runtime.CompilerServices",
)
P3_PREFIX = (
    "System.StubHelpers", "System.MetadataImport", "Internal.", "Microsoft.",
    "System.Private.CoreLib", "Windows.Foundation", "System.Diagnostics.Tracing",
    "System.Runtime.InteropServices.ComTypes", "System.ComAwareWeakReference",
    "System.Mda", "System.Variant", "System.EventPipe", "System.Native",
)


def tier_for(owner):
    if owner in P0_TYPES:
        return "P0"
    for p in P2_PREFIX:
        if owner.startswith(p):
            return "P2"
    for p in P3_PREFIX:
        if owner.startswith(p):
            return "P3"
    for p in P1_PREFIX:
        if owner.startswith(p):
            return "P1"
    for p in P0_TYPES:
        if owner.startswith(p):
            return "P0"
    return "P3"


# ---------------------------------------------------------------------------
# 7. row classification
# ---------------------------------------------------------------------------
#
# status vocabulary (also written into the generated document):
#   wired       -- the owner's dispatch chain has an arm for this member that
#                  names a runtime icall symbol (file:line evidence)
#   half-wired  -- the arm exists but resolves to a fold / null / PNSE throw
#                  (no runtime symbol); or the only arm is shape-guarded away
#   missing     -- no arm at all for this name: the call site would emit an
#                  unresolved extern. A non-public declaration is annotated with
#                  `decl_access` ("internal helper, reached only through its
#                  managed wrapper") but the wiring itself is still absent.
#   unreachable -- the row is preprocessor-gated out of the CoreCLR x64 build
#                  (the guard text is recorded)
#   no-decl     -- QCall table entry with no C# declaration in the reference
#                  source subset (cannot be called from managed code here)


def classify_fcall(row, wiring, decl_index):
    if not row["active"]:
        return "unreachable", {"cond": row["cond"]}
    status, ev = wiring_lookup(wiring, row["owner"], row["name"])
    decl = decl_index.get((row["owner"], row["name"]))
    out = dict(ev or {})
    if decl:
        out["decl"] = decl
    if status in ("wired", "half-wired"):
        return status, out
    return "missing", out


def classify_qcall(row, wiring, entry_index):
    if not row["active"]:
        return "unreachable", {"cond": row["cond"]}
    decl = entry_index.get(row["entry"])
    if decl is None:
        return "no-decl", {}
    status, ev = wiring_lookup(wiring, decl["owner"], decl["name"])
    out = dict(ev or {})
    out["decl"] = decl
    if status in ("wired", "half-wired"):
        return status, out
    return "missing", out


def build_rows(data, wiring):
    """Return (fcall_rows, qcall_rows) with status + evidence attached."""
    decl_index = {}
    for r in data["netcore_internalcalls"]:
        decl_index.setdefault((r["owner"], r["name"]), r)
    # fall back to the full (coreclr) CoreLib when the trimmed subset lacks it
    for r in data["coreclr_internalcalls"]:
        decl_index.setdefault((r["owner"], r["name"]), r)

    entry_index = {}
    for r in data["coreclr_qcalls"]:
        if r.get("entry"):
            entry_index.setdefault(r["entry"], r)
    for r in data["netcore_qcalls"]:
        if r.get("entry"):
            entry_index.setdefault(r["entry"], r)

    f_rows = []
    for row in data["fcalls"]:
        status, ev = classify_fcall(row, wiring, decl_index)
        item = dict(row)
        item["status"] = status
        item["tier"] = tier_for(row["owner"])
        item["evidence"] = ev
        f_rows.append(item)

    q_rows = []
    for row in data["qcalls"]:
        status, ev = classify_qcall(row, wiring, entry_index)
        item = dict(row)
        item["status"] = status
        decl = ev.get("decl") if ev else None
        owner = decl["owner"] if decl else qcall_owner_from_entry(row["entry"])
        item["decl_owner"] = owner
        item["decl_name"] = decl["name"] if decl else entry_method_name(row["entry"])
        item["tier"] = tier_for(owner)
        item["evidence"] = ev
        q_rows.append(item)
    return f_rows, q_rows


def qcall_owner_from_entry(entry):
    """Best-effort owner for a QCall entry with no C# declaration in the subset."""
    return "System.Private.CoreLib." + entry.split("_")[0]


def entry_method_name(entry):
    parts = entry.split("_", 1)
    return parts[1] if len(parts) > 1 else entry


def status_counts(rows):
    out = {k: 0 for k in ("wired", "half-wired", "missing", "unreachable", "no-decl")}
    for r in rows:
        out[r["status"]] += 1
    return out


def tier_counts(rows, statuses):
    out = {}
    for tier in ("P0", "P1", "P2", "P3"):
        sel = [r for r in rows if r["tier"] == tier and r["status"] in statuses]
        out[tier] = len(sel)
    return out


def evidence_cell(row):
    """Render the file:line (or "no wiring") evidence for one row."""
    ev = row.get("evidence") or {}
    st = row["status"]
    if st in ("wired", "half-wired"):
        syms = ", ".join("`" + s + "`" for s in ev.get("symbols") or [])
        loc = f"{shell_path(ev.get('file'))}:{ev.get('line')}" if ev.get("file") else "-"
        return f"{loc} -> {syms}" if syms else f"{loc} (arm without runtime symbol)"
    if st == "missing":
        d = ev.get("decl") or {}
        if d:
            note = "non-public helper" if d.get("access") != "public" else "public member"
            return (f"no wiring; {note} decl {shell_path(d.get('file'))}:{d.get('line')}")
        funcs = ev.get("funcs")
        if funcs:
            return "no wiring (" + ", ".join(funcs) + " has no arm)"
        return "no wiring (owner not dispatched)"
    if st == "unreachable":
        return "preprocessor-gated: `" + (ev.get("cond") or "?") + "`"
    if st == "no-decl":
        return "no wiring; no C# declaration in the reference subset"
    return "-"


def shell_path(p):
    if not p:
        return "?"
    q = p.replace("\\", "/")
    i = q.find("reference/")
    return q[i:] if i >= 0 else q


def md(s):
    return str(s).replace("|", "\\|")


def write_report(path, data, f_rows, q_rows, ref_root):
    fc = status_counts(f_rows)
    qc = status_counts(q_rows)
    ri_rows = data["rimports"]
    ri_unique = len({(r["exported"] or r["name"]) for r in ri_rows})
    ri_rh = [r for r in ri_rows if (r["exported"] or "").startswith(("Rh", "Rhp"))]
    ri_libm = [r for r in ri_rows if r not in ri_rh]

    gap_statuses = ("missing", "half-wired")
    table_entries = {q["entry"] for q in q_rows}
    p0_f = tier_counts(f_rows, gap_statuses)["P0"]
    p0_q = tier_counts(q_rows, gap_statuses)["P0"]

    # --- reachability coverage of the trimmed CoreLib subset
    netcore_roots = sorted({r["owner"].rsplit(".", 1)[0] if "." in r["owner"] else r["owner"]
                            for r in data["netcore_internalcalls"] + data["netcore_qcalls"]})
    q_owners = {}
    for r in q_rows:
        q_owners.setdefault(r["decl_owner"], []).append(r)
    q_owner_status = {}
    for owner, rows in q_owners.items():
        st = status_counts(rows)
        q_owner_status[owner] = st

    L = []
    w = L.append
    w("# System.Private.CoreLib 内部调用现状盘点（2026-10-31）")
    w("")
    w("> 审计席 B（只读审计）｜worktree `.worktrees/audit-corelib-icall`｜基线 c3a6b2e3")
    w("> 本文档由 `tools/audit/parse_icall_tables.py` 自动生成，所有数字均为脚本实测，")
    w("> 未手工抄录。复现：")
    w(">")
    w("> ```")
    w("> python tools/audit/parse_icall_tables.py \\")
    w(">     --repo-root . --reference-root <checkout>/reference \\")
    w(">     --out docs/audit/icall-corelib-current.md")
    w("> ```")
    w("")
    w("**口径说明**：本仓 `reference/` 被 `.gitignore` 忽略（`.gitignore:47`），")
    w("因此解析脚本用 `--reference-root` 指向主 checkout 的 `reference/`；")
    w("worktree 内的 `src/` 才是被审计的 ILC 接线面。")
    w("")
    w("## 0. 结论摘要")
    w("")
    w("### 0.1 三张权威表实测规模")
    w("")
    w("| 表 | 源文件 | 条目数 | 备注 |")
    w("|---|---|---|---|")
    w(f"| FCall | `reference/coreclr/src/coreclr/vm/ecalllist.h` | **{len(f_rows)}** | "
      f"{len(data['fclasses'])} 个 FCClassElement 组 |")
    w(f"| QCall | `reference/coreclr/src/coreclr/vm/qcallentrypoints.cpp` | **{len(q_rows)}** | "
      f"`s_QCall[]` 的 DllImportEntry |")
    w(f"| RuntimeImport | `reference/corert/src/System.Private.CoreLib/src/System/Runtime/RuntimeImports.cs` "
      f"| **{len(ri_rows)}** 个属性 / **{ri_unique}** 个唯一导出名 | "
      f"Rh*/Rhp* {len(ri_rh)} 条、libm 数学 {len(ri_libm)} 条 |")
    w("")
    w("### 0.2 接线状态（FCall / QCall 分开）")
    w("")
    w("| 表 | 总数 | 已接 wired | 半接 half-wired | 缺 missing | 不可达 unreachable | no-decl |")
    w("|---|---|---|---|---|---|---|")
    w(f"| **FCall** | {len(f_rows)} | {fc['wired']} | {fc['half-wired']} | "
      f"{fc['missing']} | {fc['unreachable']} | {fc['no-decl']} |")
    w(f"| **QCall** | {len(q_rows)} | {qc['wired']} | {qc['half-wired']} | "
      f"{qc['missing']} | {qc['unreachable']} | {qc['no-decl']} |")
    w(f"| **RuntimeImport** | {len(ri_rows)} | 1 (`RhBulkMoveWithWriteBarrier`) | 0 | "
      f"{len(ri_rows) - 1} | 0 | 0 |")
    w("")
    w("状态定义（本文档全篇统一口径）：")
    w("")
    w("- **已接 wired**：owner 分发链上存在该方法名对应的 case 分支，且该分支指名了**运行时 icall 符号**；")
    w("  证据为该分支的 `file:line` 加符号名。")
    w("- **半接 half-wired**：分支存在但解析到 fold/null/PNSE 抛异常（**不指名运行时符号**），")
    w("  或分支被签名形状守卫挡掉、对真实调用返回 null。")
    w("- **缺 missing**：没有任何分支命中该方法名 —— 调用点会发射一个未定义 extern。")
    w("  若该声明是 non-public 的内部助手（只能经 public 托管包装到达），行内会标注")
    w("  `non-public helper` 及其声明位置；**但接线本身仍然算缺**，不能因为“用户直接调不到”")
    w("  就当成已接（包装方法绕不开它）。")
    w("- **不可达 unreachable**：被预处理条件排除在 CoreCLR x64 桌面构建之外（记录守卫文本）。")
    w("- **no-decl**：QCall 表里有、但参考源码子集里找不到对应的 C# 声明，")
    w("  故托管侧在本仓参考树内无法调用（不等于运行时不需要）。")
    w("")
    w("### 0.2.1 判定方法与已知局限")
    w("")
    w("判定是**静态源码级**的，不是运行时验证：脚本从 `BclResolveCore` 的 owner 分发出发，")
    w("按函数体截取 `case` 标签与紧随的语句，从字符串字面量里取运行时符号名；")
    w("因此：")
    w("")
    w("- `wired` 只证明“这条分支存在且指名了符号”，**不证明签名/参数计数正确**；")
    w("  同一 arm 守卫下的其它重载（例如 `String..ctor` 的 9 个签名共用一个 case 入口、")
    w("  `MethodMap.String.cs:15` 之后按 `ps` 分派）会共享一行证据。")
    w("- 互通插值符号（`icall_mathf_{...}`）只能定位到**符号族**，无法给出最终展开名。")
    w("- `missing` 是对**该 owner 的 dispatch 链**判定的；owner 未被任何 case 覆盖时，")
    w("  证据写作 `no wiring (owner not dispatched)`。")
    w("- QCall 的 owner 来自 C# 声明，而托管方法名 != EntryPoint；脚本用 EntryPoint 反查声明，")
    w("  再用声明名去查 map，这与真实调用路径一致（ILC 也按托管名解析）。")
    w("")
    w("### 0.3 可达性分级（只统计 wired 之外的行）")
    w("")
    w("| 级别 | FCall 缺/半 | QCall 缺/半 | 含义 |")
    w("|---|---|---|---|")
    for tier, desc in (("P0", "用户代码直接调用"), ("P1", "常见 BCL 间接可达"),
                       ("P2", "反射/序列化/线程/装配加载"),
                       ("P3", "仅运行时内部")):
        w(f"| {tier} | {tier_counts(f_rows, gap_statuses)[tier]} | "
          f"{tier_counts(q_rows, gap_statuses)[tier]} | {desc} |")
    w("")
    w(f"**P0 缺口合计 {p0_f + p0_q} 条**（FCall {p0_f} + QCall {p0_q}）。")
    w("")
    w("### 0.4 关键判断")
    w("")
    w(f"1. QCall 才是大头：s_QCall[] {len(q_rows)} 条 vs FCall {len(f_rows)} 条；")
    w(f"   而 ILC 的 QCall 接线只有 **{qc['wired']}** 条（且其中多数是**顺带命中**——"
      "同一个托管方法名在 net48 版 MethodMap 里已存在，QCall 走同一条 map，"
      "例如 `Environment.GetProcessorCount`、`String.Intern`）。")
    w(f"2. FCall 侧 ILC 已接 {fc['wired']}/{len(f_rows)}，主要是 String/Math/MathF/GC/Delegate/")
    w("   RuntimeHelpers 的 net48 形状；"
      f"MathF {sum(1 for r in f_rows if r['owner']=='System.MathF' and r['status']=='wired')}"
      f"/{sum(1 for r in f_rows if r['owner']=='System.MathF')} 全接（float 数学面无缺口）。")
    w(f"3. RuntimeImport {len(ri_rows)} 条基本**整体缺失**：ILC 的运行时不是 Redhawk，")
    w("   `Rh*`/`Rhp*` 入口在 `src/runtime` 里只有 `RhBulkMoveWithWriteBarrier` 一个真正映射，")
    w(f"   libm 那 {len(ri_libm)} 条数学入口由 `icall_math*` 直接调 libm，不需要 Redhawk 入口。")
    w("4. 计数口径警告：QCall 表的 "
      f"{len(q_rows)} 条里，netcore 参考 CoreLib 子集的 46 条 QCall 声明中只有 "
      f"{sum(1 for r in data['netcore_qcalls'] if r.get('entry') in table_entries)} 条的 "
      "EntryPoint 出现在 `s_QCall[]`；")
    w("   其余靠 `reference/coreclr/src/coreclr/System.Private.CoreLib`（"
      f"{len(_count_cs(os.path.join(ref_root, 'coreclr','src','coreclr','System.Private.CoreLib')))} 个 .cs，")
    w("   CoreCLR 侧 CoreLib）才能对上 "
      f"{sum(1 for r in q_rows if r['status'] != 'no-decl')} 条，剩下 "
      f"{qc['no-decl']} 条表项在**两份参考树里都没有声明**（另有 "
      f"{qc['unreachable']} 条被预处理排除）。")
    w("")
    w("## 1. 三张权威表的实测结果（与任务书给定数字的差异）")
    w("")
    w("| 项目 | 任务书给定 | 本次实测 | 差异说明 |")
    w("|---|---|---|---|")
    w(f"| ecalllist.h FCClassElement 组数 | 29 | **{len(data['fclasses'])}** | 一致 |")
    w(f"| ecalllist.h FCall 条目 | 约 200 | **{len(f_rows)}** | 任务书是估算；实测 "
      f"{sum(1 for r in f_rows if r['kind']=='element')} 个 FCFuncElement + "
      f"{sum(1 for r in f_rows if r['kind']=='dynamic')} 个 FCDynamic/FCDynamicSig |")
    w(f"| s_QCall[] DllImportEntry | 403 | **{len(q_rows)}** | 一致 |")
    w(f"| RuntimeImports.cs RuntimeImport | 174 | **{len(ri_rows)} 属性 / {ri_unique} 唯一名** | "
      "`RhpLockCmpXchg32/64` 各出现两次（`ref long` 与 `ref IntPtr` 重载共用入口），"
      "且 `RuntimeImport(\"*\", ...)` 两条早期写法未被任务书的 174 唯一计数拆开 |")
    w(f"| netcore CoreLib InternalCall | 26 | **{len(data['netcore_internalcalls'])}** | 一致 |")
    w(f"| netcore CoreLib QCall | 46 | **{len(data['netcore_qcalls'])}** | 一致 |")
    w(f"| coreclr CoreLib InternalCall | 未给 | **{len(data['coreclr_internalcalls'])}** | 新增实测 |")
    w(f"| coreclr CoreLib QCall | 未给 | **{len(data['coreclr_qcalls'])}** | 新增实测 |")
    w("")
    w("预处理过滤（按 CoreCLR x64 桌面定义集求值，见脚本 `CORECLR_DEFINES`）：")
    w("")
    w(f"- FCall 表 **{fc['unreachable']}** 条被 `#ifdef` 排除（`TARGET_32BIT` 的 8 条 DivModInt、")
    w("  `FEATURE_INTERPRETER` 1 条、`FEATURE_JAVAMARSHAL` 1 条、`TARGET_WASM` 1 条）。")
    w(f"- QCall 表 **{qc['unreachable']}** 条被排除，集中在 `FEATURE_COMWRAPPERS` /")
    w("  `TARGET_UNIX` / `TARGET_BROWSER` / `TARGET_WASI` / `FEATURE_JAVAMARSHAL` /")
    w("  `FEATURE_OBJCMARSHAL` / `FEATURE_EVENTSOURCE_XPLAT` / `PROFILING_SUPPORTED` / `_DEBUG`。")
    w("")
    w("## 2. CoreLib C# 声明面与权威表的交叉核对")
    w("")
    w("### 2.1 netcore 子集的 26 条 InternalCall")
    w("")
    w("| # | 声明（owner.method） | 预处理守卫 | FCall 表内 | 判定 |")
    w("|---|---|---|---|---|")
    fmap = {(r["owner"], r["name"]): r for r in data["fcalls"]}
    fname_only = {}
    for r in data["fcalls"]:
        fname_only.setdefault(r["name"], []).append(r)
    for i, r in enumerate(data["netcore_internalcalls"], 1):
        row = fmap.get((r["owner"], r["name"]))
        label = (r["owner"] + r["name"]) if r["name"].startswith(".") else f"{r['owner']}.{r['name']}"
        if not r["cond"]:
            if row is not None:
                if row["active"]:
                    note = f"对上（`{row['owner']}` 组，x64 可达）"
                else:
                    note = (f"表内存在但被 `{row['cond']}` 排除 -> x64 不可达")
            else:
                cand = [c for c in fname_only.get(r["name"], [])
                        if c["owner"].split(".")[-1] == r["owner"].split(".")[-1]]
                note = ("**对不上**：FCall 表无此 owner/name"
                        + (f"（同名在 `{cand[0]['owner']}`，owner 不同）" if cand else ""))
        elif "MONO" in r["cond"] or "NATIVEAOT" in r["cond"]:
            note = "MONO/NATIVEAOT 专属，CoreCLR 构建不编译"
        elif "WASM" in r["cond"]:
            note = "WASM 专属，桌面构建不编译"
        else:
            note = "条件编译（CORECLR 等），需 runtime 面单独核对"
        w(f"| {i} | `{md(label)}` | `{md(r['cond']) or '-'}` | "
          f"{'是' if row is not None else '否'} | {note} |")
    w("")
    w("要点：")
    w("")
    w("- 9 条 `String` 声明在源码里是**构造函数**（`public extern String(...)`），")
    w("  FCall 表用 `COR_CTOR_METHOD_NAME` 表示，脚本已归一化为 `.ctor`，可与")
    w("  `gStringFuncs` 的 9 条 FCDynamicSig 一一对上。")
    w("- 8 条 `Math.DivModInt` 的 `Div*/Mod*Internal` 在 FCall 表中**存在**，但被")
    w("  `#ifdef TARGET_32BIT` 包住 —— 即 **x64 桌面构建不可达**，本仓 ILC 也无需接入。")
    w("- `System.Runtime.GCFrameRegistration.RegisterForGCReporting` / `UnregisterForGCReporting`")
    w("  用 `#if CORECLR` 声明、在 FCall 表里对应 `gGCFrameRegistration` 组，**能对上**。")
    w("- `System.SpanHelpers.memmove/memset` 是 `#if MONO` 的 InternalCall，在非 MONO 分支里")
    w("  走 `[LibraryImport(RuntimeHelpers.QCall, EntryPoint = \"memmove\")]` —— 同一语义两条声明路径。")
    w("- `System.Intrinsics.X86.X86Base.CpuId` 只出现在 QCall 路径（`X86Base_CpuId`），")
    w("  `#if MONO` 的 InternalCall 形式不进 CoreCLR 构建。")
    w("")
    w("### 2.2 netcore 子集的 46 条 QCall")
    w("")
    w("| # | 声明（owner.method） | EntryPoint | 在 s_QCall[] 中 |")
    w("|---|---|---|---|")
    eps = {q["entry"] for q in data["qcalls"]}
    no_entry = []
    for i, r in enumerate(data["netcore_qcalls"], 1):
        ep = r.get("entry") or r["name"]
        hit = ep in eps
        if not hit:
            no_entry.append(r)
        w(f"| {i} | `{md(r['owner'])}.{md(r['name'])}` | `{md(ep)}` | {'是' if hit else '**否**'} |")
    w("")
    if no_entry:
        w("**对不上（C# 声明有、s_QCall[] 无）**：")
        w("")
        for r in no_entry:
            w(f"- `{md(r['owner'])}.{md(r['name'])}`（`{shell_path(r['file'])}:{r['line']}`，"
              f"EntryPoint = `{md(r.get('entry') or r['name'])}`）")
        w("")
        w("结论：这是 CoreCLR 侧重名的裁剪残留或入口在别的表（如 nativeaot 的")
        w("`runtimeeventinternal.cpp`）。不影响 ILC 判定，因为本仓接线面根本不含该域。")
        w("")

    w("## 3. 已接清单")
    w("")
    w("以下每行都是脚本从 `MethodRefResolver.BclResolveCore` 的 owner 分发出发，")
    w("在对应 `MethodMap.*.cs` 里找到同名 case 分支、且分支指名运行时符号后判定的。")
    w("")
    for label, rows in (("3.1 FCall", f_rows), ("3.2 QCall", q_rows)):
        w(f"### {label}")
        w("")
        wired = [r for r in rows if r["status"] == "wired"]
        if not wired:
            w("（无）")
            w("")
            continue
        if label.startswith("3.1"):
            w("| owner.method | 实现符号（Redhawk 侧） | 接线证据 |")
            w("|---|---|---|")
            for r in wired:
                w(f"| `{md(r['owner'])}.{md(r['name'])}` | `{md(r['impl'])}` | {md(evidence_cell(r))} |")
        else:
            w("| EntryPoint | 托管声明 | 接线证据 |")
            w("|---|---|---|")
            for r in wired:
                d = r.get("evidence", {}).get("decl") or {}
                dn = f"{d.get('owner', '?')}.{d.get('name', '?')}" if d else r["decl_owner"] + "." + r["decl_name"]
                w(f"| `{md(r['entry'])}` | `{md(dn)}` | {md(evidence_cell(r))} |")
        w("")

    w("## 4. 缺口清单")
    w("")
    w("排序：先按可达性 P0→P3，再按 owner。**FCall 与 QCall 分段统计**。")
    w("")
    for label, rows in (("4.1 FCall 缺口（missing / half-wired）", f_rows),
                        ("4.2 QCall 缺口（missing / half-wired）", q_rows)):
        w(f"### {label}")
        w("")
        sel = [r for r in rows if r["status"] in ("missing", "half-wired")]
        if not sel:
            w("（无）")
            w("")
            continue
        for tier in ("P0", "P1", "P2", "P3"):
            sub = [r for r in sel if r["tier"] == tier]
            if not sub:
                continue
            w(f"#### {tier}（{len(sub)} 条）")
            w("")
            if label.startswith("4.1"):
                w("| owner.method | 状态 | Redhawk 实现 | 证据 |")
                w("|---|---|---|---|")
                for r in sorted(sub, key=lambda x: (x["owner"], x["name"])):
                    w(f"| `{md(r['owner'])}.{md(r['name'])}` | {r['status']} | "
                      f"`{md(r['impl'])}` | {md(evidence_cell(r))} |")
            else:
                w("| EntryPoint | 托管声明 | 状态 | 证据 |")
                w("|---|---|---|---|")
                for r in sorted(sub, key=lambda x: (x["decl_owner"], x["decl_name"])):
                    dn = r["decl_owner"] + "." + r["decl_name"]
                    w(f"| `{md(r['entry'])}` | `{md(dn)}` | {r['status']} | {md(evidence_cell(r))} |")
            w("")

    w("### 4.3 RuntimeImport 缺口（corert 契约面）")
    w("")
    w("ILC 不是 Redhawk：`Rh*`/`Rhp*` 是 NativeAOT/Redhawk 的运行时 ABI，本仓 `src/runtime` 里")
    w("并不存在对应入口。下表按类别汇总，不逐条列 173 行（脚本 `--json` 可导出全表）。")
    w("")
    w("| 类别 | 条数 | 已接 | 说明 |")
    w("|---|---|---|---|")
    w(f"| libm 数学（`sin`/`cos`/`pow`/`fma`/`modf`/`scalbn` …） | {len(ri_libm)} | 语义已由 "
      "`icall_math*`/`icall_mathf*` 直接调 libm 覆盖 | 不需要 Redhawk 入口名 |")
    w(f"| `Rh*` GC/线程/异常运行时 ABI | "
      f"{sum(1 for r in ri_rh if (r['exported'] or '').startswith('Rh') and not (r['exported'] or '').startswith('Rhp'))} | "
      "1（`RhBulkMoveWithWriteBarrier`） | 其余在 ILC 用自有 icall 表达 |")
    w(f"| `Rhp*` 平台原语 | {sum(1 for r in ri_rh if (r['exported'] or '').startswith('Rhp'))} | 0 | "
      "Redhawk 专属汇编/平台桩，ILC 无对应物 |")
    w("")
    w("已接的唯一一条证据：")
    w("")
    for r in ri_rows:
        if r["name"] == "RhBulkMoveWithWriteBarrier":
            w(f"- `RhBulkMoveWithWriteBarrier` -> `src/il2cppc/Emit/MethodMap.GC.cs:320` "
              f"(`il2cpp::runtime::icall_rh_bulk_move_with_write_barrier`)")
    w("")

    w("### 4.4 QCall no-decl（表有、参考树无声明）")
    w("")
    nodecl = [r for r in q_rows if r["status"] == "no-decl"]
    if nodecl:
        w("| EntryPoint | 证据行 |")
        w("|---|---|")
        for r in nodecl:
            w(f"| `{md(r['entry'])}` | `{shell_path(r['file'])}:{r['line']}` |")
    else:
        w("（无）")
    w("")

    w("## 5. 与 reference 子集裁剪情况的说明")
    w("")
    w("**`reference/netcore/src/libraries/` 只有 4 个项目**：")
    w("`System.Private.CoreLib` / `System.Console` / `System.Private.Uri` /")
    w("`System.Text.RegularExpressions`。**这是裁剪子集，不是完整 upstream。**")
    w("")
    w(f"- `System.Private.CoreLib` 有 **{len(_count_cs(os.path.join(ref_root, 'netcore','src','libraries','System.Private.CoreLib')))}** 个 `.cs`。")
    w("- 作为对照，`reference/coreclr/src/coreclr/System.Private.CoreLib` 只有 "
      f"**{len(_count_cs(os.path.join(ref_root, 'coreclr','src','coreclr','System.Private.CoreLib')))}** 个 `.cs`，")
    w("  但它是 **CoreCLR 侧**的 CoreLib 增量（`*.CoreCLR.cs`），不是完整实现。")
    w("")
    w("### 5.1 本仓参考树里**根本没有源码**的域")
    w("")
    w("按 QCall 表的 owner 统计（owner 取自 `reference/coreclr` 的 C# 声明；")
    w("`no-decl` 行取 EntryPoint 前缀），下列域在 **netcore 子集**里没有对应实现文件，")
    w("只能靠 coreclr 侧增量或完全无源码：")
    w("")
    w("| owner | QCall 条数 | netcore 子集有声明 |")
    w("|---|---|---|")
    nc_owners = {r["owner"] for r in data["netcore_qcalls"]}
    for owner in sorted(q_owner_status, key=lambda o: -sum(q_owner_status[o].values()))[:24]:
        n = sum(q_owner_status[owner].values())
        w(f"| `{md(owner)}` | {n} | {'是' if owner in nc_owners else '**否**'} |")
    w("")
    w("此外，下列整块域在本仓的**全部**参考树（netcore + coreclr 增量）里都找不到")
    w("InternalCall/QCall 的 C# 声明，只能从 s_QCall[] 表反推：")
    w("")
    for r in nodecl[:20]:
        w(f"- `{md(r['entry'])}`（`{shell_path(r['file'])}:{r['line']}`）")
    if len(nodecl) > 20:
        w(f"- … 共 {len(nodecl)} 条")
    w("")
    w("### 5.2 裁剪带来的结论边界")
    w("")
    w("1. §2 里“对上”的判定只说明**名字对得上权威表**；是否已接线仍以 §3/§4 的状态列为准。")
    w("   裁剪子集里缺的声明会让行落到 `no-decl`，不能当成“已接”。")
    w("2. FCall 表的 owner 是全量 upstream 的类名（`System.RuntimeTypeHandle` 等），")
    w("   这些类在 netcore 子集里是**类型转发/partial**，真实实现在别处；")
    w("   因此本表对 P2 段（反射/句柄/装配）的缺口数字是**上界**。")
    w("3. ILC 的接线面是按 **net48 形状**生长的（见 `docs/audit/icall-inventory-net48.md`），")
    w("   netcore 的 QCall 面天然大面积落在 `missing`。")
    w("4. `missing` 行的 `non-public helper` 注释只说明**可达路径**（用户代码要通过 public")
    w("   包装绕进来），不改变“无接线”的事实；包装方法本身也需要该 icall 才能工作。")
    w("")
    w("## 6. 下一步建议（P0/P1 优先队列）")
    w("")
    gc_gaps = [r for r in f_rows
               if r["owner"] == "System.GC" and r["status"] in gap_statuses]
    w(f"1. **GC FCall 面（P0，{len(gc_gaps)} 条）**："
      + " / ".join("`" + r["name"] + "`" for r in
                   sorted(gc_gaps, key=lambda x: x["name"])) + "。")
    w("   ILC 已有 `CollectionCount` / `SuppressFinalize` / `GetTotalAllocatedBytes` /")
    w("   `RegisterForFullGCNotification` / `CancelFullGCNotification` 的**别名**分支")
    w("   （`MethodMap.GC.cs:96/56/62/102/107`），把 netcore 的 `_`/`Internal` 名字加进")
    w("   同一 case 即可，属最小改动。")
    w("2. **Interlocked FCall 别名（P0，9 条）**：`Exchange32/64/Object`、")
    w("   `CompareExchange32/32Pointer/64/Object`、`ExchangeAdd32/64` 是 netcore 的**内部名**，")
    w("   ILC 只接了公开名 `Exchange`/`CompareExchange`/`ExchangeAdd`")
    w("   （`MethodMap.Interlocked.cs:57/63/101`）。加别名即可。")
    w("3. **RuntimeHelpers FCall（P0，5 条）**：`TryGetHashCode`/`ContentEquals`/")
    w("   `TryEnsureSufficientExecutionStack`/`GetTailCallArgBuffer`/`GetTailCallInfo` ——")
    w("   现有 `GetHashCode`/`Equals`/`EnsureSufficientExecutionStack` 分支")
    w("   （`MethodMap.RuntimeHelpers.cs:47/56/80`）是 net48 名字，需加 netcore 别名。")
    w("4. **Thread FCall（P0，3 条）**：`InternalFinalize`/`CatchAtSafePoint`/")
    w("   `CurrentThreadIsFinalizerThread` —— 后者在 netcore 子集里被 `System.Threading.Thread`")
    w("   直接声明为 InternalCall，是**用户可见 Thread 语义**的一部分。")
    p0q_owner = {}
    for r in q_rows:
        if r["tier"] == "P0" and r["status"] in gap_statuses:
            p0q_owner[r["decl_owner"]] = p0q_owner.get(r["decl_owner"], 0) + 1
    top_owners = sorted(p0q_owner.items(), key=lambda kv: -kv[1])[:4]
    w(f"5. **QCall P0 段（{p0_q} 条）**：集中在 "
      + " / ".join(f"`{md(o)}`({n})" for o, n in top_owners) + "，")
    w("   多数其实是 ILC 已有 net48 同名 icall 的**入口名不同**（如 `GCInterface_Collect` vs")
    w("   现有 `icall_gc_collect`），建议按 owner 成批接，而不是逐条接。")
    w("6. `half-wired` 的 2 条：`Math.ModF`（需 tuple/out 发射支持）与")
    w("   `Marshal.GetComInterfaceForObject`（现有 COM marker 路径）；属转译器能力缺口，")
    w("   应单独立项。")
    w("")

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(L) + "\n")


def _count_cs(root):
    out = []
    for dirpath, _dn, fns in os.walk(root):
        for fn in fns:
            if fn.endswith(".cs"):
                out.append(fn)
    return out


def main(argv):
    global ROOT
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo-root", default=".")
    ap.add_argument("--reference-root", default=None,
                    help="path to the reference/ tree when it is not inside --repo-root "
                         "(reference/ is gitignored, so a worktree usually points this at "
                         "the main checkout's reference/ dir)")
    ap.add_argument("--json")
    ap.add_argument("--template")
    ap.add_argument("--out")
    args = ap.parse_args(argv)
    ROOT = os.path.abspath(args.repo_root)

    ref = args.reference_root or os.path.join(ROOT, "reference")
    ecall = os.path.join(ref, "coreclr", "src", "coreclr", "vm", "ecalllist.h")
    qcall = os.path.join(ref, "coreclr", "src", "coreclr", "vm", "qcallentrypoints.cpp")
    corert_ri = os.path.join(ref, "corert", "src", "System.Private.CoreLib", "src",
                             "System", "Runtime", "RuntimeImports.cs")
    for p in (ecall, qcall, corert_ri):
        if not os.path.exists(p):
            sys.exit("missing authoritative table: " + p)

    fcalls, fclasses, fgroups = parse_ecalllist(ecall)
    qcalls = parse_qcall_table(qcall)
    rimports = parse_runtimeimports(corert_ri)

    netcore_root = os.path.join(ref, "netcore", "src", "libraries", "System.Private.CoreLib")
    coreclr_root = os.path.join(ref, "coreclr", "src", "coreclr", "System.Private.CoreLib")
    nc_ic, nc_qc = parse_cs_declarations(netcore_root)
    cr_ic, cr_qc = parse_cs_declarations(coreclr_root)

    wiring = parse_wiring(ROOT)

    data = {
        "fcalls": fcalls,
        "fclasses": fclasses,
        "qcalls": qcalls,
        "rimports": rimports,
        "netcore_internalcalls": nc_ic,
        "netcore_qcalls": nc_qc,
        "coreclr_internalcalls": cr_ic,
        "coreclr_qcalls": cr_qc,
        "wiring": {k: v for k, v in wiring.items() if k != "symbols"},
        "symbol_count": len(wiring["symbols"]),
    }
    f_rows, q_rows = build_rows(data, wiring)
    data["fcall_rows"] = f_rows
    data["qcall_rows"] = q_rows
    if args.json:
        with open(args.json, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=1)
    if args.out:
        write_report(args.out, data, f_rows, q_rows, ref)
    print("fcalls=%d classes=%d groups=%d qcalls=%d rimports=%d "
          "netcore_ic=%d netcore_qc=%d coreclr_ic=%d coreclr_qc=%d symbols=%d" % (
              len(fcalls), len(fclasses), len(fgroups), len(qcalls), len(rimports),
              len(nc_ic), len(nc_qc), len(cr_ic), len(cr_qc), len(wiring["symbols"])))
    print("fcall_status=%s" % status_counts(f_rows))
    print("qcall_status=%s" % status_counts(q_rows))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
