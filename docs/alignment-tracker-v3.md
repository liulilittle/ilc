# .NET Framework 4.8 对齐追踪清单 v3（工程总控表 — 域进度刷新版）

- 日期：2026-08-06（v3 快照；v1 = `docs/alignment-tracker.md`，2026-08-03；v2 = `docs/alignment-tracker-v2.md`，保留不动）
- 目的：ILC（IL→C++ AOT）对 .NET Framework 4.8 常用行为对齐的进度总控表，供所有代理（runtime / transpiler / tests 域）参考。
- git 基线：`f36f386`（M6 audit batch 14，HEAD）；log 覆盖批次 12-14（stable/audit batches 12-14 + matrix5 全量）。
- 数据来源（本快照实测，方法同 v2）：
  - 域头：`src/runtime/include/internal/*.h` 共 **104** 个；`icall_` 前缀函数声明（`icall_[a-z0-9_]+(?=\()` 去重）合计 **459** 个（v2: 455），其中**已接线 393**（85.6%，v2: 384/84.4%）。
  - 接线：`src/il2cppc/**/*.cs` 中对 `icall_*` 的唯一引用 **422** 个（v2: 417）；非 icall 命名（`il2cpp_*` 直连）token 级唯一引用 **~140**（26 个直连域头，见 §1.2；口径与 v2 不同，见备注）。
  - MethodMap 文件：`src/il2cppc/Emit/MethodMap*.cs` 共 **66** 个（v2: 66；**全部 committed**，v2 时 9 个 untracked 已入库；工作树 modified 2：Crypto/Type）。
  - e2e：`tests/cases` **137** 目录（v2: 128）；matrix5 全量 **PASS 92/131（70.2%）**；106 交集 **80/106（75.5%，+8）**。
- 状态图例：✅=已提交（git 跟踪且未修改）｜🟡=工作树未提交（untracked 或 tracked+modified，多为并行代理在途）｜⬜=未做。
- ⚠️ 快照说明：并行代理持续新建/改写头与实现（本快照实测期间 git status = 21 modified + 33 untracked；v2 为 51+75，已大批入库）；所有计数为测量时刻值，调度前复核 `git status` 与 mtime。

## 1. 域总表：域 | 头文件 | icall 声明 | 已接线 | 状态

> 仅列含 icall 声明的 39 个域头；其余 65 头 0 icall 声明（对象/直连/辅助域，见 §1.2）。

