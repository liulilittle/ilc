# e2e Full Matrix Audit 4 — 大量修复后全量重跑（2026-08-05）

日期：2026-08-05（13:2x 快照；矩阵运行约 13:25-13:5x 完成，106 用例 8 并行）
基线：`dotnet build src/il2cppc -c Release` → **0 错 0 警**（复验）；il2cppc.dll 12:49:46 新于全部转译器源（最新 StackFold.cs 12:49:16）。
前置门槛：**在途代理已收工确认**——任务启动时代理仍活跃（CMakeLists.txt 11:10、aes.cpp 12:05、StackFold.cs 12:49 持续在写），轮询 3 轮共 ~2h 至 **13:20:36 确认 src 最后写入 12:49:16 达 31.3min ≥30min + 无构建进程**后重跑；随后源稳定（aes.h 12:37 43min）。
关键修复：**共享 runtime 库过期**——`build/runtime_e2e_shared/libil2cpp_runtime.a` 旧 10:44:43（runtime 源 aes.h 12:37 新）→ 重新 cmake configure + mingw32-make -j8 重建为 **13:22:40**（含 aes/http/ipaddr/rng/sha512/hmac/conv/arraysort 全部新源），否则矩阵全部构建用旧库结果无效。
方法：复用 matrix3 脚本法（`%TEMP%\opencode\run-e2e-matrix4.ps1`，8 并行，每用例 转译 → 共享 runtime 库 wrapper cmake MinGW 构建 → 运行 → **net48 live 参考输出逐字节 diff**，LF-join 比较）+ 对 diff 失败例做 expected.txt 二次 byte-check（recheck9 教训：build 失败一律以 stage=FAIL 为准）。

对比基线：docs/audit/e2e-full-matrix3.md（matrix3，47/88 PASS）。

## 汇总

| 指标 | matrix1 | matrix2 | matrix3 | **matrix4** | M3→M4 变化 |
|---|---|---|---|---|---|
| 总用例 | 88 | 88 | 88 | **106**（新增 18 例） | +18 |
| **PASS（逐字节一致）** | 38 | 39 | 47 | **72（67.9%）** | +25 |
| FAIL | 50 | 49 | 41 | **34** | −7 |
| 转译失败 | 26 | 25 | 26 | **17** | −9 |
| cmake 构建失败 | 7 | 6 | 2 | **10** | +8 |
| 运行/diff 失败 | 16 | 17 | 12 | **7** | −5 |
| net48 参考构建失败 | 1 | 1 | 1 | **0** | −1 |
| build 警告/错误 | 0/0 | 0/0 | 0/0 | **0/0** | = |

**88 例交集集（可比口径）**：matrix3 47/88 → **matrix4 59/88（67.0%，+12）**——净增 12（15 例新绿 − 3 例回归）。

**PASS 率变化**：53.4% → **67.9%（全量）/ 67.0%（88 交集）**。新绿 15 例经 net48 live 逐字节确认；**3 例回归**（generics/interlockedcase/valuetypes，均 cmake_build，见 §回归分析）。

## 全量统计表（106 用例 | matrix3 | matrix4 | 阶段 | 变化 | 失败归因）

### matrix3 交集集（88 例）

