# e2e Sprint Worklist — 41 例剩余 FAIL 逐例精确修复点（施工单）

日期：2026-08-05（02:4x 快照，全量重跑证据：`%TEMP%\opencode\e2e-sprint\transpile-logs\<case>.log` + `build-out\<case>.build.log/.diff.txt`）
消费方：下一批接线代理（每行 = 一个可独立认领的最小施工单）。
验证基线：`dotnet src/il2cppc.dll <refExe> <genDir>` 直跑（il2cppc.dll 01:45:21 > 最新源 StackFold.cs 01:20:29，环境稳定）；diff 用例为 MinGW 构建+运行逐行对比 net48 参考输出。

## 0. 关键新发现（本批转译级全量重跑 + 15 例构建运行）

1. **overflow 实为 PASS**：matrix3 的 net48build 失败是 8 并行下 dotnet 构建争用伪影。本批串行重跑：ref 构建 0 错 → 转译 exit=0 → cmake 构建 0 错 → 运行 gen exit=0 → **逐字节 PASS**。matrix4 的 runner 需对缺失 ref 的用例串行构建（或失败重试一次）。
2. **stringbuilder/exdeep 都是异常匹配问题，不是容量语义**：
   - stringbuilder L1-33 全部正确（容量语义 ✓），崩溃在 L34 `catch(ArgumentOutOfRangeException)` 不匹配 → 未处理。根因：`src/runtime/src/stringbuilder.cpp:99` 的链 `"System.ArgumentOutOfRangeException,System.ArgumentException,System.Exception"` 违反 il2cpp_exception_ext.h:23-25 的契约（matcher 从第一个逗号后开始走链，首元素永不匹配）→ 必须 `"{T},{T},{Base1},..."` 重复首元素。
   - exdeep f18（L29）：同一 try 区多 catch 子句，第 2+ 子句永不生效 → 未处理。生成码结构（Cases_Exdeep.cpp L291-299）是"同类型 catch + 守卫重抛"模式；**独立 C++ 复现证明 GCC 的同类型 catch 重抛不继续到下一子句**（rethrow-continue-test.exe 输出 `OUTER: unhandled`，与 exdeep 崩溃完全一致）→ 生成码模式必须改（单 catch + 子句链式分发）。**2026-08-05 已修复**：CodeGenerator.cs EH dance 对同 try 范围 ≥2 catch 子句发单 `catch (DotNetException&)` + `if (matches) {} else if (matches) {} else { throw; }` 分派链（OpenChainCatch/ContinueChainCatch/CloseChainCatch）；exceptionchain2 c8 的"空首子句被丢弃"潜在吞异常缺口一并修复（空子句保留在链中）。验证：exdeep 37/37、exceptionchain2 17/17、exceptions/cctor PASS；EH 单元 7/7。
