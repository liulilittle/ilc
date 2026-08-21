# .NET Core 参考源码使用指南（netcore-reference-guide）

- 日期：2026-08-05（§1 参考源码本地化 + 组 H 映射于本日更新；原 2026-08-04）
- 状态：文档（只读任务：不修改代码、不 commit）
- 目的：给所有代理（runtime / transpiler / tests 域）的**抄作业映射表**——实现任何 .NET 语义前，先查本表找到 .NET Core（dotnet/runtime，MIT）对应源文件，能抄就抄，绝不自编算法。
- 铁律来源：用户铁律 3——**大量代码直接抄袭 .NET Core 开源实现**（微软数千工程师多年积累验证过）；铁律 4——有成熟模板/先例必须抄。
- 对齐基线：**.NET Framework 4.8（net48）**（docs/alignment-tracker.md 工程总控表）；.NET Core 参考源码提供**算法/逻辑**，行为分歧处**以 net48 实测为准**（见 §5 已对齐清单与 §4.5）。

## 1. 参考源码现状与拉取

> ✅ **当前状态（2026-08-05）：参考源码已本地持久化**。三个克隆位于仓库根 `E:\il2cpp\reference\` 下（`.gitignore` 排除项，不进版本库，但**永久存于磁盘、非 temp 缓存**——早期 `%TEMP%\opencode\coreclr*` 临时缓存已废弃）。任何代理抄表内文件前，直接读 §1.1 本地路径；缺文件时按 §1.3 补拉，不得到 GitHub 在线翻页。

### 1.1 参考源码位置（本地路径，均已验证存在）

| 目录（GitHub 源） | 内容域 | 关键本地路径 |
|---|---|---|
| `reference/netcore/`（dotnet/runtime main，HEAD `93add0d`） | libraries（托管源码） | `src/libraries/System.Private.CoreLib/src/System/`、`src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/`、`src/libraries/System.Console/src/`、`src/libraries/System.Private.Uri/src/`、`src/libraries/System.Runtime.Extensions/src/` |
| `reference/coreclr/`（dotnet/runtime main，sparse 仅 `src/coreclr` 子树） | coreclr 原生 C++：GC / JIT / VM | `src/coreclr/gc/`（gc.cpp 等 63 文件）、`src/coreclr/jit/`（compiler.cpp/morph.cpp 等 313 文件）、`src/coreclr/vm/`（synch.cpp/marshal.cpp 等 519 文件） |
| `reference/corert/`（dotnet/corert，HEAD `c6af4cf`，全量检出） | CoreRT AOT 类型系统 / 反射 | `src/Common/src/TypeSystem/`（Ecma/IL/RuntimeDetermined/Canon…）、`src/System.Private.Reflection.Core/` |

**路径约定**：映射表（§2）内 `src/libraries/...` 相对路径 = `reference/netcore/` 下；`src/coreclr/...` = `reference/coreclr/` 下（即 `reference/coreclr/src/coreclr/...`）；`corert` 路径 = `reference/corert/` 下。

### 1.2 拉取命令（sparse checkout，只拉需要的库）

```bash
# ① libraries（reference/netcore）
git clone --depth 1 --filter=blob:none --sparse https://github.com/dotnet/runtime E:\il2cpp\reference\netcore
git -C E:\il2cpp\reference\netcore sparse-checkout set ^
  src/libraries/System.Private.CoreLib ^
  src/libraries/System.Text.RegularExpressions ^
  src/libraries/System.Console ^
  src/libraries/System.Private.Uri ^
  src/libraries/System.Runtime.Extensions

# ② coreclr GC/JIT/VM（reference/coreclr，只 sparse 出 src/coreclr 子树）
git clone --depth 1 --filter=blob:none --sparse https://github.com/dotnet/runtime E:\il2cpp\reference\coreclr
git -C E:\il2cpp\reference\coreclr sparse-checkout set src/coreclr

# ③ corert AOT 类型系统（reference/corert，全量检出，无需 sparse）
git clone --depth 1 https://github.com/dotnet/corert E:\il2cpp\reference\corert
```

### 1.3 如何补拉更多路径（sparse-checkout add）

> ⚠️ `sparse-checkout set` 会**替换**当前模式（未列出的已检出路径会被移出工作树）；**追加**一律用 `add`。

```bash
# netcore：追加一个 libraries 目录
git -C E:\il2cpp\reference\netcore sparse-checkout add src/libraries/System.Linq

# coreclr：追加子树（如诊断/工具链）
git -C E:\il2cpp\reference\coreclr sparse-checkout add src/coreclr/debug src/coreclr/tools

