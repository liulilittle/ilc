# GAPS.md -- enumerableops recorded findings (e2e batch C73, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 40-line byte-exact).

## D-LINQC-1 -- Enumerable.Count / Count(predicate) over a ToList() receiver is not wired

- **Phenomenon**: `src.ToList().Count()` and `src.ToList().Count(pred)` FAIL'd at
  runtime (transpiled exe printed `c_tolist=False` / `c_pred_tolist=False`,
  expected `True`), while every other materializer form passed.
- **Causal chain** (from the generated C++): MethodMap.Linq.cs maps `Count` to
  `il2cpp_linq_count(Il2CppArray*)` / `il2cpp_linq_count_i4(Il2CppArray*,
  Il2CppObject*)` (linq.h:43/52 -- array-only signatures). When the receiver is
  a `List<int32>` produced by `il2cpp_linq_to_list`, the emitted call is
  `il2cpp_linq_count(reinterpret_cast<Il2CppArray*>(t0))` (cases .cpp line 384:
  the List object is mis-cast to Il2CppArray*), so the runtime reads the List's
  memory as an array header and returns a wrong count. The WIRED path is the
  listi4 domain's `get_Count` -> `il2cpp_list_i4_get_count` (MethodMap.List.cs),
  which the case asserts instead.
- **Evidence**: generated `Cases_EnumerableOps.cpp:384` and `:404` show the
  `reinterpret_cast<Il2CppArray*>` on a `il2cpp_linq_to_list` result; net48
  oracle asserts 8 / 3; the transpiled run printed False.
- **Disposition**: the case's Count-combo section asserts the wired property
  form `ToList().Count == 8` / `Where(pred).ToList().Count == 3` (get_Count),
  and documents this paragraph in the .cs header. No runtime change (per batch
  policy, defects are recorded, not fixed).
- **Suspect**: MethodRefResolver / MethodMap.Linq.cs `Count` cast shape -- the
  `"a"` cast on a List receiver needs a List-aware count entry (e.g. a
  `il2cpp_linq_count_list` taking Il2CppObject* that dispatches to the listi4
  count) or a receiver-type guard before the array cast.
