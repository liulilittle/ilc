# COM Stage 17 批次 4 规格 — CCW→dispatch 桥（native IDropTarget DragEnter → 托管 OnDragEnter）

- 日期：2026-08-17
- 性质：审计子代理产出（纯研究，无代码改动）
- 上游：stage 16 审计结论 "完整托管派发 3 前置条件：① WinForms 类型转译注册 ② typed-RCW 基建 ③ 托管 ctor 调用机制 —— 分 3 批"；批次 3 收口（bbc6834）后验证 ① IDropTarget/DragEventArgs 描述符已注册 ② typed-RCW 方向已建（TypedDataObjectRcwClass + icall_com_wrap_data_object）③ il2cpp_object_construct 已可用 → 桥 = 组装既有原语，零新机制
- 基线：HEAD bbc6834（批次 3，test_com 524 断言，runtime 139/139，unit 1807/1807）

---

## 0. 结论摘要

| 项 | 结论 |
|---|---|
| 桥可行性 | **完全可行、低风险**：批次 3 已把 construct（il2cpp_object_construct）与 typed-RCW（wrap_data_object）带进运行时，桥是**既有原语的组装**，无新机制、无转译器改动 |
| 与 stage 16 转发钩子的关系 | **桥 = 完整托管派发，作为 register_drop_dispatch 的补充（additive）**，managed 模式优先：CcwDropDispatchImpl 先查 managed 注册，再回落 native 回调（stage 16），未注册回落 E_NOTIMPL（stage 15）。不删除/不替换 stage-16（section 18 已锁 14-16 断言；native 转发层保留为底层选项） |
| 桥的组成 | ① 新 per-CCW 注册 icall `icall_com_ccw_register_drop_dispatch_managed(ccw, idropTargetClass, argsClass)`（**every-platform**）② 新 pump icall `icall_com_ccw_drop_dispatch_managed(ccw, method, pDataObj, keyState, x, y, pdwEffect)`（**every-platform**，视图方法的内部核心 + 测试直接驱动入口）③ Windows IDropTarget 视图方法（CcwDropDragEnter/DragOver/Drop）在 managed 注册时调用 pump（POINTL 解码后） |
| 桥的数据流 | native DragEnter → 解码 POINTL(x/y) → `wrap_data_object(pDataObj)` → `il2cpp_object_construct(&DragEventArgs_class, wrapper, x, y, *pdwEffect)` → `il2cpp_interface_dispatch(managed, &IDropTarget_class, slot)` → `OnDragEnter(ev)` → 回写 `*pdwEffect = ev->Effect` |
| 方法槽解析 | **按名解析**（batch-2 先例 kDataObjectMethodNames）：注册时经 iface typeinfo 把 `kComDropMethod*`（0=DragEnter/1=DragOver/2=Drop/3=DragLeave）映射到托管接口槽（字母序实测 OnDragDrop=0/OnDragEnter=1/OnDragLeave=2/OnDragOver=3）——不硬编码槽号 |
| headless 可验证性 | ✅ **桥核心全平台可无头验证**：pump icall 是纯内存 + metadata + dispatch（无 ole32 依赖，stage-17 typed-view 每平台先例），section 22 全平台块直接驱动；Windows 块额外经真实 IDropTarget 视图 `call_vtable_slot` 验证 POINTL 解码 → 桥 → 托管 handler 的**端到端集成**（无需窗口/OLE 注册，section 18 先例） |
| DragLeave | **视图不派发**（保持 stage-15 S_OK 通知契约，无原生参数可构造 args）；pump 支持 method=3（零值 args 构造 + OnDragLeave 派发）供托管侧程序化驱动与测试 |
| 安全性 | 派发前 `il2cpp_is_instance(managed, dropIface)` 门控（batch-2 QI 门先例）——非实现者返回 E_NOTIMPL，**不触发 interface_dispatch 的 abort**；construct 失败（args 类未注册/无 .ctor）→ E_NOTIMPL |
| 测试设计 | test_com **section 22**（批次 3 之后的下一节）：22a every-platform 块（~15-18 断言）+ 22b Windows 块（~5-8 断言），合计 ~20-26 断言（524 → ~545-550） |
| 改动面 | il2cpp_com.h（2 声明）+ com.cpp（CCW 结构 6 成员 + 初始化 + kDropMethodNames + 注册/泵 icall + CcwDropDispatchImpl managed 分支）+ test_com.cpp section 22；**转译器 0 改动、fixture 0 改动（可选增强除外）、CMakeLists 0 改动** |

