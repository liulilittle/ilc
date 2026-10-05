# Regex 域设计文档（System.Text.RegularExpressions）

> 定位：本文档是 ILC 运行时（src/runtime）新增 `System.Text.RegularExpressions` 基础域的
> **设计先行文档**——只定义范围 / 架构 / 契约，不实现。产出的任务卡（§5）可直接拆分为
> 3 个并行实现任务。
>
> 语义基线：**.NET Framework 4.8（net48）**——项目 BCL 对齐基线（同 versionuri.h 的
> SEMANTIC BASELINE 惯例）。所有裁剪与行为分歧在本文档**显式声明**（§1.4 汇总表）。
>
> 现状快照：`grep "Regex" src/` 零匹配——该域尚未接线；本文档是 regex 域的第一份设计输入。
> 接线模式参考：`MethodRefResolver.BclResolveCore` 按 owner 全名分派
> （src/il2cppc/Emit/MethodRefResolver.cs:216-241，`System.Text.StringBuilder` →
> `StringBuilderDispatch` 先例在 715）。
>
> 配套文档：`docs/architecture/overview.md`（ILC 总览）、`docs/REQUIREMENTS.md`（铁律）。

---

## 1. 范围裁剪

### 1.1 API 面

| 成员 | v1 | 说明 |
|---|---|---|
| `Regex.IsMatch(string, string[, RegexOptions])` | ✅ 静态 | 直接接线 |
| `Regex.Match(string, string[, RegexOptions])` | ✅ 静态 | 返回值裁剪：`string[]` 而非 Match 对象（§1.4-1） |
| `Regex.Matches(string, string[, RegexOptions])` | 🟡 静态 | 语义定义见 §2.5；C API 可选扩展（§4.6），v1 可不接 |
| `Regex.Replace(string, string, string[, RegexOptions])` | ✅ 静态 | 全量替换（无 count 重载） |
| `Regex.Split(string, string[, RegexOptions])` | ✅ 静态 | v1 忽略捕获组（§1.4-2） |
| `Regex` 构造函数 + 实例方法（`new Regex(...).Match/IsMatch/...`） | ❌ v2 | 需 Regex 句柄/描述符类型（§1.4-7） |
| `Regex.Replace(string, string, MatchEvaluator)` / `Regex.Split(string, int)` | ❌ | 回调委托 / count 重载，v2+ |

> v1 只接**静态重载**：均可经 `MethodRefResolver` 直接映射到 C API，无对象生命周期问题。
> `Matches` 标 🟡：其内部迭代逻辑（§2.5）在 T2/T3 一并实现，仅 C API 入口（§4.6）可选挂接。

### 1.2 RegexOptions 子集

**IN（三个位，net48 常量值）：**

| 位 | 名称 | 值 | 语义 |
|---|---|---|---|
| 0 | IgnoreCase | 0x01 | 匹配时 ASCII 大小写折叠（§1.4-3） |
| 1 | Multiline | 0x02 | `^`/`$` 变为行首/行尾（§3） |
| 2 | Singleline | 0x04 | `.` 也匹配 `\n` |

**OUT（白名单外一律拒绝）：** Compiled(0x08，AOT 无运行时编译)、ExplicitCapture(0x10)、
CultureInvariant(0x20)、IgnorePatternWhitespace(0x40)、RightToLeft(0x80)、ECMAScript(0x100)。
传入非白名单位组合 → `il2cpp_throw_not_supported_exception`（loud 失败，与 BclResolve 的
NotSupported 模式一致，杜绝静默错误语义）。

### 1.3 明确不做（及理由）

