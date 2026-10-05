# 字符串池化优化设计（三层池化：编译期去重 → 常量拼接折叠 → 静态字符串表）

> 依据：用户铁律——字符串必须有池化技术优化（性能铁律 2e：产物性能 >= 官方 JIT；
> 字符串在性能基准关键路径清单，REQUIREMENTS.md:116）。既有路线已挂号：
> 性能路线图"字符串缓存"行（docs/architecture/性能路线图.md:37）、jit-预留 #19
> （docs/architecture/jit-预留.md:41）、任务卡 T405（常量拼接折叠）与 T412
> （编译期字符串池，docs/plans/taskcards-128.md:547-549, 589-591）。本文档是这三处
> 的统一落地设计，任务卡名称与其对齐（C8 命名一致性）。
>
> 范围：**纯转译器 + 生成侧优化**；运行时零新增 API（静态表填充复用既有
> `il2cpp_string_intern_literal`）。不改 .NET 语义。

---

## 1. 现状（池化已有一半）

### 1.1 现有链路（每条均实测引用）

```
ldstr  →  StringConstExpr  →  (Il2CppObject*)il2cpp::il2cpp_string_intern_literal("...")
  ↑            ↑                      ↑
StackFold.cs:196-197   Expr.cs:28      CodeGenerator.cs:966-975
```

运行时 intern 池（stringintern.cpp）：

- 开放寻址哈希表，存于**托管引用数组**，表指针注册为 GC 静态根（stringintern.cpp:48-50）——
  **池内字符串永不回收（.NET intern 池语义，il2cpp_string_intern.h:19-20）**；
- 初始 64 槽、70% 负载因子翻倍、上限 2^20 槽、满则 loud abort（stringintern.cpp:42-43, 227-228）；
- 纯 ASCII 字面量**命中零分配**（字节级哈希+比较快路径，stringintern.cpp:297-316）；
  非 ASCII 走 create-first 再插入（stringintern.cpp:317）。

### 1.2 已有（池化的"运行时半边"）

| 能力 | 状态 | 证据 |
|---|---|---|
| 同内容字面量运行时同实例（.NET interned 语义） | ✅ 已有 | stringintern.cpp:280-318；CodeGenerator.cs:968-970 注释 |
| 命中零分配（ASCII 快路径） | ✅ 已有 | stringintern.cpp:297-316 |
| 运行时容量上限 + loud 溢出 | ✅ 已有 | stringintern.cpp:43, 169-171 |

### 1.3 缺失（池化的"编译期半边"，即本设计目标）

1. **转译器侧无全程序去重**：每个 ldstr 每次执行都是一次函数调用（内容哈希+探测）。
   同方法内重复字面量也无 temp 复用（P1-3 缺口，coreclr-jit-优化.md:118 已记录未做）——
   实例唯一性由运行时池保证，但查找代价每次执行都付。
2. **常量拼接不折叠**：`"a"+"b"` 是 `String.Concat` 调用 + 运行时 concat 分配，
   编译期完全可折叠（T405 卡片已挂号未落地）。
3. **无生成侧静态字符串表**：没有 .rodata / 静态初始化一次形态；每个字面量的构造
   都惰性发生在运行时（性能路线图.md:37 的"字符串缓存"行正是此设计，未落地；
   该行现状描述"每个 ldstr 运行时 new_from_cs 新分配"已**过时**——现状是
   intern_literal，本文档落地时同步修正该行）。
4. 非 ASCII 字面量被运行时转码器"逐字节跳过 ≥0x80"（transpiler-audit.md M2）——
   属编码域独立缺口，本设计不解决，仅注明静态表继承该行为。

---

## 2. 设计：三层池化

### 2.1 层 ① 转译器编译期去重（全程序，跨方法共享）

- 新 pass `PassStringPool.cs`（对齐 T412 卡名）：全程序扫描所有方法体的
  `StringConstExpr` 内容（序数字典 `Dictionary<string,int>`），
  **同内容字面量（跨方法/跨类型）→ 单表项 id**。
- `StringConstExpr` 折叠为 `StringPoolRefExpr { int Id }`（新 IR 节点，Expr.cs）。
  同方法内重复字面量天然获得单表项（顺带实现 P1-3 的方法内复用）。
