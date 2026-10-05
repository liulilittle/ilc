# COM Stage 15 规格 — OLE 拖放最小面（托管派发 E_NOTIMPL 过渡）

- 日期：2026-08-16
- 性质：审计子代理产出（纯研究，无代码改动）
- 上游：stage 14 审计结论 "拖放完整面（含托管派发）stage 15+ 远期；拖放最小面（托管派发 E_NOTIMPL）可作 stage 14b 过渡"
- 基线：HEAD 83980a8（stage 14 GetComInterfaceForObject 接线，test_com 400+ 断言，runtime 139/139）
- 前置：stage 14 已接线 `icall_com_get_com_interface_for_object(o, iid)`（com.cpp:2187 附近），拖放视图可经其统一入口暴露

---

## 0. 结论摘要

| 项 | 结论 |
|---|---|
| OLE 拖放最小面可行性 | **完全可行、低风险**：RegisterDragDrop/RevokeDragDrop（ole32 运行时解析）+ IDropTarget CCW 视图（4 方法，托管派发 E_NOTIMPL）均可无头验证 |
| 托管派发决策 | **E_NOTIMPL**（最小面）：DragEnter/DragOver/Drop 返回 E_NOTIMPL，DragLeave 返回 S_OK（无参数、无编组）。完整托管派发（DragEventArgs 构造 + typed IDataObject RCW + POINTL/DWORD 编组）为 stage 16+ 远期 |
| headless 可验证性 | ✅ 注册/撤销 + QI 视图 + 直接调视图方法（DragLeave 无参数可全验证；DragEnter/DragOver/Drop 带最小 stub IDataObject 验证 E_NOTIMPL 返回） |
| **关键发现** | stage-14 的 `icall_com_get_com_interface_for_object`（com.cpp:2193）**硬编码**只支持 IUnknown/IDispatch（com.cpp:2206-2213）——要让其暴露 drop target 视图需**扩展**该 icall（可选 stage 15b）；核心最小面用直接 QI 即可，不依赖此扩展 |
| **推荐 stage 15** | **拖放最小面**（注册/撤销 + IDropTarget 视图 + 托管派发 E_NOTIMPL） |
| 替代方案 | IProvideClassInfo（低价值）/ IOleInPlaceSite（需托管派发）/ 拖放完整面（高风险）——均不推荐，拖放最小面为最可行 |

---

## 1. 拖放最小面可行性分析

### 1.1 OLE 拖放协议面（最小面）

OLE 拖放由 ole32 的四个 API + 两个 COM 接口构成。最小面只取**可无头验证**的部分：

| 面 | 说明 | 最小面取舍 | 现有先例 |
|---|---|---|---|
| `OleInitialize` / `OleUninitialize` | 进程级 OLE 初始化 | **不新增**：`EnsureOle32()` 已调 `CoInitializeEx`（com.cpp:1770），OleInitialize 是独立入口，拖放注册在 CoInitializeEx 下即可工作 | `EnsureOle32()` |
| `RegisterDragDrop(HWND, IDropTarget*)` | 把窗口注册为拖放目标 | ✅ 新增 icall | 无 |
| `RevokeDragDrop(HWND)` | 撤销注册 | ✅ 新增 icall | 无 |
| `DoDragDrop(IDataObject*, IDropSource*, DWORD, DWORD*)` | 启动拖放循环（**需真实鼠标**） | ❌ **排除**：模态消息循环 + 鼠标捕获，不可无头 | 无 |
| `IDropTarget`（CCW 视图） | DragEnter/DragOver/DragLeave/Drop 4 方法 | ✅ 新增 CCW 视图（托管派发 E_NOTIMPL） | 无 |
| `IDropSource`（CCW 视图） | QueryContinueDrag/GiveFeedback 2 方法 | ❌ **排除**：仅 DoDragDrop 使用（源侧），无头不可达 | 无 |

**关键取舍**：`IDropSource` 视图与 `DoDragDrop` 一起排除——IDropSource 是拖放**源**侧接口，只在 DoDragDrop 的模态循环中被 OLE 调用；无 DoDragDrop 则 IDropSource 视图无任何无头消费方，实现它纯属死代码（YAGNI）。最小面只做**目标侧**（IDropTarget），这是 WinForms `Control.AllowDrop` 的注册路径。

