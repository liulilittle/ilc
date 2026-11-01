# System.Windows.Forms / System.Drawing P/Invoke 现状盘点（2026-10-31）

> 只读审计，基线 `a542e5b4`。以仓库**当前**代码为准，不引用旧文档数字。
> 输入程序集：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll`
> 与 `...\System.Drawing.dll`（net48 GAC）。
> 本文由 `tools/audit/render_winforms_report.py` 从工具输出**生成**，所有数字可复现（见 §9）。

## 0. 结论摘要

### 0.1 1317 条的分类分布

| 类 | 含义 | 条数 | 占比 |
|---|---|---|---|
| **A** | 现有 marshal 即可（只需接线/声明） | 106 | 8.0% |
| **B** | 需新增 marshal 能力 | 1101 | 83.6% |
| **C** | 需 COM 运行时（RCW/CCW/IDispatch） | 50 | 3.8% |
| **D** | 阶段 1 不可达（域不在范围内） | 53 | 4.0% |
| **E** | 托管/运行时侧实现即可 | 7 | 0.5% |
| | **合计** | **1317** | 100% |

要点：

- **B 类占到 84%**；其中 983 条（75%）涉及**同一个**能力 —— `HandleRef` → native handle 的衰减，且 617 条**只差这一个**（见 §1.9 与 §8）。这是整份盘点的最大结论。
- **A 类只有 106 条**，且集中在 gdiplus 之外的 DLL：gdiplus 的 566 条里只有 4 条不需要新能力。
- **入口名后缀探测**（`ExactSpelling=false` + A/W-only 导出）是第二个被漏掉的能力：174 条 P/Invoke 会转译成功、运行时抛 `EntryPointNotFoundException`（§1.10 有 red 证据）。
- **InternalCall = 0**（方法层）已复核：Forms 0 / Drawing 0。但有 **4 个 ComImport 类的 `.ctor`** 带 `InternalCall` 位（`impl=4099`），它们由 CLR 的 COM 激活器实现，不是普通方法 —— 这 4 条是 COM 面的一部分（§5.3）。
- 阶段 1 从 `Application.Run` / `Control.WndProc` / `Control.WmPaint` 等 11 个 root 前向 BFS 可达 **327 个** 不同 EntryPoint（529 条声明），其中 85 条落在 3 跳以内。
- 把 §1 里 **全部** B 类能力补齐后，阶段 1 可达集里仍剩 **20 个** EntryPoint 属于 C 类（COM）或 D 类（域外），它们不是 marshal 能力能解决的，明细见 §4.4/§4.5。

### 0.2 `InternalCall = 0` 的证据

`tools/audit/WinFormsPinvokeGraph` 直接读 `MethodDef.ImplAttributes` 位：

```
internalcalls=4
  icall System.Windows.Forms System.Windows.Forms.FileDialogNative+FileOpenDialogRCW::.ctor rva=0
  icall System.Windows.Forms System.Windows.Forms.FileDialogNative+FileSaveDialogRCW::.ctor rva=0
  icall System.Windows.Forms System.Windows.Forms.UnsafeNativeMethods+CorRuntimeHost::.ctor rva=0
  icall System.Windows.Forms System.Windows.Forms.UnsafeNativeMethods+SMsoComponentManager::.ctor rva=0
