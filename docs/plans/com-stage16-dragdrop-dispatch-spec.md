# COM Stage 16 规格 — IDropTarget 派发转发钩子（原始 OLE 参数 → 注册回调）

- 日期：2026-08-16
- 性质：审计子代理产出（纯研究，无代码改动）
- 上游：stage 15 审计结论 "拖放完整面（含托管派发）stage 16+ 远期，需先建托管派发基建（DragEventArgs 构造 + typed IDataObject RCW + POINTL/DWORD 编组 + il2cpp_interface_dispatch 实例方法派发）"
- 基线：HEAD 86aff31（stage 15 OLE 拖放最小面已合入，test_com 428 断言，runtime 139/139）
- 关键决策：**完整托管派发（DragEventArgs + typed RCW + interface_dispatch）不可作为独立 COM 阶段落地** —— 推荐 stage 16 = **派发转发钩子**（native 视图方法把原始 OLE 参数转发给注册的 native 回调；未注册时回落 stage-15 面）

---

## 0. 结论摘要

| 项 | 结论 |
|---|---|
| 完整托管派发可行性 | **不可行（作为独立 COM 阶段）**：需 WinForms 类型（Control/DragEventArgs/托管 IDropTarget 接口）已转译注册 + typed IDataObject RCW 基建 + 运行时调用带对象/整型参数的托管构造函数 —— 三者皆无先例且耦合 WinForms 域，非 COM 运行时自包含 |
| il2cpp_interface_dispatch 适用性 | **机制本身适用**（metadata.cpp:359 是 CCW 视图 → 托管方法的正确派发机制），但**无法无头验证**：测试的托管目标（MakeManagedTarget = string）无接口映射，dispatch 会 abort（"string does not implement"）—— 且仍需先构造 DragEventArgs 实参 |
| **推荐 stage 16** | **IDropTarget 派发转发钩子**：`icall_com_ccw_register_drop_dispatch(ccw, fn)` 注册一个 native 派发回调；DragEnter/DragOver/Drop/DragLeave 视图方法把原始 OLE 参数（pDataObj / grfKeyState / POINTL x,y / pdwEffect）解码后转发给回调，回调的 HRESULT 与 effect 写回传播。未注册 → 回落 stage-15 面（DragLeave S_OK / 其余 E_NOTIMPL） |
| headless 可验证性 | ✅ 完全无头：测试用纯 C++ 回调函数注册 + `icall_com_call_vtable_slot` 直接驱动视图，断言原始参数字节精确转发 + effect/HRESULT 双向传播 + 双 CCW 隔离。无需窗口/线程/OLE 注册 |
| 关键发现 | stage-15 视图 vtable 的 `const void* pt` 参数是**按值 POINTL 的错误声明**（x64 下 8 字节 POINTL 占一个寄存器槽，读到 `void*` 位型中，按指针解引用即 UB）—— 因 E_NOTIMPL 从不解引用而未暴露；stage 16 必须改为按值解码（`intptr_t pt` 载体，x=低32位 y=高32位，little-endian） |

---

## 1. 拖放完整托管派发可行性分析

### 1.1 编组鸿沟（完整面路径，4 步）

native `IDropTarget` 收原始 OLE 参数，托管 `OnDragEnter` 收 `DragEventArgs` 对象。完整派发路径（com-stage14 审计 §1.2 已列）：

| # | 步骤 | 现状（证据） | 可行性 |
|---|---|---|---|
| 1 | `IDataObject*` → 托管 typed RCW | `WrapUnknown`（com.cpp:1825）只产通用 `__ComObject`，**无接口类型分派**；无 typed-RCW 基建 | ❌ 新基建，无先例 |
| 2 | 构造 `DragEventArgs` 托管对象 | 需 WinForms 类型转译注册 + **从 native 调用带 (IDataObject, int, int, int, DragDropEffects, DragDropEffects) 的托管构造函数**；`il2cpp_method_invoke_i4`（api.cpp:128）仅 C API 静态 4×i4 export 表派发，非实例构造 | ❌ 耦合 WinForms 域 + 无 ctor 调用机制 |
| 3 | POINTL/DWORD 编组 | POINTL = 两个 int32（offset 0/4），可直读；无 pinvoke-struct 先例但 trivial | ✅ 低风险 |
| 4 | `il2cpp_interface_dispatch` 派发到 `OnDragEnter` | metadata.cpp:359 机制存在（obj->klass->interfaces 线性搜索 + slotMap[ifaceSlot]） | ⚠️ 机制对，但无头不可验证（见 1.2） |