### 1.2 托管派发决策（核心）

**决策：最小面托管派发 = E_NOTIMPL。**

stage 14 审计（com-stage14-dragdrop-spec.md §1.2）已确认托管派发的编组鸿沟：

- 托管 `IDropTarget` 方法签名（net48 反射确认）：
  ```
  Void OnDragEnter(System.Windows.Forms.DragEventArgs)
  Void OnDragLeave(System.EventArgs)
  Void OnDragDrop(System.Windows.Forms.DragEventArgs)
  Void OnDragOver(System.Windows.Forms.DragEventArgs)
  ```
- native `IDropTarget` 方法签名（oleidl.h）：
  ```
  DragEnter(IDataObject* pDataObj, DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
  DragOver(DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
  DragLeave()
  Drop(IDataObject* pDataObj, DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
  ```

编组鸿沟：native 收原始 OLE 参数，托管收 `DragEventArgs` 对象。完整派发需：
1. 把 native `IDataObject*` 包装为托管 `IDataObject` **typed RCW**（现有 `WrapUnknown` 只产通用 RCW，无接口类型分派）
2. 构造 `DragEventArgs` 托管对象（需 WinForms 类型已转译/注册 + 调用其构造函数）
3. 编组 POINTL（x,y）与 DWORD keyState/effect 进构造函数参数
4. 通过 `il2cpp_interface_dispatch` 派发到托管 `OnDragEnter` 等

现有 COM 阶段全部走 **intptr carrier + 简单 VARIANT**（VT_I4/VT_R8/VT_BSTR），**无构造带复杂签名托管对象的先例**。`il2cpp_method_invoke_i4`（api.cpp:128）是 C API 静态方法派发，非实例方法派发，不适用。

**最小面决策**：视图方法返回 E_NOTIMPL（DragLeave 返回 S_OK——无参数、无编组、可无头全验证）。这使：
- 注册/撤销路径**完整可用**（RegisterDragDrop 接受视图指针，OLE 可调 DragLeave）
- 视图形状（QI + vtable 布局 + refcount）**完整可验证**
- 托管派发基建（DragEventArgs + typed RCW）**推迟到 stage 16+**，不阻塞最小面

### 1.3 headless 可验证性

| 面 | 可无头验证 | 说明 |
|---|---|---|
| `RegisterDragDrop` / `RevokeDragDrop` | ✅ | 注册/撤销 + 返回 HRESULT（需有效 HWND + 已初始化 OLE） |
| CCW `IDropTarget` 视图 QI | ✅ | QI 返回视图指针 + AddRef/Release |
| 直接调 CCW `DragLeave` | ✅ | 无参数，返回 S_OK |
| 直接调 CCW `DragEnter`/`DragOver`/`Drop` | ✅ | 带最小 stub IDataObject，返回 E_NOTIMPL（托管派发未实现） |
| `DoDragDrop` | ❌ | 需真实鼠标（模态消息循环 + 鼠标捕获），不可无头——**排除** |

**HWND 获取**：测试需一个有效 HWND。test_com.cpp 是纯运行时测试（无 WinForms），可 `CreateWindowExW` 一个隐藏窗口（user32，测试进程已链接）或复用 `GetDesktopWindow()`。`RegisterDragDrop` 要求窗口属于调用线程且 OLE 已初始化——`EnsureOle32()` 的 `CoInitializeEx` 已满足。

---

## 2. 完整规格

### 2.1 新 icall 签名（il2cpp_com.h 声明）

