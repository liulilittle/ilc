"""Recut the CoreLib icall gap audit against the SHIPPED assembly's own tables.

The existing audit joins the reference-source tables (ecalllist.h /
qcallentrypoints.cpp) against the transpiler's wiring. Those tables describe the
CoreCLR MAIN BRANCH, so a gap can be a member no released assembly defines. This
script answers the other question: of the members the TARGET assembly actually
declares, which are unwired, and can user code reach them?

Inputs are produced by two probes over the shipped DLL:

  --fcall-tsv   MetadataIcallCounter.exe <corelib.dll> <out.tsv>
                owner \t name \t sig \t typeAccess/methodAccess/static \t kind
  --qcall-tsv   QCallProbe.exe <corelib.dll> <out.tsv>
                owner \t name \t entry \t typeAccess/methodAccess/static

Accessibility is the whole point and is why the reference tables could not be
used: a member is user-reachable only when its TYPE is public. CoreCLR's
internal types (RuntimeAssembly, RuntimeFieldHandle) hold hundreds of members
that no program can name, and treating them as live gaps overstates the work.

Name normalisation is mandatory. .NET 7+ lowers [LibraryImport] into
`<X>g____PInvoke|N_M`, so 64 of the 192 shipped QCalls carry a generated name;
the real member is the `<X>` inside the angle brackets.

Two questions are answered independently, because they need different evidence:

  WIRED   is this member mapped by the transpiler? Matched against the wiring
          JSON from tools/audit/parse_icall_tables.py (the reference tables still
          describe WHICH members the transpiler implements -- that part is a
          property of our code, not of the target).
  REACH   can user code name the member? Decided from the shipped assembly's own
          type accessibility, needing no call graph at all. This is what closes
          the "338 unmeasured" hole: a QCall on an internal type is not an
          unmeasured risk, it is provably out of reach of user code.

Usage:
    python tools/audit/recut_shipped_icalls.py \
        --fcall-tsv <fcall-8.0.31.tsv> --qcall-tsv <qcall-8.0.31.tsv> \
        --wiring <icall-corelib.json> [--json <out>]
"""

import argparse
import collections
import json
import re
import sys

# `<GetModule>g____PInvoke|52_0` -> `GetModule`
_PINVOKE_GEN = re.compile(r"^<([^>]+)>g____PInvoke\|\d+_\d+$")


def normalise_name(name):
    m = _PINVOKE_GEN.match(name)
    if m:
        return m.group(1)
    return name


def load_tsv(path, has_sig):
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\r\n")
            if not line:
                continue
            parts = line.split("\t")
            if has_sig:
                if len(parts) < 5:
                    continue
                owner, name, sig, access, kind = parts[0], parts[1], parts[2], parts[3], parts[4]
            else:
                if len(parts) < 4:
                    continue
                owner, name, entry, access = parts[0], parts[1], parts[2], parts[3]
                sig, kind = "", ""
            if not owner or not name:
                continue
            acc = access.split("/")
            rows.append({
                "owner": owner.replace("+", "."),
                "name": normalise_name(name),
                "rawName": name,
                "sig": sig,
                "typeAccess": acc[0] if len(acc) > 0 else "",
                "methodAccess": acc[1] if len(acc) > 1 else "",
                "static": acc[2] if len(acc) > 2 else "",
                "kind": kind,
            })
    return rows


def load_wiring(path):
    """(owner, name) of every member the transpiler resolves, from the probe TSV.

    The probe calls MethodRefResolver.BclResolve directly, so this is the emit
    path's own answer. Reference-table "wired" counts cannot be used for this
    join: those tables cover 29 classes, and members they omit (System.Object.
    GetType, Monitor.Enter, Interlocked.CompareExchange) are implemented but
    would be reported as gaps.

    "loud" counts as wired too: a deliberate LOUD rejection means the member
    is RECOGNISED and refuses to emit a wrong call, which is a different (and
    louder) state than "unmapped" -- but neither is a silent undefined symbol.
    Only "unmapped" is treated as a gap.
    """
    wired = set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\r\n").split("\t")
            if len(parts) < 5:
                continue
            owner, name, _sig, kind, status = parts[0], parts[1], parts[2], parts[3], parts[4]
            if kind != "icall":
                continue
            if status in ("resolved", "loud"):
                wired.add((owner.replace("+", "."), normalise_name(name)))
    return wired