---

## 1. 现状分析

### 1.1 IDropTarget drop 视图（stage 15）—— 视图骨架就位，方法体未接托管派发

- `Il2CppComDropTargetVtable`（com.cpp:199-208）：7 槽，`pt` 参数已是 **intptr 载体**（stage-16 修正按值 POINTL）。
- CCW 持有 `dropVtbl`（Windows 门内，com.cpp:341）+ QI 分派（com.cpp:634，无条件交出视图）。
- 视图方法（com.cpp:874-907）：`CcwDropDragEnter`/`CcwDropDragOver`/`CcwDropDrop` → `CcwDropDispatchImpl`（com.cpp:855-872）；`CcwDropDragLeave` 保持 S_OK 通知契约。
- `kDropVtable` 组装（com.cpp:1299-1307）。**视图形状完整、方法体是转发层** —— 桥的接缝点。

### 1.2 转发钩子（stage 16）—— native 回调层，桥的"既有邻居"

- CCW `dropDispatch` 成员（com.cpp:343，Windows 门内）+ `icall_com_ccw_register_drop_dispatch`（com.cpp:3186，ccw==0→ANE，fn==0 清除）。
- `CcwDropDispatchImpl`（com.cpp:855）：`dropDispatch == nullptr → E_NOTIMPL`；否则解码 POINTL（com.cpp:866-867，x=低32/y=高32，little-endian x64）→ 调 `DropDispatchFn`（method/pDataObj/keyState/x/y/pdwEffect）。
- `kComDropMethod*` 常量（il2cpp_com.h:697-700）。section 18 已锁（14-16 断言，含字节精确转发、effect/HRESULT 双向传播、双 CCW 隔离、清除回落）。

### 1.3 托管 ctor 调用机制（批次 3）—— 桥的构造原语

- `il2cpp_object_construct(klass, 4×intptr)`（il2cpp_runtime.h:75，object.cpp:107）：object_new + typeinfo `.ctor` fnPtr + 签名解码（返回 'v'；参数 r:→Il2CppObject*（仅位置 0）/ i、V:→int32）+ paramCount 0..4 switch；**失败返 nullptr 非抛**。
- DragEventArgs 单 ctor `"v(r:IDataObject,i,i,V:DragDropEffects)"`（gen_v3 DragEventArgs.cpp:34）—— 桥构造形状完全匹配（4 载体：data/x/y/effect）。

### 1.4 typed-RCW 方向（批次 3）—— 桥的 pDataObj→托管 Data 原语

- `icall_com_wrap_data_object(pDataObj, ifaceClass)`（com.cpp:2389）：pDataObj==0→ANE；ifaceClass==0 按名惰性解析 IDataObject；产物 `TypedDataObjectRcwClass`（com.cpp:2216，vtable 3 stub 转发原生槽 3/5/7）；**借引用语义**。
- CCW 回环（section 21.7）已证：WfDataObject→CCW+view→wrap→Data→dispatch 回原对象。

### 1.5 转译注册产物（批次 1-3，gen_v3）—— 桥的目标/参数类型全部就位

- `IDropTarget_class`（IDropTarget.cpp:10：instanceSize 0、vtableSize 4、无接口表）+ typeinfo 4 方法（virtualSlot 字母序 OnDragDrop=0/OnDragEnter=1/OnDragLeave=2/OnDragOver=3）。
- `DragEventArgs_class`（DragEventArgs.cpp:10：instanceSize 40、refFieldBitmap bit1、Data 槽1/X 槽2/Y 槽3/Effect 槽4）+ typeinfo fields（Effect offset 32）+ `.ctor`（hash 095e8b5153636074）。
- `WfDropSource`/`WfDropSource2`（IDropTarget 双实现者，观察字段 EnterCount/LastX/LastY/LastEffect...）。
- `WfDataObject`（IDataObject 实现者，供 pDataObj 的 CCW view 回环）。entry.cpp `il2cpp_runtime_init` 注册全部 class + typeinfo。

