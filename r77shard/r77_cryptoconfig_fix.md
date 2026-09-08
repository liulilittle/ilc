# r77 CryptoConfig cannot-cast fix

## Errors in r77 shard3 (54 total, 18 cannot-cast from int32_t to Il2CppObject*)

Root cause: int32 locals compared to nullptr / passed to il2Cpp_is_instance

## Fix 1: null comparison (CodeGenerator.cs ~3269-3340)
- When non-null operand is int (locKind "val"/"i"/int32*), cast both sides to intptr_t

## Fix 2: isinst/castclass (CodeGenerator.cs ~6180-6192)
- When obj operand is int, bridge via intptr_t: reinterpret_cast<Il2CppObject*>(static_cast<intptr_t>(obj))

## Build result: 0 errors, 5 warnings (pre-existing)
