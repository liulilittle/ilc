"""Classify the net48 System.Windows.Forms / System.Drawing P/Invoke surface.

Input: the TSVs emitted by tools/audit/WinFormsPinvokeGraph (pinvokes.tsv,
comimports.tsv, delegates.tsv, resources.tsv). Output: one row per P/Invoke with
a class (A/B/C/D/E) and a machine-readable reason, plus the per-DLL aggregates
the audit document renders.

Classes (see docs/audit/winforms-pinvoke-current.md section 0):
  A  the existing marshal is enough -- only a wiring/declaration entry is missing
  B  a NEW marshal capability is required (the reason names which one)
  C  needs the COM runtime (RCW/CCW/IDispatch), not just a marshal shape
  D  unreachable in stage 1 (the owning domain is out of scope), reason names it
  E  better implemented in the managed/runtime layer (pure memory/string/time)

Precedence is D > C > B > E > A, because a printing P/Invoke with a HandleRef
argument is still D (no printer in stage 1) and a COM entry with an array
argument is still C (the array is the smaller problem).

Evidence for every B verdict is the red probe in
tools/audit/run-marshal-probe.ps1 (TRANSPILE-FAIL / C++-BUILD-FAIL), so the
classification is reproducible rather than asserted.

usage: python tools/audit/classify_winforms_pinvokes.py <graphDir> <outTsv>
"""

import csv
import io
import os
import sys

# --- D: domains stage 1 does not reach --------------------------------
D_MODULES = {
    'clr.dll': 'CLR-hosting / ClickOnce isolation interop (only meaningful inside a CLR host process)',
    'UIAutomationCore.dll': 'MSAA/UIA accessibility provider surface (needs the UIA provider COM graph)',
    'imm32.dll': 'IME context/candidate-window management (no IME input in the stage-1 model)',
    'hhctrl.ocx': 'HTML Help control (external OCX outside the control surface)',
    'winspool.drv': 'printing subsystem (PrintDialog / PrinterSettings / PrintDocument)',
    'crypt32.dll': 'Authenticode license + timestamp verification (ClickOnce signing domain)',
    'wldp.dll': 'Windows Lockdown Policy query (CLR hosting policy probe)',
    'Powrprof.dll': 'system power state (Application.SetSuspendState only)',
}
D_OWNER_MARKERS = (
    'System.Deployment.Internal.Isolation',
    'System.Deployment.Internal.CodeSigning',
)

# --- C: COM runtime, independent of the marshal shape -----------------
C_ENTRYPOINTS = {
    'CoCreateInstance', 'CoGetClassObject', 'CoInitialize', 'CoInitializeEx',
    'CoUninitialize', 'OleInitialize', 'OleUninitialize', 'RegisterDragDrop',
    'RevokeDragDrop', 'DoDragDrop', 'OleGetClipboard', 'OleSetClipboard',
    'OleFlushClipboard', 'OleIsCurrentClipboard', 'CreateILockBytesOnHGlobal',
    'StgCreateDocfileOnILockBytes', 'StgOpenStorageOnILockBytes',
    'OleCreateFontIndirect', 'OleCreatePictureIndirect', 'OleTranslateColor',
    'CLSIDFromString', 'StringFromCLSID', 'IIDFromString', 'CoTaskMemAlloc',
    'CoTaskMemFree', 'SysAllocString', 'SysAllocStringLen', 'SysFreeString',
    'SysStringLen', 'VariantClear', 'VariantInit', 'VariantChangeType',
    'SafeArrayCreate', 'SafeArrayDestroy', 'SafeArrayGetElement',
    'SafeArrayPutElement', 'SafeArrayGetDim', 'SafeArrayGetLBound',
    'SafeArrayGetUBound', 'SafeArrayAccessData', 'SafeArrayUnaccessData',
    'GetHGlobalFromILockBytes', 'CreateStdAccessibleObject', 'LresultFromObject',
    'ObjectFromLresult', 'CreateStdAccessibleProxy', 'AccessibleObjectFromWindow',
}
C_OBJECT_RETURN_MODULES = {'ole32.dll', 'oleaut32.dll'}

# --- E: implement in the runtime instead of calling out ----------------
E_ENTRYPOINTS = {
    'RtlMoveMemory': 'memcpy -- the runtime implements it directly (no OS dependency)',
    'ZeroMemory': 'memset -- the runtime implements it directly',
    'lstrlen': 'string length -- the runtime implements it directly',
    'GetTickCount': 'monotonic clock -- the runtime already owns one',
    'GetCurrentThreadId': 'the runtime already owns the thread identity',
    'GetCurrentProcessId': 'the runtime already owns the process identity',
    'GetProcessHeap': 'the runtime already owns a native heap',
    'HeapFree': 'the runtime already owns a native heap',
}

