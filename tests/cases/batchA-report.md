# e2e 扩量批次 A 报告（2026-08-06）

执行代理交付：6 个新 e2e 用例（tests/cases/<名>/，仅建用例，未改任何产品代码，
未 git commit）。验证驱动：`scripts/run-tests.ps1 -Compiler mingw -Case <名>`
（net48 oracle live 运行 vs mingw 转译构建运行，LF-join 逐字节 diff）。
`.inflight` 检查：无他代理标记（本批 6 用例目录先建 `.inflight`，完成后删除）。

## 0. 过程说明

- 开工前读取：docs/plans/e2e-sprint-worklist.md、docs/roadmap-status-v5.md、
  现有用例 mathdeep/numchain/strcomp2/hashchain（格式）与 enumflags/arraydeep/
  timecase/strdeep（同域参照），以及 MethodMap.{Array,Enum,DateTime,TimeSpan,
  String,Stack,Queue,ArrayList}.cs + 对应 runtime 头，逐一确认接线面。
- 风险构造先探针（临时目录，已清理）：多维数组、byte/long 底层枚举、
  DateTime 格式说明符、DayOfWeek 拼接、TrimStart 空集、Stack/Queue/ArrayList
  newobj 路径。
- 发现批次 B（另一代理）已交付 stackq/pathext/charstr/consoleio/boxunbox/
  recurse2（tests/cases/batchB-report.md）：**stackops 与 stackq 重复 → 删除**
  （保留唯一的 ArrayList 域 listops）。strpath 与 pathext 互补（string 方法 vs
  System.IO.Path 方法，无重复）。

## 1. 新建用例清单 + 每个 diff 结果

| 用例 | 域 | 探针面 | 输出行数 | 结果 | 阶段 |
|---|---|---|---|---|---|
| **arrdeep** | System.Array | GetLength(0)/GetUpperBound(0)/Length、空数组 UB==-1、GetLength(1)→IndexOutOfRange、越界读写/负索引捕获、运行时长度路径、二维式嵌套循环初始化(行×列索引算术)、Array.Copy 局部、foreach 累加、byte[]/string[] 方法面 | 31 | **PASS** | diff 一致 |
| **enumops** | enum 位运算 | int 底层 [Flags] 全位运算（\|&\^~ 组合、\|=/&= 复合、成员/交集测试、0 字面量比较）、(int)/(Enum)(int) 往返、未命名值同一性、^ 链/双重补码、非连续 Level 关系运算、显式 Zero 成员的第二个 [Flags] | 41 | **PASS** | diff 一致 |
| **timeops** | DateTime/TimeSpan | 3/6 参 ctor、全 getter、DayOfWeek 名称打印（boxed-enum 路径）、AddDays/AddHours/AddMinutes 跨月跨年+负值、闰年 2/28-2/29、op_Subtraction/Subtract(DateTime)→TimeSpan、CompareTo、文化中立格式（yyyy-MM-dd HH:mm:ss/yy/MM/dd/MM/dd/yyyy/dd/MM/yyyy/HH:mm/yyyy/MM/dd）、TimeSpan 3 参/ticks ctor、组件+Total*+Ticks、Add/op_Addition/Subtract/op_Subtraction/Negate/op_UnaryNegation、CompareTo、链式算术 | 51 | **PASS** | diff 一致 |
| **strpath** | string 路径类操作 | LastIndexOf+Substring 的 windows/posix 路径目录/文件名/扩展名提取、父/祖父目录回退、IndexOf 多形态（char/string × 0/1/2 参+越界）、LastIndexOf 多形态（含空串 net48 语义）、Substring 边界+异常、Join(string,string[])、Replace(char,char) 链、Trim 家族、StartsWith/EndsWith(string,StringComparison) 组合+无效模式/空引用异常 | 59 | **PASS** | diff 一致 |
| **listops**（奖励） | System.Collections.ArrayList | Add/get_Item/set_Item/Count/Clear/RemoveAt(IndexOf(v))/Contains/增长扩容/string 元素/交错 Add-IndexOf-RemoveAt+累加 | 25 | **FAIL** | cmake_build（2 缺陷，见 D-AL-1/2） |

4 个必建用例全部 PASS（全通 → 允许加奖励用例；因批次 B 已有 stackq，
奖励只保留唯一域 listops）。
5 用例中 4 PASS / 1 FAIL（FAIL 的 listops 暴露 2 个精确转译器缺陷，是有效交付，
且证明 D-STK-1/2 缺陷族同样覆盖 arraylist.h）。

net48 oracle 证据：[BASH] 每用例 `out/<名>.exe` 运行 exit=0，行数与 expected.txt
逐字节一致（仅尾换行差异，与 harness 自身 LF-join 比较口径一致）；harness diff
阶段结果如上（PASS 4 例均在本次会话重跑复核）。

## 2. 转译器/运行时缺陷清单（本批暴露，均未修）

图例：T=转译器，R=运行时，H=harness。每项 = 用例 + 现象 + 疑似根因文件。

