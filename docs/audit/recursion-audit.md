# 深递归与尾调用现状审计（recursion-audit，2026-08-03，只读）

> 范围：深递归用例（tests/cases/recurse）在 AOT 转译下的栈深度极限、转译器尾调用优化现状、
> 递归函数栈帧大小实测。exdeep 用例一并勘误。
> 方法：共享树（gen + build_mingw）不可直接编译（见 §4），全部测量走**临时目录**方案——
> 用现版 Release 转译器（构建于 19:03，新于全部源码 18:57）把 recurse.exe 转译到
> `%TEMP%\opencode\recurse_probe\gen`，手写补充缺失的嵌套类型 Node 声明（见 §4），
> 链接复用预构建 `build_mingw\runtime\libil2cpp_runtime.a`（17:52 产物，符号齐全），
> 探针入口从 argv 构造 string[] args 传入 Main（深度可调）。**仓库零文件改动。**
> 工具链：C:/Program Files/ming-w64/bin/g++.exe（实际为 clang 17.0.6，cmake 同源）。
> 关联文档：docs/audit/stackframe-audit.md（temp/GC 根膨胀率）、docs/audit/generated-code-audit.md。

---

## 1. 用例现状

### tests/cases/recurse（真·深递归用例）
- `recurse.cs`：Fib(25)（双路递归）、Sum1ToN（直链递归，`n + call`，非尾）、
  DeepAlloc（**每层 new 对象 + 每层 GC frame 压栈**，底端 GC.Collect() 全深根扫描）、
  TailSum/TailFact（累加器尾递归样式）。默认深度：10000 / 4000 / 20000，
  `expected.txt` 5 行输出。
- 源注释记录的 net48（1MB 栈）参考极限：Sum1ToN ~31850、DeepAlloc 5744（5743 ok / 5744 SO）、
  TailSum 达 1,000,000 不崩（net48 JIT 尾调用优化）。

### tests/cases/exdeep（勘误：非递归用例）
- 名为 exdeep 但**不是深递归**：f1-f23 是嵌套 try/catch/finally 的异常语义链
  （f20 三层嵌套 finally 展开顺序），"deep" = 异常嵌套深度。
- **当前不可转译**：现版转译器解码抛 `NotSupportedException: IL opcode 0xFE1A
  (endfinally) at offset 231 not supported in M1`（IlDecoder.cs:236 default 分支）。
  （若 0xFE1A 解码支持已合入，该用例仍受嵌套类型发射缺失影响——见 §4.2。）

## 2. 实测：AOT 二进制最大递归深度（探针，clang-17/MinGW，主线程 1MB 栈）

### 2.1 无优化构建（当前 e2e 配置：run-tests.ps1 cmake 未设 CMAKE_BUILD_TYPE → 无 -O）

| 递归函数 | 帧内容（-O0） | OK 最大 | 崩溃深度 | 推断帧大小 |
|---|---|---|---|---|
| Sum1ToN | t0(4B)+shadow+ra | **16000** | 17000 | ~60-65 B |
| TailSum | t0(4B)+shadow+ra | **16000** | 18000 | ~60 B |
| DeepAlloc | t0,loc0(ptr×2)+t1+`__frame`(32B)+`__frameRefs[2]`(16B)+shadow | **5000** | 5500 | ~190-200 B |

- **默认深度运行（10000/4000/20000）崩溃**：fib✓、sum1to✓、alloc_deep✓ 三行后
  TailSum(20000) 段错误。**当前构建配置下 recurse 用例无法通过。**
- 崩溃形态是 `0xC0000005`（访问保留区），非 `0xC00000FD` 守护页异常——MinGW 无
  栈探测（无 MSVC /Gs 等价物），守护页一次性放行后撞保留区。
- 深递归**全部**消耗真实 C++ 调用栈帧：尾递归样式（TailSum `return t0;`）在 -O0 下
  与普通递归同深度崩溃，转译器未提供任何尾调用优化。

### 2.2 -O2 构建（对照组：同样的生成 C++，仅编译器优化级别不同）

