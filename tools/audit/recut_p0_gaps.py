"""Recut the CoreLib P0 gap list against the shipped CoreLib surfaces.

The reference-source tables (ecalllist.h / qcallentrypoints.cpp) describe the
CoreCLR main branch, so a member listed there may not exist in any released
System.Private.CoreLib. This script answers "of the P0 gaps the audit reports,
which ones actually exist in the target assembly?".

Two join-key corrections are mandatory and are the whole point of this file:

  1. A member's table can migrate between the reference source and the shipped
     assembly (67 QCalls became FCalls in 8.0.31), so the lookup runs over the
     FCall u QCall union.
  2. .NET 7+ expands [LibraryImport] through a source generator, so 64 of the
     192 shipped QCalls carry the name `<X>g____PInvoke|N_M`. The real entry
     point rides ImplMap.Entry. Names are normalised back to `X` before join.

Every row lands in exactly one of three buckets, and the script asserts that
the three sum to the input size -- the earlier version silently dropped the
"only in 9.0.20" bucket and undercounted 17 as 12.

Usage (from the repo root):
    dotnet build tools/audit/MetadataIcallCounter/MetadataIcallCounter.csproj -c Release
    dotnet build tools/audit/QCallProbe/QCallProbe.csproj -c Release
    MetadataIcallCounter.exe <corelib.dll> <out-fcall.tsv>
    QCallProbe.exe <corelib.dll> <out-qcall.tsv>
    python tools/audit/recut_p0_gaps.py --probe-dir <dir-with-tsvs> \
        --audit-json <tmp>/icall-corelib.json [--target 80] [--json <out>]
"""

import argparse
import collections
import json
import os
import re
import sys

GEN_NAME = re.compile(r"^<(.+?)>g____PInvoke\|\d+_\d+$")


def canonical_name(name):
    """Strip the source-generator suffix so a shipped method joins its declaration."""
    m = GEN_NAME.match(name)
    return m.group(1) if m else name


def read_tsv(path):
    rows = []
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 2 and parts[0]:
                rows.append(parts)
    return rows


def surface(probe_dir, version):
    """(declaringType, methodName) set for one CoreLib version, across both tables.

    Probe files are named by major version (corelib8-fcall.tsv), so "80" and "8"
    both resolve to the same files.
    """
    short = version[0] if len(version) > 1 else version
    keys = set()
    found = False
    for stem in (version, short):
        for table in ("fcall", "qcall"):
            path = os.path.join(probe_dir, "corelib%s-%s.tsv" % (stem, table))
            if not os.path.exists(path):
                continue
            found = True
            for parts in read_tsv(path):
                keys.add((parts[0], canonical_name(parts[1])))
        if found:
            break
    return keys


def gap_rows(audit_json):
    """P0 rows whose wiring status is a gap (missing / half-wired / stub / no-decl)."""
    with open(audit_json, encoding="utf-8") as fh:
        doc = json.load(fh)
    gap_status = {"missing", "half-wired", "stub", "no-decl"}
    out = {"FCall": [], "QCall": []}
    for table, key in (("FCall", "fcall_rows"), ("QCall", "qcall_rows")):
        for row in doc[key]:
            if row.get("tier") != "P0" or row.get("status") not in gap_status:
                continue
            owner = row.get("decl_owner") or row.get("owner")
            name = row.get("decl_name") or row.get("name")
            out[table].append({"owner": owner, "name": name,
                               "status": row.get("status"),
                               "entry": row.get("entry") or row.get("impl")})
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--probe-dir", required=True)
    ap.add_argument("--audit-json", required=True)
    ap.add_argument("--target", default="80", help="target CoreLib minor version (80 / 90)")
    ap.add_argument("--other", default=None, help="secondary version; defaults to the other one")
    ap.add_argument("--json", default=None)
    args = ap.parse_args()

    other = args.other or ("90" if args.target == "80" else "80")
    target_surface = surface(args.probe_dir, args.target)
    other_surface = surface(args.probe_dir, other)
    if not target_surface:
        sys.exit("no probe TSVs found for version %s under %s" % (args.target, args.probe_dir))

    gaps = gap_rows(args.audit_json)

    report = {}
    total = 0
    sums = 0
    for table in ("FCall", "QCall"):
        rows = gaps[table]
        present = [r for r in rows if (r["owner"], r["name"]) in target_surface]
        only_other = [r for r in rows
                      if (r["owner"], r["name"]) not in target_surface
                      and (r["owner"], r["name"]) in other_surface]
        absent = [r for r in rows
                  if (r["owner"], r["name"]) not in target_surface
                  and (r["owner"], r["name"]) not in other_surface]
        total += len(rows)
        sums += len(present) + len(only_other) + len(absent)
        report[table] = {"total": len(rows), "present": present,
                         "only_other": only_other, "absent": absent}

        print("=== %s  P0 gap = %d ===" % (table, len(rows)))
        print("  present in target (%s) : %d" % (args.target, len(present)))
        print("  only in other   (%s) : %d" % (other, len(only_other)))
        print("  in neither             : %d" % len(absent))
        for r in present:
            print("    PRESENT %-46s %-34s %s" % (r["owner"], r["name"], r["status"]))
        for r in only_other:
            print("    ONLY%s  %-46s %s" % (other, r["owner"], r["name"]))
        print()

    if sums != total:
        sys.exit("bucket identity broken: %d + ... != %d" % (sums, total))
    print("BUCKET IDENTITY OK: three buckets sum to %d" % total)

    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump(report, fh, ensure_ascii=False, indent=1)
        print("wrote " + args.json)


if __name__ == "__main__":
    main()
