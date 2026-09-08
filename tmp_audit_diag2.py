import re, pathlib

# GC diagnostics
gc_map_text = pathlib.Path('E:/il2cpp/src/il2cppc/Emit/MethodMap.GC.cs').read_text(encoding='utf-8', errors='ignore')
# Find the entries routed via TryMapGC for diagnostics/stacktrace
# Those with cpp names containing stack_trace, assert, log, tracing
patterns = re.findall(r'"il2cpp::runtime::(icall_[a-z_]+)"', gc_map_text)
diag_gc = [p for p in patterns if any(k in p for k in ['stack_trace','assert','log','tracing'])]
print('GC diagnostics related icalls:', diag_gc)

# Also check stubs.h diagnostics stubs
stubs_h = pathlib.Path('E:/il2cpp/src/runtime/include/internal/stubs.h').read_text(encoding='utf-8', errors='ignore')
print('stubs.h diagnostics region:')
for i,line in enumerate(stubs_h.splitlines(),1):
    if 'Diagnostics' in line or 'diagnostics' in line or 'tracing' in line or 'stack' in line.lower():
        print(f"{i}: {line}")

stubs_cpp = pathlib.Path('E:/il2cpp/src/runtime/src/stubs.cpp').read_text(encoding='utf-8', errors='ignore')
# find tracing
for i,line in enumerate(stubs_cpp.splitlines(),1):
    if 'tracing' in line.lower() or 'stack_trace' in line.lower() or 'assert' in line.lower() and 'icall' in line.lower():
        print(f"stubs.cpp {i}: {line}")

# Also check exception/statics for StackTrace
exc_h = pathlib.Path('E:/il2cpp/src/runtime/include/internal/il2cpp_exception.h').read_text(encoding='utf-8', errors='ignore')
import re as re2
exc_icalls = re.findall(r'\bicall_\w+', exc_h)
print('exception.h icalls', exc_icalls[:20])

# Check il2cpp_exception_ext.h
try:
    ext_h = pathlib.Path('E:/il2cpp/src/runtime/include/internal/il2cpp_exception_ext.h').read_text(encoding='utf-8', errors='ignore')
    exc2 = re.findall(r'\bicall_\w+', ext_h)
    print('exception_ext icalls', exc2[:20])
    # print first 30 lines
    for i,l in enumerate(ext_h.splitlines()[:50],1):
        print(i,l)
except Exception as e:
    print(e)

# Check stacktrace icall definition location
import glob
for f in glob.glob('E:/il2cpp/src/runtime/src/*.cpp'):
    txt = pathlib.Path(f).read_text(encoding='utf-8', errors='ignore')
    if 'icall_stack_trace' in txt:
        print('found stack_trace in', f)
        for i,l in enumerate(txt.splitlines(),1):
            if 'icall_stack_trace' in l:
                print(f" {i}: {l}")
for f in glob.glob('E:/il2cpp/src/runtime/include/internal/*.h'):
    txt = pathlib.Path(f).read_text(encoding='utf-8', errors='ignore')
    if 'icall_stack_trace' in txt:
        print('found stack_trace hdr in', f)
        for i,l in enumerate(txt.splitlines(),1):
            if 'icall_stack_trace' in l:
                print(f" hdr {i}: {l}")