```

反射通道（`GetMethods` 只枚举方法不枚举构造函数）得到 **0**；把构造函数一并枚举后得到同 4 条，且这 4 个类型都带 `Import`（`TypeAttributes.Import = 0x1000`，即 `[ComImport]`）—— 它们是 COM 类的激活构造函数，`MethodImplAttributes` 恰为 `4099 = InternalCall|Runtime|Managed`。所以「Forms 里的 internal C++ 调用不存在」这个结论仍然成立：**方法与构造函数合计 4 条，全部属于 COM 激活器**。

`Runtime` 方法 465（Forms）/ 42（Drawing）全部是委托的 `Invoke/BeginInvoke/EndInvoke`，非委托的真实 native 方法 **0**。

## 1. 复核表格：marshal 能力现状（逐项 file:line）

这一节是本审计最重要的一节。旧文档 `docs/plans/winforms-plan.md`（2026-08-07）的「大缺口」清单已被后续批次部分补掉，下面逐项给出**当前**证据，并以 `tools/audit/run-marshal-probe.ps1` 的 red probe 作为可执行证明（每个 shape 单独一个 net48 程序集，编译 → 转译 → C++ 构建，任一阶段失败即打印诊断）。

| # | 能力 | 当前状态 | 证据（file:line） | red probe |
|---|---|---|---|---|
| 1.1 | 标量全宽度（i1/i2/i4/i8/u4/u8/f/d） | 已有 | `src/il2cppc/Emit/CodeGenerator.cs:9108`（switch 分支 `l/q/f/d/n/i/b/c/i1/u1/i2/u2/tr`） | TRANSPILE-PASS + C++-BUILD-OK |
| 1.2 | native-int（IntPtr/UIntPtr）**参数** | 已有（旧文档「大缺口」**不成立**） | `src/il2cppc/Emit/CodeGenerator.cs:9114`（`"n" => "intptr_t"`）；签名解码 `src/il2cppc/Emit/MethodSignatureCoder.cs:365`（`0x18 => "n"`） | TRANSPILE-PASS + C++-BUILD-OK |
| 1.3 | UTF-16 字符串**入参**（CharSet.Unicode/Auto） | 已有（旧文档「大缺口」**不成立**） | `src/il2cppc/Emit/CodeGenerator.cs:9061`（CharSet mask 解码）→ `:9096`（`const char16_t*`）；`src/il2cppc/Emit/MethodRefResolver.cs:684`（`u16:System.String` 编码） | TRANSPILE-PASS + C++-BUILD-OK |
| 1.4 | UTF-16 运行时编解码 | 已有 | `src/runtime/src/pinvokeext.cpp:136`（`string_to_utf16_alloc`）/ `:156`（`utf16_free`）/ `:166`（`string_from_utf16`）/ `:186`（`_len`） | 同上（调用点 `src/il2cppc/Emit/CodeGenerator.cs:5364` alloc / `src/il2cppc/Emit/CodeGenerator.cs:4637` free） |
| 1.5 | 结构体 by-ref / by-value **参数** | 已有（旧文档「大缺口」**不成立**） | `src/il2cppc/Emit/CodeGenerator.cs:9075`（`B:V:` → 结构体指针）/ `:9081`（`V:` → 值）；注册表 `src/runtime/include/internal/il2cpp_pinvoke_struct.h:56`（Point/Rect/Msg）；未注册结构体按地址透传 `src/il2cppc/Emit/CodeGenerator.cs:9045` | by-ref: TRANSPILE-PASS + C++-BUILD-OK；by-value: TRANSPILE-PASS + C++-BUILD-OK |
| 1.6 | 结构体 by-value **返回**（未注册类型） | **缺**（声明退回 `int32_t`） | `src/il2cppc/Emit/CodeGenerator.cs:9153`（注册表只有 Point/Rectangle/Message）；`src/il2cppc/Emit/CodeGenerator.cs:9157`（`_ => null` 落到 `int32_t`） | TRANSPILE-PASS 但 **C++-BUILD-FAIL**（`request for member cx in il2cpp_pinvoke_call<int32_t>`） |
| 1.7 | `byte[]` / `char[]` / `int[]` 数组参数 | **缺**（旧文档「大缺口」**成立**） | `src/il2cppc/Emit/CodeGenerator.cs:9138`（`r:`/`F:`/`P:`/`B:` 一律 LOUD 抛）与 `:9142`（异常消息）；运行时 helper `src/runtime/src/pinvokeext.cpp:257` 存在但**无发射路径** | TRANSPILE-FAIL `P/Invoke parameter type 'r:u1[]' ... not supported` |
| 1.8 | `ref`/`out` 标量（`int&` / `IntPtr&`） | **缺** | 同 1.7 的 LOUD 分支 `src/il2cppc/Emit/CodeGenerator.cs:9138`（`B:` 前缀）；只有注册结构体的 `B:V:` 在 `:9075` 被放行 | TRANSPILE-FAIL `P/Invoke parameter type 'B:i' ... not supported` |
| 1.9 | `HandleRef` 参数 | **缺**（本审计新发现，影响 983 条） | `src/il2cppc/Emit/CodeGenerator.cs:9128`（只有委托 `r:` 被特判）→ `src/il2cppc/Emit/CodeGenerator.cs:9153`（HandleRef 无衰减路径）。net48 把 HandleRef 作为**特殊**结构体：只传 `.Handle`（一个 IntPtr），wrapper 对象只用于保活；当前发射器按普通值类型（`V:`）传整个 16 字节结构体，即使类型能声明，ABI 也不对 | TRANSPILE-PASS 但 **C++-BUILD-FAIL**（`System_Runtime_InteropServices_HandleRef_class` 未声明） |
| 1.10 | 入口名 A/W 后缀探测（`ExactSpelling=false`） | **缺**（本审计新发现，影响 174 条） | `src/runtime/src/pinvoke.cpp:153`（`il2cpp_pinvoke_resolve` 只用传入的字面 entry 名，无后缀探测） | **运行时** `EntryPointNotFoundException: Unable to find an entry point named 'GetWindowText'` |
| 1.11 | 委托 → 函数指针回调 | 已有（旧文档「施工中」**已落地**） | `src/il2cppc/Emit/CodeGenerator.cs:9128`（`DelegateGfpForPinvokeParam` → `intptr_t`）；`src/il2cppc/Emit/MethodRefResolver.cs:3227`；运行时 `src/runtime/src/pinvokeext.cpp:278` | TRANSPILE-PASS + C++-BUILD-OK（`il2cpp_delegate_gfp_...` 分发器） |
| 1.12 | `SetLastError` 捕获 / 读取 | 已有 | 捕获 `src/runtime/src/pinvoke.cpp:278`；读取 `:291`；发射点 `src/il2cppc/Emit/CodeGenerator.cs:4623`；折叠点 `src/il2cppc/Ir/StackFold.cs:2560`；`Marshal.GetLastWin32Error` → `src/il2cppc/Emit/MethodMap.Marshal.cs:95` | 生成代码含 `il2cpp_pinvoke_capture_last_error();` |
| 1.13 | `SetLastError` **调用前清零** | **非缺口（net48 语义就是不清零）** | net48 oracle 实测：`SetLastError(126)` → `GetCurrentProcess()`（`SetLastError=true`、成功且不改错）→ `Marshal.GetLastWin32Error()` = **126**（陈旧值保留），原生 `GetLastError()` 也是 126；预置 0 时读回 0。**清零是 .NET Core 3.0+ 行为**（`reference/coreclr` 与 `reference/netcore` 均有 `StubHelpers::ClearLastError`，netfx 无）。运行时 `src/runtime/src/pinvokeext.cpp:329` 的 `il2cpp_pinvoke_clear_last_error` 属**无人调用的死代码** | 转译输出**不含** `il2cpp_pinvoke_clear_last_error()`，且必须继续不含：`tests/cases/probe-lasterror`（10 records，`ok=True`，oracle sha = runtime sha = `86c8e322…`）钉住该语义 |
| 1.14 | 返回值：`IntPtr` / 宽度 / `void` | 已有 | `src/il2cppc/Emit/CodeGenerator.cs:8986`（`PinvokeReturnTypeCpp`）；结果 kind `src/il2cppc/Emit/MethodRefResolver.cs:712` | TRANSPILE-PASS + C++-BUILD-OK |
| 1.15 | 返回值：字符串 | **缺**（native `char*`/`char16_t*` 返回没有回填） | `src/il2cppc/Emit/CodeGenerator.cs:8986` 尾部注释（「STRING-typed return ... out of P0 scope」） | 本次未单列 probe（签名里没有这样的 net48 P/Invoke，见 §1 备注） |
| 1.16 | 嵌入资源解码（`ManifestResource` / `ResourceManager`） | 已有（旧文档「转译器 0 匹配」**不成立**） | 计数 `src/il2cppc/Emit/CodeGenerator.cs:9167`（`ResourceCount`）→ 头文件 `:2084`（`resource-pool.h`）→ 填充 `:13499`（`il2cpp_resource_pool_fill()`）；读取 `src/il2cppc/Emit/MethodMap.Resources.cs:54`；`ResourceManager` 接线 `src/il2cppc/Emit/MethodMap.Resource.cs:58` | Forms 227 个资源全部被枚举（`resources.tsv`），实际 223 + Drawing 4 |
| 1.16b | 结构体布局的**逐字节**等价性（非 blittable 结构体） | **缺**（本审计新发现，影响 55 条） | `src/il2cppc/Emit/TypeEmitter.cs:1054`（`LayoutValueTypeFields` 按 C++ 自然对齐排字段，但 `src/il2cppc/MetadataLoader.cs:1011` 把引用字段解成 GC 指针）；`src/il2cppc/Emit/CodeGenerator.cs:9045`（未注册结构体按地址透传）；实测清单 `structs.tsv`（WinFormsStructProbe） | LOGFONT 形态 by-ref：TRANSPILE-PASS，生成字段是 `Il2CppObject*`（原生 `WCHAR[32]`） |
| 1.17 | COM（RCW/CCW/IDispatch） | 已有**部分**运行时（旧文档「M8 远期、无 COM 运行时」**不成立**） | `src/runtime/include/internal/il2cpp_com.h:3`；实现 `src/runtime/src/com.cpp:8045`（`icall_com_create_instance_clsid`）等；托管面 `src/il2cppc/Emit/MethodMap.Marshal.cs:457`（`IsComObject`）/ `:472` / `:512` / `:541` | ComImport 接口本身可转译（`probe_com`：TRANSPILE-PASS），vtable 调用仍缺 |

**1.15 备注**：net48 里返回 native 指针/字符串的 P/Invoke 会把结果声明成 `IntPtr`（`GetProcAddress`、`SHGetKnownFolderPath` 等），再由托管侧 `Marshal.PtrToStringUni` 解码 —— 解码侧 `src/il2cppc/Emit/MethodMap.Marshal.cs:120` 已接线。所以 1.15 在 Forms/Drawing 的 1317 条里**没有实际命中**（`pinvokes.tsv` 的 `ret` 列无 `System.String`）。

### 1.1 旧文档 vs 现状（一句话版）

| 旧文档条目 | 旧判定 | 现判定 |
|---|---|---|
| #6 UTF-16 marshal | 大缺口 | **已作废**：参数/运行时/解码三侧都有（1.3/1.4） |
| #7 结构体 by-value/by-ref | 大缺口 | **部分作废**：参数侧已有（1.5）；by-value **返回**仍缺（1.6） |
| #8 IntPtr 参数 | 大缺口 | **已作废**：`n => intptr_t`（1.2） |
| #9 bool/char/byte 数组 | 大缺口 | **仍成立**，且扩大到全部数组与 ref/out 标量（1.7/1.8） |
| #10 委托→函数指针 | 可接（施工中） | **已落地**（1.11），另有未记录的 `HandleRef` 缺口（1.9） |
| #15 嵌入资源 | 大缺口（转译器 0 匹配） | **已作废**：资源池 + ResourceManager 全链（1.16） |
| #18 COM 全量 | 大缺口（M8 远期） | **部分作废**：运行时有 RCW/CCW/IDispatch 骨架（1.17），但 ComImport 接口的 vtable 调用/参数 marshal 仍缺 |
| #2 SetLastError | 已覆盖 | **正确**（非半对）：捕获/读取已有，且 net48 **本就不清零**（1.13）；清零是 .NET Core 3.0+ 语义 |
| — 入口名后缀 | 未提及 | **新增缺口**：174 条（1.10） |

## 2. P/Invoke 逐 DLL 汇总

| DLL | A | B | C | D | E | 合计 |
|---|---|---|---|---|---|---|
| `gdiplus.dll` | 4 | 552 | 10 | 0 | 0 | 566 |
| `user32.dll` | 57 | 244 | 0 | 0 | 0 | 301 |
| `gdi32.dll` | 19 | 184 | 0 | 0 | 0 | 203 |
| `kernel32.dll` | 15 | 47 | 0 | 2 | 7 | 71 |
| `uxtheme.dll` | 3 | 28 | 0 | 0 | 0 | 31 |
| `ole32.dll` | 0 | 3 | 21 | 0 | 0 | 24 |
| `comctl32.dll` | 4 | 14 | 3 | 0 | 0 | 21 |
| `oleaut32.dll` | 0 | 5 | 13 | 0 | 0 | 18 |
| `clr.dll` | 0 | 0 | 0 | 16 | 0 | 16 |
| `shell32.dll` | 1 | 11 | 1 | 0 | 0 | 13 |
| `comdlg32.dll` | 1 | 10 | 0 | 0 | 0 | 11 |
| `imm32.dll` | 0 | 0 | 0 | 10 | 0 | 10 |
| `UIAutomationCore.dll` | 0 | 0 | 0 | 9 | 0 | 9 |
| `crypt32.dll` | 0 | 0 | 0 | 5 | 0 | 5 |
| `hhctrl.ocx` | 0 | 0 | 0 | 5 | 0 | 5 |
| `winspool.drv` | 0 | 0 | 0 | 4 | 0 | 4 |
| `shlwapi.dll` | 0 | 2 | 0 | 0 | 0 | 2 |
| `oleacc.dll` | 0 | 0 | 2 | 0 | 0 | 2 |
| `ntdll.dll` | 1 | 0 | 0 | 0 | 0 | 1 |
| `advapi32.dll` | 0 | 1 | 0 | 0 | 0 | 1 |
| `Powrprof.dll` | 0 | 0 | 0 | 1 | 0 | 1 |
| `wldp.dll` | 0 | 0 | 0 | 1 | 0 | 1 |
| `SHCore.dll` | 1 | 0 | 0 | 0 | 0 | 1 |
| **合计** | **106** | **1101** | **50** | **53** | **7** | **1317** |

### 2.1 B 类按缺失能力分组（同一能力一起补 = 一次大批量收益）

| 缺失能力 | 涉及条数 | 补齐后**单独**解决 |
|---|---|---|
| HandleRef -> native handle decay（`handleref`） | 983 | 617 |
| ref/out SCALAR (int*/IntPtr*) + out-Guid + out-string（`byref-scalar`） | 238 | 39 |
| CharSet A/W entry-point suffix probing（`entrypoint-suffix`） | 174 | 22 |
| array buffer pin/copy (byte[]/char[]/...)（`array`） | 67 | 16 |
| struct layout not byte-identical (managed ref field vs inline native data)（`nonblittable-struct`） | 55 | 3 |
| StringBuilder native-write buffer（`stringbuilder`） | 21 | 3 |
| ref/out of class / foreign-assembly value type（`byref-other`） | 13 | 0 → **9 已可用 / 4 属 COM**（§2.1b） |
| struct C++ name vs Windows SDK typedef（`name-clash`） | 11 | 0 |
| by-value struct RETURN for unregistered structs（`struct-return`） | 9 | 3 |
| System.Object VARIANT-style parameter（`object`） | 7 | 0 |
| entry point absent from the module (macro alias / comctl32 v6 only)（`entrypoint-missing`） | 1 | 0 |

> 组合能力见 §8 的贪心顺序：`handleref` → `entrypoint-suffix` → `byref-scalar` → ...

### 2.1b `byref-other` 的 13 条：逐行定性（2026-11-01 实测）

`byref-other` 的 13 是「**触及**该能力的行数」，不是「13 个参数全坏」——`cap_alone` 为 0 只因这 13 行每一行
都还同时需要 handleref / array / string / IntPtr（表头 `涉及条数 / 补齐后单独解决` 见 §2.1 与
`render_winforms_report.py:178-185`）。逐行实测（每行签名原样抄进 census 工程单独转译 + MinGW 编译 +
与 net48 oracle 对跑）：

| # | 入口 | 参数 | 结论 | 机制 / 原因 |
|---|---|---|---|---|
| 1,2,3,4,5,6,7,11 | gdiplus `GdipGetEncoderParameterList`/`...Size`/`GdipGetImageRawFormat`/`GdipImageGetFrameCount`/`GdipImageSelectActiveFrame`/`GdipSaveImageToFile`、ole32 `ReadClassStg`、shell32 `SHGetKnownFolderPath` | `System.Guid&` | **已可用**（转译 OK + C++ 编译 OK + 与 net48 逐字节相同） | `B:V:System.Guid` → `TypeCppName` → `il2cpp::Il2CppGuid`（16B，`src/runtime/include/internal/guid.h:59`）`+ "*"`；调用点直接传 `ldloca` 槽地址，**无拷贝**，callee 写的就是托管槽 |
| 12 | user32 `SendMessage` | `NativeMethods+LVHITTESTINFO&` | **已可用** | 本地值类型 TypeDef → 生成的 blittable 结构体，`B:V:` 传地址直通 |
| 8,9 | oleaut32 `OleCreatePropertyFrame` ×2 | `[MarshalAs(Interface)] ref object` | **仍拒绝**，属 `object` + COM 组 | 指向 **IUnknown 接口指针**的指针，需 RCW/CCW/接口 marshal（COM 组 50 参数） |
| 10 | oleaut32 `VarFormat` | `ref object`（无 MarshalAs） | **仍拒绝**，属 `object` + COM 组 | 指向 **VARIANT** 的指针，需 `GetNativeVariantForObject` / `GetObjectForNativeVariant` |
| 13 | user32 `SendMessage`（editOle） | `[MarshalAs(IUnknown)] ref object` | **仍拒绝**，属 COM 组 | 同 8/9 |

**13 条里没有一条是 `nonblittable-struct`（55 参数组）的伪装**：唯二的值类型形态 `System.Guid`（16 字节
POD）与 `LVHITTESTINFO`（5×int32，`structs.tsv` 判 blittable）都已是字节等价布局。

net48 实测（`tests/cases/probe-pinvoke-byrefother/`，自建 SHARED dll，oracle == 转译运行时逐字节）：

- `out Guid` 是**真 16 字节槽**，且 **marshal 桩不预先清零**（清零来自方法入口 `.locals init`）：
  callee 只写前 8 字节时后 8 字节保持调用方原值（fresh out → 全 0；`ref` 有旧值 → 旧高 8 字节保留）。
- `ref Guid` callee 看得到调用方的旧值；16 字节全 `0xff` 时 callee 求和 4080，16 字节全部过界。
- `ref object` 的真实形状（`VirtualQuery` + 目标内存转储）：**默认形态**callee 收到的是**对象引用本身**
  （不是指向引用的指针、也不是 VARIANT）；带 `[MarshalAs(IUnknown)]` 时收到的是 `void**`，其指向的是
  **IUnknown 接口指针**。callee 改成别的接口指针，拷回后托管侧拿到的是**正确的 RCW**（类型仍是原类）；
  callee 写 `nullptr` → 托管引用变 `null`；传入 `null` → callee 收到非空槽内含 `nullptr`。
  这证明 `ref object` 是 COM/VARIANT marshal 契约，**不能**靠「传托管槽地址」静态化。


### 2.2 入口名导出实况（`WinFormsExportProbe`，真实 `GetProcAddress`）

| verdict | 含义 | 条数 |
|---|---|---|
| `bare` | 裸名可解析（当前实现即可） | 1116 |
| `only-W` | 只有 `NameW` 导出（`ExactSpelling=true` 时 .NET 也会失败，但这些声明都带 `CharSet`+`ExactSpelling=false`） | 182 |
| `only-A` | 只有 `NameA` 导出 | 0 |
| `missing` | 模块里根本没有（`ZeroMemory` 是 kernel32 的导入别名，`LoadIconWithScaleDown`/`ImageList_WriteEx` 在 comctl32 v6 才导出） | 3 |
| `no-module` | `LoadLibrary` 失败（`clr.dll` 只存在于 CLR 宿主进程） | 16 |

`only-W` 的分布（按 DLL，去重后的 EntryPoint）：

| DLL | unique EntryPoint | 列表 |
|---|---|---|
| `user32.dll` | 42 | `CallWindowProc`, `CreateAcceleratorTable`, `CreateWindowEx`, `DefFrameProc`, `DefMDIChildProc`, `DefWindowProc`, `DispatchMessage`, `EnumDisplaySettings`, `FindWindow`, `GetClassInfo`, `GetClassName`, `GetClipboardFormatName`, `GetMenuItemInfo`, `GetMonitorInfo`, `GetUserObjectInformation`, `GetWindowLong`, `GetWindowLongPtr`, `GetWindowText`, `GetWindowTextLength`, `InsertMenuItem`, `LoadCursor`, `LoadIcon`, `MessageBox`, `PeekMessage`, `PostMessage`, `PostThreadMessage`, `RegisterClass`, `RegisterClipboardFormat`, `RegisterWindowMessage`, `SendDlgItemMessage`, `SendMessage`, `SendMessageTimeout`, `SetClassLong`, `SetClassLongPtr`, `SetMenuItemInfo`, `SetWindowLong`, `SetWindowLongPtr`, `SetWindowText`, `SetWindowsHookEx`, `SystemParametersInfo`, `UnregisterClass`, `VkKeyScan` |
| `kernel32.dll` | 12 | `CreateActCtx`, `FormatMessage`, `GetComputerName`, `GetLocaleInfo`, `GetModuleFileName`, `GetModuleHandle`, `GetShortPathName`, `GetStartupInfo`, `GetTempFileName`, `GlobalAddAtom`, `LoadLibrary`, `LoadLibraryEx` |
| `gdi32.dll` | 9 | `AddFontResourceEx`, `CreateDC`, `CreateFontIndirect`, `CreateIC`, `ExtTextOut`, `GetObject`, `RemoveFontResourceEx`, `ResetDC`, `StartDoc` |
| `comdlg32.dll` | 7 | `ChooseColor`, `ChooseFont`, `GetOpenFileName`, `GetSaveFileName`, `PageSetupDlg`, `PrintDlg`, `PrintDlgEx` |
| `shell32.dll` | 2 | `ExtractAssociatedIcon`, `ShellExecute` |
| `winspool.drv` | 2 | `DocumentProperties`, `EnumPrinters` |
| `advapi32.dll` | 1 | `GetUserName` |
| `hhctrl.ocx` | 1 | `HtmlHelp` |

## 3. user32 / gdi32 / kernel32 逐 EntryPoint 全表

列说明：**overloads** = 同一 EntryPoint 的声明数（重载）；**class** = 分类；**needs** = 仍缺的 marshal 能力；**exports** = `GetProcAddress` 实况（`bare` / `only-W` / `missing`）。

### 3.1 `user32.dll` — 301 条声明 / 196 个 EntryPoint

| EntryPoint | overloads | class | needs | exports |
|---|---|---|---|---|
| `ActivateKeyboardLayout` | 1 | B | handleref | bare |
| `AdjustWindowRectEx` | 1 | A | - | bare |
| `AdjustWindowRectExForDpi` | 1 | A | - | bare |
| `AreDpiAwarenessContextsEqual` | 2 | A | - | bare |
| `BeginPaint` | 1 | B | handleref | bare |
| `BlockInput` | 1 | A | - | bare |
| `CallNextHookEx` | 1 | B | handleref | bare |
| `CallWindowProc` | 1 | B | entrypoint-suffix | only-W |
| `ChildWindowFromPointEx` | 1 | B | handleref | bare |
| `ClientToScreen` | 1 | B | handleref | bare |
| `ClipCursor` | 2 | A | - | bare |
| `CloseDesktop` | 1 | A | - | bare |
| `CopyImage` | 2 | B | handleref | bare |
| `CreateAcceleratorTable` | 1 | B | entrypoint-suffix, handleref | only-W |
| `CreateIconFromResourceEx` | 1 | A | - | bare |
| `CreateMenu` | 1 | A | - | bare |
| `CreatePopupMenu` | 1 | A | - | bare |
| `CreateWindowEx` | 1 | B | entrypoint-suffix, handleref, object | only-W |
| `DefFrameProc` | 1 | B | entrypoint-suffix | only-W |
| `DefMDIChildProc` | 1 | B | entrypoint-suffix | only-W |
| `DefWindowProc` | 1 | B | entrypoint-suffix | only-W |
| `DestroyAcceleratorTable` | 1 | B | handleref | bare |
| `DestroyCursor` | 1 | B | handleref | bare |
| `DestroyIcon` | 1 | B | handleref | bare |
| `DestroyMenu` | 1 | B | handleref | bare |
| `DestroyWindow` | 1 | B | handleref | bare |
| `DispatchMessage` | 1 | B | entrypoint-suffix | only-W |
| `DispatchMessageA` | 1 | A | - | bare |
| `DispatchMessageW` | 1 | A | - | bare |
| `DrawEdge` | 1 | B | handleref | bare |
| `DrawFrameControl` | 1 | B | handleref | bare |
| `DrawIcon` | 1 | B | handleref | bare |
| `DrawIconEx` | 2 | B | handleref | bare |
| `DrawMenuBar` | 1 | B | handleref | bare |
| `DrawTextA` | 2 | B | array, handleref | bare |
| `DrawTextExA` | 2 | B | array, handleref | bare |
| `DrawTextExW` | 2 | B | handleref | bare |
| `DrawTextW` | 2 | B | handleref | bare |
| `EnableMenuItem` | 1 | B | handleref | bare |
| `EnableScrollBar` | 1 | B | handleref | bare |
| `EnableWindow` | 1 | B | handleref | bare |
| `EndDialog` | 1 | B | handleref | bare |
| `EndPaint` | 1 | B | handleref | bare |
| `EnumChildWindows` | 1 | B | handleref | bare |
| `EnumDisplayMonitors` | 1 | B | handleref | bare |
| `EnumDisplaySettings` | 1 | B | entrypoint-suffix, nonblittable-struct | only-W |
| `EnumThreadWindows` | 1 | B | handleref | bare |
| `EnumWindows` | 1 | A | - | bare |
| `FillRect` | 3 | B | handleref | bare |
| `FindWindow` | 1 | B | entrypoint-suffix | only-W |
| `GetActiveWindow` | 1 | A | - | bare |
| `GetAncestor` | 1 | B | handleref | bare |
| `GetAsyncKeyState` | 1 | A | - | bare |
| `GetAwarenessFromDpiAwarenessContext` | 1 | B | struct-return | bare |
| `GetCapture` | 1 | A | - | bare |
| `GetCaretBlinkTime` | 1 | A | - | bare |
| `GetCaretPos` | 1 | B | struct-return | bare |
| `GetClassInfo` | 2 | B | entrypoint-suffix, handleref | only-W |
| `GetClassName` | 1 | B | entrypoint-suffix, handleref, stringbuilder | only-W |
| `GetClientRect` | 3 | B | handleref | bare |
| `GetClipCursor` | 1 | A | - | bare |
| `GetClipboardFormatName` | 1 | B | entrypoint-suffix, stringbuilder | only-W |
| `GetCursor` | 1 | A | - | bare |
| `GetCursorPos` | 1 | A | - | bare |
| `GetDC` | 4 | B | handleref | bare |
| `GetDCEx` | 1 | B | handleref | bare |
| `GetDesktopWindow` | 1 | A | - | bare |
| `GetDlgItem` | 1 | B | handleref | bare |
| `GetDoubleClickTime` | 1 | A | - | bare |
| `GetDpiForWindow` | 1 | B | handleref | bare |
| `GetFocus` | 1 | A | - | bare |
| `GetForegroundWindow` | 1 | A | - | bare |
| `GetIconInfo` | 2 | B | handleref | bare |
| `GetKeyState` | 1 | A | - | bare |
| `GetKeyboardLayout` | 1 | A | - | bare |
| `GetKeyboardLayoutList` | 1 | B | array | bare |
| `GetKeyboardState` | 1 | B | array | bare |
| `GetMenu` | 1 | B | handleref | bare |
| `GetMenuItemCount` | 1 | B | handleref | bare |
| `GetMenuItemID` | 1 | B | handleref | bare |
| `GetMenuItemInfo` | 2 | B | entrypoint-suffix, handleref, nonblittable-struct | only-W |
| `GetMessageA` | 1 | B | handleref | bare |
| `GetMessagePos` | 1 | A | - | bare |
| `GetMessageW` | 1 | B | handleref | bare |
| `GetMonitorInfo` | 1 | B | entrypoint-suffix, handleref, nonblittable-struct | only-W |
| `GetParent` | 1 | B | handleref | bare |
| `GetPhysicalCursorPos` | 1 | B | struct-return | bare |
| `GetProcessWindowStation` | 1 | A | - | bare |
| `GetScrollInfo` | 2 | B | handleref | bare |
| `GetSubMenu` | 1 | B | handleref | bare |
| `GetSysColor` | 2 | A | - | bare |
| `GetSysColorBrush` | 1 | A | - | bare |
| `GetSystemMenu` | 1 | B | handleref | bare |
| `GetSystemMetrics` | 2 | A | - | bare |
| `GetSystemMetricsForDpi` | 1 | A | - | bare |
| `GetThreadDpiAwarenessContext` | 2 | A | - | bare |
| `GetUpdateRgn` | 1 | B | handleref | bare |
| `GetUserObjectInformation` | 1 | B | byref-scalar, entrypoint-suffix, handleref | only-W |
| `GetWindow` | 1 | B | handleref | bare |
| `GetWindowDC` | 1 | B | handleref | bare |
| `GetWindowDpiAwarenessContext` | 1 | A | - | bare |
| `GetWindowLong` | 1 | B | entrypoint-suffix, handleref | only-W |
| `GetWindowLongPtr` | 1 | B | entrypoint-suffix, handleref | only-W |
| `GetWindowPlacement` | 1 | B | handleref | bare |
| `GetWindowRect` | 1 | B | handleref | bare |
| `GetWindowText` | 1 | B | entrypoint-suffix, handleref, stringbuilder | only-W |
| `GetWindowTextLength` | 1 | B | entrypoint-suffix, handleref | only-W |
| `GetWindowThreadProcessId` | 1 | B | byref-scalar, handleref | bare |
| `HideCaret` | 1 | B | handleref | bare |
| `InsertMenuItem` | 1 | B | entrypoint-suffix, handleref, nonblittable-struct | only-W |
| `InvalidateRect` | 2 | B | handleref | bare |
| `InvalidateRgn` | 1 | B | handleref | bare |
| `IsChild` | 2 | B | handleref | bare |
| `IsDialogMessage` | 1 | B | handleref | bare |
| `IsWindow` | 1 | B | handleref | bare |
| `IsWindowEnabled` | 1 | B | handleref | bare |
| `IsWindowUnicode` | 1 | B | handleref | bare |
| `IsWindowVisible` | 1 | B | handleref | bare |
| `IsZoomed` | 1 | B | handleref | bare |
| `KillTimer` | 1 | B | handleref | bare |
| `LoadCursor` | 1 | B | entrypoint-suffix, handleref | only-W |
| `LoadIcon` | 1 | B | entrypoint-suffix, handleref | only-W |
| `MapWindowPoints` | 3 | A/B | handleref | bare |
| `MessageBeep` | 1 | A | - | bare |
| `MessageBox` | 1 | B | entrypoint-suffix, handleref | only-W |
| `MonitorFromPoint` | 1 | A | - | bare |
| `MonitorFromRect` | 1 | A | - | bare |
| `MonitorFromWindow` | 1 | B | handleref | bare |
| `MsgWaitForMultipleObjectsEx` | 1 | A | - | bare |
| `NotifyWinEvent` | 1 | B | handleref | bare |
| `OemKeyScan` | 1 | A | - | bare |
| `OpenInputDesktop` | 1 | A | - | bare |
| `PeekMessage` | 1 | B | entrypoint-suffix, handleref | only-W |
| `PeekMessageA` | 1 | B | handleref | bare |
| `PeekMessageW` | 1 | B | handleref | bare |
| `PostMessage` | 3 | B | entrypoint-suffix, handleref | only-W |
| `PostQuitMessage` | 1 | A | - | bare |
| `PostThreadMessage` | 1 | B | entrypoint-suffix | only-W |
| `RedrawWindow` | 2 | B | handleref | bare |
| `RegisterClass` | 1 | B | entrypoint-suffix, nonblittable-struct | only-W |
| `RegisterClipboardFormat` | 1 | B | entrypoint-suffix | only-W |
| `RegisterWindowMessage` | 1 | B | entrypoint-suffix | only-W |
| `ReleaseCapture` | 1 | A | - | bare |
| `ReleaseDC` | 4 | B | handleref | bare |
| `RemoveMenu` | 1 | B | handleref | bare |
| `ScreenToClient` | 1 | B | handleref | bare |
| `ScrollWindow` | 1 | B | handleref | bare |
| `ScrollWindowEx` | 1 | B | handleref | bare |
| `SendDlgItemMessage` | 1 | B | entrypoint-suffix, handleref | only-W |
| `SendInput` | 1 | B | array | bare |
| `SendMessage` | 61 | B | array, byref-other, byref-scalar, entrypoint-suffix, handleref, name-clash, nonblittable-struct, stringbuilder | only-W |
| `SendMessageTimeout` | 1 | B | byref-scalar, entrypoint-suffix, handleref | only-W |
| `SetActiveWindow` | 1 | B | handleref | bare |
| `SetCapture` | 1 | B | handleref | bare |
| `SetClassLong` | 1 | B | entrypoint-suffix, handleref | only-W |
| `SetClassLongPtr` | 1 | B | entrypoint-suffix, handleref | only-W |
| `SetCursor` | 1 | B | handleref | bare |
| `SetCursorPos` | 1 | A | - | bare |
| `SetFocus` | 1 | B | handleref | bare |
| `SetForegroundWindow` | 1 | B | handleref | bare |
| `SetKeyboardState` | 1 | B | array | bare |
| `SetLayeredWindowAttributes` | 1 | B | handleref | bare |
| `SetMenu` | 1 | B | handleref | bare |
| `SetMenuDefaultItem` | 1 | B | handleref | bare |
| `SetMenuItemInfo` | 1 | B | entrypoint-suffix, handleref, nonblittable-struct | only-W |
| `SetParent` | 1 | B | handleref | bare |
| `SetProcessDPIAware` | 1 | A | - | bare |
| `SetProcessDpiAwarenessContext` | 1 | A | - | bare |
| `SetScrollInfo` | 1 | B | handleref | bare |
| `SetScrollPos` | 1 | B | handleref | bare |
| `SetThreadDpiAwarenessContext` | 1 | A | - | bare |
| `SetTimer` | 1 | B | handleref | bare |
| `SetWindowLong` | 2 | B | entrypoint-suffix, handleref | only-W |
| `SetWindowLongPtr` | 2 | B | entrypoint-suffix, handleref | only-W |
| `SetWindowPlacement` | 1 | B | handleref | bare |
| `SetWindowPos` | 1 | B | handleref | bare |
| `SetWindowRgn` | 1 | B | handleref | bare |
| `SetWindowText` | 1 | B | entrypoint-suffix, handleref | only-W |
| `SetWindowsHookEx` | 1 | B | entrypoint-suffix, handleref | only-W |
| `ShowCaret` | 1 | B | handleref | bare |
| `ShowCursor` | 1 | A | - | bare |
| `ShowWindow` | 1 | B | handleref | bare |
| `SystemParametersInfo` | 10 | B | array, byref-scalar, entrypoint-suffix, name-clash, nonblittable-struct | only-W |
| `SystemParametersInfoForDpi` | 1 | B | name-clash, nonblittable-struct | bare |
| `TrackPopupMenuEx` | 1 | B | handleref | bare |
| `TranslateMDISysAccel` | 1 | A | - | bare |
| `TranslateMessage` | 1 | A | - | bare |
| `UnhookWindowsHookEx` | 1 | B | handleref | bare |
| `UnregisterClass` | 1 | B | entrypoint-suffix, handleref | only-W |
| `UpdateWindow` | 1 | B | handleref | bare |
| `ValidateRect` | 1 | B | handleref | bare |
| `VkKeyScan` | 1 | B | entrypoint-suffix | only-W |
| `WaitMessage` | 1 | A | - | bare |
| `WindowFromDC` | 3 | B | handleref | bare |
| `WindowFromPoint` | 1 | A | - | bare |
| `keybd_event` | 1 | A | - | bare |

### 3.2 `gdi32.dll` — 203 条声明 / 86 个 EntryPoint

| EntryPoint | overloads | class | needs | exports |
|---|---|---|---|---|
| `AbortDoc` | 1 | B | handleref | bare |
| `AddFontResourceEx` | 1 | B | entrypoint-suffix | only-W |
| `AngleArc` | 2 | B | handleref | bare |
| `Arc` | 2 | B | handleref | bare |
| `BeginPath` | 2 | B | handleref | bare |
| `BitBlt` | 2 | B | handleref | bare |
| `CombineRgn` | 3 | B | handleref, struct-return | bare |
| `CreateBitmap` | 4 | A/B | array | bare |
| `CreateBrushIndirect` | 1 | A | - | bare |
| `CreateCompatibleBitmap` | 2 | B | handleref | bare |
| `CreateCompatibleDC` | 4 | B | handleref | bare |
| `CreateDC` | 3 | B | entrypoint-suffix, handleref | only-W |
| `CreateDIBSection` | 2 | B | array, byref-scalar, handleref, nonblittable-struct | bare |
| `CreateFontIndirect` | 2 | B | entrypoint-suffix, object | only-W |
| `CreateHalftonePalette` | 1 | B | handleref | bare |
| `CreateIC` | 3 | B | entrypoint-suffix, handleref | only-W |
| `CreatePatternBrush` | 1 | B | handleref | bare |
| `CreatePen` | 3 | A | - | bare |
| `CreateRectRgn` | 4 | A | - | bare |
| `CreateSolidBrush` | 3 | A | - | bare |
| `DeleteDC` | 4 | B | handleref | bare |
| `DeleteObject` | 5 | B | handleref | bare |
| `Ellipse` | 2 | B | handleref | bare |
| `EndDoc` | 1 | B | handleref | bare |
| `EndPage` | 1 | B | handleref | bare |
| `EndPath` | 2 | B | handleref | bare |
| `ExtCreatePen` | 2 | B | array | bare |
| `ExtEscape` | 2 | B | array, byref-scalar, handleref | bare |
| `ExtTextOut` | 2 | B | array, entrypoint-suffix, handleref | only-W |
| `GdiFlush` | 2 | A | - | bare |
| `GetBitmapBits` | 1 | B | array, handleref | bare |
| `GetBkColor` | 3 | B | handleref | bare |
| `GetBkMode` | 2 | B | handleref | bare |
| `GetClipRgn` | 4 | B | handleref | bare |
| `GetCurrentObject` | 2 | B | handleref | bare |
| `GetDIBits` | 2 | B | array, handleref, nonblittable-struct | bare |
| `GetDeviceCaps` | 4 | B | handleref | bare |
| `GetGraphicsMode` | 2 | B | handleref | bare |
| `GetMapMode` | 2 | B | handleref | bare |
| `GetNearestColor` | 2 | B | handleref | bare |
| `GetObject` | 12 | B | array, byref-scalar, entrypoint-suffix, handleref, name-clash, nonblittable-struct | only-W |
| `GetObjectType` | 2 | B | handleref | bare |
| `GetPaletteEntries` | 2 | B | array, handleref | bare |
| `GetROP2` | 2 | B | handleref | bare |
| `GetRegionData` | 1 | B | handleref | bare |
| `GetRgnBox` | 3 | B | handleref, struct-return | bare |
| `GetStockObject` | 4 | A | - | bare |
| `GetSystemPaletteEntries` | 1 | B | array, handleref | bare |
| `GetTextAlign` | 2 | B | handleref | bare |
| `GetTextColor` | 3 | B | handleref | bare |
| `GetTextExtentPoint32A` | 2 | B | array, handleref | bare |
| `GetTextExtentPoint32W` | 2 | B | handleref | bare |
| `GetTextMetricsA` | 3 | B | handleref | bare |
| `GetTextMetricsW` | 3 | B | handleref | bare |
| `GetViewportExtEx` | 2 | B | handleref | bare |
| `GetViewportOrgEx` | 3 | B | handleref | bare |
| `IntersectClipRect` | 2 | B | handleref | bare |
| `LPtoDP` | 1 | B | handleref | bare |
| `LineTo` | 3 | B | handleref | bare |
| `MoveToEx` | 3 | B | handleref | bare |
| `OffsetViewportOrgEx` | 2 | B | handleref | bare |
| `PatBlt` | 1 | B | handleref | bare |
| `RealizePalette` | 1 | B | handleref | bare |
| `Rectangle` | 3 | B | handleref | bare |
| `RemoveFontResourceEx` | 1 | B | entrypoint-suffix | only-W |
| `ResetDC` | 1 | B | entrypoint-suffix, handleref | only-W |
| `RestoreDC` | 2 | B | handleref | bare |
| `SaveDC` | 2 | B | handleref | bare |
| `SelectClipRgn` | 4 | B | handleref, struct-return | bare |
| `SelectObject` | 4 | B | handleref | bare |
| `SelectPalette` | 1 | B | handleref | bare |
| `SetBkColor` | 3 | B | handleref | bare |
| `SetBkMode` | 3 | B | handleref | bare |
| `SetGraphicsMode` | 2 | B | handleref | bare |
| `SetMapMode` | 3 | B | handleref | bare |
| `SetROP2` | 3 | B | handleref | bare |
| `SetTextAlign` | 2 | B | handleref | bare |
| `SetTextColor` | 3 | B | handleref | bare |
| `SetViewportExtEx` | 3 | B | handleref | bare |
| `SetViewportOrgEx` | 3 | B | handleref | bare |
| `SetWindowExtEx` | 1 | B | handleref | bare |
| `SetWindowOrgEx` | 1 | B | handleref | bare |
| `StartDoc` | 1 | B | entrypoint-suffix, handleref, nonblittable-struct | only-W |
| `StartPage` | 1 | B | handleref | bare |
| `StretchDIBits` | 1 | B | array, handleref, nonblittable-struct | bare |
| `StrokePath` | 2 | B | handleref | bare |

### 3.3 `kernel32.dll` — 71 条声明 / 46 个 EntryPoint

| EntryPoint | overloads | class | needs | exports |
|---|---|---|---|---|
| `ActivateActCtx` | 1 | B | byref-scalar | bare |
| `CloseHandle` | 2 | B | handleref | bare |
| `CreateActCtx` | 1 | B | entrypoint-suffix, nonblittable-struct | only-W |
| `DeactivateActCtx` | 1 | A | - | bare |
| `DuplicateHandle` | 1 | B | byref-scalar, handleref | bare |
| `FormatMessage` | 3 | B | entrypoint-suffix, handleref, stringbuilder | only-W |
| `FreeLibrary` | 2 | B | handleref | bare |
| `GetComputerName` | 1 | B | array, entrypoint-suffix, stringbuilder | only-W |
| `GetCurrentActCtx` | 1 | B | byref-scalar | bare |
| `GetCurrentProcess` | 1 | A | - | bare |
| `GetCurrentProcessId` | 1 | E | - | bare |
| `GetCurrentThread` | 1 | A | - | bare |
| `GetCurrentThreadId` | 1 | E | - | bare |
| `GetExitCodeThread` | 2 | B | byref-scalar, handleref | bare |
| `GetLocaleInfo` | 1 | B | entrypoint-suffix, stringbuilder | only-W |
| `GetModuleFileName` | 1 | B | entrypoint-suffix, handleref, stringbuilder | only-W |
| `GetModuleHandle` | 2 | B | entrypoint-suffix | only-W |
| `GetProcAddress` | 2 | B | handleref | bare |
| `GetProcessHeap` | 1 | D | - | bare |
| `GetShortPathName` | 1 | B | entrypoint-suffix, stringbuilder | only-W |
| `GetStartupInfo` | 1 | B | entrypoint-suffix | only-W |
| `GetSystemDefaultLCID` | 1 | A | - | bare |
| `GetSystemPowerStatus` | 1 | A | - | bare |
| `GetTempFileName` | 1 | B | entrypoint-suffix, stringbuilder | only-W |
| `GetThreadLocale` | 2 | A | - | bare |
| `GetTickCount` | 1 | E | - | bare |
| `GetUserDefaultLCID` | 4 | A | - | bare |
| `GlobalAddAtom` | 1 | B | entrypoint-suffix | only-W |
| `GlobalAlloc` | 2 | A | - | bare |
| `GlobalDeleteAtom` | 1 | A | - | bare |
| `GlobalFree` | 2 | B | handleref | bare |
| `GlobalLock` | 2 | B | handleref | bare |
| `GlobalReAlloc` | 1 | B | handleref | bare |
| `GlobalSize` | 1 | B | handleref | bare |
| `GlobalUnlock` | 2 | B | handleref | bare |
| `HeapFree` | 1 | D | - | bare |
| `LoadLibrary` | 2 | B | entrypoint-suffix | only-W |
| `LoadLibraryEx` | 2 | B | entrypoint-suffix | only-W |
| `MapViewOfFile` | 1 | B | handleref | bare |
| `MultiByteToWideChar` | 1 | B | array | bare |
| `RtlMoveMemory` | 7 | B/E | array, handleref | bare |
| `SetThreadLocale` | 1 | A | - | bare |
| `UnmapViewOfFile` | 1 | B | handleref | bare |
| `WideCharToMultiByte` | 3 | B | array | bare |
| `ZeroMemory` | 1 | E | - | missing |
| `lstrlen` | 1 | E | - | bare |

## 4. 阶段 1 最小可跑集（P0 清单）

### 4.1 取证方法

用 `tools/audit/WinFormsPinvokeGraph`（与 `tools/audit/IcallCallGraph` 同源的 IL 解码器）对**托管 IL** 建调用图：解码 Forms+Drawing 共 34178 个方法体（`decodeFailures=0`，解码器自校验：方法体必须被精确消费），得到 108427 条调用边。从 11 个阶段 1 root 做前向 BFS：

```
  root: System.Windows.Forms.Application::Run (il)
  root: System.Windows.Forms.Application::Run (il)
  root: System.Windows.Forms.Application::Run (il)
  root: System.Windows.Forms.Button::OnClick (il)
  root: System.Windows.Forms.Control::CreateHandle (il)
  root: System.Windows.Forms.Control::OnPaint (il)
  root: System.Windows.Forms.Control::WmPaint (il)
  root: System.Windows.Forms.Control::WndProc (il)
  root: System.Windows.Forms.Label::OnPaint (il)
  root: System.Windows.Forms.NativeWindow::CreateHandle (il)
  root: System.Windows.Forms.NativeWindow::WndProc (il)
