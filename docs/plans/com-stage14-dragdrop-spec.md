# COM Stage 14 规格 — OLE 拖放最小面（含 GetComInterfaceForObject 前置接线）

- 日期：2026-08-16
- 性质：审计子代理产出（纯研究，无代码改动）
- 上游：stage 13 审计结论 "a（拖放）推荐为 stage 14（需先补 GetComInterfaceForObject 接线——现为 LOUD——与托管侧派发/记录机制）"
- 基线：HEAD 81d225e（stage 13 Marshal BSTR 桥，test_com 390 断言，runtime 139/139）

---

## 0. 结论摘要

| 项 | 结论 |
|---|---|
| OLE 拖放最小面可行性 | **部分可行**：RegisterDragDrop/RevokeDragDrop + IDropTarget CCW 视图（4 方法）本身可行；但**托管侧派发机制复杂度过高**（需构造 `DragEventArgs` 托管对象 + 包装 `IDataObject*` 为托管 RCW + 编组 POINTL/DWORD），远超现有 COM 阶段能力 |
| 前置依赖 GetComInterfaceForObject | **完全可行、低风险**：net48 `(object, Type) -> IntPtr`，按 GetIUnknownForObject 模式接线 |
| **推荐 stage 14** | **GetComInterfaceForObject 单独接线**（前置依赖，独立交付），拖放降级为 stage 15+ 远期 |
| 替代方案 | GetComInterfaceForObject 单独接线（推荐）/ IProvideClassInfo（低价值）/ 拖放完整面（高风险，需托管派发基建） |

---

## 1. 拖放可行性分析

### 1.1 OLE 拖放协议面（最小面）

OLE 拖放由 ole32 的四个 API + 两个 COM 接口构成：

| 面 | 说明 | 现有先例 |
|---|---|---|
| `OleInitialize` / `OleUninitialize` | 进程级 OLE 初始化 | `EnsureOle32()` 已调 `CoInitializeEx`（com.cpp:1770），OleInitialize 是独立入口 |
| `RegisterDragDrop(HWND, IDropTarget*)` | 把窗口注册为拖放目标 | 无（需新 icall） |
| `RevokeDragDrop(HWND)` | 撤销注册 | 无（需新 icall） |
| `DoDragDrop(IDataObject*, IDropSource*, DWORD, DWORD*)` | 启动拖放循环（**需真实鼠标**） | 无（不可无头） |
| `IDropTarget`（CCW 视图） | DragEnter/DragOver/DragLeave/Drop 4 方法 | 无（需新 CCW 视图） |
| `IDropSource`（CCW 视图） | QueryContinueDrag/GiveFeedback 2 方法 | 无（需新 CCW 视图） |

### 1.2 托管侧派发机制（核心风险）

**关键发现**：审计原以为 com.cpp 无托管派发先例，实际存在：

- `il2cpp_interface_dispatch(obj, iface, ifaceSlot)`（metadata.cpp:359，il2cpp_runtime.h:101）：通过 `obj->klass->interfaces` 线性搜索 iface 条目 + `entry.slotMap[ifaceSlot]` 解析到 vtable 槽，返回函数指针。这是 CCW 视图派发到托管方法的机制基础。
- `icall_gchandle_get_target(handle)`（il2cpp_gchandle.h:83）：从 CCW 的 GCHandle 恢复托管对象。

**但存在不可逾越的编组障碍**：

托管 `IDropTarget` 接口方法签名（net48 反射确认）：
```
Void OnDragEnter(System.Windows.Forms.DragEventArgs)
Void OnDragLeave(System.EventArgs)
Void OnDragDrop(System.Windows.Forms.DragEventArgs)
Void OnDragOver(System.Windows.Forms.DragEventArgs)
```
`DragEventArgs` 构造函数：
```
.ctor(System.Windows.Forms.IDataObject, Int32, Int32, Int32,
      System.Windows.Forms.DragDropEffects, System.Windows.Forms.DragDropEffects)
```

native `IDropTarget` 方法签名（oleidl.h）：
```
DragEnter(IDataObject* pDataObj, DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
DragOver(DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
DragLeave()
Drop(IDataObject* pDataObj, DWORD grfKeyState, POINTL pt, DWORD* pdwEffect)
```

**编组鸿沟**：native 方法收原始 OLE 参数，托管方法收 `DragEventArgs` 对象。CCW 视图需：
1. 把 native `IDataObject*` 包装为托管 `IDataObject` RCW（**typed RCW**，非现有通用 RCW —— 现有 `WrapUnknown` 只产通用 RCW，无接口类型分派）
2. 构造 `DragEventArgs` 托管对象（需 WinForms 类型已转译/注册 + 调用其构造函数）
3. 编组 POINTL（x,y）与 DWORD keyState/effect 进构造函数参数
4. 通过 `il2cpp_interface_dispatch` 派发到托管 `OnDragEnter` 等

