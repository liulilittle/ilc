# DateTime 解析语义对照审计（.NET Core DateTimeParse.cs + net48 oracle）

日期：2026-08-04 · 审计域：`src/runtime/src/time.cpp` / `timeext.cpp` / `timeext2.cpp`（+ 各自头文件、测试）
参考源：.NET Core `src/libraries/System.Private.CoreLib/src/System/Globalization/DateTimeParse.cs`（main 分支，本地副本 `%TEMP%\opencode\DateTimeParse.cs`，264 KB）
Oracle：net48 csc.exe 探针（`%TEMP%\opencode\dtprobe\probe{1,2}.cs`，.NET Framework 4.8 + InvariantCulture，Gregorian TwoDigitYearMax=2049）

## 摘要

现有实现（timeext2.cpp）此前只支持 ParseExact 的固定宽子集（yyyy/MM/dd/HH/mm/ss 全双位、`yy`/`M` 拒绝、`'T'` 引号不支持、无通用 Parse、ToString 无分数秒、无标准 "G"）。本次按 .NET Core 源码语义 + net48 oracle 修正 4 项并新增 1 项（通用 Parse 子集）。三张差异表如下。

## 表 1：Parse 差异表（现有 vs .NET）

.NET 语义来源：DateTimeParse.cs `ParseNew`（宽松扫描）+ invariant 格式表；**现有实现此前完全没有 Parse**（timeext.cpp/timeext2.cpp 无入口），本次新增 `il2cpp_datetime_parse`（timeext2.cpp:585）子集。

| 语义点 | .NET（net48 invariant） | 现有（修复前） | 本次修复后 | 证据 |
|---|---|---|---|---|
| 格式识别 | 多格式表：`yyyy'-'MM'-'dd`、`MM/dd/yyyy`、`M/d/yyyy`、`MM-dd-yy`、`yyyyMMdd`(拒绝)、`dd-MMM-yy` 等 | 无 Parse → 任何输入抛"未接线" | ISO `YYYY-MM-DD[ T]HH:MM[:SS]` + US `M/D/YY[YY][ T]HH:MM[:SS]`（'-' 或 '/'） | oracle: `P 2024-02-29 -> ok`、`P 6/15/2024 -> ok`、`P 20240615 -> THROW` |
| 单数字字段 | 月份/日/时分秒均 1-2 位（`2024-2-29`、`2024-6-1`、`2024-2-29 3:4:5` 全部 OK） | 无 | 1-2 位 ✓ | oracle: `P 2024-2-29 3:4:5 -> 2024-02-29 03:04:05` |
| 两数字年 | `6/15/24 -> 2024`、`6/15/49 -> 2049`、`6/15/50 -> 1950`（TwoDigitYearMax=2049）；`24-06-15` → 月 24 → THROW（US 序） | 无 | US 形 1-2 位年按 2049 展开（timeext2.cpp:489）；`24-06-15` 拒绝 ✓ | oracle: `P 6/15/24 -> 2024-06-15`、`P 24-06-15 -> THROW` |
| 闰年 | `2024-02-29` OK、`2023-02-29`/`1900-02-29` THROW、`2000-02-29` OK | 无 | 同（DaysInMonth 400 年规则，复用 ParseExact 校验） | oracle: `P 2023-02-29 -> THROW FormatException`；test_timeext2.cpp:277-289 |
| 时区处理 | `+08:00`/`Z` 后缀解析并**转换为本地时间**（Kind=Local） | 无 | **简化：拒绝**（FormatException，文档化分歧） | oracle: `P 2024-02-29T13:45:30Z -> 2024-02-29 21:45:30 +08:00`（本机 UTC+8） |
| 分数秒 | `2024-02-29T13:45:30.123` OK（0.123 s 保留） | 无 | 简化：拒绝（文档化分歧） | oracle: `P ...30.123 -> ok` |
| 空白 | 首尾 Unicode 空白修剪（`" 2024-02-29 "` OK） | 无 | ASCII 空白（space/tab/CR/LF）修剪 ✓ | oracle: `P ws 2024-02-29 -> 2024-02-29` |
| 时间默认 | 仅时间输入（`13:45`）→ **日期默认今天** | 无 | 简化：拒绝（文档化分歧） | oracle: `P 13:45 -> 2026-08-04 13:45:00` |
| 秒可选 | `2024-06-15 13:45` OK（秒缺省 0） | 无 | 支持 ✓ | oracle: `P 2024-06-15 13:45 (no sec) -> ok` |
| T 分隔 | `2024-02-29T13:45:30` OK | 无 | 支持 ✓ | oracle: `P 2024-02-29T13:45:30 -> ok` |
| Ticks 往返 | Parse 结果 == new DateTime ctor ticks | 无 | ✓（2024-02-29 == 638447616000000000） | oracle: `TICKS1 parse==ctor : True`；test_timeext2.cpp:290 |

