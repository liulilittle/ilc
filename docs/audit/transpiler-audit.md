# 转译器质量审计报告（src/il2cppc/** + src/runtime/**）

- 审计日期：2026-08-03
- 审计性质：只读审计（未修改任何代码、未 git commit）
- 审计范围：转译器（src/il2cppc/Ir/StackFold.cs、Emit/CodeGenerator.cs、Emit/TypeEmitter.cs、
  Emit/MethodRefResolver.cs、Emit/MethodSignatureCoder.cs、Emit/VTablePlanner.cs、Emit/SigHash.cs、
  Emit/MethodEmitter.cs、Il/IlDecoder.cs、Il/MethodBodyReader.cs、MetadataLoader.cs、Program.cs、
  Model/*）与运行时（src/runtime/src/*.cpp + include/internal/*.h）
- 审计标准：docs/REQUIREMENTS.md（铁律）+ 审计清单 9 项
- 结论摘要：Critical 4 项 / High 9 项 / Medium 11 项 / Low 6 项
  （严重度定义：Critical = 合法 .NET 代码下产生静默内存损坏或静默错误结果且无任何失败信号；
  High = 合法代码下产生错误行为/崩溃/UB，但通常伴随 loud 信号或触发条件较窄；Medium = 静默数据
  丢失/诊断不足/架构性隐患；Low = 死代码/文档过期/非关键）

---

## Critical

### C1  GC 根缺失——引用参数（含 this）不是 GC 根 → 仅经参数可达的对象被回收

【位置】CodeGenerator.cs:922-954（WrapFrameRoots 只注册 refLocals）；CodeGenerator.cs:313
（refLocals = 声明为 Il2CppObject* 的局部，不含参数）；gc.cpp:663-692（根集合 = 静态根 +
root ranges + 帧栈 refs，无参数槽）。

【问题描述】方法序言只把**引用类型局部变量**注册为帧根（`__frameRefs = { &loc0, ... }`）。
引用类型参数（arg0 = this、argN）从未进入 refLocals。GC 标记时若某对象仅经参数可达
（未被字段/静态/局部引用），该对象被误判为垃圾回收；后续对参数解引用即为悬垂指针读。
标记清除（非移动）下该内存可被下一块分配复用 → 静默数据损坏。触发条件极宽：任何
`void M(Node n) { 分配新对象/字符串/数组; n.xxx }` 形状的合法代码。

【证据】
- CodeGenerator.cs:922 `private static string WrapFrameRoots(string body, List<string> refLocals, ...)`——只接收局部名。
- CodeGenerator.cs:943 `refLocals.Select(l => $"(void*)&{l}")`——全部来自局部声明，无参数。
- gc.cpp:682-692 帧扫描 `frame->refs[i]`——只覆盖帧内注册槽。
- gc.cpp:663-677 根集合三源（静态根/root range/帧）——均不含参数地址。

【建议修复】帧根必须扩展为「引用局部 + 引用参数（含 this）」。在 EmitBody 的 refLocals 收集处
并入 `bodyInfo.ArgCount` 中签名为引用类型的参数（MethodDef.ParamTypes 已含 `r:` 前缀标记）。
注意：参数同时是 C++ 函数形参，帧 refs 直接指向 `&argN` 可行。修复后需新增 e2e：
`M(Node n)` 内分配大量对象触发 GC 后再读 n 字段。

### C2  移动式 Gen0 压缩与 C++ 栈根不兼容——寄存器/原生栈驻留引用、堆内字段地址不更新

【位置】gc.cpp:742-775（Phase A：Gen0 存活块整体 memcpy 到新段）；gc.cpp:552-646
（Phase C：只重写静态根、root ranges、帧栈 refs 指向的内存槽、存活对象字段）；
CodeGenerator.cs:708-710（FieldAddrExpr → `&obj->field` 取 GC 对象内地址）。

【问题描述】Gen0 压缩把对象物理移动。Phase C 只更新「注册过地址的内存槽」。但三类引用位置
不被更新：
1. C++ 寄存器驻留的引用（编译器把引用局部优化进 callee-saved 寄存器跨分配调用存活）——运行时
   写内存槽，寄存器里仍是旧地址 → use-after-free；
2. 原生栈上未注册的槽（C++ 临时量、中间结果）；
3. `ldflda`/`ldloca` 指向 GC 对象内部的地址（FieldAddrExpr，M3 值类型字段取址路径）——压缩后
   地址指向已拷贝走的旧内存，写入即悬垂。
根因是**移动式 GC 需要精确栈图/根位置，而生成 C++ 无法提供**（这正是 .NET CoreCLR 由 JIT 生成
precise stack map 的原因）。当前 e2e 通过依赖「编译器恰好把根放内存」的运气，非保证。

【证据】
- gc.cpp:765 `std::memcpy(newObj, obj, size)`——对象物理移动。
- gc.cpp:552-646 UpdateReferencesAfterCompaction——枚举的三类更新点不含寄存器/临时槽。
- CodeGenerator.cs:710 `return $"(&{FieldAccess(...)})"`——FieldAddrExpr 发射堆内地址。
- Expr.cs:134-137 注释明言「safe ONLY for the duration of the statement sequence (no GC point
  between), matching the M2 non-moving collector」——但 M3 已是移动式。

【建议修复】二选一：(a) 撤销压缩，恢复纯非移动 mark-sweep（压缩段暂缓，代价=碎片，但根协议才
成立）；(b) 保留压缩但要求所有引用在任何调用/分配前**从内存重载**（生成代码无法强制 C++ 编译器），
不现实。推荐 (a)，压缩留待未来「保守扫描原生栈 + pin」方案或精确栈图（需放弃纯 C++ 生成）。
同时禁止 ldflda 指向 GC 堆内对象跨分配存活（文档化 + 生成期检测）。

### C3  int64 常量/算术静默截断为 int32 —— `long x = 4294967296L` 得 0

【位置】StackFold.cs:92-93（Ldc_i8 → IntConstExpr{Value=long}）；CodeGenerator.cs:640
（IntConstExpr → `{Value}LL` 原样发射）；CodeGenerator.cs:320（非 ref temp → `int32_t`）；
StackFold.cs:279-283（Conv_i4/Conv_i 折叠为 no-op）；CodeGenerator.cs:1151-1167
（TryFoldInt32 把 int64 常量二元运算按 int32 回绕折叠）。

【问题描述】`long` 局部在本地签名里只数槽数（MethodBodyReader.cs:106-116 不读类型），生成 C++
类型统一 int32_t。`ldc.i8` 压入 64 位常量，EmitExpr 原样发射 `4294967296LL`，赋值给 int32_t temp
→ C++ 隐式窄化（GCC -Woverflow 警告但不阻断）→ 值变 0。`conv.i4`/`conv.i` 被当作「int32 恒等」
折叠为 no-op，来源若是 int64 则本应截断的低 32 位语义丢失；`ldc.i8 A; ldc.i8 B; add` 经
TryFoldInt32 直接 Wrap32 回绕 → `long` 算术按 int32 求值。这是**静默错误结果**，且 `long` 在
真实代码中极常见（连 `long x = int.MaxValue + 1L` 这类写法都错）。

【证据】
- StackFold.cs:92-93 `Ldc_i8 => Push(new IntConstExpr { Value = ins.Operand }, ins)`——Operand 为 long。
- CodeGenerator.cs:640 `i.Value.ToString(InvariantCulture) + "LL"`——64 位常量原样输出。
- CodeGenerator.cs:320 `w.Line(kind == "ref" ? ... : $"int32_t {d};")`——int64 常量 temp 声明为 int32_t。
- StackFold.cs:280-283 `Conv_i4/Conv_i` 折叠为 no-op，注释「int32 -> int32 恒等」——对 int64 来源错误。
- CodeGenerator.cs:1153 `BinaryOp.Add => Wrap32(l + r)`——int64 常量加法按 32 位回绕。

【建议修复】引入 Int64ConstExpr / 真正的 64 位 IR 类型。短期内最经济：Ldc_i8 且值超出 int32 范围
时抛 NotSupportedException（loud 拒绝，杜绝静默截断）；Conv_i4 需按来源宽度真正截断（若栈值
可判定为 int64 则发射 `(int32_t)(expr)`）；TryFoldInt32 只在确认 int32 来源时折叠。配套 long
e2e（正/负/超 32 位边界）。

### C4  引用类型数组 `newarr` 恒按 int32 分配 → `stelem_ref` 越界写 → 堆损坏

【位置】CodeGenerator.cs:1291-1295（newarr 恒 `il2cpp_array_new(nullptr, 4, false, len)`，
elemSize=4、isRefElement=false）；CodeGenerator.cs:1312-1319（stelem_ref 写 8 字节
`*(Il2CppObject**)ptr = ...`）；StackFold.cs:337-345（Newarr token 未校验元素类型）。

【问题描述】`string[] s = new string[2]; s[1] = x;`（合法 C#）→ newarr 分配
`ArrayAllocSize(4, 2)` = header + 4 字节载荷；stelem_ref 在 `data + 1*4` 处写 8 字节指针 →
**越界 4 字节写**（每个引用元素超写 4 字节，最后一个元素超写块尾）。越界写破坏相邻块/段填充，
且该 8 字节被 GC 以 int32 数组（isRefElem=false）扫描 → 不会按引用扫描，写入的引用可被回收 →
双重损坏。同时 ldelem_ref 读 4 字节 stride 中的 8 字节指针 → 读到 4 字节垃圾。

【证据】
- CodeGenerator.cs:1295 `il2cpp_array_new(nullptr, 4, false, (int32_t)(...))`——元素尺寸恒 4。
- CodeGenerator.cs:1318 `(*(Il2CppObject**){ptr} = (Il2CppObject*)(...))`——8 字节写。
- array.cpp:99 `il2cpp_gc_alloc_size(arrayClass, ArrayAllocSize(elemSize, length))`——按 4 字节分配。
- gc.cpp:496-503 只有 `isRefElem` 数组按引用扫描——int32 数组不回扫。

【建议修复】newarr 按 token 类型决定 elemSize/isRefElement（System.String/System.Object/引用类型
→ sizeof(void*)/true；int32 → 4/false；其余类型 → NotSupportedException 拒绝）。Element type 从
Newarr token 解码（现已有 TypeTokenFullName 可复用）。配套 `string[]`、`object[]`、`byte[]`
e2e。

---

## High

### H1  Cgt_un/Clt_un 非折叠路径按有符号发射 → uint 比较静默错误

【位置】CodeGenerator.cs:1342-1348（OpSymbol：CgtUn/CltUn → `>`/`<`）；CodeGenerator.cs:691
（BinaryExpr → `(l op r)`）；CodeGenerator.cs:1151-1167（仅折叠路径用 `(uint)` 语义）。

【问题描述】`uint a, b;` 的比较经 `cgt.un` 折叠。**常量**两操作数被 TryFoldInt32 以
`(uint)l > (uint)r` 正确求值；**非常量**路径（操作数是局部/调用结果）发射
`(int32_t 表达式 > int32_t 表达式)` ——有符号比较。任何高位为 1 的 uint 值比较结果与 .NET 相反
（如 `(uint)0xFFFFFFFFu > (uint)1u` 应为 true，生成代码得 false）。uint 参数本身可转译
（MethodSignatureCoder.cs:49 `0x11 => V:System.UInt32`，ParamTypesCpp → int32_t），
故该缺陷对合法 C# 可达且静默。memory.md 记录为「延后到 uint 支持」，但 uint 参数已能通过转译，
比较结果却是错的。

【证据】
- CodeGenerator.cs:1346 `BinaryOp.CgtUn => ">", BinaryOp.CltUn => "<"`——有符号符号。
- CodeGenerator.cs:691 `$"({EmitExpr(b.Left)} {OpSymbol(b.Op)} {EmitExpr(b.Right)})"`——直接发射。
- MethodSignatureCoder.cs:49 `0x11 => "V:" + ResolveTypeName(...)`——uint 参数正常解码。
- 测试用例零 `cgt.un`/`uint`（grep tests/cases/*.cs 无匹配）——无回归保护。

【建议修复】发射层对 CgtUn/CltUn 生成 `((uint32_t)l > (uint32_t)r)`；同时补 uint 比较 e2e。

### H2  `leave` 折叠为 no-op —— try 内 break/continue/goto 出 try 静默错控制流

【位置】StackFold.cs:284-290（Leave/Leave_s → 注释 + no-op）；CodeGenerator.cs:326-468
（try 区域按 IL offset 逐语句发射，无「leave 跳出 try」处理）。

【问题描述】IL `leave` 是「无条件离开 try 块并执行 finally」，目标是任意后续偏移。编译器对
`while(...) { try { ...; break; } finally { } }` 生成 `leave L_loop_end`（循环外）。StackFold 把
leave 折叠为顺序流继续——生成代码不跳出循环、不执行应有的离开语义，直接落回 try 剩余语句
（若 try 在此结束则顺序流进入 try 之后，但循环没有退出）→ **静默无限循环/错误逻辑**。
`return` 在 try 内（leave→finally→ret 形状）恰巧因目标 = finally 之后而正确，但 break/continue/
任意 goto 出 try 均错。当前不拒绝也不报错。

【证据】
- StackFold.cs:288-290 `// leave 的目标 = try 后代码（顺序流已到达）；... leave 折叠为 no-op`——无实现。
- CodeGenerator.cs:330-332 注释明言「leave-to-outside is NOT supported ... the exception cases in
  scope fall through the try block naturally」——已知限制但无检测。

【建议修复】leave 折叠时必须校验：目标偏移是否等于 try 区域之后的顺序指令偏移。不等 → 抛
NotSupportedException（loud），而不是静默继续。后续再实现「leave 出 try = 先跑 finally 再 goto」
的完整语义。配套 e2e：try 内 break 的循环 + try 内 return。

### H3  box 非 Int32 值类型静默装箱为 Int32 盒

【位置】CodeGenerator.cs:1275-1276（box_ 前缀 → 恒 `il2cpp_box_int32(nullptr, v)`）；
StackFold.cs:315-322（Box case 用 token 但发射层忽略 token）。

【问题描述】`object o = 5L;`（box long）、`object o = someStruct;` 等合法代码 → 生成的
`il2cpp_box_int32(nullptr, value)` 把值截断为 int32 并盖上「System.Int32」盒类。后续
`o is long`/unbox 行为全错且无任何信号。box token 未校验是否为 System.Int32。

【证据】
- CodeGenerator.cs:1275-1276 `if (c.Function.StartsWith("box_") ...) return $"il2cpp::il2cpp_box_int32(nullptr, ...)"`——与 token 无关。
- boxing.cpp:43-53 g_int32BoxClass 唯一盒类；boxing.cpp:78 硬编码 offset 8 写 int32。

【建议修复】box 发射前用 TypeTokenFullName 校验 token 类型 == System.Int32，否则 NotSupportedException。
补 box long/double/struct e2e 断言 loud 拒绝。

【补充（2026-08-03 复核）】并行域已新增 boxingext.cpp（bool/char/double 盒类 + boxed_equals），
但转译器 box_ 发射（CodeGenerator.cs:1275-1276）仍恒调 `il2cpp_box_int32`，未按 token 分派到新
助手——运行时能力存在而转译器未接线，H3 维持成立。

### H4  castclass 失败返回 null 而非抛 InvalidCastException

【位置】CodeGenerator.cs:1282-1289（castclass → `is_instance ? obj : nullptr`）；
StackFold.cs:333-336。

【问题描述】`castclass` 语义 = 类型不匹配抛 InvalidCastException。生成代码返回 null —— 调用方
无法区分「转换成功得 null 输入」与「转换失败」。后续对该值解引用 → NRE（abort，loud 但类型错）；
赋给字段/传参则 null 静默传播 → 程序行为与 .NET 偏离。注释标注「M3 简化为 null」但这是静默
行为改变，未显式拒绝。

【证据】
- CodeGenerator.cs:1288 `(il2cpp::il2cpp_is_instance(obj, &..._class) ? (Il2CppObject*)({objCode}) : nullptr)`。

【建议修复】castclass 失败路径调用 `il2cpp_throw_new("System.InvalidCastException,...", ...)`。
补 castclass 失败 e2e。

### H5  P/Invoke 多字符串参数共享单一静态缓冲区 → 参数别名

【位置】pinvoke.cpp:152-182（`static char g_marshalBuffer[4096]` 单缓冲区）；
CodeGenerator.cs:1323-1326（每个 string 参数在调用表达式内就地包装 marshall）。

【问题描述】`[DllImport] int f(string a, string b)` 调用 `f(s1, s2)` → 生成
`f(il2cpp_pinvoke_string_to_utf8(s1), il2cpp_pinvoke_string_to_utf8(s2))`。C++ 实参求值顺序
未规定，且两个 marshall 写同一个 g_marshalBuffer → **a 与 b 最终指向同一缓冲**，a 内容被 b 覆盖
（或被 a 覆盖，取决于求值顺序）→ 原生函数收到两个相同/错误的串。静默错误。同时缓冲区 4096 字节，
超长字符串静默截断（循环 `o + 4 < sizeof` 截断，无错误）。

【证据】
- pinvoke.cpp:154 `static char g_marshalBuffer[4096];`
- pinvoke.cpp:161 `o + 4 < sizeof(g_marshalBuffer)`——静默截断。
- CodeGenerator.cs:1323-1326 就地包装，无 temp 拆分。

【建议修复】多 string 参数（或同一表达式中多个 marshall 调用）各自分配独立缓冲：改每个参数
marshall 前先存到独立局部 temp（生成器在调用前为每个 string 参数引入临时变量），或运行时改为
每调用一个结构体内含多槽缓冲（按参数计数）。超长截断改为 loud（如抛 MarshalDirectiveException
占位）。补双 string 参数 P/Invoke e2e（含两个不同串）。

### H6  位图 `1u << slot` 在 slot≥32 时 UB —— >31 个指针槽的类型引用字段漏扫

【位置】TypeEmitter.cs:79（`bitmap |= 1u << slot`）；TypeEmitter.cs:66-87（slot 无上限）；
gc.cpp:507-518（扫描 `slot < 32`）。

【问题描述】32 位 refFieldBitmap 只表达 slot 0..31。类声明 ≥31 个实例字段（slot ≥ 32）时：
`1u << slot` 对 slot≥32 是未定义行为（MSVC 实际得 0），GC 扫描上限 `slot < 32`——slot≥32 的
引用字段**永不扫描** → 仅经这些字段可达的对象被回收 → 悬垂/静默损坏。真实代码中 30+ 字段的
配置/状态类并不罕见。

【证据】
- TypeEmitter.cs:79 `bitmap |= 1u << slot;`（slot 无上限检查）。
- gc.cpp:510 `for (int32_t slot = 1; slot < 32 && slot < slotLimit; ++slot)`。
- gc.cpp:628-631 压缩阶段同款 `slot < 32`。

【建议修复】字段数 > 31（或 InstanceSize/8 > 32）时转译期抛 NotSupportedException；或位图升级为
64 位 + 动态槽扫描。补 32 字段类型的 e2e（当前静默错误）。

### H7  继承接口分派缺基类链 —— 派生实例经基类实现接口 dispatch 时运行时 abort

【位置】metadata.cpp:129-154（`il2cpp_interface_dispatch` 只扫 `klass->interfaces`，不走
baseClass）；VTablePlanner.cs:63-79（BuildInterfaceMaps 只遍历 `t.InterfaceTypes` 直接接口）；
MetadataLoader.cs:52-62（InterfaceImpl 只收直接接口名）。

【问题描述】`class Base : IFoo { public void M() {} }`、`class Derived : Base {}`、合法 C#
`IFoo x = new Derived(); x.M();` → Derived 描述符 interfaces 表不含 IFoo 条目 →
`il2cpp_interface_dispatch` 找不到 → `std::abort()`。.NET 语义应经基类实现分派。合法代码
运行时崩溃（loud 但错误）。

【证据】
- metadata.cpp:129 `for (int32_t i = 0; i < klass->interfaceCount; ++i)`——仅当前类型接口表。
- metadata.cpp:148-153 未命中 → abort。
- VTablePlanner.cs:65 `foreach (var iface in t.InterfaceTypes)`——仅直接接口。

【建议修复】接口分派沿 baseClass 链扫描各层 interfaces 表（与 is_instance 的基类接口扫描一致，
boxing.cpp:130-136 已是此模式）；BuildInterfaceMaps 汇总继承的接口映射。补继承接口 e2e。

### H8  throw 恒抛 System.Exception（对象被丢弃）—— catch 派生类型永不匹配，行为静默偏离

【位置】StackFold.cs:302-307（Throw → `stack.Pop()` 丢弃异常对象 + `CallStmt throw_exception`）；
CodeGenerator.cs:600-605（`throw_exception` → `il2cpp_throw_new("System.Exception", "exception thrown")`）。

【问题描述】`throw new InvalidOperationException("...")` 的 newobj/ctor 被折叠为
`il2cpp_object_ctor_nop`（MethodRefResolver.cs:109-112），throw 点恒抛 System.Exception 固定消息。
同方法或上层 `catch (InvalidOperationException)` 经 `il2cpp_exception_matches`
（exception.cpp:36-71）判定不匹配 → rethrow → 顶层 catch-all → 未处理异常崩溃。.NET 语义应为
catch 命中。异常类型与消息全部丢失（StackTrace/诊断失真）。文档标注 M3 简化但**静默改变可观察行为**。

【证据】
- StackFold.cs:306 `stack.Pop();`——异常对象丢弃。
- CodeGenerator.cs:603 固定 `"System.Exception"`、`"exception thrown"`。
- MethodRefResolver.cs:109-111 System.Exception..ctor → nop。

【建议修复】M3.5 前至少做到：throw 时解析异常类型 token → `il2cpp_throw_new("{Type}," + 基类链, ...)`
（运行时已支持链式匹配，exception.cpp:36-71）。补 `catch(派生类型)` e2e。

【补充（2026-08-03 复核）】并行域已新增 exceptionext.cpp（11 个 `il2cpp_throw_*` 工厂，
含 ArgumentNull/ArgumentOutOfRange 等），但转译器 `throw_exception` 发射（CodeGenerator.cs:600-605）
仍恒抛固定 System.Exception，未接线到新工厂——H8 维持成立。

### H9  `INT_MIN / -1` 为 C++ UB —— .NET 抛 OverflowException，本实现 SIGFPE/崩溃

【位置】exception.cpp:106-115（il2cpp_checked_div/rem 只查除零）；CodeGenerator.cs:663-666
（所有 int32 Div/Rem 走该 helper）。

【问题描述】C++ 标准中 `INT_MIN / -1` 是 UB（结果不可表示）；x86 idiv 对该输入产生 #DE
→ SIGFPE 进程崩溃。.NET（默认 unchecked）下该表达式抛 OverflowException。注释自认
（exception.cpp:100-104）「C++ result is UB, the overflow check arrives with the
arithmetic-overflow task」——当前即静默/崩溃 UB。

【证据】
- exception.cpp:106-115 只 `if (b == 0)`，`return a / b;`——INT_MIN/-1 无保护。
- exception.cpp:100-104 注释自认 UB。

【建议修复】checked_div/rem 增加 `(a == INT32_MIN && b == -1)` → 抛
System.OverflowException。补 `int.MinValue / -1` e2e。

---

## Medium

### M1  嵌套 try 区域静默误编
【位置】CodeGenerator.cs:326-468（区域发射算法单层；注释「NON-NESTED regions」无检测）。
【问题】try 套 try（C# 常见：外 try/catch 包内 try/finally）时，`inTry` 只记最内层，关闭逻辑
`regions[activeTry]` 用单层索引，内层 finally/外层 catch 的打开/关闭顺序会交错 → 生成错误嵌套
C++ 或逻辑错，且不报错。测试用例 exceptions.cs 全为单层。
【建议】检测区域重叠/嵌套 → NotSupportedException（loud），或实现区域栈。

### M2  非 ASCII 字符串字面量静默丢弃
【位置】string.cpp:134-157（new_from_cs 跳过 ≥0x80 字节）；CodeGenerator.cs:1351-1356
（EscapeC 原样透传非 ASCII）。
【问题】C# 字面量含中文/emoji → 生成 UTF-8 源码字符串 → 运行时逐字节过滤 ≥0x80 → 字符串内容
静默缩短/损坏；Console 输出同样丢（runtime.cpp:21 `c < 0x80 ? c : '?'`）。无错误信号。
【建议】字面量非 ASCII → 按 UTF-8 解码转 UTF-16 或 loud 拒绝；输出改为编码感知。

### M3  P/Invoke 字符串缓冲 4096 静默截断（与 H5 同源，单列）
【位置】pinvoke.cpp:161。
【问题】超长串静默截断，原生函数拿到截断串，无信号。

### M4  诊断信息缺方法名/IL 偏移上下文
【位置】StackFold.cs:389（`fold: opcode X not supported in M3` 无 offset/方法名）；
Program.cs:24-28（catch-all 只打印 ex.Message，无栈/方法名）。
【问题】转译器报错时用户无从定位哪个方法哪条 IL。IlDecoder.cs:235-236 有 offset，但折叠层
异常无 offset；IR 层面 Stmt.SourceOffset 未进异常消息。
【建议】异常统一携带方法全名 + IL offset；Program 打印 `Type::Method` 上下文。

### M5  IlByteReader 无边界检查
【位置】IlDecoder.cs:267-296（`_il[_pos++]` 直接索引）。
【问题】损坏/截断 IL 触发 IndexOutOfRange，被 CLI catch-all 以 exit 2 吞掉——非静默损坏但诊断
劣。discover.md 已列为 M2 待办，至今未做。
【建议】读前校验 RemainingBytes，越界抛带偏移的 NotSupportedException。

### M6  isinst/castclass 目标为 BCL 类型 → 引用未定义描述符符号 → 链接错误
【位置】CodeGenerator.cs:1286-1288（`&{Sanitize(typeName)}_class`）；运行时只有 g_stringClass/
g_int32BoxClass（string.cpp:39、boxing.cpp:43），无 `System_String_class`。
【问题】`o is string`、`o is int`（isinst System.String/System.Int32）→ 生成 `&System_String_class`
未定义符号 → 链接失败（loud，但合法 C# 无法构建）。user 类型 isinst 正常。
【建议】BCL 类型映射到运行时描述符（string → il2cpp_string_class() 等），或 loud 拒绝。

### M7  抽象类方法体 RVA=0 → 转译器抛「method has no body」
【位置】CodeGenerator.cs:134（含所有非静态方法）；MethodBodyReader.cs:61-62（RVA=0 → 抛）。
【问题】任何抽象类（abstract class / abstract method，真实代码极常见）转译失败。loud 但缺支持；
接口已特判跳过（CodeGenerator.cs:138），抽象类未特判。
【建议】抽象方法（IsAbstract）跳过发射，仅进 vtable 规划。

### M8  方法名哈希碰撞（SigHash FNV-1a 64）
【位置】SigHash.cs:5-15（FNV-1a 64，非密码学）；MethodRefResolver.cs:53/117；
CodeGenerator.cs:1404-1405。
【问题】碰撞概率当前 ~0（方法数小），全量 BCL（M5，~10 万方法）生日界 ~2.7e-10——低但非零。
碰撞后果 = 两个不同方法同名 C++ 函数 → 重复符号/重定义 → 编译错误（loud，非静默损坏）。
对抗性输入（用户可控源码）可刻意构造碰撞。另：泛型多实例化到来时，哈希输入用定义名
（MethodRefResolver.cs:70 hashInput=owner 定义名），不同实例化必须把实例名纳入哈希/名称，
否则跨实例碰撞（当前单实例化不触发）。
【建议】保留 64 位（loud 失败兜底已足够），泛型多实例化时名称须含实例化后缀；如需更强可换
128 位。

### M9  帧仅覆盖含引用局部的编译方法 → 未处理异常报告/StackTrace 不完整
【位置】CodeGenerator.cs:924（refLocals.Count==0 直接返回，无帧）；exception.cpp:80-96
（il2cpp_report_unhandled_exception 只列存活帧）。
【问题】纯算术/无引用局部的方法不压帧 → 深层异常链报告缺失中间帧；与「禁内联/禁尾调用+帧栈」
的 StackTrace 目标相悖。
【建议】至少为所有方法压含 methodName 的空 refs 帧（开销小），或文档化限制。

### M10  MethodRefResolver.cs:115 isInstanceMr 布尔逻辑混乱
【位置】MethodRefResolver.cs:115（`A && B && C || D`）。
【问题】`get_`/`set_` 前缀方法（属性访问器）被判为非实例 → MemberRef 路径调用不弹 this。当前
同程序集调用走 MethodDef（IsInstance 正确），该路径主要命中跨程序集/BCL MemberRef；但逻辑可读
性差且 get_ 启发式不可靠，易被真实代码触发错位。
【建议】按签名调用约定位（HASTHIS）判定，删除字符串启发式。

### M11  转译器单线程假设未文档化；共享可变 TypeDef 状态
【位置】VTablePlanner.cs:36-50（PlanAll 原地改写 TypeDef.VirtualMethods/VTableSlot）；
TypeEmitter.cs:66-87（LayoutInstanceFields 改写 InstanceSize/bitmap）。
【问题】CodeGenerator.Generate 非纯函数、不可重入：并发对同一 types 列表调用会竞争；无并发文档。
Program.cs 单线程使用，风险当前为零，但违反「文档化单线程假设」的审计项。
【建议】Generate 入口加文档注释声明非线程安全；或将布局/规划改为局部副本。

---

## Low

### L1  死代码（清理候选，只报告不删除）
- CodeGenerator.cs:1392 `ParamList(string[])`——无调用者。
- CodeGenerator.cs:1052-1065 `FindMethodHandle`——无调用者。
- gc.cpp:1063-1102 `il2cpp_gc_add_root_range`/`remove_root_range`——生成代码已改用帧协议
  （WrapFrameRoots，CodeGenerator.cs:922-954 不再调用），仅测试 test_gc.cpp/test_gcstress.cpp 使用。
- object.cpp:74-82 `il2cpp_class_is_ref_field`——仅 test_object.cpp 使用，生产零调用。
- StackFold.cs:891-923 `EnsureCctor`/`EnsureCctorForCall`/`EnsureCctorForNewObj` 的 `types` 参数
  未使用（签名冗余）。
- CodeGenerator.cs:908-909 注释仍描述旧的 add/remove_root_range 协议（已死路径）。

### L2  文档/注释过期
- il2cpp_frame.h:14-18 声称「every 'return' ... pops」——实际实现是 RAII 析构弹出
  （CodeGenerator.cs:950-953），头注释与实现不符。
- CodeGenerator.cs:1220-1221 DescribeMethodSemantics 文案「M1 32-bit signed integer contract」
  ——项目已到 M4。
- CodeGenerator.cs:1208 IlMethodSignature 把所有参数描述为 System.Int32（实际含引用参数）。

### L3  api.cpp:63-66 export 表溢出时 fprintf+return（非 abort）——静默丢导出项
【位置】api.cpp:63-66。
【问题】溢出时仅打印并返回，C API 查询不到该导出 → 静默缺失；与运行时「fail-loud」惯例不一致。

### L4  api.cpp:122 g_lastExceptionMessage 1024 字节截断
【位置】api.cpp:122。

### L5  icall_environment_get_newline 每次调用新建字符串，无缓存
【位置】runtime.cpp:54-57。
【问题】反复调用产生垃圾与分配；字符串语义虽对，但违反「静态常量尽量常量初始化」倾向。

### L6  SigHash 输入不含返回类型
【位置】SigHash.cs；MethodRefResolver.cs:53/117。
【问题】C# 不允许仅按返回类型重载，当前安全；仅记录以便未来 CLS 之外场景核查。

---

## 审计清单逐项结论（对照必读项）

1. 未定义行为：C2（移动 GC vs 根）、C3（int64 窄化）、C4（引用数组越界写）、H1（无符号比较）、
   H6（位图移位 UB）、H9（INT_MIN/-1）、另函数指针经 void* 往返（EmitVTable/EmitVirtualCall/
   DelegateInvokeExpr）为平台惯例式 conditionally-supported 转换——x64 三编译器可用但严格 ISO C++
   属 UB，建议注释声明依赖。运行时强制转换集中：reinterpret_cast 均在 x64 布局已证前提下。
2. 栈折叠正确性（StackFold.cs）：opcode 覆盖 = 未处理操作码一律 default→NotSupportedException
   （loud，StackFold.cs:388-389）✓；但 Conv_i4/Conv_i 是**静默 no-op**（C3）、leave 是**静默
   no-op**（H2）；栈深度不匹配在 join 处显式抛（StackFold.cs:62/446）✓；dup 安全（不可变树，
   StackFold.cs:135-138）✓；pop 有空栈保护（StackFold.cs:300）✓；分支汇合栈一致性经 join 槽
   校验 ✓；stloc/starg 快照经 ReferencesTarget（StackFold.cs:476-495）✓。
3. 异常路径：try/catch/finally 有实现（CodeGenerator.cs:326-468），catch 类型匹配经
   il2cpp_exception_matches ✓；缺口 = throw 恒 System.Exception（H8）、嵌套 try 无检测（M1）、
   leave 出 try（H2）、顶层防护 il2cpp_report_unhandled_exception 存在（host.cpp，CodeGenerator.cs:1493-1501）✓。
4. GC 根完整性：C1（参数非根，Critical）、C2（移动压缩 vs 寄存器/FieldAddr）、H6（>31 字段漏扫）、
   M9（无引用局部方法无帧）。静态字段恒 int32_t（EmitStaticStorage，TypeEmitter.cs:247-257）→
   当前模型无静态引用字段可漏，但引用静态字段根本不可表示（会编译失败）。
5. 类型系统：int64/double/uint 静默截断或错误（C3、H1、H3）；bool 存储为 int32_t（slot 网格，
   .NET bool 1 字节语义丢失但当前范围一致）——字段/局部按 int32 语义工作，无显式拒绝（Low）；
   double/float 常量（Ldc_r4/r8）→ fold 层 NotSupportedException（loud）✓；byte 数组静默按 int32
   数组（C4 关联，Medium）。
6. 并发/重入：运行时单线程假设已文档化（framestack.cpp:3「M3 single-threaded」、gc.cpp:902）
   ✓；转译器单线程假设未文档化（M11）。
7. SigHash 碰撞：64 位 FNV-1a 非密码学，碰撞概率现可忽略、后果 loud（重复符号），泛型多实例化
   须含实例名（M8）。
8. 错误报告：折叠/解析异常多数无方法名/偏移上下文（M4）；CLI catch-all 只有 ex.Message
   （Program.cs:26）；部分运行时 abort 有上下文（gc.cpp FailLoud、metadata.cpp）。
9. 死代码/未用：见 L1（ParamList、FindMethodHandle、add/remove_root_range 生产未用、
   il2cpp_class_is_ref_field、EnsureCctor types 参数、过期注释）。
