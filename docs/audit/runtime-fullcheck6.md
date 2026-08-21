# runtime 全量复验 6 —— 构建局部阻断 + 111/111 PASS（crypto_obj 修复后）

- 时间：2026-08-06 09:28-09:58
- 命令：`cmake -S tests/runtime -B build/runtime_fullcheck6 -G "MinGW Makefiles"` + `cmake --build`（+ `make -k` 全枚举）+ 逐个运行全部已构建 `test_*.exe`
- 结果：**构建 ❌（1/112 目标阻断）+ 已构建 111 个 exe 全部 PASS（111/111）**
- 仅报告，不修复。

## 一、构建状态

| 项目 | 值 |
|---|---|
| cmake configure | ✅ 成功 |
| cmake --build | ❌ exit=2（test_marshal 编译失败） |
| 测试源文件（test_*.cpp） | 112 |
| 构建成功目标 | 111（`make -k` 全枚举证实） |
| 构建失败目标 | 1（test_marshal） |
| 共享 runtime 库（il2cpp_runtime） | ✅ 构建通过（stringbuilder.cpp 在飞重构完成后） |
| 测试运行 | 111/111 已构建 exe 全 PASS |

## 二、构建错误清单（3 处，全在 test_marshal.cpp）

```
test_marshal.cpp:47:11: error: cannot initialize a variable of type 'void *' with an
    rvalue of type 'intptr_t' (aka 'long long')
test_marshal.cpp:58:9:  error: cannot initialize a parameter of type 'Il2CppArray *' with
    an rvalue of type 'il2cpp::Il2CppObject *'
test_marshal.cpp:66:39: error: cannot initialize a parameter of type 'Il2CppArray *' with
    an rvalue of type 'il2cpp::Il2CppObject *'
```

| 行 | 测试调用（旧签名） | 头契约 il2cpp_runtime.h |
|---|---|---|
| :47 | `void* buf = icall_marshal_alloc_hglobal(32)` | :136 `intptr_t ...(int32_t)` |
| :58 | `reinterpret_cast<Il2CppObject*>(arr)` | :138 `Il2CppArray* array` |
| :66 | `reinterpret_cast<Il2CppObject*>(back)` | :139 `Il2CppArray* array` |

## 三、mtime 归因 —— 签名已落地，test 文件陈旧

| 文件 | 状态 | mtime | 说明 |
|---|---|---|---|
| `src/runtime/src/runtime.cpp` | committed `2fca145` | 2026-08-06 06:48:54 | **签名已随 batch-14 提交**（void*→intptr_t、Il2CppObject*→Il2CppArray*），不再在途 |
| `src/runtime/src/crypto_obj.cpp` | committed `2fca145` | 2026-08-06 07:03:20 | 本批修复对象，编译通过 |
| `src/runtime/include/internal/il2cpp_runtime.h` | committed 2659d8c/2fca145 | 2026-08-05 23:50:46 | 契约 :136-139 = intptr_t / Il2CppArray* |
| `tests/runtime/test_marshal.cpp` | committed f6b92be（M4） | 2026-08-03 12:15:49 | **陈旧**：M4 遗留，从未按新契约更新，仍用旧签名（void* / Il2CppObject*） |

**结论**：任务提示的"marshal 域 runtime.cpp 签名若在途"已**不成立**——签名在 `2fca145` 落地。
当前唯一构建阻断是 M4 遗留 `test_marshal.cpp` 未随头契约（2659d8c→2fca145）更新，与已提交的
runtime 库及头契约不一致。同域新测试 `test_marshal2`/`test_marshalext` 已按新契约编写并 PASS。

**回归性质**：fullcheck4/5 均产出 test_marshal.exe，本次为新阻断；根因 = 测试文件陈旧，非 runtime 侧。

## 四、在途观察（transient，非稳定失败）

- `stringbuilder.cpp`（mtime 09:39:36，代理重构 `void**`→`Il2CppObject**` GC-root 槽类型）：
  首构建 13 错、二次 12 错（声明已改、调用未跟）→ 完成后共享库编译通过。**属在飞编辑，非本验证失败。**

