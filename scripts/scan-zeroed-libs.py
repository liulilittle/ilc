#!/usr/bin/env python3
"""scan-zeroed-libs.py -- detect NTFS zero-fill damaged build artifacts.

A hard reset / power loss on NTFS can leave files with their original size but
contents filled with NUL bytes.  A prebuilt static library in that state makes
the linker fail with "file format not recognized", which the sweep would record
as a spurious FAIL.  This tool scans a directory tree for such artifacts.

A file is reported as an offender when it is non-empty and its first 8 bytes
are all 0x00 (healthy archives start with "!<arch>\\n", ELF objects with
0x7f 'E' 'L' 'F', COFF/PE with 'M' 'Z' or 0x00-padded headers -- the first 8
bytes of real artifacts are never all zero).

Usage:
    python scan-zeroed-libs.py <root> [--ext .a .o .lib .obj] [--quiet]

Exit codes:
    0 = no offender
    1 = at least one offender found
    2 = usage / path error
"""
import argparse
import os
import sys

DEFAULT_EXTS = (".a", ".o", ".lib")


def is_zeroed(path):
    try:
        with open(path, "rb") as f:
            head = f.read(8)
    except OSError:
        return False
    return bool(head) and head == b"\x00" * len(head)


def main(argv=None):
    ap = argparse.ArgumentParser(description="detect zero-filled build artifacts")
    ap.add_argument("root", help="directory to scan")
    ap.add_argument("--ext", nargs="+", default=list(DEFAULT_EXTS),
                    help="file extensions to check (default: .a .o .lib)")
    ap.add_argument("--quiet", action="store_true", help="only print summary")
    args = ap.parse_args(argv)

    if not os.path.isdir(args.root):
        print("ERROR: not a directory: %s" % args.root, file=sys.stderr)
        return 2

    exts = tuple(e if e.startswith(".") else "." + e for e in args.ext)
    exts_lower = tuple(e.lower() for e in exts)
    offenders = []
    checked = 0
    for dirpath, _, filenames in os.walk(args.root):
        for name in filenames:
            if not name.lower().endswith(exts_lower):
                continue
            p = os.path.join(dirpath, name)
            checked += 1
            if is_zeroed(p):
                offenders.append(p)

    if not args.quiet:
        for p in offenders:
            print("ZEROED\t%s\tsize=%d" % (p, os.path.getsize(p)))
    print("scan-zeroed-libs: root=%s ext=%s checked=%d offenders=%d"
          % (args.root, ",".join(exts), checked, len(offenders)))
    return 1 if offenders else 0


if __name__ == "__main__":
    sys.exit(main())