- 语义不变式：.NET interned 语义由运行时池兜底（表项最终就是池实例）；
  编译期 id 只决定"发射哪个表槽"，不改变引用身份语义。

**新 IR 节点波及面（C9：所有 pattern-match 点必须同步）**：`Normalize.cs:426/461/473`
（常量类判定）、`StackFold.cs`（如命中则替换）、`CodeGenerator.EmitExpr`（发射）。
节点语义与 StringConstExpr 一致（纯内容、无副作用），Normalize 的常量相等判定
按 `Value` 比较即可。

### 2.2 层 ② 常量拼接折叠

- 新 pass `PassFoldString.cs`（对齐 T405 卡名）：`String.Concat(a, b, ...)`
  且**全部参数为字符串常量** → 折叠为单个 `StringConstExpr`（`"a"+"b"` → `"ab"`）。
- 折叠安全：纯内容拼接、无副作用、无求值顺序问题（常量求值顺序无观察点）。
- 边界：任一参数非常量 → 不折叠（落入既有 Concat 调用）；`Concat(object, object)`
  数值便利重载（如 `Concat(2, 3)`）非字符串常量形态 → 不折叠。
- **管道顺序：② 必须先于 ①**——折叠产出的新内容（"ab"）必须参与去重，
  才能与既有 "ab" 字面量合并。最终管道：`② 折叠 → ① 去重 → ③ 发射`。

### 2.3 层 ③ 生成侧静态字符串表

- 生成代码新增（generated.cpp / generated.h）：
  `extern Il2CppObject* g_strPool[N];` + `il2cpp_string_pool_fill()`。
- **填充**：启动时一次性（runtime bootstrap 尾部）对每个去重后字面量调
  `il2cpp_string_intern_literal(utf8)` → 表项 = **池实例**（引用身份与
  `String.Intern` 完全一致——ldstr 结果 `==` 动态 Intern 结果仍成立）。
- **ldstr 发射** → `g_strPool[id]`：单次 load，零调用、零分支、零查找。
- **GC 交互（关键简化）**：表项已由 intern 池强引用（池是 GC 根）→
  **表本身无需注册 GC 根**。若表内"直接新建"（绕过池）则必须另注册根——
  双重机制，弃（KISS；且会破坏与 String.Intern 的身份一致性）。
- 急切填充 vs 惰性填充：选**急切**（启动一次，N = 编译期去重后字面量数，
  有限且可预估）；惰性（每槽 null 检查）会让每个 ldstr 多一个分支，弃。
  启动时单线程（bootstrap），无竞态。
- 与运行时池共存：动态字符串 `String.Intern` 仍走 `il2cpp_string_intern`
  （MethodMap.StringIntern 已接线）；池容量策略（2^20 + loud abort）不变。

---

## 3. 权衡

| 维度 | 决策 | 理由 |
|---|---|---|
| 静态表大小上限 | N = 编译期去重后 distinct 数，**编译期已知**，无运行时增长 | 去重后体积最小（Σ distinct 内容）；无需上限策略 |
| 运行时池上限 | 维持 2^20 槽 + loud abort（stringintern.cpp:43, 169-171） | 动态 Intern 语义不变；已有契约不动 |
| 长字符串不池化阈值 | 默认 1KB（编译期可配置）；超阈字面量**不进静态表**，仍走运行时 `intern_literal` 惰性路径 | ① 启动成本不随嵌入大块（base64/资源串）膨胀；② 大块字面量多为冷路径，实际执行才构造；③ .NET intern 语义不受影响（实例唯一性仍由池保证，任何长度都池化） |
| GC 交互 | interned 字符串永不回收（.NET 语义，il2cpp_string_intern.h:19-20）；静态表强引用 = 池引用，内存 = 去重后最小值 | 语义正确性优先；"非常量缓存可回收"（StringBuilder 结果等）不属本域，另行设计 |
| 启动成本 | N 次构造（ASCII 快路径；去重后仅首触真实分配） | 换取运行时每个 ldstr 从"哈希探测调用"降为"单次 load" |
| 代码体积 | 每 ldstr 函数调用 → 8 字节索引 load；填充函数 O(N) | 净减（去重后每 distinct 字面量只保留一处） |
| 非 ASCII 字面量 | 表填充继承运行时转码行为（≥0x80 字节跳过，M2 缺口） | 编码域独立问题，不在本设计范围 |

