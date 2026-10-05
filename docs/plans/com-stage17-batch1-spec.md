# COM Stage 17 批次 1 规格 — 真实 WinForms 最小类型转译注册（IDropTarget 形状 + slotMap + dispatch 无头验证）

- 日期：2026-08-16
- 性质：审计子代理产出（纯研究，无代码改动）
- 上游：stage 16 审计结论 "完整托管派发（OnDragEnter(DragEventArgs)）3 前置条件：① WinForms 类型转译注册 ② typed-RCW 基建 ③ 托管 ctor 调用机制 —— 全部非 COM 自包含，分 3 批"；Phase 45 收口后定路线：批次 1 = 真实 WinForms 最小类型转译注册（Control + DragEventArgs + IDropTarget 接口描述符 + slotMap），解锁 typed-RCW 验证与 dispatch 无头验证
- 基线：HEAD 8672320（Phase 45 TabControl，winforms1 778 行 byte-exact，test_com 450 断言，runtime 139/139）

---

## 0. 结论摘要

| 项 | 结论 |
|---|---|
| 转译器接口机制现状 | **已完整就绪**：VTablePlanner.BuildInterfaceMaps（VTablePlanner.cs:94-125）计算 `InterfaceSlotMaps[iface][ifaceSlot]=vtableSlot`（SigKey=Name+ParamTypes 匹配，含继承接口）；TypeEmitter.EmitInterfaceMaps（TypeEmitter.cs:621-648）发射 `int32_t {Type}_iface_{Iface}_map[]` + `InterfaceMapEntry {Type}_interfaces[]`；EmitClassDescriptor（TypeEmitter.cs:577-609）wire 接口描述符（instanceSize=0, vtable=null, vtableSize=VirtualMethods.Count）与具体类描述符（vtable + interfaces） |
| 运行时接口机制现状 | **已完整就绪**：`Il2CppClass.interfaces`（il2cpp_metadata.h:56）+ `InterfaceMapEntry{iface, slotMap}`（il2cpp_metadata.h:61-65）；`il2cpp_interface_dispatch`（metadata.cpp:359-405）线性扫描 + slotMap 解析 + 返回 `&klass->vtable[typeSlot]`；`il2cpp_class_register`（metadata.cpp:290）幂等；`il2cpp_object_new`（object.cpp:36）分配 |
| e2e 先例 | **tests/cases/interface（ICalc/IMul + Doubler/Tripler/Multi/Inherited/DerivedOnly）已证明"转译 C# 接口 → 接口描述符 + slotMap → callvirt 经 dispatch 解析"整条链路可用**（但全部为 int 参数/返回签名） |
| 缺口 | test_com.cpp（450 断言）**只链 il2cpp_runtime、只造 string 对象**（MakeManagedTarget = string），无法引用任何转译类型；dispatch 无头验证需转译的接口实现类进测试进程 |
| **推荐批次 1** | **方案 B-lite（真 WinForms IDropTarget 最小形状）**：新 fixture `tests/cases/com17type/` 定义 `DragDropEffects` 枚举 + 最小 `DragEventArgs`（X/Y/Effect + ctor）+ `IDropTarget` 接口（OnDragEnter/OnDragOver/OnDragLeave/OnDragDrop 4 方法，DragEventArgs 参数）+ 最小 Control 形状类 `WfDropSource : IDropTarget`（含 2 个实现者）；转译产物提交（gen/ 先例）；链入 test_com；section 19 直接调 `il2cpp_interface_dispatch` 断言方法被调 + 参数/返回值正确 |
| 转译器改动 | **0**（机制已存在；无需改 CodeGenerator/VTablePlanner/TypeEmitter） |
| runtime 改动 | **0**（dispatch 机制已存在；CCW 已接受任意托管对象 com.cpp:2681 + 强 GCHandle com.cpp:2721） |
| 测试改动 | test_com.cpp section 19（~15-20 断言）+ tests/runtime/CMakeLists.txt 把 com17type 生成源接入 test_com 目标 |
| 关键设计约束 | ① 接口方法用**隐式实现**（显式实现 SigKey 含接口名前缀会匹配失败）；② fixture 的 cctor/Main 必须无窗口无线程（`il2cpp_runtime_init` 会跑 cctor）；③ 测试对象经 CCW 的 GCHandle 根定（native 栈不是 GC 根）；④ 观察字段用**实例字段**（避免静态根接线）；⑤ com17type fixture 纯逻辑、无 P/Invoke → 生成代码三平台（MinGW/MSVC/WSL）可编译 |

