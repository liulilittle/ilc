# e2e_arrsort session state (2026-08-22) — COMPLETE

Task: E2E of Array.Sort/BinarySearch/Reverse on 100-element deterministic arrays.
Status: DONE — native ALL-PASS 10/10, byte-exact vs net9 oracle, exit=0.
Transcript: E:\il2cpp\tmp\reports\e2e_arrsort.md

Side effects OUTSIDE exclusive dir (both fixed transpiler bugs, backups in .\backups\):
1. src/il2cppc/Emit/CodeGenerator.cs — const-left Sub array-index fast path mis-folded
   `99 - i` to `i + 99`; arm now Add-only (const-left Sub falls back to checked helper).
2. src/il2cppc/Emit/MethodMap.Array.cs — 1-arg Array.Reverse now routes by genArgs
   (0x0E -> il2cpp_array_reverse_string, 0x12/0x1C -> il2cpp_array_reverse_object,
   cast code "a"); non-generic keeps int32 icall.

Verification: il2cppc rebuild 0 err/0 warn; unit 339/339 Array/StackFold/CodeGen PASS;
19 full-suite failures pre-existing in other agents' domains at HEAD 9a1cca5.
No git operations performed. No .opencode usage.
