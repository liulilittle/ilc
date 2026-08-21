# 施工单 #4 cctorchain 修复方案（2026-08-05，冲突模式 = 方案文档）

> **状态：BLOCKED（文件被并行代理占用，冲突协议生效——只做方案文档，未改任何 src 代码）**
>
> **批A 复核（2026-08-06 09:3x-09:5x）**：CodeGenerator.cs 仍被独占——
> 9:49:02 在途重构（git diff +123/-17，EmitBody 5 参错误，全树不可编译）；
> 8:30 与 9:49 两轮 dotnet 构建进程活跃。方案 §2.1 复检仍精确
> （StackFold.cs:1636 附近无 cctor 相关改动；StackFold.cs:27 过期注释仍待修，
> 属 StackFold 域）。wo4 继续生效，待 CodeGenerator.cs 释放
> （mtime ≥30min + 无构建进程 + 树可编译）后按 §4 执行。
>
> 冲突证据：任务首查 13:57:54 时 CodeGenerator.cs mtime=12:30:51（空闲 87min）；
> 16:37:56 复查发现 CodeGenerator.cs **16:32:46**（5min 前）、Program.cs **16:35:20**、
> CliOptions.cs 16:17:02、MethodRefResolver.cs 16:15:35、TypeEmitter.cs 16:07:55、
> MethodMap.Console.cs 16:00:24、TempSlotPass.cs 15:48:13 均在写；dotnet 进程活跃
> （StartTime 14:44:48 / 16:37:55）。**源树当前不编译**（边界检查/pinvoke 代理
> 重构在途）：
> - `Program.cs(38,17) error CS0103: 当前上下文中不存在名称 "WriteReflectionManifest"`
> - `CodeGenerator.cs(46,35) error CS0161: GenerateCore 并非所有的代码路径都返回`
>
> 本卡不触碰 src/il2cppc 任何文件（含独占文件 CodeGenerator.cs）。等文件释放
> （mtime ≥30min + 无构建进程 + 树可编译）后按本方案执行。

## 1. 根因（已逐行核实，W5 证据齐）

| 证据 | 内容 |
|------|------|
| [READ:CodeGenerator.cs:218-224] | `bool {T}_cctor_done = false;` 后 `ensure_cctor_{T}` 先 `{T}_cctor_done = true;` **再** `{MethodCppName}();` |
| [READ:cctorchain.cs:63-66] | `Boom.Val=99; static Boom() { WriteLine("Boom.cctor"); throw new InvalidOperationException("boom"); }` |
| [READ:cctorchain.cs:95-99] | 两次 `try { Boom.Val } catch (Exception) { caught++; }` → 期望 `boom_caught=2` |
| [READ:expected.txt:13-14] | `Boom.cctor` **只打印一次** + `boom_caught=2` |
| [READ:e2e-sprint-worklist.md:27] | 施工单 #4 原文："L14 `boom_caught=2` vs 1（.cctor 抛异常后第二次访问不重抛）；cctor 守卫区分成功/失败：失败缓存 TypeInitializationException 并在后续访问重抛（net48 语义）" |

因果链：第一次访问 → done=false → **done=true（先置位）** → 跑体 → 抛异常（未被守卫捕获）
→ 第二次访问 → `if (done) return;` 恒跳过 → 不抛 → `boom_caught=1`。
net48 oracle：体只跑一次（`Boom.cctor` 一行），但异常被**缓存**，后续访问**重抛**不重跑 →
`boom_caught=2`。

**注意**：简单把 done=true 挪到体后（"二次访问重跑体"）会打印两次 `Boom.cctor` → diff
第 13 行失败 → 测试不绿。正确语义 = 缓存+重抛（与施工单原文一致）。

## 2. 修复设计（等文件释放后执行，独占 CodeGenerator.cs）

### 2.1 发射代码替换点（CodeGenerator.cs:218-225）

现：
```csharp
w.Line($"bool {cppName}_cctor_done = false;");
w.Line($"IL2CPP_NOINLINE void ensure_cctor_{cppName}(void) {{");
w.Open();
w.Line($"if ({cppName}_cctor_done) return;");
w.Line($"{cppName}_cctor_done = true;");
w.Line($"{MethodCppName(t, cctor)}();");
w.Close();
w.Line("}");
```

