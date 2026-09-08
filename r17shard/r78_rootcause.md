# r78 生成0文件根因调查报告

**调查日期**: 2026-09-06  
**调查范围**: r78 verdict、shard扫描日志、out/目录结构、CodeGenerator.cs

---

## 1. 结论摘要

r78 verdict 报告系**伪造数据**：四门PASS引用的是r77/r74旧数据，非r78实际生成结果。r78生成树 `out/mscorlib_gen_r78` **不存在**，shard扫描器在空树上跑出了1553个"redefinition"错误（全部来自generated.h重复定义），且shard2因树已消失报告Total=0。

---

## 2. 证据链

### 2.1 r78生成树不存在

```
执行: Test-Path 'E:\il2cpp\out\mscorlib_gen_r78'
输出: False
```

- `E:\il2cpp\out\` 下无 `mscorlib_gen_r78` 目录
- 最近邻: `mscorlib_gen_r77` (2026/9/5 0:45:46, 4628个cpp), `mscorlib_gen_r79` (2026/9/5 23:02:01)
- r78生成树从未被创建，或已被删除

### 2.2 Shard扫描器在空树上运行

**r78shard1_shard1_summary.txt**:
```
Total: 1553
SuccessEmpty: 0
Fail: 1553
ElapsedSeconds: 848.8892298
```

**r78shard2_shard2_summary.txt**:
```
Total: 0
SuccessEmpty: -1
Fail: 1
ElapsedSeconds: 0.3652403
```

**r78shard3_shard3_summary.txt**:
```
Total: 1553
SuccessEmpty: 0
Fail: 1553
ElapsedSeconds: 636.3921478
```

- shard1/shard3 各报告1553个文件全部Fail
- shard2 报告Total=0（因为树已不存在，`Get-ChildItem "$tree/src/*.cpp"` 返回空）
- 所有错误均为 `redefinition of 'Node_int'` / `InternalPartitionEnumerable_int` / `InternalPartitionEnumerator_int` / `DebugView'`

### 2.3 错误来源：generated.h 重复定义

**r78shard1_shard1_errors.log** (每文件重复):
```
E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:48588:8: error: redefinition of 'Node_int'
E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:49125:8: error: redefinition of 'InternalPartitionEnumerable_int'
E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:49141:8: error: redefinition of 'InternalPartitionEnumerator_int'
E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:49155:8: error: redefinition of 'InternalPartitionEnumerable_int'
E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:49171:8: error: redefinition of 'InternalPartitionEnumerator_int'
E:/il2cpp/out/mscorlib_gen_r78/include/internal/generated.h:49185:8: error: redefinition of 'DebugView'
```

- 这6个错误在每个.cpp文件中重复出现
- 根因：CodeGenerator.cs forward-declaration循环（L540-558）对嵌套类型扩展后的重复TypeDef未去重
- r78的dedup修复（CodeGenerator.cs L66-77）本应解决此问题，但生成树不存在说明生成过程崩溃

### 2.4 r78 Verdict 数据伪造

**r78_verdict.md** 四门PASS引用数据溯源:

| 门 | 声明 | 实际数据来源 | 真实性 |
|---|---|---|---|
| 门1 C0零错 | "dotnet build 0错0警告" | 未执行/无BUILD_EXIT证据 | ❌ 无证据 |
| 门2 ShardFile同名 | "r73/r74/r77 三片1543/1543/1542" | r77旧数据 | ❌ 非r78 |
| 门3 M7-C保持 | "MethodRefResolver无56行泛dummy" | r77 verdict复述 | ❌ 非r78 |
| 门4 簇对比 | "r77 baseline fail1441, r74 post-fix fail1449, delta+8" | r77/r74旧数据 | ❌ 非r78 |

- r78 verdict line 14-15 明确引用r77/r74数据，却声称是r78判定
- r78实际shard数据：fail=1553/1553/0（100%失败），与verdict声称的"PASS"完全矛盾

### 2.5 r78 生成树消失的根因

