# Decimal 域设计文档（System.Decimal）

> 定位：本文档是 ILC 运行时（src/runtime）新增 `System.Decimal` 基础域的**设计先行文档**
> ——只定义表示 / 架构 / icall 契约 / 接线改动，不实现。产出的任务卡（§5）可直接拆分为
> 3 个实现任务。
>
> 语义基线：**.NET Framework 4.8（net48）**——项目 BCL 对齐基线。算法基线：
> **dotnet/runtime 的 Decimal.cs / Decimal.DecCalc.cs**（reference/netcore 已拉取，
> 见 §3）——铁律 3：**抄 .NET 源码算法**，C++ 移植 DecCalc 的既有实现，不做自研
> 大数算法。
>
> 现状快照（2026-08-05 探路）：
> - transpiler 侧零 decimal 支持——`grep "System.Decimal" src/il2cppc/` 仅注释提及
>   （CodeGenerator.cs:1881 TypeCppName 映射表无条目；MethodMap.* 无 Decimal 文件）。
> - e2e 卡例：`tests/cases/fmtchain/fmtchain.cs:16` 显式注释
>   `decimal.ToString(format) -- no MethodMap entry for System.Decimal`（已排除出子集）。
> - runtime 侧已有 partial 痕迹：`marshalext2.cpp:75` 已识别 TypeCode 15 = 16 字节；
>   `typecheck.cpp:181-214` / `typeext.cpp:130-166` 的 TypeCode 表 Decimal 目前回落 1
>   （TypeCode.Object）。
>
> 配套文档：`docs/design/regex-domain.md`（域设计模板）、`docs/architecture/overview.md`、
> `docs/REQUIREMENTS.md`（铁律）。

---

## 1. 表示：Il2CppDecimal（16 字节，对齐 .NET / Win32 DECIMAL）

### 1.1 .NET 字段布局（逐字段抄）

`System.Decimal`（Decimal.cs:127-129）与 OleAut DECIMAL 同布局，只读字段：

| 字段 | 类型 | 偏移 | 内容 |
|---|---|---|---|
| `_flags` | int32 | 0 | bit31=sign（1=负）；bit16-23=scale（0..28）；其余位必须为 0 |
| `_hi32` | uint32 | 4 | 96 位尾数的高 32 位 |
| `_lo64` | uint64 | 8 | 96 位尾数的低 64 位 |

- SignMask = `0x80000000`，ScaleMask = `0x00FF0000`，ScaleShift = 16（Decimal.cs:80-88）。
- 值 = `(sign ? -1 : +1) * (96 位无符号尾数) / 10^scale`；范围 ±(2^96-1)，
  有效数字 28 位（Decimal.cs:99-105）。
- **顺序敏感**：`GetBytes`（Decimal.cs:622-630）按 lo/mid/hi/flags 小端写出——C 结构
  必须与之逐字节一致。

### 1.2 C 结构定义

```c
// src/runtime/include/internal/decimal.h
namespace il2cpp::runtime {
// 与 .NET Decimal 逐字节同布局（static_assert sizeof == 16）。
// 小端 x86/x64 平台（本项目目标）下字段序与 .NET 完全一致。
struct Il2CppDecimal {
    int32_t flags;   // bit31 sign, bit16-23 scale, 其余 0（IsValid 校验, Decimal.cs:260）
    uint32_t hi32;   // 96 位尾数高 32 位
    uint64_t lo64;   // 96 位尾数低 64 位
};
}
```

DecCalc 内部以显式偏移访问 lo/mid/hi（Decimal.DecCalc.cs:43-57：
uflags@0 / uhi@4 / ulo@8 / umid@12）——**移植 DecCalc 时直接按 uflags/uhi/ulo/umid
四个 uint32 视图 + ulomid 视图工作**，C++ 移植用 union 或 memcpy 视图即可（DecCalc
原样照抄，不重构）。

### 1.3 不变量（IsValid）

`flags & ~(SignMask|ScaleMask) == 0 && scale <= 28`（Decimal.cs:260）。icall 入口
对非法 flags → `il2cpp_throw_arithmetic_exception`（OverflowException 链，net48 行为）。

---

