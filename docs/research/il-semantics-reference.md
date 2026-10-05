# IL 语义参考研究：ILSpy / IL2Spy 借鉴记录

日期：2026-08-02
状态：持续扩充（每次实现 IL 相关功能前先查阅本文件，实现后补充决策）
铁律依据：REQUIREMENTS.md 铁律 2f —— 转译 IL 之前必须透彻理解 ECMA-335 语义；ILSpy/IL2Spy 是官方学习参考，借鉴点必须记录。

## 1. 参考实现与用途

| 项目 | 定位 | 我们的借鉴 |
|---|---|---|
| icsharpcode/ILSpy | .NET 反编译器（IL→C# 恢复），ILReader/StackAnalysis/控制流重构经过二十年验证 | 指令语义理解、栈机折叠、异常处理恢复、表达式树构建 |
| ilyafomenko/IL2Spy | IL→C# 静态转译器，无运行时依赖的纯转译管线 | 转译流程（读取→解码→AST→文本）、类型系统建模、方法体独立转译 |

**关系定位**：ILSpy 把 IL"反编译"回 C#（保留可读性）；我们把 IL"转译"成 C++（保留语义 + 极致性能）。共同核心 = 正确理解 IL 指令的 ECMA-335 语义。我们的复杂度更高：输出目标是 C++ 静态类型系统 + GC 语义 + AOT 性能，无 CLR 运行时兜底。

## 2. 指令语义核心模型（ECMA-335 III 章，我们实现所依据的）

### 2.1 栈机模型
- 每条指令定义：操作数栈效果（弹出 N 入栈 M）+ 类型约束。
- 我们（M1-M2 已完成/进行中）：`IlDecoder`（字节解码，含 0xFE 双字节前缀、分支相对偏移换算）→ `StackFold`（指令流 → IR 语句序列，栈表达式折叠 + 控制流 join 归一化）。
- ILSpy 对应：ILReader（指令解码）+ ILStackAnalysis（栈效果跟踪）。
- **借鉴点记录**：ILSpy 对 `dup`/`pop`/`stloc` 后惰性引用失效的处理（我们 StackFold.EmitStore 的 temp 快照即为此）；对分支目标 join 点栈高度归一化（我们 joinRestore 机制同源）。

### 2.2 值类型 vs 引用类型
- ELEMENT_TYPE_VALUETYPE(0x11) vs CLASS(0x12) vs OBJECT(0x1C) vs STRING(0x0E)——签名解码必须保留原始字节（BlobReader.ReadSignatureTypeCode 会重映射 0x11/0x12 → 0x40，丢失区分）。
- 我们（M2）：`MethodSignatureCoder` 原始字节解码 + `MetadataLoader.DecodeFieldSignature` 字段分类（Int32/Reference）；`MethodRefResolver` 参数/返回引用标志（r: 前缀）。
- IL2Spy 借鉴：类型解码为独立组件、全名规范化（TypeReference/TypeDefinition 统一解析为 FullName）——我们 `ResolveTypeName`/`GetFullName` 同款。

### 2.3 控制流
- 基本块划分 → 分支/join 图。我们 M1-M2 用 goto + 标签 + join 临时槽（ECMA 语义正确，性能无损——优化器消除 goto）；ILSpy 恢复结构化控制流（if/while/for）供人类阅读，**我们暂不恢复结构化**（goto 生成对 AOT 性能无影响且保语义简单），M3+ 若需可读性再议。
- **注意点**：Leave 指令 = 异常处理块退出（M3 异常体系实现时参考 ILSpy ExceptionHandler 恢复）。

### 2.4 隐式转换与常量
- ldc.i4 回绕（int32 模 2^32）、Conv 指令家族语义、分支比较的无符号/有符号区分（Bge_un ≠ Bge）。
- 我们（M1 完成）：TryFoldInt32 生成期常量折叠（含 Wrap32 回绕）、CgtUn/CltUn 显式无符号、MapBranchCompare 中 Bge≡Not(Clt) 的推导。
- **借鉴点**：ILSpy 对编译期可定值表达式（const）的折叠策略——我们更激进（全部折叠，因为 C++ 侧无 JIT 兜底，常量在生成期定死即性能）。

## 2.5 表达式正确性 = 性能前提（用户铁律 2f-2）
- **为什么理解语义**：IL 指令序列必须被正确翻译为 C++ 表达式——运算符优先级/结合律、括号位置、求值顺序、类型转换，一处错误即产生错误代码。
- **括号策略（我们 CodeGenerator 的既有规则）**：
  - BinaryExpr/UnaryExpr 发射必带括号（`(l + r)`），组合树天然正确；
  - 常量折叠结果（裸 `-2147483648LL`）无括号可剥——EmitCondition 仅在首尾都是括号时剥除，防止 `if((x == 4LL))` 触发 -Wparentheses-equality；
  - 分支比较折叠：Bge ≡ !(Clt)，Ble ≡ !(Cgt)，Bne_un ≡ !(Ceq)——Not 复用 Brfalse 的 Ceq(e,0) 助手。
