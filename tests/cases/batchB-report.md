# e2e 扩量批次 B 报告（2026-08-06）

执行代理交付：6 个新 e2e 用例（tests/cases/<名>/，仅建用例，未改任何产品代码，
未 git commit）。验证驱动：`scripts/run-tests.ps1 -Compiler mingw -Case <名>`
（net48 oracle live 运行 vs mingw 转译构建运行，LF-join 逐字节 diff）。
`.inflight` 检查：无标记，全程单代理。

## 1. 新建用例清单 + 每个 diff 结果

| 用例 | 域 | 探针面 | 输出行数 | 结果 | 阶段 |
|---|---|---|---|---|---|
| **consoleio** | System.Console | WriteLine 全格式（含 boxed object）/ Write 组合 / ReadLine EOF→null / String.Format 位置复合格式 | 31 | **PASS** | diff 一致 |
| **boxunbox** | 装箱/拆箱 | boxed GetHashCode(Int32 值基)/ToString 分发/GetType / isinst+castclass+unbox-rebox / 泛型字段读取装箱 | 20 | **PASS** | diff 一致 |
| **recurse2** | 递归 | 互递归(IsEven↔IsOdd, 6000) / 尾递归式+非尾递归 / 带异常展开(200 层 unwind) / 深度 5000 级联 | 10 | **PASS** | diff 一致 |
| **charstr** | char/string | Unicode 转义（BMP/代理对）/ 索引器 char 运算 / char 比较算术 / StringBuilder Append(char) | 34 | **PASS** | diff 一致 |
| **pathext** (奖励 1) | System.IO.Path | Combine(2 参)/嵌套、GetFileName、GetExtension、ChangeExtension、File 往返 | 17 | **PASS** | diff 一致 |
| **stackq** (奖励 2) | System.Collections | Stack Push/Pop/Peek/Count/Clear、Queue 全操作、混合元素类型 | 24 | **FAIL** | cmake_build（2 缺陷，见 D-STK-1/2） |

4 个必建用例全部 PASS（全通 → 允许加 2 个新域奖励用例，已加 pathext/stackq）。
6 用例中 5 PASS / 1 FAIL（FAIL 的 stackq 暴露 2 个精确转译器缺陷，是有效交付）。

net48 oracle 证据：[BASH] 每用例 `out/<名>.exe` 运行 exit=0，行数与
expected.txt 逐字节一致；harness diff 阶段结果如上。

## 2. 转译器/运行时缺陷清单（本批暴露，均未修）

图例：T=转译器，R=运行时，H=harness。每项 = 用例 + 现象 + 疑似根因文件。

