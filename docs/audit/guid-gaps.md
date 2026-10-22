# System.Guid 作为参数/返回值的残余缺口（b5/guidmethod 审计 r2）

基线 commit：`aec894de`（Guid 参数/返回值支持）+ 审计 r1 补丁（RetStmt VALUETYPE
返回桥 / `IsGuidField` 泛型实例 owner 回退）+ 本轮的
`StackFold.UserCallResultKind` 0x11 扩展。

本文件如实登记 **仍然不支持** 的 Guid 形态。上一轮报告把这些写成
"原 LOUD 保留（NotSupportedException）"，**该表述不准确**：除明确标注
`NotSupportedException` 的条目外，其余在生成阶段不会抛异常，而是产出
**无法通过 C++ 编译** 的代码（difftest 会在 cmake build 阶段失败，而不是
在转译阶段被拒绝）。下文逐条给出真实行为。

## D3 — `ldelema` 结构体指针 temp 被 Guid kind 抢占（未修，单独立项）

审计 r1 建议单独立项，本轮**未做任何改动**。

- 形态：对 Guid 数组/结构体取地址（`ldelema` / `ldflda`）得到的指针结果
  temp，其 `locKind` 会被既有的 "guid" 传播规则当成 Guid **值**槽，于是
  声明成 `il2cpp::Il2CppGuid`（16 字节值）而不是指针。
- 真实行为：**C++ 编译错误**（指针初始化/赋值类型不匹配），非
  NotSupportedException。
- 影响面：需要"对 Guid 槽取地址后再解引用/写入"的形态。
  **按值**的参数/返回值**不触发**；但 **byref 参数与 ref 返回会触发**，
  且属于本任务（Guid 作为参数/返回值）的范围：
  - `void SetRef(ref b.G)`（`ref` 字段）→ cmake FAIL：`'Il2CppGuid*'` 无法转
    `'int32_t'`（`ldflda` 指针 temp 被声明成 Guid 值槽）。
  - `void SetRef(ref arr[1])`（`ref` 数组元素）→ cmake FAIL：`'intptr_t'`
    无法转 `'Il2CppGuid*'`。
  - `ref Guid` 返回（`ref return`）→ 同样失败。
  - 补丁前后行为一致（不是本轮引入），但**不能**声称"本任务范围内不触发"。
- 建议：单独立项，把"地址型 Guid 表达式"与"值型 Guid 表达式"在
  `locKind` 通道上分离（例如引入 `guidptr` kind）。

## D5 — Guid 的未接线成员

以下成员在 `MethodMap.Guid.cs` 里**明确抛 NotSupportedException**（转译期
LOUD，非 C++ 编译错误）：

| 成员 | 真实行为 | 原因 |
|------|----------|------|
| `Guid.Equals(object)` | `NotSupportedException("Equals(object) not wired: a boxed Guid has no runtime box class...")` | boxed Guid 无 runtime box class |
| `Guid.CompareTo(object)` | `NotSupportedException("CompareTo(object) not wired: ...")` | 同上 |
| `Guid.ctor(byte[])` | `NotSupportedException(".ctor(byte[]) not wired: guid.h has no from-bytes icall ...")` | guid.h 无 from-bytes icall |
| `Guid.ToString(string format)`（非 string 参数形态） | `NotSupportedException("ToString(format) not wired: ...")` | 见下：string 形态已接线 |
| 11 参数分量 ctor `Guid(int,int,int,byte...)` | **不抛异常**：`TryMapGuid` 返回 `null` → 落回用户方法路径 → 产出引用未定义符号 / 类型不匹配的 C++（**cmake build 失败**） | 缺"Guid 值槽就地 newobj"路径 |

注：`Guid.ToString("D"/"N"/"B"/"P"/"X")` 已接线到
`il2cpp_guid_to_string_format`（`MethodMap.Guid.cs` 的 `ps.Length==1 &&
ps[0]==0x0E` 分支），**不是**缺口。

## D6 — 其他未覆盖形态

