# r79 生成门状态报告

**日期**: 2026-09-06
**状态**: ❌ r79 生成失败，无 cpp 文件产出

---

## 1. 目录状态

| 目录 | 存在 | src/ 状态 | cpp 文件数 |
|------|------|-----------|------------|
| `out\mscorlib_gen_r79` | ✅ | src/ 不存在 | 0 |
| `out\mscorlib_gen_r79v2` | ✅ | src/ 存在但空 | 0 |
| `out\mscorlib_gen_r78` | ✅ | src/ 不存在 | 0 |
| `out\mscorlib_gen_r77` | ✅ | ✅ | **4628** |

---

## 2. 错误日志

**日志文件**：`E:\il2cpp\out\gen_r79_err.log` (39492 bytes)

### 2.1 致命错误

```
il2cppc error: Invalid compressed integer.
   at System.Reflection.Throw.InvalidCompressedInteger()
   at System.Reflection.Metadata.BlobReader.ReadCompressedInteger()
   at Il2Cpp.Transpiler.Il.MethodBodyReader.LocalTypeKind(...) in MethodBodyReader.cs:line 335
   at Il2Cpp.Transpiler.Il.MethodBodyReader.ReadLocalNames(...) in MethodBodyReader.cs:line 222
   at Il2cpp.Transpiler.Il.MethodBodyReader.Read(...) in MethodBodyReader.cs:line 122
   at Il2Cpp.Transpiler.Emit.CodeGenerator.GenerateCore(...) in CodeGenerator.cs:line 458
```

**错误位置**：`MethodBodyReader.cs:335` — `LocalTypeKind` 方法中读取压缩整数失败。

### 2.2 前驱警告

- 多个 `duplicate FullName 'Node'/'InternalPartitionEnumerable'/'InternalPartitionEnumerator'/'DebugView' skipped` 警告（r78 dedup 已处理）
- `DBG r4:` 调试日志显示正在处理 `ConcurrentDictionary`、`Lazy` 等闭包泛型展开

---

## 3. 根因分析

### 3.1 失败阶段

`MethodBodyReader.LocalTypeKind` 在解析局部变量签名 blob 时遇到无效的压缩整数。这发生在方法体读取阶段（`GenerateCore` 行 458），**早于**任何 cpp 文件生成。

### 3.2 可能原因

1. **mscorlib 元数据损坏** — 某个方法的局部变量签名 blob 包含无效编码
2. **不支持的局部变量类型** — `LocalTypeKind` 遇到未处理的 ELEMENT_TYPE 字节
3. **r79 引入的新代码路径** — 与 r77 相比，r79 可能有新的 MetadataLoader 逻辑触发了不同方法的读取

### 3.3 对比 r77 成功

r77 有 4628 cpp 文件生成。r78/r79 在同一阶段崩溃，说明变化发生在 r78 引入的代码中。

---

## 4. 相关文件状态

### 4.1 源码改动（未暂存）

```
modified: src/il2cppc/Emit/CodeGenerator.cs  (r78 dedup + Yield iterator fix)
modified: src/il2cppc/Emit/MethodSignatureCoder.cs
untracked: src/il2cppc/Emit/CodeGenerator.cs.bak_cannotinit
untracked: src/il2cppc/Emit/MethodMap.Emit.cs
untracked: src/il2cppc/Emit/ScalarTable.cs
```

### 4.2 r78 vs r79 目录

- `mscorlib_gen_r78`：空 src/（生成失败）
- `mscorlib_gen_r79`：无 src/ 目录（生成失败）
- `mscorlib_gen_r79v2`：空 src/（生成失败）

---

## 5. 建议修复方向

### 5.1 立即检查

1. 对比 `MethodBodyReader.cs:335` 的 `LocalTypeKind` 实现
2. 确认 r79 新增代码是否修改了 `MethodBodyReader` 或 `MetadataLoader` 路径
3. 添加 try-catch 包裹 `MethodBodyReader.Read`（类似 r78 inlineCandidate 的容错修复）

### 5.2 长期方案

在 `GenerateCore` 中对 `MethodBodyReader.Read` 添加异常捕获，遇到无效方法体时跳过并记录，而非终止整个生成流程。

---

## 附录：证据

| 证据 | 来源 |
|------|------|
| r79 目录存在但无 cpp | [BASH:Get-ChildItem out\mscorlib_gen_r79\src:0 files] |
| Invalid compressed integer 错误 | [读:out\gen_r79_err.log:14141-14150] |
| MethodBodyReader.cs:335 崩溃点 | [读:out\gen_r79_err.log:14144] |
| r77 成功 4628 cpp | [BASH:Get-ChildItem out\mscorlib_gen_r77\src:4628 files] |
| r78/r79 均为空 | [BASH:Get-ChildItem out\mssql_gen_r78\src:0 files] |