| 用例 | M3 | M4 | 阶段 | 变化 | 失败归因 |
|---|---|---|---|---|---|
| arith | PASS | **PASS** | done | = | |
| arraybounds | PASS | **PASS** | done | = | |
| arrayext | PASS | **PASS** | done | = | |
| arrayext2 | FAIL | FAIL | transpile | = | **前进**：仍卡 `Array.FindIndex`（icall 取 (array,start,count) 但 .NET Predicate 重载不供给） |
| arrays | PASS | **PASS** | done | = | |
| base64case | FAIL | **PASS** | done | **新绿** | **Encoding/Base64 接线落地**（GetBytes raw-buffer + FromBase64String 已修） |
| bitconvcase | FAIL | FAIL | cmake_build | **前进** | 转译已过（GetBytes/ToString 已接）→ 卡生成码 `Il2CppArray*` vs 字面量 + `System_BitConverter_sf_IsLittleEndian` 未声明 |
| boxdeep | PASS | **PASS** | done | = | |
| boxext | PASS | **PASS** | done | = | |
| boxext2 | PASS | **PASS** | done | = | |
| boxing | PASS | **PASS** | done | = | |
| boxing2 | PASS | **PASS** | done | = | |
| boxstr | PASS | **PASS** | done | = | |
| branch | PASS | **PASS** | done | = | |
| cctor | PASS | **PASS** | done | = | |
| cctorchain | FAIL | FAIL | diff | = | boom_caught=1 vs 2（cctor 异常 catch 计数，15/15 行） |
| collchain | FAIL | FAIL | transpile | = | 仍卡 `ArrayList.Remove(object)` 无 icall |
| concatcase | PASS | **PASS** | done | = | |
| consolecase | FAIL | FAIL | transpile | = | Console.IsInputRedirected 未接线 |
| convcase | FAIL | **PASS** | done | **新绿** | **uint32 打印修复落地**（79/79，recheck9 已证） |
| convertcase | FAIL | **PASS** | done | **新绿** | **half-to-even 舍入修复落地**（41/41） |
| crossstat | PASS | **PASS** | done | = | |
| datetimechain | PASS | **PASS** | done | = | |
| delechain | FAIL | FAIL | cmake_build | = | 生成码 int32↔Il2CppObject* 类型错配 |
| delegate2 | FAIL | FAIL | transpile | = | Ldsflda M3 fold 缺 |
| delegatecase | PASS | **PASS** | done | = | |
| delegatecb | FAIL | FAIL | transpile | = | Marshal.GetFunctionPointerForDelegate 泛型无映射 |
| delegates | PASS | **PASS** | done | = | |
| encodingcase | FAIL | FAIL | transpile | = | **前进**：GetBytes 已接 → 卡 `GetChars` 分配形 char[]-result icall 不存在 |
| environment | PASS | **PASS** | done | = | |
| environment2 | FAIL | **PASS** | done | **新绿** | **tick 时序抖动修复**（10/10，recheck9 已证） |
| exceptions | PASS | **PASS** | done | = | |
| excext | FAIL | FAIL | transpile | = | Exception.get_Message 未接线 |
| exdeep | FAIL | **PASS** | done | **新绿** | **f18 多 catch 链式分派修复落地**（37/37，EHRegionEmitTests 7/7） |
| filechain | PASS | **PASS** | done | = | |
| fmtchain | PASS | **PASS** | done | = | |
| formatcase | FAIL | **PASS** | done | **新绿** | **∞ 编码仲裁后一致**（net48 live 下逐字节，oracle 规范化） |
| formatext | FAIL | FAIL | diff | = | L11 货币符号 � vs ¥（oracle 控制台伪影）+ L73 FormatException vs D112345 |
| gccase | FAIL | **PASS** | done | **新绿** | **strhash same-literal 修复落地**（GC-priming 修复） |
| gcext | FAIL | FAIL | transpile | = | WeakReference ctor 未接线 |
| gcstress | PASS | **PASS** | done | = | |
| generics | PASS | **FAIL** | cmake_build | **回归** | 生成码 `Box_1_int__ctor_…` 未声明（应 `Box_1__ctor_…`）——泛型实例 ctor 命名/展开回归 |
| genlist | FAIL | FAIL | transpile | = | 泛型实参 0x0E 无 M6 展开 |
| guidcase | FAIL | **PASS** | done | **新绿** | **Guid 值槽接线落地**（constrained callvirt + uint8_t[16] 槽） |
| hashtable2 | FAIL | FAIL | transpile | = | WriteLine(object) 未跟踪 box 类型 |
| hashtablecase | FAIL | FAIL | transpile | = | 同上 |
| interface | PASS | **PASS** | done | = | |
| interlockedcase | PASS | **FAIL** | cmake_build | **回归** | 生成码 `(uint8_t*)(&loc)` 传 `int32_t*`/`int64_t*` 参数——CodeGenerator.cs:2811 Guid 值槽 cast 过广（b59c6d9） |
| intern | PASS | **PASS** | done | = | |
| internchain | PASS | **PASS** | done | = | |
| iocase | FAIL | FAIL | transpile | = | Path.Combine 3 参未接线 |
| loop | PASS | **PASS** | done | = | |
| math2 | PASS | **PASS** | done | = | |
| mathcase | PASS | **PASS** | done | = | |
| mathext | FAIL | **PASS** | done | **新绿** | **∞ 编码仲裁后一致**（100/100 net48 live） |
| mathextra | PASS | **PASS** | done | = | |
| methods | PASS | **PASS** | done | = | |
| objects | PASS | **PASS** | done | = | |
| overflow | FAIL | **PASS** | done | **新绿** | **用例自身修复**（net48 编译期 checked 溢出 CS0220 已解——用例改造 + expected.txt） |
| overloads | PASS | **PASS** | done | = | |
| paramroot | PASS | **PASS** | done | = | |
| parsecase | FAIL | FAIL | transpile | = | Int32.TryParse ref/out 未接线 |
| pinvoke | FAIL | FAIL | cmake_build | **前进** | 转译已过（分类修复）→ 卡链接 undefined: add_ints/strlen_wrap/fail_with_error（mylib 符号） |
| pinvokeext | FAIL | FAIL | cmake_build | **前进** | 转译已过 → 卡 `const char*` vs lvalue 类型错配 |
| propcase | PASS | **PASS** | done | = | |
| props | PASS | **PASS** | done | = | |
| randomcase | FAIL | FAIL | transpile | = | String.Join 1 参未接线 |
| recurse | FAIL | FAIL | cmake_build | = | entry.cpp Main 签名不匹配 |
| recursive_arith | PASS | **PASS** | done | = | |
| regexcase | PASS | **PASS** | done | = | |
| sbchain | PASS | **PASS** | done | = | |
| statics | PASS | **PASS** | done | = | |
| strdeep | FAIL | FAIL | cmake_build | **前进** | 转译已过（Array.Empty 已接）→ 卡 `System_String_PadLeft` 未声明 |
| string | PASS | **PASS** | done | = | |
| stringbuilder | FAIL | **PASS** | done | **新绿** | **capacity 修复落地**（40/40，recheck9 已证） |
| stringext | FAIL | FAIL | diff | **前进** | 转译已过（Array.Empty 已接）→ 运行 L1 后 `Unhandled managed exception: value`（41/42） |
| stringext2 | PASS | **PASS** | done | = | |
| stringops | PASS | **PASS** | done | = | |
| swcase | FAIL | **PASS** | done | **新绿** | **Stopwatch.get_IsRunning 接线落地** |
| threadcase | FAIL | **PASS** | done | **新绿** | **Thread.get_CurrentThread 对象模型落地** |
| timecase | FAIL | FAIL | diff | **前进** | 转译已过 → 运行 L8 `Monday` vs `1`（DayOfWeek 枚举→字符串 未映射；9/9 行） |
| timeext | FAIL | FAIL | diff | = | L1 culture zh-CN vs en-US + L2 后 `Format specifier was invalid` 崩（22/84） |
| typecheckcase | FAIL | FAIL | transpile | = | TypeSpec SZARRAY 0x1D isinst |
| uintcmp | PASS | **PASS** | done | = | |
| valuesem | FAIL | **PASS** | done | **新绿** | **double NaN hash 修复落地** |
| valuetypes | PASS | **FAIL** | cmake_build | **回归** | 生成码 `Point__ctor((uint8_t*)(&loc0),…)`——CodeGenerator.cs:2811 Guid cast 过广（b59c6d9） |
| versioncase | FAIL | **PASS** | done | **新绿** | **Version 对象 gap 接线落地** |
| virtual | PASS | **PASS** | done | = | |

