# Regex 语义对照审计：现有引擎 vs .NET 实现

日期：2026-08-04 · 域：regex_parse.cpp / regex_match.cpp / regex_api.cpp + 测试（独占）
方法：**net48 oracle 实测**（csc.exe + .NET Framework 4.8，探针 `%TEMP%\opencode\regexprobe\probe_audit.cs`）
对照 **.NET Core 源码**（dotnet/runtime main，官方 sparse checkout
`reference/netcore/src/libraries/System.Text.RegularExpressions/`）：
RegexParser.cs / RegexInterpreter.cs / RegexRunner.cs / Regex.cs
net48 与 .NET Core 8 探针 **36 项结果完全一致**（唯一差别：异常类型名 ArgumentException vs RegexParseException，行为同为抛错）。

## 1. 语法子集差异表（现有解析器 vs .NET RegexParser）

| # | 语法 | 现有实现 | .NET 行为 | 证据 | 状态 |
|---|------|---------|-----------|------|------|
| 1 | `[]` / `[^]` | 报错 UnclosedClass | 抛错（UnterminatedBracket，RegexParser.cs:807） | net48: cl_empty=THROW | ✅ 一致 |
| 2 | `[]]`（首 `]` 字面） | 类 = {`]`} | 类 = {`]`}（ScanCharClass:627 首 `]` 不闭合） | net48: cl_bracket_bracket=True | ✅ 一致 |
| 3 | `[^]]` | 否定类 {`]`} | 同（:627-658） | net48: cl_neg_bracket=True | ✅ 一致 |
| 4 | `[-a]` / `[a-]`（`-` 首尾字面） | `-` 字面 | 同（:775 仅 `-` 后非 `]` 才算范围候选） | net48（T2 已测） | ✅ 一致 |
| 5 | `[a-b-c]` | 范围+字面`-`+字面`c` | 同（:747-802） | net48: cl_abc=True | ✅ 一致 |
| 6 | `[z-a]` | 报错 InvalidClassRange | 抛错 ReversedCharacterRange（:767-769） | net48（T2 已测） | ✅ 一致 |
| 7 | `[a-\d]`（范围到简写） | 报错 InvalidClassRange | 抛错 ShorthandClassInCharacterRange（:668-670） | net48: cl_range_shorthand=THROW | ✅ 行为一致（错误码名不同，均抛） |
| 8 | `[\d-]`（简写后 `-`） | `-` 字面 | 同（:775 检查不成立 → 字面） | net48: cl_digit_dash=True | ✅ 一致 |
| 9 | `a{2` / `a{2,` / `a{2,3` / `a{2x` | **字面** `{` | 同——IsTrueQuantifier（:2150-2183）：`{`+数字+`}` 或 +`,`+数字+`}` 才算量词；扫到串尾/非数字 → 字面 | net48: curly_2/2c/2x=True；net8 同 | ✅ 一致 |
| 10 | `{2}` 无原子 | 报错 QuantifierNothing | 抛错 QuantifierAfterNothing（:434-438） | net48（T2 已测） | ✅ 一致 |
| 11 | `a**` 嵌套量词 | 报错 QuantifierNothing | 抛错 NestedQuantifiersNotParenthesized（:436-437） | net48（T2 已测） | ✅ 一致 |
| 12 | `{2,1}`（n>m） | 报错 InvalidRepeat | 抛错 ReversedQuantifierRange（:515-518） | net48: curly_rev=THROW | ✅ 一致 |
| 13 | `{9999999999}`（溢出） | 报错 InvalidRepeat | 抛错 QuantifierOrCaptureGroupOutOfRange（ScanDecimal:1541-1544） | net48: curly_huge=THROW | ✅ 一致 |
| 14 | `\a` `\e` 转义 | **报错（修复前）→ \x07/\x1B** | 支持（ScanCharEscape:1665-1670） | net48: esc_a/esc_e=True；net8 同 | 🔧 **已修复** |
| 15 | `[\b]` 类内退格 | **报错（修复前）→ \x08** | 类内 `\b`=退格（:1667-1668）；顶层 `\b`=词边界 | net48: cl_b=True | 🔧 **已修复** |
| 16 | `\~` 非 word 字符转义 | **报错（修复前）→ 字面** | 字面（ScanCharEscape 默认分支 :1683-1688：word 字符抛错，其他返回该字符） | net48: esc_tilde=True；esc_q=THROW | 🔧 **已修复** |
| 17 | `\q` 未识别转义 | 报错 UnrecognizedEscape | 抛错（q 为 word 字符） | net48: esc_q/cl_q=THROW | ✅ 一致 |
| 18 | 类内 `\D\W\S` | **取正集（修复前）→ 补集** | 补集成员（ScanCharClass:664-698 AddDigit(ch=='D') 等） | net48: cl_D_digit=False / cl_D_letter=True / cl_negD_digit=True / cl_a_plus_D=False | 🔧 **已修复** |
| 19 | 类内 `\d\w\s` | 正集 | 正集（同 :672/:684/:697） | net48: cl_D_plus_d=True | ✅ 一致 |
| 20 | `$` 非 Multiline | 串尾 **或末尾 `\n` 前** | 同（EndZ，RegexInterpreter.cs:809-818：`runtextpos<len-1 || (pos<len && text[pos]!='\n')` → 失败） | net48: dollar_nl=True / dollar_crnl=False（`a$` 不匹配 `"a\r\n"`） | ✅ 一致 |
| 21 | `$` Multiline | 串尾或任意 `\n` 前 | 同（Eol，:750-758） | net48: dollar_ml=True | ✅ 一致 |
| 22 | `^` 非 Multiline | 仅串首 | 同（Beginning，:793-799） | T2 已测 | ✅ 一致 |
| 23 | `^` Multiline | 串首或 `\n` 后 | 同（Bol，:739-748） | T2 已测 | ✅ 一致 |

