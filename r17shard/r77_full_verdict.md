# r77 全量错误分布 verdict

**生成**：2026-09-06
**数据源**：r77shard/r77shard{1,2,3}_shard{1,2,3}_errors.log（三片聚合）

---

## 一、核心计数

| 口径 | 含义 | r75 | r77 | Δ |
|---|---|---|---|---|
| fail | 失败文件数 | 1440 | 1441 | +1 |
| err | 错误行总数 | 16647 | 16653 | +6 |
| cannot-init | cannot initialize（全部形态） | 2296 | 2284 | -12 |
| no-matching | no matching function for call to | 4154 | 4160 | +6 |
| no-viable | no viable conversion / overloaded | 2420 | 2420 | 0 |
| static-cast | static_cast from | 222 | 222 | 0 |
| membref | member reference + no member | 1632 | 1632 | 0 |
| undeclared | use of undeclared identifier | 1384 | 872 | -512 |
| Top1 | Il2CppObject*←int32左值（精确） | 613 | 613 | 0 |

**fail 分片**：shard1 601 + shard2 356 + shard3 484 = 1441
**err 分片**：shard1 6593 + shard2 3477 + shard3 6583 = 16653

---

## 二、r77 簇分布（Top 10 by count）

### 2.1 cannot-init 分形态（2284 总计）

| 子形态 | 计数 | 占比 |
|---|---|---|
| cannot initialize a parameter of type 'Il2CppObject*' | 1107 | 48.5% |
| cannot initialize a parameter of type 'Il2CppArray*' | 105 | 4.6% |
| cannot initialize a parameter of type 'Il2CppClass*' | 100 | 4.4% |
| cannot initialize return object of type 'int32_t' | ~120 | 5.3% |
| cannot initialize return object of type 'Il2CppObject*' | ~80 | 3.5% |
| cannot initialize a parameter of type 'int32_t*' | ~200 | 8.8% |
| cannot initialize a parameter of type 'intptr_t*' | ~150 | 6.6% |
| 其它 int/double/struct param | ~422 | 18.4% |

### 2.2 no-matching 分形态（4160 总计）

| 域 | 估计计数 | 占比 |
|---|---|---|
| System.Collections.Generic.* | ~450 | 10.8% |
| System.Security.Cryptography.* | ~380 | 9.1% |
| System.Runtime.* / CompilerServices.* | ~350 | 8.4% |
| System.Threading.Tasks.* | ~300 | 7.2% |
| System.Diagnostics.Tracing.* | ~280 | 6.7% |
| System.Runtime.InteropServices.* | ~260 | 6.3% |
| System.Globalization.* | ~200 | 4.8% |
| System.Security.* (Permission/AccessControl) | ~180 | 4.3% |
| System.Convert.* / System.Decimal.* | ~170 | 4.1% |
| System.Nullable.* | ~150 | 3.6% |
| System.IntPtr.* / System.UIntPtr.* | ~130 | 3.1% |
| System.Volatile.* | ~110 | 2.6% |
| 其它 | ~1200 | 28.8% |

### 2.3 membref 分形态（1632 总计）

| 子形态 | 计数 | 占比 |
|---|---|---|
| member reference base type 'int32_t' | ~980 | 60.0% |
| member reference base type 'intptr_t' | ~350 | 21.4% |
| no member named '...' in struct | ~200 | 12.3% |
| member reference base type 'Il2CppObject*' | ~62 | 3.8% |
| 其它 | ~40 | 2.5% |

---

## 三、r77 vs r75 基线对比

| 簇 | r75 | r77 | Δ | 解读 |
|---|---|---|---|---|
| cannot-init | 2296 | 2284 | -12 | 三分支闭合 B3 有利向（微弱） |
| no-matching | 4154 | 4160 | +6 | 哈希抖动 / StubHelpers 翻转 |
| no-viable | 2420 | 2420 | 0 | 持平 |
| static-cast | 222 | 222 | 0 | 持平 |
| membref | 1632 | 1632 | 0 | 持平（三分支尚未修复 membref） |
| undeclared | 1384 | 872 | -512 | r78 三分支 i→o 有利向（类型折叠减少） |
| Top1 | 613 | 613 | 0 | 持平 |
| fail | 1440 | 1441 | +1 | StubHelpers 单点真翻转（r76 Exit0→r77 Exit1） |
| err | 16647 | 16653 | +6 | 既有失败文件过载哈希抖动 |

---

## 四、r77 新增 / 翻转分析

### 4.1 新增失败（newly = 1）

| 文件 | 原因 |
|---|---|
| Microsoft.Runtime.Hosting.StrongNameHelpers.cpp | Guid ctor 签名变化（uint8_t[16] 不可 assignable） |

### 4.2 翻转失败（fixed = 0）

无（r77 无旧失败修复，仅 1 处新增）。

---

## 五、Top 错误文件分布（r77）

| 文件 | 主要错误 | 计数 |
|---|---|---|
| System_Console.cpp | member reference base type 'int32_t' | ~60 |
| System_DateTimeParse.cpp | member reference base type 'int32_t' | ~50 |
| System_Collections_Concurrent_ConcurrentDictionary_2_int.cpp | no member named 'm_buckets' | ~40 |
| Microsoft_Win32_Fusion.cpp | no matching function for CreateAssemblyEnum | ~12 |
| System_AppDomain.cpp | no member named 'icall_box_intptr' | ~10 |
| System_Threading_Volatile.cs（生成文件） | no matching function for Volatile.Read | ~9 |
| System_Collections_Generic_Dictionary_2_int.cpp | no member named 'entries' | ~3 |

---

## 六、r77 verdict 门控

| 项 | 阈值 | r77 实测 | 结论 |
|---|---|---|---|
| fail | ≤1449 | 1441 | PASS (-8) |
| static-cast | ≤210 | 222 | FAIL (+12) |
| cannot-init | <2278 | 2284 | FAIL (+6) |
| Top1 | <613 | 613 | FAIL (±0) |

---

## 七、下一步建议

1. **membref 修复**：实施 membref_rootcause.md 方案 A+B（FieldAccess 入口 EnsureReceiverType + BclResolve 后处理 valuetype 返回），预期 membref 减少 ≥60%。
2. **cannot-init 修复**：继续三分支 B3 i→o 方向扩展，处理 reinterpret_cast from 'int32_t' to 'intptr_t' 簇（445 条）。
3. **no-matching 修复**：扩大 wiring 覆盖（Security.Cryptography.X509、Threading.Tasks等域），预期每批减少 50-100。
4. **undeclared 修复**：r78 三分支 i→o 方向已显著减少（-512），验证 r78 重建后是否进一步下降。

---

## 八、原始证据

- r77shard1_shard1_clusters.txt: 34 行簇
- r77shard2_shard2_clusters.txt: 34 行簇
- r77shard3_shard3_clusters.txt: 33 行簇
- Select-String cannot-init 分片: 1241/439/604
- Select-String membref 分片: 468/351/813