### 1.6 测试形态（test_com.cpp）

- section 17（Windows，stage 15）：视图 QI/DragLeave S_OK/其余 E_NOTIMPL/RegisterDragDrop（DropRegThread worker + 真窗口）。
- section 18（Windows，stage 16）：`call_vtable_slot(dropView, kComDropSlotDragEnter, dropData, 0xABCD, ptBits, &effect)` 直接驱动视图 → 回调记录判别子。**无窗口/OLE 依赖的直接驱动视图 = 22b 的模板**。
- section 19/20/21（every-platform）：dispatch/typed-view/construct + wrap。**22a 的邻居**。
- 断言计数：524 = 504 + section 21 的 20。

---

## 2. 完整规格

### 2.1 设计决策

**D1 桥 = 完整托管派发，作为 register_drop_dispatch 的补充（additive）。**
- 替代方案的代价：删除 section 18 已锁的 native 转发层 = 回归；且 native 回调是文档化的底层选项。补充 + managed 优先 = 零回归 + 新增完整路径。
- 语义：同一 CCW 上 managed 与 native 两个注册槽独立存在；视图方法**先查 managed**，再回落 native 回调，最后 E_NOTIMPL。测试不混用两模式；文档注明优先级。

**D2 桥核心 every-platform。**
- 数据流中除"原生 OLE 视图入口"外全是纯内存 + metadata + interface_dispatch（无 ole32 依赖）—— 桥核心拆成**公开 pump icall**（stage-17 typed-view 每平台先例：dataObjectVtbl/dataObjectSlots 都在 Windows 门外）。这使 section 22a 全平台同代码块验证，Windows 的 OLE 视图只负责 POINTL 解码 + 调 pump（22b 额外验集成）。
- 因此新 CCW 成员放 Windows 门**外**（与 dataObjectSlots 同区）。

**D3 方法槽按名解析**（batch-2 kDataObjectMethodNames 先例，非硬编码槽号）。
- 注册时经 `il2cpp_type_get_method(ifaceInfo, kDropMethodNames[m])->virtualSlot` 缓存到 `dropSlots[4]`；-1 = 接口未声明/形状不支持 → 派发 E_NOTIMPL。

**D4 注册自校验**（防错误配对）。
- `ifaceClass->instanceSize == 0`（必须是接口描述符）否则 E_INVALIDARG；
- 每个 drop 方法的签名必须匹配 `"v(r:<argsClass 名>)"`（返回 'v'、1 个 r: 参数、r: 类型名 == argsClass->name）否则 E_INVALIDARG —— 直接拦截"把 WfDropSource 当 args 类"这类配对错误；
- argsClass 必须有 typeinfo + `.ctor`（`il2cpp_type_get_method(argsInfo, ".ctor")` 非空）否则 E_INVALIDARG；
- argsClass 的 `Effect` 字段 offset 经 `il2cpp_type_get_field(argsInfo, "Effect")` 解析并缓存（无 Effect 字段 → -1 → 跳过回写）。

**D5 派发前 is_instance 门控。**
- `!il2cpp_is_instance(managed, dropIface)` → E_NOTIMPL（不触发 interface_dispatch 的 abort 分支；batch-2 QI 门先例）。注册本身不校验 is_instance（与 register_interface_view 同——注册与派发门分离）。

**D6 签名子集。**
- pump 只支持 `"v(r:X)"` 形状（fixture IDropTarget 精确形状）；void → S_OK。其它形状 → E_NOTIMPL（CcwDataObjectDispatch 的 v(i,i)/i(i) 子集先例）。

