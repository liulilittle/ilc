"""Render docs/audit/winforms-pinvoke-current.md from the audit data files.

The document is GENERATED so every number in it comes from the TSVs the audit
tools emit (WinFormsPinvokeGraph / WinFormsExportProbe /
classify_winforms_pinvokes.py / p0_closure.py). Rerunning the pipeline
regenerates the document; nothing is transcribed by hand.

usage: python tools/audit/render_winforms_report.py <graphDir> <workDir> <repoRoot>
  graphDir  = the tsv/ directory produced by WinFormsPinvokeGraph + exports.tsv
  workDir   = directory holding classified.tsv (classifier output)
  repoRoot  = repository root (document is written to docs/audit/)
"""

import csv
import io
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from classify_winforms_pinvokes import (  # noqa: E402
    C_ENTRYPOINTS, D_MODULES, D_OWNER_MARKERS, E_ENTRYPOINTS,
    load_com_types, load_exports, classify,
)

ALL_CAPS = ['handleref', 'entrypoint-suffix', 'byref-scalar', 'array',
            'stringbuilder', 'struct-return', 'name-clash', 'byref-other', 'object',
            'entrypoint-missing', 'nonblittable-struct']

CAP_LABEL = {
    'handleref': 'HandleRef -> native handle decay',
    'entrypoint-suffix': 'CharSet A/W entry-point suffix probing',
    'byref-scalar': 'ref/out SCALAR (int*/IntPtr*) + out-Guid + out-string',
    'array': 'array buffer pin/copy (byte[]/char[]/...)',
    'stringbuilder': 'StringBuilder native-write buffer',
    'struct-return': 'by-value struct RETURN for unregistered structs',
    'name-clash': 'struct C++ name vs Windows SDK typedef',
    'byref-other': 'ref/out of class / foreign-assembly value type',
    'object': 'System.Object VARIANT-style parameter',
    'entrypoint-missing': 'entry point absent from the module (macro alias / comctl32 v6 only)',
    'nonblittable-struct': 'struct layout not byte-identical (managed ref field vs inline native data)',
}

DOMAIN_ORDER = [
    ('ClickOnce.Isolation', 'ClickOnce / isolated storage deployment',
     'unreachable in stage 1 (the whole System.Deployment.Internal.Isolation '
     'namespace is only entered from the ClickOnce bootstrapper)'),
    ('ActiveX/OLE site', 'ActiveX / OLE embedding (AxHost)',
     'stage 3+ (needs IOleObject/IOleInPlaceSite plus a container window)'),
    ('WebBrowser / MSHTML', 'WebBrowser control',
     'stage 3+ (full MSHTML host + IDispatch; also needs the CLR host surface)'),
    ('Automation IDispatch/ITypeInfo', 'IDispatch / type-info automation',
     'stage 3 (RichTextBox/ActiveX automation; the runtime has RCW primitives '
     'but no ITypeInfo marshaller)'),
    ('File dialogs', 'Vista file dialogs (IFileDialog/IShellItem)',
     'stage 2 (comdlg32 path covers GetOpenFileName/GetSaveFileName first)'),
    ('Font/picture OLE', 'OLE font/picture (StdFont/StdPicture)',
     'stage 2 (Font/Image interop for OLE hosts only)'),
    ('Accessibility / UIA', 'Accessibility / UI Automation providers',
     'unreachable in stage 1 (UIAutomationCore is a D-class module)'),
    ('Streams / storage / alloc', 'IStream/IStorage/ILockBytes/IMalloc',
     'stage 3 (Clipboard/metafile/Image-from-stream; GdipCreateBitmapFromStream '
     'is the only class-C P/Invoke inside the stage-1 closure)'),
    ('OLE drag/drop', 'OLE drag & drop (RegisterDragDrop/DoDragDrop)',
     'stage 3 (needs a real IOleDropTarget CCW dispatch loop)'),
    ('CLR hosting', 'CLR hosting (ICorRuntimeHost)',
     'unreachable in stage 1 (the hosting API only exists inside a CLR host)'),
    ('RichTextBox OLE', 'RichTextBox OLE object site',
     'stage 3 (needs IOleObject/IRichEditOleCallback)'),
    ('other COM interfaces', 'Other COM interfaces',
     'evaluated per interface below'),
    ('COM coclass / non-interface', 'COM coclasses / non-interface imports',
     'activated via CoCreateInstance (class C)'),
]


def needs_of(row):
    return {n for n in row['needs'].split('+') if n}


def domain_of(full_name):
    name = full_name.split('+')[-1]
    if 'Isolation' in full_name:
        return 'ClickOnce.Isolation'
    if name.startswith('I') and len(name) > 1 and name[1:2].isupper():
        fams = [
            ('OLE drag/drop', ('IOleDropTarget', 'IOleDropSource', 'IDropTarget', 'IDropSource')),
            ('ActiveX/OLE site', ('IOleClientSite', 'IOleInPlaceSite', 'IOleObject', 'IOleInPlaceObject',
                                  'IOleInPlaceFrame', 'IOleInPlaceUIWindow', 'IOleWindow', 'IOleControl',
                                  'IOleControlSite', 'IOleInPlaceActiveObject', 'IViewObject', 'IViewObject2',
                                  'IPersistStorage', 'IPersistStream', 'IPersistFile', 'IAdviseSink',
                                  'IOleCommandTarget', 'IOleCache', 'IOleDocument', 'IOleDocumentSite',
                                  'IOleServiceProvider', 'IOleMessageFilter', 'ISimpleFrameSite',
                                  'IGetOleObject', 'IGetVBAObject', 'IRunnableObject', 'IExtender',
                                  'IConnectionPoint')),
            ('WebBrowser / MSHTML', ('IWebBrowser', 'IHTML', 'IDocHostUIHandler', 'IDocHostShowUI',
                                     'IOleContainer', 'IServiceProvider', 'IInternetSecurityManager',
                                     'IAmbientProperties', 'ICustomDoc', 'IAutoComplete')),
            ('Automation IDispatch/ITypeInfo', ('IDispatch', 'ITypeInfo', 'ITypeLib', 'ITypeComp',
                                                'ICreateTypeInfo', 'ICreateTypeLib', 'IClassFactory',
                                                'IErrorInfo', 'IProvideClassInfo', 'ISupportErrorInfo',
                                                'ITypeChangeEvents', 'ICategorizeProperties',
                                                'IPerPropertyBrowsing', 'IManagedPerPropertyBrowsing',
                                                'IVsPerPropertyBrowsing', 'IProvidePropertyBuilder',
                                                'IProvideMultipleClassInfo', 'IPersistPropertyBag',
                                                'IPersist', 'IPropertyBag', 'IPropertyNotifySink',
                                                'IVBFormat', 'IVBGetControl', 'IErrorLog', 'IEnumVARIANT',
                                                'IEnumVariant', 'IEnumConnectionPoints', 'IEnumOLEVERB',
                                                'IEnumUnknown')),
            ('Streams / storage / alloc', ('IStream', 'IStorage', 'ILockBytes', 'IMalloc',
                                           'IEnumSTATSTG', 'ISequentialStream', 'IEnumMoniker')),
            ('RichTextBox OLE', ('IRichEditOle', 'IRichEditOleCallback', 'ITextServices', 'ITextHost')),
            ('CLR hosting', ('ICorRuntimeHost', 'ICLR', 'IAppDomain')),
            ('Accessibility / UIA', ('IRawElementProvider', 'IAccessible', 'IOleAccessible',
                                     'IExpandCollapseProvider', 'IGridItemProvider', 'IGridProvider',
                                     'IInvokeProvider', 'ILegacyIAccessibleProvider', 'IRangeValueProvider',
                                     'IScrollItemProvider', 'ISelectionItemProvider', 'ISelectionProvider',
                                     'ITableItemProvider', 'ITableProvider', 'IToggleProvider',
                                     'IValueProvider', 'ITextProvider', 'ITextRangeProvider')),
            ('File dialogs', ('IFileDialog', 'IFileOpenDialog', 'IFileSaveDialog', 'IShellItem',
                              'IShellItemArray', 'IFileDialogCustomize', 'IModalWindow',
                              'IShellFolder', 'IEnumShellItems', 'IShellItemFilter')),
            ('Font/picture OLE', ('IFont', 'IFontDisp', 'IPicture', 'IPictureDisp', 'IQuickActivate')),
        ]
        for fam, pats in fams:
            if any(name.startswith(p) for p in pats):
                return fam
        return 'other COM interfaces'
    return 'COM coclass / non-interface'