| 形态 | 真实行为 | 说明 |
|------|----------|------|
| `Guid.GetHashCode()` | **不抛异常**，落回用户方法路径 → 引用未定义符号（**cmake build / link 失败**） | guid.h 无 hash icall；未接线 |
| `Guid` 静态字段**读取**（`static Guid X` 的 `ldsfld`） | **不抛异常**：生成 `extern int32_t <field>;`，类型反射登记为 `System.Int32`，却按 16 字节 Guid 解引用 → **越界读**（读到相邻静态存储的字节，无异常、无编译错误） | `TypeEmitter` 的静态存储类型推导无 Guid 分支，静态字段被当作 `System.Int32`；写入侧由下方 `stsfld` 条目 LOUD 拦住，但**读取侧没有守卫** |
| `Guid` 静态字段存储（`static Guid X` 的 `stsfld`） | `NotSupportedException("Guid static-field store not wired: TypeEmitter static storage has no 16-byte Guid slot")` | TypeEmitter 静态存储无 Guid 语义（`EmitStaticStorage` / `StaticFieldCppType` 无 Guid 分支） |
| 经 `Func<Guid,...>` / `Action<Guid>` 的**委托调用** | 未接线；multicast helper 无 struct-value ABI | 用例只覆盖委托**创建**与 `Target` 读取（`guidmethod` S8），调用未覆盖 |
| 结构体类型的**实例字段**（`struct Outer { Inner I; }`，`o.I.G`） | 加载期 `NotSupportedException("value-type instance fields not supported in M2")`（`MetadataLoader.DecodeFieldSignature`，`extended:false` 的实例字段路径） | **预存在限制**，与 Guid 无关；`guidmethod2` 因此改用 struct **接收者**字段（`h.G`，`h` 为 struct）覆盖同一 `RetStmt` 通道 |

## 本轮已修复（供对照）

- `return <Guid 字段>`（class / struct 接收者）——此前 `retKind` 判成 `val`
  并被 VALUETYPE 返回桥 `static_cast<int32_t>` 装箱 → C++ 编译错误。
- `return Guid.Empty` —— 同上（`StaticFieldExpr` 分支把 `guid` kind 覆盖成
  `val`）。
- `return <泛型实例 owner 的 Guid 字段>`（`Gen<int>.G`）——`IsGuidField` 只
  精确匹配 `OwnerFullName`，泛型实例名 `Gen`1_int` 匹配不到定义 `Gen`1`。
- `return <decimal/DateTime/TimeSpan 参数>` —— `UserCallResultKind` 的 0x11
  分支只映射 Guid/Decimal，DateTime/TimeSpan 返回 `null` → 结果 temp
  声明 `int32_t`。
- `return <用户 enum 参数>` —— 同一 VALUETYPE 返回桥的装箱问题。

## 预存在缺陷（非 Guid 专属，本任务不修，仅登记以免下一轮误判"全族已修"）

| 形态 | 真实行为 | 说明 |
|------|----------|------|
| `enum : long` 的返回/局部 | **静默截断**：oracle `5000000000` vs runtime `705032704` | `TypeEmitter.cs:1067-1074` 只有 `int32_t value__`；**推翻**"enum 底层是 int32 所以落 null 也对"的假设 |
| 自定义 struct 的 call-result 就地字段访问（`MakeS().A`） | **C++ 编译失败** | `UserCallResultKind` 的 `_ => null` 仍给 int32 槽（未按返回类型声明 struct 槽） |
| `static DateTime` / `static TimeSpan` 声明分裂 | 生成不一致 | `generated.h` 声明 `Il2CppDateTime`，`.cpp` 定义 `int32_t` |

## 回归基线

- `guidmethod`（S1..S8）输出 sha 保持
  `9f8b1853cecfe2819d008894ed49dc47a4b127a3944aabebb34eedf725e596ed`
  （1200B）。
- 新增 `guidmethod2`（S9..S14）输出 sha
  `02a4bba1021cdecea0c951a32953551011078833bdab33a7874154d4c641cb8c`
  （803B）。
- `loud-guid-struct` sha `69baedc9b8c0546a05fdbb704f2f575f0957882d03129830340e949021b5462d`。
