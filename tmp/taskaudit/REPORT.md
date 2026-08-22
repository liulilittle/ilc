# Task<T> 剩余泛型实例化审计报告

- 日期：2026-08-15
- HEAD：efa6741（batch 164-168 之后）
- 审计范围：`Task<T>` 转译器 Carrier() 选择器（src/il2cppc/Emit/MethodMap.Task.cs）在剩余泛型实例化上的行为
- 方法：net48 运行（UTF-8 捕获）→ il2cppc 转译 → cmake(MinGW) 构建 → 运行 gen exe → Compare-Object / -ceq 字节对比
- 铁律遵守：仅创建 E:\il2cpp\tmp\taskaudit\ 下探针；未动 src/、tests/cases/、build/runtime_full

## 探针结果一览

| 探针 | 内容 | 转译 | net48 vs gen | 载体后缀 | 异常 |
|---|---|---|---|---|---|
| A | Task<byte>/sbyte/short/ushort/uint（async + await FromResult） | PASS (exit 0) | IDENTICAL（10 行字节全等） | 无后缀 int32（from_result / get_result_original / set_result / get_result 全无后缀） | 无 |
| B | Task<decimal>/Task<DateTime>（async + await FromResult，Main 访问 .Result） | LOUD-REJECT (exit 2) | —（未生成） | — | NotSupportedException（VALUETYPE gate，get_Result 触发） |
| B' | 同 B 但 Main 仅调用方法不访问 .Result | LOUD-REJECT (exit 2) | —（未生成） | — | NotSupportedException（VALUETYPE gate，FromResult 触发，instName 空但 genArgs==0x11 兜底） |
| C | Task<string[]>（SZARRAY of string） | PASS (exit 0) | IDENTICAL（3 行字节全等） | _s（from_result_s / get_result_original_s / set_result_s / get_result_s） | 无 |
| D | Task<Cases.TaskAudit.Point>（自定义 class） | PASS (exit 0) | IDENTICAL（2 行字节全等） | _s（4 个调用全带 _s） | 无 |

## 探针 B 精确错误消息（LOUD 判定依据）

场景 1（Main 访问 .Result，触发点 Task`1::get_Result）：
```
il2cppc error: System.Threading.Tasks.Task`1::get_Result: Task<T> carrier for VALUETYPE T (instName 'System.Threading.Tasks.Task`1_valuetype') is not built in M3 -- the 16-byte struct carriers (decimal/DateTime/Guid) are out of scope; only the primitive/ref carriers (int/long/double/float/string/object/ulong/bool/char/class/array) exist
```
（堆栈：MethodRefResolver.BclResolve -> ResolveMemberReference -> StackFold.Fold -> CodeGenerator.GenerateCore，exit 2）

场景 2（仅 async 体内 Task.FromResult，触发点 Task::FromResult）：
```
il2cppc error: System.Threading.Tasks.Task::FromResult: Task<T> carrier for VALUETYPE T (instName '') is not built in M3 -- the 16-byte struct carriers (decimal/DateTime/Guid) are out of scope; only the primitive/ref carriers (int/long/double/float/string/object/ulong/bool/char/class/array) exist
```
（注意 instName 为空 —— 此路径由 genArgs[0]==0x11（VALUETYPE 0x11）通道兜底检测，MethodMap.Task.cs:64-65；get_Result 路径则由 instName "_valuetype" 后缀检测，line 64。双通道均验证 LOUD。）

## 生成 C++ 载体明细

### 探针 A（int32 家族，MethodMap.Task.cs Carrier() 返回 ("","")）
- 状态机内：`icall_task_from_result(3LL)`、`icall_task_get_result_original(...)`、`icall_async_builder_set_result(...)` —— 全部无后缀
- Main 内：`icall_task_get_result(t0)`（无后缀）；打印 byte/sbyte/short/ushort 走 `icall_console_write_line_i`，uint 走 `icall_console_write_line_u32`
- GetType().Name 的 box：`icall_box_byte` / `icall_box_sbyte` / `icall_box_short` / `icall_box_ushort` / `icall_box_uint` —— 类型精确，net48 输出 Byte/SByte/Int16/UInt16/UInt32 逐一一致
- 无截断风险证据：byte 1+2=3、sbyte 1+2=3 等全部数值一致

### 探针 C（SZARRAY，_s 载体）
- `icall_task_from_result_s(t0)`、`icall_task_get_result_original_s(...)`、`icall_async_builder_set_result_s(...)`、Main 内 `icall_task_get_result_s(t0)`
- `newarr string[]` → `il2cpp_array_new(&System_String_class, 8, true, 2)`；elemSize=8 为 runtime 64 位 slot 统一约定（array.cpp ArrayAllocSize，与 ldelem/GC 引用扫描一致；探针 C 运行验证 Length=2 / a[0]="x" / GetType().Name="String[]" 全部与 net48 一致）

### 探针 D（自定义 class，_s 载体）
- `icall_task_from_result_s(t0)`、`icall_task_get_result_original_s(...)`、`icall_async_builder_set_result_s(...)`、Main 内 `icall_task_get_result_s(t0)`
- newobj → `il2cpp_object_new(&Cases_TaskAudit_Point_class)` + 内联字段赋值（X=3、Y=4），ctor 走 `il2cpp_object_ctor_nop`
- Main 输出 p.X=3、GetType().Name="Point" 与 net48 一致

## 结论

1. **静默错误：零。** 本审计覆盖的 9 种实例化（byte/sbyte/short/ushort/uint/string[]/自定义 class + decimal/DateTime 对照）中，全部可转译实例化均与 net48 字节级一致；无任何"转译成功但行为错误"的情况。
2. **LOUD 正确：** VALUETYPE（decimal/DateTime）在所有 result-bearing 路径（get_Result / FromResult，SetResult 同理共享 Carrier() gate）均 LOUD NotSupportedException + exit 2，错误消息精确指明 instName 与 scope；双检测通道（instName 后缀 / genArgs==0x11）均生效。
3. **需要修：无**（本审计范围内）。int32 家族、SZARRAY、自定义 class 载体行为全部正确，与 batch 164-168 已记录的 11 种载体矩阵（int/long/double/float/string/object/ulong/bool/char/class/array）互补无冲突。
4. 观察项（非缺陷）：FromResult 场景下 instName 为空（依赖 genArgs 通道），若未来引入非 1-arg 泛型签名需注意；`il2cpp_array_new` elemSize=8 是 runtime 既有约定，与 Task 载体无关。