## 2. 引擎语义差异表（现有引擎 vs .NET 解释器）

| # | 语义 | 现有实现 | .NET | 证据 | 状态 |
|---|------|---------|------|------|------|
| 1 | 匹配定位 | leftmost bumpalong 0..len，首成功即胜 | 同（RegexInterpreter.Scan:349-366） | — | ✅ 一致 |
| 2 | 交替 | 左优先 + 跨组回溯（CPS） | 同（回溯解释器，`(a|ab)c` → 组1="ab"） | T2 probe | ✅ 一致 |
| 3 | 贪婪量词 | 贪心 + 递减回溯 | 同（Track 栈回溯） | T2 测试 | ✅ 一致 |
| 4 | 捕获组 | 参与=start≥0；未参与=-1；`(ab)+` 组=最后一次 | 同（crawl 栈语义） | T2 测试 | ✅ 一致 |
| 5 | 空迭代 | 空迭代满足 min 后终止 | 同（空匹配不推进） | — | ✅ 一致 |
| 6 | 迭代空匹配推进 | 空匹配后 pos+1 | 同（Regex.cs:725-731 `match.Length==0 → bump 1`） | — | ✅ 一致 |
| 7 | `.` | 非 Singleline 排除 `\n` | 同（Parser:415-428 Notone('\n')） | T2 测试 | ✅ 一致 |
| 8 | 步数预算 10M | 超限确定性 no-match | **无预算**——灾难回溯挂死 | net48 `(a*)*b` vs 50a 120s 超时（T2） | ⚠ 有意偏差（docs §2.4，防挂死） |
| 9 | 匹配超时 | 无 | Regex.MatchTimeout 默认 ~15s 也防挂死 | — | ⚠ 偏差：行为等价（都防挂死），机制不同 |

## 3. 修复清单（全部有 net48 实测 + .NET 源码双证据）

1. **类内 `\D\W\S` = 补集**（regex_parse.cpp：ParseClassBody 内建类分支）
   修复前：`[\D]` 匹配数字（错误）；修复后：补集区间，`[\D]` 匹配非数字、`[^\D]` 匹配数字。
   实现：`AddBuiltinComplementRanges()` 硬编码补集（对 char16_t 全域减去本引擎 ASCII 集；\W 补集含 0x0060 等 5 段）。
