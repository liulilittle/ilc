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

    # The two inputs cannot be joined on the signature: qcall-8031.tsv column 3
    # is the ENTRY name (Buffer_MemMove) while the wiring TSV column 3 is the
    # full signature. So the join key is (owner, name) -- which is only sound
    # while that key is unique. Assert it rather than assume it: a duplicate
    # would merge overloads and report a member as unmapped when only one
    # overload is (Marshal.DestroyStructure(IntPtr) is unmapped while
    # DestroyStructure(IntPtr, Type) resolves; the FCall set has 16 such
    # duplicates). Today the QCall set has 192 keys and 0 duplicates.
    dup = [k for k, n in collections.Counter(
        (r["owner"], r["name"]) for r in qcalls).items() if n > 1]
    if dup:
        raise SystemExit(
            "qcall-8031.tsv has %d duplicate (owner, name) keys; the (owner, name) "
            "join would merge overloads and overstate the gap. Join on the "
            "signature instead, or split the duplicates by hand. First: %r"
            % (len(dup), dup[0]))

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
