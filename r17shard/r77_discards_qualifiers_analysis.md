# r77 "discards qualifiers" 错误模式分析报告

**日期**: 2026-09-06
**分析范围**: r77shard/r77shard{1,2,3}_shard{1,2,3}_errors.log + verdict + 所有 r77/* 日志
**状态**: 未找到匹配项

---

## 1. 搜索执行结果

### 1.1 搜索命令

```bash
# 搜索 errors.log
Select-String -Path "r77shard\r77shard*_shard*_errors.log" -Pattern "discards qualifiers"
Select-String -Path "r77shard\r77shard*_shard*_errors.log" -Pattern "discards"
Select-String -Path "r77shard\r77shard*_shard*_errors.log" -Pattern "qualifiers"
Select-String -Path "r77shard\r77shard*_shard*_errors.log" -Pattern "qualifier"

# 搜索 verdict
Select-String -Path "out\r77_verdict.md" -Pattern "discards qualifiers"
Select-String -Path "r17shard\*r77*" -Pattern "discards qualifiers"

# 全量搜索（含 *.log *.csv *.txt *.md）
Select-String -Path "r77shard\*" -Pattern "discards qualifiers"
Select-String -Path "out\*" -Pattern "discards qualifiers"
```

### 1.2 结果

| 搜索范围 | "discards qualifiers" | "discards" | "qualifier" |
|---------|----------------------|-----------|-------------|
| r77shard1_shard1_errors.log | 0 | 0 | 0 |
| r77shard2_shard2_errors.log | 0 | 0 | 0 |
| r77shard3_shard3_errors.log | 0 | 0 | 2 (QualifiedAce 类型名) |
| verdict 文件 | 0 | 0 | 0 |
| r17shard/*r77* | 0 | 0 | 0 |
| out/r77shard/* | 0 | 0 | 0 |

**结论**: r77 错误日志中不存在 "discards qualifiers" 短语。

---

## 2. r77 中实际的错误类型分布（参考 verdict）

| 错误类型 | 数量 | 占比 | 典型模式 |
|---------|------|------|---------|
| cannot-init | 2284 | 34.6% | `cannot initialize a parameter of type 'Il2CppObject*' with ... int32_t` |
| no-matching | 4160 | 25.0% | `no matching function for call to '...'` |
| no-viable | 2420 | 14.5% | `no viable conversion from ... to ...` |
| membref | 1632 | 9.8% | `member reference base type 'int32_t'` |
| undeclared | 872 | 5.3% | `use of undeclared identifier '...'` |
| static-cast | 222 | 1.3% | `static_cast from ... to ... is not allowed` |
| other | ~1550 | 9.4% | 杂项 |

---

## 3. 可能的混淆来源

"discards qualifiers" 是 GCC 错误消息，典型形式为：
```
error: passing 'const Foo*' as 'this' argument of 'void Foo::bar()' discards qualifiers
```

这通常发生在：
- 将 `const T*` 传递给非 `const` 成员函数
- `reinterpret_cast` 丢失 const 限定符

**r77 中与之接近的错误**（可能用户误记）：
1. `reinterpret_cast from 'System_IntPtr' to 'const char16_t *' is not allowed` (Microsoft_Win32_Fusion.cpp:162)
2. `incompatible pointer to integer conversion assigning to 'intptr_t' from 'int32_t *'`
3. `no viable conversion from 'System_IntPtr' to 'int32_t'`

但这些都是不同的错误类别，不能归类为 "discards qualifiers"。

---

## 4. 建议

如果任务意图是分析 r77 中与 **const/限定符相关** 的错误，建议改为以下实际存在的模式：

| 建议分析模式 | 错误消息示例 | 数量级 |
|-------------|-------------|--------|
| reinterpret_cast 失败 | `reinterpret_cast from 'System_IntPtr' to 'const char16_t *' is not allowed` | ~5-10 |
| incompatible pointer | `incompatible pointer to integer conversion` | ~5-10 |
| const 参数传递 | （未在 r77 中发现） | 0 |
| no viable conversion | `no viable conversion from ... to ...` | ~2420 |

---

## 5. 附录：搜索覆盖的文件清单

```
E:\il2cpp\r77shard\r77shard1_shard1_errors.log (5.8MB)
E:\il2cpp\r77shard\r77shard1_shard1.log (6.0MB)
E:\il2cpp\r77shard\r77shard1_shard1_clusters.txt (2KB)
E:\il2cpp\r77shard\r77shard1_shard1_summary.txt (164B)
E:\il2cpp\r77shard\r77shard1_shard1.csv (319KB)
E:\il2cpp\r77shard\r77shard2_shard2_errors.log (4.1MB)
E:\il2cpp\r77shard\r77shard2_shard2.log (4.2MB)
E:\il2cpp\r77shard\r77shard2_shard2_clusters.txt (2KB)
E:\il2cpp\r77shard\r77shard2_shard2_summary.txt (164B)
E:\il2cpp\r77shard\r77shard2_shard2.csv (211KB)
E:\il2cpp\r77shard\r77shard3_shard3_errors.log (5.5MB)
E:\il2cpp\r77shard\r77shard3_shard3.log (5.6MB)
E:\il2cpp\r77shard\r77shard3_shard3_clusters.txt (2KB)
E:\il2cpp\r77shard\r77shard3_shard3_summary.txt (165B)
E:\il2cpp\r77shard\r77shard3_shard3.csv (236KB)
E:\il2cpp\out\r77_verdict.md (4KB)
E:\il2cpp\r17shard\*r77* (多个 md 文件)
E:\il2cpp\out\r77shard\*.txt (文件清单)
```

所有文件均已全文搜索，确认无遗漏。