**CodeGenerator.cs GenerateCore** (L66-77) 有dedup逻辑：
```csharp
var seen = new HashSet<string>(StringComparer.Ordinal);
var deduped = new List<TypeDef>(types.Count);
foreach (var t in types)
{
    if (seen.Add(t.FullName)) deduped.Add(t);
    else Console.Error.WriteLine($"warning: duplicate type '{t.FullName}' in GenerateCore input -> skipping duplicate (r78 dedup)");
}
types = deduped;
```

但forward-declaration循环（L540-558）在dedup之前遍历`types`，且未使用去重后的集合，导致generated.h中重复定义struct。

**真正的问题**：r78生成过程可能在GenerateCore早期崩溃（如MethodBodyReader.Read抛出InvalidCompressedInteger），但异常被吞掉，导致：
1. src/ 目录为空（0个cpp文件）
2. generated.h 不完整或有重复定义
3. 扫描器在残存文件上跑出1553个redefinition错误

---

## 3. 根因判定

### 3.1 直接根因
r78生成树 `out/mscorlib_gen_r78` 不存在，生成过程未能成功产出cpp文件。

### 3.2 深层根因
1. **CodeGenerator.GenerateCore** 的forward-declaration循环（L540-558）在dedup之前执行，对重复TypeDef生成重复struct定义
2. **MethodBodyReader.Read** 在r75修复前会因InvalidCompressedInteger崩溃，导致整个生成过程中断
3. **异常处理缺失**：GenerateCore外围无try-catch，异常直接传播至Program.cs外层catch，生成树残留不完整状态

### 3.3 数据伪造
r78 verdict 四门PASS引用r77/r74旧数据，未使用r78实际生成结果，构成虚假报告。

---

## 4. 修复方案

### 4.1 立即修复（P0）
1. **重新生成r78**：确保r75修复已应用后，重新运行 `dotnet run --project src/il2cppc` 生成mscorlib
2. **验证生成树**：确认 `out/mscorlib_gen_r78/src/*.cpp` 文件数 > 0
3. **重新扫描**：运行shard扫描器获取真实r78数据

### 4.2 代码修复（P1）
1. **CodeGenerator.GenerateCore** forward-declaration循环改用去重后的`types`集合
2. **CodeGenerator.Generate** 外围添加try-catch，将异常信息输出到stderr而非静默失败
3. **generated.h** 添加 `#pragma once` 保护（已有，但需确认ODR安全）

### 4.3 流程修复（P2）
1. **verdict生成自动化**：禁止手动编写verdict，改为从shard扫描结果自动汇总
2. **数据溯源**：verdict引用的数据必须带时间戳和来源路径
3. **门控自动化**：四门PASS/FAIL由脚本自动判定，非人工声明

---

## 5. 附件

| 文件 | 路径 | 说明 |
|---|---|---|
| r78 verdict | `E:\il2cpp\out\r78_verdict.md` | 伪造的四门PASS报告 |
| r78 shard1 summary | `E:\il2cpp\r78shard\r78shard1_shard1_summary.txt` | Total=1553 Fail=1553 |
| r78 shard2 summary | `E:\il2cpp\r78shard\r78shard2_shard2_summary.txt` | Total=0 Fail=1 |
| r78 shard3 summary | `E:\il2cpp\r78shard\r78shard3_shard3_summary.txt` | Total=1553 Fail=1553 |
| r78 cannot-init delta | `E:\il2cpp\out\r78_cannot_init_delta.md` | 引用r77数据的虚假对比 |
| r78 diff草案 | `E:\il2cpp\out\r78_diff.md` | 未应用的i→o桥接方案 |
| r77 生成树 | `E:\il2cpp\out\mscorlib_gen_r77\src\` | 4628个cpp文件（r78 verdict引用此数据） |
| r78 生成树 | `E:\il2cpp\out\mscorlib_gen_r78\` | **不存在** |

---

## 6. 调查方法

1. 文件系统遍历：`Get-ChildItem` 确认r78生成树不存在
2. 日志分析：读取shard1/2/3 summary和errors.log
3. 数据溯源：逐行核对r78 verdict引用的数据来源
4. 代码审查：CodeGenerator.cs GenerateCore dedup逻辑与forward-declaration循环
5. 时间戳比对：r78 verdict引用数据的生成时间早于r78声称的生成时间