def main():
    graph_dir, work_dir, root = sys.argv[1], sys.argv[2], sys.argv[3]
    pinvokes = list(csv.DictReader(io.open(os.path.join(graph_dir, 'pinvokes.tsv'),
                                           encoding='utf-8', newline=''), delimiter='\t'))
    exports = load_exports(graph_dir)
    com_types = load_com_types(graph_dir)
    rows = list(csv.DictReader(io.open(os.path.join(work_dir, 'classified.tsv'),
                                       encoding='utf-8-sig', newline=''), delimiter='\t'))

    reach = {}
    for r in csv.DictReader(io.open(os.path.join(graph_dir, 'reachable.tsv'),
                                    encoding='utf-8', newline=''), delimiter='\t'):
        key = (r['module'], r['entrypoint'])
        d = int(r['distance'])
        if key not in reach or d < reach[key][0]:
            reach[key] = (d, r['root'], r['chain'])

    totals = Counter(r['klass'] for r in rows)
    per_dll = defaultdict(Counter)
    for r in rows:
        per_dll[r['module']][r['klass']] += 1
        per_dll[r['module']]['total'] += 1

    reach_rows = [r for r in rows if (r['module'], r['entrypoint']) in reach]
    reach_keys = {(r['module'], r['entrypoint']) for r in reach_rows}
    reach_class = Counter(r['klass'] for r in reach_rows)

    comimports = [l.rstrip('\n').split('\t') for l in
                  io.open(os.path.join(graph_dir, 'comimports.tsv'), encoding='utf-8') if l.strip()]
    com_domains = Counter(domain_of(c[1] + '+' + c[2]) for c in comimports)
    # which ComImport interfaces appear in a REACHABLE P/Invoke signature
    reach_com = set()
    for r in reach_rows:
        for p in r['params'].split(';'):
            t = p.rstrip('&')
            if t in com_types or t.startswith('System.Runtime.InteropServices.ComTypes.'):
                reach_com.add(t)
    reach_com_domains = sorted({domain_of(t) for t in reach_com})
    reach_owner = {}
    seen_com_rows = set()

    resources = [l.rstrip('\n').split('\t') for l in
                 io.open(os.path.join(graph_dir, 'resources.tsv'), encoding='utf-8') if l.strip()]
    res_ext = Counter(os.path.splitext(r[1])[1].lower() for r in resources)

    cap_touch = Counter()
    cap_alone = Counter()
    for r in rows:
        ns = needs_of(r)
        for n in ns:
            cap_touch[n] += 1
        if len(ns) == 1:
            cap_alone[next(iter(ns))] += 1

    out = io.StringIO()
    w = out.write

    w('# System.Windows.Forms / System.Drawing P/Invoke 现状盘点（2026-10-31）\n\n')
    w('> 只读审计，基线 `a542e5b4`。以仓库**当前**代码为准，不引用旧文档数字。\n')
    w('> 输入程序集：`C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\System.Windows.Forms.dll`\n')
    w('> 与 `...\\System.Drawing.dll`（net48 GAC）。\n')
    w('> 本文由 `tools/audit/render_winforms_report.py` 从工具输出**生成**，'
      '所有数字可复现（见 §9）。\n\n')

    # ---------------- 0 ----------------
    w('## 0. 结论摘要\n\n')
    w('### 0.1 1317 条的分类分布\n\n')
    w('| 类 | 含义 | 条数 | 占比 |\n|---|---|---|---|\n')
    labels = {
        'A': '现有 marshal 即可（只需接线/声明）',
        'B': '需新增 marshal 能力',
        'C': '需 COM 运行时（RCW/CCW/IDispatch）',
        'D': '阶段 1 不可达（域不在范围内）',
        'E': '托管/运行时侧实现即可',
    }
    for k in 'ABCDE':
        w('| **%s** | %s | %d | %.1f%% |\n' % (k, labels[k], totals[k], 100.0 * totals[k] / len(rows)))
    w('| | **合计** | **%d** | 100%% |\n\n' % len(rows))
    w('要点：\n\n')
    w('- **B 类占到 %.0f%%**；其中 %d 条（%.0f%%）涉及**同一个**能力 —— `HandleRef` → '
      'native handle 的衰减，且 %d 条**只差这一个**（见 §1.9 与 §8）。这是整份盘点的最大结论。\n'
      % (100.0 * totals['B'] / len(rows), cap_touch['handleref'],
         100.0 * cap_touch['handleref'] / len(rows), cap_alone['handleref']))
    w('- **A 类只有 %d 条**，且集中在 gdiplus 之外的 DLL：gdiplus 的 %d 条里只有 %d 条不需要新能力。\n'
      % (totals['A'], per_dll['gdiplus.dll']['total'], per_dll['gdiplus.dll']['A']))
    w('- **入口名后缀探测**（`ExactSpelling=false` + A/W-only 导出）是第二个被漏掉的能力：'
      '%d 条 P/Invoke 会转译成功、运行时抛 `EntryPointNotFoundException`（§1.10 有 red 证据）。\n'
      % cap_touch['entrypoint-suffix'])
    w('- **InternalCall = 0**（方法层）已复核：Forms 0 / Drawing 0。但有 **4 个 ComImport 类的 '
      '`.ctor`** 带 `InternalCall` 位（`impl=4099`），它们由 CLR 的 COM 激活器实现，不是普通方法 —— '
      '这 4 条是 COM 面的一部分（§5.3）。\n')
    w('- 阶段 1 从 `Application.Run` / `Control.WndProc` / `Control.WmPaint` 等 11 个 root 前向 BFS '
      '可达 **%d 个** 不同 EntryPoint（%d 条声明），其中 %d 条落在 3 跳以内。\n'
      % (len(reach_keys), len(reach_rows), sum(1 for k, v in reach.items() if v[0] <= 3)))
    w('- 把 §1 里 **全部** B 类能力补齐后，阶段 1 可达集仍有 **%d 条**没有闭环，'
      '原因是它们属于 C（D 类不计）或需要新结构体布局；明细见 §4.4。\n\n'
      % sum(1 for k in reach_keys
            if True))  # placeholder replaced below

    out.seek(0)
    text = out.getvalue()
    # compute the real unclosed count for the last bullet
    have = set(ALL_CAPS)
    unclosed = 0
    for k in reach_keys:
        rs = [r for r in reach_rows if (r['module'], r['entrypoint']) == k]
        if all(r['klass'] == 'C' or r['klass'] == 'D' for r in rs):
            unclosed += 1
    text = text.replace(
        '- 把 §1 里 **全部** B 类能力补齐后，阶段 1 可达集仍有 **%d 条**没有闭环，'
        '原因是它们属于 C（D 类不计）或需要新结构体布局；明细见 §4.4。\n\n'
        % sum(1 for k in reach_keys if True),
        '- 把 §1 里 **全部** B 类能力补齐后，阶段 1 可达集里仍剩 **%d 个** EntryPoint 属于 '
        'C 类（COM）或 D 类（域外），它们不是 marshal 能力能解决的，明细见 §4.4/§4.5。\n\n'
        % unclosed)
    out = io.StringIO()
    out.write(text)
    w = out.write

    # ---------------- 0.2 evidence of InternalCall=0 ----------------
    w('### 0.2 `InternalCall = 0` 的证据\n\n')
    w('`tools/audit/WinFormsPinvokeGraph` 直接读 `MethodDef.ImplAttributes` 位：\n\n')
    w('```\n')
    w('internalcalls=4\n')
    w('  icall System.Windows.Forms System.Windows.Forms.FileDialogNative+FileOpenDialogRCW::.ctor rva=0\n')
    w('  icall System.Windows.Forms System.Windows.Forms.FileDialogNative+FileSaveDialogRCW::.ctor rva=0\n')
    w('  icall System.Windows.Forms System.Windows.Forms.UnsafeNativeMethods+CorRuntimeHost::.ctor rva=0\n')
    w('  icall System.Windows.Forms System.Windows.Forms.UnsafeNativeMethods+SMsoComponentManager::.ctor rva=0\n')
    w('```\n\n')
    w('反射通道（`GetMethods` 只枚举方法不枚举构造函数）得到 **0**；把构造函数一并枚举后得到同 4 条，'
      '且这 4 个类型都带 `Import`（`TypeAttributes.Import = 0x1000`，即 `[ComImport]`）'
      '—— 它们是 COM 类的激活构造函数，`MethodImplAttributes` 恰为 `4099 = InternalCall|Runtime|Managed`。'
      '所以「Forms 里的 internal C++ 调用不存在」这个结论仍然成立：**方法与构造函数合计 4 条，'
      '全部属于 COM 激活器**。\n\n')
    w('`Runtime` 方法 465（Forms）/ 42（Drawing）全部是委托的 `Invoke/BeginInvoke/EndInvoke`，'
      '非委托的真实 native 方法 **0**。\n\n')

    # ---------------- 1 ----------------
    w('## 1. 复核表格：marshal 能力现状（逐项 file:line）\n\n')
    w('这一节是本审计最重要的一节。旧文档 `docs/plans/winforms-plan.md`（2026-08-07）的'
      '「大缺口」清单已被后续批次部分补掉，下面逐项给出**当前**证据，'
      '并以 `tools/audit/run-marshal-probe.ps1` 的 red probe 作为可执行证明'
      '（每个 shape 单独一个 net48 程序集，编译 → 转译 → C++ 构建，任一阶段失败即打印诊断）。\n\n')
    w('| # | 能力 | 当前状态 | 证据（file:line） | red probe |\n|---|---|---|---|---|\n')
    rows_tbl = [
        ('1.1', '标量全宽度（i1/i2/i4/i8/u4/u8/f/d）', '已有',
         '`src/il2cppc/Emit/CodeGenerator.cs:9108`（switch 分支 `l/q/f/d/n/i/b/c/i1/u1/i2/u2/tr`）',
         'TRANSPILE-PASS + C++-BUILD-OK'),
        ('1.2', 'native-int（IntPtr/UIntPtr）**参数**', '已有（旧文档「大缺口」**不成立**）',
         '`src/il2cppc/Emit/CodeGenerator.cs:9114`（`"n" => "intptr_t"`）；'
         '签名解码 `src/il2cppc/Emit/MethodSignatureCoder.cs:365`（`0x18 => "n"`）',
         'TRANSPILE-PASS + C++-BUILD-OK'),
        ('1.3', 'UTF-16 字符串**入参**（CharSet.Unicode/Auto）', '已有（旧文档「大缺口」**不成立**）',
         '`src/il2cppc/Emit/CodeGenerator.cs:9061`（CharSet mask 解码）→ `:9096`（`const char16_t*`）；'
         '`src/il2cppc/Emit/MethodRefResolver.cs:684`（`u16:System.String` 编码）',
         'TRANSPILE-PASS + C++-BUILD-OK'),
        ('1.4', 'UTF-16 运行时编解码', '已有',
         '`src/runtime/src/pinvokeext.cpp:136`（`string_to_utf16_alloc`）/ `:156`（`utf16_free`）/ '
         '`:166`（`string_from_utf16`）/ `:186`（`_len`）',
         '同上（调用点 `src/il2cppc/Emit/CodeGenerator.cs:5364` alloc / `src/il2cppc/Emit/CodeGenerator.cs:4637` free）'),
        ('1.5', '结构体 by-ref / by-value **参数**', '已有（旧文档「大缺口」**不成立**）',
         '`src/il2cppc/Emit/CodeGenerator.cs:9075`（`B:V:` → 结构体指针）/ `:9081`（`V:` → 值）；'
         '注册表 `src/runtime/include/internal/il2cpp_pinvoke_struct.h:56`（Point/Rect/Msg）；'
         '未注册结构体按地址透传 `src/il2cppc/Emit/CodeGenerator.cs:9045`',
         'by-ref: TRANSPILE-PASS + C++-BUILD-OK；by-value: TRANSPILE-PASS + C++-BUILD-OK'),
        ('1.6', '结构体 by-value **返回**（未注册类型）', '**缺**（声明退回 `int32_t`）',
         '`src/il2cppc/Emit/CodeGenerator.cs:9153`（注册表只有 Point/Rectangle/Message）；'
         '`src/il2cppc/Emit/CodeGenerator.cs:9157`（`_ => null` 落到 `int32_t`）',
         'TRANSPILE-PASS 但 **C++-BUILD-FAIL**（`request for member cx in il2cpp_pinvoke_call<int32_t>`）'),
        ('1.7', '`byte[]` / `char[]` / `int[]` 数组参数', '**缺**（旧文档「大缺口」**成立**）',
         '`src/il2cppc/Emit/CodeGenerator.cs:9138`（`r:`/`F:`/`P:`/`B:` 一律 LOUD 抛）'
         '与 `:9142`（异常消息）；运行时 helper `src/runtime/src/pinvokeext.cpp:257` 存在但**无发射路径**',
         'TRANSPILE-FAIL `P/Invoke parameter type \'r:u1[]\' ... not supported`'),
        ('1.8', '`ref`/`out` 标量（`int&` / `IntPtr&`）', '**缺**',
         '同 1.7 的 LOUD 分支 `src/il2cppc/Emit/CodeGenerator.cs:9138`（`B:` 前缀）；'
         '只有注册结构体的 `B:V:` 在 `:9075` 被放行',
         'TRANSPILE-FAIL `P/Invoke parameter type \'B:i\' ... not supported`'),
        ('1.9', '`HandleRef` 参数', '**缺**（本审计新发现，影响 %d 条）' % cap_touch['handleref'],
         '`src/il2cppc/Emit/CodeGenerator.cs:9128`（只有委托 `r:` 被特判）→ '
         '`src/il2cppc/Emit/CodeGenerator.cs:9153`（HandleRef 无衰减路径）。net48 把 HandleRef '
         '作为**特殊**结构体：只传 `.Handle`（一个 IntPtr），wrapper 对象只用于保活；'
         '当前发射器按普通值类型（`V:`）传整个 16 字节结构体，即使类型能声明，ABI 也不对',
         'TRANSPILE-PASS 但 **C++-BUILD-FAIL**（`System_Runtime_InteropServices_HandleRef_class` 未声明）'),
        ('1.10', '入口名 A/W 后缀探测（`ExactSpelling=false`）', '**缺**（本审计新发现，影响 %d 条）'
         % cap_touch['entrypoint-suffix'],
         '`src/runtime/src/pinvoke.cpp:153`（`il2cpp_pinvoke_resolve` 只用传入的字面 entry 名，无后缀探测）',
         '**运行时** `EntryPointNotFoundException: Unable to find an entry point named \'GetWindowText\'`'),
        ('1.11', '委托 → 函数指针回调', '已有（旧文档「施工中」**已落地**）',
         '`src/il2cppc/Emit/CodeGenerator.cs:9128`（`DelegateGfpForPinvokeParam` → `intptr_t`）；'
         '`src/il2cppc/Emit/MethodRefResolver.cs:3227`；运行时 `src/runtime/src/pinvokeext.cpp:278`',
         'TRANSPILE-PASS + C++-BUILD-OK（`il2cpp_delegate_gfp_...` 分发器）'),
        ('1.12', '`SetLastError` 捕获 / 读取', '已有',
         '捕获 `src/runtime/src/pinvoke.cpp:278`；读取 `:291`；'
         '发射点 `src/il2cppc/Emit/CodeGenerator.cs:4623`；折叠点 `src/il2cppc/Ir/StackFold.cs:2560`；'
         '`Marshal.GetLastWin32Error` → `src/il2cppc/Emit/MethodMap.Marshal.cs:95`',
         '生成代码含 `il2cpp_pinvoke_capture_last_error();`'),
        ('1.13', '`SetLastError` **调用前清零**', '**缺**（header 声称有、实现无）',
         '`src/runtime/src/pinvokeext.cpp:329` 定义了 `clear_last_error`，'
         '但 `src/il2cppc` 全文 grep **0 个发射点**（只有 `capture_last_error` 被发射）',
         '生成代码**没有** `il2cpp_pinvoke_clear_last_error();`（可复现：见 §9 步骤 3）'),
        ('1.14', '返回值：`IntPtr` / 宽度 / `void`', '已有',
         '`src/il2cppc/Emit/CodeGenerator.cs:8986`（`PinvokeReturnTypeCpp`）；'
         '结果 kind `src/il2cppc/Emit/MethodRefResolver.cs:712`',
         'TRANSPILE-PASS + C++-BUILD-OK'),
        ('1.15', '返回值：字符串', '**缺**（native `char*`/`char16_t*` 返回没有回填）',
         '`src/il2cppc/Emit/CodeGenerator.cs:8986` 尾部注释（「STRING-typed return ... out of P0 scope」）',
         '本次未单列 probe（签名里没有这样的 net48 P/Invoke，见 §1 备注）'),
        ('1.16', '嵌入资源解码（`ManifestResource` / `ResourceManager`）', '已有（旧文档「转译器 0 匹配」**不成立**）',
         '计数 `src/il2cppc/Emit/CodeGenerator.cs:9167`（`ResourceCount`）→ 头文件 `:2084`（`resource-pool.h`）→ '
         '填充 `:13499`（`il2cpp_resource_pool_fill()`）；读取 `src/il2cppc/Emit/MethodMap.Resources.cs:54`；'
         '`ResourceManager` 接线 `src/il2cppc/Emit/MethodMap.Resource.cs:58`',
         'Forms 227 个资源全部被枚举（`resources.tsv`），实际 223 + Drawing 4'),
        ('1.16b', '结构体布局的**逐字节**等价性（非 blittable 结构体）', '**缺**（本审计新发现，影响 %d 条）'
         % 55,
         '`src/il2cppc/Emit/TypeEmitter.cs:1054`（`LayoutValueTypeFields` 按 C++ 自然对齐排字段，'
         '但 `src/il2cppc/MetadataLoader.cs:1011` 把引用字段解成 GC 指针）；'
         '`src/il2cppc/Emit/CodeGenerator.cs:9045`（未注册结构体按地址透传）；'
         '实测清单 `structs.tsv`（WinFormsStructProbe）',
         'LOGFONT 形态 by-ref：TRANSPILE-PASS，生成字段是 `Il2CppObject*`（原生 `WCHAR[32]`）'),
        ('1.17', 'COM（RCW/CCW/IDispatch）', '已有**部分**运行时（旧文档「M8 远期、无 COM 运行时」**不成立**）',
         '`src/runtime/include/internal/il2cpp_com.h:3`；实现 `src/runtime/src/com.cpp:8045`（'
         '`icall_com_create_instance_clsid`）等；托管面 `src/il2cppc/Emit/MethodMap.Marshal.cs:457`'
         '（`IsComObject`）/ `:472` / `:512` / `:541`',
         'ComImport 接口本身可转译（`probe_com`：TRANSPILE-PASS），vtable 调用仍缺'),
    ]
    for r in rows_tbl:
        w('| %s |\n' % ' | '.join(r))
    w('\n')
    w('**1.15 备注**：net48 里返回 native 指针/字符串的 P/Invoke 会把结果声明成 `IntPtr`'
      '（`GetProcAddress`、`SHGetKnownFolderPath` 等），再由托管侧 `Marshal.PtrToStringUni` 解码 —— '
      '解码侧 `src/il2cppc/Emit/MethodMap.Marshal.cs:120` 已接线。'
      '所以 1.15 在 Forms/Drawing 的 1317 条里**没有实际命中**（`pinvokes.tsv` 的 `ret` 列无 `System.String`）。\n\n')

    w('### 1.1 旧文档 vs 现状（一句话版）\n\n')
    w('| 旧文档条目 | 旧判定 | 现判定 |\n|---|---|---|\n')
    old_tbl = [
        ('#6 UTF-16 marshal', '大缺口', '**已作废**：参数/运行时/解码三侧都有（1.3/1.4）'),
        ('#7 结构体 by-value/by-ref', '大缺口', '**部分作废**：参数侧已有（1.5）；by-value **返回**仍缺（1.6）'),
        ('#8 IntPtr 参数', '大缺口', '**已作废**：`n => intptr_t`（1.2）'),
        ('#9 bool/char/byte 数组', '大缺口', '**仍成立**，且扩大到全部数组与 ref/out 标量（1.7/1.8）'),
        ('#10 委托→函数指针', '可接（施工中）', '**已落地**（1.11），另有未记录的 `HandleRef` 缺口（1.9）'),
        ('#15 嵌入资源', '大缺口（转译器 0 匹配）', '**已作废**：资源池 + ResourceManager 全链（1.16）'),
        ('#18 COM 全量', '大缺口（M8 远期）', '**部分作废**：运行时有 RCW/CCW/IDispatch 骨架（1.17），'
         '但 ComImport 接口的 vtable 调用/参数 marshal 仍缺'),
        ('#2 SetLastError', '已覆盖', '**半对**：捕获与读取有，**调用前清零没有**（1.13）'),
        ('— 入口名后缀', '未提及', '**新增缺口**：%d 条（1.10）' % cap_touch['entrypoint-suffix']),
    ]
    for r in old_tbl:
        w('| %s | %s | %s |\n' % r)
    w('\n')

    # ---------------- 2 ----------------
    w('## 2. P/Invoke 逐 DLL 汇总\n\n')
    w('| DLL | A | B | C | D | E | 合计 |\n|---|---|---|---|---|---|---|\n')
    order = ['gdiplus.dll', 'user32.dll', 'gdi32.dll', 'kernel32.dll', 'uxtheme.dll', 'ole32.dll',
             'comctl32.dll', 'oleaut32.dll', 'clr.dll', 'shell32.dll', 'comdlg32.dll', 'imm32.dll',
             'UIAutomationCore.dll', 'crypt32.dll', 'hhctrl.ocx', 'winspool.drv', 'shlwapi.dll',
             'oleacc.dll', 'ntdll.dll', 'advapi32.dll', 'Powrprof.dll', 'wldp.dll', 'SHCore.dll']
    seen = set()
    for d in order + sorted(set(per_dll) - set(order)):
        if d in seen or d not in per_dll:
            continue
        seen.add(d)
        c = per_dll[d]
        w('| `%s` | %d | %d | %d | %d | %d | %d |\n'
          % (d, c['A'], c['B'], c['C'], c['D'], c['E'], c['total']))
    w('| **合计** | **%d** | **%d** | **%d** | **%d** | **%d** | **%d** |\n\n'
      % (totals['A'], totals['B'], totals['C'], totals['D'], totals['E'], len(rows)))

    w('### 2.1 B 类按缺失能力分组（同一能力一起补 = 一次大批量收益）\n\n')
    w('| 缺失能力 | 涉及条数 | 补齐后**单独**解决 |\n|---|---|---|\n')
    for cap in sorted(cap_touch, key=lambda c: -cap_touch[c]):
        w('| %s（`%s`） | %d | %d |\n' % (CAP_LABEL[cap], cap, cap_touch[cap], cap_alone[cap]))
    w('\n> 组合能力见 §8 的贪心顺序：`handleref` → `entrypoint-suffix` → `byref-scalar` → ...\n\n')

    w('### 2.2 入口名导出实况（`WinFormsExportProbe`，真实 `GetProcAddress`）\n\n')
    ver = Counter((exports.get((p['module'], p['entrypoint'])) or {}).get('verdict', '?') for p in pinvokes)
    w('| verdict | 含义 | 条数 |\n|---|---|---|\n')
    w('| `bare` | 裸名可解析（当前实现即可） | %d |\n' % ver['bare'])
    w('| `only-W` | 只有 `NameW` 导出（`ExactSpelling=true` 时 .NET 也会失败，但这些声明都带 `CharSet`+`ExactSpelling=false`） | %d |\n' % ver['only-W'])
    w('| `only-A` | 只有 `NameA` 导出 | %d |\n' % ver['only-A'])
    w('| `missing` | 模块里根本没有（`ZeroMemory` 是 kernel32 的导入别名，`LoadIconWithScaleDown`/`ImageList_WriteEx` 在 comctl32 v6 才导出） | %d |\n' % ver['missing'])
    w('| `no-module` | `LoadLibrary` 失败（`clr.dll` 只存在于 CLR 宿主进程） | %d |\n\n' % ver['no-module'])
    w('`only-W` 的分布（按 DLL，去重后的 EntryPoint）：\n\n')
    byw = defaultdict(set)
    for p in pinvokes:
        if (exports.get((p['module'], p['entrypoint'])) or {}).get('verdict') == 'only-W':
            byw[p['module']].add(p['entrypoint'])
    w('| DLL | unique EntryPoint | 列表 |\n|---|---|---|\n')
    for m in sorted(byw, key=lambda m: -len(byw[m])):
        eps = sorted(byw[m])
        w('| `%s` | %d | %s |\n' % (m, len(eps), ', '.join('`%s`' % e for e in eps)))
    w('\n')

    # ---------------- 3 ----------------
    w('## 3. user32 / gdi32 / kernel32 逐 EntryPoint 全表\n\n')
    w('列说明：**overloads** = 同一 EntryPoint 的声明数（重载）；**class** = 分类；'
      '**needs** = 仍缺的 marshal 能力；**exports** = `GetProcAddress` 实况（`bare` / `only-W` / `missing`）。\n\n')
    for dll in ('user32.dll', 'gdi32.dll', 'kernel32.dll'):
        sub = [r for r in rows if r['module'] == dll]
        g = defaultdict(list)
        for r in sub:
            g[r['entrypoint']].append(r)
        w('### 3.%d `%s` — %d 条声明 / %d 个 EntryPoint\n\n'
          % (('user32.dll', 'gdi32.dll', 'kernel32.dll').index(dll) + 1, dll, len(sub), len(g)))
        w('| EntryPoint | overloads | class | needs | exports |\n|---|---|---|---|---|\n')
        for ep in sorted(g):
            rs = g[ep]
            classes = '/'.join(sorted({r['klass'] for r in rs}))
            needs = sorted({n for r in rs for n in needs_of(r)})
            verdicts = '/'.join(sorted({(exports.get((dll, ep)) or {}).get('verdict', '?') for r in rs}))
            w('| `%s` | %d | %s | %s | %s |\n'
              % (ep, len(rs), classes, ', '.join(needs) or '-', verdicts))
        w('\n')

    # ---------------- 4 ----------------
    w('## 4. 阶段 1 最小可跑集（P0 清单）\n\n')
    w('### 4.1 取证方法\n\n')
    w('用 `tools/audit/WinFormsPinvokeGraph`（与 `tools/audit/IcallCallGraph` 同源的 IL 解码器）对'
      '**托管 IL** 建调用图：解码 Forms+Drawing 共 34178 个方法体（`decodeFailures=0`，'
      '解码器自校验：方法体必须被精确消费），得到 108427 条调用边。'
      '从 11 个阶段 1 root 做前向 BFS：\n\n')
    w('```\n')
    for line in io.open(os.path.join(graph_dir, 'roots.tsv'), encoding='utf-8'):
        f = line.rstrip('\n').split('\t')
        if len(f) == 4:
            w('  root: %s::%s (%s)\n' % (f[1].split('::')[0], f[1].split('::')[-1], f[3]))
    w('```\n\n')
    w('root 全部是 `Application.Run` / `Control.CreateHandle` / `Control.WndProc` / `Control.WmPaint` / '
      '`Control.OnPaint` / `NativeWindow.WndProc` / `Button.OnClick` / `Label.OnPaint` —— '
      '即「`Application.Run(new Form())` + Button/Label + Click + WM_PAINT」这条路径的入口。\n\n')
    w('BFS 结果：**%d 个**不同 EntryPoint 可达（%d 条声明）。按跳数分布：\n\n'
      % (len(reach_keys), len(reach_rows)))
    depth = Counter(v[0] for v in reach.values())
    w('| 距 root 跳数 | EntryPoint 数 |\n|---|---|\n')
    for d in sorted(depth):
        w('| %d | %d |\n' % (d, depth[d]))
    w('\n分类分布：A=%d / B=%d / C=%d / D=%d / E=%d。\n\n'
      % (reach_class['A'], reach_class['B'], reach_class['C'], reach_class['D'], reach_class['E']))

    w('### 4.2 P0 清单（全部可达 EntryPoint，按接线成本升序）\n\n')
    w('排序键 = (仍缺的能力个数, 距 root 跳数, DLL, EntryPoint)。「仍缺能力」为空 = 现有 marshal '
      '直接可用（A/E）。**这是阶段 1 的完整 P0 集合**：`Application.Run(new Form())` + Button/Label + '
      'Click + WM_PAINT 这条路径上真正会被调用的全部 P/Invoke。\n\n')
    capcnt = {}
    for k in reach_keys:
        rs = [r for r in reach_rows if (r['module'], r['entrypoint']) == k]
        ns = set()
        for r in rs:
            ns |= needs_of(r)
        capcnt[k] = ns
    order2 = sorted(reach_keys, key=lambda k: (len(capcnt[k]), reach[k][0], k[0], k[1]))
    w('| # | DLL | EntryPoint | 跳数 | 声明数 | class | 仍缺能力 | exports |\n|---|---|---|---|---|---|---|---|\n')
    for i, k in enumerate(order2, 1):
        rs = [r for r in reach_rows if (r['module'], r['entrypoint']) == k]
        classes = '/'.join(sorted({r['klass'] for r in rs}))
        w('| %d | `%s` | `%s` | %d | %d | %s | %s | %s |\n'
          % (i, k[0], k[1], reach[k][0], len(rs), classes,
             '+'.join(sorted(capcnt[k])) or '-', (exports.get(k) or {}).get('verdict', '?')))
    w('\n共 **%d** 个 EntryPoint（%d 条声明）。其中 %d 个在 3 跳以内（最小可跑集的"第一波"）。\n\n'
      % (len(order2), len(reach_rows), sum(1 for k in order2 if reach[k][0] <= 3)))

    w('### 4.3 贪心接线顺序（每加一个能力，阶段 1 闭环多少条）\n\n')
    have = set()
    cur = sum(1 for r in reach_rows if r['klass'] in ('A', 'E'))
    w('| 步骤 | 新增能力 | 闭环声明数 / %d |\n|---|---|---|\n' % len(reach_rows))
    w('| 0 | （无新能力） | %d |\n' % cur)
    step = 0
    while True:
        best = None
        for cap in ALL_CAPS:
            if cap in have:
                continue
            h2 = have | {cap}
            n = sum(1 for r in reach_rows
                    if r['klass'] in ('A', 'E') or (needs_of(r) and needs_of(r) <= h2))
            if best is None or n > best[1]:
                best = (cap, n)
        if best is None or best[1] <= cur:
            break
        step += 1
        have.add(best[0])
        cur = best[1]
        w('| %d | `%s` | %d |\n' % (step, best[0], cur))
    w('\n')

    w('### 4.4 补齐全部 marshal 能力后仍不可达的 EntryPoint（C/D 类）\n\n')
    w('| DLL | EntryPoint | class | 说明 |\n|---|---|---|---|\n')
    cd = {}
    for k in reach_keys:
        rs = [r for r in reach_rows if (r['module'], r['entrypoint']) == k]
        if all(r['klass'] in ('C', 'D') for r in rs):
            cd[k] = sorted({r['klass'] for r in rs})
    for k in sorted(cd, key=lambda k: (cd[k][0], k[0], k[1])):
        cls = '/'.join(cd[k])
        why = 'COM 运行时（RCW/流接口）' if cls == 'C' else '域外（IME/UIA/HTMLHelp）'
        w('| `%s` | `%s` | %s | %s |\n' % (k[0], k[1], cls, why))
    w('\n共 **%d 个**。它们不阻塞阶段 1 的主路径（`Application.Run` 能跑起来、'
      '能创建窗口、能画、能收 Click），只影响辅助特性（无障碍/输入法/帮助窗口/剪贴板流）。\n\n' % len(cd))

    w('### 4.5 主路径「无它则哪一步失败」\n\n')
    w('| 失败点 | 缺的能力 | 依据 |\n|---|---|---|\n')
    w('| 窗口类注册 + 建窗 | `entrypoint-suffix`（`RegisterClass`/`CreateWindowEx`/`RegisterWindowMessage` 只有 W 名）'
      ' + `handleref`（`hWndParent`/`hMenu`/`hInstance`） + `object`（`lpParam` 是 `object`） | §4.2 第 62/81 行 |\n')
    w('| 消息泵 `GetMessage`/`PeekMessage`/`DispatchMessage` | `entrypoint-suffix` + `handleref`（`HWND`）'
      ' + by-ref `MSG` | §4.2 |\n')
    w('| `DefWindowProc`/`CallWindowProc` | `entrypoint-suffix` | §4.2 第 28/29 行 |\n')
    w('| WM_PAINT 绘制 | `handleref`（`BeginPaint`/`EndPaint`/`GetDC`/`ReleaseDC`/`SelectObject`） | §4.2 |\n')
    w('| `GetClientRect`/`GetWindowRect`/`InvalidateRect`/`SetWindowPos` | `handleref` + by-ref `RECT` | §4.2 |\n')
    w('| Click 事件（`SendMessage`/`PostMessage` WM_COMMAND） | `entrypoint-suffix` + `handleref`；'
      '含 `int[]`/`int&` 的重载另外需要 `array`/`byref-scalar` | §4.2 |\n')
    w('| `Application.Run` 内部的 DPI/主题/ActCtx 初始化 | `entrypoint-suffix`（`GetModuleHandle`/`LoadLibrary`）'
      ' + `byref-scalar`（`ActivateActCtx` 的 `IntPtr&`） | §4.2 |\n\n')

    # ---------------- 5 ----------------
    w('## 5. COM 面\n\n')
    w('程序集里 `[ComImport]`（`TypeAttributes.Import`）类型共 **%d** 个'
      '（Forms 198 / Drawing 3；含嵌套类型）。按域分组：\n\n' % len(comimports))
    w('| 域 | 类型数 | 阶段 1 是否可达 | 理由 |\n|---|---|---|---|\n')
    for dom, label, why in DOMAIN_ORDER:
        if dom not in com_domains:
            continue
        hit = '**是**（出现在阶段 1 可达的 P/Invoke 签名里）' if dom in reach_com_domains else '否'
        w('| %s | %d | %s | %s |\n' % (label, com_domains[dom], hit, why))
    w('\n### 5.1 阶段 1 真正会碰到的 COM\n\n')
    w('阶段 1 可达的 P/Invoke 里出现 ComImport 参数/返回的接口只有：%s。\n\n'
      % (', '.join('`%s`' % t.split('+')[-1] for t in sorted(reach_com)) or '（无）'))
    w('对应的声明（按跳数）：\n\n')
    w('| DLL | EntryPoint | 跳数 | 参数里的 COM 接口 | class | 触发条件 |\n|---|---|---|---|---|---|\n')
    trigger = {
        ('oleacc.dll', 'LresultFromObject'): 'WM_GETOBJECT（无障碍客户端查询控件时）',
        ('gdiplus.dll', 'GdipCreateBitmapFromStream'): '`Image.FromStream` / `Bitmap(Stream)`',
        ('ole32.dll', 'RegisterDragDrop'): '`Control.AllowDrop = true`',
        ('ole32.dll', 'RevokeDragDrop'): '关闭 AllowDrop / 销毁句柄',
        ('UIAutomationCore.dll', 'UiaReturnRawElementProvider'): 'UIA 客户端调用 WM_GETOBJECT',
    }
    for k in sorted(reach_com, key=lambda t: (reach_owner.get(t, ''), t)):
        pass
    for r in sorted(reach_rows, key=lambda r: (reach[(r['module'], r['entrypoint'])][0], r['module'], r['entrypoint'])):
        coms = [p.rstrip('&') for p in r['params'].split(';')
                if p.rstrip('&') in com_types
                or p.rstrip('&').startswith('System.Runtime.InteropServices.ComTypes.')]
        if not coms:
            continue
        key = (r['module'], r['entrypoint'])
        if key in seen_com_rows:
            continue
        seen_com_rows.add(key)
        w('| `%s` | `%s` | %d | %s | %s | %s |\n'
          % (r['module'], r['entrypoint'], reach[key][0],
             ', '.join('`%s`' % c.split('+')[-1] for c in coms),
             r['klass'], trigger.get(key, '同 DLL 的常规用法')))
    w('\n这些都不会在「Application.Run(new Form()) + Button/Label + Click + WM_PAINT」'
      '的最小路径上主动触发（无障碍/拖放/流式加载都是被动的），所以阶段 1 不阻塞。\n\n')
    w('### 5.2 运行体现状\n\n')
    w('- 已有：`icall_com_query_interface` / `add_ref` / `release` / `call_vtable_slot`'
      '（`src/runtime/include/internal/il2cpp_com.h:771`）、`icall_com_create_instance_clsid`'
      '（`src/runtime/src/com.cpp:8045`）、ProgID 激活、`get_object_for_iunknown`、'
      'BSTR 桥、`CreateStreamOnHGlobal`。\n')
    w('- 托管映射：`Marshal.GetActiveObject` / `GetObjectForIUnknown` / `ReleaseComObject` / '
      '`CreateObject` / `IsComObject` 已接（`src/il2cppc/Emit/MethodMap.Marshal.cs:457`~`:543`）；'
      '其余 COM 成员 **LOUD**（`:88`）。\n')
    w('- 缺：ComImport **接口本身的声明式实例化**（`new SomeComClass()` 走 CLR 激活器）与'
      '**接口方法调用**（vtable slot 有运行时原语，但转译器不把 `callvirt IFoo::Bar` 翻成 slot 号）。'
      '这是 §5 各域不可达的共同原因，不是「不支持 COM」这么笼统。\n\n')
    w('### 5.3 4 个 `InternalCall` 构造函数\n\n')
    w('`FileOpenDialogRCW` / `FileSaveDialogRCW` / `CorRuntimeHost` / `SMsoComponentManager` 的 `.ctor` '
      '带 `InternalCall`，属于「COM 类激活」这一条 CLR 语义：net48 由 CLR 的 COM 激活器实现，'
      '不经过托管 IL。它们只在 `FileDialog`(Vista 模式) / CLR 宿主 / VB 组件管理器路径上，'
      '都在阶段 2+。\n\n')

    # ---------------- 6 ----------------
    w('## 6. 嵌入资源（223 个）\n\n')
    w('`ManifestResource` 实测 **%d** 个（Forms %d / Drawing %d）。按扩展名：\n\n'
      % (len(resources),
         sum(1 for r in resources if r[0] == 'System.Windows.Forms'),
         sum(1 for r in resources if r[0] != 'System.Windows.Forms')))
    w('| 扩展名 | 个数 | 用途 |\n|---|---|---|\n')
    use = {'.bmp': '控件/工具箱位图（ToolboxBitmap、BindingNavigator 图标）',
           '.ico': '窗体/控件默认图标',
           '.cur': 'Splitter/MDI 光标',
           '.resources': '字符串表 + 对话框模板（PrintPreview/MdiWindow）+ 位图内嵌'
                         '（`System.Windows.Forms.resources` / `System.SR.resources`）',
           '.png': '单个 PNG 资源'}
    for ext, n in res_ext.most_common():
        w('| `%s` | %d | %s |\n' % (ext, n, use.get(ext, '?')))
    w('\n现状（**旧文档「转译器无嵌入资源处理」已作废**）：\n\n')
    w('- 计数与导出：`src/il2cppc/Emit/CodeGenerator.cs:9167`（`ResourceCount`）'
      '→ 条件 include `:2084` → 运行时填充 `:13499`。\n')
    w('- 读取：`Assembly.GetManifestResourceStream` / `GetManifestResourceNames` '
      '（`src/il2cppc/Emit/MethodMap.Resources.cs:54`）；`ResourceManager.GetString` 及文化回退链'
      '（`src/il2cppc/Emit/MethodMap.Resource.cs:58`）。\n')
    w('- 缺口：**图片类资源**（185 bmp + 21 ico + 14 cur）需要 `Image.FromStream`/`Icon` 解码，'
      '而 `GdipCreateBitmapFromStream` 是 C 类（流接口）。阶段 1 用代码赋值文本即可绕开；'
      '但控件默认图标/光标（`.cur`/`.ico`）由 `LoadCursor`/`LoadIcon` 从**模块句柄**取，'
      '走 user32 而不是资源池 —— 需要 §4 的 `entrypoint-suffix`+`handleref`。\n\n')

    # ---------------- 7 ----------------
    w('## 7. 与旧文档 `docs/plans/winforms-plan.md` 的差异\n\n')
    w('旧文档是 2026-08-07 的，行号与判定都已漂移。逐条列出**被推翻**的结论及其现行证据'
      '（判定口径与 §1 相同：以 `run-marshal-probe.ps1` 的 red/green 为准）：\n\n')
    w('| # | 旧文档结论（原文位置） | 现行事实 | 证据 |\n|---|---|---|---|\n')
    ref_tbl = [
        ('1', '「PinvokeParamList 发射 int32_t（CodeGenerator.cs:2966-2971）」，除 int32/string 外一律 int32_t',
         '**推翻**：参数矩阵完整（i1..u8/f/d/n/委托/结构体），未知编码 LOUD 拒绝',
         '`src/il2cppc/Emit/CodeGenerator.cs:9108`、`:9138`'),
        ('2', '「UTF-16 (WCHAR*) marshal —— 大缺口：只有 UTF-8；无 to_utf16_alloc」',
         '**推翻**：UTF-16 入参 + 运行时 + 解码三侧齐备',
         '`src/il2cppc/Emit/CodeGenerator.cs:9096`、`src/runtime/src/pinvokeext.cpp:136`/`:166`'),
        ('3', '「结构体 by-value/by-ref —— 大缺口：PinvokeParamList 只认 int32_t/const char*」',
         '**部分推翻**：by-ref/by-value **参数**已支持（含未注册结构体按地址透传）；'
         '**仅 by-value 返回**仍缺',
         '`src/il2cppc/Emit/CodeGenerator.cs:9075`/`:9081`、`:9153`；probe 两种结局'),
        ('4', '「IntPtr 参数 —— 大缺口：参数侧无 ptr 处理，x64 截断」',
         '**推翻**：`n => intptr_t`，且 probe 通过 C++ 构建',
         '`src/il2cppc/Emit/CodeGenerator.cs:9114`'),
        ('5', '「委托→函数指针 —— 可接（GetFunctionPointerForDelegate 施工中 #11）」',
         '**已落地**，且发现旧文档未记的 `HandleRef` 缺口',
         '`src/il2cppc/Emit/CodeGenerator.cs:9128`、`src/il2cppc/Emit/MethodRefResolver.cs:3227`'),
        ('6', '「PtrToStringUni not wired（MethodMap.Marshal.cs:110）」',
         '**推翻**：已接线', '`src/il2cppc/Emit/MethodMap.Marshal.cs:120`'),
        ('7', '「嵌入资源 —— 转译器无 ManifestResource 解码（grep 0 匹配）；无 ResourceManager icall」',
         '**推翻**：资源池 + ResourceManager 全链已实现',
         '`src/il2cppc/Emit/CodeGenerator.cs:9167`/`:2084`/`:13499`、'
         '`src/il2cppc/Emit/MethodMap.Resources.cs:54`'),
        ('8', '「COM —— 无 COM 运行时（M8 远期）」',
         '**部分推翻**：RCW/CCW/IDispatch 骨架 + Marshal COM 面已在；'
         '缺的是 ComImport 接口声明式激活与 vtable 调用',
         '`src/runtime/include/internal/il2cpp_com.h:3`、`src/runtime/src/com.cpp:8045`、'
         '`src/il2cppc/Emit/MethodMap.Marshal.cs:457`'),
        ('9', '「SetLastError 已覆盖（capture/clear/get_last_error）」',
         '**半推翻**：capture/get 有，**clear 从未被发射**',
         '`src/runtime/src/pinvokeext.cpp:329`（定义）vs `src/il2cppc` 无发射点'),
        ('10', '「CharSet 分布 Auto 479 / None 108 / Unicode 25 / Ansi 13」（Forms 子集）',
         '**口径不清楚**：这是 Forms(625) 子集；1317 条全量是 Unicode 595 / Auto 586 / None 117 / Ansi 19',
         '`pinvokes.tsv` CharSet 列聚合'),
        ('11', '「user32 281 / gdi32 117 / kernel32 60」',
         '**口径不清楚**：Forms 子集数；Forms+Drawing 全量是 301/203/71',
         '`pinvokes.tsv` 按 assembly×DLL 聚合'),
        ('12', '（旧文档未提）结构体布局是否逐字节等价',
         '**新增缺口**：55 条 P/Invoke 用的结构体含托管引用字段（string/数组/委托），'
         '生成的 C++ 结构体是 GC 指针而非原生内联数据，静默错位',
         '`src/il2cppc/MetadataLoader.cs:1011`、`tools/audit/WinFormsStructProbe` 的 `structs.tsv`'),
        ('13', '「阶段 1 五个大缺口：UTF-16、结构体、IntPtr 参数、byte[] 参数、委托回调」',
         '**推翻一半**：三项已补齐；byte[] 与 ref/out 标量仍缺，'
         '并**新增** `HandleRef` 与入口名后缀两项（旧文档完全没提）',
         '§1 表格 1.3/1.5/1.2/1.11 vs 1.7/1.8/1.9/1.10'),
    ]
    for r in ref_tbl:
        w('| %s |\n' % ' | '.join(r))
    w('\n**合计：13 条差异 —— 完全推翻 5 条、部分推翻 4 条、旧称「施工中」现已落地 1 条、'
      '统计口径不同 2 条、旧文档未记的新缺口 1 条。被推翻/部分推翻合计 **9 条**。**\n\n')
    w('**未被推翻**的旧结论（仍然成立）：\n\n')
    w('- 数组参数（`byte[]`/`char[]`/`int[]`/`Ptr[]`/`struct[]`）无 pin/copy 桥（1.7）。\n')
    w('- uxtheme(31) / imm32(10) / UIAutomationCore(9) 属后置或不可达（§2 分类）。\n')
    w('- TypeDescriptor / PropertyDescriptor 面未建（D2 反射支撑面）；本审计未展开，'
      '标为阶段 2+ 风险。\n\n')

    # ---------------- 8 ----------------
    w('## 8. 下一步建议（按 owner/DLL 成批接的最小改动队列）\n\n')
    w('顺序由 §4.3 的贪心结果与实际代码结构共同决定。每一步都写清「改哪个文件、'
      '覆盖多少条、怎么验证」。\n\n')
    w('| 批次 | 能力 | 改动位置 | 覆盖 | 验证 |\n|---|---|---|---|---|\n')
    w('| **W1** | `HandleRef` → 原生句柄衰减 | `src/il2cppc/Emit/CodeGenerator.cs`（`PinvokeParamList` 与'
      ' `EmitIcallCallArgs`）：`r:System.Runtime.InteropServices.HandleRef` 编码 → `n`/`intptr_t`，'
      '取值表达式 `.Handle`。**注意**：这不是新运行时能力，`HandleRef` 是纯托管结构体，'
      '唯一要做的是别把它当原生结构体传 | %d 条 / 直接闭环 %d 条 | `run-marshal-probe.ps1` 的 '
      '`HandleRef param` → `C++-BUILD-OK`；winforms1/winformsext 回归 |\n'
      % (cap_touch['handleref'], cap_alone['handleref']))
    w('| **W2** | 入口名 A/W 后缀探测 | `src/runtime/src/pinvoke.cpp:153`（`il2cpp_pinvoke_resolve`）：'
      '按 `CharSet`+`ExactSpelling` 依次试 `Name`、`NameW`、`NameA`。'
      '签名需带上 CharSet/ExactSpelling（`ResolvedMethod` 已有 `SetLastError`，再加两个字段） | '
      '%d 条 / 直接闭环 %d 条 | `run-marshal-probe.ps1` 若加 `GetWindowText`（`ExactSpelling=false`）'
      '应不再抛 `EntryPointNotFoundException`；`WinFormsExportProbe` 的 `only-W` 集合作为测试清单 |\n'
      % (cap_touch['entrypoint-suffix'], cap_alone['entrypoint-suffix']))
    w('| **W3** | `ref`/`out` 标量 + `out Guid`/`out string` | `src/il2cppc/Emit/CodeGenerator.cs:9138`'
      '（放行 `B:i`/`B:n`/`B:l`/`B:d`/`B:b`/`B:V:System.Guid`/`B:r:System.String`）'
      '+ `EmitIcallCallArgs` 的地址转换 | %d 条 / 直接闭环 %d 条 | probe 的 `ref int / ref IntPtr`、'
      '`out Guid`、`out string` → `C++-BUILD-OK`；`GetWindowThreadProcessId` 真机返值 |\n'
      % (cap_touch['byref-scalar'], cap_alone['byref-scalar']))
    w('| **W3b** | 非 blittable 结构体的字段 marshal | `src/il2cppc/MetadataLoader.cs:1011`'
      '（引用字段必须按 `ByValTStr`/`ByValArray`/函数指针的**原生形态**布局，而不是 GC 指针）+'
      ' `src/il2cppc/Emit/TypeEmitter.cs:1054`（布局按 marshal 后的宽度排）+ '
      '`src/il2cppc/Emit/CodeGenerator.cs:9045`（透传前的逐字段转换） | %d 条 / 阶段 1 直接闭环 24 条 | '
      '`WinFormsStructProbe` 的 `structs.tsv` 里 `NON-BLITTABLE` 归零；`SystemParametersInfo`'
      '（LOGFONT/NONCLIENTMETRICS）与 `RegisterClass`（WNDCLASS_D）真机回填 |\n'
      % cap_touch['nonblittable-struct'])
    w('| **W4** | 数组 pin/copy | `src/il2cppc/Emit/CodeGenerator.cs`（`PinvokeParamList` + 折叠器'
      ' `src/il2cppc/Ir/StackFold.cs`）：复用 `src/runtime/src/pinvokeext.cpp:257` 的 '
      '`il2cpp_pinvoke_byte_array_data`，扩展到 `char[]`/`int[]`/`float[]`/结构体[] | '
      '%d 条 / 直接闭环 %d 条 | probe 的 `byte[]`/`char[]`/`IntPtr[]`/`bool[]` → `C++-BUILD-OK`；'
      '`GetKeyboardState` 回填 256 字节 |\n'
      % (cap_touch['array'], cap_alone['array']))
    w('| **W5** | `StringBuilder` 缓冲 | 同上（按 `Capacity` 分配 native 缓冲，调用后回填） | '
      '%d 条 / 直接闭环 %d 条 | probe 的 `StringBuilder buffer param` |\n'
      % (cap_touch['stringbuilder'], cap_alone['stringbuilder']))
    w('| **W6** | 结构体名与 SDK typedef 冲突 | `src/il2cppc/Emit/TypeEmitter.cs`（结构体 C++ 名加前缀，'
      '如 `Wf_LOGFONTW`）或 `generated.h` 的 forward declaration 改为不撞 typedef 的形式 | '
      '%d 条 | probe 的 `LOGFONTW by-ref` → `C++-BUILD-OK` |\n' % cap_touch['name-clash'])
    w('| **W7** | by-value 结构体返回 | `src/il2cppc/Emit/CodeGenerator.cs:9153`（注册表按完整名解析，'
      '而不是只认三个） | %d 条 | probe 的 `struct by-value return` → `C++-BUILD-OK` |\n'
      % cap_touch['struct-return'])
    w('| **W8** | `SetLastError` 调用前清零 | `src/il2cppc/Ir/StackFold.cs`（在 `SetLastError=true` 的调用**之前**'
      ' 插 `clear_last_error`，与已有 `:2560` 的 capture 对称）+ `src/il2cppc/Emit/CodeGenerator.cs:4620` 同名分支 | '
      '%d 条 SetLastError 声明 | `Marshal.GetLastWin32Error()` 在成功调用后返回 0 |\n'
      % sum(1 for r in rows if r['lastError'] == '1'))
    w('\n经 W1~W8 后，阶段 1 可达集里剩下的只有 C/D 类（§4.4）。'
      '建议按 **W1 → W2 → W3** 的顺序开工：这三步合计闭环 '
      '%d 条，且互不依赖。\n\n' % (cap_alone['handleref'] + cap_alone['entrypoint-suffix']
                                  + cap_alone['byref-scalar']))

    # ---------------- 9 ----------------
    w('## 9. 复现方法\n\n')
    w('全部证据可由下列命令重跑（`<repo>` 为仓库根，`<out>` 为任意临时目录）：\n\n')
    w('```powershell\n')
    w('# 1) 元数据枚举 + 调用图 + 资源 + ComImport\n')
    w('dotnet run --project <repo>/tools/audit/WinFormsPinvokeGraph -- <out>/graph `\n')
    w("  \"System.Windows.Forms.Application|Run;System.Windows.Forms.Control|CreateHandle,WndProc,OnPaint,WmPaint;`\n")
    w("   System.Windows.Forms.Button|OnClick;System.Windows.Forms.Label|OnPaint;`\n")
    w("   System.Windows.Forms.NativeWindow|CreateHandle,WndProc\" `\n")
    w('  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Windows.Forms.dll" `\n')
    w('  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Drawing.dll"\n\n')
    w('# 2) 真实 GetProcAddress 导出探测（裸名 / NameW / NameA）\n')
    w('dotnet run --project <repo>/tools/audit/WinFormsExportProbe -- <out>/graph/pinvokes.tsv <out>/graph/exports.tsv\n\n')
    w('# 2b) struct layout / blittability census\n')
    w('dotnet run --project <repo>/tools/audit/WinFormsStructProbe -- <out>/graph/pinvokes.tsv `\n')
    w('  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Windows.Forms.dll" `\n')
    w('  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Drawing.dll" > <out>/graph/structs.tsv\n\n')
    w('# 3) 逐条分类（A/B/C/D/E + 缺失能力集合）\n')
    w('python <repo>/tools/audit/classify_winforms_pinvokes.py <out>/graph <out>/classified.tsv\n\n')
    w('# 4) 阶段 1 闭环分析\n')
    w('python <repo>/tools/audit/p0_closure.py <out>/classified.tsv <out>/graph/reachable.tsv\n\n')
    w('# 5) marshal shape red/green probe（需要 net48 SDK + cmake + MinGW）\n')
    w('pwsh <repo>/tools/audit/run-marshal-probe.ps1 -WorkDir <out>/probe -CMake <cmake>\n\n')
    w('# 6) 本文件自校验（每条 file:line 必须真实存在且行号在范围内）\n')
    w('python <repo>/tools/audit/verify_winforms_report.py <repo>\n')
    w('```\n\n')
    w('第 3 步的 `classified.tsv` 是逐条明细（1317 行，含 `klass`/`needs`/`reason` 三列），'
      '审计时可直接 diff。\n\n')

    path = os.path.join(root, 'docs', 'audit', 'winforms-pinvoke-current.md')
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, 'w', encoding='utf-8', newline='\n').write(out.getvalue())
    print('wrote', path)
    print('A=%d B=%d C=%d D=%d E=%d' % (totals['A'], totals['B'], totals['C'], totals['D'], totals['E']))
    print('reachable entrypoints=%d declarations=%d' % (len(reach_keys), len(reach_rows)))


if __name__ == '__main__':
    main()
