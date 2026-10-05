# 反射支持状态 v2（typeinfo / invoker / 模式 B 批次落地后刷新）

- 刷新日期：2026-08-05（typeinfo/invoker/模式 B 批次之后）
- 刷新性质：只读核查 + 文档（未修改任何 src 代码、未 git commit）
- 依据：docs/architecture/reflection-runtime-design.md（M5.5 设计，329 行）、
  docs/audit/reflection-audit.md（2026-08-03 基线，缺口 #1-#6）、
  docs/audit/reflection-chain-verify-2026-08-05.md（15:06 只读核查）
- 范围：design §1 API 面 / §2 元数据表 / §3 统一 Invoker / §4 模式 A/B/C / §5 类型系统衔接
  逐项对账 —— 已落地 vs 未落地 + 施工单

---

## 1. 结论摘要

| 能力（design §1/§4） | 状态 | 关键证据 |
|---|---|---|
| typeof 折叠（ldtoken + GetTypeFromHandle → 常量字符串） | ✅ 已落地 | StackFold.cs:781-800（Ldtoken case）、1106-1122（GetTypeFromHandle 折叠） |
| Type.Name / MemberInfo::get_Name 短名 | ✅ 已落地 | MethodMap.Type.cs:46-47 → il2cpp_type_get_short_name；StackFold.cs:1142-1147（typeof 路径截短） |
| Type 域原语接线（GetTypeCode/IsAssignableFrom/Equals） | ✅ 已落地 | MethodMap.Type.cs:58-79；runtime typeext.cpp:107-138、typecheck.cpp:245 |
| typeinfo 元数据表（runtime 侧：结构/注册表/查询原语） | ✅ 已落地（runtime 侧） | typeinfo.h:43-136、typeinfo.cpp:96-181；test_typeinfo.cpp 37 断言全绿 |
| 模式 B CLI（--reflection-list / --reflection-granularity） | ✅ 已落地（CLI 面） | CliOptions.cs:3-106；Program.cs:37-38；CliTests.cs 12 测试 |
| TypeInfo 表**转译器发射**（{Type}_typeinfo + 注册） | ❌ 未落地 | src/il2cppc 无任何 "typeinfo" 命中 → runtime 注册表永远为空 |
| 统一 Invoker（薄包装 + icall_method_invoke） | ❌ 未落地 | src 全树无 invoker 实现（typeinfo.h:52 仅注释；api.cpp:101 是 M4 C-API 导出表，非统一 Invoker） |
| GetMethod/GetField/GetProperties 端到端 + 信息对象 | ❌ 未落地 | typeinfo.cpp 查询原语无任何调用方；无 MethodInfo/FieldInfo/PropertyInfo 托管包装 |
| MethodInfo.Invoke / FieldInfo.GetValue/SetValue | ❌ 未落地 | 无 icall_method_invoke |
| Activator.CreateInstance | ❌ 未落地 | 无 icall_activator_create |
| Type.GetType(string) 字符串驱动 | ❌ 未落地 | typeext.h 无 type_get_type 原语；MethodMap.Type.cs 无 case |
| 模式 A（全量开关）/ 模式 C（静态预扫描） | ❌ 未落地 | ReflectionGranularity.All 仅解析/回写未消费；无 typeofSet 收集 pass |

**一句话**：design §7 第 1-2 步的 **runtime 半侧**（typeinfo 结构+注册表+查询原语、typeof 折叠、
Type 域接线、模式 B CLI 面）已落地；第 2-4 步的**转译器生成侧**（TypeInfo 表发射、清单/扫描
驱动、统一 Invoker）与全部"端到端"能力（GetMethod/Invoke/CreateInstance）未落地。

---

## 2. 已落地清单（含证据）

### L1. typeof 折叠（审计缺口 #1 → 已闭环；design §5.2 已落地）