### 1.2 派发机制分析（il2cpp_interface_dispatch 是否适用）

`il2cpp_interface_dispatch(obj, iface, ifaceSlot)`（metadata.cpp:359，il2cpp_runtime.h:101）：
- 通过 `obj->klass->interfaces` 线性搜索 iface 条目 + `entry.slotMap[ifaceSlot]` 解析到 vtable 槽，返回函数指针地址。
- CCW 持有一个强 GCHandle（`icall_gchandle_alloc_normal`，com.cpp:2667）；`icall_gchandle_get_target`（il2cpp_gchandle.h:83）恢复托管对象。

**机制本身是正确的最终派发通道**（转译的 WinForms Control 实现托管 `System.Windows.Forms.IDropTarget` 接口时，其 klass 带 interfaces + slotMap，dispatch 可解析到生成的 `OnDragEnter`）。但作为**独立 COM 阶段**落地存在三个不可逾越的障碍：

1. **无头不可验证**：test_com.cpp 的托管目标 `MakeManagedTarget()`（test_com.cpp:152）= 一个 string。string 的 klass 不实现 IDropTarget → `il2cpp_interface_dispatch` 走 abort 分支（metadata.cpp:399 "does not implement"）。无 WinForms 类型注册就**无法在无头测试中构造一个实现托管 IDropTarget 的对象**（接口描述符 + slotMap + vtable 是转译器发射的，非运行时可造）。
2. **DragEventArgs 实参构造无机制**：即便 dispatch 可用，还需先构造 `DragEventArgs(IDataObject, int, int, int, DragDropEffects, DragDropEffects)`。运行时无"按名调用托管构造函数"的能力（api.cpp export 表仅静态 4×i4）；WinForms 类型本身也未转译注册。
3. **typed RCW 无先例**：`WrapUnknown` 产通用 RCW 无接口分派；构造 typed IDataObject RCW 需新的运行时基建（RCW 类描述符携带接口表 + 方法映射到 native vtable 槽）。

**结论**：完整托管派发的 4 步中 3 步（typed RCW / DragEventArgs 构造 / dispatch 验证）依赖 WinForms 域（类型转译注册）或新的运行时基建，**不是 COM 阶段自包含的工作**。诚实的路径是：stage 16 提供**转发层**（native 视图把原始 OLE 参数转发给注册回调），把 DragEventArgs 构造 + typed RCW 包装留在托管侧（未来 WinForms 层绑定 delegate thunk，在托管代码里构造对象——那里类型存在）。

### 1.3 POINTL 按值 ABI 陷阱（stage-16 必须修）

stage-15 视图 vtable（com.cpp:203-207）声明 `dragEnter(void*, void*, uint32_t, const void*, uint32_t*)` —— 第 4 参数 `const void* pt`。

oleidl.h 中 `DragEnter(IDataObject*, DWORD, POINTL, DWORD*)` 的 **POINTL 是按值传递**（8 字节聚合，Win64 ABI 占一个寄存器槽）。C++ 函数指针类型 `const void*` 也占一个 8 字节槽 → **调用 ABI 匹配**（调用方在同一个寄存器里传 8 字节），但**按值 POINTL 的位型被声明为指针**：`const void* pt` 收到的是 8 字节原始位，按指针解引用（`((int32_t*)pt)[0]`）即 UB。stage-15 因 E_NOTIMPL 从不读 pt 而未暴露；stage 16 必须把 pt 声明改为整数载体并按值解码：