3. **formatext 大部分是捕获编码伪影**：L11/L12/L23/L32/L53 货币符号双方都打 ¥（U+00A5，探针 L13=165 双端一致）；L65/66 gen 正确打 ∞（U+221E）；字节差异来自 ref(GBK 936) vs gen(UTF-8) 控制台捕获。真实分歧仅 L73：`D100` net48 自定义回退 "D112345" vs gen FormatException（用例注释已文档化）。→ harness 统一 UTF-8 捕获（`[Console]::OutputEncoding`/chcp 65001）+ 可选 D100 回退。
4. **gccase 现卡 L1**：`GC.GetTotalMemory(false) > 0` False vs True（icall 返回 0）；L2-L4 已通过（GetGeneration 提升 ✓）。修复 = gc.cpp 累计字节计数器 accessor（用例头注释即工单）。
5. **recurse/delechain 生成码错误行已精确到行**：recurse entry.cpp:14 无参调用 vs Main 声明 `(Il2CppObject* arg0)`（Main(string[])）；delechain Cases_Delechain.cpp:137/138/160/161 事件 add/remove 参数与静态字段被解析成 int32_t（泛型委托 Func`2 类型解析回退）。
6. **delegate2 的 Ldsflda 与委托无关**：`"A:"+order`（静态 int 字段拼接）——Roslyn 发 `ldsflda + call instance Int32::ToString()`（byref 接收者实例调用）。5 处命中（IL_0011/IL_01a6/IL_023c/IL_03a4 等）。

## 1. 全量 41 例施工表（40 例真实 FAIL + overflow 已 PASS）

图例：域 = T(转译器)/R(runtime)/H(harness)/C(用例自身)。L# 为输出行号（net48 参考 vs gen）。

| # | 用例 | 状态 | 精确错误/差异（本批实测） | 域 | 修复动作一行式 | 优先级 |
|---|---|---|---|---|---|---|
| 1 | arrayext2 | transpile | `System.Array::FindIndex not wired: runtime icall takes (array,startIndex,count) which the .NET Predicate overloads do not supply` @MethodRefResolver.cs:366 | T+R | arrayext2.cpp 加 `icall_array_find_index(array,start,count,predicate)`（委托调用）+ transpiler 映射 Predicate 形参 | P1 |
| 2 | base64case | transpile | `Encoding.GetBytes: raw-buffer code-page protocols (il2cpp_encoding_get_bytes(text,page,dst,capacity))...unwireable until an allocating runtime icall and/or emitter support exists` @MethodRefResolver.cs:366 | T+R | 新增分配式 `icall_encoding_get_bytes_alloc(string,page)->byte[]` + emitter byte[] 返回支持 | P2 |
| 3 | bitconvcase | transpile | `BitConverter.ToString(byte[]): no runtime byte-array hex formatter on the bitconverter.h surface (24 entries)` @MethodRefResolver.cs:366 | R | bitconverter.cpp 加 `icall_bitconverter_to_string_hex(byte[])->"AB-CD"` 格式器 | P1 |
| 4 | cctorchain | diff | L14 `boom_caught=2` vs 1（.cctor 抛异常后第二次访问不重抛；15/15 行仅此一处） | R | cctor 守卫区分"成功/失败"：失败缓存 TypeInitializationException 并在后续访问重抛（net48 语义） | P1 |
| 5 | collchain | transpile | `ArrayList.Remove: Remove(object) has no runtime icall (arraylist.h exposes remove_at by index only)` @MethodRefResolver.cs:366 | R | arraylist.cpp 加 `icall_arraylist_remove(self,object)`（按值移除首个匹配） | P2 |
| 6 | consolecase | transpile | `Console.IsInputRedirected not wired: the runtime has no icall_console_is_input_redirected` @MethodRefResolver.cs:366 | R | console.cpp 加 `icall_console_is_input_redirected`（GetConsoleMode 检测） | P1 |
| 7 | convcase | **PASS** | ~~L60/L61 `(uint)-1`→4294967295 vs -1、`(uint)int.MinValue`→2147483648 vs -2147483648（uint32 按有符号打印）；L79 `checked((int)vPlus1)`→OVERFLOW vs -2147483648（conv.ovf.i4 无范围检查）；79/79 行~~ 2026-08-05 修复后 **79/79 ALL MATCH**（详见 §6） | R | uint32→string 无符号路径（或 conv.u4 结果类型化）+ conv.ovf.i4/i8 范围检查抛 OverflowException | P0 |
| 8 | convertcase | **PASS** | ~~L11/L12 `Convert.ToInt32(2.9)`→2 vs **3**（截断；应四舍五入 away-from-zero）；L26 `ToInt32(2147483648L)`→**-2147483648** vs OverflowException（checked 缺失）；30/30 行~~ 2026-08-05 修复后 **41/41 ALL MATCH**（含 10 行新增边界探针，详见 §11） | R+T | Convert.ToInt32(double) 改 half-to-even 四舍五入（runtime）+ conv.u8 零扩展（T 域已在工作树自愈）+ ConvU8 单元测试字节修正 | P0 |
| 9 | delechain | cmake_build | Cases_Delechain.cpp:137:66 `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'`；:138:35 / :160:65 / :161:35 同类（事件 add/remove 的参数与 `_changed` 静态字段被解析为 int32_t） | T | 泛型委托 Func`2 实例化解析为委托类描述符（勿回退 int32 默认实例化） | P1 |
| 10 | delegate2 | transpile | `fold: opcode Ldsflda not supported in M3` @StackFold.cs:773；触发构造 = `"A:"+order` 静态 int 字段拼接（ldsflda + call instance Int32::ToString()，5 处） | T | StackFold 实现 Ldsflda（静态字段取址 + byref 接收者调用）或折叠为 ldsfld+值调用 | P0 |
| 11 | delegatecb | transpile | `generic instantiation of Marshal.GetFunctionPointerForDelegate has no BCL mapping in M5` @MethodRefResolver.cs:224 | T | 泛型 Marshal 方法映射到 `il2cpp_delegate_get_function_pointer` | P1 |
| 12 | encodingcase | transpile | 同 #2 GetBytes raw-buffer | T+R | 同 #2 | P2 |
| 13 | environment2 | diff | L7 `tick_delta=31` vs 15/32 计时抖动（9/9 行，跨运行不等） | C | 用例/harness 容差（该行改为区间断言或 runner 重试），非 runtime 缺陷 | P2 |
| 14 | excext | transpile | `Exception.get_Message not wired: M3 exception carrier (DotNetException) is a C++ type-tag + message, not a managed Exception object (catch variables are unsupported)` @MethodRefResolver.cs:366 | T+R | catch 变量 + get_Message icall（异常对象模型里程碑项） | P2 |
| 15 | exdeep | **PASS** | ~~L29 f18（同 try 区 3 catch 子句，第 2 子句 InvalidOperationException 应命中）→未处理崩溃 `Unhandled managed exception: exception thrown`，gen exit=1，37/37 行中 28 行通过（f16 throw-null ✓、f17 首子句 ✓、f3 外层区 ✓）；**独立 C++ 复现确认 GCC 同类型 catch 重抛不续检**~~ 2026-08-05 修复后 **37/37 ALL MATCH + exit=0**（详见 §12 修复记录） | T | EH 生成码改单 catch + 子句守卫链式分发（`if(match1){} else if(match2){} else throw;`）替代同类型多 catch 重抛 —— **已落地** | P1 |
| 16 | formatcase | diff | L24/L25 `∞`(U+221E) vs **Infinity**（double ±Infinity ToString；37/37 行） | R | double→string ±Inf 映射 U+221E ∞/-∞（net48 行为） | P0 |
| 17 | formatext | diff | L11-L53 货币符号 = **捕获编码伪影**（双端都打 ¥ U+00A5；探针 L13=165 双端一致）；L65/66 同伪影（gen 正确打 ∞）；**L73 真实分歧**：`D100`→"D112345"（net48 自定义回退）vs FormatException；73/73 行 | H+R | harness：双端 `[Console]::OutputEncoding=UTF8` 捕获（L11-66 自愈）；可选：formatext.cpp 整数标准格式多位数精度回退自定义（消除文档化分歧） | P1 |
| 18 | gccase | diff | L1 `GC.GetTotalMemory(false)>0` False vs True（icall 返回 0；L2-L4 已过）；4/4 行 | R | gc.cpp 累计堆字节计数器公开 accessor，icall_gc_get_total_memory 返回真值 | P1 |
| 19 | gcext | transpile | `WeakReference::.ctor not wired: runtime weak-ref icalls take client-owned void** slots...finalization wiring (WaitForPendingFinalizers) is missing` @MethodRefResolver.cs:366 | R | WeakReference 对象模型 + icall_weakref_ctor/get_target/is_alive + WaitForPendingFinalizers | P2 |
| 20 | genlist | transpile | `generic instantiation of Cases.GenList.Id with generic argument 0x0E (ELEMENT_TYPE_STRING) has no expansion in M6 -- only the T->int32 default instantiation` @MethodRefResolver.cs:333 | T | EnsureInt32DefaultInstantiation 扩展：string（0x0E）/int64 实例化 | P1 |
| 21 | guidcase | transpile | `callvirt owner System.Object not in translated types` @CodeGenerator.cs:1477；触发 = boxed Guid 的 `Object::ToString()` callvirt（IL_0014/IL_0022/IL_00df） | T | VirtualTarget：属主 System.Object 时按接收者 box 类型派发（对象头 vtable ToString） | P1 |
| 22 | hashtable2 | transpile | `Console.WriteLine(object) with unknown argument type (no tracked box type) not wired in M5` @MethodRefResolver.cs:366 | T | StackFold box 表达式携带类型信息，WriteLine(object) 按 box 类型分发 | P2 |
| 23 | hashtablecase | transpile | 同 #22 | T | 同 #22 | P2 |
| 24 | iocase | transpile | `Path.Combine with 3 arg(s) not wired: the runtime icall takes exactly (string, string)` @MethodMap.IO.cs:43（MethodRefResolver.cs:383 包装抛；8/5 14:2x 复现：dotnet run il2cppc → exit=2） | T+R | path.cpp 3 参 Combine icall（或 transpiler 展开为嵌套 2 参 Combine）；8/5 14:25 核查 MethodMap.IO.cs mtime=8/3 19:12:39（IO 域代理未在途）、无构建进程 → 未修 | P1 |
| 25 | mathext | diff | L22/L28/L33/L45/L53/L89 `∞` vs **Infinity**（同 formatcase）；100/100 行 | R | 同 #16（一处修复 +2 PASS） | P0 |
| 26 | overflow | **PASS** | matrix3 net48build 为 8 并行 dotnet 构建争用伪影；**本批串行全管线逐字节 PASS**（ref exit=0 / transpile=0 / cmake=0 / gen exit=0） | H | matrix4 runner：缺失 ref exe 的用例串行构建或失败重试一次 | P0 |
| 27 | parsecase | transpile | `Int32.TryParse not wired: the icall takes an int32_t* out result and the M-stage transpiler has no ref/out support (ref/out milestone item)` @MethodRefResolver.cs:366 | T | ref/out 里程碑：TryParse int32_t* out 参数接线 | P2 |
| 28 | pinvoke | transpile | `call at offset 0x0047: resolved System_Runtime_InteropServices_Marshal_GetLastWin32Error_* as INSTANCE (pops this) but the stack is empty -- wrong static/instance classification` @StackFold.cs:1369 | T | Marshal 静态方法分类修正（GetLastWin32Error） | P0 |
| 29 | pinvokeext | **PASS** | **2026-08-06 10:2x 批 B 修复落地**：TempSlotPass.cs:241 KindOf 加 `or "ptr"`（一处）→ 隔离 patched dll 预验证 + 真实 dll 重转译 → gen `intptr_t t5` 独立槽 → 构建 → 运行 diff **9 行逐字节同 net48**（5/0/cba/-1/123/0/123/0/0）。TempSlot 单测 15/15 PASS，转译器 0 错 0 警 | T | 已闭环（§6 详细） | P0 |
| 30 | randomcase | transpile | `String.Join with 1 arg(s) not wired in M5 (the runtime join takes (string, string[]))`；用例为 `string.Join(",", buf)` @MethodRefResolver.cs:366 | T+R | 接入 randomcase 所用 Join 重载（string[]/object[]）到 runtime join | P2 |
| 31 | recurse | cmake_build | entry.cpp:14:5 `no matching function for call to 'Cases_Recurse_Main_9866ee88af8eb9db'`（声明 `(Il2CppObject* arg0)`，entry 无参调用；Main(string[])） | T | entry.cpp 生成器：Main 带参时传 `nullptr` 实参 | P1 |
| 32 | strdeep | cmake_build | **2026-08-05 matrix4+solo 复现：转译 OK**（Array.Empty 已于 M6 batch12 闭合），构建被 6 处未声明符号阻断（4 符号）：`System_String_PadLeft_ea75a81aaadfb9b6`（(int)）、`System_String_PadLeft_780ecb62502555c1`（(int,char)）、`System_String_PadRight_a7719e7aefeb53a7`（(int,char)）、`System_String_PadRight_e8183c651800b180`（(int)），对应 strdeep.cs:186-191 的 6 断言 | T+R | MethodMap.String.cs **无 PadLeft/PadRight 条目** → 默认 `System_String_<name>_<hash>` 命名发射而 runtime 无符号。T：MethodMap.String.cs 映射 `icall_string_pad_left/right`（仿 TrimStart/TrimEnd 的 drop-arg 模式）；R：string.cpp/ext 实现——net48 语义：totalWidth≤Length 返回原串、否则左/右补 padChar（缺省 0x20） | P0 |
| 33 | stringbuilder | diff | L1-33 全对；L34 `catch(ArgumentOutOfRangeException)` 不匹配 → `Unhandled managed exception: capacity`（gen exit=1）；根因 = stringbuilder.cpp:99 链缺首元素重复 | R | stringbuilder.cpp:99 链改 `"System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.Exception"`（契约 il2cpp_exception_ext.h:23-25）；并 grep 全仓同型短链 | P0 |
| 34 | stringext | transpile | 同 #32 | T | 同 #32 | P0 |
| 35 | swcase | transpile | `Stopwatch.get_IsRunning not wired: isRunning is object state; stopwatch.h has no bool-returning entry point (only the 5 static primitives)` @MethodRefResolver.cs:366 | R | stopwatch 对象加 isRunning 状态 + `icall_stopwatch_get_is_running` | P1 |
| 36 | threadcase | transpile | `Thread.CurrentThread not wired: the runtime has no Thread object model (ManagedThreadId/Priority are unreachable)` @MethodRefResolver.cs:366 | R | Thread 对象模型 + CurrentThread/ManagedThreadId/Priority（线程里程碑） | P2 |
| 37 | timecase | **PASS** | `get_TotalMinutes` 已接线（MethodMap.TimeSpan.cs:27 → icall_timespan_get_total_minutes）；matrix5 全 PASS（11 行，2026-08-06 批 B 复核） | R | ~~timespan icall 补 TotalMinutes~~ 已闭环 | P0 || 38 | timeext | diff | L1 `zh-CN` vs en-US、L3/L9/L10 当前文化日期（2020/6/15 vs 06/15/2020）——文档化分歧；**L20 起崩溃**：`year5.ToString("y")`（标准年月说明符）→ `Format specifier was invalid` 未处理；stdout 19/84 行 | R | DateTime 格式器实现标准 "y"（年月）说明符；文化数据 zh-CN（P2 可选） | P1 |
| 39 | typecheckcase | transpile | `TypeSpec signature byte 0x1D (SZARRAY) not GenericInst(0x15) in M5` @CodeGenerator.cs:1326 | T | GenericTypeSpecDefinitionName 处理 0x1D SZARRAY（isinst 数组类型） | P1 |
| 40 | valuesem | diff | L20 `double hash NaN: -524288` vs 2146959360（0xFFF80000 vs 0x7FF80000，NaN 符号位未规范化）；43/43 行其余全对 | R | double GetHashCode：NaN 规范化（掩符号位 → 0x7FF8...0000 哈希） | P1 |
| 41 | versioncase | transpile | `System.Version object gap: no runtime object ctor icall...Runtime work order: System_Version_class descriptor (int32 component storage) + icall_version_ctor(Il2CppObject*,string) + icall_version_get_*(Il2CppObject*)` @MethodRefResolver.cs:366 | R | 错误消息自带完整工单：class descriptor + ctor/get icall + BclAllocExpr | P2 |
| 42 | delegateinst | run | 0xC0000005 crash 0/24��2026-08-06 ���֣�exit=-1073741819�����κ������: ȫ�� 18 �� delegate_new ���� 1-arg `il2cpp_delegate_new(&fn)`��ʵ�� target ��ʧ����StackFold.cs:1849 ֻȡ FuncPtrExpr������ ctor �� object target ʵ�Σ���CodeGenerator.cs:1654 �� 2-arg ���䣻invoke �� icall_multicast_invoke_i4��Cases_Delegateinst.cpp:85������ null this ��ʵ������ Cases_Counter_Add �� ���ֶ� AV��24 ��Ԥ�ڣ�expected.txt��0 ��ƥ�� | T | StackFold.cs:1847-1865 proxy ��ȡ�� FuncPtr ʵ���� target ���� DelegateNewExpr��Expr.cs:194 �� Target �ۣ���CodeGenerator.cs:1654 ���� 2-arg `il2cpp_delegate_new(&fn, target)`����runtime �Ѿ�����il2cpp_delegate.h:50 default nullptr��delegateext.cpp:259 �� (target,a0,..) �ַ�ʵ��Ҷ������̬ί�� target=nullptr ��Ϊ���� | P0 |

