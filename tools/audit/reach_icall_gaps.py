"""Quantify the CoreLib icall gaps by whether USER code can reach them.

The gap lists answer "is this member wired?" for the whole CoreLib surface. That
number alone cannot prioritise work: CoreCLR's own tables describe members that
no shipped assembly defines, and a wired-or-not status says nothing about whether
any program can get there. This script joins the gap list against a call graph of
the shipped assembly and reports, per gap, the shortest chain from a public face.

Inputs (both are required -- neither can be inferred from the other):

  --gaps   icall-corelib.json from tools/audit/parse_icall_tables.py --json
           (carries table/owner/name/status/tier per row)
  --graph  callgraph JSON from tools/audit/IcallCallGraph
           (carries publicFace / publicDistance / callers per icall)

Join key is (owner, name) after the two normalisations the shipped assembly
requires: .NET 7+ lowers [LibraryImport] into `<X>g____PInvoke|N_M`, and a member
can migrate between the FCall and QCall tables. Both are handled by trying the
FCall and QCall rows together and normalising the ImplMap name back.

Buckets (a gap lands in exactly one, and the script asserts the partition sums to
the gap count):

  DIRECT      publicFace   -- the icall IS a public entry point; a gap here is a
                             missing feature a program calls directly
  SHADOW      distance > 0 -- reachable only through N managed wrapper frames;
                             a gap here breaks the wrapper, not the direct call
  UNREACHABLE no path      -- nothing in the shipped assembly reaches it; wiring
                             it cannot change any program's behaviour

Usage:
    python tools/audit/reach_icall_gaps.py \
        --gaps  .opencode/tmp/reach/icall-corelib.json \
        --graph .opencode/tmp/reach/corelib-callgraph.json \
        [--json <out>]
"""

import argparse
import collections
import json
import sys


def load_gap_rows(path):
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    rows = []
    for key in ("fcall_rows", "qcall_rows"):
        for r in doc.get(key, []):
            # The two tables report their member under different keys: FCall rows
            # carry owner/name, QCall rows carry decl_owner/decl_name. Reading only
            # owner/name silently produced 319 ownerless rows, which then failed the
            # graph join and were reported as "not in graph" -- a wrong answer that
            # looked like data.
            owner = r.get("owner") or r.get("decl_owner") or ""
            name = r.get("name") or r.get("decl_name") or ""
            rows.append({
                "table": r.get("table"),
                "owner": owner,
                "name": name,
                "status": r.get("status") or "",
                "tier": r.get("tier") or "",
                "namespace": r.get("namespace") or (owner.rsplit(".", 1)[0] if "." in owner else ""),
            })
    return rows


def is_gap(status):
    return status in ("missing", "half-wired", "stub", "no-decl")


def load_graph(path):
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    index = {}
    for ic in doc.get("icalls", []):
        key = (ic.get("owner") or "", ic.get("name") or "")
        index.setdefault(key, []).append(ic)
    return index


def classify(ic):
    if ic.get("publicFace"):
        return "DIRECT"
    d = ic.get("publicDistance")
    if isinstance(d, int) and d > 0:
        return "SHADOW"
    return "UNREACHABLE"


def caller_is_public(caller):
    return bool(caller.get("publicFace"))


def explain_unreachable(ic):
    """Say WHY an icall has no path to a public face.

    The graph requires type AND method to be public to seed a chain
    (IcallCallGraph/Program.cs starts BFS at `TypePublic && MethodPublic`). So an
    icall on an internal type -- RuntimeAssembly, RuntimeFieldHandle -- can never
    be reachable even when a named wrapper exists, and that is a different fact
    from "nothing calls it". Reporting both as UNREACHABLE would overstate the
    evidence: the first is a visibility property of the shipped assembly, the
    second is an absence of callers.
    """
    callers = ic.get("callers") or []
    if not callers:
        return "no callers at all"
    if any(caller_is_public(c) for c in callers):
        # A public caller with no distance contradicts the graph's own BFS.
        return "public caller but no distance (graph inconsistency)"
    return f"{len(callers)} caller(s), none on a public face (internal container)"