**D7 DragLeave。**
- 视图方法（Windows）不派发 DragLeave（保持 stage-15 S_OK 通知契约；原生无参数可构造）。pump 支持 method=3（零值 args：data=0/x=0/y=0/effect=0 → 构造 → OnDragLeave）供托管侧程序化驱动与测试。22b 锁定"视图 DragLeave S_OK 且 LeaveCount 不 bump"。

**D8 effect 回写。**
- 进入时 `*pdwEffect` 携带允许掩码 → 作 ctor 的 effect 参数（成为 ev->Effect）；派发后 `*pdwEffect = ev->Effect`（读缓存的 Effect 字段 offset）。`pdwEffect == nullptr` 或 Effect offset -1 → 跳过回写（guard）。

### 2.2 com.cpp 改动清单

**① CCW 结构新增 6 成员**（com.cpp:355 `dataObjectSlots` 之后，Windows 门**外**）：

```cpp
    // Stage 17 batch 4: the managed drop dispatch (the CCW->OnDragEnter
    // bridge). Registered via icall_com_ccw_register_drop_dispatch_managed
    // (every platform); the pump icall_com_ccw_drop_dispatch_managed drives
    // decode->construct->dispatch->effect-writeback and the Windows IDropTarget
    // view methods call it after the POINTL decode (managed takes priority over
    // the stage-16 native forwarding callback). Plain pointers/flags -- no
    // ownership, nothing to free in the destructor.
    int32_t  dropManaged;            // 1 = the managed dispatch is registered
    Il2CppClass* dropIface;          // the transpiled IDropTarget descriptor
    const Il2CppTypeInfo* dropIfaceInfo; // its typeinfo (name -> slot resolution)
    Il2CppClass* dropArgsClass;      // the DragEventArgs descriptor
    int32_t  dropSlots[4];           // cached interface slot per kComDropMethod*; -1 = undeclared
    int32_t  dropEffectOffset;       // the args' "Effect" field offset; -1 = no write-back
```

**② 方法名表**（every-platform，anon ns 内，靠近 kDataObjectMethodNames com.cpp:1891）：

```cpp
// The native IDropTarget method discriminators (kComDropMethod*, stage 16) ->
// MANAGED interface method NAMES (stage 17 batch 4). The interface's vtable
// position is resolved by name because the transpiler sorts interface slots
// ALPHABETICALLY (OnDragDrop=0/OnDragEnter=1/OnDragLeave=2/OnDragOver=3) while
// the OLE vtable is DECLARATIVE -- only the NAME is a stable bridge.
const char* const kDropMethodNames[4] = {
    "OnDragEnter", // kComDropMethodDragEnter (0)
    "OnDragOver",  // kComDropMethodDragOver  (1)
    "OnDragDrop",  // kComDropMethodDrop      (2)
    "OnDragLeave", // kComDropMethodDragLeave (3)
};
```

**③ ccw_create 初始化**（com.cpp:3107 dataObjectSlots 循环之后，Windows 门外）：`dropManaged=0; dropIface=nullptr; dropIfaceInfo=nullptr; dropArgsClass=nullptr; dropSlots 全 -1; dropEffectOffset=-1;`

**④ CcwDropDispatchImpl managed 分支**（com.cpp:855，顶部插入）：

```cpp
    if (ccw->dropManaged)
    {
        // The managed drop dispatch (batch 4): decode the by-value POINTL
        // (already decoded below -- x = low 32, y = high 32) and drive the
        // full bridge (wrap pDataObj -> construct DragEventArgs ->
        // interface_dispatch -> effect write-back) through the shared pump.
        const int32_t x = static_cast<int32_t>(static_cast<uint64_t>(pt));
        const int32_t y = static_cast<int32_t>(static_cast<uint64_t>(pt) >> 32);
        return icall_com_ccw_drop_dispatch_managed(
            reinterpret_cast<intptr_t>(ccw), method,
            reinterpret_cast<intptr_t>(pDataObj), grfKeyState, x, y, pdwEffect);
    }
    if (ccw->dropDispatch == nullptr) { return kComHrNotImpl; }
    // ... existing native forwarding unchanged ...
```

（POINTL 解码逻辑与既有分支共享——把解码提到分支前一次完成，两个分支都复用。）