现有 COM 阶段全部走 **intptr carrier + 简单 VARIANT**（VT_I4/VT_R8/VT_BSTR），**无构造带复杂签名托管对象的先例**。`il2cpp_method_invoke_i4`（api.cpp:128）是 C API 静态方法派发（`static int32(int32,int32,int32,int32)`，经 export 表），**非实例方法派发**，不适用。

**结论**：托管派发机制复杂度过高，构成拖放完整面的主要风险。最小面（注册/撤销 + 直接调 CCW 视图方法）可绕过 DoDragDrop 的鼠标依赖，但 DragEnter/Drop 的 `IDataObject*` 编组仍无法回避。

### 1.3 headless 可验证性

| 面 | 可无头验证 |
|---|---|
| `RegisterDragDrop` / `RevokeDragDrop` | ✅ 注册/撤销 + 返回 HRESULT（需有效 HWND + 已初始化 OLE） |
| CCW `IDropTarget` 视图 QI | ✅ QI 返回视图指针 + AddRef/Release |
| 直接调 CCW `DragEnter`/`DragOver`/`DragLeave`/`Drop` | ⚠️ 可调，但 `IDataObject*` 参数需一个真实 IDataObject（可构造最小 stub） |
| `DoDragDrop` | ❌ 需真实鼠标（模态消息循环 + 鼠标捕获），不可无头 |

---

## 2. 推荐 stage 14：GetComInterfaceForObject 单独接线

### 2.1 为什么推荐这个

- **前置依赖**：拖放（以及所有按接口 QI 的场景）都需要 `GetComInterfaceForObject` 先接线。
- **低风险**：完全复用 `GetIUnknownForObject` / `GetIDispatchForObject` 的既有模式（com.cpp:2082 / 2125），无新编组。
- **独立交付**：不依赖托管派发基建，可立即完成并验证。
- **net48 语义清晰**：`(object, Type) -> IntPtr`，反射确认。

### 2.2 net48 语义

```
public static IntPtr GetComInterfaceForObject(object o, Type T)
```
- 返回对象按接口类型 T 暴露的 native 接口指针（AddRef'd，调用者负责 Release）。
- **非 RCW 托管对象**：包装为新 CCW，QI 指定接口（net48 coreclr `GetComIPFromObjectRef`）。
- **RCW**：QI 其包装的 native IUnknown 为指定接口。
- 对象不实现接口 T → `InvalidCastException`（net48）。
- `o == null` → `ArgumentNullException`；`T == null` → `ArgumentNullException`。
- 非 Windows：非 RCW 分支（CCW 方向）功能正常；RCW 分支不可达（RCW 仅 Windows 存在）。

### 2.3 新 icall 签名（il2cpp_com.h 声明）

```cpp
// Marshal.GetComInterfaceForObject(object o, Type T): exposes the object's
// native interface pointer for the requested interface type T (AddRef'd --
// the caller must Release). net48 semantics (coreclr GetComIPFromObjectRef
// with the requested IID): a NON-RCW managed object is wrapped behind a fresh
// CCW and QI'd for the interface; an RCW QIs its wrapped native IUnknown.
// The requested interface is identified by the managed Type's GUID (the
// GuidAttribute on the interface type -- the net48 contract). The runtime
// resolves the Type -> IID through the type registry (the interface's
// GuidAttribute value, 16 bytes .NET Guid layout). Null o or T ->
// ArgumentNullException; the object does not implement T ->
// InvalidCastException-shaped throw. Non-Windows: the RCW branch is
// unreachable (RCWs only exist there) and the CCW branch is functional.
intptr_t icall_com_get_com_interface_for_object(Il2CppObject* o, Il2CppObject* type);
```

**设计要点**：
- `type` 参数是托管 `System.Type` 对象（`Il2CppObject*`）。运行时需从 Type 解析其 GUID（接口的 `GuidAttribute`）。
- **GUID 解析依赖**：运行时需能从 `System.Type` 读取接口 GUID。现有 `il2cpp_class_by_name` 只按名字解析类型描述符，**无 Type -> GUID 的反射路径**。这是本 icall 的主要实现风险。

**GUID 解析的替代设计**（规避反射依赖）：
- 方案 A（推荐）：icall 接收 `Il2CppObject* type`，运行时通过 `type->klass` 的接口表 + 一个**接口 GUID 注册表**（类似 `icall_com_ccw_register_connection_point` 的 IID 注册）解析。但接口 GUID 需从托管 `GuidAttribute` 提取，仍依赖反射。
- 方案 B（务实）：**新增一个注册 icall** `icall_com_ccw_register_interface(ccw, iid, ifaceSlotBase)`，托管侧在创建 CCW 时声明对象实现的接口 IID 与 vtable 槽映射。`GetComInterfaceForObject` 的 Type 参数在托管侧（MethodMap）先解析为 IID（通过 `Marshal.GenerateGuidForType` 或 `GuidAttribute`），再传 IID 给 icall。**这避免运行时反射依赖**，与 `icall_com_ccw_register_connection_point` 的 IID 注册先例一致。