- vtable 函数指针类型参数 `const void* pt` → `intptr_t pt`（8 字节整数载体）。
- 解码（little-endian x64，ILC 目标 x64 —— pinvokecb.cpp:48 static_assert 先例）：`x = (int32_t)(uint64_t)pt; y = (int32_t)((uint64_t)pt >> 32);`。
- 这使按值 POINTL 的位型显式化为整数，解码良定义。stage-12 的按值 LARGE_INTEGER 专属 int64_t 参数 icall 是先例（com.cpp 头部注释 701-713：按值 64 位聚合需专门载体，4×intptr vtable-slot 载体在 x86 劈两半 / x64 整传，双平台镜像不一致）。

---

## 2. 推荐 stage 16 完整规格：IDropTarget 派发转发钩子

### 2.1 设计原则

- **转发而非编组**：native 视图方法把原始 OLE 参数（pDataObj / grfKeyState / POINTL x,y / pdwEffect）解码后转发给注册回调，回调返回 HRESULT 并写入 effect（OLE in/out 契约）。DragEventArgs/typed RCW 的构造留给托管侧（未来 WinForms 层经 delegate thunk 绑定，见 §2.4）。
- **向后兼容**：未注册回调 → 完全回落 stage-15 面（DragLeave S_OK / DragEnter/DragOver/Drop E_NOTIMPL），stage-15 的 428 断言零回归。
- **每 CCW 一个回调**：CCW 结构持有 `dropDispatch` 指针（Windows 门内，与 dropVtbl 同区），天然隔离多 CCW。
- **无窗口/线程/OLE 依赖**：测试直接驱动视图 vtable（`icall_com_call_vtable_slot`），比 stage-15 的 DropRegThread 简单得多。

### 2.2 新 icall 签名（il2cpp_com.h 声明）

```cpp
// IDropTarget 派发方法索引（stage 16）：注册回调收到的方法判别符（逻辑
// IDropTarget 方法序号，非 vtable 槽位）。
constexpr int32_t kComDropMethodDragEnter = 0;
constexpr int32_t kComDropMethodDragOver  = 1;
constexpr int32_t kComDropMethodDrop      = 2;
constexpr int32_t kComDropMethodDragLeave = 3;

// IDropTarget 派发回调注册（stage 16 -- the OLE drag-drop forwarding layer,
// WINDOWS only）。注册一个 native 派发回调，CCW 的 IDropTarget 视图方法
// （DragEnter/DragOver/Drop/DragLeave）把原始 OLE 参数解码后转发给它：
//
//   int32_t (*)(int32_t method, intptr_t pDataObj, uint32_t grfKeyState,
//               int32_t x, int32_t y, uint32_t* pdwEffect)
//
//   method        -- kComDropMethod* 判别符（0=DragEnter, 1=DragOver,
//                    2=Drop, 3=DragLeave）
//   pDataObj      -- 按值 IDataObject*（intptr carrier）；DragOver/DragLeave
//                    无数据对象（0）
//   grfKeyState   -- OLE 键盘修饰位（DWORD，原样转发）
//   x, y          -- POINTL 成员（从按值 POINTL 解码：x=低32位, y=高32位，
//                    little-endian -- 见 com.cpp 实现注释）
//   pdwEffect     -- OLE effect in/out 指针：进入时携带允许的 effect 掩码，
//                    回调写入其接受的效果（在 *pdwEffect 上就地更新，调用
//                    方继续看到）
//   返回值        -- 回调返回的 HRESULT，作为视图方法的返回（转发不掩码）
//
// 视图方法转发语义：DragEnter/DragOver/Drop 返回回调的 HRESULT 并传播回调
// 对 *pdwEffect 的写回；DragLeave 返回回调的 HRESULT（无参数，pdwEffect=0）。
// fn == 0（intptr carrier 0）清空派发 → 回落 stage-15 面（DragLeave S_OK /
// 其余 E_NOTIMPL）。ccw == 0 -> ArgumentNullException。Windows: full
// implementation。Non-Windows: E_NOTIMPL（the POSIX stub）。
int32_t icall_com_ccw_register_drop_dispatch(intptr_t ccw, intptr_t fn);
```

