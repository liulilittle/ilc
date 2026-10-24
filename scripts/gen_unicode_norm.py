#!/usr/bin/env python3
#------------------------------------------------------------------------------
# gen_unicode_norm.py -- generate the complete Unicode normalization tables used
# by the il2cpp runtime (src/runtime/src/stringnormalize.cpp).
#
# The tables are generated from the Unicode Character Database (UCD), covering
# every BMP and astral codepoint -- no sample data is hardcoded:
#
#   * the decomposition table  (canonical + compatibility)
#   * the canonical combining-class table
#   * the canonical composition table
#
# Two data sources are supported:
#
#   1. --ucd-dir <dir>: read UnicodeData.txt and CompositionExclusions.txt from
#      <dir>. This pins the table to an EXACT Unicode version, which is how the
#      runtime matches the net48 NLS normalization baseline (net48 on the
#      reference host is Unicode 12.0.0 -- see the header of the generated
#      file for the exact evidence).
#
#   2. no --ucd-dir: use the Python standard library's `unicodedata` module,
#      which ships a full versioned copy of the UCD.
#
# Composition table derivation:
#   * UCD path: for every codepoint C whose canonical decomposition has exactly
#     two characters (A, B), (A, B) -> C is a composition UNLESS C is a
#     singleton decomposition, a non-starter decomposition (ccc(A) != 0) or is
#     listed in CompositionExclusions.txt (the UAX #15 primary exclusion list).
#   * unicodedata path: empirical -- NFC(A + B) == C. This automatically applies
#     the exclusion rules without a separate list.
#
# Usage:
#   python scripts/gen_unicode_norm.py [--ucd-dir DIR] <data.h> <data.cpp>
#------------------------------------------------------------------------------
import argparse
import os
import sys
import unicodedata


def parse_ucd(ucd_dir, version):
    """Parse UnicodeData.txt + CompositionExclusions.txt from a UCD directory.

    Returns (version, decomp, ccc, excluded) where decomp is a list of
    (cp, is_compat, [units]) and ccc is a list of (cp, ccc).
    """
    ud_path = os.path.join(ucd_dir, 'UnicodeData.txt')
    ce_path = os.path.join(ucd_dir, 'CompositionExclusions.txt')
    with open(ud_path, encoding='utf-8') as f:
        lines = f.readlines()

    decomp = []
    ccc = []
    for line in lines:
        p = line.rstrip('\n').split(';')
        if len(p) < 5:
            continue
        cp = int(p[0], 16)
        if 0xD800 <= cp <= 0xDFFF:
            continue
        d = p[5]
        if d:
            parts = d.split()
            is_compat = 0
            if parts[0].startswith('<'):
                is_compat = 1
                parts = parts[1:]
            decomp.append((cp, is_compat, [int(x, 16) for x in parts]))
        c = int(p[3], 10)
        if c:
            ccc.append((cp, c))

    excluded = set()
    if os.path.exists(ce_path):
        with open(ce_path, encoding='utf-8') as f:
            for line in f:
                line = line.split('#', 1)[0].strip()
                if not line:
                    continue
                excluded.add(int(line.split()[0], 16))
    return version, decomp, ccc, excluded


def composition_from_ucd(decomp, ccc, excluded):
    ccc_map = {cp: c for cp, c in ccc}
    comp = []
    for cp, is_compat, units in decomp:
        if is_compat or len(units) != 2:
            continue
        a, b = units
        if ccc_map.get(a, 0) != 0:      # non-starter decomposition
            continue
        if cp in excluded:              # UAX #15 primary exclusion list
            continue
        comp.append((a, b, cp))
    comp.sort(key=lambda e: ((e[0] << 21) | e[1]))
    return comp