## 2. 运算 icall 清单（对照 Decimal.cs / DecCalc 算法，铁律 3 抄源码）

> 全部 icall 声明在 `src/runtime/include/internal/decimal.h`，实现在
> `src/runtime/src/decimal.cpp`（namespace il2cpp::runtime，icall_ 前缀——versionuri.h
> / guid.h 惯例）。**值类型按 Guid 先例传 RAW 指针**（MethodMap.Guid.cs:7-13）：
> decimal 参数/接收者 = 值槽地址（`Il2CppDecimal*`），与 MethodMap.DateTime.cs 的
> `deref:dt` 模式不同——decimal 是 16 字节，需要**地址传递**（16 字节不能像 8 字节
> 一样单寄存器返回；Guid 的 `""` uncast 先例同款）。

### 2.1 四则 + 比较（T1）

| 成员 | icall | 算法来源 | 备注 |
|---|---|---|---|
| `decimal+decimal` | `icall_decimal_add(Il2CppDecimal*, Il2CppDecimal*)` | DecCalc.DecAddSub(d1,d2,false) | Decimal.cs:969-973；d1 就地写结果 |
| `decimal-decimal` | `icall_decimal_sub` | DecCalc.DecAddSub(d1,d2,true) | Decimal.cs:975-979 / 693-697 |
| `decimal*decimal` | `icall_decimal_mul` | DecCalc.VarDecMul | Decimal.cs:651-655 / DecCalc.cs:1389 |
| `decimal/decimal` | `icall_decimal_div` | DecCalc.VarDecDiv | Decimal.cs:437-441 / DecCalc.cs:1877；除零 → DivideByZeroException |
| `decimal%decimal` | `icall_decimal_rem` | DecCalc.VarDecMod | Decimal.cs:643-647 / DecCalc.cs:2143 |
| `static int Compare(decimal,decimal)` + 实例 CompareTo | `icall_decimal_compare(const Il2CppDecimal*, const Il2CppDecimal*)` | DecCalc.VarDecCmp | Decimal.cs:408-411 / DecCalc.cs:1304-1384 |
| `==` / `!=` | `icall_decimal_equals`（!= 用 `!` 前缀，Guid 先例 MethodMap.Guid.cs:82-85） | DecCalc.Equals | Decimal.cs:1003-1007 / DecCalc.cs:1287 |
| `<` `<=` `>` `>=` | `icall_decimal_compare` 上叠加比较 | VarDecCmp | Decimal.cs:1009-1018 |
| 一元 `-` | 就地 flags^SignMask（Decimal.cs:961 规则；`decimal.Negate` 同） | 1 行，不需 icall | 可在 T1 内做字段级发射或 icall |

**Add/Sub 的核心形态**（DecCalc.cs:931-1054）：scale 对齐（把较小 scale 操作数乘
10^Δscale 到 192 位缓冲 Buf24），对齐后 96 位加减 + 规范化 + 溢出检查（>2^96 →
`ThrowDecimalOverflowException`）。移植时**照抄**：UInt32Powers10[0..9]、
UInt64Powers10[1..19]、TenToPowerNine、Buf12/Buf16/Buf24 缓冲结构、
UInt64x64To128（DecCalc.cs:159-166）。

**Mul 形态**（DecCalc.cs:1389-…）：scale 相加（byte 溢出取低 8 位，uflags+uflags>>16）、
128/192 位乘积、Unscale 到 96 位（DecCalc.cs:310）。

**Div 形态**（DecCalc.cs:1877-2139）：三路——32 位除数（Div96By32，DecCalc.cs:191）、
64 位除数（Div128By64）、96 位除数（Div128By96，MSB 归一化
`LeadingZeroCount`，DecCalc.cs:1973）；余数非零时 SearchScale / IncreaseScale 循环补
商位，到 DEC_SCALE_MAX=28 或溢出时按 IEEE 半位规则舍入（`remainder<<1` 与除数比较 +
LSB 奇偶，DecCalc.cs:1932-1958）。**这是全域最复杂部分**——见 §6 风险。

**Mod 形态**：VarDecMod（DecCalc.cs:2143）——先 Div 得商，再 `被除数 - 商*除数`
（net48 语义：余数与被除数同号）。