```

root 全部是 `Application.Run` / `Control.CreateHandle` / `Control.WndProc` / `Control.WmPaint` / `Control.OnPaint` / `NativeWindow.WndProc` / `Button.OnClick` / `Label.OnPaint` —— 即「`Application.Run(new Form())` + Button/Label + Click + WM_PAINT」这条路径的入口。

BFS 结果：**327 个**不同 EntryPoint 可达（529 条声明）。按跳数分布：

| 距 root 跳数 | EntryPoint 数 |
|---|---|
| 1 | 7 |
| 2 | 23 |
| 3 | 55 |
| 4 | 55 |
| 5 | 48 |
| 6 | 47 |
| 7 | 31 |
| 8 | 22 |
| 9 | 25 |
| 10 | 11 |
| 11 | 3 |

分类分布：A=61 / B=441 / C=7 / D=17 / E=3。

### 4.2 P0 清单（全部可达 EntryPoint，按接线成本升序）

排序键 = (仍缺的能力个数, 距 root 跳数, DLL, EntryPoint)。「仍缺能力」为空 = 现有 marshal 直接可用（A/E）。**这是阶段 1 的完整 P0 集合**：`Application.Run(new Form())` + Button/Label + Click + WM_PAINT 这条路径上真正会被调用的全部 P/Invoke。

| # | DLL | EntryPoint | 跳数 | 声明数 | class | 仍缺能力 | exports |
|---|---|---|---|---|---|---|---|
| 1 | `user32.dll` | `GetDesktopWindow` | 1 | 1 | A | - | bare |
| 2 | `user32.dll` | `GetFocus` | 1 | 1 | A | - | bare |
| 3 | `user32.dll` | `GetKeyState` | 1 | 1 | A | - | bare |
| 4 | `UIAutomationCore.dll` | `UiaReturnRawElementProvider` | 2 | 1 | D | - | bare |
| 5 | `kernel32.dll` | `DeactivateActCtx` | 2 | 1 | A | - | bare |
| 6 | `oleacc.dll` | `LresultFromObject` | 2 | 1 | C | - | bare |
| 7 | `user32.dll` | `GetForegroundWindow` | 2 | 1 | A | - | bare |
| 8 | `user32.dll` | `GetThreadDpiAwarenessContext` | 2 | 2 | A | - | bare |
| 9 | `UIAutomationCore.dll` | `UiaDisconnectProvider` | 3 | 1 | D | - | bare |
| 10 | `gdi32.dll` | `GetStockObject` | 3 | 4 | A | - | bare |
| 11 | `gdiplus.dll` | `GdipCreateHalftonePalette` | 3 | 1 | A | - | bare |
| 12 | `kernel32.dll` | `GetCurrentProcess` | 3 | 1 | A | - | bare |
| 13 | `kernel32.dll` | `GetCurrentThread` | 3 | 1 | A | - | bare |
| 14 | `kernel32.dll` | `GetCurrentThreadId` | 3 | 1 | E | - | bare |
| 15 | `user32.dll` | `AreDpiAwarenessContextsEqual` | 3 | 2 | A | - | bare |
| 16 | `user32.dll` | `ReleaseCapture` | 3 | 1 | A | - | bare |
| 17 | `user32.dll` | `SetThreadDpiAwarenessContext` | 3 | 1 | A | - | bare |
| 18 | `user32.dll` | `WindowFromPoint` | 3 | 1 | A | - | bare |
| 19 | `hhctrl.ocx` | `HtmlHelp` | 4 | 5 | D | - | only-W |
| 20 | `imm32.dll` | `ImmGetContext` | 4 | 1 | D | - | bare |
| 21 | `imm32.dll` | `ImmGetConversionStatus` | 4 | 1 | D | - | bare |
| 22 | `imm32.dll` | `ImmReleaseContext` | 4 | 1 | D | - | bare |
| 23 | `imm32.dll` | `ImmSetConversionStatus` | 4 | 1 | D | - | bare |
| 24 | `kernel32.dll` | `GetSystemDefaultLCID` | 4 | 1 | A | - | bare |
| 25 | `ole32.dll` | `OleUninitialize` | 4 | 1 | C | - | bare |
| 26 | `ole32.dll` | `RegisterDragDrop` | 4 | 1 | C | - | bare |
| 27 | `ole32.dll` | `RevokeDragDrop` | 4 | 1 | C | - | bare |
| 28 | `user32.dll` | `DispatchMessageA` | 4 | 1 | A | - | bare |
| 29 | `user32.dll` | `DispatchMessageW` | 4 | 1 | A | - | bare |
| 30 | `user32.dll` | `GetActiveWindow` | 4 | 1 | A | - | bare |
| 31 | `user32.dll` | `GetCapture` | 4 | 1 | A | - | bare |
| 32 | `user32.dll` | `GetKeyboardLayout` | 4 | 1 | A | - | bare |
| 33 | `user32.dll` | `GetProcessWindowStation` | 4 | 1 | A | - | bare |
| 34 | `user32.dll` | `GetSysColorBrush` | 4 | 1 | A | - | bare |
| 35 | `user32.dll` | `GetSystemMetrics` | 4 | 2 | A | - | bare |
| 36 | `user32.dll` | `TranslateMessage` | 4 | 1 | A | - | bare |
| 37 | `user32.dll` | `WaitMessage` | 4 | 1 | A | - | bare |
| 38 | `SHCore.dll` | `SetProcessDpiAwareness` | 5 | 1 | A | - | bare |
| 39 | `gdi32.dll` | `CreateSolidBrush` | 5 | 3 | A | - | bare |
| 40 | `imm32.dll` | `ImmAssociateContext` | 5 | 1 | D | - | bare |
| 41 | `imm32.dll` | `ImmCreateContext` | 5 | 1 | D | - | bare |
| 42 | `imm32.dll` | `ImmGetOpenStatus` | 5 | 1 | D | - | bare |
| 43 | `imm32.dll` | `ImmSetOpenStatus` | 5 | 1 | D | - | bare |
| 44 | `user32.dll` | `SetProcessDPIAware` | 5 | 1 | A | - | bare |
| 45 | `user32.dll` | `SetProcessDpiAwarenessContext` | 5 | 1 | A | - | bare |
| 46 | `comctl32.dll` | `ImageList_Create` | 6 | 1 | A | - | bare |
| 47 | `comctl32.dll` | `InitCommonControls` | 6 | 1 | A | - | bare |
| 48 | `gdi32.dll` | `CreateRectRgn` | 6 | 4 | A | - | bare |
| 49 | `oleacc.dll` | `CreateStdAccessibleObject` | 6 | 1 | C | - | bare |
| 50 | `user32.dll` | `AdjustWindowRectEx` | 6 | 1 | A | - | bare |
| 51 | `user32.dll` | `AdjustWindowRectExForDpi` | 6 | 1 | A | - | bare |
| 52 | `ole32.dll` | `OleInitialize` | 7 | 1 | C | - | bare |
| 53 | `user32.dll` | `GetWindowDpiAwarenessContext` | 7 | 1 | A | - | bare |
| 54 | `UIAutomationCore.dll` | `UiaClientsAreListening` | 8 | 1 | D | - | bare |
| 55 | `UIAutomationCore.dll` | `UiaRaiseAutomationEvent` | 8 | 1 | D | - | bare |
| 56 | `comctl32.dll` | `InitCommonControlsEx` | 8 | 1 | A | - | bare |
| 57 | `gdiplus.dll` | `GdipCreateBitmapFromStream` | 8 | 1 | C | - | bare |
| 58 | `user32.dll` | `CreatePopupMenu` | 8 | 1 | A | - | bare |
| 59 | `user32.dll` | `GetMessagePos` | 8 | 1 | A | - | bare |
| 60 | `user32.dll` | `MonitorFromPoint` | 8 | 1 | A | - | bare |
| 61 | `uxtheme.dll` | `GetThemeAppProperties` | 8 | 1 | A | - | bare |
| 62 | `uxtheme.dll` | `IsAppThemed` | 8 | 1 | A | - | bare |
| 63 | `user32.dll` | `GetSysColor` | 9 | 2 | A | - | bare |
| 64 | `user32.dll` | `GetCursor` | 10 | 1 | A | - | bare |
| 65 | `user32.dll` | `GetCursorPos` | 10 | 1 | A | - | bare |
| 66 | `user32.dll` | `GetSystemMetricsForDpi` | 10 | 1 | A | - | bare |
| 67 | `user32.dll` | `MonitorFromRect` | 11 | 1 | A | - | bare |
| 68 | `gdi32.dll` | `SelectPalette` | 1 | 1 | B | handleref | bare |
| 69 | `kernel32.dll` | `GetModuleHandle` | 1 | 2 | B | entrypoint-suffix | only-W |
| 70 | `user32.dll` | `GetParent` | 1 | 1 | B | handleref | bare |
| 71 | `gdi32.dll` | `GetNearestColor` | 2 | 2 | B | handleref | bare |
| 72 | `gdi32.dll` | `RealizePalette` | 2 | 1 | B | handleref | bare |
| 73 | `gdiplus.dll` | `GdipCreateSolidFill` | 2 | 1 | B | byref-scalar | bare |
| 74 | `gdiplus.dll` | `GdipRestoreGraphics` | 2 | 1 | B | handleref | bare |
| 75 | `kernel32.dll` | `ActivateActCtx` | 2 | 1 | B | byref-scalar | bare |
| 76 | `user32.dll` | `BeginPaint` | 2 | 1 | B | handleref | bare |
| 77 | `user32.dll` | `CallWindowProc` | 2 | 1 | B | entrypoint-suffix | only-W |
| 78 | `user32.dll` | `DefWindowProc` | 2 | 1 | B | entrypoint-suffix | only-W |
| 79 | `user32.dll` | `EndPaint` | 2 | 1 | B | handleref | bare |
| 80 | `user32.dll` | `GetClientRect` | 2 | 3 | B | handleref | bare |
| 81 | `user32.dll` | `GetDlgItem` | 2 | 1 | B | handleref | bare |
| 82 | `user32.dll` | `GetDpiForWindow` | 2 | 1 | B | handleref | bare |
| 83 | `user32.dll` | `GetMenu` | 2 | 1 | B | handleref | bare |
| 84 | `user32.dll` | `RegisterWindowMessage` | 2 | 1 | B | entrypoint-suffix | only-W |
| 85 | `gdi32.dll` | `BitBlt` | 3 | 2 | B | handleref | bare |
| 86 | `gdi32.dll` | `SetBkColor` | 3 | 3 | B | handleref | bare |
| 87 | `gdi32.dll` | `SetTextColor` | 3 | 3 | B | handleref | bare |
| 88 | `gdiplus.dll` | `GdipDrawImageRect` | 3 | 1 | B | handleref | bare |
| 89 | `gdiplus.dll` | `GdipDrawString` | 3 | 1 | B | handleref | bare |
| 90 | `gdiplus.dll` | `GdipSetClipGraphics` | 3 | 1 | B | handleref | bare |
| 91 | `gdiplus.dll` | `GdipSetPageUnit` | 3 | 1 | B | handleref | bare |
| 92 | `kernel32.dll` | `GetCurrentActCtx` | 3 | 1 | B | byref-scalar | bare |
| 93 | `kernel32.dll` | `GetProcAddress` | 3 | 2 | B | handleref | bare |
| 94 | `kernel32.dll` | `LoadLibrary` | 3 | 2 | B | entrypoint-suffix | only-W |
| 95 | `kernel32.dll` | `LoadLibraryEx` | 3 | 2 | B | entrypoint-suffix | only-W |
| 96 | `kernel32.dll` | `MultiByteToWideChar` | 3 | 1 | B | array | bare |
| 97 | `kernel32.dll` | `WideCharToMultiByte` | 3 | 3 | B | array | bare |
| 98 | `user32.dll` | `EnableWindow` | 3 | 1 | B | handleref | bare |
| 99 | `user32.dll` | `GetDC` | 3 | 4 | B | handleref | bare |
| 100 | `user32.dll` | `GetMenuItemCount` | 3 | 1 | B | handleref | bare |
| 101 | `user32.dll` | `GetMenuItemID` | 3 | 1 | B | handleref | bare |
| 102 | `user32.dll` | `GetSubMenu` | 3 | 1 | B | handleref | bare |
| 103 | `user32.dll` | `GetWindow` | 3 | 1 | B | handleref | bare |
| 104 | `user32.dll` | `GetWindowRect` | 3 | 1 | B | handleref | bare |
| 105 | `user32.dll` | `InvalidateRect` | 3 | 2 | B | handleref | bare |
| 106 | `user32.dll` | `IsWindowEnabled` | 3 | 1 | B | handleref | bare |
| 107 | `user32.dll` | `IsWindowVisible` | 3 | 1 | B | handleref | bare |
| 108 | `user32.dll` | `MapWindowPoints` | 3 | 3 | A/B | handleref | bare |
| 109 | `user32.dll` | `NotifyWinEvent` | 3 | 1 | B | handleref | bare |
| 110 | `user32.dll` | `RedrawWindow` | 3 | 2 | B | handleref | bare |
| 111 | `user32.dll` | `ReleaseDC` | 3 | 4 | B | handleref | bare |
| 112 | `user32.dll` | `SetCapture` | 3 | 1 | B | handleref | bare |
| 113 | `user32.dll` | `SetCursor` | 3 | 1 | B | handleref | bare |
| 114 | `user32.dll` | `SetFocus` | 3 | 1 | B | handleref | bare |
| 115 | `user32.dll` | `SetWindowPos` | 3 | 1 | B | handleref | bare |
| 116 | `comctl32.dll` | `ImageList_DrawEx` | 4 | 1 | B | handleref | bare |
| 117 | `comctl32.dll` | `ImageList_GetImageCount` | 4 | 1 | B | handleref | bare |
| 118 | `comctl32.dll` | `ImageList_GetImageInfo` | 4 | 1 | B | handleref | bare |
| 119 | `gdi32.dll` | `DeleteObject` | 4 | 5 | B | handleref | bare |
| 120 | `gdi32.dll` | `GetDeviceCaps` | 4 | 4 | B | handleref | bare |
| 121 | `gdi32.dll` | `SelectObject` | 4 | 4 | B | handleref | bare |
| 122 | `gdiplus.dll` | `GdipDrawLine` | 4 | 1 | B | handleref | bare |
| 123 | `gdiplus.dll` | `GdipDrawRectangle` | 4 | 1 | B | handleref | bare |
| 124 | `gdiplus.dll` | `GdipGetClip` | 4 | 1 | B | handleref | bare |
| 125 | `gdiplus.dll` | `GdipGetMatrixElements` | 4 | 1 | B | handleref | bare |
| 126 | `gdiplus.dll` | `GdipGetStringFormatFlags` | 4 | 1 | B | handleref | bare |
| 127 | `gdiplus.dll` | `GdipGetTextRenderingHint` | 4 | 1 | B | handleref | bare |
| 128 | `gdiplus.dll` | `GdipGetWorldTransform` | 4 | 1 | B | handleref | bare |
| 129 | `gdiplus.dll` | `GdipReleaseDC` | 4 | 1 | B | handleref | bare |
| 130 | `gdiplus.dll` | `GdipSetSolidFillColor` | 4 | 1 | B | handleref | bare |
| 131 | `gdiplus.dll` | `GdipSetStringFormatFlags` | 4 | 1 | B | handleref | bare |
| 132 | `gdiplus.dll` | `GdipSetStringFormatHotkeyPrefix` | 4 | 1 | B | handleref | bare |
| 133 | `gdiplus.dll` | `GdipSetStringFormatTrimming` | 4 | 1 | B | handleref | bare |
| 134 | `kernel32.dll` | `FreeLibrary` | 4 | 2 | B | handleref | bare |
| 135 | `user32.dll` | `GetAncestor` | 4 | 1 | B | handleref | bare |
| 136 | `user32.dll` | `GetMessageA` | 4 | 1 | B | handleref | bare |
| 137 | `user32.dll` | `GetMessageW` | 4 | 1 | B | handleref | bare |
| 138 | `user32.dll` | `IsChild` | 4 | 2 | B | handleref | bare |
| 139 | `user32.dll` | `IsWindowUnicode` | 4 | 1 | B | handleref | bare |
| 140 | `user32.dll` | `SetWindowRgn` | 4 | 1 | B | handleref | bare |
| 141 | `user32.dll` | `WindowFromDC` | 4 | 3 | B | handleref | bare |
| 142 | `gdi32.dll` | `CreateCompatibleDC` | 5 | 4 | B | handleref | bare |
| 143 | `gdi32.dll` | `GetBkColor` | 5 | 3 | B | handleref | bare |
| 144 | `gdi32.dll` | `GetCurrentObject` | 5 | 2 | B | handleref | bare |
| 145 | `gdi32.dll` | `GetObjectType` | 5 | 2 | B | handleref | bare |
| 146 | `gdi32.dll` | `GetTextAlign` | 5 | 2 | B | handleref | bare |
| 147 | `gdi32.dll` | `GetTextColor` | 5 | 3 | B | handleref | bare |
| 148 | `gdi32.dll` | `SaveDC` | 5 | 2 | B | handleref | bare |
| 149 | `gdi32.dll` | `SetTextAlign` | 5 | 2 | B | handleref | bare |
| 150 | `gdiplus.dll` | `GdipBitmapUnlockBits` | 5 | 1 | B | handleref | bare |
| 151 | `gdiplus.dll` | `GdipCreateBitmapFromFile` | 5 | 1 | B | byref-scalar | bare |
| 152 | `gdiplus.dll` | `GdipCreateBitmapFromFileICM` | 5 | 1 | B | byref-scalar | bare |
| 153 | `gdiplus.dll` | `GdipCreateImageAttributes` | 5 | 1 | B | byref-scalar | bare |
| 154 | `gdiplus.dll` | `GdipCreateMatrix` | 5 | 1 | B | byref-scalar | bare |
| 155 | `gdiplus.dll` | `GdipCreateRegion` | 5 | 1 | B | byref-scalar | bare |
| 156 | `gdiplus.dll` | `GdipFillRectangle` | 5 | 1 | B | handleref | bare |
| 157 | `gdiplus.dll` | `GdipImageForceValidation` | 5 | 1 | B | handleref | bare |
| 158 | `gdiplus.dll` | `GdipSetStringFormatAlign` | 5 | 1 | B | handleref | bare |
| 159 | `gdiplus.dll` | `GdipSetStringFormatLineAlign` | 5 | 1 | B | handleref | bare |
| 160 | `kernel32.dll` | `CloseHandle` | 5 | 2 | B | handleref | bare |
| 161 | `user32.dll` | `DrawTextExW` | 5 | 2 | B | handleref | bare |
| 162 | `user32.dll` | `GetScrollInfo` | 5 | 2 | B | handleref | bare |
| 163 | `user32.dll` | `HideCaret` | 5 | 1 | B | handleref | bare |
| 164 | `user32.dll` | `IsDialogMessage` | 5 | 1 | B | handleref | bare |
| 165 | `user32.dll` | `PostThreadMessage` | 5 | 1 | B | entrypoint-suffix | only-W |
| 166 | `user32.dll` | `SetParent` | 5 | 1 | B | handleref | bare |
| 167 | `user32.dll` | `ShowWindow` | 5 | 1 | B | handleref | bare |
| 168 | `user32.dll` | `TrackPopupMenuEx` | 5 | 1 | B | handleref | bare |
| 169 | `comctl32.dll` | `ImageList_SetBkColor` | 6 | 1 | B | handleref | bare |
| 170 | `gdi32.dll` | `DeleteDC` | 6 | 4 | B | handleref | bare |
| 171 | `gdi32.dll` | `GetBkMode` | 6 | 2 | B | handleref | bare |
| 172 | `gdi32.dll` | `GetClipRgn` | 6 | 4 | B | handleref | bare |
| 173 | `gdi32.dll` | `OffsetViewportOrgEx` | 6 | 2 | B | handleref | bare |
| 174 | `gdi32.dll` | `SetBkMode` | 6 | 3 | B | handleref | bare |
| 175 | `gdiplus.dll` | `GdipBitmapLockBits` | 6 | 1 | B | handleref | bare |
| 176 | `gdiplus.dll` | `GdipCombineRegionRegion` | 6 | 1 | B | handleref | bare |
| 177 | `gdiplus.dll` | `GdipCreatePen1` | 6 | 1 | B | byref-scalar | bare |
| 178 | `gdiplus.dll` | `GdipCreateStringFormat` | 6 | 1 | B | byref-scalar | bare |
| 179 | `gdiplus.dll` | `GdipDeleteGraphics` | 6 | 1 | B | handleref | bare |
| 180 | `gdiplus.dll` | `GdipDeleteMatrix` | 6 | 1 | B | handleref | bare |
| 181 | `gdiplus.dll` | `GdipDeleteRegion` | 6 | 1 | B | handleref | bare |
| 182 | `gdiplus.dll` | `GdipDisposeImage` | 6 | 1 | B | handleref | bare |
| 183 | `gdiplus.dll` | `GdipDrawImageI` | 6 | 1 | B | handleref | bare |
| 184 | `gdiplus.dll` | `GdipDrawImageRectRectI` | 6 | 1 | B | handleref | bare |
| 185 | `gdiplus.dll` | `GdipGetGenericFontFamilySansSerif` | 6 | 1 | B | byref-scalar | bare |
| 186 | `gdiplus.dll` | `GdipGetPropertyItem` | 6 | 1 | B | handleref | bare |
| 187 | `gdiplus.dll` | `GdipGraphicsClear` | 6 | 1 | B | handleref | bare |
| 188 | `gdiplus.dll` | `GdipSetImageAttributesColorKeys` | 6 | 1 | B | handleref | bare |
| 189 | `gdiplus.dll` | `GdipSetImageAttributesColorMatrix` | 6 | 1 | B | handleref | bare |
| 190 | `gdiplus.dll` | `GdipTranslateRegion` | 6 | 1 | B | handleref | bare |
| 191 | `gdiplus.dll` | `GdipTranslateWorldTransform` | 6 | 1 | B | handleref | bare |
| 192 | `user32.dll` | `EnumThreadWindows` | 6 | 1 | B | handleref | bare |
| 193 | `user32.dll` | `IsWindow` | 6 | 1 | B | handleref | bare |
| 194 | `user32.dll` | `SetActiveWindow` | 6 | 1 | B | handleref | bare |
| 195 | `comctl32.dll` | `ImageList_Add` | 7 | 1 | B | handleref | bare |
| 196 | `comctl32.dll` | `ImageList_ReplaceIcon` | 7 | 1 | B | handleref | bare |
| 197 | `gdi32.dll` | `CreateCompatibleBitmap` | 7 | 2 | B | handleref | bare |
| 198 | `gdiplus.dll` | `GdipDeleteFont` | 7 | 1 | B | handleref | bare |
| 199 | `gdiplus.dll` | `GdipDrawImagePointsRect` | 7 | 1 | B | handleref | bare |
| 200 | `gdiplus.dll` | `GdipDrawImageRectRect` | 7 | 1 | B | handleref | bare |
| 201 | `gdiplus.dll` | `GdipGetFontStyle` | 7 | 1 | B | handleref | bare |
| 202 | `gdiplus.dll` | `GdipGetFontUnit` | 7 | 1 | B | handleref | bare |
| 203 | `gdiplus.dll` | `GdipImageGetFrameDimensionsList` | 7 | 1 | B | handleref | bare |
| 204 | `gdiplus.dll` | `GdipTranslateMatrix` | 7 | 1 | B | handleref | bare |
| 205 | `user32.dll` | `DefMDIChildProc` | 7 | 1 | B | entrypoint-suffix | only-W |
| 206 | `user32.dll` | `DrawMenuBar` | 7 | 1 | B | handleref | bare |
| 207 | `user32.dll` | `GetAwarenessFromDpiAwarenessContext` | 7 | 1 | B | struct-return | bare |
| 208 | `user32.dll` | `GetIconInfo` | 7 | 2 | B | handleref | bare |
| 209 | `user32.dll` | `ScrollWindowEx` | 7 | 1 | B | handleref | bare |
| 210 | `user32.dll` | `UpdateWindow` | 7 | 1 | B | handleref | bare |
| 211 | `gdiplus.dll` | `GdipDisposeImageAttributes` | 8 | 1 | B | handleref | bare |
| 212 | `gdiplus.dll` | `GdipDrawImage` | 8 | 1 | B | handleref | bare |
| 213 | `gdiplus.dll` | `GdipGetTextureTransform` | 8 | 1 | B | handleref | bare |
| 214 | `gdiplus.dll` | `GdipSetImageAttributesWrapMode` | 8 | 1 | B | handleref | bare |
| 215 | `gdiplus.dll` | `GdipSetTextureTransform` | 8 | 1 | B | handleref | bare |
| 216 | `gdiplus.dll` | `GdipTranslateClip` | 8 | 1 | B | handleref | bare |
| 217 | `user32.dll` | `SetScrollInfo` | 8 | 1 | B | handleref | bare |
| 218 | `user32.dll` | `UnhookWindowsHookEx` | 8 | 1 | B | handleref | bare |
| 219 | `gdi32.dll` | `CreateBitmap` | 9 | 4 | A/B | array | bare |
| 220 | `gdi32.dll` | `GetTextMetricsA` | 9 | 3 | B | handleref | bare |
| 221 | `gdi32.dll` | `GetTextMetricsW` | 9 | 3 | B | handleref | bare |
| 222 | `gdi32.dll` | `IntersectClipRect` | 9 | 2 | B | handleref | bare |
| 223 | `gdiplus.dll` | `GdipCombineRegionRect` | 9 | 1 | B | handleref | bare |
| 224 | `gdiplus.dll` | `GdipCreateRegionRect` | 9 | 1 | B | byref-scalar | bare |
| 225 | `gdiplus.dll` | `GdipDrawImageRectI` | 9 | 1 | B | handleref | bare |
| 226 | `gdiplus.dll` | `GdipSetClipRegion` | 9 | 1 | B | handleref | bare |
| 227 | `user32.dll` | `CallNextHookEx` | 9 | 1 | B | handleref | bare |
| 228 | `user32.dll` | `DestroyMenu` | 9 | 1 | B | handleref | bare |
| 229 | `user32.dll` | `DestroyWindow` | 9 | 1 | B | handleref | bare |
| 230 | `user32.dll` | `DrawIconEx` | 9 | 2 | B | handleref | bare |
| 231 | `user32.dll` | `EnumDisplayMonitors` | 9 | 1 | B | handleref | bare |
| 232 | `user32.dll` | `FillRect` | 9 | 3 | B | handleref | bare |
| 233 | `user32.dll` | `FindWindow` | 9 | 1 | B | entrypoint-suffix | only-W |
| 234 | `user32.dll` | `InvalidateRgn` | 9 | 1 | B | handleref | bare |
| 235 | `user32.dll` | `KillTimer` | 9 | 1 | B | handleref | bare |
| 236 | `user32.dll` | `SetTimer` | 9 | 1 | B | handleref | bare |
| 237 | `uxtheme.dll` | `CloseThemeData` | 9 | 1 | B | handleref | bare |
| 238 | `uxtheme.dll` | `DrawThemeParentBackground` | 9 | 1 | B | handleref | bare |
| 239 | `uxtheme.dll` | `OpenThemeData` | 9 | 1 | B | handleref | bare |
| 240 | `gdi32.dll` | `GetMapMode` | 10 | 2 | B | handleref | bare |
| 241 | `gdi32.dll` | `RestoreDC` | 10 | 2 | B | handleref | bare |
| 242 | `gdi32.dll` | `SetMapMode` | 10 | 3 | B | handleref | bare |
| 243 | `gdiplus.dll` | `GdipGetRegionBounds` | 10 | 1 | B | handleref | bare |
| 244 | `gdiplus.dll` | `GdipSetInterpolationMode` | 10 | 1 | B | handleref | bare |
| 245 | `user32.dll` | `DestroyIcon` | 10 | 1 | B | handleref | bare |
| 246 | `user32.dll` | `RemoveMenu` | 11 | 1 | B | handleref | bare |
| 247 | `uxtheme.dll` | `IsThemePartDefined` | 11 | 1 | B | handleref | bare |
| 248 | `gdiplus.dll` | `GdipGetDC` | 2 | 1 | B | byref-scalar+handleref | bare |
| 249 | `gdiplus.dll` | `GdipSaveGraphics` | 2 | 1 | B | byref-scalar+handleref | bare |
| 250 | `user32.dll` | `PostMessage` | 2 | 3 | B | entrypoint-suffix+handleref | only-W |
| 251 | `gdiplus.dll` | `GdipCreateFromHDC` | 3 | 1 | B | byref-scalar+handleref | bare |
| 252 | `gdiplus.dll` | `GdipGetImageHeight` | 3 | 1 | B | byref-scalar+handleref | bare |
| 253 | `gdiplus.dll` | `GdipGetImageWidth` | 3 | 1 | B | byref-scalar+handleref | bare |
| 254 | `user32.dll` | `GetClassInfo` | 3 | 2 | B | entrypoint-suffix+handleref | only-W |
| 255 | `user32.dll` | `GetWindowLong` | 3 | 1 | B | entrypoint-suffix+handleref | only-W |
| 256 | `user32.dll` | `GetWindowLongPtr` | 3 | 1 | B | entrypoint-suffix+handleref | only-W |
| 257 | `user32.dll` | `GetWindowTextLength` | 3 | 1 | B | entrypoint-suffix+handleref | only-W |
| 258 | `user32.dll` | `GetWindowThreadProcessId` | 3 | 1 | B | byref-scalar+handleref | bare |
| 259 | `user32.dll` | `RegisterClass` | 3 | 1 | B | entrypoint-suffix+nonblittable-struct | only-W |
| 260 | `user32.dll` | `SetWindowText` | 3 | 1 | B | entrypoint-suffix+handleref | only-W |
| 261 | `user32.dll` | `UnregisterClass` | 3 | 1 | B | entrypoint-suffix+handleref | only-W |
| 262 | `gdiplus.dll` | `GdipGetFontSize` | 4 | 1 | B | byref-scalar+handleref | bare |
| 263 | `gdiplus.dll` | `GdipImageSelectActiveFrame` | 4 | 1 | B | byref-other+handleref | bare |
| 264 | `gdiplus.dll` | `GdipIsInfiniteRegion` | 4 | 1 | B | byref-scalar+handleref | bare |
| 265 | `gdiplus.dll` | `GdipIsMatrixIdentity` | 4 | 1 | B | byref-scalar+handleref | bare |
| 266 | `kernel32.dll` | `DuplicateHandle` | 4 | 1 | B | byref-scalar+handleref | bare |
| 267 | `kernel32.dll` | `GetShortPathName` | 4 | 1 | B | entrypoint-suffix+stringbuilder | only-W |
| 268 | `ole32.dll` | `CoRegisterMessageFilter` | 4 | 1 | B | byref-scalar+handleref | bare |
| 269 | `shell32.dll` | `ShellExecute` | 4 | 2 | B | entrypoint-suffix+handleref | only-W |
| 270 | `user32.dll` | `PeekMessage` | 4 | 1 | B | entrypoint-suffix+handleref | only-W |
| 271 | `gdi32.dll` | `CombineRgn` | 5 | 3 | B | handleref+struct-return | bare |
| 272 | `gdiplus.dll` | `GdipCreateBitmapFromScan0` | 5 | 1 | B | byref-scalar+handleref | bare |
| 273 | `gdiplus.dll` | `GdipCreateFont` | 5 | 1 | B | byref-scalar+handleref | bare |
| 274 | `gdiplus.dll` | `GdipGetImageGraphicsContext` | 5 | 1 | B | byref-scalar+handleref | bare |
| 275 | `gdiplus.dll` | `GdipGetImagePixelFormat` | 5 | 1 | B | byref-scalar+handleref | bare |
| 276 | `gdiplus.dll` | `GdipGetImageRawFormat` | 5 | 1 | B | byref-other+handleref | bare |
| 277 | `gdiplus.dll` | `GdipGetRegionHRgn` | 5 | 1 | B | byref-scalar+handleref | bare |
| 278 | `kernel32.dll` | `RtlMoveMemory` | 5 | 7 | B/E | array+handleref | bare |
| 279 | `user32.dll` | `DrawTextExA` | 5 | 2 | B | array+handleref | bare |
| 280 | `user32.dll` | `SetWindowLong` | 5 | 2 | B | entrypoint-suffix+handleref | only-W |
| 281 | `user32.dll` | `SetWindowLongPtr` | 5 | 2 | B | entrypoint-suffix+handleref | only-W |
| 282 | `gdiplus.dll` | `GdipCreateBitmapFromHBITMAP` | 6 | 1 | B | byref-scalar+handleref | bare |
| 283 | `gdiplus.dll` | `GdipCreateFromHWND` | 6 | 1 | B | byref-scalar+handleref | bare |
| 284 | `gdiplus.dll` | `GdipGetDpiY` | 6 | 1 | B | array+handleref | bare |
| 285 | `gdiplus.dll` | `GdipGetEmHeight` | 6 | 1 | B | byref-scalar+handleref | bare |
| 286 | `gdiplus.dll` | `GdipGetFamilyName` | 6 | 1 | B | handleref+stringbuilder | bare |
| 287 | `gdiplus.dll` | `GdipGetFontHeight` | 6 | 1 | B | byref-scalar+handleref | bare |
| 288 | `gdiplus.dll` | `GdipGetLineSpacing` | 6 | 1 | B | byref-scalar+handleref | bare |
| 289 | `gdiplus.dll` | `GdipGetPropertyItemSize` | 6 | 1 | B | byref-scalar+handleref | bare |
| 290 | `user32.dll` | `LoadCursor` | 6 | 1 | B | entrypoint-suffix+handleref | only-W |
| 291 | `gdi32.dll` | `CreateFontIndirect` | 7 | 2 | B | entrypoint-suffix+object | only-W |
| 292 | `gdi32.dll` | `GetPaletteEntries` | 7 | 2 | B | array+handleref | bare |
| 293 | `gdi32.dll` | `SelectClipRgn` | 7 | 4 | B | handleref+struct-return | bare |
| 294 | `gdiplus.dll` | `GdipBitmapGetPixel` | 7 | 1 | B | byref-scalar+handleref | bare |
| 295 | `gdiplus.dll` | `GdipCreateTexture` | 7 | 1 | B | byref-scalar+handleref | bare |
| 296 | `gdiplus.dll` | `GdipGetFamily` | 7 | 1 | B | byref-scalar+handleref | bare |
| 297 | `gdiplus.dll` | `GdipGetImageFlags` | 7 | 1 | B | byref-scalar+handleref | bare |
| 298 | `gdiplus.dll` | `GdipImageGetFrameDimensionsCount` | 7 | 1 | B | byref-scalar+handleref | bare |
| 299 | `gdiplus.dll` | `GdipMeasureString` | 7 | 1 | B | byref-scalar+handleref | bare |
| 300 | `kernel32.dll` | `GetExitCodeThread` | 7 | 2 | B | byref-scalar+handleref | bare |
| 301 | `user32.dll` | `LoadIcon` | 7 | 1 | B | entrypoint-suffix+handleref | only-W |
| 302 | `user32.dll` | `SystemParametersInfoForDpi` | 7 | 1 | B | name-clash+nonblittable-struct | bare |
| 303 | `gdiplus.dll` | `GdipCloneImage` | 8 | 1 | B | byref-scalar+handleref | bare |
| 304 | `gdiplus.dll` | `GdipCreateBitmapFromHICON` | 8 | 1 | B | byref-scalar+handleref | bare |
| 305 | `gdiplus.dll` | `GdipCreateFontFamilyFromName` | 8 | 1 | B | byref-scalar+handleref | bare |
| 306 | `user32.dll` | `SetWindowsHookEx` | 8 | 1 | B | entrypoint-suffix+handleref | only-W |
| 307 | `gdiplus.dll` | `GdipGetImageType` | 9 | 1 | B | byref-scalar+handleref | bare |
| 308 | `gdiplus.dll` | `GdipGetLogFontA` | 9 | 1 | B | handleref+object | bare |
| 309 | `gdiplus.dll` | `GdipGetLogFontW` | 9 | 1 | B | handleref+object | bare |
| 310 | `gdi32.dll` | `CreateDC` | 10 | 3 | B | entrypoint-suffix+handleref | only-W |
| 311 | `gdiplus.dll` | `GdipCreateHBITMAPFromBitmap` | 10 | 1 | B | byref-scalar+handleref | bare |
| 312 | `user32.dll` | `CreateWindowEx` | 2 | 1 | B | entrypoint-suffix+handleref+object | only-W |
| 313 | `user32.dll` | `GetClassName` | 3 | 1 | B | entrypoint-suffix+handleref+stringbuilder | only-W |
| 314 | `user32.dll` | `GetWindowText` | 3 | 1 | B | entrypoint-suffix+handleref+stringbuilder | only-W |
| 315 | `user32.dll` | `GetUserObjectInformation` | 4 | 1 | B | byref-scalar+entrypoint-suffix+handleref | only-W |
| 316 | `user32.dll` | `InsertMenuItem` | 5 | 1 | B | entrypoint-suffix+handleref+nonblittable-struct | only-W |
| 317 | `gdi32.dll` | `GetDIBits` | 6 | 2 | B | array+handleref+nonblittable-struct | bare |
| 318 | `gdiplus.dll` | `GdipCreateFontFromLogfontA` | 6 | 1 | B | byref-scalar+handleref+object | bare |
| 319 | `gdiplus.dll` | `GdipCreateFontFromLogfontW` | 6 | 1 | B | byref-scalar+handleref+object | bare |
| 320 | `gdiplus.dll` | `GdipImageGetFrameCount` | 6 | 1 | B | array+byref-other+handleref | bare |
| 321 | `user32.dll` | `GetMenuItemInfo` | 6 | 2 | B | entrypoint-suffix+handleref+nonblittable-struct | only-W |
| 322 | `user32.dll` | `SetMenuItemInfo` | 7 | 1 | B | entrypoint-suffix+handleref+nonblittable-struct | only-W |
| 323 | `user32.dll` | `GetMonitorInfo` | 8 | 1 | B | entrypoint-suffix+handleref+nonblittable-struct | only-W |
| 324 | `gdi32.dll` | `CreateDIBSection` | 6 | 2 | B | array+byref-scalar+handleref+nonblittable-struct | bare |
| 325 | `user32.dll` | `SystemParametersInfo` | 3 | 10 | B | array+byref-scalar+entrypoint-suffix+name-clash+nonblittable-struct | only-W |
| 326 | `gdi32.dll` | `GetObject` | 5 | 12 | B | array+byref-scalar+entrypoint-suffix+handleref+name-clash+nonblittable-struct | only-W |
| 327 | `user32.dll` | `SendMessage` | 1 | 61 | B | array+byref-other+byref-scalar+entrypoint-suffix+handleref+name-clash+nonblittable-struct+stringbuilder | only-W |

共 **327** 个 EntryPoint（529 条声明）。其中 85 个在 3 跳以内（最小可跑集的"第一波"）。

### 4.3 贪心接线顺序（每加一个能力，阶段 1 闭环多少条）

| 步骤 | 新增能力 | 闭环声明数 / 529 |
|---|---|---|
| 0 | （无新能力） | 64 |
| 1 | `handleref` | 295 |
| 2 | `entrypoint-suffix` | 374 |
| 3 | `byref-scalar` | 428 |
| 4 | `nonblittable-struct` | 452 |
| 5 | `array` | 473 |
| 6 | `name-clash` | 483 |
| 7 | `object` | 490 |
| 8 | `stringbuilder` | 495 |
| 9 | `struct-return` | 500 |
| 10 | `byref-other` | 505 |

### 4.4 补齐全部 marshal 能力后仍不可达的 EntryPoint（C/D 类）

| DLL | EntryPoint | class | 说明 |
|---|---|---|---|
| `gdiplus.dll` | `GdipCreateBitmapFromStream` | C | COM 运行时（RCW/流接口） |
| `ole32.dll` | `OleInitialize` | C | COM 运行时（RCW/流接口） |
| `ole32.dll` | `OleUninitialize` | C | COM 运行时（RCW/流接口） |
| `ole32.dll` | `RegisterDragDrop` | C | COM 运行时（RCW/流接口） |
| `ole32.dll` | `RevokeDragDrop` | C | COM 运行时（RCW/流接口） |
| `oleacc.dll` | `CreateStdAccessibleObject` | C | COM 运行时（RCW/流接口） |
| `oleacc.dll` | `LresultFromObject` | C | COM 运行时（RCW/流接口） |
| `UIAutomationCore.dll` | `UiaClientsAreListening` | D | 域外（IME/UIA/HTMLHelp） |
| `UIAutomationCore.dll` | `UiaDisconnectProvider` | D | 域外（IME/UIA/HTMLHelp） |
| `UIAutomationCore.dll` | `UiaRaiseAutomationEvent` | D | 域外（IME/UIA/HTMLHelp） |
| `UIAutomationCore.dll` | `UiaReturnRawElementProvider` | D | 域外（IME/UIA/HTMLHelp） |
| `hhctrl.ocx` | `HtmlHelp` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmAssociateContext` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmCreateContext` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmGetContext` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmGetConversionStatus` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmGetOpenStatus` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmReleaseContext` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmSetConversionStatus` | D | 域外（IME/UIA/HTMLHelp） |
| `imm32.dll` | `ImmSetOpenStatus` | D | 域外（IME/UIA/HTMLHelp） |