---

## 1. 现状分析

### 1.1 转译器类型发射（slotMap/接口映射，全部已就绪）

| 环节 | 位置 | 现状 |
|---|---|---|
| 接口方法 → 类的 vtable 槽 | VTablePlanner.cs:94-125 BuildInterfaceMaps | `slotMap[islot] = t.VirtualMethods` 中 SigKey 匹配的槽；own interfaces 优先（re-implementation wins），再遍历 baseClass 链（audit H7 继承接口） |
| 隐式接口实现 → pseudo-virtual | VTablePlanner.cs:152-160 Plan | `interfaceSigs`（接口方法签名集）把未声明 virtual 的实现方法纳入 slotCandidates → 进 vtable |
| 槽布局（override 语义） | VTablePlanner.cs:127-183 Plan | base 槽深拷贝 + 签名匹配覆盖（ECMA-335 II.9.3） |
| 接口描述符发射 | CodeGenerator.cs:457-460 + :571-578 | `extern Il2CppClass {iface}_class;` + 描述符（instanceSize=0, vtable=nullptr, vtableSize=VirtualMethods.Count, interfaces=nullptr） |
| 具体类 vtable 发射 | CodeGenerator.cs:547-552 + TypeEmitter.cs:663-678 | `void* {Type}_vtable[] = { reinterpret_cast<void*>(&{MethodCppName}), ... }`（slot i = effective virtual） |
| 接口映射发射 | CodeGenerator.cs:558-564 + TypeEmitter.cs:621-648 | `int32_t {Type}_iface_{Iface}_map[]` + `InterfaceMapEntry {Type}_interfaces[]` |
| 接口 callvirt 发射 | CodeGenerator.cs:3478-3485 | `*il2cpp_interface_dispatch(obj, &{iface}_class, ifaceSlot)` → cast 函数指针 → call |
| 单实现者 devirt（S3） | CodeGenerator.cs:3561-3574 | 仅影响生成代码 callvirt 直调；test_com 直接调 dispatch 不受影响 |

### 1.2 运行时接口派发（全部已就绪）

- `Il2CppClass`（il2cpp_metadata.h:34-58）+ `InterfaceMapEntry{iface, slotMap}`（il2cpp_metadata.h:61-65）。
- `il2cpp_interface_dispatch(obj, iface, ifaceSlot)`（metadata.cpp:359-405）：线性扫描 `obj->klass->interfaces` + baseClass 链，`entry.slotMap[ifaceSlot]` → vtable 槽，返回 `&klass->vtable[typeSlot]`（`void**`）；槽越界/缺失/未实现 → abort（编译器保证 call site 正确，编程错误）。
- 注册：`il2cpp_class_register`（metadata.cpp:290-310，幂等，registry ≤ kMaxRegisteredClasses）。
- 分配：`il2cpp_object_new(Il2CppClass*)`（object.cpp:36）→ GC 分配 + klass 戳。
- CCW：`icall_com_ccw_create(Il2CppObject*)`（com.cpp:2681）接受**任意托管对象**（不限 string）+ 强 GCHandle（com.cpp:2721）；`icall_gchandle_get_target`（gchandle.cpp:206）恢复对象 —— 这是批次 3 CCW→dispatch 桥的现成底座。
- C++ 侧 API 全在 `il2cpp_runtime.h`（il2cpp_interface_dispatch:101、il2cpp_object_new:56、il2cpp_class_register:58）—— test_com.cpp 可直接调用。

### 1.3 缺口（本批次要补的）