### 2.2 舍入（T1 尾 / T2 头）

| 成员 | icall | 算法来源 |
|---|---|---|
| `Round(decimal[, int decimals[, MidpointRounding]])` | `icall_decimal_round(Il2CppDecimal*, int32_t scale, int32_t mode)` | DecCalc.InternalRound（DecCalc.cs:2322-2438）+ Decimal.cs:678-689（decimals 0..28 校验、mode 校验） |
| `Truncate` | `icall_decimal_truncate` | Decimal.cs:888-…（scale 保留、尾数按 10^scale 截断，舍向零） |
| `Floor` / `Ceiling` | `icall_decimal_floor` / `icall_decimal_ceiling` | InternalRound mode=ToNegativeInfinity / ToPositiveInfinity（Decimal.cs:397-403, 469-475） |

InternalRound 要点：从 scale 位看，先把 `scale` 个十进制位从 96 位尾数**用 10^9 分组
长除**剥离（DecCalc.cs:2329-2363），再按 mode 决定是否 +1（ToEven 需 LSB 奇偶判据、
AwayFromZero 半位判据、To±Infinity 符号判据，DecCalc.cs:2402-2435）。净位溢出
（++Low64==0 → uhi++，DecCalc.cs:2434-2435）。

### 2.3 格式化 + 解析（T2）

| 成员 | icall | 算法来源 |
|---|---|---|
| `ToString()` / `ToString(format[, provider])` | `icall_decimal_to_string(const Il2CppDecimal*, Il2CppString* format)` | Number.FormatDecimal（Decimal.cs:482-500）——**ILC 侧不移植整个 Number.Formatting.cs**，只移植 decimal 必需路径：G 默认（§4.3 裁剪表） |
| `static decimal Parse(string)` / `TryParse(string, out decimal)` | `icall_decimal_parse(Il2CppString*, Il2CppDecimal*)` / `icall_decimal_try_parse` | Number.ParseDecimal / TryParseDecimal（Decimal.cs:520-561）——数字串 → 尾数+scale 累加，超 28 位或溢出 → OverflowException / false |
| 数字提取原语 | 内部 `DecDivMod1E9`（DecCalc.cs:2440-2447） | 格式化尾数按 10^9 分组提取，Decimal.cs:29-33 暴露给 Number 域 |

**ToString("G") 的核心算法**（本域唯一自实现部分——但用 .NET 数字布局规则）：
尾数 96 位十进制展开（DecDivMod1E9 循环提取 9 位一组）+ scale 落点：
- scale=0：纯整数。
- 整数部分 >0：`整数.frac`，frac 用 scale 位截取。
- 整数部分 =0：`0.000…frac`（小数点后补 scale 个前导零）。
- net48 规则：**尾部零不裁剪**（`1.50m.ToString() == "1.50"`），整数 0 但 frac 非零
  时也输出整数部分。这与 net48 的 decimal.ToString 完全一致（net48 decimal 无
  净48 G 的科学计数切换——decimal 恒定点，`1234567890123456789012345678.9m`
  ToString 就是全数字）。
- 格式串子集：见 §4.3 裁剪（F/G/N/E/C 可复用 formatext.cpp 的既有形式逻辑，但
  输入改为 decimal 尾数数字串）。

**Parse**：ASCII 数字 + 可选 `+/-` + 可选单个 `.`（+ 千分位 `,` 容忍——NumberStyles.Number
默认含 AllowThousands）；数字位数 >28 有效位 → OverflowException；scale 累计不能 >28。
对照 Number.Parsing.cs 的 decimal 分支（`ParseDecimal` 用 ulong 累加 + 大数进位）。
**ILC 版本**：小数位计数 + 尾数按 10^n 累加，超 2^96-1 → Overflow（net48：
`79228162514264337593543950335.1m` Parse 抛 OverflowException）。

### 2.4 构造 / 转换（T1）

