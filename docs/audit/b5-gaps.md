# B5 fix -- residual gaps (registered, NOT silent)

Scope of this batch: D-1 (non-i4 `Any(pred)`/`Count(pred)`/`All`/`Where`/
`Select`/`First`/`Last` for every element family), D-2 (`List<T>` carrier
dispatch), the R2 root-table abort, the R2 OrderBy/Last(pred) wiring, and the
R3/R4/R5 items.

Everything registered here is one of:

- (a) a precision-only issue with no functional effect,
- (b) a documented semantic deviation,
- (c) an operator/shape whose kernel does not exist, which raises a
  COMPILE-TIME `NotSupportedException`, or
- (d) a shape that is rejected at RUN time with `NotSupportedException` because
  the decision needs a runtime type (`object`/interface keys).

Claims of the form "never a silent wrong value" are deliberately NOT made here:
the audit rounds found several silent wrong values (the zip family mix-up, the
object-key string ordering, the elemKind-less materializers), so this document
enumerates what is checked instead of asserting a global property.

## 1. Type-tracking precision (D-6) -- registered, no functional impact

`LinqRefArrayTypeName` / `LinqRefElementTypeName` (MethodMap.Linq.cs) return
`System.Object[]` / `Object` for a non-string reference element type (they only
special-case `0x0E` = String). Verified with the audit probes `classarr`,
`fieldprobe`, `chainref`: all MATCH. The tracked name only feeds downstream
member resolution hints; the emitted call and the runtime carrier are correct.
Fixing this needs a real type-name table for arbitrary reference generic args
(the byte alone cannot name a class), so it is deferred rather than guessed.

