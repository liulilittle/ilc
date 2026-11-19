# System.Collections.ArrayList's surface

Status: GREEN (`probe-alextra`, byte-exact against the net48 oracle, sha
`25355ab2458412417103698ac1229a83308d2dfd7133ff0c4814c9f9ab3f6b49`, 1058B).

## What was missing

27 of the net48 `ArrayList` members had no mapping arm.  20 were value
semantics, 7 were the wrapper family:

```
GetEnumerator()  GetEnumerator(int,int)  GetRange  AddRange  InsertRange
SetRange  ToArray()  ToArray(Type)  LastIndexOf(x)  LastIndexOf(x,i)
LastIndexOf(x,i,c)  Sort()  Sort(IComparer)  Sort(int,int,IComparer)
BinarySearch(x)  BinarySearch(x,IComparer)  BinarySearch(int,int,x,IComparer)
CopyTo(Array)  CopyTo(Array,int)  CopyTo(int,Array,int,int)  Repeat
Adapter  FixedSize  ReadOnly  Synchronized
```

Every one failed with the same whole-method loss:

```
il2cppc error: Program::Main: System_Collections_ArrayList_GetEnumerator_...:
  not wired in M5 (BCL owner has no mapping arm and is not defined in the input
```

## Net48 measurements that pin the contract

Each rule below was measured on the net48 oracle (`.opencode/tmp/aloracle`),
not inferred from a reference source:

| Behaviour | net48 |
|---|---|
| capacity growth | doubling: 4->8->16->32 (3->6->12->24) |
| `GetRange(i,c)` capacity | the SOURCE list capacity (empty source -> 0); after `TrimToSize` -> count |
| `Repeat(v,0)` | Count 0, Capacity 4 |
| `Repeat(v,n>0)` | Capacity n (4 when n<=4) |
| `AddRange`/`InsertRange` growth | doubling when the need exceeds capacity |
| `GetEnumerator` | SNAPSHOT: a structural change then `MoveNext` throws `InvalidOperationException`; `Current` before/after throws `InvalidOperationException` (messages 「枚举尚未开始。」/「枚举已完成。」); the enumerator is NOT `IDisposable` |
| `GetEnumerator(index,count)` | index<0 -> ArgumentOutOfRangeException; index>Count or count>Count-index -> ArgumentException |
| `Sort` | default comparer; mixed element types -> InvalidOperationException; range out of bounds -> ArgumentException / ArgumentOutOfRangeException |
| `BinarySearch` | found -> index; missing -> `~insertionPoint`; empty -> -1 |
| `ToArray(Type)` | int[] -> string[]/long[]/int[][] all InvalidCastException; object[] accepts anything; a null element into a value array -> InvalidCastException |
| `CopyTo` | counts 1 / 2 / 4; negative index or count -> ArgumentOutOfRangeException; overrun -> ArgumentException; null -> ArgumentNullException |
| wrappers | share the backing items (`Add` is visible through the source); `ReadOnly` rejects EVERY mutation (NotSupportedException); `FixedSize` rejects size changes but allows an existing-index overwrite; `SyncRoot` is a fresh `System.Object` for a plain list too (`ReferenceEquals(root, list) == False`); `IsFixedSize`/`IsReadOnly`/`IsSynchronized` follow the wrapper kind |

## Implementation shape

* `Il2CppArrayList` gained a non-GC `int32_t wrapperKind` slot; the struct
  stays compact (`offsetof == 20`).
* The wrapper family shares `items` with the source list, so `Add` through a
  wrapper is observable on the source (net48 parity).
* Mutation guards: `ThrowIfWrapperRejectsMutation(self, sizeChanging)` is
  inserted in every mutating icall.
* `Sort` uses an in-file quicksort plus `il2cpp_comparer_compare`; `ComparerCompareCore`
  is file-local to `arraysort.cpp`, so it is not reachable from `arraylist.cpp`.
* `ToArray(Type)` dispatches on the element *name* (the compiler emits the
  `Type` value as a type-name string); the supported families are object /
  string / int32 / int64 / double / bool / char / byte / sbyte / int16 /
  uint16 / uint32 / uint64 / single / intptr.
* `il2cpp_throw_invalid_cast_exception` does not exist in this runtime; the
  mismatch path throws `System.InvalidCastException` by name.

## Two bugs the probe caught (both fixed)

1. `SetRange` enumerated `self` instead of `collection`, so the non-generic
   enumerator dispatch found no vtable slot and raised
   `the receiver type has no reflected virtual member`.  Symptom: runtime
   stopped right after `insertrange=`.
2. `SyncRoot` returned `self` for a plain `ArrayList`; net48 returns a fresh
   `System.Object` even then.  Symptom: a single line divergence at record 1051.

## Mutation evidence

Breaking `il2cpp_arraylist_get_sync_root` to `return self;` turns the probe
RED (`firstDiff stdout=1051`); restoring it returns the byte-exact sha above.
