# 转译器审计修复报告（transpiler-audit-fixes）

- 日期：2026-08-03
- 范围：docs/audit/transpiler-audit.md 全部 Critical（C1/C3/C4）+ High（H1-H9）+ 属性折叠（铁律 2j）
- 方法：全 TDD（每缺陷先失败测试→修复→验证）；单元测试 39→57；受影响 e2e 35/35 × 三编译器零警告
  （**全量 artifact 清空后全新构建验证**——修正 H2 规则后重跑，杜绝陈旧产物）；runtime C++ 测试 30/30
  全过；dotnet build 0 错 0 警。未 git commit。
- 过程事故记录：初版 H2 规则（leave 目标 == tryEnd）被实测 Roslyn IL 证伪——真实产物 leave 一律指向
  handlerEnd，TryDiv/boxing 直接转译失败；修正后全量重验。期间并行代理多次清理 tests/cases 产物目录，
  曾导致部分 e2e 以陈旧 exe 通过——已通过"wipe 后全新运行"消除该疑点。

## Critical

### C4 newarr 元素类型（引用数组越界写）
- 根因：CodeGenerator.cs:1295 newarr 恒 `il2cpp_array_new(nullptr, 4, false, len)`——引用数组按 4 字节分配，
  stelem_ref 写 8 字节指针越界；且 ldelem/stelem 的引用检测（`Contains("ldelem_ref")`）因 StackFold 函数名
  编码 token（`ldelem_{token}`）永不匹配——引用数组元素一直被当 int32 读。
- 修复：① CodeGenerator `ArrayElementType`：System.Int32→(4,false)；System.String/Object/用户引用类型→(8,true)；
  其余 loud 拒绝；② StackFold ldelem/stelem 函数名改编码 OpCode，发射层 `EndsWith("_ref")` 判定。
- 测试：`Generate_RefArray_EmitsPointerElementSize`（fixture refarr）+ e2e arrays.cs 扩展（string[]/object[]）。
- 证据：生成代码 `il2cpp_array_new(nullptr, 8, true, ...)` / `(nullptr, 4, false, ...)` 并存。

### C3 int64 静默截断
- 根因：StackFold.cs:92-93 Ldc_i8 无条件入栈（long 值超 int32 范围时赋值 int32_t 静默窄化）。
- 修复：Ldc_i8 值超 int32 范围 → NotSupportedException（loud）。conv.i4 在全部常量入 int32 范围后保持 no-op 恒等
  （int64 参数/返回值本就 loud 拒绝：MethodSignatureCoder 无 0x0A 分支、ReturnTypeCpp 无 Int64）。
- 测试：`Fold_LdcI8_OutOfRange_ThrowsLoud` / `Fold_LdcI8_InRange_FoldsToIntConst` / `Fold_LdcI8_NegativeInRange_FoldsToIntConst`。
- 残余披露：`int.MaxValue + 1L` 中段溢出（常量均在范围内，TryFoldInt32 无类型信息）仍按 int32 回绕——后续
  int64 IR 类型化任务解决。

### C1 引用参数非 GC 根
- 根因：WrapFrameRoots 只注册 ref 局部；引用参数（含 this）不注册 → 仅经参数可达的对象被回收。
- 修复：CodeGenerator 方法循环并入 ref 参数（`r:` 前缀参数 + 非值类型 this）到 refLocals（`&argN` 为合法根地址）。
- 测试：`Generate_RefParam_IsRegisteredAsFrameRoot`（fixture refarr）+ e2e paramroot（参数唯一持有对象 + 5000
  分配压力，42/7/-1 输出与 net48 逐字节一致）。

## High

### H1 Cgt_un/Clt_un 无符号比较
- 根因：CodeGenerator.cs:1346 非常量路径发射有符号 `>`/`<`；且 Roslyn 的 uint 参数是裸字节 0x09 (U4)——
  审计报告的 0x11 VALUETYPE 假设错误，MethodSignatureCoder 此前直接拒绝。