```cpp
// IID_IDropTarget: {00000122-0000-0000-C000-000000000046} -- the OLE drop
// target interface (stage 15, the WinForms Control.AllowDrop dependency): a
// native host QIs the CCW for it and calls DragEnter/DragOver/DragLeave/Drop
// as the user drags over the registered window. Same .NET Guid byte layout as
// kComIidIUnknown.
constexpr uint8_t kComIidIDropTarget[16] = {
    0x22, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
};

// IDropTarget method slots (interface-relative after the IUnknown base:
// 0=QI, 1=AddRef, 2=Release -- canonical oleidl.h order).
constexpr int32_t kComDropSlotDragEnter = 3; // void* pDataObj, uint32_t grfKeyState, const void* pt, uint32_t* pdwEffect
constexpr int32_t kComDropSlotDragOver  = 4; // uint32_t grfKeyState, const void* pt, uint32_t* pdwEffect
constexpr int32_t kComDropSlotDragLeave = 5; // (void)
constexpr int32_t kComDropSlotDrop      = 6; // void* pDataObj, uint32_t grfKeyState, const void* pt, uint32_t* pdwEffect

// OLE drag-drop registration (ole32, WINDOWS only). RegisterDragDrop(hwnd,
// dropTarget) registers a window as a drop target; RevokeDragDrop(hwnd)
// revokes it. 'dropTarget' is the CCW's IDropTarget view (obtained via
// GetComInterfaceForObject / QI). hwnd == 0 -> ArgumentNullException.
// Windows: full implementation (RegisterDragDrop / RevokeDragDrop resolved at
// runtime like the rest of the ole32 API -- kept OUT of GetOle32().ok, the
// stage-11 strategy). Non-Windows: E_NOTIMPL (the POSIX stub).
int32_t icall_com_ole_register_drag_drop(intptr_t hwnd, intptr_t dropTarget);
int32_t icall_com_ole_revoke_drag_drop(intptr_t hwnd);
```

**设计要点**：
- `hwnd` 是 intptr carrier（M-stage IntPtr 模型，与 `icall_com_stream_create_on_hglobal` 的 hGlobal 一致）。
- `dropTarget` 是 intptr carrier（IDropTarget 视图指针）。
- `RegisterDragDrop`/`RevokeDragDrop` 从 ole32.dll 运行时解析（`LoadLibraryA`/`GetProcAddress`），**DELIBERATELY 不进 `GetOle32().ok`**（stage-11/12 策略：`GetOle32()` 的 "ole32.dll loaded" 门保持其语义；两个新入口单独检查，不可解析时 NotSupportedException——`EnsureOle32` 先例）。
- 不新增 `OleInitialize`/`OleUninitialize` icall：`EnsureOle32()` 的 `CoInitializeEx` 已初始化 OLE，`RegisterDragDrop` 在 CoInitializeEx 下即可工作（net48 的 `OleInitialize` 是独立入口，但拖放注册不强制它）。

### 2.2 com.cpp 设计（IDropTarget 视图）

**新增结构**（Windows 门内，`#if defined(_WIN32)`）：

```cpp
// IDropTarget vtable (slots 0-6: IUnknown + DragEnter/DragOver/DragLeave/Drop
// -- the OLE drop target surface, canonical oleidl.h order). Lives at
// &ccw->dropVtbl -- a DISTINCT address inside the CCW allocation (container_of
// recovery via offsetof, the phase-4 view precedent).
struct Il2CppComDropTargetVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* dragEnter)(void*, void*, uint32_t, const void*, uint32_t*); // slot 3
    int32_t  (IL2CPP_COM_CALL* dragOver)(void*, uint32_t, const void*, uint32_t*);         // slot 4
    int32_t  (IL2CPP_COM_CALL* dragLeave)(void*);                                           // slot 5
    int32_t  (IL2CPP_COM_CALL* drop)(void*, void*, uint32_t, const void*, uint32_t*);       // slot 6
};
```

**CCW 结构新增成员**（Windows 门内，`Il2CppComCcw` 结构，com.cpp:290）：

```cpp
    const Il2CppComDropTargetVtable* dropVtbl; // -> kDropVtable (IDropTarget view)
```

**QI 分派**（`CcwQueryInterface`，com.cpp:484，Windows 门内新增分支）：

```cpp
    if (std::memcmp(iid, kComIidIDropTarget, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->dropVtbl); // the IDropTarget view
        ++ccw->refCount;
        return kComHrOk;
    }
```

**视图方法实现**（托管派发 E_NOTIMPL）：