1. **test_com 无转译类型可达**：test_com.cpp 只链 il2cpp_runtime（tests/runtime/CMakeLists.txt，GLOB test_*.cpp），`MakeManagedTarget()` = string（test_com.cpp:323）。`il2cpp_interface_dispatch` 对 string 走 abort 分支（metadata.cpp:399）—— dispatch 无头验证不可行，直到有转译的接口实现类对象。
2. **"真实 WinForms 形状"未覆盖**：现有 interface case 全 int 签名；`OnDragEnter(DragEventArgs)` 的 **r:（引用类型）参数 + void 返回** 的接口派发未在任何 e2e/运行时测试中覆盖。
3. **类型转译注册的"产物"未进运行时测试**：winforms1 的 WfControl 等已转译（gen/ 提交），但无 IDropTarget 形状、无接口映射、且不进 test_com。

---

## 2. 方案 A vs 方案 B

### 方案 A：自定义最小接口实现类（机制验证）

```csharp
public interface IDropMin { int Compute(int a, int b); }
public class TestDropSource : IDropMin {
    public int Compute(int a, int b) => a * 100 + b;
}
```

- 工作量：~20 行 fixture；转译器 0；runtime 0。
- 覆盖：接口描述符发射 + slotMap + dispatch 解析 + int 参数/返回。
- **局限**：① 与现有 interface case 机制重复（ICalc/IMul 已证明），增量价值低；② 不覆盖 r: 参数（DragEventArgs）、void 返回、多方法、枚举 —— 批次 2/3 需要的形状全缺；③ "真实 WinForms" 无体现。

### 方案 B：真 WinForms IDropTarget 形状（最小规模）

```csharp
public enum DragDropEffects { None = 0, Copy = 1, Move = 2, Link = 4 }

public class DragEventArgs {
    public int X; public int Y; public DragDropEffects Effect;
    public DragEventArgs(int x, int y, DragDropEffects effect) { X = x; Y = y; Effect = effect; }
}

public interface IDropTarget {
    void OnDragEnter(DragEventArgs e);
    void OnDragOver(DragEventArgs e);
    void OnDragLeave(DragEventArgs e);
    void OnDragDrop(DragEventArgs e);
}

public class WfDropSource : IDropTarget {
    public int EnterCount; public int OverCount; public int LeaveCount; public int DropCount;
    public int LastX; public int LastY; public int LastEffect;
    public void OnDragEnter(DragEventArgs e) { EnterCount++; LastX = e.X; LastY = e.Y; LastEffect = (int)e.Effect; }
    public void OnDragOver(DragEventArgs e) { OverCount++; }
    public void OnDragLeave(DragEventArgs e) { LeaveCount++; }
    public void OnDragDrop(DragEventArgs e) { DropCount++; LastX = e.X; LastY = e.Y; LastEffect = (int)e.Effect; }
}
public class WfDropSource2 : IDropTarget { /* 第二个实现者：防 devirt S3 + 隔离断言 */ ... }
```

- 工作量：~70-90 行 fixture；转译器 0；runtime 0。
- 覆盖：**r: 参数接口派发（首次）+ void 返回 + 多方法（4 槽）+ 枚举字段 + 对象字段（观察计数）** —— 正是批次 3 CCW→OnDragEnter 桥的精确形状。
- 直接解锁：批次 2（typed-RCW 需接口描述符——IDropTarget_class 已在）；批次 3（DragEventArgs 已在、IDropTarget 描述符已在，只需托管 ctor 调用机制 + CCW→dispatch 桥）。
- 风险：比 A 多 ~60 行 fixture + 1 个额外转译类型（DragEventArgs）+ 1 枚举；**机制面完全同 A（无新转译器/运行时改动）**。

### "真实 WinForms 类型"的定义（关键判断）

本项目**不直接转译 System.Windows.Forms.dll**（转译器加载用户程序集；winforms1 的 WfControl/WfTabControl 等即 fixture 自写的 WinForms-mirror 类，P/Invoke 真 user32/comctl32）。故"真实 WinForms 类型"= fixture 自写的、**镜像真实 WinForms 契约形状**的类。批次 1 取 IDropTarget 契约形状（4 方法 + DragEventArgs）为"真实"面；"Control 全量（WfControl 等）"是后续批次（现 winforms1 已转译，但无 IDropTarget 形状）。

---

## 3. 推荐 + 理由

**批次 1 = 方案 B（真 WinForms IDropTarget 最小形状，即"方案 B-lite"）。**

