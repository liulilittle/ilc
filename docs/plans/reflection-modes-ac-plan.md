# M5.5 反射模式 A/C 调研 + 实施方案（反射模式 A 全量 / 模式 C Type.GetType 字符串驱动）

- 日期：2026-08-09；分支 m1；调研代理（只读，未改任何 src/）
- 关联：docs/architecture/reflection-runtime-design.md（M5.5 设计，329 行）、
  docs/architecture/reflection-status-v2.md（R-REFL-01/02/07/08 施工票）、
  docs/roadmap-status-v5.md §1 M5.5 行、docs/REQUIREMENTS.md 铁律 2m（三模式裁剪）/
  2g-2（反射 = 完整运行时操作能力）
- 参考：reference/corert（dotnet/corert HEAD c6af4cf）MetadataManager 系列
  （MetadataManager.cs / UsageBasedMetadataManager.cs / AnalysisBasedMetadataManager.cs）
- 范围：**调研 + 方案，不实现**。铁律：只读调研；产出本文档 + 报告。

---

## 0. 执行摘要

1. **现状（实测）**：TypeInfo 表发射已落地（批次 18 bf4e32e），当前 CodeGenerator
   对**全部类型**无条件发射 TypeInfo 表（`EmitTypeInfo(t)`，reflectedNames=null =
   模式 A 语义），仅被三类守卫排除（折叠访问器宿主 / 返回类型不可编码宿主 /
   C++ 标识符非法宿主）。`--reflection-list` 清单与 `--reflection-granularity`
   **只解析、写回清单文件，从不被生成器消费**——模式 B 的"清单限定发射"并未真正生效。
2. **Type.GetType(string) 运行时半截已落地**：`il2cpp_type_get_type`
   （reflect_base.cpp:140）经 typeinfo 注册表按**原始全串**查找（identity 返回）；
   转译器已接线（MethodMap.Reflect.cs:71-75）。缺的是：① 编译期静态预扫描收集
   GetType 字符串常量 + typeof（模式 C 的"静态可知自动分析"）；② **程序集限定名解析**
   （"Ns.Type, Assembly" 现在整体当 key 查 → 必然 miss）；③ 范围接线（模式 C
   生成范围当前不存在——发射是无条件的）。
3. **模式 A 的核心障碍不是"未发射"**，而是三守卫 + 模型缺口：
   `EncodeReturnType` 对 0x11 VALUETYPE / 0x12 CLASS / 0x15 GenericTypeInstance
   返回类型 LOUD-throw（MethodDef 只带 SignatureTypeCode，不带全名）——**这是最大
   依赖**，需 MethodDef 补齐全名。折叠访问器无 fnPtr 符号 → 整类型被排除。这两项
   是"真·全量"的硬门槛。
4. **方案**：模式 A = CLI 接线 + 拆除三守卫（依赖 D1/D2/D3）+ md 传入补字段全名；
   模式 C = 新预扫描 pass（typeofSet + GetType 字面量收集，AccessorAnalyzer 同构）
   + 范围接线（让 EmitTypeInfo 的 reflectedNames 真正生效——**这同时修复模式 B**）
   + 运行时程序集限定名解析。阶段化见 §5，依赖清单见 §6。

---

## 1. 现状（审计证据，全部本代理实测）

### 1.1 模式 B CLI 面（已落地，仅 CLI 面）

| 项 | 现状 | 证据 |
|---|---|---|
| CLI 枚举 | `ReflectionGranularity{Members, All}` | [READ:CliOptions.cs:3-7] |
| `--reflection-list <manifest>` | 解析清单文件（# 注释/空行跳过） | [READ:CliOptions.cs:35-39, 79-100] |
| `--reflection-granularity all\|members` | 解析大小写不敏感 | [READ:CliOptions.cs:40-44, 74-77] |
| 清单回写 | `ReflectionManifest.Write` 写 `reflection-manifest.txt` | [READ:CliOptions.cs:103-118] |
| 清单过滤 | `FilterReflectionManifest`：清单条目按全名匹配 assembly 类型，Kept/Dropped | [READ:TypeEmitter.cs:899-913] |
| CLI 测试 | CliTests.cs 12 个反射测试 | [READ:grep CliTests.cs:23-329] |
| **生成器消费** | `CodeGenerator.Generate(types, outDir, asm, symMap)` **不接收 CliOptions** → ReflectionList/Granularity 从未影响发射 | [READ:Program.cs:45] |

