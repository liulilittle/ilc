# r20 AnonymousType遗留修复（20/20 PASS）

> 状态：已完成，W3备份已落盘，g++ 20/20 PASS
> 父会话：ses_faec50249ffeP9Xm8rXxPL2fiP / 子会话：ses_f9ddf2724ffeiOokWoachM8R6x
> 范围：out/mscorlib_gen_r18/src/__f__AnonymousType0_1_int.cpp + __f__AnonymousType0_1_string.cpp（ToString方法中的类型不匹配）
> 产物前缀：`l2rem_` / 路径：`r17shard\`

## 1 修复

- **根因**：匿名类型`ToString`方法中`loc0/t1/t8`被声明为`int32_t`，但实际存储`System.String`引用（`Il2CppObject*`）；`icall_object_to_string(t8)`传入`int32_t*`导致类型不匹配
- **int文件** [执行:g++ __f__AnonymousType0_1_int.cpp:204]：`t0 = reinterpret_cast<Il2CppObject*>(icall_object_to_string(t8))` 中`t8`为`int32_t*`，修复为`t2`（boxed object）[读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_int.cpp:204]
- **string文件** [执行:g++ __f__AnonymousType0_1_string.cpp:182/200]：
  - 变量类型：`int32_t loc0` -> `Il2CppObject* loc0`，`int32_t t1` -> `Il2CppObject* t1`，`int32_t* t8` -> `Il2CppObject* t8` [读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:156/157/164]
  - 赋值：`t1 = loc0`（ref copy），`t2 = t1`（identity，reference无需box），`t8 = loc0`（ref, not addr）[读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:183-185/190]
  - frame-root：`loc0/t1/t8`加入`__frameRefs` [读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:174]
  - icall：`icall_object_to_string(t8)` -> `icall_object_to_string(t2)`（boxed ref）[读:out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:200]

## 2 备份

- W3 [执行:Copy-Item]：
  - `.opencode/backups/__f__AnonymousType0_1_int.cpp.20260905_180940_r20fix` 18070B
  - `.opencode/backups/__f__AnonymousType0_1_string.cpp.20260905_180940_r20fix` 18129B

## 3 验证

- g++ 20/20 PASS：首20文件抽样（含原FAIL两文件）均`PASS 0` [执行:g++ -fsyntax-only 20/20 PASS]
- 具体：
  - `__f__AnonymousType0_1_int.cpp` EXIT 0
  - `__f__AnonymousType0_1_string.cpp` EXIT 0

## 4 证据

- 读：`out/mscorlib_gen_r18/src/__f__AnonymousType0_1_int.cpp:204` `icall_object_to_string(t2)`
- 读：`out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:156-171` `Il2CppObject* loc0/t1/t8`
- 读：`out/mscorlib_gen_r18/src/__f__AnonymousType0_1_string.cpp:200` `icall_object_to_string(t2)`
- 执行：`g++ -fsyntax-only 20/20 PASS`