## 2. 分类统计（本批实测，41 → 40 真实 FAIL）

| 类别 | 数量 | 占比 | 说明 |
|---|---|---|---|
| 转译器接线缺（LOUD 中止） | 26 | 63.4% | 未接 BCL 映射 19（含 Array.Empty×2、Marshal×2、Encoding×2 等成对）、IL fold 缺口 4（Ldsflda / callvirt Object / TypeSpec SZARRAY / FindIndex Predicate）、static/instance 分类错 2、box 类型跟踪 2 |
| runtime 输出不一致（diff） | 11 | 26.8% | 数字格式 4（uint 符号/Convert 舍入/±Inf×2/NaN hash → 实际 5 域）、异常匹配 2（AOORE 链、多子句分发）、cctor 异常缓存 1、GC 统计 1、DateTime 说明符 1、编码伪影 1 |
| 转译器生成码 bug（C++ 编译失败） | 2 | 4.9% | delechain 类型错配、recurse 入口签名 |
| 用例自身 | 1 | 2.4% | environment2 计时抖动（flaky） |
| （已 PASS） | 1 | — | overflow：matrix3 net48build 为并行构建伪影，solo 全管线逐字节 PASS |

> 注：26 例 transpile 中 19 例的消息自带修复提示（"not wired: ..." 明确缺口与形态），另 7 例（genlist/typecheckcase/guidcase/pinvoke/pinvokeext/delegate2/hashtable2）为分类/折叠类，修复点已精确到 文件:行。

## 3. P0 清单（修 5 个最高优先 → 至少 +5 PASS，全部有实测证据）

| 排序 | 施工单（#） | 域 | 预计 +PASS | 理由 |
|---|---|---|---|---|
| 1 | overflow（#26） | H | **+1（已验证）** | 无需代码修复；runner 串行构建/重试即可，solo 已逐字节 PASS |
| 2 | formatcase + mathext（#16/#25） | R | **+2** | 同一修复：double→string ±Inf → U+221E；两用例仅此一处分歧（37/37、100/100 行） |
| 3 | pinvoke + pinvokeext（#28/#29） | T | **+2** | 同一修复：Marshal 静态方法分类；LOUD 已指出 offset 与符号名，StackFold.cs:1369 |
| 4 | strdeep + stringext（#32/#34） | T+R | **+2（strdeep 半程）** | Array.Empty\<T\> 已闭合（strdeep 转译 OK）；strdeep 剩余 PadLeft/PadRight 4 符号未声明（6 处，matrix4 cmake_build，2026-08-05 复现）；stringext 另见 #34 |
| 5 | stringbuilder（#33） | R | **+1** | 一行链修正（stringbuilder.cpp:99）；L1-33 已全对，L34 是唯一崩溃点 |

合计潜在 **+8 PASS**（保守按 5 项 = +8，若按"5 个修复点"= +8；即 47 → 55/88）。
次选 P0（未入 Top5，各 +1）：convcase（#7）、convertcase（#8）、timecase（#37）、delegate2（#10）。
建议并行批次（避免同域冲突）：批 A = overflow(harness)+stringbuilder(runtime)；批 B = formatcase/mathext(runtime number)；批 C = pinvoke/pinvokeext+strdeep/stringext+delegate2(transpiler)；批 D = convcase+convertcase+timecase(runtime number)。

## 4. 交接注意事项

- 每例的完整错误栈/构建日志/diff 明细：`%TEMP%\opencode\e2e-sprint\transpile-logs\<case>.log`、`build-out\<case>.build.log`、`build-out\<case>.diff.txt`；生成码现场：`tests\cases\<case>\gen\src\*.cpp`。
- 异常链契约（勿再踩）：`il2cpp_exception_ext.h:23-25` —— 链首元素仅整串精确比较，走链从第一个逗号后开始，链必须 `"{T},{T},{Base1}..."` 重复首元素。**修 #33 时全仓 grep 同型短链**（如 random.cpp:92 的 `"AOORE,System.Exception"`）。
- 多 catch 子句生成码（#15）：禁止再用"同类型多 catch + 守卫 `throw;`"——GCC 同类型 catch 重抛不续检（已独立复现）；改单 catch + `if(match)… else if(match)… else throw;`。
- formatext/timeext 的文化差异与编码伪影与 runtime 语义无关的部分（#17/#38）改 harness：双端统一 UTF-8 捕获；改后 #17 的 L11-L66 应自愈。
- 改码后验证：`dotnet build src/il2cppc -c Release`（0 错 0 警）+ 单例重跑（ref exe 复用）+ 全量 matrix4（runner 修 overflow 后预期 47+8=55 起步）。

## 5. timecase 诊断修复记录（2026-08-05 timecase 代理）

**#37 根因（本域，已修复）**：`MethodMap.TimeSpan.cs:27-31` 对 get_TotalMinutes/Hours/Days/Milliseconds
LOUD 抛 NotSupportedException；runtime 侧只有 `icall_timespan_get_total_seconds`
（il2cpp_time.h:114 / time.cpp:402）。

**修复（T+R，time 域）**：
- runtime：time.cpp + il2cpp_time.h 新增 4 个 icall（纯算术 ticks / TicksPer*，double）
  `icall_timespan_get_total_minutes/hours/days/milliseconds`
- transpiler：MethodMap.TimeSpan.cs 4 个 getter 全部接线（`"d"` 结果、`deref:ts`，同 TotalSeconds 形态）
- 验证：`dotnet build src/il2cppc` 0 err；单元 `Resolve_TimeSpan_TotalGetters` 4/4 PASS
  （断言 IsInstance=true —— 实例 getter 与 get_Ticks 同形，勿写 false）；
  运行时 `test_time.exe` ALL PASS（新增 1-day/90min/10000-ticks 精确 double 断言）；
  timecase 转译错误从 `get_TotalMinutes not wired` 推进到下一阻点（见下），证明接线生效。

**下一阻点（他域——enum/boxing/console，精确施工单）**：timecase.cs L14
`Console.WriteLine(new DateTime(2020,6,15).DayOfWeek)` → `box System.DayOfWeek` + WriteLine(object)。
实测错误：`System.Console::WriteLine: Console.WriteLine(object) with box type System.DayOfWeek not wired in M5`
（MethodMap.Console.cs:42）。根因链 = MethodMap.Enum.cs "REGISTER WIRING REPORT" 已文档化的 enum 里程碑：
1. `CodeGenerator.cs:2089` box_ switch `_ => throw` —— box of enum 无 runtime helper（enum box 里程碑）
2. `MethodMap.Console.cs:42` —— WriteLine(object) box-type switch 无枚举臂；net48 期望 "Monday"
   （2020-06-15 周一），需 `il2cpp_enum_to_string`（enumext.h:148，已存在）——注册后按名打印
3. `System.DayOfWeek` 元数据注册（il2cpp_enum_register，Sunday=0..Saturday=6，Int32 底层；
   按 MethodMap.Enum.cs 报告 a-e：MetadataLoader literal 读取 / 生成静态 Il2CppEnumInfo /
   cctor 或 entry.cpp 注册钩子 / 容量 32）
4. 依赖项：boxed 枚举的 class 描述符/ldtoken 发射（同样在报告 gap chain 内）
完成后 timecase 预期全 11 行 PASS（其余行已全部接线：ctor3/6、Now/UtcNow、AddDays/Add/Subtract、
op_Subtraction、get_Year/DayOfWeek、Total*）。

**重跑记录（2026-08-05 08:3x，timecase 代理二次派单）**：
- il2cppc.dll 重建 8:28:18（0 err 0 warn，> 最新源 8:20:25）；直跑 timecase → EXIT=2
  `System.Console::WriteLine: Console.WriteLine(object) with box type System.DayOfWeek
  not wired in M5` @MethodRefResolver.cs:376 —— 与本单记录逐字一致，阻点无漂移。
- 本域修复复验全绿：单元 Resolve_TimeSpan_TotalGetters 4/4 PASS；
  test_time.exe "all runtime time tests passed" EXIT=0（含 1-day/1:30/10000-ticks Total* 断言）；
  MethodMap.DateTime.cs 全表复读（ctor3/6、Now/UtcNow、get_Year/DayOfWeek、AddDays、
  Subtract/op_Subtraction、ToString(format) 全部接线）。
- mtime 冲突复检：time 域文件无他域改动（time.cpp/il2cpp_time.h 8/5 3:26 为本修复，
  MethodMap.DateTime.cs 8/3、timeext.cpp/timeext2.cpp 8/4 未动）。
