"""Stage-1 (P0) closure analysis over the classified P/Invoke rows.

Reads the classified TSV (from classify_winforms_pinvokes.py) and the
reachability TSV (from WinFormsPinvokeGraph reachable.tsv) and answers two
questions the audit document must answer with numbers, not prose:

  1. Which P/Invokes does the stage-1 control path actually reach, and what is
     the shortest call-graph chain from a stage-1 root to each of them?
  2. Given N marshal capabilities, how many of those reachable P/Invokes are
     fully serviced? The greedy order is the recommended wiring order.

usage: python tools/audit/p0_closure.py <classified.tsv> <reachable.tsv> [capacities]
"""

import csv
import io
import sys
from collections import Counter, defaultdict

ALL_CAPS = ['handleref', 'entrypoint-suffix', 'byref-scalar', 'array',
            'stringbuilder', 'struct-return', 'name-clash', 'byref-other', 'object',
            'entrypoint-missing', 'nonblittable-struct']


def needs_of(row):
    return {n for n in row['needs'].split('+') if n}


def main():
    classified_path, reachable_path = sys.argv[1], sys.argv[2]
    rows = list(csv.DictReader(io.open(classified_path, encoding='utf-8-sig', newline=''),
                               delimiter='\t'))
    reach = {}
    for r in csv.DictReader(io.open(reachable_path, encoding='utf-8', newline=''),
                            delimiter='\t'):
        key = (r['module'], r['entrypoint'])
        d = int(r['distance'])
        reach.setdefault(key, []).append((d, r['root'], r['chain']))

    print('## 1. reachable set')
    print('unique (module, entrypoint) reachable from the stage-1 roots: %d' % len(reach))
    reach_rows = [r for r in rows if (r['module'], r['entrypoint']) in reach]
    print('declarations covering them: %d' % len(reach_rows))
    print('class split: %s' % dict(Counter(r['klass'] for r in reach_rows)))
    print()

    print('## 2. dependency depth of the reachable set')
    first = {}
    for key, lst in reach.items():
        first[key] = min(lst, key=lambda t: t[0])
    depth = Counter(v[0] for v in first.values())
    for d in sorted(depth):
        print('  depth %2d: %d entry points' % (d, depth[d]))
    print()

    print('## 3. nearest P/Invokes (the ones a stage-1 fix must land first)')
    for key in sorted(first, key=lambda k: (first[k][0], k[0], k[1])):
        d, root, chain = first[key]
        if d > 2:
            break
        cls = sorted({r['klass'] for r in reach_rows if (r['module'], r['entrypoint']) == key})
        print('  d=%d %-16s %-30s %s  via %s' % (d, key[0], key[1], '/'.join(cls), root))
    print()

    print('## 4. greedy capability wiring order (stage-1 closure)')
    caps = list(ALL_CAPS)
    have = set()
    # baseline: A rows need nothing; E rows are runtime-implemented (free).
    def closed(caps_set):
        n = 0
        for r in reach_rows:
            if r['klass'] in ('A', 'E'):
                n += 1
            elif r['klass'] == 'B' and needs_of(r) <= caps_set:
                n += 1
        return n
    cur = closed(have)
    print('  baseline (no new capability): %d / %d' % (cur, len(reach_rows)))
    while True:
        best = None
        for cap in caps:
            if cap in have:
                continue
            n = closed(have | {cap})
            if best is None or n > best[1]:
                best = (cap, n)
        if best is None or best[1] <= cur:
            break
        have.add(best[0])
        cur = best[1]
        print('  +%-17s -> %d / %d' % (best[0], cur, len(reach_rows)))
    print('  uncovered after all capabilities: %d' % (len(reach_rows) - cur))
    for r in reach_rows:
        if r['klass'] == 'B' and not needs_of(r) <= have:
            print('    still blocked: %-16s %-28s needs=%s' % (r['module'], r['entrypoint'], r['needs']))
    print('  (C/D rows are excluded from the closure: they need the COM runtime or are out of scope.)')


if __name__ == '__main__':
    main()