### 新增用例（18 例，不在 matrix3 表）

| 用例 | M4 阶段 | 失败归因 |
|---|---|---|
| boxdeep2 | cmake_build | `System_Object_ToString_a21e…` 未声明 + `System_UInt32_class` 未声明（生成码） |
| datetime2 | transpile | DateTime.Parse（非 ParseExact）未接线：runtime 表面无 Parse icall |
| delegateinst | diff | **0xC0000005 崩溃（0/24）**——委托实例相等性/多播相关 |
| enumdeep | transpile | IL opcode 0x0045（brtrue.s？）@169 不 M1 支持 |
| exceptionchain2 | **PASS** | |
| filestreamcase | **PASS** | |
| hashcase | **PASS** | |
| hexcase | **PASS** | |
| regexcase2 | **PASS** | |
| sortdeep | **PASS** | |
| strcompare | diff | L3/7/10 `True` vs `False` + L17 `Unhandled managed exception: value`（19/66） |
| stringrwcase | **PASS** | |
| swcase2 | **PASS** | |
| threadcase2 | **PASS** | |
| threadpoolcase | **PASS** | |
| uintcase | **PASS** | |
| uricase | **PASS** | |
| valdeep | **PASS** | |

**新增用例 PASS 13/18**：exceptionchain2/filestreamcase/hashcase/hexcase/regexcase2/sortdeep/stringrwcase/swcase2/threadcase2/threadpoolcase/uintcase/uricase/valdeep 全 PASS。