1. **机制已被 interface case 证明**，再"验证机制"（方案 A）增量价值低；批次的增量价值是"真实形状 + 无头 dispatch"。
2. **r: 参数接口派发是未覆盖面**：现有 interface case 全 int；而 `OnDragEnter(DragEventArgs)`（Il2CppObject* 槽签名）正是批次 3 的桥形状 —— 现在测，批次 3 无意外。
3. **增量成本极小**（~70 行 fixture + 1 个测试 section + 提交生成物），远小于"真 Control 全量"（那是后续批次，且 winforms1 已有 Control 面）。
4. **风险护栏**：若转译 r: 接口方法遇到意外（极小概率，VirtualParamTypesCpp 已处理 r: 且 WfControl 实例方法已用 Il2CppObject* 参数），可退回方案 A（fixture 形状几乎不变，只换签名）。

---

## 4. 完整规格

### 4.1 面 1：C# fixture（新目录 `tests/cases/com17type/`，~70-90 行）

`com17type.cs` + `com17type.csproj`（net48，与其他 case 一致；不引用 System.Windows.Forms）：

- `DragDropEffects` 枚举（None=0/Copy=1/Move=2/Link=4 —— net48 真实值）。
- `DragEventArgs`：公开实例字段 X/Y（int）+ Effect（DragDropEffects）+ 简单 ctor（`(int,int,DragDropEffects)`）。**批次 1 不含 IDataObject 字段**（typed-RCW 批次再加）。
- `IDropTarget` 接口：OnDragEnter/OnDragOver/OnDragLeave/OnDragDrop，各 `(DragEventArgs)`，void 返回。
- `WfDropSource : IDropTarget`：实现 4 方法（**隐式实现**：`public void OnDragEnter(...)`），写入实例观察字段（EnterCount/OverCount/LeaveCount/DropCount/LastX/LastY/LastEffect）。
- `WfDropSource2 : IDropTarget`：第二个实现者（不同计数值）——防 devirt S3 干扰 e2e 侧 callvirt + 测试双实现者隔离。
- `Cases.Com17type.Main`：空或打印 PASS 行（供 standalone e2e；test_com 用 `il2cpp_runtime_init()` 不跑 Main）。
- **约束**：无窗口、无线程、无 P/Invoke、无静态初始化器（cctor 保持无副作用，`il2cpp_runtime_init` 会跑 cctor）。

### 4.2 面 2：转译器（src/il2cppc/）—— 0 改动

- 机制已存在（§1.1）。无 CodeGenerator/VTablePlanner/TypeEmitter 改动。
- 生成命令（run-tests.ps1:102 同流程）：
  ```
  dotnet build tests/cases/com17type/com17type.csproj -c Release -o tests/cases/com17type/out
  dotnet run --project src/il2cppc -- tests/cases/com17type/out/com17type.exe <genOut>
  ```
- **产物提交**（gen/ 先例）：`<genOut>/src/*.cpp`、`<genOut>/include/internal/generated.h`、`<genOut>/CMakeLists.txt`。提交路径建议 `gen_com17type/`（不混入根 gen/，根 gen/ 是 winforms1 专属 + kernel32 等 shim）。
- 关键生成物形态（可预期）：
  ```cpp
  // <genOut>/include/internal/generated.h
  extern Il2CppClass IDropTarget_class;      // 接口描述符（instanceSize=0, vtableSize=4, interfaces=nullptr）
  extern Il2CppClass WfDropSource_class;      // 具体类（vtable + interfaces 已 wire）
  extern Il2CppClass DragEventArgs_class;
  extern InterfaceMapEntry WfDropSource_interfaces[];   // {{&IDropTarget_class, WfDropSource_iface_IDropTarget_map}}
  extern int32_t WfDropSource_iface_IDropTarget_map[];  // { enterSlot, overSlot, leaveSlot, dropSlot }
  void WfDropSource__ctor_<hash>(Il2CppObject*);
  void WfDropSource_OnDragEnter_<hash>(Il2CppObject*, Il2CppObject*);  // (this, DragEventArgs*)
  ```

### 4.3 面 3：runtime（src/runtime/）—— 0 改动

- dispatch 机制已存在（§1.2）。无 metadata.cpp/com.cpp 改动。
- 可选（不推荐）：无新 icall；不扩展 CCW 视图（那是批次 2/3）。