# --- B reason texts (each backed by a probe row) ----------------------
R_HANDLEREF = ('handleref',
               'HandleRef parameter: the value type (WndProc + IntPtr) must decay to its handle, '
               'but the emitter declares the struct and the generated C++ does not define it '
               '[probe: HandleRef param = TRANSPILE-PASS then C++-BUILD-FAIL, '
               "'System_Runtime_InteropServices_HandleRef_class' was not declared]")
R_ARRAY = ('array',
           'array parameter (byte[]/char[]/int[]/float[]/IntPtr[]/bool[]/struct[]): no pin/copy '
           'bridge; the runtime helper il2cpp_pinvoke_byte_array_data (pinvokeext.cpp:257) exists '
           'but NO emitter path reaches it [probe: TRANSPILE-FAIL "reference-typed ... not supported"]')
R_SB = ('stringbuilder',
        'StringBuilder buffer parameter (fixed-size native write buffer): no bridge '
        '[probe: TRANSPILE-FAIL]')
R_BYREF_SCALAR = ('byref-scalar',
                  'ref/out of a SCALAR (int/uint/short/bool/float/IntPtr): PinvokeParamList '
                  'LOUD-rejects every "B:" parameter unless it is a registered struct '
                  '[probe: "B:i" = TRANSPILE-FAIL]')
R_BYREF_CLASS = ('byref-class',
                 'ref/out of a CLASS or an unrecognised value type (Guid, enum, nested Win32 '
                 'struct declared in the OTHER assembly): same "B:" rejection as a scalar '
                 '[probe: "B:r:System.String" = TRANSPILE-FAIL]')
R_STRUCT_RETURN = ('struct-return',
                   'BY-VALUE struct return of a type outside the layout registry: the native '
                   'declaration falls back to int32_t and the call site breaks '
                   '[probe: TRANSPILE-PASS then C++-BUILD-FAIL]')
R_STRING_OUT = ('string-out',
                'out/ref string: needs a native buffer plus a UTF-16 hand-back '
                '[probe: TRANSPILE-FAIL]')
R_OBJECT = ('object',
            'System.Object / interface-typed parameter with no ComImport affiliation: needs a '
            'VARIANT-style marshal that does not exist')
R_MISSING = ('entrypoint-missing',
             'the module ships neither the bare name nor the CharSet-matching A/W suffix on this '
             'machine (ZeroMemory is a kernel32 macro alias for RtlZeroMemory; LoadIconWithScaleDown '
             'and ImageList_WriteEx need comctl32 v6) [probe: WinFormsExportProbe verdict=missing]')
R_SUFFIX = ('entrypoint-suffix',
            'ExactSpelling=false: .NET resolves "NameW"/"NameA" (per CharSet) before the bare '
            '"Name"; il2cpp_pinvoke_resolve looks up the BARE name only, so a W/A-only API '
            'throws EntryPointNotFoundException [probe: CharSet.Unicode GetWindowText with '
            'ExactSpelling=false = runtime "Unable to find an entry point named GetWindowText"]')
R_NONBLIT = ('nonblittable-struct',
             'struct parameter whose managed layout is NOT byte-identical to the native one (it carries '
             'a managed reference where the native side has inline data): LOGFONTW.lfFaceName is '
             'WCHAR[32] natively but an Il2CppObject* in the generated struct; the same for '
             'CHOOSEFONT/OPENFILENAME/NOTIFYICONDATA/MONITORINFOEX/... '
             '[probe: LOGFONT-like struct by-ref = TRANSPILE-PASS, generated field is a GC pointer]')
R_NAME_CLASH = ('name-clash',
                'struct parameter whose C++ struct name collides with a Windows SDK typedef '
                '(LOGFONTW / TEXTMETRICW / ...): generated.h forward-declares "struct LOGFONTW" '
                'against the windows.h typedef [probe: LOGFONTW by-ref = C++-BUILD-FAIL '
                '"using typedef-name after struct"]')
R_WNDPROC = ('wndproc-delegate',
             'WNDPROC/HOOKPROC delegate parameter: the delegate-to-function-pointer path works, '
             'but the callee must also receive wParam/lParam as IntPtr and the thunk must be '
             'callable from the native window procedure')