## 本轮净增 PASS（15 例，FAIL→PASS，全部经 net48 live 逐字节确认）

1. **base64case** — Encoding/Base64 接线（GetBytes raw-buffer + FromBase64String 修复落地）
2. **convcase** — uint32 打印修复（79/79，recheck9 已证）
3. **convertcase** — half-to-even 舍入 + 范围检查（41/41）
4. **environment2** — tick 时序（10/10，recheck9 已证）
5. **exdeep** — f18 多 catch 链式分派（37/37，EHRegionEmitTests 7/7）
6. **formatcase** — ∞ 编码仲裁（net48 live 逐字节）
7. **gccase** — strhash same-literal + GC-priming
8. **guidcase** — Guid 值槽（uint8_t[16] locals + NewGuid/Parse statement-form icalls）
9. **mathext** — ∞ 编码仲裁（100/100）
10. **overflow** — 用例自身（checked 溢出用例改造 + expected.txt）
11. **stringbuilder** — capacity 修复（40/40，recheck9 已证）
12. **swcase** — Stopwatch.get_IsRunning 接线
13. **threadcase** — Thread.CurrentThread 对象模型
14. **valuesem** — double NaN hash（-524288 → 2146959360 语义对齐）
15. **versioncase** — Version 对象 gap 接线

> recheck9 的 9 例 PASS（convcase/environment2/threadpoolcase/hexcase/stringops/stringbuilder/exceptionchain2/regexcase2/uintcase）在矩阵 4 下**全部保持 PASS**；recheck9 的 6 例 FAIL 中 sortdeep/mathext/formatcase/exdeep/valdeep **已翻 PASS**，仅 datetime2 仍 FAIL（转译期 Parse 缺口，见上）。

## 回归分析（3 例，matrix3 PASS → matrix4 FAIL，均 cmake_build）

1. **interlockedcase** — 生成码 `il2cpp_interlocked_*( (uint8_t*)((&locN)) ,…)` 但 runtime 签名 `int32_t*/int64_t*`。根因 = **commit b59c6d9（Guid 值槽 batch 11）CodeGenerator.cs:2811**：为 Guid 的 uint8_t[16] 槽把「空 cast + LocAddrExpr」一律加 `(uint8_t*)` cast，注释自述「the only ""-cast + ldloca-address combination today is Guid's」——但 Interlocked 的 `""` cast + 地址参数同样命中，被误 cast 成 byte 指针。
2. **valuetypes** — 同根因：`Point__ctor_fe171…((uint8_t*)(&loc0), 3LL, 4LL)`，ctor 签名 `Point*` 被 `(uint8_t*)` 破坏。
3. **generics** — 生成码调用 `Box_1_int__ctor_56a7d…`（泛型实例展开名）但声明为 `Box_1__ctor_56a7d…`——泛型实例 ctor 命名/展开回归（M6 泛型展开路径），与 #1/#2 不同源。