def composition_from_unicodedata(decomp):
    comp = []
    for cp, is_compat, units in decomp:
        if is_compat or len(units) != 2:
            continue
        a, b = units
        if unicodedata.normalize('NFC', chr(a) + chr(b)) == chr(cp):
            comp.append((a, b, cp))
    comp.sort(key=lambda e: ((e[0] << 21) | e[1]))
    return comp


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('data_h')
    ap.add_argument('data_cpp')
    ap.add_argument('--ucd-dir', default=None)
    ap.add_argument('--ucd-version', default=None,
                    help='Unicode version label for the --ucd-dir source '
                         '(e.g. 12.0.0); defaults to the directory name')
    args = ap.parse_args()

    if args.ucd_dir:
        version = args.ucd_version or os.path.basename(os.path.normpath(args.ucd_dir))
        _, decomp, ccc, excluded = parse_ucd(args.ucd_dir, version)
        comp = composition_from_ucd(decomp, ccc, excluded)
        source = 'the Unicode Character Database (UnicodeData.txt + ' \
                 'CompositionExclusions.txt), Unicode %s' % version
    else:
        version = unicodedata.unidata_version
        decomp = []
        ccc = []
        for cp in range(0x110000):
            if 0xD800 <= cp <= 0xDFFF:
                continue
            ch = chr(cp)
            d = unicodedata.decomposition(ch)
            if d:
                parts = d.split()
                is_compat = 0
                if parts[0].startswith('<'):
                    is_compat = 1
                    parts = parts[1:]
                decomp.append((cp, is_compat, [int(p, 16) for p in parts]))
            c = unicodedata.combining(ch)
            if c:
                ccc.append((cp, c))
        comp = composition_from_unicodedata(decomp)
        source = 'the Python standard-library `unicodedata` module, Unicode data version %s' % version

    # --- emit -----------------------------------------------------------------
    lines_h = []
    lines_h.append('#pragma once')
    lines_h.append('//------------------------------------------------------------------------------')
    lines_h.append('// il2cpp_unicode_norm_data.h -- GENERATED FILE. DO NOT EDIT BY HAND.')
    lines_h.append('//')
    lines_h.append('// Complete Unicode normalization tables for the System.String')
    lines_h.append('// Normalize / IsNormalized runtime implementation')
    lines_h.append('// (src/runtime/src/stringnormalize.cpp).')
    lines_h.append('//')
    lines_h.append('// Source: %s' % source)
    lines_h.append('// (full UnicodeData.txt coverage of the BMP + all astral planes).')
    lines_h.append('// Generated by scripts/gen_unicode_norm.py.')
    lines_h.append('//')
    lines_h.append('// Layout:')
    lines_h.append('//   Il2CppNormDecomp  { cp, units_offset, count, is_compat } -- one entry')
    lines_h.append('//                       per codepoint carrying a decomposition, sorted by cp.')
    lines_h.append('//   g_norm_units      flat codepoint payload for the decompositions.')
    lines_h.append('//   Il2CppNormCcc     { cp, ccc } -- one entry per nonzero canonical')
    lines_h.append('//                       combining class, sorted by cp.')
    lines_h.append('//   Il2CppNormComp    { key, composed } -- key = (first << 21) | second,')
    lines_h.append('//                       sorted by key; the canonical composition table.')
    lines_h.append('//')
    lines_h.append('// The payload arrays are defined in')
    lines_h.append('// src/runtime/src/il2cpp_unicode_norm_data.cpp; this header declares')
    lines_h.append('// them extern with their exact extents so the normalization')
    lines_h.append('// implementation can size them with sizeof without a second copy.')
    lines_h.append('//------------------------------------------------------------------------------')
    lines_h.append('')
    lines_h.append('#include <cstdint>')
    lines_h.append('')
    lines_h.append('namespace il2cpp {')
    lines_h.append('namespace runtime {')
    lines_h.append('namespace normdata {')
    lines_h.append('')
    lines_h.append('struct Il2CppNormDecomp { uint32_t cp; uint32_t offset; uint8_t count; uint8_t is_compat; uint8_t pad[2]; };')
    lines_h.append('struct Il2CppNormCcc { uint32_t cp; uint8_t ccc; uint8_t pad[3]; };')
    lines_h.append('struct Il2CppNormComp { uint64_t key; uint32_t composed; };')
    lines_h.append('')
    lines_h.append('extern const Il2CppNormDecomp g_norm_decomp[%d];' % len(decomp))
    lines_h.append('extern const uint32_t       g_norm_units[%d];' % sum(len(us) for _, _, us in decomp))
    lines_h.append('extern const Il2CppNormCcc    g_norm_ccc[%d];' % len(ccc))
    lines_h.append('extern const Il2CppNormComp   g_norm_comp[%d];' % len(comp))
    lines_h.append('')
    lines_h.append('} // namespace normdata')
    lines_h.append('} // namespace runtime')
    lines_h.append('} // namespace il2cpp')
    lines_h.append('')

    with open(args.data_h, 'w', encoding='ascii', newline='\n') as f:
        f.write('\n'.join(lines_h))

    out = []
    out.append('//------------------------------------------------------------------------------')
    out.append('// GENERATED FILE (scripts/gen_unicode_norm.py) -- %s.' % version)
    out.append('// See include/internal/il2cpp_unicode_norm_data.h for the layout.')
    out.append('//------------------------------------------------------------------------------')
    out.append('#include "il2cpp_unicode_norm_data.h"')
    out.append('')
    out.append('namespace il2cpp {')
    out.append('namespace runtime {')
    out.append('namespace normdata {')
    out.append('')

    units = []
    out.append('const Il2CppNormDecomp g_norm_decomp[] = {')
    for cp, is_compat, us in decomp:
        offset = len(units)
        units.extend(us)
        out.append('    {0x%X,%d,%d,%d,{0,0}},' % (cp, offset, len(us), is_compat))
    out.append('};')
    out.append('')
    out.append('const uint32_t g_norm_units[] = {')
    for i in range(0, len(units), 16):
        out.append('    ' + ','.join('0x%X' % u for u in units[i:i + 16]) + ',')
    out.append('};')
    out.append('')

    out.append('const Il2CppNormCcc g_norm_ccc[] = {')
    for cp, c in ccc:
        out.append('    {0x%X,%d,{0,0,0}},' % (cp, c))
    out.append('};')
    out.append('')

    out.append('const Il2CppNormComp g_norm_comp[] = {')
    for a, b, c in comp:
        out.append('    {0x%X,0x%X},' % (((a << 21) | b), c))
    out.append('};')
    out.append('')
    out.append('} // namespace normdata')
    out.append('} // namespace runtime')
    out.append('} // namespace il2cpp')
    out.append('')

    with open(args.data_cpp, 'w', encoding='ascii', newline='\n') as f:
        f.write('\n'.join(out))

    sys.stderr.write(
        'gen_unicode_norm: source=%s decomp=%d ccc=%d comp=%d units=%d\n'
        % (version, len(decomp), len(ccc), len(comp), len(units)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
