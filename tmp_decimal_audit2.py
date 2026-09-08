import pathlib, re
# Check decimal_format layer
def names(path):
    t = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    return sorted(set(re.findall(r'^\s*(?:int32_t|int64_t|uint32_t|uint64_t|double|float|void|bool|Il2CppString\*)\s+(icall_decimal_\w+)\s*\(', t, flags=re.MULTILINE)))

hdr_fmt = names(r"E:\il2cpp\src\runtime\include\internal\decimal_format.h")
print("HDR fmt count", len(hdr_fmt), hdr_fmt)
defs_fmt = names(r"E:\il2cpp\src\runtime\src\decimal_format.cpp")
print("DEF fmt count", len(defs_fmt), defs_fmt)
# Also check raw regex for any icall_decimal in those files
t1 = pathlib.Path(r"E:\il2cpp\src\runtime\include\internal\decimal_format.h").read_text(encoding='utf-8')
t2 = pathlib.Path(r"E:\il2cpp\src\runtime\src\decimal_format.cpp").read_text(encoding='utf-8')
print("raw fmt.h", sorted(set(re.findall(r'icall_decimal_\w+', t1))))
print("raw fmt.cpp", sorted(set(re.findall(r'icall_decimal_\w+', t2))))
# Also check MethodMap.Decimal includes those? Already did.
# Now total runtime icalls across both headers
hdr_all = sorted(set(names(r"E:\il2cpp\src\runtime\include\internal\decimal.h") + names(r"E:\il2cpp\src\runtime\include\internal\decimal_format.h")))
print("HDR ALL", len(hdr_all), hdr_all)
# And all defs
def_all = sorted(set(names(r"E:\il2cpp\src\runtime\src\decimal.cpp") + names(r"E:\il2cpp\src\runtime\src\decimal_format.cpp")))
print("DEF ALL", len(def_all), def_all)
print("HDR_ALL not DEF_ALL", sorted(set(hdr_all)-set(def_all)))
print("DEF_ALL not HDR_ALL", sorted(set(def_all)-set(hdr_all)))
# Map already 30
txt = pathlib.Path(r"E:\il2cpp\src\il2cppc\Emit\MethodMap.Decimal.cs").read_text(encoding='utf-8')
ics = re.findall(r'"(icall_decimal_[^"]+)"', txt)
pref = re.findall(r'il2cpp::runtime::(icall_decimal_\w+)', txt)
all_map = sorted(set(ics + pref))
print("MAP", len(all_map), all_map)
print("MAP not HDR_ALL", sorted(set(all_map)-set(hdr_all)))
print("HDR_ALL not MAP", sorted(set(hdr_all)-set(all_map)))
print("MAP not DEF_ALL", sorted(set(all_map)-set(def_all)))
print("DEF_ALL not MAP", sorted(set(def_all)-set(all_map)))
# Check which hdr entries correspond to which file
for n in hdr_all:
    in_h = n in names(r"E:\il2cpp\src\runtime\include\internal\decimal.h")
    in_f = n in names(r"E:\il2cpp\src\runtime\include\internal\decimal_format.h")
    print(n, "h=" , in_h, "fmt=", in_f)