def load_com_types(graph_dir):
    names = set()
    path = os.path.join(graph_dir, 'comimports.tsv')
    if not os.path.exists(path):
        return names
    for line in io.open(path, encoding='utf-8'):
        parts = line.rstrip('\n').split('\t')
        if len(parts) == 3:
            names.add(parts[2])
    return names


def is_com_type(t, com_types):
    return t in com_types or t.startswith('System.Runtime.InteropServices.ComTypes.')


def struct_registered(t):
    # CodeGenerator.PinvokeStructCppType (CodeGenerator.cs:9153) registers exactly these.
    return t in ('System.Drawing.Point', 'System.Drawing.Rectangle',
                 'System.Windows.Forms.Message')


def flags_of(row):
    return set(f for f in row['flag'].split(',') if f)


def load_nonblittable_structs(graph_dir):
    """Struct names whose layout is NOT byte-identical to the native one.

    WinFormsStructProbe marks a struct NON-BLITTABLE when it has a managed
    reference field (string / class / array / delegate). The emitter passes such
    a struct BY ADDRESS with a GC pointer where the native side expects inline
    data (e.g. LOGFONTW.lfFaceName is WCHAR[32] natively but Il2CppObject* in the
    generated struct) -- a silent layout mismatch, not a LOUD rejection.
    """
    path = os.path.join(graph_dir, 'structs.tsv')
    out = set()
    if not os.path.exists(path):
        return out
    for r in csv.DictReader(io.open(path, encoding='utf-16', newline=''), delimiter='\t'):
        if r['verdict'] != 'blittable':
            out.add(r['struct'])
    return out


def load_exports(graph_dir):
    """(module, entrypoint) -> verdict from WinFormsExportProbe, when present."""
    path = os.path.join(graph_dir, 'exports.tsv')
    out = {}
    if not os.path.exists(path):
        return out
    for r in csv.DictReader(io.open(path, encoding='utf-8', newline=''), delimiter='\t'):
        out[(r['module'], r['entrypoint'])] = r
    return out


def classify(row, com_types, exports, nonblittable):
    module = row['module']
    owner = row['owner']
    entry = row['entrypoint']
    ret = row['ret']
    params = [p for p in row['params'].split(';') if p]
    flags = flags_of(row)

    # --- D ---
    if module in D_MODULES:
        return 'D', D_MODULES[module], []
    for marker in D_OWNER_MARKERS:
        if marker in owner:
            return 'D', 'owner %s -- ClickOnce deployment/signing domain' % marker, []

    # --- C ---
    if entry in C_ENTRYPOINTS:
        return 'C', 'COM entry point %s (native interface handle in/out; RCW/CCW needed)' % entry, []
    com_params = [p for p in params if is_com_type(p.rstrip('&'), com_types)]
    if com_params:
        return 'C', 'ComImport interface parameter %s (RCW needed)' % com_params[0], []
    if is_com_type(ret, com_types):
        return 'C', 'ComImport interface return %s (RCW needed)' % ret, []
    if ret == 'System.Object' and module in C_OBJECT_RETURN_MODULES:
        return 'C', 'System.Object return from %s (native interface pointer wrapped into an RCW)' % module, []

    # --- B: collect EVERY missing capability (set cover for the roadmap) ---
    needs = []
    for f in flags:
        if f.startswith('ret-vt:') or f.startswith('ret-enum:'):
            name = f.split(':', 1)[1]
            if f.startswith('ret-vt:') and struct_registered(name):
                continue
            if name == 'System.Windows.Forms.DpiAwarenessContext':
                continue
            needs.append('struct-return')
            break
    if ret.startswith('System.') and ret not in (
            'System.IntPtr', 'System.UIntPtr', 'System.Object', 'System.String',
            'System.Windows.Forms.DpiAwarenessContext') and not ret.startswith('System.Int'):
        needs.append('struct-return')
    export = exports.get((module, entry))
    if row['exactSpelling'] == '0' and isinstance(export, dict):
        suffix_col = 'W' if row['charset'] in ('Unicode', 'Auto') else 'A'
        has_suffix = export.get(suffix_col) == '1'
        has_bare = export.get('bare') == '1'
        if has_suffix and not has_bare:
            needs.append('entrypoint-suffix')
        elif not has_suffix and not has_bare and export.get('verdict') not in ('no-module',):
            needs.append('entrypoint-missing')
    if 'handleref' in flags:
        needs.append('handleref')
    if 'sb' in flags:
        needs.append('stringbuilder')
    if 'array' in flags:
        needs.append('array')
    for f in flags:
        if f.startswith('byref-ref:System.String'):
            needs.append('string-out')
        elif f.startswith('byref-scalar:'):
            needs.append('byref-scalar')
        elif f.startswith('byref-ref:'):
            needs.append('byref-other')
        elif f.startswith('vt:') and ('LOGFONT' in f or 'TEXTMETRIC' in f or 'NONCLIENTMETRICS' in f):
            needs.append('name-clash')
    if 'object' in flags:
        needs.append('object')
    struct_params = [p.rstrip('&') for p in params if p.rstrip('&') in nonblittable]
    if struct_params:
        needs.append('nonblittable-struct')

    needs = [n for n in dict.fromkeys(needs)]
    # ZeroMemory is a kernel32 MACRO (the loader has no such export at all);
    # RtlMoveMemory/lstrlen/GetTickCount are trivial runtime helpers. For those
    # the "native call is impossible/unnecessary" verdict beats a marshal gap.
    if entry in E_ENTRYPOINTS and set(needs) <= {'entrypoint-missing'}:
        return 'E', E_ENTRYPOINTS[entry], []
    if needs:
        reasons = {
            'entrypoint-suffix': R_SUFFIX[1],
            'entrypoint-missing': R_MISSING[1],
            'nonblittable-struct': R_NONBLIT[1],
            'handleref': R_HANDLEREF[1],
            'stringbuilder': R_SB[1],
            'array': R_ARRAY[1],
            'byref-scalar': R_BYREF_SCALAR[1],
            'byref-other': R_BYREF_CLASS[1],
            'struct-return': R_STRUCT_RETURN[1],
            'string-out': R_STRING_OUT[1],
            'object': R_OBJECT[1],
            'name-clash': R_NAME_CLASH[1],
        }
        first = reasons[needs[0]]
        if len(needs) > 1:
            first += ' [+ also needs: ' + '+'.join(needs[1:]) + ']'
        return 'B', first, needs

    # --- E ---
    if entry in E_ENTRYPOINTS:
        return 'E', E_ENTRYPOINTS[entry], []

    # --- A ---
    if ret == 'System.Object':
        return 'B', R_OBJECT[1] + ' (return)', ['object']
    return 'A', 'scalar / IntPtr / UTF-16 string / delegate / enum / registered-or-by-ref struct only', []


