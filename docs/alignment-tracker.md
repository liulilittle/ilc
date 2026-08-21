# .NET Framework 4.8 对齐追踪清单（工程总控表）

- 日期：2026-08-03（快照）
- 目的：ILC（IL→C++ AOT）对 .NET Framework 4.8 常用行为对齐的进度总控表，供所有代理（runtime / transpiler / tests 域）参考。
- 数据来源（本快照实测）：
  - 域头：`src/runtime/include/internal/*.h` 共 **64** 个；`icall_` 前缀函数声明合计 **313** 个（另有 `il2cpp_*` 前缀的非 icall 命名域，见备注列）。
  - 接线：`src/il2cppc/**/*.cs` 中对 `icall_*` 的唯一引用 **236** 个（MethodRefResolver.cs 为主 + CodeGenerator.cs 直连 box/unbox/console 等）。
  - git 基线：`f1db70d`（M5 BCL runtime domains）。未跟踪（工作树未提交）文件 = 并行代理在途产物。
- 状态图例：✅=已提交（git 跟踪且未修改）｜🟡=工作树未提交（untracked 或 tracked+modified）｜⬜=未做。
- ⚠️ 快照说明：并行代理持续新建头/实现（本次测量期间即新出现 arraysort.h、binaryio.h、monitor.h、timeext2.h、guid.h、numberformat.h）。本表为测量时刻值，调度前请复核 `git status` 与文件 mtime（编辑 CodeGenerator.cs / MethodRefResolver.cs / StackFold.cs 前必须查 mtime，见 .opencode/memory.md）。

## 1. 域总表：域 | 头文件 | icall 数 | 已接线（近似）| 状态