| 域 | 头文件 | icall 声明 | 已接线 | 状态 | 备注 |
|----|--------|-----------:|-------:|------|------|
| Crypto（AES/HMAC/MD5/SHA/RNG） | crypto_obj.h | 44 | 20 | 🟡 | hashcase/cryptocase e2e 全绿（矩阵验证修复 5 阻断）；HMAC/AES/SHA512/RNG icall 未接 |
| Conv（conv 指令） | il2cpp_conv.h | 14 | 5 | 🟡 | convcase 79/79 PASS；conv.u8 零扩展已落地 |
| Marshal | il2cpp_marshal_ext.h | 7 | 0 | ⬜ | icall_marshal_* 未接线；P/Invoke 走独立 direct 路径（pinvoke e2e PASS） |
| Math | il2cpp_math.h | 17 | 11 | ✅ | clamp 等 6 缺口 |
| Decimal | decimal.h | 22 | 22 | ✅ | **T3 落地全接**（3ab788d；v2 时 18/22） |
| GC 弱引用 | il2cpp_gc_ext.h | 8 | 4 | 🟡 | weakref 4 缺口（WeakReference 对象模型在途；weakrefcase e2e 0xC0000005 崩溃） |
| String 扩展2 | il2cpp_string_ext2.h | 14 | 11 | ✅ | 3 缺口（Trim/Join 部分） |
| Type 检查 | il2cpp_typecheck.h | 6 | 3 | ✅ | 3 缺口（typecheckcase transpile：SZARRAY 0x1D，#39） |
| Thread | il2cpp_thread.h | 7 | 4 | ✅ | 3 缺口（CurrentThread 对象模型已落地于 threadobj.h） |
| Boxing | il2cpp_boxing_ext.h | 8 | 6 | ✅ | 2 缺口 |
| Array 扩展2 | il2cpp_array_ext2.h | 11 | 10 | ✅ | 1 缺口：Array.FindIndex（worklist #1，P1） |
| Delegate | il2cpp_delegate_ext.h | 8 | 7 | ✅ | 1 缺口 |
| Format | il2cpp_format.h | 8 | 7 | ✅ | 1 缺口 |
| P/Invoke | il2cpp_pinvoke_ext.h | 1 | 0 | ✅ | 非接线路径（direct-call） |
| Thread 扩展 | threadext.h | 1 | 0 | ✅ | |
| Time | il2cpp_time.h | 33 | 33 | ✅ | 全接（v2: 🟡 已入库） |
| Time 扩展 | il2cpp_time_ext.h | 1 | 1 | ✅ | |
| Math 扩展 | il2cpp_math_ext.h | 17 | 17 | ✅ | |
| String | il2cpp_string_ext.h | 15 | 15 | ✅ | |
| String 扩展3 | il2cpp_string_ext3.h | 13 | 13 | ✅ | 全接（+2 新 icall：LastIndexOf match-end 等，2fca145） |
| StringBuilder | il2cpp_stringbuilder.h | 23 | 23 | ✅ | 全接；stringbuilder/sblong e2e 已验（sblong 越界崩为生成码问题） |
| Array | il2cpp_array_ext.h | 6 | 6 | ✅ | |
| Boxing 扩展2 | il2cpp_boxing_ext2.h | 19 | 19 | ✅ | 全接（+2 新 icall：UInt32 box/unbox，89a44e8） |
| Value 语义 | il2cpp_value_semantics.h | 8 | 8 | ✅ | |
| Convert | il2cpp_convert.h | 13 | 13 | ✅ | convertcase 41/41 PASS |
| Console | il2cpp_console.h | 11 | 11 | ✅ | 全接；u32/u64 臂落地（convcase 79/79） |
| Environment | il2cpp_environment.h | 11 | 11 | ✅ | |
| IO | il2cpp_io.h | 16 | 16 | ✅ | 全接（IO 4-gap closure，2659d8c） |
| Hashtable | il2cpp_hashtable.h | 8 | 8 | ✅ | |
| GC API | il2cpp_gc_api.h | 7 | 7 | ✅ | |
| Random | il2cpp_random.h | 7 | 7 | ✅ | |
| Parse | il2cpp_parse.h | 6 | 6 | ✅ | TryParse icall 已接 |
| Format 扩展 | il2cpp_format_ext.h | 3 | 3 | ✅ | |
| Runtime 基座 | il2cpp_runtime.h | 10 | 10 | ✅ | marshal icall 签名契约已对齐（2fca145） |
| Version/Uri | versionuri.h | 20 | 20 | ✅ | 对象闭环（versioncase/uricase 新绿） |
| Stopwatch | stopwatch.h | 11 | 11 | ✅ | 对象模型（swcase/swcase2 新绿） |
| StringReader/Writer | stringrw.h | 11 | 11 | ✅ | 对象模型（stringrwcase 新绿） |
| Thread 对象 | threadobj.h | 10 | 10 | ✅ | 对象模型（threadcase/threadcase2 新绿） |
| Decimal 格式 | decimal_format.h | 4 | 4 | ✅ | |

### 1.2 非 icall 命名域（`il2cpp_*` 直连；v3 测量 = 源码 token 级唯一调用引用）

> 口径备注：v2 的 "114 个唯一引用" 为 MethodMap 条目级计数（含跨文件重复去除方式未留脚本），v3 重测为
> 对 26 个直连域头逐个 `il2cpp_[a-z0-9_]+(?=\()` 去重后在 `src/il2cppc` 中引用计数——绝对值口径不同，
> **仅比较形状**（未接 = 待施工面）。合计 26 头 ~178 声明 / ~137 已接（token 级）。