- 修复：① `0x09 => "i"`（uint 存 int32_t 槽，ABI 同 int32）；② EmitExpr CgtUn/CltUn 非空比较发射
  `((uint32_t)(l) op (uint32_t)(r))`。
- 测试：`Generate_UintComparison_EmitsUnsignedCompare` + e2e uintcmp（8 行输出，含 0xFFFFFFFFu/0x80000000u）。

### H2 leave 折叠 no-op
- 根因：StackFold.cs:284-290 leave 无条件 no-op——try 内 break/continue 出 try 静默错控制流。
- 修复：MethodContext 透传 ExceptionRegions；Leave 目标必须 == 所在**最内层区域**（按 extent 最小者）的
  **handler 结束偏移**（handlerOffset + handlerLength），否则 NotSupportedException（loud）。
- 实测修正：Roslyn 的 try 体与 handler 体的 leave 都指向 handlerEnd（如 exceptions.TryDiv leave@0x06 →
  0x0D = handlerEnd；boxing.Main 四个 leave 均 = 各自区域 handlerEnd）——初版"tryEnd"规则误拒真实 Roslyn
  产物（TryDiv/boxing 直接转译失败），已按实测 IL 修正。嵌套区域（H8 错类型传播用例）用最小 extent
  区域判定。
- 测试：`Fold_Leave_OutOfRegionTarget_ThrowsLoud` / `Fold_Leave_ToHandlerEnd_IsNoOp`（规则修正后更新）。

### H3 box 非 Int32
- 根因：CodeGenerator.cs:1275-1276 box 恒 il2cpp_box_int32（与 token 无关）。
- 修复：box/unbox 发射前校验 TypeTokenFullName == System.Int32，否则 NotSupportedException。
- 测试：`Generate_BoxLong_ThrowsLoud`（fixture boxlong：box 用户 struct——`object o = 5L` 在 conv.i8 处先拒绝，
  负测试必须确证失败点在目标校验处）。

### H4 castclass 失败抛 InvalidCastException
- 根因：CodeGenerator.cs:1288 castclass 失败返回 null（静默行为偏离）。
- 修复：失败路径调 `il2cpp::il2cpp_castclass_fail()`（exceptionext.cpp 新增：il2cpp_throw_new
  InvalidCastException 后返回 nullptr 供编译；抛错路径恒展开）。
- 测试：e2e boxing.cs `a as Bird` 已有 null 路径回归（isinst 语义不变）；castclass 失败路径经
  `il2cpp_castclass_fail` 断言在生成代码（CodeGeneratorTests 无专门断言——行为由后续 M3.5 异常对象任务深测）。

### H5 P/Invoke 多 string 参数别名
- 根因：pinvoke.cpp 单静态旋转缓冲 + 调用表达式内就地包装——C++ 实参求值顺序未定，多 string 参数必别名。
- 修复（转译器侧）：StackFold EmitCall 为每个 string 参数建独立 MarshalUtf8Expr temp（kind "cstr"→const char*），
  调用后 pinvoke_utf8_free；EmitTask9Call 移除就地包装。独立堆缓冲（il2cpp_pinvoke_string_to_utf8_alloc，
  pinvokeext 既有 API）顺带消除 4096 截断。
- 测试：e2e pinvoke.cs 扩展 concat_len("hello","x")=6（别名时得 2 或 10）+ mylib 新导出。

### H8 throw 类型
- 根因：StackFold.cs:302-307 抛固定 System.Exception；派生异常 catch 永不匹配；且 BCL 异常 ctor 链接到不存在的
  生成符号（`throw new InvalidOperationException()` 直接链接失败）。
- 修复：① StackFold objTypes 跟踪 newobj temp→类型，Throw 发 `throw_{Type}`；② CodeGenerator ExceptionChain：
  用户类型走基类链 / BclExceptionBases 映射 / 默认 System.Exception，链格式 "{Type},{Type},{Base1},..."（matcher
  行走跳过首元素——exceptionext 同款约定）；③ MethodRefResolver：BCL `*Exception::.ctor` → nop。
