import pathlib, re

headers = {
 "socket.h": pathlib.Path(r"E:\il2cpp\src\runtime\include\internal\socket.h").read_text(encoding='utf-8'),
 "ipaddr.h": pathlib.Path(r"E:\il2cpp\src\runtime\include\internal\ipaddr.h").read_text(encoding='utf-8'),
 "http.h": pathlib.Path(r"E:\il2cpp\src\runtime\include\internal\http.h").read_text(encoding='utf-8'),
 "il2cpp_http.h": pathlib.Path(r"E:\il2cpp\src\runtime\include\internal\il2cpp_http.h").read_text(encoding='utf-8'),
}
cpp_texts = {
 "socket.cpp": pathlib.Path(r"E:\il2cpp\src\runtime\src\socket.cpp").read_text(encoding='utf-8'),
 "ipaddr.cpp": pathlib.Path(r"E:\il2cpp\src\runtime\src\ipaddr.cpp").read_text(encoding='utf-8'),
 "http.cpp": pathlib.Path(r"E:\il2cpp\src\runtime\src\http.cpp").read_text(encoding='utf-8'),
}
map_text = pathlib.Path(r"E:\il2cpp\src\il2cppc\Emit\MethodMap.Net.cs").read_text(encoding='utf-8')

def find_decls(txt):
    return re.findall(r'\b(?:il2cpp_[a-zA-Z0-9_]+|icall_[a-zA-Z0-9_]+)\s*\(', txt)

for name, txt in headers.items():
    decls = find_decls(txt)
    print(f"HDR {name}: {decls}")

for name, txt in cpp_texts.items():
    decls = find_decls(txt)
    print(f"CPP {name}: {decls} len={len(decls)}")

cpp_names = re.findall(r'I\("il2cpp::runtime::([^"]+)"', map_text)
print(f"MAP cpp_names: {cpp_names} len={len(cpp_names)}")
# LOUD socket gaps
loud_matches = re.findall(r'NotSupportedException', map_text)
print(f"LOUD NotSupportedException count: {len(loud_matches)}")
# enumerate map functions
print("Map func list detail:")
for m in cpp_names:
    print(f"  {m}")

# hdr counts
hdr_socket = re.findall(r'il2cpp_socket_\w+', headers["socket.h"])
hdr_ipaddr = re.findall(r'il2cpp_ipaddress_\w+|il2cpp_dns_\w+', headers["ipaddr.h"])
hdr_http = re.findall(r'il2cpp_http_\w+', headers["http.h"])
hdr_web = re.findall(r'il2cpp_web_client_\w+|icall_web[_\w]+', headers["il2cpp_http.h"])
print(f"hdr_socket {hdr_socket} count {len(set(hdr_socket))}")
print(f"hdr_ipaddr {hdr_ipaddr} count {len(set(hdr_ipaddr))}")
print(f"hdr_http {hdr_http} count {len(set(hdr_http))}")
print(f"hdr_web {hdr_web} count {len(set(hdr_web))}")

# cpp def checks: list unique
for k, txt in cpp_texts.items():
    uniq = sorted(set(re.findall(r'\bil2cpp_[a-zA-Z0-9_]+\b|\bicall_[a-zA-Z0-9_]+\b', txt)))
    # filter only il2cpp/ipaddr/http/socket/web
    filt = [u for u in uniq if any(x in u for x in ["socket","ipaddr","dns","http","web_client","webclient"])]
    print(f"CPP {k} uniq filt: {filt}")

# map gap analysis: hdr set vs map set
hdr_all = set()
hdr_all.update(re.findall(r'\bil2cpp_socket_\w+\b', headers["socket.h"]))
hdr_all.update(re.findall(r'\bil2cpp_ipaddress_\w+\b|\bil2cpp_dns_\w+\b', headers["ipaddr.h"]))
hdr_all.update(re.findall(r'\bil2cpp_http_\w+\b', headers["http.h"]))
hdr_all.update(re.findall(r'\bil2cpp_web_client_\w+\b|\bicall_web[_\w]+\b', headers["il2cpp_http.h"]))
# also class descriptor not a function
hdr_all.add("System_Net_WebClient_class")
print(f"HDR_ALL {sorted(hdr_all)} len {len(hdr_all)}")
map_all = set(cpp_names)
# map names are without il2cpp::runtime:: prefix, just function name; compare stripped
# hdr names are il2cpp_socket_create etc, map names are same
print(f"MAP_ALL {sorted(map_all)} len {len(map_all)}")
print(f"HDR not in MAP: {sorted(hdr_all - map_all - {'System_Net_WebClient_class'})}")
print(f"MAP not in HDR: {sorted(map_all - hdr_all)}")

# cpp all check
cpp_all = set()
for txt in cpp_texts.values():
    cpp_all.update(re.findall(r'\bil2cpp_socket_\w+\b', txt))
    cpp_all.update(re.findall(r'\bil2cpp_ipaddress_\w+\b|\bil2cpp_dns_\w+\b', txt))
    cpp_all.update(re.findall(r'\bil2cpp_http_\w+\b', txt))
    cpp_all.update(re.findall(r'\bil2cpp_web_client_\w+\b|\bicall_web[_\w]+\b', txt))
# also need to include System_Net_WebClient_class definition in http.cpp
cpp_all.add("System_Net_WebClient_class")
print(f"CPP_ALL {sorted(cpp_all)} len {len(cpp_all)}")
print(f"HDR vs CPP gap: hdr not cpp {sorted(hdr_all - cpp_all)} cpp not hdr {sorted(cpp_all - hdr_all)}")