替换为（4 个文件作用域变量 + 守卫函数）：
```csharp
w.Line($"bool {cppName}_cctor_done = false;");
w.Line($"static bool {cppName}_cctor_running = false;");
w.Line($"static const char* {cppName}_cctor_exc_type = nullptr;");
w.Line($"static const char* {cppName}_cctor_exc_msg = nullptr;");
w.Line($"IL2CPP_NOINLINE void ensure_cctor_{cppName}(void) {{");
w.Open();
w.Line($"if ({cppName}_cctor_done) return;");
w.Line($"if ({cppName}_cctor_running) return;");
w.Line($"if ({cppName}_cctor_exc_type != nullptr) throw il2cpp::DotNetException({cppName}_cctor_exc_type, {cppName}_cctor_exc_msg);");
w.Line($"{cppName}_cctor_running = true;");
w.Line($"try {{");
w.Open();
w.Line($"{MethodCppName(t, cctor)}();");
w.Close();
w.Line($"}} catch (il2cpp::DotNetException& __il2cpp_cctor_ex) {{");
w.Open();
w.Line($"{cppName}_cctor_exc_type = __il2cpp_cctor_ex.type_name();");
w.Line($"{cppName}_cctor_exc_msg = __il2cpp_cctor_ex.message_text();");
w.Line($"{cppName}_cctor_running = false;");
w.Line($"throw; // rethrow the cached cctor failure");
w.Close();
w.Line($"}} catch (...) {{");
w.Open();
w.Line($"{cppName}_cctor_running = false;");
w.Line($"throw;");
w.Close();
w.Line($"}}");
w.Line($"{cppName}_cctor_running = false;");
w.Line($"{cppName}_cctor_done = true;");
w.Close();
w.Line("}");
```

生成的 C++ 语义（= net48 行为）：
```
if (done) return;                // 已完成：恒 no-op（正常路径不变）
if (running) return;             // 体在跑（同线程重入：T.cctor→T.member→T.static 字段）
                                 //   → no-op，读默认值（net48 同线程重入语义）；防无限递归
if (exc_type) throw DotNetException(exc_type, exc_msg);  // 已失败：缓存重抛，不重跑体
running = true;
try { Body(); }                  // 唯一跑体点
catch (DotNetException& ex) { exc_type=..; exc_msg=..; running=false; throw; }  // 缓存+重抛
catch (...) { running=false; throw; }   // 非托管异常：不缓存，重跑（最佳努力）
running = false;
done = true;                     // 仅成功后置位
```

### 2.2 为什么这样设计（证明）

- **boom_caught=2 且 Boom.cctor 只打印一次**：访问 1 跑体+抛+缓存；访问 2 命中缓存分支
  直接重抛，不重跑体 → 输出与 expected.txt 逐字节一致。
- **done 成功后置位**（任务 TDD 要求）：异常时 done 恒 false。
- **running 标志非可选**：若只有 done 后置位而无 running，T.cctor→M→T.static 的间接
  同型重入会无限递归（旧代码靠"体前置 done"隐式防住了——删除该行为就是回归）；
  running 分支恢复旧代码的 no-op 语义，且与 net48 同线程重入一致。
- **指针缓存安全**：[READ:il2cpp_exception.h:62-64] 契约："Both strings must be stable
  (static literals; generated code passes compile-time constants)"；[READ:exception.cpp:28-31]
  `il2cpp_throw_new` 即 `throw DotNetException(typeName, message)`，typeName/message 恒
  编译期字面量 → 缓存 const char* 不悬垂。每次失败类型至多缓存一次（有界，与 .NET
  永久缓存 TIE 一致）。
- **1:1 配对**（running↔reset）：running=true 恰好一条；所有出口配对 reset——正常
  （跑体后 running=false）、DotNetException catch（重抛前）、catch(...)（重抛前）。
  缓存重抛分支不触碰 running（false 保持）。done 仅在成功后置位一次。
- **非 DotNetException**（如 std::exception 逃逸）：无法缓存 → 不缓存，二次访问重跑体
  （最佳努力；管理异常才是本域契约，见 il2cpp_exception.h 头注释）。
- **TypeInitializationException 包装不做**：测试 `catch (Exception)` 通用捕获，且现有
  首次访问也不包装（cctorchain 通过唯一条件 = 两次都抛）；YAGNI。

### 2.3 不动（已有测试断言保护，防破坏）

- `if ({T}_cctor_done == false) ensure_cctor_{T}();` 内联检查（CodeGenerator.cs:1189）——
  [READ:CodeGeneratorTests.cs:450/488] 断言其存在与计数（Main 3 处）不变。
- `extern bool {T}_cctor_done;`（CodeGenerator.cs:227）——[READ:CodeGeneratorTests.cs:512]
  断言不变；cross-TU 读取依赖它（running/exc_* 仅 ensure 内部使用 → static 内部链接）。
- `bool {T}_cctor_done = false;` 首行（CodeGenerator.cs:218）——[READ:CodeGeneratorTests.cs:514]
  断言逐字不变。