| 环节 | 落地位置 | 证据 |
|---|---|---|
| Ldtoken 解码分流 | IlDecoder.cs:193-227：TypeDefOrRef → `IlOperandKind.TypeToken`（Field/Method token 保持原语义，InitializeArray 模式） | [READ:IlDecoder.cs:193-227] |
| StackFold Ldtoken case | StackFold.cs:781-800：Type 类 token → IntConstExpr 标记；781-800 注释自述 "typeof(T) support (reflection audit F1)" | [READ:StackFold.cs:781-800] |
| GetTypeFromHandle 折叠 | StackFold.cs:1106-1122：ldtoken+call 对折叠为类型**全名字符串**（简化 Type 模型：Type 值 = 全名托管字符串，与 icall_object_get_type 输出 byte-identical） | [READ:StackFold.cs:1106-1122] |
| typeof 链消费折叠 | StackFold.cs:1138-1154：get_Name → ShortTypeName 截短；GetTypeCode → 常量 | [READ:StackFold.cs:1138-1154] |
| 发射接线 | MethodMap.Type.cs:31-33：GetTypeFromHandle → `il2cpp_type_get_type_from_handle`（descriptor ABI），返回类型记录为 "System.Type" 供链上解析 | [READ:MethodMap.Type.cs:25-33] |
| 落地批次 | commit 01c1f14（typeof-chain folds + StackFoldTests.TypeChain）、f66a1c8 | [BASH:git log --oneline] |

### L2. GetType 短名（审计 §1 "Type.Name 部分✅" → 完整）

- 运行时接收者路径：MethodMap.Type.cs:46-47 `get_Name` → `il2cpp_type_get_short_name`
  （runtime typecheck.cpp:245，全名→短名截断，"System.Int32"→"Int32"，net48 语义）
- 折叠接收者路径：StackFold.cs:1142-1147 对 typeof 折叠值直接截短（不发调用）
- 修复对象：boxing2/boxdeep 的 GetType 全名 mismatch（commit ac30d01 消息自述）
- 批次：ac30d01（"Type.Name / MemberInfo::get_Name -> il2cpp_type_get_short_name runtime truncation"）、01c1f14

### L3. Type 域原语接线（审计缺口 #2 → 部分闭环）

| 原语 | 转译器 | 运行时 |
|---|---|---|
| GetTypeFromHandle | MethodMap.Type.cs:31-33 | typeext.cpp:107（klass → 全名字符串） |
| get_Name（短名） | MethodMap.Type.cs:46-47 | typecheck.cpp:245 |
| GetTypeCode（静态 1 参） | MethodMap.Type.cs:58-60 | typeext.cpp:138（klass → TypeCode，net48 表） |
| IsAssignableFrom | MethodMap.Type.cs:69-71 | typeext.cpp:124（identity/array 前缀/baseClass 链/接口扫） |
| Equals（跟踪 Type 实参） | MethodMap.Type.cs:77-79 → icall_type_equals | typecheck.cpp（descriptor identity + 名字等价） |
| 实例 GetTypeCode()（IConvertible） | LOUD 拒绝（MethodMap.Type.cs:65） | —（简化 Type 模型无 string→TypeCode 路径） |

### L4. typeinfo 元数据表（design §2.1 按草案落地；审计缺口 #3 runtime 侧 + #4 查询原语）

- 结构：typeinfo.h:43-96 —— Il2CppMethodInfo（name/signature/paramCount/flags/virtualSlot/declaringType/fnPtr）、
  Il2CppFieldInfo（name/typeName/offset/isRef/staticAddr/flags）、Il2CppPropertyInfo（accessor 只存名，单权威）、
  Il2CppTypeInfo（name/klass/baseType/interfaces/fields/methods/properties）
- 注册表：typeinfo.cpp:96-117 `il2cpp_typeinfo_register`（幂等 + overflow abort，kMax=256 镜像 metadata.cpp）、
  typeinfo.cpp:127-142 `il2cpp_typeinfo_lookup`（线性扫全名）
- 查询原语：typeinfo.cpp:147-181 `il2cpp_type_get_property/get_method/get_field`（FindMemberThroughBaseChain：
  自有表优先 = .NET shadowing，再走 baseType 链；null 入参 → nullptr 防御式约定）
- 测试：tests/runtime/test_typeinfo.cpp 37 断言全绿（注册表 7 / get_property 9 / get_method 10 / get_field 7 /
  布局落地 4；含继承命中、遮蔽、trimmed 条目 fnPtr=null 仍可查、静态字段 offset=-1+staticAddr）
- 批次：commit 8a9a3a9（"M6 BCL runtime new domains (.../typeinfo)"）
- ⚠ **范围限定**：以上全部是 **runtime 侧就绪**；转译器尚未发射任何表（见 U1），注册表在真实程序里为空。

### L5. 模式 B CLI（审计 §4 建议 → CLI 面落地）