**⑤ 注册 icall**（anon ns 关闭 com.cpp:2377 之后，namespace scope，near icall_com_wrap_data_object com.cpp:2389）：

```cpp
// icall_com_ccw_register_drop_dispatch_managed -- declare the managed drop
// dispatch on a CCW (stage 17 batch 4, EVERY platform -- pure memory +
// typeinfo + dispatch, no ole32 dependency). 'ccw' is the identity IUnknown
// (icall_com_ccw_create), 'ifaceClass' the transpiled IDropTarget descriptor
// and 'argsClass' the DragEventArgs descriptor (both intptr carriers; com.cpp
// cannot reference generated symbols). Registration resolves the interface's
// typeinfo (il2cpp_typeinfo_lookup by name) and caches, per kComDropMethod*,
// the MANAGED interface slot of the method whose NAME matches
// (kDropMethodNames -- the typeinfo virtualSlot lookup); validates the args
// class pairs with the interface's "v(r:Args)" methods. ccw/ifaceClass/
// argsClass == 0 -> ArgumentNullException; a non-interface iface / no typeinfo
// / a mismatched args class / no .ctor -> E_INVALIDARG. Re-registering
// replaces the state; there is no clear (an unregistered CCW is the fallback).
int32_t icall_com_ccw_register_drop_dispatch_managed(
    intptr_t ccwPtr, intptr_t ifaceClassPtr, intptr_t argsClassPtr)
```

实现要点：null 守卫（3 个 ANE）→ iface 校验（instanceSize==0，否则 E_INVALIDARG）→ `il2cpp_typeinfo_lookup(iface->name)`（null → E_INVALIDARG）→ 逐 m：`il2cpp_type_get_method(ifaceInfo, kDropMethodNames[m])`（null → dropSlots[m]=-1；非 "v(r:<args 名>)" 形状 → E_INVALIDARG；否则缓存 virtualSlot）→ argsInfo 解析 + `.ctor` 存在校验 + `Effect` 字段 offset 解析 → 写 `dropIface/dropIfaceInfo/dropArgsClass/dropSlots/dropEffectOffset/dropManaged=1` → S_OK。

**⑥ pump icall**（紧邻注册 icall）：

```cpp
// icall_com_ccw_drop_dispatch_managed -- drive the managed drop dispatch
// (stage 17 batch 4, EVERY platform -- the shared core the Windows IDropTarget
// view methods call after the POINTL decode, and the direct entry a caller or
// test uses with already-decoded x/y). 'method' is kComDropMethod*, 'pDataObj'
// the by-value IDataObject* (0 = none), 'grfKeyState' the OLE modifier bits
// (DROPPED -- the fixture DragEventArgs has no KeyState field), 'x'/'y' the
// POINTL members, 'pdwEffect' the effect in/out (in = the allowed mask the
// ctor seeds ev->Effect with; out = ev->Effect written back). Pipeline:
//   data = (pDataObj != 0) ? icall_com_wrap_data_object(pDataObj, 0) : 0
//   ev   = il2cpp_object_construct(argsClass, data, x, y, *pdwEffect)
//   il2cpp_interface_dispatch(managed, dropIface, dropSlots[method])(managed, ev)
//   *pdwEffect = ev->Effect (the cached Effect field offset)
// Unregistered (dropManaged == 0) / undeclared method (slot -1) / construct
// failure / non-implementer (is_instance gate, no abort) -> E_NOTIMPL.
// ccw == 0 -> ArgumentNullException; method out of 0..3 ->
// ArgumentOutOfRangeException. void ("v(r:X)") methods -> S_OK.
int32_t icall_com_ccw_drop_dispatch_managed(
    intptr_t ccwPtr, int32_t method, intptr_t pDataObj,
    uint32_t grfKeyState, int32_t x, int32_t y, uint32_t* pdwEffect)
```

