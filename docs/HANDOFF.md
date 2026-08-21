# ILC 项目交接提示词（交付给下一个 AI 会话，2026-08-06）

> 使用方式：将本文整个复制粘贴为新会话的第一条消息。新 AI 读完本提示词后，
> 先读 docs/project-state-20260806.md（完整快照）再开始工作。

---

你是 ILC（IL→C++ AOT 编译器）项目的调度主代理，继续推进项目直到对齐 .NET Framework 4.8 行为。项目路径 E:\il2cpp（git 分支 m1，HEAD=f36f386）。

## 铁律（不可违背）

1. **正式名 ILC，不是 il2cpp**（il2cpp 是形意词汇，非工程定义；命名纠正方案 docs/architecture/rename-ilc-plan.md）。
2. **主代理=监工/调度**：只拆任务派发并行子代理（用户要求每批 40-100+ 并发），不做实现。任务卡约束：文件域隔离（每文件一代理独占，先查 mtime<15min=在途）+ 独立 build 目录 + 不 git commit（主代理统一提交）。
3. **绝对原则：实现任何 .NET 语义前必须先抄 .NET Core 开源实现**（微软数十年验证，MIT）——本地参考：E:\il2cpp\reference\netcore\（libraries+coreclr gc/jit/vm）+ reference\corert\（TypeSystem）；映射表 docs/netcore-reference-guide.md；conformance 审计 docs/audit/*conformance*。
4. 跨平台铁律：IOS/MACOS/LINUX/WINDOWS/ANDROID/OPENBSD 接口；已落地 NDK/WINDOWS/LINUX；三编译器门 MinGW/MSVC/WSL GCC7.5 零警告。
5. 字符串池化技术（T3 g_strPool 已落地）；性能铁律（fib 1.50×/array 1.33× 已超 net48）。
6. 每批次 git 提交防回退（提交前 git diff --cached --stat 核对+无 .opencode/build/gen/out/obj/.o/.inflight 泄漏）。

## 已完成（别重做）

- **M1-M4**：IL 解码/IR 折叠/CodeGenerator/EH/P-Invoke/Marshal/C API
- **M5 接线 85.6%**（459 icall/393 已接，MethodMap 66 个全 committed）：Math/String/IO/Console/DateTime/TimeSpan/Random/Array/Convert/GC/Delegate/Type/Enum/Attribute/Format/Regex/List<int>/ArrayList/Stopwatch 对象态/Thread 对象态/ThreadPool/Process/Environment/TimeZone/XML/Crypto（MD5/SHA1/SHA256/SHA384/512/HMAC/AES/RNG）/Net IPAddress/Hashtable/SortedList/Stack/Queue/StringBuilder/StringReader/Writer/BinaryIO/Encoding/Base64/BitConverter/Marshal/Interlocked/IntPtr/Monitor/lock/WeakReference/LINQ
- **M5.5 反射**：typeof 折叠/GetType 短名/typeinfo 注册表/invoker 统一签名/Activator.CreateInstance/Type.GetType/模式 B CLI
- **M6 转译器**：泛型展开/嵌套类型/委托实例绑定/多播 invoke/EH（rethrow/catch-all/sibling 分派链 exdeep 37/37）/尾调用（TailSum 100 万深度）/switch 折叠/字符串池 T3/槽复用（11→3）/内联/边界检查（部分）/cctor 守卫缓存（boom_caught=2）
- **M6.5 跨平台**：平台层 31 API/encoding POSIX/NDK 探通/三编译器全绿/console UTF-8/gc 压缩器修复/sblong 根槽化
- **M7**：fib 1.50×/array 1.33× 超 net48；conformance 审计 4 份
- **关键 P0 全修复**：UTF-8 多字节/数组越界异常化/frame 配对/ResultKind int64/throw null NRE/INT_MIN 折叠/gc 8 字节块/sblong GC 移动/internchain Concat 折叠/ldelem 宽度/box 描述符/conv.u8/round-half-to-even

## 当前数字（2026-08-06）

- unit **1047/1073**（26 失败：1 本域回归 EHRegionTests.Emit_CatchThenFinally（CodeGenerator finally 尾部 `;` 打破形状正则，1 行级修复）+ 25 在途域）
- runtime **106/106 全绿**（fullcheck5；test_marshal 签名已修待重跑确认）
- e2e **80/106（75.5%）**（趋势 47→72→80；matrix6 待跑——大量修复后预计 90+，**跑前必须重建 il2cppc.dll + 共享 runtime 库**）
- 三编译器全绿（MSVC 残留 C4996×8 非新域）

## 未完成（按序做）

**P0**：
1. 树健康：dotnet build（0 错 0 警）+ dotnet test（先修 EHRegion CatchThenFinally 回归）+ cmake runtime 全量（test_marshal 已修应全绿）
2. 重建共享 runtime 库 → e2e 矩阵6 全量（逐用例 转译→构建→运行→diff，产出 docs/audit/e2e-full-matrix6.md）
3. 提交批次 15（工作树大量成果：internchain 修复/gc kMinBlockSize/sblong 根槽化/weakref/typechain/xmlcase/aescase/switch 折叠/decimal result-first/CodeGenerator 三件（lockcase+cctorchain+recurse entry）等——按在途判定提交）

**P1 施工单**（详情在 docs/plans/e2e-sprint-worklist.md）：
- #44 HAS_THIS bit（0x20）static/instance 分类（MethodRefResolver.cs:337——delegate2+enumdeep 一修双卡，**影响面大需全量回归**）
- #42 delegateinst 2-arg 发射（StackFold.cs:1847 提取 target+Expr.cs Target 槽+CodeGenerator 2-arg）
- StreamIO 对象化（streamcase 验收 20/20：icall_stream_file_open(Il2CppObject* self,...)+handle 注册表+BclAllocExpr+System_IO_*_class）
- TypeInfo 表发射（R-REFL-01，reflectcase GAP-REFL-1）
- netcase ip4 槽（CodeGenerator kind 列表 3 处+ipaddr.h include）
- webcase #43（dispatch WebClient/Socket）
- pinvokeext M4.5 IntPtr 返回全宽
- decimalcase 收尾（发射 3 缺陷：ctor_5 void/ loc1+sf_One/Zero 未发射/ parse 参数序）
- Encoding GetChars 分配形 / MethodInfo.Invoke 端到端 / LINQ ToList / 多维 0x14 解码（已派）/ Split newarr char C4

**P2**：逃逸分析（object 0.02/string 0.34 bench）/Apple/OpenBSD 验证/rename 层 1-4/反射模式 A+C/e2e 用例继续扩量（137 已建）/C4996 清零/bench v3

## 环境备忘

- 转译器：dotnet build src/il2cppc/il2cppc.csproj -c Release；unit：dotnet test tests/unit/unit.csproj
- runtime：cmake -S tests/runtime -B build/<唯一名> -G "MinGW Makefiles"（GLOB 自动纳入新文件）
- e2e 单用例：pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>
- 工具链：MinGW=clang17、MSVC=vswhere VS2022、WSL= C:\Program Files\WSL\wsl.exe（Ubuntu1804 GCC7.5）、NDK=D:\android\sdk\ndk（禁 adb）
- net48 oracle：C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
- 探针隔离：%TEMP%\opencode\；g++ 独立编译（-std=c++17 -Wall -Wextra -fexceptions -fno-rtti）
- UTF-8 字节比较用 cmd /c 重定向（pwsh *> 会 GBK 重编码）
- 在途判定：文件 mtime<15min 等 20min；e2e 前验 il2cppc.dll 新于全部源

## 恢复后 5 步

1. git status + git log -5（确认 HEAD=f36f386）
2. 树健康（build+test+cmake）→ 修 EHRegion 回归
3. 重建共享库 → e2e 矩阵6
4. 派发批次 20+（40-100 并行）：P0/P1 施工单 + 新域 + e2e 扩量
5. 每批提交（批次 15 先补）