- 施工单 4 点复检仍精确：CodeGenerator.cs:2089 box_ `_ => throw`（无枚举臂）；
  MethodMap.Console.cs:42 box-type switch 无枚举臂（仅 Double/Single/Int32/Boolean/Char/
  StringBuilder/String）；enumext.h:99 il2cpp_enum_register + :148 il2cpp_enum_to_string
  (Il2CppClass*, int64_t) 均在；MethodMap.Enum.cs REGISTER WIRING REPORT a-e 未被改动。

## 6. pinvoke/pinvokeext 诊断修复记录（2026-08-05 marshal 代理，#28/#29）

**根因（本域，已修复）**：MethodMap.Marshal.cs 的 IntPtr 参数守卫只认 0x11
（VALUETYPE+TypeHandle），而 mscorlib 声明 IntPtr 参数用原生整型字节 **0x17/0x18**
（ELEMENT_TYPE_I/U）——实测 pinvokeext.exe 的 MemberRef blob PtrToStringAnsi [00,01,0E,18]。
0x11-only 守卫对真实 blob 返回 null → 回退生成名 → INSTANCE 分类 → StackFold
pops-this（#28 GetLastWin32Error 同因 0 参数不触发，故 pinvoke 先于 pinvokeext 前进）。

**修复（T 域，仅 MethodMap.Marshal.cs + tests/unit/MethodMapTests.Marshal.cs）**：
- IsIntPtrByte(b) = b is 0x11 or 0x17 or 0x18，应用于 PtrToStringAnsi/PtrToStringUni/
  AllocHGlobal/FreeHGlobal/Copy 的全部 IntPtr 参数守卫（icall 均有：il2cpp_pinvoke.h:47-60、
  il2cpp_runtime.h:127-130；无 icall 的 SetLastWin32Error/SizeOf/OffsetOf/PtrToStringUni 保持 LOUD 精确）。
- 单测 Sig() 助手重构为真实 blob 模型（参数节含尾随 TypeHandle/元素字节，count 由走查推导；
  旧助手把尾随字节当独立参数 → 非法 blob → MethodSignatureCoder OutOfBounds）。
- 验证：dotnet build src/il2cppc -c Release 0 错 0 警；MethodMapMarshalTests **12/12 PASS**
  （修前 3/11）；全量 785 pass，11 fail 全为他域在途（ConvU8/String/Thread/MemberInfo/Generics/
  HotPath，零 Marshal 域）。

**e2e 前进证据（il2cppc.dll 重建后直跑）**：
- pinvoke：GetLastWin32Error as INSTANCE (pops this) → **icall arg cast code 'r:System.String' not implemented** @CodeGenerator.cs:2381
- pinvokeext：PtrToStringAnsi as INSTANCE (pops this) → 同 'r:System.String' 错误
两例同落下一阻点，分类修复全链生效。

**下一阻点施工单（他域，CodeGenerator.cs/MethodRefResolver.cs——marshal 代理文件域外）**：
- 症状：P/Invoke string 参数调用发射时 EmitIcallCallArgs cast switch（CodeGenerator.cs:2381）
  遇 "r:System.String" 抛错。
- 根因链：ResolveMethodDefinition P/Invoke 分支（MethodRefResolver.cs:264）把
  MethodSignatureCoder 原始编码放入 ParamTypeNames（"r:System.String"）；该编码不是
  EmitIcallCallArgs 的 cast 代码表成员。而 fold 已把 string 参数预 marshal 为 cstr 临时
  （StackFold.cs:1543-1560 StringArgIndices → MarshalUtf8Expr → il2cpp_pinvoke_string_to_utf8_alloc），
  发射只需裸透传。

**✅ 已修复（2026-08-05 pinvoke 代理，pinvoke/pinvokeext 转译阻点清除）**：
- 修复 1（CodeGenerator.cs cast 表）：EmitIcallCallArgs 增 `"r:System.String" => code` 裸透传
  case——fold 预 marshal 的 cstr 临时已是 char*（il2cpp_pinvoke_ext.h:38 契约），native
  const char* 参数直接接受。**注意：不是 "s" cast**（那是 Il2CppString*，方向相反）。
- 修复 2（CodeGenerator.cs cstr 临时声明）：审计发现 cstr 临时声明为 `const char*`，
  但 runtime 契约 alloc/free 均收 char* → 改 `char* {d};`（gen 构建 7 处
  "cannot initialize char* with const char*" 归零）。
- MethodRefResolver.cs:264 **未改 null**（worklist 原拟方案有误）：StackFold.cs:1443
  的 string 参数检测以 `ParamTypeNames != null` 为守卫——null 会使 StringArgIndices 恒空，
  string 参数不再预 marshal，Il2CppObject* 会直接塞进 const char* 形参。保留原始编码
  （兼具 fold 检测 + 发射 case 双职），仅补注释。
- 验证：il2cppc 0 错 0 警；全量单元 812/816（4 失败 = 既有他域在途，零回归）；
  **pinvoke e2e PASS 1/1（mingw 全链：net48 oracle + 转译 + cmake + 运行 + 逐字节 diff）**，
  H5 双 string 缓冲实证（concat_len("hello","x")=6 非 10）；pinvokeext 转译 EXIT=0。

**再下一阻点（M4.5 PinvokeReturnTypeCpp，文档化未修）**：
- pinvokeext gen 构建 1 错：Cases_PinvokeExt.cpp:61 `il2cpp_pinvoke_string_from_utf8(t5)`
  ——reverse_static 返回 IntPtr 落在 int32 载槽（PInvokeReturnTypeCpp 恒 int32_t，
  CodeGenerator.cs:2116），string_from_utf8 收 const char*。需全宽指针模型
  （返回类型 + extern 声明 + 临时 kind + PtrToStringAnsi 参数 cast 联动，
  载槽注记见 MethodMap.Marshal.cs 头）。x64 上 int32 截断即使强转可编译也会错值——
  必须全模型，不可编译级 hack。

**实证确认（2026-08-06 09:5x 批 B 代理重验）**：当前转译器（il2cppc.dll 8:37:24）已为
`il2cpp_pinvoke_string_from_utf8((const char*)(t1))` 加**编译级强转** → gen 构建**通过**
（worklist 原"构建 1 错"已过期）；但运行 **0xC0000005**（exit -1073741819）。无缓冲
stdout（host.cpp 临时 setvbuf _IONBF）实证：输出 "5\n0" 后、写 "cba" 前崩溃 =
`reverse_static`(intptr_t) 截断进 `int32_t t1` → `(const char*)t1` 回扩成错地址 →
`il2cpp_pinvoke_string_from_utf8` 内 strlen 越界。**与 §6 预测完全一致**：必须全宽指针
模型（T 域 CodeGenerator.cs:2116 + 临时 kind + cast 链联动），编译级 hack 只过编译、
x64 运行必错值。开工前状态：#29 转译/构建阻点均已解除，仅剩此 R 返回截断。

**全链已实证在源（2026-08-06 10:0x 复核，批 A 代理在飞实现）**：MethodRefResolver.cs:275
`ResultKind: returnType is IntPtr/UIntPtr ? "ptr" : null`（9:46:35）→ CodeGenerator.cs:1819
`ExprKind CallExpr "ptr"` → :717-719 `kind=="ptr" → intptr_t {d};` 临时声明 → :2561
PinvokeReturnTypeCpp IntPtr→intptr_t（9:51:49）。**修复链全链路已在源**——dll 8:37:24
早于这两处编辑，批 B 崩溃证据即旧 dll 产物；批 A 代理重建 dll 后 pinvokeext 应直接 PASS
（验证项：t1 声明变 intptr_t）。

**残余根因锁定（2026-08-06 10:1x，批 B 代理）——上述全链仍不充分，实测 dll 9:59:12
转译 pinvokeext 仍是 `int32_t t1`**：
- 生成码实证（Cases_PinvokeExt.cpp）：`t1` 被 utf8_byte_len(int32) 与 reverse_static(intptr_t)
  **共享同一载槽** —— TempSlotPass 槽复用把两类结果并成一个 int32_t 声明（:34 声明 /
  :54 `t1 = reverse_static(t0)` 截断）。
- 精确根因：**TempSlotPass.cs:241** KindOf 的 CallExpr 列表 `"l" or "d" or "dt" or "ts" or
  "guid"` **缺 `"ptr"`**（CodeGenerator.cs:1819 有）→ reverse_static 的 "ptr" 调用在 pass 内
  落入 `_ => "val"`（:247）→ 与 utf8_byte_len 的 "val" 同 kind → :152 mixed-RHS 检查不触发
  → 槽复用合并 → 单 int32_t 声明。而 "ptr" 不在 EligibleKinds(:68) → 若 KindOf 识别 "ptr"，
  t1 自动丧失复用资格（如 "guid"/"dec"），reverse_static 结果落独立新槽 → 声明 intptr_t。
- **修复（一行）**：TempSlotPass.cs:241 加 `or "ptr"`（`c.ResultKind is "l" or "d" or "dt" or
  "ts" or "guid" or "ptr"`）。回归面：无（仅新增一种不可复用 kind，与 guid/dec 同待遇）。
- 验收：转译 pinvokeext → gen t1 独立槽声明 intptr_t → 构建 → 运行输出 9 行逐字节同 net48。
- 占用：T 域在飞（StackFold 9:59:25 / CodeGenerator 9:58:53）→ 施工单，勿并行编辑。