```cpp
// Recover the owning CCW from the drop-target view pointer.
Il2CppComCcw* CcwFromDropView(void* self)
{
    return reinterpret_cast<Il2CppComCcw*>(
        reinterpret_cast<char*>(self) - offsetof(Il2CppComCcw, dropVtbl));
}

// IDropTarget view QI: IUnknown/IDispatch -> the identity pointer (the base
// address -- COM requires QI(IUnknown) to return the same address from every
// view), IDropTarget -> this view, the other OLE views -> their views (the
// phase-4 identity-QI mirror).
int32_t IL2CPP_COM_CALL CcwDropQueryInterface(void* self, const void* riid, void** ppv)
{
    auto* ccw = CcwFromDropView(self);
    if (std::memcmp(riid, kComIidIUnknown, 16) == 0 ||
        std::memcmp(riid, kComIidIDispatch, 16) == 0)
    {
        *ppv = ccw; // the identity pointer (same address, AddRef'd)
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(riid, kComIidIDropTarget, 16) == 0)
    {
        *ppv = self; // this view
        ++ccw->refCount;
        return kComHrOk;
    }
    // The other OLE views (IOleObject / IOleClientSite / IConnectionPointContainer)
    // -- the phase-4 identity-QI mirror (each view QIs to the others).
    if (std::memcmp(riid, kComIidIOleObject, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->oleVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(riid, kComIidIOleClientSite, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->siteVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    if (std::memcmp(riid, kComIidIConnectionPointContainer, 16) == 0)
    {
        *ppv = reinterpret_cast<void*>(&ccw->cpcVtbl);
        ++ccw->refCount;
        return kComHrOk;
    }
    *ppv = nullptr;
    return kComHrNoInterface;
}

uint32_t IL2CPP_COM_CALL CcwDropAddRef(void* self)
{
    return CcwAddRef(CcwFromDropView(self));
}

uint32_t IL2CPP_COM_CALL CcwDropRelease(void* self)
{
    return CcwReleaseImpl(CcwFromDropView(self));
}

// IDropTarget::DragEnter / DragOver / Drop -- the managed dispatch (OnDragEnter
// / OnDragOver / OnDragDrop with a DragEventArgs) is the stage-16+ path; the
// minimal stage-15 surface returns E_NOTIMPL (the documented no-managed-dispatch
// answer -- the view shape is real, the method bodies are not yet wired).
int32_t IL2CPP_COM_CALL CcwDropNotImpl5(void*, void*, uint32_t, const void*, uint32_t*)
{ return kComHrNotImpl; }
int32_t IL2CPP_COM_CALL CcwDropNotImpl4(void*, uint32_t, const void*, uint32_t*)
{ return kComHrNotImpl; }

// IDropTarget::DragLeave -- no parameters, no marshaling: S_OK (the minimal
// surface's one fully-functional method -- a native host's drag-leave
// notification is acknowledged).
int32_t IL2CPP_COM_CALL CcwDropDragLeave(void* self)
{
    (void)self;
    return kComHrOk;
}
```

**静态 vtable 组装**：

```cpp
// The IDropTarget view vtable (7 slots, canonical oleidl.h order). The minimal
// stage-15 surface implements DragLeave (S_OK -- no params, no marshaling);
// DragEnter/DragOver/Drop return E_NOTIMPL (the managed dispatch is stage 16+).
const Il2CppComDropTargetVtable kDropVtable = {
    &CcwDropQueryInterface,
    &CcwDropAddRef,
    &CcwDropRelease,
    &CcwDropNotImpl5,   // slot 3 DragEnter
    &CcwDropNotImpl4,   // slot 4 DragOver
    &CcwDropDragLeave,  // slot 5 DragLeave
    &CcwDropNotImpl5,   // slot 6 Drop
};
```

**CCW 构造初始化**：`icall_com_ccw_create`（com.cpp:2402）需把 `dropVtbl` 初始化为 `&kDropVtable`（Windows 门内，与 `oleVtbl`/`siteVtbl`/`cpcVtbl` 一致——在 com.cpp:2413 的 `ccw->cpcVtbl = &kCpcVtable;` 之后新增 `ccw->dropVtbl = &kDropVtable;`）。

**Ole32Api 扩展**（com.cpp:345，Windows 门内）：

```cpp
typedef int32_t (WINAPI* RegisterDragDropFn)(void* hwnd, void* pDropTarget);
typedef int32_t (WINAPI* RevokeDragDropFn)(void* hwnd);

struct Ole32Api
{
    // ... existing members ...
    RegisterDragDropFn registerDragDrop;
    RevokeDragDropFn   revokeDragDrop;
};
```

`LoadOle32`（com.cpp:1697）新增两个 `GetProcAddress` 解析，**DELIBERATELY 不进 `api.ok`**（stage-11/12 策略：`GetOle32()` 的 "ole32.dll loaded" 门保持其语义；两个新入口单独检查，不可解析时 NotSupportedException——`EnsureOle32` 先例）。