**结论**：模式 B 是"CLI 面完整、生成面未接线"——与 reflection-status-v2.md L90-91
快照一致；该快照"TypeInfo 表转译器发射未落地"的 U1 已被批次 18 修复，但
"清单限定发射"仍与设计 §4 B 行（"只对清单类型生成表"）不符。

### 1.2 TypeInfo 表发射（已落地——批次 18 bf4e32e，无条件全量）

| 项 | 现状 | 证据 |
|---|---|---|
| 发射点 | 每类型 .cpp：`if (!noTypeInfoOwners.Contains(t.FullName)) w.Line(EmitTypeInfo(t))` — **reflectedNames 恒为 null（=模式 A 全量）** | [READ:CodeGenerator.cs:547-548] |
| EmitTypeInfo 契约 | `reflectedNames==null` = 发射每类型（模式 A）；否则只发集合内 | [READ:TypeEmitter.cs:672-674, 710] |
| 表形态 | `{T}_typeinfo_methods/fields/interfaces` + 主表非 const 全局 `{T}_typeinfo` | [READ:TypeEmitter.cs:718-797] |
| extern/注册 | generated.h extern + entry.cpp `il2cpp_typeinfo_register` | [READ:CodeGenerator.cs:735-736, 5241-5242] |
| 运行时注册表 | typeinfo.cpp 静态数组，kMax=256，线性查找 | [READ:typeinfo.cpp:37-42] |

### 1.3 三守卫（真·全量的障碍）

| 守卫 | 规则 | 证据 |
|---|---|---|
| G1 折叠访问器宿主 | 有折叠访问器的类型整类型排除（访问器无 fnPtr 符号 → `&{Symbol}` 链接失败） | [READ:CodeGenerator.cs:360-371] |
| G2 返回不可编码宿主 | 方法返回 0x11/0x12/0x15 → EncodeReturnType LOUD → 整类型排除 | [READ:CodeGenerator.cs:372-385; TypeEmitter.cs:856-876] |
| G3 非法 C++ 标识符 | `<PrivateImplementationDetails>.__StaticArrayInitTypeSize=3` 类名含 '=' → 整类型排除 | [READ:CodeGenerator.cs:386-399] |

### 1.4 Type.GetType 字符串驱动（运行时半截已落地）

| 项 | 现状 | 证据 |
|---|---|---|
| 运行时原语 | `il2cpp_type_get_type`：`il2cpp_typeinfo_lookup(nameBuf)` 全串查找，命中返回输入串本身（identity，简化 Type 模型），miss→null（.NET 契约） | [READ:reflect_base.cpp:125-157] |
| 转译器接线 | `Type.GetType(string)` → `il2cpp::il2cpp_type_get_type` | [READ:MethodMap.Reflect.cs:64-76] |
| 程序集限定名 | **不支持**：`"System.String, mscorlib"` 整体当注册表 key → miss（注册表 key 是 "System.String"） | [READ:reflect_base.cpp:146-151] |
| Activator | `il2cpp_activator_create_instance` 已落地（Type 单参形式） | [READ:reflect_base.cpp:101-122] |
| 静态预扫描 | **不存在**：无 typeofSet、无 GetType 字面量收集 | [READ:grep StackFold.cs typeofSet → 零命中] |
| typeof 折叠 | Ldtoken TypeToken 标记 + GetTypeFromHandle → 全名串折叠 + get_Name/GetTypeCode 链式折叠（已落地，但**不记录收集集**） | [READ:StackFold.cs:1390-1426, 1816-1864] |

### 1.5 corert 参考（模式 A/C 的设计对照）