共 **20 个**。它们不阻塞阶段 1 的主路径（`Application.Run` 能跑起来、能创建窗口、能画、能收 Click），只影响辅助特性（无障碍/输入法/帮助窗口/剪贴板流）。

### 4.5 主路径「无它则哪一步失败」

| 失败点 | 缺的能力 | 依据 |
|---|---|---|
| 窗口类注册 + 建窗 | `entrypoint-suffix`（`RegisterClass`/`CreateWindowEx`/`RegisterWindowMessage` 只有 W 名） + `handleref`（`hWndParent`/`hMenu`/`hInstance`） + `object`（`lpParam` 是 `object`） | §4.2 第 62/81 行 |
| 消息泵 `GetMessage`/`PeekMessage`/`DispatchMessage` | `entrypoint-suffix` + `handleref`（`HWND`） + by-ref `MSG` | §4.2 |
| `DefWindowProc`/`CallWindowProc` | `entrypoint-suffix` | §4.2 第 28/29 行 |
| WM_PAINT 绘制 | `handleref`（`BeginPaint`/`EndPaint`/`GetDC`/`ReleaseDC`/`SelectObject`） | §4.2 |
| `GetClientRect`/`GetWindowRect`/`InvalidateRect`/`SetWindowPos` | `handleref` + by-ref `RECT` | §4.2 |
| Click 事件（`SendMessage`/`PostMessage` WM_COMMAND） | `entrypoint-suffix` + `handleref`；含 `int[]`/`int&` 的重载另外需要 `array`/`byref-scalar` | §4.2 |
| `Application.Run` 内部的 DPI/主题/ActCtx 初始化 | `entrypoint-suffix`（`GetModuleHandle`/`LoadLibrary`） + `byref-scalar`（`ActivateActCtx` 的 `IntPtr&`） | §4.2 |

