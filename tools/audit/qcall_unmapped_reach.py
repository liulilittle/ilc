#!/usr/bin/env python3
"""Tier the UNMAPPED QCalls by metadata accessibility.

Answers one question: of the shipped QCalls that have no mapping arm, which can
user code name? The two-level test (type access, then member access) is the one
recut_shipped_icalls.py uses; a public type with a private member is INNER-ONLY,
not a hole.

Inputs (both produced by the audit probes, see docs/audit/qcall-unmapped-reachability.md):
  --qcall-tsv   QCallProbe.exe <corelib.dll> <out.tsv>
                owner \t name \t entry \t typeAccess/methodAccess/static
  --wiring      IcallWiringProbe.exe <corelib.dll> <out.tsv>   (kind=qcall rows)

Usage:
  python tools/audit/qcall_unmapped_reach.py \
      --qcall-tsv .opencode/tmp/reach/qcall-8031.tsv \
      --wiring    .opencode/tmp/reach/wiring-8031-qcall.tsv
"""
import argparse
import collections
import io


def load_qcalls(path):
    rows = []
    for line in io.open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line.strip():
            continue
        f = line.split("\t")
        if len(f) < 4:
            continue
        acc = f[3].split("/")
        rows.append({
            "owner": f[0],
            "name": f[1],
            "typeAccess": acc[0] if len(acc) > 0 else "",
            "methodAccess": acc[1] if len(acc) > 1 else "",
        })
    return rows


def load_unmapped_qcalls(path):
    pairs = set()
    for line in io.open(path, encoding="utf-8"):
        f = line.rstrip("\n").split("\t")
        if len(f) >= 5 and f[3] == "qcall" and f[4] == "unmapped":
            pairs.add((f[0], f[1]))
    return pairs


def reachability(row):
    """USER-CALLABLE / INNER-ONLY / INTERNAL-TYPE (recut_shipped_icalls.py:138)."""
    if row["typeAccess"] != "public":
        return "INTERNAL-TYPE"
    if row["methodAccess"] in ("public", "family", "familyorassembly"):
        return "USER-CALLABLE"
    return "INNER-ONLY"


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--qcall-tsv", required=True)
    ap.add_argument("--wiring", required=True)
    args = ap.parse_args(argv)

    qcalls = load_qcalls(args.qcall_tsv)
    unmapped = load_unmapped_qcalls(args.wiring)
    selected = [r for r in qcalls if (r["owner"], r["name"]) in unmapped]

    print("unmapped_pairs=%d" % len(unmapped))
    print("matched_rows=%d" % len(selected))
    counts = collections.Counter(reachability(r) for r in selected)
    for tier, n in counts.most_common():
        print("%s %d" % (tier, n))
    for r in selected:
        if reachability(r) == "USER-CALLABLE":
            print("USER-CALLABLE %s::%s" % (r["owner"], r["name"]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
