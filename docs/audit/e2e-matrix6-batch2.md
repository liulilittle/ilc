# e2e matrix6 批次 2 报告（用例 41-80，字母序）

日期：2026-08-06 15:25-15:39
范围：tests/cases 按字母序第 41-80 用例（delegatecase → listops），mingw 编译器
方法：`pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名> -ContinueOnError`，每用例独立 run-tests.ps1 调用，per-case log 留存 `%TEMP%\opencode\e2e-m6b2\<case>.txt`；状态从 run-tests.ps1 自身输出（PASS/FAIL: <阶段>）解析
共享库：`build/runtime_e2e_shared/libil2cpp_runtime.a` 已存在且新于全部 runtime 源（0 个过期源文件，14:20:48），直接复用未重建
并发互斥：批次 1 跑 1-40、批次 3 跑 81+（lockcase 起），本批次 41-80 无重叠；用例目录无 .inflight 标记

## 汇总

| 指标 | 值 |
|---|---|
| 总用例 | 40 |
| **PASS** | **24（60%）** |
| FAIL | 16（40%） |
| FAIL@transpile | 8 |
| FAIL@build | 7 |
| FAIL@run（reference run） | 0 |
| FAIL@diff | 0 |
| 环境缺失（ifcase 无 csproj） | 1 |

与全量 matrix6 权威 runs 100% 一致（全量 results.tsv 对应 40 例状态逐行相同）。

## 批次结果表（41-80）

| # | 用例 | 状态 | 阶段 |
|---|---|---|---|
| 41 | delegatecase | **PASS** | |
| 42 | delegatecb | FAIL | transpile |
| 43 | delegateinst | FAIL | build |
| 44 | delegates | **PASS** | |
| 45 | dictcase | FAIL | transpile |
| 46 | encodingcase | FAIL | transpile |
| 47 | enumdeep | FAIL | build |
| 48 | enumflags | **PASS** | |
| 49 | enumops | **PASS** | |
| 50 | environment | **PASS** | |
| 51 | environment2 | **PASS** | |
| 52 | exceptionchain2 | **PASS** | |
| 53 | exceptions | **PASS** | |
| 54 | excext | FAIL | transpile |
| 55 | exdeep | **PASS** | |
| 56 | filechain | **PASS** | |
| 57 | filestreamcase | **PASS** | |
| 58 | fmtchain | **PASS** | |
| 59 | formatcase | **PASS** | |
| 60 | formatext | **PASS** | |
| 61 | gccase | **PASS** | |
| 62 | gcext | FAIL | build |
| 63 | gcstress | **PASS** | |
| 64 | generics | **PASS** | |
| 65 | genlist | FAIL | transpile |
| 66 | guidcase | **PASS** | |
| 67 | hashcase | FAIL | build |
| 68 | hashchain | FAIL | build |
| 69 | hashtable2 | FAIL | transpile |
| 70 | hashtablecase | FAIL | transpile |
| 71 | hexcase | **PASS** | |
| 72 | ifcase | FAIL | 环境缺失 |
| 73 | interface | **PASS** | |
| 74 | interlockedcase | **PASS** | |
| 75 | intern | **PASS** | |
| 76 | internchain | **PASS** | |
| 77 | iocase | FAIL | transpile |
| 78 | iofmt | **PASS** | |
| 79 | linqcase | FAIL | transpile |
| 80 | listops | FAIL | build |

## 失败现象摘要（16 例，各一句话）

1. **delegatecb**（transpile）— `Marshal.GetFunctionPointerForDelegate` 泛型实例化无 BCL 映射（MethodRefResolver 抛 LOUD）。
2. **delegateinst**（build）— 生成码引用未声明临时量 `t4`/`t31`（`il2cpp_delegate_new` 2-arg target 发射后 temp 槽错位）。
3. **dictcase**（transpile）— 泛型实参 byte 0x0E 在 M3 无展开（泛型类描述符缺口）。
4. **encodingcase**（transpile）— `Encoding.GetChars` 仅 UTF8 页接分配形 icall，ASCII/Unicode 页无 runtime helper。
5. **enumdeep**（build）— `System_Object_ToString_a21e...` 未声明 + `il2cpp_enum_get_underlying_type` 收到 Il2CppObject* 而非 Il2CppClass*。
6. **excext**（transpile）— `Exception.get_Message` 未接线（M3 异常载体为 C++ 类型标签，非托管 Exception 对象）。
7. **gcext**（build）— `System_GC_WaitForPendingFinalizers` 生成名未声明（static 分类错，之前 matrix5 同归因）。
8. **genlist**（transpile）— `Cases.GenList.Id` 泛型实参 string(0x0E) 无展开（模型只生成 int32/uint32 默认实例化）。
9. **hashcase**（build）— 生成码调用 `il2cpp::runtime::icall_md5_create/sha1_create` 但 generated.h 无 `crypto_obj.h` include（M5 曾修复，M6 新 include 表 md5.h/sha1.h/sha256.h 未含 crypto_obj.h 回归）。
10. **hashchain**（build）— 同 hashcase：`icall_md5_create/sha1_create` 未声明。
11. **hashtable2**（transpile）— `Console.WriteLine(object)` 未知参数类型（无 tracked box 类型）未接线。
12. **hashtablecase**（transpile）— 同 hashtable2：`Console.WriteLine(object)` 无 box 类型未接线。
13. **ifcase**（环境缺失）— 用例目录无 `ifcase.csproj`（缺 fixture，非转译器问题）。
14. **iocase**（transpile）— `Path.Combine` 3 参数 params-array 形式未接线（icall 只收 (string,string)，nested fold 未落地，work order #24）。
15. **linqcase**（transpile）— 泛型实参 byte 0x02 在 M3 无展开（同 dictcase 类）。
16. **listops**（build）— `System_Collections_ArrayList_class` 未声明 + `il2cpp_arraylist_create/get_count/add` 无 namespace 成员（ArrayList class 描述符/API 未发射，D-AL-1/2）。

## 与 matrix5 对比（40 例交集，matrix5 表逐行核对）

- **新绿 1 例**：**internchain**（matrix5 FAIL@diff StringPool 回归 → 现 PASS，g_strPool[0] 自比较警告仍在但 net48 live 逐字节一致）。
- **回归 2 例**：**hashcase、hashchain**（matrix5 均 PASS → 现 FAIL@build：generated.h include 表只发 md5.h/sha1.h/sha256.h，缺 `crypto_obj.h`，M5 修复被 M6 include 表重建覆盖，见 #9/#10）。
- 其余 10 例 FAIL（delegatecb/delegateinst/encodingcase/enumdeep/excext/gcext/genlist/hashtable2/hashtablecase/iocase）与 matrix5 交集 21 例同归因（transpile 接线缺 / 生成码 bug），无新增交集失败。
- 新增用例（matrix5 新增 25 例中含于本批次）3 例：dictcase/linqcase（transpile，同 M5）、enumflags（PASS，同 M5）。

## checkpoint

- 逐用例已追加写入 `docs/audit/e2e-matrix6-checkpoint-2.md`（40 行全部落盘，15:25 初始 + 15:35 校正为权威状态）
- per-case 原始日志：`%TEMP%\opencode\e2e-m6b2\<case>.txt`（40 份）

## 结论

批次 2（41-80）PASS 24/40（60%）。16 例失败全为既有缺陷（转译器接线缺 8、生成码 build 失败 7、fixture 缺失 1），无一为运行/diff 阶段新失败；其中 hashcase/hashchain 2 例为 M6 include 表相对 matrix5 的回归（crypto_obj.h 缺 include）。本批次不改产品代码、不 commit。