| 域 | 头文件 | 声明 | 已接 | 备注 |
|----|--------|-----:|-----:|------|
| BitConverter | bitconverter.h | 25 | ~23 | ToHex 落地（hexcase 新绿）；GetBytes×10/to_*×9 全接 |
| BinaryReader/Writer | binaryio.h | 22 | ~14 | read×8/write×8 已接 |
| StreamIO | streamio.h | 14 | ~14 | 含 memory/file 对象（filestreamcase 新绿） |
| ArraySort/Search | arraysort.h | 12 | ~12 | sort×5/index_of×4/binary_search×4/copy_to 直连（sortdeep 新绿） |
| Stack/Queue | stackqueue.h | 14 | ~12 | 对象模型已接（stack×6/queue×5） |
| Interlocked | il2cpp_interlocked.h | 12 | ~6 | 原子家族部分接（interlockedcase matrix5 翻 PASS） |
| IntPtr | intptr.h | 7 | ~7 | 已接 |
| Monitor | monitor.h | 5 | ~5 | lockcase 用例已建（matrix5 cmake 阻塞，8/6 修复已实施） |
| Guid | guid.h | 6 | ~6 | 值槽接线（guidcase 新绿） |
| Enum | enumext.h | 5 | ~5 | enum_to_string/register 已接（enum box 臂） |
| Buffer | buffer.h | 3 | ~3 | |
| Stopwatch 静态 | stopwatch.h | 7 | ~3 | Frequency/GetTimestamp 直连 |
| DateTime 非 icall | timeext2.h | 6 | ~6 | parse/parse_exact/add_ticks/compare 等直连 |
| P/Invoke 直连 | il2cpp_pinvoke.h | 5 | ~4 | pinvoke/pinvokeext 转译/构建阻点已清（#28/#29 落地；run 侧待 #29 "ptr" 一行修复） |
| TypeInfo/Reflect | typeinfo.h/reflect_base.h | 8+2 | ~4+2 | mode B 反射 CLI 落地（81d25b6） |
| CultureInfo | cultureinfo.h | 6 | 0 | ⬜ 仍走 resolver 字面量折叠（en-US）；culturecase 卡 `::lit::` 残留（#45） |
| 其它（http/ipaddr/socket/regex/linq 等） | http.h/ipaddr.h/socket.h/regex*.h/linq*.h | 各 0-9 | 少量 | regex 已接 4/4；socket 0/7、linq 0/9、allocator 0/6 未接（对象域里程碑） |

## 2. e2e 覆盖（matrix5 全量 + 最新复验）

> 权威矩阵：`docs/audit/e2e-full-matrix5.md`（2026-08-06 06:31-06:51，131 用例 8 并行，net48 live 逐字节 diff；
> 前置：共享 runtime 库全新重建 6:31:24，107 成员）；复验：`docs/audit/e2e-batchC-status.md`、`runtime-fullcheck6.md`。

| 指标 | matrix1 | matrix2 | matrix3 | matrix4 | **matrix5** | 说明 |
|---|---|---|---|---|---|---|
| 总用例 | 88 | 88 | 88 | 106 | **131** | +25 新增 |
| **PASS（逐字节一致）** | 38 | 39 | 47 | 72（67.9%） | **92（70.2%）** | +20 |
| FAIL | 50 | 49 | 41 | 34 | **39** | transpile 21 / cmake 8 / diff 10 |
| 交集 PASS（88/106 例） | 38 | 39 | 47 | 59（67.0%） | **80/106（75.5%）** | +8（9 新绿 − 1 回归） |
| 回归 | — | — | — | 3 | **1（internchain）** | StringPool T3 Concat 池化破坏 fresh-instance |