**设计要点**：
- `ccw` 是 `icall_com_ccw_create` 返回的 identity IUnknown 指针（intptr carrier）。
- `fn` 是 native 函数指针（intptr carrier）；`0` = 清除派发（合法，非空检查——与注册调用不同）。这与 `icall_com_ccw_register_connection_point` 的"每 CCW 注册"模式一致（il2cpp_com.h:536），回调本身是裸函数指针（pinvokecb.cpp 的 thunk 池先例：native 侧持有指针，运行时只管存储/调用）。
- 命名 `register_drop_dispatch` 而非 `set_drop_callback`：对齐 `icall_com_ccw_register_connection_point` / `icall_com_ccw_enum_register_item` 的"每 CCW 注册"命名族。

### 2.3 com.cpp 设计

**① vtable 函数指针类型修正**（`Il2CppComDropTargetVtable`，com.cpp:198-207）：pt 参数 `const void*` → `intptr_t`（按值 POINTL 整数载体，§1.3）：

```cpp
struct Il2CppComDropTargetVtable
{
    int32_t  (IL2CPP_COM_CALL* queryInterface)(void*, const void*, void**);
    uint32_t (IL2CPP_COM_CALL* addRef)(void*);
    uint32_t (IL2CPP_COM_CALL* release)(void*);
    int32_t  (IL2CPP_COM_CALL* dragEnter)(void*, void*, uint32_t, intptr_t, uint32_t*); // slot 3
    int32_t  (IL2CPP_COM_CALL* dragOver)(void*, uint32_t, intptr_t, uint32_t*);         // slot 4
    int32_t  (IL2CPP_COM_CALL* dragLeave)(void*);                                        // slot 5
    int32_t  (IL2CPP_COM_CALL* drop)(void*, void*, uint32_t, intptr_t, uint32_t*);       // slot 6
};
```

**② CCW 结构新增成员**（Windows 门内，com.cpp:316 附近）：

```cpp
    void* dropDispatch;              // stage 16: 注册的派发回调（null = 未注册）
```

**③ `icall_com_ccw_create` 初始化**（com.cpp:2636-2656，Windows 门内 `ccw->dropVtbl = &kDropVtable;` 之后）：

```cpp
    ccw->dropDispatch = nullptr;
```

**④ 视图方法实现**（替换 CcwDropNotImpl5 / CcwDropNotImpl4，com.cpp:784-787）：

```cpp
// 共享派发转发实现：method 判别 + 按值 POINTL 解码 + 回调调用。
// 未注册（dropDispatch == null）→ 回落 stage-15 面（E_NOTIMPL；
// DragLeave 走专用 S_OK 路径）。回调返回的 HRESULT 与对 *pdwEffect 的
// 写回原样传播（转发不掩码、不伪造）。
int32_t CcwDropDispatchImpl(Il2CppComCcw* ccw, int32_t method, void* pDataObj,
                            uint32_t grfKeyState, intptr_t pt, uint32_t* pdwEffect)
{
    if (ccw->dropDispatch == nullptr)
    {
        return kComHrNotImpl; // stage-15 面：托管派发未注册
    }
    // 按值 POINTL 解码（little-endian x64，ILC 目标 x64 -- pinvokecb.cpp
    // static_assert 先例）：POINTL { LONG x; LONG y; } 8 字节按值占一个
    // 寄存器槽，整数载体读出后 x=低32位 y=高32位。
    const int32_t x = static_cast<int32_t>(static_cast<uint64_t>(pt));
    const int32_t y = static_cast<int32_t>(static_cast<uint64_t>(pt) >> 32);
    typedef int32_t (IL2CPP_COM_CALL* DropDispatchFn)(
        int32_t, intptr_t, uint32_t, int32_t, int32_t, uint32_t*);
    return reinterpret_cast<DropDispatchFn>(ccw->dropDispatch)(
        method, reinterpret_cast<intptr_t>(pDataObj), grfKeyState, x, y, pdwEffect);
}

int32_t IL2CPP_COM_CALL CcwDropDragEnter(void* self, void* pDataObj,
                                         uint32_t grfKeyState, intptr_t pt,
                                         uint32_t* pdwEffect)
{
    return CcwDropDispatchImpl(CcwFromDropView(self), kComDropMethodDragEnter,
                               pDataObj, grfKeyState, pt, pdwEffect);
}

int32_t IL2CPP_COM_CALL CcwDropDragOver(void* self, uint32_t grfKeyState,
                                        intptr_t pt, uint32_t* pdwEffect)
{
    return CcwDropDispatchImpl(CcwFromDropView(self), kComDropMethodDragOver,
                               nullptr, grfKeyState, pt, pdwEffect);
}

int32_t IL2CPP_COM_CALL CcwDropDrop(void* self, void* pDataObj,
                                    uint32_t grfKeyState, intptr_t pt,
                                    uint32_t* pdwEffect)
{
    return CcwDropDispatchImpl(CcwFromDropView(self), kComDropMethodDrop,
                               pDataObj, grfKeyState, pt, pdwEffect);
}

// DragLeave 保持 stage-15 的 S_OK 语义（无参数、无回调参数可转）。注册回调
// 不改变 DragLeave 的返回值（与 stage-15 契约一致，避免回归）。
int32_t IL2CPP_COM_CALL CcwDropDragLeave(void* self)
{
    (void)self;
    return kComHrOk;
}
```