- **性能路径 = 大表达式树**：ldc/ldarg/ldloc/算术/比较/字段读 → 折叠成单一 C++ 表达式，交 -O2 做寄存器分配/常量传播/公共子表达式消除/内联。逐指令落临时变量的伪栈模拟生成物性能不可接受（纯栈上操作不可能获得高性能输出）。
- **临时变量的唯一合法来源**：call 结果（值语义强制）、join 槽（控制流汇合）、stloc/starg 快照（惰性引用失效）——除此以外不得引入。

## 3. 关键差异决策（我们 vs ILSpy/IL2Spy）

| 维度 | ILSpy/IL2Spy | 我们 | 理由 |
|---|---|---|---|
| 输出目标 | C#（有 CLR 兜底） | C++ + 自研运行时（无兜底） | AOT 原生性能 |
| 类型表示 | C# 类型系统 | 生成 struct + Il2CppClass 描述符 + 裸 GC 指针 | 静态布局 + 精确 GC 位图 |
| 异常 | 恢复 try/catch 块 | 自研异常体系（M3） | .NET 语义 + C++ 异常桥 |
| 控制流 | 结构化恢复 | goto + join 槽（优化器消除） | 语义保真 + 性能 |
| 动态性 | 反编译单程序集 | 全程序 AOT 闭合 | 无 JIT |
| 性能 | 不关注 | 首要约束（铁律 2e） | 生成代码 >= 官方 JIT |

## 4. 实施检查清单（写 IL 相关代码前过一遍）

- [ ] 该指令的 ECMA-335 语义（栈效果/类型/异常）在本文档或设计文档有记录
- [ ] 类型分类（值/引用）来自原始签名字节，不经 SignatureTypeCode 重映射
- [ ] 常量运算按 .NET 回绕语义在生成期折叠
- [ ] 分支比较显式区分有符号/无符号
- [ ] join 点栈高度归一化；dup 惰性引用在 stloc 后失效
- [ ] 新增指令/决策追加到本文档（借鉴点或差异）

---

# 追加：八族指令语义精化（2026-08-03）

> 依据：ECMA-335 III 章 + net48（.NET Framework 4.8，版本 4.0.30319.42000）实测。
> 实证方法：tests/research/ 下 8 个探针程序（C# 7.3，编译产物 IL 即对应指令族），
> `dotnet build -c Release -o out` + 运行，输出捕获于 tests/research/out/<probe>.out.txt。
> 铁律：所有转换/比较/移位输入走**变量**（Roslyn 常量折叠与 JIT 运行时语义存在分叉，
> 且 checked 常量溢出是 CS0221 编译错误）；模糊语义一律以 net48 实测为权威。
> 探针清单：convprobe（转换 51 行）/ cmpprobe（比较 27 行）/ bitprobe（位运算 25 行）/
> arrprobe（数组 23 行）/ fieldprobe（字段 12 行）/ boxprobe（装箱 22 行）/
> swprobe（控制流 38 行）/ exprobe（异常 32 行），全部 EXIT=0。

## 6. 转换族（conv.*）

### 6.1 指令与栈效果（ECMA-335 III.3.27 conv. / III.3.28 conv.ovf. / III.3.29 conv.ovf..un）

| 指令（操作码） | 输入类型 | 输出类型 | 说明 |
|---|---|---|---|
| conv.i (D3) / conv.u (E0) | native int 或 int32/int64 | native int | 原生宽度转换（x64 = int64） |
| conv.i1 (67) / conv.i2 (68) / conv.i4 (69) / conv.i8 (6A) | 任意数值 | 有符号整数 | 窄化截断 / 加宽符号扩展 |
| conv.u1 (D2) / conv.u2 (D1) / conv.u4 (6B) / conv.u8 (6C) | 任意数值 | 无符号整数 | 窄化截断 / 加宽零扩展 |
| conv.r4 (6D) / conv.r8 (6E) | 任意数值 | float/double | 舍入（round-to-nearest-even） |
| conv.r.un (76) | 无符号整型 | float/double | uint→float 专用 |
| conv.ovf.i1 (B3)/i2 (B5)/i4 (B7)/i8 (B9)/u1 (B4)/u2 (B6)/u4 (B8)/u8 (BA)/i (D4)/u (D5) | 任意数值 | 有符号/无符号整数 | checked：超范围抛 OverflowException |
| conv.ovf.i1.un (82)/i2.un (83)/i4.un (84)/i8.un (85)/u1.un (86)/u2.un (87)/u4.un (88)/u8.un (89)/i.un (8A)/u.un (8B) | **无符号**整型/浮点 | 整数 | checked 无符号源（.un 后缀 = 无符号输入） |

栈效果：弹出 1 个值 → 压入 1 个转换结果。所有 conv.* 无操作数。

### 6.2 精确语义（net48 实证，tests/research/out/convprobe.out.txt）

- **窄化截断取低 N 位**（无符号溢出检查）：conv.i1 (sbyte)128 = -128；conv.i2 (short)65535 = -1；
  conv.u1 (byte)-1 = 255；conv.u2 (ushort)-1 = 65535；conv.u4 (uint)-1 = 4294967295
  （第 3-7 行）。
- **加宽**：conv.i8 (long)-1 = -1（int32 源符号扩展）；conv.i8 (long)uint.MaxValue = 4294967295
  （uint 源**零扩展**——按源类型而非按位模式解释，第 8-9 行）。
