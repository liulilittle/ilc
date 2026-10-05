# .NET Framework 4.8 对齐追踪清单 v2（工程总控表 — 域进度刷新版）

- 日期：2026-08-06（v2 快照；v1 = `docs/alignment-tracker.md`，2026-08-03，保留不动）
- 目的：ILC（IL→C++ AOT）对 .NET Framework 4.8 常用行为对齐的进度总控表，供所有代理（runtime / transpiler / tests 域）参考。
- git 基线：`69510bf`（M6 audit batch 12，HEAD）；git log -20 覆盖批次 8-12（stable batches 8-12 + audit batches 8-12）。
- 数据来源（本快照实测，方法同 v1）：
  - 域头：`src/runtime/include/internal/*.h` 共 **104** 个；`icall_` 前缀函数声明（`icall_[a-z0-9_]+(?=\()` 去重）合计 **455** 个，其中**已接线 384**（84.4%）。
  - 接线：`src/il2cppc/**/*.cs` 中对 `icall_*` 的唯一引用 **417** 个（v1: 236）；另有非 icall 命名（`il2cpp_*` 直连）接线 **114** 个唯一引用。
  - MethodMap 文件：`src/il2cppc/Emit/MethodMap*.cs` 共 **66** 个（committed 57 + untracked 9 + modified 19）。
  - e2e：`tests/cases` **128** 目录；matrix4 全量重跑 **PASS 72/106（67.9%）**；88 交集 **59/88（67.0%，+12）**。
- 状态图例：✅=已提交（git 跟踪且未修改）｜🟡=工作树未提交（untracked 或 tracked+modified，多为并行代理在途）｜⬜=未做。
- ⚠️ 快照说明：并行代理持续新建/改写头与实现（本快照实测期间 git status = 51 modified + 75 untracked 的域状态混合）；所有计数为测量时刻值，调度前复核 `git status` 与 mtime。

## 1. 域总表：域 | 头文件 | icall 声明 | 已接线 | 状态

> 仅列含 icall 声明的 39 个域头；其余 65 头 0 icall 声明（对象/直连/辅助域，见 §1.2）。