| 特性 | 理由 |
|---|---|
| 后台编译 / `Regex.CacheSize` LRU 缓存 | v1 每次调用独立编译（§2.1）；缓存是后续性能优化项，不是语义项 |
| `MatchTimeout` | net48 默认 `InfiniteMatchTimeout`；裁剪后以**内部步数预算**兜底（§2.4） |
| Balancing groups `(?(name)...)` / 条件分支 / 递归 | 解析器与捕获模型复杂度大增 |
| 环视 `(?=)` `(?!)` `(?<=)` `(?<!)` | 需要正向/反向子匹配；v2 候选 |
| 反向引用 `\1` `\k<name>` | 需运行期捕获读取；v2 候选 |
| 命名组 `(?<name>...)` `(?'name'...)` | 组查找表 + 命名解析 |
| 原子组 `(?>...)` | 子集不含惰性量词与嵌套回溯热点时可后补；预算护栏已覆盖防护面 |
| 内联选项 `(?i)` `(?-i)` | 语法子集外 |
| Unicode 类 `\p{...}` `\P{...}`、词边界 `\b` `\B` | `\d\w\s` 已裁为 ASCII（§1.4-4） |
| 懒量词 `*?` `+?` `??` `{n,m}?` | 贪心唯一 → 回溯器单一路径，语义与 net48 贪心分支一致 |
| `RightToLeft` | 全方向性支持 |
| 替换 token `${name}` `$&` `` $` `` `$'` | replacement 仅 `$0`-`$9`、`$$`（§1.4-6） |

### 1.4 裁剪声明汇总（与 net48 的行为分歧，全部显式化）

1. **Match 返回值裁剪**：`il2cpp_regex_match` 返回 `string[]`（`[0]`=整体匹配串，
   `[1..n]`=捕获组串，`-1` 位置表示组未参与），配合 `outStart/outLength` 输出匹配位置。
   不建 `Match` / `Group` / `Capture` 类型描述符——v1 不映射 .NET 的 Match 对象图。
2. **Split 忽略捕获组**：net48 的 `Regex.Split` 会把捕获组内容插入结果数组；v1 裁剪掉
   （低使用率，实现可少一条路径）。文档化为分歧。
3. **IgnoreCase 仅 ASCII 折叠**：`'A'-'Z'` ↔ `'a'-'z'`；非 ASCII 字符不折叠。
   net48 是 culture-aware（当前 culture 的 ToLower 表）。此分歧同时保证确定性。
4. **`\d \w \s` 为 ASCII 集合**：`\d=[0-9]`，`\w=[A-Za-z0-9_]`，`\s=[ \t\r\n\f\v]`；
   net48 为 Unicode 感知（`\w` 含 `\p{L}\p{Mn}\p{Nd}\p{Pc}` 等）。补类 `\D \W \S` 相应取补。
5. **无超时语义**：net48 默认不设超时，灾难回溯（§2.4）会真正指数挂死；v1 用步数预算
   在确定性边界失败（返回 no-match），属于**性能护栏**，非 net48 语义——文档化。
6. **replacement 支持 `$0`-`$9`（组引用）与 `$$`（字面 `$`）**：`$10` 及以上按 net48
   ParseReplacement 规则——两位数字中组号存在则取该组，否则首位组 + 其余字面
   （如组 10 不存在时 `$10` = 组 1 + 字面 `0`）；其余 `$` 序列按字面保留。
7. **Regex 实例 API 推迟 v2**：运行时对象即 `Il2CppObject*`（il2cpp_string.h:39-44），
   无 Regex 类描述符；实例 API 需要新描述符或句柄映射表，v2 单独设计。
8. **无缓存**：每次调用重新解析/匹配（简单、线程安全、无共享状态）；缓存后补。

---

## 2. 架构：AST → 直接模拟（无 NFA 编译）

### 2.1 管线

```
pattern (char16_t*) + options (int32_t)
   │  RegexParser（§3 语法子集）
   ▼
AST（RegexNode 树，regex_ast.h 契约）
   │  RegexBacktracker（§2.3 递归回溯）
   ▼
MatchResult（start / length / 捕获组列表）
```

- **无 NFA 编译**：子集小、语义面窄，直接 AST 递归回溯是 KISS 最优解。
- **无 opcode VM**：coreclr 的 `RegexWriter`(opcode) + `RegexInterpreter`(栈式 VM) 是参考
  而非必选——coreclr 自身在 .NET 5+ 已把编译路径改为**直接遍历 RegexNode 树**（PR #62318
  删除旧 opcode 代码生成，见 §6），即为"AST 直走"路线的先例。
- **每次调用独立编译**，无共享缓存（§1.4-8）：天然线程安全（D13：无共享可变状态，
  无需锁）。
- 三阶段与 3 张任务卡（§5）一一对应。

### 2.2 AST 定义（regex_ast.h 契约，T1 产出）