## 5. COM 面

程序集里 `[ComImport]`（`TypeAttributes.Import`）类型共 **201** 个（Forms 198 / Drawing 3；含嵌套类型）。按域分组：

| 域 | 类型数 | 阶段 1 是否可达 | 理由 |
|---|---|---|---|
| ClickOnce / isolated storage deployment | 64 | 否 | unreachable in stage 1 (the whole System.Deployment.Internal.Isolation namespace is only entered from the ClickOnce bootstrapper) |
| ActiveX / OLE embedding (AxHost) | 29 | 否 | stage 3+ (needs IOleObject/IOleInPlaceSite plus a container window) |
| WebBrowser control | 6 | 否 | stage 3+ (full MSHTML host + IDispatch; also needs the CLR host surface) |
| IDispatch / type-info automation | 25 | 否 | stage 3 (RichTextBox/ActiveX automation; the runtime has RCW primitives but no ITypeInfo marshaller) |
| Vista file dialogs (IFileDialog/IShellItem) | 7 | 否 | stage 2 (comdlg32 path covers GetOpenFileName/GetSaveFileName first) |
| OLE font/picture (StdFont/StdPicture) | 6 | 否 | stage 2 (Font/Image interop for OLE hosts only) |
| Accessibility / UI Automation providers | 21 | **是**（出现在阶段 1 可达的 P/Invoke 签名里） | unreachable in stage 1 (UIAutomationCore is a D-class module) |
| IStream/IStorage/ILockBytes/IMalloc | 5 | **是**（出现在阶段 1 可达的 P/Invoke 签名里） | stage 3 (Clipboard/metafile/Image-from-stream; GdipCreateBitmapFromStream is the only class-C P/Invoke inside the stage-1 closure) |
| OLE drag & drop (RegisterDragDrop/DoDragDrop) | 2 | **是**（出现在阶段 1 可达的 P/Invoke 签名里） | stage 3 (needs a real IOleDropTarget CCW dispatch loop) |
| CLR hosting (ICorRuntimeHost) | 1 | 否 | unreachable in stage 1 (the hosting API only exists inside a CLR host) |
| RichTextBox OLE object site | 1 | 否 | stage 3 (needs IOleObject/IRichEditOleCallback) |
| Other COM interfaces | 3 | 否 | evaluated per interface below |
| COM coclasses / non-interface imports | 31 | 否 | activated via CoCreateInstance (class C) |