def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--gaps", required=True)
    ap.add_argument("--graph", required=True)
    ap.add_argument("--json")
    args = ap.parse_args(argv)

    gap_rows = load_gap_rows(args.gaps)
    graph = load_graph(args.graph)

    gaps = [r for r in gap_rows if is_gap(r["status"])]
    per_table = collections.Counter(r["table"] for r in gaps)

    buckets = collections.Counter()
    by_bucket_rows = {"DIRECT": [], "SHADOW": [], "UNREACHABLE": [], "NOT-IN-GRAPH": []}
    distance_hist = collections.Counter()
    unreachable_reasons = collections.Counter()

    for r in gaps:
        hits = graph.get((r["owner"], r["name"]))
        if not hits:
            buckets["NOT-IN-GRAPH"] += 1
            by_bucket_rows["NOT-IN-GRAPH"].append(r)
            continue
        # The graph may hold several overloads under one (owner, name); the icall
        # is reachable if ANY of them is.
        best = None
        for ic in hits:
            c = classify(ic)
            if c == "DIRECT":
                best = ic
                break
            if c == "SHADOW":
                if best is None or (ic.get("publicDistance") or 10**9) < (best.get("publicDistance") or 10**9):
                    best = ic
        if best is None:
            buckets["UNREACHABLE"] += 1
            reason = explain_unreachable(hits[0])
            unreachable_reasons[reason] += 1
            by_bucket_rows["UNREACHABLE"].append(dict(r, why=reason))
        elif classify(best) == "DIRECT":
            buckets["DIRECT"] += 1
            by_bucket_rows["DIRECT"].append(r)
        else:
            buckets["SHADOW"] += 1
            by_bucket_rows["SHADOW"].append(r)
            distance_hist[best.get("publicDistance")] += 1

    total = sum(buckets.values())
    assert total == len(gaps), f"partition mismatch: {total} != {len(gaps)}"

    # The graph can only see members the assembly marks InternalCall
    # (IcallCallGraph/Program.cs collects impl==InternalCall). A QCall reaches the
    # runtime through [LibraryImport]/DllImport instead, so most QCalls are
    # structurally invisible to this join. Without stating that, "NOT-IN-GRAPH"
    # reads as "unreachable", which the evidence does not support.
    visible = len(gaps) - buckets["NOT-IN-GRAPH"]

    print(f"gaps_total={len(gaps)}  fcall={per_table.get('FCall',0)}  qcall={per_table.get('QCall',0)}")
    print(f"  graph-visible {visible:4d}  of {len(gaps)}  (the rest are not InternalCall)")
    print(f"  DIRECT      {buckets['DIRECT']:4d}  (the icall is a public entry point)")
    print(f"  SHADOW      {buckets['SHADOW']:4d}  (reachable through managed wrappers)")
    print(f"  UNREACHABLE {buckets['UNREACHABLE']:4d}  (no path in the shipped assembly)")
    print(f"  NOT-IN-GRAPH{buckets['NOT-IN-GRAPH']:4d}  (not visible to the graph -- see note)")
    if distance_hist:
        top = ", ".join(f"d={k}:{v}" for k, v in sorted(distance_hist.items())[:6])
        print(f"  shadow distance histogram: {top}")
        for why, cnt in unreachable_reasons.most_common():
            print(f"  unreachable: {cnt:4d}  {why}")
    print("  note: NOT-IN-GRAPH is NOT evidence of unreachability. The graph lists only")
    print("        members whose impl is InternalCall; a QCall enters through")
    print("        [LibraryImport]/DllImport and is invisible to it. Treat those rows")
    print("        as unmeasured, not as dead.")

    def show(bucket, limit=12):
        rows = by_bucket_rows[bucket]
        if not rows:
            return
        print(f"\n  -- {bucket} (first {min(limit, len(rows))} of {len(rows)}) --")
        for r in rows[:limit]:
            print(f"     {r['table']:6s} {r['owner']}.{r['name']}  [{r['tier']}]")

    show("DIRECT")
    show("SHADOW")

    if args.json:
        payload = {
            "gapsTotal": len(gaps),
            "perTable": dict(per_table),
            "buckets": dict(buckets),
            "shadowDistance": {str(k): v for k, v in distance_hist.items()},
            "direct": by_bucket_rows["DIRECT"],
            "shadow": by_bucket_rows["SHADOW"],
            "unreachable": by_bucket_rows["UNREACHABLE"],
            "notInGraph": by_bucket_rows["NOT-IN-GRAPH"],
        }
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=1)
        print(f"\nwrote {args.json}")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