| 成员 | icall / 处理 | 算法来源 |
|---|---|---|
| 字面量 `1.5m`（IL 形态：`newobj Decimal(int lo,int mid,int hi,bool sign,byte scale)`） | `icall_decimal_ctor_5`（5 参）——直接装配字段 | Decimal.cs:5 参 ctor（校验 hi/lo 组合合法性——lo/mid/hi 为带符号 int 解释） |
| `.ctor(int)` / `.ctor(long)` / 其他整数 ctor | 装配（int → lo64 低 32） | Decimal.cs:133-183 |
| `.ctor(float/double)` | `VarDecFromR4/R8`（DecCalc.cs:1548-1556）——double → 尾数+scale 转换 | 较复杂；可延后（T1.5 或显式 LOUD） |
| `ToInt32/ToInt64/ToUInt32/ToUInt64`（含显式转换 `(int)1.5m`） | `icall_decimal_to_int32/int64/...`——截断后溢出检查 | Decimal.cs:763-871（`Truncate` 后范围检查，溢出 → OverflowException） |
| `ToDouble/ToSingle` | `VarR8FromDec / VarR4FromDec`（DecCalc.cs:1671-1688） | 双精度近似；可延后 |
| `GetHashCode` | `DecCalc.GetHashCode`（DecCalc.cs:1854） | 简单；含 |
| `GetBits` / `new decimal(int[4])` | 尾数+flags 直接搬移 | 可选 |

### 2.5 明确不做（v1）

| 成员 | 理由 |
|---|---|
| `decimal(decimal)` 复制 ctor / `ToString(IFormatProvider)` 单参 | 无状态变化；provider 参数无 icall 形态（MethodMap.Format.cs:22-26 的 IFormatProvider 先例：loud） |
| `decimal.ToString(string, IFormatProvider)` | 两参形态可用 FormatCasts "drop" 先例（MethodMap.Format.cs:55-56）——**做**（格式化 T2） |
| OACurrency / Serialization / IConvertible 接口方法 | 使用率低，M 阶段无接口分派 |
| `decimal.Parse(string, NumberStyles, IFormatProvider)` 三参 | 裁剪：只有 `Parse(string)` / `Parse(string, IFormatProvider)` 两参可用形态 |
| 浮点 ctor / ToDouble | 见 §2.4 延后项——首版 LOUD |

---

## 3. 转译器接线（T3）

### 3.1 已就绪、无需改动

- **MethodSignatureCoder**：`V:System.Decimal` 已按 0x11 VALUETYPE 编码为
  `V:System.Decimal`（MethodSignatureCoder.cs:78）——签名哈希/参数解析天然支持，
  **零改动**。
- 调用折叠路径：`newobj Decimal::.ctor` → ValueTypeNewExpr（CodeGenerator.cs:1353）；
  `ldloca` 地址 → `deref:dt` 先例（StackFold.cs:1635-1640 DateTime/TimeSpan 就地路径）。

### 3.2 需要改动（逐点，全部有先例）

**A. TypeCppName 映射**（CodeGenerator.cs:1881-1886 私有映射表）：
```
"System.Decimal" => "il2cpp::runtime::Il2CppDecimal",
```
先例：`"System.DateTime" => "il2cpp::runtime::Il2CppDateTime"`。用户代码
`decimal d = ...` 的本地变量槽 / 值类型参数（ParamTypesCpp，CodeGenerator.cs:2483
`{Type}*`）随即得到正确的 C++ 类型。TypeEmitter.cs 的全局 TypeCppName 表不需要动
（decimal 永不发射结构体——BCL 类型）。

**B. 结果临时变量 kind**：新增 `"dec"` kind（CodeGenerator.cs:530-539 的 "dt"/"ts"
先例）：
```c
if (kind == "dec") { w.Line($"il2cpp::runtime::Il2CppDecimal {d};"); continue; }
```
StackFold.cs:1364 的 CallExpr ResultKind 分流加 `"dec"`（"dt"/"ts" 同排）。

**C. 值类型接收者 cast**：`"deref:dec"` 分支（CodeGenerator.cs:2324-2325
`deref:dt` 先例）：`(*(il2cpp::runtime::Il2CppDecimal*)({code}))`。参数 uncast
`""` 传地址（Guid 先例）。赋值发射：结构体复制是原生 C++ 拷贝（比 Guid 的
uint8_t[16] memcpy 更简单——Il2CppDecimal 是 struct，AssignStmt 直接 `a = b`）。