| 递归函数 | OK 最大 | 崩溃深度 | 备注 |
|---|---|---|---|
| TailSum | **1,000,000** | 不崩 | clang 兄弟调用/尾调用优化把 `t0=call; return t0` 折叠为跳转，栈 O(1) |
| DeepAlloc | 8000 | 12000 | 有 `__frame`（非平凡析构）→ 尾调用被正确阻止，仍吃栈 |
| 默认运行(10000/4000/20000) | **全部通过**，与 expected.txt 逐字节一致 | | |

**关键结论：** 生成的尾形状（`t0 = F(...); return t0;`）在 -O2 下被 C++ 编译器
自动尾调用优化，效果等同 net48 JIT；但 e2e 脚本当前以无优化构建，此优化完全没吃到。
给 e2e 构建加 `-DCMAKE_BUILD_TYPE=Release`（或 -O2）即可让 recurse 默认深度通过——
这是今天的零代码改动修法。

## 3. 转译器尾调用现状：**完全没有**

- grep `src/il2cppc/`（tail/TailCall/tailcall）：仅 `CodeGenerator.cs:613-620` 的
  `tailRegion`——异常处理器区域变量，**与尾调用无关**。
- IL 层：`tail.` 前缀（0xFE14）落入 IlDecoder.cs:235-237 default → 抛
  `NotSupportedException "not supported in M1"`。含 `tail.` 的 IL（F# 等产出）直接拒绝。
- 发射层：所有方法 `IL2CPP_NOINLINE`；方法序言统一 `il2cpp_frame_push`（引用局部才推），
  RAII 析构自动 pop（il2cpp_frame.h:61-64）。尾调用形状照常发射为
  `t0 = Call(...); return t0;`。
- CLI（Program.cs:12）：仅 `<input-assembly> <output-dir>` 两个位置参数，
  **无 --stack-size、无优化/发射开关**。
- 转译器层尾调用优化 = 无。现状全靠 C++ 编译器 -O2 顺手的兄弟调用优化兜底，
  且只在"无 GC frame 的方法"上生效。

## 4. 共享树不可编译（本次审计的硬约束，均已验证）

1. **runtime 在途改动**：`src/runtime/src/stackqueue.cpp:49` static_assert 失败
   （`offsetof(Il2CppQueue,count)==20`，断言要求 `==3*sizeof(void*)`=24）——并行代理
   在途，cmake 全量构建中止。复用 17:52 预构建 .a 规避（符号齐全，含 frame_push）。
2. **转译器嵌套类型发射缺失/在途**：新鲜转译产出引用 `Node_class`/`Node`/
   `Node__ctor_b3740a9592788e34` 却**从不定义**（generated.h 无 Node 声明；
   仓库旧 gen 树同病）。对照 props 用例 gen（`Counter_class = {...}`、
   `struct Counter { Il2CppObject _obj; ... }`）可知正常发射形态。本审计在临时
   目录手写补齐 Node（struct + 描述符 + 空 ctor）完成测量。
3. **entry.cpp 与 Main 签名不匹配**：生成代码声明
   `Cases_Recurse_Main(Il2CppObject*)`，entry.cpp 却无参调用——旧 gen 树同样编不过，
   即 recurse 用例自 M3 frame-push 改造后从未通过编译。

## 5. 栈帧大小实测（任务 5）

经验值（崩溃阈值倒推，1MB / 深度）：

| 函数 | 生成 C++ 局部（静态） | 实测帧大小 |
|---|---|---|
| Fib | t0, t1（2×int32=8B） | ~48-64 B |
| Sum1ToN | t0（1×int32=4B） | ~60-65 B |
| TailSum/TailFact | t0（4B） | ~60 B |
| DeepAlloc | t0,loc0(Il2CppObject*×2)+t1+`__frame`(32B)+`__frameRefs[2]`(16B) | ~190-200 B |
| Main | 33 局部 + `__frameRefs[17]`(136B)+`__frame`(32B) | ~900 B（序言 subq $0x2d8） |

估算规则：-O0 下每帧 ≈ 局部量 + 32B shadow space + 返回地址/保存寄存器；
GC frame（`__frame` 32B + refs 数组）把深递归帧从 ~60B 抬到 ~200B。
**1MB 栈下量级：无 frame 递归 ≈1.6 万层；带 GC frame 递归 ≈5 千层。**

## 6. 设计建议

### (a) 转译器尾调用 → goto 循环转换（转译层 TCO）