- **matrix5 新绿 9 例（全 net48 live 确认）**：bitconvcase/consolecase/formatext/generics/interlockedcase/strdeep/stringext/timecase/valuetypes——**matrix4 的 3 例回归（generics/interlockedcase/valuetypes）全部翻 PASS**（b59c6d9 cast 收窄 + 泛型 ctor 命名展开）。
- **新增 25 例 PASS 12**：arraydeep/convdeep/cryptocase/enumflags/loopdeep/marshalcase/matrixcase/processcase/regdeep/sortcase2/strfmt/timezonecase。
- **矩阵验证修复 5 阻断**（非代理修复，验证方）：runtime.cpp marshal icall 签名×2、crypto_obj.cpp 类描述符命名空间、generated.h 缺 crypto_obj.h include、StackFold ReturnsRef 未跟随 re-resolve。
- **matrix5 后新增用例（未入矩阵）**：arrcomp（PASS 全链）、webcase（施工单 #43 在途）；tests/cases 目录 137 = matrix5 131 + 6 在途（含上述 2 + 其余待 matrix6 纳入）。
- **工作树修复（8/6 10:0x，未入矩阵，待 matrix6）**：lockcase 2 bug（静态 ref 字段存储 Il2CppObject\* + finally 异常副本 label）转译 0 错 0 警、cctorchain 守卫发射（wo4 §2.1 逐字，单测 GREEN）、recurse entry argv 构造（单测 GREEN）。
- **EH 域复验**：exceptions/exceptionchain2/exdeep/cctor 全绿（EH 域 4 例，含 exdeep 37/37、exceptionchain2 17/17）。
- **worklist 基线**：`docs/plans/e2e-sprint-worklist.md`（8/6 10:08 更新）= 42 施工单（#42-#46 新发）；matrix4 的 41 例 FAIL 已闭环 ~20（convcase/convertcase/overflow/formatcase/mathext/strdeep/stringext/stringbuilder/gccase/timecase/exdeep/sortdeep/swcase/bitconvcase/hexcase/delegateinst-部分 等）；P0 清单（§3）已翻新见下。

## 3. 缺口优先序（引用最新 worklist：docs/plans/e2e-sprint-worklist.md §15/§16 + e2e-full-matrix5.md）

**P0 — 回归 + 崩溃 + 一修双卡（最高优先；前 5 项修后交集预期 80 → ~88/106，达 83%）:**

1. **internchain 回归（StringPool 域，worklist 无旧单）**：T3 把 `Concat("intern-","chain")` 常量折叠进字面量池 g_strPool[0] → ReferenceEquals True vs net48 False（fresh-instance 语义破坏）——**唯一交集回归**，修后 80→81/106。
2. **#44 HAS_THIS 签名头分类（T 域，MethodSignatureCoder.Decode 读 0x20 bit；MethodRefResolver.cs:337 heuristic 替换）**：delegate2（get_Method 误判 STATIC）+ enumdeep（op_Equality 误判 INSTANCE）**一修双卡**；enumdeep 修后下一阻点 = switch IL 0x45（IlDecoder/StackFold/CodeGenerator 三件套，新缺口）。
3. **崩溃域 ×2**：weakrefcase（0xC0000005 0/9，WeakReference 对象模型 + icall_weakref_ctor/get_target/is_alive + WaitForPendingFinalizers，#19 升级）；delegateinst（0xC0000005 0/24，StackFold delegate newobj 提取 target → 2-arg `il2cpp_delegate_new(&fn, target)`，runtime 已支持）。
4. **#29 pinvokeext 残余根因（T 域，一行）**：TempSlotPass.cs:241 KindOf 加 `or "ptr"`（reverse_static intptr_t 结果与 int32 载槽合并复用 → 截断 AV）；10:1x 已锁定，全链其余已在源。
5. **static/instance 分类家族（T 域，可能与 #44 同根）**：netcase/socketcase（IPAddress.Parse 判 INSTANCE）、aescase/typechain/xmlcase（callvirt on non-instance）。
6. **lockcase（matrix5 cmake 阻塞）**：int32↔Il2CppObject\* 错配——**8/6 10:0x 修复已实施**（CodeGenerator.cs 独占，转译 0 错 0 警 + 单测 35/37），待矩阵复核。
7. **cctorchain #4（R 域）**：.cctor 抛异常后二次访问不重抛（boom_caught 1 vs 2）——**守卫发射已实施**（wo4 §2.1 逐字：done 后置 + running + exc 缓存重抛，单测 GREEN），e2e 待共享 runtime 重建复核。
8. **recurse #31（T 域）**：entry.cpp Main 无参调用——**argv 构造已实施**（il2cpp_entry_main(argc,argv) + string[] 构造，单测 GREEN），e2e 待复核。