实现要点：ccw==0→ANE → method<0||>3→AOORE → `dropManaged==0`→E_NOTIMPL → `dropSlots[method]` 校验 → `icall_gchandle_get_target(ccw->gcHandle)`（null→E_NOTIMPL）→ `il2cpp_is_instance` 门 → wrap（pDataObj==0→null data）→ `il2cpp_object_construct(dropArgsClass, data, x, y, effectIn)`（null→E_NOTIMPL）→ 签名解码（`v(r:X)` 形状）→ `il2cpp_interface_dispatch` + cast `void(*)(Il2CppObject*, Il2CppObject*)` 调用 → effect 回写 → S_OK。

> 注意：pump 与注册 icall 都是**公开 icall**（il2cpp_com.h 声明），必须定义在 anon ns 之外（batch-3 教训：anon 内定义 = 内部链接 undefined symbol）；anon 成员（kDropMethodNames、CcwFromDropView 等）TU 内无限定引用可用。pump 不直接引用视图方法——视图方法引用 pump（header 声明可见）。

**⑦ 无析构改动**：6 个成员是裸指针/标志/偏移，无所有权（dropArgsClass/dropIface 是被动存储，不 AddRef 不释放）。

### 2.3 il2cpp_com.h 改动清单

两条声明 + 完整契约注释（~55-65 行），放 stage-16 `icall_com_ccw_register_drop_dispatch`（il2cpp_com.h:762）之后的新块 **"Managed drop dispatch (stage 17 batch 4 -- EVERY platform)"**（块本身不 gate `_WIN32`，两条 icall 全平台实现）：

```cpp
int32_t icall_com_ccw_register_drop_dispatch_managed(intptr_t ccw, intptr_t ifaceClass, intptr_t argsClass);
int32_t icall_com_ccw_drop_dispatch_managed(intptr_t ccw, int32_t method, intptr_t pDataObj,
                                            uint32_t grfKeyState, int32_t x, int32_t y, uint32_t* pdwEffect);
```

### 2.4 测试改动清单（test_com.cpp section 22，批次 3 之后、Report 之前）

**22a every-platform 块**（core，~15-18 断言）：

前置：`il2cpp_runtime_init()` 已在 section 19 调用；复用 section 21.7 的 typed-RCW 回环模式造 `pDataObj`（WfDataObject src + CCW + register_interface_view + QI → `dataView` 原生 IDataObject*）。

1. **注册**：`icall_com_ccw_register_drop_dispatch_managed(ccw, &IDropTarget_class, &DragEventArgs_class)` → S_OK。
2. **null 守卫**：ccw 0 / iface 0 / args 0 → ArgumentNullException（各 1 条）。
3. **校验**：ifaceClass 传 `&WfDropSource_class`（instanceSize != 0 非接口）→ E_INVALIDARG；argsClass 传 `&WfDropSource_class`（r: 类型名不匹配）→ E_INVALIDARG。
4. **DragEnter pump**：`icall_com_ccw_drop_dispatch_managed(ccw, kComDropMethodDragEnter, dataView, 0xABCD, 0x1122, 0x3344, &effect[in=0x2])` → S_OK + `EnterCount==1` + `LastX==0x1122 && LastY==0x3344 && LastEffect==2`（方法被调 + 字段正确）+ `effect==0x2`（回写）。keyState 0xABCD 被吞但不破坏。
5. **DragOver pump**（pDataObj 0）：`OverCount==1` + x/y 判别子。
6. **Drop pump**：`DropCount==1` + 新判别子（0x5566/0x7788/4）。
7. **DragLeave pump**（method 3，零值 args）：`LeaveCount==1`（managed OnDragLeave 路径可达）。
8. **回写判别子**：effect 输入 0x2 → 派发后 `*pdwEffect==0x2`（回写源自构造的 args 对象 Effect，非 echo 输入——handler 读取 e.Effect 已由 LastEffect 佐证）。
9. **未注册回落**：fresh CCW pump → E_NOTIMPL。
10. **非实现者门控**：WfDataObject 的 CCW 注册成功（注册不校验 is_instance）→ pump → E_NOTIMPL（is_instance 门，**不 abort**）。
11. **双 CCW 隔离**：第二个未注册 CCW pump → E_NOTIMPL；第一个仍派发。
12. **method OOB**：pump method 4 → ArgumentOutOfRangeException。
13. **refcount 不扰**：注册前后 AddRef/Release 计数不变。

