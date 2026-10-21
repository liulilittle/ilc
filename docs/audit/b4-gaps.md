# B4-1 GroupBy known gaps (deliberately LOUD, never silent)

Scope: `System.Linq.Enumerable.GroupBy` as wired by
`src/il2cppc/Emit/MethodMap.Linq.cs` (the `GroupBy` case) onto
`il2cpp_linq_group_by_full_key` in `src/runtime/src/linq_map.cpp`.

Every entry below is a **transpile-time LOUD** rejection
(`il2cpp: not transpiled: <Type>::<Method>`, or a
`NotSupportedException` naming the unsupported generic argument). **None of
them produce silent wrong values.** The list exists so a later batch can tell
"deliberately rejected" from "forgotten" -- the B4-1 D1 defect was exactly a
gate that was widened past what the runtime could represent.

Anything NOT listed here is expected to work: int32/bool/char/sbyte/byte/
short/ushort/uint keys over an int32[] source, with or without an element
selector, a result selector and a user `IEqualityComparer<TKey>`.

## 1. GroupBy key = `long` (0x0A)

**Why LOUD**: the bucket key is an `int32_t` slot; a 64-bit key would be
truncated, collapsing distinct net48 buckets (measured: 3 keys -> 1 bucket).
The transpiler gate rejects it in `KeyKindFor` (returns -1 ->
`src/il2cppc/Emit/MethodMap.Linq.cs:650-670`).

**Not silent**: the call is rejected at transpile time with
`Enumerable.GroupBy<int32,int64> not wired: ... a 64-bit (0x0A long / 0x0B
ulong) ... key has no runtime key carrier yet`.

**Change surface for a real fix** (all five ABI points are int32 today):

| # | Location | What is int32 today |
|---|---|---|
| 1 | `src/runtime/include/internal/linq_map.h:249` (`struct Il2CppLinqGrouping`) | `int32_t key;` -- the bucket slot itself (instanceSize 32, no GC ref) |
| 2 | `src/runtime/include/internal/linq_map.h:420` | `int32_t il2cpp_linq_grouping_get_key(Il2CppObject*)` -- the public key getter |
| 3 | `src/runtime/src/linq_map.cpp:1464` | `InvokeSelKeyGroupI4(...)` -- int32 result-selector ABI `(this, int32_t key, Il2CppObject* bucket)` |
| 4 | `src/runtime/src/linq_map.cpp:1471` | `InvokeSelKeyGroupRef(...)` -- same, reference result |
| 5 | `src/runtime/src/linq_map.cpp:1483` | `InvokeSelKeyGroupTyped(...)` -- same, typed (i8/double) result |
| 6 | `src/runtime/src/linq_map.cpp:1561` | `ComparerKeyEquals(cmp, int32_t key, int32_t existing, int32_t keyKind)` -- the user-comparer ABI |
| 7 | `src/il2cppc/Emit/MethodMap.Linq.cs` `get_Key` mapping | the `IGrouping<,>.get_Key` result type is tracked as int32 |

A real fix needs an int64 key carrier in the grouping struct (plus the
`instanceSize`/refFieldBitmap bookkeeping), int64 variants of the three result
selectors, an int64 `ComparerKeyEquals` (the reflected comparer slot for
`IEqualityComparer<long>` is `"(l,l)"`), and the transpiler `get_Key` type map.

## 2. GroupBy key = `ulong` (0x0B)

Same shape and same five ABI points as `long`; additionally the unsigned
display/`get_Key` path would need the uint64 tracking (`UInt64::ToString`
already works for VALUES -- see the B4-1 D2 fix -- but the grouping key slot is
still int32).

**Not silent**: transpile-time LOUD, same message as above (`0x0B ulong`).

## 3. GroupBy key = `decimal` / `DateTime` / `Guid` / any 0x11 value type

**Why LOUD**: every value-type key reaches the transpiler as the single byte
`0x11`, and the runtime has **no value-type key carrier at all** -- the bucket
stores an `int32_t` and the enumerator unboxes i4. There is no boxing path for
a key in the grouping result (the ELEMENT families are boxed; the key is not).

**Not silent**: transpile-time LOUD naming the real type via `genArgNames[1]`
(e.g. `Enumerable.GroupBy<int32,System.Decimal> not wired: ... value-type
(System.Decimal) key ...`).

**Change surface**: the same int32 key slot as gap 1, plus a boxing/equality
path for the key (`EqualityComparer<T>.Default` over a boxed value type, or a
per-type key slot), plus the transpiler `get_Key` type map. `decimal` also
needs the 0x11 `decimal` member surface, which is a separate known gap.

## 4. GroupBy over a NON-int32 source array (`byte[]`, `long[]`, `bool[]`, `string[]`, ...)

**Why LOUD**: the grouping kernel indexes the source through `I4At`
(`RequireInt32Family`, `src/runtime/src/linq_map.cpp:103-110`: "Only
one-dimensional System.Int32 arrays are supported"). The transpiler's
`genArgs[0] != 0x08` gate rejects the shape before the runtime is reached
(`src/il2cppc/Emit/MethodMap.Linq.cs:325-344`).

Note the KEY family and the SOURCE family are independent: a `byte` KEY over an
`int32[]` source works (B4-1 D1); a `byte[]` SOURCE does not.

**Not silent**: transpile-time LOUD
(`Enumerable.Where<byte> not wired: the stage-1 runtime LINQ surface is int32
(0x08) element-family only ...`).

**Change surface**: a per-element-family source reader (elemSize/isRefElem
dispatch instead of `I4At`) in every grouping entry, plus the corresponding
element-storage families (already present for the projected elements).

## 5. GroupBy with a user `IEqualityComparer<TKey>` whose reflected slot is missing

Not a planned gap -- it is a **runtime** LOUD, and only reachable when the
comparer type genuinely has no reflected `Equals(TKey,TKey)` slot. The runtime
now selects the signature by `keyKind`
(`KeyComparerFragments`, `src/runtime/src/linq_map.cpp:1523-1543`):
`(i,i)` int/uint, `(u1,u1)` byte, `(i1,i1)` sbyte, `(i2,i2)` short,
`(u2,u2)` then `(c,c)` ushort/char, `(b,b)` bool. If none matches it throws
`IEqualityComparer<TKey>.Equals dispatch: the comparer type has no reflected
virtual Equals(...) slot.` -- an explicit exception, never a wrong bucket.

## References

- Gate: `src/il2cppc/Emit/MethodMap.Linq.cs:640-670` (`KeyKindFor` + the
  GroupBy LOUD message), `:325-344` (non-int32 source gate).
- Runtime key carrier: `src/runtime/include/internal/linq_map.h:249`
  (`int32_t key`), `:420` (`il2cpp_linq_grouping_get_key`).
- Narrowing (the part that IS supported): `NarrowKey`
  (`src/runtime/src/linq_map.cpp:1606-1618`) and the `keyKind` third literal.
- Test that pins the supported set: `tests/unit/MethodMapTests.B4.cs`
  (`GroupBy_64BitKeys_StayLoud`, `GroupBy_DecimalKey_StaysLoud_AndNamesDecimal`)
  and `tests/cases/loud-p0-04-int/loud-p0-04-int.cs`.
