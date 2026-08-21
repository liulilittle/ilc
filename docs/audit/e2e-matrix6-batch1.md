# e2e Matrix6 批次 1 报告 — tests/cases 字母序用例 1-40

日期：2026-08-06
分支：m1
编译器：mingw
共享 runtime 库：`build/runtime_e2e_shared/libil2cpp_runtime.a` 已确认当前（archive 14:20:48 > 最新源 src/runtime 13:07:18，`cmake --build` 返回 no-op = 已含最新 gc/stringbuilder/encoding/io/decimal/linq_map 修复）。
方法：40 用例逐用例 `pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>`；失败阶段按脚本异常 / 重跑捕获归类。

## 结果摘要

- 总数：40
- **PASS：29（72.5%）**
- FAIL：11
  - FAIL@build：6（aescase / boxdeep2 / cryptocase / decimalcase / delechain / delegate2）
  - FAIL@transpile：3（arrayext2 / collchain / delecb2）
  - FAIL@diff：2（bitopcase / datetime2）

Checkpoint：`docs/audit/e2e-matrix6-checkpoint-1.md`

## 失败现象摘要（一句话）

| 用例 | 阶段 | 现象 |
|---|---|---|
| aescase | build | generated.h 缺 crypto_obj.h → icall_aes_create/set_key/set_iv 等 undeclared（Cases_AesCase.cpp:44+） |
| arrayext2 | transpile | Array.FindIndex not wired：runtime icall 取 (array,startIndex,count)，.NET Predicate 重载不提供 |
| bitopcase | diff | 114 行输出中 ~10 处 False（位运算域语义） |
| boxdeep2 | build | System_Object_ToString_a21e6f611551b28e undeclared + System_UInt32_class undeclared |
| collchain | transpile | ArrayList::Remove 无 runtime icall（仅 remove_at by index）→ StackFold.cs:410 |
| cryptocase | build | generated.h 缺 crypto_obj.h → icall_md5_create/sha1/sha256 undeclared（matrix5 曾 PASS，回归） |
| datetime2 | diff | 5 处 False（日期运算域） |
| decimalcase | build | System_Decimal_sf_One/Zero undeclared + ctor arity 6 expected 5 |
| delecb2 | transpile | 返回类型 GenericTypeInstance 不受 M3 支持（CodeGenerator.cs:3248） |
| delechain | build | int32↔Il2CppObject* 错配（Cases_Delechain.cpp:137-138,160-161,189） |
| delegate2 | build | System_Delegate_get_Method/MemberInfo_get_Name undeclared + expected expression |

## 与 matrix5 对比（交集 40 例）

- 新增翻绿 2：culturecase（matrix5 build FAIL → PASS）、cctorchain（matrix5 diff → PASS）
- 新回归 1：cryptocase（matrix5 PASS → 本批 build FAIL）。根因：generated.h include 列表当前不含 crypto_obj.h（`git log -S crypto_obj.h -- CodeGenerator.cs` 无提交，工作树 CodeGenerator.cs:161-167 仅 md5/sha1/sha256 三个 include），而 crypto 对象层 icall（icall_md5_create 等）声明在 crypto_obj.h:199，生成码调用 `il2cpp::runtime::icall_md5_create`；crypto_obj.h:87 声明位于 `namespace il2cpp`，与生成码 `il2cpp::runtime::` 限定命名空间不一致（crypto_obj.cpp:318 定义在 `il2cpp::runtime`）。需产品侧核对 header 命名空间契约与 generated.h include。
- 其余 8 FAIL 与 matrix5 同因（aescase/arrayext2/bitopcase/boxdeep2/collchain/datetime2/decimalcase/delechain/delegate2），delecb2 为新增用例首测。

## 附注

- `.inflight`：run-tests.ps1 不创建 .inflight；tests/runtime/test_crypto_obj.cpp.inflight 为执行前已存在的 untracked 遗留文件，未动。
- 产品代码/用例未改动；未 commit。
