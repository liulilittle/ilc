# sweep-exclude 严格化（P2-1 / R3）

> 缺陷来源：`.opencode/tmp/w11next/auditA-gap-truth.md:341-346`（审计 A 的 P2-1）。

## 1. 两个缺陷

### 1.1 排除清单不校验存在性

`scripts/sweep-exclude.ps1` 只做解析，不检查 `tests/cases/<name>/` 是否存在。
后果：条目名写错、用例改名、用例删除后，该条目变成**死指令**——它不排除任何东西，
却让清单看起来仍然权威。更糟的是它**看起来像已经处理过**，下一个 reviewer 不会去核。

### 1.2 全 SKIP 的运行被当作成功

`scripts/run-tests.ps1:530`（修复前）：
```powershell
exit $(if ($failures -gt 0) { 1 } else { 0 })
```
`$failures` 为 0 时退出 0，但 `$passed` 从未参与判断。若整个池子都在排除清单里
（或 `-Cases` 挑中的全是排除项），运行**没有执行任何一次比对**却报成功。
这与「空测试套件通过」是同一个缺陷。

## 2. 修复

### 2.1 新增 `Assert-SweepExcludeEntriesExist`

`scripts/sweep-exclude.ps1` 新增导出函数：遍历清单条目，逐个 `Test-Path -PathType Container`
于 `tests/cases/<name>`；缺失则 `throw` 并列出全部缺失名。

抛异常而不是打警告是刻意的：清单是门禁输入，门禁静默接受垃圾就不是门禁。

### 2.2 `run-tests.ps1` 接入 + 全 SKIP 断言

- 加载 `sweep-exclude.ps1` 后立即调用 `Assert-SweepExcludeEntriesExist`
  （在 `scripts/run-tests.ps1:214` 的既有加载点之后），扫描开始前就失败。
- 退出前新增：
```powershell
if ($total -gt 0 -and $passed -eq 0 -and $failures -eq 0) {
    Write-Host "ERROR: every case was skipped ($skipped/$total) -- nothing was actually verified"
    exit 1
}
exit $(if ($failures -gt 0) { 1 } else { 0 })
```

### 2.3 CI 接入

`scripts/ci.ps1:129-132` 新增「3.9 sweep-exclude strictness」，调用
`scripts/probe-sweep-exclude-strict.ps1`（不编译，开销约 0.5s）。

## 3. 验收（TDD 双半门 + 变异）

探针：`scripts/probe-sweep-exclude-strict.ps1`

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/probe-sweep-exclude-strict.ps1
```

| 阶段 | 结果 |
|---|---|
| 红证（实现未改，只加探针） | `A FAIL: Assert-SweepExcludeEntriesExist is not defined`<br>`B FAIL: run-tests.ps1 exits 0 when every case was skipped`<br>`SWEEP-EXCLUDE-STRICT: FAIL (2)`，`RED_EXIT=1` |
| 绿证（补实现后） | `A OK: rejected the non-existent entry`<br>`A OK: all-real list accepted`<br>`B OK: run-tests.ps1 consults the passed count`<br>`SWEEP-EXCLUDE-STRICT: PASS`，`GREEN_EXIT=0` |
| 变异红 | 两处判定同时改坏（存在性检查 → `$null = $dir`；全 SKIP 守卫 → 删除）⇒ `FAIL (2)`，`MUT_EXIT=1` |
| 恢复绿 | `restore` + 无 `MUTANT` 残留 ⇒ `PASS`，`GREEN_EXIT=0` |
| 语法 | `Parser::ParseFile` 三个脚本 `parse_errors=0` |
| 真实清单 | `Assert-SweepExcludeEntriesExist` 于 `scripts/sweep-exclude.txt` ⇒ `REAL-LIST OK`（8 条全部存在） |

端到端（不经探针，直接跑真实入口）：

```
run-tests.ps1 -Compiler mingw -Cases p-stacktrace   (唯一一条排除项)
  -> SKIP: p-stacktrace (...)
  -> PASS: 0  FAIL: 0  SKIP: 1  TOTAL: 1
  -> ERROR: every case was skipped (1/1) -- nothing was actually verified
  -> E2E_EXIT=1        (修复前为 0)

run-tests.ps1 -Compiler mingw -Cases comslot        (正常用例)
  -> PASS: comslot
  -> PASS: 1  FAIL: 0  SKIP: 0  TOTAL: 1
  -> NORMAL_EXIT=0     (无回归)
```

## 4. 已知局限

1. 「全 SKIP」判据是 `$passed -eq 0 -and $failures -eq 0`。若一次运行里
   **全部用例都 FAIL**，该条件不成立（`$failures > 0`），走原来的 `exit 1` 分支，行为正确。
   若 `$total -eq 0`（`-Cases` 挑空），不加判据 —— 那是调用方错误，不在本修复范围（**未验证**）。
2. `Assert-SweepExcludeEntriesExist` 只校验目录存在，不校验目录里有 `.csproj`。
   一个空目录仍能通过。这是刻意的：本修复只治理「名字指不到任何东西」。
3. 变异测试同时改坏两处，未逐条独立验证各自的红证（**未验证**每条单独是否足够）。

## 5. 复现

```powershell
# 探针（同时验 A 与 B，约 0.5s，不编译）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/probe-sweep-exclude-strict.ps1

# 全 SKIP 端到端
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-tests.ps1 -Compiler mingw -Cases p-stacktrace
# 期望 E2E_EXIT=1 且输出 "every case was skipped"
```