- **int64→int32 回绕**（conv.i4 取低 32 位）：unchecked((int)4294967297L) = 1；
  unchecked((int)-4294967297L) = -1（第 11-12 行）。
- **浮点→整型截断向零**：conv.i4 (int)2.9 = 2、(int)-2.9 = -2（第 18-19 行）。
- **整型→浮点精度损失**：conv.r8 (double)9007199254740993L（2^53+1）= 9.00719925474099E+15
  （= 2^53，舍入）；conv.r4 (double)(float)16777217 = 16777216（2^24 舍入）；
  conv.r4 (double)(float)0.1 = 0.100000001490116（第 14/16/17 行）。
- **conv.r.un 精确**：(double)(uint)uint.MaxValue = 4294967295（2^32-1 < 2^53，double 精确表示，
  第 22 行）。
- **conv.u8 浮点源位模式**：unchecked((ulong)(double)-1.0) = 18446744073709551615（第 20 行，
  cvttsd2si 位模式；conv 域既有实证同源）。
- **conv.i（native int）**：x64 上 = int64；实证 IntPtr.Size = 8、(int)(IntPtr)(-1) = -1（第 2/10 行）。
- **checked（conv.ovf.*）**：范围检查而非截断——ovf.i4：2147483647L 合法、2147483648L →
  OverflowException、-2147483648L 合法、-2147483649L → OverflowException（第 23-26 行）；
  浮点源同规则：ovf.i4 checked((int)2147483648.0) → OverflowException，边界 ±2^31 合法（第 27-30 行）；
  ovf.i8 checked((long)2^63) → OverflowException、NaN/±Inf → OverflowException、2^63-1024 合法
  = 9223372036854774784（第 31-34 行）。
- **.un 变体（无符号源）**：ovf.i8.un checked((long)9223372036854775807UL) 合法、
  checked((long)ulong.MaxValue) → OverflowException；ovf.u1.un checked((byte)255u) 合法、
  checked((byte)256u) → OverflowException、checked((byte)(uint)-1) → OverflowException；
  ovf.i2.un 32767 合法 / 32768 抛；ovf.u2.un 65535 合法 / 65536 抛（第 38-46 行）。
- **unchecked 浮点→整型溢出 = 整型不定值**（cvttsd2si 语义，非抛出）：
  unchecked((int)2147483648.0) = -2147483648、NaN = -2147483648、+Inf = -2147483648、
  unchecked((long)2^63) = -9223372036854775808（第 48-51 行，与 conv 域既有实证一致）。

### 6.3 本项目转译器当前处理

- 解码：IlDecoder.cs:198-231 全部 conv.* 无操作数解码 ✓。
- 折叠：StackFold.cs:298-302 仅 conv.i4/conv.i 视为 int32 恒等 no-op（M1 int32 契约内正确）；
  **其余全部 conv.\*（i8/u1..u8/r4/r8/r.un/ovf.\*）→ StackFold.cs:446-447 default
  loud 拒绝**（"fold: opcode ... not supported in M3"）。
- 常量路径：Ldc_i8 超 int32 范围 loud 拒绝（StackFold.cs:104-112 audit C3）；范围内 i8 常量按
  int32 存。int64/浮点值类型不在 M 阶段 ABI（conv 域的 il2cpp_conv.h/conv.cpp 运行时原语
  已实现但转译器未接线——MethodRefResolver 无映射）。
- 转译器常量折叠（TryFoldInt32）无双路径隐患：折叠只作用于 int32 常量算术，conv 指令
  本身不参与折叠（conv.i4/i 是 no-op）。

## 7. 比较族（ceq/cgt/clt/cgt.un/clt.un）

### 7.1 指令与栈效果（ECMA-335 III.3.21 ceq / III.3.22 cgt / III.3.23 cgt.un / III.3.25 clt / III.3.26 clt.un）

| 指令（操作码） | 语义 |
|---|---|
| ceq (FE01) | 相等（IEEE 有序：NaN≠NaN） |
| cgt (FE02) | 大于（有符号/有序） |
| cgt.un (FE03) | 无符号大于；**浮点 = "unordered or greater"（NaN 参与 → 真）** |
| clt (FE04) | 小于（有符号/有序） |
| clt.un (FE05) | 无符号小于；**浮点 = "unordered or less"（NaN 参与 → 真）** |

栈效果：弹出右操作数 → 弹出左操作数 → 压入 int32 0/1（**结果恒为 0 或 1**，非任意非零）。

### 7.2 类型约束与行为（net48 实证，tests/research/out/cmpprobe.out.txt）

- **int32 有符号**：cgt(-1,1) = 0、clt(-1,1) = 1（第 4-5 行）。
- **int32 无符号**：cgt.un(0xFFFFFFFF,1) = 1，对照 cgt(0xFFFFFFFF,1) = 0（第 6-7 行）；
  clt.un(0, 0xFFFFFFFF) = 1、clt.un(0xFFFFFFFF,1) = 0（第 8-9 行）。