**22b Windows 块**（视图集成，~5-8 断言，section 18 模板，无窗口/OLE）：

14. 注册 managed dispatch 的 CCW → QI `kComIidIDropTarget` → 独立视图。
15. `call_vtable_slot(view, kComDropSlotDragEnter, dataView, 0xABCD, ptBits(0x55667788|0x11223344), &effect)` → S_OK + `EnterCount` bump + `LastX==0x11223344 && LastY==0x55667788`（POINTL 解码 → 桥 → 托管 handler 端到端）+ effect 回写。
16. `call_vtable_slot(view, kComDropSlotDragOver, ...)` / `kComDropSlotDrop, ...` → OverCount/DropCount bump。
17. **DragLeave 契约**：`call_vtable_slot(view, kComDropSlotDragLeave, ...)` → S_OK **且 LeaveCount 不 bump**（视图不派发 DragLeave）。
18. 释放视图 + CCW → 0。

**断言计数**：~20-26（524 → ~545-550）。POSIX 无新 stub（22a 全平台）。

### 2.5 顺序

1. **il2cpp_com.h** 两条声明（+注释）→ cmake build 0 error（测试尚未引用，编译过）。
2. **com.cpp**：CCW 成员 + 初始化 + kDropMethodNames + 注册/pump icall + CcwDropDispatchImpl managed 分支 → build 0 error。
3. **test_com.cpp section 22** 写 22a（1-13）→ build → 跑 → 绿；补 22b（14-18，`#if defined(_WIN32)`）→ 绿。
4. **全量回归**：test_com（~545-550）→ runtime 139/139 → unit 1807/1807 → e2e（com17type + 关键案例）0 diff。
5. 提交。W8 更新 4 memory 文件。

### 2.6 验证命令（C0）

```
cmake --build E:\il2cpp\build\runtime_full -j 16   # 0 errors
E:\il2cpp\build\runtime_full\test_com.exe          # all PASS（断言 ~545-550）
# 全量 139 test_*.exe                                # 139/139
dotnet test E:\il2cpp\tests\unit\unit.csproj       # 1807/1807
pwsh scripts/run-tests.ps1 -Compiler mingw          # e2e com17type 等 0 diff
```

### 2.7 可选增强（独立交付，本批不强制）

**桥内 Data 端到端观察**：当前 fixture 的 OnDragEnter 不读 e.Data，桥测试无法观察到 Data 经桥落到 handler。若需断言，改 fixture（com17type.cs WfDropSource + `public IDataObject LastData;` + OnDragEnter 加 `LastData = e.Data;`）→ 重转译 gen_v4 → CMakeLists gen_v3→gen_v4 → section 22 加"LastData 非空 + is_instance IDataObject（DragEnter）/ LastData==null（DragOver pDataObj 0）"2 条断言。涟漪：OnDragEnter 方法 hash 变（generated.h/vtable 自洽；test_com 不按符号引 OnDragEnter，只经 dispatch——零 hash 同步）；WfDropSource 结构尾部 +8 字节（既有字段 offset 不变）；e2e expected.txt 不变。**本规格核心不依赖此增强**（X/Y/Effect 正确性已由 LastX/LastY/LastEffect + effect 回写充分断言；Data 的原始级正确性由 section 21.7 已证）。

---

## 3. 工作量 + 风险

| 项 | 工作量 | 风险 |
|---|---|---|
| il2cpp_com.h 2 声明 + 注释 | 低（~60 行） | 低 |
| com.cpp：CCW 6 成员 + 初始化 | 低（~15 行） | 低（纯标志/指针，无所有权） |
| com.cpp：kDropMethodNames + 注册 icall + pump icall | 中（~110 行） | 中（新签名解码面 + 自校验逻辑 + anon ns 外定义纪律） |
| com.cpp：CcwDropDispatchImpl managed 分支 | 低（~8 行） | 低（纯新增分支；POINTL 解码复用） |
| test_com.cpp section 22（22a + 22b） | 中（~160-190 行，20-26 断言） | 中（pump 判别子 + 视图集成 + is_instance 门不 abort） |
| 全量回归（runtime/unit/e2e） | 中 | 低 |
| **合计** | **中（~350-400 行）** | **低-中** |

