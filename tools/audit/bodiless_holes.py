import io
import json
import collections

ROOT = "F:/il2cpp"

# A method with an IL body needs no arm: the body itself transpiles. A method
# with NO IL body has no other way to acquire a body than a MethodMap arm, so
# "bodiless + unmapped" is a real hole, while "unmapped" alone is meaningless
# for an ordinary managed method. That distinction is what makes this join
# trustworthy where the round-40 join was not.
bodiless = []
for line in io.open(ROOT + "/.opencode/tmp/reach/bodiless-public2.tsv", encoding="utf-8"):
    p = line.rstrip("\r\n").split("\t")
    if len(p) < 4:
        continue
    bodiless.append({"owner": p[0], "name": p[1], "sig": p[2], "access": p[3]})

status = {}
for line in io.open(ROOT + "/.opencode/tmp/reach/wiring-now.tsv", encoding="utf-8"):
    p = line.rstrip("\r\n").split("\t")
    if len(p) < 5:
        continue
    if p[3] not in ("publicapi", "icall", "qcall"):
        continue
    status[(p[0].replace("+", "."), p[1])] = p[4]

rows = []
for b in bodiless:
    key = (b["owner"], b["name"])
    st = status.get(key)
    rows.append({**b, "status": st or "NOT-PROBED"})

c = collections.Counter(r["status"] for r in rows)
print("bodiless_public", len(rows))
for k, v in c.most_common():
    print("  %-12s %d" % (k, v))

# Delegates are the dominant false positive: Invoke/BeginInvoke/EndInvoke have no
# IL body because the runtime supplies the call indirectly, so "bodiless and
# unmapped" says nothing about a defect. They are identified by base type, which
# the probe records as System.MulticastDelegate for every delegate type.
DELEGATE_METHODS = {"Invoke", "BeginInvoke", "EndInvoke"}

holes = [r for r in rows if r["status"] == "unmapped"
         and r["name"] not in DELEGATE_METHODS]
print("  (excluded %d delegate Invoke/Begin/End rows)"
      % sum(1 for r in rows if r["status"] == "unmapped" and r["name"] in DELEGATE_METHODS))
print("\n=== bodiless + unmapped: real hole candidates ===", len(holes))
owners = collections.Counter(r["owner"] for r in holes)
for k, v in owners.most_common(20):
    print("  %-52s %d" % (k, v))

io.open(ROOT + "/.opencode/tmp/reach/bodiless-holes.json", "w", encoding="utf-8", newline="").write(
    json.dumps(holes, ensure_ascii=False, indent=1))
print("\nwrote bodiless-holes.json")
