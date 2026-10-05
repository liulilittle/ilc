# Regression Recheck — R-A1 / R-A2 三例复验（2026-08-06 00:40-01:5x）

> 触发：matrix4（e2e-full-matrix4.md）记录的 3 例回归（generics/interlockedcase/valuetypes，
> 均 cmake_build）在施工单下复验。结论：**3 例生成码级 + 全链运行级全部翻绿**；
> 官方 runner 的 cmake_build 阶段仍被**非本域在飞文件**阻塞（marshal header/impl 分叉）。

## 施工单状态

| 施工单 | 域 | 回归记录（matrix4） | 修复内容 | 状态 |
|---|---|---|---|---|
| R-A2 | generics | 调用 `Box_1_int__ctor_…` 但声明 `Box_1__ctor_…` [READ:e2e-full-matrix4.md:75] | 泛型实例展开名对齐（`TypeCppName` 前缀 `_int`，MethodRefResolver GenericInstantiationName 同编码） | **已修复** |
| R-A1 | interlockedcase | `(uint8_t*)(&loc)` 传 `int32_t*`/`int64_t*` [READ:e2e-full-matrix4.md:81] | cast 判定收窄：`""` 纯透传，Guid 槽改显式 `"g"` cast（CodeGenerator.cs:2969-2977, 3004-3012） | **已修复** |
| R-A1 | valuetypes | `Point__ctor((uint8_t*)(&loc0),…)` 破坏 `Point*` [READ:e2e-full-matrix4.md:119] | 同上（一修双卡） | **已修复** |

## 验证证据

### 0. 代理收工确认
- R-A1/R-A2 相关转译器文件 mtime 全部 ≥30min（00:44 采样）：CodeGenerator.cs 23:49:00、
  MethodRefResolver.cs 23:58:50、TypeEmitter.cs 16:07:55、MethodMap.Interlocked.cs 20:28:07。
- il2cppc.dll（Release 23:59:28 / Debug 00:40:28）晚于全部真实 .cs 源（最新 23:58:50）——
  DLL 为当前源构建。00:35/00:44 两次采样 src/ 无新写入（后续 1:49 有 valuesemantics.cpp
  独立域写入，与本 3 例无关）。
- 结论：R-A1/R-A2 施工单域已收工；runtime 仍有多域在飞（marshal/stringext3/valuesemantics）。

### 1. 官方 run-tests.ps1 三例结果（mingw）
| 用例 | transpile | cmake_build | diff | runner 结果 |
|---|---|---|---|---|
| generics | 过 | **失败**（runtime.cpp:121 marshal header 分叉） | 未达 | FAIL（非本域阻塞） |
| interlockedcase | 过 | **失败**（同） | 未达 | FAIL（非本域阻塞） |
| valuetypes | 过 | **失败**（同） | 未达 | FAIL（非本域阻塞） |

> 阻塞根因：marshal 域在飞改动 `il2cpp_runtime.h:136`（23:50:46，未提交）将
> `icall_marshal_alloc_hglobal` 声明改为 `intptr_t`，但实现 `runtime.cpp:121`（提交态，
> mtime 8/4）仍为 `void*` → C++ 重载冲突（仅返回类型不同）。**与 R-A1/R-A2 无关**。

### 2. 生成码级验证（matrix4 回归即生成码 C++ 编译失败）
g++ -c 独立编译生成码，全部 exit=0：
- generics：Box_1_int.cpp / Cases_Generics.cpp / entry.cpp / host.cpp **4/4 exit=0**
- interlockedcase：InterlockedCase.cpp / entry.cpp / host.cpp **3/3 exit=0**
- valuetypes：Cases_ValueTypes.cpp / Point.cpp / entry.cpp / host.cpp **4/4 exit=0**

R-A2 命名不变量核对（声明 == 调用，前缀均 `Box_1_int`）：
```
Box_1_int.cpp:62   IL2CPP_NOINLINE void Box_1_int__ctor_56a7d5b86e85333c(Il2CppObject* arg0)
Cases_Generics.cpp:31/35  Box_1_int__ctor_56a7d5b86e85333c(t0); / (t1);
```
R-A1 cast 核对（无 `(uint8_t*)(&loc)` 泄漏到非 Guid 域）：
```
InterlockedCase.cpp:45  il2cpp_interlocked_increment((&loc0))   // int64_t* 直通
Cases_ValueTypes.cpp:23 Point__ctor_fe17127c3b869a52((&loc0), 3LL, 4LL)  // Point* 直通
Point.cpp:17  IL2CPP_NOINLINE void Point__ctor_fe17127c3b869a52(Point* arg0,...)
```

### 3. 全链运行级验证（shadow-runtime 链接，逐字节 diff vs net48）
构建方法：当前 runtime 源以**提交态 marshal 头**遮蔽（`git show HEAD:…/il2cpp_runtime.h`
作 shadow，与提交态 runtime.cpp 配对）；排除另 2 个无关在飞域（stringext3.cpp、
valuesemantics.cpp，独立编译错误，本 3 例不引用其符号）→ ar 归档 105 个 .o →
链接各用例生成码 → 运行。

| 用例 | 运行输出 | net48 参考 | 逐字节 |
|---|---|---|---|
| generics | 42/7/42 | 42/7/42 | **MATCH** |
| interlockedcase | 28×True | 28×True | **MATCH** |
| valuetypes | 7/10/110/8 | 7/10/110/8 | **MATCH** |

## 结论
- **R-A2（generics 泛型 ctor 命名）**：修复确认。生成码声明名 == 调用名
  （`Box_1_int__ctor_…`），编译零错，运行 42/7/42 与 net48 逐字节一致。matrix4 的
  「声明分叉」记录为批次在飞期暂态，当前树不复现。
- **R-A1（interlockedcase / valuetypes cast）**：修复确认。`""` cast 纯透传，Guid 槽
  走显式 `"g"` cast；生成码无 `(uint8_t*)(&loc)` 泄漏；interlockedcase 28×True、
  valuetypes 7/10/110/8 均与 net48 逐字节一致。
- **官方 runner 翻 PASS 的剩余阻塞**：marshal 域在飞 `il2cpp_runtime.h` 声明
  （intptr_t/Il2CppArray*）与 `runtime.cpp` 实现（void*/Il2CppObject*）分叉——
  marshal 代理需把 runtime.cpp 实现同步到新签名（或将声明回退）。该阻塞全量影响
  所有用例的 cmake_build 阶段，与 R-A1/R-A2 无关。stringext3.cpp / valuesemantics.cpp
  另有独立在飞编译错（Il2CppString*→Il2CppObject* 返回、Il2CppDecimal 重定义）。

## 遗留
- 待 marshal 域落地 runtime.cpp 后，run-tests.ps1 官方全链三例应直接 PASS（本次
  shadow 运行已证明生成码 + 当前 runtime 源输出正确）。