---

## 4. 落地步骤（3 张任务卡）

依赖图：`T2 折叠 → T1 去重 → T3 发射`（T2 产出新常量须先于去重；T1/T2 文件互不
重叠可并行；T3 依赖两者产物）。每卡遵守：备份 → 改 → dotnet build 零错零警 →
dotnet test → e2e 双跑。不 git commit（项目约束）。

### T1 编译期去重 pass（40 分钟）

- **文件域**：[写] `src/il2cppc/Ir/PassStringPool.cs`（本卡独占）；[改] `src/il2cppc/Ir/Expr.cs`
  （新增 `StringPoolRefExpr { int Id }`）；[改] `src/il2cppc/Ir/Normalize.cs`
  （常量类判定 426/461/473 同步）；[改] `src/il2cppc/Emit/CodeGenerator.cs`
  （Generate 预扫描注册 + EmitExpr 发射表索引）；[写] `tests/unit/PassStringPoolTests.cs`。
  参考形态：AccessorAnalyzer 的预扫描 pass 风格（属性折叠设计.md §4.2）。
- **验证**：① 单测：同内容跨方法→同 id；异内容→异 id；空串；含转义字符内容；
  ② `dotnet build` 0 错 0 警 + `dotnet test` 全绿（既有 205 不回归）；
  ③ 生成代码 grep 断言：每个 distinct 字面量内容**只出现一次**。

### T2 常量拼接折叠（50 分钟，对齐既有卡 T405）

- **文件域**：[写] `src/il2cppc/Ir/PassFoldString.cs`（本卡独占）；[改]
  `src/il2cppc/Emit/CodeGenerator.cs`（管道接线：折叠 pass 在去重 pass 之前）；
  [写] `tests/unit/PassFoldStringTests.cs`；[写] `tests/cases/foldstr/`（e2e fixture）。
- **验证**：① 单测：2/3/4 参数全常量→折叠；混合非常量→不折叠；空串参与；Unicode
  内容；② `dotnet build` + `dotnet test` 全绿；③ e2e 双跑（net48 参考 vs 转译产物
  逐字节一致）。

### T3 静态表发射（50 分钟，对齐既有卡 T412）

- **文件域**：[改] `src/il2cppc/Emit/CodeGenerator.cs`（发射 `g_strPool[N]` 声明/定义
  + `il2cpp_string_pool_fill()` + `StringPoolRefExpr` → `g_strPool[id]`；
  **注意并行代理在途，编辑前查 mtime**）；[改] `src/il2cppc/Emit/CppWriter.cs`
  （生成文件结构，如需）；运行时**零改动**（fill 复用 `il2cpp_string_intern_literal`）；
  [写] `tests/unit/CodeGeneratorTests` 扩展（表形状：N、id 单调、转义）；[写]
  `tests/runtime/test_stringpool.cpp`（fill 后：同内容引用相等、与 `String.Intern`
  一致、阈值以上字面量不在表中）；e2e 回归：`tests/cases/stringcase/`、`interncase2/`
  只读双跑 + gcstress。
- **验证**：① 单测全绿；② 三编译器（MSVC/MinGW/WSL GCC 7.5）零警告；
  ③ e2e 双跑逐字节一致；④ 反汇编抽查（性能路线图阶段 4 体系）：ldstr 点附近
  **无 call**，仅有 `mov` 取表。

---

## 5. 与既有文档对齐

| 既有文档 | 关系 |
|---|---|
| 性能路线图.md:37（字符串缓存行） | 本文档即其落地设计；该行现状描述同步修正（现状是 intern_literal，非 new_from_cs） |
| jit-预留.md #19（字符串池 interning） | 编译期字符串池 = 本设计 T1+T3；JIT 阶段复用同一张表（运行时侧共享） |
| taskcards-128.md T405 / T412 | 卡名与文件域以本文档为准（T405→T2，T412→T1+T3）；两卡实施时按本文档拆分/合并 |
| generated-code-audit.md §3.1（字符串 4/5） | 剩余缺口（每次调用重新 intern 查找）由 T3 关闭 |
| REQUIREMENTS.md 铁律 2e / 2f-2 | 零开销抽象：ldstr 从"每次哈希探测"降为"索引 load"，符合"编译期定死" |
