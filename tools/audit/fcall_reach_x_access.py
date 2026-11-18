#!/usr/bin/env python3
"""Cross FCall gap accessibility with call-graph reachability.

Two documents each answered one axis of the same 172-member set:
  - icall-gap-reachability-301.md : DIRECT / SHADOW / UNREACHED (call graph)
  - corelib-p0-shipped-recut.md   : USER-CALLABLE / INNER-ONLY / INTERNAL-TYPE

This prints the cross, which is what decides whether a gap is worth wiring: a
gap reachable only through a public method that already works is interior
machinery, not a user-visible hole.

Input: .opencode/tmp/reach/gap-reach-301.json (produced by reach_gaps_301.py).
Its `detail` rows carry both `reach` and `bucket`, so the two axes cannot drift
apart -- they are read from the same record.

Usage:
  python tools/audit/fcall_reach_x_access.py [--json PATH]
"""
import argparse
import collections
import io
import json


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", default=".opencode/tmp/reach/gap-reach-301.json")
    args = ap.parse_args(argv)

    data = json.load(io.open(args.json, encoding="utf-8"))
    rows = [r for r in data["detail"] if r["table"] == "FCall"]

    print("fcall_gaps=%d" % len(rows))
    cross = collections.Counter((r["reach"], r["bucket"]) for r in rows)
    print("%-14s %-10s %s" % ("accessibility", "bucket", "count"))
    for (acc, bucket), n in sorted(cross.items()):
        print("%-14s %-10s %d" % (acc, bucket, n))

    print("--- accessibility totals ---")
    for k, v in collections.Counter(r["reach"] for r in rows).most_common():
        print("%s %d" % (k, v))
    print("--- bucket totals ---")
    for k, v in collections.Counter(r["bucket"] for r in rows).most_common():
        print("%s %d" % (k, v))

    reachable = [r for r in rows if r["bucket"] in ("DIRECT", "SHADOW")]
    user = [r for r in reachable if r["reach"] == "USER-CALLABLE"]
    print("reachable=%d user_callable_among_reachable=%d" % (len(reachable), len(user)))
    for r in user:
        print("USER-CALLABLE %s::%s" % (r["owner"], r["name"]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
