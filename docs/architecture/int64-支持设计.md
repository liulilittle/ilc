# int64 全链路支持设计（M5）

> 域状态：**由 M5 接线代理在途实现**（Int64ConstExpr / DoubleConstExpr / LocalKinds /
> conv icall 接线已入树，2026-08-03）。本文档为架构参考：完整支持面清单、类型流设计、
> 与既有 int32 模型的冲突点、TDD 建议、以及**保守回退策略**（若在途实现回退）。
> 本文档由 uint/移位语义补全域撰写，字节级事实全部经 Roslyn net48 真实产物实证。

## 1. 问题

M1-M4 转译器是 int32 单型模型：IR 值槽、局部、参数、字段、数组元素全按 int32_t，
常量折叠（TryFoldInt32）与 C++ 发射全部按 32 位有符号语义。int64 相关输入此前只能
loud 拒绝（C3：ldc.i8 超范围）。残余静默风险：

- `int.MaxValue + 1L`（常量）→ Roslyn 常量折叠为 ldc.i8 2147483648 → 超范围 → 已 loud。
- `x + 1L`（变量，int 参数）→ Roslyn 发 `ldarg; conv.i8; ldc.i4.1; conv.i8; add`
  → conv.i8 无 case → 已 loud（实证：mixprobe IL `02 6A 17 6A 58 2A`）。
- **手写/其他编译器 IL 的 in-range ldc.i8**（如 `ldc.i8 1; add` 无 conv.i8）→
  之前静默按 int32 折叠 → 错误（本域已加 IsInt64 标记防 int32 折叠，见 §4）。
- **long 局部**：`long x = <int32 值>; x = x + x`（值 ≥2^31 时 int64 中间结果
  与 int32 回绕分叉）→ 此前静默错误 → 现由 LocalKinds "l" 槽 + conv icall 解决。
- long 参数（0x0A）、long 返回、long 字段、long 数组元素：MethodSignatureCoder /
  ReturnTypeCpp / DecodeFieldSignature / ArrayElementType 已 loud 拒绝（保持）。

## 2. 支持面清单（全链路）

| 面 | 现状 | 全链路目标 |
|---|---|---|
| ldc.i8 常量 | C3 超范围拒绝 → Int64ConstExpr 承载 | Int64ConstExpr 全范围 |
| conv.i8 / conv.u8 / ovf 变体 | 无 case → loud | Int64ConstExpr / icall |
| int64 算术（add/sub/mul/div/rem/neg/not/and/or/xor） | int32 折叠或 C++ 提升 | 发射 int64 表达式 |
| int64 移位（shl/shr/shr.un，计数掩码 63） | shr/shr.un int32 掩码 31 | 64 位掩码 |
| int64 比较（ceq/cgt/clt/cgt.un/clt.un + 无符号分支） | int32 语义 | 64 位比较 |
| int64 参数/返回 | 0x0A 签名 loud | "l" 参数槽 + int64_t 返回 |
| int64 局部 | LocalKinds "l"（已入树） | int64_t 槽声明 + ldloca 64 位解引用 |
| int64 字段（实例/静态） | DecodeFieldSignature 0x0A loud | 8 字节槽（refFieldBitmap 无涉） |
| int64 数组元素 | newarr/ldelem.i8/stelem.i8 loud | elemSize 8 |
| box/unbox Int64 | H3 loud | 运行时已有 icall_box_long/unbox_long（boxingext2 域） |
| int64 → int32 / int32 → int64 | conv icall（icall_conv_i4_i8 / i8_i4 已入树） | 复用 conv 域 icall |
| double（r8）链路 | Ldc_r8/DoubleConstExpr 已入树 | conv icall（conv 域已备 icall_conv_*_d） |

## 3. 类型流设计（在途实现的选择，已实证可行）

- **常量**：ldc.i8 超范围 → `Int64ConstExpr`（Expr.cs）；in-range → `IntConstExpr`
  带 `IsInt64=true` 标记（位级与 int32 相同，但不得按 int32 折叠）。