```cpp
enum class RegexNodeKind {
    Empty,       // 空序列（空模式 ""）
    Char,        // 单字面量字符（受 IgnoreCase 折叠）
    Any,         // '.'：除 \n 外任意；Singleline 时含 \n
    Set,         // 字符类 [...]（含否定与 \d\w\s 展开）
    Sequence,    // 顺序子节点列表
    Alternation, // 分支列表（leftmost-first，§2.3）
    Repeat,      // 量词：child 重复 [min,max]（贪心）
    Group,       // 捕获组：child + groupIndex（1 起；0=整体）
    Anchor,      // ^ $（Start/End/LineStart/LineEnd）
};

struct RegexNode {
    RegexNodeKind kind;
    char16_t     ch;          // Char
    bool         ignoreCase;  // Char/Set 在 IgnoreCase 下折叠（ASCII）
    bool         negate;      // Set
    Range*       ranges;      // Set：[lo,hi] 含端点，有序不重叠
    int32_t      rangeCount;
    RegexNode*   child;       // Repeat / Group
    RegexNode**  children;    // Sequence / Alternation
    int32_t      childCount;
    int32_t      min, max;    // Repeat：max=-1 = ∞；{n} = {n,n}
    int32_t      groupIndex;  // Group
    AnchorKind   anchor;      // Anchor
};
```

所有权：T1 一次性 arena 分配整棵树，T2 只读，`RegexAstFree()` 整体释放（C++ 侧 RAII 包装）。
AST 头文件是 **T1 的产出契约**，T2/T3 依赖它并行编译（§5 依赖图）。

### 2.3 匹配算法伪码（回溯式）

整体入口：bumpalong 主循环——对每个候选起点尝试完整匹配；`^`（非 Multiline）锚定时
只试起点 0；Multiline 时试起点 0 与每个 `\n` 之后。leftmost 匹配（非最长）。

```
function MatchOnce(root, text, ctx) -> MatchResult | NoMatch:
    for start in candidateStarts(root, text):     # §2.3.1 起点枚举
        ctx.beginOverall(start)
        if p = MatchNode(root, start, ctx):
            return MatchResult(start, p, ctx.captures)
    return NoMatch

function MatchNode(node, pos, ctx) -> pos' | FAIL:
    switch node.kind:
      Empty:               return pos
      Char:                return Fold(node.ch) == Fold(text[pos]) ? pos+1 : FAIL
      Any:                 return (pos < len) && (text[pos] != '\n' || singleline) ? pos+1 : FAIL
      Set:                 return SetContains(node, text[pos]) ? pos+1 : FAIL
      Anchor:              return AnchorHolds(node, pos, text, options) ? pos : FAIL
      Group:               ctx.beginCapture(node.groupIndex, pos)
                           if p = MatchNode(node.child, pos, ctx):
                               ctx.endCapture(node.groupIndex, pos, p)   # 定稿 (start,len)
                               return p
                           ctx.truncateCaptures(mark)                    # 失败回滚
                           return FAIL
      Sequence:            return MatchSequence(node.children, 0, pos, ctx)
      Alternation:         for branch in node.children:                  # leftmost-first
                               mark = ctx.captureDepth()                 # 快照
                               if p = MatchNode(branch, pos, ctx): return p
                               ctx.truncateCaptures(mark)                # 该分支捕获作废
                           return FAIL
      Repeat:              return MatchRepeat(node, pos, ctx, 0)

function MatchSequence(children, i, pos, ctx) -> pos' | FAIL:
    if i == childCount: return pos
    if children[i].kind == Repeat:
        return MatchRepeat(children[i], pos, ctx, 0)   # Repeat 回溯可回到此处重试后续
    p = MatchNode(children[i], pos, ctx) or FAIL
    return MatchSequence(children, i+1, p, ctx)

function MatchRepeat(node, pos, ctx, count) -> pos' | FAIL:      # 贪心 + 递减回溯
    if count < node.max:
        mark = ctx.captureDepth()
        if p = MatchNode(node.child, pos, ctx):                  # 再迭代一次
            if r = MatchRepeat(node, p, ctx, count+1): return r
            ctx.truncateCaptures(mark)                           # 撤销本次迭代捕获
    if count >= node.min:                                        # 迭代次数合法 → 继续后续
        return MatchSequence(rest, ...)                          # 经 Repeat 所在 Sequence 返回
    return FAIL
```

要点（实现时由 T2 复核 net48 行为）：

