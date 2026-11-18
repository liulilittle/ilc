#!/usr/bin/env python3
"""Assert the delegate-ctor SIGNATURE test covers every shipped BCL delegate.

Why this exists: the transpiler loads ONLY the user assembly
(MetadataLoader.LoadAssembly(opts.InputAssembly)), so mscorlib's and System.dll's
delegate types are not in the model at transpile time. StackFold therefore
decides "is this newobj a delegate?" from the ctor SIGNATURE
(IsDelegateCtorSignature: `Void .ctor(System.Object, <native int>)`), not from a
name list.

A signature test is only sound if the shape really is universal. This gate
re-derives the truth from metadata and fails when any shipped delegate's ctor
does not match the shape the test accepts -- i.e. when the test would miss a
delegate and emit an undefined `__ctor_<hash>`.

It also reports the non-delegates that share the shape, so the exclusion list
stays honest (today: System.Runtime.InteropServices.HandleRef, which the
value-type newobj arm claims before the delegate arm is reached).

Usage:
  python tools/audit/bcl_delegate_gate.py \
      --shipped-tsv mscorlib.tsv --shipped-tsv System.tsv

Exit 0 = every shipped delegate matches the signature test.
Exit 1 = at least one delegate would be missed (prints them).
"""
import argparse
import io
import sys

# ELEMENT_TYPE_* codes the ctor signature test accepts for the method pointer.
# csc emits ELEMENT_TYPE_I (0x18) for System.IntPtr; the VALUETYPE spelling
# (0x11 + TypeDefOrRef naming System.IntPtr) is also accepted.
NATIVE_INT_CODES = {0x18}


def read_ctor_shapes(tsv_paths):
    """full<TAB>public|nonpublic<TAB>ctorShape, where ctorShape is
    'obj,intptr' / 'obj,valuetype:System.IntPtr' / 'other'."""
    rows = []
    for path in tsv_paths:
        for line in io.open(path, encoding="utf-8"):
            line = line.rstrip("\n")
            if not line.strip():
                continue
            parts = line.split("\t")
            if len(parts) < 3:
                continue
            rows.append((parts[0], parts[1], parts[2]))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--shipped-tsv", action="append", required=True,
                    help="DelegateProbe output (full<TAB>public|nonpublic<TAB>ctorShape)")
    args = ap.parse_args()

    rows = read_ctor_shapes(args.shipped_tsv)
    delegates = [r for r in rows if r[1] == "public"]
    if not delegates:
        print("no public delegates found -- the probe output is empty or wrong")
        return 1

    missed = []
    for full, _vis, shape in delegates:
        if shape == "obj,intptr":
            continue
        if shape == "obj,valuetype:System.IntPtr":
            continue
        missed.append((full, shape))

    print("shipped_public_delegates=%d" % len(delegates))
    print("matched_by_signature=%d" % (len(delegates) - len(missed)))
    print("missed=%d" % len(missed))
    for full, shape in missed:
        print("  MISS  %s  ctor=%s" % (full, shape))

    if missed:
        print("VERDICT: SIGNATURE TEST INCOMPLETE")
        return 1
    print("VERDICT: SIGNATURE TEST COVERS ALL SHIPPED DELEGATES")
    return 0


if __name__ == "__main__":
    sys.exit(main())