**✅ 修复落地（2026-08-06 10:2x，批 B）**：TempSlotPass.cs:241 加 `or "ptr"`（隔离 patched
dll 预验证 → 真实 dll 10:25:32 重转译）→ gen `intptr_t t5;` 独立槽（t1 保留 int32 给
utf8_byte_len/set_error_if_negative）→ 构建 OK → 运行 diff **9 行逐字节 PASS**。转译器
0 错 0 警；TempSlotPassTests 15/15 PASS（零回归）。#29 闭环。

## 6. convcase 诊断修复记录（2026-08-05 convcase 代理）

**#7 根因（两处，R 域）**：
1. L60/L61：MethodMap.Console.cs 的 WriteLine 分派把 0x09 (ELEMENT_TYPE_U4) 与 0x08 (I4) 合并映射到 icall_console_write_line_i（有符号打印）——0xFFFFFFFF 打印 -1。runtime 有 full-range u64 打印（console_format_u64）但无 u32 入口。
2. L79：matrix3 时 conv.ovf.i4 无范围检查——**已自愈**（conv.u8 零扩展修复 + overflow factory 里程碑后重跑无此差异），conv.cpp:155-162 icall_conv_ovf_i4_i8 本就带范围检查。

**修复（R+Console 域，3 文件）**：
- console.cpp + il2cpp_console.h：新增 icall_console_write_line_u32(uint32_t)（复用 console_format_u64 数字循环，u32 精确提升）
- MethodMap.Console.cs:17：0x09 独立臂 → icall_console_write_line_u32（镜像 0x0B ulong 先例）
- 验证：dotnet build src/il2cppc 0 err；单元 MethodMapConsoleTests 4/4 PASS（新增 Resolve_Console_WriteLineUInt32_MapsToUInt32Icall）；静态归档定向更新（-f build.make 绕过缺失 Makefile2）；convcase 重跑 **79/79 ALL MATCH**（L60=4294967295/L61=2147483648）

**遗留（范围外，已文档化）**：Console.Write(uint) 行 68 同型缺陷（0x08/0x09 合并 → 有符号），无用例覆盖，待 Write 域后续。


## 9. enum box 里程碑 — 转译器侧 2 点（2026-08-05 10:2x，enum-box 域）

**施工单 2（本域，已修复）**：`MethodMap.Console.cs:42` WriteLine(object) box-type
switch 加**枚举臂**：boxed enum → `il2cpp_enum_to_string`（enumext.h:148）+ write_line_s。
- 形态：`I("il2cpp::runtime::icall_console_write_line_s", 1, ..., new[] { "enum:l" }, ..., isConsole: true)`；
  cast 表（CodeGenerator.cs EmitIcallCallArgs，1 行）加 `"enum:l"` →
  `(il2cpp::Il2CppObject*)il2cpp_enum_to_string(nullptr, icall_unbox_long(box))`——
  null 描述符 = 已文档化 Int32 no-metadata 路径（数字十进制），命名路径待
  register 发射 + 名字类描述符落地（同 MethodMap.Enum.cs ToString 前向 ABI）。
- **守卫**：boxed 原语无 int64-carrier unbox cast 者（Int64/UInt64/UInt32/Byte/
  SByte/Int16/UInt16）保持 LOUD（when-guard 显式 throw——unbox_long klass 校验，
  错路 = 运行时 InvalidCast 而非编译期错误；boxed Int64 还会经 Int32 数字回退截断）。
  既有 `Resolve_Console_WriteLineUInt64Boxed_ThrowsLoud` 测试继续通过。
- 验证：dotnet build 0 err；MethodMapConsoleTests **11/11 PASS**（+BoxedEnum 映射 +
  6 例 boxed-prim LOUD Theory）；全量 814：8 fail 全为他域在途（BitConverter/
  String Trim/ConvU8/ArraySort/MemberInfo/Generics，零 Console/Enum 域）。

**timecase 重跑（il2cppc.dll 重建后直跑）**：折叠期 LOUD 错误
`Console.WriteLine(object) with box type System.DayOfWeek not wired in M5` **已消除**，
转译前进到下一阻点 = 施工单 1（他域，enum 代理在途）：
`box of System.DayOfWeek not supported -- no runtime box helper` @CodeGenerator.cs:2391
（box_ switch `_ => throw` 无枚举臂）。L8 全通依赖该臂 + 施工单 3（register 发射，
CodeGenerator/TypeEmitter/MetadataLoader，他域）+"Monday" 命名路径还依赖施工单 4
（boxed 枚举名字类描述符）。

**施工单 3 状态（register 接线 a-e，他域发射）**：MethodMap.Enum.cs REGISTER WIRING
REPORT 已更新为实施状态；发射侧（a MetadataLoader literal 读取 / b 生成静态
Il2CppEnumInfo / c ensure_cctor + il2cpp_runtime_init 双钩 / d 容量 32 LOUD）在
CodeGenerator.cs + TypeEmitter.cs + MetadataLoader.cs，未落地（enum 代理在途）。

## 9.5 enumdeep e2e 用例核查（2026-08-05，enumdeep 域）

**状态：BLOCKED at transpile —— switch (IL 0x45) M1 不支持。**
`tests/cases/enumdeep/` 已存在（8:42-8:47 建，cs/csproj/expected.txt/out 齐全；
net48 oracle 43/43 与 expected.txt 一致，EXIT=0）。全链重跑
（run-tests.ps1 -Case enumdeep mingw）+ 新 dll 直跑均 FAIL：
`IL opcode 0x0045 at offset 169 not supported in M1`（IlDecoder.cs:265
default throw）。探针 dump Main IL 实证：offset 169 = switch（4 targets）。
worklist 此前无 switch/0x45 缺口记录 → **新增缺口**：IlDecoder/StackFold/
CodeGenerator 无 switch 支持（M1 目标外），enum 上 switch 用例 = 验收面。
- 用例覆盖：值比较/转换/算术位运算/枚举 switch×3/[Flags] 组合（全 bool 输出）
  + ToString×3 / GetUnderlyingType×2（后两组 = enumext 施工单：Object::ToString
  路由 TryMapObject 无枚举接收者；GetUnderlyingType 需 ldtoken/typeof 发射，
  MethodMap.Enum.cs REGISTER WIRING e.1，MethodMap.Enum.cs:145-148）。
- 待 switch 落地后：重跑 `pwsh scripts/run-tests.ps1 -Compiler mingw -Case enumdeep`
  核对 43/43。

## 8. threadpoolcase 诊断修复记录（2026-08-05 ThreadPool 域，新用例）

**状态：threadpoolcase 全 PASS（mingw 全链路，输出与 net48 oracle 逐字节一致）**。

**本域修复（3 处）**：
1. 	hreadpool.cpp:255-271：il2cpp_threadpool_queue_work_item_delegate 返回类型
   void → **bool**（threadpool.h 8:16 契约升级为 bool，cpp 未跟上 → 编译冲突；
   net48 语义 + 发射器 ReturnsValue=true 均要求 bool，eturn true）。
2. MethodMap.ThreadPool.cs:32-37：QueueUserWorkItem 两重载 v=false → **rv=true**
   （net48 bool 返回；原映射 4:44 写反，单元测试 5:01 断言正确 → 2 FAIL）+ 显式
   
ew[] { "", "" } casts（null casts 使 ParamTypeNames=null → 测试 NRE）。
3. CodeGenerator.cs:71 后：generated.h **补 #include "threadpool.h"**（代理接线时遗漏
   → 生成 C++ 见不到 icall 声明，cmake 构建 3 error）。

**跨域小改（StackFold.cs，委托域）**：IsDelegateType BCL 名单加
System.Threading.WaitCallback（与 ThreadStart 同形先例）——否则 newobj WaitCallback
走 object_new + 生成 ctor 路径 → System_Threading_WaitCallback_class 未声明。

**探针新发现（StackFold 缺口，施工单）**："..." + staticInt 编译为
**ldsflda + Int32::ToString()**（ECMA 0x7F），StackFold switch **无 Ldsflda case**
（有 Ldflda 无 Ldsflda）→ default LOUD old: opcode Ldsflda not supported in M3。
threadpoolcase.cs 已用"先读入局部再拼接"绕开；修复建议 = 加 Ldsflda case →
StaticFieldAddrExpr（或 ldsflda+ToString 模式折叠），属 fold/field 域。

**验证证据**：dotnet build 0 err；单元 ThreadPool 5/5 PASS（rv/casts 修复后）；
test_threadpool.exe ALL PASS（bool 修复后，含 delegate 双 arity/异常路径）；
threadpoolcase e2e PASS 1/1（queued 2-arg/1-arg: True, all ran: True, done count: 8）。

## 9. swcase/swcase2 诊断修复记录（2026-08-05 Stopwatch 域 M6 对象闭包）

**状态：swcase + swcase2 全 PASS（mingw 全链路，输出与 net48 oracle 逐字节一致）**。
卡 #35（Stopwatch 实例状态）闭环：runtime 定义 System_Diagnostics_Stopwatch_class
描述符（Il2CppStopwatchObject = 头 + startTimestamp + accumulatedTicks + isRunning，
无引用字段）+ 11 个对象 icall（start_new/ctor_default/start/stop/restart/reset/
is_running/elapsed_ticks/elapsed_ms/elapsed_us/elapsed→TimeSpan）；MethodMap.Stopwatch.cs
全映射（StartNew 静态→icall_stopwatch_start_new + returnTypeName 跟踪）；Stop 后 Start
不重置累计（net48 语义，swcase2 32 探针覆盖）。