| # | 用例 | 域 | 现象（实测） | 疑似根因文件 |
|---|---|---|---|---|
| D-CON-1 | consoleio | T | `String.Format("no args")` 被 Roslyn 降级为 `Format(s, Array.Empty<object>())` → LOUD `Array.Empty<T> generic argument 0x1C not wired in M5`（Array.Empty 只接 0x03/0x08/0x0E） | MethodMap.Array.cs Empty switch |
| D-CON-2 | consoleio | T | `Console.Error` 无 get_Error 映射 → 生成未定义 extern `System_Console_get_Error_<hash>` → 构建 undefined symbol；TextWriter::WriteLine 误路由到 stringwriter icall | MethodMap.Console.cs（无 get_Error/Out/In）、MethodMap.StringIO.cs |
| D-CON-3 | consoleio | T/R | `Console.ReadKey` 用阻塞 `_getch()`，EOF stdin 无确定性事件；`Console.IsInputRedirected` LOUD（MethodMap.Console.cs:112） | MethodMap.Console.cs + console.cpp:245 |
| D-CON-4 | consoleio | R | `String.Format("{N,width}")` 对齐说明符 → runtime `NotSupportedException`（AppendFormatCore），仅 `{N}` 可用 | stringbuilder.cpp AppendFormatCore |
| D-CON-5 | consoleio | T | `Console.WriteLine(string, object)` 复合重载：WriteLine switch 按 ps[0] 单参分发，2 参 IL 调用 arity 错配 | MethodMap.Console.cs WriteLine |
| D-BOX-1 | boxunbox | T | 用户 struct 装箱 LOUD `box of Fix not supported -- no runtime box helper` → "装箱后修改字段"（接口分发变异）探针不可达 | CodeGenerator.cs:2752 box_ switch |
| D-BOX-2 | boxunbox | R | `o is long` isinst Int64 → `System_Int64_class` 未声明（仅 System_Int32_class 等） | runtime 描述符表 il2cpp_metadata.h |
| D-BOX-3 | boxunbox | T | 装箱值上接口 callvirt（`((IComparable)o).CompareTo(42)`）→ 未定义生成名 `System_IComparable_CompareTo_<hash>` | StackFold/CodeGenerator callvirt 路径 |
| D-BOX-4 | boxunbox | T | 泛型 `T→object` 产生的 box 上 ToString → 接收者类型未跟踪 → `System_Object_ToString_<hash>` 未定义 | MethodMap.Object.cs / StackFold box 类型跟踪 |
| D-BOX-5 | boxunbox | T | boxed UInt32 `.ToString()` → ToString receiverType 白名单缺 `"System.UInt32"`（runtime icall 有该臂）→ 未定义 extern | MethodMap.Object.cs ToString 白名单 |
| D-BOX-6 | boxunbox | R | boxed GetHashCode 仅 Int32 值基；bool/char/double/long 等返回地址哈希（ptr>>3）→ 同值跨 box 不一致 + 偏离 net48（bool→1/0, char→unit, double→xor-halves） | valuesemantics.cpp icall_object_get_hash_code |
| D-CHA-1 | charstr | T | 孤立代理项字面量 `"\uD800"` → string pool 报告不可编码 → **string-pool.h 写空(0 字节)** → 全部 g_strPool 引用编译失败 | StringPoolPass.cs（T3 表发射） |
| D-CHA-2 | charstr | T | `char.IsDigit/IsLetter/IsWhiteSpace/Char.ConvertToUtf32` 无 MethodMap → 静默生成未定义 `System_Char_*` extern（非 LOUD）。模式：未映射 BCL 静态走生成名而非 LOUD | 缺 MethodMap.Char.cs；默认生成名路径 |
| D-REC-1 | recurse2 | T | 既有 recurse 用例 cmake_build 缺陷：entry.cpp 无参调用 `Main(string[])` 声明 (Il2CppObject* arg0)（recurse2 用 Main() 规避） | CodeGenerator.cs ~3254 entry.cpp 发射 |
| D-REC-2 | recurse2 | H | gen CMakeLists 无 CMAKE_BUILD_TYPE → mingw -O0，深递归帧预算有限（6000 互递归 OK） | CodeGenerator CMakeLists 发射（观察） |
| D-STK-1 | stackq | T | generated.h 包含链缺 `stackqueue.h` → `il2cpp_stack_*`/`il2cpp_queue_*` 未声明（同 reflect_base.h 矩阵5 已修族） | CodeGenerator.cs generated.h include 列表 |
| D-STK-2 | stackq | T | create 型 ctor（`il2cpp_stack_create()` 返回新对象）的 newobj 仍发射 `il2cpp_object_new(&System_Collections_Stack_class)`（未声明描述符）→ 双分配 + 编译失败 | CodeGenerator.cs:1311 newobj fallback |

## 3. 备注

- 各用例 GAPS.md 内含每项缺陷的完整因果链与生成码证据；stackq 生成码实证见
  `tests/cases/stackq/gen/src/Cases_StackQ.cpp:31-35`。
- 未触碰 tests/cases 之外任何文件（git status 核对：本批改动仅 6 个新用例目录；
  其余 M/?? 条目为前批次在飞面）。未 git commit。
- 修复优先级建议：D-STK-1（一行 include，解锁 stackq 24 探针）+ D-BOX-5
  （白名单一行）成本最低；D-BOX-6/D-CON-4 是运行时刻真分歧。