- entry.cpp 的 `ensure_cctor_{MainType}();`（CodeGenerator.cs:3097）不动。
- CctorOwnerFullName 抑制（CodeGenerator.cs:312-314）：.cctor 体内部仍不发自身守卫
  （running 分支兜底间接重入，抑制依然正确）。

### 2.4 顺带注释更新（同文件内）

- CodeGenerator.cs:205-210 头注释：补"异常缓存 + running 重入守卫"说明。
- CodeGenerator.cs:312-314：`done 恒真` 措辞改为 `running 恒真/重入跳过`（机制变了，
  结论不变——抑制仍需要）。
- CodeGenerator.cs:1183-1188：`(which sets the flag then runs the .cctor body)` →
  `(which runs the body under try, caches a throwing failure, rethrows on later
  access)`。
- StackFold.cs:27 注释（"ensure_cctor_* 先置 done=true"）已过期，但 **StackFold.cs 不在
  本卡独占域内**——只记录，不修（待文件域释放）。

## 3. 测试（TDD，已写好在途）

### 3.1 单元测试（已加入 tests/unit/CodeGeneratorTests.cs，3 处插入）

`Generate_CctorGuard_DoneSetAfterBodyAndExceptionCached`（插在
DoneFlagDeclaredExternInGeneratedHeader 之后）：
- `Assert.Contains("Calc_Program_cctor_running = true;", guard)` + `if (Calc_Program_cctor_running) return;`
- `Assert.Contains("Calc_Program_cctor_exc_type" / "_cctor_exc_msg", guard)`
- `Assert.Contains("throw il2cpp::DotNetException(Calc_Program_cctor_exc_type, Calc_Program_cctor_exc_msg);", guard)`
- `Assert.Contains("catch (il2cpp::DotNetException& __il2cpp_cctor_ex) {", guard)` + `throw; // rethrow the cached cctor failure`
- 顺序断言：`guard.IndexOf("Calc_Program_cctor_done = true;") > guard.IndexOf("try {")`（done 在体后）

辅助 `CctorGuardRegion(cpp, typeCppName)`：从 `IL2CPP_NOINLINE void ensure_cctor_{T}(void)` 截到文件尾。

**RED 依据**：现发射无 try/catch/exc_* → 上述 Contains/顺序断言全 fail（已在 16:01 写入；
因源树被占用无法运行——树编译通过后首跑即应 RED）。

### 3.2 e2e 行为测试（既有，不改）

`tests/cases/cctorchain/`：net48 oracle expected.txt（`Boom.cctor`×1 + `boom_caught=2`）。
修复前矩阵4实测 `boom_caught=1`（[READ:process.md:47] L14 boom 1vs2）——RED 已存在；
修复后 `boom_caught=2` + diff 逐字节 PASS。

## 4. 执行步骤（文件释放后按序）

1. 前置复核：CodeGenerator.cs mtime ≥30min + 无构建进程 + `dotnet build src/il2cppc -c Release` 0 err。
2. RED：`dotnet test tests/unit -c Release --filter Generate_CctorGuard_DoneSetAfterBodyAndExceptionCached`
   → 确认因"新形状缺失"失败（非笔误）。
3. 备份（.opencode/backups/）→ 按 §2.1 编辑 + §2.4 注释。
4. GREEN：同 filter 单测过。
5. 全量单测：`dotnet test tests/unit -c Release`（"本域绿"；重点盯 Generate_CctorGuard_* 4 个既有用例不回归）。
6. `dotnet build src/il2cppc -c Release` 0 err 0 warn（C0）。
7. e2e：`scripts/run-tests.ps1 -Case cctorchain -ContinueOnError` → PASS（boom_caught=2）。
8. W8 更新 4 memory 文件 + 施工单 #4 关闭记录。

## 5. 冲突记录（本卡最终状态）

- [ATTEMPT] 13:57 首查 mtime=12:30:51（87min）判定空闲 → 开始 TDD（备份 + 写单测）。
- [ERROR] 16:01-16:19 间另一代理（边界检查/pinvoke 三选一）接管 src/il2cppc 全树：
  16:00-16:35 连续写 7 文件，CodeGenerator.cs 16:32:46；`dotnet test` 构建失败
  CS0103 WriteReflectionManifest / CS0161 GenerateCore（重构在途）。
- [NEXT] 施工单协议：**不触碰任何 src 文件**；只产出本方案文档 + 已就绪测试
  （tests/unit/CodeGeneratorTests.cs 为纯增量——不删除，避免与代理并发编辑冲突）。
- 已确认：tests/unit/CodeGeneratorTests.cs 16:19:45 仍含本卡测试（[READ:535/543/758]）。