**D. 盒装**（CodeGenerator.cs:2062-2104 box_/unbox_ 表）：
- `"System.Decimal" => icall_box_decimal(...)` —— box 函数在 runtime
  boxingext2.cpp（16 字节 payload，icall_box_long 先例 boxingext2.cpp:305-307；
  **新盒类 System_Decimal_class 需要注册**——box 类表/descriptor 模式查
  il2cpp_boxing_ext2.h:79）。
- `"System.Decimal" => icall_unbox_decimal(...)`；unbox:* cast 表
  （CodeGenerator.cs:2326-2330）加 `"unbox:dec"`。
- **栈上值→对象格式化**：valuesemantics.cpp 的 `icall_object_to_string`
  （valuesemantics.cpp:246-294 的 HasKlassName 链）加
  `HasKlassName(obj,"System.Decimal")` → decimal ToString icall——WriteLine(decimal)
  由此通。decimal 的 box 对象必须带 System.Decimal 的 klass 名。

**E. TypeCode（类型码 15）**——两处运行时表 + 一处折叠表，必须同步：
1. `typecheck.cpp:181-214` `icall_object_get_type_code`：加
   `if (HasKlassName(obj, "System.Decimal")) return 15;`（net48: TypeCode.Decimal=15）。
2. `typeext.cpp:130-166` `il2cpp_type_get_typecode`：同表（两文件头注释要求保持同步）。
3. `StackFold.cs:1957-1965` `TypeCodeOfTypeName` 折叠表：
   `"System.Decimal" => 15`（与 runtime 表逐项一致）。

**F. 格式化接线**：
- `MethodMap.Format.cs:27-49` 的 owner 分派加 `"System.Decimal"` →
  `icall_decimal_to_string_custom`（G/F/N/E/C 子集，复用 FormatCasts 先例）。
- MethodMap.ValueType.cs 的 TryMapValueType 分派加 `System.Decimal` 段（无参
  ToString → icall_decimal_to_string；Parse/TryParse；Equals/CompareTo/GetHashCode）。
- **方法映射文件新建**：`MethodMap.Decimal.cs`（Guid/DateTime 先例）——
  全部 icall 映射 + LOUD 拒绝未接线成员（.ctor(float) 等）。

**G. 值类型 ToString 折叠**（StackFold.cs:1280-1291 constrained 表）：
加 `"System.Decimal"` → `icall_decimal_to_string`（`deref:dec` 接收者）——
与 Guid/DateTime 分支同排；MethodMap.Object.cs:69 的 receiver-typed ToString 列表
同步加 Decimal 段（callvirt Object::ToString on decimal receiver）。

### 3.3 接线顺序约束

1. runtime 先落：decimal.h/cpp（T1）→ 格式化/解析（T2）——**transpiler 映射必须在
   icall 符号存在之后接线**（discover.md:32-34 的 Uri 先例：符号缺失 = 链接失败）。
2. T3 只接线：MethodMap.Decimal.cs + A/B/C/D/E/F/G 点。
3. e2e：fmtchain.cs 去掉 :16 的排除注释、补 decimal 断言段 → 重跑 net48 oracle diff。

---

## 4. 范围裁剪

### 4.1 API 面（v1）

| 成员 | v1 | 说明 |
|---|---|---|
| `+ - * / %` 与比较运算符全家 | ✅ | T1，抄 DecCalc |
| `decimal.Round(d[, decimals[, mode]])` / `Truncate` / `Floor` / `Ceiling` | ✅ | T1 尾/T2 头，InternalRound |
| `ToString()` / `ToString(string format[, IFormatProvider])` | ✅ | T2，G 默认 + 子集格式 |
| `Parse(string[, IFormatProvider])` / `TryParse(string, out decimal)` | ✅ | T2（TryParse 用 `B:` 编码——MethodSignatureCoder.cs:89 已有 out 编码，Guid 先例） |
| 整数 ctor + 5 参 ctor（字面量） | ✅ | T1 |
| `ToInt32/Int64/UInt32/UInt64` 截断转换 | ✅ | T1 尾 |
| `GetHashCode` / `Equals(decimal)` / `CompareTo` | ✅ | T1 |
| `.ctor(float/double)`、`ToDouble/ToSingle` | 🟡 | VarDecFromR4/R8 独立可拆任务；未接线前 LOUD |
| `GetBits` / `int[]` ctor | ❌ | v2（低使用率） |
| `FromOACurrency` / `ToOACurrency` | ❌ | v2 |

