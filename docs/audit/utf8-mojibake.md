# UTF-8 乱码诊断报告 — stringext2（e2e 矩阵 "UTF 乱码×1"）

- 日期：2026-08-04
- 类型：只读诊断 + 施工单（未修任何文件，未 commit）
- 矩阵记录出处：docs/audit/e2e-full-matrix2.md L104（"多字节 UTF-16→UTF-8 乱码（runtime，同 M1）"）+ e2e-full-matrix.md L99（同描述）；matrix2 percase CSV 实测 "gen exit=0 lines=41/42"

## 0. 结论摘要（三行）

1. **矩阵归因不准确**：stringext2 的 diff 不是"UTF-16→UTF-8 转换丢失"，而是 **runtime `String.Join(string, object[])` 把 boxed int 元素当字符串读（reinterpret_cast 类型混淆）**，把 box 内存当 UTF-16 载荷输出；用例本身**不含任何中文字符**。
2. **两条真实差异**（41/42 行一致）：(a) L19 `String.Join(",", new object[]{1,2,3})` 输出 8 字节 `00 2C 00 00 2C 00 00 00`（应为 "1,2,3"）；(b) L23 `String.Join(",", (string[])null)` 未抛 ArgumentNullException（runtime 文档化偏离：null 数组返回空串），catch 不触发，整行缺失。
3. **逐字节闭环**：Il2CppString={klass@0,length@8,chars@12}，boxed int={klass@0,payload@8}；box 被当 string 读 → **length=box 载荷（即 1/2/3 本身）**，chars=box 外零填充内存 → 总长=1+2+3+2×1=8，输出 `\0,\0\0,\0\0\0` —— 与实测逐字吻合。

## 1. 复现步骤（全链，本报告实测）

```powershell
# 0) 前置：dll 新鲜度（17:52:18 新于全部源）
# 1) net48 参考（oracle）
& tests/cases/stringext2/out/stringext2.exe | Set-Content -Encoding utf8 ref.txt   # 42 行
# 2) 转译（il2cppc.dll 直跑）
dotnet src/il2cppc/bin/Release/net8.0/il2cppc.dll tests/cases/stringext2/out/stringext2.exe <gen>
#    → EXIT=0，生成 Cases_StringExt2.cpp
# 3) 构建（共享 runtime 库 wrapper：GLOB gen/src/*.cpp 排除 host.cpp → lib；host.cpp 单独 exe）
cmake -S wrap -B wrap/build -G "MinGW Makefiles" && cmake --build wrap/build
#    → BUILD_EXIT=0
# 4) 运行 + diff
& wrap/build/il2cpp_gen_exe.exe | Set-Content -Encoding utf8 act.txt   # RUN_EXIT=0, 41 行
Compare-Object (Get-Content ref.txt) (Get-Content act.txt)
#    → L19 乱码 + ArgumentNullException 行缺失；其余 40 行逐字节一致（= 41/42，与 matrix2 CSV 吻合）
```

实测证据（本报告）：
- 转译 EXIT=0；构建 BUILD_EXIT=0；运行 RUN_EXIT=0；ACT=41 REF=42 行
- ACT L19 码元：`0000 002C 0000 0000 002C 0000 0000 0000`（8 个 UTF-16 码元），文件字节 `00 2C 00 00 2C 00 00 00 0D`
- REF L19 = `1,2,3`；REF L23 = `ArgumentNullException`
- 新鲜转译（17:52 dll）与 matrix2-era gen 逐字同形（join 发射无漂移）

## 2. 根因链（丢失环节逐段定位）

