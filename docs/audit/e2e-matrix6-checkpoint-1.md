# e2e Matrix6 Batch1 Checkpoint 1 — 用例 1-40（按字母序）阶段结果

日期：2026-08-06（矩阵6 批次1 执行中）
环境：mingw；共享 runtime 库 build/runtime_e2e_shared/libil2cpp_runtime.a 已确认当前（14:20:48 > 最新源 13:07:18），`cmake --build` 无重编译（= 已含最新修复）。
方法：每用例 `pwsh scripts/run-tests.ps1 -Compiler mingw -Case <名>` 逐用例跑；失败阶段按脚本异常消息/重跑输出归类。
格式：用例|结果|现象

## 用例 1-15
```
aescase|FAIL@build|generated.h 缺 crypto_obj.h include → icall_aes_create/set_key/set_iv 等 undeclared（Cases_AesCase.cpp:44+）
arith|PASS|
arraybounds|PASS|
arraydeep|PASS|
arrayext|PASS|
arrayext2|FAIL@transpile|System.Array::FindIndex not wired: runtime icall 取 (array,startIndex,count)，.NET Predicate 重载不提供
arrays|PASS|
arrcomp|PASS|
arrdeep|PASS|
base64case|PASS|
bitconvcase|PASS|
bitopcase|FAIL@diff|114 行输出中 ~10 处 False（位运算域语义）
boxdeep|PASS|
boxdeep2|FAIL@build|System_Object_ToString_a21e6f611551b28e undeclared + System_UInt32_class undeclared（did you mean System_Int32_class?）
boxext|PASS|
```

## 用例 16-30
```
boxext2|PASS|
boxing|PASS|
boxing2|PASS|
boxstr|PASS|
boxunbox|PASS|
branch|PASS|
cctor|PASS|
cctorchain|PASS|
charstr|PASS|
collchain|FAIL@transpile|ArrayList::Remove has no runtime icall（arraylist.h 仅 remove_at by index）→ StackFold.cs:410
concatcase|PASS|
consolecase|PASS|
consoleio|PASS|
convcase|PASS|
convdeep|PASS|
```

## 用例 31-40
```
convertcase|PASS|
crossstat|PASS|
cryptocase|FAIL@build|generated.h 缺 crypto_obj.h → icall_md5_create/sha1/sha256 等 undeclared（Cases_CryptoCase.cpp:40+）
culturecase|PASS|
datetime2|FAIL@diff|5 处 False（日期运算域）
datetimechain|PASS|
decimalcase|FAIL@build|System_Decimal_sf_One/Zero undeclared + ctor arity 6 expected 5（Cases_DecimalCase.cpp:68-74）
delecb2|FAIL@transpile|返回类型 GenericTypeInstance 不受 M3 支持（CodeGenerator.cs:3248 ReturnTypeCpp）
delechain|FAIL@build|int32↔Il2CppObject* 错配（Cases_Delechain.cpp:137-138,160-161,189）
delegate2|FAIL@build|System_Delegate_get_Method/MemberInfo_get_Name undeclared + expected expression（Cases_Delegate2.cpp:179-180,290）
```

## 累计
- 通过：29/40（PASS）
- 失败：11（build 6：aescase/boxdeep2/cryptocase/decimalcase/delechain/delegate2；transpile 3：arrayext2/collchain/delecb2；diff 2：bitopcase/datetime2）
- 与 matrix5 交集对比：matrix5 中这批 40 例 FAIL 为 aescase/arrayext2/bitopcase/boxdeep2/collchain/cryptocase(PASS当时)/culturecase(build当时)/datetime2/decimalcase/delechain/delegate2；本批 culturecase 翻 PASS、cryptocase 从 PASS 回落到 build FAIL（generated.h crypto_obj include 丢失）。