### 4.2 精度与异常语义（net48 对齐）

- 溢出（结果超 2^96-1 或 scale>28）→ **OverflowException**（`ThrowDecimalOverflowException`，
  DecCalc.cs:163）。除零 → **DivideByZeroException**（DecCalc.cs:1894）。
- Round decimals 参数 0..28 校验 → ArgumentOutOfRangeException（Decimal.cs:680-681）；
  MidpointRounding 非法值 → ArgumentException（Decimal.cs:682-683）。
- 解析溢出 → OverflowException；格式非法 → FormatException。

### 4.3 格式化子集裁剪（vs net48 Number.FormatDecimal）

| 格式 | v1 | 说明 |
|---|---|---|
| 默认（null / "G" / "g"） | ✅ | §2.3 核心算法；尾部零保留 |
| "F"/"F<n>" | ✅ | 固定小数位（formatext.cpp:280 既有 fixed-point 逻辑可复用数字布局） |
| "N"/"N<n>" | ✅ | 千分位分组 |
| "E"/"E<n>" | ✅ | 科学计数（formatext.cpp:462 既有 E 逻辑，输入换 decimal 数字串） |
| "C"/"C<n>" | ✅ | 货币（复用 formatext 既有路径，小数位 2） |
| "P"/"P<n>" | 🟡 | ×100 语义；可延后 |
| "D"/"X" | ❌ | decimal 不支持（net48 抛 FormatException）——loud |
| 自定义 `"0.00"` / `"#,##0.00"` | 🟡 | 占位符替换引擎在 formatext.cpp 有先例；可延后 |

---

## 5. 任务卡拆分

### T1：表示 + 四则（对照 Decimal.cs）
**文件**：新建 `src/runtime/include/internal/decimal.h`、`src/runtime/src/decimal.cpp`；
`src/runtime/CMakeLists.txt` 加源。
**内容**：
1. `Il2CppDecimal` 结构 + `static_assert(sizeof==16)` + IsValid 校验。
2. 抄 DecCalc：UInt32Powers10/UInt64Powers10/TenToPowerNine/Buf12/Buf16/Buf24/
   UInt64x64To128/Div96By32/Div64By32/Unscale 等全部 helper（DecCalc.cs:106-310）。
3. 抄 DecAddSub（931）、VarDecMul（1389）、VarDecDiv（1877）、VarDecMod（2143）、
   VarDecCmp（1304）、Equals（1287）、GetHashCode（1854）、InternalRound（2322）。
4. icall 表面：add/sub/mul/div/rem/compare/equals/round/truncate/floor/ceiling +
   整数 ctor + 5 参 ctor + ToInt32/64/UInt32/64 截断 + Negate。
5. 测试：tests/runtime/test_decimal.cpp（对照 DecCalc 已知值表：0.1+0.2、maxvalue、
   1.5m 除 2、舍入半位 ToEven/AwayFromZero 等 + net48 oracle 交叉验证）。

### T2：格式化 + 解析（对照 Decimal.DecCalc + Number）
**文件**：decimal.cpp 追加（或 decimal_fmt.cpp）。
**内容**：
1. DecDivMod1E9 尾数数字提取（DecCalc.cs:2440）。
2. `icall_decimal_to_string`：G 默认布局（§2.3）+ 格式子集（F/N/E/C，复用
   formatext.cpp 数字布局函数族）。
3. `icall_decimal_parse` / `icall_decimal_try_parse`：数字串 → 尾数+scale 累加，
   28 位有效位 / 2^96 上限溢出检查，OverflowException/FormatException 链。
4. `icall_decimal_to_string_custom`（format 参，MethodMap.Format.cs 契约）。
5. 测试：test_decimal 追加格式化/解析往返（net48 oracle：`1.50m`、`0.0001m`、
   极大值、科学计数边界、解析溢出）。

