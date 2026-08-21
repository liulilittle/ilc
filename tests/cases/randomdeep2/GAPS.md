# GAPS.md -- randomdeep2 recorded findings (e2e batch C70, 2026-08-12)

Case status: net48 oracle 46-line byte-exact reference written; dual-compiler
PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## D-RND2-1 -- int[] element INCREMENT (`arr[i]++`) emits broken C++ (transpiler emission)

- `buckets[v / 100000000]++` generated an `ldelema` + `il2cpp_unbox_int32`
  pair: `t15 = reinterpret_cast<intptr_t>(...); icall_int32_to_string(
  il2cpp_unbox_int32(static_cast<Il2CppObject*>(t15)))` -- the element
  ADDRESS (intptr_t) is unboxed as an object -> "expected ';'" /
  "cannot cast from type 'intptr_t' to 'Il2CppObject*'" cmake build abort.
- WORKAROUND (this case): `int bi = ...; buckets[bi] = buckets[bi] + 1;`
  (read-through-locals + plain store) compiles and matches net48.
- Root: the ++ on a 32-bit array element folds to the ldelema address path
  instead of a load/store pair (CodeGenerator emission gap, same family as
  the recorded random2b DEFECT-RAND-1 ldlen_ fallback regression).

## D-RND2-2 -- int[] element READ feeding string concat also hits the ldelema path

- `hist += buckets[i]` (element int -> string concat) emitted the SAME
  ldelema -> unbox_int32(intptr_t) shape -> build abort.
- WORKAROUND (this case): copy `int b = buckets[i];` into a local first,
  then `hist += b;`.

## D-RND2-3 -- (int) cast on a double in a COMPARISON is dropped (float-to-int fold)

- `(int)(rd.NextDouble() * 10) == rn.Next(10)` emitted
  `static_cast<double>(t2 * 10.0) == static_cast<double>(icall_random_next_max(...))`
  -- the outer `(int)` truncation vanished, so the comparison ran in double
  space and FAILED on a value net48 (int-truncated) compares as equal
  (cons10 False vs net48 True).
- WORKAROUND (this case): assign the truncated value to an `int` local first
  (`int a10 = (int)(rd.NextDouble() * 10); int b10 = rn.Next(10);`) -- the
  conversion-to-int32 fold on a STORE is correct, the expression-position
  int cast of a double is what drops.
- Root: the emitter folds `(int)` over a double-typed expression only in
  store/slot context, not in comparison-expression context.

## Verified byte-exact contract (this case pins, no gap)

- Seed distribution: seeds 0..9 first-two raw values; spread audit over
  seeds 0..99 (22-bucket histogram + occupied-bucket count); adjacent-seed
  first-draw divergence 0..9; negative/large/mid-range seed pins.
- NextDouble boundary over seeds 0..999: all in [0,1), none == 1.0, zero
  count, min/max F6 pins.
- Sample-path consistency (via int locals): (int)(NextDouble()*max) ==
  Next(max) for max 10/100/1 on fresh same-seed instances.
- Long-run: seed 42 draws 1000..1002; precision pins seed 123456 F6.