- **int64 同构**：ceq(long.MaxValue,long.MaxValue) = 1、cgt(1L,-1L) = 1、
  cgt.un(0xFFFFFFFFFFFFFFFF,1L) = 1 vs cgt = 0（第 10-14 行）。
- **浮点 NaN 语义**：cgt(NaN,0) = 0、clt(NaN,0) = 0、ceq(NaN,NaN) = 0（IEEE 有序全假，
  第 15-17 行）；cgt(2.0,1.0) = 1、clt(1.0,2.0) = 1（第 18-19 行）。
  ⚠ **cgt.un/clt.un 对浮点 NaN → 真**（unordered 语义）C# 编译器不发射该形态，
  属 ECMA-only 知识——net48 侧最近可达证据是 `NaN != 0.0` = 1（ceq+neg，第 20 行）。
- **引用比较 = 指针恒等（非内容）**：内容相同实例不同的两个 string → 0；同实例 → 1；
  ceq(ref,null) = 0、ceq(null,null) = 1（第 21-25 行）。
- **C# 编译产物形态**：`x == null` → ceq(ref,null)；`x != null` → **cgt.un(ref,null)**
  （第 23 行实证 = 1）。引用上的 cgt/clt 未定义（ECMA 仅允许 ceq/cgt.un/clt.un）。

### 7.3 本项目转译器当前处理

- 折叠：StackFold.cs:214-218 + MapBinary（StackFold.cs:1246-1264）→ BinaryOp.Ceq/Cgt/Clt/
  CgtUn/CltUn；分支族 MapBranchCompare（StackFold.cs:1268-1291，Bge≡Not(Clt) 等）。
- 发射：CodeGenerator.cs:750-753 CgtUn/CltUn 显式 `(uint32_t)` 双 cast（audit H1 修复）；
  CodeGenerator.cs:721-743 引用与 null 比较特判（含 cgt.un(ref,null) → != nullptr）。
- 常量折叠：CodeGenerator.cs:1229-1233（TryFoldInt32，含 (uint) 无符号比较）。
- 限制：浮点常量/比较不在 M 阶段支持面（StackFold 无 Ldc_r4/r8 case → default 拒绝）；
  结果 int32 0/1 语义与 IR 一致。

## 8. 位运算族（and/or/xor/shl/shr/shr.un）

### 8.1 指令与栈效果（ECMA-335 III.3.3 and / III.3.53 or / III.3.67 xor / III.3.58 shl / III.3.59 shr / III.3.60 shr.un）

| 指令（操作码） | 语义 |
|---|---|
| and (5F) / or (60) / xor (61) | 逐位与/或/异或 |
| shl (62) | 左移：低位移 0，**溢出丢弃（回绕）** |
| shr (63) | 算术右移：**有符号，负号扩展（补 1）** |
| shr.un (64) | 逻辑右移：**无符号，高位补 0** |

栈效果：弹出右操作数（移位计数）→ 弹出左操作数 → 压入结果。

### 8.2 精确语义（net48 实证，tests/research/out/bitprobe.out.txt）

- **移位计数掩码**：int32 计数掩 &31（5 位）、int64 掩 &63（6 位）——1<<33 = 2（33&31=1）、
  1<<-1 = -2147483648（-1&31=31）、1<<-33 = -2147483648、1L<<65 = 2（65&63=1，第 14-16/19 行）。
  负计数不抛异常，按掩码后计数执行。
- **shl 回绕**：1<<31 = -2147483648；3L<<62 = -4611686018427387904（第 7/23 行）。
- **shr 算术**：0x80000000>>1 = -1073741824（0xC0000000 补 1）、0x80000001>>1 = -1073741824、
  -1>>5 = -1、long.MinValue>>63 = -1（第 9-11/20 行）。
- **shr.un 逻辑**：(uint)0x80000000>>1 = 1073741824、(uint)-1>>8 = 16777215、
  (ulong)long.MinValue>>63 = 1、(ulong)-1>>62 = 3、(ulong)-1>>4 = 1152921504606846975
  （第 12-13/21-22/24 行）。
- **and/or/xor**：与 C++ 逐位一致（第 1-6 行；-1 & 0xFF = 255）。

### 8.3 本项目转译器当前处理

- 折叠：StackFold.cs:209-213 + MapBinary（1246-1264）。**BinaryOp 枚举无 ShrUn**
  （Expr.cs:3）——shr.un 是显式缺口。
- 解码：IlDecoder.cs 无 Shr_un case（0x64）→ 解码即 loud 拒绝（IlDecoder.cs:234-236）。
- 常量折叠：CodeGenerator.cs:1227-1228 掩码 `r & 31` ✓（int32 语义正确）。
- 发射：OpSymbol ">>"（CodeGenerator.cs:1461），C++ 对有符号 int 为算术右移（shr ✓）。
  ⚠ **发射路径无显式计数掩码**：常量折叠路径已掩码，但变量计数的发射是裸 `l >> r`——
  x86 硬件 SHL 自动掩码 5 位与 .NET 一致，但 C++ 标准对计数 ≥ 宽度是 UB 且
  ARM 上饱和不同——若未来支持变量移位计数需在发射处补 `& 31`（64 位 `& 63`）。

## 9. 数组指令族（newarr/ldelem.\*/stelem.\*/ldlen/ldelema）

