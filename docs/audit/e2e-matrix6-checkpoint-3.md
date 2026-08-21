# e2e Matrix6 批次3 Checkpoint（2026-08-06 15:xx）

范围：tests/cases 字母序 #81 lockcase 至 #155 xmlcase（共 75 例）。编译器 mingw，逐字节 net48 live diff。
共享库：build/runtime_e2e_shared/libil2cpp_runtime.a（14:20:48）新于全部 runtime 源（13:07:18），`cmake --build` 0 改 → 直接复用。

## Checkpoint 1/5（#81-95 lockcase..overflow，15 例）

| # | 用例 | 结果 | 阶段 | 失败现象 |
|---|---|---|---|---|
| 81 | lockcase | FAIL | build | monitor_enter/exit 收 int32_t（loc4/LockCase_sf_gate 被声明 int32）→ Il2CppObject* 参数错配 + L_54 等 label 重定义（20 errors） |
| 82 | loop | PASS | | |
| 83 | loopdeep | PASS | | |
| 84 | marshalcase | PASS | | |
| 85 | math2 | PASS | | |
| 86 | mathcase | PASS | | |
| 87 | mathdeep | FAIL | diff | 19 处 b_* NaN/Inf 边界：expected False，actual True（sinInf/cosNegInf/tanInf/asin2/acos2/logNeg/log10Neg/expNaN/logNaN/sinNaN/tanhNaN/sqrtNeg/sqrtNaN/floorNaN/ceilNaN/roundNaN/truncNaN/ieee0/ieeeInf）——runtime NaN 传播语义 vs net48 |
| 88 | mathext | PASS | | |
| 89 | mathextra | PASS | | |
| 90 | matrixcase | PASS | | |
| 91 | methods | PASS | | |
| 92 | netcase | FAIL | build | 转译成功但 `no member named 'il2cpp_ipaddress_parse'`（IPAddress.Parse 映射到不存在的 runtime 符号）+ t1 收 int32 传给 icall_object_to_string |
| 93 | numchain | FAIL | build | icall_try_parse_int32 缺 loc12/loc13/loc14 声明（temp 未分配）+ `no member named 'icall_try_parse_double'`（20 errors） |
| 94 | objects | PASS | | |
| 95 | overflow | PASS | | |

小计：11 PASS / 4 FAIL（build×3 diff×1）

## Checkpoint 2/5（#96-110 overloads..reflectcase，15 例）

| # | 用例 | 结果 | 阶段 | 失败现象 |
|---|---|---|---|---|
| 96 | overloads | PASS | | |
| 97 | paramroot | PASS | | |
| 98 | parsecase | FAIL | transpile | Convert.ToInt32(string, fromBase) 未接线——runtime 只有 try_parse 无 throwing fromBase icall（MethodRefResolver.cs:426） |
| 99 | pathext | PASS | | |
| 100 | pinvoke | PASS | | ⚠ matrix5 FAIL（链接 undefined mylib）→ 现 PASS |
| 101 | pinvokeext | PASS | | ⚠ matrix5 FAIL → 现 PASS |
| 102 | processcase | PASS | | |
| 103 | propcase | PASS | | |
| 104 | props | PASS | | |
| 105 | randomcase | FAIL | diff | RNG 时序值不一致（6 行全异）+ 输出形状差（actual 多 1 行 NextBytes 输出、少 1 空行）——matrix5 同归因 |
| 106 | recurse | FAIL | build | entry.cpp:18 生成码 Il2CppString*→Il2CppObject* 数组写入类型不兼容（main-args 接线后新入口签名） |
| 107 | recurse2 | PASS | | |
| 108 | recursive_arith | PASS | | |
| 109 | refinvoke | FAIL | build | `no member named 'il2cpp_typeinfo_get_method'`（TypeInfo/MethodInfo.Invoke 域未映射） |
| 110 | reflectcase | FAIL | build | `no member named 'il2cpp_type_get_type'`（Type.GetType 域未映射）+ g_strPool[3]==g_strPool[3] 自比较告警 |

小计：10 PASS / 5 FAIL（build×3 transpile×1 diff×1）累计 21 PASS / 9 FAIL（30 例）

## Checkpoint 3/5（#111-125 regdeep..strfmt，15 例）

