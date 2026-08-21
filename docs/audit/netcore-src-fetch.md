# .NET Core 参考源码拉取报告 — reference/netcore/

- 日期：2026-08-04
- 任务：拉取 dotnet/runtime 关键源文件到 `reference/netcore/`（.gitignore 已排除，不提交），为铁律3「实现 .NET 语义前先抄 .NET Core 已验证实现」提供本地参考
- 方式：git sparse checkout（`--depth 1 --filter=blob:none --sparse`），非全量 clone
- 约束：不 commit；不改任何现有代码

## 1. 来源与版本

| 项 | 值 |
|---|---|
| 仓库 | `https://github.com/dotnet/runtime.git`（分支 `main`） |
| commit | `93add0d39af4c738d817797feebfd539e3ee9fd0` |
| 主题 | Emit a resolvable type name for Runtime Async continuations in GC heap dumps (#131684) |
| 日期 | 2026-08-03 14:52:55 -0400 |
| 检出方式 | `git clone --depth 1 --filter=blob:none --sparse --no-checkout` + `git sparse-checkout set --no-cone <paths>` + `git checkout` |
| sparse 检出文件数 | 1829 |

## 2. 成功清单（关键文件，路径 + 字节）

全部 26/26 关键文件存在（`[BASH:Get-ChildItem]` 逐文件验证 size > 0）：

| # | 路径（`reference/netcore/` 下） | 大小 |
|---|---|---|
| 1 | `src/libraries/System.Private.CoreLib/src/System/Number.Formatting.cs` | 131,928 |
| 2 | `src/libraries/System.Private.CoreLib/src/System/Number.Parsing.cs` | 73,421 |
| 3 | `src/libraries/System.Private.CoreLib/src/System/Globalization/DateTimeParse.cs` | 271,167 |
| 4 | `src/libraries/System.Private.CoreLib/src/System/String.cs` | 29,777 |
| 5 | `src/libraries/System.Private.CoreLib/src/System/String.Comparison.cs` | 53,227 |
| 6 | `src/libraries/System.Private.CoreLib/src/System/Convert.cs` | 104,336 |
| 7 | `src/libraries/System.Private.CoreLib/src/System/BitConverter.cs` | 61,751 |
| 8 | `src/libraries/System.Private.CoreLib/src/System/Text/StringBuilder.cs` | 154,011 |
| 9 | `src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs` | 44,805 |
| 10 | `src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs` | 100,624 |
| 11 | `src/libraries/System.Private.CoreLib/src/System/Guid.cs` | 76,261 |
| 12 | `src/libraries/System.Private.CoreLib/src/System/Version.cs` | 21,354 |
| 13 | `src/libraries/System.Private.CoreLib/src/System/Threading/Interlocked.cs` | 54,929 |
| 14 | `src/libraries/System.Private.CoreLib/src/System/Threading/Monitor.cs` | 5,805 |
| 15 | `src/libraries/System.Private.CoreLib/src/System/IO/BinaryReader.cs` | 22,896 |
| 16 | `src/libraries/System.Private.CoreLib/src/System/IO/BinaryWriter.cs` | 19,733 |
| 17 | `src/libraries/System.Private.CoreLib/src/System/IO/StreamReader.cs` | 65,625 |
| 18 | `src/libraries/System.Private.CoreLib/src/System/IO/StreamWriter.cs` | 45,950 |
| 19 | `src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Decoder.cs` | 17,082 |
| 20 | `src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Encoder.cs` | 15,629 |
| 21 | `src/libraries/System.Private.CoreLib/src/System/Environment.cs` | 11,416 |
| 22 | `src/libraries/System.Console/src/System/Console.cs` | 39,064 |
| 23 | `src/libraries/System.Private.Uri/src/System/Uri.cs` | 197,641 |
| 24 | `src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs` | 94,019 |
| 25 | `src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexInterpreter.cs` | 47,057 |
| 26 | `src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexRunnerFactory.cs` | 818 |

另含同一 sparse 集内大量配套文件（System.Private.CoreLib/src/System 全目录、Regex 目录、System.Private.Uri 全目录等共 1829 个文件），含 `Number.Dragon4.cs`、`Number.DiyFp.cs`、`Number.Grisu3.cs`、`DateTime.cs`、`DateTimeOffset.cs`、`Globalization/NumberFormatInfo.cs`、`RegexRunner.cs`、`RegexCharClass.cs`、`RegexNode.cs` 等。

## 3. 失败清单（无）

- 全部 26 个目标文件成功拉取，0 失败。
- 有 2 处「任务书文件名与当前 main 布局不符」的**路径映射**（非失败，文件已就位）：
  - `System/Number.cs` → 当前 main 已拆分：`Number.Formatting.cs` / `Number.Parsing.cs` / `Number.Dragon4.cs` 等（本报告 #1/#2）
  - `System/DateTimeParse.cs` → 已移至 `System/Globalization/DateTimeParse.cs`（本报告 #3）
  - `Convert.Base64.cs` → 已并入 `Convert.cs` + `Buffers/Text/Base64Decoder/Encoder.cs`（本报告 #6/#19/#20）
  - `System.Runtime.Extensions/src/System/Environment.cs` → 已移至 `System.Private.CoreLib/src/System/Environment.cs`（本报告 #21）
  - `System.Uri` → 独立程序集 `System.Private.Uri/src/System/Uri.cs`（本报告 #23）
  - Regex 目录 → `System.Text.RegularExpressions/src/System/Text/RegularExpressions/`（本报告 #24-26）
  - String 哈希/比较 → `String.Comparison.cs` 独立文件（本报告 #5）

## 4. 过程备注（异常与处置）

1. **发现残留进程竞态**：`reference/netcore/` 初始存在上一会话残留文件（含 14 字节的 `Number.cs` = raw 下载的 `404: Not Found` 响应），且有一个 15 分钟间隔的孤儿 powershell 循环进程仍在持续写该目录（D2/D3 双写入者竞态风险）。已终止该孤儿循环并清理残留；改用 git sparse checkout 作为权威来源后重建。
2. `.git` 元数据丢失事故：首次用 `Move-Item netcore_git/*` 迁移时，PowerShell `*` 不匹配隐藏的 `.git` → git 历史丢失。随后直接重新 clone 到最终路径 `reference/netcore/`，现持有完整 sparse repo（`git log` 可见 commit `93add0d`）。
3. `git sparse-checkout set --no-cone` + `git checkout` 在 `--filter=blob:none` 下按需拉 blob，最终 1829 文件就位。

## 5. 后续建议

1. **保留稀疏仓库而非散文件**：`reference/netcore/` 是带 `.git` 的 sparse clone，后续可用 `git sparse-checkout add` 增量扩充任意源文件（无需全量 clone），并用 `git fetch --depth 1 && git checkout` 升级版本。
2. **锁定 commit 以对齐语义**：铁律 3 抄实现时以 `93add0d` 为准；若未来升级版本，需重新核对与既有 ILC 语义的差异（尤其 Regex/DateTimeParse 行为）。
3. **补充参考映射**：docs 已有 `netcore-reference-guide.md`（未跟踪），后续实现各域（Number/DateTime/String/Regex/IO/Threading）时按域补充 → 参考文件路径映射。
4. **文件大小提示**：DateTimeParse.cs（271KB）、Uri.cs（197KB）、StringBuilder.cs（154KB）偏大，抄写/转译时建议只取关键节（任务书已提示「若太大取关键段」）。
5. **仓库体积**：`.git` 目录在 `reference/` 下被 .gitignore 排除，不进入主仓库提交；但本地占用（sparse 元数据）需知悉。