### 9.1 指令与栈效果（ECMA-335 III.4 Object model instructions：III.4.7 ldelem / III.4.8 ldelem.\<type\> / III.4.9 ldelema / III.4.12 ldlen / III.4.20 newarr / 同组 stelem.\*）

| 指令（操作码） | 栈效果 | 说明 |
|---|---|---|
| newarr (8D) | [count] → [array] | 单维数组分配；元素类型 = typeTok |
| ldlen (8E) | [array] → [length int32] | 长度（int32！） |
| ldelema (8F) | [array, index] → [managed ptr] | 元素地址 |
| ldelem.i1(90)/u1(91)/i2(92)/u2(93)/i4(94)/u4(95)/i8(96)/i(97)/r4(98)/r8(99)/ref(9A) | [array, index] → [value] | 元素读 |
| stelem.i(9B)/i1(9C)/i2(9D)/i4(9E)/i8(9F)/r4(A0)/r8(A1)/ref(A2) | [array, index, value] → [] | 元素写 |

### 9.2 精确语义（net48 实证，tests/research/out/arrprobe.out.txt）

- **newarr 尺寸检查**：new int[0].Length = 0、new int[5].Length = 5（第 1-2 行）；
  **负尺寸 → OverflowException**（第 3 行，new int[-1] 走变量——C# 常量 -1 是 CS0248
  编译期拒绝）；**超大尺寸（int.MaxValue 元素 × 4B = 8GB）→ OutOfMemoryException**
  （第 4 行——ECMA 写"太大 → OverflowException"，net48 实现给 OOM，按实测记录）。
- **ldlen**：正常返回长度；**null 数组 → NullReferenceException**（第 5-6 行）。
- **ldelem/stelem 边界**：索引 == Length 或负索引 → **IndexOutOfRangeException**
  （第 8-11 行）；界内正常读写（第 7/12 行）。
- **stelem.ref 协变检查**：string[] → object[] 协变合法（第 13 行）；经 object[] 写
  不兼容元素（int）→ **ArrayTypeMismatchException**（第 14 行）；object[] 写 int 合法
  （自动装箱，第 15 行）。
- **ldelema**：ref 元素写生效（第 16 行）；越界 → IndexOutOfRangeException（第 17 行）。
- **值类型数组**：元素为值拷贝（第 18-19 行：拷贝修改不影响数组）；ldelema 值类型
  元素可原地写（第 20 行 d[1].Year = 1999）。
- **多维数组不走 newarr**：newobj + stelem.rank.\*（第 21-23 行：Rank=2、GetLength(0)=2、
  m[1,2]=7）——C# 编译产物形态，转译器需注意。

### 9.3 本项目转译器当前处理

- 解码：IlDecoder.cs:63-82 全系 ldelem/stelem 解码 ✓；**Ldelema（0x8F）无 case → 解码即
  拒绝**（IlDecoder.cs:234-236）。
- 折叠：StackFold.cs:392-400 newarr、401-404 ldlen、405-424 ldelem（仅折叠
  i4/ref/u1/i1/u2/i2——函数名编码 OpCode，audit C4 修复）、425-445 stelem（仅 i4/ref/i1/i2）；
  其余 ldelem/stelem 变体 → StackFold.cs:446-447 loud 拒绝。
- 发射：CodeGenerator.cs:1376-1413（newarr/ldlen/ldelem/stelem helpers）；
  ArrayElementType（CodeGenerator.cs:1443-1456）：Int32→(4,false)，
  String/Object/用户引用类→(8,true)，其余（byte/double/值类型）loud 拒绝（audit C4）。
- 边界检查由运行时 il2cpp_array_get_elem_ptr 承担（越界 → IndexOutOfRangeException）。
- 缺口：ldelem.u4/i8/r4/r8/i、stelem.i/i8/r4/r8、ldelema、多维数组均不在 M 阶段支持面。

## 10. 字段族（ldfld/stfld/ldsfld/stsfld/ldflda + volatile 前缀）

### 10.1 指令与栈效果（ECMA-335 III.4 对象模型指令：III.4.10 ldfld / III.4.11 ldflda / III.4.14 ldsfld / III.4.15 ldsflda / stfld·stsfld 同组；volatile. 前缀 = III.2.6）

| 指令（操作码） | 栈效果 |
|---|---|
| ldfld (7B) | [obj] → [value] |
| ldflda (7C) | [obj] → [managed ptr] |
| stfld (7D) | [obj, value] → [] |
| ldsfld (7E) | [] → [value] |
| ldsflda (7F) | [] → [managed ptr] |
| stsfld (80) | [value] → [] |
| volatile. (FE13 前缀) | 修饰其后的 ldfld/stfld/ldsfld/stsfld |

### 10.2 精确语义（net48 实证，tests/research/out/fieldprobe.out.txt）

- **null 接收者**：ldfld/stfld → **NullReferenceException**（第 7-8 行）。
- **静态字段与 cctor**：首次 ldsfld/stsfld 触发类型初始化器（cctor）运行一次（第 1-2 行：
  首访表达式求值时 cctor 已跑，StaticCtorTrace = "cctor-ran"、StaticCount = 7）。
