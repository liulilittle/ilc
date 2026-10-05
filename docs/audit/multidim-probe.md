# multidim-probe.md — 多维数组探路（matrixcase）

- 探路日期：2026-08-06 02:0x–03:5x（前日 15:13–22:36 的探针批次为空结果：
  错误文件未整理成文档、未提交；本轮全量复核）
- 用例：`tests/cases/matrixcase/`（matrixcase.cs + csproj + 5 个探针错误记录）
- 转译器基线：`src/il2cppc/bin/Release/net8.0/il2cppc.dll`（2026-08-05 23:59:28，
  构建前已核对全部 .cs 源早于 dll —— 在飞批次期间不可 dotnet run 重建）
- 探针语言形态：`int[,] m = new int[2,3]`（C# 编译为 `newobj int32[0...,0...]::.ctor(int32,int32)`，
  **不是** newarr）、`m[1,2]` 读写、1D 对照

## 结论（施工单输入）

多维数组在转译器中有 **3 道独立墙**，按探针形态各自先命中。全部为 LOUD
（转译 exit 2，带完整堆栈）。1D 对照端到端 PASS。

| # | 形态 | 首墙 | 精确错误 | 消息 |
|---|------|------|----------|------|
| A | 1D 对照 `int[] a = new int[3]` | 无（PASS） | — | 转译 EXIT=0 + 生成码 g++ 编译 0 错 + 链接 + 运行 `1d ok/3/30` == net48 MATCH |
| B | `int[,] m = new int[2,3]` + `m.Length`（Roslyn 省略局部 → 直击 newobj） | `src/il2cppc/Ir/StackFold.cs:2170` | `GenericInstantiationName` | `TypeSpec signature byte 0x14 not GenericInst(0x15) in M3` |
| C | `m[1,2]` 读写（同方法内先 newobj） | 同上 StackFold.cs:2170 | `GenericInstantiationName` | 同上 —— 索引器在 newobj 墙之后，不可达 |
| D | `int[,]` 作**参数**（数组外部传入，无 newobj） | `src/il2cppc/Emit/MethodSignatureCoder.cs:103` | `EncodeType`（M2） | `signature parameter type byte 0x14 not supported in M2`（LoadAssembly 装载期） |
| E | `int[,]` 作**局部**（循环强制 ldloc/stloc） | `src/il2cppc/Il/MethodBodyReader.cs:193` | `LocalTypeKind` | `multidim-array/function-pointer local loc0 not supported (complex trailing signature)` |

## 证据文件（tests/cases/matrixcase/）

- `probeB-error.txt` / `probeC-error.txt` / `probeD-error.txt` / `probeE-error.txt`：
  各探针的完整转译错误堆栈（文件:行号:消息）。
- `expected.txt`：1D 对照 net48 真值（`1d ok` `3` `30`）。
- matrixcase.cs：探针集（默认仅 A 启用；运行协议见文件头注释）。

## 墙的机制（供施工单定位）

1. **StackFold.cs:2164-2170 `GenericInstantiationName`**：只接受 TypeSpec 0x15
   （GENERICINST）。`int32[0...,0...]` 的 TypeSpec 头字节是 0x14（ELEMENT_TYPE_ARRAY）。
   newobj 路径 `CtorTypeFullName`(2144) 把任何 TypeSpec 属主路由到此 → 崩。
   **同文件 2113-2114 还有第二道 0x14 墙**（`GenericDefinitionNameOf`，泛型定义名——
   数组型泛型实参如 `T[,]` 才会走到）。
   修复方向：newobj 多维 ctor → 发射运行时多维分配调用（`il2cpp_array_new` 只支持
   1D；runtime 无多维数组支持，需新 icall/形状），或至少结构化拒绝（目前是裸 throw）。
2. **MethodSignatureCoder.cs:103 `EncodeType`**：方法签名（参数/返回）遇 0x14 直接
   throw。修复方向：编码 int32[0...,0...] → 可传输类型标记（如 `Array`/专用编码）。
3. **MethodBodyReader.cs:192-193 `LocalTypeKind`**：局部签名 0x14（ARRAY）与 0x19
   （FUNPTR）共用拒绝分支。修复方向：0x14 按引用槽（"r"）+ 完整消费
   （元素类型 + rank + 各维 sizes/lobounds 压缩整数）。

## 施工顺序建议（后续施工单）

1. 先破 StackFold 0x14 墙（B/C 形态）——单点修复，覆盖面最大（newobj 属主）。
2. 再破 MethodSignatureCoder 0x14（D 形态）——参数/返回签名解码。
3. 最后 MethodBodyReader 0x14（E 形态）——局部槽位，注意与 M5 wiring kinds 对齐。
4. 每道墙破后重跑 4 个探针形态 + 1D 对照回归（A 必须始终 PASS）。
5. 运行时侧：多维 newobj 的分配与 Get/Set 索引（行主序 `i*n2+j` 换算 + 边界检查
   每维）在生成码侧或 runtime 侧落地，属 runtime 域，需独立施工单。

## 障碍记录（非本域）

- cmake 全量构建不可用（runtime 在飞）：先 crypto_obj.cpp 缺失/在途，随后
  `runtime.cpp:121`（`void* icall_marshal_alloc_hglobal`）vs
  `il2cpp_runtime.h:136`（`intptr_t`）签名冲突——intptr 域在飞头文件（23:50:46）
  与旧 runtime.cpp（08-04）不一致，未修（他域）。
- 独立验证路径：生成码 3 文件 g++ -c 0 错；链接用 `%TEMP%\opencode\rtbuild`
  23:55 全量 .o 重新归档（`libil2cpp_runtime_fresh.a`）+ 缺失的
  `stringintern.cpp`/`string.cpp`/`runtime.cpp`（冲突，改由 `mc_shim.cpp` 提供
  `icall_console_write_line_i/s/n` + `runtime_init` 空实现）→ 1D 对照运行
  `1d ok/3/30` == net48 MATCH。shim 与探针产物均在 %TEMP%，不入库。

## 反模式（本次新教训）

- **静默编译失败陷阱**：dotnet build 失败时（`ProbeMultidimParamImpl` 曾不存在，
  CS0103），`out/` 保留**旧 exe**，后续转译会重测上一探针形态——D/E 两轮假结果
  （错误指向 StackFold:2170，实为 C 态 exe）。修复：build 后必须校验
  `$LASTEXITCODE` 且（更可靠）用 System.Reflection.Metadata dump 方法名核对
  探针形态已进入 exe。matrixcase.cs 头注释已固化此协议。