| 域 | 头文件 | icall 声明 | 已接线 | 状态 | 备注 |
|----|--------|-----------:|-------:|------|------|
| Crypto（AES/HMAC/MD5/SHA/RNG） | crypto_obj.h | 44 | 19 | 🟡 | 未跟踪在途（crypto_obj.cpp.inflight 仍在）；md5/sha1/sha256 hash_bytes 已接（hashcase 新绿）；HMAC/AES/RNG/SHA512 icall 未接 |
| Conv（conv 指令） | il2cpp_conv.h | 14 | 5 | 🟡 | 指令级转换部分接（convcase 79/79 PASS） |
| Marshal | il2cpp_marshal_ext.h | 7 | 0 | ⬜ | icall_marshal_* 未接线；P/Invoke 走独立路径（pinvoke/pinvokeext 已过转译闸） |
| Math | il2cpp_math.h | 17 | 11 | ✅ | clamp 等 6 缺口；其余全接 |
| Decimal | decimal.h | 22 | 18 | 🟡 | 未跟踪在途（decimal T2 已落地，decimalcase 用例已建） |
| GC 弱引用 | il2cpp_gc_ext.h | 8 | 4 | 🟡 | weakref 4 缺口（WeakReference 对象模型在途） |
| String 扩展2 | il2cpp_string_ext2.h | 14 | 11 | 🟡 | 3 缺口（Trim/Join 部分） |
| Type 检查 | il2cpp_typecheck.h | 6 | 3 | ✅ | 3 缺口（typecheckcase 仍 transpile：SZARRAY 0x1D） |
| Thread | il2cpp_thread.h | 7 | 4 | ✅ | 3 缺口（CurrentThread 对象模型已落地于 threadobj.h） |
| Boxing | il2cpp_boxing_ext.h | 8 | 6 | ✅ | 2 缺口 |
| Array 扩展2 | il2cpp_array_ext2.h | 11 | 10 | ✅ | 1 缺口：Array.FindIndex（worklist #1，P1） |
| Delegate | il2cpp_delegate_ext.h | 8 | 7 | ✅ | 1 缺口 |
| Format | il2cpp_format.h | 8 | 7 | ✅ | 1 缺口 |
| P/Invoke | il2cpp_pinvoke_ext.h | 1 | 0 | ✅ | 非接线路径（direct-call） |
| Thread 扩展 | threadext.h | 1 | 0 | ✅ | |
| Time | il2cpp_time.h | 33 | 33 | 🟡 | 全接；头文件 M（在途） |
| Time 扩展 | il2cpp_time_ext.h | 1 | 1 | ✅ | |
| Math 扩展 | il2cpp_math_ext.h | 17 | 17 | ✅ | |
| String | il2cpp_string_ext.h | 15 | 15 | ✅ | |
| String 扩展3 | il2cpp_string_ext3.h | 11 | 11 | ✅ | |
| StringBuilder | il2cpp_stringbuilder.h | 23 | 23 | 🟡 | 全接；头文件 M |
| Array | il2cpp_array_ext.h | 6 | 6 | ✅ | |
| Boxing 扩展2 | il2cpp_boxing_ext2.h | 17 | 17 | ✅ | |
| Value 语义 | il2cpp_value_semantics.h | 8 | 8 | ✅ | |
| Convert | il2cpp_convert.h | 13 | 13 | ✅ | |
| Console | il2cpp_console.h | 11 | 11 | 🟡 | 全接；头文件 M（enum box 臂已落地） |
| Environment | il2cpp_environment.h | 11 | 11 | 🟡 | 头文件 M |
| IO | il2cpp_io.h | 16 | 16 | 🟡 | 头文件 M（filestreamcase 新绿） |
| Hashtable | il2cpp_hashtable.h | 8 | 8 | ✅ | |
| GC API | il2cpp_gc_api.h | 7 | 7 | ✅ | |
| Random | il2cpp_random.h | 7 | 7 | ✅ | |
| Parse | il2cpp_parse.h | 6 | 6 | ✅ | TryParse icall 已接 |
| Format 扩展 | il2cpp_format_ext.h | 3 | 3 | ✅ | |
| Runtime 基座 | il2cpp_runtime.h | 10 | 10 | 🟡 | 头文件 M |
| Version/Uri | versionuri.h | 20 | 20 | ✅ | 对象闭环（versioncase/uricase 新绿） |
| Stopwatch | stopwatch.h | 11 | 11 | ✅ | 对象模型（swcase/swcase2 新绿） |
| StringReader/Writer | stringrw.h | 11 | 11 | ✅ | 对象模型（stringrwcase 新绿） |
| Thread 对象 | threadobj.h | 10 | 10 | 🟡 | 未跟踪在途（threadcase/threadcase2 新绿） |
| Decimal 格式 | decimal_format.h | 4 | 4 | 🟡 | 未跟踪在途 |

### 1.2 非 icall 命名域（`il2cpp_*` 直连，114 个唯一接线引用，v1 标注 * 者已大量接上）

| 域 | 头文件 | 声明（非 icall 命名） | 已接线 | 状态 | 备注 |
|----|--------|-----------:|-------:|------|------|
| BitConverter | bitconverter.h | 24 | ~24 | ✅ | get_bytes×10/to_*×9/hex/bits×2 全接（hexcase 新绿） |
| BinaryReader/Writer | binaryio.h | 22 | 16 | 🟡 | read×8/write×8 已接 |
| StreamIO | streamio.h | 14 | 15 | 🟡 | 含 memory/file 对象（filestreamcase 新绿） |
| ArraySort/Search | arraysort.h | 12 | 12 | ✅ | sort×5/index_of×4/binary_search×4/copy_to 直连（sortdeep 新绿） |
| Stack/Queue | stackqueue.h | 14 | 11 | ✅ | 对象模型已接（stack×6/queue×5） |
| Interlocked | il2cpp_interlocked.h | 12 | 6 | 🟡 | 原子家族部分接（interlockedcase matrix4 回归，R-A1） |
| IntPtr | intptr.h | 6 | 7 | 🟡 | 已接 |
| Monitor | monitor.h | 5 | 5 | ✅ | lockcase 用例已建 |
| Guid | guid.h | 6* | 6 | ✅ | 值槽接线（guidcase 新绿） |
| Enum | enumext.h | 5* | 5 | ✅ | enum_to_string/register 已接（enum box 臂） |
| Buffer | buffer.h | 2 | 3 | ✅ | |
| Stopwatch 静态 | stopwatch.h | 2 | 2 | ✅ | Frequency/GetTimestamp |
| DateTime 非 icall | timeext2.h | 5* | 8 | ✅ | parse/parse_exact/add_ticks/compare 等直连 |
| CultureInfo | cultureinfo.h | 6* | 0 | ⬜ | 仍走 resolver 字面量折叠（en-US），culturecase 用例已建 |
| 其它（http/ipaddr/socket/xml/regex/linq 等） | http.h/ipaddr.h/socket.h/xml_*.h/regex*.h/linq*.h | 各 0-4 | 少量 | 🟡 | 未跟踪在途域，用例已建（netcase/socketcase/xmlcase/linqcase） |