- **leftmost-first 交替**：分支按书写序尝试，首个成功即返回——`a|ab` 对 `"ab"` 匹配 `"a"`。
- **贪心重复 + 捕获撤销**：`(a(b)?)+` 回溯时，已废弃迭代内的组捕获必须一并回滚
  （`captureDepth` 快照 + `truncateCaptures` 增量撤销，非全量复制）。
- **组语义**：组成功结束才定稿捕获 `(start,len)`；组未参与 → `start=-1`。
- **空匹配推进**（Matches/Replace/Split 共享，§2.5）：空匹配后起点 +1 继续，防死循环。
- **预算护栏**：每次 `MatchNode` 调用计 1 步，超 `kMaxMatchSteps`（默认 10,000,000，
  头文件常量）→ 整体 NoMatch（§1.4-5）。

#### 2.3.1 候选起点枚举

| 模式 | 候选起点 |
|---|---|
| 非锚定 | 0..len-1 全部（首节点为字面量时可先扫描首个匹配字符再试，可选优化） |
| `^`（默认） | 仅 0 |
| `^`（Multiline） | 0 与每个 `\n` 之后 |

### 2.4 catastrophic backtracking：风险与限制（明确标注）

- 风险真实存在：`(a*)*b` 对 `"aaaaaaaaaa..."` 呈指数时间（coreclr 官方文档同款警示）。
- v1 缓解三层：
  1. 文档要求：语法子集外结构（环视等）在**解析期**直接抛 `ArgumentException`，错误早暴露；
  2. 实现建议：模式极小化提示（如 `(a+)+` 提示改 `a+`）不入文档，但任务卡 T2 测试含经典
     灾难用例验证预算生效；
  3. **步数预算**：确定性失败（no-match），绝不让调用方挂死（net48 默认会挂死——§1.4-5）。
- 限制声明：预算耗尽时 v1 **不抛异常**（net48 设 Timeout 时抛 `RegexMatchTimeoutException`），
  返回 no-match；此分歧已文档化。后续如需要可加异常路径，接口已预留。

### 2.5 迭代语义（Matches / Replace / Split 共享）

- 匹配**不重叠**：下一次搜索从上次匹配 `end` 起（bumpalong 内循环）。
- **空匹配推进**：若本次匹配为空（`start==end`），下一次搜索从 `start+1` 起（net48 同，
  防止 `""` 模式死循环）。
- `Replace`：按迭代序逐一替换，替换结果拼接为新 `string`。
- `Split`：按迭代序切分，分隔符本身不进结果；v1 不含捕获组（§1.4-2）。

---

## 3. 语法子集

### 3.1 支持语法表（每项给出 net48 语义）