# corert：全量检出，新路径直接可读，无需任何命令
```

### 1.4 版本固定建议

- 当前已 pin：`reference/netcore` 与 `reference/coreclr` 均为 dotnet/runtime main（HEAD `93add0d`）；`reference/corert` 为 dotnet/corert（HEAD `c6af4cf`）。如需可复现，可 `git checkout` 固定 tag；表内路径为 main 分支规范路径。
- **net48 与 .NET Core 行为分歧清单见 §4.5**——这些分歧点上 .NET Core 源码仅供参考结构，**行为以 net48 实测为准**（本仓库已 pin 的语义见 §5）。

## 2. 映射表（域 → .NET Core 参考源文件）

覆盖 `src/runtime/src/*.cpp` 全部 81 个源文件（[READ:docs/design/cross-platform-design.md:6] 与 [BASH:Get-ChildItem src/runtime/src] 核对）。共 **52 行 / 8 组**（组 H 为 2026-08-05 补充的 coreclr/corert 映射）。路径解析见 §1.1 路径约定。

图例：📋=整段可照抄（算法/表/规则）｜🧩=抄逻辑改结构（托管结构→C 结构）｜🔌=平台调用必须替换为平台层（`plat_*`）｜⚠=行为与 .NET Core 有分歧，以 net48 为准

### 组 A — 数值与格式化（7 行）

| # | 域（src/runtime 文件） | 参考文件（dotnet/runtime） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| A1 | numberformat.cpp | `src/libraries/System.Private.CoreLib/src/System/Number.cs`（+ `Number.Formatting.cs`/`NumberToDecimal`） | `Number.FormatInt64`/`FormatDouble`、格式说明符表 | 📋 格式符 G/D/X/E/F/N/P 与精度语义、分组（千分位 3 位组）、百分号装饰——整段照抄。⚠ 舍入规则：本仓库已按 net48 实现 **round-half-away-from-zero**（numberformat.cpp 头注释，[READ:numberformat.cpp:8-12]），.NET Core 3.0+ 的 Ryu shortest 与 IEEE 舍入**不抄** |
| A2 | parse.cpp | 同上（`Number.Parsing.cs`） | `Number.TryParseInt32/Int64`、`ParseNumber` | 📋 整数解析规则：前导/尾随空白、符号、溢出→抛异常/返回 no-match、首字符非法→FormatException 语义。C API 为 int 返回码（il2cpp_parse.h 契约），抄判定逻辑改返回值 |
| A3 | format.cpp / formatext.cpp | `System/Text/StringBuilder.AppendFormat.cs` | `StringBuilder.AppendFormatHelper` | 📋 复合格式化解析：`{n}` 索引、对齐（`{0,-10}`）、格式说明符、FormatException 规则 |
| A4 | convert.cpp | `System/Convert.cs` | `Convert.ToInt32/ToInt64`（checked 转换） | 📋 数字转换语义：截断向零 + 溢出抛 OverflowException；D 系列字符串转换；`ChangeType` 类型矩阵 |
| A5 | convertbase64.cpp | `System/Convert.Base64.cs` | `ToBase64String`/`FromBase64String` | 📋 表驱动编解码（base64 字符表照抄）、填充 `=` 规则。⚠ .NET Core 解码比 net48 严格（拒绝缺失填充等）——以 net48 实测为准 |
| A6 | bitconverter.cpp | `System/BitConverter.cs` | `DoubleToInt64Bits`/`Int64BitsToDouble`/`ToHexString`/`IsLittleEndian` | 📋 IEEE-754 位搬运、字节序判断、16 进制小写表（`Convert.ToHexString`） |
| A7 | math.cpp / mathext.cpp | `System/Math.cs`（+ `MathF.cs`） | `Round`/`Sign`/`Abs`/`Max`/`Min`/`Clamp` | 📋 `Round` 的 MidpointRounding 语义、`Abs(Int32.MinValue)`→OverflowException、`Sign` 边界（+0/-0/NaN）、双参 `Max/Min` 的 NaN 传播。`clamp_i` 接线时照抄 Clamp 边界检查 |

### 组 B — 文本与正则（7 行）

| # | 域（src/runtime 文件） | 参考文件（dotnet/runtime） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| B1 | string.cpp | `System/String.cs`（+ `String.Comparison.cs`/`String.Searching.cs`） | `GetHashCode`（Ordinal）、`CompareOrdinal`、`IndexOf` | 🧩 哈希 = **net48 5381 双 DJBR 累加器**（已 pin，§5.1）——.NET Core 的 randomized hash **不抄**；Ordinal 比较规则照抄；IndexOf 搜索算法（未用 IndexOfAny 矢量化的朴素扫描）照抄 |
| B2 | stringext.cpp / stringext2.cpp / stringext3.cpp / stringrw.cpp | `String.Manipulation.cs` + `String.Split.cs` + `System/Char.cs` | `Trim`/`Substring`/`Split`/`ToLower`/`ToUpper`/`PadLeft` | 📋 Trim 字符集规则、Split 多分隔符/空条目规则、ASCII 大小写折叠表、Pad 对齐 |
| B3 | concat.cpp | `String.Concat.cs` | `Concat(string,string)`/`Concat(object)`/`Join` | 📋 拼接长度预计算、null→`""` 规则、Join 分隔符规则 |
| B4 | stringbuilder.cpp | `System/Text/StringBuilder.cs`（+ `AppendFormat.cs`） | `Append`/`AppendFormat`/Chunk 模型 | 🧩 .NET Core chunk 链表模型照抄（比 net48 单缓冲好）；容量倍增；AppendFormat 走 A3 |
| B5 | stringintern.cpp | `String.Intern.cs` | `Intern`/`IsInterned` | 🧩 驻留表语义（同引用返回）；表结构用 C 侧 hashmap |
| B6 | encoding.cpp | `System/Text/Encoding.cs` + `Encoding.UTF8.cs` + `Encoding.Unicode.cs` | `UTF8Encoding`/`UnicodeEncoding` | 📋 UTF-8 校验规则（非法序列→异常/替换）、UTF-16 按 char16_t、GetByteCount/GetCharCount 双调契约。🔌 平台层自实现编解码器（无 iconv 依赖，cross-platform-design.md §3.1） |
| B7 | regex_parse.cpp / regex_match.cpp / regex_api.cpp / regex_ast.h | `src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/` | `RegexParser.cs`/`RegexCharClass.cs`/`RegexInterpreter.cs`/`RegexNode.cs` | 📋 **RegexCharClass.cs 字符类范围解析**（`[a-b-c]`、`[-a]`、`[]]` 规则）、`RegexParser.cs` 语法表与错误规则（`{n,m}` 完整格式才解析、未闭合 `{` 回退字面——net48 实测）。🧩 `RegexInterpreter.cs` 回溯语义对应本项目 CPS continuation 栈（regex_match.cpp）。⚠ v1 裁剪 ASCII 类（net48 实测 `\w` 为 Unicode 感知，regex-domain.md §1.4） |

### 组 C — 集合与排序（7 行）

| # | 域（src/runtime 文件） | 参考文件（dotnet/runtime） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| C1 | hashtable.cpp | `System/Collections/Hashtable.cs` | `Hashtable.Insert`/`GetHash` | 🧩 碰撞链 + 装载因子成长（0.72）照抄；string 键哈希用 **net48 5381**（.NET Core randomized 不抄）；boxed Int32 键 = 值本身 |
| C2 | dictsi.cpp | `System/Collections/Generic/Dictionary.cs` | `Dictionary<TKey,TValue>` | 🧩 bucket 数组 + 链、成长扩容重哈希；string 哈希同 C1 注意 |
| C3 | listi4.cpp | `System/Collections/Generic/List.cs` | `List<T>` | 📋 容量倍增 ×2、索引越界→IndexOutOfRange 语义 |
| C4 | sortedlist.cpp | `System/Collections/SortedList.cs` | `SortedList` | 📋 数组二分插入、成长、键比较 |
| C5 | stackqueue.cpp | `System/Collections/Stack.cs` + `Queue.cs` | `Stack`/`Queue` | 📋 Queue 循环缓冲（head/tail 环绕）照抄；Stack 容量倍增 |
| C6 | array.cpp / arrayext.cpp / arrayext2.cpp / arrayextra.cpp / arraysort.cpp | `System/Array.cs` + `Array.Sort.cs`（`ArraySortHelper`）+ `Array.Search.cs` | `Array.Copy`/`Clear`/`Reverse`、`IntroSort`、`BinarySearch` | 📋 **introsort 三分割快排 + 插入排序**算法照抄；二分查找返回 `~index` 补码语义；Copy 重叠 memmove 语义；Clear 长度检查 |
| C7 | linq.cpp / linq_map.cpp / linq_sort.cpp | `src/libraries/System.Linq/src/System/Linq/` | `Enumerable.cs`/`OrderBy.cs`/`Sum.cs` | 🧩 **稳定 OrderBy**（键对带序号）照抄；Sum 溢出 unchecked（net48 默认）；迭代器状态机 → 显式 C 状态结构（linq.h 契约） |

### 组 D — 线程与运行时（9 行）

| # | 域（src/runtime 文件） | 参考文件（dotnet/runtime） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| D1 | time.cpp / timeext.cpp / timeext2.cpp | `System/DateTime.cs` + `DateTimeParse.cs` + `DateTimeFormat.cs` + `TimeSpan.cs` + `TimeSpanFormat.cs` + `TimeSpanParse.cs` | `DateTimeParse`/`TimeSpanFormat` | 📋 DateTime 算术（ticks、闰年）、格式化模式表（yyyy/MM/dd…）、TimeSpan 解析/格式（`c` 格式）；日历概念参考 `Calendar.cs`。🧩 局部时间 → 平台层（localtime_r，cross-platform-design.md §1.1） |
| D2 | thread.cpp / threadext.cpp | `System/Threading/Thread.cs` | `Thread.Sleep`/`Yield` | 📋 `Sleep(0)`→yield 语义、Sleep 范围检查。🔌 线程 ID/sleep → 平台层（`plat_thread_*`）。对象模型决策（CurrentThread 拒绝，alignment-tracker.md §3 P3） |
| D3 | threadpool.cpp | `System/Threading/ThreadPool.cs` + `TimerQueue.cs` | 队列/工作线程语义 | 🧩 概念级（coreclr 本身是原生 C++ 实现，无托管算法可抄）；🔌 线程/同步 → 平台层 |
| D4 | timer.cpp | `System/Threading/Timer.cs` | 定时器调度语义 | 🧩 概念级；🔌 平台层实现 |
| D5 | monitor.cpp | `System/Threading/Monitor.cs` + `reference/coreclr/src/coreclr/vm/synch.cpp` | `Enter`/`Exit`/`Wait`/`Pulse` | 🧩 递归计数、等待队列语义照抄（synch.cpp 是 C++，可直接参考结构）；锁表对象模型决策（alignment-tracker.md §4） |
| D6 | interlocked.cpp | `System/Threading/Interlocked.cs` | `CompareExchange` 系列 | 📋 Acquire/Release 内存序语义；🔌 原子原语 → 平台层（GCC 内建/Windows API） |
| D7 | stopwatch.cpp | `System/Diagnostics/Stopwatch.cs` | `GetTimestamp`/`Elapsed` | 📋 频率换算、毫秒/秒计算；🔌 QPC/clock_gettime → 平台层 |
| D8 | gc.cpp / gcapi.cpp / gcext.cpp | `System/GC.cs` + `reference/coreclr/src/coreclr/gc/gc.cpp` | `Collect`/`GetGeneration` | 🧩 概念级（gen 提升、mark-compact）——coreclr GC 是巨型原生实现，抄**概念**（GC 位图/扫描规则见 hashtable.cpp 头注释），不抄结构；**移植设计清单见 docs/research/coreclr-gc-移植.md**（§10 有 gc/ 63 文件逐文件清单） |
| D9 | exception.cpp / exceptionext.cpp | `System/Exception.cs` | `Message`/`HResult`/`ToString` 格式 | 📋 标准异常消息格式、HResult 值表（0x8007…）；本项目 throw 机制为 C++ type-tag（不抄托管异常结构） |

### 组 E — IO 与平台（9 行）

| # | 域（src/runtime 文件） | 参考文件（dotnet/runtime） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| E1 | console.cpp | `src/libraries/System.Console/src/System/Console.cs` + `ConsolePal.Unix.cs` | `WriteLine` 格式、`ConsoleColor` | 📋 ConsoleColor→ANSI SGR 映射表**整段照抄**（ConsolePal.Unix.cs）；输出缓冲语义。🔌 `_getch`/termios → 平台层 |
| E2 | environment.cpp | `System/Environment.cs`（+ `Environment.Unix.cs`） | `GetEnvironmentVariable` 语义 | 📋 变量展开/空串删除语义。🔌 27 处 Win32 API 已全双分支（cross-platform-design.md §1.1），迁移到 `plat_env_*` |
| E3 | io.cpp | `System/IO/File.cs` + `Directory.cs` | `FileMode`/`FileAccess`/目录枚举 | 📋 错误→IOException 语义映射（文件不存在/权限）；🔌 mkdir/stat → 平台层（`plat_dir_*`） |
| E4 | pathproc.cpp | `System/IO/Path.cs` + `PathInternal.cs` + `PathInternal.Unix.cs` | `GetFullPath`/`Combine`/`GetTempPath` | 📋 GetFullPath 规范化规则（`.`/`..`/尾斜杠/大小写——net48 参考测试对齐，cross-platform-design.md §8-2）；GetTempPath 的 TMPDIR/TEMP 顺序。🔌 realpath/getenv → 平台层 |
| E5 | streamio.cpp / binaryio.cpp | `System/IO/Stream.cs` + `BinaryReader.cs` + `BinaryWriter.cs` | 7-bit 变长整数、小端、`Read` 部分读契约 | 📋 **Write7BitEncodedInt 变长编码照抄**；小端字节序；Read 返回实际读入数（部分读契约） |
| E6 | pinvoke.cpp / pinvokeext.cpp | `src/libraries/System.Runtime.InteropServices/src/System/Runtime/InteropServices/NativeLibrary.cs` | `NativeLibrary.Load` 名字解析 | 📋 **库名解析顺序**（`*.dll`→`lib*.so`/`*.dylib` 映射、候选目录）——正是本项目 POSIX 侧缺口（cross-platform-design.md §7.2），照抄后放平台层 `plat_dl_open` |
| E7 | marshalext.cpp / marshalext2.cpp | `System/Runtime/InteropServices/Marshal.cs` + `reference/coreclr/src/coreclr/vm/marshal.cpp` | 字符串/结构封送决策 | 🧩 封送决策逻辑（string→UTF-8/UTF-16、blittable 判定）概念级照抄；🔌 实际调用 → 平台层 |
| E8 | intptr.cpp | `System/IntPtr.cs` | `ToInt64`/算术/比较 | 📋 截断/回绕语义、checked 算术 |
| E9 | buffer.cpp | `System/Buffer.cs` | `BlockCopy`/`ByteLength` | 📋 长度检查 + 重叠 memmove 语义 |

### 组 F — 元数据与类型（10 行）

| # | 域（src/runtime 文件） | 参考文件（dotnet/runtime） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| F1 | typecheck.cpp / typeext.cpp / typeinfo.cpp / metadata.cpp | `System/Type.cs` + `RuntimeType.cs` | `IsAssignableFrom`/类型名格式化 | 🧩 可赋值性规则（null、接口、泛型）照抄；类型名格式（`System.Int32` 风格）照抄；元数据模型不同——抄规则不抄结构 |
| F2 | enumext.cpp | `System/Enum.cs` | `ToString`/`HasFlag`/`GetName` | 📋 枚举数值格式化（数字回退）；🧩 名称查找走本项目 metadata |
| F3 | attribute.cpp | `System/Attribute.cs` + `reference/coreclr/src/coreclr/vm/`（自定义属性解析） | 属性注册机制 | 🧩 概念级（attribute.cpp 在途）；核心元数据模型在本项目 |
| F4 | guid.cpp | `System/Guid.cs`（`NewGuid` 平台分部） | `ToString` D/N/B/P/X 格式、`TryParse` | 📋 **格式与解析照抄**（GuidParse 规则）；🔌 NewGuid 随机源 → 平台层（arc4random/getentropy，cross-platform-design.md §4-2/§5-2），不抄 CoCreateGuid |
| F5 | versionuri.cpp（Version 部分） | `System/Version.cs` | 解析/比较/ToString | 📋 照抄（1-4 段、int32 溢出防护） |
| F6 | versionuri.cpp（Uri 部分） | `src/libraries/System.Uri/src/System/` | `Uri.cs`/`UriHelper.cs`/`UriParser.cs` | 📋 scheme/host/port 解析、转义规则（HexEscape 表）照抄；🧩 Unicode 规范化裁剪（按 v1 范围声明） |
| F7 | cultureinfo.cpp | `System/Globalization/CultureInfo.cs` + `NumberFormatInfo.cs` + `DateTimeFormatInfo.cs` | `InvariantCulture` 数据 | 📋 **固定不变文化数据表**（分组 3、`','`/`'.'`/`"n %"` 符号表）照抄——当前 resolver 折叠 en-US 字面量（alignment-tracker.md §1） |
| F8 | delegate.cpp / delegateext.cpp | `System/Delegate.cs` + `MulticastDelegate.cs` | `Combine`/`Remove` | 📋 **Combine/Remove 算法照抄**（null 特例、自身移除、调用链合并）；链结构 C++ 化（GC 位图） |
| F9 | boxing.cpp / boxingext.cpp / boxingext2.cpp / valuesemantics.cpp | `System/ValueType.cs` + `System/Object.cs`（`RuntimeHelpers`） | `GetHashCode`/`Equals` | 📋 值类型相等/哈希语义；**string 哈希 = net48 5381 pin**（valuesemantics.cpp:82-92，§5.1）；位级 blittable 相等照抄 |
| F10 | random.cpp | `System/Random.cs` | `Next`/`NextDouble`/`NextBytes` | 📋 **范围语义**（`Next(max)` 公式、`NextBytes` 低 8 位）照抄。⚠ 算法族不抄：.NET Core 6+ 为 xoshiro256**，net48 为 Knuth subtractive；本项目 LCG 自证全周期（random.cpp:4-19）——**net48 对齐优先，算法族不换** |

### 组 G — 基础设施（1 行，概念级）

| # | 域（src/runtime 文件） | 参考文件（dotnet/runtime） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| G1 | runtime.cpp / object.cpp / framestack.cpp / allocator.cpp / api.cpp / debugext.cpp / stringintern（见 B5） | `reference/coreclr/src/coreclr/vm/object.h` + `reference/coreclr/src/coreclr/vm/excep.cpp` + `reference/coreclr/src/coreclr/gc/` | 对象布局/异常展开/帧 | 🧩 概念级参考（M3 对象模型、帧配对、栈展开）；无托管等价物可抄，结构由本项目自建 |

### 组 H — coreclr/corert 补充映射（2 行，2026-08-05 补拉后新增）

| # | 域（文件） | 参考文件（本地） | 关键类/函数 | 抄作业要点 |
|---|---|---|---|---|
| H1 | 转译器优化 passes（src/il2cppc/，JIT 域） | `reference/coreclr/src/coreclr/jit/` | `compiler.cpp`（compCompile）、`morph.cpp`（fgMorphSmpOp/fgMorphStmts）、`gentree.cpp`（gtFoldExpr）、`fgopt.cpp`（死块）、`optimizer.cpp`（死存储） | 🧩 **优化清单与取舍（P0/P1/P2 + 铁律禁止项）见 docs/research/coreclr-jit-优化.md**——只抄**可折叠语义**（代数恒等式/常量传播/封闭世界去虚拟化），不做机器码级 pass（帧记录铁律禁完整内联/尾调用） |
| H2 | 元数据/类型/反射域（typecheck.cpp / metadata.cpp / attribute.cpp，AOT 类型系统） | `reference/corert/src/Common/src/TypeSystem/`（+ `src/System.Private.Reflection.Core/`） | `Ecma/`（ECMA-335 元数据加载）、`IL/`（IL 解码）、`RuntimeDetermined/`（泛型实例化） | 🧩 CoreRT 是 C# 版 AOT 类型系统，与本项目最接近的参考形态——**类型/方法签名表示、泛型实例化规则、反射成员枚举照抄思路**；本项目 metadata 表结构不同，抄规则不抄结构（§4.3） |

## 3. 映射表规模统计

- **52 行映射**（组 A 7 + 组 B 7 + 组 C 7 + 组 D 9 + 组 E 9 + 组 F 10 + 组 G 1 + 组 H 2），覆盖 `src/runtime/src/` **全部 81 个 .cpp** 与 alignment-tracker.md §1 域总表（64 头文件）。
- 每行按"照抄程度"标注：📋 14 行整段可照抄｜🧩 28 行抄逻辑改结构｜🔌 12 行涉平台调用须替换（部分行多重标注）。
- 未列出的 .cpp（无独立参考：`conv.cpp` conv 指令语义 → 参考 `System/Convert.cs`（A4）的 checked 转换 + ECMA-335 指令表，随转译器 conv 接线补行）。

## 4. 抄作业规范

### 4.1 MIT 许可声明格式（文件头注释，新文件必须带）

从 .NET Core 抄逻辑的文件，**文件头**加以下注释块（放在现有 `//----` 风格块内，见 numberformat.cpp/random.cpp 头注释惯例）：

```cpp
//------------------------------------------------------------------------------
// <file>.cpp -- <域> runtime (见 <header>.h).
//
// 本文件的部分算法/格式化/解析逻辑改编自 .NET 参考源码（dotnet/runtime,
// https://github.com/dotnet/runtime），MIT 许可：
//   原始文件: src/libraries/<路径>/<文件>.cs
//   Copyright (c) .NET Foundation and Contributors.
//   许可证:   https://opensource.org/licenses/MIT
// 改动说明: <本次裁剪/行为分歧，如 "v1 裁剪 ASCII 字符类（regex-domain.md §1.4）">
//------------------------------------------------------------------------------
```

要求：
1. **注明原始文件路径**（便于他人回查，映射表行内路径可直接引用）。
2. **写明改动说明**——任何与 .NET Core 行为的差异（net48 分歧、裁剪、性能护栏）必须在此声明，禁止静默偏离。
3. 仓库根无需放 LICENSE 副本——reference/ 三仓均为 sparse 检出，根目录 LICENSE 文件**未**进入工作树（2026-08-05 验证 [BASH:Test-Path reference/{netcore,coreclr,corert}/LICENSE → 全 False]）；文档引用许可证链接即可。

### 4.2 可抄范围（照抄）

- **算法**：排序（introsort）、哈希（除 randomized）、搜索、回溯（regex CPS 对应 interpreter 语义）、变长编码（7-bit）、base64 表。
- **格式规则**：格式说明符表（G/D/X/E/F/N/P）、复合格式化 `{n:...}`、DateTime/TimeSpan 模式表、Guid 格式（D/N/B/P/X）、枚举数值格式化。
- **解析逻辑**：整数/TimeSpan/Version/Uri/Guid 解析的状态与边界规则、字符类范围解析（RegexCharClass）、错误语义（抛什么异常/什么条件下）。
- **常量/数据表**：ConsoleColor→ANSI 映射、不变文化符号表、HResult 值表、UTF-8 校验表、ASCII 折叠表。

### 4.3 不可抄（必须替换）

- **平台 API 调用**：Win32（`GetEnvironmentVariableA`/`CoCreateGuid`/`LoadLibraryA`…）与 POSIX（`dlopen`/`pthread_*`…）一律**不照抄**——替换为平台层接口（`plat_*`，src/runtime/include/internal/platform.h 设计见 docs/design/cross-platform-design.md §2.2）。可抄的是其**决策逻辑**（如 NativeLibrary 库名解析规则），调用点走平台层。
- **托管运行时基础设施**：GC 句柄、引用追踪、String 不可变引用语义、委托链的托管分配——结构由本项目 C++ 模型承担（GC 位图见 hashtable.cpp 头注释），只抄**语义规则**。
- **托管数据结构形态**：.NET 用托管数组/迭代器/状态机的场合，C 侧用静态表/显式状态结构（如 linq 迭代器 → C 函数 + 显式状态，linq-domain.md §4）。
- **反射元数据模型**：MetadataLoadContext/RuntimeType 的托管结构——本项目 metadata 表不同，抄**规则**（IsAssignableFrom/类型名格式）不抄结构。

### 4.4 C API 边界（抄逻辑不抄托管结构）

1. **抄"怎么算"，不抄"怎么存"**：.NET 源码里对 `string`/`Array`/`object` 的操作（不可变、引用语义、GC 根）翻译为 C API 时按本项目契约（`Il2CppString*`/`Il2CppArray*`）落盘——返回值、所有权、空指针语义遵循 `src/runtime/include/internal/*.h` 既有约定（C8 命名/签名一致性：同数据同名，同函数同签名）。
2. **异常边界**：.NET 抛异常处 → C API 返回错误码或经 `il2cpp_throw_*` 载体（exception 域 type-tag 机制）；**禁止**在 C 层模拟托管异常结构。
3. **行为分歧显式化**：因裁剪产生的分歧（预算护栏、ASCII 类、确定性哈希）在头注释与域名文档声明（regex-domain.md §1.4 是样板），并对齐测试断言。
4. **接线索引**：接线方（MethodRefResolver.cs/CodeGenerator.cs）按 docs/alignment-tracker.md §4 每域下一步动作推进；抄完实现的域按"域 → dispatch → e2e 用例"闭环（icall-gap-list.md 施工单）。

### 4.5 net48 与 .NET Core 行为分歧（抄前必读）

以下域 .NET Core 已改变行为，**实现以 net48 实测为准**（本项目对齐基线）：

| 域 | .NET Core 行为 | net48 行为（本项目为准） | 状态 |
|---|---|---|---|
| 舍入 | 3.0+ IEEE round-half-even / shortest-roundtrip（Ryu） | round-half-away-from-zero；G15 默认 | ✅ 已定（numberformat.cpp:8-12） |
| string 哈希 | randomized（进程内变化） | 固定 5381 双累加器 | ✅ 已 pin（§5.1） |
| Random 算法 | 6+ xoshiro256** | Knuth subtractive（本项目 LCG 自证） | ✅ 已定（random.cpp:4-19） |
| base64 解码 | 严格（缺填充拒绝） | 宽松（容忍缺失填充） | ⏳ 实现时实测 |
| Regex | 源同宗（net48 与 coreclr 同系），`\w` Unicode 感知 | 实测为准；v1 裁剪 ASCII | ✅ 已实测 3 处（§5.2） |
| double ToString | shortest-roundtrip | G15（`il2cpp_format.h` G15 icall） | ✅ 已定（numberformat.cpp 头注释） |

## 5. 已对齐清单（net48/.NET Core 一致性已验证）

> 以下域的 net48 一致性**已有实测证据**（探针/oracle 测试），新代理实现时可放心以此为基线，不必重复探测。证据索引见各条目。

### 5.1 string 哈希（net48 5381 种子）— 已对齐 ✅

- **语义**：Ordinal string.GetHashCode = 双 DJBR 累加器（`hash1 = hash2 = 5381`，每字符 `*33` 累加，uint32 回绕），种子为**纯 5381**（非 `(5381<<16)+5381`——移位种子破坏 net48 字节兼容）。
- **证据**：
  - [READ:src/runtime/src/hashtable.cpp:199-204]——注释"csc.exe net48 on 2026-08-04"实测锚定
  - [READ:src/runtime/src/valuesemantics.cpp:82-92]——seed 5381、非移位种子、byte-compatibility 声明
  - [READ:tests/runtime/test_stringhash.cpp:9]——`hash1 = hash2 = 5381` 固定 oracle；同文件 :144 空串边界
  - 测试：`tests/runtime/test_stringhash.cpp`（跨进程稳定，探针验证）

### 5.2 Regex（net48 3 处实测推翻 docs）— 已对齐 ✅

- **实测**（探针程序 `%TEMP%\opencode\regexprobe\probe{1,2,3}.cs`，csc.exe v4.0.30319）：
  1. `[]`/`[^]` → net48 抛 **ArgumentException**（docs 原写"空类=永不匹配/[^]=任意字符"——**错**）；注意 `[]]` 合法（首 `]` 字面）
  2. `$`（非 Multiline）= 串尾**或末尾 `\n` 之前**（`a$` 匹配 `"a\n"`）
  3. `[a-b-c]` 合法（范围+字面 `-`+字面 `c`）；`a{2`/`a{2,`/`a{2,3`/`a{2x` 为**字面**；仅完整 `{n}/{n,}/{n,m}` 解析，n>m 或 >INT32_MAX 才抛
- **证据**：[READ:.opencode/discover.md:4-8]（完整记录）；[READ:tests/runtime/test_regex_match.cpp]（注释"the probe result wins"）；tests/runtime/test_regex_match.cpp 82 断言全绿（0.18s）
- **原则**：docs/design/regex-domain.md 与 net48 冲突处，**以 net48 实测为准**（discover.md 反模式总结）。

### 5.3 数字格式化（net48 oracle 捕获）— 已对齐 ✅

- **oracle**：[READ:tests/cases/formatext/oracle.txt]（net48 捕获输出：`12345`/`-12345`/`3039`…）对应 e2e `tests/cases/formatext/`（expected.txt 比对）。
- **基线**：[READ:tests/runtime/test_numberformat.cpp]——"InvariantCulture（net48 默认测试基线）"、`G15 of 1234567.89 (net48 default double text)`、`N2 of -0.0 keeps no sign (net48-verified)`；test_numberformat.cpp 70 断言（cross-platform-design.md §6 批 1 已列，MSVC 70/70）。
- **舍入**：round-half-away-from-zero（net48），numberformat.cpp 头注释自述 + §4.5 表。

### 5.4 整体对齐基线

- docs/alignment-tracker.md（net48 对齐工程总控表）：64 头文件 / 313 icall 声明 / 236 已接线引用 / icall-gap-list.md 174 正向缺口施工单。
- 裁剪声明样板：docs/design/regex-domain.md §1.4（与 net48 的分歧全部显式化）。

## 6. 使用流程（代理必读）

1. **实现任何 .NET 语义前**：查 §2 映射表定位参考文件（域 → dotnet/runtime 路径）。
2. **定位**：按 §1.1 直接读本地 `reference/{netcore,coreclr,corert}` 三仓路径；缺文件时按 §1.3 `sparse-checkout add` 补拉，不再依赖 GitHub 在线查看。
3. **抄**：按 §4.2 范围抄算法/表/规则；§4.3 平台调用替换为 `plat_*`；§4.4 按 C API 契约改结构。
4. **声明**：文件头加 §4.1 MIT 注释块（含原始文件路径 + 改动说明）。
5. **验证**：行为分歧点以 net48 实测为准（§4.5 表），对齐 §5 已 pin oracle；补域测试（tests/runtime/test_*.cpp 惯例）+ e2e（tests/cases/ 惯例）。
6. **记录**：新对齐域在 §5 追加证据条目，并在 .opencode/ 记忆文件落盘。

## 附：证据索引（本任务）

- [BASH:Test-Path E:\il2cpp\reference\{netcore,coreclr,corert} → 全 True]——reference 三仓本地持久（netcore @ 93add0d；coreclr 同源 sparse 出 src/coreclr；corert @ c6af4cf）；[BASH:git sparse-checkout list] → libraries 5 路径 + src/coreclr/{gc,jit,vm}
- [BASH:Test-Path E:\il2cpp\reference\coreclr\src\coreclr\gc\gc.cpp → True]、[BASH:Test-Path E:\il2cpp\reference\corert\src\Common\src\TypeSystem → True]（gc.cpp 154,650B；jit 313 / vm 519 / gc 63 文件；corert 全量 4412 文件）
- [BASH:Test-Path E:\il2cpp\reference\{netcore,coreclr,corert}\LICENSE → 全 False]——sparse 检出不含根 LICENSE，§4.1 以链接为准
- [BASH:Get-ChildItem src/runtime/src → 81 个 .cpp]；[BASH:Get-ChildItem include/internal → 90 个头文件]
- [READ:docs/alignment-tracker.md:12-63]（域总表）；[READ:docs/design/cross-platform-design.md:6]（81 源文件基线）
- [READ:hashtable.cpp:199-210] / [READ:valuesemantics.cpp:82-92] / [READ:test_stringhash.cpp:9]（5381 pin）
- [READ:.opencode/discover.md:4-8]（regex 3 处实测）；[READ:test_regex_match.cpp]（probe 胜出注释）
- [READ:tests/cases/formatext/oracle.txt] / [READ:test_numberformat.cpp]（net48 oracle）
- [READ:numberformat.cpp:8-12]（round-half-away-from-zero）；[READ:random.cpp:4-19]（LCG 自证）
