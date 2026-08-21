# e2e Matrix6 批次 3 报告 — tests/cases 字母序用例 81-155

日期：2026-08-06 15:20-15:55
分支：m1
编译器：mingw（`pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>`）
共享库：`build/runtime_e2e_shared/libil2cpp_runtime.a`（14:20:48，新于全部 runtime 源 13:07:18；`cmake --build` 0 改动 → 直接复用，未 configure/重建）

## 结果摘要

- 范围：#81 lockcase → #155 xmlcase，共 **75 例**
- **PASS：55（73.3%）**
- FAIL：20（26.7%）
  - FAIL@build（cmake）：8（lockcase/netcase/numchain/recurse/refinvoke/reflectcase/socketcase/stackq/timezonecase → 实为 9？见下）
  - FAIL@transpile：8（parsecase/strcomp2/streamcase/typecheckcase/webcase/xmlcase + typecheckcase 重复计？）
  - FAIL@diff：4（mathdeep/randomcase/strcompare/strjoin/timeext → 实为 5？）

> 注：上述快速计数含重复；以下表为准（build×9 / transpile×6 / diff×5 = 20）。

## 结果表（75 例）

| # | 用例 | 结果 | 阶段 | 失败现象摘要 |
|---|---|---|---|---|
| 81 | lockcase | FAIL | build | monitor_enter/exit 收 int32_t（loc4/sf_gate 声明为 int32）→ Il2CppObject* 参数错配 + L_54/L_206/L_218/L_245 label 重定义（20 errors） |
| 82 | loop | PASS | | |
| 83 | loopdeep | PASS | | |
| 84 | marshalcase | PASS | | |
| 85 | math2 | PASS | | |
| 86 | mathcase | PASS | | |
| 87 | mathdeep | FAIL | diff | 19 处 b_* NaN/Inf 边界：sinInf/cosNegInf/tanInf/asin2/acos2/logNeg/log10Neg/expNaN/logNaN/sinNaN/tanhNaN/sqrtNeg/sqrtNaN/floorNaN/ceilNaN/roundNaN/truncNaN/ieee0/ieeeInf expected False actual True —— runtime NaN 传播语义 vs net48 |
| 88 | mathext | PASS | | |
| 89 | mathextra | PASS | | |
| 90 | matrixcase | PASS | | |
| 91 | methods | PASS | | |
| 92 | netcase | FAIL | build | `no member named 'il2cpp_ipaddress_parse'`（IPAddress.Parse 映射到不存在的 runtime 符号）+ t1 收 int32 传 icall_object_to_string |
| 93 | numchain | FAIL | build | icall_try_parse_int32/double 用 loc12-14 未声明（temp 未分配）+ `no member 'icall_try_parse_double'`（20 errors） |
| 94 | objects | PASS | | |
| 95 | overflow | PASS | | |
| 96 | overloads | PASS | | |
| 97 | paramroot | PASS | | |
| 98 | parsecase | FAIL | transpile | Convert.ToInt32(string, fromBase) 未接线（runtime 只有 try_parse 无 throwing fromBase icall，MethodRefResolver.cs:426） |
| 99 | pathext | PASS | | 新增用例 |
| 100 | pinvoke | PASS | | ⚠ matrix5 FAIL → 转绿 |
| 101 | pinvokeext | PASS | | ⚠ matrix5 FAIL → 转绿 |
| 102 | processcase | PASS | | |
| 103 | propcase | PASS | | |
| 104 | props | PASS | | |
| 105 | randomcase | FAIL | diff | RNG 时序值全异（6 行）+ 输出形状差（actual 多 1 行 NextBytes、少 1 空行）——matrix5 同因 |
| 106 | recurse | FAIL | build | entry.cpp:18 生成码 Il2CppString* 赋 Il2CppObject** 数组元素类型不兼容（main-args 新入口接线） |
| 107 | recurse2 | PASS | | 新增用例 |
| 108 | recursive_arith | PASS | | |
| 109 | refinvoke | FAIL | build | `no member named 'il2cpp_typeinfo_get_method'`（TypeInfo/MethodInfo.Invoke 域未映射） |
| 110 | reflectcase | FAIL | build | `no member named 'il2cpp_type_get_type'`（Type.GetType 域未映射）+ g_strPool[3]==g_strPool[3] 自比较告警 |
| 111 | regdeep | PASS | | |
| 112 | regexcase | PASS | | |
| 113 | regexcase2 | PASS | | |
| 114 | sbchain | PASS | | |
| 115 | sblong | PASS | | ⚠ matrix5 FAIL（L2 越界崩 6/31）→ 转绿 |
| 116 | socketcase | FAIL | build | 同 netcase：`no member 'il2cpp_ipaddress_parse'` + t1 int32→Il2CppObject* |
| 117 | sortcase2 | PASS | | |
| 118 | sortdeep | PASS | | |
| 119 | stackq | FAIL | build | 新增用例：System_Collections_Stack_class 未声明 + il2cpp_stack_create/get_count/push/peek 全缺（D-STK-1/2） |
| 120 | statics | PASS | | |
| 121 | strcomp2 | FAIL | transpile | `newarr element type System.Char not supported (audit C4)` |
| 122 | strcompare | FAIL | diff | 66 行中 4 处 False：Equals(OrdinalIgnoreCase/CurrentCultureIgnoreCase)、null 值参数未抛、`"".CompareTo("")==0` |
| 123 | strdeep | PASS | | |
| 124 | streamcase | FAIL | transpile | FileStream ctor 未接线（handle FACTORY vs object ctor icall 缺 il2cpp_stream_file_open 对象型） |
| 125 | strfmt | PASS | | |
| 126 | string | PASS | | |
| 127 | stringbuilder | PASS | | |
| 128 | stringext | PASS | | |
| 129 | stringext2 | PASS | | |
| 130 | stringops | PASS | | |
| 131 | stringrwcase | PASS | | |
| 132 | strjoin | FAIL | diff | 生成 exe 直接崩 exit 0xC0000374（STATUS_HEAP_CORRUPTION，0 输出 vs 30 行 True）——String.Join 域 |
| 133 | strpath | PASS | | 新增用例 |
| 134 | swcase | PASS | | |
| 135 | swcase2 | PASS | | |
| 136 | threadcase | PASS | | |
| 137 | threadcase2 | PASS | | |
| 138 | threadpoolcase | PASS | | |
| 139 | timecase | PASS | | |
| 140 | timeext | FAIL | diff | 84 行中 23 处：L1 文化 zh-CN→en-US、短日期 2020/6/15→06/15/2020、"y" 说明符 FormatException 崩（后续 58 行断）——文化 + 格式说明符域 |
| 141 | timeops | PASS | | 新增用例 |
| 142 | timezonecase | FAIL | build | ⚠ **matrix5 PASS → 回归**：generated.h include 表缺 `timezone.h`，MethodMap.TimeZone 生成码调 il2cpp_timezone_get_utc_offset/datetime_to_local/from_local/is_daylight_saving 全 undeclared（git log -S 'timezone.h' 无提交；tzsec 发射自 81d25b6 已有）——与 hashcase/cryptocase crypto_obj.h 同类 include 回归 |
| 143 | typechain | PASS | | ⚠ matrix5 transpile FAIL → 转绿 |
| 144 | typecheckcase | FAIL | transpile | `TypeSpec signature byte 0x1D not GenericInst(0x15) in M5`（matrix5 同因） |
| 145 | uintcase | PASS | | |
| 146 | uintcmp | PASS | | |
| 147 | uricase | PASS | | |
| 148 | valdeep | PASS | | |
| 149 | valuesem | PASS | | |
| 150 | valuetypes | PASS | | |
| 151 | versioncase | PASS | | |
| 152 | virtual | PASS | | |
| 153 | weakrefcase | PASS | | ⚠ matrix5 diff FAIL（0xC0000005 崩 0/9）→ 转绿 |
| 154 | webcase | FAIL | transpile | WebClient.DownloadString 未接线（http.h 引擎存在但缺 managed 单调用 wrapper icall_web_client_download_string） |
| 155 | xmlcase | FAIL | transpile | `icall arg cast code 'node' not implemented`（XmlNode 域，matrix5 同因 callvirt 分类） |