**设计决策（DragLeave 不转发）**：DragLeave 已在 stage-15 返回 S_OK 且被测试锁定（test_com.cpp:2867 "DragLeave (slot 5) returns S_OK"）。把它也转发到回调（method 3）会把"通知"变成"可能失败的调用"（回调可返回非 S_OK），改变已锁定的契约。保持 S_OK = 最小变更、零回归。托管侧 OnDragLeave 的通知是未来 WinForms 层在回调内自己编排的事（kComDropMethodDragLeave 常量保留供未来扩展）。若评审认为必须转发 DragLeave，替代：回调返回非 S_OK 时仍返回 S_OK（掩码），但那是"观测性转发"，增加复杂度无验证收益 —— 不推荐。

**⑤ vtable 组装更新**（`kDropVtable`，com.cpp:1187-1195）：

```cpp
const Il2CppComDropTargetVtable kDropVtable = {
    &CcwDropQueryInterface,
    &CcwDropAddRef,
    &CcwDropRelease,
    &CcwDropDragEnter,  // slot 3 DragEnter（转发）
    &CcwDropDragOver,   // slot 4 DragOver（转发）
    &CcwDropDragLeave,  // slot 5 DragLeave（S_OK）
    &CcwDropDrop,       // slot 6 Drop（转发）
};
```

**⑥ icall 实现**：

```cpp
int32_t icall_com_ccw_register_drop_dispatch(intptr_t ccwPtr, intptr_t fn)
{
    if (ccwPtr == 0)
    {
        il2cpp_throw_argument_null_exception("ccw");
    }
#if defined(_WIN32)
    auto* ccw = reinterpret_cast<Il2CppComCcw*>(ccwPtr);
    ccw->dropDispatch = reinterpret_cast<void*>(fn); // fn == 0 清除（回落 stage-15 面）
    return kComHrOk;
#else
    (void)ccwPtr;
    (void)fn;
    return kComHrNotImpl;
#endif
}
```

**⑦ POSIX stub**：`#else` 分支返回 `kComHrNotImpl`（netcore Marshal.Unix.cs 契约——平台限制，非静默）。null 守卫（ccw == 0 → ArgumentNullException）位于平台门之前（stage-13 BSTR 先例，所有平台一致）。

### 2.4 与未来托管侧的关系（为什么这是正确的转发层）

最终 WinForms 派发的形状：CCW 视图 → 托管 `OnDragEnter(DragEventArgs)`。stage 16 的转发钩子是这个链路的**转发段**，未来托管侧绑定方式（无需改 com.cpp 视图）：