def reachability(row):
    """Decide who can name this member, in two steps that must both pass.

    A member is user-reachable only when its TYPE is public AND its own access
    lets callers in. Testing the type alone overstates the gap badly: of 295
    unwired members on public types, 132 are `private` and 120 are
    `internal`/`assembly`, so 252 of them cannot be written in user source at
    all. Both facts come from the shipped assembly's metadata, so neither needs
    a call graph -- this is what makes the QCall hole closable.

    Returns one of:
      USER-CALLABLE  public type + public/protected member (a real usable gap)
      INNER-ONLY     public type, but the member is private/internal/assembly
      INTERNAL-TYPE  the declaring type itself is not public
    """
    if row["typeAccess"] != "public":
        return "INTERNAL-TYPE"
    if row["methodAccess"] in ("public", "family", "familyorassembly"):
        return "USER-CALLABLE"
    return "INNER-ONLY"


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--fcall-tsv", required=True)
    ap.add_argument("--qcall-tsv", required=True)
    ap.add_argument("--wiring", required=True,
                    help="TSV from IcallWiringProbe (resolved/loud/unmapped per icall)")
    ap.add_argument("--json")
    args = ap.parse_args(argv)

    fcalls = load_tsv(args.fcall_tsv, has_sig=True)
    qcalls = load_tsv(args.qcall_tsv, has_sig=False)
    wired = load_wiring(args.wiring)

    report = {}
    for table, rows in (("FCall", fcalls), ("QCall", qcalls)):
        if table == "QCall":
            # The wiring probe enumerates InternalCall methods only; a QCall
            # enters through [LibraryImport]/DllImport, so the probe cannot see
            # it and every QCall would be reported as a gap. Their wiring state
            # is UNMEASURED here, not absent.
            report[table] = {
                "declared": len(rows),
                "unwired": 0,
                "byReach": {},
                "userCallable": [],
                "innerOnlyCount": 0,
                "internalTypeCount": 0,
                "unmeasured": len(rows),
            }
            continue
        unwired = [r for r in rows if (r["owner"], r["name"]) not in wired]
        reach = collections.Counter(reachability(r) for r in unwired)
        report[table] = {
            "declared": len(rows),
            "unwired": len(unwired),
            "byReach": dict(reach),
            "userCallable": [r for r in unwired if reachability(r) == "USER-CALLABLE"],
            "innerOnlyCount": reach.get("INNER-ONLY", 0),
            "internalTypeCount": reach.get("INTERNAL-TYPE", 0),
        }

    for table in ("FCall", "QCall"):
        d = report[table]
        print(f"{table}: declared={d['declared']} unwired={d['unwired']}")
        print(f"  USER-CALLABLE {d['byReach'].get('USER-CALLABLE', 0):4d}  (public type + public/protected member)")
        print(f"  INNER-ONLY    {d['innerOnlyCount']:4d}  (public type, private/internal member)")
        print(f"  INTERNAL-TYPE {d['internalTypeCount']:4d}  (declaring type is not public)")
        if d.get("unmeasured"):
            print(f"  UNMEASURED    {d['unmeasured']:4d}  (the wiring probe cannot see this table)")

    total_public = sum(report[t]["byReach"].get("USER-CALLABLE", 0) for t in ("FCall", "QCall"))
    total_inner = sum(report[t]["innerOnlyCount"] for t in ("FCall", "QCall"))
    total_internal = sum(report[t]["internalTypeCount"] for t in ("FCall", "QCall"))
    total = total_public + total_inner + total_internal
    # Partition assertion: every unwired member is exactly one of the two.
    declared_total = report["FCall"]["declared"] + report["QCall"]["declared"]
    unwired_total = report["FCall"]["unwired"] + report["QCall"]["unwired"]
    assert total == unwired_total, f"partition mismatch {total} != {unwired_total}"

    print()
    print(f"shipped tables: declared={declared_total}  unwired={unwired_total}")
    print(f"  USER-CALLABLE (live)  : {total_public}")
    print(f"  INNER-ONLY            : {total_inner}")
    print(f"  INTERNAL-TYPE         : {total_internal}")

    # The union view: shipped-unwired that are user-facing, per table.
    print()
    print("live user-facing gaps by namespace:")
    ns = collections.Counter(
        (r["owner"].rsplit(".", 1)[0] or r["owner"])
        for t in ("FCall", "QCall")
        for r in report[t]["userCallable"]
    )
    for name, cnt in ns.most_common(12):
        print(f"  {cnt:4d}  {name}")

    if args.json:
        payload = {
            "fcallDeclared": report["FCall"]["declared"],
            "qcallDeclared": report["QCall"]["declared"],
            "fcallUnwired": report["FCall"]["unwired"],
            "qcallUnwired": report["QCall"]["unwired"],
            "liveGaps": total_public,
            "qcallUnmeasured": report["QCall"].get("unmeasured", 0),
            "innerOnly": total_inner,
            "internalTypeMembers": total_internal,
            "byNamespace": dict(ns),
            "userCallable": {
                "FCall": report["FCall"]["userCallable"],
                "QCall": report["QCall"]["userCallable"],
            },
        }
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=1)
        print(f"\nwrote {args.json}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