| # | 用例 | 域 | 现象（实测） | 疑似根因文件 |
|---|---|---|---|---|
| D-ARR-1 | arrdeep | T | `int[,] m = new int[2,3]`（多维 newarr/local）→ LOUD `multidim-array/function-pointer local loc0 not supported (complex trailing signature)`；用例已将多维段注释为验收面 | MethodBodyReader.cs:193（LocalTypeKind 0x14/0x19 只报错不解码） |
| D-ARR-2 | arrdeep | T | `Array.GetLowerBound(int)` 无 MethodMap.Array.cs 条目 → 生成名 `System_Array_GetLowerBound_<hash>` 未定义 → cmake 链接失败；且 runtime icall 文档把 GetLength/GetUpperBound 标为"one-dimensional scope"（il2cpp_array_ext.h） | MethodMap.Array.cs（无 GetLowerBound case）；il2cpp_array_ext.h |
| D-ARR-3 | arrdeep | T | 多维数组 `m.Rank` / `m[i,j]` 元素访问同样不可达（依赖多维存储模型，未接线） | MethodBodyReader/StackFold 多维链（同上） |
| D-ENU-1 | enumops | T | 枚举底层类型 byte（value__ 字段 0x05）/ long（0x0A）→ LOUD `field type byte 0x05/0x0A not supported in M2`；仅 int 底层可转译 | MetadataLoader.cs:216 DecodeFieldSignature |
| D-ENU-2 | enumops | T | 枚举值 ToString / HasFlag / Enum.Parse / GetUnderlyingType 均不可达：`"label="+d.DayOfWeek` 触发 constrained. callvirt System.Object::ToString → 生成名未定义（timeops 已用 boxed-enum WriteLine 路径规避）；Parse/HasFlag LOUD（ref/out + 无 flags 原语） | MethodMap.Enum.cs:199/227；TryMapObject（MethodMap.Object.cs）枚举接收者缺条目 |
| D-TIM-1 | timeops | R | `DateTime.ToString("dd MMM yyyy")` 文化差异：net48(zh-CN) 输出中文/本地月份缩写 vs runtime 英文 "Jun" → 真分歧；"MMM"/月名说明符与无格式 ToString() 均排除在用例外（timeext 已文档化文化面） | timeext.cpp DateTime 格式器（固定 en-US） |
| D-TIM-2 | timeops | T | `"label="+d.DayOfWeek`（枚举值拼接）→ constrained ToString 生成名（同 D-ENU-2）；用例改用 `Console.Write("dow_name="); Console.WriteLine(d.DayOfWeek)` 走 boxed-enum→il2cpp_enum_to_string（timecase 接线） | 同 D-ENU-2 |
| D-STR-1 | strpath | T | `string.Split(...)` 无 Split case → 生成名回退（strcomp2 已记录同缺口）；用例注释为验收面 | MethodMap.String.cs（无 Split 条目） |
| D-STR-2 | strpath | T | `Replace(string, string)` 无 string 形态 icall（Replace case 硬编码 char16_t 强转）；仅 (char,char) 接线 | MethodMap.String.cs:39 Replace |
| D-STR-3 | strpath | T | `Trim(char[] 非空)` → `newarr System.Char` → LOUD `newarr element type System.Char not supported (audit C4)`（非优化 csc 下空集也 newarr；Release 下空集走 Array.Empty<char> 已接线） | CodeGenerator.cs:3118 ArrayElementType（无 System.Char 臂） |
| D-AL-1 | listops | T | **generated.h 包含链缺 `arraylist.h`** → `il2cpp::il2cpp_arraylist_index_of/contains/get_item...` 全部"no member in namespace 'il2cpp'"（20 错误）。探针实证：在生成 gen 的 generated.h 手工补 `#include "arraylist.h"` 后 index_of/contains 等 14 个错误消除 → 纯缺 include | CodeGenerator.cs generated.h include 列表（EmitMain，缺 arraylist.h/stackqueue.h） |
| D-AL-2 | listops | T | create 型 ctor（`il2cpp_arraylist_create()` 返回新对象）的 newobj 仍发射 `il2cpp_object_new(&System_Collections_ArrayList_class)`（未声明描述符）→ 双分配 + 编译失败（同批次 B D-STK-2，ArrayList 同型） | CodeGenerator.cs:1311 newobj fallback（应走 runtime `il2cpp_arraylist_class()` 访问器） |

## 3. 备注

- 各用例文件头含完整的 KNOWN GAPS / ACCEPTANCE SURFACE 注释（引用精确
  文件:行）；listops 生成码缺陷证据见
  `tests/cases/listops/gen/src/Cases_ListOps.cpp:38-39,92,97`（重跑后再生）。
- 未触碰 tests/cases 之外任何文件（git status 核对：本批新增
  arrdeep/enumops/timeops/strpath/listops 5 个目录 + 本报告；stackops 因与
  stackq 重复已删除）。未 git commit。
- 与批次 B 缺陷同族：D-AL-1=D-STK-1（缺 include）、D-AL-2=D-STK-2（create-ctor
  描述符），修复时应一并处理 stackqueue.h+arraylist.h。D-ENU-1 为本次新发现
  （byte/long 枚举底层），D-ARR-1/2 为本次新发现（多维数组 + GetLowerBound）。
- 修复优先级建议：D-AL-1（一行 include，解锁 listops 25 探针）+ D-AL-2
  （newobj 路由访问器）成本最低；D-ENU-1 为枚举底层模型缺口。