**主要风险**：

1. **pump 签名解码新子集（中）**：`"v(r:X)"`（1 个 r: 参数的 void 接口方法）是 dispatch 侧首次按签名解码调用的形状（CcwDataObjectDispatch 只覆盖 v(i,i)/i(i)）。但 section 19 已直接调 `(void(*)(Il2CppObject*, Il2CppObject*))(*slot)(obj, ev)` 证明调用形状正确——pump 只是加一个 decode→cast 路由。判别子测试（x/y/effect 独立值）锁定。
2. **is_instance 门缺失即 abort（中）**：忘加 `il2cpp_is_instance` 门 → 对非实现者 pump 会触发 interface_dispatch 的 abort 分支（metadata.cpp:399）→ 测试崩溃而非断言失败。断言 10（非实现者 → E_NOTIMPL）显式锁定此门。
3. **anon namespace 纪律（低）**：注册/pump 必须定义在 `} // namespace`（com.cpp:2377）之后（batch-3 教训：anon 内定义 = undefined symbol）。kDropMethodNames 必须在 Windows 门**外**（pump every-platform 要引用）。
4. **每平台放置（低）**：CCW 新成员必须在 Windows 门外（pump every-platform 读 dropSlots/dropEffectOffset 等）。`#if` 门边界编辑后 git diff 核对（batch-3 教训）。
5. **注册自校验过度/不足（低）**：r: 类型名匹配用简单名（"DragEventArgs"）比较——与生成签名/描述符名一致（gen_v3 验证）。若未来带命名空间全名，需解码扩展（超出本批）。
6. **construct 失败路径（低）**：args 类未注册 typeinfo / 无 .ctor → construct 返 nullptr → pump E_NOTIMPL（非崩溃）。注册时已校验 `.ctor` 存在，运行期失败是防御性路径。
7. **零回归（低）**：native 转发（stage 16）与视图 E_NOTIMPL 回落（stage 15）分支原样保留；section 17/18 断言不动。22b 的 DragLeave 断言显式重申 stage-15 契约。

**验证**：
- `cmake --build E:\il2cpp\build\runtime_full -j 16` → 0 errors（C0）
- `test_com.exe` → all PASS（断言 524 → ~545-550）
- 全量 `test_*.exe` → 139/139
- `dotnet test unit.csproj` → 1807/1807
- e2e（com17type 等）→ 0 diff

---

## 4. 最终推荐

**批次 4 = CCW→dispatch 桥（完整托管派发，补充 register_drop_dispatch）**：

1. **il2cpp_com.h**：`icall_com_ccw_register_drop_dispatch_managed` + `icall_com_ccw_drop_dispatch_managed` 两条声明（every-platform）。
2. **com.cpp**：CCW 结构 6 成员（Windows 门外）+ ccw_create 初始化 + `kDropMethodNames` + 注册/pump 两个 icall（anon ns 外，namespace scope）+ `CcwDropDispatchImpl` managed 分支（优先于 native 回调）。
3. **test_com.cpp section 22**：22a every-platform（注册/守卫/校验/pump 判别子/回写/未注册回落/is_instance 门/双 CCW 隔离/OOB/refcount）+ 22b Windows（真实 IDropTarget 视图 → POINTL 解码 → 桥 → 托管 handler 端到端 + DragLeave 契约）。
4. **转译器 0 改动、fixture 0 改动（核心）、CMakeLists 0 改动**。
5. **验证**：runtime_full 0 errors + test_com ~545-550 + 139/139 + unit 1807/1807 + e2e 0 diff。

**批次 4 解锁**：WinForms Phase 46+ 的拖放面（Control.AllowDrop 的托管 OnDragEnter 派发落地路径已通）、以及任意"CCW 视图 → 构造参数对象 → interface_dispatch"的通用桥模式（pump icall 是该模式的通用载体）。