### 5.1 阶段 1 真正会碰到的 COM

阶段 1 可达的 P/Invoke 里出现 ComImport 参数/返回的接口只有：`IStream`, `IOleDropTarget`, `IRawElementProviderSimple`。

对应的声明（按跳数）：

| DLL | EntryPoint | 跳数 | 参数里的 COM 接口 | class | 触发条件 |
|---|---|---|---|---|---|
| `UIAutomationCore.dll` | `UiaReturnRawElementProvider` | 2 | `IRawElementProviderSimple` | D | UIA 客户端调用 WM_GETOBJECT |
| `UIAutomationCore.dll` | `UiaDisconnectProvider` | 3 | `IRawElementProviderSimple` | D | 同 DLL 的常规用法 |
| `ole32.dll` | `RegisterDragDrop` | 4 | `IOleDropTarget` | C | `Control.AllowDrop = true` |
| `UIAutomationCore.dll` | `UiaRaiseAutomationEvent` | 8 | `IRawElementProviderSimple` | D | 同 DLL 的常规用法 |
| `gdiplus.dll` | `GdipCreateBitmapFromStream` | 8 | `IStream` | C | `Image.FromStream` / `Bitmap(Stream)` |

这些都不会在「Application.Run(new Form()) + Button/Label + Click + WM_PAINT」的最小路径上主动触发（无障碍/拖放/流式加载都是被动的），所以阶段 1 不阻塞。