def main():
    graph_dir, out_tsv = sys.argv[1], sys.argv[2]
    com_types = load_com_types(graph_dir)
    exports = load_exports(graph_dir)
    nonblittable = load_nonblittable_structs(graph_dir)

    rows = list(csv.DictReader(io.open(os.path.join(graph_dir, 'pinvokes.tsv'),
                                       encoding='utf-8', newline=''), delimiter='\t'))
    out = []
    for r in rows:
        cls, reason, needs = classify(r, com_types, exports, nonblittable)
        out.append(dict(r, klass=cls, needs='+'.join(needs), reason=reason))

    cols = ['klass', 'needs', 'module', 'entrypoint', 'owner', 'method', 'charset',
            'lastError', 'exactSpelling', 'ret', 'paramCount', 'params', 'flag', 'reason']
    with io.open(out_tsv, 'w', encoding='utf-8-sig', newline='\n') as fh:
        fh.write('\t'.join(cols) + '\n')
        for r in out:
            fh.write('\t'.join(str(r[c]) for c in cols) + '\n')

    print('total=%d' % len(out))
    for k in 'ABCDE':
        print('%s=%d' % (k, sum(1 for r in out if r['klass'] == k)))

    print()
    print('## per DLL')
    dlls = sorted({r['module'] for r in out},
                  key=lambda d: (-sum(1 for r in out if r['module'] == d), d))
    print('module\tA\tB\tC\tD\tE\ttotal')
    for d in dlls:
        sub = [r for r in out if r['module'] == d]
        print('%s\t%s\t%d' % (d, '\t'.join(str(sum(1 for r in sub if r['klass'] == k)) for k in 'ABCDE'), len(sub)))

    print()
    print('## B reason families')
    fam = {}
    for r in out:
        if r['klass'] != 'B':
            continue
        key = r['reason'].split(':')[0]
        fam[key] = fam.get(key, 0) + 1
    for k, v in sorted(fam.items(), key=lambda kv: -kv[1]):
        print('%d\t%s' % (v, k))


if __name__ == '__main__':
    main()