## 表 2：ParseExact 差异表（yyyy-MM-dd HH:mm:ss 精确宽度 token 语义）

.NET 语义来源：DateTimeParse.cs `ParseByFormat`（:4108-4317）+ `ParseDigits`（:3170-3183）+ 默认字面量分支（:4593）+ `TryParseQuoteString`（:4565）。

| token | .NET 语义（源码行） | 现有（修复前） | 修复后 | oracle 证据 |
|---|---|---|---|---|
| `yyyy` | 精确 4 位，**不展开**（fUseTwoDigitYear 仅 tokenLen≤2 置位，:4122-4126） | 精确 4 位 ✓ | 不变 ✓；`0001` 保持年 1（曾误展开为 2001——已修） | `PE 2024 / yyyy -> 2024`；test 断言 year-1 tick 0 |
| `yy` | 精确 2 位 + TwoDigitYearMax 展开（29→2029，99→1999，49→2049，50→1950，00→2000） | **拒绝（不支持）** | 精确 2 位 + 展开 ✓ | oracle: `PE 29/yy -> 2029`、`PE 50/yy -> 1950`、`PE 6/yy -> THROW` |
| `y`（单个） | 1-2 位 + 展开 | 拒绝 | 1-2 位 + 展开 ✓ | 源码 :4122 |
| `yyy`/`yyyyy` | 精确 3/5 位，不展开 | 拒绝 | 精确 run 位 ✓ | oracle: `PE 024/yyy -> 0024` |
| `M` / `d`（单个） | 1-2 位（:4144-4154） | **拒绝** | 1-2 位 ✓ | oracle: `PE 2024-6-1 / yyyy-M-d -> ok` |
| `MM` / `dd` | 精确 2 位 | 精确 2 位 ✓ | 不变 ✓ | oracle: `PE 2024-6-1 / yyyy-MM-dd -> THROW` |
| `MMM`/`ddd`+ | 月/日名（不支持域） | 拒绝 | 拒绝（显式）✓ | 源码 :4156-4174 |
| `H`/`m`/`s` | 1-2 位；`HHH` 等 ≥2 位 token = 精确 2 位（:4250 `tokenLen < 2 ? 1 : 2`） | 仅 `HH` 精确 2（`HHH` 会按 3 位读——**错**） | 1-2 位 / ≥2 位精确 2 ✓ | oracle: `PE 2024-02-29 13:4:5 / yyyy-MM-dd HH:m:s -> ok`；`HHH` 语义 :4250 |
| `h`/`t`/`f`/`F`/`g`/`z`/`Z`/`K` | 12 小时/AMPM/分数/纪元/时区 token | 字面量或拒绝（不定） | **显式拒绝**（FormatException，防静默错读；文档化分歧） | oracle: `PE h 12:04 -> 00:04`（.NET 12 时换算）未实现 |
| 未知字母（如 `T`、`q`） | 单字符字面量逐个匹配（:4593 默认分支） | **拒绝** | 单字符字面量 ✓ | oracle: `PE 2024-02-29T13:45:30 / yyyy-MM-ddTHH:mm:ss -> ok` |
| 引号 `'...'`/`"..."` | 字面量分组匹配（TryParseQuoteString :4565） | 引号本身当字面量（`'T'` 需输入含引号——**错**） | 分组匹配 ✓ | 源码 :4565；test_timeext2.cpp:230 |
| `\x` 转义 | 转义单字符字面量（:4534） | 当字面量 | 转义单字符 ✓ | 源码 :4534 |
| 空白 | ParseExact **不修剪**（`" 2024-02-29 "` THROW） | 不修剪 ✓ | 不变 ✓（与 Parse 相反） | oracle: `PE ws ... -> THROW` |
| 闰年 | Feb 29 按 400 年规则 | ✓ | ✓ | oracle: `PE 2023-2-29 / yyyy-M-d -> THROW` |
| 未实现 .NET token（`%`、`.`+F 可选分数、`K` 时区） | 特殊语义 | — | 按字面量/拒绝（文档化） | — |