2. **`\a`(0x07) `\e`(0x1B) 转义 + 类内 `[\b]`(0x08)**（ControlChar 扩展；顶层 `\b` 仍=词边界报错，属子集裁剪）。
3. **非 word 字符转义 = 字面**（`\~` 匹配 `~`；word 字符 `\q` 仍报错）——ParseAtom + ParseClassBody 默认分支。
4. **Bare Repeat 必须运行 continuation**（regex_match.cpp MatchRepeat 裸路径）——修复前 `(\d+)-(\d+)` 只匹配数字、组全 -1、序列余部被跳过（组内 Repeat 的 groupCont/序列余部从未执行；`(ab)+` 正常是因 Repeat 在组外，T2 未覆盖组内形态）。修复后与 children 路径同构：`Continue(cont, p)` 失败则递减让出重试。net48 对照：`(\d+)-(\d+)` vs "abc 12-34 def" → "12-34"、组 "12"/"34"。

## 4. 已知范围偏差（故意裁剪，未修——docs §1.4/§3.4 白名单）

| 项 | .NET 行为 | 本引擎 | 备注 |
|----|-----------|--------|------|
| `\d\w\s` 集合 | Unicode 类别集（é 是 `\w`） | ASCII-only | docs §1.4 明确 ASCII；修需 Unicode 类别表（独立任务） |
| IgnoreCase 折叠 | 完整不变量文化/Unicode 折叠 | ASCII 折叠 | **net48 实测折叠 é/É、æ/Æ、œ/Œ**（probe ic_ea/ic_ae/ic_oe=True）——T2 探针曾误报"不折叠"（UTF-8 无 BOM 被 csc 按 ANSI 误读，本任务用 \u 转义复测纠正）。修需移植 RegexCaseEquivalences 大表（独立任务） |
| `(?:)` 非捕获组 / 前瞻后顾 / 懒量词 / `\b` 词边界 / `\p{}` / `\x` / `\u` / `\1` 反向引用 / `(?<name>)` / 类减法 `[a-[b]]` / `\A\Z\z` / `\cX` | 全部支持 | 报错 UnsupportedConstruct/LazyQuantifier | docs §3.4 白名单裁剪 |
| 错误报告 | 抛 ArgumentException | 返回错误码，regex_api.cpp 转抛 ArgumentException | 契约设计（regex_ast.h） |

## 5. 验证证据

- [BASH] g++ -std=c++17 -Wall -Wextra -fexceptions -fno-rtti（regex_parse.cpp + regex_match.cpp + test_regex_match.cpp）→ 0 警告 0 错误
- [BASH] test_regex_match.exe → PASS（原 82 断言 + 新增 24 断言全绿：审计修复 3 项 + 引擎 bare-repeat 回归 4 项）
- [BASH] test_regex_api.exe（g++ 全链接，排除在途 timeext2.cpp/numberformat.cpp）→ **all checks passed**（修复前 4 个 FAIL：match 组捕获——引擎 bare-repeat bug 所致；API 层本身无缺陷）
- [BASH] net48 probe_audit.exe：36 项输出与期望逐一吻合（上表）
- [BASH] .NET Core 8（dotnet run netcoreprobe）：36 项与 net48 完全一致
- [BASH] cmake 全量构建：regex 文件零错误；被在途文件阻塞——timeext2.cpp:122/212 `ParsedFields` 重复定义（mtime 4:19）、numberformat.cpp:383 `kGuardDigits` 未声明（mtime 4:48）——均为并行代理域外文件，未触碰

## 6. 参考源（dotnet/runtime main，2026-08-04）

- 官方 sparse checkout：`reference/netcore/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/`（并行代理已建立，commit 93add0d；同版本备份 `%TEMP%\opencode\regexaudit\netcore-src/`）
- RegexParser.cs（94019B）：量词/类/转义/锚点解析；IsTrueQuantifier:2150、ScanCharClass:574、ScanCharEscape:1655、ScanDecimal:1531
- RegexInterpreter.cs（47057B）：Bol:739 / Eol:750 / Beginning:793 / EndZ:809 / Scan bumpalong:349
- RegexRunner.cs + Regex.cs：迭代空匹配推进 Regex.cs:725
- net48 referencesource 未拉取（raw.githubusercontent 404）——net48 行为以 csc.exe 实测为准，与 .NET Core 源码无冲突