**icall 实现**：

```cpp
// icall_com_ole_register_drag_drop: RegisterDragDrop(hwnd, dropTarget).
// hwnd == 0 -> ArgumentNullException; dropTarget == 0 -> ArgumentNullException;
// RegisterDragDrop unresolvable -> NotSupportedException (the EnsureOle32
// precedent); a failing RegisterDragDrop -> the COMException-shaped throw
// carrying the HRESULT.
int32_t icall_com_ole_register_drag_drop(intptr_t hwnd, intptr_t dropTarget)
{
    if (hwnd == 0 || dropTarget == 0)
    {
        il2cpp_throw_argument_null_exception("hwnd/dropTarget");
    }
    // EnsureOle32() both loads ole32 and initializes COM (CoInitializeEx) --
    // RegisterDragDrop requires an initialized OLE (else OLE_E_NOTINITIALIZED).
    EnsureOle32();
    if (GetOle32().registerDragDrop == nullptr)
    {
        il2cpp_throw_not_supported_exception("RegisterDragDrop unavailable");
    }
    const int32_t hr = GetOle32().registerDragDrop(
        reinterpret_cast<void*>(hwnd), reinterpret_cast<void*>(dropTarget));
    if (hr != kComHrOk)
    {
        // the COMException-shaped throw carrying the HRESULT (the
        // CreateInstanceByClsid precedent, com.cpp:1802 -- ThrowComException)
        ThrowComException(hr);
    }
    return kComHrOk;
}

// icall_com_ole_revoke_drag_drop: RevokeDragDrop(hwnd). hwnd == 0 ->
// ArgumentNullException; unresolvable -> NotSupportedException; failing ->
// COMException-shaped throw.
int32_t icall_com_ole_revoke_drag_drop(intptr_t hwnd)
{
    if (hwnd == 0)
    {
        il2cpp_throw_argument_null_exception("hwnd");
    }
    EnsureOle32();
    if (GetOle32().revokeDragDrop == nullptr)
    {
        il2cpp_throw_not_supported_exception("RevokeDragDrop unavailable");
    }
    const int32_t hr = GetOle32().revokeDragDrop(reinterpret_cast<void*>(hwnd));
    if (hr != kComHrOk)
    {
        ThrowComException(hr);
    }
    return kComHrOk;
}
```

**POSIX stub**（`#else` 分支）：两个 icall 返回 `kComHrNotImpl`（HRESULT 形式，netcore Marshal.Unix.cs 契约——平台限制，非静默）。

### 2.3 测试断言清单（test_com.cpp section 17）

**Windows 块**（`#if defined(_WIN32)`，section 16 之后）：

前置：测试需一个有效 HWND。用 `CreateWindowExW` 创建一个隐藏窗口（user32，测试进程已链接）或 `GetDesktopWindow()`。`icall_com_ole_register_drag_drop` 内部调用 `EnsureOle32()`（CoInitializeEx 初始化 OLE），无需测试预触发。

1. **QI 视图**：`icall_com_ccw_create` → CCW；`icall_com_query_interface(ccw, kComIidIDropTarget, &view)` → S_OK + `view != ccw`（独立视图地址）+ AddRef。
2. **视图 refcount**：`icall_com_release(view)` → 1（identity 仍持有）；`icall_com_release(ccw)` → 0（销毁）。
3. **DragLeave（无参数，S_OK）**：`icall_com_call_vtable_slot(view, kComDropSlotDragLeave, 0,0,0,0)` → S_OK。
4. **DragEnter/DragOver/Drop（E_NOTIMPL）**：带最小 stub IDataObject（一个 3-slot IUnknown 对象，`TestSink` 模式）：
   - `icall_com_call_vtable_slot(view, kComDropSlotDragEnter, (intptr_t)stub, 0, 0, 0)` → E_NOTIMPL
   - `icall_com_call_vtable_slot(view, kComDropSlotDragOver, 0, 0, 0, 0)` → E_NOTIMPL
   - `icall_com_call_vtable_slot(view, kComDropSlotDrop, (intptr_t)stub, 0, 0, 0)` → E_NOTIMPL