### 5.2 运行体现状

- 已有：`icall_com_query_interface` / `add_ref` / `release` / `call_vtable_slot`（`src/runtime/include/internal/il2cpp_com.h:771`）、`icall_com_create_instance_clsid`（`src/runtime/src/com.cpp:8045`）、ProgID 激活、`get_object_for_iunknown`、BSTR 桥、`CreateStreamOnHGlobal`。
- 托管映射：`Marshal.GetActiveObject` / `GetObjectForIUnknown` / `ReleaseComObject` / `CreateObject` / `IsComObject` 已接（`src/il2cppc/Emit/MethodMap.Marshal.cs:457`~`:543`）；其余 COM 成员 **LOUD**（`:88`）。
- 缺：ComImport **接口本身的声明式实例化**（`new SomeComClass()` 走 CLR 激活器）与**接口方法调用**（vtable slot 有运行时原语，但转译器不把 `callvirt IFoo::Bar` 翻成 slot 号）。这是 §5 各域不可达的共同原因，不是「不支持 COM」这么笼统。

### 5.3 4 个 `InternalCall` 构造函数

`FileOpenDialogRCW` / `FileSaveDialogRCW` / `CorRuntimeHost` / `SMsoComponentManager` 的 `.ctor` 带 `InternalCall`，属于「COM 类激活」这一条 CLR 语义：net48 由 CLR 的 COM 激活器实现，不经过托管 IL。它们只在 `FileDialog`(Vista 模式) / CLR 宿主 / VB 组件管理器路径上，都在阶段 2+。

## 6. 嵌入资源（223 个）

`ManifestResource` 实测 **227** 个（Forms 223 / Drawing 4）。按扩展名：

| 扩展名 | 个数 | 用途 |
|---|---|---|
| `.bmp` | 185 | 控件/工具箱位图（ToolboxBitmap、BindingNavigator 图标） |
| `.ico` | 21 | 窗体/控件默认图标 |
| `.cur` | 14 | Splitter/MDI 光标 |
| `.resources` | 6 | 字符串表 + 对话框模板（PrintPreview/MdiWindow）+ 位图内嵌（`System.Windows.Forms.resources` / `System.SR.resources`） |
| `.png` | 1 | 单个 PNG 资源 |

现状（**旧文档「转译器无嵌入资源处理」已作废**）：

- 计数与导出：`src/il2cppc/Emit/CodeGenerator.cs:9167`（`ResourceCount`）→ 条件 include `:2084` → 运行时填充 `:13499`。
- 读取：`Assembly.GetManifestResourceStream` / `GetManifestResourceNames` （`src/il2cppc/Emit/MethodMap.Resources.cs:54`）；`ResourceManager.GetString` 及文化回退链（`src/il2cppc/Emit/MethodMap.Resource.cs:58`）。
- 缺口：**图片类资源**（185 bmp + 21 ico + 14 cur）需要 `Image.FromStream`/`Icon` 解码，而 `GdipCreateBitmapFromStream` 是 C 类（流接口）。阶段 1 用代码赋值文本即可绕开；但控件默认图标/光标（`.cur`/`.ico`）由 `LoadCursor`/`LoadIcon` 从**模块句柄**取，走 user32 而不是资源池 —— 需要 §4 的 `entrypoint-suffix`+`handleref`。

## 7. 与旧文档 `docs/plans/winforms-plan.md` 的差异

旧文档是 2026-08-07 的，行号与判定都已漂移。逐条列出**被推翻**的结论及其现行证据（判定口径与 §1 相同：以 `run-marshal-probe.ps1` 的 red/green 为准）：

| # | 旧文档结论（原文位置） | 现行事实 | 证据 |
|---|---|---|---|
| 1 | 「PinvokeParamList 发射 int32_t（CodeGenerator.cs:2966-2971）」，除 int32/string 外一律 int32_t | **推翻**：参数矩阵完整（i1..u8/f/d/n/委托/结构体），未知编码 LOUD 拒绝 | `src/il2cppc/Emit/CodeGenerator.cs:9108`、`:9138` |
| 2 | 「UTF-16 (WCHAR*) marshal —— 大缺口：只有 UTF-8；无 to_utf16_alloc」 | **推翻**：UTF-16 入参 + 运行时 + 解码三侧齐备 | `src/il2cppc/Emit/CodeGenerator.cs:9096`、`src/runtime/src/pinvokeext.cpp:136`/`:166` |
| 3 | 「结构体 by-value/by-ref —— 大缺口：PinvokeParamList 只认 int32_t/const char*」 | **部分推翻**：by-ref/by-value **参数**已支持（含未注册结构体按地址透传）；**仅 by-value 返回**仍缺 | `src/il2cppc/Emit/CodeGenerator.cs:9075`/`:9081`、`:9153`；probe 两种结局 |
| 4 | 「IntPtr 参数 —— 大缺口：参数侧无 ptr 处理，x64 截断」 | **推翻**：`n => intptr_t`，且 probe 通过 C++ 构建 | `src/il2cppc/Emit/CodeGenerator.cs:9114` |
| 5 | 「委托→函数指针 —— 可接（GetFunctionPointerForDelegate 施工中 #11）」 | **已落地**，且发现旧文档未记的 `HandleRef` 缺口 | `src/il2cppc/Emit/CodeGenerator.cs:9128`、`src/il2cppc/Emit/MethodRefResolver.cs:3227` |
| 6 | 「PtrToStringUni not wired（MethodMap.Marshal.cs:110）」 | **推翻**：已接线 | `src/il2cppc/Emit/MethodMap.Marshal.cs:120` |
| 7 | 「嵌入资源 —— 转译器无 ManifestResource 解码（grep 0 匹配）；无 ResourceManager icall」 | **推翻**：资源池 + ResourceManager 全链已实现 | `src/il2cppc/Emit/CodeGenerator.cs:9167`/`:2084`/`:13499`、`src/il2cppc/Emit/MethodMap.Resources.cs:54` |
| 8 | 「COM —— 无 COM 运行时（M8 远期）」 | **部分推翻**：RCW/CCW/IDispatch 骨架 + Marshal COM 面已在；缺的是 ComImport 接口声明式激活与 vtable 调用 | `src/runtime/include/internal/il2cpp_com.h:3`、`src/runtime/src/com.cpp:8045`、`src/il2cppc/Emit/MethodMap.Marshal.cs:457` |
| 9 | 「SetLastError 已覆盖（capture/clear/get_last_error）」 | **半推翻**：capture/get 有，**clear 从未被发射** | `src/runtime/src/pinvokeext.cpp:329`（定义）vs `src/il2cppc` 无发射点 |
| 10 | 「CharSet 分布 Auto 479 / None 108 / Unicode 25 / Ansi 13」（Forms 子集） | **口径不清楚**：这是 Forms(625) 子集；1317 条全量是 Unicode 595 / Auto 586 / None 117 / Ansi 19 | `pinvokes.tsv` CharSet 列聚合 |
| 11 | 「user32 281 / gdi32 117 / kernel32 60」 | **口径不清楚**：Forms 子集数；Forms+Drawing 全量是 301/203/71 | `pinvokes.tsv` 按 assembly×DLL 聚合 |
| 12 | （旧文档未提）结构体布局是否逐字节等价 | **新增缺口**：55 条 P/Invoke 用的结构体含托管引用字段（string/数组/委托），生成的 C++ 结构体是 GC 指针而非原生内联数据，静默错位 | `src/il2cppc/MetadataLoader.cs:1011`、`tools/audit/WinFormsStructProbe` 的 `structs.tsv` |
| 13 | 「阶段 1 五个大缺口：UTF-16、结构体、IntPtr 参数、byte[] 参数、委托回调」 | **推翻一半**：三项已补齐；byte[] 与 ref/out 标量仍缺，并**新增** `HandleRef` 与入口名后缀两项（旧文档完全没提） | §1 表格 1.3/1.5/1.2/1.11 vs 1.7/1.8/1.9/1.10 |

**合计：13 条差异 —— 完全推翻 5 条、部分推翻 4 条、旧称「施工中」现已落地 1 条、统计口径不同 2 条、旧文档未记的新缺口 1 条。被推翻/部分推翻合计 **9 条**。**

**未被推翻**的旧结论（仍然成立）：

- 数组参数（`byte[]`/`char[]`/`int[]`/`Ptr[]`/`struct[]`）无 pin/copy 桥（1.7）。
- uxtheme(31) / imm32(10) / UIAutomationCore(9) 属后置或不可达（§2 分类）。
- TypeDescriptor / PropertyDescriptor 面未建（D2 反射支撑面）；本审计未展开，标为阶段 2+ 风险。

## 8. 下一步建议（按 owner/DLL 成批接的最小改动队列）

顺序由 §4.3 的贪心结果与实际代码结构共同决定。每一步都写清「改哪个文件、覆盖多少条、怎么验证」。

| 批次 | 能力 | 改动位置 | 覆盖 | 验证 |
|---|---|---|---|---|
| **W1** | `HandleRef` → 原生句柄衰减 | `src/il2cppc/Emit/CodeGenerator.cs`（`PinvokeParamList` 与 `EmitIcallCallArgs`）：`r:System.Runtime.InteropServices.HandleRef` 编码 → `n`/`intptr_t`，取值表达式 `.Handle`。**注意**：这不是新运行时能力，`HandleRef` 是纯托管结构体，唯一要做的是别把它当原生结构体传 | 983 条 / 直接闭环 617 条 | `run-marshal-probe.ps1` 的 `HandleRef param` → `C++-BUILD-OK`；winforms1/winformsext 回归 |
| **W2** | 入口名 A/W 后缀探测 | `src/runtime/src/pinvoke.cpp:153`（`il2cpp_pinvoke_resolve`）：按 `CharSet`+`ExactSpelling` 依次试 `Name`、`NameW`、`NameA`。签名需带上 CharSet/ExactSpelling（`ResolvedMethod` 已有 `SetLastError`，再加两个字段） | 174 条 / 直接闭环 22 条 | `run-marshal-probe.ps1` 若加 `GetWindowText`（`ExactSpelling=false`）应不再抛 `EntryPointNotFoundException`；`WinFormsExportProbe` 的 `only-W` 集合作为测试清单 |
| **W3** | `ref`/`out` 标量 + `out Guid`/`out string` | `src/il2cppc/Emit/CodeGenerator.cs:9138`（放行 `B:i`/`B:n`/`B:l`/`B:d`/`B:b`/`B:V:System.Guid`/`B:r:System.String`）+ `EmitIcallCallArgs` 的地址转换 | 238 条 / 直接闭环 39 条 | probe 的 `ref int / ref IntPtr`、`out Guid`、`out string` → `C++-BUILD-OK`；`GetWindowThreadProcessId` 真机返值 |
| **W3b** | 非 blittable 结构体的字段 marshal | `src/il2cppc/MetadataLoader.cs:1011`（引用字段必须按 `ByValTStr`/`ByValArray`/函数指针的**原生形态**布局，而不是 GC 指针）+ `src/il2cppc/Emit/TypeEmitter.cs:1054`（布局按 marshal 后的宽度排）+ `src/il2cppc/Emit/CodeGenerator.cs:9045`（透传前的逐字段转换） | 55 条 / 阶段 1 直接闭环 24 条 | `WinFormsStructProbe` 的 `structs.tsv` 里 `NON-BLITTABLE` 归零；`SystemParametersInfo`（LOGFONT/NONCLIENTMETRICS）与 `RegisterClass`（WNDCLASS_D）真机回填 |
| **W4** | 数组 pin/copy | `src/il2cppc/Emit/CodeGenerator.cs`（`PinvokeParamList` + 折叠器 `src/il2cppc/Ir/StackFold.cs`）：复用 `src/runtime/src/pinvokeext.cpp:257` 的 `il2cpp_pinvoke_byte_array_data`，扩展到 `char[]`/`int[]`/`float[]`/结构体[] | 67 条 / 直接闭环 16 条 | probe 的 `byte[]`/`char[]`/`IntPtr[]`/`bool[]` → `C++-BUILD-OK`；`GetKeyboardState` 回填 256 字节 |
| **W5** | `StringBuilder` 缓冲 | 同上（按 `Capacity` 分配 native 缓冲，调用后回填） | 21 条 / 直接闭环 3 条 | probe 的 `StringBuilder buffer param` |
| **W6** | 结构体名与 SDK typedef 冲突 | `src/il2cppc/Emit/TypeEmitter.cs`（结构体 C++ 名加前缀，如 `Wf_LOGFONTW`）或 `generated.h` 的 forward declaration 改为不撞 typedef 的形式 | 11 条 | probe 的 `LOGFONTW by-ref` → `C++-BUILD-OK` |
| **W7** | by-value 结构体返回 | `src/il2cppc/Emit/CodeGenerator.cs:9153`（注册表按完整名解析，而不是只认三个） | 9 条 | probe 的 `struct by-value return` → `C++-BUILD-OK` |
| ~~**W8**~~ | ~~`SetLastError` 调用前清零~~ | **撤销该项：net48 不清零（§1.13），实施即偏离 byte-exact**。已用 `tests/cases/probe-lasterror` 钉住 net48 语义；`il2cpp_pinvoke_clear_last_error` 保留为未来 Core 模式的死代码 | 0 条（原估 879 条错误） | `probe-lasterror` `ok=True`；若有人发射 clear 该 probe 立即 DIFF |

经 W1~W8 后，阶段 1 可达集里剩下的只有 C/D 类（§4.4）。建议按 **W1 → W2 → W3** 的顺序开工：这三步合计闭环 678 条，且互不依赖。

## 9. 复现方法

全部证据可由下列命令重跑（`<repo>` 为仓库根，`<out>` 为任意临时目录）：