| corert 组件 | 语义 | 对应本项目 |
|---|---|---|
| `MetadataManager`（抽象基类） | 管理嵌入元数据 blob：EmbeddedMetadata + TypeMap/InvokeMap/FieldAccessMap/ArrayMap 反射映射 | typeinfo.h 表 + typeinfo.cpp 注册表 |
| `UsageBasedMetadataManager` | "every type/method statically used shall be reflectable"（≈ 模式 C） | 模式 C 预扫描 + 范围接线 |
| `AnalysisBasedMetadataManager` | 编译期已知全部元数据集合（reflectableTypes/Methods/Fields 字典 + MetadataCategory）≈ 模式 A 全量 | 模式 A 全量发射 |
| `MetadataBlockingPolicy` / 粒度 | 决定类型/成员描述与运行时映射的裁剪 | 按需粒度（2m 第 5 点） |
| TypeParsing（TypeLexer/TypeName） | 程序集限定名词法解析 | 模式 C 运行时名字解析 |

[READ:reference/corert/src/ILCompiler.Compiler/src/Compiler/MetadataManager.cs:47-60]
[READ:.../UsageBasedMetadataManager.cs:20-25]
[READ:.../AnalysisBasedMetadataManager.cs:15-40]

---

## 2. 模式 A：全量反射元数据（`--reflection-all` 驱动）

### 2.1 目标形态

`--reflection-all`（或消费既有 `--reflection-granularity all`）→ 转译器对**全部**
类型发射完整 TypeInfo 表（方法/字段/基类/接口；属性表待 PropertyDef 模型落地，
2m 第 5 点按需粒度可裁）。运行时零改动（一个运行时，三套编译策略）。

### 2.2 现状与目标差

发射本身已是"全类型"，故模式 A 主体 = **拆除三守卫 + CLI 接线 + 元数据深度**：

| # | 工作项 | 现状 | 目标 | 依赖 |
|---|---|---|---|---|
| A1 | CLI 消费接线 | `--reflection-granularity all`/`--reflection-list` 不被生成器消费 | `CodeGenerator.Generate` 接收 CliOptions；all→全量发射；members→成员级过滤（按需粒度）；manifest→限定范围（模式 B 一并修复） | D4 |
| A2 | 返回类型全名（G2 拆除） | MethodDef.ReturnType 仅 SignatureTypeCode；0x11/0x12/0x15 LOUD | MethodDef 携带返回类型**全名**（metadata 签名解码）；EncodeReturnType 全部可编码；TypeInfoReturnEncodable 白名单废除 | D1 |
| A3 | 折叠访问器 fnPtr（G1 拆除） | 折叠访问器宿主整类型排除 | 折叠访问器集合传入 EmitTypeInfo，对应方法 fnPtr=nullptr（表仍在，符号不引用） | D2 |
| A4 | 非法标识符（G3） | Sanitize 留 '=' → 整类型排除 | Sanitize 产出合法 C++ 标识符（'='→替代转写）；或保守保持排除并文档化 | D3 |
| A5 | md 传入 | `EmitTypeInfo(t)` 未传 md → 静态字段语义回退模型 | 传 md → 静态字段全名/引用位完整（TypeEmitter.StaticFieldSemantics 已有解码器，TypeEmitter.cs:597-647） | — |
| A6 | 体积/容量 | 全量发射在 BCL 级 MB 级；typeinfo 注册表 kMax=256 | 体积报告（设计 §4 成本对比）；注册表容量与 kMaxRegisteredClasses 对齐扩容 | D7 |

### 2.3 验收

- `--reflection-all` 下生成码含 `{T}_typeinfo` 表且编译零警告（三编译器门）
- 反射方法含 0x12 CLASS / 0x11 VALUETYPE / 0x15 GenericTypeInstance 返回的类型
  **不再被排除**（G2 拆除的直接判据）
- 折叠访问器类型有表（fnPtr=nullptr）不再链接失败（G1 拆除判据）
- `test_typeinfo.cpp` fixture 断言不变；reflectcase e2e 全绿
- CliTests 增生成断言（all 模式对全部类型生成表）

---

## 3. 模式 C：Type.GetType 字符串驱动（静态预扫描 + 名称→类型解析表）

### 3.1 目标形态

转译器 IL 预扫描收集：① `ldtoken T` + GetTypeFromHandle（typeof 常量）；②
`Type.GetType(ldstr "…")` 的字符串常量；③ nameof（编译期已折叠为常量）。命中类型
→ 生成 TypeInfo 表（默认模式）。运行时 `Type.GetType` 按名解析（含程序集限定名）。

### 3.2 现状与目标差

运行时查找已存在，但 (a) 无预扫描收集，(b) 无程序集限定名解析，(c) 无范围接线。