| 域 | 头文件 | icall 声明 | 已接线≈ | 状态 | 备注 |
|----|--------|-----------:|--------:|------|------|
| Math | il2cpp_math.h | 17 | 16 | ✅ | clamp_i 未接；abs/max/min/sign/sqrt/pow/floor/ceiling/round 已接 |
| Math 扩展 | il2cpp_math_ext.h | 16 | 16 | 🟡 | truncate/三角/exp/log 全接 |
| Time/DateTime/TimeSpan | il2cpp_time.h | 29 | 29 | ✅ | 最大域，全接 |
| Time 扩展 | il2cpp_time_ext.h | 2 | 2 | 🟡 | |
| Time 扩展2 | timeext2.h | 5 | 3 | 🟡 | il2cpp_datetime_*（ParseExact/ticks/compare）非 icall 命名，接 3 |
| Console | il2cpp_console.h | 8 | 8 | ✅ | IsInputRedirected 显式拒绝（resolver:328） |
| Convert | il2cpp_convert.h | 15 | 15 | 🟡 | 全接 |
| String | il2cpp_string_ext.h | 15 | 15 | ✅ | |
| String 扩展2 | il2cpp_string_ext2.h | 13 | 11 | 🟡 | |
| String 拼接 | il2cpp_concat.h | 4 | 1 | 🟡 | il2cpp_string_concat_*/join/repeat 未接线 |
| StringBuilder | il2cpp_stringbuilder.h | 11 | 11 | 🟡 | 全接（含 tostring:sb cast） |
| Random | il2cpp_random.h | 14 | 7 | 🟡 | 约半数（NextBytes/重载等未接） |
| Environment | il2cpp_environment.h | 11 | 11 | 🟡 | 全接 |
| IO File/Directory/Path | il2cpp_io.h | 11 | 11 | 🟡 | Directory.Delete 显式拒绝（resolver:829），仅 icall_file_delete |
| IO Stream/Binary | streamio.h / binaryio.h | 14 / 22 | 0 / 0 | 🟡 | il2cpp_stream_* / il2cpp_binary_* 非 icall 命名，**已实现未接线** |
| Hashtable | il2cpp_hashtable.h | 8 | 8 | 🟡 | 全接 |
| Stack/Queue | stackqueue.h | 14* | 0 | 🟡 | *非 icall 命名（il2cpp_stack_*/il2cpp_queue_*）已实现未接线 |
| Thread | il2cpp_thread.h | 7 | 5 | 🟡 | Sleep/Yield/CriticalRegion 已接；CurrentThread/ManagedThreadId/Priority 拒绝（resolver:1111-1114） |
| Monitor | monitor.h | 5* | 0 | 🟡 | *il2cpp_monitor_* 已实现未接线 |
| Interlocked | il2cpp_interlocked.h | 12* | 0 | 🟡 | *il2cpp_interlocked_* 已实现未接线 |
| GC | il2cpp_gc_api.h / il2cpp_gc_ext.h | 7 / 4 | 7 / 0 | 🟡 | gc_api 全接；gc_ext 弱引用未接（WeakReference 全拒绝，resolver:1127-1129） |
| Array | il2cpp_array_ext.h / il2cpp_array_ext2.h | 6 / 8 | 6 / 8 | 🟡 | |
| Array 排序/搜索 | arraysort.h | 12* | 3 | 🟡 | *il2cpp_array_sort_*/index_of/binary_search/copy_to 部分接 |
| Boxing/ValueType | il2cpp_boxing_ext.h / il2cpp_boxing_ext2.h | 8 / 16 | 6 / 15 | 🟡 | CodeGenerator 直连 box/unbox |
| Value 语义 | il2cpp_value_semantics.h | 8 | 8 | 🟡 | 全接 |
| Delegate | il2cpp_delegate_ext.h | 9 | 5 | 🟡 | |
| BitConverter | bitconverter.h | 24* | 2 | 🟡 | *il2cpp_bitconverter_* 已实现未接线；仅 DoubleToInt64Bits/Int64BitsToDouble 经 il2cpp_double_bits 接（resolver:948-955） |
| CultureInfo | cultureinfo.h | 6* | 0 | 🟡 | *il2cpp_culture_* 未接线；resolver 仅属性折叠（InvariantCulture→null、CurrentCulture→"en-US" 字面量，resolver:1139-1156） |
| NumberFormat | numberformat.h | 0 | 1 | 🟡 | invariant 数字格式化（新文件，待核） |
| Version/Uri | versionuri.h | 7* | 0 | 🟡 | *il2cpp_version_*/il2cpp_uri_* 已实现未接线 |
| ConvertBase64 | il2cpp_convertbase64.h | 2* | 0 | 🟡 | *非 icall 命名，已实现未接线 |
| Buffer | buffer.h | 2* | 0 | 🟡 | *il2cpp_buffer_* 已实现未接线 |
| IntPtr | intptr.h | 6* | 0 | 🟡 | *il2cpp_intptr_* 已实现未接线 |
| Stopwatch | stopwatch.h | 2* | 0 | 🟡 | *il2cpp_stopwatch_* 已实现未接线 |
| Guid | guid.h | 0* | 0 | 🟡 | 新文件（测量期间刚出现），待核 |
| Enum | enumext.h | 4* | 2 | 🟡 | *il2cpp_enum_* 部分接 |
| Parse（TryParse） | il2cpp_parse.h | 6 | 0 | 🟡 | icall_try_parse_* 已实现未接线 |
| Conv（conv 指令） | il2cpp_conv.h | 13 | 4 | 🟡 | 指令级转换，部分接 |
| Format | il2cpp_format.h / il2cpp_format_ext.h | 8 / 6 | 7 / 4 | 🟡 | |
| Marshal | il2cpp_marshal_ext.h | 12 | 0 | 🟡 | 未接线；P/Invoke DllImport 走独立 marshal 路径（MarshalUtf8Expr，H5 修复在途） |
| P/Invoke | il2cpp_pinvoke_ext.h / il2cpp_pinvoke.h | 2 / 0 | 0 / 0 | ✅ | |
| Exception | il2cpp_exception_ext.h / il2cpp_exception.h | 0 / 0 | — | 🟡 | 无 icall 声明（throw/type-tag 机制）；get_Message 拒绝（resolver:974-975） |
| Type/反射 | il2cpp_typeext.h / il2cpp_typecheck.h | 2 / 6 | 2 / 2 | 🟡 | metadata.h/attribute.h 无 icall 声明；attribute 注册机制在途（attribute.cpp） |
| Runtime 基座 | il2cpp_runtime.h | 10 | 7 | 🟡 | tracked+modified |
| 对象模型 | il2cpp_object.h / il2cpp_gc.h / il2cpp_string.h / il2cpp_array.h | 0 | — | 🟡 | tracked+modified（并行代理在途） |
| 基础设施（allocator/frame/metadata/encoding/intern/typecheck 等） | 各 0-7 | 见上 | 见上 | ✅/🟡 | allocator/frame/metadata 已提交；encoding.h 0 声明（encoding.cpp 骨架） |