## 2. e2e 覆盖（matrix4 全量 + 最新复验）

> 权威矩阵：`docs/audit/e2e-full-matrix4.md`（2026-08-05 13:2x，106 用例 8 并行，net48 live 逐字节 diff）；复验：`docs/audit/e2e-recheck9.md`、`docs/audit/e2e-stable-check.md`。

| 指标 | matrix1 | matrix2 | matrix3 | **matrix4** | 说明 |
|---|---|---|---|---|---|
| 总用例 | 88 | 88 | 88 | **106** | +18 新增 |
| **PASS（逐字节一致）** | 38 | 39 | 47 | **72（67.9%）** | +25 |
| FAIL | 50 | 49 | 41 | **34** | transpile 17 / cmake_build 10 / diff 7 / case 0 |
| 88 例交集 PASS | 38 | 39 | 47 | **59（67.0%）** | +12（15 新绿 − 3 回归） |
| 3 回归 | — | — | — | generics/interlockedcase/valuetypes | 均 cmake_build，R-A1/R-A2 施工单已出 |

- **新绿 15 例**：base64case/convcase/convertcase/environment2/exdeep/formatcase/gccase/guidcase/mathext/overflow/stringbuilder/swcase/threadcase/valuesem/versioncase。
- **新增 18 例 PASS 13/18**：exceptionchain2/filestreamcase/hashcase/hexcase/regexcase2/sortdeep/stringrwcase/swcase2/threadcase2/threadpoolcase/uintcase/uricase/valdeep。
- **工作树新增用例（matrix4 之后，未入矩阵）**：bitopcase/cryptocase/culturecase/decimalcase/enumflags/ifcase/linqcase/lockcase/loopdeep/marshalcase/matrixcase/netcase/processcase/regdeep/sblong/socketcase/strfmt/typechain/weakrefcase/xmlcase 等 —— 待 matrix5 纳入。
- **EH 域独立复验（e2e-stable-check.md）**：exceptions 11/11、exceptionchain2 17/17、cctor 4/4 全 PASS（EH 4 例全绿：含 exdeep 37/37）。
- **worklist 基线**：`docs/plans/e2e-sprint-worklist.md` = 41 例施工表（40 真实 FAIL + overflow 已 PASS），P0 已闭环至 **48/88 起步基线**（convertcase +1）；per-case 修复记录 §5-13（timecase/stopwatch/threadpool/bitconv/convert/exdeep/sortdeep/delegateinst）。

## 3. 缺口优先序（引用最新 worklist：docs/plans/e2e-sprint-worklist.md + docs/roadmap-status-v4.md）

**P0 — 3 回归（最高优先，修复后 88 交集 → 62/88 达 70.5%，roadmap-status-v4 §2.3/§4.1）：**
- R-A1：CodeGenerator.cs:2811 Guid 值槽 blanket `(uint8_t*)` cast 过广 → interlockedcase + valuetypes 一修双卡（按目标签名收窄）。
- R-A2：泛型实例 ctor 命名分叉（`Box_1_int__ctor_…` vs `Box_1__ctor_…`，generics）。
- V-1：matrix5 全量复验（预期 75/106 全量 / 62/88 交集）。