| # | 工作项 | 现状 | 目标 | 依赖 |
|---|---|---|---|---|
| C1 | 预扫描 pass（新文件，AccessorAnalyzer.cs:61 同构） | 无 typeofSet / 无 GetType 字面量收集 | 收集 `typeofSet` + `GetTypeSet` → 生成范围 `ReflectionScope` | D5 |
| C2 | 范围接线 | `EmitTypeInfo(t)` 无条件全量 | `EmitTypeInfo(t, reflectedNames: scope)`；scope = typeofSet ∪ GetTypeSet ∪（模式 B 清单 Kept 并集） | D2/D4 |
| C3 | 运行时程序集限定名解析 | 全串查注册表，"Ns.Type, Assembly" miss | `il2cpp_type_get_type` 先解析限定名：末层逗号分割剥 "， Assembly"、嵌套 '+' 转写 → 注册表按简单全名查找 | D6 |
| C4 | 未覆盖告警 | 无 | GetType 字面量命中未生成表类型 → 编译期告警（提示转模式 B 或 typeof，设计 §4 C 行） | D5 |
| C5 | members 粒度 | 无 | `--reflection-granularity members`：只发方法表子集（按需粒度，2m 第 5 点） | D4 |

### 3.3 收集点细节

- **typeof**：StackFold.cs:1816-1832 的 GetTypeFromHandle 折叠处是天然收集点——
  折叠时把 `TypeFullNameFromToken(md, ldtMarker)` 记入 `typeofSet`（同 pass，零额外遍历）。
- **GetType(ldstr)**：EmitCall 拦截 `System.Type::GetType`（MethodMap.Reflect.cs:71-75
  同一特判域），当实参是 `StringConstExpr` 时收集其值（`"Ns.Type, Assembly"` 形态，
  需与运行时解析器同一规则规范化——D6 与 C3 共享一个"限定名→简单名"规范化函数，
  转译器侧收集**简单名**，运行时侧解析**限定名**，两边同规约避免 D3 分歧）。

### 3.4 运行时解析器（C3）设计

- 位置：reflect_base.cpp `il2cpp_type_get_type` 的 AsciiFromManagedString 之后
  新增 `NormalizeTypeName`：末层顶层逗号分割（`"Ns.Type, Assembly"` → `"Ns.Type"`；
  处理 `"Ns.Nested+Nested"` → `"Ns.Nested.Nested"`；泛型 arity 后缀保留注册表 key
  原样）。corert 参考：System.Private.Reflection.Core TypeParsing（TypeLexer）。
- 兼容：净名字符串（无逗号）原样查——零回归。

### 3.5 验收

- reflectcase e2e 增补：`Type.GetType("System.Int32, mscorlib")` 命中、
  `Type.GetType("Cases.ReflectA")` 命中（typeof/GetType 预扫描纳入）、
  `Type.GetType("No.Such.Type")` → null
- 运行时 test_reflect_base 增限定名用例；CliTests 增预扫描范围断言
- 模式 B 回归：清单限定发射在接入 C2 后生效（CliTests 生成断言）

---

## 4. 模式 A/B/C 合并规则（设计 §4 的落地形态）

- **模式 C 默认开启**；模式 B 清单与 C 扫描结果**保守并集**（设计 §4 合并规则）；
  模式 A = 全集（`--reflection-all` 时 scope 取全类型）。
- 按需粒度：`members` 粒度下方法表静态可达 + 清单命中成员全留；属性表待
  PropertyDef 模型落地（设计 §4 第 5 点，先全留后细化）。
- 泛型：`typeof(List<>)` 泛型定义条目 / `typeof(List<int>)` 已实例化条目——
  现有 TypeDef 泛型实例化机制（MethodRefResolver.cs:853 泛型实参展开先例）对齐。

---

## 5. 阶段化实施顺序（供批次计划引用）

