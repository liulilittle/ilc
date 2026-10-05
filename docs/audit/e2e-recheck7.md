# e2e Recheck 7 — 本批修复影响用例增量复验（2026-08-05 00:4x，完成）

只读+运行+写文档；未修任何代码；未 commit。

## 前置核查（任务门槛）

| 项目 | 结果 | 证据 |
|------|------|------|
| il2cppc.dll 重建 | `dotnet build src/il2cppc -c Release` → 0 错 0 警；终验 dll 00:34:23 > 全部真实源（最新源仅 AutoGen AssemblyInfo 00:34:22） | [BASH:dotnet build EXIT=0]、[BASH:dll_newer=True] |
| 转译器相关源稳定（mtime ≥30min） | CodeGenerator 33.8min / StackFold 349.7 / MethodRefResolver 408.6 / MethodMap.Guid 30.1 / MethodMap.Console 35.8 全稳定 | [BASH:final poll 00:32:30] |
| runtime 相关源稳定（mtime ≥30min） | boxingext2 34.3 / stringext2 30.5 / console 39.5 / runtime 39.8 / string 41.2 全稳定 | [BASH:final poll 00:32:30] |
| 共享 runtime 库重建 | `cmake --build build/runtime_e2e_shared --target il2cpp_runtime` → 100% Built（lib 00:02:33，含 boxingext2/console/runtime/string/stringext2 最新 obj） | [BASH:CMAKE_EXIT=0] |
| net48 oracle exe | 10/10 用例 exe 均存在 | [BASH:EXE_OK ×10] |

**关键过程发现（必须先记录）**：任务启动后并行代理仍在活跃编辑 runtime 与转译器
（boxingext2.cpp 23:58 / console.cpp 23:53 / runtime.cpp 23:52 / string.cpp 23:51 /
MethodMap.Console.cs 23:57 / dll 两次被代理重建 0:04、0:06:50）。首轮 e2e（19:10 旧库 +
18:43 旧 dll）产生**不稳定结果**（stringext2 41/42→42/42、boxing2 build-崩溃→transpile-LOUD、
guidcase Parse-LOUD→op_Equality 分类错）——均为在途编辑伪影。**按任务门槛等待至全部
相关文件 ≥30min 稳定后重跑，以下为终态结果。**

## 状态表（10 用例，终态）

| 用例 | 转译 | 构建 | 运行 | diff | 状态 | 精确缺口 |
|------|------|------|------|------|------|---------|
| base64case | FAIL(2) | – | – | – | FAIL | **Encoding.UTF8.GetBytes LOUD**（raw-buffer code-page 协议文档化不可接，MethodRefResolver.cs:366）——符合预期"卡 Encoding.UTF8 LOUD" |
| guidcase | FAIL(2) | – | – | – | FAIL | **Guid::op_Equality 静态/实例误分类**（offset 0x00CD：`p == g1` 被作 INSTANCE 弹 this 但栈空）——已**越过**预期的 Guid::Parse LOUD（Parse 已接 il2cpp_guid_parse_strict） |
| collchain | FAIL(2) | – | – | – | FAIL | **ArrayList::Remove(object) LOUD**（无 runtime icall，指引 RemoveAt(IndexOf(x))）——符合预期"卡 Remove(object) LOUD" |
| math2 | OK | OK | EXIT=0 | **PASS 64/64** | **PASS** | 无（L58/L59 Round(x,2)=3.14/2.68 逐字节正确；L63 Truncate(-0.5)=0 正确） |
| exdeep | OK | OK | EXIT=1 | 26/37 | 部分 | f16 `throw null` → 未处理崩级联（f17+ 缺失）；**f1-f15b 共 26 行逐字节一致**（stdout 分离验证，Unhandled 头为 stderr 合并伪影）——与基线 26/37 相同 |
| boxext | OK | OK | EXIT=0 | **PASS 15/15** | **PASS** | 无（vs net48 live 逐字节） |
| boxing2 | FAIL(2) | – | – | – | FAIL | **Console.WriteLine(ulong) LOUD**（新门；console 代理加 0x0B LOUD 防 UInt64.MaxValue 打印为 -1）——从旧"build+run 26/34"**阶段回退**至转译期 LOUD，非 box 修复回归 |
| boxdeep | OK | OK | EXIT=0 | **PASS 32/32** | **PASS** | 无（接口表 is / 字符串描述符 / catch 全对；vs expected.txt + net48 live 双逐字节） |
| delegatecase | OK | OK | EXIT=0 | **PASS 57/57** | **PASS** | 无（vs net48 live 逐字节） |
| stringext2 | OK | OK | EXIT=0 | **PASS 42/42** | **PASS** | 无（join 修复落地；vs net48 live 逐字节；此前 41/42 的 L19 join + ArgumentNullException 全消除） |