**P1 接线缺口（worklist 施工单，精确修复点见各条）：**
- delegate2 Ldsflda（StackFold fold，worklist #10，P0 级）｜ bitconvcase byte[] newarr（CodeGenerator.cs:2691 ArrayElementType 加 System.Byte，worklist §10 #42）｜ arrayext2 FindIndex（icall_array_find_index + Predicate 映射，#1）｜ enumdeep switch IL 0x45（IlDecoder/StackFold/CodeGenerator 三件套，新缺口）｜ strdeep/stringext PadLeft/PadRight（MethodMap.String.cs 4 符号，#32/#34，P0）｜ delechain 泛型委托类型解析（#9）｜ recurse entry.cpp Main 签名（#31）｜ iocase Path.Combine 3 参（#24）｜ consolecase IsInputRedirected（#6）｜ timecase DayOfWeek 枚举→字符串（#37 后续，enum box 命名路径）｜ cctorchain cctor 异常缓存（#4）｜ delegateinst 0xC0000005（DelegateNewExpr + Target 字段 → 2-arg il2cpp_delegate_new，worklist §9.5 施工单精确就绪）｜ strcompare/stringext unhandled "value"（同根：异常 message 未处理）｜ formatext D100 回退（#17，harness UTF-8 已解 L11-66）。

**P2（需对象模型/ref-out 里程碑，worklist 保留 LOUD 拒绝或宽排队）：**
- collchain ArrayList.Remove(object)（#5）｜ encodingcase GetChars 分配形（#12，M5 唯一剩余对象态卡）｜ datetime2 DateTime.Parse（R1 已修 ParseExact，W2 部分未接）｜ parsecase TryParse ref/out（#27，卡 ref/out 里程碑）｜ excext get_Message + catch 变量（#14，异常对象模型里程碑）｜ gcext WeakReference 对象模型（#19）｜ delegatecb GetFunctionPointerForDelegate（#11）｜ genlist 泛型 0x0E 展开（#20）｜ hashtable2/hashtablecase WriteLine(object) box 类型跟踪（#22/#23）｜ timeext "y" 说明符 + zh-CN 文化（#38）。

**P3 — 显式拒绝项（需对象模型，保持 loud NotSupportedException）：**
- Console.IsInputRedirected（重定向状态）｜ Directory.Delete（仅 file_delete）｜ Marshal icall 家族（il2cpp_marshal_ext.h 7 个，走 P/Invoke direct 路径）｜ CultureInfo 文化数据（当前 en-US 字面量折叠）。

**基础设施（roadmap-status-v4 §3 Chain C/D）：**
- T101 MethodMapHub 未建（66 MethodMap 文件下 MethodRefResolver.cs 单 switch 序列化瓶颈）。
- 尾调用 ApplyTailCallLoop 工作树在途 +270 行（StackFold.cs:2890）未提交。
- LICM / 逃逸分析 / 可证边界检查消除未落地（M7 优化器路径；bench: object 0.02 / string 0.34 / arr_write 0.83）。
- M6.5 残项：WSL pathproc.cpp 硬缺口 + Threads/-pthread 链接；MSVC 2 test exe。
- bench 基线入库 + 3-compiler CI routine（M6 出口铁律）。

## 附：统计数字摘要（v2，2026-08-06 实测）

- 头文件总数：104（internal/*.h，v1: 64）
- icall 声明：455（函数形态去重；v1: 313）；**已接线 384（84.4%，v1: 236）**；转译器唯一 icall 引用 417
- 非 icall 命名直连：114 个唯一引用（bitconverter 26 / binary 16 / stream 15 / arraysort 12 / stackqueue 11 / intptr 7 / interlocked 6 / guid 6 / enum 5 / monitor 5 / buffer 3 / stopwatch 2 / datetime 8 等）
- MethodMap 文件：66（committed 57 + untracked 9：Crypto/Decimal/Marshal/Net/Process/Reflect/ThreadPool/TimeZone/Xml；modified 19）
- 含 icall 声明的域头：39 个；全接域：24 个；部分接域：15 个（最大缺口：crypto_obj 25、conv 9、math 6、decimal 4、gc_ext 4、marshal 7）
- e2e：tests/cases 128 目录；matrix4 PASS 72/106（67.9%）/ 88 交集 59/88（+12）；34 FAIL = transpile 17 / cmake_build 10 / diff 7；3 回归（generics/interlockedcase/valuetypes）
- git 基线：69510bf；工作树 51 modified + 75 untracked（并行代理在途）
