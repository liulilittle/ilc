# 任务卡清单 — 128 个并行小任务（ILC 剩余大工程拆分）

> 用途：主调度者按本清单派发"尽可能多的小并行任务"。每卡 ≤60 分钟，
> 文件域零冲突（每个文件只有一个写者；共享文件一律标 `[读]` 只读）。
> 任务执行约定：不 git commit（除非调度者另行指示）；每卡完成后按各自验证命令自证。

## 0. 并行调度规则（先读）

1. **写者唯一律**：全仓库每个文件至多被一张卡"写"。卡内 `文件域: [写]...` = 该卡独占创建/修改的文件；`[读]...` = 只读依赖（可能被其他卡写，但本卡不得改）。
2. **依赖软约束**：卡依赖只允许"依赖已完成卡的文件存在"（如 `依赖: T101 的 MethodMapHub.cs 已存在`）。不允许依赖其他卡的运行行为。
3. **新文件命名即锁定**：创建任何文件前先查本清单文件域表，避免与其他卡重名。
4. **e2e fixture 命名**：目录名 `tests/cases/<name>/` 必须与现有 63 个用例及本清单所有卡均不重复。
5. **runtime 源文件自动纳入**：`src/runtime/CMakeLists.txt` 与 `tests/runtime/CMakeLists.txt` 均为 GLOB 自动发现，新 .cpp 无需改 CMakeLists（`[读]` 即可，任何人不得写）。

## 0.1 标准验证速查（卡内命令按此缩写引用）

- **转译器构建**：`dotnet build src/il2cppc/il2cppc.csproj`（必须零错误）
- **单元测试**：`dotnet test tests/unit/unit.csproj`
- **runtime C++ 单测（新域卡）**：
  `cmake -S tests/runtime -B build/runtime_<name> -G "MinGW Makefiles" && cmake --build build/runtime_<name>` 然后 `& ./build/runtime_<name>/test_<name>.exe` → 退出码 0、g_failures=0
- **e2e 双跑（fixture `<name>`）**：
  1. `dotnet build tests/cases/<name>/<name>.csproj -c Release -o tests/cases/<name>/out`
  2. `& tests/cases/<name>/out/<name>.exe > tests/cases/<name>/expected.txt`（net48 参考运行）
  3. `dotnet run --project src/il2cppc -- tests/cases/<name>/out/<name>.exe tests/cases/<name>/gen`
  4. `cmake -S tests/cases/<name>/gen -B tests/cases/<name>/build_mingw -G "MinGW Makefiles" && cmake --build tests/cases/<name>/build_mingw`
  5. `& tests/cases/<name>/build_mingw/il2cpp_gen_exe.exe > tests/cases/<name>/actual.txt`
  6. 逐字节 diff：`Compare-Object (Get-Content expected.txt) (Get-Content actual.txt)` → 空输出即通过
- **三编译器全量**：`pwsh scripts/run-tests.ps1 -Compiler mingw|msvc|wsl`（注意：全量脚本遇首个失败用例即中止，本卡验证以"速查 e2e 流程"为准）

## 0.2 分类配额

| 分类 | 卡数 | 编号段 |
|---|---|---|
| BCL 接线 | 35 | T101-T135 |
| 新 runtime 域 | 30 | T201-T230 |
| 反射三模式落地 | 10 | T301-T310 |
| 转译器优化 | 20 | T401-T420 |
| e2e 用例扩量 | 15 | T501-T515 |
| 文档/设计 | 10 | T601-T610 |
| 验证/工具 | 8 | T701-T708 |
| **合计** | **128** | |

---

# 一、BCL 接线 ×35（T101-T135）

**接线模式**（T101 定骨架，其余卡照抄）：runtime 已有 icall（`src/runtime/src/*.cpp` + `include/internal/*.h`），
转译器侧尚无 dispatch。每卡产出 `Emit/MethodMap.<域>.cs`（partial 静态类，静态构造函数
`MethodMapHub.Register("System.X", XxxResolve)` 自注册）+ 1 个 e2e fixture，按速查双跑。
MethodRefResolver.cs 与 hub 文件全部只读（唯一写者 = T101）。

