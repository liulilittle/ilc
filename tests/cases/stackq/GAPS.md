# GAPS.md — stackq blocked: 2 transpiler defects (2026-08-06)

The case was built as a NEW-DOMAIN probe (System.Collections.Stack / Queue,
non-generic object collections) and FAILS at the cmake build stage. Both
defects are VERIFIED against the generated code; neither is fixed here (batch
B rule: record defects, do not touch the transpiler).

## D-STK-1 — stackqueue.h is missing from the generated.h include chain

- **Phenomenon**: cmake build errors
  `no member named 'il2cpp_stack_create' in namespace 'il2cpp'; did you mean
  'il2cpp_list_i4_create'?` (and il2cpp_stack_push/pop/peek/get_count/clear +
  the whole queue family).
- **Causal chain**: the runtime DECLARES the stackqueue icalls in
  `src/runtime/include/internal/stackqueue.h` (plain `il2cpp::` namespace,
  verified by read), but the transpiler's generated.h include list
  (`CodeGenerator.cs` generated.h emission) contains listi4.h / dictsi.h /
  il2cpp_hashtable.h and NOT stackqueue.h -- the generated .cpp never sees the
  declarations. Same defect family as the reflect_base.h include gap (fixed
  in matrix5 for crypto/reflect).
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` (generated.h
  `#include` list) -- add `"stackqueue.h"`.

## D-STK-2 — create-style ctor newobj emits an undeclared class descriptor

- **Phenomenon**: cmake build error
  `use of undeclared identifier 'System_Collections_Stack_class'`; the
  generated code is
  `t0 = il2cpp_object_new(&System_Collections_Stack_class); ...;
  il2cpp::il2cpp_stack_create();` -- TWO object creations for one `new Stack()`.
- **Causal chain**: the Stack/Queue `.ctor` mapping is a CREATE-style icall
  (`il2cpp_stack_create()` returns the new object; the newobj temp is passed
  as the first arg and DROPPED -- MethodMap.Stack.cs comment). But the
  newobj fold STILL emits the fallback `il2cpp_object_new(&{TypeCppName}_class)`
  for the temp slot (CodeGenerator.cs:1311 fallback), and `{TypeCppName}_class`
  for Stack is `System_Collections_Stack_class` -- which the runtime does NOT
  declare (the descriptor lives behind the `il2cpp_stack_class()` accessor,
  stackqueue.cpp:265). The temp is dead (the create icall's return is used),
  so the object_new emission is both wrong and uncompilable.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` (newobj
  fallback ~:1311) -- for a ctor mapped to a CREATE-style icall the temp
  allocation must be skipped (or emit `il2cpp_object_new(il2cpp_stack_class())`).

## Notes

- The runtime surface itself is complete (stackqueue.h: il2cpp_stack_create/
  push/pop/peek/get_count/clear + il2cpp_queue_create/enqueue/dequeue/peek/
  get_count/clear). Once D-STK-1/D-STK-2 land, this case (24 net48 probes:
  LIFO/FIFO order, Peek-no-remove, mixed int/string/bool elements, Clear)
  should pass unchanged.