- WinForms 层把处理逻辑做成一个**单播 delegate**，经 delegate → native thunk 绑定（`il2cpp_pinvoke_cb_bind_wndproc` 先例，pinvokecb.cpp:202 —— 该绑定把 delegate 的 methodPtr 包进固定池 thunk，返回 native 地址；需新开一个 drop 签名族 thunk，签名 `int32_t (*)(int32_t, intptr_t, uint32_t, int32_t, int32_t, uint32_t*)`），把 thunk 地址经 `icall_com_ccw_register_drop_dispatch(ccw, thunk)` 注册。
- thunk 内（托管代码）：`pDataObj` 经 typed-RCW icall（未来基建）包装为 `IDataObject`，`x/y/keyState` 直接喂 `DragEventArgs` 构造函数，构造后调 `OnDragEnter`，再把 `e.Effect` 写回 `*pdwEffect`。
- stage 16 不需要 MethodMap 改动：`icall_com_ccw_register_drop_dispatch` 是运行时 icall（如 `icall_com_ccw_fire_event`），托管侧经内部 helper 调用；`RegisterDragDrop`/`RevokeDragDrop` 已是 P/Invoke 路径（stage-15 已确认无 MethodMap 依赖）。

### 2.5 测试断言清单（test_com.cpp section 18，Windows 块）

**前置**：无需窗口/线程/OLE 注册（直接驱动视图 vtable）。复用 stage-15 的 `TestSink dropDataObj`（一个最小 IUnknown stub 作为按值 IDataObject* 载体）与 `MakeManagedTarget()`。定义一个纯 C++ 派发回调：

```cpp
// 记录派发回调收到的原始 OLE 参数 + 返回可控 HRESULT + 写回可控 effect。
struct TestDropRecord
{
    int32_t  calls;
    int32_t  lastMethod;
    intptr_t lastData;
    uint32_t lastKey;
    int32_t  lastX;
    int32_t  lastY;
    uint32_t effectWrite;   // 回调写回 *pdwEffect 的值
    int32_t  returnHr;      // 回调返回的 HRESULT
};
int32_t TEST_COM_CALL TestDropDispatch(int32_t method, intptr_t pDataObj,
                                       uint32_t grfKeyState, int32_t x, int32_t y,
                                       uint32_t* pdwEffect)
{
    auto* r = static_cast<TestDropRecord*>(/* 全局或传入载体 */);
    ++r->calls; r->lastMethod = method; r->lastData = pDataObj;
    r->lastKey = grfKeyState; r->lastX = x; r->lastY = y;
    if (pdwEffect != nullptr) { *pdwEffect = r->effectWrite; }
    return r->returnHr;
}
```

（回调是无状态裸函数，需把记录放全局/静态 `TestDropRecord` —— test_com 已有全局断言计数先例。）

断言清单（预计 12-15 条，Windows `#if defined(_WIN32)` 块，section 17 之后）：

1. **注册**：`icall_com_ccw_register_drop_dispatch(dropCcw, (intptr_t)&TestDropDispatch)` → S_OK。
2. **null 守卫**：`icall_com_ccw_register_drop_dispatch(0, fn)` → ArgumentNullException。
3. **清除**：`icall_com_ccw_register_drop_dispatch(dropCcw, 0)` → S_OK（清除合法）；重新注册后继续测试。
4. **DragEnter 转发**（视图 vtable slot 3，经 `icall_com_call_vtable_slot(dropView, kComDropSlotDragEnter, dropData, keyState, ptBits, &effect)`）：回调收到 `method==kComDropMethodDragEnter`、`pDataObj==dropData`（字节精确）、`grfKeyState==0xABCD`、`x/y` 与 ptBits 解码一致。
5. **按值 POINTL 解码字节精确**：`ptBits = ((intptr_t)(uint32_t)0x55667788 << 32) | 0x11223344` → 回调收到 `lastX==0x11223344`、`lastY==0x55667788`（判别子 —— 若按指针解引用会崩溃，若高低位错位则值不符）。
6. **effect 写回传播**：回调 `effectWrite=0x2`（DROPEFFECT_MOVE），`&effect` 初始 0x1 → 视图返回后 `effect==0x2`（调用方看到回调的写回）。
7. **HRESULT 传播**：回调 `returnHr=kComHrSFALSE` → 视图方法返回 `kComHrSFALSE`（非 S_OK 的 HRESULT 不被掩码）。
8. **DragOver 转发**（slot 4，`a0=keyState, a1=ptBits, a2=&effect`）：回调收到 `method==kComDropMethodDragOver`、`pDataObj==0`（DragOver 无数据对象）、x/y/keyState 正确。
9. **Drop 转发**（slot 6）：回调收到 `method==kComDropMethodDrop`、`pDataObj==dropData`、x/y/keyState 正确、effect 写回传播。
10. **DragLeave 不回归**（slot 5）：注册回调后仍返回 S_OK（stage-15 契约锁定）。
11. **清除回落**：`register_drop_dispatch(dropCcw, 0)` → DragEnter slot 3 返回 E_NOTIMPL（stage-15 面恢复）；DragOver/Drop 同理。
12. **双 CCW 隔离**：第二个 CCW `dropCcw2`（不注册）的 DragEnter → E_NOTIMPL；第一个 CCW（已注册）仍转发 —— 回调是每 CCW 状态。
13. **OOB 守卫不回归**：`icall_com_call_vtable_slot(dropView, 7, ...)` → ArgumentOutOfRangeException（7-slot 守卫仍锁）。
14. **refcount 不回归**：注册/清除不影响 CCW refcount（AddRef/Release 计数不变）。

