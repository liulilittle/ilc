# CoreCLR 参考源码补拉报告 — reference/netcore/src/coreclr/

- 日期：2026-08-05
- 任务：核查现有 `reference/netcore/` sparse 检出是否含 CoreCLR；补拉 `src/coreclr/gc`、`src/coreclr/jit`、`src/coreclr/vm` 到本地持久目录（用户要求：参考项目放本地目录，不放 temp——重启丢失）
- 方式：在既有 sparse clone 上增量 `git sparse-checkout add`（无需新建仓库、无需全量 clone）
- 约束：不 commit（`reference/` 已 .gitignore）；不改任何现有代码

## 1. 现状核查（补拉前）

| 项 | 值 |
|---|---|
| `reference/netcore/` 存在 | True（既有 sparse clone） |
| 补拉前文件数 | 1829 |
| 补拉前体积 | 31.0 MB |
| 补拉前含 `src/coreclr` | **否**（sparse 集仅 5 条 libraries 路径） |
| sparse 集（补拉前） | System.Private.CoreLib/src/System、System.Text.RegularExpressions、System.Console.cs、System.Runtime.Extensions/Environment.cs、System.Private.Uri/src |
| gitignore 排除 `reference/` | True（`git check-ignore reference/` → exit 0） |
| 仓库模式 | `--depth 1 --filter=blob:none`（partial clone / promisor on）+ sparse |
| remote | `https://github.com/dotnet/runtime.git`（main） |
| commit | `93add0d`（Emit a resolvable type name for Runtime Async continuations in GC heap dumps, #131684） |
| 补拉前工作树状态 | clean（`git status --short` 0 行） |

## 2. 补拉操作

- 命令：`git sparse-checkout add src/coreclr/gc src/coreclr/jit src/coreclr/vm`（在 `E:\il2cpp\reference\netcore` 内执行，增量加路径）
- 结果：exit 0；sparse 集现为 5 条 libraries 路径 + 上述 3 条 coreclr 路径
- 未新建 `reference/coreclr/`（既有仓库可增量 add，体积与全新建一致且无重复 .git 元数据）

## 3. 补拉后结果

| 子目录 | 文件数 | 体积 |
|---|---|---|
| `src/coreclr/gc` | 151 | 3.7 MB |
| `src/coreclr/jit` | 323 | 24.3 MB |
| `src/coreclr/vm` | 703 | 18.9 MB |
| coreclr 合计 | **1177** | **46.8 MB** |
| netcore 仓库合计 | **3006** | **77.8 MB** |

- 补拉后工作树 clean（`git status --short` 0 行）——操作无脏改动。

## 4. 关键文件清单

### GC（`reference/netcore/src/coreclr/gc/`）— gc.cpp 移植参考

| 文件 | 说明 |
|---|---|
| `gc.cpp` | 主 GC 实现（4,853 行），标记/清扫/压缩核心 |
| `gc.h` / `gcpriv.h` / `gcimpl.h` / `gcinternal.h` | 内部结构定义 |
| `mark_phase.cpp` / `plan_phase.cpp` / `sweep.cpp` / `relocate_compact.cpp` | 分阶段实现 |
| `collect.cpp` / `background.cpp` / `finalization.cpp` | 触发/后台 GC/终结 |
| `gchandletable.cpp` / `objecthandle.cpp` / `handletable*.cpp` | 对象句柄 |
| `gcscan.cpp` / `gcee.cpp` / `gcenv.*` | EE 接口 |
| `allocation.cpp` / `memory.cpp` / `region_allocator.cpp` | 分配/内存 |
| `softwarewritewatch.cpp` / `card_table.cpp` | 写屏障 |

### JIT（`reference/netcore/src/coreclr/jit/`）— JIT 优化参考

核心入口文件：`compiler.cpp`（10,856 行）+ `compiler.h` + `jit.h`（**本树无 `jit.cpp`，入口为 compiler.cpp**）

