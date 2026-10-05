# GAPS.md — marshalcase unwired API surface

This case exercises the *wired* Marshal surface (AllocHGlobal / FreeHGlobal /
Copy / GetLastWin32Error / PtrToStringAnsi). It transpiles but the generated
C++ **does not compile** (stages: transpile PASS, cmake_build FAIL), all from
the documented M4.5 pointer-carrier work item plus two sibling wiring gaps.
The net48 reference output (5x `True`) is correct (`expected.txt`); the gaps
are all emitter/runtime-side, not test-side.

| # | Gap | Blocking evidence |
|---|-----|-------------------|
| 1 | `IntPtr::op_Inequality` — no `MethodMap.IntPtr.cs` mapping at all | `p != IntPtr.Zero` → transpiler error `resolved System_IntPtr_op_Inequality_… as INSTANCE (pops this) but the stack is empty` (StackFold.cs:1486). Test uses the wired `ToInt64()` form instead. |
| 2 | `IntPtr::op_Equality` (and Add/Subtract/CompareTo) guard accepts only ps `0x11` (VALUETYPE) | Real mscorlib MemberRef blobs carry IntPtr params as raw native-int `0x17`/`0x18` (same note as `MethodMap.Marshal.cs:50-58`); `TryMapIntPtr` returns null → fallback INSTANCE misclassification → same "pops this" error. `IsIntPtrByte` (Marshal.cs:150) exists but IntPtr.cs does not use it. |
| 3 | Pointer-carrier: `IntPtr` return-to-slot and slot-to-pointer-param both emit with no cast | `gen/src/Cases_MarshalCase.cpp:37/66` `loc0 = il2cpp::runtime::icall_marshal_alloc_hglobal(…);` — `void*` → `int32_t` (return direction); `:46/56/71/73` `icall_marshal_copy_from_unmanaged(loc0, …)` / `free_hglobal(loc0)` / `string_from_utf8(loc3)` — `int32_t` → `void*`/`const char*`. MethodMap.Marshal.cs:33-43 documents only the param direction; the return direction is equally broken. |
| 4 | Copy icalls array param: runtime declares `Il2CppObject*`, emitter emits `(Il2CppArray*)` cast | `il2cpp_runtime.h:129-130` `icall_marshal_copy_to/from_unmanaged(Il2CppObject* array, …)` vs `Cases_MarshalCase.cpp:44/70` `(Il2CppArray*)(t2)` — unrelated structs in this runtime, no implicit conversion, compile error. |

## Re-enable plan

Re-enable each probe in `marshalcase.cs` once the corresponding gap closes:

1. Restore `Console.WriteLine(!(p == IntPtr.Zero));` when #1+#2 land (fix
   `MethodMap.IntPtr.cs` guards to `IsIntPtrByte` + add op_Inequality mapping).
2. The round-trip / FreeHGlobal / PtrToStringAnsi probes already exist — they
   become buildable once #3 (full-width pointer model, M4.5
   `PinvokeReturnTypeCpp`) and #4 (align runtime decl or emitter cast) land.

## Verification context (2026-08-05 ~18:2x)

- Transpile used a **temp copy** of `src/il2cppc`
  (`%TEMP%\opencode\il2cppc-t4`) because the repo tree was mid-refactor
  (in-flight T3 string-pool work: CS0161/CS1501/CS0152 appeared and were
  agent-fixed during this task; the copy was re-synced to the fixed state).
- Build used the matrix4 shared-runtime wrapper against
  `build/runtime_e2e_shared/libil2cpp_runtime.a` (13:22:40) — all 7 required
  symbols verified present via `nm`. A full tree build was impossible because
  the runtime's `crypto_obj.cpp` is mid-refactor (namespace breakage,
  `crypto_obj.h:99` `Il2CppMd5Context` — in-flight agent work, tree-wide,
  unrelated to marshalcase).