**转换条件（全部满足才转）：**
1. **方法自递归**：call 的目标函数 == 当前方法（同一 `{Owner}::{Name}`+签名，比对
   转译函数名即可，含虚调用前经 MethodRefResolver 解析确认的具体目标）。
2. **调用在尾位置**：IR 形状为 `t0 = CallF(args); return t0;` 或
   `return CallF(args);`——即 call 结果不做任何后续运算直接被 ret。
3. **无异常处理器**：call 到 ret 之间不存在任何 try/catch/finally 区域覆盖
   （有 handler 则必须真实返回以展开，禁止循环化）。
4. **GC frame 安全**：方法有 `__frame` 时，帧槽（refs 数组内局部地址）在循环
   复用后地址不变 → 单次 push 仍有效；但**新参数必须先写入参数局部槽再跳回**，
   且保证 frame push 在循环外（序言一次）。无 frame 方法无此约束。

**转换形态（示例，TailSum）：**
```cpp
// 原发射：
//   t0 = TailSum(arg0-1, arg1+arg0); return t0;
// 转换为：
//   L_tail: arg0 = arg0-1; arg1 = arg1+arg0; goto L_entry;
// 入口标签 L_entry 挂在函数体第一条语句前；循环出口 = 原非递归 return。
```
- 实现位置：Ir/StackFold.cs 折叠出 `return call` 形状时标记；CodeGenerator.cs
  发射前做"自递归尾调用→goto"重写（或 IR 层加 `TailJumpStmt`）。
- 语义等价证明：自递归尾调用 `return F(newArgs)` 与
  `{ args=newArgs; goto start; }` 在所有执行路径上可观测行为一致（无 EH、
  无 frame 副作用的条件下；返回值路径唯一收敛）。
- 与 -O2 的关系：转译层 TCO 让**无优化构建**也获得 O(1) 栈（探针证明编译器
  -O2 只救无 frame 方法；转译层转换对带 frame 方法同样生效，覆盖更广）。

### (b) 深递归用户应对（不做转译层 TCO 时的兜底）

1. **提高主线程栈大小（链接器）**：在生成 CMakeLists.txt 为 exe target 追加
   - MSVC：`target_link_options(il2cpp_gen_exe PRIVATE "/STACK:8388608")`
   - MinGW/GCC：`target_link_options(il2cpp_gen_exe PRIVATE "-Wl,--stack,8388608")`
   8MB 栈下：Sum1ToN ≈13 万层、DeepAlloc ≈4 万层（实测帧大小线性外推）。
2. **`--stack-size` CLI 选项（建议）**：il2cppc 增加可选参数
   `--stack-size <bytes>`，透传到生成 CMakeLists 的链接选项；缺省不写
   （保持默认 1MB 与 .NET Framework 对齐）。Program.cs 参数解析需从
   固定 2 位置参数改为支持开关。
3. **e2e 脚本加 -O2（今天即可做，零代码）**：run-tests.ps1 的 MinGW 分支
   `cmake -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release`——默认深度用例立即
   通过，且 TailSum 类尾形状获得编译器尾调用优化（实测 1M 深度不崩）。
4. **文档化栈预算**：1MB 栈下无 frame 递归 ≈1.6 万层、带 frame ≈5 千层；
   用例默认深度须低于此（recurse 默认 20000 已越界——TailSum 应降到 ≤16000
   或按第 3 条加 -O2）。

## 7. 结论

- **现状**：转译器无任何尾调用识别/优化（`tail.` 前缀直接拒绝；尾形状原样发射）。
  深递归全靠 C++ 调用栈；1MB 栈下无 GC frame 递归 ~1.6 万层、带 GC frame ~5 千层即崩。
  **recurse 用例默认深度（TailSum 20000）在当前无优化构建下必然崩溃。**
- **exdeep 不是递归用例**（异常嵌套语义），且当前被 endfinally(0xFE1A) 解码缺口阻塞。
- **最小修法**：e2e 构建加 `-DCMAKE_BUILD_TYPE=Release` → 默认深度全部通过
  （-O2 下 TailSum 实测 1,000,000 层不崩）。
- **根治方向**：转译层"自递归尾调用→goto 循环"（条件见 §6a）+ `--stack-size`
  链接选项（§6b），二者互补：前者 O(1) 栈，后者线性提升容量。