```powershell
# 1) 元数据枚举 + 调用图 + 资源 + ComImport
dotnet run --project <repo>/tools/audit/WinFormsPinvokeGraph -- <out>/graph `
  "System.Windows.Forms.Application|Run;System.Windows.Forms.Control|CreateHandle,WndProc,OnPaint,WmPaint;`
   System.Windows.Forms.Button|OnClick;System.Windows.Forms.Label|OnPaint;`
   System.Windows.Forms.NativeWindow|CreateHandle,WndProc" `
  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Windows.Forms.dll" `
  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Drawing.dll"

# 2) 真实 GetProcAddress 导出探测（裸名 / NameW / NameA）
dotnet run --project <repo>/tools/audit/WinFormsExportProbe -- <out>/graph/pinvokes.tsv <out>/graph/exports.tsv

# 2b) struct layout / blittability census
dotnet run --project <repo>/tools/audit/WinFormsStructProbe -- <out>/graph/pinvokes.tsv `
  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Windows.Forms.dll" `
  "C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Drawing.dll" > <out>/graph/structs.tsv

# 3) 逐条分类（A/B/C/D/E + 缺失能力集合）
python <repo>/tools/audit/classify_winforms_pinvokes.py <out>/graph <out>/classified.tsv

# 4) 阶段 1 闭环分析
python <repo>/tools/audit/p0_closure.py <out>/classified.tsv <out>/graph/reachable.tsv

# 5) marshal shape red/green probe（需要 net48 SDK + cmake + MinGW）
pwsh <repo>/tools/audit/run-marshal-probe.ps1 -WorkDir <out>/probe -CMake <cmake>

# 6) 本文件自校验（每条 file:line 必须真实存在且行号在范围内）
python <repo>/tools/audit/verify_winforms_report.py <repo>
```

第 3 步的 `classified.tsv` 是逐条明细（1317 行，含 `klass`/`needs`/`reason` 三列），审计时可直接 diff。

## 10. `struct-return` 重新实测（2026-11-02，基线 `c5cf04c8`，impl-struct-return 席位）

> 本节更正 §2.1 / §4.3 对 `struct-return` 的旧归因。证据均在
> `.opencode/tmp/impl-struct-return/NOTES.md`（原始 oracle/runtime 记录、ABI 数值、变异红绿
> 原始输出），不引用旧文档自身的结论。

### 10.1 9 条 `struct-return` 行全部是 `ret-enum:`，不是结构体返回

`classified.tsv` 里带 `struct-return` 的 9 行逐条是：

| 行 | 真实返回编码 |
|---|---|
| `GetAwarenessFromDpiAwarenessContext` | `ret-enum:...DPI_AWARENESS` |
| `GetCaretPos` / `GetPhysicalCursorPos` | `ret-enum:...BOOL` |
| `CombineRgn` ×2（Forms/Drawing `IntUnsafeNativeMethods`） | `ret-enum:...RegionFlags` |
| `GetRgnBox` ×2 | `ret-enum:...RegionFlags` |
| `SelectClipRgn` ×2 | `ret-enum:...RegionFlags` |

实测（`tests/cases/pinvoke-structreturn/`，net48 oracle == 转译运行时逐字节）：把它们当
「by-value 结构体返回」命名生成枚举类型是**错误**的——本模型的用户枚举 C++ 结构体带对象头
（`Il2CppObject _obj` + `int32_t value__`，16 字节），而原生枚举是 4 字节 int32；16 字节聚合走
隐藏 sret 指针，造成参数/返回全部错位（实测 `[enum.dpi]=-1619005961089843198`、
`[enum.caret.ret]=8589934593`、`[enum.combine]=0`）。因此修复里枚举返回**保持 `int32_t`**
（这正是 CLR 的枚举 ABI）。这 9 条真正缺的是 `handleref`（已实现）+ 枚举返回的 int32 语义；
新 case 用相同的 1/2/3 个 HandleRef 参数形态验证通过。

另外 4 条 `CombineRgn`/`GetRgnBox`/`SelectClipRgn` 的 `int` 返回重载从来不是
`struct-return` 缺陷，只差 `handleref`。

### 10.2 真正的 by-value 结构体返回缺陷（已修复并钉住）

任务书的原始输入（`SZP { int cx, cy; }`、`static extern SZP Query();`）在本基线实测为
`il2cpp_pinvoke_call<int32_t>(...).cx`，mingw 原始报错：

```
error: request for member 'cx' in 'il2cpp::runtime::il2cpp_pinvoke_call<...>(...)',
       which is of non-class type 'int'
```

根因：`PinvokeReturnStructNameOfHandle` 只认注册表（Point/Rectangle/Message），同程序集
TypeDef 返回 null，`PinvokeReturnTypeCppOf` 兜底成 `int32_t`；而 by-value/by-ref 参数路径
（`:9407-9412` / `:9387-9395`）早就有 `TypeEmitter.Sanitize` 兜底。修复后：

* 同程序集 TypeDef 结构体返回 -> 生成的 C++ 结构体名（与参数侧一致）；
* 同程序集枚举返回 -> `int32_t`（见 10.1 的实测理由）；
* 外部程序集 TypeRef 且无注册布局 -> 显式 `NotSupportedException`（不再静默猜 int32）；
* 桥头 `il2cpp_pinvoke_bridge.h` **无需改动**：`il2cpp_pinvoke_call<Ret>` 本来就按值返回
  `Ret`，`il2cpp_pinvoke_native_convert` 对聚合是合法拷贝；已用 BUILD+ORACLE 验证
  `AbiPair(2×int32, 8B)`、`AbiWide(int64, 8B)`、`AbiTriple(3×int32, 12B)`、
  `AbiFour(4B)` 四种 ABI 形态字段值全部逐字节一致。

新 case：`tests/cases/pinvoke-structreturn/`（338B，oracle/runtime 同 sha256；不在
`sweep-exclude.txt`）。变异证明：把同程序集兜底改回 `return null` -> 重新生成
`il2cpp_pinvoke_call<int32_t>` -> mingw 报 `no match for 'operator=' (... and 'int')`
（红）；恢复 -> 0 错、17 条记录全绿。

### 10.3 仍存在、但不属于本缺陷的实测残余（供后续能力项）

1. **STATIC 链接模块**的同程序集结构体返回：`MethodRefResolver.PinvokeRetCpp`（:2934）只映射
   标量，调用点仍发 `il2cpp_pinvoke_call<int32_t>`；动态（SHARED）/Windows probe 路径已绿。
   修它需要改 `MethodRefResolver.cs`（本席位允许文件之外）。
2. 结构体返回结果经**用户方法内联参数临时槽**取字段时，临时槽可能被通用 temp-slot 分配成
   `int32_t`（实测 `int64_t` 字段只写低 32 位）；直接 `call().field` 形态已绿。
3. **枚举 by-value 参数**仍按 16 字节枚举结构体传（实测 `[enum.combine]=0` 当传入枚举类型；
   传 `int` 后绿）。属枚举参数族，不是结构体返回。
4. `System.Drawing.Point.X/Y` 属性访问会发射未声明的 `System_Drawing_Point_get_X_...`
   helper（与本缺陷无关的既有缺口；新 case 用原生回读避开）。

## 11. name-clash 修订（2026-11-02，可执行红绿）

### 11.1 缺陷与复现

托管结构体在**全局命名空间**下按 `Sanitize(FullName)` 生成裸 C++ 名；`LOGFONTW` 这类名字正好是 `<windows.h>` 的 typedef（`typedef struct tagLOGFONTW {...} LOGFONTW;`），于是：

```text
generated.h:172:8: error: using typedef-name 'LOGFONTW' after 'struct'
generated.h:190:8: error: using typedef-name 'TEXTMETRICW' after 'struct'
```

转译器本身不报错（`transpile=0`），只有 C++ 编译失败；这属于**静默通过转译、构建期才红**的一类缺口。命名空间/嵌套形态（`Cases.LOGFONTW`、`NativeMethods+LOGFONTW`）不带裸名，不会触发；这解释了为什么现有 winforms1 的嵌套 `MSG`/`RECT`/`SIZE` 一直能编译。

### 11.2 冲突名清单（实测，不靠记忆）

探测方法：`g++ -E -P` 预处理 `windows.h + commctrl.h + commdlg.h + wingdi.h + winuser.h`，从 typedef 声明块抽取候选标识符，再对每个候选编译 `struct <name>;`，收集 `using typedef-name '<name>' after 'struct'`。mingw-w64 g++ 16.1.0 实测得到 **6373** 个冲突名（输出：`.opencode/tmp/impl-name-clash/sdk_typedef_conflicts.txt`）。除 `LOGFONTW/LOGFONTA`、`TEXTMETRICW/TEXTMETRICA` 外，还包含 `NOTIFYICONDATAW/A`、`CHOOSEFONTW`、`OPENFILENAMEW`、`MONITORINFOEXW`、`NONCLIENTMETRICSW`、`MINMAXINFO`、`WNDCLASSEXW`、`CREATESTRUCTW`、`MSG`、`RECT`、`POINT`、`SIZE` 等；只补 `LOGFONTW` 会立刻在 `TEXTMETRICW` 复现同一错误。完整清单以静态表内嵌在 `src/il2cppc/Emit/TypeEmitter.cs:2726`（`WindowsSdkTypedefNames`）。

### 11.3 实现（复用 `_cppNameOverrides`）

- `ResolveTypeCppNameCollisions` 在既有「托管类型互相撞名」后缀之后，对 **非嵌套**、未获 `_h` 后缀、且 `TypeCppNameCore` 恰为 SDK typedef 的类型写入 `_sdk`（`TypeEmitter.cs:820-836`）；定义、字段类型、`_class`/`_typeinfo`、数组元素等所有走 `TypeCppName` 的引用点自动一致。
- 字符串侧也必须拿到同一个后缀：`PinvokeParamListOf` 的 `((int32_t(*)(LOGFONTW*))...)` 和 `MethodRefResolver.UserMethodCppName` 不经过 `TypeCppName(TypeDef)`，而是各自的 `Sanitize`。因此 `Resolve` 从同一张 override 表派生 `_sanitizedNameOverrides`（仅收裸顶层非泛型 identity，`TypeEmitter.cs:837-848`），`SanitizeIdentChars` 命中时追加同一后缀（`TypeEmitter.cs:968-996`）。`TypeCppNameCore` 计算裸 core 时用线程局部开关抑制该查找，避免二次追加（`TypeEmitter.cs:643-659`）。
- 未改 `Sanitize` 的 Split/Join 与白名单规则；后缀只对**本 Generate 实际命中的全局冲突类型**生效。11 个锚逐字节不变（§11.6），winforms1 的嵌套 `MSG`/`RECT`/`SIZE` 不触发。

### 11.4 e2e 回归：`tests/cases/pinvoke-nameclash`

全局 `LOGFONTW` 与 `TEXTMETRICW`（各带 `[MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)]`）经 `ref` 传给本 case 自己的 `nameclashprobe.dll`；原生侧读取数值字段、返回 IN 值校验和并写回新值，托管侧打印返回值与写回后的字段（不是“能编译”断言）：

```text
[lf.ret]=1596 [lf.height]=13 [lf.width]=36 [lf.weight]=59 [lf.face]=Segoe UI
[tm.ret]=7900 [tm.height]=18 [tm.ascent]=30 [tm.descent]=42 [tm.weight]=54 [tm.face]=Consolas
```

net48 oracle 与 runtime 逐字节一致：**182B，sha=6fe3b4b1f46a1849e0f57c62e91139b4ad0fe3ea77ff02cae7f937a2c6a33f67**。

变异证明：把 `overrides[id] = SdkTypedefSuffix;` 改成空后缀 → 同一 case 回到 11.1 的 `using typedef-name after 'struct'`（C++ 构建失败）→ 恢复后 182B 双绿。变异标记 grep 为 0、`git status` 干净。

### 11.5 四个 EntryPoint 在 name-clash 修复后还差什么

先说明口径：GAC 里这四个入口的结构体是**嵌套**形态（`System.Windows.Forms.NativeMethods+...`），C++ 名带前缀，本来就不撞 typedef；审计的 name-clash 标记对应“全局镜像结构体”形态（本次修复的对象）。下表按**当前代码**（不是旧审计基线）列出剩余能力：

| EntryPoint | 本次修复后仍缺 | 证据（当前代码） |
|---|---|---|
| `SystemParametersInfoForDpi` | `nonblittable-struct`：`NONCLIENTMETRICS` 内含 `LOGFONTW` 的 `ByValTStr`，本模型把该字段落成 GC 指针而非内联 `WCHAR[32]`；按值传递的 ABI/尺寸也不对 | `MetadataLoader.cs:1011`、`TypeEmitter.cs` 的 `LayoutValueTypeFields`；审计 §1.16b/W3b |
| `SystemParametersInfo` | `nonblittable-struct`（同上，`NONCLIENTMETRICS`/`LOGFONT` 重载）+ `bool[]`（net48 4 字节 BOOL 元素，模型 1 字节，当前 LOUD 拒绝） | `CodeGenerator.cs:9572-9574`（bool[] LOUD）；数组原语 `int[]`/`IntPtr[]` 已支持（`:9541`）；entrypoint-suffix/byref-scalar 已补 |
| `GetObject` | 仅 `nonblittable-struct`（`LOGFONT` 的 `ByValTStr`）；`HandleRef` 衰减、A/W 后缀探测、`ref int`、`int[]` 均已打通 | handleref `CodeGenerator.cs:9386`；suffix `:11965`/runtime `pinvoke.cpp:314`；byref-scalar `:9404`；数组 `:9541` |
| `SendMessage` | `nonblittable-struct`（`LOGFONT`/`CHARFORMATW` 等）+ `StringBuilder` 缓冲参数（当前 LOUD）+ `ref object`（`B:r:` 当前 LOUD）；`HandleRef`/suffix/byref-scalar/`int[]`/string/intptr 已通 | StringBuilder/ref-object LOUD `CodeGenerator.cs:9475-9480`；其余同上 |

结论：name-clash 是这些入口的**独立前置缺口**，修完后它们不等于全通；剩余缺口按上表逐项收敛，其中 `nonblittable-struct`（W3b）是四个入口的共同剩余项。

### 11.6 锚回归（本 worktree difftest，oracle==runtime）

| case | 字节 | sha256 |
|---|---|---|
| winforms1 | 20521 | `8a4c0011723c4e462ad6240f094841c905e086dde4d9d2a0f2f5a975794c2138` |
| collation1 | 1356 | `90013e33c575bf609c3d4a50ac2a7a6bd1233237f12908e5885625307853c8ca` |
| urihash1 | 1417 | `9e622815bf31d62eaaba80863848d7158a59bda584816ed1fc6b67c77d3366d7` |
| diffuri001 | 585 | `20d4483c1f06658e916a2278c0873bec25cba48dedf7ffa508372d08e09d6061` |
| probe-pinvoke-array | 845 | `b8f5d7e861eb5e6acf723e1a32815a91326d6987bad91dbc9da1b52008cba657` |
| probe-pinvoke-byref | 1185 | `55f3e5c810d2b015af0fca36c58d251e0cb7704fad0a657a02712fd1e085ef66` |
| probe-pinvoke-byrefother | 648 | `10c90ffd2dc29415a2e6d97e49cfedacfc9367da0f415df92087bf64659d6bb2` |
| probe-handleref | 217 | `a089ccbfc400627c7d360a0deb5b8d8ad6cf66c50dd92aa5ee87b1cfc05b766a` |
| dtoiformattable1 | 1421 | `3c919969fe9229b0593f53ebce2d432fa66625d9fac7b84930332ab7f7d30195` |
| dtoobjcmp1 | 710 | `40fcc5a09d31f6764e3ed7a079367b99c2c5aba9714b0d757fed6b5701faedb5` |
| corelibp0 | 887 | `391cb96fe4862f2af565c2ef9b56037cbf7a0ccb6c48dd25e24550a6fc577d56` |

单位测试：`dotnet test tests/unit/unit.csproj -c Debug` → **4147 通过 / 0 失败 / 0 跳过**；C0 `dotnet build src/il2cppc/il2cppc.csproj -c Debug --no-incremental` → **0 警告 0 错误**。