## 表 3：ToString(format) 差异表（format 字符集）

.NET 语义来源：DateTime.cs 标准格式表 + DateTimeFormat 自定义引擎（MaxSecondsFractionDigits=7）。

| 格式 | .NET（net48 invariant） | 现有（修复前） | 修复后 | oracle 证据 |
|---|---|---|---|---|
| `G`（标准单字母） | `MM/dd/yyyy HH:mm:ss`（= 默认 ToString()） | **抛 FormatException**（未知标准符） | 映射到同默认输出 ✓ | oracle: `TS G -> [02/29/2024 13:45:30]` |
| `d/D/t/T/f/F` | 现有 6 个已 pin | ✓ | 不变 ✓ | 原 timeext oracle |
| `g/m/y/s/O/R` | 有效标准符（`g`=`MM/dd/yyyy HH:mm` 等） | 抛 FormatException | **保持抛**（文档化 out-of-scope） | oracle: `TS g -> [02/29/2024 13:45]` |
| 未知单字母（`q` 等） | FormatException | ✓ | 不变 ✓ | 原 oracle |
| 自定义 `f`（1-7 位） | 分数秒前 N 位，补零（`fff` on .123 s → `123`；on .000 → `000`） | **当字面量输出**（`fff`→"fff"——错） | 1-7 位补零 ✓ | oracle: `TS fff -> ...30.123`、`TS fff-zero -> [000]` |
| 自定义 `F`（1-7 位） | 同 f 但去尾零（FFF on .000 → 空串） | 字面量 | 去尾零 ✓（可空） | oracle: `TS FFF-zero -> []`、`TS FFFFFF -> 123` |
| `ffffffff`（8 位） | **FormatException**（>7） | 字面量 | FormatException ✓ | oracle: `TS ffffffff -> THROW FormatException` |
| `yyyy/MM/dd/HH/mm/ss/yy/hh` 等既有 token | 已 pin | ✓ | 不变 ✓ | 原 timeext oracle |
| 自定义未知字母（`tt`、`z` 等） | 有效 token 或 FormatException | 字面量（文档化分歧） | 保持字面量（out-of-scope） | 原 timeext.cs case |

## net48 oracle 关键实证（原始输出，%TEMP%\opencode\dtprobe\oracle{1,2}.txt）

```
P 2024-02-29 -> 2024-02-29 00:00:00      P 2023-02-29 -> THROW FormatException
P 2000-02-29 -> 2000-02-29               P 1900-02-29 -> THROW FormatException
P 2024-2-29 -> 2024-02-29                P 24-06-15 -> THROW FormatException
P 6/15/24 -> 2024-06-15                  P 6/15/50 -> 1950-06-15  P 6/15/49 -> 2049-06-15
P 2024-02-29T13:45:30 -> ok              P 2024-02-29T13:45:30+08:00 -> 13:45:30 +08:00 (Kind Local)
P 2024-02-29T13:45:30Z -> 21:45:30 +08:00  (UTC 转换本地)
PE 2024-6-1 / yyyy-M-d -> 2024-06-01     PE 2024-6-1 / yyyy-MM-dd -> THROW FormatException
PE 29/yy -> 2029  PE 99/yy -> 1999  PE 49/yy -> 2049  PE 50/yy -> 1950  PE 6/yy -> THROW
PE 024/yyy -> 0024                      PE 2024-02-29 13:4:5 / yyyy-MM-dd HH:m:s -> ok
PE 2024-02-29T13:45:30 / yyyy-MM-ddTHH:mm:ss -> ok（未引号未知字母=字面量）
PE " 2024-02-29 " / yyyy-MM-dd -> THROW（ParseExact 不修剪）
TS G -> 02/29/2024 13:45:30              TS fff -> 2024-02-29 13:45:30.123
TS fff-zero -> [000]  TS FFF-zero -> []  TS fffffff -> 1234567  TS ffffffff -> THROW
AD1 2024-02-28+1 -> 2024-02-29   AD2 +2 -> 2024-03-01   AD3 2023-02-28+1 -> 2023-03-01
AD6 2024-02-28+365 -> 2025-02-27  AD7 2023-02-28+365 -> 2024-02-28
TICKS1 parse==ctor : True (638447616000000000)   TICKS2 parseexact==ctor : True
TDYM : 2049
```

