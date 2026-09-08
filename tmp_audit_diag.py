import re, pathlib, glob

def extract(path, pat):
    text = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    return re.findall(pat, text)

hdr_sw = extract('E:/il2cpp/src/runtime/include/internal/stopwatch.h', r'\b(il2cpp_stopwatch_\w+|icall_stopwatch_\w+)\s*\(')
hdr_dbg = extract('E:/il2cpp/src/runtime/include/internal/debugext.h', r'\b(il2cpp_debug_\w+|il2cpp_trace_\w+)\s*\(')
hdr_dbe = extract('E:/il2cpp/src/runtime/include/internal/debuggerext.h', r'\b(il2cpp_debugger_\w+)\s*\(')
hdr_proc = extract('E:/il2cpp/src/runtime/include/internal/pathproc.h', r'\b(il2cpp_process_\w+|il2cpp_path_\w+)\s*\(')

print('HDR stopwatch', hdr_sw, len(hdr_sw))
print('HDR debugext', hdr_dbg, len(hdr_dbg))
print('HDR debuggerext', hdr_dbe, len(hdr_dbe))
print('HDR pathproc', hdr_proc, len(hdr_proc))
print('TOTAL HDR', len(hdr_sw)+len(hdr_dbg)+len(hdr_dbe)+len(hdr_proc))

def extract_cpp(path, pat):
    text = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    return re.findall(pat, text)

cpp_sw = extract_cpp('E:/il2cpp/src/runtime/src/stopwatch.cpp', r'\b(il2cpp_stopwatch_\w+|icall_stopwatch_\w+)\s*\(')
cpp_dbg = extract_cpp('E:/il2cpp/src/runtime/src/debugext.cpp', r'\b(il2cpp_debug_\w+|il2cpp_trace_\w+)\s*\(')
cpp_dbe = extract_cpp('E:/il2cpp/src/runtime/src/debuggerext.cpp', r'\b(il2cpp_debugger_\w+)\s*\(')
cpp_proc = extract_cpp('E:/il2cpp/src/runtime/src/pathproc.cpp', r'\b(il2cpp_process_\w+|il2cpp_path_\w+)\s*\(')

print('CPP stopwatch', cpp_sw, len(cpp_sw))
print('CPP debugext', cpp_dbg, len(cpp_dbg))
print('CPP debuggerext', cpp_dbe, len(cpp_dbe))
print('CPP pathproc', cpp_proc, len(cpp_proc))
print('TOTAL CPP', len(cpp_sw)+len(cpp_dbg)+len(cpp_dbe)+len(cpp_proc))

# Dedup sets
print('\n--- THREE LAYER GAP ---')
hdr_set = set(hdr_sw+hdr_dbg+hdr_dbe+hdr_proc)
cpp_set = set(cpp_sw+cpp_dbg+cpp_dbe+cpp_proc)
print('HDR set', sorted(hdr_set), len(hdr_set))
print('CPP set', sorted(cpp_set), len(cpp_set))
print('HDR not in CPP', sorted(hdr_set - cpp_set))
print('CPP not in HDR', sorted(cpp_set - hdr_set))

# MethodMap
# Stopwatch: extract CppName strings
import re as re2
text_sw = pathlib.Path('E:/il2cpp/src/il2cppc/Emit/MethodMap.Stopwatch.cs').read_text(encoding='utf-8', errors='ignore')
map_sw = re2.findall(r'"il2cpp::runtime::(il2cpp_stopwatch_\w+|icall_stopwatch_\w+)"', text_sw)
text_dbg = pathlib.Path('E:/il2cpp/src/il2cppc/Emit/MethodMap.Debug.cs').read_text(encoding='utf-8', errors='ignore')
map_dbg = re2.findall(r'"il2cpp::runtime::(il2cpp_debug_\w+|il2cpp_trace_\w+)"', text_dbg)
text_dbe = pathlib.Path('E:/il2cpp/src/il2cppc/Emit/MethodMap.Debugger.cs').read_text(encoding='utf-8', errors='ignore')
map_dbe = re2.findall(r'"il2cpp::runtime::(il2cpp_debugger_\w+)"', text_dbe)
text_proc = pathlib.Path('E:/il2cpp/src/il2cppc/Emit/MethodMap.Process.cs').read_text(encoding='utf-8', errors='ignore')
map_proc = re2.findall(r'"il2cpp::runtime::(il2cpp_process_\w+)"', text_proc)

print('\nMAP stopwatch', map_sw, len(map_sw))
print('MAP debug', map_dbg, len(map_dbg))
print('MAP debugger', map_dbe, len(map_dbe))
print('MAP process', map_proc, len(map_proc))
map_set = set(map_sw+map_dbg+map_dbe+map_proc)
print('TOTAL MAP unique', sorted(map_set), len(map_set))
print('MAP not in HDR', sorted(map_set - hdr_set))
print('HDR not in MAP', sorted(hdr_set - map_set))
print('MAP not in CPP', sorted(map_set - cpp_set))
print('CPP not in MAP', sorted(cpp_set - map_set))