### 4.4 面 4：测试

#### 4.4.1 tests/runtime/CMakeLists.txt（~10 行）

把 com17type 生成源接入 test_com 目标（**不改 GLOB**；其他 test_*.exe 不链生成代码）：

```cmake
set(COM17TYPE_GEN <repo>/gen_com17type)
add_executable(test_com
    test_com.cpp
    ${COM17TYPE_GEN}/src/IDropTarget.cpp
    ${COM17TYPE_GEN}/src/DragEventArgs.cpp
    ${COM17TYPE_GEN}/src/DragDropEffects.cpp
    ${COM17TYPE_GEN}/src/WfDropSource.cpp
    ${COM17TYPE_GEN}/src/WfDropSource2.cpp
    ${COM17TYPE_GEN}/src/Cases_Com17type.cpp
    ${COM17TYPE_GEN}/src/entry.cpp
)
target_include_directories(test_com PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/../../src/runtime/include/internal
    ${COM17TYPE_GEN}/include/internal
)
```

（注意：生成的 CMakeLists 源文件清单以实际转译输出为准；接口/枚举类型有独立 .cpp。entry.cpp 的 `il2cpp_runtime_init` 定义 `il2cpp_entry_main`/`il2cpp_runtime_init` 两个导出符号，test_com 的 main 不与它们冲突。）

#### 4.4.2 test_com.cpp section 19（~100-130 行，15-20 断言）

**位置**：放**全平台块**（接口派发 + CCW 全平台可用；com17type 纯逻辑生成代码三平台可编译）。在 section 16/18（drop dispatch）之后、`#if defined(_WIN32)` 之前。

**前置**：`#include "generated.h"`（com17type 的生成头）；测试声明 `extern void il2cpp_runtime_init();`（entry.cpp 的 C++ 链接导出；若 generated.h 已声明则直接用）。

断言清单：

1. **注册**：调用 `il2cpp_runtime_init()`（真实注册路径：descriptor + typeinfo + 静态根 + 无副作用 cctor）→ 不抛。断言 `il2cpp::il2cpp_class_by_name("IDropTarget") == &IDropTarget_class`、`il2cpp_class_by_name("WfDropSource") == &WfDropSource_class`。
2. **接口描述符形状**：`IDropTarget_class.vtableSize == 4`（4 方法槽）、`IDropTarget_class.instanceSize == 0`、`interfaceCount == 0`。
3. **具体类接口表**：`WfDropSource_class.interfaceCount == 1` 且 `WfDropSource_class.interfaces[0].iface == &IDropTarget_class`；`WfDropSource_class.interfaces[0].slotMap[0..3]` 都在 `[0, vtableSize)`。
4. **对象构造**：`il2cpp_object_new(&WfDropSource_class)` + `WfDropSource__ctor_<hash>(obj)` → `obj->klass == &WfDropSource_class`。
5. **OnDragEnter 派发（参数精确）**：造 `DragEventArgs`（`il2cpp_object_new(&DragEventArgs_class)` + ctor 或直接置字段 `e->X=0x1122; e->Y=0x3344; e->Effect=2`）；`void** s = il2cpp_interface_dispatch(obj, &IDropTarget_class, 0)`（ifaceSlot 0 = OnDragEnter，声明序）；cast `((void(*)(Il2CppObject*, Il2CppObject*))s)`；调 `(obj, e)`；断言 `obj->EnterCount==1`、`obj->LastX==0x1122`、`obj->LastY==0x3344`、`obj->LastEffect==2`（**方法被调 + 参数/字段读取正确**）。
6. **OnDragOver（槽 1）**：调后 `OverCount==1`。
7. **OnDragLeave（槽 2）**：调后 `LeaveCount==1`。
8. **OnDragDrop（槽 3）+ 返回/副作用**：换新 e（X/Y/Effect 判别子），调后 `DropCount==1` 且 `LastX/LastY/LastEffect` 更新。
9. **slotMap 判别子**：OnDragEnter 与 OnDragDrop 的 ifaceSlot 不同（0 vs 3）且解析到**不同** vtable 槽 —— 断言 `sEnter != sDrop`（指针不等）。
10. **双实现者隔离**：`il2cpp_object_new(&WfDropSource2_class)` 的对象 dispatch slot 0 → 其 EnterCount 递增、WfDropSource 对象不受影响（每对象状态）。
11. **继承接口（audit H7，可选）**：若 fixture 加 `class WfDropSourceSub : WfDropSource {}`，dispatch 经 baseClass 链命中（与 tests/cases/interface 的 DerivedOnly 同款）。
12. **CCW 桥前奏（批次 3 底座）**：`icall_com_ccw_create(obj)` → CCW 非 0；`icall_com_is_com_object(obj) == false`（普通托管对象非 RCW）；经 CCW GCHandle 目标恢复 == obj（`icall_gchandle_get_target` / CCW 内部句柄）。**这也根定对象**（native 栈非 GC 根；GC 自动触发时会收集未根定对象）。
13. **CCW 隔离**：两个对象各自 CCW，dispatch 互不影响。
14. **CCW 释放**：`icall_com_release(ccw) == 0` 销毁（refcount 平衡）。
15. **未实现接口 abort 不回归（不直接断言 abort）**：`il2cpp_interface_dispatch(MakeManagedTarget(), &IDropTarget_class, 0)` 会 abort —— 不在测试中触发；文档标注"编程错误路径"。
16. **OOB 槽（不触发 abort 的前提）**：ifaceSlot 0..3 均解析；越界 ifaceSlot（如 7）触发 metadata.cpp abort —— 不测（标注）。