## 修正清单（每项断言数）

1. **ParseExact token 宽度语义**（timeext2.cpp `TryParseExact`）：单字符 token 1-2 位、双字符精确 2 位、`yy` 精确 2 + 展开、`yyy+` 精确 run 位、`HHH` 裁剪为精确 2 —— 断言 12（test_timeext2.cpp:212-268）
2. **两位数年展开**（`ExpandTwoDigitYear` timeext2.cpp:82，TwoDigitYearMax=2049；`yyyy` 不展开——修复了 `0001` 被误展开为 2001 的 bug，timeext2.cpp:319）——断言 6（含 year-1 tick 0 回归）
3. **ParseExact 字面量**：`'...'`/`"..."` 引号分组、`\x` 转义、未知字母单字符字面、未实现 .NET token（h/t/f/F/g/z/Z/K）显式拒绝 —— 断言 4
4. **通用 Parse**（`il2cpp_datetime_parse`，timeext2.cpp:585；ISO/US 双子集 + 修剪 + 闰年 + 2049 展开；时区/分数/仅时间拒绝=文档化分歧）——断言 27（test_timeext2.cpp:278-392）
5. **ToString 标准 `G`**（= 默认输出模式）——断言 1（test_timeext.cpp:229-231）
6. **ToString 自定义 `f`/`F`**（1-7 位分数秒；`f` 补零、`F` 去尾零；>7 抛 FormatException）——断言 10（test_timeext.cpp:234-273）

既有断言更新 3 处（原 pin 了分歧行为：`'yy' token rejects`、`single 'M' rejects`、`unknown letter 'T' rejects` → 按 .NET 语义翻转）。

## 独立验证（cmake 被 metadata.cpp 阻塞）

- `cmake -S tests/runtime -B build/runtime_dtchk` 配置成功；`--target test_time test_timeext test_timeext2` 构建失败——**仅** metadata.cpp 19 编译错（并行代理 namespace 半成品，8/3 22:07 在途，与 memory 记录一致）→ 按任务预案走 g++ 独立编译
- g++ -std=c++17 -Wall -Wextra -Werror -fexceptions -fno-rtti 编译 timeext2.cpp / timeext.cpp：**0 警告 0 错误**
- 独立链接（timeext2/timeext/time/string/exception/exceptionext/gc/runtime/framestack/object/console + 临时 metadata 注册表 stub）：
  - `test_timeext2.exe` → **PASS，exit 0**（含 27 条新 Parse 断言 + 22 条 ParseExact 断言）
  - `test_timeext.exe` → **PASS，exit 0**（含 10 条 f/F + G 新断言，原有 47 条不变）
  - `test_time.exe` → **PASS，exit 0**（time.cpp 未改动，回归通过）

## 遗留分歧（文档化，未实现）

- Parse/ParseExact 时区后缀（`Z`/`zzz`/`K`）：.NET 转本地时间——运行时无时区概念，拒绝
- Parse 仅时间输入（.NET 日期默认今天）、分数秒、紧凑格式、Unicode 空白：拒绝
- ParseExact `h`/`t`/`f`/`F`/`g`/`z`/`Z`/`K`/`%`、`.`+F 可选分数：显式拒绝（不静默误读）
- ToString 标准 `g/m/y/s/O/R`、自定义 `tt`/`z`：抛 FormatException（out-of-scope）
- **接线**：`il2cpp_datetime_parse` 尚未映射到 MethodMap.TimeExt2.cs（该文件域外只读）——转译代码在映射落地前不可达；头文件已注明