**本域修复（1 处，e2e 暴露）**：BCL 对象描述符命名与发射器约定不符 ——
CodeGenerator.cs:1311 newobj fallback 发 `il2cpp_object_new(&{TypeCppName}_class)`
（点→下划线全名），runtime 描述符却命名 **System_Stopwatch_class**（stopwatch.h:45-47
注释错误假设 fallback 发短名）→ swcase2 生成代码
`undeclared identifier 'System_Diagnostics_Stopwatch_class'`（swcase 走 StartNew icall
不引用描述符所以 PASS，工厂路径测不到 newobj fallback）。修复 = 描述符改名
**System_Diagnostics_Stopwatch_class**（stopwatch.h/cpp/test_stopwatch.cpp/MethodMap
注释 4 文件，备份先行；Version/Uri/Thread/StringReader 全名先例验证方向）。

**施工单（新，描述符命名铁律）**：BCL 对象域 runtime 描述符必须 = {TypeCppName}_class
全名变形（System.Diagnostics.Stopwatch → System_Diagnostics_Stopwatch_class）；
域接线验收清单含一步：grep CodeGenerator 的 `_class` 发射路径对账 runtime 描述符名。
对象域 e2e 用例必须含 `new T()` 直构路径（仅测 StartNew 工厂测不到 fallback）。

**验证证据**：dotnet build 0 err 0 warn；单元 Stopwatch 14/14 PASS；test_stopwatch.exe
ALL PASS EXIT=0（15 组：对象模型/状态往返/累计恢复/精度 ±10%）；swcase e2e PASS 1/1
（22 探针）+ swcase2 e2e PASS 1/1（32 探针，含 Stop 冻结/Start 恢复累计/Reset 归零/
Restart 重置/Frequency/GetTimestamp 单调）。

## 9. delegateinst 新用例记录（2026-08-05 委托实例绑定）

**状态：delegateinst 用例已建（tests/cases/delegateinst/，cs+csproj+expected.txt，net48 oracle 24 行全 bool）。转译 EXIT=0；MinGW cmake 构建 0 err；运行 EXIT=-1073741819 (0xC0000005) AV——首次实例 invoke 即崩溃，0 行输出。diff FAIL（预期）。**

**根因（转译器缺口，未接线）：StackFold.cs ~:1704 delegate newobj 代理只取 FuncPtrExpr，静默丢弃 target 参数（:1702 注释 "(null target, method pointer)"）；CodeGenerator.cs:1333 只发 1-arg il2cpp_delegate_new(&<method>)。实例绑定委托到达 runtime 时 target==null → icall_multicast_invoke_i4 静态路径调用实例 ABI {ret} M(Il2CppObject* this, ...) → this=实参（如 5）→ null_check 通过后解引用 0x5+8 → AV。**

**runtime 侧已完整支持（探针 20/20）：il2cpp_delegate_new(methodPtr, target) 双参（delegate.cpp:53）+ delegateext.cpp:259-262 逐叶 (target, args) 分发 + icall_delegate_get_target（MethodMap.Delegate.cs:26 已接）+ 叶相等 = methodPtr+target（delegateext.cpp:75）。**

**修复施工单（fold 域）：StackFold delegate newobj case 提取 args 中的 target 引用参数（非 FuncPtrExpr 者）→ DelegateNewExpr 加 Target 字段 → CodeGenerator 发 2-arg il2cpp_delegate_new(&<method>, target)（M3.5 文档形状；test_delegateext.cpp:183 MakeInstDel 先例）。修复后 delegateinst e2e 应 24/24 与 oracle 一致。**

**用例覆盖：invoke 读实例字段（c.Add(5)==15 / d.Add(5)==25）/ GetTarget（ReferenceEquals(f.Target,c) 与静态 null）/ Equality（同 target+method 等、异实例不等、实例 vs 静态不等）/ 混合多播实例+静态（ti-first/ts-second 顺序 + last-result）/ -= 移除实例叶 / 纯实例链移除后剩余叶仍读自身字段。**

## 10. bitconvcase/hexcase 诊断修复记录（2026-08-05 BitConverter 域，施工单 #3）

**状态：`BitConverter.ToString(byte[])` 已接线（G2 施工单 #3 闭环）**；hexcase e2e **PASS 1/1**
（G2 段 = ToHex 直走 icall）；bitconvcase 从"transpile 卡 ToString"推进到"transpile 卡
新阻点 byte[] newarr"（见下新施工单）。

**本域修复（3 文件 + 2 测试文件，独占域）**：
- runtime：bitconverter.h + bitconverter.cpp 新增 `il2cpp_bitconverter_to_hex_string
  (Il2CppArray* bytes) -> Il2CppString*` —— net48 规范形：大写双十六进制对 + '-' 分隔
  （"00-FF-80-12-AB"）；空数组 → ""（net48 ToString(byte[]) 无 startIndex，空合法——
  csc net48 探针 2026-08-05 验证）；null → ArgumentNullException（paramName "value"）。
  实现 = 单趟 scratch（3n-1 单元）+ 单次 il2cpp_string_new（concat.cpp 契约）。
- transpiler：MethodMap.BitConverter.cs ToString case 接线
  `I("il2cpp::runtime::il2cpp_bitconverter_to_hex_string", 1, true, true, false,
  new[] { "a" })`（"a" cast = byte[] 槽，ToBase64String 先例）；(byte[], int)/(byte[],
  int, int) 重载无 runtime helper 保持 LOUD（arity 消息）。
- 测试：MethodMapTests.BitConverter.cs `ToStringByteArray_MapsToHexFormatterIcall`（原
  ThrowsLoud 测试改写）+ `ToStringWithStartIndex_ThrowsLoud`；test_bitconverter.cpp §7
  八断言（"78-56-34-12"/"00-00-80-3F"/"EF-BE-AD-DE"/{01}/{00}/探针序列 00-FF-80-12-AB/
  空→空串/null→ArgumentNullException）。

**验证证据**：dotnet build 0 err；单元 BitConverter 域 27/27 PASS（含改写两测试）；
test_bitconverter.exe ALL PASS EXIT=0；hexcase e2e PASS 1/1（生成码
`Cases_HexCase.cpp:51` 直调 `il2cpp_bitconverter_to_hex_string((Il2CppArray*)(arg0))`，
30/30 行与 net48 oracle 逐字节一致）。hexcase.cs ToHex 从手卷 X2 循环改为
`BitConverter.ToString(a)`（原注释声明的缺口已闭合）。

**新施工单（#42，array/CodeGenerator 域）**：bitconvcase transpile 卡
`newarr element type System.Byte not supported (audit C4)` @CodeGenerator.cs:2691
（ArrayElementType 仅支持 Int32/String/Object/用户类；System.Byte 未列）。
触发 = bitconvcase.cs L28-30 `new byte[] { 0 }/{1}/{42}`（ToBoolean 探针）——G2 快照时
ToString 缺口先抛，掩盖了此缺口。修复 = ArrayElementType 加 System.Byte → (1, false) +
byte[] stelem/ldelem 路径验证（hexcase 刻意只用 icall 返回的 byte[] 绕开此缺口，勿动）。
修复后 bitconvcase 应全 17 行 PASS。

**他域观察（不修，记录）**：全量单元 809/816 有 7 失败——String TrimStart/TrimEnd
char[]（MethodMapTests.String）、StackFold ConvU8 3 例、MemberInfo GetName 1 例、
GenericsSignature 1 例——均在并行代理域（StackFold.cs 9:21 / MethodMap.String.cs 5:13
在途改动），与 BitConverter 域 27/27 无交集（我未改 MethodRefResolver.cs/StackFold.cs）。

## 11. convertcase 诊断修复记录（2026-08-05 convertcase 代理，重派第 3 次）

**#8 根因（两处，均非"ToChar 范围检查"——该 icall 本就正确，case L29/L30 一直过）**：
1. **L11/L12（R 域，convert.cpp:163-165）**：`icall_convert_to_int32_d` 用 C 强转截断，
   注释错误宣称"truncate = .NET 语义"。net48 实证（本次 10 行新探针）=
   **先 half-to-even 四舍五入（Math.Round 语义），再对四舍五入结果按 [INT32_MIN, 2^31)
   检查**：2.9→3、-2.9→-3、2.5→2、-2.5→-2、3.5→4、2147483646.5→2147483646、
   2147483647.0→合法 2147483647、-2147483648.0→合法、2147483647.5→OverflowException
   （舍入到 2^31）、-2147483648.5→合法 -2147483648。先前参考源码记忆
   "value >= 2147483647.0 throws" 被实证否定——边界语义是"舍入后检查"。
2. **L26（T 域，已在工作树自愈）**：csc 对 2147483648L 发 `ldc.i4 0x80000000; conv.u8`
   （真实字节 **0x6E**，不是 0x6B！）——StackFold 9:21 新增的 Conv_u8 零扩展 case
   （`(long)(uint)`，在途未提交）修复了 sign-extend 翻转；重跑后 gen =
   `icall_convert_to_int32_i8((int64_t)(2147483648LL))`，runtime 本就带范围检查 → 抛
   OverflowException。与 convcase #7 L79 的"已自愈"同源。

**修复（本域 R，2 文件 + 测试修正 1 文件）**：
- convert.cpp + il2cpp_convert.h：`icall_convert_to_int32_d` = NaN/Inf 先抛 →
  `std::nearbyint`（默认 FE_TONEAREST = half-to-even；全仓无 fesetround）→ 对舍入
  结果 `>= 2^31 || < -2^31` 抛 OverflowException → 强转。文档注释同步修正
  （头文件第 24/49-50 行旧"截断"契约）。
- test_convert.cpp（7:10 在途 pins 已实证正确，未改）；**StackFoldTests.ConvU8.cs
  3 例字节错**：`0x6B`（= conv.r4！）应为 `0x6E`（= conv.u8）——代理写测试时用错
  字节 → 3/3 恒 FAIL（译码出 Conv_r4，与修复无关）；已修 → 3/3 PASS。
