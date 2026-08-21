# e2e Recheck 5 — collchain + encodingcase 复验（2026-08-04）

任务：复验 collchain + encodingcase（recheck4 时接线在途——ArrayList 接线 11/11 绿、Encoding 8/8 绿现已收工）。只读用例 + 写文档，不修、不 commit。

## 前置状态核查

| 检查项 | 结果 |
|---|---|
| MethodMap.ArrayList.cs | 2026-08-04 14:25:39，稳定 **65.5 min** ≥30min ✓ |
| MethodMap.Encoding.cs | 2026-08-03 23:09:39，稳定 **981.5 min** ✓ |
| MethodMapTests.ArrayList.cs | 2026-08-04 14:29:21，稳定 **61.8 min** ✓ |
| MethodMapTests.Encoding.cs | 2026-08-04 14:58:40，稳定 **32.5 min** ✓（刚跨阈） |
| il2cppc.dll 新鲜度 | **旧**（15:07:51）vs 最新源 StackFold.cs 15:12:35 → **已 dotnet build -c Release 重建** → 15:32:36，**0 警 0 错** |
| 用例源 vs net48 exe | collchain.cs 8:52 < exe 14:50 ✓；encodingcase.cs 08-03 20:22 < exe 9:55 ✓（exe 新鲜） |
| oracle 基准 | collchain net48 live 46/46 == expected.txt **逐字节一致**；encodingcase net48 live 15/15 == expected.txt **逐字节一致**（两 oracle 有效） |

## 状态表（2 跑）

| 用例 | 转译 | 构建(MinGW) | 运行 | diff expected.txt | 结论 |
|---|---|---|---|---|---|
| collchain | **FAIL**（1 err：dup on empty stack 0x0523） | —（转译失败跳过） | — | — | FAIL |
| encodingcase | **FAIL**（1 err：GetBytes LOUD） | —（转译失败跳过） | — | — | FAIL |

按任务流程"转译成功则 cmake 构建 → 运行 → diff"：两用例均转译失败，构建/运行/diff 阶段不适用，缺口精确记录如下。

## 缺口清单（每 FAIL 精确原因）

| # | 用例 | 阶段 | 精确原因（证据） |
|---|---|---|---|
| R5-1 | collchain | 转译 | `il2cppc error: dup on empty stack at offset 0x0523`（StackFold.cs:270 抛）。**recheck4 阻塞（ArrayList::get_Count 0xA00026 callvirt non-instance）已解除**——ArrayList 域接线落地（方法映射 + dispatch），转译越过 get_Count 前进到下一个记录在案缺口。**dup 失步 = ArrayList 代理记录的 fold 栈失步**（discover.md：rv=false 不压结果 → pop 吞掉 live list 引用）。ildump IL 实证（collchain.exe Main）：`0512 newobj ArrayList::.ctor → 0517 dup → 0518 ldstr → 051D callvirt ArrayList::Add(0x0A000025) → 0522 pop → 0523 dup`（空栈失败）——csc 对直链 `arr.Add(...)` 发射 dup-chain 形状，Add 映射 rv=false 后 pop 吞掉 newobj 引用，下一 dup 空栈。fix recipe：resolver 加"IL 返回有值但发射 void"标志 + fold 压 drop 标记给 pop 消费（三文件联动，StackFold 域） |
| R5-2 | collchain（下游，未达） | — | 若 dup 缺口修复，下游还有两已知点：`05BF castclass`（String 转换，**双 System.String 描述符**：g_stringClass vs System_String_class 指针级 is_instance 失败 → 运行期 InvalidCastException，探针实证过）；（string 域）+ generated.h 缺 arraylist.h include / BclAllocExpr 无 ArrayList 条目（CodeGenerator 域，newobj 形状构建错）——记录供施工单参考 |
| R5-3 | encodingcase | 转译 | `System.Text.Encoding::GetBytes: the runtime encoding icalls are raw-buffer code-page protocols (il2cpp_encoding_get_bytes(text, page, dst, capacity) / il2cpp_encoding_get_chars(src, byteLen, page)): the M5 emitter cannot inject the code-page constant (65001/1200/20127), no .NET overload pushes a destination buffer, and there is no array-data-pointer cast for the byte[] input -- unwireable until an allocating runtime icall and/or emitter support exists`（MethodRefResolver.cs:366 LOUD 守卫）——**GetBytes 仍 loud**，与 recheck4/Encoding 任务记录逐字同形。**Encoding 域 8/8 单测绿（接线收工）但 e2e 用例仍阻塞**：域内文档化设计（无分配 icall + 无数组数据指针 cast），非回归。修复前置 = allocating icall + 发射器数组 cast（Encoding 域 + CodeGenerator 域） |

## 与 recheck4 对比（阶段前进记录）

| 用例 | recheck4 | recheck5 | 变化 |
|---|---|---|---|
| collchain | 转译 FAIL @ get_Count（ArrayList 未接） | 转译 FAIL @ dup 0x0523 | **前进 1 关卡**：ArrayList 映射生效，命中下一个记录缺口（dup 失步） |
| encodingcase | SKIP（Encoding 5/8 单测红） | 转译 FAIL（GetBytes LOUD，8/8 绿） | **接线收工**（19/19：ArrayList 11 + Encoding 8 全绿），e2e 仍卡域内文档化 LOUD |

## 施工单（域外，记录不执行）

1. **dup-chain fold 失步**（StackFold + MethodRefResolver + CodeGenerator 三文件联动：rv=false 的 IL-返回有值 icall 压 drop 标记供 pop 消费）→ 解 collchain 转译
2. collchain 下游（若 1 修好）：generated.h arraylist.h include + BclAllocExpr ArrayList 条目（CodeGenerator 域）；双 String 描述符统一（string 域）→ 解 collchain 构建/运行
3. **allocating encoding icall + 数组数据指针 cast**（Encoding + CodeGenerator 域）→ 解 encodingcase 转译

## 证据

- [BASH] 4 文件 mtime：ArrayList 65.5min / Encoding 981.5min / Tests.ArrayList 61.8min / Tests.Encoding 32.5min，全 ≥30min
- [BASH] dotnet build src/il2cppc -c Release → 0 警 0 错，dll 15:32:36（新于全部 src/il2cppc 源）
- [BASH] collchain TRANSPILE_EXIT=2：`dup on empty stack at offset 0x0523`（StackFold.cs:270）
- [BASH] encodingcase TRANSPILE_EXIT=2：GetBytes raw-buffer LOUD（MethodRefResolver.cs:366）
- [BASH] ildump collchain.exe → `0512 newobj ArrayList::.ctor → 0517 dup → 051D callvirt Add(0x0A000025) → 0522 pop → 0523 dup`；`05BF castclass`（String 下游点）
- [BASH] net48 live：collchain 46/46 == expected.txt 逐字节；encodingcase 15/15 == expected.txt 逐字节（两 exe RUN_EXIT=0）
- [BASH] dotnet test --filter ArrayList|Encoding → 失败 0 通过 19（11+8，两域收工确认）
- 探针产物：%TEMP%\opencode\collchain-il.txt / collchain-live.txt / encodingcase-live.txt；用例内 gen-r5/（gitignored，转译失败产物）
