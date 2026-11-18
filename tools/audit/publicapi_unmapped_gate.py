#!/usr/bin/env python3
"""Gate the core-type `publicapi unmapped` set: it may shrink, never grow.

Why this exists: `IcallWiringProbe` emits a `publicapi` row for every public
method on a public type, precisely so a public method with no mapping arm can be
spotted. Nothing gated on those rows, and two real holes shipped through it --
`Array.CreateInstance` (six overloads) and `Array.Clone` -- each found only by
accident while chasing something else. Both were `publicapi unmapped` the whole
time.

The full net48 set is 9328 rows, most of them APIs a normal program never names
(Reflection.Emit builders, remoting, `_Type`). Gating all of it would be noise.
This gate covers the CORE owners -- the types a program names directly: the
primitives, String, Array, Console, Convert, Math, the collections, IO, and the
threading primitives. That subset is 517 rows today.

Two baselines, because one is not enough:

  --baseline          the UNMAPPED set. A row here that is now resolved is fine
                      (the set shrank). A row NOT here that is unmapped is a NEW
                      hole and fails.
  --resolved-baseline the RESOLVED set. A row here that is no longer resolved is
                      a REGRESSION and fails. Without this, deleting a mapping
                      arm is invisible: the row simply returns to the unmapped
                      set, where the unmapped baseline already lists it (or, if
                      it was never listed, the unmapped gate sees no change
                      either). Measured: deleting the Array.Clone arm with only
                      the unmapped baseline in place reported new=0.

So the gate is two-sided: the unmapped set may shrink, the resolved set may grow,
and neither may move the other way.

Usage:
  python tools/audit/publicapi_unmapped_gate.py \
      --wiring .opencode/tmp/reach/wiring-net48.tsv \
      --baseline scripts/publicapi-unmapped-baseline.txt

Exit 0 = no new core-type public API lost its mapping arm.
Exit 1 = at least one core-type public API is newly unmapped (prints them).
"""
import argparse
import io
import sys

# The types a program names directly. Kept in sync with the baseline file's
# owner set; a new core owner must be added here AND to the baseline, which is
# the deliberate act of widening the gate.
CORE_OWNERS = frozenset([
    "System.Array", "System.String", "System.Object",
    "System.Int32", "System.Int64", "System.Int16", "System.Byte",
    "System.SByte", "System.UInt16", "System.UInt32", "System.UInt64",
    "System.Double", "System.Single", "System.Boolean", "System.Char",
    "System.Decimal", "System.Enum", "System.Convert", "System.Math",
    "System.Console", "System.Exception", "System.DateTime", "System.TimeSpan",
    "System.Guid", "System.Text.StringBuilder", "System.Text.Encoding",
    "System.IO.File", "System.IO.Path", "System.IO.Stream",
    "System.IO.FileStream", "System.IO.TextWriter", "System.IO.TextReader",
    "System.Collections.Generic.List`1",
    "System.Collections.Generic.Dictionary`2",
    "System.Collections.Generic.HashSet`1",
    "System.Collections.Generic.Queue`1",
    "System.Collections.Generic.Stack`1",
    "System.Collections.ArrayList", "System.Collections.Hashtable",
    "System.Nullable`1", "System.Tuple", "System.ValueTuple", "System.Random",
    "System.Environment", "System.GC", "System.Type", "System.Activator",
    "System.Threading.Thread", "System.Threading.Monitor",
    "System.Threading.Interlocked",
])


def load_baseline(path):
    keys = set()
    for line in io.open(path, encoding="utf-8"):
        # Strip BOTH terminators: a baseline edited on Windows carries CRLF,
        # and a trailing \r would make every key differ from the probe's.
        line = line.rstrip("\r\n")
        if not line.strip():
            continue
        parts = line.split("\t")
        if len(parts) < 3:
            raise SystemExit(
                "baseline line is not owner<TAB>name<TAB>sig: %r" % line)
        keys.add((parts[0], parts[1], parts[2]))
    return keys


def load_resolved_core(path):
    keys = set()
    for line in io.open(path, encoding="utf-8"):
        p = line.rstrip("\r\n").split("\t")
        if len(p) < 5:
            continue
        if p[3] != "publicapi" or p[4] != "resolved":
            continue
        if p[0] not in CORE_OWNERS:
            continue
        keys.add((p[0], p[1], p[2]))
    return keys


def load_unmapped_core(path):
    keys = set()
    for line in io.open(path, encoding="utf-8"):
        p = line.rstrip("\r\n").split("\t")
        if len(p) < 5:
            continue
        if p[3] != "publicapi" or p[4] != "unmapped":
            continue
        if p[0] not in CORE_OWNERS:
            continue
        keys.add((p[0], p[1], p[2]))
    return keys


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--wiring", required=True,
                    help="IcallWiringProbe output TSV")
    ap.add_argument("--baseline", required=True,
                    help="the pinned core-type unmapped set")
    ap.add_argument("--resolved-baseline", required=True,
                    help="the pinned core-type resolved set; a row leaving it "
                         "is a regression (a mapping arm was removed)")
    args = ap.parse_args(argv)

    baseline = load_baseline(args.baseline)
    resolved_baseline = load_baseline(args.resolved_baseline)
    current = load_unmapped_core(args.wiring)
    current_resolved = load_resolved_core(args.wiring)

    if not current and not baseline:
        print("both the baseline and the probe output are empty -- "
              "the probe did not run or the owner list is wrong")
        return 1
    if not resolved_baseline:
        print("the resolved baseline is empty -- it must list the core-type "
              "public APIs that DO have a mapping arm, or a removed arm is "
              "invisible")
        return 1

    new_holes = sorted(current - baseline)
    closed = sorted(baseline - current)
    regressed = sorted(resolved_baseline - current_resolved)
    gained = sorted(current_resolved - resolved_baseline)

    print("unmapped: baseline=%d current=%d closed=%d new=%d"
          % (len(baseline), len(current), len(closed), len(new_holes)))
    print("resolved: baseline=%d current=%d regressed=%d gained=%d"
          % (len(resolved_baseline), len(current_resolved),
             len(regressed), len(gained)))
    for owner, name, sig in closed:
        print("  CLOSED     %s::%s  %s" % (owner, name, sig))
    for owner, name, sig in gained:
        print("  GAINED     %s::%s  %s" % (owner, name, sig))
    for owner, name, sig in new_holes:
        print("  NEW        %s::%s  %s" % (owner, name, sig))
    for owner, name, sig in regressed:
        print("  REGRESSED  %s::%s  %s" % (owner, name, sig))

    if new_holes or regressed:
        if new_holes:
            print("VERDICT: %d core-type public API(s) newly unmapped -- a new "
                  "public method appeared without a mapping arm"
                  % len(new_holes))
        if regressed:
            print("VERDICT: %d core-type public API(s) lost their mapping arm "
                  "-- a resolved row is unmapped again" % len(regressed))
        return 1
    print("VERDICT: no core-type public API lost its mapping arm")
    return 0


if __name__ == "__main__":
    sys.exit(main())