> 测量方法：`icall_[a-z0-9_]+(?=\()` 计数头文件声明；`src/il2cppc/**/*.cs` 全部 `icall_*` 唯一引用按头文件内容 substring 匹配得"已接线"。带 `*` 的域为 `il2cpp_*` 非 icall 命名（声明数含注释引用时可能 ±1 偏差）。"已接线"含 CodeGenerator 直连与注释误计，精度为近似。

## 2. .NET Framework 4.8 常用 API 大类对照表

| System.* 大类 | 运行时域 | 覆盖状态 | 说明 |
|---------------|----------|:--------:|------|
| System.Math | math.h + math_ext.h | 完整 | 33 声明≈全接（clamp_i 例外） |
| System.Convert | convert.h | 完整 | 15/15 |
| System.DateTime / System.TimeSpan | time.h + time_ext.h + timeext2.h | 完整 | 36 声明全接（ParseExact 部分） |
| System.Text.StringBuilder | stringbuilder.h | 完整 | 11/11 |
| System.Environment | environment.h | 完整 | 11/11 |
| System.Collections.Hashtable | hashtable.h | 完整 | 8/8 |
| System.Console | console.h | 部分 | 全接但 IsInputRedirected 拒绝 |
| System.String | string_ext.h + string_ext2.h + concat.h | 部分 | concat 未接 |
| System.Array | array_ext.h + array_ext2.h + arraysort.h | 部分 | 排序/搜索只接 3/12 |
| System.Random | random.h | 部分 | 7/14 |
| System.Threading.Thread | thread.h | 部分 | 无线程对象模型（CurrentThread 拒绝） |
| System.IO.File/Directory/Path | io.h | 部分 | Directory.Delete 拒绝 |
| System.GC | gc_api.h + gc_ext.h | 部分 | gc_ext/弱引用未接 |
| System.Delegate | delegate_ext.h | 部分 | 5/9 |
| System.BitConverter | bitconverter.h | 未做 | 24 helper 已实现未接线（仅 2 bits 函数） |
| System.IO.Stream / BinaryReader / BinaryWriter | streamio.h + binaryio.h | 未做 | 36 函数已实现未接线 |
| System.Collections.Stack / Queue | stackqueue.h | 未做 | 14 函数已实现未接线 |
| System.Threading.Monitor | monitor.h | 未做 | 5 函数已实现未接线 |
| System.Threading.Interlocked | interlocked.h | 未做 | 12 函数已实现未接线 |
| System.Globalization.CultureInfo | cultureinfo.h | 未做 | 已实现未接线（resolver 仅固定 en-US 折叠） |
| System.Version / System.Uri | versionuri.h | 未做 | 7 函数已实现未接线 |
| System.IntPtr | intptr.h | 未做 | 6 函数已实现未接线 |
| System.Guid | guid.h | 未做 | 新文件，未接线 |
| System.Diagnostics.Stopwatch | stopwatch.h | 未做 | 2 函数已实现未接线 |
| System.ConvertBase64 | convertbase64.h | 未做 | 2 函数已实现未接线 |
| System.Buffer | buffer.h | 未做 | 2 函数已实现未接线 |
| System.Text.Encoding | encoding.h | 未做 | 头 0 声明、cpp 骨架 |
| 值类型装箱/拆箱 | boxing_ext.h + boxing_ext2.h + value_semantics.h | 完整 | CodeGenerator 直连 |
| System.Type/反射/Attribute | typeext.h + typecheck.h + metadata.h + attribute.h + enumext.h | 部分 | 反射面窄；attribute 注册机制在途 |
| System.Exception | exception.h + exception_ext.h | 部分 | type-tag 载体；catch 变量/Message 拒绝 |
| P/Invoke / Marshal | pinvoke_ext.h + marshal_ext.h | 部分 | DllImport 路径可用；icall_marshal_* 未接 |