**推荐方案 B**（与现有 IID 注册先例一致，规避反射）：
```cpp
// Register 'iid' as an interface the CCW 'ccw' exposes (the phase-5
// connection-point registry precedent). GetComInterfaceForObject then QIs the
// CCW for 'iid' and returns the interface pointer. The managed wiring layer
// resolves the Type -> IID (via GuidAttribute) and registers it.
int32_t icall_com_ccw_register_interface(intptr_t ccw, const uint8_t iid[16]);
```

但注意：**仅注册 IID 不足以派发**。CCW 的 QI 需为注册的 IID 返回一个**接口视图**（vtable），而该视图的方法需派发到托管对象。若只做"QI 返回视图 + 方法 E_NOTIMPL"，则 `GetComInterfaceForObject` 返回的接口指针**不可用**（方法全 E_NOTIMPL），价值有限。

**因此 GetComInterfaceForObject 的完整价值需要接口视图 + 托管派发**，而这又回到拖放的托管派发问题。

### 2.4 务实的最小交付（推荐）

为规避"接口视图需托管派发"的循环依赖，stage 14 的最小交付应聚焦**已实现接口视图的 QI 接线**：

- `GetComInterfaceForObject(o, T)` 对 **IDispatch / IUnknown / 已注册的 CCW 视图接口**（IOleObject / IOleClientSite / IConnectionPointContainer / IEnumVARIANT / IDropTarget）返回对应视图指针。
- 对**未实现视图的任意接口**：返回 E_NOINTERFACE → 托管侧抛 `InvalidCastException`（net48 语义）。
- 这样 `GetComInterfaceForObject` 成为**已实现视图的统一入口**，为后续新增视图（含拖放）提供接线基础，且不引入新托管派发。

**icall 签名（最终推荐）**：
```cpp
// Marshal.GetComInterfaceForObject(object o, Type T): expose the object's
// native interface pointer for the requested interface type T (AddRef'd).
// The Type -> IID resolution happens on the MANAGED side (the GuidAttribute
// on the interface type, resolved via the existing Marshal GUID surface);
// the icall receives the resolved IID (16 bytes, .NET Guid layout) plus the
// object. A NON-RCW managed object is wrapped behind a fresh CCW and QI'd
// for the IID; an RCW QIs its wrapped native IUnknown. The CCW answers the
// IID only for the interfaces it implements a view for (IUnknown / IDispatch
// / the registered OLE / connection-point / enum / drop-target views);
// anything else -> E_NOINTERFACE -> the managed side throws
// InvalidCastException (net48). Null o -> ArgumentNullException.
intptr_t icall_com_get_com_interface_for_object(Il2CppObject* o, const uint8_t iid[16]);
```

**MethodMap.Marshal.cs 接线**：
```csharp
// IntPtr GetComInterfaceForObject(object o, Type T): the Type -> IID
// resolution happens here (the GuidAttribute on T, via the existing
// Marshal.GenerateGuidForType / GuidAttribute surface); the icall receives
// the resolved IID. ps[0] = object (0x12/0x1C), ps[1] = Type (0x12/0x1C).
case "GetComInterfaceForObject":
    if (ps.Length != 2 || ps[0] is not (0x12 or 0x1C) || ps[1] is not (0x12 or 0x1C)) return null;
    return I("il2cpp::runtime::icall_com_get_com_interface_for_object", 2, true, false, false,
        new[] { "", "" }, "ptr");
```
（注：Type -> IID 的托管侧解析需验证 `Marshal.GenerateGuidForType` 是否已接线；若未接线，需在 MethodMap 补一个 GUID 解析 icall，或复用 `icall_com_get_type_from_progid` 的 CLSID 模式。）

---

## 3. 完整拖放规格（stage 15+ 远期，供参考）

若后续决定推进拖放完整面，规格如下：

### 3.1 新 icall 签名（il2cpp_com.h）

```cpp
// OLE drag-drop registration (ole32, WINDOWS only). RegisterDragDrop(hwnd,
// dropTarget) registers a window as a drop target; RevokeDragDrop(hwnd)
// revokes it. The dropTarget is the CCW's IDropTarget view (obtained via
// GetComInterfaceForObject). hwnd == 0 -> ArgumentNullException. Windows:
// full implementation (OleInitialize + RegisterDragDrop / RevokeDragDrop,
// resolved at runtime like the rest of the ole32 API). Non-Windows:
// E_NOTIMPL (the POSIX stub).
int32_t icall_com_ole_register_drag_drop(intptr_t hwnd, intptr_t dropTarget);
int32_t icall_com_ole_revoke_drag_drop(intptr_t hwnd);
```

