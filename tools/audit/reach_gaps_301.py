import io
import json
import collections

ROOT = "F:/il2cpp"
graph = json.load(open(ROOT + "/.opencode/tmp/reach/corelib-callgraph.json", encoding="utf-8"))
recut = json.load(open(ROOT + "/.opencode/tmp/reach/shipped-recut.json", encoding="utf-8"))

# Forward edges: caller -> callees. publicFaces are the entry points user code can
# actually name, so a gap is reachable iff a publicFace reaches it by some path.
adj = collections.defaultdict(set)
for c_owner, c_name, c_sig, t_owner, t_name, t_sig in graph["edges"]:
    adj[(c_owner, c_name, c_sig)].add((t_owner, t_name, t_sig))

faces = set(tuple(f) for f in graph["publicFaces"])

# Multi-source BFS from every public face; hop count = distance to the gap.
dist = {}
q = collections.deque()
for f in faces:
    if f in dist:
        continue
    dist[f] = 0
    q.append(f)
while q:
    cur = q.popleft()
    d = dist[cur] + 1
    for nxt in adj.get(cur, ()):
        if nxt not in dist:
            dist[nxt] = d
            q.append(nxt)

print("faces", len(faces), "reachable_nodes", len(dist))


def normalise(n):
    import re
    m = re.match(r"^<([^>]+)>g____PInvoke\|\d+_\d+$", n)
    return m.group(1) if m else n


# Rebuild the 301 unwired rows with the same two-step filter recut uses.
def reach_class(typeAccess, methodAccess):
    if typeAccess != "public":
        return "INTERNAL-TYPE"
    if methodAccess in ("public", "family", "familyorassembly"):
        return "USER-CALLABLE"
    return "INNER-ONLY"


def load_qcall():
    rows = []
    for line in open(ROOT + "/.opencode/tmp/reach/qcall-8031.tsv", encoding="utf-8"):
        p = line.rstrip("\r\n").split("\t")
        if len(p) < 4:
            continue
        acc = p[3].split("/")
        rows.append({
            "table": "QCall", "owner": p[0].replace("+", "."), "name": normalise(p[1]),
            "typeAccess": acc[0] if len(acc) > 0 else "",
            "methodAccess": acc[1] if len(acc) > 1 else "",
        })
    return rows


def load_fcall():
    rows = []
    for line in open(ROOT + "/.opencode/tmp/reach/fcall-8031.tsv", encoding="utf-8"):
        p = line.rstrip("\r\n").split("\t")
        if len(p) < 5:
            continue
        # fcall-8031.tsv packs type/method/static into ONE slash-joined column
        # (col3), and col4 is the member KIND ("method"/"ctor"), not the access.
        acc = p[3].split("/")
        rows.append({
            "table": "FCall", "owner": p[0].replace("+", "."), "name": normalise(p[1]),
            "typeAccess": acc[0] if len(acc) > 0 else "",
            "methodAccess": acc[1] if len(acc) > 1 else "",
        })
    return rows


def wired_set(path, kinds):
    s = set()
    for line in open(path, encoding="utf-8"):
        p = line.rstrip("\r\n").split("\t")
        if len(p) < 5 or p[3] not in kinds:
            continue
        if p[4] in ("resolved", "loud"):
            s.add((p[0].replace("+", "."), normalise(p[1])))
    return s


wired = wired_set(ROOT + "/.opencode/tmp/reach/wiring-final.tsv", ("icall", "qcall"))

rows = load_fcall() + load_qcall()

# The join key is (owner, name) because the wiring TSV and the metadata TSVs
# cannot be joined on the signature (qcall-8031.tsv column 3 is the ENTRY name,
# not the signature). That key is only sound while no (owner, name) has
# overloads that straddle the wired/unwired boundary -- otherwise one overload
# being wired would hide the other being unwired. Assert it instead of
# assuming it: the shipped FCall table has 6 duplicate keys today and all 6
# are uniformly wired or uniformly unwired, which is why the looser key gives
# the right answer. A future table where that stops holding must fail here
# rather than silently undercount.
# The check has to run on the WIRING rows, not on `rows`: `rows` carries no
# signature, so every overload of one (owner, name) would look identical and
# the test could never fire (a vacuous assertion). The wiring TSV does carry
# the signature, so group ITS rows by (owner, name) and look for a key whose
# overloads disagree on resolved/loud vs unmapped. That is exactly the shape
# that would make the (owner, name) join undercount.
_wired_rows = collections.defaultdict(list)
for _line in open(ROOT + "/.opencode/tmp/reach/wiring-final.tsv", encoding="utf-8"):
    _p = _line.rstrip("\r\n").split("\t")
    if len(_p) < 5 or _p[3] not in ("icall", "qcall"):
        continue
    _k = (_p[0].replace("+", "."), normalise(_p[1]))
    _wired_rows[_k].append(_p[4] in ("resolved", "loud"))
_mixed = [k for k, v in _wired_rows.items() if len(v) > 1 and len(set(v)) > 1]
if _mixed:
    raise SystemExit(
        "%d (owner, name) keys have overloads straddling the wired/unwired "
        "boundary; the (owner, name) join would undercount the gap. Join on "
        "the signature instead. First: %r" % (len(_mixed), _mixed[0]))

unwired = [r for r in rows if (r["owner"], r["name"]) not in wired]
print("unwired_total", len(unwired))

# An icall is reachable when ANY caller from the call graph reaches it. The graph
# keys by (owner, name, sig) while the gap rows only carry (owner, name), so look
# up every graph node with that owner+name.
by_owner_name = collections.defaultdict(list)
for ic in graph["icalls"]:
    by_owner_name[(ic["owner"], ic["name"])].append(ic)

stats = collections.Counter()
detail = []
for r in unwired:
    key = (r["owner"], r["name"])
    nodes = by_owner_name.get(key, [])
    best = None
    if nodes:
        ds = [dist.get((ic["owner"], ic["name"], ic["sig"])) for ic in nodes]
        ds = [d for d in ds if d is not None]
        if ds:
            best = min(ds)
    if best is None:
        bucket = "NOT-IN-GRAPH" if not nodes else "UNREACHED"
    elif best == 1:
        bucket = "DIRECT"
    else:
        bucket = "SHADOW"
    stats[(r["table"], bucket)] += 1
    detail.append({**r, "reach": reach_class(r["typeAccess"], r["methodAccess"]),
                   "bucket": bucket, "hops": best})

print("\n=== by table x reachability ===")
for t in ("FCall", "QCall"):
    for b in ("DIRECT", "SHADOW", "UNREACHED", "NOT-IN-GRAPH"):
        n = stats[(t, b)]
        if n:
            print("  %-6s %-13s %d" % (t, b, n))

print("\n=== by table x access x reachable ===")
reachable = [x for x in detail if x["bucket"] in ("DIRECT", "SHADOW")]
print("reachable_gaps", len(reachable))
c = collections.Counter((x["table"], x["reach"]) for x in reachable)
for k, v in sorted(c.items()):
    print("  %-6s %-14s %d" % (k[0], k[1], v))

print("\n=== reachable INNER/INTERNAL samples ===")
for x in reachable[:12]:
    print("  %-6s %-13s %-16s %s::%s hops=%s"
          % (x["table"], x["bucket"], x["reach"], x["owner"], x["name"], x["hops"]))

io.open(ROOT + "/.opencode/tmp/reach/gap-reach-301.json", "w", encoding="utf-8", newline="").write(
    json.dumps({"total": len(unwired), "detail": detail}, ensure_ascii=False, indent=1))
print("\nwrote gap-reach-301.json")
