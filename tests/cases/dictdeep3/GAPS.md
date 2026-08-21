# GAPS.md -- dictdeep3 defects (e2e batch C52, 2026-08-12)

Case status: **PASS dual-compiler (mingw + msvc)** -- the active probes cover
the WIRED Dictionary<string,int> surface (MethodMap.Dictionary.cs +
dictsi_obj.h): collision under load, content-dedup collision, ordinal/prefix
non-aliasing, capacity matrix, small-capacity growth, operation-order
independence. The gaps below are RECORDED, NOT FIXED (this batch creates test
cases only).

## D-DICT3-1 -- Dictionary<string,int>.Keys / .Values (and foreach) not wired (transpiler-side)

- **Phenomenon**: any `d.Keys`, `d.Values` or `foreach` over the dictionary
  would abort the transpile with `NotSupportedException` (the generated
  method name has no runtime mapping). No probe in dictdeep3.cs uses them --
  **traversal ORDER is therefore out of reach** and the case asserts the
  order-INDEPENDENT observable contract instead.
- **Causal chain**: MethodMap.Dictionary.cs maps ctor / Add / set_Item /
  get_Item / TryGetValue / ContainsKey / Remove / get_Count / Clear ONLY --
  there is no `get_Keys` / `get_Values` case (compare Hashtable and
  SortedList, which both map them). The runtime dictsi header DOES expose
  `il2cpp_dict_si_get_keys` / `il2cpp_dict_si_get_values`
  (src/runtime/include/internal/dictsi.h:144/149), but the resolver never
  emits them, so the transpiler falls to the generated-name path and fails.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.Dictionary.cs` -- add
  get_Keys/get_Values cases + the tracked pseudo-type wiring
  (MethodRefResolver.cs:687 precedent).

## D-DICT3-2 -- raw string GetHashCode is process-randomized on net48 (by design, documented)

- **Phenomenon**: `"abc".GetHashCode()` is randomized per-process on net48
  (UseRandomizedStringHashAlgorithm), so raw hash values are NOT byte-stable
  across two processes. dictdeep3 probes only hash RELATIONSHIPS (equal
  content -> same key, distinct content -> distinct key), never raw values.
- **Not a fixable defect** -- net48 semantic; the runtime's deterministic
  ordinal hash (icall_string_get_hash_code) is documented as deliberately
  different (hashdeep.cs records the same).