```
C# 源码 String.Join(",", new object[] { 1, 2, 3 })
  │ (1) csc 编译 ✓ 无损
  ▼
IL: newarr object + box(int32)×3 + stelem.ref×3 + call String::Join(string, object[])
  │ (2) 转译器 MethodMap.String.cs:126：只按 ps=[0x0E,0x1D] 分派 → object[] 与 string[] 签名不可区分
  │     （RawSignature 不消费 SZARRAY 元素字节 0x0C vs 0x0E——discover.md 已记录的同一盲区，bitconvcase 同源）
  ▼
生成 C++（Cases_StringExt2.cpp:90-97）：il2cpp_array_new(elemSize=8, isRefElem=true, 3)
    + box_int32×3 + stelem.ref×3 + icall_string_join(",", t21)   ← 生成正确，语义错配
  │ (3) runtime icall_string_join（stringext2.cpp:349）→ JoinFromArray（stringext2.cpp:162）
  │     只查 array->isRefElem（object[] 通过）——无元素类型检查
  ▼
(4) ★ 丢失环节 ★ ArrayElementAsString（stringext2.cpp:48-53）= reinterpret_cast<Il2CppString*>(elem)
    boxed int 被当 Il2CppString 读：
      length @8 = box 载荷（=1/2/3）
      chars  @12 = box 之外的内存（GC 零填充 → 全 0x0000）
    pass-1 total = 1+2+3 + 2×1 = 8 ✓（与输出字节数逐字吻合）
  ▼
(5) join 结果 = "\0,\0\0,\0\0\0"（8 码元 [00 2C 00 00 2C 00 00 00]）
  ▼
(6) 控制台 icall_console_write_line_s（runtime.cpp:15-27）：码元 <0x80 原样 fputc，
    ≥0x80 → '?'（M3 文档化 ASCII-only 策略）→ 8 字节原样落盘 → diff FAIL
```

第二差异（null 数组）：`JoinFromArray`（stringext2.cpp:164-166）`array==nullptr → return 空串` —— **文档化偏离**（MethodMap.String.cs:110-114 注释 + stringext2.cpp:159-161 注释明示 ".NET throws ArgumentNullException, documented in the header"）；生成代码 Cases_StringExt2.cpp:112 正常发射 try/catch(ArgumentNullException) 且匹配器就绪，但 runtime 不抛 → catch 不触发 → 输出行缺失。

## 3. "UTF-8 乱码"归因更正与真中文丢失环节

- 矩阵两代（M1/M2）都写 "多字节 UTF-16→UTF-8 乱码"：**不准确**。实际机制是 box-as-string 类型混淆；`00 2C 00 00` 的 NUL 半字节形态易被误读为"UTF-16 转换坏掉"，推测为原记录来源。stringext2.cs 全 ASCII（git 唯一提交 b827e1b，从未含中文）。
- **真中文的丢失环节核查（供未来中文 e2e 参考）**：
  - C# 源码 → IL：UTF-16 字面量，csc 保证无损 ✓
  - IL → 生成 C++：EscapeC（CodeGenerator.cs:2362-2368）只转义 `\ " \n \r \t`，中文**原样透传**（无 \uXXXX 转义）；File.WriteAllText 默认 UTF-8 无 BOM → C++ 源 = UTF-8；CMakeLists 已带 /utf-8（MSVC）+ g++ 默认 UTF-8 → 编译器解读一致 ✓
  - C++ 字面量 → runtime：intern_literal 非 ASCII 走 stringintern.cpp:317 → **il2cpp_string_new_from_utf8 完整 UTF-8 解码**（string.cpp:211-227，1-4 字节序列 + net48 对齐）✓
  - runtime → 控制台：**★ 唯一丢失环节 = write_line_s/write_line 的 ASCII-only 策略（runtime.cpp:23,39：≥0x80 → '?'）★** —— 中文（如 0x4E2D）到输出端变 '?'。这是 M3 文档化策略（注释明示 "until encoding-aware output arrives"），未来接 UTF-8 输出时应作为 console 域施工项。

## 4. 施工单（按域，未执行）

### R1 runtime 域（stringext2.cpp — 主修复，解 L19）
`JoinFromArray` 元素循环必须按数组元素实际类型分派：
- string[]（元素 klass == string 类）→ 现路径（AsString 直读）
- 其他引用数组（object[] 等）→ 每元素先验 klass：
  - 元素是 Il2CppString → 直读；
  - 元素是 boxed 值 → icall_object_to_string（或装箱值格式化）取文本再拼接；
  - 元素 null → 空串（.NET Join 对 null 元素 = 空串）