### 3.2 com.cpp 设计（IDropTarget 视图）

- 新增 `kComIidIDropTarget`（{00000122-0000-0000-C000-000000000046}）。
- CCW 结构新增 `const Il2CppComDropTargetVtable* dropVtbl`（Windows 门内）。
- 新增 `Il2CppComDropTargetVtable`（7 槽：IUnknown + DragEnter/DragOver/DragLeave/Drop）。
- QI 分派 `kComIidIDropTarget` → `&ccw->dropVtbl`（Windows 门内）。
- 视图方法签名（oleidl.h）：
  - `DragEnter(void* self, void* pDataObj, uint32_t grfKeyState, const void* pt, uint32_t* pdwEffect)`
  - `DragOver(void* self, uint32_t grfKeyState, const void* pt, uint32_t* pdwEffect)`
  - `DragLeave(void* self)`
  - `Drop(void* self, void* pDataObj, uint32_t grfKeyState, const void* pt, uint32_t* pdwEffect)`
- **托管派发**：通过 `icall_gchandle_get_target(ccw->gcHandle)` 恢复托管对象 + `il2cpp_interface_dispatch(obj, iface, ifaceSlot)` 派发。但需先解决 DragEventArgs 构造 + IDataObject RCW 编组（见 §1.2 风险）。

### 3.3 测试断言清单（test_com.cpp section 16）

Windows 块：
1. `icall_com_ole_register_drag_drop(0, x)` → ArgumentNullException
2. `icall_com_ole_revoke_drag_drop(0)` → ArgumentNullException
3. CCW QI `kComIidIDropTarget` → S_OK + 视图指针 + AddRef
4. 直接调视图 `DragLeave` → S_OK（无参数，可无头）
5. `DragOver`/`DragEnter`/`Drop` 带最小 stub IDataObject → 返回 S_OK（或 E_NOTIMPL，取决于托管派发实现）
6. `RevokeDragDrop` 撤销后再次注册 → S_OK
7. 视图 Release 到 0 → 销毁 CCW

POSIX 块（4i）：
8. `icall_com_ole_register_drag_drop` → E_NOTIMPL
9. `icall_com_ole_revoke_drag_drop` → E_NOTIMPL

### 3.4 工作量 + 风险

| 项 | 工作量 | 风险 |
|---|---|---|
| GetComInterfaceForObject 接线（推荐 stage 14） | 中（1 icall + MethodMap + 测试） | 低（复用既有模式） |
| Type -> IID 托管侧解析 | 中 | 中（依赖 GuidAttribute / GenerateGuidForType 接线） |
| IDropTarget CCW 视图 | 中（1 视图 + 4 方法） | 中（视图本身简单） |
| 托管派发（DragEventArgs + IDataObject RCW） | 高 | **高**（无先例，需新编组基建） |
| RegisterDragDrop/RevokeDragDrop | 低（2 icall + ole32 解析） | 低 |
| DoDragDrop | 高 | 高（需真实鼠标，不可无头） |

---

## 4. 替代方案评估

| 方案 | 可行性 | 价值 | 推荐 |
|---|---|---|---|
| **GetComInterfaceForObject 单独接线** | ✅ 高 | 高（前置依赖，统一已实现视图入口） | **推荐 stage 14** |
| IProvideClassInfo | ✅ 高 | 低（仅返回 ITypeInfo，无实际消费方） | 不推荐 |
| 拖放完整面（含托管派发） | ⚠️ 中 | 高（WinForms 拖放依赖） | stage 15+ 远期 |
| 拖放最小面（注册/撤销 + 视图，托管派发 E_NOTIMPL） | ✅ 高 | 中（可验证注册/撤销 + 视图形状，但方法不可用） | 可作 stage 14b 过渡 |

---

## 5. 最终推荐

**stage 14 = GetComInterfaceForObject 单独接线**：
1. 新增 `icall_com_get_com_interface_for_object(o, iid)`（IID 由托管侧解析）。
2. MethodMap.Marshal.cs 接线 `GetComInterfaceForObject`（从 kComMarshalMembers LOUD 集移除）。
3. 托管侧 Type -> IID 解析（GuidAttribute / GenerateGuidForType）。
4. test_com.cpp section 16：非 RCW -> CCW + QI 已实现视图；RCW -> QI IUnknown；未实现接口 -> InvalidCastException；null -> ArgumentNullException；POSIX stub。
5. 为后续拖放（stage 15+）提供接线基础。

**拖放完整面**（含托管派发）降级为 stage 15+ 远期，需先建托管派发基建（DragEventArgs 构造 + typed IDataObject RCW + POINTL/DWORD 编组）。
