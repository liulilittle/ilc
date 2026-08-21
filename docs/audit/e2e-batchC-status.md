# e2e 剩余接线批 C — 状态表（2026-08-06 08:30-09:25，45 分钟任务）

## 状态表（5 例）

| # | 用例 | 矩阵5 | 本批动作 | 结果 | 证据 |
|---|------|-------|---------|------|------|
| ① | formatext | PASS | ∞ 已修重验剩余 | **PASS 72/72** 逐字节 | [BASH:recheck-batchC formatext → PASS 72/72]；用例 ∞ 已 R5 bool 断言（formatext.cs:115-123） |
| ② | gccase | PASS | GC 压缩器修复后重验 | **PASS 4/4** 逐字节 | [BASH:recheck-batchC gccase → PASS 4/4]；gc.cpp 8:50:37 kMinBlockSize clamp 已入库共享库（lib 8:52:12 > gc.cpp 8:50:37） |
| ③ | typecheckcase | FAIL(transpile) | 诊断 | **被占不修**（T 域施工单 #39） | [BASH:il2cppc.dll 直跑 → exit=2 `TypeSpec signature byte 0x1D not GenericInst(0x15)` @CodeGenerator.cs:1738]；转译器代理在飞（MethodRefResolver.cs 9:18:16 / StackFold.cs 8:55:53） |
| ④ | ehchain | — | 澄清 | **N/A 不存在** | [READ:e2e-stable-check.md:10-13] 全仓 0 命中，用户确认记错，替代 cctor 已 PASS |
| ⑤ | math2 | PASS | 重验 | **PASS 64/64** 逐字节 | [BASH:recheck-batchC math2 → PASS 64/64] |

## 结论

- **可达用例 3/3 全 PASS**（formatext 72/72、gccase 4/4、math2 64/64，net48 oracle 逐字节一致，共享库 8:52:12 + il2cppc.dll 9:09:27 快照）。
- gccase：GC 压缩器修复（gc.cpp AllocCore size<kMinBlockSize clamp，防 sweep FreeBlock 头写越界）
  已由 GC 代理收工落地并重建共享库，本批重验确认 GetTotalMemory>0（L1）与 GetGeneration>0（L4）真值语义。
- typecheckcase：阻点为 **T 域**（CodeGenerator.cs:1738 GenericTypeSpecDefinitionName 无 0x1D SZARRAY
  分支，EmitTask9Call 数组 isinst 路径），施工单 #39 同点；转译器代理在飞（MethodRefResolver.cs/
  StringPoolPass.cs 9:02-9:18 持续写）→ 被占施工单，本批不修，待转译器代理收工后由 T 域批次处理。
- ehchain：不存在（用户已澄清），无需动作。

## 遗留（移交）
- typecheckcase 转译阻点：CodeGenerator.cs:1738（施工单 #39）——T 域，代理在飞。
- 环境注记：%TEMP%\opencode\e2e-matrix5 矩阵产物被 CCleaner（8:26 启动）清理；重验脚本
  重新创建于 C:\Users\windo\AppData\Local\Temp\opencode\recheck-batchC.ps1（共享库 wrapper 法）。