## 3. 缺口优先序

> `docs/audit/icall-gap-list.md` **存在**（2026-08-03 19:10 并行代理产出）——它是接线缺口的**权威施工单**：runtime 头 438 个声明（50 头，双命名 `icall_*`/`il2cpp_*` 词边界去重）vs 转译器 264 个引用 → 正向缺口 **174**（P1 高 78 / P2 中 55 / P3 低 41），反向缺口 **0**（无链接失败隐患）。本文档 §1 数字为快速快照（substring 匹配，含注释噪音与模板插值盲区），**以 icall-gap-list.md 的逐项人工核验为准**。正确性债务另见 docs/audit/audit-summary.md（C1-C4 Critical / H1-H9 High）与 docs/audit/reflection-audit.md / hotpath-audit-2.md / worktree-report.md。

**P0 — 正确性债务（audit-summary.md 已列，非接线问题，先行）：**
C1 引用参数非 GC 根 ｜ C2 移动式 Gen0 压缩 vs C++ 根 ｜ C3 int64 静默截断 ｜ C4 引用数组越界写 ｜ H1-H9（uint 比较、leave 折叠、box 非 Int32、castclass 静默 null、P/Invoke 字符串缓冲、字段槽位>31 漏扫、接口继承分派、throw 恒 Exception、INT_MIN/-1 UB）。修复顺序见 audit-summary.md「建议修复顺序」。

**P1 接线缺口（icall-gap-list.md 施工单批次）：**
- 批次 1（纯增量，无新机制）：Math Max/Min/Clamp（7）、string.Concat 3/4 + Join(char)（3）、int.ToString("X")（1）、Stopwatch（5）、Interlocked（12）、IntPtr（6）、CultureInfo（6）、throw 特化（il2cpp_throw_* 4-6 个）
- 批次 2（需对象模型）：Queue/Stack（14，hashtable 模式照搬）、Stream（14，il2cpp_stream_t 句柄）、Encoding（5，size-query 双调）
- 批次 3（需 ref/out）：TryParse（6）——卡 M 阶段 out 支持，先保持 loud 拒绝
- 批次 4（P2）：Delegate（3）、GC finalizer/weakref（4）、Marshal（11）、Type/类型检查（8）、Array.FindIndex（1）
- 独立修复（P1 帧配对）：CodeGenerator.cs:1293 发射 `il2cpp_frame_push(&__frame)` 但无 frame_pop → 帧配对断裂（1:1 违规，icall-gap-list.md P3 节指出，应升 P1）

**P3 — 显式拒绝项（需对象模型才能接，先保持 loud NotSupportedException）：**
- System.WeakReference 全拒绝（需要 WeakReference 对象模型 + finalizer 链）
- Thread.CurrentThread / ManagedThreadId / Priority（需要线程对象模型）
- Console.IsInputRedirected（需要重定向状态）
- Directory.Delete（仅 file_delete 存在）
- Exception.get_Message / catch 变量（M3 carrier 是 C++ type-tag）