**POSIX 块**（`#else`，4k）：
15. `icall_com_ccw_register_drop_dispatch(0x1, 0x1)` → E_NOTIMPL。
16. `icall_com_ccw_register_drop_dispatch(0, 0x1)` → ArgumentNullException（null 守卫先于平台门）。

**断言计数**：约 14-16 条新增（Windows）+ 2 条 POSIX（428 → ~445）。

### 2.6 测试验证流程（stage-15 基线）

```
cmake --build E:\il2cpp\build\runtime_full -j 16          → 0 errors（C0）
test_com.exe                                               → all PASS（断言 ~445）
all test_*.exe                                             → 139/139（无回归）
unit tests                                                 → 1807/1808（batch 188 已知 1 个 pre-existing LOUD 失败，不动）
```

---

## 3. 替代方案评估

| 方案 | 可行性 | 价值 | 推荐 |
|---|---|---|---|
| **IDropTarget 派发转发钩子**（注册回调 + 原始参数转发） | ✅ 高（纯 native，无窗口/线程依赖，无头全验证） | 中高（把 E_NOTIMPL 换成真实转发层，effect/HRESULT 双向传播可验证；为托管侧提供绑定点） | **推荐 stage 16** |
| 完整托管派发（DragEventArgs + typed RCW + interface_dispatch） | ❌ 低（3/4 步骤依赖 WinForms 域转译注册 + 新运行时基建；无头无法验证 dispatch 路径） | 高 | stage 17+ 远期（需 WinForms 域 + typed-RCW 基建先行） |
| GetComInterfaceForObject 允许 IDropTarget IID（stage-15b） | ✅ 高 | 低-中（让托管侧 `GetComInterfaceForObject(o, typeof(IDropTarget))` 取视图） | **可并入 stage 16 的 15b 项**（独立交付，不阻塞核心） |
| 仅把 E_NOTIMPL 换成"记录"icall（无回调，只记录参数） | ✅ 高 | 低（只验证参数捕获，无双向传播、无托管侧绑定点） | 不推荐（转发回调覆盖其全部价值） |

**为什么转发钩子最可行**：
- 完全复用既有模式：CCW 每对象注册（register_connection_point 先例）、裸函数指针存储/调用（pinvokecb thunk 池先例）、按值聚合整数载体（stage-12 int64 先例）。
- **无窗口/线程/OLE 依赖**：直接驱动视图 vtable，比 stage-15 的 DropRegThread（OleInitialize STA + 自建窗口）简单一个量级。
- 向后兼容：未注册回落 stage-15 面，428 断言零回归。
- 为 stage 17+ 托管派发提供**转发段**：未来托管侧只需把 delegate thunk 地址注册进来，DragEventArgs/typed RCW 构造全在托管代码完成。

---

## 4. 工作量 + 风险