- convertcase.cs 追加 10 行边界/中点探针（2.5/-2.5/3.5/-3.5/2147483646.5/
  2147483647.0/-2147483648.0/2147483647.5/-2147483648.5），net48 oracle 逐行实证。

**验证证据**：net48 oracle 运行 EXIT=0 捕获 41 行（30 原 + 11 新）；runtime 单元
test_convert.exe：修复前 RED（3 FAIL + -2147483648.5 边界崩溃），修复后
`all runtime convert tests passed` EXIT=0；dotnet build 0 err 0 警；单元 ConvU8
3/3 PASS；全量 812/816（4 失败全他域在途：String Trim×2/MemberInfo/Generics，与
convert 域无交集）；**convertcase e2e PASS 1/1（41/41 与 net48 逐字节一致，mingw）**。
产出（三 P0 次选之一）：47 +1 = **48/88** 起步基线。

## 12. sortdeep 命名空间修复记录（2026-08-05 11:1x sortdeep 代理，R2 施工单）

**R2 施工单：`il2cpp_array_copy_to` 7 处编译失败** —— arraysort.h 单层
`namespace il2cpp` vs 旧发射器 `il2cpp::runtime::`（2018-08-05 9:59 gen 实证）。
Array 域代理已修 MethodMap.ArraySort.cs 11 处发射为裸 `il2cpp::`；本代理验证
统一方向 + 全链闭环：

**统一方向 = 裸 `il2cpp::` + 单层头（仓库 direct-call 域多数约定）**：
- guid.h:43/80、arrayextra.h:73/129、arraylist.h:54/107、dictsi.h:78/137、
  arraysort.h:87/155 全是单层 `namespace il2cpp`；MethodMap.Guid.cs 9 处裸
  `il2cpp::`（40-94 行）、test_arraysort.cpp 67 处裸 `il2cpp::` —— arraysort
  属同一 direct-call 域，发射裸 `il2cpp::` 与头一致，**arraysort.h 无需改**。
- icall 域（il2cpp_array_ext.h/ext2.h 等）是 `il2cpp::runtime`，MethodMap.Array.cs
  发射 `il2cpp::runtime::` 是正确归属，未动。

**arraysort.cpp 未提交改动（10:52:15）= kTypeMismatchChain 4 项形状**：
`System.ArrayTypeMismatchException,System.ArrayTypeMismatchException,
System.SystemException,System.Exception`（catch-matcher 链行走在首个逗号后，
类型须在位置 1 自重复，exceptionext.cpp 工厂约定；非命名空间问题，保留）。

**验证证据**：
- dotnet build 0 err 0 警；发射器 grep 无 `il2cpp::runtime::il2cpp_array_` 残留。
- 单测 ArraySort 17/17 PASS（tests/unit，含 MethodMapTests.ArraySort 全量）。
- 重转译 sortdeep gen（11:29:07）= 7 处裸 `il2cpp::il2cpp_array_copy_to`、
  0 处 `il2cpp::runtime::`（9:59 旧 gen 为 7 处 runtime::，stale 废目录
  E:\il2cpp\gen_sortdeep 已确认不参与 e2e）。
- e2e：run-tests.ps1 -Case sortdeep mingw → **PASS 1/1**（构建 11:29:17-28，
  运行 True 输出与 expected.txt 一致）。
- test_arraysort 回归：重建（含 10:52 arraysort.cpp 链修复）→
  `all runtime arraysort tests passed` EXIT=0。
- 全仓案例 gen 扫描：无任何残留 `il2cpp::runtime::` arraysort 调用；
  sortdeep 是唯一使用 arraysort 发射的案例。

**状态**：sortdeep e2e 已闭环，施工单 2 完成。遗留：根目录 gen_sortdeep
（9:59 stale 产物）可删（非 e2e 路径）。

## 14. webcase 新用例 + 施工单 #43（2026-08-06，net T3 缺口探针）

**用例已建**：`tests/cases/webcase/`（webcase.cs + webcase.csproj + expected.txt），
WebClient.DownloadString 形状探针，**无服务器设计**——4 个非法 URL 全部在
net48 上确定性失败且不触网（文件路径回退）：
- net48 实测（webprobe48.exe）：`http://` / `http://[::1` / `://host` →
  **NotSupportedException**（"不支持给定路径的格式"，文件回退）；`not a url` →
  **WebException**（文件未找到）。
- oracle（expected.txt）= `other`×3 + `web_exception`，EXIT=0。
- catch 链保留 UriFormatException/WebException/fallback 三形状（未来 live-server e2e 契约）。

**当前状态（2026-08-06 实测，il2cppc.dll 7:45:55）**：转译 **EXIT=0（静默降级，不 LOUD）**，
生成码含 3 个未接线符号 → cmake 构建失败（3 error）：
- `Cases_WebCase.cpp:33` `use of undeclared identifier 'System_Net_WebClient_class'`
- `Cases_WebCase.cpp:34` `System_Net_WebClient__ctor_97f1b4b9898109bb`
- `Cases_WebCase.cpp:35` `System_Net_WebClient_DownloadString_7588e4fa3a7334e0`

**施工单 #43（net T3 接线，T+R 两域）**：
- R：http.cpp 域加 **System_Net_WebClient_class 描述符**（空对象，无引用字段；
  描述符命名铁律 = `{TypeCppName}_class` 全名）+ 对象 icall
  `icall_webclient_download_string(Il2CppObject*, Il2CppString*)->Il2CppString*`
  （内部 = string→cstr → il2cpp_http_get → body→Il2CppString + il2cpp_http_free_body；
  net48 语义错误映射：非 http scheme/malformed → NotSupportedException 或 WebException，
  以 oracle 4 行为验收：other×3 + web_exception×1）。
- T：MethodMap.NetWebClient.cs（WebClient ctor 直构 + DownloadString(string) 接线，
  仿 MethodMap.Stopwatch.cs 对象模式 + newobj 描述符路径）。
- 验收：`pwsh scripts/run-tests.ps1 -Case webcase mingw` → PASS（4/4 行）。



## 13. exdeep f18 EH 修复记录（2026-08-05 12:0x-12:5x，R3 施工单 #15）

**根因（G2 实证）**：exdeep.cs f18 = 同一 try 区 3 个兄弟 catch 子句（ArgumentException /
InvalidOperationException / Exception），生成码为 3 个连续 catch (il2cpp::DotNetException&)
+ 守卫重抛（`if (!matches(...)) throw;`）。C++ 语义：catch 内 	hrow; 重抛后，搜索在
**当前 try 块 handler 列表之后**继续——同 try 剩余子句永不参与匹配（GCC 编译期即警告
-Wexceptions: will be caught by earlier handler）。f18 抛 InvalidOperationException →
子句 1 捕获（DotNetException& 全匹配）→ 守卫失败重抛 → 子句 2/3 被跳过 → 未处理 exit=1。
- 独立复现（本任务实证）：%TEMP%\opencode\rethrowprobe → OUTER unhandled: System.InvalidOperationException。
- exceptionchain2 c8 "兄弟 catch 语义正常"是**假象**：c8 首子句为空（catch (ArgumentException) { }），
  无 IR 语句落入其 handler → 发射器丢弃该子句 → 只剩单 catch 直配。发射器曾把空子句静默丢弃
  （潜在吞异常/漏吞 bug——空 catch 应吞掉匹配异常，丢弃后异常会漏出）。

**修复（CodeGenerator.cs EH dance）**：同 try 范围（TryOffset/TryLength 相同）≥2 catch 子句 →
发**单 catch** + if/else-if 分派链：
`catch (DotNetException& e) { if (matches(c1)) { ... } else if (matches(c2)) { ... }
else { throw; // no clause matched } }`
- OpenChainCatch/ContinueChainCatch/CloseChainCatch 三个本地函数 + openChains 跟踪；
- 空子句保留在链中（顺带修复丢弃缺口）；catch-all 尾子句 = 最终 lse（无 throw 尾）；
- 单子句 catch / finally-as-catch / 嵌套 try 路径不变（OpenCatch 原样）。

**验证**：
- EHRegionEmitTests 3 新测试（2 子句链 / 3 子句含 catch-all / 空首子句）+ 重写旧 sibling 测试 → 7/7 PASS；
- 全量单元 840/844（4 失败 = 既有他域 Trim×2/MemberInfo/Generics，零回归）；
- exdeep e2e：**37/37 ALL MATCH + exit=0**（基线 28/37 exit=1；f18 + f19-f23 级联全过）；
- exceptionchain2 17/17、exceptions PASS、cctor PASS（EH 域零回归）。
- 顺带清障：aes.cpp（并行 AES 代理未跟踪文件）核心函数残留调试 printf（缺 <cstdio> 致全
  runtime 构建失败 + 污染 stdout）→ 已删 3 行调试输出。

## 15. 批 A 状态表 + 施工单 #44/#45（2026-08-06 9:0x，批 A 接线代理）

> 占用判定：MethodRefResolver.cs 9:18 / CodeGenerator.cs 8:52 / StackFold.cs 8:55 均被他方
> 代理在飞修改（runtime_fullcheck6 + gc.cpp.pre-diag 8:50 同活跃期）→ 本批**零源码改动**，
> 全部转为精确施工单。隔离构建验证：`dotnet build src/il2cppc -o %TEMP%\opencode\il2cppc_batchA`
> 0 错 0 警（源树可编译）；该隔离 dll 重转译 5 例结果与本表一致（9:18 他域改动未触及本批根因）。