**POSIX/全平台**：section 19 不 gate（全平台块）；若需 POSIX 块 4l：断言 class_by_name 注册同样成功（生成代码无平台差异）。

**断言计数**：约 15-20 条新增（test_com 450 → ~470）。

### 4.5 顺序

1. **fixture**：`tests/cases/com17type/`（com17type.cs + csproj）→ dotnet build → **net48 参考运行**确认可执行（standalone 可 e2e 对比）。
2. **转译**：il2cppc → `<genOut>/` → **提交生成物**（gen_com17type/）。
3. **三平台编译**：用生成的 CMakeLists standalone 构建（MinGW/MSVC/WSL）→ `il2cpp_gen_exe` 跑通（与 run-tests.ps1 同流程，比对 net48 oracle 0 diff —— 该 case 加入 e2e 套件，run-tests.ps1 自动 GLOB）。
4. **测试接线**：tests/runtime/CMakeLists.txt 把生成源接入 test_com。
5. **section 19 断言**：写断言 → 构建 test_com → 跑 → 全绿。
6. **全量验证**：cmake build runtime_full + test_com + **139/139** + **unit 1807** + e2e（含新 com17type case）0 diff。

### 4.6 验证命令（C0）

```
cmake --build E:\il2cpp\build\runtime_full -j 16            # 0 errors
E:\il2cpp\build\runtime_full\test_com.exe                   # all PASS（断言 ~470）
# 全量 139 test_*.exe                                        # 139/139 无回归
dotnet test E:\il2cpp\tests\unit\unit.csproj                # 1807
pwsh scripts/run-tests.ps1 -Compiler mingw                   # e2e 含 com17type，0 diff
```

---

## 5. 工作量 + 风险

| 项 | 工作量 | 风险 |
|---|---|---|
| fixture com17type.cs + csproj | 低（~70-90 行） | 低（纯逻辑；net48 编译 + net48 参考运行确定性高） |
| 转译 + 提交生成物（gen_com17type/） | 低（生成 ~8-10 个 .cpp + generated.h） | 低（il2cppc 已成熟；interface case 先例） |
| tests/runtime/CMakeLists.txt 接线 | 低（~10 行） | 低（不改 GLOB；其他 test 不受影响） |
| test_com.cpp section 19 | 中（~100-130 行，15-20 断言） | 中（r: 参数接口派发首次直接测试；判别子测试锁定） |
| standalone e2e（com17type 入 e2e 套件） | 低 | 低（run-tests.ps1 自动 GLOB；net48 oracle 对比） |
| 全量验证（runtime_full/139/unit/e2e） | 中 | 低 |
| **合计** | **中（~200-230 行 + 提交生成物）** | **低-中** |