5. **视图 QI 身份**：`icall_com_query_interface(view, kComIidIUnknown, &id)` → S_OK + `id == ccw`（身份指针）；`icall_com_query_interface(view, kComIidIDropTarget, &self)` → S_OK + `self == view`。
6. **GetComInterfaceForObject 统一入口（可选，需扩展 stage-14 icall）**：`icall_com_get_com_interface_for_object(MakeManagedTarget(), kComIidIDropTarget)` → 非零。**注意**：stage-14 的 `icall_com_get_com_interface_for_object`（com.cpp:2193）**硬编码**只支持 IUnknown/IDispatch（com.cpp:2206-2213 对任何其它 IID 抛 InvalidCastException）。要让 GetComInterfaceForObject 暴露 drop target 视图，需**扩展该 icall** 允许已实现视图的 IID（stage-14 审计 §2.4 的推荐）。**核心最小面不依赖此扩展**——测试用直接 QI（断言 1）即可验证视图；此断言标记为"可选（需扩展 stage-14 icall）"。
7. **RegisterDragDrop/RevokeDragDrop**：
   - `icall_com_ole_register_drag_drop(0, view)` → ArgumentNullException
   - `icall_com_ole_revoke_drag_drop(0)` → ArgumentNullException
   - `icall_com_ole_register_drag_drop(hwnd, view)` → S_OK（有效 HWND + 已初始化 OLE）
   - `icall_com_ole_revoke_drag_drop(hwnd)` → S_OK
   - 撤销后再次注册 → S_OK（幂等）
8. **视图 Release 到 0** → 销毁 CCW。

**POSIX 块**（`#else`，4j）：
9. `icall_com_ole_register_drag_drop(0x1, 0x1)` → E_NOTIMPL
10. `icall_com_ole_revoke_drag_drop(0x1)` → E_NOTIMPL
11. QI `kComIidIDropTarget` → E_NOINTERFACE（POSIX stub，视图不提供）

**断言计数**：约 15-20 条新增（Windows）+ 3 条 POSIX。

### 2.4 MethodMap 接线（可选）

拖放最小面**不需要** MethodMap 接线——`RegisterDragDrop`/`RevokeDragDrop` 是 WinForms `UnsafeNativeMethods` 的 P/Invoke（`[DllImport("ole32.dll")]`），走 P/Invoke 路径而非 COM icall。`IDropTarget` 视图经 CCW 的 QI 暴露（测试直接 QI）。

**可选扩展**：若要让托管侧经 `GetComInterfaceForObject(o, typeof(IDropTarget))` 获取视图，需扩展 stage-14 的 `icall_com_get_com_interface_for_object`（com.cpp:2193）——把 com.cpp:2206-2213 的硬编码 IUnknown/IDispatch 检查改为"允许已实现视图的 IID"（IUnknown/IDispatch/IOleObject/IOleClientSite/IConnectionPointContainer/IEnumVARIANT/IDropTarget）。这涉及 MethodMap.Marshal.cs 的 Type->IID 解析（`typeof(IDropTarget)` 需转译期解析为 `kComIidIDropTarget`，StackFold marker 扩展）。**建议作为 stage 15b 独立交付**，不阻塞核心最小面。

---

## 3. 替代方案评估

| 方案 | 可行性 | 价值 | 推荐 |
|---|---|---|---|
| **拖放最小面**（注册/撤销 + IDropTarget 视图 + 托管派发 E_NOTIMPL） | ✅ 高 | 中高（WinForms AllowDrop 注册路径 + 视图形状可验证） | **推荐 stage 15** |
| IProvideClassInfo | ✅ 高 | 低（仅返回 ITypeInfo，无实际消费方；且需 ITypeInfo 基建） | 不推荐 |
| IOleInPlaceSite | ⚠️ 中 | 中（ActiveX 就地激活，但需托管派发——同拖放鸿沟） | 不推荐（stage 16+ 远期） |
| 拖放完整面（含托管派发） | ⚠️ 中 | 高（WinForms 拖放依赖） | stage 16+ 远期（需托管派发基建） |
| IDropSource 视图 + DoDragDrop | ❌ 低 | 低（源侧，需真实鼠标，不可无头） | 排除 |