- 更粗粒度替代：`ArrayElementAsString` 前加 klass 检查，非字符串抛 InvalidCastException（.NET Join(object[]) 实际调 ToString，不抛 InvalidCast——**语义应以 ToString 为准**）
- 自测位：String.Join(object[]) 与 string[] 的 e2e 回归（stringext2 L19/L25/L27）

### R2 runtime 域（stringext2.cpp — 次修复，解 L23）
`JoinFromArray` null 数组 → `il2cpp_throw_argument_null_exception("values")`（对齐 .NET ArgumentNullException）；同步更新 il2cpp_string_ext2.h 头注释与 MethodMap.String.cs:110-114 的"文档化偏离"注释（若保留偏离则 e2e 永远差这一行）。

### T1 转译器域（MethodRefResolver.cs RawSignature — 根因之一）
RawSignature 消费 SZARRAY 参数的元素字节（0x0C object vs 0x0E string 可区分）→ `Join` 按元素类型精确分派：string[] → icall_string_join；object[] → LOUD 或新 object-join icall。**这是既有盲区（discover.md：SZARRAY 元素字节不消费，bitconvcase 同源）**，修一处解两例。

### C1 console 域（runtime.cpp — 未来中文输出前置）
write_line_s/write_line 的 ASCII-only 策略（≥0x80 → '?'）改为 UTF-8 编码写出（encode UTF-16 码元 → UTF-8 字节流）；注意与现有二进制 stdout 模式（console.cpp ConsoleStreamInitializer）兼容。此项非 stringext2 diff 的必要修复，但为一切真多字节输出的前置。

## 5. 证据索引

- [READ:docs/audit/e2e-full-matrix2.md:104,148] 矩阵归因原文
- [READ:%TEMP%\opencode\e2e-matrix2\percase\stringext2.csv] "gen exit=0 lines=41/42"
- [BASH:转译 EXIT=0（17:52 dll）] [BASH:构建 BUILD_EXIT=0] [BASH:运行 RUN_EXIT=0 41 行]
- [BASH:ACT L19 hex=00 2C 00 00 2C 00 00 00 0D] / 码元 0000 002C 0000 0000 002C 0000 0000 0000
- [READ:gen/src/Cases_StringExt2.cpp:90-97] object[] → box_int32×3 + icall_string_join(",", t21)
- [READ:src/runtime/src/stringext2.cpp:48-53] ArrayElementAsString = reinterpret_cast（无类型检查）
- [READ:src/runtime/src/stringext2.cpp:162-220] JoinFromArray（isRefElem 门 + 文档化 null→空串偏离）
- [READ:src/runtime/src/stringext2.cpp:164-166,159-161] null 数组返回空串注释
- [READ:src/il2cppc/Emit/MethodMap.String.cs:109-117] Join 只按 ps=[0x0E,0x1D] 分派（object[] 同形）
- [READ:src/runtime/src/runtime.cpp:15-27] write_line_s ASCII-only（<0x80 原样，≥0x80 → '?'）
- [READ:src/runtime/src/string.cpp:211-227] il2cpp_string_new_from_utf8 完整解码（字面量路径无损）
- [READ:src/runtime/src/stringintern.cpp:280-318] intern_literal 非 ASCII → string_new_from_utf8
- [READ:src/il2cppc/Emit/CodeGenerator.cs:2362-2368] EscapeC 无 UTF-8 转义、原样透传；:339 File.WriteAllText 默认 UTF-8
- [READ:src/runtime/src/boxingext2.cpp:194-221] box 布局 kBoxPayloadOffset=8（payload@8）
- [READ:src/runtime/include/internal/il2cpp_string.h:40-44] Il2CppString={klass,length,chars[1]}

## 6. 约束遵守

- 只读诊断 + 写本文档；未修改任何 src/tests 文件；未 commit
- 临时产物：%TEMP%\opencode\utf8diag_gen（转译）/ utf8diag_wrap（构建）/ stringext2_ref.txt / stringext2_act.txt / stringext2_act_fresh.txt
- 新鲜转译与 matrix2-era gen 逐字同形 → 归因对当前 dll（17:52）与 matrix2 运行（14:54）同时成立