> 三者均由「matrix3 之后新增/改动的转译器代码」引入（b59c6d9 + 泛型展开在途改动），非测试方法差异——官方 run-tests.ps1 复跑确认一致 FAIL。若 3 回归修复，88 交集 PASS = **62/88（70.5%）**，超预期 60+/88。

## 失败归因分类（matrix4，34 例）

### 1. 转译器接线缺（LOUD 中止）— 17 例（50.0%）
- **未接 BCL 映射 / 对象模型缺**：arrayext2（FindIndex）、collchain（ArrayList.Remove）、consolecase（IsInputRedirected）、datetime2（DateTime.Parse 非 ParseExact）、delegatecb（GetFunctionPointerForDelegate）、encodingcase（GetChars 分配形）、excext（get_Message）、gcext（WeakReference ctor）、genlist（泛型 0x0E）、iocase（Path.Combine 3 参）、parsecase（TryParse ref/out）、randomcase（String.Join 1 参）
- **IL/fold 缺口**：delegate2（Ldsflda）、enumdeep（IL 0x0045 @169）、typecheckcase（SZARRAY 0x1D）
- **Console 域**：hashtable2/hashtablecase（WriteLine(object) 未跟踪 box 类型）

### 2. 转译器生成码 bug（转译成功但 C++ 编译失败）— 10 例（29.4%）
- **Guid cast 过广（b59c6d9）**：interlockedcase、valuetypes（+ generics 泛型 ctor 命名）
- **类型错配/未声明符号**：delechain（int32↔Il2CppObject*）、bitconvcase（Il2CppArray* vs 字面量 + IsLittleEndian 未声明）、boxdeep2（ToString/UInt32_class 未声明）、strdeep（PadLeft 未声明）、pinvokeext（const char* vs lvalue）、recurse（entry.cpp Main 签名）、pinvoke（链接 undefined mylib 符号）

### 3. runtime 缺失/错误（构建运行成功，输出不一致）— 7 例（20.6%）
- **异常/崩溃**：strcompare（L3 比较错 + L17 unhandled "value"）、stringext（L1 后 unhandled "value"，41/42）、delegateinst（0xC0000005，0/24）
- **格式/文化**：timeext（zh-CN vs en-US + FormatException）、formatext（货币符号 oracle 伪影 + L73）、cctorchain（boom_caught 1 vs 2）、timecase（DayOfWeek 枚举→字符串未映射，Monday vs 1）

### 4. 用例自身 — 0 例（matrix3 的 overflow/environment2 均已翻 PASS）

## 结论

1. **build 全绿**：`dotnet build src/il2cppc -c Release` → 0 错 0 警；dll 12:49:46 新于全部源（最新 StackFold.cs 12:49:16）。在途代理已收工（src 最后写入 12:49:16，13:20:36 确认 ≥30min 稳定）；**runtime 共享库因源过期已重新构建**（13:22:40，含全部新源），否则矩阵全部构建结果无效。
2. **PASS 47 → 72/106（全量 67.9%）/ 59/88（交集 67.0%），净增 12（交集）**；15 例新绿（base64/conv/convert/environment2/exdeep/formatcase/gccase/guidcase/mathext/overflow/stringbuilder/swcase/threadcase/valuesem/versioncase）+ recheck9 全部 9 例保持 + 新增 13/18；**3 例回归**（generics/interlockedcase/valuetypes，均 cmake_build，根因 = b59c6d9 Guid cast 过广 + 泛型 ctor 命名）。若回归修复 → **62/88（70.5%）**，达成 60+/88 预期。
3. **剩余 34 例主因**：转译器接线缺 17（未接 BCL 映射 + IL fold 缺口，占比 50.0%）、生成码 bug 10（Guid cast 过广 3 + 类型错配 7）、runtime 输出不一致 7（异常/崩溃 3 + 格式/文化 4）。8 例从早期阶段**前进到更深处**（bitconvcase/pinvoke/pinvokeext/strdeep/stringext/timecase 越过转译闸门，前 5 例进入构建/运行/diff 更深处），下一批修复面 = 3 回归优先（b59c6d9 cast 收窄 + 泛型 ctor 命名）+ 7 例 diff 类。
