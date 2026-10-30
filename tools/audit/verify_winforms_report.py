"""Self-check for docs/audit/winforms-pinvoke-current.md.

Two independent checks, mirroring tools/audit/verify_report.py:

  1. every `path/to/file.ext:NNN` citation in the document MUST name an
     existing repository file and a line number inside that file;
  2. every citation in the "must be true" set below must additionally have the
     quoted token on that exact line, so a line number cannot silently drift
     while still being "in range".

The second check is what keeps the marshal-capability table honest: it pins the
exact code that proves each claim.

usage: python tools/audit/verify_winforms_report.py <repoRoot>
exit code 0 = all citations valid, 1 = failures (printed)
"""

import io
import os
import re
import sys

CITE = re.compile(r'`([A-Za-z0-9_./\\-]+\.(?:cs|cpp|h|md|ps1|py)):(\d+)`')

# (file, line, token-that-must-appear-on-that-line)
PINNED = [
    ('src/il2cppc/Emit/CodeGenerator.cs', 9052, 'PinvokeParamList'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9061, 'utf16PInvoke'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9096, 'const char16_t*'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9108, 'p switch'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9114, '"n" => "intptr_t"'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9128, 'rDel'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9138, 'r.StartsWith("r:"'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9153, 'PinvokeStructCppType'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 9167, 'ResourceCount'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 8986, 'PinvokeReturnTypeCpp'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 2084, 'resource-pool.h'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 13499, 'il2cpp_resource_pool_fill'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 4623, 'il2cpp_pinvoke_capture_last_error'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 5364, 'il2cpp_pinvoke_string_to_utf16_alloc'),
    ('src/il2cppc/Emit/CodeGenerator.cs', 4637, 'il2cpp_pinvoke_utf16_free'),
    ('src/il2cppc/Emit/MethodRefResolver.cs', 684, 'ParamTypeNames'),
    ('src/il2cppc/Emit/MethodRefResolver.cs', 713, 'ResultKind: returnType switch'),
    ('src/il2cppc/Emit/MethodRefResolver.cs', 3227, 'DelegateGfpForPinvokeParam'),
    ('src/il2cppc/Emit/MethodSignatureCoder.cs', 364, '0x18 => "n"'),
    ('src/il2cppc/Emit/MethodMap.Marshal.cs', 95, 'GetLastWin32Error'),
    ('src/il2cppc/Emit/MethodMap.Marshal.cs', 120, 'PtrToStringUni'),
    ('src/il2cppc/Emit/MethodMap.Marshal.cs', 457, 'IsComObject'),
    ('src/il2cppc/Emit/MethodMap.Resources.cs', 54, 'GetManifestResourceStream'),
    ('src/il2cppc/Emit/MethodMap.Resource.cs', 58, 'System.Resources.ResourceManager'),
    ('src/il2cppc/Emit/TypeEmitter.cs', 1054, 'LayoutValueTypeFields'),
    ('src/il2cppc/Ir/StackFold.cs', 2560, 'capture_last_error'),
    ('src/runtime/src/pinvoke.cpp', 153, 'il2cpp_pinvoke_resolve'),
    ('src/runtime/src/pinvoke.cpp', 278, 'il2cpp_pinvoke_capture_last_error'),
    ('src/runtime/src/pinvoke.cpp', 291, 'il2cpp_pinvoke_get_last_error'),
    ('src/runtime/src/pinvokeext.cpp', 56, 'il2cpp_pinvoke_string_to_utf8_alloc'),
    ('src/runtime/src/pinvokeext.cpp', 136, 'il2cpp_pinvoke_string_to_utf16_alloc'),
    ('src/runtime/src/pinvokeext.cpp', 156, 'il2cpp_pinvoke_utf16_free'),
    ('src/runtime/src/pinvokeext.cpp', 166, 'il2cpp_pinvoke_string_from_utf16'),
    ('src/runtime/src/pinvokeext.cpp', 186, 'il2cpp_pinvoke_string_from_utf16_len'),
    ('src/runtime/src/pinvokeext.cpp', 257, 'il2cpp_pinvoke_byte_array_data'),
    ('src/runtime/src/pinvokeext.cpp', 278, 'icall_pinvoke_register_callback'),
    ('src/runtime/src/pinvokeext.cpp', 329, 'il2cpp_pinvoke_clear_last_error'),
    ('src/runtime/include/internal/il2cpp_com.h', 3, 'il2cpp_com.h'),
    ('src/runtime/src/com.cpp', 8045, 'icall_com_create_instance_clsid'),
]


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else '.'
    doc_path = os.path.join(root, 'docs', 'audit', 'winforms-pinvoke-current.md')
    text = io.open(doc_path, encoding='utf-8').read()

    cache = {}
    fails = []
    cites = CITE.findall(text)
    for rel, ns in cites:
        rel_norm = rel.replace('\\', '/')
        path = os.path.join(root, rel_norm)
        if not os.path.exists(path):
            fails.append((rel, ns, 'file missing'))
            continue
        if path not in cache:
            cache[path] = io.open(path, encoding='utf-8-sig', errors='replace').read().split('\n')
        lines = cache[path]
        n = int(ns)
        if not (1 <= n <= len(lines)):
            fails.append((rel, ns, 'line out of range (file has %d)' % len(lines)))
    print('citations in doc:', len(cites))
    print('range failures:', len(fails))
    for f in fails:
        print('  ', f)

    pin_fails = []
    for rel, n, token in PINNED:
        path = os.path.join(root, rel)
        if not os.path.exists(path):
            pin_fails.append((rel, n, 'file missing'))
            continue
        if path not in cache:
            cache[path] = io.open(path, encoding='utf-8-sig', errors='replace').read().split('\n')
        lines = cache[path]
        if not (1 <= n <= len(lines)):
            pin_fails.append((rel, n, 'out of range'))
            continue
        if token not in lines[n - 1]:
            pin_fails.append((rel, n, 'token %r absent; line=%r' % (token, lines[n - 1].strip()[:90])))
    print('pinned claims:', len(PINNED))
    print('pin failures:', len(pin_fails))
    for f in pin_fails:
        print('  ', f)

    # informational: which pins are ALSO cited in the document. The pins exist
    # to detect line drift even for claims stated in prose, so a pin without a
    # literal citation is fine.
    doc_cites = {(r.replace('\\', '/').rsplit('/', 1)[-1], int(n)) for r, n in cites}
    cited_pins = sum(1 for r, n, _ in PINNED if (r.rsplit('/', 1)[-1], n) in doc_cites)
    print('pinned claims that are also literally cited in the doc: %d / %d'
          % (cited_pins, len(PINNED)))

    ok = not fails and not pin_fails
    print('RESULT:', 'OK' if ok else 'FAIL')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