## 4. 每域下一步动作（写给调度者）

| 域 | 下一步动作 |
|----|-----------|
| BitConverter | MethodRefResolver.BitConverterDispatch 接线 24 个 helper（参照 box/convert 的 I() 模式 + unbox casts）；补 tests/cases/bitconv/ e2e |
| BinaryIO / StreamIO | 新增 Resolver 域 dispatch（System.IO.BinaryReader/Writer、Stream 子类），接 il2cpp_binary_*/il2cpp_stream_*；先核 cpp 已实现（binaryio.cpp/streamio.cpp 在途） |
| Stack/Queue | Resolver 新增 System.Collections.Stack/Queue dispatch；接 14 函数；补 stackqueuecase e2e |
| Interlocked | Resolver 新增 System.Threading.Interlocked dispatch（纯静态原子，无对象模型依赖）；tests/cases/interlocked/ |
| Monitor | 先定对象模型决策（lock(obj) 需要锁表？）；或先接 enter/exit 于静态标记对象；询调度者 |
| Arraysort | ArrayDispatch 扩接 sort/index_of/binary_search/copy_to；补引用数组（C4 修复依赖 stelem_ref 修正） |
| Version/Uri | Resolver 新增 System.Version/Uri dispatch；versionuri.cpp 已实现 |
| CultureInfo | 接 il2cpp_culture_* 替代当前 en-US 字面量折叠（resolver:1139-1156）；与 numberformat.h 协调 |
| Parse | Int32Dispatch/StringDispatch 接 icall_try_parse_*（TryParse 家族） |
| Concat | StringDispatch 接 concat_many/concat_array/join/repeat |
| Random | RandomDispatch 补齐 NextBytes/Next 重载（7 个缺口） |
| GC | gc_ext 弱引用依赖 WeakReference 对象模型（P3），先不动；gc_api 已全接 |
| Conv | 核 conv.h 13 声明与 CodeGenerator 指令发射对应关系，补 9 缺口 |
| Type/反射/Attribute | attribute.cpp 注册机制在途；核 metadata.h/typecheck.h 缺口 |
| Encoding / Guid / NumberFormat | 新域，先核头文件完整性（0 icall 声明），再定接线方案 |
| 基座文件（M 状态） | il2cpp_array.h/gc.h/object.h/runtime.h/string.h 为 tracked+modified——先与并行代理协调 commit 时机（避免冲突，编辑前查 mtime） |
| 帧配对 | CodeGenerator.cs:1293 发射 frame_push 无 frame_pop——1:1 违规（icall-gap-list.md P3 节升 P1）；接线或修发射 |
| 全域 | 每域完成接线后：`dotnet build src/il2cppc` + `dotnet test tests/unit` + `pwsh scripts/run-tests.ps1 -Compiler mingw`（脚本在首个失败用例 abort，只能逐域推进，见 discover.md）；不 git commit（约束） |

## 附：统计数字摘要

- 头文件总数：64（internal/*.h）
- icall 前缀声明：313（+非 icall 命名域函数约 150+：bitconverter 24 / binaryio 22 / streamio 14 / stackqueue 14 / interlocked 12 / arraysort 12 / versionuri 7 / cultureinfo 6 / intptr 6 / monitor 5 / timeext2 5 / enumext 4 / concat 4 / frame 4 / debugext 4 / convertbase64 2 / buffer 2 / stopwatch 2）
- src/il2cppc 唯一 icall 引用：236
- 已提交域：18（✅）+ 5 基座（🟡 modified）；工作树未提交域：41（🟡）
- Resolver 域 dispatch：36 个 `case "System.*"`（含 1 个 stub：BitConverter 仅 2 函数、1 个全拒绝：WeakReference）
