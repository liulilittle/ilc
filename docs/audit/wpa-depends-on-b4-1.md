# WP-A fix-forward: commit dependency record

## Actual parent

`9213c629` ("arraysort: make DateTime/TimeSpan scans needle-shape independent;
drop dead kind 6/7 arms") has **parent `81ba17fd`** ("linq: narrow GroupBy key
families to the declared TKey width (P0-04), carry ulong Aggregate through i8
with value-slot ToString (P0-06), consume genArgNames"), not `9e1035f5` as the
first report stated.

```
$ git rev-parse 9213c629^
81ba17fd9b5d21ef8cecdd675a0369385b282ba4
```

## Why the parent matters

The C1 fix in `9213c629` decides the element type from the MethodSpec generic
argument NAME (`genArgNames`) instead of the StackFold `arg0Type/arg1Type`
hints. That `genArgNames` plumbing (`GenericArgFullNames` +
`BclResolveCore(..., genArgNames)` + the `genArgNames:` argument at the
MethodSpec BCL call site) was introduced by `81ba17fd`.

Consequences:

* `9213c629` and `81ba17fd` **must be merged together**.
* Cherry-picking `9213c629` alone onto `9e1035f5` does **not** compile: the
  `TryMapArraySort(..., genArgNames, ...)` parameter has no caller passing it
  in that tree.
* Reverting `81ba17fd` while keeping `9213c629` also breaks the build.

## Still LOUD for DateTime[]/TimeSpan[] (transpile-time rejection)

Every item below is a **transpile-time LOUD** failure (`method fold failed ->
skipped`, the method becomes a `not transpiled` stub). None of them can
silently produce a wrong value. Measured on `9213c629` by transpiling a probe
that calls each overload with a `DateTime[]`/`TimeSpan[]` receiver:

| Overload | Diagnostic |
|---|---|
| `Array.IndexOf<T>(T[], T, int)` | `Array.IndexOf<T> generic argument 0x11 with 3 arg(s)` |
| `Array.LastIndexOf<T>(T[])` | `Array.LastIndexOf<T> generic argument 0x11 with 2 arg(s)` |
| `Array.LastIndexOf<T>(T[], T, int)` | `Array.LastIndexOf<T> generic argument 0x11 with 3 arg(s)` |
| `Array.LastIndexOf<T>(T[], T, int, int)` | `Array.LastIndexOf<T> generic argument 0x11 with 4 arg(s)` |
| `Array.BinarySearch<T>(T[], T, IComparer<T>)` | `Array.BinarySearch<T> generic argument 0x11 with 3 arg(s)` |
| `Array.Sort<T>(T[], int, int)` | `Array.Sort<T> generic argument 0x11 with 3 arg(s) not ...` |
| `Array.Sort<T>(T[], IComparer<T>)` | `Array.Sort<T> generic argument 0x11 with 2 arg(s) not ...` |
| non-generic `Array.IndexOf(Array, object)` / `Array.BinarySearch(Array, object)` | no DateTime/TimeSpan box helper exists (the transpiler rejects the box cast) |

Supported (mapped by value, verified by `tests/cases/arraydtkind` and
`tests/cases/arraydtneedle`, both `diff=0`):

* `Array.IndexOf<DateTime/TimeSpan>(T[], T)`
* `Array.IndexOf<DateTime/TimeSpan>(T[], T, int, int)`
* `Array.BinarySearch<DateTime/TimeSpan>(T[], T)`
* `Array.BinarySearch<DateTime/TimeSpan>(T[], int, int, T)`
* `Array.Sort(Array)` (the default comparer; the Sort<T> comparer/range
  shapes need a comparer-call ABI for these element types)

## Unrelated pre-existing failures (not introduced here)

* `test_arraysort`: `binary_search_int32 with duplicates returns the first
  match` fails identically when the `9e1035f5` tree's own `tests/runtime` is
  built, so it predates this work.
* `typeof(DateTime[]).ToString()` reports `"[System.DateTime"`;
  `Type.IsAssignableFrom`/`IsInstanceOfType` treat every value array alike.
  Both are baseline behaviour for `int[]`/`long[]` too.
* `Array.Copy` does not validate element types (`long[] -> DateTime[]` copies
  silently instead of throwing `ArrayTypeMismatchException`).
* `Array.Sort(keys, items, ...)` guard order deviates from net48 in four
  branches (both-negative index/length reports `index` rather than `length`,
  `items == null`, and the `keys == null` ParamName).