- 测试：e2e exceptions.cs 扩展（catch InvalidOperationException / catch Exception 基类 / 用户 MyEx 双 catch /
  错类型不匹配向外传播）。

### H9 INT_MIN/-1 除法
- 根因：exception.cpp:106-115 只查除零，INT_MIN/-1 为 C++ UB（x86 SIGFPE）。
- 修复：checked_div/rem 加 `(a==INT32_MIN && b==-1)` → OverflowException；链格式统一 "{Type},{Type},{Base}"。
- 测试：test_exception.cpp 5 断言（div/rem 抛 OverflowException + 正常除法回归）+ e2e overflow.cs 扩展。

### H6 位图 slot≥32
- 根因：TypeEmitter.cs:79 `1u << slot` 对 slot≥32 是 UB（MSVC 得 0），GC 扫描上限 slot<32 → 大类型引用字段漏扫。
- 修复：LayoutInstanceFields 引用字段落 slot≥32 → NotSupportedException（值字段不限）。
- 测试：`Layout_RefFieldAtSlot32_ThrowsLoud` / `Layout_ValueFieldAtSlot32_IsAllowed`。

### H7 接口分派基类链
- 根因：① metadata.cpp:129 只扫当前类型 interfaces；② VTablePlanner.BuildInterfaceMaps 只遍历直接接口；
  ③ 更隐蔽：descriptor baseClass 恒 nullptr——isinst 的 `a is Base` 继承判定此前也是错的（顺带修复）。
- 修复：① VTablePlanner 汇总基类链接口映射（派生先、基类后，去重）；② EmitClassDescriptor 接
  `&{Base}_class`；③ il2cpp_interface_dispatch 沿 baseClass 链扫描、槽位在**接收者** vtable 解析（派生
  override 生效）。
- 测试：e2e interface.cs 扩展（DerivedOnly 纯继承接口 + `d is ICalc`）+ boxing/virtual 回归（isinst 继承语义）。

## 属性折叠（铁律 2j）

- 实现：AccessorAnalyzer.cs（新，预扫描 pass）+ StackFold.Fold 签名扩展（accessors 参数）+ EmitCall 双路径
  （call/callvirt——C# 实例属性访问发 callvirt）折叠 + CodeGenerator Scan/跳过被折叠 accessor 发射 +
  MetadataLoader 加载 `<X>k__BackingField`（自动属性）+ (void)argN 未用参数泛化。
- 判定：get_/set_ 前缀 + 非虚/非抽象 + 字段同类 + 精确形态（get: ldarg.0; ldfld; ret / set: ldarg.0; ldarg.1;
  stfld; ret / 静态变体，nop 容忍）。Ldarg_1 无操作数（Operand 恒 0）——短格式判定须分开。
- 验证：props e2e 双跑一致（8 行输出）+ 生成代码 grep：accessor 函数符号 0 个；调用点全为
  `->_value`/`->_Auto_k__BackingField`/`Counter_sf__Shared_k__BackingField` 直接字段访问；计算属性 Doubled 与
  虚属性 Tag 保留函数/vtable 槽（无过度折叠）。单测 6 个（AccessorAnalyzerTests）+ CodeGeneratorTests 1 个。

## 剩余未修项

- 审计 Medium 11 项未动：M1 嵌套 try 静默误编 / M2 非 ASCII 字面量 / M4 诊断上下文 / M5 IlByteReader 边界 /
  M6 isinst BCL 类型 / M7 抽象类 / M8 哈希碰撞（loud 兜底已够）/ M9 空帧 / M10 逻辑混乱 / M11 线程安全文档。
- 外部域缺口（非转译器）：arrayext e2e 需 BCL Array.Sort/Copy/Reverse/IndexOf → icall 接线（0x1D U 类型支持，
  M5 域任务）；stringbuilder.cpp 的 include 修复已由本会话完成（纯编译修复）。
- 残余披露：int64 中段运算（`int.MaxValue + 1L`）按 int32 回绕；uint 移位（>> 逻辑/算术）语义未区分。
