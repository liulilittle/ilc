# Array.GetEnumerator: the shared enumerator, and the value-element box

`Array.GetEnumerator` had no mapping arm and failed at TRANSPILE time:

```
warning: method fold failed -> skipped (Program::Main: System.Array::GetEnumerator:
System.Array::GetEnumerator: not wired in M5 (BCL owner has no mapping arm and is
not defined in the input assembly -- a phantom generated-name call would never
link; receiverType='System.Int32[]' arg0Type='System.Int32[]'))
```

## Reuse, not a new type

The runtime already has `Il2CppArrayEnumerator` (`array` + `index`, index -1 =
before the first element) and the full `il2cpp_sortedlist_enumerator_*` icall
family (`move_next`/`reset`/`dispose`). `Array.GetEnumerator` allocates the same
object and stamps the same class descriptor, so those icalls work unchanged.

The array is stored in the enumerator's GC-scanned field, so it stays reachable
while the `foreach` runs. `il2cpp_object_new` may compact and MOVE the receiver,
so it is rooted with `GcRoot` and re-read before the store.

## The defect the first build found

`il2cpp_ienum_get_current_obj` (the non-generic `IEnumerator.Current` carrier)
dispatched the SortedList enumerator class to
`il2cpp_sortedlist_enumerator_get_current`, which reads the slot as an
`Il2CppObject*`:

```
return reinterpret_cast<Il2CppObject* const*>(en->array->data)[en->index];
```

That is correct only for a REFERENCE array. `Array.GetEnumerator` stamps the
same class on an enumerator over a VALUE array, so `(int[])a.GetEnumerator()`
read the int payload as a pointer and crashed with an access violation
(`0xC0000005`).

The fix routes that class through `ArrayEnumRef`, which already boxes by element
kind (`isRefElem` -> the slot; otherwise `il2cpp_box_int32` / `icall_box_long` /
...). The reference path is unchanged.

## Evidence

`tests/cases/probe-arrayenum` (7 lines), byte-identical to the net8 oracle
(`sha=c9d54e6e87ebb952ec1b23652f5ec10c9e192f23aa44639975ae624b3570bd59`):

```
sum:60
reset:3
ref:pq
empty:False
foreach:60
objcur:10:Int32
objref:p
```

`objcur` is the load-bearing line: it reads through the `object` carrier
(`IEnumerator.Current`), which is the path that routes through
`il2cpp_ienum_get_current_obj`. The earlier `sum`/`foreach` lines use the TYPED
`il2cpp_ienum_get_current_i4` accessor and therefore never exercised the defect.

## Mutation evidence

| mutation | result |
|---|---|
| `get_current_obj` routes the SortedList class back to the reference-only read | access violation, exit `-1073741819`, no output |
| restored | 7/7 lines match the oracle, exit 0 |

The first mutation attempt hit `il2cpp_ienum_get_current_ref` (a different
function, same shape) and stayed green -- a false test. It identified that the
case needed the `object`-carrier line: without `objcur`, the typed accessor
masked the defect entirely.

## Regression

21 cases pass, including every array case in the tree.

## Still not covered

`Array.AsReadOnly<T>` is still unmapped. It returns a `ReadOnlyCollection<T>`,
which needs that collection type's own descriptor, constructor and `IList<T>`
implementation -- a collection-type task, not an array one.