### T3：接线 + 类型支持（转译器 + runtime 表）
**文件**：新建 `src/il2cppc/Emit/MethodMap.Decimal.cs`；改 CodeGenerator.cs /
StackFold.cs / MethodMap.Format.cs / MethodMap.ValueType.cs / MethodMap.Object.cs /
valuesemantics.cpp / typecheck.cpp / typeext.cpp / boxingext2.cpp。
**内容**：§3.2 的 A-G 全部点 + `icall_box_decimal`/`icall_unbox_decimal` +
System.Decimal box 类 descriptor（boxingext2 先例）+ e2e fmtchain decimal 段 +
单元测试（MethodMapTests.Decimal.cs 仿 VersionUri）。

**依赖**：T1 → T2（格式化需要尾数提取）→ T3（映射需要 icall 符号存在）。
T1 与 T2 内部分工可与 T3 并行准备（T3 无 runtime 依赖时先写测试占位）。

---

## 6. 风险与对策

1. **96 位除法复杂度（最高风险）**：Div128By96/Div64By32/Div96By32 是长除法 +
   归一化 + 缩放循环的组合（DecCalc.cs:1877-2139），任何一步抄错都会在边界值
   （maxvalue / 大尾数除法）产生静默错误。
   **对策**：铁律 3 逐函数照抄（含 Buf12/Buf16 位操作细节），不简化；测试用
   .NET 生成的已知值表（net48 oracle 全量交叉验证，含 maxvalue/极小余数/舍入边界）。
2. **舍入语义**：ToEven vs AwayFromZero vs To±Infinity 五种 mode 判据不同
   （DecCalc.cs:2402-2435）。对策：测试覆盖每个 mode 的半位用例（2.5、0.5、-2.5 等）。
3. **溢出路径**：Add/Mul/Div 的 3 条溢出路径（>2^96、scale>28、OverflowUnscale）
   各测试一个触发用例。
4. **16 字节值传递**：不能走 8 字节的"值返回"通道；全部走地址（Guid 先例已证明
   该模式），CodeGenerator 需要 `deref:dec` 与 uncast 参数两条路径（§3.2 C）。
5. **格式差异风险**：decimal 的 G 与 double 的 G15 不同——decimal 恒定点、保尾零、
   无科学计数切换；移植时**禁止**复用 double 的 FormatInt64Decimal 判定逻辑
   （formatext.cpp:967 的 G 分支是 double 语义）。
6. **TypeCode 三表同步**：typecheck.cpp / typeext.cpp / StackFold.cs 任一漏改 →
   `typeof(decimal).GetTypeCode()` 或 boxed decimal 的 GetTypeCode 返回 1。测试断言
   三路径一致（net48: 15）。

---

## 7. 探路证据索引（2026-08-05）

- 表示：Decimal.cs:80-88（掩码）、127-129（字段布局）、622-630（GetBytes 顺序）；
  DecCalc.cs:39-57（显式偏移布局）。
- 算法入口：DecAddSub 931 / VarDecMul 1389 / VarDecDiv 1877 / VarDecMod 2143 /
  InternalRound 2322 / VarDecCmp 1304 / DecDivMod1E9 2440 / VarDecFromR4/R8 1548-1556。
- 公共 API 面：Decimal.cs:389-881（Add..Truncate）、959-1018（运算符）、
  482-561（ToString/Parse/TryParse）、673-689（Round）。
- 转译器接线点：CodeGenerator.cs:530-539（dt/ts kind）、1881-1886（TypeCppName 表）、
  2062-2104（box/unbox 表）、2324-2330（deref/unbox cast）；StackFold.cs:1280-1291
  （constrained ToString 折叠）、1957-1965（TypeCodeOfTypeName）；MethodSignatureCoder.cs:78
  （V: 编码）；MethodMap.Format.cs:55-56（FormatCasts drop）。
- runtime 表：typecheck.cpp:181-214、typeext.cpp:130-166（TypeCode）、
  valuesemantics.cpp:246-294（object_to_string 分派）、marshalext2.cpp:75（16 字节）。
- e2e 卡例：tests/cases/fmtchain/fmtchain.cs:16。