| 项 | 工作量 | 风险 |
|---|---|---|
| `kComDropMethod*` 常量 + icall 声明（il2cpp_com.h） | 低（~35 行含注释） | 低 |
| vtable `pt` 参数修正（const void* → intptr_t）+ `dropDispatch` 成员 + ctor 初始化 | 低（~10 行） | 低-中（修正按值 POINTL 声明 —— stage-15 未暴露，改动在视图函数指针类型，需确认 kDropVtable 全槽签名同步） |
| 视图方法转发实现（CcwDropDispatchImpl + 3 方法）+ vtable 组装 | 中（~60 行） | 低（纯内存 + 整数解码，无 OLE 调用） |
| `icall_com_ccw_register_drop_dispatch` + POSIX stub | 低（~30 行） | 低 |
| test_com.cpp section 18 + POSIX 4k | 中（~90 行，14-16 断言） | 中（回调记录载体 + 按值 POINTL 判别子；无窗口/线程复杂度） |
| **可选 15b**：GetComInterfaceForObject 允许 IDropTarget IID | 中（改 com.cpp:2360 检查 + StackFold.cs:2662 常量 Type 解析扩展） | 中（改 stage-14 契约，需 MethodMap 回归） |
| **合计（核心）** | **中（~225 行）** | **低** |

**主要风险**：
1. **按值 POINTL 声明修正**（中）：stage-15 的 `const void* pt` 是 ABI 槽位匹配但位型错误的声明。改动影响 `Il2CppComDropTargetVtable` 三个函数指针类型 + kDropVtable 组装 + CcwDropNotImpl5/4 签名。判别子测试（ptBits 高/低 32 位独立值）锁定解码正确性；若 x86 也需支持，`IL2CPP_COM_CALL`（__stdcall）下按值 POINTL 在栈上仍以 8 字节两槽传递，`intptr_t` 载体在 x86 只有 4 字节会截断 —— **ILC 目标 x64**（pinvokecb.cpp:48 static_assert 已锁），文档需明确 x64-only。
2. **回调签名 ABI**（低）：6 参数裸函数指针，x64 统一约定；测试回调用 `TEST_COM_CALL` 与 IL2CPP_COM_CALL 对齐（test_com.cpp:117 先例）。
3. **测试回调无状态**（低）：裸函数不能捕获 lambda；用全局 `TestDropRecord`（test_com 已有全局断言计数先例），逐断言重设记录。
4. **零回归**（低）：DragLeave S_OK 与 OOB 守卫是 stage-15 已锁契约，测试显式重申；未注册回落保证旧断言不受影响。

**验证**：
- `cmake --build E:\il2cpp\build\runtime_full -j 16` → 0 errors（C0）
- `test_com.exe` → all PASS（断言 428 → ~445）
- `all test_*.exe` → 139/139

---

## 5. 最终推荐

**stage 16 = IDropTarget 派发转发钩子**（注册回调 + 原始 OLE 参数转发）：

1. il2cpp_com.h：`kComDropMethod*` 常量 + `icall_com_ccw_register_drop_dispatch` 声明。
2. com.cpp：vtable `pt` 参数修正（const void* → intptr_t）+ CCW `dropDispatch` 成员 + ctor 初始化 + 转发方法实现（CcwDropDispatchImpl + DragEnter/DragOver/Drop，未注册回落 E_NOTIMPL；DragLeave 保持 S_OK）+ kDropVtable 组装更新 + icall + POSIX stub。
3. test_com.cpp section 18（Windows）+ 4k（POSIX）。
4. 无 MethodMap 改动（P/Invoke 路径；icall 经运行时内部 helper）。
5. 为 stage 17+ 完整托管派发提供转发段（托管侧经 delegate thunk 绑定，DragEventArgs/typed RCW 在托管代码构造）。

**可选 stage 16b**（独立交付）：扩展 `icall_com_get_com_interface_for_object` 允许 IDropTarget IID（+ StackFold.cs:2662 常量 Type 解析），使托管侧经 `GetComInterfaceForObject(o, typeof(IDropTarget))` 获取视图。

**完整托管派发**（DragEventArgs 构造 + typed IDataObject RCW + `il2cpp_interface_dispatch` 派发到 WinForms Control）**降级为 stage 17+**，前置依赖：(a) WinForms 域转译注册（Control/IDropTarget/DragEventArgs 类型存在且带接口映射）、(b) typed-RCW 基建、(c) 托管 ctor 调用机制 —— 三者均非 COM 阶段自包含工作。
