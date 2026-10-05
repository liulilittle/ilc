# ILC 项目完整进度快照（2026-08-06，opencode.db 清理前权威状态）

> 本文档是会话删除后的**唯一权威状态来源**。恢复时先读本文档，再读：
> docs/alignment-tracker-v3.md（域接线）、docs/roadmap-status-v5.md（里程碑/e2e 趋势）、
> docs/plans/e2e-sprint-worklist.md（剩余用例施工单）、docs/audit/e2e-full-matrix5.md（矩阵5）。

## 1. 项目定位（铁律，不可违背）

- **正式名 ILC**（IL→C++ AOT 编译器），**不是 il2cpp**——"il2cpp 是形意词汇，不是工程定义"。命名纠正方案见 docs/architecture/rename-ilc-plan.md（分层重命名，层 5 目录改名须等所有代理收工）。
- 目标：对齐 .NET Framework 4.8 行为（M2-M8 全部里程碑）。
- **绝对原则**：实现任何 .NET 语义前**必须先抄 .NET Core 开源实现**（微软数十年验证，MIT）——本地参考源码：
  - `E:\il2cpp\reference\netcore\`（dotnet/runtime sparse：libraries 1829 文件 + src/coreclr 1177 文件（gc 151/jit 323/vm 703））
  - `E:\il2cpp\reference\corert\`（4412 文件 @c6af4cf——TypeSystem 296 文件 + CppWriter.cs 与 ILC 转译器思路最接近）
  - 映射表：docs/netcore-reference-guide.md（52 行/8 组）；conformance 审计 4 份在 docs/audit/*conformance*
- **跨平台铁律**：IOS/MACOS/LINUX/WINDOWS/ANDROID/OPENBSD 接口；已落地 NDK/WINDOWS/LINUX（三编译器 MinGW/MSVC/WSL GCC7.5 零警告门）；设计见 docs/design/cross-platform-design.md。
- **性能铁律**：fib 1.50×/array 1.33× 已超 net48（M7 里程碑达成）；字符串池化技术（T3 g_strPool 已落地）。
- **主代理角色**：监工/调度——只拆任务派发并行子代理（用户要求**上百并发/批**），不做实现。任务卡约束：文件域隔离（每文件一代理独占）+ 独立 build 目录 + 不 git commit（主代理统一提交）。

## 2. git 状态

- 分支 `m1`，**HEAD = f36f386**（M6 audit batch 14）
- 最近提交链：f36f386(docs) → 5532ff5(test) → 2fca145(runtime) → 3ab788d(transpiler) → b021dea(fix) → 64d4483(docs) → 8fa2755(test) → 2659d8c(runtime) → 81d25b6(transpiler) → 69510bf(batch12) → b59c6d9(batch11) → d670a84 → 7091ead → 93cb337 → 67735c7 → 0d28414...
- 工作树：约 22 modified + 大量 untracked（并行代理在途 + 新用例）

## 3. 当前数字（2026-08-06 快照）

| 指标 | 数值 | 备注 |
|---|---|---|
| 转译器 unit | **1047/1073（97.6%）** | 26 失败：1 本域回归（EHRegionTests.Emit_CatchThenFinally——CodeGenerator 正常路径 finally 尾部 `;` 打破形状正则，待修）+ 25 在途域 |
| runtime 测试 | **106/106 全绿**（fullcheck5） | fullcheck6/7 被 test_marshal.cpp 旧签名阻塞（已修复：intptr_t/Il2CppArray* 对齐，需重跑确认） |
| e2e | **80/106（75.5%）** | matrix5：92/131；趋势 47→72→80；matrix6 待跑（大量修复后预计 90+） |
| 域接线 | **459 icall / 393 已接（85.6%）** | MethodMap 66 个全 committed；最大缺口 crypto_obj 44/20 |
| e2e 用例 | 137 个（tests/cases/） | |
| 三编译器 | MinGW ✅ / MSVC ✅（新域 7 目标全绿）/ WSL ✅ | WSL 曾阻塞 io.cpp 缺 <unistd.h>（已修复 1 行）；MSVC 残留 C4996×8（format.cpp strcpy + test sprintf——非新域） |
| 性能 | fib 1.50×/loop 0.97/array 1.33×/string 0.34/object 0.02/sort 0.56/hash 1.00；arr_sum 1.33/arr_write 0.84/str_len 0.95 | 剩余差距：object/string 需逃逸分析+LICM（M7 主战场） |

## 4. 里程碑完成状态（M1-M8）

- **M1-M4 ✅**：IL 解码/IR 栈折叠/CodeGenerator/异常 EH/入口；P-Invoke/Marshal/C API（M4）
- **M5 接线 🟡（85.6%）**：34+ 域——Math/String/IO/Console/DateTime/TimeSpan/Random/Array/Convert/GC/Delegate/Type/Enum/Attribute/Format/Regex/List<int>/ArrayList/Stopwatch(对象态)/Thread(对象态)/ThreadPool/Process/Environment/TimeZone/XML/Crypto(MD5/SHA1/SHA256/SHA384/512/HMAC/AES/RNG)/Net(IPAddress)/Hashtable/SortedList/Stack/Queue/StringBuilder/StringReader/Writer/BinaryIO/Encoding/Base64/BitConverter/Marshal/Interlocked/IntPtr/UIntPtr/CultureInfo/Debug/Monitor/lock/WeakReference/LINQ(Count/Any/Where/Select/OrderBy)...
- **M5.5 反射 🟡**：typeof 折叠（Ldtoken+GetTypeFromHandle→全名串）/GetType().Name 短名（il2cpp_type_get_short_name）/typeinfo 注册表（37 断言绿）/invoker 统一签名（341 条目查表）/Activator.CreateInstance/Type.GetType/模式 B CLI（--reflection-list 过滤）；**未落地**：TypeInfo 表转译器发射（R-REFL-01，reflectcase GAP-REFL-1）、MethodInfo.Invoke 端到端、模式 A/C
- **M6 转译器 🟡**：泛型（Id<int> 展开/MethodSpec 解码/VAR-MVAR 字节消费）/嵌套类型发射/委托（实例绑定 target、多播 i4 invoke、Equals 结构化）/EH（rethrow/catch-all 归一/sibling-catch 单 catch+if-else 分派链/exdeep 37/37）/尾调用（StackFold goto 循环——TailSum 100 万深度不崩）/switch 0x45 折叠（Ceq 链）/字符串池 T3（g_strPool[id] 单 load）/临时槽复用（TempSlotPass 11→3）/内联（InlinePass 15/15）/LICM（hoistable 检测）/边界检查消除（部分）/cctor 守卫（done 后置+exc 缓存重抛——boom_caught=2 过）/IntPtr 全宽（M4.5 部分）
- **M6.5 跨平台 🟡**：平台层 31 API（platform.h win/posix 双实现全绿）/encoding POSIX 手写 UTF-8 编解码（WSL 零警）/NDK 26.1 arm64 探通（交叉编译过）/gc 压缩器修复（kMinBlockSize）/console UTF-8 输出（中文 29/29 字节）/ws2_32 链接/Threads::Threads PUBLIC
- **M7 性能 🟡**：fib/array 超 net48；conformance 审计 4 份（Number 251/251 net48 匹配/Base64 41 向量/DateTime 60+ 断言/Regex 106 断言）；**剩余**：object 0.02/string 0.34 逃逸分析
- **M8 未开始**（跨平台扩展验证 Apple/OpenBSD）

## 5. 关键 P0 修复历史（已完成，勿重复）

- UTF-8 多字节被过滤（string.cpp DecodeUtf8Sequence 完整转码——中文全链）
- 数组越界 abort→可捕获 IndexOutOfRangeException
- frame_push/frame_pop 配对（leaf icall 无帧）
- ResultKind int64 传播（recursive_arith fact=1307674368000）
- throw null→NRE 载型（exdeep f16）
- 常量 INT_MIN/-1 不折叠（checked_div 保留）
- gc 压缩器 8 字节块覆写 FreeBlock 头（kMinBlockSize=sizeof(FreeBlock)——0xC0000005 根因）
- sblong GC 移动+裸指针（StringBuilder icall 全根槽化——31/31）
- internchain StringPool Concat 折叠破坏 ReferenceEquals（移除常量 Concat 折叠）
- ldelem/stelem 宽度分派（1 字节元素越界读邻堆）
- box 描述符接口表（isinst/castclass System.* 命中）
- GetType 短名/boxed ToString 值格式化/conv.u8 零扩展/round-half-to-even
- EH 兄弟 catch（单 catch+分派链）/catch-all 归一/rethrow 保型

## 6. 剩余工作（施工单清单——按优先级）

**P0（矩阵6 前）**：
1. EHRegionTests.Emit_CatchThenFinally 回归（CodeGenerator 正常路径 finally 尾部 `;`——1 行级修复）
2. e2e 矩阵6 全量重跑（需先重建 il2cppc.dll + 共享 runtime 库（含 gc/sblong 修复））
3. 共享 runtime 库重建（build/runtime_e2e_shared——matrix6 前必须）
4. test_marshal 签名修复已做——runtime fullcheck 重跑确认

**P1（施工单编号）**：
- **#44 HAS_THIS bit（0x20）static/instance 分类**（MethodRefResolver.cs:337 name heuristic → 签名头 HAS_THIS——delegate2 get_Method + enumdeep op_Equality 一修双卡）
- **#42 delegateinst 2-arg 发射**（StackFold.cs:1847 提取 target + Expr.cs DelegateNewExpr.Target + CodeGenerator 2-arg——已派实施中）
- **StreamIO 对象化**（streamcase 验收 oracle 20/20：icall_stream_file_open(Il2CppObject* self,...) + handle 注册表 + BclAllocExpr + System_IO_*_class 描述符 + Read/Write(byte[])）
- **TypeInfo 表发射**（R-REFL-01——TypeEmitter 生成注册表——reflectcase GAP-REFL-1）
- **netcase ip4 槽**（CodeGenerator kind 列表 3 处 + ipaddr.h include）
- **webcase #43**（BclResolveCore dispatch WebClient/Socket——生成名路径消除）
- **pinvokeext M4.5 IntPtr 返回全宽**（PinvokeReturnTypeCpp 恒 int32_t——x64 截断）
- **decimalcase 收尾**（T1/T2/T3 已落+签名统一 result-first——发射 3 缺陷：ctor_5 void 赋值/loc1 未声明+sf_One/Zero 未发射/parse 参数序）
- **Encoding GetChars 分配形**（GetBytes 已做——GetChars(byte[])→char[]）
- **MethodInfo.Invoke 端到端**（methodinfo.cpp 已派——invoker 已建）
- **LINQ ToList**（linq_map.cpp il2cpp_linq_to_list——listi4 已建）
- **array2d 多维**（MethodSignatureCoder 0x14 解码已派——发射层待）
- **Split newarr char C4**（CodeGenerator newarr char 元素）

**P2**：逃逸分析（object/string bench）/Apple/OpenBSD 验证/rename-ilc-plan 层 1-4 执行/Type.GetType 字符串驱动（模式 C）/反射模式 A 全量/更多 e2e 用例扩量/三编译器 C4996 清零/性能 v3 基准重跑

## 7. 环境/工具备忘

- 转译器：src/il2cppc/（.NET 8，dotnet build src/il2cppc/il2cppc.csproj -c Release——0 错 0 警门）
- unit：dotnet test tests/unit/unit.csproj -c Release
- runtime：cmake -S tests/runtime -B build/<唯一名> -G "MinGW Makefiles" && cmake --build；CMakeLists 已 GLOB（新文件自动纳入）
- e2e 单用例：pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>（-ContinueOnError 开关已加）
- 三编译器：MinGW=clang17（C:\Program Files\ming-w64）、MSVC=VS2022 19.39（vswhere）、WSL=Ubuntu1804 GCC7.5（C:\Program Files\WSL\wsl.exe）
- NDK：D:\android\sdk\ndk（**禁用 adb**）
- 探针/隔离验证惯例：%TEMP%\opencode\ 独立项目；共享树被在途代理破坏时 g++ 独立编译（-std=c++17 -Wall -Wextra -fexceptions -fno-rtti）
- 在途判定：编辑前查文件 mtime（<15min=在途，等 20min）；e2e 前需 il2cppc.dll 新于全部源 + 共享库重建
- net48 oracle：csc.exe 在 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\
- UTF-8 字节比较必须 cmd /c 重定向（pwsh *> 会 GBK 重编码）

## 8. 恢复后第一动作（5 步）

1. git status + git log -5 确认 HEAD=f36f386
2. dotnet build src/il2cppc -c Release（0 错 0 警？）+ dotnet test（收敛 26 失败——先修 EHRegion CatchThenFinally 回归）
3. cmake 全量 runtime 构建（test_marshal 修复后应全绿）
4. 重建共享 runtime 库（gc/sblong 修复入库后）→ e2e 矩阵6
5. 派发批次 20+（40-100 并行任务）：P0/P1 施工单 + 新域 + e2e 扩量 + 每批提交