## T101 接线骨架：MethodMapHub 注册表 + MethodRefResolver 尾链（60 分钟）
**背景**：MethodRefResolver.cs:222-257 已内联 36 个 dispatch；新增域若都改它必然冲突，需要 hub 机制。
**任务**：1. 读 `[读] src/il2cppc/Emit/MethodRefResolver.cs`（BclResolveCore 结构、ResolvedMethod 定义、I() 助手签名）→ 2. `[写] src/il2cppc/Emit/MethodMapHub.cs`：静态字典 `Dictionary<string, List<Func<string,byte[],int,ResolvedMethod?>>>`（多 resolver 链式尝试，支持同 owner 多文件注册），`Register(owner, resolver)` + `TryResolve(owner,name,ps,count)` → 3. `[写]` 修改 MethodRefResolver.cs **末尾**（唯一写者）：BclResolveCore 所有 case 未命中时 `return MethodMapHub.TryResolve(owner, name, ps, count);`（一处尾链，不动既有 36 行）→ 4. `[写] src/il2cppc/Emit/MethodMap.Sample.cs`：以 System.Version 为样例域完成自注册 + dispatch（占位，供后续卡模仿）→ 5. 验证：`dotnet build src/il2cppc/il2cppc.csproj` + `dotnet test tests/unit/unit.csproj`
**文件域**：[写] Emit/MethodMapHub.cs、Emit/MethodMap.Sample.cs、Emit/MethodRefResolver.cs（本卡独占）；[读] 其余 Emit/*
**依赖**：无

## T102 接线 System.Version + System.Uri 域（55 分钟）
**背景**：versionuri.h/.cpp 已实现（Version.Parse/ToString、Uri 解析），未接 dispatcher。
**任务**：1. 读 `[读] include/internal/versionuri.h` 列 icall 签名 → 2. `[写] Emit/MethodMap.VersionUri.cs` 自注册 "System.Version"+"System.Uri" 两 owner（每 owner 一个 resolver，参数按 RawSignature 解码，可复用既有 I() 形态）→ 3. `[写] tests/cases/versionuri/`（csproj net48 + 主逻辑：Version.Parse/ToString/Compare + Uri.IsWellFormedUriString）→ 4. 按速查 e2e 双跑（版号格式化逐字节一致）
**文件域**：[写] Emit/MethodMap.VersionUri.cs、tests/cases/versionuri/*；[读] versionuri.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T103 接线 System.Diagnostics.Stopwatch 域（45 分钟）
**背景**：stopwatch.h/.cpp 已实现（Start/Stop/Elapsed），e2e 已有 stopwatch 相关但未接泛化。
**任务**：1. 读 stopwatch.h 列签名 → 2. `[写] Emit/MethodMap.Stopwatch.cs` → 3. `[写] tests/cases/stopwatch2/`（Start/Stop/ElapsedMilliseconds/Reset/IsRunning 断言）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Stopwatch.cs、tests/cases/stopwatch2/*；[读] stopwatch.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T104 接线 System.Collections.Stack/Queue 域（45 分钟）
**背景**：stackqueue.h/.cpp 已实现（Push/Pop/Peek/Enqueue/Dequeue），无 dispatcher。
**任务**：1. 读 stackqueue.h → 2. `[写] Emit/MethodMap.StackQueue.cs` 注册 "System.Collections.Stack"+"System.Collections.Queue" → 3. `[写] tests/cases/stackqueue2/`（混合 Push/Pop 与 Enqueue/Dequeue + 空容器抛异常路径）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.StackQueue.cs、tests/cases/stackqueue2/*；[读] stackqueue.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T105 接线 System.IntPtr/UIntPtr 域（45 分钟）
**背景**：intptr.h/.cpp 已实现（ToInt64/ToString/比较），未接线。
**任务**：1. 读 intptr.h → 2. `[写] Emit/MethodMap.IntPtr.cs` 注册 "System.IntPtr"+"System.UIntPtr" → 3. `[写] tests/cases/intptr2/`（IntPtr.Size、转换、算术）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.IntPtr.cs、tests/cases/intptr2/*；[读] intptr.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T106 接线 System.Type 基础查询域（55 分钟）
**背景**：typecheck.h/.cpp 已实现类型判定/转换查询（isinst/类型名等），Type 的 Name/FullName/IsValueType 无 dispatcher。
**任务**：1. 读 typecheck.h + typeext.h 列可用查询 → 2. `[写] Emit/MethodMap.TypeBase.cs` 注册 "System.Type"（只做 Name/FullName/IsValueType/IsEnum/IsPrimitive 等只读查询；GetMethod/Invoke 归 T303）→ 3. `[写] tests/cases/typebase/`（typeof 用法反射查询输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.TypeBase.cs、tests/cases/typebase/*；[读] typecheck.h、typeext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T107 接线 System.RuntimeTypeHandle 域（45 分钟）
**背景**：metadata.cpp/typeext.cpp 有类型句柄辅助，RuntimeTypeHandle 未接线。
**任务**：1. 读 typeext.h + metadata.h → 2. `[写] Emit/MethodMap.TypeHandle.cs` 注册 "System.RuntimeTypeHandle" → 3. `[写] tests/cases/typehandle/`（typeof 的句柄相等性/Value）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.TypeHandle.cs、tests/cases/typehandle/*；[读] typeext.h、metadata.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T108 接线 System.ValueType 域（45 分钟）
**背景**：valuesemantics.h/.cpp 已实现（Equals/GetHashCode 语义），未接线。
**任务**：1. 读 valuesemantics.h → 2. `[写] Emit/MethodMap.ValueType.cs` 注册 "System.ValueType" → 3. `[写] tests/cases/valuetype2/`（struct Equals/GetHashCode 往返）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.ValueType.cs、tests/cases/valuetype2/*；[读] valuesemantics.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T109 接线 System.Attribute 域（45 分钟）
**背景**：attribute.h/.cpp 已实现特性实例查询，未接线。
**任务**：1. 读 attribute.h → 2. `[写] Emit/MethodMap.Attribute.cs` 注册 "System.Attribute" → 3. `[写] tests/cases/attributecase2/`（自定义特性 + GetCustomAttribute 查询）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Attribute.cs、tests/cases/attributecase2/*；[读] attribute.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T110 接线 System.Enum 域（55 分钟）
**背景**：enumext.h/.cpp 已实现（IsDefined/GetName/Parse），未接线。
**任务**：1. 读 enumext.h → 2. `[写] Emit/MethodMap.Enum.cs` 注册 "System.Enum" → 3. `[写] tests/cases/enum2/`（Enum.Parse/ToString/IsDefined/GetNames 往返）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Enum.cs、tests/cases/enum2/*；[读] enumext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T111 接线 System.Diagnostics.Debug 域（40 分钟）
**背景**：debugext.h/.cpp 已实现（断言/输出），未接线。
**任务**：1. 读 debugext.h → 2. `[写] Emit/MethodMap.Debug.cs` 注册 "System.Diagnostics.Debug" → 3. `[写] tests/cases/debugcase/`（Debug.WriteLine/Assert 输出一致）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Debug.cs、tests/cases/debugcase/*；[读] debugext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T112 接线 System.Buffer 域（45 分钟）
**背景**：buffer.h/.cpp 已实现（BlockCopy/ByteLength），未接线。
**任务**：1. 读 buffer.h → 2. `[写] Emit/MethodMap.Buffer.cs` 注册 "System.Buffer" → 3. `[写] tests/cases/buffercase/`（BlockCopy 数组间拷贝 + ByteLength）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Buffer.cs、tests/cases/buffercase/*；[读] buffer.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T113 接线 System.Text.Encoding 基础域（55 分钟）
**背景**：encoding.h/.cpp 已实现（UTF8/ASCII/GetBytes），未接线。
**任务**：1. 读 encoding.h → 2. `[写] Emit/MethodMap.Encoding.cs` 注册 "System.Text.Encoding"（Encoding.UTF8/ASCII/Default + GetBytes/GetString）→ 3. `[写] tests/cases/encoding2/`（UTF8 往返 + ASCII 截断行为）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Encoding.cs、tests/cases/encoding2/*；[读] encoding.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T114 接线 System.Globalization 补齐域（50 分钟）
**背景**：CultureInfoDispatch 已接 CultureInfo（MethodRefResolver.cs:255）；NumberFormatInfo/TextInfo 未接。
**任务**：1. 读 cultureinfo.h → 2. `[写] Emit/MethodMap.Globalization.cs` 注册 "System.Globalization.NumberFormatInfo" + "System.Globalization.TextInfo"（只读查询）→ 3. `[写] tests/cases/culture2/`（CurrentCulture 数字/文本信息输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Globalization.cs、tests/cases/culture2/*；[读] cultureinfo.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T115 接线 System.String 扩展1：比较/查找域（55 分钟）
**背景**：stringext.h/.cpp 已实现 Compare/CompareOrdinal/IndexOf 家族，未接线。
**任务**：1. 读 stringext.h → 2. `[写] Emit/MethodMap.StringCmp.cs` 注册 "System.String" 的 Compare/CompareOrdinal/IndexOf/Contains/StartsWith/EndsWith（注意 StringDispatch 已存在 → 本卡注册同名 owner 需在 MethodMapHub 合并（hub 支持多 resolver 链式尝试），若 hub 不支持则改为注册 "System.StringExt" 并经 StringDispatch 尾链）→ 3. `[写] tests/cases/stringcmp/`（比较/查找输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.StringCmp.cs、tests/cases/stringcmp/*；[读] stringext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T116 接线 System.String 扩展2：分割/裁剪域（45 分钟）
**背景**：stringext2.h/.cpp 已实现 Split/Trim/Join，未接线。
**任务**：1. 读 stringext2.h → 2. `[写] Emit/MethodMap.StringSplit.cs`（Split/Trim/TrimStart/TrimEnd/Join）→ 3. `[写] tests/cases/stringsplit/`（分隔符数组/空白裁剪/Join 拼接）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.StringSplit.cs、tests/cases/stringsplit/*；[读] stringext2.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T117 接线 System.Array 扩展1：排序域（50 分钟）
**背景**：arrayext.h/.cpp 已实现 Sort/Reverse，未接线。
**任务**：1. 读 arrayext.h → 2. `[写] Emit/MethodMap.ArraySort.cs` 注册 "System.Array" 的 Sort/Reverse/IndexOf 重载 → 3. `[写] tests/cases/arraysort/`（int/string 排序 + 反转输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.ArraySort.cs、tests/cases/arraysort/*；[读] arrayext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T118 接线 System.Array 扩展2：拷贝/查找域（45 分钟）
**背景**：arrayext2.h/.cpp 已实现 Copy/Clear/Find 辅助，未接线。
**任务**：1. 读 arrayext2.h → 2. `[写] Emit/MethodMap.ArrayCopy.cs`（Copy/Clear/Exists/FindIndex）→ 3. `[写] tests/cases/arraycopy/`（数组段拷贝 + 查找）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.ArrayCopy.cs、tests/cases/arraycopy/*；[读] arrayext2.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T119 接线 System.Math 补齐域（45 分钟）
**背景**：mathext.h/.cpp 已实现 Abs/Sign/Clamp/Atan2 等扩展；MathDispatch 已接基础（MethodRefResolver.cs:223）。
**任务**：1. 读 mathext.h → 2. `[写] Emit/MethodMap.MathExt.cs`（Math 未覆盖的 mathext 函数，链式尝试）→ 3. `[写] tests/cases/math3/`（Abs/Clamp/Atan2/Sqrt 输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.MathExt.cs、tests/cases/math3/*；[读] mathext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T120 接线 System.DateTime/TimeSpan 补齐域（55 分钟）
**背景**：timeext.h/.cpp 已实现 Parse/ToString 格式化扩展；DateTime/TimeSpan dispatch 已接基础（L252-253）。
**任务**：1. 读 timeext.h + il2cpp_time_ext.h → 2. `[写] Emit/MethodMap.TimeExt.cs`（Parse/格式化/运算补齐）→ 3. `[写] tests/cases/datetime2/`（Parse "yyyy-MM-dd" 往返 + 运算输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.TimeExt.cs、tests/cases/datetime2/*；[读] timeext.h、il2cpp_time_ext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T121 接线 System.String.Intern 域（40 分钟）
**背景**：stringintern.h/.cpp 已实现 Intern/IsInterned，未接线。
**任务**：1. 读 stringintern.h → 2. `[写] Emit/MethodMap.StringIntern.cs` 注册 "System.String" 的 Intern/IsInterned → 3. `[写] tests/cases/intern2/`（Intern 引用相等性 + IsInterned）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.StringIntern.cs、tests/cases/intern2/*；[读] stringintern.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T122 接线 System.String.Format 域（55 分钟）
**背景**：format.h/.cpp 已实现占位符格式化引擎，String.Format 未接线。
**任务**：1. 读 format.h → 2. `[写] Emit/MethodMap.StringFormat.cs`（String.Format 0-3 参数 + 格式项）→ 3. `[写] tests/cases/stringfmt/`（"{0:D4}"、"{1:F2}" 等格式串输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.StringFormat.cs、tests/cases/stringfmt/*；[读] format.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T123 接线 System.Format 扩展：自定义格式化域（50 分钟）
**背景**：formatext.h/.cpp 已实现扩展占位符语义（ICustomFormatter 支持），未接线。
**任务**：1. 读 formatext.h → 2. `[写] Emit/MethodMap.FormatExt.cs` 注册 "System.String" 的 format 扩展重载 + 自定义格式提供者路径 → 3. `[写] tests/cases/fmtcustom/`（IFormatProvider 自定义输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.FormatExt.cs、tests/cases/fmtcustom/*；[读] formatext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T124 接线 System.Parse 补齐域（45 分钟）
**背景**：parse.h/.cpp 已实现通用解析辅助（整数/浮点解析），未接线。
**任务**：1. 读 parse.h → 2. `[写] Emit/MethodMap.Parse.cs`（未覆盖的 Parse/TryParse 变体重载补齐）→ 3. `[写] tests/cases/parse2/`（TryParse 失败分支 + 进制解析）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Parse.cs、tests/cases/parse2/*；[读] parse.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T125 接线 System.Exception 扩展域（45 分钟）
**背景**：exceptionext.h/.cpp 已实现 Data/TargetSite/内层异常，ExceptionDispatch 已接基础（L257）。
**任务**：1. 读 exceptionext.h → 2. `[写] Emit/MethodMap.ExceptionExt.cs`（Data/Message/TargetSite 补齐）→ 3. `[写] tests/cases/exceptiondata/`（catch 后 Data 字典读写输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.ExceptionExt.cs、tests/cases/exceptiondata/*；[读] exceptionext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T126 接线 System.GC 补齐域（40 分钟）
**背景**：gcext.h/.cpp 已实现 GCSettings/收集扩展；GCDispatch 已接（L248）。
**任务**：1. 读 gcext.h → 2. `[写] Emit/MethodMap.GCExt.cs`（GCSettings.LatencyMode/Generation 查询）→ 3. `[写] tests/cases/gcsets/`（GC.GetTotalMemory 趋势 + 代数输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.GCExt.cs、tests/cases/gcsets/*；[读] gcext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T127 接线 System.Runtime.InteropServices.Marshal 补齐域（55 分钟）
**背景**：marshalext.h/.cpp 已实现 marshal 辅助（M4 产物），Marshal 域未完整接线。
**任务**：1. 读 marshalext.h + pinvokeext.h → 2. `[写] Emit/MethodMap.Marshal.cs` 注册 "System.Runtime.InteropServices.Marshal"（SizeOf/StringToHGlobal/AllocHGlobal/FreeHGlobal）→ 3. `[写] tests/cases/marshal2/`（非托管分配 + 拷贝往返）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Marshal.cs、tests/cases/marshal2/*；[读] marshalext.h、pinvokeext.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T128 接线 System.Threading.Interlocked 域（45 分钟）
**背景**：interlocked.h/.cpp 已实现原子操作，未接线。
**任务**：1. 读 interlocked.h → 2. `[写] Emit/MethodMap.Interlocked.cs` 注册 "System.Threading.Interlocked"（Increment/Decrement/Exchange/CompareExchange）→ 3. `[写] tests/cases/interlockedcase/`（单线程序列 + 多线程累加）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Interlocked.cs、tests/cases/interlockedcase/*；[读] interlocked.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T129 接线 System.Threading.Monitor 补齐域（50 分钟）
**背景**：thread.h/.cpp 已实现 Monitor（memory.md 记录 Monitor 域已收尾），无 dispatcher。
**任务**：1. 读 thread.h 的 monitor 部分 → 2. `[写] Emit/MethodMap.Monitor.cs` 注册 "System.Threading.Monitor"（Enter/Exit/TryEnter）→ 3. `[写] tests/cases/monitorcase/`（锁保护计数器输出一致）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Monitor.cs、tests/cases/monitorcase/*；[读] thread.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T130 接线 System.Convert.Base64 域（45 分钟）
**背景**：convertbase64.h/.cpp 已实现 RFC4648 编解码，ConvertDispatch 未含 Base64。
**任务**：1. 读 convertbase64.h → 2. `[写] Emit/MethodMap.ConvertBase64.cs` 注册 "System.Convert" 的 ToBase64String/FromBase64String → 3. `[写] tests/cases/base64case/`（已知向量 "Man"→"TWFu" + 非法输入异常）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.ConvertBase64.cs、tests/cases/base64case/*；[读] convertbase64.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T131 接线 System.Text.StringBuilder 补齐域（45 分钟）
**背景**：stringbuilder.h/.cpp 已实现核心；StringBuilderDispatch 已接（L241）。
**任务**：1. 读 stringbuilder.h → 2. `[写] Emit/MethodMap.SBExt.cs`（AppendFormat/Capacity/索引器补齐）→ 3. `[写] tests/cases/sbappend/`（AppendFormat 输出）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.SBExt.cs、tests/cases/sbappend/*；[读] stringbuilder.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T132 接线 System.IO.Stream 基础域（50 分钟）
**背景**：streamio.h/.cpp 已实现流基础（Read/Write/Flush），File/Directory/Path 已接（L245-247）但 Stream 未接。
**任务**：1. 读 streamio.h → 2. `[写] Emit/MethodMap.Stream.cs` 注册 "System.IO.Stream"（Read/Write/Flush/Position/Seek）→ 3. `[写] tests/cases/streamcase/`（FileStream 写读往返）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.Stream.cs、tests/cases/streamcase/*；[读] streamio.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T133 接线 System.Runtime.CompilerServices.RuntimeHelpers 域（45 分钟）
**背景**：runtime.h/.cpp 已实现辅助（InitializeArray/GetHashCode），未接线。
**任务**：1. 读 il2cpp_runtime.h → 2. `[写] Emit/MethodMap.RuntimeHelpers.cs` 注册 "System.Runtime.CompilerServices.RuntimeHelpers" → 3. `[写] tests/cases/runtimehelp/`（InitializeArray 静态数组 + 引用相等）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.RuntimeHelpers.cs、tests/cases/runtimehelp/*；[读] il2cpp_runtime.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T134 接线 Boxing/ValueType 装箱辅助域（45 分钟）
**背景**：boxingext.h/boxingext2.h 已实现装箱/拆箱辅助，未接线。
**任务**：1. 读 boxingext.h + boxingext2.h → 2. `[写] Emit/MethodMap.BoxAux.cs`（注册对应 owner：装箱辅助类）→ 3. `[写] tests/cases/boxaux/`（值类型装箱 GetType/Equals 往返）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.BoxAux.cs、tests/cases/boxaux/*；[读] boxingext.h、boxingext2.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T135 接线 System.Console 补齐域（45 分钟）
**背景**：console.h/.cpp 已实现 Read/ReadLine/颜色；ConsoleDispatch 已接（L222）。
**任务**：1. 读 console.h → 2. `[写] Emit/MethodMap.ConsoleExt.cs`（Read/ReadLine/ResetColor 补齐）→ 3. `[写] tests/cases/consoleread/`（ReadLine 回显 + 颜色序列输出；net48 参考运行注意输入重定向）→ 4. 速查双跑
**文件域**：[写] Emit/MethodMap.ConsoleExt.cs、tests/cases/consoleread/*；[读] console.h、MethodMapHub.cs
**依赖**：T101 文件存在

---

# 二、新 runtime 域 ×30（T201-T230）

**模式**（参考既有 convertbase64 域范式）：`[写] include/internal/<域>.h`（icall 声明 + 英文 doc 块）+
`[写] src/<域>.cpp`（实现，零第三方库，C++17，-Wall -Wextra 零警告）+
`[写] tests/runtime/test_<域>.cpp`（Expect 框架，cmake 独立构建运行）+
`[写] Emit/MethodMap.<域>.cs` 接线（依赖 T101 的 hub）+ `[写] tests/cases/<fixture>/` e2e 双跑。

## T201 正则引擎 v1：字面量/字符类/锚点（60 分钟）
**背景**：System.Text.RegularExpressions 无任何 runtime 实现；此为 3 卡拆分第 1 卡。
**任务**：1. 读 `[读] convertbase64.h/.cpp` 学习域范式 → 2. `[写] include/internal/il2cpp_regexcore.h` + `[写] src/regexcore.cpp`：最小正则编译器（字面量/`.`/`[...]`/`^`/`$`，NFA 状态机匹配）→ 3. `[写] tests/runtime/test_regexcore.cpp`（字面量、字符类、锚点、失败路径）→ 4. cmake 独立构建运行全绿 → 5. `[写] tests/cases/regex1/` e2e fixture（IsMatch 输出用例；若 MethodMap.Regex.cs 尚未存在则双跑标为条件执行，结果记入 GAPS.md）→ 6. 接线文件 MethodMap.Regex.cs 由 T203 独占（本卡不写）
**文件域**：[写] il2cpp_regexcore.h、regexcore.cpp、test_regexcore.cpp、tests/cases/regex1/*（仅 Regex.cs 主文件，Regex2/3.cs 由 T202/T203 追加）；[读] 既有域范式文件、MethodMapHub.cs
**依赖**：T101 文件存在

## T202 正则引擎 v2：量词/分组/交替（60 分钟）
**背景**：T201 的 NFA 内核已建立；本卡扩量词与分组。
**任务**：1. 读 `[读] il2cpp_regexcore.h`（T201 产出）→ 2. `[写] include/internal/il2cpp_regexpats.h` + `[写] src/regexpats.cpp`：`*`/`+`/`?`/`{n,m}`/分组捕获/`|` 交替 → 3. `[写] tests/runtime/test_regexpats.cpp`（贪心/回溯边界、嵌套分组）→ 4. cmake 独立构建运行全绿 → 5. e2e 补充 `[写] tests/cases/regex1/` 内新增断言（追加修改本卡私有 fixture，其余文件只读）
**文件域**：[写] il2cpp_regexpats.h、regexpats.cpp、test_regexpats.cpp；[写] tests/cases/regex1/Regex2.cs（追加）；[读] il2cpp_regexcore.h、regexcore.cpp、MethodMapHub.cs
**依赖**：T201 文件存在

## T203 正则 API 域：Match/Replace/Split 接线（60 分钟）
**背景**：正则引擎 v1+v2 就绪；Regex 类完整 API 未接线。
**任务**：1. 读 `[读] il2cpp_regexcore.h` + `[读] il2cpp_regexpats.h` → 2. `[写] include/internal/il2cpp_regexapi.h` + `[写] src/regexapi.cpp`：Match/MatchCollection/Replace/Split 封装 → 3. `[写] tests/runtime/test_regexapi.cpp` → 4. cmake 独立构建全绿 → 5. `[写] Emit/MethodMap.Regex.cs` 扩展（Match/Replace 全家族；本卡独占该文件）→ 6. e2e 双跑（Replace 文本替换输出）
**文件域**：[写] il2cpp_regexapi.h、regexapi.cpp、test_regexapi.cpp、Emit/MethodMap.Regex.cs；[写] tests/cases/regex1/Regex3.cs（追加）；[读] regexcore/regexpats 头文件、MethodMapHub.cs
**依赖**：T201 + T202 文件存在

## T204 LINQ 基座：Where/Select/ToArray 全托管转译（60 分钟）
**背景**：Enumerable 泛型方法应全托管转译（泛型共享代码机制已就绪），需验证迭代器状态机支持。
**任务**：1. 读 `[读] tests/cases/generics/` 现有泛型用例 → 2. `[写] tests/cases/linqbase/`（Where/Select/ToArray/ToList 链式 + 简单 lambda）→ 3. 速查 e2e 双跑；若转译失败 → 4. 定位缺口，`[写]` 缺口分析到 tests/cases/linqbase/GAPS.md（卡私有），转译器缺口记录上报调度者（不修共享文件）→ 5. 能通的部分全部断言通过
**文件域**：[写] tests/cases/linqbase/*（含 GAPS.md 私有记录）；[读] tests/cases/generics/*、MethodMapHub.cs
**依赖**：T101 文件存在

## T205 LINQ 排序辅助域：OrderBy/ThenBy 比较器 icall（60 分钟）
**背景**：OrderBy 依赖泛型比较回调；原生排序辅助可减少装箱。
**任务**：1. 读 `[读] stackqueue.h`（容器域范式）→ 2. `[写] include/internal/il2cpp_sort.h` + `[写] src/sort.cpp`：比较器回调快速排序（object* 数组 + Compare 委托）→ 3. `[写] tests/runtime/test_sort.cpp`（int/string/稳定序）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.Sort.cs` 注册 "System.Linq.Enumerable" 的 OrderBy/ThenBy 辅助入口 → 6. `[写] tests/cases/linqsort/` e2e 双跑（OrderBy 稳定输出）
**文件域**：[写] il2cpp_sort.h、sort.cpp、test_sort.cpp、Emit/MethodMap.Sort.cs、tests/cases/linqsort/*；[读] stackqueue.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T206 LINQ 聚合域：Sum/Aggregate/First/Any 接线（60 分钟）
**背景**：聚合类 Enumerable 方法应可全托管；本卡验证 + 接线必要 icall。
**任务**：1. 读 `[读] il2cpp_sort.h`（T205 产出）→ 2. `[写] tests/cases/linqagg/`（Sum/Aggregate/Any/All/First/Count/Contains）→ 3. 速查 e2e 双跑；缺口记录到本卡私有 GAPS.md → 4. 若缺 icall 则 `[写] include/internal/il2cpp_linq.h` + `[写] src/linq.cpp`（最小：默认比较器/累加辅助）→ 5. `[写] Emit/MethodMap.Linq.cs` 接线 → 6. 双跑通过
**文件域**：[写] tests/cases/linqagg/*（含 GAPS.md）、il2cpp_linq.h、linq.cpp、Emit/MethodMap.Linq.cs；[读] il2cpp_sort.h、MethodMapHub.cs
**依赖**：T205 文件存在（软：仅读其头文件）

## T207 XML v1：XmlDocument 解析内核（60 分钟）
**背景**：System.Xml 无 runtime 实现；3 卡拆分第 1 卡。
**任务**：1. 读 `[读] parse.h`（解析域范式）→ 2. `[写] include/internal/il2cpp_xmlcore.h` + `[写] src/xmlcore.cpp`：最小 XML 词法 + DOM（元素/属性/文本/自闭合）→ 3. `[写] tests/runtime/test_xmlcore.cpp`（嵌套元素、属性、转义实体 &amp; 等）→ 4. cmake 全绿 → 5. e2e 骨架 `[写] tests/cases/xml1/`（读 XML 字符串 → 输出节点名/文本）
**文件域**：[写] il2cpp_xmlcore.h、xmlcore.cpp、test_xmlcore.cpp、tests/cases/xml1/*；[读] parse.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T208 XML v2：XmlReader/Writer 流式域（60 分钟）
**背景**：DOM 内核就绪；流式读写 API 缺失。
**任务**：1. 读 `[读] il2cpp_xmlcore.h`（T207）→ 2. `[写] include/internal/il2cpp_xmlio.h` + `[写] src/xmlio.cpp`：XmlWriter 序列化（WriteStartElement/WriteString/WriteEndElement）+ XmlReader 游标读取 → 3. `[写] tests/runtime/test_xmlio.cpp`（写→读往返）→ 4. cmake 全绿 → 5. e2e 追加 `[写] tests/cases/xml1/Xml2.cs`（Write 输出 + Read 游标）双跑
**文件域**：[写] il2cpp_xmlio.h、xmlio.cpp、test_xmlio.cpp、tests/cases/xml1/Xml2.cs；[读] il2cpp_xmlcore.h、MethodMapHub.cs
**依赖**：T207 文件存在

## T209 XML v3：XmlSerializer 基础 + 接线域（60 分钟）
**背景**：XML 读写内核就绪；对象↔XML 绑定未做。
**任务**：1. 读 `[读] il2cpp_xmlcore.h` + `[读] il2cpp_xmlio.h` → 2. `[写] include/internal/il2cpp_xmlser.h` + `[写] src/xmlser.cpp`：公有属性/字段反射式序列化（字段名→元素）→ 3. `[写] tests/runtime/test_xmlser.cpp`（POCO 往返）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.Xml.cs` 注册 "System.Xml.XmlSerializer" → 6. e2e 双跑（Serialize→Deserialize 输出）
**文件域**：[写] il2cpp_xmlser.h、xmlser.cpp、test_xmlser.cpp、Emit/MethodMap.Xml.cs、tests/cases/xml1/Xml3.cs（追加）；[读] xmlcore/xmlio 头文件、MethodMapHub.cs
**依赖**：T207 + T208 文件存在

## T210 加密域 v1：SHA-256（60 分钟）
**背景**：System.Security.Cryptography 无实现；RFC 6234 参考自实现。
**任务**：1. `[写] include/internal/il2cpp_sha.h` + `[写] src/sha.cpp`：SHA-256（纯 C++，FIPS 180-4 常量）→ 2. `[写] tests/runtime/test_sha.cpp`（NIST 已知向量：""、"abc"、"abcdbcde...z"）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.Sha.cs` 注册 "System.Security.Cryptography.SHA256" → 5. `[写] tests/cases/sha256case/` e2e 双跑（ComputeHash 十六进制输出）
**文件域**：[写] il2cpp_sha.h、sha.cpp、test_sha.cpp、Emit/MethodMap.Sha.cs、tests/cases/sha256case/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T211 加密域 v2：MD5 + RandomNumberGenerator（60 分钟）
**背景**：SHA-256 内核就绪；MD5 与安全随机数缺失。
**任务**：1. 读 `[读] il2cpp_sha.h` 范式 → 2. `[写] include/internal/il2cpp_md5.h` + `[写] src/md5.cpp`（RFC 1321）+ `[写] include/internal/il2cpp_cryptorng.h` + `[写] src/cryptorng.cpp`（Windows BCrypt 或 /dev/urandom 平台分支）→ 3. `[写] tests/runtime/test_md5.cpp` + `[写] tests/runtime/test_cryptorng.cpp`（已知向量 + 随机性冒烟）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.Md5.cs` 接线 "System.Security.Cryptography.MD5" → 6. e2e 双跑（MD5 已知向量输出；RNG 仅字节数断言）
**文件域**：[写] il2cpp_md5.h、md5.cpp、il2cpp_cryptorng.h、cryptorng.cpp、test_md5.cpp、test_cryptorng.cpp、Emit/MethodMap.Md5.cs、tests/cases/md5case/*；[读] il2cpp_sha.h、MethodMapHub.cs
**依赖**：T101 文件存在（软读 T210 头文件）

## T212 网络域 v1：HTTP GET 最小客户端（60 分钟）
**背景**：System.Net 无实现；零第三方约束下用 WinSock2。
**任务**：1. 读 `[读] io.cpp`（平台层范式）→ 2. `[写] include/internal/il2cpp_nethttp.h` + `[写] src/nethttp.cpp`：WinSock2 TCP 连接 + HTTP/1.1 GET 最小解析（状态行/头/体）→ 3. `[写] tests/runtime/test_nethttp.cpp`（本地回环 HTTP 冒烟：连接 127.0.0.1 自建测试 socket 服务，超时保护）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.NetHttp.cs` 注册 "System.Net.WebRequest" 最小路径 → 6. 不依赖外网：e2e fixture 用可离线断言路径或跳过 e2e 仅保留 runtime 测试（在 GAPS.md 记录）
**文件域**：[写] il2cpp_nethttp.h、nethttp.cpp、test_nethttp.cpp、Emit/MethodMap.NetHttp.cs、tests/cases/nethc/（含 GAPS.md）；[读] io.cpp、MethodMapHub.cs
**依赖**：T101 文件存在

## T213 网络域 v2：TcpClient/Listener 基础（60 分钟）
**背景**：HTTP 客户端就绪；通用 socket 封装缺失。
**任务**：1. 读 `[读] il2cpp_nethttp.h`（T212）→ 2. `[写] include/internal/il2cpp_netsocket.h` + `[写] src/netsocket.cpp`：TcpClient（Connect/GetStream）/TcpListener（Start/AcceptTcpClient）→ 3. `[写] tests/runtime/test_netsocket.cpp`（本机回环 127.0.0.1:0 回显）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.NetSocket.cs` 接线 "System.Net.Sockets.TcpClient" → 6. e2e 双跑（回环回显输出一致）
**文件域**：[写] il2cpp_netsocket.h、netsocket.cpp、test_netsocket.cpp、Emit/MethodMap.NetSocket.cs、tests/cases/netsocket/；[读] il2cpp_nethttp.h、MethodMapHub.cs
**依赖**：T212 文件存在（软）

## T214 System.Guid 域（55 分钟）
**背景**：Guid 生成/解析无实现。
**任务**：1. `[写] include/internal/il2cpp_guid.h` + `[写] src/guid.cpp`（RFC 4122 v4 随机 + NewGuid/Parse/ToString "D" 格式）→ 2. `[写] tests/runtime/test_guid.cpp`（Parse 往返、格式、"D" 32 位十六进制）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.Guid.cs` 注册 "System.Guid" → 5. `[写] tests/cases/guidcase/` e2e 双跑（输出为 Parse("已知串") 的规范化结果，规避随机性）
**文件域**：[写] il2cpp_guid.h、guid.cpp、test_guid.cpp、Emit/MethodMap.Guid.cs、tests/cases/guidcase/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T215 System.Decimal 域：四则/舍入/解析（60 分钟）
**背景**：Decimal 96 位尾数 + 指数结构无实现。
**任务**：1. `[写] include/internal/il2cpp_decimal.h` + `[写] src/decimal.cpp`（加/减/乘/除/舍入 Round/Parse/ToString 最小集，95 位尾数防溢出）→ 2. `[写] tests/runtime/test_decimal.cpp`（0.1+0.2、乘除、Round、Parse 往返）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.Decimal.cs` 注册 "System.Decimal" → 5. `[写] tests/cases/decimalcase/` e2e 双跑
**文件域**：[写] il2cpp_decimal.h、decimal.cpp、test_decimal.cpp、Emit/MethodMap.Decimal.cs、tests/cases/decimalcase/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T216 System.Numerics.BigInteger 域：四则/比较（60 分钟）
**背景**：大整数无实现（int64-支持设计.md 已规划）。
**任务**：1. 读 `[读] docs/architecture/int64-支持设计.md` → 2. `[写] include/internal/il2cpp_biginteger.h` + `[写] src/biginteger.cpp`（动态 u32 数组：加/减/乘/除/模/比较/ToString）→ 3. `[写] tests/runtime/test_biginteger.cpp`（阶乘 50! 已知值、大数乘法、除取模）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.BigInteger.cs` 注册 "System.Numerics.BigInteger" → 6. e2e 双跑（50! 输出）
**文件域**：[写] il2cpp_biginteger.h、biginteger.cpp、test_biginteger.cpp、Emit/MethodMap.BigInteger.cs、tests/cases/bigintcase/*；[读] int64-支持设计.md、MethodMapHub.cs
**依赖**：T101 文件存在

## T217 System.DateTimeOffset 域（50 分钟）
**背景**：DateTime 已实现；DateTimeOffset 缺失。
**任务**：1. 读 `[读] il2cpp_time.h` → 2. `[写] include/internal/il2cpp_dtoffset.h` + `[写] src/dtoffset.cpp`（UtcDateTime/Offset/ToLocalTime/ToString）→ 3. `[写] tests/runtime/test_dtoffset.cpp` → 4. cmake 全绿 → 5. `[写] Emit/MethodMap.Dtoffset.cs` 注册 "System.DateTimeOffset" → 6. e2e 双跑（固定偏移输出）
**文件域**：[写] il2cpp_dtoffset.h、dtoffset.cpp、test_dtoffset.cpp、Emit/MethodMap.Dtoffset.cs、tests/cases/dtoffsetcase/*；[读] il2cpp_time.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T218 System.TimeZoneInfo 域（50 分钟）
**背景**：时区信息无实现（本地/UTC 偏移）。
**任务**：1. `[写] include/internal/il2cpp_timezone.h` + `[写] src/timezone.cpp`（Local/Utc 枚举 + GetUtcOffset + 名称）→ 2. `[写] tests/runtime/test_timezone.cpp`（本地偏移冒烟：与系统时区偏差一致）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.TimeZone.cs` 注册 "System.TimeZoneInfo" → 5. e2e 双跑（用固定断言格式输出 UTC 与本地）
**文件域**：[写] il2cpp_timezone.h、timezone.cpp、test_timezone.cpp、Emit/MethodMap.TimeZone.cs、tests/cases/timezonecase/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T219 System.Diagnostics.StackTrace 域（55 分钟）
**背景**：framestack.h/.cpp 已实现帧记录三合一；StackTrace 字符串化未做。
**任务**：1. 读 `[读] il2cpp_frame.h` + framestack.cpp → 2. `[写] include/internal/il2cpp_stacktrace.h` + `[写] src/stacktrace.cpp`（帧遍历 → "at X" 格式字符串，复用帧根元数据）→ 3. `[写] tests/runtime/test_stacktrace.cpp`（3 层嵌套调用帧数断言）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.StackTrace.cs` 注册 "System.Diagnostics.StackTrace" → 6. e2e 双跑（输出含方法名，行号容差处理）
**文件域**：[写] il2cpp_stacktrace.h、stacktrace.cpp、test_stacktrace.cpp、Emit/MethodMap.StackTrace.cs、tests/cases/stacktracecase/*；[读] il2cpp_frame.h、framestack.cpp、MethodMapHub.cs
**依赖**：T101 文件存在

## T220 System.Threading.ThreadPool 域（55 分钟）
**背景**：Thread 已实现；线程池缺失。
**任务**：1. 读 `[读] thread.cpp` → 2. `[写] include/internal/il2cpp_threadpool.h` + `[写] src/threadpool.cpp`（固定 worker 线程 + 任务队列 QueueUserWorkItem + WaitCallback 调用）→ 3. `[写] tests/runtime/test_threadpool.cpp`（N 任务执行完断言）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.ThreadPool.cs` 注册 "System.Threading.ThreadPool" → 6. e2e 双跑（并发计数输出一致——注意 net48 线程数差异，断言最终计数而非顺序）
**文件域**：[写] il2cpp_threadpool.h、threadpool.cpp、test_threadpool.cpp、Emit/MethodMap.ThreadPool.cs、tests/cases/threadpoolcase/*；[读] thread.cpp、MethodMapHub.cs
**依赖**：T101 文件存在

## T221 System.Threading.Semaphore/Mutex/EventWaitHandle 域（55 分钟）
**背景**：命名同步原语缺失（Monitor 已有）。
**任务**：1. 读 `[读] thread.h` → 2. `[写] include/internal/il2cpp_waithandle.h` + `[写] src/waithandle.cpp`（Windows HANDLE 封装：Semaphore/Mutex/EventWaitHandle + WaitOne/Release）→ 3. `[写] tests/runtime/test_waithandle.cpp`（两线程互斥 + 信号量计数）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.WaitHandle.cs` 接线 → 6. e2e 双跑
**文件域**：[写] il2cpp_waithandle.h、waithandle.cpp、test_waithandle.cpp、Emit/MethodMap.WaitHandle.cs、tests/cases/waithandlecase/*；[读] thread.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T222 System.IO.MemoryStream 域（45 分钟）
**背景**：流抽象已接（T132 后 streamio 可用）；MemoryStream 未实现。
**任务**：1. 读 `[读] streamio.h` → 2. `[写] include/internal/il2cpp_memorystream.h` + `[写] src/memorystream.cpp`（字节缓冲 + Read/Write/Position/ToArray）→ 3. `[写] tests/runtime/test_memorystream.cpp` → 4. cmake 全绿 → 5. `[写] Emit/MethodMap.MemoryStream.cs` 接线 → 6. e2e 双跑
**文件域**：[写] il2cpp_memorystream.h、memorystream.cpp、test_memorystream.cpp、Emit/MethodMap.MemoryStream.cs、tests/cases/memstreamcase/*；[读] streamio.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T223 System.IO.StreamReader/StreamWriter 域（55 分钟）
**背景**：文本层读写缺失（流已实现）。
**任务**：1. 读 `[读] streamio.h` + `[读] encoding.h` → 2. `[写] include/internal/il2cpp_textstream.h` + `[写] src/textstream.cpp`（UTF8 文本读写 + ReadLine/WriteLine）→ 3. `[写] tests/runtime/test_textstream.cpp`（中英文混合往返）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.TextStream.cs` 接线 "System.IO.StreamReader"/"StreamWriter" → 6. e2e 双跑
**文件域**：[写] il2cpp_textstream.h、textstream.cpp、test_textstream.cpp、Emit/MethodMap.TextStream.cs、tests/cases/textstreamcase/*；[读] streamio.h、encoding.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T224 System.IO.BinaryReader/BinaryWriter 域（50 分钟）
**背景**：二进制读写缺失。
**任务**：1. `[写] include/internal/il2cpp_binaryio.h` + `[写] src/binaryio.cpp`（LE 编码 ReadInt32/ReadDouble/Write* 家族）→ 2. `[写] tests/runtime/test_binaryio.cpp`（写→读往返 + 越界异常）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.BinaryIO.cs` 接线 → 5. e2e 双跑
**文件域**：[写] il2cpp_binaryio.h、binaryio.cpp、test_binaryio.cpp、Emit/MethodMap.BinaryIO.cs、tests/cases/binaryiocase/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T225 System.IO.Compression.DeflateStream 域（60 分钟）
**背景**：压缩无实现；自研最小 inflate（存储块 + 固定 Huffman）。
**任务**：1. `[写] include/internal/il2cpp_deflate.h` + `[写] src/deflate.cpp`（inflate：BTYPE=00 存储块 + BTYPE=01 固定 Huffman；deflate 用原始存储输出）→ 2. `[写] tests/runtime/test_deflate.cpp`（zlib 已知流解压 + 往返）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.Deflate.cs` 接线 → 5. e2e 双跑（往返一致性断言）
**文件域**：[写] il2cpp_deflate.h、deflate.cpp、test_deflate.cpp、Emit/MethodMap.Deflate.cs、tests/cases/deflatecase/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T226 System.Diagnostics.Process 域（55 分钟）
**背景**：进程启动无实现。
**任务**：1. `[写] include/internal/il2cpp_process.h` + `[写] src/process.cpp`（CreateProcess + WaitForExit + ExitCode + 参数引号转义）→ 2. `[写] tests/runtime/test_process.cpp`（启动 cmd /c exit 7 → 退出码 7）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.Process.cs` 注册 "System.Diagnostics.Process" → 5. e2e 双跑（启动自身派生进程输出）
**文件域**：[写] il2cpp_process.h、process.cpp、test_process.cpp、Emit/MethodMap.Process.cs、tests/cases/processcase/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T227 System.Globalization.GregorianCalendar 域（50 分钟）
**背景**：日历换算缺失（公历 1582+）。
**任务**：1. 读 `[读] cultureinfo.h` → 2. `[写] include/internal/il2cpp_calendar.h` + `[写] src/calendar.cpp`（civil→days 儒略日互转：年/月/日/星期/DayOfYear）→ 3. `[写] tests/runtime/test_calendar.cpp`（2000-02-29 闰年、DayOfWeek 已知值）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.Calendar.cs` 接线 → 6. e2e 双跑
**文件域**：[写] il2cpp_calendar.h、calendar.cpp、test_calendar.cpp、Emit/MethodMap.Calendar.cs、tests/cases/calendarcase/*；[读] cultureinfo.h、MethodMapHub.cs
**依赖**：T101 文件存在

## T228 System.Threading.Tasks.Task 基础域（55 分钟）
**背景**：Task.Run/Wait 依赖线程池（T220 后可用）。
**任务**：1. 读 `[读] il2cpp_threadpool.h`（T220）→ 2. `[写] include/internal/il2cpp_task.h` + `[写] src/task.cpp`（Task.Run + Wait + Result 状态机最小集，Action 回调包装）→ 3. `[写] tests/runtime/test_task.cpp`（N 个 Task.Run 完成 + Result 值）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.Task.cs` 接线 → 6. e2e 双跑（并发求和断言最终值）
**文件域**：[写] il2cpp_task.h、task.cpp、test_task.cpp、Emit/MethodMap.Task.cs、tests/cases/taskcase/*；[读] il2cpp_threadpool.h、MethodMapHub.cs
**依赖**：T220 文件存在（软）

## T229 System.Linq.Expressions 表达式树求值域（60 分钟）
**背景**：表达式树类型可全托管转译；求值需要最小解释器。
**任务**：1. `[写] include/internal/il2cpp_exprtree.h` + `[写] src/exprtree.cpp`（BinaryExpression Add/Sub/Mul/Div/And/Or/Compare + Constant/Lambda 求值）→ 2. `[写] tests/runtime/test_exprtree.cpp`（表达式树结构构建 + 求值断言）→ 3. cmake 全绿 → 4. `[写] Emit/MethodMap.ExprTree.cs` 接线 "System.Linq.Expressions" 最小入口 → 5. e2e 双跑（表达式树求值输出）
**文件域**：[写] il2cpp_exprtree.h、exprtree.cpp、test_exprtree.cpp、Emit/MethodMap.ExprTree.cs、tests/cases/exprtreecase/*；[读] MethodMapHub.cs
**依赖**：T101 文件存在

## T230 System.Text.Encoding 完整域：转换 API 扩充（55 分钟）
**背景**：encoding.h 已有基础编解码；Encoding.GetBytes/GetString 全家族与 Unicode/UTF32 缺失。
**任务**：1. 读 `[读] encoding.h` → 2. `[写] include/internal/il2cpp_encodingconv.h` + `[写] src/encodingconv.cpp`（UTF-16LE/BE/UTF-32 编解码 + GetBytes/GetString 重载 + 编码名解析）→ 3. `[写] tests/runtime/test_encodingconv.cpp`（中文 UTF-16/UTF-32 往返）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.EncodingConv.cs` 接线扩充 → 6. e2e 双跑
**文件域**：[写] il2cpp_encodingconv.h、encodingconv.cpp、test_encodingconv.cpp、Emit/MethodMap.EncodingConv.cs、tests/cases/encodingconvcase/*；[读] encoding.h、MethodMapHub.cs
**依赖**：T101 文件存在

---

# 三、反射三模式落地 ×10（T301-T310）

依据：REQUIREMENTS.md 铁律 2m/2g-2（模式 A 全量 / 模式 B 用户清单 / 模式 C 静态可知自动分析 + 按需粒度 + 统一 Invoker）。

## T301 反射元数据格式设计（60 分钟）
**背景**：反射元数据静态固化设计（路线图阶段 B、jit-预留.md #14）需要落地为结构定义。
**任务**：1. 读 `[读] docs/architecture/jit-预留.md` + `[读] il2cpp_metadata.h` + `[读] docs/REQUIREMENTS.md`（铁律 2m 三模式原文）→ 2. `[写] docs/architecture/反射元数据设计.md`：TypeMetadata 扩展表布局（方法名/字段名/属性/签名编码/泛型参数）+ 按需粒度分级（类型/成员两级）→ 3. `[写] include/internal/il2cpp_reflmeta.h`：结构体定义（ReplMethodEntry/ReplFieldEntry/ReplPropertyEntry + 静态数组内嵌只读段）→ 4. 三编译器零警告验证（用最小占位 .cpp 引用结构体编译）
**文件域**：[写] docs/architecture/反射元数据设计.md、include/internal/il2cpp_reflmeta.h、tests/runtime/test_reflmeta.cpp（结构体布局冒烟）；[读] jit-预留.md、il2cpp_metadata.h、REQUIREMENTS.md
**依赖**：无

## T302 元数据静态固化发射器（60 分钟）
**背景**：T301 定义了格式；转译器需把元数据发射为静态数组。
**任务**：1. 读 `[读] il2cpp_reflmeta.h`（T301）→ 2. `[写] src/il2cppc/Emit/MetadataTableEmitter.cs`：遍历类型表 → 发射 ReplMethodEntry/FieldEntry 静态数组（含签名编码，复用 MethodSignatureCoder 只读）→ 3. `[写] src/il2cppc/Model/ReflectEntry.cs`（中间模型）→ 4. `[写] tests/unit/MetadataTableEmitterTests.cs`（小类型图 → 断言数组内容）→ 5. `dotnet build` + `dotnet test` 全绿
**文件域**：[写] Emit/MetadataTableEmitter.cs、Model/ReflectEntry.cs、tests/unit/MetadataTableEmitterTests.cs；[读] il2cpp_reflmeta.h、Emit/MethodSignatureCoder.cs、Emit/MethodEmitter.cs
**依赖**：T301 文件存在

## T303 运行时反射查询域 v1：GetType/GetMethod/GetField（60 分钟）
**背景**：查询 API 未实现；元数据表（T302）就绪后可查。
**任务**：1. 读 `[读] il2cpp_reflmeta.h` + `[读] typeext.cpp` → 2. `[写] include/internal/il2cpp_reflquery.h` + `[写] src/reflquery.cpp`（按名查方法/字段/属性表条目 + GetType 全名查找）→ 3. `[写] tests/runtime/test_reflquery.cpp`（注册模拟表 → 查询命中/未命中）→ 4. cmake 全绿 → 5. `[写] Emit/MethodMap.ReflQuery.cs` 接线 "System.Type" 的 GetMethod/GetField 查询路径（与 T106 只读查询互补）
**文件域**：[写] il2cpp_reflquery.h、reflquery.cpp、test_reflquery.cpp、Emit/MethodMap.ReflQuery.cs；[读] il2cpp_reflmeta.h、typeext.cpp、typecheck.h、MethodMapHub.cs
**依赖**：T301 + T101 文件存在

## T304 统一 Invoker ABI 域（60 分钟）
**背景**：MethodInfo.Invoke 需要动态调用 ABI（jit-预留.md #15）。
**任务**：1. 读 `[读] docs/architecture/jit-预留.md` #15 + `[读] delegate.cpp`（既有函数指针调用形态）→ 2. `[写] include/internal/il2cpp_invoker.h` + `[写] src/invoker.cpp`：`Il2CppObject* invoke(void* argbuf, int argc)`（参数数组 → 按签名 marshalling → 调用 fnPtr → 结果装箱）→ 3. `[写] tests/runtime/test_invoker.cpp`（静态/实例/值类型方法动态调用）→ 4. cmake 全绿
**文件域**：[写] il2cpp_invoker.h、invoker.cpp、test_invoker.cpp；[读] jit-预留.md、delegate.cpp、il2cpp_metadata.h
**依赖**：T301 文件存在

## T305 模式 A：全量反射编译开关（60 分钟）
**背景**：模式 A = 全部类型按需粒度编译进去（铁律 2m）。
**任务**：1. 读 `[读] src/il2cppc/Program.cs` + `[读] Emit/MetadataTableEmitter.cs`（T302）→ 2. 修改 `[写] src/il2cppc/Program.cs`（唯一写者）：`--reflect=full` 开关 → 全类型表发射；`--reflect=none`（默认，现状）→ 3. `[写] tests/unit/ReflectModeTests.cs`（两种模式参数解析 + 发射开关行为）→ 4. `dotnet build` + `dotnet test` 全绿 → 5. `[写] docs/architecture/反射模式A.md`（用法 + 体积预期）
**文件域**：[写] src/il2cppc/Program.cs（本卡独占）、tests/unit/ReflectModeTests.cs、docs/architecture/反射模式A.md；[读] Emit/MetadataTableEmitter.cs
**依赖**：T302 文件存在

## T306 模式 B：用户反射清单（60 分钟）
**背景**：模式 B = 用户提供反射类型清单（铁律 2m 第 3 条）。
**任务**：1. 读 `[读] docs/architecture/反射元数据设计.md` → 2. `[写] src/il2cppc/TrimPlan/ReflectListParser.cs`（解析清单文件：每行类型全名 [+ 成员子集]）→ 3. `[写] tests/unit/ReflectListParserTests.cs`（空行/注释/成员子集语法）→ 4. `[写] Emit/MethodMap.ReflectList.cs` 接线（--reflect=list:<file> 路径，Program.cs 只读——由 T305 之后集成或经 hub 声明）→ 5. `dotnet build` + `dotnet test` 全绿 → 6. `[写] docs/architecture/反射模式B.md`（清单格式规范）
**文件域**：[写] src/il2cppc/TrimPlan/ReflectListParser.cs、tests/unit/ReflectListParserTests.cs、Emit/MethodMap.ReflectList.cs、docs/architecture/反射模式B.md；[读] 反射元数据设计.md、Program.cs
**依赖**：T301 文件存在

## T307 模式 C：静态可知反射自动分析（60 分钟）
**背景**：模式 C = 自动分析 typeof/nameof/直接成员访问纳入保留集（铁律 2m 第 4 条）。
**任务**：1. 读 `[读] src/il2cppc/Il/IlDecoder.cs` + `[读] Model/MethodDef.cs` → 2. `[写] src/il2cppc/TrimPlan/StaticReflectAnalyzer.cs`（扫描 IL：ldtoken/ldstr("TypeName") 模式 → 类型保留集）→ 3. `[写] tests/unit/StaticReflectAnalyzerTests.cs`（typeof(T) 命中、字符串类型名命中、无反射代码零保留）→ 4. `dotnet build` + `dotnet test` 全绿 → 5. `[写] docs/architecture/反射模式C.md`
**文件域**：[写] src/il2cppc/TrimPlan/StaticReflectAnalyzer.cs、tests/unit/StaticReflectAnalyzerTests.cs、docs/architecture/反射模式C.md；[读] Il/IlDecoder.cs、Model/MethodDef.cs
**依赖**：无（软读 T301 文档）

## T308 反射操作接线：MethodInfo.Invoke/FieldInfo.SetValue（60 分钟）
**背景**：查询（T303）+ Invoker（T304）就绪；System.Reflection 类型接线未做。
**任务**：1. 读 `[读] il2cpp_reflquery.h` + `[读] il2cpp_invoker.h` → 2. `[写] Emit/MethodMap.Reflection.cs`：注册 "System.Reflection.MethodInfo"（Invoke → invoker 调用）+ "System.Reflection.FieldInfo"（GetValue/SetValue，GC 引用字段经 box/unbox）+ "System.Reflection.PropertyInfo" → 3. `[写] tests/cases/reflectops/`（Invoke 用户静态/实例方法 + SetValue 字段 → 行为变化断言）→ 4. 速查 e2e 双跑
**文件域**：[写] Emit/MethodMap.Reflection.cs、tests/cases/reflectops/*；[读] il2cpp_reflquery.h、il2cpp_invoker.h、MethodMapHub.cs
**依赖**：T303 + T304 文件存在

## T309 反射 e2e 用例集：三能力矩阵（60 分钟）
**背景**：反射 = 调用函数/设置属性/设置字段全能力（铁律 2g-2 原文）。
**任务**：1. 读 `[读] tests/cases/reflectops/`（T308）→ 2. `[写] tests/cases/reflectfull/`：三个子场景（Invoke 与直接调用结果一致 / SetValue 与直接赋值结果一致 / 构造反射实例化 Activator.CreateInstance）→ 3. 速查 e2e 双跑逐字节一致 → 4. 缺口记录本卡 GAPS.md（私有）
**文件域**：[写] tests/cases/reflectfull/*（含 GAPS.md）；[读] tests/cases/reflectops/*、MethodMapHub.cs
**依赖**：T308 文件存在

## T310 反射三模式矩阵验证脚本（60 分钟）
**背景**：L0/L1/L2 裁剪 × 模式 A/B/C 需要矩阵验证。
**任务**：1. 读 `[读] scripts/run-tests.ps1`（双跑 harness 复用）→ 2. `[写] scripts/run-reflect-matrix.ps1`：三模式 × 固定反射 fixture 矩阵（--reflect=full / list / 静态分析）→ 逐模式转译+双跑 → 汇总表 → 3. 跑通 min 矩阵（至少 full 模式 + 静态模式各 1 fixture）→ 4. `[写] docs/architecture/反射验证矩阵.md`（结果 + 失败项上报）
**文件域**：[写] scripts/run-reflect-matrix.ps1、docs/architecture/反射验证矩阵.md；[读] scripts/run-tests.ps1
**依赖**：T305 + T306 + T307 文件存在（软：脚本对缺失模式容错）

---

# 四、转译器优化 ×20（T401-T420）

**并行机制**：T401 建 `Ir/PassRunner.cs`（IOptimizationPass 接口 + 反射自发现 + 在 StackFold.cs 产 IR 后挂接，StackFold.cs 唯一写者）；其余卡各自新建 `Ir/Pass<名称>.cs`（实现接口，无需改任何共享文件）+ `tests/unit/` 单测自证。T408/T410/T412/T414/T415/T416/T417 为既有文件的唯一写者。

## T401 优化管线骨架：PassRunner（60 分钟）
**背景**：多个优化点需要统一挂接点且不互相冲突。
**任务**：1. 读 `[读] src/il2cppc/Ir/StackFold.cs` + `[读] src/il2cppc/Ir/Stmt.cs` + `[读] src/il2cppc/Ir/Expr.cs`（IR 结构）→ 2. `[写] src/il2cppc/Ir/IOptimizationPass.cs`（`string Name; Stmt[] Apply(Stmt[] ir)`）+ `[写] src/il2cppc/Ir/PassRunner.cs`（程序集反射发现全部实现类，顺序执行，skip 异常 pass 并告警）→ 3. 修改 `[写] src/il2cppc/Ir/StackFold.cs`（唯一写者）：IR 产出末尾调用 `PassRunner.Run(stmts)` → 4. `[写] tests/unit/PassRunnerTests.cs`（注册 2 个假 pass 验证顺序与异常隔离）→ 5. `dotnet build` + `dotnet test` + 现有 e2e 抽查（mathcase）回归
**文件域**：[写] src/il2cppc/Ir/IOptimizationPass.cs、Ir/PassRunner.cs、Ir/StackFold.cs（本卡独占）、tests/unit/PassRunnerTests.cs；[读] Ir/Stmt.cs、Ir/Expr.cs、Ir/Normalize.cs
**依赖**：无

## T402 属性折叠 v1：只读 getter → 字段读取（60 分钟）
**背景**：属性折叠设计.md 要求调用点折叠（性能路线图阶段 1 首项）。
**任务**：1. 读 `[读] docs/architecture/属性折叠设计.md` + `[读] Ir/PassRunner.cs`（T401）→ 2. `[写] src/il2cppc/Ir/PassPropertyFold.cs`：识别 `call get_X`（X 为只读简单属性，无参数、体为单字段 return）→ 替换为字段读（保守：不折叠虚属性/带副作用/ldftn 引用）→ 3. `[写] tests/unit/PassPropertyFoldTests.cs`（折叠命中/不折叠场景矩阵）→ 4. `dotnet build` + `dotnet test` → 5. e2e fixture `[写] tests/cases/propfold/`（属性密集循环）双跑一致
**文件域**：[写] src/il2cppc/Ir/PassPropertyFold.cs、tests/unit/PassPropertyFoldTests.cs、tests/cases/propfold/*；[读] 属性折叠设计.md、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T403 属性折叠 v2：setter/常量属性（60 分钟）
**背景**：getter 折叠（T402）已立；setter 与常量 getter 未覆盖。
**任务**：1. 读 `[读] Ir/PassPropertyFold.cs`（T402）→ 2. `[写] src/il2cppc/Ir/PassPropertyFold2.cs`：setter（体为单字段赋值）→ 字段写；常量属性（体为 return 常量）→ 常量传播 → 3. `[写] tests/unit/PassPropertyFold2Tests.cs` → 4. `dotnet build` + `dotnet test` → 5. e2e 追加断言（本卡 fixture 或复用 propfold 只读目录——追加 propfold/Props2.cs）
**文件域**：[写] src/il2cppc/Ir/PassPropertyFold2.cs、tests/unit/PassPropertyFold2Tests.cs、tests/cases/propfold/Props2.cs（追加）；[读] Ir/PassPropertyFold.cs、Ir/PassRunner.cs
**依赖**：T401 + T402 文件存在

## T404 常量折叠扩展：int64 常量运算（50 分钟）
**背景**：TryFoldInt32 已存在于发射期（CodeGenerator.cs）；int64 未覆盖。
**任务**：1. 读 `[读] src/il2cppc/Emit/CodeGenerator.cs`（TryFoldInt32 语义：unchecked 回绕 + 符号扩展）→ 2. `[写] src/il2cppc/Ir/PassFoldInt64.cs`：IR 层 int64 常量二元运算折叠（与发射期折叠互补，不重复发射期改动）→ 3. `[写] tests/unit/PassFoldInt64Tests.cs`（溢出回绕、符号扩展、负数）→ 4. `dotnet build` + `dotnet test`
**文件域**：[写] src/il2cppc/Ir/PassFoldInt64.cs、tests/unit/PassFoldInt64Tests.cs；[读] Emit/CodeGenerator.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T405 常量折叠扩展：字符串常量拼接（50 分钟）
**背景**：相邻 ldstr + concat 可编译期合并（性能路线图阶段 1 第 2 项）。
**任务**：1. 读 `[读] src/il2cppc/Ir/Expr.cs`（常量节点形态）→ 2. `[写] src/il2cppc/Ir/PassFoldString.cs`：`String.Concat(ldstr...ldstr)` 且参数全为编译期字符串 → 折叠为单 ldstr → 3. `[写] tests/unit/PassFoldStringTests.cs`（2/3/4 参数、混合非常量不折叠）→ 4. `dotnet build` + `dotnet test` → 5. e2e `[写] tests/cases/foldstr/` 双跑
**文件域**：[写] src/il2cppc/Ir/PassFoldString.cs、tests/unit/PassFoldStringTests.cs、tests/cases/foldstr/*；[读] Ir/Expr.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T406 常量折叠扩展：枚举/const 字段（45 分钟）
**背景**：枚举常量与 const 字段引用可折叠为字面量。
**任务**：1. 读 `[读] src/il2cppc/Ir/Expr.cs`（字段引用形态）+ `[读] Model/FieldDef.cs` → 2. `[写] src/il2cppc/Ir/PassFoldEnum.cs`：静态字面量字段（const/enum 成员）读取 → 常量替换 → 3. `[写] tests/unit/PassFoldEnumTests.cs` → 4. `dotnet build` + `dotnet test`
**文件域**：[写] src/il2cppc/Ir/PassFoldEnum.cs、tests/unit/PassFoldEnumTests.cs；[读] Ir/Expr.cs、Model/FieldDef.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T407 temp 最小化：只写不读 temp 消除（55 分钟）
**背景**：temp 仅被赋值未读取 → 不声明不发射（性能路线图阶段 1 第 3 项，M5 体量下必须）。
**任务**：1. 读 `[读] src/il2cppc/Ir/Stmt.cs`（temp 赋值/读取节点）→ 2. `[写] src/il2cppc/Ir/PassDeadTemp.cs`：数据流一遍：只写不读 temp → 删除赋值语句（副作用保留检查：调用结果 temp 不可删）→ 3. `[写] tests/unit/PassDeadTempTests.cs`（纯值 temp 删、调用结果 temp 保、跨 join 点不误删）→ 4. `dotnet build` + `dotnet test` → 5. e2e 抽查 mathcase 回归
**文件域**：[写] src/il2cppc/Ir/PassDeadTemp.cs、tests/unit/PassDeadTempTests.cs；[读] Ir/Stmt.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T408 栈帧根：refLocals 为空跳过 WrapFrameRoots（55 分钟）
**背景**：WrapFrameRoots 每方法无条件包裹（CodeGenerator.cs:176 区域，性能路线图阶段 1 末项）。
**任务**：1. 读 `[读] src/il2cppc/Emit/CodeGenerator.cs`（WrapFrameRoots 调用点上下文 + refLocals 判定）→ 2. 修改 `[写] src/il2cppc/Emit/CodeGenerator.cs`（唯一写者）：方法无引用局部 → 跳过包裹（保帧记录序言不受影响——只跳过 root 注册，不跳 framestack push）→ 3. `[写] tests/cases/noframelocals/`（纯值方法 + 引用局部方法混合）→ 4. 速查 e2e 双跑 + `[写] tests/cases/gcstress/` 只读回归（gcstress 用例双跑）
**文件域**：[写] src/il2cppc/Emit/CodeGenerator.cs（本卡独占）、tests/cases/noframelocals/*；[读] tests/cases/gcstress/*、Ir/Stmt.cs
**依赖**：无

## T409 分支条件归一化（45 分钟）
**背景**：Ceq(e,0) 反写为 e==0 剥多余括号（性能路线图阶段 1 第 4 项，EmitCondition 已有）。
**任务**：1. 读 `[读] src/il2cppc/Ir/Expr.cs`（比较节点形态）→ 2. `[写] src/il2cppc/Ir/PassNormalizeCmp.cs`：`Ceq(e,0)`/`Ceq(0,e)` → 逻辑非形态；双重否定折叠 → 3. `[写] tests/unit/PassNormalizeCmpTests.cs` → 4. `dotnet build` + `dotnet test` → 5. e2e 抽查 branchcase 回归（只读）
**文件域**：[写] src/il2cppc/Ir/PassNormalizeCmp.cs、tests/unit/PassNormalizeCmpTests.cs；[读] Ir/Expr.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T410 尾调用直返优化（60 分钟）
**背景**：方法尾部调用后立即返回 → 消除中间调用帧（用户点名尾调用）。
**任务**：1. 读 `[读] src/il2cppc/Ir/Stmt.cs`（调用 + 返回节点）→ 2. `[写] src/il2cppc/Ir/PassTailCall.cs`：识别"调用结果直接 return"（含同一方法自递归）→ 标记/改写为直接返回表达式（不产生新调用形态）→ 3. 修改 `[写] src/il2cppc/Emit/MethodEmitter.cs`（唯一写者）：尾调用标记发射为直接 return（保帧记录序言，仅省调用后无操作）→ 4. `[写] tests/unit/PassTailCallTests.cs` + `[写] tests/cases/tailcall/`（互递归/自递归深度用例）→ 5. `dotnet build` + `dotnet test` + e2e 双跑
**文件域**：[写] src/il2cppc/Ir/PassTailCall.cs、src/il2cppc/Emit/MethodEmitter.cs（本卡独占）、tests/unit/PassTailCallTests.cs、tests/cases/tailcall/*；[读] Ir/Stmt.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T411 死代码扩展：不可达赋值链清理（50 分钟）
**背景**：死块消除已做（CodeGenerator.cs:215-264）；赋值链级死代码未做。
**任务**：1. 读 `[读] src/il2cppc/Ir/Stmt.cs` → 2. `[写] src/il2cppc/Ir/PassDeadStore.cs`：赋值后无读取且目标非引用 → 删除；连锁（a=x; b=a; 均死 → 全删）→ 3. `[写] tests/unit/PassDeadStoreTests.cs`（链式死赋值、引用类型不删、跨块保守）→ 4. `dotnet build` + `dotnet test`
**文件域**：[写] src/il2cppc/Ir/PassDeadStore.cs、tests/unit/PassDeadStoreTests.cs；[读] Ir/Stmt.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T412 编译期字符串池（60 分钟）
**背景**：每个 ldstr 运行时新分配（性能路线图阶段 2 第 3 项：静态表索引读取）。
**任务**：1. 读 `[读] src/il2cppc/Emit/CppWriter.cs`（生成文件结构）+ `[读] src/il2cppc/Emit/MethodEmitter.cs`（ldstr 发射点，只读）→ 2. 修改 `[写] src/il2cppc/Emit/CppWriter.cs`（唯一写者）：收集全程序字符串字面量 → 发射静态构造表（一次性 il2cpp_string_new_from_cs）→ 3. `[写] src/il2cppc/Ir/PassStringPool.cs`：ldstr → 表索引读取（唯一写者该文件）→ 4. `[写] tests/unit/PassStringPoolTests.cs`（去重/编码转义/非 ASCII）→ 5. `dotnet build` + `dotnet test` + e2e 双跑抽查（stringcase 只读回归）
**文件域**：[写] src/il2cppc/Emit/CppWriter.cs（本卡独占）、src/il2cppc/Ir/PassStringPool.cs、tests/unit/PassStringPoolTests.cs；[读] Emit/MethodEmitter.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T413 null 检查内联发射（50 分钟）
**背景**：il2cpp_null_check 每次解引用一次调用（性能路线图阶段 2 第 4 项）。
**任务**：1. 读 `[读] src/il2cppc/Emit/CodeGenerator.cs`（null_check 发射点，只读）+ `[读] src/runtime/include/internal/il2cpp_runtime.h`（既有宏形态，只读）→ 2. `[写] src/il2cppc/Ir/PassInlineNullCheck.cs`：解引用前 null 检查 → `if(!obj) throw` 直接形态（含 __builtin_expect 提示，分编译器宏）→ 3. `[写] tests/unit/PassInlineNullCheckTests.cs`（每块插入一次、循环外提升不越界——保守不提升）→ 4. `dotnet build` + `dotnet test` + 异常 e2e 回归（exceptions 只读）
**文件域**：[写] src/il2cppc/Ir/PassInlineNullCheck.cs、tests/unit/PassInlineNullCheckTests.cs；[读] Emit/CodeGenerator.cs、il2cpp_runtime.h、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T414 简单 icall 头文件内联化（55 分钟）
**背景**：简单 icall 跨 TU 调用有开销（性能路线图阶段 2 第 2 项）。
**任务**：1. 读 `[读] src/runtime/include/internal/il2cpp_runtime.h`（icall 声明区 104-134 附近）→ 2. 修改 `[写] il2cpp_runtime.h`（唯一写者）：为 5 个最简单 icall（如 environment_get_newline、string 长度类）提供 `static inline` 内联实现（IL2CPP_FORCE_INLINE 宏 + 原声明保留）→ 3. `[写] tests/runtime/test_inlineicall.cpp`（行为与跨 TU 调用一致）→ 4. cmake 全绿 → 5. 反汇编抽查（dumpbin/objdump 确认调用点无 call 或内联）
**文件域**：[写] src/runtime/include/internal/il2cpp_runtime.h（本卡独占）、tests/runtime/test_inlineicall.cpp；[读] src/runtime/src/runtime.cpp、src/runtime/src/environment.cpp
**依赖**：无

## T415 接口分派哈希化（60 分钟）
**背景**：接口分派线性扫描（metadata.cpp:116-154 注释明示 M2 线性、M3 哈希待做；性能路线图阶段 2 第 6 项）。
**任务**：1. 读 `[读] src/runtime/src/metadata.cpp`（接口查找路径）→ 2. 修改 `[写] metadata.cpp`（唯一写者）：iface → entry 哈希表（初始化期构建，查找 O(1)）→ 3. `[写] tests/runtime/test_ifacehash.cpp`（多接口/继承链命中 + 未实现返回负）→ 4. cmake 全绿 → 5. e2e 回归（interface/virtual 用例只读双跑）
**文件域**：[写] src/runtime/src/metadata.cpp（本卡独占）、tests/runtime/test_ifacehash.cpp；[读] src/runtime/include/internal/il2cpp_metadata.h、il2cpp_hashtable.h
**依赖**：无

## T416 栈根区间批量注册（55 分钟）
**背景**：帧记录 push/pop 每方法一次；栈根区间可批量（性能路线图阶段 2 第 5 项配套）。
**任务**：1. 读 `[读] src/runtime/src/framestack.cpp` + `[读] src/runtime/include/internal/il2cpp_frame.h` → 2. 修改 `[写] framestack.cpp`（唯一写者）+ `[写] il2cpp_frame.h`（唯一写者）：批量注册 API（一次调用注册连续地址区间根，替代逐根注册）→ 3. `[写] tests/runtime/test_framebatch.cpp`（批量区间扫描与逐根等价）→ 4. cmake 全绿 → 5. gcstress 回归（只读双跑）
**文件域**：[写] src/runtime/src/framestack.cpp（本卡独占）、src/runtime/include/internal/il2cpp_frame.h（本卡独占）、tests/runtime/test_framebatch.cpp；[读] src/runtime/src/gc.cpp（扫描侧只读）
**依赖**：无

## T417 TLAB：线程本地分配缓冲（60 分钟）
**背景**：分代 GC 已就绪；Gen0 bump 指针 per-thread 降锁竞争（性能路线图阶段 2 第 1 项）。
**任务**：1. 读 `[读] src/runtime/src/gc.cpp`（Gen0 bump 分配路径）+ `[读] il2cpp_gc.h` → 2. 修改 `[写] src/runtime/src/gc.cpp`（唯一写者）+ `[写] include/internal/il2cpp_gc.h`（唯一写者）：per-thread 小对象缓冲（TLAB 风格，锁内补充），大对象直入 LOH → 3. `[写] tests/runtime/test_tlab.cpp`（多线程分配压力 + 代际晋升正确）→ 4. cmake 全绿 → 5. gcstress/gc 用例只读回归
**文件域**：[写] src/runtime/src/gc.cpp（本卡独占）、include/internal/il2cpp_gc.h（本卡独占）、tests/runtime/test_tlab.cpp；[读] il2cpp_allocator.h、src/allocator.cpp
**依赖**：无

## T418 调用约定研究：>N 参数传递分析（60 分钟）
**背景**：现有 ABI this=arg0；大参数数量栈传开销未量化（性能路线图阶段 3 调用约定项）。
**任务**：1. 读 `[读] docs/architecture/overview.md` + `[读] docs/architecture/jit-预留.md`（ABI 部分）→ 2. 反汇编抽样：dumpbin/objdump 对比 0-12 参数方法序言（`[写] tests/cases/callconv/` fixture 生成 >8 参数方法 + 反汇编记录）→ 3. `[写] docs/architecture/调用约定研究.md`：证据 + 结论（保持现状 / 或有限寄存器传递方案 + 影响面）→ 4. 结论纳入性能路线图更新（新文件，不覆盖原文档）
**文件域**：[写] tests/cases/callconv/*、docs/architecture/调用约定研究.md；[读] overview.md、jit-预留.md、性能路线图.md
**依赖**：无

## T419 CSE 实测与实现：跨语句重复子表达式（60 分钟）
**背景**：跨语句重复子表达式生成器侧可折叠（性能路线图阶段 3 首项，先实测后实现）。
**任务**：1. 读 `[读] tests/bench/`（T702 产出，若已存在）+ `[读] src/il2cppc/Ir/Expr.cs` → 2. 反汇编实测：循环内重复计算热点（`[写] tests/cases/cseprobe/`）→ 3. `[写] src/il2cppc/Ir/PassCse.cs`（同一基本块内相同子表达式 → 复用 temp；保守：不跨 join 点）→ 4. `[写] tests/unit/PassCseTests.cs` → 5. `dotnet build` + `dotnet test` + 双跑回归；实测证据写入 GAPS/结论到 `[写] docs/architecture/CSE-实测.md`
**文件域**：[写] src/il2cppc/Ir/PassCse.cs、tests/unit/PassCseTests.cs、tests/cases/cseprobe/*、docs/architecture/CSE-实测.md；[读] Ir/Expr.cs、Ir/PassRunner.cs
**依赖**：T401 文件存在

## T420 内联策略研究：AggressiveInlining（60 分钟）
**背景**：禁内联是 StackTrace/帧记录铁律（memory.md 记录）；属性折叠是合法替代，AggressiveInlining 需带证明实施（性能路线图阶段 3 内联项）。
**任务**：1. 读 `[读] docs/architecture/属性折叠设计.md` + `[读] src/runtime/src/framestack.cpp`（帧记录语义）→ 2. `[写] docs/architecture/AggressiveInlining-研究.md`：内联方法帧记录/StackTrace 语义影响证明（内联后帧不出现 → 与铁律冲突分析 + 方案：仅标记方法且调用点保持帧记录序言的"半内联"或拒绝清单）→ 3. `[写] tests/cases/inlineprobe/`（标 AggressiveInlining 方法调用 + StackTrace 断言现状）→ 4. 结论 + 推荐方案入库（不实现，证据先行）
**文件域**：[写] docs/architecture/AggressiveInlining-研究.md、tests/cases/inlineprobe/*；[读] 属性折叠设计.md、framestack.cpp、性能路线图.md
**依赖**：无

---

# 五、e2e 用例扩量 ×15（T501-T515）

**模式**：每卡新建 1 个 fixture 目录 `tests/cases/<name>/`（csproj net48 + 主逻辑），速查双跑；
转译缺口记录到本卡 GAPS.md（私有），不修共享文件。

## T501 e2e 泛型嵌套用例（40 分钟）
**背景**：泛型共享代码机制已就绪；嵌套泛型组合未充分覆盖。
**任务**：1. 读 `[读] tests/cases/generics/` 现有用例 → 2. `[写] tests/cases/gennest/`（List<List<int>>、Dictionary<string,List<int>>、泛型方法嵌套调用）→ 3. 速查双跑；缺口记录 GAPS.md
**文件域**：[写] tests/cases/gennest/*；[读] tests/cases/generics/*
**依赖**：无

## T502 e2e 值类型数组装箱遍历用例（40 分钟）
**背景**：装箱语义已有实现；值类型数组遍历装箱路径待覆盖。
**任务**：1. 读 `[读] tests/cases/boxing/` → 2. `[写] tests/cases/boxarray/`（struct[] → object[] 装箱往返 + 拆箱修改）→ 3. 速查双跑
**文件域**：[写] tests/cases/boxarray/*；[读] tests/cases/boxing/*
**依赖**：无

## T503 e2e 委托链多播用例（40 分钟）
**背景**：delegatecase 已覆盖基础；多播 +=/-= 顺序未覆盖。
**任务**：1. 读 `[读] tests/cases/delegates/` → 2. `[写] tests/cases/delegatemulticast/`（+= 3 个处理器顺序输出、-= 移除、返回值聚合语义）→ 3. 速查双跑
**文件域**：[写] tests/cases/delegatemulticast/*；[读] tests/cases/delegates/*
**依赖**：无

## T504 e2e 嵌套异常 finally 用例（40 分钟）
**背景**：exceptions/exdeep 已覆盖单层；嵌套 try/finally + 重抛未覆盖。
**任务**：1. 读 `[读] tests/cases/exceptions/` → 2. `[写] tests/cases/nestexcept/`（try{try{throw}finally{...}}catch 双层 + finally 内异常吞噬）→ 3. 速查双跑
**文件域**：[写] tests/cases/nestexcept/*；[读] tests/cases/exceptions/*
**依赖**：无

## T505 e2e 字符串 interning 一致性用例（40 分钟）
**背景**：stringintern 已实现；双跑 intern 语义一致性未覆盖。
**任务**：1. 读 `[读] tests/cases/intern/` → 2. `[写] tests/cases/interncase2/`（常量字符串引用相等、动态构建 IsInterned 结果）→ 3. 速查双跑（注意 net48 与 AOT intern 时机差异——断言仅涉公开语义）
**文件域**：[写] tests/cases/interncase2/*；[读] tests/cases/intern/*
**依赖**：无

## T506 e2e checked/unchecked 溢出用例（40 分钟）
**背景**：overflow 用例已存在；checked 显式块未覆盖。
**任务**：1. 读 `[读] tests/cases/overflow/` → 2. `[写] tests/cases/checkedcase/`（checked 加乘抛 OverflowException、unchecked 回绕、嵌套混合）→ 3. 速查双跑
**文件域**：[写] tests/cases/checkedcase/*；[读] tests/cases/overflow/*
**依赖**：无

## T507 e2e 多线程共享状态用例（40 分钟）
**背景**：threadcase 已覆盖基础线程；共享状态竞争语义未覆盖。
**任务**：1. 读 `[读] tests/cases/threadcase/` → 2. `[写] tests/cases/threadshare/`（N 线程 lock 累加 → 断言最终值；不用 Interlocked 依赖确定性输出）→ 3. 速查双跑
**文件域**：[写] tests/cases/threadshare/*；[读] tests/cases/threadcase/*
**依赖**：无

## T508 e2e 递归深度 + 帧记录用例（40 分钟）
**背景**：recurse 已覆盖递归；深递归 StackTrace/帧行为未覆盖。
**任务**：1. 读 `[读] tests/cases/recurse/` + `[读] tests/cases/exdeep/` → 2. `[写] tests/cases/deepframe/`（深度 10000 递归 + 每层帧计数输出边界值）→ 3. 速查双跑
**文件域**：[写] tests/cases/deepframe/*；[读] tests/cases/recurse/*、tests/cases/exdeep/*
**依赖**：无

## T509 e2e 属性折叠形态反汇编用例（40 分钟）
**背景**：T402 折叠落地后需形态验证（属性折叠设计.md §5.2 反汇编断言）。
**任务**：1. 读 `[读] docs/architecture/属性折叠设计.md` → 2. `[写] tests/cases/propasm/`（属性密集热循环）→ 3. 速查双跑 + `[写] tests/cases/propasm/asm-notes.md`：objdump 抽查结论（无 get_X 调用即折叠生效；T402 未落地则记录为预期失败证据）
**文件域**：[写] tests/cases/propasm/*（含 asm-notes.md）；[读] 属性折叠设计.md
**依赖**：无（与 T402 互不影响——断言记录而非硬失败）

## T510 e2e 枚举解析/格式化往返用例（40 分钟）
**背景**：Enum 域接线（T110）后需往返覆盖。
**任务**：1. 读 `[读] tests/cases/enum2/`（T110 产出，若未完成则自建等效场景）→ 2. `[写] tests/cases/enumround/`（Parse→ToString→Parse 链、Flags 组合、未知值回退）→ 3. 速查双跑
**文件域**：[写] tests/cases/enumround/*；[读] tests/cases/enum2/*
**依赖**：无

## T511 e2e P/Invoke 回调注册表用例（40 分钟）
**背景**：pinvokeext 回调注册表已实现；多回调/跨线程回调未覆盖。
**任务**：1. 读 `[读] tests/cases/pinvokeext/` → 2. `[写] tests/cases/pinvokecb/`（注册 3 个回调轮询 + 回调中调用托管方法再回调）→ 3. 速查双跑
**文件域**：[写] tests/cases/pinvokecb/*（含 mylib/ 原生库）；[读] tests/cases/pinvokeext/*
**依赖**：无

## T512 e2e GC 压力 + 弱引用用例（40 分钟）
**背景**：gccase/gcstress 已覆盖基础；WeakReference 回收语义未覆盖。
**任务**：1. 读 `[读] tests/cases/gccase/` + `[读] tests/cases/gcstress/` → 2. `[写] tests/cases/weakrefcase/`（强引用存活 → 置空 → GC.Collect → IsAlive 翻转；输出确定性布尔序列）→ 3. 速查双跑
**文件域**：[写] tests/cases/weakrefcase/*；[读] tests/cases/gccase/*
**依赖**：无

## T513 e2e 静态构造多线程触发用例（40 分钟）
**背景**：cctor 已覆盖单线程；多线程并发触发未覆盖。
**任务**：1. 读 `[读] tests/cases/cctor/` → 2. `[写] tests/cases/cctorrace/`（多线程首访同一类型 → cctor 执行一次断言 + 输出确定）→ 3. 速查双跑
**文件域**：[写] tests/cases/cctorrace/*；[读] tests/cases/cctor/*
**依赖**：无

## T514 e2e 文化相关格式化用例（40 分钟）
**背景**：CultureInfo 已接线（L255）；文化格式化未覆盖。
**任务**：1. 读 `[读] tests/cases/timecase/` + `[读] cultureinfo.h` → 2. `[写] tests/cases/culturefmt/`（固定 CultureInfo("en-US")/("zh-CN") 数字/日期格式化输出；规避操作系统差异——只用固定指定文化）→ 3. 速查双跑
**文件域**：[写] tests/cases/culturefmt/*；[读] tests/cases/timecase/*、include/internal/cultureinfo.h
**依赖**：无

## T515 e2e 集合 + LINQ 组合用例（40 分钟）
**背景**：LINQ 域（T204-T206）落地后组合场景覆盖。
**任务**：1. 读 `[读] tests/cases/hashtable2/` → 2. `[写] tests/cases/linqcomb/`（List + Where + OrderBy + GroupBy + Sum 组合流水线输出）→ 3. 速查双跑；缺口记录 GAPS.md
**文件域**：[写] tests/cases/linqcomb/*（含 GAPS.md）；[读] tests/cases/hashtable2/*
**依赖**：无

---

# 六、文档/设计 ×10（T601-T610）

## T601 BCL 接线模式设计文档（45 分钟）
**背景**：MethodMapHub 机制（T101）需要权威文档供 34 个域卡照抄。
**任务**：1. 读 `[读] src/il2cppc/Emit/MethodMapHub.cs`（T101，若未完成则按 T101 卡描述写）→ 2. `[写] docs/architecture/BCL接线模式.md`：hub 注册模式、RawSignature 解码速查表、I() 助手语义（memory.md 内容规范化）、域卡清单 → 3. 交叉核对：与 `[读] src/il2cppc/Emit/MethodRefResolver.cs` 既有 dispatch 风格一致
**文件域**：[写] docs/architecture/BCL接线模式.md；[读] MethodMapHub.cs、MethodRefResolver.cs
**依赖**：无（文档随 T101 状态自适应）

## T602 新 runtime 域 API 约定文档（45 分钟）
**背景**：新域卡（T2xx）需要统一 API 约定（头文件 doc 块/icall 命名/-Wall -Wextra 门）。
**任务**：1. 读 `[读] src/runtime/include/internal/il2cpp_convertbase64.h` + `[读] src/runtime/include/internal/il2cpp_math.h`（doc 块约定）→ 2. `[写] docs/architecture/runtime域约定.md`：头文件/源文件/测试文件三件套规范、icall 命名 `icall_<域>_<名>`、错误类型映射表（ArgumentNull/Format/Overflow 等）、CMake 自动发现说明 → 3. 新增域命名索引（T201-T230 文件域清单）
**文件域**：[写] docs/architecture/runtime域约定.md；[读] il2cpp_convertbase64.h、il2cpp_math.h、tests/runtime/CMakeLists.txt
**依赖**：无

## T603 反射三模式落地总设计（60 分钟）
**背景**：REQUIREMENTS 铁律 2m 三模式需要总设计文档串联 T301-T310。
**任务**：1. 读 `[读] docs/REQUIREMENTS.md`（铁律 2m/2g-2 原文）+ `[读] docs/architecture/反射元数据设计.md`（T301）→ 2. `[写] docs/architecture/反射三模式设计.md`：三模式决策矩阵（何时用哪个）、按需粒度两级裁剪、与保留集（静态可达图）合并规则、验证标准 → 3. 与 T301-T310 卡交叉核对无缺口
**文件域**：[写] docs/architecture/反射三模式设计.md；[读] REQUIREMENTS.md、反射元数据设计.md
**依赖**：T301 文件存在（软）

## T604 转译器优化管线设计（45 分钟）
**背景**：PassRunner（T401）+ 19 个 pass 需要架构文档。
**任务**：1. 读 `[读] src/il2cppc/Ir/PassRunner.cs`（T401）→ 2. `[写] docs/architecture/优化管线设计.md`：pass 顺序语义（折叠→死代码→归一化）、异常隔离策略、每 pass 的 IR 不变量（不破坏帧记录/语义）、新增 pass 步骤模板 → 3. 性能路线图各阶段 ↔ pass 卡映射表
**文件域**：[写] docs/architecture/优化管线设计.md；[读] Ir/PassRunner.cs、性能路线图.md
**依赖**：T401 文件存在（软）

## T605 基准体系设计（50 分钟）
**背景**：性能路线图阶段 4 基准体系未设计落地。
**任务**：1. 读 `[读] docs/architecture/性能路线图.md`（阶段 4 组件清单）→ 2. `[写] docs/architecture/基准体系设计.md`：tests/bench 目录结构、微基准模板（循环 N 次计时 + 输出校验防优化消除）、net48 双跑对比流程、基线入库格式、波动阈值（10%）规则 → 3. 与 T702 核对衔接
**文件域**：[写] docs/architecture/基准体系设计.md；[读] 性能路线图.md、scripts/run-tests.ps1
**依赖**：无

## T606 e2e 用例编写规范（40 分钟）
**背景**：15 个新 e2e 卡需要统一 fixture 规范。
**任务**：1. 读 `[读] tests/cases/consolecase/` + `[读] tests/cases/mathcase/`（模板形态）+ `[读] scripts/run-tests.ps1`（fixture 要求：csproj net48、主逻辑、mylib 约定）→ 2. `[写] docs/architecture/e2e用例规范.md`：目录结构、csproj 模板、确定性输出原则（禁随机/禁绝对时间/禁环境差异）、GAPS.md 记录约定 → 3. 命名登记表（现有 63 + 本清单新增，防重名）
**文件域**：[写] docs/architecture/e2e用例规范.md；[读] tests/cases/consolecase/*、scripts/run-tests.ps1
**依赖**：无

## T607 三编译器矩阵 CI 设计（50 分钟）
**背景**：三编译器验证常态化（路线图 M6）需要 CI 设计。
**任务**：1. 读 `[读] scripts/run-tests.ps1`（三编译器分支）+ `[读] scripts/vcvars.ps1` → 2. `[写] docs/architecture/三编译器CI设计.md`：本地矩阵脚本调用方式（mingw/msvc/wsl 循环）、WSL Ubuntu1804 环境固化说明、失败分类（转译失败/构建失败/双跑不一致）、CI 配置草案（GitHub Actions yaml 建议）→ 3. 与 T701 衔接核对
**文件域**：[写] docs/architecture/三编译器CI设计.md；[读] scripts/run-tests.ps1、scripts/vcvars.ps1
**依赖**：无

## T608 泛型特化/共享策略文档（45 分钟）
**背景**：泛型共享代码机制（引用共享/值类型特化）需权威现状文档。
**任务**：1. 读 `[读] docs/superpowers/specs/2026-08-02-il2cpp-design.md`（4.4 泛型机制）+ `[读] src/il2cppc/Emit/VTablePlanner.cs` → 2. `[写] docs/architecture/泛型策略.md`：共享代码布局、值类型特化限量（每基元 1 份）、T 映射机制（Il2CppClass* 隐藏参数）、边界（动态实例化 → M7 解释器）→ 3. 附已知缺口清单
**文件域**：[写] docs/architecture/泛型策略.md；[读] il2cpp-design.md、Emit/VTablePlanner.cs
**依赖**：无

## T609 裁剪 L0/L1/L2 分级规范（50 分钟）
**背景**：M5.5 裁剪分级验证前置（路线图）。
**任务**：1. 读 `[读] docs/REQUIREMENTS.md`（裁剪相关）+ `[读] docs/architecture/路线图.md`（M5.5）→ 2. `[写] docs/architecture/裁剪分级规范.md`：L0（全量）/L1（默认裁剪集）/L2（最小集）定义、保留规则（静态可达 ∪ 反射保留清单）、验证方法（分级双跑一致）→ 3. 与反射模式（T305-T307）边界划分
**文件域**：[写] docs/architecture/裁剪分级规范.md；[读] REQUIREMENTS.md、路线图.md、反射模式A/B/C.md
**依赖**：无

## T610 性能对齐基线文档：net48 对比方法学（50 分钟）
**背景**：铁律 2e（性能 >= 官方 JIT）需要方法学文档。
**任务**：1. 读 `[读] docs/architecture/性能路线图.md` + `[读] docs/architecture/基准体系设计.md`（T605）→ 2. `[写] docs/architecture/性能对齐基线.md`：对比协议（同机同输入、net48 参考、计时方法、噪声控制：重复取中位数、CPU 绑定）、首批基线项清单（整数/分配/虚调用/接口/字符串/装箱/委托）、反汇编证据要求 → 3. 基线数据记录格式模板
**文件域**：[写] docs/architecture/性能对齐基线.md；[读] 性能路线图.md、基准体系设计.md
**依赖**：T605 文件存在（软）

---

# 七、验证/工具 ×8（T701-T708）

## T701 三编译器矩阵批量脚本（60 分钟）
**背景**：run-tests.ps1 单编译器；矩阵批量验证需循环脚本（遇失败中止语义需处理）。
**任务**：1. 读 `[读] scripts/run-tests.ps1`（全量结构、ErrorActionPreference=Stop、失败中止语义）→ 2. `[写] scripts/run-matrix.ps1`：参数 `-Compilers mingw,msvc,wsl -Case <name>`（默认全部）→ 逐编译器循环调用 run-tests.ps1，单用例失败继续下一编译器（-ContinueOnError 开关），汇总报告表 → 3. 用 2-3 个已知绿用例冒烟跑通 → 4. 用法写入 `[写] scripts/NOTES-701.md`（README.md 统一由 T706 维护）
**文件域**：[写] scripts/run-matrix.ps1、scripts/NOTES-701.md；[读] scripts/run-tests.ps1
**依赖**：无

## T702 基准骨架：tests/bench 目录 + 微基准模板（60 分钟）
**背景**：性能路线图阶段 4 需要 tests/bench/ 起步（最小骨架）。
**任务**：1. 读 `[读] docs/architecture/基准体系设计.md`（T605，若未完成按性能路线图阶段 4 组件清单）→ 2. `[写] tests/bench/bench_template.cpp`（循环计时模板：N 次迭代 + 结果累加防消除 + 输出校验）+ `[写] tests/bench/CMakeLists.txt`（bench 独立目标，GLOB）→ 3. `[写] tests/bench/bench_int_arith.cpp`（整数运算基准样例）→ 4. cmake 独立构建运行，输出可读计时表
**文件域**：[写] tests/bench/*（bench_template.cpp、CMakeLists.txt、bench_int_arith.cpp）；[读] 基准体系设计.md、性能路线图.md
**依赖**：无

## T703 反汇编审查工具脚本（50 分钟）
**背景**：性能证据需要 dumpbin/objdump 形态断言（性能路线图阶段 4 反汇编审查）。
**任务**：1. 读 `[读] scripts/run-tests.ps1`（构建产物路径约定）→ 2. `[写] scripts/asm-check.ps1`：参数 `-Case <name> -Func <符号模式>` → 对 build_mingw 产物运行 objdump（或 MSVC dumpbin），grep 断言（无 call 模式/无 temp 泛滥），输出形态摘要 → 3. 用 propfold 或 mathcase 冒烟 → 4. 用法写入 `[写] scripts/NOTES-703.md`（README.md 统一由 T706 维护）
**文件域**：[写] scripts/asm-check.ps1、scripts/NOTES-703.md；[读] scripts/run-tests.ps1
**依赖**：无

## T704 单元测试补强：MethodRefResolver 签名解码（50 分钟）
**背景**：RawSignature 解码逻辑（memory.md 记录 0x08=i4 等）无独立单测。
**任务**：1. 读 `[读] src/il2cppc/Emit/MethodRefResolver.cs`（RawSignature 解码 + I() 助手）→ 2. `[写] tests/unit/RawSignatureTests.cs`（全类型码表：0x02-0x1D 逐码解码、组合签名、非法码拒绝）→ 3. `dotnet test` 全绿 → 4. 缺失类型码补测报告（如发现未覆盖路径）
**文件域**：[写] tests/unit/RawSignatureTests.cs；[读] Emit/MethodRefResolver.cs
**依赖**：无

## T705 运行时测试覆盖率审计（50 分钟）
**背景**：61 个 test_*.cpp 的覆盖缺口未知。
**任务**：1. 读 `[读] tests/runtime/`（GLOB 列表）+ `[读] src/runtime/src/`（icall 清单）→ 2. 脚本 `[写] scripts/coverage-audit.ps1`：对照 icall 名 ↔ 测试断言引用，输出未测 icall 清单 → 3. 运行脚本 → 4. `[写] docs/audit/runtime-coverage-audit.md`：缺口清单 + 优先级（未测 icall 分 P0/P1/P2）
**文件域**：[写] scripts/coverage-audit.ps1、docs/audit/runtime-coverage-audit.md；[读] tests/runtime/*、src/runtime/src/*
**依赖**：无

## T706 双跑工具增强：单用例精确复跑脚本（50 分钟）
**背景**：全量 run-tests.ps1 遇首个失败中止（discover.md 记录）；需要单用例精确复跑。
**任务**：1. 读 `[读] scripts/run-tests.ps1` 全量逻辑 → 2. `[写] scripts/run-case.ps1`：`-Name <case> -Compiler mingw|msvc|wsl` → 复用 run-tests.ps1 内部步骤（重构抽取共享函数到 `[写] scripts/run-common.ps1`——本卡独占）→ 3. 单用例冒烟（mathcase 全编译器）→ 4. `[写] scripts/README.md`（scripts/ 目录脚本总览：run-matrix/run-case/asm-check/setup-wsl 等用法汇总；本卡唯一写者，不存在则创建）
**文件域**：[写] scripts/run-case.ps1、scripts/run-common.ps1、scripts/README.md（唯一写者）；[读] scripts/run-tests.ps1
**依赖**：无

## T707 性能回归 CI 脚本（55 分钟）
**背景**：性能回退即失败（铁律 2e 原文）；基线对比需脚本。
**任务**：1. 读 `[读] docs/architecture/基准体系设计.md` + `[读] tests/bench/`（T702）→ 2. `[写] scripts/bench-regress.ps1`：跑 bench 产物 → 对比基线 JSON（首次运行生成基线）→ 偏差 >10% 报失败 → 输出表 → 3. 冒烟：连跑两次比对（噪声评估）→ 4. `[写] tests/bench/基线示例.json`（格式样例）
**文件域**：[写] scripts/bench-regress.ps1、tests/bench/基线示例.json；[读] 基准体系设计.md、tests/bench/*
**依赖**：T702 文件存在（软）

## T708 WSL 工具链固化脚本（50 分钟）
**背景**：WSL Ubuntu1804（GCC 7.5）验证链存在（run-tests.ps1 wsl 分支），环境未固化。
**任务**：1. 读 `[读] scripts/run-tests.ps1`（wsl 分支：cmake 3.10.2 无 -S/-B、/mnt 路径转换）→ 2. `[写] scripts/setup-wsl.ps1`：检查 wsl -d Ubuntu1804 存在、gcc/cmake/make 版本断言、缺失提示安装命令 → 3. `[写] scripts/wsl-env-check.ps1`：一键健康检查（三编译器版本 + 路径映射）→ 4. 本机冒烟运行
**文件域**：[写] scripts/setup-wsl.ps1、scripts/wsl-env-check.ps1；[读] scripts/run-tests.ps1
**依赖**：无

---

# 八、冲突自查结果（写者唯一性审计）

**规则**：每卡 `文件域: [写]` 条目 = 该卡独占写者。下表 = 全仓库**既有文件**被多卡引用的审计（既有文件至多 1 个写者；新文件全部唯一命名）：

| 既有共享文件 | 写者（唯一） | 只读引用卡 |
|---|---|---|
| src/il2cppc/Emit/MethodRefResolver.cs | T101 | T601、T704 等 |
| src/il2cppc/Emit/CodeGenerator.cs | T408 | T404、T413、T418 等 |
| src/il2cppc/Ir/StackFold.cs | T401 | T402-T419 等 |
| src/il2cppc/Emit/MethodEmitter.cs | T410 | T412 |
| src/il2cppc/Emit/CppWriter.cs | T412 | T607 等 |
| src/il2cppc/Program.cs | T305 | T306 |
| src/runtime/include/internal/il2cpp_runtime.h | T414 | T413 |
| src/runtime/src/metadata.cpp | T415 | T133 |
| src/runtime/src/framestack.cpp | T416 | T219、T420 |
| src/runtime/include/internal/il2cpp_frame.h | T416 | T219 |
| src/runtime/src/gc.cpp | T417 | T416（只读扫描侧） |
| src/runtime/include/internal/il2cpp_gc.h | T417 | — |
| scripts/run-tests.ps1 | 无写者（只读） | T701-T708、T310、T606-T607 |
| scripts/README.md | T706（唯一写者，创建+汇总） | — |
| src/runtime/CMakeLists.txt | 无写者（GLOB，只读） | 全部 T2xx |
| tests/runtime/CMakeLists.txt | 无写者（GLOB，只读） | 全部 T2xx |

**新增文件查重结果**（逐卡人工扫描，冲突 = 同路径双写者）：
- MethodMap.*.cs：T102-T135 各 1 个 + T2xx 各 1 个 + T308 = **39 个互异文件名**，无重名
- Ir/Pass*.cs：T401-T413/T419 = **15 个互异文件名**，无重名
- include/internal/il2cpp_*.h：T2xx 每域独立命名（regexcore/regexpats/regexapi、xmlcore/xmlio/xmlser、sha/md5/cryptorng、nethttp/netsocket、guid/decimal/biginteger/dtoffset/timezone/stacktrace/threadpool/waithandle/memorystream/textstream/binaryio/deflate/process/calendar/task/exprtree/encodingconv、sort/linq）+ T414(runtime.h 既有)、T416(frame.h 既有)、T417(gc.h 既有)、T301(reflmeta.h)——**无重名**
- src/*.cpp 新域：与上对应 28 个互异文件名 + reflquery/invoker + sort/linq，**无重名**
- tests/runtime/test_*.cpp：每卡独立命名（test_regexcore/test_regexpats/test_regexapi/test_sort/test_xmlcore/test_xmlio/test_xmlser/test_sha/test_md5/test_cryptorng/test_nethttp/test_netsocket/test_guid/test_decimal/test_biginteger/test_dtoffset/test_timezone/test_stacktrace/test_threadpool/test_waithandle/test_memorystream/test_textstream/test_binaryio/test_deflate/test_process/test_calendar/test_task/test_exprtree/test_encodingconv/test_reflmeta/test_reflquery/test_invoker/test_ifacehash/test_framebatch/test_tlab/test_inlineicall）——**36 个互异文件名**，与现有 61 个 test_*.cpp 无重名
- tests/cases/<name>/ 目录：本清单全部 fixture 名（versionuri/stopwatch2/stackqueue2/intptr2/typebase/typehandle/valuetype2/attributecase2/enum2/debugcase/buffercase/encoding2/culture2/stringcmp/stringsplit/arraysort/arraycopy/math3/datetime2/intern2/stringfmt/fmtcustom/parse2/exceptiondata/gcsets/marshal2/interlockedcase/monitorcase/base64case/sbappend/streamcase/runtimehelp/boxaux/consoleread/regex1/linqbase/linqsort/linqagg/xml1/sha256case/md5case/nethc/netsocket/guidcase/decimalcase/bigintcase/dtoffsetcase/timezonecase/stacktracecase/threadpoolcase/waithandlecase/memstreamcase/textstreamcase/binaryiocase/deflatecase/processcase/calendarcase/taskcase/exprtreecase/encodingconvcase/reflectops/reflectfull/propfold/foldstr/tailcall/noframelocals/cseprobe/inlineprobe/callconv/gennest/boxarray/delegatemulticast/nestexcept/interncase2/weakrefcase/cctorrace/culturefmt/linqcomb）——全部与现有 63 个用例目录互异且彼此互异。

**修正项（自查发现，已全部落实）**：
1. `tests/cases/intern2/`：T121（接线卡）与 T505（e2e 卡）同名 fixture → **冲突**。修正：T505 用例目录改为 `tests/cases/interncase2/`（T505 卡体已改）。
2. `scripts/README.md`：T701/T703/T706 三卡皆引用 → **冲突**。修正：README.md 唯一写者 = T706；T701 改写 scripts/NOTES-701.md、T703 改写 scripts/NOTES-703.md（卡体已改）。
3. `tests/cases/regex1/`（T201/T202/T203）、`tests/cases/propfold/`（T402/T403）、`tests/cases/xml1/`（T207/T208/T209）：三组共享 fixture 目录 → **按"文件级追加"设计**：每组第 1 卡独占目录主文件（Regex.cs / Props.cs / Xml1.cs），后续卡只追加互异新文件（Regex2.cs/Regex3.cs、Props2.cs、Xml2.cs/Xml3.cs）——同目录内无同文件双写者。
4. `Emit/MethodMap.Regex.cs`：T201 与 T203 均列写 → **冲突**。修正：MethodMap.Regex.cs 唯一写者 = T203（T201 卡体步骤 5 已改为"接线文件由 T203 独占"，T201 只负责引擎 + e2e fixture）。

**最终结论**：修正上述 4 项后，全清单 128 卡：每个既有文件 ≤1 写者、每个新文件恰 1 写者、新 fixture 目录与现有 63 个及彼此全部互异（仅 3 组共享目录按文件级追加设计，同文件仍唯一写者）。冲突清零（机器复核：337 个写者条目零重复；新写 fixture 目录零撞名）。

---

# 九、调度建议（主调度者）

1. **首批并行**（无依赖、高优先）：T101、T301、T401、T701、T702、T601-T602、T606-T609、T704-T705、T707-T708、T414-T417、T408、T410、T412、T501-T515 —— 约 30 卡可立即同发。
2. **依赖波 1**：T102-T135（依赖 T101 文件）、T2xx（依赖 T101 + 各自前置域）、T4xx（依赖 T401 文件）。
3. **依赖波 2**：T302-T310（依赖 T301 起）、T419（依赖 T401）。
4. **聚合收尾**：T310（反射矩阵）、T701/T706/T707（工具链）、T509（反汇编证据）——全部卡完成后由主调度者汇总双跑矩阵。
5. **冲突纪律**：任何卡发现需改"非本卡写者"文件 → 停止该动作，把需求写入本卡 GAPS.md，报告调度者重新分配，禁止自行修改。