**为什么拖放最小面最可行**：
- 完全复用既有模式：CCW 视图（phase-4 多视图先例）、ole32 运行时解析（stage-11/12 先例）、E_NOTIMPL 方法（phase-4 先例）、intptr carrier（M-stage 模型）。
- 无新编组：不构造 DragEventArgs、不包装 typed IDataObject RCW、不调 `il2cpp_interface_dispatch`。
- 无头可验证：注册/撤销 + QI + 直接调视图方法全部可断言。
- 为 stage 16+ 完整面提供视图骨架（vtable 布局、QI 分派、refcount 已就位，只需替换方法体为托管派发）。

---

## 4. 工作量 + 风险

| 项 | 工作量 | 风险 |
|---|---|---|
| `kComIidIDropTarget` + 槽常量（il2cpp_com.h） | 低（~10 行） | 低 |
| `Il2CppComDropTargetVtable` + CCW 成员 + QI 分派 + 视图方法 + vtable 组装（com.cpp） | 中（~80 行） | 低（phase-4 视图先例） |
| `Ole32Api` 扩展 + `LoadOle32` 解析（RegisterDragDrop/RevokeDragDrop） | 低（~15 行） | 低（stage-11/12 先例） |
| `icall_com_ole_register_drag_drop` / `icall_com_ole_revoke_drag_drop` + POSIX stub | 低（~40 行） | 低 |
| test_com.cpp section 17 + POSIX 4j | 中（~60 行，15-20 断言） | 中（HWND 获取 + OLE 初始化环境） |
| **可选 stage 15b**：扩展 `icall_com_get_com_interface_for_object` 允许已实现视图 IID | 中（改 com.cpp:2206 检查 + MethodMap/StackFold Type->IID） | 中（改 stage-14 契约，需回归） |
| **合计（核心最小面）** | **中（~200 行）** | **低** |

**主要风险**：
1. **HWND 获取**（中）：`RegisterDragDrop` 要求有效 HWND + 已初始化 OLE。测试用 `CreateWindowExW` 隐藏窗口或 `GetDesktopWindow()`。若 `CoInitializeEx` 未满足 OLE 要求，`RegisterDragDrop` 返回 `OLE_E_NOTINITIALIZED`（0x800401F0）——需确认 `EnsureOle32()` 的 `CoInitializeEx` 调用在测试路径已执行（`icall_com_create_instance_clsid` 等已触发）。
2. **RegisterDragDrop 的窗口线程要求**（低）：窗口须属于调用线程。测试在同一线程创建窗口 + 注册，满足要求。
3. **E_NOTIMPL 的语义**（低）：DragEnter/DragOver/Drop 返回 E_NOTIMPL 是"托管派发未实现"的诚实答案（与 phase-4 的 E_NOTIMPL 方法一致），非静默错误。

**验证**：
- `cmake --build E:\il2cpp\build\runtime_full -j 16` → 0 errors（C0）
- `test_com.exe` → all PASS（断言计数增加）
- `all test_*.exe` → 139/139

---

## 5. 最终推荐

**stage 15 = 拖放最小面**（注册/撤销 + IDropTarget 视图 + 托管派发 E_NOTIMPL）：
1. il2cpp_com.h：`kComIidIDropTarget` + `kComDropSlot*` + `icall_com_ole_register_drag_drop` / `icall_com_ole_revoke_drag_drop` 声明。
2. com.cpp：`Il2CppComDropTargetVtable` + CCW `dropVtbl` 成员 + QI 分派 + 视图方法（DragLeave S_OK，其余 E_NOTIMPL）+ vtable 组装 + `Ole32Api` 扩展 + 两个 icall + POSIX stub。
3. test_com.cpp section 17（Windows）+ 4j（POSIX）。
4. 无 MethodMap 改动（P/Invoke 路径）。
5. 为 stage 16+ 完整面（托管派发）提供视图骨架。

**可选 stage 15b**：扩展 `icall_com_get_com_interface_for_object` 允许已实现视图的 IID（含 IDropTarget），使托管侧经 `GetComInterfaceForObject(o, typeof(IDropTarget))` 获取视图——stage-14 审计 §2.4 的推荐，独立交付。

**拖放完整面**（含托管派发）降级为 stage 16+ 远期，需先建托管派发基建（DragEventArgs 构造 + typed IDataObject RCW + POINTL/DWORD 编组 + `il2cpp_interface_dispatch` 实例方法派发）。