The tracked List receiver NAME is likewise a heuristic and is known to be
unreliable (MEASURED in R5: a `List<double>` receiver inside a method that also
builds a `List<int>` was tracked as `System.Collections.Generic.List`1_int`, and
`il2cpp_linq_list_zip` then read the double cells as int32). R5 therefore made
the MethodSpec generic argument (`genArgs[0]`) the ELEMENT AUTHORITY in
`LinqListKind`; the name suffix is used only when the call carries no MethodSpec
at all (the `List<T>` instance-method forms). Any future operator that reads the
element family must follow that order: metadata first, tracked name second.

## 2. String ordering keys -- two-layer LOUD (was a silent defect)

`Enumerable.OrderBy` / `OrderByDescending` with a `Func<TSource,string>` key
selector throws `NotSupportedException` at TRANSPILE time.

Why: net48's `Comparer<string>.Default` is CULTURE-aware (it sorts `"a"` before
`"A"` and applies OS-NLS collation weights). This runtime has no collation
infrastructure -- `stringext2.cpp` documents that the culture comparison modes
degrade to an ASCII case fold and that "net48's OS-NLS collation is not
replicated"; `string.CompareTo` is ordinal. An ordinal comparison would
therefore silently misorder mixed-case input.

A STATIC `string` key is caught by the transpiler, but an `object`/interface key
must be judged at RUN time: `object[] { "b", "A", "c", "a" }.OrderBy(x => x)`
has `TKey == System.Object`, and its keys happen to BE strings. `CompareRefKeys`
(linq_sort.cpp) therefore throws `NotSupportedException` when either key
instance is a `System.String` (registered lock:
`tests/cases/loud-p0-16-objkey-loud`). Every other reference key type still goes
through `IComparable.CompareTo` (Comparer<T>.Default semantics), null-first.

**NULL keys are NOT rejected.** The null checks run BEFORE the string check,
because net48 orders a null key before every non-null key without consulting the
comparer at all -- so `object[] { null, "a" }.OrderBy(x => x)` is answerable and
returns `{ null, "a" }` (and the descending form puts null last). Rejecting that
would be a LOUD misfire. Registered lock: `tests/cases/b5nullkey` (ascending,
descending, two nulls, and null mixed with a non-string `IComparable` key). A
comparison of two NON-null keys where at least one is a string still throws.

Honest history: the ordinal branch was introduced by this batch (commit
2a2cf2cf's `SortRefsKeyCopy`/`CompareRefKeys`), and the first e2e case was
narrowed to single-case input to avoid the mismatch. Both were wrong; the
current state is the compile-time rejection (arrays and `List<T>`, lock:
`tests/cases/loud-p0-14-strkey-loud`) plus the runtime instance check above.

## 3. Documented semantic deviations

- Every `List<T>` / array operator materializes EAGERLY: `var q = l.Where(p);
  l.Add(x); foreach (q)` sees the snapshot, not the late mutation. This is the
  stage-1 design deviation documented in `linq_map.h`.
- `string.Join(Array)` over a NARROW value array (`sbyte[]`/`byte[]`/`short[]`/
  `ushort[]`/`char[]`/`bool[]`) formats through `stringext2.cpp`
  `ValueArrayElement`, whose table has no signed/unsigned-narrow or bool entry
  (elemSize 1 cannot distinguish sbyte from byte; elemSize 2 cannot distinguish
  short from ushort from char). This is a PRE-EXISTING shared-formatter
  limitation, independent of LINQ: LINQ itself returns the correct cells (the
  e2e asserts them with explicit casts). A fix belongs to the string/format
  domain, not this batch.
- Narrow-family result arrays therefore carry `elemKind 0` (their family travels
  as the element-kind LITERAL the transpiler appends, never as a cell kind):
  `ValueElemKindFor` maps only 1 int32 / 2 int64 / 3 single / 4 double /
  5 uint32. `string.Join` over such an array hits the limitation above.
- CORRECTION (R6) to an earlier report: the claim that "the `zip_i4` shortcut
  now requires `resKind == 0` and exact int32 cells on both sides" was WRONG on
  both counts -- `il2cpp_linq_zip_i4` has **no `resKind` parameter at all**.
  What actually happened: the shortcut was **removed from the List path
  entirely**. `il2cpp_linq_list_zip` now delegates to
  `il2cpp_linq_zip_family` unconditionally, and the transpiler emits
  `il2cpp_linq_zip_family` for every Zip (see section 5b: `il2cpp_linq_zip_i4`
  has no caller). The truncation defect is fixed by routing through the
  result-family-aware entry, not by adding a guard to `zip_i4`.
- `Zip` with a `UInt64` (`ulong`) TResult is mapped onto the int64 result family
  (`resKind` 2): the cell width and the write ABI are identical, but a value
  above `Int64.MaxValue` prints through the int64 formatter, so an `ulong`
  projection is NOT bit-exact in `string.Join`. Registered, not silent (the
  cells hold the correct 8 bytes).
- Float results ride the 8-byte double carrier in a cell typed `elemKind 3`
  (the low 4 bytes are the float's own bits) -- the same convention as
  `il2cpp_linq_select_wide`. Readers take elemKind first, so a carrier array fed
  back as a source reads correctly.

## 4. Kernels that do not exist yet -> still LOUD (never silent)

| Operator | Shape | Reason |
| --- | --- | --- |
| `Where` / `Select` | `Func<T,int,...>` (index overload) over a **LIST** source | no list index kernel (the ARRAY forms are wired: `where_idx_i4`, `where_idx_ref`, `select_idx_i4_*`, `select_idx_ref_ref`) |
| `OrderBy` / `OrderByDescending` | `Func<T,int,TKey>` (index key selector) | no index key-selector entry |
| `Select` | `Func<T,int,TResult>` over a reference source with a NON-reference `TResult` | the reference index-selector surface covers a REFERENCE `TResult` only (`select_idx_ref_ref`) |
| `Select` | reference source with a NON-reference `TResult` | only `il2cpp_linq_select_ref_ref` exists for ref sources |
| `OrderBy` / `OrderByDescending` | `decimal` (or any non-i4/i8/d/ref) key family | no key kernel |
| `OrderBy` / `OrderByDescending` | a `System.String` key -- STATIC (`TKey == string`) | transpile-time rejection (no collation) |
| `OrderBy` / `OrderByDescending` | a key whose RUNTIME type is `System.String` (`object`/interface key) | runtime rejection in `CompareRefKeys` (no collation) |
| `ThenBy` / `ThenByDescending` | LIST receiver, or a reference-element source | the chained-sorter fold only rewrites the int32 ARRAY form |
| `GroupBy` | non-int32 key family / non-int32 list | the stage-1 grouping surface is int32 keys + int32 elements |
| `Sum`/`Min`/`Max`/`Average` | reference-element source or LIST | numeric aggregates over a reference family have no kernel |
| `Aggregate` | seed/accumulator family outside i4/i8/f/d/ref | no kernel |
| `ToList` | reference-element array | `Resolve_ToList_String_ThrowsLoud` (pre-existing) |
| `Last(pred)` / `LastOrDefault(pred)` | `List<string>` (reference-element list) | no list predicate-last entry; the array entry exists |
| `Zip` | TResult = `sbyte`/`byte`/`short`/`ushort` | transpile-time rejection: the wide/narrow zip surface writes 4- or 8-byte result cells, so a 1/2-byte result cell has no ABI |
| `Zip` | TResult = `bool` / `char` | same as above (1/2-byte cell), and the shared formatter has no bool/signed-narrow entry |
| `Zip` | TResult = a reference type, value-family receiver | the value-cell zip surface cannot write a reference slot |
| `Zip` | ANY reference-element source (array or `List<T>`), any TResult | **transpile-time rejection.** This used to CRASH, not throw: the reference Zip branch called the selector through `Il2CppObject*(Il2CppObject*, Il2CppObject*)`, so a value TResult (int32 in EAX) was stored as a GC pointer through the write barrier -- measured `exit=-1073741819` (0xC0000005) for `string[]{"a","bb"}.Zip(a, (x,y) => x.Length + y.Length)`. Both array arms and the `List<T>` arm now reject the shape at transpile time, and the runtime reference branch was REMOVED (section 5c) so it cannot be re-entered. |

## 5. Export inventory -- BOTH scopes (this replaces the earlier "0 unwired")

The earlier claim "0 truly-unwired exports" only checked whether the
transpiler REFERENCED a symbol, never whether the symbol had a DEFINITION. That
masked 17 declared-but-undefined symbols (the R3 link failures). The inventory
must be read on both axes:

### 5a. Referenced by the transpiler but with NO definition

NONE at the time of writing. Verified with `nm --defined-only -C
libil2cpp_runtime.a` against every `il2cpp_linq_*` declaration in
`linq.h` / `linq_map.h` / `linq_list.h` / `linq_sort.h`:
`declared=308, missing=0` (the R3 batch implemented `where_{i8,d,f,narrow}`,
`select_wide`, `first_{i8,d,f,narrow}`, `first_or_default_*`,
`last_{i8,d,f,narrow}`, `last_or_default_*`; the R5 batch re-cut
`zip_wide`/`list_zip` and added the `*_pred_i4` last variants).

### 5b. Declared and defined but NOT referenced by the transpiler

These are runtime-internal helpers or superseded entries -- NOT gaps:

- `il2cpp_linq_list_{shortName}_{suffix}` / `..._sel_{psuffix}` -- Sum/Min/Max/
  Average over a list (built by `$"..."` interpolation).
- `il2cpp_linq_aggregate_seed_{acc}_rs_{res}` / `..._rs_{res}` -- built by
  `cppBase + resSuffix` in the Aggregate arm.
- `il2cpp_linq_grouping_*` / `il2cpp_linq_grouping_result_*` -- runtime-side
  class/enumerator symbols consumed by `linq_map.cpp` + `arraysort.cpp`.
- `il2cpp_linq_list_snapshot` -- internal helper of `linq_list.cpp`.
- `il2cpp_linq_zip_i4` (linq_map.cpp) -- **defined but now has NO CALLER**. The
  four transpiler Zip emission sites all target `il2cpp_linq_zip_family`
  (verified: `grep il2cpp_linq_zip_family src/il2cppc/Emit/MethodMap.Linq.cs`
  -> 4 sites; `grep il2cpp_linq_zip_i4 src/il2cppc` -> only a comment), and the
  former internal caller in `il2cpp_linq_list_zip` now delegates to the unified
  entry. It is kept only as the int32-only fast-path ABI; if a future batch does
  not add a caller, deleting it (declaration + definition) is the correct
  cleanup.

### 5c. Unreferenced legacy declarations (honest statement)

`il2cpp_linq_order_by_ref` / `il2cpp_linq_order_by_string` take a 2-arg
`Comparison<T>`, while `Enumerable.OrderBy` passes a 1-arg key selector: a
1-arg delegate invoked through a 2-arg signature is undefined behaviour, so
neither entry can serve `Enumerable.OrderBy`. **They have NO CALLER**: `grep`
over `src/il2cppc` finds only the explanatory comment in `MethodMap.Linq.cs`
(no emission site exists), and `linq_sort.h` documents them as the low-level
`Comparison<T>` ABI kept for a future caller. They are NOT "wired", they are
unreferenced declarations -- if a future batch does not add a caller, deleting
them (declaration + definition) is the correct cleanup.

**Removed in R6: the reference branch of `il2cpp_linq_zip_family`** (it was
`linq_list.cpp` ~1277-1302). It was unreachable-but-dangerous dead code: with
the transpiler now rejecting every reference-element Zip, the only way in was a
hand-written C++ caller, and that path stored a value-result thunk's EAX as a GC
pointer. `grep ZipRefRefFn|ZipRefRefThisFn` showed exactly two users (the two
typedefs plus that branch), so the branch AND both typedefs were deleted; the
same conditions now raise `il2cpp_throw_not_supported_exception` instead.

## 6. Rooting table

All four `RootScope` implementations (`linq_list.cpp`, `linq.cpp`,
`linq_map.cpp`, `linq_sort.cpp`) grow a `std::vector<void**>` on demand; there
is no fixed capacity and no `std::abort()`. An allocation failure is reported as
`System.OutOfMemoryException` via `il2cpp_throw_new` (there is no dedicated
`il2cpp_throw_out_of_memory_exception` factory in this repository), so a real
OOM is no longer disguised as `NotSupportedException`.

## 7. The zip SIDE descriptor (R5) -- why it is a separate enumeration

`il2cpp_linq_zip_wide` reads each source by its OWN family (the two sources of a
`Zip` may differ, e.g. `List<double>.Zip(int[])`) and calls the selector through
the ABI of its STATICALLY DECLARED parameter types (int32_t for the int32 slot
and every narrow family, int64_t for i8, double for float/double). The side
descriptor is `IL2CPP_LINQ_NARROW_*` (0..6) plus `IL2CPP_LINQ_ZIP_I4/I8/F/D`
(7..10) and is deliberately NOT the `IL2CPP_LIST_KIND_*` space: the two share
the small integers with DIFFERENT meanings (LIST_KIND SBYTE=5 vs NARROW CHAR=5,
LIST_KIND BYTE=6 vs NARROW UINT=6), and passing one through as the other reads a
1-byte cell as char16_t/uint32 -- a measured out-of-bounds read. `linq_list.cpp`
translates through the explicit named table `ZipSideOfListKind`.

The second source's family travels as an appended literal (`secondKind`) derived
from the SAME MethodSpec authority as `kind`.