| 阶段 | 内容 | 依赖 | 出口判据 |
|---|---|---|---|
| **S1** | D1：MethodDef 返回类型全名（MetadataLoader + MethodSignatureCoder 解码） | — | unit 全绿；EncodeReturnType 0x11/0x12/0x15 不再 LOUD |
| **S2** | D2 + A3：折叠访问器集合传入 EmitTypeInfo，fnPtr=nullptr；D3 + A4：Sanitize 合法标识符 | S1 后可并行 | G1/G3 拆除；生成码零链接/编译错误 |
| **S3** | D4 + A1：CliOptions 传入 CodeGenerator；`--reflection-all` / granularity 消费；**模式 B 清单限定发射生效**（C2 前置） | S2 | CliTests 生成断言；模式 B 清单限定验证 |
| **S4** | D5 + C1 + C4：预扫描 pass（typeofSet + GetType 字面量）+ 告警；C2 范围接线（默认模式 C） | S3 | typeof/GetType 命中类型有表；未命中告警正确 |
| **S5** | D6 + C3：运行时程序集限定名解析（NormalizeTypeName）+ test_reflect_base 用例 | S4 | "Ns.Type, Assembly" 命中；净名零回归 |
| **S6** | A5 md 传入 + A6 体积报告/容量扩容；C5 members 粒度 | S5 | 全量发射零警告；体积报告产出；质量门（三编译器 + dotnet test + runtime C++） |

预估：S1-S3 ≈ 模式 A 主体（含模式 B 修复）；S4-S5 ≈ 模式 C 主体；S6 = 收尾质量门。

---

## 6. 依赖清单（实施前置条件）

| 依赖 | 内容 | 阻塞 |
|---|---|---|
| **D1** | MethodDef 携带返回类型**全名**（0x11/0x12/0x15 返回类型的 metadata 签名解码；先例：TypeEmitter.StaticFieldSemantics 字段签名解码 TypeEmitter.cs:597-647） | A2（G2 拆除）、模式 C 中 0x12 返回类型的表发射 |
| **D2** | 折叠访问器方法集合从 CodeGenerator 传入 EmitTypeInfo（签名扩展 `reflectedNames` → 增 `foldedAccessorNames`/方法 token 集） | A3（G1 拆除）、C2 范围接线（两处共用同签名演进） |
| **D3** | TypeEmitter.Sanitize 对 '=' 等非法字符产出合法 C++ 标识符 | A4（G3） |
| **D4** | `CodeGenerator.Generate` 接收 CliOptions（Program.cs:45 一行接线 + 签名） | A1、C2（范围驱动）、C5 |
| **D5** | 新预扫描 pass（`src/il2cppc/Opt/ReflectionScopePass.cs` 或 StackFold 收集 + CodeGenerator 汇总） | C1、C4 |
| **D6** | reflect_base.cpp `NormalizeTypeName`（程序集限定名 → 简单名） | C3 |
| **D7** | typeinfo.cpp 注册表容量扩容（kMaxRegisteredTypeInfos=256，对齐 kMaxRegisteredClasses） | A6（模式 A 全量体积） |
| D8 | PropertyDef 模型（属性表当前空，TypeEmitter.cs:794 注明） | 模式 A/C 属性反射完整性（可后置） |

---

## 7. 风险

| 风险 | 缓解 |
|---|---|
| 模式 A 元数据体积爆炸（BCL 级 MB） | L1 裁剪默认 + 体积报告（设计 §4）；紧凑编码（jit-预留.md 2.6）；members 粒度按需 |
| 程序集限定名解析引入回归 | NormalizeTypeName 净名原样路径零回归 + 双跑 net48 参考 |
| 预扫描漏 GetType 动态拼串（模式 C 盲区） | 编译期告警 + 提示转模式 B（铁律 2m 明确要求） |
| 折叠访问器 fnPtr 改动碰 AccessorAnalyzer/CodeGenerator（并行代理域） | 实施前复查 src mtime（memory.md 在途 watch 规则）；S2 独立成批 |
| 类型注册表/typeinfo 注册表容量不同步 | D7 一次扩容对齐 |

---

## 8. 一句话结论

TypeInfo 表发射已是"全类型"形态，但 CLI 未接线 + 三守卫（折叠访问器/不可编码返回/
非法标识符）阻止真·全量；Type.GetType 运行时原语已存在但缺静态预扫描、程序集限定名
解析与范围接线。模式 A = CLI 接线 + 拆除三守卫（最大依赖 D1 返回类型全名）；模式 C =
预扫描收集（typeof + GetType 字面量）+ 范围接线（同时修复模式 B 清单限定）+ 运行时
限定名解析；阶段 S1-S6 见 §5。