- **局部**：MethodBodyReader 按局部签名逐槽解析（实证字节：0x0A/0x0B=I8/U8、
  0x0C/0x0D=R4/R8、0x1D=**SZARRAY**（元素类型随后）、0x11/0x12/0x40=VALUETYPE/CLASS
  +压缩索引、0x13/0x1E=VAR/MVAR+索引、0x14/0x19=多维数组/函数指针→loud），产出
  `MethodBodyInfo.LocalKinds`（"i"/"l"/"d"/"r"/"v"）→ StackFold.LocKindOf 路由转换。
- **转换**：conv.i4/i8/u4/u8/d 在 StackFold 折叠层分发——常量直接位操作
  （unchecked 低 32 位）、int64 变量经 conv 域 icall（icall_conv_i4_i8 等，
  语义 = cvttsd2si 截断向零 + 溢出不定值，见 conv 域报告）。
- **发射**：混合 int32/int64 表达式靠 C++ 隐式提升自然加宽
  （`(int32_t)x + 1LL` → int64），无需显式类型传播；int32 常量折叠（TryFoldInt32）
  必须**拒绝 int64 标记常量**（本域已实现：`case IntConstExpr i: if (i.IsInt64) return false`），
  否则 `1L + 2147483647L`（手写 IL 可达）会被错折成 int32 回绕。
- **比较/分支**：int64 比较需 64 位 C++ 运算符（`==`/`>`/`<` 对 int64_t 自动成立）；
  无符号 64 位比较用 `(uint64_t)` 双cast（与 H1 的 uint32 模式同构）。

## 4. 本域已交付的关联加固（2026-08-03，uint/移位语义域）

1. **shr.un 逻辑右移**（必须项）：BinaryOp.ShrUn 全链路——IlDecoder 0x64 解码、
   StackFold 折叠、发射 `((int32_t)((uint32_t)(x) >> (n & 31)))`（零填充保证 +
   int32_t 结果型保下游有符号语义）；常量折叠 `Wrap32((uint)l >> (r & 31))`。
   e2e 实证：`0x80000000u >> 2 = 536870912`（算术右移会给 -536870912）。
2. **移位计数掩码统一**：发射层 Shl/Shr/ShrUn 一律 `(n & 31)`（折叠层原本就掩码；
   发射层此前不掩码 = 计数 ≥32 时 C++ UB，与 .NET 低 5 位语义分叉）。
3. **IsInt64 标记**（Expr.cs）：in-range ldc.i8 携带 int64 出处；TryFoldInt32 拒绝
   折叠 int64 标记常量（防静默 int32 回绕）；conv.i4 显式收窄清除标记
   （`(int)5L` 合法进入 int32 算术）。
4. **uint 语义验证**：Cgt_un/Clt_un 无符号折叠/发射 + Bge_un 无符号分支 +
   uintcmp e2e 8/8 与 net48 一致（常量折叠路径随 const-prop 域并入）。

## 5. 回退策略（若在途实现回退）

若 M5 int64 实现回退，恢复保守策略只需两步：
1. ldc.i8 → 一律 loud 拒绝（还原 C3，删 Int64ConstExpr 承载路径）。
2. IsInt64 标记常量在二元/一元运算处 loud 拒绝（本域初版即此形态，见备份
   `.opencode/backups/uintshift_*`），防 `ldc.i8 1; add` 静默 int32 回绕。

## 6. TDD 建议（若从零实现）

- 每面先写 net48 参考程序（变量路径跑运行时语义——常量路径会被 Roslyn 折叠，
  见 conv 域实证）→ 捕获 oracle → 写转译器失败测试 → 最小实现。
- 字节级陷阱（已实证）：shr.un=0x64 单字节（非 0xFE 前缀）；cgt.un=0xFE03、
  clt.un=0xFE05；conv.i4=0x69、conv.i8=0x6A、conv.i=0xD3；bge.un=0x41；
  0x1F=ldc.i4.s（**不是** ldc.i4.m1，m1=0x15）；局部签名 SZARRAY=0x1D+元素字节。
- int64 算术的 C++ 提升正确性：发射层不折叠 int64 表达式即可（C++ 自动加宽），
  唯一禁地是 TryFoldInt32 对标记常量折叠。
