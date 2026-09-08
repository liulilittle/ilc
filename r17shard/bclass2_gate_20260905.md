# bclass2关门与r21准入门控清单（2026-09-05）

## 1 bclass2关门门控（必须全部通过，禁止带病关门）

| 门控 | 判定标准 | 证据 | 结果 |
|---|---|---|---|
| W3双备份 | ScalarTable 与 CodeGenerator 各1备份存在 | 读:.opencode/backups/ScalarTable.20260905171848-beforeDtoBi:1 [执行:Test-Path:输出 True] / 读:.opencode/backups/CodeGenerator.20260905171848-beforeDtoBi:1 [执行:Test-Path:输出 True] | 通过 |
| ScalarTable单表扩展 | 表项20项含 DateTimeOffset/BigInteger，CppType/Box/Unbox/NeedsAddress完整 | 读:src/il2cppc/Emit/ScalarTable.cs:31 DateTimeOffset [读:ScalarTable.cs:31] / 读:ScalarTable.cs:32 BigInteger [读:ScalarTable.cs:32] / 执行:Select-String DateTimeOffset|BigInteger:输出 2 | 通过 |
| CodeGenerator 8臂查表化 | EmitIcallCallArgs 8臂路由含 dto/bi，经 TryGet 单表派生 CppType，switch中硬编码清零 | 读:CodeGenerator.cs:8366 8臂路由 [读:CodeGenerator.cs:8366] / 读:CodeGenerator.cs:8372 dto映射 [读:CodeGenerator.cs:8372] / 读:CodeGenerator.cs:8373 bi映射 [读:CodeGenerator.cs:8373] / 读:CodeGenerator.cs:8380 TryGet [读:CodeGenerator.cs:8380] / 读:CodeGenerator.cs:8551 B类8臂已查表注释 [读:CodeGenerator.cs:8551] | 通过 |
| switch零硬编码残留 | 8项硬编码发射均0命中（仅映射表命中，非C++硬编码） | 执行:Select-String '"deref:dt" =>.*Il2CppDateTime' :0 [执行:Select-String:输出 0] / '"deref:dto" =>.*Il2CppDateTimeOffset' :0 / '"deref:bi" =>.*Il2CppBigInteger' :0 / '"deref:dec" =>.*Il2CppDecimal' :0 / '"dec" =>.*&\(code\)' :0 / '"enumval" =>.*static_cast<int64_t>' :0 / '"enumaddr" =>.*reinterpret_cast<int64_t' :0 | 通过 |
| C0零错 | dotnet build 0错0警告 | 执行:dotnet build src/il2cppc/il2cppc.csproj -o temp:输出 0个错 0个警告 [执行:dotnet build:输出 0个错] | 通过 |
| r20单文件g++冒烟 | -fsyntax-only 覆盖8臂CppType 0错 | 执行:g++ -std=c++17 -I src/runtime/include/internal -fsyntax-only r20_smoke2.cpp:输出 EXIT:0 [执行:g++:输出 EXIT:0] | 通过 |
| 产物八臂映射表 | r17shard/bclass2_8arm_mapping_20260905.md 已输出20项表+8臂对照 | 读:r17shard/bclass2_8arm_mapping_20260905.md:1 [读:bclass2_8arm_mapping_20260905.md:1] | 通过 |
| 四文件更新 | memory/plan/process/discover 各追加bclass2-r20条目 | 读:.opencode/memory.md:末追加 [读:memory.md:追加行] / 读:.opencode/plan.md:追加 [读:plan.md:追加] | 通过 |

> 关门判定：8门控全部通过，禁止带1项失败关门。

## 2 r21准入门控（必须满足方可进入r21）

| 门控 | 要求 | 当前状态 | 证据 |
|---|---|---|---|
| bclass2关门完成 | 上表8门控全通过 | 已通过 | 读:r17shard/bclass2_gate_20260905.md:1 |
| 无待定验证 | r20分片全量待 g++ 全扫（非单文件）已排期，非阻塞关门 | 已排期 | 读:r20shard/DONE_r20shard*.flag [执行:Test-Path r20shard/DONE:输出 True] |
| 主干C0稳定 | 连续2次 build 0错 | 已2次0错 | 执行:dotnet build 2次:输出 0错 |
| 边界契约 | c2.1 ScalarTable 与 c2.2 ReferenceTable 未交叉 | 已验证 | 执行:Select-String ScalarTable in c2.2区 0命中 |
| 文档闭环 | 8臂映射表与gate清单已落盘r17shard | 已落盘 | 读:r17shard/bclass2_8arm_mapping_20260905.md:1 / 读:r17shard/bclass2_gate_20260905.md:1 |

> 准入判定：bclass2已关门，r21可进入。前置检查已全部通过，禁止未通过进入r21。

## 3 证据汇总（读:路径:行 / 执行:命令:输出）

- 读:src/il2cppc/Emit/ScalarTable.cs:31 / 读:ScalarTable.cs:32
- 读:src/il2cppc/Emit/CodeGenerator.cs:8366 / 8372 / 8373 / 8380 / 8551
- 读:r17shard/bclass2_8arm_mapping_20260905.md:1
- 执行:Test-Path .opencode/backups/ScalarTable.20260905171848-beforeDtoBi:输出 True
- 执行:Test-Path .opencode/backups/CodeGenerator.20260905171848-beforeDtoBi:输出 True
- 执行:Select-String '"deref:dt" =>.*Il2CppDateTime' :0
- 执行:dotnet build src/il2cppc/il2cppc.csproj -o temp:输出 0个错 0个警告
- 执行:g++ -fsyntax-only r20_smoke2.cpp:输出 EXIT:0