- CliOptions.cs:3-16：`ReflectionGranularity{Members,All}`、`ReflectionListProvided`、`ReflectionList`
- CliParser：`--reflection-list <manifest>`（:35-39）、`--reflection-granularity all|members`（:40-44）、
  manifest 读取（# 注释 + 空行跳过，:70-91）
- Program.cs:37-38：提供清单时把清单回写为 `reflection-manifest.txt`（ReflectionManifest.Write，CliOptions.cs:94-105）
- 测试：CliTests.cs 12 测试（解析/缺省/粒度/缺文件/缺值/非法粒度/清单回写/非零退出）
- 批次：commit f66a1c8
- ⚠ **范围限定**：CLI 面完整但**生成面未接线**——ReflectionList/Granularity 仅被解析、存储、
  回写清单；CodeGenerator 从不消费（src 全树 grep ReflectionGranularity 仅在 CliOptions.cs，[BASH:grep]）。
  另注意：缺省粒度为 `Members`（CliOptions.cs:15），与审计建议的"缺省 all"不同。

### L6. 邻接域（相关但独立，记录在案）

- 自定义特性：attribute.h:68/90 `il2cpp_attribute_register`/`il2cpp_attribute_get_custom`（runtime 侧，
  batch 94b5b18 "reflection" 域）——与 design §1 API 面不同轨，未在 M5.5 范围内验收。
- MethodMap.Attribute.cs:29-30 注释仍称 "typeof/GetTypeFromHandle is not emitted yet"——**该注释已过期**
  （L1 已落地），施工单 U-8 顺手校正。

---

## 3. 未落地清单（含差距细节）

### U1. TypeInfo 表转译器发射（审计缺口 #3 转译器侧；design §2.2）

- 现状：src/il2cppc 全树 grep `typeinfo|TypeInfo` → 零命中；CodeGenerator entry pass 只发
  `il2cpp_class_register`（CodeGenerator.cs:2045 附近，design §2.2 指定在此旁追加
  `il2cpp_typeinfo_register`）——**无 {Type}_typeinfo 静态表、无子表数组、无注册调用**。
- 后果：L4 的 runtime 注册表/查询原语在真实程序中永远空转；`il2cpp_type_get_method` 等无数据可查。
- 缺口所在 pass（design §2.2）：TypeEmitter（与 EmitClassDescriptor 同 pass）、CodeGenerator entry pass；
  数据源 TypeDef（MetadataLoader）+ MethodSignatureCoder 签名编码均已就绪（审计"已具备 3"）。

### U2. 统一 Invoker（审计缺口 #5；design §3）

- 现状：src 全树 grep `invoker|Invoker` → 唯一命中 typeinfo.h:52 注释（"or invoker wrapper"）；
  无 invoker.h/cpp、无 per-方法薄包装发射。
- 易混澄清：api.cpp:101 `il2cpp_method_invoke_i4` 是 **M4 C-API 导出表**（int32(int32,int32,int32,int32)
  按 (typeName, methodName) 匹配 il2cpp_runtime_register_export 注册的导出项），**不是** design §3.1 的
  统一签名 Invoker（Il2CppObject* invoker(void* fn, void* argbuf, int32_t argc)），不可互替。
- 缺失件：MethodEmitter 每反射方法薄包装、Il2CppMethodInfo.fnPtr 填值、icall_method_invoke
  （argbuf 构造 + box/unbox marshalling 复用 boxing.cpp + WrapFrameRoots 帧根）。

### U3. GetMethod/GetField/GetProperties 端到端 + 信息对象（审计缺口 #6）

- 现状：typeinfo.cpp:147-181 查询原语**无任何调用方**（grep 仅测试引用）；MethodRefResolver 无
  GetMethod/GetField/GetProperties case；无 MethodInfo/PropertyInfo/FieldInfo 托管包装对象
  （design §1 的 wrapper 构造与 §2.1 "GC 无需感知、包装对象运行时构造"约定未实现）。

### U4. MethodInfo.Invoke / FieldInfo.GetValue/SetValue / PropertyInfo 访问器

- 依赖 U2（Invoker）+ U3（信息对象）；转译器与 runtime 均无 icall_method_invoke。

### U5. Activator.CreateInstance

- 无 icall_activator_create；design §1 要求"查方法表中标志 ctor 且零参条目 → Invoker"未实现。

### U6. Type.GetType(string) 字符串驱动（模式 B/C 关键入口）

