import pathlib, re

# Let's focus on the three-layer audit summary
# Layer1: MethodMap.Decimal (30 icalls) + MethodMap.Format (2 decimal custom) actually belong to decimal domain
# But by file: MethodMap.Decimal.cs = 30, MethodMap.Format.cs handles decimal's custom format via format domain

# So for decimal domain total Map should be:
# Decimal.cs 30 + Format.cs decimal branch 2 = 32 distinct icall names? But overlap: ToString etc not overlap
# Let's compute distinct

txtD = pathlib.Path(r"E:\il2cpp\src\il2cppc\Emit\MethodMap.Decimal.cs").read_text(encoding='utf-8')
icsD = re.findall(r'"(icall_decimal_[^"]+)"', txtD)
prefD = re.findall(r'il2cpp::runtime::(icall_decimal_\w+)', txtD)
mapD = sorted(set(icsD + prefD))

txtF = pathlib.Path(r"E:\il2cpp\src\il2cppc\Emit\MethodMap.Format.cs").read_text(encoding='utf-8')
icsF = re.findall(r'"(icall_decimal_[^"]+)"', txtF)
prefF = re.findall(r'il2cpp::runtime::(icall_decimal_\w+)', txtF)
mapF = sorted(set(icsF + prefF))

print("Map Decimal file", len(mapD), mapD)
print("Map Format file decimal subset", len(mapF), mapF)
map_total = sorted(set(mapD) | set(mapF))
print("Map total decimal domain", len(map_total), map_total)

# Runtime headers
def names(path):
    t = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    return sorted(set(re.findall(r'^\s*(?:int32_t|int64_t|uint32_t|uint64_t|double|float|void|bool|Il2CppString\*)\s+(icall_decimal_\w+)\s*\(', t, flags=re.MULTILINE)))

hdr_dec = names(r"E:\il2cpp\src\runtime\include\internal\decimal.h")
hdr_fmt = names(r"E:\il2cpp\src\runtime\include\internal\decimal_format.h")
hdr_all = sorted(set(hdr_dec+hdr_fmt))
print("HDR dec", len(hdr_dec), hdr_dec)
print("HDR fmt", len(hdr_fmt), hdr_fmt)
print("HDR all", len(hdr_all))

def names_cpp(path):
    t = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    return sorted(set(re.findall(r'^\s*(?:int32_t|int64_t|uint32_t|uint64_t|double|float|void|bool|Il2CppString\*)\s+(icall_decimal_\w+)\s*\(', t, flags=re.MULTILINE)))

def_dec = names_cpp(r"E:\il2cpp\src\runtime\src\decimal.cpp")
def_fmt = names_cpp(r"E:\il2cpp\src\runtime\src\decimal_format.cpp")
def_all = sorted(set(def_dec+def_fmt))
print("DEF dec", len(def_dec))
print("DEF fmt", len(def_fmt))
print("DEF all", len(def_all), def_all)

# Cross
print("HDR not DEF", sorted(set(hdr_all)-set(def_all)))
print("DEF not HDR", sorted(set(def_all)-set(hdr_all)))
print("MAP total not HDR", sorted(set(map_total)-set(hdr_all)))
print("HDR not MAP total", sorted(set(hdr_all)-set(map_total)))
print("MAP total not DEF", sorted(set(map_total)-set(def_all)))
print("DEF not MAP total", sorted(set(def_all)-set(map_total)))

# Now check stubs.h / stubs.cpp for fcall etc
hdr_stubs = names(r"E:\il2cpp\src\runtime\include\internal\stubs.h")
def_stubs = names_cpp(r"E:\il2cpp\src\runtime\src\stubs.cpp")
print("HDR stubs decimal", [x for x in hdr_stubs if "decimal" in x])
print("DEF stubs decimal", [x for x in def_stubs if "decimal" in x])
# But fcall is in stubs, not in decimal headers - is that considered runtime? It's still runtime
# So if we count runtime as decimal.h+decimal_format.h only, fcall appears as gap. But if we count stubs.h too, it's covered.

# Let's also check all runtime headers for any icall_decimal
import pathlib as pl
all_hdrs = []
for p in pl.Path(r"E:\il2cpp\src\runtime\include").rglob("*.h"):
    txt = p.read_text(encoding='utf-8', errors='ignore')
    found = re.findall(r'^\s*(?:int32_t|int64_t|uint32_t|uint64_t|double|float|void|bool|Il2CppString\*)\s+(icall_decimal_\w+)\s*\(', txt, flags=re.MULTILINE)
    if found:
        for f in found:
            all_hdrs.append((str(p), f))
print("ALL headers decimal decls")
for p,f in sorted(set(all_hdrs)):
    print(p, f)

all_defs = []
for p in pl.Path(r"E:\il2cpp\src\runtime\src").rglob("*.cpp"):
    txt = p.read_text(encoding='utf-8', errors='ignore')
    found = re.findall(r'^\s*(?:int32_t|int64_t|uint32_t|uint64_t|double|float|void|bool|Il2CppString\*)\s+(icall_decimal_\w+)\s*\(', txt, flags=re.MULTILINE)
    if found:
        for f in found:
            all_defs.append((str(p), f))
print("ALL cpp decimal defs")
for p,f in sorted(set(all_defs)):
    print(p, f)