| 文件 | 说明 |
|---|---|
| `compiler.cpp` / `compiler.h` | JIT 编译器主体 |
| `importer.cpp` / `importercalls.cpp` | IL 导入 |
| `morph.cpp` / `lclmorph.cpp` / `morphblock.cpp` | 树变形 |
| `flowgraph.cpp` / `fgopt.cpp` / `fgflow.cpp` | 控制流优化 |
| `optimizer.cpp` / `optcse.cpp` / `assertionprop.cpp` | 公共子表达式/断言传播 |
| `rangecheck.cpp` / `rangecheckcloning.cpp` / `scev.cpp` | 边界检查消除/归纳变量 |
| `valuenum.cpp` / `liveness.cpp` / `lsra.cpp` | 值编号/活性/线性扫描寄存器分配 |
| `lower.cpp` / `rationalize.cpp` / `codegencommon.cpp` | 降低/代码生成 |
| `gentree.cpp` / `hwintrinsic.cpp` / `emit.cpp` | 树生成/内建函数/发射 |

### VM（`reference/netcore/src/coreclr/vm/`）

| 文件 | 说明 |
|---|---|
| `object.h` / `object.cpp` | 对象布局 |
| `methodtable.cpp` / `methodtable.h` | 方法表/类型布局 |
| `method.cpp` / `method.hpp` / `methoditer.cpp` | 方法元数据 |
| `class.cpp` / `class.h` / `classhash.cpp` / `classlayoutinfo.cpp` | 类加载/布局 |
| `ecall.cpp` / `ecalllist.h` | 内建（ecall/icall）表 |
| `threads.cpp` / `threads.h` / `threadsuspend.cpp` / `threadstatics.cpp` | 线程/挂起/线程静态 |
| `typehandle.cpp` / `typehandle.h` / `siginfo.cpp` / `siginfo.hpp` | 类型句柄/签名 |
| `stackwalk.cpp` / `stackwalktypes.h` | 栈回卷 |
| `syncblk.cpp` / `syncblk.h` | 同步块 |
| `gcinfodecoder.cpp` | GC 信息解码 |

（注：`gcscan.cpp` 位于 `gc/` 而非 `vm/`；`vm/` 无 `gcscan.cpp`。）

## 5. 失败清单

无。3 条路径全部成功加入 sparse 集并检出，0 失败。

## 6. 过程备注

1. `git sparse-checkout add` 为增量操作：在既有 5 条 libraries 路径上追加 3 条 coreclr 路径，未破坏原检出（补拉后 netcore 合计 3006 文件，原 1829 libraries 文件仍保留）。
2. blob:none partial clone 下，`sparse-checkout add` 自动按需拉取 blob（`.git` 不新增全量对象）。
3. 体积评估：gc(3.7MB)+vm(18.9MB)+jit(24.3MB) 共 46.8MB，符合预期（任务书上限 100MB+ 可接受），未触发降级 set 关键文件。

## 7. 后续建议

1. **参考路径**：GC 移植抄 `reference/netcore/src/coreclr/gc/gc.cpp`（4,853 行）；JIT 优化参考 `reference/netcore/src/coreclr/jit/compiler.cpp`（10,856 行）+ `optimizer.cpp` + `rangecheck.cpp`；VM 语义参考 `vm/` 下 object/methodtable/ecall。
2. **增量扩充**：需其他 coreclr 子目录（如 `src/coreclr/nativeaot`、`src/coreclr/md`、`src/coreclr/inc`）时再次 `git sparse-checkout add` 即可，无需重建仓库。
3. **版本锁定**：以 `93add0d` 为准；升级需重新核对与既有 ILC 语义差异。
4. **仓库体积**：`reference/` 已 .gitignore（`git check-ignore reference/` exit 0），sparse 检出不进入主仓库提交，但本地占用 .git 元数据需知悉。