- typeext.h 无字符串驱动原语（仅有 descriptor ABI 的 4 个）；MethodMap.Type.cs 无 case；
  模式 B 的"未覆盖 → null"、模式 C 的"分析不到 → 编译期告警"均无载体。

### U7. 模式 A / 模式 C（design §4）

- 模式 A：`ReflectionGranularity.All` 无消费者（L5 限定）——"全量编译开关"仅存在于 CLI 枚举。
- 模式 C：无 IL 预扫描 pass——StackFold.cs:781-800 折叠 typeof 时**不记录 typeofSet**；
  `Type.GetType(ldstr)` 字符串常量收集、与模式 B 清单的并集合并（design §4 合并规则）均未实现。

### U8. 文档/测试侧

- 设计文档 §0 基线表已过期：`typeof 落地: 未实现`（IlDecoder.cs:186-187 行号也已漂移，现为 193-227）→ 已落地；
  MethodMap.Attribute.cs:29-30 过期注释同上。
- tests/cases 无 reflectioncase（design §6.2 验收用例 1-6 未建）；test_typeinfo.cpp 覆盖的是
  **手工 fixture 表**（test_typeinfo.cpp:49-58 注释 "mirroring what TypeEmitter would generate"），
  非转译器真实产物。

---

## 4. 与设计文档 §7 实施顺序对账

| §7 步骤 | 状态 | 说明 |
|---|---|---|
| 1. typeinfo.h + 运行时注册/查找 | ✅ runtime 半侧完成 | L4；转译器发射（U1）未做 |
| 2. Ldtoken 分流 → LdtokenExpr → &{Type}_class；GetTypeFromHandle 接线 | ✅ 完成（语义为折叠到全名字符串，非 &{Type}_class 常量发射——简化 Type 模型选择） | L1/L3 |
| 3. 模式 C 预扫描 + TypeEmitter TypeInfo 生成 + entry 注册 | ❌ | U1+U7（此步骤含 U1 主体） |
| 4. 统一 Invoker + GetField/GetProperties/GetMethods/Activator icalls | ❌ | U2-U5 |
| 5. 模式 B 清单文件解析（合并进模式 C） | ⚠ CLI 解析已落地，生成面合并未做 | L5+U7 |
| 6. 模式 A = 全部类型开关 | ❌ | U7 |
| 7. 验收：reflectioncase e2e + 质量门 | ❌ | U8 |

---

## 5. 施工单（按依赖顺序，供 plan 引用）

| # | 施工项 | 文件锚点 | 验收标准 |
|---|---|---|---|
| R-REFL-01 | TypeInfo 表发射（TypeEmitter 与 EmitClassDescriptor 同 pass）：`{Type}_typeinfo` 静态表 + methods/fields/properties 子表 + entry.cpp 旁 `il2cpp_typeinfo_register` | TypeEmitter.cs:159-190 同款 CppWriter；CodeGenerator.cs:2045 旁；签名走 MethodSignatureCoder | 生成码含 {Type}_typeinfo 且编译零警告；test_typeinfo.cpp fixture 可由真实生成产物替代（断言不变） |
| R-REFL-02 | 反射保留集驱动：先接通模式 B（ReflectionList → 生成集合），再补模式 C 预扫描（StackFold.cs:781-800 记录 typeofSet + `Type.GetType(ldstr)` 字符串常量收集，AccessorAnalyzer.cs:61 同构先例） | StackFold.cs:781-800、MethodMap.Type.cs；CliOptions/Program 消费 ReflectionList | 清单/typeof 命中的类型生成表；未命中类型 `il2cpp_typeinfo_lookup` 返回 null；CliTests 增生成断言 |
| R-REFL-03 | 统一 Invoker：MethodEmitter 每反射方法薄包装 `invoker_{Type}_{Name}_{hash}`（统一 ABI argbuf/argc，值类型拷贝/引用取指针/this 第 0 槽，结果装箱）+ fnPtr 填值 | MethodEmitter.EmitMethod；MethodRefResolver.cs:52-55 命名同款 | 包装函数编译；调用真实 {Type}_{Name}_{hash} 结果一致（单测可直接调用包装断言装箱值） |
| R-REFL-04 | icall_method_invoke（MethodRefResolver 接线）：MethodInfo* → fnPtr → argbuf（box/unbox 复用 boxing.cpp + WrapFrameRoots 帧根）→ 调用 → 结果 | MethodRefResolver Type 域扩展；runtime typeinfo 域新增 invoke 原语 | design §6.2 用例 3（GetMethod+Invoke 实例/静态/私有各一） |
| R-REFL-05 | GetMethod/GetField/GetProperties icall + MethodInfo/FieldInfo/PropertyInfo 托管包装对象（§2.1 约定） | typeinfo.cpp 原语 → icall 包装；MethodRefResolver | design §6.2 用例 2/4（GetField+SetValue/GetValue、GetProperty 含索引器） |
| R-REFL-06 | Activator.CreateInstance：`icall_activator_create`（查 ctor+零参条目 → Invoker） | MethodRefResolver；runtime | design §6.2 用例 1（CreateInstance → GetType().Name） |
| R-REFL-07 | 模式 A 开关：`--reflection-granularity all` 真正驱动全量发射（当前仅 CLI 面） | CodeGenerator/CliOptions | all 模式对全部类型生成表；体积报告（design §4 成本对比） |
| R-REFL-08 | Type.GetType(string)：字符串驱动原语（复用 il2cpp_class_by_name + TypeInfo 校验；未生成 → null/.NET 语义） | runtime typeext 域 + MethodRefResolver | design §6.2 用例 5（覆盖时返回类型、未覆盖 null） |
| R-REFL-09 | 文档/测试收尾：design §0 基线表刷新（typeof 行改"已落地"）、MethodMap.Attribute.cs:29-30 过期注释校正、tests/cases/reflectioncase e2e 双跑（net48 参考） | 文档 + tests/cases | design §6.2 用例 1-6 全过 + 质量门（三编译器零警告 + CI 全绿 + dotnet test + runtime C++ 测试） |