- **静态字段经实例访问**：C# 7.3 编译期禁止（CS0176，字段探针注释）；IL 层面
  ldsfld/stsfld **  操作数只有字段 token 无接收者** → 不存在 null 检查、不可能 NRE
  （ECMA-335 III.4 对象模型指令组 ldsfld/stsfld）——与 ldfld 的接收者检查是结构性差异。
- **字段默认值**：int 0、引用 null（第 4-5 行）。
- **ldflda**：值类型局部字段取址（ref 写 pt.X = 100 生效，第 9 行）；派生实例经基类
  字段读取合法（第 12 行）。
- **volatile**：单线程往返值正确（第 10-11 行）。ECMA I.12.6.7：volatile 读 = acquire
  （后续读写不得前移）、volatile 写 = release（先前读写不得后移）——内存序语义，
  **单线程无可观测差异**，跨线程可见性靠硬件缓存一致性 + 内存屏障。C# volatile 字段
  编译产物 = 每条 ldfld/stfld 前加 volatile. 前缀。

### 10.3 本项目转译器当前处理

- 折叠：StackFold.cs:162-203（Ldsfld/Stsfld 带 EnsureCctor 守卫、Ldfld、Ldflda、Stfld）。
- 发射：CodeGenerator.cs:1155-1178 FieldAccess（接收者 il2cpp_null_check → NRE 语义 ✓）；
  FieldStorageName（CodeGenerator.cs:1314-1319）静态字段 C++ 存储名。
- 属性折叠：AccessorAnalyzer 已把纯 accessor 折成字段访问（plan.md 属性折叠条目）。
- **volatile 缺口**：IlDecoder.cs 无 volatile. 前缀 case（0xFE13）→ 解码即 loud 拒绝
  （IlDecoder.cs:234-236）——C# volatile 字段的程序当前无法转译（loud 拒绝而非静默
  去 volatile，正确——静默丢弃会破坏 acquire/release 语义）。

## 11. 装箱族（box/unbox/unbox.any/isinst/castclass）

### 11.1 指令与栈效果（ECMA-335 III.4 对象模型指令：III.4.1 box / III.4.3 castclass / III.4.6 isinst / unbox·unbox.any 同组）

| 指令（操作码） | 栈效果 |
|---|---|
| box (8C) | [valueType] → [object] |
| unbox (79) | [obj] → [managed ptr 指向盒内载荷] |
| unbox.any (A5) | [obj] → [valueType 值拷贝] |
| isinst (75) | [obj] → [obj 或 null]（**不抛**） |
| castclass (74) | [obj] → [obj]（失败抛） |

### 11.2 精确语义（net48 实证，tests/research/out/boxprobe.out.txt）

- **box = 值快照拷贝**：box 后改原值不影响盒内（第 1 行，S.X 1 → 盒内仍 1）。
- **盒无驻留**：同一值两次 box 是**不同对象**——引用比较 = 0、Equals = 1（第 2-3 行）；
  两次盒 0.1 同理（第 21-22 行，double 位模式相等但对象不同）；同引用 = 1（第 4 行）。
- **unbox.any**：匹配 → 值拷贝（第 7-8 行）；**null → NullReferenceException**
  （第 9 行）；**类型不匹配 → InvalidCastException**（第 10-11 行，(string)盒 int、
  (long)盒 int 均抛）。
- **isinst = 匹配返回原引用 / 不匹配返回 null（不抛）**：盒 int is int = 1、
  盒 int is string = 0、盒 long is long = 1 / is int = 0（精确类型 + 继承判定，
  第 5-6/12-13 行）；null is string = 0（第 14 行）；继承：B 实例 is A（基类）= 1、
  A 实例 is B = 0（第 15-17 行）。
- **castclass**：null 输入 → **null（不抛）**（第 18 行）；不匹配 → InvalidCastException
  （第 19 行）；匹配 → 原引用（第 20 行）。
- **unbox（非 any）**：返回指向盒内载荷的托管指针，配合 stobj 可原地改盒内值——C#
  编译器不发射（C# 只有 unbox.any），ECMA-only 知识。

### 11.3 本项目转译器当前处理

- 折叠：StackFold.cs:370-390（box/unbox.any/unbox/isinst/castclass 占位 CallExpr）。
- 发射：CodeGenerator.cs:1337-1375——box/unbox **仅 System.Int32**（audit H3：
  其他值类型 loud 拒绝，防静默截断）；isinst → il2cpp_is_instance（返回 obj/null，
  audit H4 修复）；castclass → isinst 判定 + InvalidCastException 链发射
  （CodeGenerator.cs:1371-1373）。
- 运行时既有：il2cpp_is_instance（typecheck 域 icall_type_is_instance 已就绪，
  交接建议在 plan.md typecheck 条目）；**数组 is object 缺口**（M4 规则只比 '[' 前缀，
  typecheck 域报告）。
- ⚠ unbox 发射直读载荷（il2cpp_unbox_int32 无 null/类型检查）——与 ECMA 的
  NRE/InvalidCastException 有差距，H3 拒绝面外不可达（仅 Int32 且来自已检查路径）；
  未来扩展时需补 unbox 的 null/类型守卫。