**P1 接线缺口（worklist 施工单，精确修复点见各条）：**
- #1 arrayext2 FindIndex（icall_array_find_index + Predicate 映射）｜ #43 webcase WebClient（System_Net_WebClient_class 描述符 + icall_webclient_download_string）｜ #45 culturecase（StackFold callvirt 路径缺 marker 折叠 → `::lit::` 残留）｜ #46 hashcase new string(char,int)（il2cpp_string_repeat 载体 + MethodMap.String.cs .ctor 条目）｜ #24 iocase Path.Combine 3 参（path.cpp 或嵌套 2 参展开）｜ #39 typecheckcase SZARRAY 0x1D（GenericTypeSpecDefinitionName）｜ #20 genlist 泛型 string 0x0E 展开（+dictcase/linqcase 同根 0x0E/0x02）｜ #9 delechain 泛型委托类型解析（勿回退 int32 默认实例化）｜ #11 delegatecb GetFunctionPointerForDelegate ｜ #5 collchain ArrayList.Remove(object) ｜ #38 timeext "y" 说明符（L20 起崩溃）+ zh-CN 文化（P2 可选）｜ strcompare ~5 False / datetime2 5 False / bitopcase ~10 False / sblong L2 越界（生成码/runtime 语义域，归因见 matrix5 §3）｜ #30 randomcase Join 重载（matrix5 归因已改：RNG 时序值 diff 非接线）。

**P2（需对象模型/ref-out 里程碑，worklist 保留宽排队）：**
- #14 excext get_Message + catch 变量（异常对象模型里程碑）｜ #19 gcext WeakReference 对象模型（与 P0-3 合并）｜ #27 parsecase TryParse ref/out（ref/out 里程碑）｜ #22/#23 hashtable2/hashtablecase WriteLine(object) box 类型跟踪｜ #2/#12 encodingcase/base64case 分配式 GetBytes（icall_encoding_get_bytes_alloc + emitter byte[] 返回）｜ #21 guidcase boxed Guid ToString callvirt（VirtualTarget 属主 System.Object 派发，施工单已有）。

**P3 — 显式拒绝项（需对象模型，保持 loud NotSupportedException）：**
- Directory.Delete（仅 file_delete）｜ Marshal icall 家族（il2cpp_marshal_ext.h 7 个，走 P/Invoke direct 路径）｜ CultureInfo 文化数据（当前 en-US 字面量折叠）｜ Console.IsInputRedirected（v2 时 P3，**已落地接线**——consolecase 新绿，移出拒绝项）。

**基础设施（roadmap-status-v4 §3 Chain C/D，更新）：**
- T101 MethodMapHub 未建（66 MethodMap 文件全 committed，但 MethodRefResolver.cs 单 switch 序列化瓶颈仍在）。
- 优化器路径已提交（81d25b6：TailCall fold + TempSlotPass + InlinePass + LicmPass + StringPool T3）——v2 的"LICM 未落地"过期；**逃逸分析 / 可证边界检查消除仍未落地**（M7 路径）。
- 尾调用 ApplyTailCallLoop 已随 batch 13 入库（v2 的"工作树在途 +270 行"已过期）。
- M6.5 残项（WSL pathproc/Threads 链接、MSVC 2 test exe）+ bench 基线入库 / 3-compiler CI：未在本快照复核，见 batchC-status/three-compiler-recheck4。

## 附：统计数字摘要（v3，2026-08-06 实测）