小计：build×9（lockcase/netcase/numchain/recurse/refinvoke/reflectcase/socketcase/stackq/timezonecase）+ transpile×6（parsecase/strcomp2/streamcase/typecheckcase/webcase/xmlcase）+ diff×5（mathdeep/randomcase/strcompare/strjoin/timeext）= 20 FAIL。

## 与 matrix5 对比（本批次交集）

- **转绿 5 例**：pinvoke、pinvokeext（链接符号 → 现在 runtime 有 mylib 侧修复）、sblong（越界崩修复）、typechain（callvirt 分类修复）、weakrefcase（0xC0000005 崩修复）——全部 net48 live 逐字节确认。
- **新回归 1 例**：**timezonecase**（matrix5 PASS → FAIL@build）。根因：generated.h include 表缺 `timezone.h`（git 全程无该行），而 MethodMap.TimeZone 域（batch-14/15 在途改动）发射 il2cpp::runtime::il2cpp_timezone_get_utc_offset 等 4 个 timezone.h 符号。**同类回归已见 hashcase/cryptocase（缺 crypto_obj.h）**——三个都在 M6 include 表重建后丢失。
- 其余 14 例 FAIL 与 matrix5 同归因（见失败现象列），无新增交集失败。
- 本批次含新增用例 13 例（mathdeep/numchain/pathext/recurse2/refinvoke/reflectcase/stackq/strcomp2/streamcase/strjoin/strpath/timeops/webcase）：PASS 4（pathext/recurse2/strpath/timeops）；FAIL 9（mathdeep diff、numchain build、refinvoke/reflectcase build、stackq build D-STK-1/2、strcomp2/streamcase transpile、strjoin diff 0xC0000374、webcase transpile）。

## 结论

批次 3（81-155）PASS 55/75（73.3%）。20 例失败均为既有缺陷 + 1 例 M6 include 表回归（timezonecase 缺 timezone.h include）。本批次不改产品代码、不 commit。per-case 日志：`%TEMP%\opencode\e2e-m6b3\<case>.txt`；checkpoint 逐 15 例追加 `docs/audit/e2e-matrix6-checkpoint-3.md`。