| # | 用例 | matrix5 | 当前(2026-08-06) | 精确根因（G2 定位） | 域 | 施工单 |
|---|---|---|---|---|---|---|
| 4 | cctorchain | diff | transpile OK / build OK / **diff FAIL** | .cctor 抛异常后二次访问不重抛（boom_caught 1 vs 2，15/15 行仅此一处） | R | #4 已有 + cctorchain-fix-wo4.md 方案文档（P1）——runtime 域被占用，未动 |
| 31 | recurse | cmake_build | transpile OK / **cmake FAIL** | entry.cpp:15 `Cases_Recurse_Main_9866ee88af8eb9db()` 无参 vs 声明 `(Il2CppObject* arg0)`（Main(string[])）；生成器 = CodeGenerator.cs:3371-3380（il2cpp_entry_main 内 Main 调用不传实参） | T | #31 已有；补精确定位 CodeGenerator.cs:3371-3380——Main 参数数>0 时调用传 `nullptr` 实参 |
| 10 | delegate2 | transpile | **transpile FAIL** | `callvirt on non-instance method (token 167772179=0xA000013)` @StackFold.cs:1440；MemberRef RID 19 = **get_Method**（delegate2.cs L34 `f.Method.Name`，实例 getter）；MethodRefResolver.cs:337 heuristic `!StartsWith("get_")` 把实例 getter 误判 STATIC | T | **#44**（见下）——与 enumdeep 同根因 |
| - | culturecase | cmake_build | transpile OK / **cmake FAIL** | `Cases_CultureCase.cpp:33` `::lit::.(nullptr)` —— MethodMap.NumberFormat.cs Literal() 条目（IsInstance=true）经 **callvirt** 路径发射（NumberFormatInfo.CurrentInfo.NumberDecimalSeparator == "."，L10-13）；StackFold call 路径 1620-1645 有 marker 折叠但 **callvirt 路径（1395-1544）缺失** → `::lit::.` 当函数名发射 | T | **#45**（见下） |
| - | enumdeep | transpile | **transpile FAIL** | `call at offset 0x01CC: System_Type_op_Equality_970a3bb1b564144f as INSTANCE (pops this)` @StackFold.cs:1569；MemberRef RID 14/17 = **op_Equality**（gen=0x00 HAS_THIS=False 静态）；heuristic 默认 INSTANCE 误判 | T | **#44**（见下）；修后预期下一阻点 = switch IL 0x45（worklist §9.5 已记录） |

**施工单 #44（delegate2 + enumdeep 同一根因，T 域，P0）**：
- 根因：MethodRefResolver.cs:337 的 name heuristic
  `mrName != ".cctor" && !mrName.StartsWith("get_") && mrName != ".ctor" || mrName == ".ctor"`
  与 CLI 语义不符：实例 getter（get_Method）判 STATIC；静态方法（op_Equality）判 INSTANCE。
- **实证**（MemberRef 签名头）：delegate2 RID 19 get_Method gen=0x20（HAS_THIS=True 实例）；
  delegate2 RID 24 / enumdeep RID 14,17 op_Equality gen=0x00（HAS_THIS=False 静态）。
- 修复：MethodSignatureCoder.Decode 输出真实 isInstance（signature header bit 0x20 HAS_THIS，
  当前未读取——grep 全仓无 0x20/HasThis 处理）；ResolveMemberReference:337 用其替代 heuristic
  （isStaticOverride 机制保留——MethodSpec 底层签名同样有 HAS_THIS，语义一致）。
- 回归面：BCL 表 BclResolve（:313）先于 heuristic，已接线属性不受影响；accessors 折叠
  （StackFold 属性表）先于 IsInstance 检查；需重跑全量单元 + pinvoke/pinvokeext（Marshal
  静态守卫 0x11/0x17/0x18 独立路径）。
- 验收：delegate2 转译推进（下一阻点预估 = get_Method→MethodInfo.Name 链，M6 reflect 域）；
  enumdeep 转译推进（下一阻点 = switch 0x45，§9.5）。

**施工单 #45（culturecase，T 域，P1）**：
- 根因：StackFold.cs callvirt 路径（EmitCall virtualDispatch=true 分支 1395-1544）缺 fold-time
  marker 折叠；call 路径 1620-1645 有（RefEq/Null/Literal/Identity 四 marker）。
- 修复：callvirt 路径 :1439（`IsInstance:false` throw）之前插入与 call 路径同逻辑的 4 个 marker
  折叠（建议提取公共局部函数避免重复）。
- 验收：culturecase 转译 → gen 码含 `il2cpp::g_strPool[n]` 字面量（"F2"/"."/","）→ cmake 构建
  → 运行 diff（net48 oracle expected.txt，6 行文化探针）。

**批 A 结论**：5/5 例根因锁定；3 例（recurse/delegate2/enumdeep/culturecase 中 4 例 T 域）待占用
解除后按 #31/#44/#45 修复（#44 一处修复 +2 PASS）；cctorchain 待 runtime 域（#4 方案文档就绪）。
webcase 新用例 + 施工单 #43 见 §14。

## 16. 批 B 状态表（2026-08-06 09:5x，批 B 接线代理）

> 目标：arrcomp 新用例 + timecase 剩余/mathcase 重验/hashcase new string ctor/pinvokeext M4.5
> （先查 mtime，可修则修，被占则施工单）。占用判定：src/il2cppc（StackFold 9:30/
> MethodRefResolver 9:18/MethodMap.Crypto 9:29）+ src/runtime（stringbuilder.cpp 9:39）在飞
> → 本批**零源码改动**，可验者验、被占者精确施工单。

| 项 | matrix5 | 当前(2026-08-06) | 结论 | 域 |
|---|---|---|---|---|
| mathcase | PASS | **重验 PASS**（28 行逐字节同 net48 live，il2cppc.dll 8:37:24 转译 + cmake + diff） | 闭环 | - |
| timecase | PASS | **重验 PASS**（9 行逐字节，dll 9:59:12 + runtime 恢复绿后全链）——get_TotalMinutes 已接线（MethodMap.TimeSpan.cs:27），#37 施工单过期 → 行更新为 PASS | 闭环 | - |
| pinvokeext | cmake_build | **PASS（2026-08-06 10:2x 修复）**：TempSlotPass.cs:241 加 `or "ptr"` → gen `intptr_t t5` 独立槽 → 构建 OK → 运行 diff 9 行逐字节（5/0/cba/-1/123/0/123/0/0） | 闭环（#29） | T |
| hashcase | PASS | **重验 PASS**（9 行逐字节，dll 9:59:12 全链）；new string('a',100) ctor 缺口仍在（case 内 100 字面量 workaround） | 施工单 #46（见下） | T+R |
| arrcomp（新） | - | **新建 + 全链 PASS**：net48 ref 10×True ✓ / expected.txt ✓ / 转译 ✓ / MinGW 构建 ✓ / diff 逐字节 ✓ | 完成 | - |
| timeext | diff | 文化 diff（#38）未变，R 域占用 | 施工单 #38（已有） | R |

**施工单 #46（hashcase new string ctor —— `System.String::.ctor(char, int)`，T+R，P1）**：
- 根因：MethodMap.String.cs 无 `.ctor` 条目 → `new string('a', 100)` 发射
  `System_String__ctor_*`（默认命名，runtime 无此符号）→ hashcase.cs:16-23 用 100 字面量
  workaround（payload 逐字节相同，9/9 True 已 PASS）。施工单 = 去除 workaround，原生 ctor。
- **关键设计约束（2026-08-06 批 B 复核实证）**：String 是**变长类型**——引用类型 newobj
  通用路径 = `il2cpp_object_new(&System_String_class)`（固定尺寸，StackFold.EmitNewObj
  :1922-1928），无法容纳 count 未知的字符缓冲 → **自变异 ctor（mutate self）不可能**。
  必须走**工厂 newobj 路径**：EmitNewObj 对 `typeFullName == "System.String"` 特判 →
  直接发射 `t = <factory icall>(args)`（如 il2cpp_string_new_from_cs/String 类先例），
  不做分配+ctor 两步。参看 value-type（:1847）与 delegate（:1879）特判先例。
- 参数编码：`new string('a', 100)` → ctor(char c, int count) → ps = **[0x03 (CHAR), 0x08 (I4)]**
  （char=0x03，非 0x02——MethodSignatureCoder 0x03→"c" char16_t cast 先例 IndexOf）。
- 方案（R 域，runtime 空闲可先行）：string.cpp 增 `icall_string_new_char_repeat(char16_t c,
  int32_t count)` → 变长分配 + 逐单元填充（长度校验 count<0 抛/0 合法返回空串）。
  T 域（占用解除后）：① EmitNewObj String 特判（工厂路径）；② MethodRefResolver ctor 分支
  `.ctor(char,int)` → 该 icall。
- 验收：hashcase.cs 改回 `new string('a', 100)`（删 100 字面量 workaround）→ 转译 → cmake
  → 运行 diff 9/9 True 保持。
- 占用：T 域在飞（MethodMap.Encoding 11:19 / TypeEmitter 11:16 / Reflect 10:58）→ 施工单。

**arrcomp 用例记录（2026-08-06）**：tests/cases/arrcomp/（数组 1D 综合深链，全 bool）——
手动反转/元素交换/Array.Copy 偏移/Length+GetLength/IndexOf×2/BinarySearch×2（含 -3 插入点）/
string[] 排序/int[] 排序；Clone 未接线（MethodMap.Array.cs/ArraySort.cs 无条目 + runtime 无
clone icall）→ 文档化跳过。net48 参考 10×True（exit 0），expected.txt 10 行 LF 冻结；
转译 EXIT=0；MinGW 构建 + diff 待 runtime 域收工（stringbuilder.cpp 在飞编译错阻塞所有
case 构建）。