## 12. 控制流族（switch / leave / endfinally）

### 12.1 指令与栈效果（ECMA-335 III.3 基础指令：III.3.66 switch / III.3.46 leave / III.3.35 endfinally·endfault）

| 指令（操作码） | 语义 |
|---|---|
| switch (45) | 弹出 int32 索引 → 跳转表；表项 = 目标偏移；**越界/负值 → 顺序下一条** |
| leave (DD) / leave.s (DE) | 退出受保护块：**先执行 finally（若有）再跳转**；目标必须在块外 |
| endfinally (DC) | finally 处理器结束标记；与 finally 区域 1:1 配对 |
| endfault (FE1B) | fault 处理器结束标记 |

### 12.2 精确语义（net48 实证，tests/research/out/swprobe.out.txt）

- **switch 跳表**：连续 int 常量 → IL switch 跳转表——switch(0/1/2) 命中、switch(3)/switch(-1)/
  switch(100) → default（第 1-6 行）；goto case 合法（第 12 行）。
- **非跳表形态**：稀疏/负数 case（第 7-9 行）与 long switch（第 10-11 行，C# 7 起允许）
  编译为比较链（if 链/二分）而非 switch 指令——转译器需两种形态都支持。
- **leave 的 finally 确定性**：try 内 return → **finally 先执行**，返回值保留（第 13-15 行：
  "finally: 执行于 return/异常之前" 打印于返回 10 之前）；try 抛异常 → finally 在
  **传播前**执行（第 17-18/20-22 行）。
- **嵌套 finally LIFO**：inner 先于 outer（第 24-26 行：finally: inner → caught →
  finally: outer）。
- **循环内 break/continue**：每次退出 try 都执行 finally（第 28-37 行：break 时
  "finally iter 1"、continue 时 "finally iter 1" 均打印）。

### 12.3 本项目转译器当前处理

- **switch 缺口**：IlDecoder.cs 无 Switch case（0x45）→ 解码即 loud 拒绝
  （IlDecoder.cs:234-236）——C# 连续 case 的 switch 编译产物当前无法转译。
- leave：IlDecoder.cs:140-153（分支目标 = 偏移+5+operand）；StackFold.cs:303-337
  （audit H2）：leave 目标必须 == **最内层包含区域的 handler 结束偏移**，否则 loud 拒绝；
  "run finally then jump" 完整语义标注为未来里程碑。
- endfinally：StackFold.cs:341-344 no-op（M3 双路发射已处理 finally——正常路径在
  try 后、异常路径在 catch-all+rethrow）。
- 本项目 M3 异常形状：try 内联发射 + catch-all + rethrow（无结构化恢复，见 §2.3）。

## 13. 异常族（throw / rethrow / catch 过滤 / finally 确定性）

### 13.1 指令与语义（ECMA-335 III.4 对象模型指令：III.4.24 rethrow、throw 同组 + II.16.4 异常处理表）

| 指令（操作码） | 语义 |
|---|---|
| throw (7A) | 弹出异常对象 → 抛出；**null → NullReferenceException** |
| rethrow (FE1A) | 仅 catch 处理器内合法；重抛当前异常（**保留原堆栈**） |
| 区域种类 | catch / filter / finally / fault（异常处理表四类，MethodBodyReader 已读） |

### 13.2 精确语义（net48 实证，tests/research/out/exprobe.out.txt）

- **catch 匹配顺序**：按 handler 声明序，第一个类型匹配者执行（精确类型优先于基类）——
  InvalidOperationException 抛 → 精确 catch 执行；Exception 抛 → 基类 catch（第 1-3 行）。
- **throw; vs throw ex;**（重抛 vs 抛新引用）：throw;（→ IL rethrow）**保留原异常对象
  与堆栈**——StackTrace.Contains("InnerThrow") = 1（第 5 行）；throw ex;（→ IL throw）
  **堆栈被重置**（不含 InnerThrow，第 7 行）但消息保留（第 6/8 行）。
- **catch 内抛新异常**：InnerException 链保留原异常（第 9-11 行：
  ArgumentException + InnerException: InvalidOperationException）。
- **catch-when（filter）**：filter **先于** handler 求值（第 13-16 行顺序：
  try → filter → handler → finally）；filter 假 → 不进入 handler，原异常继续传播
  （第 17-18 行 outer-handler 接住）。
- ⚠ **filter 内抛异常 → net48 视为 filter=false，原异常继续传播**（第 19-21 行：
  filter 抛 ArgumentNullException，外层捕获 **Exception / original**——非异常替换！
  ECMA 对 filter 内异常定义"undefined behavior"，CLR 实测实现 = 当 filter 返回假。
  反直觉点：filter 抛出的异常**不会**成为传播异常）。
- **finally 确定性**：正常（第 23-24 行）、异常（第 25-26 行：try 抛 → finally 先执行 →
  传播）、return（第 27-28 行：finally 在返回 42 前执行）三路径全执行。
- **try/catch/finally 顺序**：try → catch → finally（第 30-32 行）。

### 13.3 本项目转译器当前处理