| 语法 | 语义 |
|---|---|
| 普通字符（非元字符） | 逐 `char16_t` 字面匹配（UTF-16 单元，代理对按 2 单元——net48 长度语义一致） |
| `.` | 任意字符**除 `\n`**；`Singleline` 时含 `\n` |
| `\d` | `[0-9]`；`\D` = 补集（ASCII 裁剪，§1.4-4） |
| `\w` | `[A-Za-z0-9_]`；`\W` = 补集（ASCII 裁剪） |
| `\s` | `[ \t\r\n\f\v]`；`\S` = 补集（ASCII 裁剪） |
| `[...]` | 字符类：单字符与范围；`[^...]` 否定（§3.2 细则） |
| `^` | 串首；`Multiline` 时串首与每个 `\n` 之后（行首） |
| `$` | **串尾**（非 Multiline 仅匹配 end-of-string，`"a\n"` 中 `$` 不匹配 `\n` 前）；`Multiline` 时串尾与每个 `\n` 之前（行尾） |
| `*` | 前项 0..∞（贪心） |
| `+` | 前项 1..∞（贪心） |
| `?` | 前项 0..1（贪心） |
| `{n}` | 前项恰 n 次（贪心） |
| `{n,}` | 前项 n..∞（贪心） |
| `{n,m}` | 前项 n..m（贪心；**非法区间 `{2,1}` → `ArgumentException`**） |
| `( )` | 捕获组；组编号 = 左括号出现顺序（1 起），组 0 = 整体匹配 |
| `\|` | 交替（leftmost-first，§2.3） |
| `\` 转义 | 元字符转义 `\. \* \+ \? \{ \} \[ \] \( \) \| \^ \$ \\`；控制转义 `\n \t \r \f \v`；**未识别转义（如 `\q`）→ `ArgumentException`**（net48 同） |

### 3.2 字符类细则

- 类内转义：`\d \w \s`（与补类）及 `\n \t \r \f \v` 在类内有效。
- `-`：`[a-z]` 为范围；**首或尾位置为字面**（`[-a]`、`[a-]`）；非法范围（`[z-a]`、
  `[a-b-c]`）→ `ArgumentException`（net48 同）。
- `]`：类内首字符为字面（`[]]` 合法）；类外未配对 `[` → `ArgumentException`。
- 空类 `[]` = 永不匹配的集合；`[^]` = 任意字符（实现时以 net48 实测复核，任务卡 T1 标注）。
- 类内 `^` 非首字符为字面。

### 3.3 RegexOptions 对语法的影响（汇总）

| 选项 | 影响对象 |
|---|---|
| IgnoreCase | `Char` 与 `Set` 匹配时 ASCII 折叠（§1.4-3）；`\d\w\s` 类不受影响（net48 同） |
| Multiline | `^`/`$` 变为行首/行尾（§3.1） |
| Singleline | `.` 含 `\n`（§3.1） |

### 3.4 明确不解析（解析期抛 `ArgumentException`，错误早暴露）

`(?:`（非捕获组）、`(?=` `?!` `(?<=` `(?<!)`（环视）、`(?>`（原子组）、`(?<`（命名组）、
`(?i)` 类内联选项、`\1` 类反向引用、`\b \B`、`\p{...}`、`\x` `\u` 十六进制转义、懒量词后缀 `?`。

---

## 4. C API 草案

### 4.1 命名与位置

- 用户指定命名：`il2cpp_regex_*`（**flat 命名，非 `icall_` 前缀**——同
  `il2cpp_bitconverter_*` 先例；transpiler 侧经 MethodRefResolver 映射到
  `il2cpp::runtime::` 命名空间）。
- 计划文件：`src/runtime/include/internal/regex.h`（API 声明，含 net48 baseline 注释块，
  风格同 versionuri.h）、实现 `src/runtime/src/regex_api.cpp`（§5 任务卡）。
- 字符串参数用托管 `Il2CppString*`（布局：`obj` + `length` + `chars[]`，直接读字段，
  il2cpp_string.h:39-44），返回用 `Il2CppString*` / `Il2CppArray*`（元素为
  `Il2CppString*` 的 object 数组）——与生成的 C++ 调用侧引用槽一致。

### 4.2 函数签名与返回值语义

```cpp
namespace il2cpp { namespace runtime {

// 1 = 匹配成功；0 = 不匹配。
// pattern 语法非法 → ArgumentException；pattern/input 为 null → ArgumentNullException。
int32_t il2cpp_regex_is_match(Il2CppString* pattern, Il2CppString* input,
                              int32_t options);

// 返回 string[]：[0] = 整体匹配串，[1..n] = 捕获组串。
// 无匹配 → 空数组（长度 0），且 *outStart = *outLength = -1。
// outStart / outLength 可为 null（调用方不需要位置时）。
// 组未参与 → 对应元素为 null（net48 Match.Groups[i].Success == false 的数组化表示）。
Il2CppArray* il2cpp_regex_match(Il2CppString* pattern, Il2CppString* input,
                                int32_t options,
                                int32_t* outStart, int32_t* outLength);

// 返回替换后的新 string（全量替换，net48 静态 Replace 语义）。
// replacement 支持 $0-$9（组引用）与 $$（字面 $），其余 $ 序列按字面保留（§1.4-6）。
Il2CppString* il2cpp_regex_replace(Il2CppString* pattern, Il2CppString* input,
                                   Il2CppString* replacement, int32_t options);

// 返回 string[]：按匹配切分（§2.5）；无匹配 → 单元素数组 [原串]。
// v1 不含捕获组内容（§1.4-2）。
Il2CppArray* il2cpp_regex_split(Il2CppString* pattern, Il2CppString* input,
                                int32_t options);

} } // namespace il2cpp::runtime
```

### 4.3 错误语义（net48 对齐）

| 条件 | 行为 |
|---|---|
| pattern 语法非法 | `il2cpp_throw_argument_exception`（net48 Regex ctor 抛 ArgumentException） |
| pattern / input / replacement 为 null | `il2cpp_throw_argument_null_exception`（net48 语义） |
| options 含白名单外位 | `il2cpp_throw_not_supported_exception`（loud，§1.2） |
| 步数预算耗尽 | 返回 no-match（**不抛**；§1.4-5 / §2.4 文档化分歧） |

异常 helper 名：il2cpp_exception_ext.h:46,51。

### 4.4 内存与线程

- 每次调用独立编译 + 匹配，**无共享状态** → 多线程可并行调用（D13：无锁）。
- 中间 AST 与匹配上下文在调用栈内/调用内分配，返回前释放；返回的 string/数组为 GC 对象
  （`il2cpp_string_new(const char16_t*, int32_t)` 创建，il2cpp_string.h:50），遵循运行时
  "调用方保持可达"规则。

### 4.5 接线建议（MethodRefResolver）

- `BclResolveCore` 增加 `case "System.Text.RegularExpressions": return RegexDispatch(name, ps, count);`
  （先例：`StringBuilderDispatch`，MethodRefResolver.cs:715）。
- 静态方法名映射（`RegexOptions` 参数按可选中继——签名 byte 模式区分）：

| 托管方法 | C++ 目标 | 参数 cast |
|---|---|---|
| `IsMatch(string,string[,RegexOptions])` | `il2cpp_regex_is_match` | string：无 cast（托管引用槽）；options：`"i"` |
| `Match(...)` | `il2cpp_regex_match` | 同上 + 返回值 `ReturnsRef=true`，`ReturnTypeName="System.String[]"` |
| `Replace(string,string,string[,RegexOptions])` | `il2cpp_regex_replace` | 同上 + `ReturnTypeName="System.String"` |
| `Split(...)` | `il2cpp_regex_split` | 同上 + `ReturnsRef=true` |
| `Matches(...)` | §4.6（v1 可选） | — |
| Regex ctor / 实例方法 | 抛 `NotSupportedException`（loud，v1 范围外 §1.1） | — |

- 返回 bool → `int32_t`（`ResultKind` 默认）；返回 string/数组 → `ReturnsRef=true`。
- 具体 cast 码与签名 byte 解码细节以 T3 实现时核对为准（raw param bytes 约定见
  memory.md:11；I() 构造见 MethodRefResolver.cs:12-55）。

### 4.6 Matches 可选扩展（v1 可不接）

```cpp
// 返回 string[][]：每行 = [整体匹配串, 组1, ..., 组n]（与 il2cpp_regex_match 布局一致）。
Il2CppArray* il2cpp_regex_matches(Il2CppString* pattern, Il2CppString* input,
                                  int32_t options);
```

语义 = §2.5 迭代规则。若 v1 不接，`Matches` 的转译调用抛 NotSupported（loud）。

---

## 5. 任务卡（3 张，可并行）

### 依赖图

```
T1 词法+解析器 ──(regex_ast.h 契约)──▶ T2 匹配引擎 ──(engine 声明)──▶ T3 API+接线
        （T1 独立；T2 依赖 T1 的 AST 头；T3 依赖 T2 的引擎入口声明）
```

并行策略：**契约头文件先行**——T1 首先产出 `regex_ast.h`（AST 定义 + `RegexAstFree` 声明 +
`RegexParse` 声明），T2 按契约并行实现；T2 产出 `regex_match.h`（引擎入口声明）后 T3 可并行。
CMake 对 `src/*.cpp` 与 `tests/runtime/test_*.cpp` 均 GLOB 自动发现（memory.md:6），
新增文件无需改构建脚本。测试框架：`Expect + g_failures + 非零退出码`（test_*.cpp 惯例）。

---

### T1 词法 + 解析器（AST 定义 + 测试）

- **文件域**：新建 `src/runtime/include/internal/regex_ast.h`（AST 契约，含语义注释）、
  `src/runtime/src/regex_parse.cpp`；新建 `tests/runtime/test_regex_parse.cpp`。
- **输入**：`const char16_t* pattern` + `int32_t length` + `int32_t options`。
- **输出**：`RegexAst*`（arena 树，§2.2 骨架）或抛 `ArgumentException`（§3.4 白名单外结构、
  未闭合 `[`/`(`/`{`、非法区间、未识别转义、非法类范围）。
- **测试要点**：
  - §3.1 每项语法正例 → AST 形状断言（kind/字段逐点核对，含组编号序、嵌套组、`{n,m}` 解析）；
  - 边界：空模式 → Empty 根；`[]` 永不匹配集合、`[^]` 任意字符（**实现时以 net48 实测复核**）；
    `[-a]`/`[a-]`/`[a-b-c]`/`[z-a]`；`{2,1}`、`{n}` 超大数溢出（`INT32_MAX` 上限）；
    `\q`、`(?=`、`\1`、`\b` 抛错；`\\` 双反斜杠折叠；
  - options 白名单校验（§1.2：非法位抛 NotSupported；IgnoreCase/Multiline/Singleline
    各自落到 AST 字段）；
  - 错误路径全部断言 `ArgumentException` 且不泄漏（`RegexAstFree` 走查）。

### T2 匹配引擎（回溯器 + 捕获组）

- **文件域**：新建 `src/runtime/src/regex_match.cpp` + `src/runtime/include/internal/regex_match.h`
  （引擎入口声明：`RegexMatchResult Match(...)` 或等价的 C 函数）；新建
  `tests/runtime/test_regex_match.cpp`。
- **输入**：`RegexAst*`（T1 契约）+ `const char16_t* input` + `int32_t length` + options。
- **输出**：`MatchResult`（start / length / 组捕获 `(start,len)` 列表，未参与组 `start=-1`）；
  无匹配标志；预算耗尽 → 无匹配（§2.4）。
- **测试要点**（全部以 net48 行为为基准断言）：
  - 经典等价表：`a|ab` vs `"ab"` → `"a"`（leftmost-first）；`ab*` vs `"abbc"` → `"abb"`；
    `\d+` vs `"123abc"` → `"123"`（leftmost 非最长）；`a*ab` vs `"aaab"` → `"aaab"`（回溯）；
  - 锚点矩阵：`^ $` × {默认, Multiline} × 输入（含 `"a\nb"` 行内 `$`/`^` 各位置）；
    `.` × {默认, Singleline} × `"a\nb"`；
  - 捕获撤销：`(a(b)?)+` vs `"ab"`（组2=`"b"`）与 `"a"`（组2 未参与 `start=-1`）；
    回溯废弃迭代的捕获必须消失（`captureDepth` 快照语义）；
  - 空匹配：`""` 模式 vs `"abc"` → 起点 0 空匹配；迭代推进规则（§2.5）；
  - IgnoreCase：ASCII 折叠矩阵（`[A-Za-z]` 双向）；非 ASCII 不折叠（分歧断言）；
  - 灾难用例：`(a*)*b` vs `"aaaa...a"`（长度 50）在预算内**确定性返回无匹配**（性能/预算测试）；
  - 长输入 smoke：10 万字符，线性量级完成（预算护栏不误伤正常模式）。

### T3 字符串操作 API + 接线建议

- **文件域**：新建 `src/runtime/src/regex_api.cpp` + `src/runtime/include/internal/regex.h`
  （§4 四个 C API 声明，net48 baseline 注释块风格同 versionuri.h）；新建
  `tests/runtime/test_regex_api.cpp`；接线修改 `src/il2cppc/Emit/MethodRefResolver.cs`
  （§4.5 RegexDispatch）。
- **输入/输出**：§4.2 四个函数（`Il2CppString*`/`Il2CppArray*`）。
- **测试要点**：
  - `is_match`：命中/未命中/边界起点；null 与非法 pattern 异常路径；
  - `match`：`[0]`=整体、组数组、`outStart/outLength` 正确；无匹配 → 空数组 + `-1/-1`；
    组未参与 → null 元素；
  - `replace`：字面替换；`$1` 组引用；`$10` 边界（组 10 存在 → 组 10；不存在 → 组 1 + 字面
    `0`，net48 ParseReplacement 规则）；`$$`；多匹配全替换；空匹配推进（`""` 模式替换不挂死）；
    未识别 `$x` 保留字面；
  - `split`：普通切分；连续分隔符 `"a,,b"` → `["a","","b"]`；首尾分隔符 `",a,"` → `["","a",""]`；
    无匹配 → `[原串]`；空模式 `""` 切分推进规则；
  - 接线：`RegexDispatch` 静态签名映射（IsMatch/Match/Replace/Split，含可选 options 参数
    两种签名字节模式）；Regex ctor/实例方法 → NotSupported（loud）；e2e：tests/cases 加一个
    net48 样例程序（IsMatch/Match 组取用/Replace/Split 各一例），走
    `pwsh scripts/run-tests.ps1 -Compiler mingw` 全链验证；
  - 回归：`dotnet build src/il2cppc/il2cppc.csproj && dotnet test tests/unit/unit.csproj`
    + runtime 测试退出码 0。

---

## 6. 参考：coreclr Regex 设计要点

来源：dotnet/runtime `src/libraries/System.Text.RegularExpressions/`（net48 与 coreclr 同宗，
同为 RegexInterpreter 时代代码；coreclr 的现代演进见下）。检索自官方 blog / 仓库 issue /
DeepWiki 架构摘要（2026-07）：

### 6.1 管线（coreclr 三引擎共享同一前端）

```
RegexParser ──▶ RegexNode 树 ──▶ { RegexWriter(opcode) → RegexInterpreter（默认）
                                  │ RegexCompiler（运行时 JIT，Reflection.Emit）
                                  │ RegexGenerator（编译期源码生成，.NET 7+）}
```

- `RegexNode` 是核心中间表示：`Kind`（Concatenate/Alternate/Oneloop/Set/Multi/...）、
  `Children`、`Str`（Multi 字面量串 / Set 定义）、`Ch`、`M`/`N`（循环 min/max）、`Options`。
  本设计的 AST（§2.2）即其裁剪版（去掉 Multi/Oneloop 折叠、保留 Group/Anchor）。
- 前端有 **reduction 优化**：相邻同字符循环合并（`a*a+` → `a{1,}`）、字面量折叠为 Multi、
  自动原子化（下）。

### 6.2 执行引擎

- **RegexInterpreter**：栈式 VM 执行 opcode 数组；回溯栈 `runtrack` + 分组栈 `runstack`。
  本设计跳过该层（AST 直走，§2.1）。
- **.NET 5+ 重大转向**：编译/源码生成路径改为**直接遍历 RegexNode 树**生成代码，PR #62318
  删除整套旧 opcode 代码生成方案——直接支持"AST 直走"路线可行（先例）。
- **自动原子化**：reduction 阶段把可证明无回溯收益的循环转成 atomic 循环
  （`Oneloopatomic`，如 `a*a*a*b` → `(?>a*)b`），从源头消除灾难回溯。本设计 v1 无原子组
  （§1.3），由步数预算承担同等防护面（§2.4）。
- **FindFirstChar / bumpalong**：`RegexRunner.FindFirstChar` 用首字符集（字面量前缀走
  Boyer-Moore / IndexOf 向量化，.NET 7 后 IndexOfAny 上限 5 字符）快速跳过不可能起点。
  本设计 §2.3.1 保留该思路的简化版（首节点字面量时先扫描），列为可选优化。

### 6.3 缓存与超时（本设计裁剪项）

- **LRU 缓存**：Regex 静态缓存上限 `Regex.CacheSize`（默认 15），LRU 淘汰——§1.4-8 裁剪。
- **超时**：`RegexRunner` 匹配循环内检查 deadline，超时抛 `RegexMatchTimeoutException`；
  默认 `InfiniteMatchTimeout`——§1.4-5 以步数预算替代。

### 6.4 参考来源

- Stephen Toub, "Regex Performance Improvements in .NET 5"（devblogs，回溯消除/原子化/FindFirstChar 综述）
- dotnet/runtime issue #62758（.NET 7 Regex 工作计划：NonBacktracking、Source Generator、引擎统一）
- DeepWiki: dotnet/runtime — Regular Expressions (Parser, Compiler, and Source Generator)
  （RegexNode 属性表、Interpreter 栈语义、三引擎管线）
- dotnet/runtime PR #62318（删除旧 opcode 代码生成，AST 直走）

> 实现提示：T1-T3 的 net48 行为断言（尤其 `[]`/`[^]` 空类、`$10` 替换、组未参与表示）在
> 实现时可用本机 .NET Framework 4.8（或 net48 目标程序）实测复核，与本文档表 3.2/§4 比对。
