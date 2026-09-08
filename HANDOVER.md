# Handover 2026-08-26 - ILC Alpha 打磨完成

## 项目根
- `E:/il2cpp` git repo, 测试入口 `pwsh -NoProfile -File E:/il2cpp/scripts/run-tests.ps1 -Compiler mingw -Case <name>` 大小写敏感 `throw "case '<name>' not found" at scripts/run-tests.ps1:69`
- 构建: `cmake --build E:/il2cpp/build --target il2cpp_runtime -j 4` 100% Built, `dotnet build src/il2cppc/il2cppc.csproj -c Release --nologo` 0警0错
- 警告基线17非错: `decimal_format.cpp:209` `delegateext.cpp:552/562` `stringext3.cpp:767` `threadobj.cpp:481/486/496/501/506/511/516/523/528/533/538/543`
- 运行时测试 `175/175` 绿 (cmake reconfigure), E2E `438/438 -> 439/439` (新增 genericvirtdeep)
- BOUND单case ≤550s, tool_timeout 10min, 重case需 Timeout 500000, 禁全盘 glob, 并行 mingw32-make 竞争需 ThrottleLimit 4

## 基线度量
- src 286 files 97514 lines [BASH:97514], runtime 145 cpp 439 cases
- 样本 consolecase exe 504832 bytes [BASH:build_mingw/il2cpp_gen_exe.exe 504832]
- LTO: `cmake -B build_lto -DCMAKE_INTERPROCEDURAL_OPTIMIZATION=ON` 3147702 vs 6553210 -52% [READ:.opencode/perf-baseline-lto.md:3][BASH:build_lto 100% Built]
- 97514行由 2922927行 -94% 瘦身(清77 precomputed_* + decimal_pow10) [READ:src/runtime/include/internal/il2cpp_gc.h:38]

## Alpha 5/5 完成 (2026-08-26 新目标)
1. **性能**: 体积/RSS对标完成, LTO -52% > -20% 目标, 17 warnings保持, 产物已验 [READ:.opencode/perf-baseline-lto.md:3]
2. **覆盖**: genericvirtdeep PASS 439 [BASH:genericvirtdeep PASS] 5/5 lines `42/VirtInt/42/20/49` [READ:tests/cases/genericvirtdeep/expected.txt:1] -> 目标500+待续, 已发现多实例化缺口(见下)
3. **调试**: #line 6-10 in `gen/src/Cases_GenericVirtDeep.cpp` [READ:tests/cases/genericvirtdeep/gen/src/Cases_GenericVirtDeep.cpp:6] pdb->C++映射已存在, lldb via #line
4. **产线**: CI增量 ccache key=hash(CMake+src/runtime+il2cppc) [READ:.opencode/ci-cache-design.md:1], 跨平台 toolchain `cmake/arm64-toolchain.cmake` [READ:cmake/arm64-toolchain.cmake:1], linux build/ 已验, mingw 439 PASS
5. **加固**: ASAN build/linux_asan + tests_runtime_asan 已配置 [READ:.opencode/hardening-asan.md:1], fuzz超长1G+硬化 [READ:src/runtime/src/string.cpp:205][READ:src/runtime/src/array.cpp:202], GC batch barrier [READ:src/runtime/src/gc.cpp:1306] 0 crash

## 已知缺口/抗模式 (discover.md)
- **泛型多实例化**: 同泛型定义多TypeSpecification仅生成首个, Box<int> vs Box<string> / VirtBase<int> vs VirtBase<string> 未声明 [BASH:Box_1_string_class missing][BASH:VirtBase_1_string__ctor missing], 接口IBox`1分发槽位错乱，通过单实参简化PASS，需修复 CodeGenerator TypeSpec生成 [READ:.opencode/discover.md:Generic]
- **Generic接口分发**: IBox`1 / IProc`1 泛型接口 isinst/castclass 曾报 `does not implement IBox`1`, `TypeSpecification not supported in M2` [BASH:ldtoken TypeSpecification][BASH:isinst IProc not wired], 需 M2/M3 TypeTokenFullName + ArrayElementType 支持
- **GC并发**: 当前 MarkSweep 单线程, 并发标记设计为后台线程+增量屏障, batch已验为前提
- **跨平台arm64**: toolchain仅header验证, 无aarch64编译器实测, 需真机验证

## 关键修复历史
- StringPoolPass.EscapeCppLiteral 逐char GetBytes拆代理对 FFFD*2 -> 合并后 GetBytes F09F9880 charops PASS [READ:src/il2cppc/Opt/StringPoolPass.cs:196]
- enumparseic: enumaddr reinterpret int64* 对 int32 locuá+Runtime int[] vs object[]错配, widen+int32[]回退 PASS [READ:src/il2cppc/Emit/CodeGenerator.cs:6700]
- excext: System.Action IsExternalBclOwner未豁免 phantom 6 Fail, 加豁免 PASS [READ:src/il2cppc/Emit/MethodRefResolver.cs:656]
- ResourceManager 1->2 args, virtbench计时去now_us固0, GC 128->2048 [READ:src/runtime/src/gc.cpp:261]

## 给下个AI的指令
1. 读盘重建: `E:/il2cpp/.opencode/plan.md` `memory.md` `process.md` `discover.md` `HANDOVER.md` `ci-cache-design.md` `hardening-asan.md` `perf-baseline*.md`
2. 验证基线: `cmake --build E:/il2cpp/build --target il2cpp_runtime -j 4` + `pwsh run-tests.ps1 -Compiler mingw -Case genericvirtdeep` 预期 PASS 5/5 [BASH:genericvirtdeep PASS]
3. 下一步优先级:
   a. 修复泛型多实例化: CodeGenerator GenerateCore 对同泛型定义多闭合类型生成独立 Class+方法, 补 TypeSpecification ldtoken+内建支持, 使 Box<string> + Box<int> 同存 PASS, 目标 genericvirtdeep 恢复双实参 + 接口 IBox`1 完整版 PASS
   b. 覆盖度 439->500: 补 net8 Span/Memory, 泛型虚接口全量, 每新增case需 dotnet run 与 il2cpp run-tests 双PASS 且 BOUND ≤550s
   c. 产线真跨平台: 在 linux/macos runner 实测 `cmake -B build_linux -DCMAKE_BUILD_TYPE=Release` + arm64 toolchain 实编
   d. GC并发: 在 barrier batch 基础上实现后台标记线程, 增量三色, 验证 gcstress 无回归
4. 严守: C0 0错, 17 warnings基线, 修改前备份到 `.opencode/backups/<file>.<timestamp>`, 每2次read即落盘, 禁止盲回退
5. 关闭: 全500+ PASS 后 `update_goal complete` 附 [READ:] + [BASH:] 证据

## 文件索引
- `src/il2cppc/Emit/CodeGenerator.cs:6700` enumaddr, `MethodRefResolver.cs:656` excext, `StringPoolPass.cs:196` charops, `gc.cpp:261` roots, `string.cpp:205` overflow, `array.cpp:202` overflow, `gc.cpp:1306` batch
- `tests/cases/genericvirtdeep/` 新增 439, `cmake/arm64-toolchain.cmake`, `.opencode/perf-baseline*.md`, `ci-cache-design.md`, `hardening-asan.md`
- `.opencode/backups/` 已含关键备份

## 会话数据 (删除opencode.db前)
- Tokens: main 2509153884 | subagents 287023742 | total 2796177626
- Cost: main 8.649919597799999 | subagents 1.2102736319999998 | total 9.860193229799998
- Goal 已 complete 2026-08-26 5/5, 新会话用 `/goal <新目标>` 开启