**主要风险**：

1. **r: 参数接口派发首次覆盖（中）**：现有 interface case 全 int；`(Il2CppObject* this, Il2CppObject* e)` 槽签名 + dispatch 返回 void** cast 是首次直接测试。但 CodeGenerator 的 `VirtualParamTypesCpp`（CodeGenerator.cs:3603-3619）已处理 `r:` → `Il2CppObject*`，且 WfControl 等实例方法已用 Il2CppObject* 参数（winforms1 778 行 byte-exact 佐证）。判别子测试（e->X/Y/Effect 独立值）锁定参数精确性。
2. **生成代码进 test_com 的构建耦合（中）**：提交生成物 + CMake 接线；改 fixture → 必须重转译 → 重提交。文档化重生成流程（fixture 头注释写明 `dotnet run --project src/il2cppc -- ...`）。**注意**：生成物含 hash 后缀方法名，重转译会变 —— 提交物必须与 fixture 一致。
3. **`il2cpp_runtime_init()` 副作用（低）**：会跑主类型 cctor + 字符串池 + 类型注册；fixture 约束 cctor 无副作用。`il2cpp_entry_main` 不被调用（Main 不跑）—— test_com 只调 `il2cpp_runtime_init()`。
4. **对象 GC 根定（低）**：native 栈非 GC 根；测试对象经 CCW GCHandle 根定（断言 12 天然覆盖）。若不 CCW 包装，需 fixture 静态字段 + `il2cpp_gc_add_static_root`（entry.cpp 生成）—— 选择 CCW 根定方案更简单且直接为批次 3 铺路。
5. **devirt S3 干扰 e2e 侧 callvirt（低）**：test_com 直接调 dispatch 无影响；e2e 侧若加接口 callvirt，双实现者（WfDropSource/WfDropSource2）使 InterfaceImplementerCount=2 → 不走 S3 直调 → 走 runtime dispatch。批次 1 的 e2e Main 建议加一条 `((IDropTarget)src).OnDragEnter(e)` 调用以 e2e 覆盖生成 callvirt 路径。
6. **枚举转译（低）**：DragDropEffects 枚举转译已有先例（boxenum、MethodMap.Enum.cs）；（int）cast 在生成代码中可用。
7. **符号命名冲突（低）**：com17type 类型名（IDropTarget/WfDropSource/DragEventArgs）与 runtime 无冲突（runtime 的 IDropTarget 是 IID 常量 kComIidIDropTarget，非 C++ 符号）；与 winforms1 的 WfControl 等不同名。

---

## 6. 最终推荐

**批次 1 = 真实 WinForms IDropTarget 最小形状转译注册**：

1. **fixture** `tests/cases/com17type/`：DragDropEffects 枚举 + 最小 DragEventArgs（X/Y/Effect）+ IDropTarget 接口（4 方法）+ WfDropSource/WfDropSource2 双实现者（隐式实现、实例观察字段、无窗口线程）。
2. **转译产物提交** `gen_com17type/`（gen/ 先例）。
3. **tests/runtime/CMakeLists.txt** 把生成源接入 test_com。
4. **test_com.cpp section 19**（全平台块）：`il2cpp_runtime_init()` 注册 → `il2cpp_interface_dispatch` 逐槽直接调用 → 断言方法被调 + 参数/返回值/副作用正确 + 双实现者隔离 + CCW 包装与目标恢复（批次 3 底座）。
5. **转译器 0 改动、runtime 0 改动**（机制已存在；批次 1 全部增量在 fixture + 测试 + 构建接线）。
6. **验证**：runtime_full 0 errors + test_com 全绿 + 139/139 + unit 1807 + e2e（com17type 入套件）0 diff。

**批次 1 解锁**：批次 2（typed-RCW 验证——IDropTarget 接口描述符已在；包装 native IDataObject 的方法映射有接口表可依）；批次 3（托管 ctor 调用机制——DragEventArgs 已在，只差 CCW 视图方法 → `il2cpp_interface_dispatch(obj, &IDropTarget_class, slot)` → OnDragEnter(DragEventArgs) 的桥，且 section 19 断言 12 已验证 CCW GCHandle 目标恢复）。