## 五、运行结果表（111/111 PASS）

```
PASS  test_aes        PASS  test_allocator   PASS  test_array       PASS  test_array_extra
PASS  test_arraybounds PASS test_arrayext     PASS  test_arrayext2   PASS  test_arraylist
PASS  test_arraysort   PASS  test_attribute   PASS  test_bench_allocator PASS test_bench_inline
PASS  test_binaryio    PASS  test_bitconverter PASS test_boxedtostring PASS test_boxingext
PASS  test_boxingext2  PASS  test_buffer      PASS  test_concat      PASS  test_console
PASS  test_conv        PASS  test_convert     PASS  test_convertbase64 PASS test_crypto_obj
PASS  test_cultureinfo PASS  test_debugext    PASS  test_decimal     PASS  test_decimal_format
PASS  test_delegate_cb PASS  test_delegateext PASS  test_dictsi      PASS  test_encoding
PASS  test_enumext     PASS  test_environment PASS  test_exception   PASS  test_exceptionext
PASS  test_format      PASS  test_formatext   PASS  test_frame_deep  PASS  test_gc
PASS  test_gcapi       PASS  test_gcext       PASS  test_gcstress    PASS  test_guid
PASS  test_hashtable   PASS  test_hmac        PASS  test_http        PASS  test_interlocked
PASS  test_intptr      PASS  test_invoker     PASS  test_io          PASS  test_ipaddr
PASS  test_linq        PASS  test_linq_map    PASS  test_linq_sort   PASS  test_listi4
PASS  test_marshal2    PASS  test_marshalext  PASS  test_math        PASS  test_math_extra
PASS  test_mathext     PASS  test_md5         PASS  test_monitor     PASS  test_numberformat
PASS  test_object      PASS  test_parse       PASS  test_pathproc    PASS  test_pinvoke
PASS  test_pinvokeext  PASS  test_platform    PASS  test_random      PASS  test_reflect_base
PASS  test_regex_api   PASS  test_regex_match PASS  test_rng         PASS  test_sha1
PASS  test_sha256      PASS  test_sha512      PASS  test_socket      PASS  test_sortedlist
PASS  test_stackqueue  PASS  test_stopwatch   PASS  test_streamio    PASS  test_string
PASS  test_string_utf8 PASS  test_stringbuilder PASS test_stringext  PASS  test_stringext2
PASS  test_stringext3  PASS  test_stringhash  PASS  test_stringintern PASS test_stringrw
PASS  test_stringrwobj PASS  test_thread      PASS  test_threadext   PASS  test_threadobj
PASS  test_threadpool  PASS  test_time        PASS  test_timeext     PASS  test_timeext2
PASS  test_timer       PASS  test_timezone    PASS  test_typecheck   PASS  test_typeext
PASS  test_typeinfo    PASS  test_valuesemantics PASS test_versionobj PASS test_versionuri
PASS  test_xml_doc     PASS  test_xml_parse   PASS  test_xml_query
```

## 六、汇总

- **构建**：❌（`test_marshal` 阻断，3 错；共享库及其余 111 目标全过）
- **测试**：**111/111 PASS**（0 FAIL，0 TIMEOUT，全部已构建 exe 逐一两遍验证 exit=0）
- **失败归因**：`test_marshal.cpp`（M4 遗留，08-03 12:15，committed f6b92be）未随头契约更新；
  runtime.cpp/crypto_obj.cpp 签名变更已随 `2fca145` 提交（非在途）。
- **唯一剩余动作**：更新 `test_marshal.cpp:47/58/66` 至新契约后重跑该目标即全绿（不修）。

## 七、结论

crypto_obj 修复（类描述符 GLOBAL 作用域）验证通过：`test_crypto_obj`/`test_hmac`/`test_sha1`/
`test_sha256`/`test_sha512`/`test_aes`/`test_rng`/`test_md5` 全部 PASS。runtime 全量 111 个可构建
测试全部通过；唯一构建阻断为 M4 陈旧测试文件，属测试侧遗留，非 runtime 回归。