- throw：StackFold.cs:349-363（audit H8 类型化——newobj 类型跟踪，未知类型回退
  System.Exception；发射 throw_{Type}）；CodeGenerator.cs:1476-1493 ExceptionChain
  （"{Type},{Type},{Base},..." 链格式，runtime matcher 跳过首段）。
- **rethrow 缺口**：IlDecoder.cs 无 Rethrow case（0xFE1A）→ 解码即 loud 拒绝
  （IlDecoder.cs:234-236）。注意 M3 的"catch-all+rethrow"是 C++ 发射形状，
  不是 IL rethrow 指令——IL rethrow 转译（保留原堆栈语义）是缺口。
- **filter（catch-when）缺口**：MethodBodyReader.cs:88-91 filter/fault 区域 → 解码即
  NotSupportedException（"exception filter regions not supported in M3"）；
  catch/finally 区域正常解码（MethodBodyReader.cs:92-100）。
- catch 匹配：运行时 il2cpp_exception_matches 沿链行走（exceptionext 约定），与
  异常链格式配套（H8 设计）。

## 14. 追加清单与关键实证汇总

### 14.1 追加章节清单

| 章 | 内容 | 探针 |
|---|---|---|
| §6 | 转换族 conv.* 全系列（含 64 位、ovf 精确语义表） | convprobe |
| §7 | 比较族 ceq/cgt/clt/\_un（int32/int64/float/引用 + NaN） | cmpprobe |
| §8 | 位运算 and/or/xor/shl/shr/shr.un（移位掩码语义） | bitprobe |
| §9 | 数组 newarr/ldelem.\*/stelem.\*/ldlen/ldelema（边界+协变） | arrprobe |
| §10 | 字段族 ldfld/stfld/ldsfld/stsfld/ldflda（volatile 语义） | fieldprobe |
| §11 | 装箱 box/unbox/unbox.any/isinst/castclass（失败语义） | boxprobe |
| §12 | 控制流 switch 表 / leave 的 finally / endfinally 配对 | swprobe |
| §13 | 异常 throw 重抛 / catch 过滤 / finally 确定性 | exprobe |

### 14.2 反直觉语义（net48 实证发现，全部钉死）

1. **filter 内抛异常 = filter 视为假**，原异常继续传播（非异常替换）——ECMA 未定义，
   CLR 实现如此（exprobe 第 19-21 行）。
2. **newarr 负尺寸 → OverflowException，超大尺寸（8GB int[]）→ OutOfMemoryException**
   而非 ECMA 字面的 OverflowException（arrprobe 第 3-4 行）。
3. **cgt.un/clt.un 浮点 NaN → 真**（unordered 语义）；ceq/cgt/clt → 假——C# 编译器
   不发射浮点 .un 形态，ECMA-only（cmpprobe 第 15-20 行 + 文档说明）。
4. **conv.i8 加宽按源类型解释**：(long)uint.MaxValue = 4294967295（零扩展）——
   位模式相同但 int 源符号扩展、uint 源零扩展（convprobe 第 8-9 行）。
5. **unchecked 浮点→整型溢出 = 整型不定值（int.MinValue/long.MinValue）**，非抛异常
   （convprobe 第 48-51 行，cvttsd2si）。
6. **移位计数掩码 + 负计数合法**：1<<-1 = int.MinValue（-1&31=31）、1<<33 = 2
   （bitprobe 第 14-16 行）。
7. **throw; 保留堆栈 / throw ex; 重置堆栈**（消息均保留）——rethrow 语义的 C# 观测
   （exprobe 第 5-8 行）。
8. **盒无驻留**：同一值两次 box = 不同对象（boxprobe 第 2-3/21-22 行）。
9. **静态字段经 null 实例访问**：C# 7.3 CS0176 编译期禁止；IL 层无接收者、不可能 NRE
   （fieldprobe 注释）。
10. **值类型数组元素 = 值拷贝**；ldelema 才可原地写（arrprobe 第 18-20 行）。
11. **leave 目标 = handler 结束偏移**（非 tryEnd）——Roslyn 产物实证已在 audit H2 记录，
    本次 swprobe 验证其观测行为（finally 全路径确定性）。
12. **switch 越界/负值 → default（下一条）**；稀疏/long switch = 比较链非跳表
    （swprobe 第 1-11 行）。

### 14.3 转译器缺口清单（本文档定位，供后续里程碑）

- conv 族除 conv.i4/i 外全部未折叠（StackFold.cs:446-447）；int64/浮点 ABI 未接线。
- shr.un：解码即拒（IlDecoder.cs:234-236）+ BinaryOp 无 ShrUn（Expr.cs:3）。
- switch：解码即拒；变量移位计数发射无显式掩码（x86 恰好一致，ARM 有风险）。
- ldelema / 多维数组 / ldelem.u4·i8·r4·r8·i / stelem.i·i8·r4·r8：解码或折叠即拒。
- volatile. 前缀：解码即拒（C# volatile 程序无法转译）。
- unbox（非 any）：发射直读载荷，无 null/类型守卫（ECMA 要求 NRE/ICE）。
- rethrow（IL 指令）：解码即拒；catch-when filter 区域：MethodBodyReader.cs:88-91 即拒。
- 数组 is object（M4 '[' 前缀规则外）：typecheck 域交接项。