依赖链：R-REFL-01 → R-REFL-02 → R-REFL-03 → R-REFL-04 → R-REFL-05 → R-REFL-06；
R-REFL-07/08 与 01-06 并行（分别依赖 01 与 01+02）；R-REFL-09 收尾。
预估：01-06 为 M5.5 主体（对齐 design §7 第 3-4 步），07-08 为模式 A/C 补全，09 为质量门。

---

## 6. 证据索引（本刷新轮次）

| 主题 | 证据 |
|---|---|
| typeinfo 结构/注册表/查询 | [READ:src/runtime/include/internal/typeinfo.h:43-136]；[READ:src/runtime/src/typeinfo.cpp:96-181] |
| runtime 测试 37 断言 | [READ:tests/runtime/test_typeinfo.cpp:5-23]（覆盖声明）；docs/audit/reflection-chain-verify-2026-08-05.md:15（构建/运行 exit 0 全绿） |
| typeof 折叠落地 | [READ:src/il2cppc/Ir/StackFold.cs:781-800、1106-1122、1138-1154]；[READ:src/il2cppc/Il/IlDecoder.cs:193-227] |
| GetType 短名接线 | [READ:src/il2cppc/Emit/MethodMap.Type.cs:46-47]；[READ:src/runtime/src/typecheck.cpp:245] |
| Type 原语接线 | [READ:src/il2cppc/Emit/MethodMap.Type.cs:31-79]；[READ:src/runtime/src/typeext.cpp:107-138] |
| 模式 B CLI 落地 | [READ:src/il2cppc/CliOptions.cs:3-106]；[READ:src/il2cppc/Program.cs:37-38]；[READ:tests/unit/CliTests.cs:23-137] |
| 模式 B CLI 未被生成面消费 | [BASH:grep ReflectionGranularity → 仅 CliOptions.cs 8 命中，CodeGenerator 零消费] |
| invoker 未建 | [BASH:grep src invoker → 唯一命中 typeinfo.h:52 注释]；[READ:src/runtime/src/api.cpp:9-12、101-119（M4 C-API 导出表，非统一 Invoker）] |
| 无 TypeInfo 发射/无 typeofSet/无 GetType(string) | [BASH:grep src/il2cppc typeinfo → 零命中]；[BASH:grep src icall_method_invoke/icall_activator/type_get_type → 零命中]；[BASH:grep src typeofSet → 零命中] |
| 设计 §0 基线已过期 | [READ:docs/architecture/reflection-runtime-design.md:23（"typeof 落地: 未实现"行）vs 本刷新 L1] |
| 无 reflectioncase e2e | [BASH:glob tests/cases/*reflection* → 无文件] |
| 落地批次 | [BASH:git log：8a9a3a9（typeinfo 域）、f66a1c8（CliOptions）、01c1f14（typeof-chain folds）、ac30d01（短名 icall）] |