| # | 用例 | 结果 | 阶段 | 失败现象 |
|---|---|---|---|---|
| 111 | regdeep | PASS | | |
| 112 | regexcase | PASS | | |
| 113 | regexcase2 | PASS | | |
| 114 | sbchain | PASS | | |
| 115 | sblong | PASS | | ⚠ matrix5 FAIL（L2 越界崩 6/31）→ 现 PASS |
| 116 | socketcase | FAIL | build | 同 netcase：`no member named 'il2cpp_ipaddress_parse'` + t1 int32→Il2CppObject* |
| 117 | sortcase2 | PASS | | |
| 118 | sortdeep | PASS | | |
| 119 | stackq | FAIL | build | 新增用例：System_Collections_Stack_class 未声明 + `il2cpp_stack_create/get_count/push/peek` 全缺（D-STK-1/2 同 memory 记录） |
| 120 | statics | PASS | | |
| 121 | strcomp2 | FAIL | transpile | `newarr element type System.Char not supported (audit C4)` |
| 122 | strcompare | FAIL | diff | 66 行中 4 处 False：Equals OrdinalIgnoreCase/CurrentCultureIgnoreCase、null 值参数未抛异常、`"".CompareTo("")==0` |
| 123 | strdeep | PASS | | |
| 124 | streamcase | FAIL | transpile | FileStream ctor 未接线（handle FACTORY vs object ctor icall 缺 il2cpp_stream_file_open 对象型） |
| 125 | strfmt | PASS | | |

小计：10 PASS / 5 FAIL（build×2 transpile×2 diff×1）累计 31 PASS / 14 FAIL（45 例）

## Checkpoint 4/5（#126-140 string..timeext，15 例）

| # | 用例 | 结果 | 阶段 | 失败现象 |
|---|---|---|---|---|
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
| 140 | timeext | FAIL | diff | 84 行中 23 处：L1 文化 zh-CN→en-US、短日期 2020/6/15→06/15/2020、"y" 说明符 FormatException（崩）后续 58 行全断——文化 + 格式说明符域 |

小计：13 PASS / 2 FAIL（diff×2）累计 44 PASS / 16 FAIL（60 例）

## Checkpoint 5/5（#141-155 timeops..xmlcase，15 例）

| # | 用例 | 结果 | 阶段 | 失败现象 |
|---|---|---|---|---|
| 141 | timeops | PASS | | 新增用例 |
| 142 | timezonecase | FAIL | build | ⚠ matrix5 PASS → 现 FAIL@build：generated.h include 表缺 `timezone.h`（git log -S 无提交，`tzsec` 发射自 81d25b6 已有），MethodMap.TimeZone 生成码调 il2cpp::runtime::il2cpp_timezone_get_utc_offset/datetime_to_local/from_local/is_daylight_saving 全 undeclared —— 与 hashcase/hashchain crypto_obj.h 同类 include 回归 |
| 143 | typechain | PASS | | ⚠ matrix5 transpile FAIL → 现 PASS |
| 144 | typecheckcase | FAIL | transpile | `TypeSpec signature byte 0x1D not GenericInst(0x15) in M5`（typecheck 域，matrix5 同因） |
| 145 | uintcase | PASS | | |
| 146 | uintcmp | PASS | | |
| 147 | uricase | PASS | | |
| 148 | valdeep | PASS | | |
| 149 | valuesem | PASS | | |
| 150 | valuetypes | PASS | | |
| 151 | versioncase | PASS | | |
| 152 | virtual | PASS | | |
| 153 | weakrefcase | PASS | | ⚠ matrix5 diff FAIL（0xC0000005 崩）→ 现 PASS |
| 154 | webcase | FAIL | transpile | WebClient.DownloadString 未接线（http.h 引擎存在但缺 managed 单调用 wrapper icall_web_client_download_string） |
| 155 | xmlcase | FAIL | transpile | `icall arg cast code 'node' not implemented`（XmlNode 域） |

小计：11 PASS / 4 FAIL（transpile×3 build×1）累计 55 PASS / 20 FAIL（75 例）

## 批次 3 总览（75 例 #81-155）

PASS 55 / FAIL 20（build×8 transpile×8 diff×4）—— 通过率 73.3%
- 转绿（vs matrix5 FAIL→PASS）：pinvoke、pinvokeext、sblong、typechain、weakrefcase（5 例）
- 新回归（vs matrix5 PASS→FAIL）：timezonecase（generated.h 缺 timezone.h include，batch14 在途改动引入）
- 其余 FAIL 与 matrix5 同因（lockcase/netcase/socketcase/numchain/recurse/refinvoke/reflectcase/stackq 等 build；parsecase/strcomp2/streamcase/typecheckcase/webcase/xmlcase transpile；mathdeep/randomcase/strcompare/timeext/strjoin diff）

## 批次 3 完成（75/75）
- 全部 5 个 checkpoint 已写（每 15 例）。最终批次报告：docs/audit/e2e-matrix6-batch3.md；与权威全量 results.tsv 100% 一致（75/75）。
- 汇总三批 + matrix5 → docs/audit/e2e-full-matrix6.md（108/155 PASS 69.7%，交集 97/131 74.0% +5）。