**汇总：PASS 5/10（math2/boxext/boxdeep/delegatecase/stringext2），部分 1/10（exdeep 26/37），
FAIL 4/10（base64case/guidcase/collchain/boxing2 全转译期 LOUD/分类错）。**

## 对比 matrix2 基线（39/88）

本批 10 用例在 matrix2（39/88）中**全部 FAIL**：

| 用例 | matrix2 状态 | 本批状态 | 变化 |
|------|-------------|---------|------|
| base64case | ldtoken FIELD InitializeArray 阻塞 | Encoding.UTF8 GetBytes LOUD | 前进（InitializeArray 折叠已落地，达 Encoding 墙） |
| guidcase | 0xFE16 constrained 阻塞 | op_Equality 分类错 | 前进（constrained+op_Inequality+Parse 全过） |
| collchain | dup 空栈 0x0523 | Remove(object) LOUD | 前进（dup 失步修复，达 Remove 墙） |
| math2 | 9/64 | **64/64 PASS** | **FAIL→PASS** |
| exdeep | 29/37 | 26/37（同基线） | 持平 |
| boxext | 16/15 FAIL | **15/15 PASS** | **FAIL→PASS** |
| boxing2 | build+run FAIL 35/34 | 转译期 Console ulong LOUD | 阶段回退（新 LOUD 门） |
| boxdeep | 26/32 | **32/32 PASS** | **FAIL→PASS** |
| delegatecase | 0xC0000005 崩 0/57 | **57/57 PASS** | **FAIL→PASS** |
| stringext2 | 41/42 | **42/42 PASS** | **FAIL→PASS** |

净增 PASS：**+5**（math2/boxext/boxdeep/delegatecase/stringext2）。按本批推估聚合
**39 + 5 = 44/88**（假设其余 78 用例无回归；本批只重验这 10 例，未做全量）。

## 施工单（按解阻面排序）

| # | 用例 | 缺口 | 归属域 | 修复方向 |
|---|------|------|--------|---------|
| R1 | boxing2 | Console.WriteLine(ulong) LOUD | console UTF-8 域（已自报施工单） | runtime 加 `il2cpp_console_write_line_u64`（uint64 十进制，同 write_line_l 形）；落地后 MethodMap.Console.cs 0x0B 恢复映射 |
| R2 | guidcase | op_Equality 静态/实例误分类（System_Guid_op_Equality fallback 落 INSTANCE） | StackFold/CodeGenerator 分类域 | Guid 域加 op_Equality 映射（runtime 已有 equals icall，MethodMap.Guid.cs 注释提到 equals/compare 面）→ 消除 fallback 分类 |
| R3 | exdeep | f16 `throw null` 未处理崩（应 NRE） | runtime throw-null 域 | StackFold throw 命名域（memory 记录既有缺口） |
| R4 | base64case | Encoding.GetBytes raw-buffer LOUD | Encoding 域 | allocating runtime icall + 发射器数组数据指针 cast（域内文档已指路，非新发现） |
| R5 | collchain | ArrayList.Remove(object) LOUD | ArrayList/runtime 域 | runtime remove-object icall 或用例改 RemoveAt(IndexOf(x)) |

## 验证证据（终态）

- [BASH:run-e2e-recheck7.ps1 全 10 例 EXIT=0]、[READ:recheck7.csv]（math2/boxdeep/boxext/delegatecase/stringext2 构建+运行全过）
- [BASH:CRLF 归一化逐字节]：math2 net48-live True / boxdeep expected+live True / boxext live True / delegatecase live True / stringext2 live True（5/5 全字节一致）
- [BASH:exdeep stdout-only]：26 行逐字节 == expected.txt 前 26 行，首差在 line 27（f16 throw null）
- [BASH:4 转译失败终态复验]：base64=Encoding LOUD / guid=op_Equality 0x00CD / collchain=Remove LOUD / boxing2=Console ulong LOUD（dll 00:34:23 复跑 EXIT=2 ×4）
- [BASH:matrix2 results.csv 基线]：88 例 PASS=39；本批 10 例 matrix2 全 FAIL
- [BASH:过程稳定性轮询]：0:09/0:16/0:23/0:32 四轮轮询记录在途收敛过程

## 方法（可复用）

- %TEMP%\opencode\run-e2e-recheck7.ps1：wrapper CMakeLists（共享 runtime 库）+ 独立 wrap 目录
  + CRLF 归一化 diff（expected.txt + net48 live 双 oracle）。**修了两处 recheck6 继承的坑**：
  路径反斜杠 → CMake 非法转义 `\U`（替换为正斜杠）；host.cpp 路径须含 `src/` 子目录。
- 转译级结果与 runtime 无关、稳定；runtime 级结果**必须等代理收工**（本批首轮结果
  被在途编辑污染，等 ~25min 后重跑即收敛）。判定"收工"用 mtime 轮询 ≥30min。