- 头文件总数：104（internal/*.h，与 v2 同）
- icall 声明：459（+4 vs v2）；**已接线 393（85.6%，+9）**；转译器唯一 icall 引用 422（+5）
- 非 icall 命名直连：26 头 ~178 声明 / ~137 已接（token 级重测口径，见 §1.2；v2 的 114 为 MethodMap 条目级口径）
- MethodMap 文件：66（**全部 committed**；modified 2：Crypto/Type；v2: 57+9 untracked+19 modified）
- 含 icall 声明的域头：39 个；**全接域 25**（v2: 24，decimal/boxing_ext2/string_ext3 新全接）；部分接域 14（最大缺口：crypto_obj 24、conv 9、marshal 7、math 6、gc_ext 4、string_ext2 3）
- e2e：tests/cases 137 目录（v2: 128）；matrix5 PASS 92/131（70.2%）/ 106 交集 80/106（75.5%，+8）；39 FAIL = transpile 21 / cmake_build 8 / diff 10；1 回归（internchain，StringPool 域）
- git 基线：f36f386（M6 audit batch 14）；工作树 21 modified + 33 untracked（v2: 51+75——大批入库）
- 8/6 工作树修复待矩阵复核：lockcase/cctorchain/recurse（CodeGenerator.cs 域，单测已绿）

---

## 附：2026-08-07 追加快照（HEAD efa74e3 / M6 batch 20；不改动上文历史数据）

> 本追加记录批次 15-20 后的域/矩阵/性能状态；上文 §1-§3 为 f36f386 快照，保留可比基线。
> 追加依据（本快照实测）：HEAD `efa74e3`（21 commits since f36f386）；unit **1369/1369**（dotnet test exit 0）；
> runtime 127 test_*.cpp / 123 exe PASS（icallc 快照）；e2e 169 用例目录。

- **⚠ 头文件架构合并（可比性重置）**：批次 15-20 将 `src/runtime/include/internal/*.h` 从 v3 口径的 104 个大幅合并为 **27 个**；icall 声明重测 = **262** 唯一名（v3: 459），转译器 icall 引用 **504** 唯一名，声明∩接线 **171**。**上文 v3 的 459/393 数字已过时，与现架构不可直接比**（本快照重测口径，调度前以本段为准）。
- **MethodMap 文件 75**（v3: 66，+9；全部 committed）。
- **矩阵7 进行中（m7a-d 四代理）**：batch1（aescase..delechain）**PASS 32**/41 有效；batch2（56-110）**PASS 39**/55；batch3（111-155）首跑被 8 并发 build 污染待重跑；m7a 14 例 BLOCKED（并发 src 编辑误报）待复核。矩阵6 3 回归（crypto_obj.h/timezone.h include）已修——hashcase/hashchain 矩阵7 翻 PASS；matrix5 的 internchain 回归矩阵7 仍绿（池化收窄解决）。
- **bench v3（2026-08-07 18:28/18:42，mingw，HEAD 41072f4，工作树 clean）6/10 反超 net48**：fib 1.50 / loop 0.94 / string 1.06 / array 6.00 / object 0.04 / sort 0.60 / hash 0.86；arr_sum 8.00 / arr_write 4.20 / str_len 1.10（arr_copy/str_chars/str_concat 仍 FAIL）。驱动：边界检查消除 + DcePass + AllocPromoPass + InlinePass 深内联 + StringPool T3 + RootSlotPass。**object 0.04 为唯一大差距**（需完整逃逸分析+分配下沉）。
- **新域**：COM stage 1（56347ef：RCW CoCreateInstance/QueryInterface/GetActiveObject + CCW IUnknown vtable；Windows-only 保证 + 跨平台 stub 不崩；test_com）｜calli+ELEMENT_TYPE_FNPTR 全链（345ec8c，fnptr e2e PASS，PRD#16 达成）｜IL 全指令审计 **217/218 decode**（arglist/endfilter/typedref LOUD）｜WinForms 计划（winforms-plan.md，41072f4：625 P/Invoke/198 ComImport/223 资源/3 阶段）｜P/Invoke UTF-16 + pinvoke struct registry（744a7c8，x64 MSG.pt@36 ABI）｜资源嵌入 RES-1（PRD#18，未实施）。
- **域接线新推进（批次 16-18）**：http WebClient / streamio 对象层 16 icall / gchandle 8 / dependhandle 7 / gc 18 / string allocation 9 / encoding GetChars 3-enc 分配形 / XML 5 / MathF 24（corert float 语义）/ reflecthandle 10 / TypeInfo 表发射修复。
- **矩阵7 剩余 FAIL 归因（m7d 汇总）**：transpile 7（泛型 0x0E/0x02 展开、Directory.Delete 非递归、fromBase、WriteLine(object)、FindIndex）｜build 8（System_*_class metadata extern、static_cast Node**→void**、int↔Il2CppObject*）｜diff 1（format2 格式器）。
- 权威快照：docs/project-state-20260807.md（新建）；roadmap-status-v5.md snapshot 8。
